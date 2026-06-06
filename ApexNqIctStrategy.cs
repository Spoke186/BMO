#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.AddOns; // ApexBridgeState (toggle del MCP)
#endregion

// Estrategia ICT para NQ/MNQ bajo reglas Apex Trader Funding.
// Logica: en tendencia (estructura HH/HL en 15m) esperar barrida de liquidez
// contra-tendencia en 5m, confirmar desplazamiento (proxy ATR), detectar FVG
// y entrar con limite en el fill completo del gap, a favor de la tendencia.
//
// IMPORTANTE: trailing drawdown y regla de consistencia 50% son APROXIMACIONES.
// Apex las calcula en su servidor con su propia metrica de high-water mark.
// Aqui son una red de seguridad local, no la verdad. Ver README.
namespace NinjaTrader.NinjaScript.Strategies
{
	public class ApexNqIctStrategy : Strategy
	{
		#region Inputs
		[NinjaScriptProperty]
		[Display(Name = "Contratos", Order = 1, GroupName = "1. Tamano")]
		[Range(1, int.MaxValue)]
		public int Contratos { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop loss fijo (USD)", Order = 2, GroupName = "2. Estrategia")]
		[Range(1, 100000)]
		public double StopLossUsd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Target fijo (USD)", Order = 3, GroupName = "2. Estrategia")]
		[Range(1, 100000)]
		public double ProfitTargetUsd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Pivote tendencia 15m (velas a cada lado)", Order = 3, GroupName = "2. Estrategia")]
		[Range(1, 20)]
		public int SwingStrength15m { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Pivote liquidez 5m (velas a cada lado)", Order = 4, GroupName = "2. Estrategia")]
		[Range(1, 20)]
		public int SwingStrength5m { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Displacement (cuerpo >= X * ATR)", Order = 5, GroupName = "2. Estrategia")]
		[Range(0.5, 5)]
		public double DisplacementAtrMult { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG minimo (puntos)", Order = 6, GroupName = "2. Estrategia")]
		[Range(0.25, 50)]
		public double MinFvgPoints { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Buffer stop (ticks)", Order = 7, GroupName = "2. Estrategia")]
		[Range(0, 50)]
		public int StopBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Velas max para retroceso al FVG", Order = 8, GroupName = "2. Estrategia")]
		[Range(1, 50)]
		public int FvgValidBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Inicio kill zone (HHmm ET)", Order = 9, GroupName = "3. Horario")]
		public int KillZoneStart { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fin kill zone (HHmm ET)", Order = 10, GroupName = "3. Horario")]
		public int KillZoneEnd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cierre forzado (HHmm ET)", Order = 11, GroupName = "3. Horario")]
		public int ForcedExit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Balance inicial cuenta (USD)", Order = 12, GroupName = "4. Riesgo Apex")]
		public double StartingBalance { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trailing drawdown Apex (USD)", Order = 13, GroupName = "4. Riesgo Apex")]
		public double TrailingDrawdown { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Max daily loss propio (USD)", Order = 14, GroupName = "4. Riesgo Apex")]
		public double MaxDailyLoss { get; set; }

		#endregion

		#region Estado interno
		private ATR atr;

		// Estructura de mercado.
		private readonly List<double> swingHighs15m = new List<double>();
		private readonly List<double> swingLows15m  = new List<double>();
		private readonly List<double> swingHighs5m  = new List<double>();
		private readonly List<double> swingLows5m   = new List<double>();

		private int trend; // 1 alcista, -1 bajista, 0 sin sesgo

		// Maquina de estados del setup intradia.
		// 0 = buscando sweep, 1 = sweep + displacement + FVG armado (limite vivo)
		private int setupState;
		private int   setupDir;        // 1 long, -1 short
		private double fvgEntryPrice;  // borde lejano del FVG (fill completo)
		private double fvgInvalidPrice;// extremo del sweep: si se cruza antes del fill, anular setup
		private int   fvgArmedBar;     // CurrentBar (5m) cuando se armo
		private Order entryOrder;

		// Control de riesgo / dia.
		private bool tradedToday;
		private bool tradingDisabled;       // por daily loss o trailing DD
		private double sessionStartCumPnl;  // realizado acumulado al abrir la sesion
		private double accountHighWater;    // pico de equity para proxy de trailing DD
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name                 = "ApexNqIctStrategy";
				Description          = "ICT sweep + FVG continuation para NQ/MNQ con guardas Apex.";
				Calculate            = Calculate.OnBarClose;
				EntriesPerDirection  = 1;
				EntryHandling        = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds    = 60;
				BarsRequiredToTrade  = 20;
				IncludeCommission    = true;

				Contratos           = 2;
				StopLossUsd         = 250;
				ProfitTargetUsd     = 700;
				SwingStrength15m    = 3;
				SwingStrength5m     = 2;
				DisplacementAtrMult = 1.5;
				MinFvgPoints        = 3.0;
				StopBufferTicks     = 2;
				FvgValidBars        = 12;
				KillZoneStart       = 830;
				KillZoneEnd         = 1100;
				ForcedExit          = 1555;
				StartingBalance     = 50000;
				TrailingDrawdown    = 2500;
				MaxDailyLoss        = 400;
			}
			else if (State == State.Configure)
			{
				// Serie primaria = 5m (aplicar la estrategia sobre un grafico de 5m).
				// Serie secundaria 15m para el sesgo de tendencia.
				AddDataSeries(BarsPeriodType.Minute, 15);
			}
			else if (State == State.DataLoaded)
			{
				atr = ATR(BarsArray[0], 14);
				accountHighWater = StartingBalance;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBars[0] < BarsRequiredToTrade || CurrentBars[1] < BarsRequiredToTrade)
				return;

			if (BarsInProgress == 1)
			{
				UpdateTrend15m();
				return;
			}

			if (BarsInProgress != 0)
				return;

			// ---- Logica en serie de 5m ----
			Track5mSwings();

			if (Bars.IsFirstBarOfSession)
				ResetForNewSession();

			UpdateRiskGuards();

			// Cierre forzado antes del fin de sesion Apex.
			if (ToTime(Time[0]) >= ForcedExit * 100)
			{
				FlattenAndCancel("Cierre forzado");
				return;
			}

			bool inKillZone = ToTime(Time[0]) >= KillZoneStart * 100
			               && ToTime(Time[0]) <  KillZoneEnd * 100;

			// Gestion del setup ya armado (limite vivo o esperando fill).
			if (setupState == 1)
			{
				ManageArmedSetup(inKillZone);
				return;
			}

			// Buscar nuevo setup solo si: bot habilitado (toggle MCP), en ventana,
			// sin trade hoy, sin posicion, riesgo OK y tendencia definida.
			if (!ApexBridgeState.TradingEnabled || !inKillZone || tradedToday || tradingDisabled
			    || trend == 0 || Position.MarketPosition != MarketPosition.Flat)
				return;

			TryArmSetup();
		}

		#region Tendencia 15m (estructura HH/HL)
		private void UpdateTrend15m()
		{
			int s = SwingStrength15m;
			if (CurrentBars[1] < 2 * s + 1)
				return;

			// Pivote confirmado en la vela 's' barras atras (fractal simetrico).
			int p = s;
			bool isHigh = true, isLow = true;
			double pivH = Highs[1][p];
			double pivL = Lows[1][p];
			for (int i = 1; i <= s; i++)
			{
				if (Highs[1][p - i] >= pivH || Highs[1][p + i] >= pivH) isHigh = false;
				if (Lows[1][p - i]  <= pivL || Lows[1][p + i]  <= pivL) isLow  = false;
			}

			if (isHigh) AddCapped(swingHighs15m, pivH);
			if (isLow)  AddCapped(swingLows15m,  pivL);

			if (swingHighs15m.Count >= 2 && swingLows15m.Count >= 2)
			{
				int n = swingHighs15m.Count, m = swingLows15m.Count;
				bool hh = swingHighs15m[n - 1] > swingHighs15m[n - 2];
				bool hl = swingLows15m[m - 1]  > swingLows15m[m - 2];
				bool lh = swingHighs15m[n - 1] < swingHighs15m[n - 2];
				bool ll = swingLows15m[m - 1]  < swingLows15m[m - 2];

				if (hh && hl)      trend = 1;
				else if (lh && ll) trend = -1;
				// Si es mixto, mantener el ultimo sesgo (evita whipsaw del sesgo).
			}
		}
		#endregion

		#region Liquidez 5m + deteccion de setup
		private void Track5mSwings()
		{
			int s = SwingStrength5m;
			if (CurrentBars[0] < 2 * s + 1)
				return;

			int p = s;
			bool isHigh = true, isLow = true;
			double pivH = High[p];
			double pivL = Low[p];
			for (int i = 1; i <= s; i++)
			{
				if (High[p - i] >= pivH || High[p + i] >= pivH) isHigh = false;
				if (Low[p - i]  <= pivL || Low[p + i]  <= pivL) isLow  = false;
			}
			if (isHigh) AddCapped(swingHighs5m, pivH);
			if (isLow)  AddCapped(swingLows5m,  pivL);
		}

		// Busca: sweep contra-tendencia -> displacement a favor -> FVG. Arma limite.
		private void TryArmSetup()
		{
			double atrVal = atr[0];
			if (atrVal <= 0) return;

			if (trend == 1)
			{
				// LONG: barrida que toma un swing low previo (liquidez bajo) y vuelve.
				double sweptLevel;
				if (!RecentSweepLow(out sweptLevel)) return;

				// Displacement alcista: vela con cuerpo >= mult * ATR cerrando arriba.
				if (!BullishDisplacement(atrVal)) return;

				// FVG alcista en las velas del desplazamiento: low[0] > high[2].
				double upper = Low[0];
				double lower = High[2];
				if (lower >= upper) return;
				if ((upper - lower) < MinFvgPoints) return;

				double sweepLow = MinLow(0, DisplacementWindow());
				double stop = sweepLow - StopBufferTicks * TickSize;

				// Fill completo = borde lejano del gap (el inferior para un long).
				ArmSetup(1, lower, stop);
			}
			else if (trend == -1)
			{
				double sweptLevel;
				if (!RecentSweepHigh(out sweptLevel)) return;

				if (!BearishDisplacement(atrVal)) return;

				double lower = High[0];
				double upper = Low[2];
				if (upper <= lower) return;
				if ((upper - lower) < MinFvgPoints) return;

				double sweepHigh = MaxHigh(0, DisplacementWindow());
				double stop = sweepHigh + StopBufferTicks * TickSize;

				// Fill completo = borde superior del gap para un short.
				ArmSetup(-1, upper, stop);
			}
		}

		private void ArmSetup(int dir, double entry, double structuralStop)
		{
			// El stop estructural ya NO define el bracket (ahora es USD fijo). Solo sirve
			// como nivel de invalidacion: si la estructura del sweep se rompe antes de que
			// el limite llene, se cancela el setup.
			setupDir        = dir;
			fvgEntryPrice   = Instrument.MasterInstrument.RoundToTickSize(entry);
			fvgInvalidPrice = Instrument.MasterInstrument.RoundToTickSize(structuralStop);
			fvgArmedBar     = CurrentBar;
			setupState      = 1;

			PlaceEntryLimit();
		}

		private void PlaceEntryLimit()
		{
			string sig = setupDir == 1 ? "LongFVG" : "ShortFVG";

			// Bracket en USD FIJO (regla operador): stop $250, target $700, ambas direcciones.
			// CalculationMode.Currency => los puntos se ajustan solos al instrumento y a Contratos.
			// OJO: estos USD asumen NQ mini. En MNQ pedirian un recorrido irreal.
			SetStopLoss(sig, CalculationMode.Currency, StopLossUsd, false);
			SetProfitTarget(sig, CalculationMode.Currency, ProfitTargetUsd);

			if (setupDir == 1)
				entryOrder = EnterLongLimit(0, true, Contratos, fvgEntryPrice, sig);
			else
				entryOrder = EnterShortLimit(0, true, Contratos, fvgEntryPrice, sig);
		}

		private void ManageArmedSetup(bool inKillZone)
		{
			// Ya en posicion: stop/target adjuntos gestionan la salida.
			if (Position.MarketPosition != MarketPosition.Flat)
				return;

			bool expired   = (CurrentBar - fvgArmedBar) > FvgValidBars || !inKillZone;
			bool invalid   = setupDir == 1 ? Low[0]  <= fvgInvalidPrice
			                               : High[0] >= fvgInvalidPrice;

			if (expired || invalid)
			{
				CancelEntry();
				setupState = 0;
			}
		}
		#endregion

		#region Helpers de patron
		private int DisplacementWindow()
		{
			return 3; // velas del bloque sweep+desplazamiento usadas para el extremo
		}

		private bool BullishDisplacement(double atrVal)
		{
			double body = Close[0] - Open[0];
			return body > 0 && body >= DisplacementAtrMult * atrVal;
		}

		private bool BearishDisplacement(double atrVal)
		{
			double body = Open[0] - Close[0];
			return body > 0 && body >= DisplacementAtrMult * atrVal;
		}

		// Sweep alcista de liquidez: una vela reciente perforo un swing low previo
		// (mecha por debajo) y el precio recupero por encima de ese nivel.
		private bool RecentSweepLow(out double level)
		{
			level = 0;
			if (swingLows5m.Count == 0) return false;
			level = swingLows5m[swingLows5m.Count - 1];
			for (int i = 1; i <= DisplacementWindow() + 1; i++)
			{
				if (Low[i] < level && Close[i] > level)
					return true;
			}
			return false;
		}

		private bool RecentSweepHigh(out double level)
		{
			level = 0;
			if (swingHighs5m.Count == 0) return false;
			level = swingHighs5m[swingHighs5m.Count - 1];
			for (int i = 1; i <= DisplacementWindow() + 1; i++)
			{
				if (High[i] > level && Close[i] < level)
					return true;
			}
			return false;
		}

		private double MinLow(int start, int count)
		{
			double m = double.MaxValue;
			for (int i = start; i < start + count && i <= CurrentBar; i++)
				m = Math.Min(m, Low[i]);
			return m;
		}

		private double MaxHigh(int start, int count)
		{
			double m = double.MinValue;
			for (int i = start; i < start + count && i <= CurrentBar; i++)
				m = Math.Max(m, High[i]);
			return m;
		}

		private void AddCapped(List<double> list, double v)
		{
			list.Add(v);
			if (list.Count > 50) list.RemoveAt(0);
		}
		#endregion

		#region Riesgo Apex (aproximaciones locales)
		private void ResetForNewSession()
		{
			tradedToday        = false;
			tradingDisabled    = false;
			setupState         = 0;
			sessionStartCumPnl = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
		}

		private void UpdateRiskGuards()
		{
			double cumRealized = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
			double unrealized  = Position.MarketPosition == MarketPosition.Flat
				? 0
				: Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

			double equity = StartingBalance + cumRealized + unrealized;
			accountHighWater = Math.Max(accountHighWater, equity);

			// Proxy trailing DD: distancia desde el pico de equity.
			if ((accountHighWater - equity) >= TrailingDrawdown)
			{
				if (!tradingDisabled)
					Print($"[RIESGO] Trailing DD proxy alcanzado ({TrailingDrawdown:C}). Trading deshabilitado.");
				tradingDisabled = true;
			}

			// Daily loss propio sobre lo realizado de la sesion.
			double sessionPnl = cumRealized - sessionStartCumPnl;
			if (sessionPnl <= -MaxDailyLoss)
			{
				if (!tradingDisabled)
					Print($"[RIESGO] Max daily loss alcanzado ({sessionPnl:C}). Trading deshabilitado hoy.");
				tradingDisabled = true;
			}

			if (tradingDisabled)
				FlattenAndCancel("Riesgo: flat");
		}
		#endregion

		#region Ordenes / utilidades
		private void CancelEntry()
		{
			if (entryOrder != null && (entryOrder.OrderState == OrderState.Working
			                        || entryOrder.OrderState == OrderState.Accepted))
				CancelOrder(entryOrder);
			entryOrder = null;
		}

		private void FlattenAndCancel(string reason)
		{
			CancelEntry();
			if (Position.MarketPosition == MarketPosition.Long)
				ExitLong("LongFVG");
			else if (Position.MarketPosition == MarketPosition.Short)
				ExitShort("ShortFVG");
			setupState = 0;
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
			int quantity, int filled, double averageFillPrice, OrderState orderState,
			DateTime time, ErrorCode error, string nativeError)
		{
			if (order == null) return;
			if (order.Name == "LongFVG" || order.Name == "ShortFVG")
				entryOrder = order;
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId,
			double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order == null) return;

			// Al llenarse la entrada, marcar trade del dia (1 setup/dia, no DCA).
			if ((execution.Order.Name == "LongFVG" || execution.Order.Name == "ShortFVG")
			    && execution.Order.OrderState == OrderState.Filled)
			{
				tradedToday = true;
				setupState  = 0; // el setup paso a posicion; stop/target la gestionan
			}
		}
		#endregion
	}
}

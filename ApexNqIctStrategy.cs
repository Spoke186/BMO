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
// Temporalidades: 15m para sesgo (HH/HL), barrida, CHoCH y FVG;
//                 1m para gatillo de entrada (rechazo o mini-CHoCH dentro del FVG).
// Flujo: sesgo 15m → barrida nivel pre-apertura → CHoCH + desplazamiento 15m →
//        FVG en el impulso → retroceso al FVG en 1m → confirmacion 1m → mercado.
//
// IMPORTANTE: trailing drawdown y consistencia 50% son APROXIMACIONES locales.
// Apex las calcula en su servidor. Ver README.
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
		[Display(Name = "Pivote tendencia 15m (velas a cada lado)", Order = 4, GroupName = "2. Estrategia")]
		[Range(1, 20)]
		public int SwingStrength15m { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Pivote mini-CHoCH 1m (velas a cada lado)", Order = 5, GroupName = "2. Estrategia")]
		[Range(1, 10)]
		public int SwingStrength1m { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Displacement 15m (cuerpo >= X * ATR)", Order = 6, GroupName = "2. Estrategia")]
		[Range(0.5, 5)]
		public double DisplacementAtrMult { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG minimo 15m (puntos)", Order = 7, GroupName = "2. Estrategia")]
		[Range(0.25, 200)]
		public double MinFvgPoints { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Rechazo 1m (mecha / cuerpo ratio)", Order = 8, GroupName = "2. Estrategia")]
		[Range(0.5, 10)]
		public double RejectionWickRatio { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Max barras 1m para retroceso al FVG", Order = 9, GroupName = "2. Estrategia")]
		[Range(1, 200)]
		public int FvgValidBars { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Max barras 15m para CHoCH tras barrida", Order = 10, GroupName = "2. Estrategia")]
		[Range(1, 20)]
		public int SweepChochMaxBars15m { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Buffer invalidacion sweep (ticks)", Order = 11, GroupName = "2. Estrategia")]
		[Range(0, 50)]
		public int StopBufferTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Inicio kill zone (HHmm ET)", Order = 12, GroupName = "3. Horario")]
		public int KillZoneStart { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fin kill zone (HHmm ET)", Order = 13, GroupName = "3. Horario")]
		public int KillZoneEnd { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Cierre forzado (HHmm ET)", Order = 14, GroupName = "3. Horario")]
		public int ForcedExit { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Balance inicial cuenta (USD)", Order = 15, GroupName = "4. Riesgo Apex")]
		public double StartingBalance { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trailing drawdown Apex (USD)", Order = 16, GroupName = "4. Riesgo Apex")]
		public double TrailingDrawdown { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Max daily loss propio (USD)", Order = 17, GroupName = "4. Riesgo Apex")]
		public double MaxDailyLoss { get; set; }
		#endregion

		#region Estado interno
		private ATR atr15m;

		// Estructura 15m: pivotes para sesgo y referencia de CHoCH
		private readonly List<double> swingHighs15m = new List<double>();
		private readonly List<double> swingLows15m  = new List<double>();

		// Swings 1m para mini-CHoCH dentro del FVG
		private readonly List<double> swingHighs1m = new List<double>();
		private readonly List<double> swingLows1m  = new List<double>();

		private int trend; // 1 alcista, -1 bajista, 0 sin sesgo

		// Maquina de estados 15m: buscar barrida → CHoCH + FVG
		// 0 = buscando barrida, 1 = barrida detectada, esperando CHoCH+FVG
		private int    sweepState15m;
		private double sweepLevel15m;  // nivel de liquidez barrido
		private int    sweepBar15m;    // CurrentBars[1] cuando se detecto la barrida

		// Setup armado (FVG 15m identificado, esperando retroceso + confirmacion 1m)
		// 0 = sin setup, 1 = setup activo
		private int    setupState;
		private int    setupDir;        // 1 long, -1 short
		private double fvgUpper;        // borde superior del FVG 15m
		private double fvgLower;        // borde inferior del FVG 15m
		private double fvgInvalidPrice; // si el precio cruza este nivel, setup cancelado
		private int    fvgArmedBar;     // CurrentBars[0] (1m) cuando se armo el setup
		private bool   priceInFvg;      // precio ya toco la zona FVG al menos una vez
		private Order  entryOrder;

		// Control de riesgo / dia
		private bool   tradedToday;
		private bool   tradingDisabled;
		private double sessionStartCumPnl;
		private double accountHighWater;

		// Consistencia 50% Apex (solo tiempo real; en backtest validar con analyze_backtest.py)
		private DailyPnlTracker pnlTracker;
		private int lastTradeCount;

		// Alertas Telegram (Stream C, alerts/TelegramAlerts.cs). Se auto-deshabilita si no hay
		// TELEGRAM_BOT_TOKEN / TELEGRAM_CHAT_ID en el entorno (N8 pendiente): wiring inerte
		// hasta que el operador ponga el token. Solo se instancia en vivo, igual que pnlTracker.
		private TelegramAlerts alerts;
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name                 = "ApexNqIctStrategy";
				Description          = "ICT sweep+CHoCH+FVG para NQ/MNQ. 15m sesgo/FVG, 1m gatillo.";
				Calculate            = Calculate.OnBarClose;
				EntriesPerDirection  = 1;
				EntryHandling        = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds    = 60;
				BarsRequiredToTrade  = 20;
				IncludeCommission    = true;

				Contratos            = 2;
				StopLossUsd          = 250;
				ProfitTargetUsd      = 700;
				SwingStrength15m     = 3;
				SwingStrength1m      = 2;
				DisplacementAtrMult  = 1.5;
				MinFvgPoints         = 6.0;   // candles 15m → gap mayor que en 5m
				RejectionWickRatio   = 1.5;
				FvgValidBars         = 60;    // 1m bars (~1h para que el precio retroceda al FVG)
				SweepChochMaxBars15m = 4;     // barras 15m para ver CHoCH despues de la barrida
				StopBufferTicks      = 2;
				KillZoneStart        = 930;   // 09:30 ET (08:30 Colombia)
				KillZoneEnd          = 1100;  // 11:00 ET (10:00 Colombia, horario verano EE.UU.)
				ForcedExit           = 1400;  // 14:00 ET: bloquea nuevas entradas, trade activo sigue
				StartingBalance      = 50000;
				TrailingDrawdown     = 2500;
				MaxDailyLoss         = 400;
			}
			else if (State == State.Configure)
			{
				// Serie primaria = 1m (gatillo de entrada en confirmacion).
				// Serie secundaria = 15m (sesgo, barrida, CHoCH, FVG).
				AddDataSeries(BarsPeriodType.Minute, 15);
			}
			else if (State == State.DataLoaded)
			{
				atr15m           = ATR(BarsArray[1], 14);
				accountHighWater = StartingBalance;
			}
			else if (State == State.Realtime)
			{
				// Activar tracker de consistencia solo en tiempo real. En backtest
				// DateTime.Now no corresponde a la barra y persistir JSON seria incorrecto.
				pnlTracker     = new DailyPnlTracker();
				lastTradeCount = SystemPerformance.AllTrades.Count;

				alerts = new TelegramAlerts();
				alerts.SendAsync(TelegramAlerts.Msg.BotStart, Instrument.FullName);
			}
			else if (State == State.Terminated)
			{
				alerts?.SendAsync(TelegramAlerts.Msg.BotStop);
				pnlTracker?.Save();
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBars[0] < BarsRequiredToTrade || CurrentBars[1] < BarsRequiredToTrade)
				return;

			if (BarsInProgress == 1) // 15m: actualizar sesgo, swings y detectar setup
			{
				UpdateTrend15m();
				TryDetectSetup15m();
				return;
			}

			if (BarsInProgress != 0) return;

			// --- Logica 1m ---
			Track1mSwings();

			if (Bars.IsFirstBarOfSession)
				ResetForNewSession();

			UpdateRiskGuards();
			RecordClosedTrades();

			// MarketCalendar adelanta el cierre en medias sesiones CME (12:45 ET).
			int forcedExitToday = Math.Min(ForcedExit, MarketCalendar.BotForceCloseTime(Time[0]));
			if (ToTime(Time[0]) >= forcedExitToday * 100)
			{
				// Bloquear nuevas entradas pasada la ventana.
				// Si hay posicion activa, dejar correr hasta TP o SL (1 oportunidad/dia).
				setupState = 0;
				sweepState15m = 0;
				return;
			}

			// Festivo CME completo o fin de semana (MarketCalendar, C6): no armar setups.
			// Cancela cualquier limite que hubiera quedado vivo de una sesion anterior.
			if (MarketCalendar.ShouldSkipToday(Time[0]))
			{
				if (setupState == 1)
				{
					CancelEntry();
					setupState = 0;
				}
				return;
			}

			bool inKillZone = ToTime(Time[0]) >= KillZoneStart * 100
			               && ToTime(Time[0]) <  KillZoneEnd   * 100;

			if (setupState == 1)
			{
				ManageSetup1m(inKillZone);
			}
		}

		#region Tendencia y swings 15m (estructura HH/HL)
		private void UpdateTrend15m()
		{
			int s = SwingStrength15m;
			if (CurrentBars[1] < 2 * s + 1) return;

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
				bool hh = swingHighs15m[n-1] > swingHighs15m[n-2];
				bool hl = swingLows15m[m-1]  > swingLows15m[m-2];
				bool lh = swingHighs15m[n-1] < swingHighs15m[n-2];
				bool ll = swingLows15m[m-1]  < swingLows15m[m-2];

				if (hh && hl)      trend = 1;
				else if (lh && ll) trend = -1;
				// mixto: mantener sesgo anterior (evita whipsaw)
			}
		}
		#endregion

		#region Deteccion de setup en 15m (barrida → CHoCH → FVG)
		private void TryDetectSetup15m()
		{
			if (setupState == 1 || tradedToday || tradingDisabled || trend == 0) return;
			if (!ApexBridgeState.TradingEnabled) return;

			double atrVal = atr15m[0];
			if (atrVal <= 0) return;

			if (sweepState15m == 0)
			{
				// Paso 2: detectar barrida de liquidez contra-tendencia
				// La vela perforo el ultimo swing y el cierre recupero: validacion de cierre obligatoria
				if (trend == 1 && swingLows15m.Count > 0)
				{
					double lastLow = swingLows15m[swingLows15m.Count - 1];
					if (Low[0] < lastLow && Close[0] > lastLow)
					{
						sweepLevel15m = lastLow;
						sweepBar15m   = CurrentBars[1];
						sweepState15m = 1;
					}
				}
				else if (trend == -1 && swingHighs15m.Count > 0)
				{
					double lastHigh = swingHighs15m[swingHighs15m.Count - 1];
					if (High[0] > lastHigh && Close[0] < lastHigh)
					{
						sweepLevel15m = lastHigh;
						sweepBar15m   = CurrentBars[1];
						sweepState15m = 1;
					}
				}
			}
			else // sweepState15m == 1: barrida detectada, buscar CHoCH + FVG
			{
				// Vencimiento: demasiadas barras sin CHoCH
				if ((CurrentBars[1] - sweepBar15m) > SweepChochMaxBars15m)
				{
					sweepState15m = 0;
					return;
				}

				// Paso 3 + 4: CHoCH (cierre mas alla del ultimo swing) + desplazamiento + FVG
				if (CurrentBars[1] < 3) return;

				if (trend == 1) // CHoCH alcista
				{
					if (swingHighs15m.Count == 0) return;
					double chochLevel = swingHighs15m[swingHighs15m.Count - 1];

					double body      = Close[0] - Open[0];
					bool isChoch     = Close[0] > chochLevel;
					bool isDisplace  = body > 0 && body >= DisplacementAtrMult * atrVal;

					// FVG alcista: hueco entre High[2] (vela 1) y Low[0] (vela 3)
					double fvgL = High[2];
					double fvgU = Low[0];
					bool hasFvg  = fvgU > fvgL && (fvgU - fvgL) >= MinFvgPoints;

					if (isChoch && isDisplace && hasFvg)
					{
						// Invalidacion: si el precio cae por debajo del extremo de la barrida
						double invalidLvl = sweepLevel15m - StopBufferTicks * TickSize;
						ArmSetup(1, fvgL, fvgU, invalidLvl);
						sweepState15m = 0;
					}
				}
				else // trend == -1: CHoCH bajista
				{
					if (swingLows15m.Count == 0) return;
					double chochLevel = swingLows15m[swingLows15m.Count - 1];

					double body     = Open[0] - Close[0];
					bool isChoch    = Close[0] < chochLevel;
					bool isDisplace = body > 0 && body >= DisplacementAtrMult * atrVal;

					// FVG bajista: hueco entre Low[2] (vela 1) y High[0] (vela 3)
					double fvgU = Low[2];
					double fvgL = High[0];
					bool hasFvg  = fvgU > fvgL && (fvgU - fvgL) >= MinFvgPoints;

					if (isChoch && isDisplace && hasFvg)
					{
						double invalidLvl = sweepLevel15m + StopBufferTicks * TickSize;
						ArmSetup(-1, fvgL, fvgU, invalidLvl);
						sweepState15m = 0;
					}
				}
			}
		}

		private void ArmSetup(int dir, double lower, double upper, double invalidPrice)
		{
			setupDir        = dir;
			fvgLower        = Instrument.MasterInstrument.RoundToTickSize(lower);
			fvgUpper        = Instrument.MasterInstrument.RoundToTickSize(upper);
			fvgInvalidPrice = Instrument.MasterInstrument.RoundToTickSize(invalidPrice);
			fvgArmedBar     = CurrentBars[0]; // barra 1m en el momento de armar
			priceInFvg      = false;
			setupState      = 1;
			swingHighs1m.Clear();
			swingLows1m.Clear();

			Print($"[SETUP] Dir={dir} FVG [{fvgLower:F2},{fvgUpper:F2}] Invalid={fvgInvalidPrice:F2}");
		}
		#endregion

		#region Gestion del setup en 1m (retroceso → confirmacion → entrada)
		private void ManageSetup1m(bool inKillZone)
		{
			// Si ya estamos en posicion, stop/target adjuntos gestionan la salida
			if (Position.MarketPosition != MarketPosition.Flat) return;

			// Cancelar si el setup expiro o la estructura del sweep fue invalidada
			bool expired = (CurrentBars[0] - fvgArmedBar) > FvgValidBars;
			bool invalid = setupDir == 1 ? Low[0]  <= fvgInvalidPrice
			                             : High[0] >= fvgInvalidPrice;

			if (expired || invalid)
			{
				setupState = 0;
				return;
			}

			// Solo entrar dentro de la kill zone (el setup puede armarse antes; la entrada no)
			if (!inKillZone) return;

			// Consistencia 50% Apex (solo en vivo)
			if (pnlTracker != null && pnlTracker.WouldViolateConsistency(ProfitTargetUsd))
			{
				Print($"[CONSISTENCIA] Setup saltado: ganarlo violaria regla 50% Apex "
				    + $"(hoy {pnlTracker.TodayPnl:C} + {ProfitTargetUsd:C} vs total {pnlTracker.TotalPnl:C}).");
				setupState = 0;
				return;
			}

			// Paso 5a: detectar que el precio entro en la zona del FVG
			if (setupDir == 1)
			{
				if (Low[0] <= fvgUpper && Low[0] >= fvgLower)
					priceInFvg = true;
			}
			else
			{
				if (High[0] >= fvgLower && High[0] <= fvgUpper)
					priceInFvg = true;
			}

			if (!priceInFvg) return;

			// Paso 5b: confirmacion en 1m (rechazo O mini-CHoCH)
			if (HasConfirmation1m())
				EnterOnConfirmation();
		}

		private bool HasConfirmation1m()
		{
			double bodySize  = Math.Abs(Close[0] - Open[0]);
			double lowerWick = Math.Min(Open[0], Close[0]) - Low[0];
			double upperWick = High[0] - Math.Max(Open[0], Close[0]);

			if (setupDir == 1) // long: rechazo con mecha inferior + cierre alcista
			{
				bool rejection = bodySize > 0
				              && lowerWick >= RejectionWickRatio * bodySize
				              && Close[0]  > Open[0];
				bool miniChoch = swingHighs1m.Count > 0
				              && Close[0] > swingHighs1m[swingHighs1m.Count - 1];
				return rejection || miniChoch;
			}
			else // short: rechazo con mecha superior + cierre bajista
			{
				bool rejection = bodySize > 0
				              && upperWick >= RejectionWickRatio * bodySize
				              && Close[0]  < Open[0];
				bool miniChoch = swingLows1m.Count > 0
				              && Close[0] < swingLows1m[swingLows1m.Count - 1];
				return rejection || miniChoch;
			}
		}

		private void EnterOnConfirmation()
		{
			string sig = setupDir == 1 ? "LongFVG" : "ShortFVG";

			// Stop y target en USD fijo (relacion 1:3 segun estrategia).
			// CalculationMode.Currency ajusta los puntos automaticamente al instrumento y a Contratos.
			SetStopLoss(sig, CalculationMode.Currency, StopLossUsd, false);
			SetProfitTarget(sig, CalculationMode.Currency, ProfitTargetUsd);

			if (setupDir == 1)
				EnterLong(0, true, Contratos, sig);
			else
				EnterShort(0, true, Contratos, sig);

			setupState = 0;
		}
		#endregion

		#region Tracking swings 1m (para mini-CHoCH)
		private void Track1mSwings()
		{
			int s = SwingStrength1m;
			if (CurrentBars[0] < 2 * s + 1) return;

			int p = s;
			bool isHigh = true, isLow = true;
			double pivH = High[p];
			double pivL = Low[p];
			for (int i = 1; i <= s; i++)
			{
				if (High[p - i] >= pivH || High[p + i] >= pivH) isHigh = false;
				if (Low[p - i]  <= pivL || Low[p + i]  <= pivL) isLow  = false;
			}
			if (isHigh) AddCapped(swingHighs1m, pivH);
			if (isLow)  AddCapped(swingLows1m,  pivL);
		}
		#endregion

		#region Riesgo Apex (aproximaciones locales)
		private void ResetForNewSession()
		{
			tradedToday        = false;
			tradingDisabled    = false;
			setupState         = 0;
			sweepState15m      = 0;
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

			// Proxy trailing DD: distancia desde el pico de equity al valor actual
			if ((accountHighWater - equity) >= TrailingDrawdown)
			{
				if (!tradingDisabled)
				{
					Print($"[RIESGO] Trailing DD proxy alcanzado ({TrailingDrawdown:C}). Trading deshabilitado.");
					alerts?.SendAsync(TelegramAlerts.Msg.DailyLossWarning,
						$"Trailing DD proxy {TrailingDrawdown:C} alcanzado. Trading off.");
				}
				tradingDisabled = true;
			}

			// Daily loss sobre P&L realizado de la sesion actual
			double sessionPnl = cumRealized - sessionStartCumPnl;
			if (sessionPnl <= -MaxDailyLoss)
			{
				if (!tradingDisabled)
				{
					Print($"[RIESGO] Max daily loss alcanzado ({sessionPnl:C}). Trading deshabilitado hoy.");
					alerts?.SendAsync(TelegramAlerts.Msg.DailyLossWarning,
						$"Max daily loss {sessionPnl:C}. Trading off hoy.");
				}
				tradingDisabled = true;
			}

			if (tradingDisabled)
				FlattenAndCancel("Riesgo: flat");
		}

		// Registra trades cerrados en el tracker de consistencia.
		// Solo activo en vivo (pnlTracker null en backtest). Usa SystemPerformance
		// para evitar problemas de timing con OnExecutionUpdate.
		private void RecordClosedTrades()
		{
			if (pnlTracker == null) return;
			int total = SystemPerformance.AllTrades.Count;
			for (int i = lastTradeCount; i < total; i++)
			{
				double tradePnl = SystemPerformance.AllTrades[i].ProfitCurrency;
				pnlTracker.RecordTrade(tradePnl);
				alerts?.SendAsync(TelegramAlerts.Msg.TradeClosed, $"PnL {tradePnl:C}");
			}
			lastTradeCount = total;
		}
		#endregion

		#region Ordenes / utilidades
		private void AddCapped(List<double> list, double v)
		{
			list.Add(v);
			if (list.Count > 50) list.RemoveAt(0);
		}

		private void CancelEntry()
		{
			if (entryOrder != null && (entryOrder.OrderState == OrderState.Working
			                        || entryOrder.OrderState == OrderState.Accepted))
				CancelOrder(entryOrder);
			entryOrder = null;
		}

		private void FlattenAndCancel(string reason)
		{
			if (entryOrder != null && (entryOrder.OrderState == OrderState.Working
			                        || entryOrder.OrderState == OrderState.Accepted))
				CancelOrder(entryOrder);
			entryOrder = null;

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
			if ((execution.Order.Name == "LongFVG" || execution.Order.Name == "ShortFVG")
			    && execution.Order.OrderState == OrderState.Filled)
			{
				tradedToday = true;
				setupState  = 0;
				alerts?.SendAsync(TelegramAlerts.Msg.TradeOpened,
					$"{(execution.Order.Name == "LongFVG" ? "LONG" : "SHORT")} {quantity} @ {price:F2}");
			}
		}
		#endregion
	}
}

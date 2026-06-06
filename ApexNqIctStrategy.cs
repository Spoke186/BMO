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

		[NinjaScriptProperty]
		[Display(Name = "Habilitar Setup B (sweep directo sin FVG)", Order = 18, GroupName = "2. Estrategia")]
		public bool EnableSetupB { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Plantilla sesion serie 15m (datos overnight)", Order = 19, GroupName = "3. Horario")]
		public string SessionTemplateName { get; set; }
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

		// Rango pre-apertura (.md §4): high/low desde apertura de sesion hasta 9:30 ET.
		// Son los niveles de liquidez que la barrida debe perforar y recuperar.
		private double preMarketHigh;
		private double preMarketLow;
		private bool   preMarketReady;    // true desde que cierra la ventana pre-apertura
		private bool   preMarketAttempted; // fallback 15m intentado (corre 1 vez por sesion)

		// Maquina de estados 15m: buscar barrida → CHoCH + FVG
		// 0 = buscando barrida, 1 = barrida detectada, esperando CHoCH+FVG
		private int    sweepState15m;
		private double sweepLevel15m;  // nivel de liquidez barrido (del rango pre-apertura)
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

		// Signal name del trade activo (para ExitLong/Short correcto con Setup A y B).
		private string activeSignal;

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

		// Bitácora DEMO Notion (infra/NotionLogger.cs). Requiere NOTION_API_KEY en entorno.
		// Registra apertura al abrir trade y actualiza el cierre al cerrar.
		private NotionLogger notion;
		private System.Threading.Tasks.Task<string> notionPageTask;
		private string  notionCurrentPageId;
		private double  lastEntryPrice;
		private int     lastEntryDir;
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
				EnableSetupB         = true;
				SessionTemplateName  = "CME US Index Futures ETH";  // Globex 24h: trae datos overnight para el rango pre-apertura
				KillZoneStart        = 930;   // 09:30 ET (08:30 Colombia)
				KillZoneEnd          = 1100;  // 11:00 ET = Colombia 10:00 (EDT). En EST (invierno): 1000.
				ForcedExit           = 1400;  // 14:00 ET: bloquea nuevas entradas; posicion abierta corre a TP/SL
				                             // (operador G3: "dejar que termine, 1 oportunidad/dia")
				StartingBalance      = 50000;
				TrailingDrawdown     = 2500;
				MaxDailyLoss         = 400;
			}
			else if (State == State.Configure)
			{
				// Serie primaria = 1m (gatillo de entrada en confirmacion).
				// Serie secundaria = 15m (sesgo, barrida, CHoCH, FVG).
				// Fijamos la plantilla ETH (Globex 24h) en el codigo: el rango
				// pre-apertura necesita barras nocturnas. Asi NT8 NO depende de la
				// plantilla que elija el usuario en el Strategy Analyzer; aunque el
				// 1m primario sea RTH, el fallback de OnBarUpdate reconstruye el rango
				// desde esta serie 15m con datos overnight. instrumentName=null =>
				// instrumento primario.
				if (!string.IsNullOrWhiteSpace(SessionTemplateName))
					AddDataSeries(null, BarsPeriodType.Minute, 15, MarketDataType.Last, SessionTemplateName);
				else
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

				notion = new NotionLogger();
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
				PublishState();
				return;
			}

			if (BarsInProgress != 0) return;

			// --- Logica 1m ---
			Track1mSwings();

			if (Bars.IsFirstBarOfSession)
				ResetForNewSession();

			// Construir rango pre-apertura (.md §4) con barras 1m antes de las 9:30 ET.
			// Una vez llegamos a KillZoneStart, el rango queda fijo para el dia.
			if (!preMarketReady)
			{
				if (ToTime(Time[0]) < KillZoneStart * 100)
				{
					preMarketHigh = Math.Max(preMarketHigh, High[0]);
					preMarketLow  = Math.Min(preMarketLow,  Low[0]);
				}
				else if (preMarketHigh > double.MinValue)
				{
					preMarketReady = true;
					Print($"[PRE-AP] Range 1m [{preMarketLow:F2}, {preMarketHigh:F2}]");
				}
				else if (!preMarketAttempted)
				{
					// Sin barras 1m nocturnas (plantilla RTH). Intento unico: escanear
					// la serie 15m hacia atras buscando barras previas a las 9:30 ET.
					// Si el grafico es ETH de 15m, esto reconstruye el rango overnight.
					preMarketAttempted = true;
					int scan = Math.Min(CurrentBars[1], 24); // hasta 6h de barras 15m
					for (int i = 0; i < scan; i++)
					{
						if (ToTime(Times[1][i]) < KillZoneStart * 100)
						{
							preMarketHigh = Math.Max(preMarketHigh, Highs[1][i]);
							preMarketLow  = Math.Min(preMarketLow,  Lows[1][i]);
						}
					}
					if (preMarketHigh > double.MinValue)
					{
						preMarketReady = true;
						Print($"[PRE-AP] Range 15m fallback [{preMarketLow:F2}, {preMarketHigh:F2}]");
					}
					else
					{
						// Ambas series son RTH: sin niveles de liquidez para hoy.
						// Usar plantilla ETH (Globex 24h) en Strategy Analyzer / grafico live.
						Print("[PRE-AP] SIN datos overnight. Usar plantilla ETH. Sin setups hoy.");
					}
				}
			}

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

			// Setup B: sweep del rango pre-mercado en 1m sin esperar CHoCH+FVG.
			// preMarketReady garantiza que los niveles estan disponibles.
			if (EnableSetupB && preMarketReady && inKillZone
			    && !tradedToday && !tradingDisabled && setupState == 0)
				TryDetectSweepB();

			PublishState();
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
			// Rango pre-apertura aun no esta listo (aun no llegamos a las 9:30 ET)
			if (!preMarketReady) return;

			double atrVal = atr15m[0];
			if (atrVal <= 0) return;

			if (sweepState15m == 0)
			{
				// Paso 2: barrida del rango pre-apertura (.md §4/§5).
				// La vela 15m perfora el nivel pre-apertura con la mecha y el CIERRE recupera.
				if (trend == 1)
				{
					// Dia alcista: barrer el MINIMO pre-apertura (liquidez debajo)
					if (Low[0] < preMarketLow && Close[0] > preMarketLow)
					{
						sweepLevel15m = preMarketLow;
						sweepBar15m   = CurrentBars[1];
						sweepState15m = 1;
					}
				}
				else if (trend == -1)
				{
					// Dia bajista: barrer el MAXIMO pre-apertura (liquidez arriba)
					if (High[0] > preMarketHigh && Close[0] < preMarketHigh)
					{
						sweepLevel15m = preMarketHigh;
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
				EnterLong(0, Contratos, sig);
			else
				EnterShort(0, Contratos, sig);

			setupState = 0;
		}
		#endregion

		#region Setup B: Opening Range Sweep (entrada directa sin FVG)
		private void TryDetectSweepB()
		{
			if (!ApexBridgeState.TradingEnabled) return;

			// Sweep del HIGH pre-mercado en 1m: mecha lo perfora, cierre recupera abajo + vela bajista.
			// Patron: liquidez arriba barrida → SHORT inmediato (sin esperar CHoCH ni FVG).
			if (High[0] > preMarketHigh && Close[0] < preMarketHigh && Close[0] < Open[0])
			{
				EnterSweepB(-1);
				return;
			}

			// Sweep del LOW pre-mercado en 1m: mecha lo perfora, cierre recupera arriba + vela alcista.
			if (Low[0] < preMarketLow && Close[0] > preMarketLow && Close[0] > Open[0])
			{
				EnterSweepB(1);
			}
		}

		private void EnterSweepB(int dir)
		{
			if (pnlTracker != null && pnlTracker.WouldViolateConsistency(ProfitTargetUsd))
			{
				Print("[CONSISTENCIA] SweepB saltado: violaria regla 50% Apex.");
				return;
			}

			string sig = dir == 1 ? "LongSweep" : "ShortSweep";
			SetStopLoss(sig, CalculationMode.Currency, StopLossUsd, false);
			SetProfitTarget(sig, CalculationMode.Currency, ProfitTargetUsd);

			if (dir == 1)
				EnterLong(0, Contratos, sig);
			else
				EnterShort(0, Contratos, sig);

			Print($"[SETUP-B] {sig} PreMkt=[{preMarketLow:F2},{preMarketHigh:F2}]");
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
			preMarketHigh      = double.MinValue;
			preMarketLow       = double.MaxValue;
			preMarketReady     = false;
			preMarketAttempted = false;
			activeSignal       = "";
			sessionStartCumPnl = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
			lock (ApexBridgeState.TodayTradesLock)
				ApexBridgeState.TodayTrades.Clear();
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
				var    t        = SystemPerformance.AllTrades[i];
				double tradePnl = t.ProfitCurrency;
				pnlTracker.RecordTrade(tradePnl);
				alerts?.SendAsync(TelegramAlerts.Msg.TradeClosed, $"PnL {tradePnl:C}");

				// Publicar trade al AddOn (B6) — accesible via GET /trades/today
				lock (ApexBridgeState.TodayTradesLock)
					ApexBridgeState.TodayTrades.Add(new TradeSummary
					{
						Direction  = t.Entry.MarketPosition == MarketPosition.Long ? "LONG" : "SHORT",
						EntryPrice = t.Entry.Price,
						ExitPrice  = t.Exit.Price,
						PnlUsd     = tradePnl,
						ExitTime   = t.Exit.Time.ToString("HH:mm:ss"),
						Result     = tradePnl > 0 ? "WIN" : "LOSS",
					});

				// Resolver el pageId de Notion (la apertura puede haber tardado unos ms)
				if (notionPageTask != null)
				{
					if (notionPageTask.IsCompleted)
						notionCurrentPageId = notionPageTask.Result;
					notionPageTask = null;
				}
				if (notion != null && !string.IsNullOrEmpty(notionCurrentPageId))
				{
					double tol    = 50;
					string notas  = tradePnl >=  ProfitTargetUsd - tol ? "Target alcanzado"
					              : tradePnl <= -StopLossUsd     + tol ? "Stop alcanzado"
					              :                                       "Cierre por horario o parcial";
					notion.ActualizarCierreAsync(notionCurrentPageId, tradePnl, notas);
					notionCurrentPageId = null;
				}
			}
			lastTradeCount = total;
		}
		#endregion

		// Publica estado ICT en ApexBridgeState para que el AddOn lo exponga via GET /setup.
		// Llamado al final de cada bar 1m y 15m. No es thread-safe estricto (double no volatile),
		// pero la imprecision es aceptable: los datos son solo para monitoreo, no para ordenes.
		private void PublishState()
		{
			ApexBridgeState.Trend           = trend;
			ApexBridgeState.PreMarketReady  = preMarketReady;
			ApexBridgeState.PreMarketHigh   = preMarketHigh > double.MinValue ? preMarketHigh : 0;
			ApexBridgeState.PreMarketLow    = preMarketLow  < double.MaxValue ? preMarketLow  : 0;
			ApexBridgeState.SweepState      = sweepState15m;
			ApexBridgeState.SweepLevel      = sweepLevel15m;
			ApexBridgeState.SetupState      = setupState;
			ApexBridgeState.SetupDir        = setupDir;
			ApexBridgeState.FvgLower        = fvgLower;
			ApexBridgeState.FvgUpper        = fvgUpper;
			ApexBridgeState.PriceInFvg      = priceInFvg;
			ApexBridgeState.TradedToday     = tradedToday;
			ApexBridgeState.TradingDisabled = tradingDisabled;
		}

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
				ExitLong(!string.IsNullOrEmpty(activeSignal) ? activeSignal : "LongFVG");
			else if (Position.MarketPosition == MarketPosition.Short)
				ExitShort(!string.IsNullOrEmpty(activeSignal) ? activeSignal : "ShortFVG");
			setupState = 0;
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
			int quantity, int filled, double averageFillPrice, OrderState orderState,
			DateTime time, ErrorCode error, string nativeError)
		{
			if (order == null) return;
			if (order.Name == "LongFVG"   || order.Name == "ShortFVG" ||
			    order.Name == "LongSweep" || order.Name == "ShortSweep")
				entryOrder = order;
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId,
			double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order == null) return;
			if ((execution.Order.Name == "LongFVG"   || execution.Order.Name == "ShortFVG" ||
			     execution.Order.Name == "LongSweep" || execution.Order.Name == "ShortSweep")
			    && execution.Order.OrderState == OrderState.Filled)
			{
				tradedToday    = true;
				setupState     = 0;
				activeSignal   = execution.Order.Name;
				lastEntryPrice = price;
				lastEntryDir   = (execution.Order.Name == "LongFVG" || execution.Order.Name == "LongSweep") ? 1 : -1;

				alerts?.SendAsync(TelegramAlerts.Msg.TradeOpened,
					$"{(lastEntryDir == 1 ? "LONG" : "SHORT")} {quantity} @ {price:F2}");

				if (notion != null)
				{
					double ptVal   = Instrument.MasterInstrument.PointValue;
					double stopPts = StopLossUsd    / (ptVal * Contratos);
					double tpPts   = ProfitTargetUsd / (ptVal * Contratos);
					double stopPx  = lastEntryDir == 1 ? price - stopPts : price + stopPts;
					double tpPx    = lastEntryDir == 1 ? price + tpPts   : price - tpPts;

					// Hora Colombia: UTC-5 (siempre, sin DST)
					var colZone    = TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
					var colTime    = TimeZoneInfo.ConvertTime(DateTime.Now, colZone);
					string horaCol = colTime.ToString("HH:mm");

					notionPageTask = notion.RegistrarAperturaAsync(
						esFondeo:    false,
						activo:      Instrument.FullName,
						dir:         lastEntryDir,
						entryPrice:  price,
						stopPrice:   stopPx,
						targetPrice: tpPx,
						horaCol:     horaCol,
						fecha:       DateTime.Now.Date);
				}
			}
		}
		#endregion
	}
}

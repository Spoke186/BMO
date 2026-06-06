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
		[Display(Name = "Setup B: ticks minimos de sweep (0 = sin filtro)", Order = 19, GroupName = "2. Estrategia")]
		[Range(0, 50)]
		public int MinSweepTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Setup B: ticks minimos cuerpo vela (0 = sin filtro)", Order = 20, GroupName = "2. Estrategia")]
		[Range(0, 50)]
		public int MinBodyTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Setup B: alinear con tendencia 15m", Order = 21, GroupName = "2. Estrategia")]
		public bool SetupBRequiresTrend { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Inicio ventana pre-mercado (HHmm ET, 0=sesion completa)", Order = 22, GroupName = "3. Horario")]
		public int PreMarketStartTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Setup B: minutos max despues de KillZoneStart (0=sin limite)", Order = 23, GroupName = "3. Horario")]
		[Range(0, 120)]
		public int SetupBMaxMinutes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Filtro sesgo diario (premarket vs cierre ayer)", Order = 24, GroupName = "2. Estrategia")]
		public bool EnableDailyBiasFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Habilitar Setup C (Order Block 1m)", Order = 25, GroupName = "2. Estrategia")]
		public bool EnableSetupC { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Setup C: ticks minimos desplazamiento (cuerpo)", Order = 26, GroupName = "2. Estrategia")]
		[Range(5, 200)]
		public int MinOBBodyTicks { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Setup C: barras max para retorno al OB (1m)", Order = 27, GroupName = "2. Estrategia")]
		[Range(5, 120)]
		public int OBValidBars { get; set; }
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
		private double sweepLevel15m;     // nivel de liquidez barrido (del rango pre-apertura)
		private int    sweepBar15m;       // CurrentBars[1] cuando se detecto la barrida
		private bool   levelBrokenLow15m; // true si ya perforamos preMarketLow (sweep multi-barra)
		private bool   levelBrokenHigh15m;// true si ya perforamos preMarketHigh (sweep multi-barra)

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

		// Setup C: Order Block 1m
		private int    obState;   // 0=buscando, 1=OB identificado esperando retorno
		private double obHigh;
		private double obLow;
		private int    obDir;     // 1=long, -1=short
		private int    obBar;     // CurrentBars[0] cuando se identifico el OB

		// Sesgo diario: cierre de la sesion anterior para determinar direccion del dia.
		private double prevSessionClose;

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
				DisplacementAtrMult  = 0.8;   // cuerpo >= 0.8 × ATR15m; 1.5 era imposible (eliminaba CHoCH real)
				MinFvgPoints         = 3.0;   // gap minimo 3 pts NQ; antes 6 era demasiado estricto
				RejectionWickRatio   = 1.5;
				FvgValidBars         = 60;    // 1m bars (~1h para que el precio retroceda al FVG)
				SweepChochMaxBars15m = 8;     // 8 barras 15m = 2h para ver CHoCH despues de la barrida
				StopBufferTicks      = 2;
				EnableSetupB         = true;
				MinSweepTicks        = 10;    // sweep moderado: 10 ticks = 2.5 pts NQ
				MinBodyTicks         = 6;     // cuerpo minimo: 6 ticks = 1.5 pts NQ
				SetupBRequiresTrend     = true;  // alinear con sesgo 15m mejora win rate
				EnableDailyBiasFilter   = true;  // solo longs si premkt > cierre ayer, solo shorts si < cierre
				PreMarketStartTime   = 800;   // rango pre-mercado: 8:00–9:30 ET (pre-market US)
				SetupBMaxMinutes     = 30;    // primeros 30 min del kill zone (9:30-10:00 ET)
				EnableSetupC         = false; // OB solo sin sweep = sin validacion → off por defecto
				MinOBBodyTicks       = 20;    // desplazamiento minimo: 20 ticks = 5 pts NQ
				OBValidBars          = 45;    // OB valido por 45 barras 1m = 45 min
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

			if (BarsInProgress == 1) // 15m: sesgo, swings, Setup A
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
			{
				prevSessionClose = Close[1]; // ultima vela de la sesion anterior
				ResetForNewSession();
			}

			// Construir rango pre-apertura con barras 1m entre PreMarketStartTime y KillZoneStart.
			// Ventana por defecto: 8:00–9:30 ET (pre-market US; excluye ruido de sesion nocturna).
			// Una vez llegamos a KillZoneStart, el rango queda fijo para el dia.
			if (!preMarketReady)
			{
				int t = ToTime(Time[0]);
				int pmStart = PreMarketStartTime * 100; // 0 = aceptar desde inicio de sesion
				bool inWindow = (pmStart == 0 || t >= pmStart) && t < KillZoneStart * 100;

				if (inWindow)
				{
					preMarketHigh = Math.Max(preMarketHigh, High[0]);
					preMarketLow  = Math.Min(preMarketLow,  Low[0]);
				}
				else if (t >= KillZoneStart * 100 && preMarketHigh > double.MinValue)
				{
					preMarketReady = true;
					Print($"[PRE-AP] Range 1m {PreMarketStartTime}–{KillZoneStart} ET [{preMarketLow:F2}, {preMarketHigh:F2}]");
				}
				else if (t >= KillZoneStart * 100 && !preMarketAttempted)
				{
					// Sin barras 1m en la ventana (plantilla RTH). Fallback: escanear 15m.
					preMarketAttempted = true;
					int scan = Math.Min(CurrentBars[1], 24); // hasta 6h de barras 15m
					for (int i = 0; i < scan; i++)
					{
						int t15 = ToTime(Times[1][i]);
						bool inW = (pmStart == 0 || t15 >= pmStart) && t15 < KillZoneStart * 100;
						if (inW)
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

			// Setup B en 1m: sweep del rango pre-mercado con filtros de calidad.
			if (EnableSetupB && preMarketReady && inKillZone
			    && !tradedToday && !tradingDisabled && setupState == 0)
				TryDetectSweepB();

			// Setup C en 1m: Order Block — detectar desplazamiento institucional y
			// entrar cuando el precio retorna a la ultima vela opuesta (OB).
			// Corre solo si A y B no dispararon (tradedToday == false).
			if (EnableSetupC && preMarketReady && inKillZone
			    && !tradedToday && !tradingDisabled && setupState == 0)
				TryDetectSetupC();

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

			int prevTrend = trend;

			// Requiere 2 swings del mismo tipo para confirmar estructura HH/HL o LH/LL.
			// Solo necesitamos 2 highs O 2 lows (no ambos) para establecer sesgo inicial.
			if (swingHighs15m.Count >= 2 && swingLows15m.Count >= 2)
			{
				int n = swingHighs15m.Count, m = swingLows15m.Count;
				bool hh = swingHighs15m[n-1] > swingHighs15m[n-2];
				bool hl = swingLows15m[m-1]  > swingLows15m[m-2];
				bool lh = swingHighs15m[n-1] < swingHighs15m[n-2];
				bool ll = swingLows15m[m-1]  < swingLows15m[m-2];

				if (hh && hl)       trend = 1;
				else if (lh && ll)  trend = -1;
				else if (hh)        trend = 1;  // HH sin HL confirmado: sesgo alcista probable
				else if (ll)        trend = -1; // LL sin LH confirmado: sesgo bajista probable
				// mixto total: mantener sesgo anterior
			}
			else if (swingHighs15m.Count >= 2)
			{
				int n = swingHighs15m.Count;
				if      (swingHighs15m[n-1] > swingHighs15m[n-2]) trend = 1;
				else if (swingHighs15m[n-1] < swingHighs15m[n-2]) trend = -1;
			}
			else if (swingLows15m.Count >= 2)
			{
				int m = swingLows15m.Count;
				if      (swingLows15m[m-1] > swingLows15m[m-2]) trend = 1;
				else if (swingLows15m[m-1] < swingLows15m[m-2]) trend = -1;
			}

			if (trend != prevTrend)
				Print($"[TREND-15m] {prevTrend} → {trend} | highs={swingHighs15m.Count} lows={swingLows15m.Count} @ {Time[0]:HH:mm}");
		}
		#endregion

		#region Deteccion de setup en 15m (barrida → CHoCH → FVG)
		private bool _loggedTrendZero; // evita spam: loguea trend=0 solo 1 vez por sesion

		private void TryDetectSetup15m()
		{
			if (setupState == 1 || tradedToday || tradingDisabled) return;
			if (!ApexBridgeState.TradingEnabled) return;
			if (!preMarketReady) return;

			if (trend == 0)
			{
				if (!_loggedTrendZero)
				{
					Print($"[SETUP-A] trend=0 en {Time[0]:HH:mm} — sin sesgo 15m, Setup A bloqueado hoy");
					_loggedTrendZero = true;
				}
				return;
			}
			_loggedTrendZero = false;

			double atrVal = atr15m[0];
			if (atrVal <= 0) return;

			if (sweepState15m == 0)
			{
				// Paso 2: barrida del rango pre-apertura en 15m (multi-barra).
				// IMPORTANTE: usar Lows[1]/Highs[1]/Closes[1] = datos de la serie 15m,
				// NO Low[0]/High[0]/Close[0] que son la serie primaria 1m.
				if (trend == 1)
				{
					if (!levelBrokenLow15m && Lows[1][0] < preMarketLow)
					{
						levelBrokenLow15m = true;
						Print($"[SWEEP-A] Low 15m perforado @ {Times[1][0]:HH:mm} nivel={preMarketLow:F2} low={Lows[1][0]:F2}");
					}
					if (levelBrokenLow15m && Closes[1][0] > preMarketLow)
					{
						sweepLevel15m     = preMarketLow;
						sweepBar15m       = CurrentBars[1];
						sweepState15m     = 1;
						levelBrokenLow15m = false;
						Print($"[SWEEP-A] Barrida LOW confirmada @ {Times[1][0]:HH:mm}. Buscando CHoCH...");
					}
				}
				else if (trend == -1)
				{
					if (!levelBrokenHigh15m && Highs[1][0] > preMarketHigh)
					{
						levelBrokenHigh15m = true;
						Print($"[SWEEP-A] High 15m perforado @ {Times[1][0]:HH:mm} nivel={preMarketHigh:F2} high={Highs[1][0]:F2}");
					}
					if (levelBrokenHigh15m && Closes[1][0] < preMarketHigh)
					{
						sweepLevel15m      = preMarketHigh;
						sweepBar15m        = CurrentBars[1];
						sweepState15m      = 1;
						levelBrokenHigh15m = false;
						Print($"[SWEEP-A] Barrida HIGH confirmada @ {Times[1][0]:HH:mm}. Buscando CHoCH...");
					}
				}
			}
			else // sweepState15m == 1: barrida detectada, buscar CHoCH + FVG en 15m
			{
				if ((CurrentBars[1] - sweepBar15m) > SweepChochMaxBars15m)
				{
					Print($"[SWEEP-A] CHoCH timeout @ {Times[1][0]:HH:mm}. Resetando.");
					sweepState15m = 0;
					return;
				}

				if (CurrentBars[1] < 3) return;

				if (trend == 1) // CHoCH alcista en 15m
				{
					if (swingHighs15m.Count == 0) return;
					double chochLevel = swingHighs15m[swingHighs15m.Count - 1];

					double body     = Closes[1][0] - Opens[1][0];
					bool isChoch    = Closes[1][0] > chochLevel;
					bool isDisplace = body > 0 && body >= DisplacementAtrMult * atrVal;

					// FVG alcista: buscar gap en ventana de 2 sets de 3 barras.
					// Ventana A: [2],[1],[0] — barra actual es la que cierra el gap.
					// Ventana B: [3],[2],[1] — barra anterior; captura FVG en la vela previa al CHoCH.
					double fvgL = 0, fvgU = 0;
					bool hasFvg = false;
					if (CurrentBars[1] >= 3)
					{
						double uA = Lows[1][0], lA = Highs[1][2];
						if (uA > lA && (uA - lA) >= MinFvgPoints) { fvgL = lA; fvgU = uA; hasFvg = true; }
					}
					if (!hasFvg && CurrentBars[1] >= 4)
					{
						double uB = Lows[1][1], lB = Highs[1][3];
						if (uB > lB && (uB - lB) >= MinFvgPoints) { fvgL = lB; fvgU = uB; hasFvg = true; }
					}

					Print($"[CHoCH-A] Long choch={isChoch}({Closes[1][0]:F0}>{chochLevel:F0}) disp={isDisplace}({body:F1}>={DisplacementAtrMult*atrVal:F1}) fvg={hasFvg}");

					if (isChoch && isDisplace && hasFvg)
					{
						double invalidLvl = sweepLevel15m - StopBufferTicks * TickSize;
						ArmSetup(1, fvgL, fvgU, invalidLvl);
						sweepState15m = 0;
					}
				}
				else // trend == -1: CHoCH bajista en 15m
				{
					if (swingLows15m.Count == 0) return;
					double chochLevel = swingLows15m[swingLows15m.Count - 1];

					double body     = Opens[1][0] - Closes[1][0];
					bool isChoch    = Closes[1][0] < chochLevel;
					bool isDisplace = body > 0 && body >= DisplacementAtrMult * atrVal;

					// FVG bajista: misma logica de ventana doble, invertido.
					// Ventana A: [2],[1],[0]; Ventana B: [3],[2],[1].
					double fvgL = 0, fvgU = 0;
					bool hasFvg = false;
					if (CurrentBars[1] >= 3)
					{
						double uA = Lows[1][2], lA = Highs[1][0];
						if (uA > lA && (uA - lA) >= MinFvgPoints) { fvgL = lA; fvgU = uA; hasFvg = true; }
					}
					if (!hasFvg && CurrentBars[1] >= 4)
					{
						double uB = Lows[1][3], lB = Highs[1][1];
						if (uB > lB && (uB - lB) >= MinFvgPoints) { fvgL = lB; fvgU = uB; hasFvg = true; }
					}

					Print($"[CHoCH-A] Short choch={isChoch}({Closes[1][0]:F0}<{chochLevel:F0}) disp={isDisplace}({body:F1}>={DisplacementAtrMult*atrVal:F1}) fvg={hasFvg}");

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

			// Sesgo diario: el punto medio del rango pre-mercado vs cierre de ayer.
			// Si premkt esta por encima del cierre anterior = dia alcista = solo longs.
			// Si esta por debajo = dia bajista = solo shorts.
			// Filtra shorts en bull market y longs en bear market (problema principal).
			if (EnableDailyBiasFilter && prevSessionClose > 0 && preMarketHigh > double.MinValue)
			{
				double midRange = (preMarketHigh + preMarketLow) / 2.0;
				bool bullishDay = midRange > prevSessionClose;
				bool bearishDay = midRange < prevSessionClose;
				if (bullishDay) // dia alcista: bloquear shorts
				{
					if (High[0] > preMarketHigh) return; // sweep del high → short bloqueado
				}
				else if (bearishDay) // dia bajista: bloquear longs
				{
					if (Low[0] < preMarketLow) return;  // sweep del low → long bloqueado
				}
			}

			// Filtro horario: solo primeros SetupBMaxMinutes del kill zone.
			// Los sweeps ICT de calidad ocurren en los primeros 15-30 min post-apertura.
			if (SetupBMaxMinutes > 0)
			{
				var kzStart = new DateTime(Time[0].Year, Time[0].Month, Time[0].Day,
				                          KillZoneStart / 100, KillZoneStart % 100, 0);
				if (Time[0] >= kzStart.AddMinutes(SetupBMaxMinutes)) return;
			}

			double minSweep = MinSweepTicks * TickSize;
			double minBody  = MinBodyTicks  * TickSize;

			// Sweep del HIGH pre-mercado: mecha perfora, cierre recupera, cuerpo bajista.
			// Confluencia: ademas del sweep, verificar OB bajista O FVG bajista en la zona.
			bool shortOk = !SetupBRequiresTrend || trend != 1;
			if (shortOk
			    && High[0]  > preMarketHigh + minSweep
			    && Close[0] < preMarketHigh
			    && (Open[0] - Close[0]) >= minBody
			    && HasConfluence(-1))
			{
				EnterSweepB(-1);
				return;
			}

			// Sweep del LOW pre-mercado: mecha perfora, cierre recupera, cuerpo alcista.
			bool longOk = !SetupBRequiresTrend || trend != -1;
			if (longOk
			    && Low[0]   < preMarketLow - minSweep
			    && Close[0] > preMarketLow
			    && (Close[0] - Open[0]) >= minBody
			    && HasConfluence(1))
			{
				EnterSweepB(1);
			}
		}

		// Confluencia: OB O FVG en la misma zona que el sweep.
		// dir=1 → buscamos confluencia alcista (vela bajista previa = OB, o FVG alcista).
		// dir=-1 → buscamos confluencia bajista.
		// Retorna true si hay al menos 1 factor de confluencia + el filtro de tendencia base.
		// Sin confluencia = sweep debil / trampa → no entrar.
		private bool HasConfluence(int dir)
		{
			if (CurrentBars[0] < 5) return false;

			// Factor 1: Order Block — ultima vela de color opuesto en las 5 barras previas
			// (zona donde los institucionales colocaron ordenes antes del impulso)
			bool hasOB = false;
			for (int i = 1; i <= 5; i++)
			{
				if (dir == 1  && Open[i] > Close[i]) { hasOB = true; break; } // vela bajista previa = OB alcista
				if (dir == -1 && Close[i] > Open[i]) { hasOB = true; break; } // vela alcista previa = OB bajista
			}

			// Factor 2: FVG en 1m — gap de 3 velas en la zona del sweep
			bool hasFvg = false;
			if (CurrentBars[0] >= 3)
			{
				if (dir == 1)  hasFvg = Lows[0][0]  > Highs[0][2] && (Lows[0][0]  - Highs[0][2]) >= MinFvgPoints;
				if (dir == -1) hasFvg = Highs[0][2] > Lows[0][0]  && (Highs[0][2] - Lows[0][0])  >= MinFvgPoints;
			}

			bool result = hasOB || hasFvg;
			Print($"[CONF-B] dir={dir} OB={hasOB} FVG={hasFvg} → {(result ? "ENTRA" : "SKIP")}");
			return result;
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

		#region Setup C: Order Block 1m
		private void TryDetectSetupC()
		{
			if (CurrentBars[0] < 3) return;

			double minBody = MinOBBodyTicks * TickSize;

			if (obState == 0)
			{
				// Detectar vela de desplazamiento institucional (cuerpo grande).
				// Desplazamiento alcista → OB = ultima vela bajista previa (zona de compras institucionales).
				// Desplazamiento bajista → OB = ultima vela alcista previa (zona de ventas institucionales).
				bool bullishDisplace = (Close[0] - Open[0]) >= minBody;
				bool bearishDisplace = (Open[0] - Close[0]) >= minBody;

				if (bullishDisplace)
				{
					// Buscar la ultima vela bajista en las 5 barras previas
					for (int i = 1; i <= 5; i++)
					{
						if (Open[i] > Close[i]) // vela bajista = posible OB alcista
						{
							obHigh  = High[i];
							obLow   = Low[i];
							obDir   = 1;
							obState = 1;
							obBar   = CurrentBars[0];
							Print($"[OB-C] Bullish OB @ {Time[0]:HH:mm} zona=[{obLow:F2},{obHigh:F2}] disp={Close[0]-Open[0]:F1}pts");
							break;
						}
					}
				}
				else if (bearishDisplace)
				{
					// Buscar la ultima vela alcista en las 5 barras previas
					for (int i = 1; i <= 5; i++)
					{
						if (Close[i] > Open[i]) // vela alcista = posible OB bajista
						{
							obHigh  = High[i];
							obLow   = Low[i];
							obDir   = -1;
							obState = 1;
							obBar   = CurrentBars[0];
							Print($"[OB-C] Bearish OB @ {Time[0]:HH:mm} zona=[{obLow:F2},{obHigh:F2}] disp={Open[0]-Close[0]:F1}pts");
							break;
						}
					}
				}
			}
			else // obState == 1: OB identificado, esperando que el precio regrese
			{
				// Timeout: si pasan demasiadas barras sin retorno, el OB pierde validez
				if (CurrentBars[0] - obBar > OBValidBars)
				{
					Print($"[OB-C] Timeout OB {(obDir==1?"Long":"Short")} @ {Time[0]:HH:mm}");
					obState = 0;
					return;
				}

				// Filtro tendencia 15m: no shortear en alcista, no longear en bajista
				if (obDir == 1  && trend == -1) { obState = 0; return; }
				if (obDir == -1 && trend ==  1) { obState = 0; return; }

				// Filtro sesgo diario
				if (EnableDailyBiasFilter && prevSessionClose > 0 && preMarketHigh > double.MinValue)
				{
					double mid = (preMarketHigh + preMarketLow) / 2.0;
					if (mid > prevSessionClose && obDir == -1) { obState = 0; return; }
					if (mid < prevSessionClose && obDir ==  1) { obState = 0; return; }
				}

				// Verificar que el precio entre a la zona del OB
				bool inOB = Low[0] <= obHigh && High[0] >= obLow;
				if (!inOB) return;

				// Confirmacion: vela de rechazo en direccion del OB
				double obMid = (obHigh + obLow) / 2.0;
				bool confirm = false;

				if (obDir == 1) // OB alcista: cierre por encima del punto medio + vela verde
					confirm = Close[0] > obMid && Close[0] > Open[0];
				else            // OB bajista: cierre por debajo del punto medio + vela roja
					confirm = Close[0] < obMid && Close[0] < Open[0];

				if (confirm)
				{
					EnterSetupC(obDir);
					obState = 0;
				}
			}
		}

		private void EnterSetupC(int dir)
		{
			if (pnlTracker != null && pnlTracker.WouldViolateConsistency(ProfitTargetUsd))
			{
				Print("[CONSISTENCIA] SetupC saltado: violaria regla 50% Apex.");
				return;
			}
			string sig = dir == 1 ? "LongOB" : "ShortOB";
			SetStopLoss(sig, CalculationMode.Currency, StopLossUsd, false);
			SetProfitTarget(sig, CalculationMode.Currency, ProfitTargetUsd);
			if (dir == 1) EnterLong(0, Contratos, sig);
			else          EnterShort(0, Contratos, sig);
			activeSignal = sig;
			tradedToday  = true;
			Print($"[SETUP-C] {sig} OB=[{obLow:F2},{obHigh:F2}]");
			alerts?.SendAsync(TelegramAlerts.Msg.TradeOpened, $"{sig} OB=[{obLow:F2},{obHigh:F2}]");
		}
		#endregion

		#region Riesgo Apex (aproximaciones locales)
		private void ResetForNewSession()
		{
			tradedToday         = false;
			tradingDisabled     = false;
			setupState          = 0;
			sweepState15m       = 0;
			levelBrokenLow15m   = false;
			levelBrokenHigh15m  = false;
			_loggedTrendZero    = false;
			preMarketHigh       = double.MinValue;
			preMarketLow        = double.MaxValue;
			preMarketReady      = false;
			preMarketAttempted  = false;
			activeSignal        = "";
			// Resetear swings y sesgo diariamente para que CHoCH compare contra
			// swings del dia actual, no contra ATHs acumulados de meses anteriores.
			swingHighs15m.Clear();
			swingLows15m.Clear();
			swingHighs1m.Clear();
			swingLows1m.Clear();
			trend               = 0;
			obState             = 0;
			obHigh              = 0;
			obLow               = 0;
			obDir               = 0;
			obBar               = 0;
			sessionStartCumPnl  = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
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

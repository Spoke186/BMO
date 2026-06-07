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
		[Range(0.1, 10)]
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

		[NinjaScriptProperty]
		[Display(Name = "Setup B: usar PDH/PDL como niveles adicionales", Order = 28, GroupName = "2. Estrategia")]
		public bool EnablePdhPdl { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Permitir 2do trade si el primero fue ganador", Order = 29, GroupName = "2. Estrategia")]
		public bool Allow2ndTradeIfWinner { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Filtro EMA tendencia 15m (EMA9 vs EMA21, dirección probable)", Order = 30, GroupName = "2. Estrategia")]
		public bool EnableEmaFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Rango pre-mercado minimo (pts, 0=desactivado)", Order = 31, GroupName = "2. Estrategia")]
		[Range(0, 500)]
		public double MinPreMarketRange { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Displacement: cuerpo minimo (fraccion del rango)", Order = 32, GroupName = "2. Estrategia")]
		[Range(0.1, 1.0)]
		public double DisplacementBodyPct { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trade Score minimo para entrar (0-100)", Order = 33, GroupName = "2. Estrategia")]
		[Range(0, 100)]
		public int MinTradeScore { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "FVG: mitigacion maxima valida (fraccion 0-1)", Order = 34, GroupName = "2. Estrategia")]
		[Range(0.1, 1.0)]
		public double FvgMitigationPct { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Bias overnight: umbral gap (pts, 0=desactivado)", Order = 35, GroupName = "2. Estrategia")]
		[Range(0, 500)]
		public double BiasOvernightThreshold { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Habilitar Setup D (FVG 15m sin barrida previa)", Order = 36, GroupName = "2. Estrategia")]
		public bool EnableSetupD { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Habilitar Setup E (Opening Range Breakout)", Order = 37, GroupName = "2. Estrategia")]
		public bool EnableSetupE { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Stop estructural maximo (USD, 0=sin limite)", Order = 38, GroupName = "4. Riesgo Apex")]
		[Range(0, 10000)]
		public double MaxStructuralRiskUsd { get; set; }
		#endregion

		#region Estado interno
		private ATR atr15m;
		private EMA    ema9_15m;       // EMA rapida 15m (usada si EnableEmaFilter=true)
		private EMA    ema21_15m;      // EMA lenta 15m
		private EMA    ema20_daily;    // EMA20 diaria (inicializada pero no usada para bias — muy lenta)
		// Sesgo diario: comparar cierre de ayer vs anteayer — responde en 1 dia al cambio de tendencia.
		// Enero alcista: ayer > anteayer casi todos los dias → BULL. Febrero bajista: lo contrario → BEAR.
		private double prevDayClose1;  // cierre RTH de ayer (actualizado cuando barra diaria cierra)
		private double prevDayClose2;  // cierre RTH de anteayer

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

		// Setup D: FVG 15m alineado con sesgo diario (EMA20 daily) — sin prereq de sweep.
		// Bullish: FVG alcista en 15m + precio baja al gap → LONG (mercado sobre EMA20 daily).
		// Bearish: FVG bajista en 15m + precio sube al gap → SHORT (mercado bajo EMA20 daily).
		private int    fvgDState;   // 0=escaneando, 1=FVG identificado esperando retroceso
		private double fvgDUpper;
		private double fvgDLower;
		private int    fvgDDir;     // 1=long, -1=short
		private int    fvgDBar;     // CurrentBars[1] (15m) cuando se detecto el FVG
		private bool   fvgDPriceIn; // precio ya toco la zona

		// Setup E: Opening Range Breakout — funciona en CUALQUIER mercado.
		// 9:30-10:00 ET: registra el rango de apertura. Despues: entra si precio rompe
		// en la direccion del sesgo diario (alcista → long breakout, bajista → short breakout).
		private double orHigh      = double.MinValue;
		private double orLow       = double.MaxValue;
		private bool   orReady;       // true despues de 10:00 ET (rango bloqueado)
		private bool   orTriggered;   // ya se entro con Setup E hoy

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

		// Previous Day High/Low — niveles ICT de mayor liquidez para Setup B.
		// sessionHigh/Low acumula la sesion activa; al resetear se guarda como prevDay.
		private double sessionHigh     = double.MinValue;
		private double sessionLow      = double.MaxValue;
		private double prevDayHigh;
		private double prevDayLow;
		private bool   prevDayReady;
		// lastRthClose: ultimo cierre RTH real (16:15 ET) — usado por bias filter.
		// prevSessionClose=Close[1] captura el cierre Globex (incorrecto para bias diario).
		private double lastRthClose;
		private bool   allowedSecondTrade; // true si ya se permitio el 2do trade hoy

		// Bias overnight: cierre ultimo bar pre-mercado para calcular gap vs cierre RTH ayer.
		// 1=bull (gap >+threshold), -1=bear (gap <-threshold), 0=neutral.
		private double preMarketLastClose;
		private int    dailyBiasDir;

		// Bias 4H: direccion de las barras de 4 horas (spec §7: Daily+4H+15M deben alinear).
		// 1=bull, -1=bear, 0=neutral (persiste entre sesiones — no resetear diariamente).
		private int    fourHBiasDir;

		// VWAP: pendiente v3 (tipo no nativo en todas las instalaciones NT8).
		// Score sin VWAP: max 95 pts, umbral 80 sigue alcanzable.

		// Guardia de drawdown: true si el equity disponible < 1 SL del limite trailing DD.
		// Bloquea nuevas entradas sin apagar el bot (no es tradingDisabled).
		private bool   nearDrawdown;

		// Score del ultimo setup armado (para log y debugging).
		private int lastTradeScore;

		// Latches de confluencia Setup A: displacement y FVG pueden llegar en barras 15m distintas.
		// Se latchean desde la barrida y se arma el setup cuando los tres ocurrieron (cualquier orden).
		private bool   sweepDispSeen;
		private bool   sweepFvgSeen;
		private double sweepFvgL;
		private double sweepFvgU;
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

				// Tamano de posicion y riesgo (spec v2: 2 contratos fijos, $250 stop / $700 target)
				Contratos            = 2;
				StopLossUsd          = 250;   // riesgo fijo MNQ: 250 pts / NQ: 25 pts
				ProfitTargetUsd      = 700;   // target fijo MNQ: 700 pts / NQ: 70 pts (~1:3 RR)
				SwingStrength15m     = 3;
				SwingStrength1m      = 2;
				DisplacementAtrMult  = 0.5;   // permisivo para primer backtest; subir a 0.8/1.0/1.5 al confirmar
				DisplacementBodyPct  = 0.25;  // 25% del rango: casi cualquier vela con cuerpo pasa
				MinFvgPoints         = 1.0;   // 1 pt NQ = 4 ticks; muy permisivo para ver cuantos setups hay
				RejectionWickRatio   = 0.4;   // mecha >= 40% del rango
				FvgValidBars         = 90;    // 1m bars = 1.5h para retroceso al FVG
				FvgMitigationPct     = 0.75;  // cancelar solo si >75% rellenado (mas permisivo)
				SweepChochMaxBars15m = 16;    // 16 barras 15m = 4h para ver CHoCH; tuning: reducir a 8
				StopBufferTicks      = 2;
				EnableSetupB         = false; // spec v2: sin sweeps directos sin FVG/iFVG
				MinSweepTicks        = 6;
				MinBodyTicks         = 6;
				SetupBRequiresTrend  = false;
				EnableDailyBiasFilter = false;
				PreMarketStartTime   = 800;
				SetupBMaxMinutes     = 0;
				EnableSetupC         = false;
				MinOBBodyTicks       = 20;
				OBValidBars          = 45;
				EnablePdhPdl         = true;
				Allow2ndTradeIfWinner = false; // spec v2: 1 trade/dia por defecto
				EnableEmaFilter      = false;
				MinPreMarketRange    = 0.0;
				MinTradeScore        = 50;    // 50 para ver trades; subir a 65/70/80 al confirmar señales
				BiasOvernightThreshold = 0;   // 0=sin filtro bias (backtest inicial); activar con 25-50 pts
				                             // al confirmar que prevDayClose1 se carga bien en backtest
				MaxStructuralRiskUsd = 0;     // 0=sin filtro estructural (recomendado para backtest inicial)
				                             // NQ 2 cts: $250 → solo 6.25 pts distancia (demasiado estricto)
				                             // MNQ 2 cts: $250 → 62.5 pts distancia (razonable → activar)
				EnableSetupD         = false;
				EnableSetupE         = false;
				KillZoneStart        = 930;   // 09:30 ET (08:30 Colombia EDT)
				KillZoneEnd          = 1130;  // 11:30 ET = Colombia 10:30 (EDT)
				ForcedExit           = 1400;  // 14:00 ET (13:00 Colombia) — bloquea nuevas entradas
				StartingBalance      = 50000;
				TrailingDrawdown     = 2500;
				MaxDailyLoss         = 250;   // spec v2: 1 perdida ($250) = stop el dia
			}
			else if (State == State.Configure)
			{
				// [0] = 1m  (gatillo de entrada)
				// [1] = 15m (sesgo, barrida, CHoCH, FVG)
				// [2] = 1D  (cierre diario para bias overnight)
				// [3] = 4H  (bias medio plazo — spec §7: Daily+4H+15M)
				AddDataSeries(BarsPeriodType.Minute, 15);
				AddDataSeries(BarsPeriodType.Day,    1);
				AddDataSeries(BarsPeriodType.Minute, 240);
			}
			else if (State == State.DataLoaded)
			{
				atr15m           = ATR(BarsArray[1], 14);
				ema9_15m         = EMA(BarsArray[1], 9);
				ema21_15m        = EMA(BarsArray[1], 21);
				ema20_daily      = EMA(BarsArray[2], 20);
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

			// Capturar cierre diario CONFIRMADO cuando la barra diaria cierra (BarsInProgress==2).
			// prevDayClose1/2 permiten detectar cambio de tendencia en 1 dia (no en 2-3 semanas como EMA20).
			if (BarsInProgress == 2)
			{
				prevDayClose2 = prevDayClose1;
				prevDayClose1 = Close[0];
				string bias = prevDayClose1 > prevDayClose2 ? "BULL" : prevDayClose1 < prevDayClose2 ? "BEAR" : "NEUTRAL";
				Print($"[DAILY] Cierre={prevDayClose1:F0} AntAyer={prevDayClose2:F0} → sesgo manana: {bias}");
				return;
			}

			if (BarsInProgress == 3) // 4H: bias medio plazo (spec §7)
			{
				UpdateFourHBias();
				return;
			}

			if (BarsInProgress == 1) // 15m: sesgo, swings, Setup A, Setup D
			{
				UpdateTrend15m();
				TryDetectSetup15m();
				if (EnableSetupD) TryDetectFvgD();
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
					preMarketHigh      = Math.Max(preMarketHigh, High[0]);
					preMarketLow       = Math.Min(preMarketLow,  Low[0]);
					preMarketLastClose = Close[0]; // guardar ultimo cierre antes de kill zone
				}
				else if (t >= KillZoneStart * 100 && preMarketHigh > double.MinValue)
				{
					preMarketReady = true;
					// Bias overnight: gap entre cierre pre-market y cierre RTH de ayer.
					// >+threshold = alcista (solo longs), <-threshold = bajista (solo shorts), neutral = ambos.
					if (BiasOvernightThreshold > 0 && preMarketLastClose > 0 && prevDayClose1 > 0)
					{
						double gap = preMarketLastClose - prevDayClose1;
						if      (gap >  BiasOvernightThreshold) dailyBiasDir =  1;
						else if (gap < -BiasOvernightThreshold) dailyBiasDir = -1;
						else                                    dailyBiasDir =  0;
						string bl = dailyBiasDir == 1 ? "BULL" : dailyBiasDir == -1 ? "BEAR" : "NEUTRAL";
						Print($"[BIAS-ON] PM_close={preMarketLastClose:F0} Ayer={prevDayClose1:F0} gap={gap:+0;-0} → {bl}");
					}
					Print($"[PRE-AP] Range 1m {PreMarketStartTime}–{KillZoneStart} ET [{preMarketLow:F2}, {preMarketHigh:F2}]");
				}
				else if (t >= KillZoneStart * 100 && !preMarketAttempted)
				{
					// Sin barras 1m en ventana premarket (plantilla RTH o sin datos overnight).
					// Fallback 1: buscar barras 15m en el rango 8:00-9:30.
					// Fallback 2: si tampoco hay, usar las ultimas N barras 15m disponibles (RTH puro).
					preMarketAttempted = true;
					int scan = Math.Min(CurrentBars[1], 32); // hasta ~8h de barras 15m
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
						// Sin datos overnight (RTH puro o contrato sin historia premarket).
						// TryDetectSetup15m usara la primera barra 15m del kill zone como Opening Range.
						Print("[PRE-AP] Sin datos premarket. Esperando Opening Range (primera barra 15m kill zone).");
					}
				}
			}

			// Solo acumular RTH (9:30–16:15 ET) — PDH/PDL y lastRthClose son niveles RTH puro.
			int tRth = ToTime(Time[0]);
			if (tRth >= 93000 && tRth < 161500)
			{
				sessionHigh  = Math.Max(sessionHigh, High[0]);
				sessionLow   = Math.Min(sessionLow,  Low[0]);
				lastRthClose = Close[0]; // se actualiza cada bar RTH; al cerrar sesion queda el ultimo
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

			// Setup D en 1m: entrar cuando precio retrocede al FVG detectado en 15m.
			if (EnableSetupD && fvgDState == 1 && preMarketReady && inKillZone
			    && !tradedToday && !tradingDisabled && setupState == 0)
				TryEnterFvgD();

			// Setup E: Opening Range Breakout.
			if (EnableSetupE && inKillZone) TrySetupE();

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

		#region Bias 4H (spec §7: Daily+4H+15M alineados)
		// Compara el ultimo cierre 4H con el anterior. Simple y robusto para backtest.
		// No se resetea diariamente — el sesgo 4H persiste hasta que cambia.
		private void UpdateFourHBias()
		{
			if (CurrentBars[3] < 2) return;
			double c0 = Closes[3][0];
			double c1 = Closes[3][1];
			int prev = fourHBiasDir;
			if      (c0 > c1) fourHBiasDir =  1;
			else if (c0 < c1) fourHBiasDir = -1;
			else              fourHBiasDir =  0;
			if (fourHBiasDir != prev)
				Print($"[4H-BIAS] {prev} → {fourHBiasDir} c0={c0:F0} c1={c1:F0}");
		}
		#endregion

		#region Deteccion de setup en 15m (barrida → CHoCH → FVG)
		private bool _loggedTrendZero;   // evita spam: loguea trend=0 solo 1 vez por sesion
		private bool _loggedDailyBias;   // evita spam: loguea sesgo diario solo 1 vez por sesion

		private void TryDetectSetup15m()
		{
			// DIAGNOSTICO: imprimir estado de todos los gates en cada barra 15m
			Print($"[DIAG-15m] {Times[1][0]:MM/dd HH:mm} setup={setupState} traded={tradedToday} disabled={tradingDisabled} near={nearDrawdown} bridgeON={ApexBridgeState.TradingEnabled} pmReady={preMarketReady} sweepSt={sweepState15m} trend={trend} pmL={preMarketLow:F0} pmH={preMarketHigh:F0}");

			if (setupState == 1 || tradedToday || tradingDisabled || nearDrawdown) return;
			if (!ApexBridgeState.TradingEnabled) return;

			// Opening Range fallback: sin datos premarket (RTH / contrato sin historia overnight).
			// Al cerrar la primera barra 15m del kill zone, usar su H/L como referencia de sweep.
			// La primera barra DEFINE el OR; los sweeps se detectan a partir de la segunda (10:00+).
			if (!preMarketReady && CurrentBars[1] >= 1)
			{
				int t15 = ToTime(Times[1][0]);
				if (t15 >= KillZoneStart * 100 && t15 < KillZoneEnd * 100)
				{
					preMarketHigh  = Highs[1][0];
					preMarketLow   = Lows[1][0];
					preMarketReady = true;
					Print($"[PRE-OR] Opening Range hasta {Times[1][0]:HH:mm}: L={preMarketLow:F0} H={preMarketHigh:F0}");
				}
			}
			if (!preMarketReady) return;

			// trend=0 permitido: si preMarketReady, el sweep mismo define la dirección.
			// Esto permite Setup A en plantillas RTH donde los swings 15m tardan 1-2h en formarse.
			// trend=0 + sweep de low → dir provisionalmente 1; + sweep de high → dir -1.
			if (trend == 0 && !_loggedTrendZero)
			{
				Print($"[SETUP-A] trend=0 @ {Times[1][0]:HH:mm} — usando premarket como referencia de estructura");
				_loggedTrendZero = true;
			}
			if (trend != 0) _loggedTrendZero = false;

			double atrVal = atr15m[0];
			if (atrVal <= 0) return;

			if (sweepState15m == 0)
			{
				// Barrida: detectar en ambas direcciones si trend=0; solo en dirección del trend si !=0.
				// La barrida que se confirme primero fija la dirección del setup.
				bool tryLong  = trend >= 0; // trend=1 o trend=0
				bool tryShort = trend <= 0; // trend=-1 o trend=0

				if (tryLong)
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
						sweepDispSeen = false; sweepFvgSeen = false; sweepFvgL = 0; sweepFvgU = 0;
						levelBrokenLow15m = false;
						levelBrokenHigh15m = false; // cancelar lado contrario
						if (trend == 0) trend = 1;  // fijar dirección provisional
						Print($"[SWEEP-A] Barrida LOW confirmada @ {Times[1][0]:HH:mm}. Dir→LONG. Buscando CHoCH...");
					}
				}
				if (tryShort && sweepState15m == 0) // solo si no se disparó ya el long
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
						sweepDispSeen = false; sweepFvgSeen = false; sweepFvgL = 0; sweepFvgU = 0;
						levelBrokenHigh15m = false;
						levelBrokenLow15m  = false; // cancelar lado contrario
						if (trend == 0) trend = -1; // fijar dirección provisional
						Print($"[SWEEP-A] Barrida HIGH confirmada @ {Times[1][0]:HH:mm}. Dir→SHORT. Buscando CHoCH...");
					}
				}
			}
			else // sweepState15m == 1: barrida detectada, buscar CHoCH + FVG en 15m
			{
				if ((CurrentBars[1] - sweepBar15m) > SweepChochMaxBars15m)
				{
					Print($"[SWEEP-A] CHoCH timeout @ {Times[1][0]:HH:mm}. Resetando.");
					sweepState15m = 0;
					sweepDispSeen = false; sweepFvgSeen = false; sweepFvgL = 0; sweepFvgU = 0;
					return;
				}

				if (CurrentBars[1] < 3) return;

				if (trend == 1) // CHoCH alcista en 15m
				{
					// CHoCH level: ultimo swing high 15m o fallback al High de la barra de sweep.
					double chochLevel;
					int sweepOff = CurrentBars[1] - sweepBar15m;
					if      (swingHighs15m.Count > 0) chochLevel = swingHighs15m[swingHighs15m.Count - 1];
					else if (sweepOff > 0 && sweepOff < CurrentBars[1]) chochLevel = Highs[1][sweepOff];
					else if (preMarketHigh > 0)       chochLevel = preMarketHigh;
					else return;

					// LATCH Displacement: bar[1] = impulso institucional.
					if (!sweepDispSeen && CurrentBars[1] >= 2)
					{
						double body  = Math.Abs(Closes[1][1] - Opens[1][1]);
						double range = Highs[1][1] - Lows[1][1];
						if (range > 0 && range >= DisplacementAtrMult * atrVal
						             && body > 0 && (body / range) >= DisplacementBodyPct)
						{
							sweepDispSeen = true;
							Print($"[DISP-A] Long displacement latcheado body={body:F1} rng={range:F1}");
						}
					}

					// LATCH FVG alcista: ventana doble + iFVG. Guarda el mayor FVG visto.
					if (CurrentBars[1] >= 3)
					{
						double uA = Lows[1][0], lA = Highs[1][2];
						if (uA > lA && (uA - lA) >= MinFvgPoints)
						{
							if (!sweepFvgSeen || (uA - lA) > (sweepFvgU - sweepFvgL))
							{ sweepFvgL = lA; sweepFvgU = uA; }
							sweepFvgSeen = true;
						}
					}
					if (CurrentBars[1] >= 4)
					{
						double uB = Lows[1][1], lB = Highs[1][3];
						if (uB > lB && (uB - lB) >= MinFvgPoints)
						{
							if (!sweepFvgSeen || (uB - lB) > (sweepFvgU - sweepFvgL))
							{ sweepFvgL = lB; sweepFvgU = uB; }
							sweepFvgSeen = true;
						}
					}
					if (!sweepFvgSeen && CurrentBars[1] >= 5)
					{
						for (int k = 1; k <= 5 && (k + 2) < CurrentBars[1]; k++)
						{
							double fl = Highs[1][k], fu = Lows[1][k + 2];
							if ((fu - fl) >= MinFvgPoints && Closes[1][0] > fu)
							{ sweepFvgL = fl; sweepFvgU = fu; sweepFvgSeen = true;
							  Print($"[iFVG-A] Long iFVG latcheado [{fl:F1},{fu:F1}]"); break; }
						}
					}

					bool isChoch = Closes[1][0] > chochLevel;
					double fvgSize = sweepFvgSeen ? sweepFvgU - sweepFvgL : 0;

					Print($"[CHoCH-A] Long choch={isChoch}(C={Closes[1][0]:F0}>{chochLevel:F0}) " +
					      $"dispSeen={sweepDispSeen} fvgSeen={sweepFvgSeen}(sz={fvgSize:F1}pts)");

					if (isChoch && sweepDispSeen && sweepFvgSeen)
					{
						if (dailyBiasDir == -1)
						{ Print("[BIAS] Sesgo overnight BEAR -> LONG bloqueado"); sweepState15m = 0; sweepDispSeen = false; sweepFvgSeen = false; return; }

						int score = CalcTradeScore(1, true, sweepDispSeen, fvgSize, true);
						if (score < MinTradeScore)
						{ Print($"[SCORE] {score}/100 < {MinTradeScore} -> skip"); sweepState15m = 0; sweepDispSeen = false; sweepFvgSeen = false; return; }

						if (MaxStructuralRiskUsd > 0)
						{
							double stopDist  = Math.Abs(sweepFvgU - (sweepLevel15m - StopBufferTicks * TickSize));
							double structRisk = stopDist * Instrument.MasterInstrument.PointValue * Contratos;
							if (structRisk > MaxStructuralRiskUsd)
							{ Print($"[STRUCT] Riesgo {structRisk:C} > {MaxStructuralRiskUsd:C} -> skip"); sweepState15m = 0; sweepDispSeen = false; sweepFvgSeen = false; return; }
						}

						lastTradeScore = score;
						ArmSetup(1, sweepFvgL, sweepFvgU, sweepLevel15m - StopBufferTicks * TickSize);
						sweepState15m = 0; sweepDispSeen = false; sweepFvgSeen = false;
						Print($"[SETUP-A] LONG armado. Score={score} FVG=[{sweepFvgL:F1},{sweepFvgU:F1}]");
					}
				}
				else // trend == -1: CHoCH bajista en 15m
				{
					double chochLevel;
					int sweepOff = CurrentBars[1] - sweepBar15m;
					if      (swingLows15m.Count > 0) chochLevel = swingLows15m[swingLows15m.Count - 1];
					else if (sweepOff > 0 && sweepOff < CurrentBars[1]) chochLevel = Lows[1][sweepOff];
					else if (preMarketLow < double.MaxValue && preMarketLow > 0) chochLevel = preMarketLow;
					else return;

					// LATCH Displacement bajista en bar[1]
					if (!sweepDispSeen && CurrentBars[1] >= 2)
					{
						double body  = Math.Abs(Opens[1][1] - Closes[1][1]);
						double range = Highs[1][1] - Lows[1][1];
						if (range > 0 && range >= DisplacementAtrMult * atrVal
						             && body > 0 && (body / range) >= DisplacementBodyPct)
						{
							sweepDispSeen = true;
							Print($"[DISP-A] Short displacement latcheado body={body:F1} rng={range:F1}");
						}
					}

					// LATCH FVG bajista
					if (CurrentBars[1] >= 3)
					{
						double uA = Lows[1][2], lA = Highs[1][0];
						if (uA > lA && (uA - lA) >= MinFvgPoints)
						{
							if (!sweepFvgSeen || (uA - lA) > (sweepFvgU - sweepFvgL))
							{ sweepFvgL = lA; sweepFvgU = uA; }
							sweepFvgSeen = true;
						}
					}
					if (CurrentBars[1] >= 4)
					{
						double uB = Lows[1][3], lB = Highs[1][1];
						if (uB > lB && (uB - lB) >= MinFvgPoints)
						{
							if (!sweepFvgSeen || (uB - lB) > (sweepFvgU - sweepFvgL))
							{ sweepFvgL = lB; sweepFvgU = uB; }
							sweepFvgSeen = true;
						}
					}
					if (!sweepFvgSeen && CurrentBars[1] >= 5)
					{
						for (int k = 1; k <= 5 && (k + 2) < CurrentBars[1]; k++)
						{
							double fl = Highs[1][k + 2], fu = Lows[1][k];
							if ((fu - fl) >= MinFvgPoints && Closes[1][0] < fl)
							{ sweepFvgL = fl; sweepFvgU = fu; sweepFvgSeen = true;
							  Print($"[iFVG-A] Short iFVG latcheado [{fl:F1},{fu:F1}]"); break; }
						}
					}

					bool isChoch = Closes[1][0] < chochLevel;
					double fvgSize = sweepFvgSeen ? sweepFvgU - sweepFvgL : 0;

					Print($"[CHoCH-A] Short choch={isChoch}(C={Closes[1][0]:F0}<{chochLevel:F0}) " +
					      $"dispSeen={sweepDispSeen} fvgSeen={sweepFvgSeen}(sz={fvgSize:F1}pts)");

					if (isChoch && sweepDispSeen && sweepFvgSeen)
					{
						if (dailyBiasDir == 1)
						{ Print("[BIAS] Sesgo overnight BULL -> SHORT bloqueado"); sweepState15m = 0; sweepDispSeen = false; sweepFvgSeen = false; return; }

						int score = CalcTradeScore(-1, true, sweepDispSeen, fvgSize, true);
						if (score < MinTradeScore)
						{ Print($"[SCORE] {score}/100 < {MinTradeScore} -> skip"); sweepState15m = 0; sweepDispSeen = false; sweepFvgSeen = false; return; }

						if (MaxStructuralRiskUsd > 0)
						{
							double stopDist  = Math.Abs(sweepFvgL - (sweepLevel15m + StopBufferTicks * TickSize));
							double structRisk = stopDist * Instrument.MasterInstrument.PointValue * Contratos;
							if (structRisk > MaxStructuralRiskUsd)
							{ Print($"[STRUCT] Riesgo {structRisk:C} > {MaxStructuralRiskUsd:C} -> skip"); sweepState15m = 0; sweepDispSeen = false; sweepFvgSeen = false; return; }
						}

						lastTradeScore = score;
						ArmSetup(-1, sweepFvgL, sweepFvgU, sweepLevel15m + StopBufferTicks * TickSize);
						sweepState15m = 0; sweepDispSeen = false; sweepFvgSeen = false;
						Print($"[SETUP-A] SHORT armado. Score={score} FVG=[{sweepFvgL:F1},{sweepFvgU:F1}]");
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
			fvgArmedBar     = CurrentBars[0];
			priceInFvg      = false;
			setupState      = 1;
			swingHighs1m.Clear();
			swingLows1m.Clear();

			Print($"[SETUP] Dir={dir} FVG [{fvgLower:F2},{fvgUpper:F2}] Invalid={fvgInvalidPrice:F2} Score={lastTradeScore}");
		}
		#endregion

		#region Gestion del setup en 1m (retroceso → confirmacion → entrada)
		private void ManageSetup1m(bool inKillZone)
		{
			// Si ya estamos en posicion, stop/target adjuntos gestionan la salida
			if (Position.MarketPosition != MarketPosition.Flat) return;

			// Cancelar si el setup expiro, la estructura fue invalidada, o el FVG fue mitigado >50%
			bool expired = (CurrentBars[0] - fvgArmedBar) > FvgValidBars;
			bool invalid = setupDir == 1 ? Low[0]  <= fvgInvalidPrice
			                             : High[0] >= fvgInvalidPrice;

			// FVG mitigation: si el precio ya retrocedio mas del % configurado dentro del FVG → cancelar.
			// Long FVG: fill desde arriba → si Low baja del nivel de mitigacion es demasiado profundo.
			// Short FVG: fill desde abajo → si High sube del nivel de mitigacion es demasiado profundo.
			double mitigLvl = setupDir == 1
			    ? fvgLower + (fvgUpper - fvgLower) * FvgMitigationPct
			    : fvgUpper - (fvgUpper - fvgLower) * FvgMitigationPct;
			bool mitigated = priceInFvg && FvgMitigationPct < 1.0 && (
			    (setupDir ==  1 && Low[0]  < mitigLvl) ||
			    (setupDir == -1 && High[0] > mitigLvl));

			if (expired || invalid || mitigated)
			{
				if (mitigated) Print($"[FVG-MITIG] FVG rellenado >{FvgMitigationPct:P0} — setup cancelado");
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
			double range1m   = High[0] - Low[0];
			double lowerWick = Math.Min(Open[0], Close[0]) - Low[0];
			double upperWick = High[0] - Math.Max(Open[0], Close[0]);

			// spec v2 §6: mecha >= RejectionWickRatio × rango (default 0.5 = 50% del rango)
			if (setupDir == 1) // long: mecha inferior >= 50% rango + cierre alcista
			{
				bool rejection = range1m > 0
				              && lowerWick >= RejectionWickRatio * range1m
				              && Close[0]  > Open[0];
				bool miniChoch = swingHighs1m.Count > 0
				              && Close[0] > swingHighs1m[swingHighs1m.Count - 1];
				return rejection || miniChoch;
			}
			else // short: mecha superior >= 50% rango + cierre bajista
			{
				bool rejection = range1m > 0
				              && upperWick >= RejectionWickRatio * range1m
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
			if (nearDrawdown) return;

			double minSweep = MinSweepTicks * TickSize;
			double minBody  = MinBodyTicks  * TickSize;

			bool shortOk = !SetupBRequiresTrend || trend != 1;
			bool longOk  = !SetupBRequiresTrend || trend != -1;

			// Sesgo overnight (gap pre-market vs cierre RTH ayer): calculado al fijar el rango pre-mercado.
			// BULL=solo longs, BEAR=solo shorts, NEUTRAL=ambos (gap < umbral o datos insuficientes).
			if (!_loggedDailyBias)
			{
				string lbl = dailyBiasDir == 1 ? "BULL (solo longs)" : dailyBiasDir == -1 ? "BEAR (solo shorts)" : "NEUTRAL";
				Print($"[BIAS-B] Overnight bias → {lbl}");
				_loggedDailyBias = true;
			}
			if (dailyBiasDir ==  1) shortOk = false;
			if (dailyBiasDir == -1) longOk  = false;

			// Filtro volatilidad: rango pre-mercado muy pequeno = dia choppy = sweeps sin follow-through.
			if (MinPreMarketRange > 0)
			{
				double preMktRange = preMarketHigh - preMarketLow;
				if (preMktRange < MinPreMarketRange)
				{
					Print($"[CHOP-FILTER] Rango pre-mkt {preMktRange:F1} pts < {MinPreMarketRange:F1} pts → dia choppy, skip");
					return;
				}
			}

			// Filtro EMA15m opcional (desactivado por defecto: reacciona demasiado rapido).
			if (EnableEmaFilter && ema9_15m != null && ema21_15m != null)
			{
				double e9  = ema9_15m[0];
				double e21 = ema21_15m[0];
				if (e9 > e21) shortOk = false;
				if (e9 < e21) longOk  = false;
			}

			// Filtro legacy premarket vs RTH close (desactivado por defecto).
			double biasRef = lastRthClose > 0 ? lastRthClose : prevSessionClose;
			if (EnableDailyBiasFilter && biasRef > 0 && preMarketHigh > double.MinValue)
			{
				double midRange = (preMarketHigh + preMarketLow) / 2.0;
				if (midRange > biasRef) shortOk = false;
				if (midRange < biasRef) longOk  = false;
			}

			// Filtro horario: solo primeros SetupBMaxMinutes del kill zone.
			if (SetupBMaxMinutes > 0)
			{
				var kzStart = new DateTime(Time[0].Year, Time[0].Month, Time[0].Day,
				                          KillZoneStart / 100, KillZoneStart % 100, 0);
				if (Time[0] >= kzStart.AddMinutes(SetupBMaxMinutes)) return;
			}

			// Buffer de recuperacion: Close debe superar el nivel en 2+ ticks (no apenas encima).
			// Evita entradas donde el cierre es marginal y el sweep es probablemente falso.
			double retBuf = 2 * TickSize;

			// --- Barridas LONG (sweep de un minimo, reversal alcista) ---
			if (longOk && (Close[0] - Open[0]) >= minBody)
			{
				// Nivel 1: rango pre-mercado (8:00-9:30 ET)
				if (Low[0] < preMarketLow - minSweep && Close[0] > preMarketLow + retBuf)
				{ EnterSweepB(1, "PreMktL"); return; }

				// Nivel 2: Previous Day Low — maximo nivel de liquidez sell-side (ICT)
				if (EnablePdhPdl && prevDayReady
				    && prevDayLow < preMarketLow - 10 * TickSize
				    && Low[0] < prevDayLow - minSweep && Close[0] > prevDayLow + retBuf)
				{ EnterSweepB(1, "PDL"); return; }
			}

			// --- Barridas SHORT (sweep de un maximo, reversal bajista) ---
			if (shortOk && (Open[0] - Close[0]) >= minBody)
			{
				// Nivel 1: rango pre-mercado
				if (High[0] > preMarketHigh + minSweep && Close[0] < preMarketHigh - retBuf)
				{ EnterSweepB(-1, "PreMktH"); return; }

				// Nivel 2: Previous Day High — maximo nivel de liquidez buy-side (ICT)
				if (EnablePdhPdl && prevDayReady
				    && prevDayHigh > preMarketHigh + 10 * TickSize
				    && High[0] > prevDayHigh + minSweep && Close[0] < prevDayHigh - retBuf)
				{ EnterSweepB(-1, "PDH"); return; }
			}
		}

		private void EnterSweepB(int dir, string source = "")
		{
			if (pnlTracker != null && pnlTracker.WouldViolateConsistency(ProfitTargetUsd))
			{
				Print("[CONSISTENCIA] SweepB saltado: violaria regla 50% Apex.");
				return;
			}

			string sig = dir == 1 ? "LongSweep" : "ShortSweep";
			SetStopLoss(sig, CalculationMode.Currency, StopLossUsd, false);
			SetProfitTarget(sig, CalculationMode.Currency, ProfitTargetUsd);

			if (dir == 1) EnterLong(0, Contratos, sig);
			else          EnterShort(0, Contratos, sig);

			string lvlInfo = (source == "PDL" || source == "PDH")
				? $"PDH={prevDayHigh:F2} PDL={prevDayLow:F2}"
				: $"PreMkt=[{preMarketLow:F2},{preMarketHigh:F2}]";
			Print($"[SETUP-B] {sig} [{source}] {lvlInfo}");
		}
		#endregion

		#region Trade Score Engine (spec v2 §18)
		// Score maximo = 100. Minimo para operar = MinTradeScore (default 80).
		// Sweep(25) + Displacement(20) + FVG/iFVG(25) + Estructura(15) + Bias(10) + VWAP(5).
		private int CalcTradeScore(int dir, bool hasSweep, bool hasDisplace, double fvgSize, bool hasStructure)
		{
			int score = 0;

			if (hasSweep)    score += 25;
			if (hasDisplace) score += 20;

			// FVG o iFVG: fuerte (>=4pts) = 25, debil (2-4pts) = 15
			if      (fvgSize >= 4.0)          score += 25;
			else if (fvgSize >= MinFvgPoints) score += 15;

			if (hasStructure) score += 15; // estructura 15m confirmada

			// Bias multi-timeframe (spec §7: Daily+4H+15M).
			// Contamos votos en favor de la direccion del trade.
			int votes = 0;
			if (dailyBiasDir ==  dir) votes++;
			else if (dailyBiasDir == -dir) votes--;
			if (fourHBiasDir ==  dir) votes++;
			else if (fourHBiasDir == -dir) votes--;
			// 15m trend ya cuenta en hasStructure; no doble conteo.

			if      (votes >= 2)  score += 10; // daily + 4H alineados con dir
			else if (votes == 1)  score += 7;  // uno alineado, otro neutral
			else if (votes == 0)  score += 3;  // ambos neutrales
			// votes < 0 → en contra → 0 pts

			// VWAP +5 pendiente v3 (requiere instalacion custom del indicador en NT8).

			return score;
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

		#region Setup D: FVG 15m alineado con sesgo diario (sin prereq sweep)
		// Detecta FVGs en 15m en la direccion del mercado (EMA20 daily).
		// En mercado alcista (precio > EMA20): busca FVG alcista, entra LONG en retroceso.
		// En mercado bajista (precio < EMA20): busca FVG bajista, entra SHORT en retroceso.
		// Corre en BarsInProgress==1 (15m).
		private void TryDetectFvgD()
		{
			if (tradedToday || tradingDisabled) return;
			if (!ApexBridgeState.TradingEnabled) return;
			if (setupState == 1) return; // Setup A ya armado
			if (prevDayClose1 <= 0 || prevDayClose2 <= 0) return; // aun no hay 2 cierres diarios
			if (CurrentBars[1] < 3) return;

			bool dailyBull = prevDayClose1 > prevDayClose2;
			bool dailyBear = prevDayClose1 < prevDayClose2;
			if (!dailyBull && !dailyBear) return;

			// Si ya hay un FVG activo, verificar expiracion (8 barras 15m = 2 horas)
			if (fvgDState == 1)
			{
				if (CurrentBars[1] - fvgDBar > 8)
				{
					fvgDState   = 0;
					fvgDPriceIn = false;
				}
				return; // seguir esperando retroceso; la entrada la maneja TryEnterFvgD en 1m
			}

			double minGap = MinFvgPoints; // pts minimos del gap (parametro existente)

			if (dailyBull)
			{
				// FVG alcista en 15m: Low[0] > High[2] — gap entre vela[2] y vela[0]
				// Zona: High[2] (piso) hasta Low[0] (techo)
				double gapLow  = Highs[1][2];
				double gapHigh = Lows[1][0];
				if (gapHigh > gapLow + minGap)
				{
					fvgDLower   = gapLow;
					fvgDUpper   = gapHigh;
					fvgDDir     = 1;
					fvgDState   = 1;
					fvgDBar     = CurrentBars[1];
					fvgDPriceIn = false;
					Print($"[FVG-D] BULL FVG [{fvgDLower:F1},{fvgDUpper:F1}] @ {Times[1][0]:HH:mm} (gap={gapHigh-gapLow:F1}pts) — esperando retroceso");
				}
			}
			else // dailyBear
			{
				// FVG bajista en 15m: High[0] < Low[2] — gap entre vela[2] y vela[0]
				// Zona: High[0] (piso) hasta Low[2] (techo)
				double gapLow  = Highs[1][0];
				double gapHigh = Lows[1][2];
				if (gapHigh > gapLow + minGap)
				{
					fvgDLower   = gapLow;
					fvgDUpper   = gapHigh;
					fvgDDir     = -1;
					fvgDState   = 1;
					fvgDBar     = CurrentBars[1];
					fvgDPriceIn = false;
					Print($"[FVG-D] BEAR FVG [{fvgDLower:F1},{fvgDUpper:F1}] @ {Times[1][0]:HH:mm} (gap={gapHigh-gapLow:F1}pts) — esperando retroceso");
				}
			}
		}

		// Corre en BarsInProgress==0 (1m): cuando el precio entra al FVG detectado en 15m.
		private void TryEnterFvgD()
		{
			if (fvgDState != 1) return;

			// Verificar que estamos dentro de kill zone (puede haberse detectado antes)
			// El caller ya verifica inKillZone, no hace falta repetirlo aqui.

			// Detectar entrada al FVG
			if (fvgDDir == 1)
			{
				if (Low[0] <= fvgDUpper && Low[0] >= fvgDLower) fvgDPriceIn = true;
				// Invalidar si rompe por debajo del FVG
				if (Low[0] < fvgDLower - 4 * TickSize) { fvgDState = 0; return; }
			}
			else
			{
				if (High[0] >= fvgDLower && High[0] <= fvgDUpper) fvgDPriceIn = true;
				// Invalidar si rompe por encima del FVG
				if (High[0] > fvgDUpper + 4 * TickSize) { fvgDState = 0; return; }
			}

			if (!fvgDPriceIn) return;

			// Confirmacion 1m: vela de rechazo o mini-CHoCH (igual que Setup A)
			double bodySize  = Math.Abs(Close[0] - Open[0]);
			double lowerWick = Math.Min(Open[0], Close[0]) - Low[0];
			double upperWick = High[0] - Math.Max(Open[0], Close[0]);
			bool   confirmed = false;

			if (fvgDDir == 1) // long: rechazo alcista O mini-CHoCH up
			{
				bool rejection = bodySize > 0 && lowerWick >= RejectionWickRatio * bodySize && Close[0] > Open[0];
				bool miniChoch = swingHighs1m.Count > 0 && Close[0] > swingHighs1m[swingHighs1m.Count - 1];
				confirmed = rejection || miniChoch;
			}
			else // short: rechazo bajista O mini-CHoCH down
			{
				bool rejection = bodySize > 0 && upperWick >= RejectionWickRatio * bodySize && Close[0] < Open[0];
				bool miniChoch = swingLows1m.Count > 0 && Close[0] < swingLows1m[swingLows1m.Count - 1];
				confirmed = rejection || miniChoch;
			}

			if (!confirmed) return;

			// Entrar
			string sig = fvgDDir == 1 ? "LongFvgD" : "ShortFvgD";
			SetStopLoss(sig, CalculationMode.Currency, StopLossUsd, false);
			SetProfitTarget(sig, CalculationMode.Currency, ProfitTargetUsd);
			if (fvgDDir == 1) EnterLong(0, Contratos, sig);
			else              EnterShort(0, Contratos, sig);
			fvgDState = 0;
			Print($"[FVG-D] Entrada {sig} @ {Close[0]:F2} FVG=[{fvgDLower:F1},{fvgDUpper:F1}]");
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
			// Guardar PDH/PDL de la sesion que cierra antes de resetear el rango.
			if (sessionHigh > double.MinValue && sessionLow < double.MaxValue)
			{
				prevDayHigh  = sessionHigh;
				prevDayLow   = sessionLow;
				prevDayReady = true;
				Print($"[PDH/PDL] Nueva sesion: PDH={prevDayHigh:F2} PDL={prevDayLow:F2}");
			}
			sessionHigh = double.MinValue;
			sessionLow  = double.MaxValue;

			tradedToday         = false;
			tradingDisabled     = false;
			nearDrawdown        = false;
			setupState          = 0;
			sweepState15m       = 0;
			levelBrokenLow15m   = false;
			levelBrokenHigh15m  = false;
			sweepDispSeen       = false;
			sweepFvgSeen        = false;
			sweepFvgL           = 0;
			sweepFvgU           = 0;
			_loggedTrendZero    = false;
			_loggedDailyBias    = false;
			preMarketHigh       = double.MinValue;
			preMarketLow        = double.MaxValue;
			preMarketLastClose  = 0;
			dailyBiasDir        = 0;
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
			allowedSecondTrade  = false;
			fvgDState           = 0;
			fvgDDir             = 0;
			fvgDUpper           = 0;
			fvgDLower           = 0;
			fvgDBar             = 0;
			fvgDPriceIn         = false;
			orHigh              = double.MinValue;
			orLow               = double.MaxValue;
			orReady             = false;
			orTriggered         = false;
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

			// Guardia de proximidad: spec §18.5E — si el equity libre < 1 SL del limite trailing DD,
			// no abrir nuevas posiciones (demasiado cerca de fallar la cuenta).
			double ddFloor    = accountHighWater - TrailingDrawdown;
			double distToDD   = equity - ddFloor;
			bool wasNear      = nearDrawdown;
			nearDrawdown = Position.MarketPosition == MarketPosition.Flat
			            && distToDD >= 0 && distToDD < StopLossUsd;
			if (nearDrawdown && !wasNear)
				Print($"[RIESGO] Cerca del trailing DD: {distToDD:C} margen disponible < SL {StopLossUsd:C}. Sin nuevas entradas.");
		}

		// Registra trades cerrados en el tracker de consistencia.
		// Solo activo en vivo (pnlTracker null en backtest). Usa SystemPerformance
		// para evitar problemas de timing con OnExecutionUpdate.
		private void RecordClosedTrades()
		{
			int total = SystemPerformance.AllTrades.Count;
			for (int i = lastTradeCount; i < total; i++)
			{
				var    t        = SystemPerformance.AllTrades[i];
				double tradePnl = t.ProfitCurrency;

				// 2do trade: si el primero fue ganador, abrir una segunda ventana de entrada.
				// Funciona en backtest Y en tiempo real (no depende de pnlTracker).
				if (Allow2ndTradeIfWinner && tradePnl > 0 && tradedToday && !allowedSecondTrade)
				{
					tradedToday        = false;
					allowedSecondTrade = true;
					Print($"[2DO-TRADE] Trade ganador (+{tradePnl:C}). tradedToday=false → 2do trade habilitado.");
				}

				if (pnlTracker == null) continue;

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

		#region Setup E: Opening Range Breakout
		// Registra el rango 9:30-10:00 ET y entra en breakout despues en la direccion del sesgo diario.
		// En febrero bajista: rompe el minimo del rango → SHORT. En enero alcista: rompe maximo → LONG.
		// Stop $375 / Target $1050 — mismo RR. Se combina con A/B/D: solo dispara si aun no se opero.
		private void TrySetupE()
		{
			int t = ToTime(Time[0]);
			int kzStart = KillZoneStart * 100; // ej 93000

			// Fase 1: construir el rango (primeros 30 min del kill zone = 9:30-10:00 ET).
			// orEndTime: KillZoneStart + 30 min en formato HHmm → HHmmss para comparar con ToTime().
			// Ejemplo: KillZoneStart=930 → 960 (10:00) → *100 = 96000 (HHmmss).
			// Ajuste de hora: 960 tiene mm=60 (invalido), necesitamos +40 al HH: 1000 → 100000.
			int kzStartHH = KillZoneStart / 100, kzStartMM = KillZoneStart % 100;
			int orEndMM   = kzStartMM + 30;
			int orEndHH   = kzStartHH + orEndMM / 60;
			orEndMM       = orEndMM % 60;
			int orEndTime = (orEndHH * 100 + orEndMM) * 100; // HHmmss

			if (!orReady)
			{
				if (t >= kzStart && t < orEndTime)
				{
					orHigh = Math.Max(orHigh, High[0]);
					orLow  = Math.Min(orLow,  Low[0]);
				}
				else if (t >= orEndTime && orHigh > double.MinValue)
				{
					orReady = true;
					Print($"[OR] Rango {kzStartHH:D2}:{kzStartMM:D2}-{orEndHH:D2}:{orEndMM:D2} bloqueado: High={orHigh:F1} Low={orLow:F1} ({orHigh-orLow:F1}pts)");
				}
				return;
			}

			// Fase 2: esperar breakout (despues del OR, hasta fin kill zone)
			if (orTriggered || tradedToday || tradingDisabled || !ApexBridgeState.TradingEnabled) return;
			if (setupState == 1) return; // Setup A armado

			// Necesitar sesgo diario confirmado (2 cierres consecutivos)
			if (prevDayClose1 <= 0 || prevDayClose2 <= 0) return;
			bool bearDay = prevDayClose1 < prevDayClose2;
			bool bullDay = prevDayClose1 > prevDayClose2;
			if (!bearDay && !bullDay) return;

			// Rango minimo: si el rango de apertura es menor a 8 pts, no es significativo
			if (orHigh - orLow < 8.0) return;

			double buf = 2 * TickSize; // buffer: cierre debe superar el nivel por 2 ticks

			if (bearDay && Close[0] < orLow - buf && Close[0] < Open[0]) // breakout bajista
			{
				Print($"[SETUP-E] SHORT breakout OR minimo={orLow:F1} close={Close[0]:F1}");
				string sig = "ShortOR";
				SetStopLoss(sig, CalculationMode.Currency, StopLossUsd, false);
				SetProfitTarget(sig, CalculationMode.Currency, ProfitTargetUsd);
				EnterShort(0, Contratos, sig);
				orTriggered = true;
			}
			else if (bullDay && Close[0] > orHigh + buf && Close[0] > Open[0]) // breakout alcista
			{
				Print($"[SETUP-E] LONG breakout OR maximo={orHigh:F1} close={Close[0]:F1}");
				string sig = "LongOR";
				SetStopLoss(sig, CalculationMode.Currency, StopLossUsd, false);
				SetProfitTarget(sig, CalculationMode.Currency, ProfitTargetUsd);
				EnterLong(0, Contratos, sig);
				orTriggered = true;
			}
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
			    order.Name == "LongSweep" || order.Name == "ShortSweep" ||
			    order.Name == "LongFvgD"  || order.Name == "ShortFvgD" ||
			    order.Name == "LongOR"    || order.Name == "ShortOR")
				entryOrder = order;
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId,
			double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution.Order == null) return;
			if ((execution.Order.Name == "LongFVG"   || execution.Order.Name == "ShortFVG" ||
			     execution.Order.Name == "LongSweep" || execution.Order.Name == "ShortSweep" ||
			     execution.Order.Name == "LongFvgD"  || execution.Order.Name == "ShortFvgD" ||
			     execution.Order.Name == "LongOR"    || execution.Order.Name == "ShortOR")
			    && execution.Order.OrderState == OrderState.Filled)
			{
				tradedToday    = true;
				setupState     = 0;
				activeSignal   = execution.Order.Name;
				lastEntryPrice = price;
				lastEntryDir   = (execution.Order.Name == "LongFVG"  ||
				                  execution.Order.Name == "LongSweep" ||
				                  execution.Order.Name == "LongFvgD" ||
				                  execution.Order.Name == "LongOR") ? 1 : -1;

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

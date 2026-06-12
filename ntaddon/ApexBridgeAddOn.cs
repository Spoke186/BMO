#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Globalization;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
#endregion

// AddOn puente para el MCP. Levanta un HTTP server en 127.0.0.1 dentro de
// NinjaTrader y expone estado de cuenta + toggle de trading.
//
// Endpoints (solo lectura + enable/disable, NUNCA order placement):
//   GET  /health
//   GET  /account            -> balance, realized P&L, posicion neta
//   GET  /position           -> posicion del instrumento configurado
//   GET  /trades/today       -> resumen de trades del dia (placeholder)
//   POST /strategy/enable    -> ApexBridgeState.TradingEnabled = true
//   POST /strategy/disable   -> ApexBridgeState.TradingEnabled = false
//
// INTEGRACION CON LA ESTRATEGIA (Stream A):
//   En ApexNqIctStrategy, antes de armar un setup, añadir:
//       if (!ApexBridgeState.TradingEnabled) return;
//   Asi el MCP puede apagar el bot sin tocar NinjaTrader a mano.
//
// SEGURIDAD: bind SOLO a 127.0.0.1. Token simple en header X-Bridge-Token.
// NO exponer a LAN sin TLS + IP allowlist.
//
// ⚠️ NO COMPILADO/PROBADO aqui (no hay NT8 en esta maquina). Revisar API de
//    Account contra tu version exacta de NT8.
namespace NinjaTrader.NinjaScript.AddOns
{
	// Resumen de un trade cerrado (escrito por la estrategia, leido por el AddOn).
	public class TradeSummary
	{
		public string Direction; // "LONG" / "SHORT"
		public double EntryPrice;
		public double ExitPrice;
		public double PnlUsd;
		public string ExitTime;  // "HH:mm:ss ET"
		public string Result;    // "WIN" / "LOSS"
	}

	// Estado compartido entre AddOn y Estrategia (misma assembly Custom).
	public static class ApexBridgeState
	{
		// Arranca habilitado; el MCP/operador lo apaga cuando quiera.
		public static volatile bool TradingEnabled = true;

		// Estado del setup ICT (escrito por la estrategia en cada bar, leido por el AddOn).
		// Solo para monitoreo — no es thread-safe estricto, pero la imprecision es aceptable.
		public static int    Trend           = 0;   // 1=alcista -1=bajista 0=sin sesgo
		public static bool   PreMarketReady  = false;
		public static double PreMarketHigh   = 0;
		public static double PreMarketLow    = 0;
		public static int    SweepState      = 0;   // 0=buscando 1=detectada
		public static double SweepLevel      = 0;
		public static int    SetupState      = 0;   // 0=sin setup 1=armado
		public static int    SetupDir        = 0;   // 1=long -1=short
		public static double FvgLower        = 0;
		public static double FvgUpper        = 0;
		public static bool   PriceInFvg      = false;
		public static bool   TradedToday     = false;
		public static bool   TradingDisabled = false;

		// Trades cerrados hoy (vaciado en ResetForNewSession; lock para acceso cross-thread).
		public static readonly List<TradeSummary> TodayTrades = new List<TradeSummary>();
		public static readonly object TodayTradesLock = new object();
	}

	public class ApexBridgeAddOn : AddOnBase
	{
		// B3: cuenta/simbolo/token vienen de variables de entorno (proceso, con fallback al
		// registro User — setx no afecta procesos ya arrancados). Defaults para Sim
		// out-of-the-box; al conectar Apex, exportar APEX_ACCOUNT (ej "APEX-XXXXX").
		// El bracket USD fijo escala por point value del instrumento: MNQ ($2/pt) y NQ
		// ($20/pt) funcionan igual — APEX_INSTRUMENT debe coincidir con el del chart.
		private static readonly string AccountName    = Env("APEX_ACCOUNT", "Sim101");
		private static readonly string InstrumentName = Env("APEX_INSTRUMENT", "NQ");
		private const int Port = 8731;
		// Seguridad: token desde env BRIDGE_TOKEN (mismo valor que usa el MCP). Sin hardcode real.
		private static readonly string Token = Env("BRIDGE_TOKEN", "CHANGE_ME_LOCAL_TOKEN");

		private static string Env(string name, string def)
		{
			return Environment.GetEnvironmentVariable(name)
				?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
				?? def;
		}

		private HttpListener listener;
		private Thread       listenerThread;
		private volatile bool running;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "ApexBridgeAddOn";
			}
			else if (State == State.Configure)
			{
				StartServer();
			}
			else if (State == State.Terminated)
			{
				StopServer();
			}
		}

		private void StartServer()
		{
			if (running) return;
			try
			{
				listener = new HttpListener();
				listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
				listener.Start();
				running = true;
				listenerThread = new Thread(Loop) { IsBackground = true };
				listenerThread.Start();
				NinjaTrader.Code.Output.Process($"[ApexBridge] HTTP en 127.0.0.1:{Port}", PrintTo.OutputTab1);
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process($"[ApexBridge] Error start: {ex.Message}", PrintTo.OutputTab1);
			}
		}

		private void StopServer()
		{
			running = false;
			try { listener?.Stop(); listener?.Close(); } catch { }
		}

		private void Loop()
		{
			while (running)
			{
				HttpListenerContext ctx;
				try { ctx = listener.GetContext(); }
				catch { break; }

				try { Handle(ctx); }
				catch (Exception ex)
				{
					Write(ctx, 500, $"{{\"error\":\"{Escape(ex.Message)}\"}}");
				}
			}
		}

		private void Handle(HttpListenerContext ctx)
		{
			// Auth simple por token local.
			if (ctx.Request.Headers["X-Bridge-Token"] != Token)
			{
				Write(ctx, 401, "{\"error\":\"unauthorized\"}");
				return;
			}

			string path   = ctx.Request.Url.AbsolutePath.TrimEnd('/');
			string method = ctx.Request.HttpMethod;

			if (method == "GET" && path == "/health")
			{
				Write(ctx, 200, "{\"status\":\"ok\"}");
			}
			else if (method == "GET" && path == "/account")
			{
				Write(ctx, 200, AccountJson());
			}
			else if (method == "GET" && path == "/position")
			{
				Write(ctx, 200, PositionJson());
			}
			else if (method == "GET" && path == "/setup")
			{
				Write(ctx, 200, SetupJson());
			}
			else if (method == "GET" && path == "/trades/today")
			{
				Write(ctx, 200, TodayTradesJson());
			}
			else if (method == "POST" && path == "/strategy/enable")
			{
				ApexBridgeState.TradingEnabled = true;
				Write(ctx, 200, "{\"trading_enabled\":true}");
			}
			else if (method == "POST" && path == "/strategy/disable")
			{
				ApexBridgeState.TradingEnabled = false;
				Write(ctx, 200, "{\"trading_enabled\":false}");
			}
			else
			{
				Write(ctx, 404, "{\"error\":\"not found\"}");
			}
		}

		private Account FindAccount()
		{
			lock (Account.All)
				return Account.All.FirstOrDefault(a => a.Name == AccountName);
		}

		private string AccountJson()
		{
			var acc = FindAccount();
			if (acc == null)
				return $"{{\"error\":\"account '{Escape(AccountName)}' no encontrada\"}}";

			double cash     = acc.Get(AccountItem.CashValue, Currency.UsDollar);
			double realized  = acc.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
			double unrealized = acc.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);

			return "{"
				+ $"\"account\":\"{Escape(acc.Name)}\","
				+ $"\"cash_value\":{Num(cash)},"
				+ $"\"realized_pnl\":{Num(realized)},"
				+ $"\"unrealized_pnl\":{Num(unrealized)},"
				+ $"\"trading_enabled\":{(ApexBridgeState.TradingEnabled ? "true" : "false")}"
				+ "}";
		}

		private string PositionJson()
		{
			var acc = FindAccount();
			if (acc == null)
				return $"{{\"error\":\"account '{Escape(AccountName)}' no encontrada\"}}";

			Position pos;
			lock (acc.Positions)
				pos = acc.Positions.FirstOrDefault(p => p.Instrument.FullName.StartsWith(InstrumentName));

			if (pos == null)
				return $"{{\"instrument\":\"{Escape(InstrumentName)}\",\"market_position\":\"FLAT\",\"quantity\":0}}";

			return "{"
				+ $"\"instrument\":\"{Escape(pos.Instrument.FullName)}\","
				+ $"\"market_position\":\"{pos.MarketPosition}\","
				+ $"\"quantity\":{pos.Quantity},"
				+ $"\"avg_price\":{Num(pos.AveragePrice)}"
				+ "}";
		}

		private string TodayTradesJson()
		{
			List<TradeSummary> snapshot;
			lock (ApexBridgeState.TodayTradesLock)
				snapshot = new List<TradeSummary>(ApexBridgeState.TodayTrades);

			double totalPnl = 0;
			var items = new System.Text.StringBuilder();
			foreach (var t in snapshot)
			{
				if (items.Length > 0) items.Append(",");
				items.Append("{"
					+ $"\"direction\":\"{t.Direction}\","
					+ $"\"entry\":{Num(t.EntryPrice)},"
					+ $"\"exit\":{Num(t.ExitPrice)},"
					+ $"\"pnl\":{Num(t.PnlUsd)},"
					+ $"\"exit_time\":\"{Escape(t.ExitTime)}\","
					+ $"\"result\":\"{t.Result}\""
					+ "}");
				totalPnl += t.PnlUsd;
			}

			return "{"
				+ $"\"count\":{snapshot.Count},"
				+ $"\"total_pnl\":{Num(totalPnl)},"
				+ $"\"trades\":[{items}]"
				+ "}";
		}

		private string SetupJson()
		{
			string trend = ApexBridgeState.Trend ==  1 ? "BULLISH"
			             : ApexBridgeState.Trend == -1 ? "BEARISH" : "NONE";
			string sweep = ApexBridgeState.SweepState == 1 ? "DETECTED" : "WATCHING";
			string setup = ApexBridgeState.SetupState == 1
			    ? (ApexBridgeState.SetupDir == 1 ? "ACTIVE_LONG" : "ACTIVE_SHORT") : "NONE";

			return "{"
				+ $"\"trend\":\"{trend}\","
				+ $"\"pre_market_ready\":{Bool(ApexBridgeState.PreMarketReady)},"
				+ $"\"pre_market_high\":{Num(ApexBridgeState.PreMarketHigh)},"
				+ $"\"pre_market_low\":{Num(ApexBridgeState.PreMarketLow)},"
				+ $"\"sweep\":\"{sweep}\","
				+ $"\"sweep_level\":{Num(ApexBridgeState.SweepLevel)},"
				+ $"\"setup\":\"{setup}\","
				+ $"\"fvg_lower\":{Num(ApexBridgeState.FvgLower)},"
				+ $"\"fvg_upper\":{Num(ApexBridgeState.FvgUpper)},"
				+ $"\"price_in_fvg\":{Bool(ApexBridgeState.PriceInFvg)},"
				+ $"\"traded_today\":{Bool(ApexBridgeState.TradedToday)},"
				+ $"\"trading_disabled\":{Bool(ApexBridgeState.TradingDisabled)},"
				+ $"\"trading_enabled\":{Bool(ApexBridgeState.TradingEnabled)}"
				+ "}";
		}

		private static void Write(HttpListenerContext ctx, int code, string json)
		{
			byte[] buf = Encoding.UTF8.GetBytes(json);
			ctx.Response.StatusCode = code;
			ctx.Response.ContentType = "application/json";
			ctx.Response.ContentLength64 = buf.Length;
			ctx.Response.OutputStream.Write(buf, 0, buf.Length);
			ctx.Response.OutputStream.Close();
		}

		private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
		private static string Bool(bool b)  => b ? "true" : "false";
		private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
	}
}

#region Using declarations
using System;
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
	// Estado compartido entre AddOn y Estrategia (misma assembly Custom).
	public static class ApexBridgeState
	{
		// Arranca habilitado; el MCP/operador lo apaga cuando quiera.
		public static volatile bool TradingEnabled = true;
	}

	public class ApexBridgeAddOn : AddOnBase
	{
		// B3: cuenta/simbolo/token vienen de variables de entorno del SO (NT8 las hereda).
		// Defaults sirven para Sim out-of-the-box; al conectar Apex, el operador exporta
		// APEX_ACCOUNT (nombre real ej "APEX-XXXXX") y deja APEX_INSTRUMENT en NQ.
		private static readonly string AccountName    =
			Environment.GetEnvironmentVariable("APEX_ACCOUNT")    ?? "Sim101";
		// LOCKED: NQ mini (el bracket USD fijo lo implica; MNQ no sirve con esos USD).
		private static readonly string InstrumentName =
			Environment.GetEnvironmentVariable("APEX_INSTRUMENT") ?? "NQ";
		private const int Port = 8731;
		// Seguridad: token desde env BRIDGE_TOKEN (mismo valor que usa el MCP). Sin hardcode real.
		private static readonly string Token =
			Environment.GetEnvironmentVariable("BRIDGE_TOKEN")    ?? "CHANGE_ME_LOCAL_TOKEN";

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
			else if (method == "GET" && path == "/trades/today")
			{
				// TODO(Stream A/C): exponer trades reales del dia desde la estrategia.
				Write(ctx, 200, "{\"trades\":[],\"note\":\"placeholder\"}");
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
		private static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
	}
}

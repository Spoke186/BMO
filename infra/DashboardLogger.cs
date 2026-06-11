// infra/DashboardLogger.cs
// Envia eventos al dashboard local (FastAPI + SQLite).
// Todas las llamadas son fire-and-forget: si el dashboard cae, BMO no se ve afectado.

using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class DashboardLogger
    {
        private readonly string _base;

        public DashboardLogger(string baseUrl = "http://localhost:8000")
        {
            _base = baseUrl.TrimEnd('/');
        }

        public void LogBotStart(string account, string instrument, string version)
            => Post("bot_start",
                $"\"account\":\"{E(account)}\",\"instrument\":\"{E(instrument)}\",\"version\":\"{E(version)}\"");

        public void LogBotStop(string account, string instrument, string reason)
            => Post("bot_stop",
                $"\"account\":\"{E(account)}\",\"instrument\":\"{E(instrument)}\",\"reason\":\"{E(reason)}\"");

        public void LogHeartbeat(string account, string instrument)
            => Post("heartbeat",
                $"\"account\":\"{E(account)}\",\"instrument\":\"{E(instrument)}\"");

        public void LogTradeOpen(string dir, string setup, double entry, double stop, double tp,
                                 string account, string instrument, string sweepSource = "")
        {
            string ts = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            Post("trade_open",
                $"\"direction\":\"{dir}\",\"setup_type\":\"{E(setup)}\"," +
                $"\"entry_price\":{F(entry)},\"stop_price\":{F(stop)},\"tp_price\":{F(tp)}," +
                $"\"account\":\"{E(account)}\",\"instrument\":\"{E(instrument)}\"," +
                $"\"entry_time\":\"{ts}\",\"sweep_source\":\"{E(sweepSource)}\"");
        }

        public void LogTradeClose(string dir, double entry, double exitPx,
                                  double pnlUsd, double pnlPts, string reason, int durMin)
        {
            string ts = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            Post("trade_close",
                $"\"direction\":\"{dir}\"," +
                $"\"entry_price\":{F(entry)},\"exit_price\":{F(exitPx)}," +
                $"\"pnl_usd\":{F(pnlUsd)},\"pnl_pts\":{F(pnlPts)}," +
                $"\"close_reason\":\"{E(reason)}\",\"duration_min\":{durMin}," +
                $"\"exit_time\":\"{ts}\"");
        }

        public void LogSetupDetected(string setup, string dir, string details)
            => Post("setup_detected",
                $"\"setup_type\":\"{E(setup)}\",\"direction\":\"{dir}\",\"details\":\"{E(details)}\"");

        // ── Internals ─────────────────────────────────────────────────────────

        private void Post(string type, string fields)
        {
            Task.Run(() =>
            {
                try
                {
                    string body = $"{{\"event_type\":\"{type}\",\"data\":{{{fields}}}}}";
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                        wc.UploadString($"{_base}/api/events", "POST", body);
                    }
                }
                catch { }  // Dashboard failure must never affect BMO
            });
        }

        private static string F(double v) =>
            v.ToString("F4", CultureInfo.InvariantCulture);

        private static string E(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

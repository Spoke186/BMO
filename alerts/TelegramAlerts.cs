// alerts/TelegramAlerts.cs
//
// Alertas Telegram — monitoreo Demo 4 semanas.
// REQUIERE variables de entorno (solo en Realtime; null en backtest → no-op automático):
//   setx TELEGRAM_BOT_TOKEN "123456:ABC..."
//   setx TELEGRAM_CHAT_ID   "123456789"
// Reiniciar NT8 tras setx.

using System;
using System.Net;
using System.Threading.Tasks;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TelegramAlerts
    {
        private readonly string _token;
        private readonly string _chatId;
        private readonly bool   _enabled;

        public enum Msg
        {
            BotStart,
            BotStop,
            TradeOpened,
            TradeClosed,
            DailyLossWarning,
            ConsistencyWarning,
            StrategyError,
            Heartbeat,
            SetupSkipped,
            DailyReport,
        }

        public TelegramAlerts()
        {
            _token   = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? "";
            _chatId  = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID")   ?? "";
            _enabled = !string.IsNullOrEmpty(_token) && !string.IsNullOrEmpty(_chatId);
        }

        // ── API heredada (compatibilidad) ────────────────────────────────────

        public void SendAsync(Msg type, string context = "")
        {
            if (!_enabled) return;
            Task.Run(() => Post(BuildLegacyMessage(type, context)));
        }

        public void SendRawAsync(string text)
        {
            if (!_enabled) return;
            Task.Run(() => Post($"[BMO] {text}"));
        }

        // ── API rica para monitoreo Demo ─────────────────────────────────────

        /// <summary>Apertura de operación con detalles completos.</summary>
        public void SendTradeOpen(string dir, string instrument, double entryPrice,
            double stopPrice, double tpPrice, double riskUsd,
            string setupType, string ctx = "")
        {
            if (!_enabled) return;
            string ts      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string emoji   = dir == "LONG" ? "📈" : "📉";
            double tpPts   = Math.Abs(tpPrice   - entryPrice);
            double slPts   = Math.Abs(stopPrice - entryPrice);
            string msg =
                $"{emoji} APERTURA\n" +
                $"Fecha/Hora:  {ts}\n" +
                $"Instrumento: {instrument}\n" +
                $"Dirección:   {dir}\n" +
                $"Setup:       {setupType}\n" +
                $"Entrada:     {entryPrice:F2}\n" +
                $"Stop Loss:   {stopPrice:F2}  (-{slPts:F1} pts  |  -${riskUsd:F0})\n" +
                $"Take Profit: {tpPrice:F2}  (+{tpPts:F1} pts)\n" +
                (string.IsNullOrEmpty(ctx) ? "" : $"Variables:   {ctx}");
            Task.Run(() => Post(msg));
        }

        /// <summary>Cierre de operación con resultado y motivo.</summary>
        public void SendTradeClose(string dir, double entryPrice, double exitPrice,
            double pnlPts, double pnlUsd, TimeSpan duration, string closeReason)
        {
            if (!_enabled) return;
            string ts    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string emoji = pnlUsd >= 0 ? "✅" : "❌";
            string sign  = pnlUsd >= 0 ? "+" : "";
            int    mins  = (int)duration.TotalMinutes;
            string msg =
                $"{emoji} CIERRE\n" +
                $"Fecha/Hora:  {ts}\n" +
                $"Dirección:   {dir}\n" +
                $"Entrada:     {entryPrice:F2}\n" +
                $"Salida:      {exitPrice:F2}\n" +
                $"Resultado:   {sign}{pnlPts:F2} pts  |  {sign}${pnlUsd:F2}\n" +
                $"Duración:    {mins}m {duration.Seconds:D2}s\n" +
                $"Motivo:      {closeReason}";
            Task.Run(() => Post(msg));
        }

        /// <summary>Setup detectado pero no ejecutado.</summary>
        public void SendSetupSkipped(string setupType, string reason)
        {
            if (!_enabled) return;
            string ts  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string msg = $"⚠️ SETUP DESCARTADO\nFecha/Hora: {ts}\nSetup: {setupType}\nMotivo: {reason}";
            Task.Run(() => Post(msg));
        }

        /// <summary>Reporte diario al final de la sesión RTH.</summary>
        public void SendDailyReport(int trades, int wins, int losses,
            double pnlDay, double ddDay, double winPnl, double lossPnl)
        {
            if (!_enabled) return;
            string ts    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            double wr    = trades > 0 ? 100.0 * wins / trades : 0.0;
            string pfStr;
            if      (lossPnl >= 0 && winPnl > 0) pfStr = "∞";
            else if (lossPnl < 0)                pfStr = (winPnl / Math.Abs(lossPnl)).ToString("F2");
            else                                 pfStr = "0.00";
            string emoji = pnlDay >= 0 ? "🟢" : "🔴";
            string sign  = pnlDay >= 0 ? "+" : "";
            string msg =
                $"{emoji} REPORTE DIARIO\n" +
                $"Fecha/Hora:   {ts}\n" +
                $"Operaciones:  {trades}  (✅ {wins}  ❌ {losses})\n" +
                $"Win Rate:     {wr:F1}%\n" +
                $"Profit Factor:{pfStr}\n" +
                $"PnL día:      {sign}${pnlDay:F2}\n" +
                $"Drawdown:     ${ddDay:F2}";
            Task.Run(() => Post(msg));
        }

        // ── API observabilidad tiempo real ──────────────────────────────────

        public void SendActivation(string account, string instrument, string timeframe, string version)
        {
            if (!_enabled) return;
            string ts = DateTime.Now.ToString("HH:mm:ss");
            string msg =
                $"[BMO]\n" +
                $"ESTRATEGIA ACTIVADA\n" +
                $"Hora:        {ts}\n" +
                $"Cuenta:      {account}\n" +
                $"Instrumento: {instrument}\n" +
                $"Estado:      Monitoreando mercado";
            Task.Run(() => Post(msg));
        }

        public void SendDeactivation(string account, string instrument, string reason)
        {
            if (!_enabled) return;
            string ts = DateTime.Now.ToString("HH:mm:ss");
            string msg =
                $"[BMO]\n" +
                $"ESTRATEGIA DETENIDA\n" +
                $"Hora:        {ts}\n" +
                $"Cuenta:      {account}\n" +
                $"Instrumento: {instrument}\n" +
                $"Motivo:      {reason}";
            Task.Run(() => Post(msg));
        }

        public void SendSessionStart(string instrument, string account)
        {
            if (!_enabled) return;
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string msg =
                $"📈 BMO SESIÓN INICIADA\n" +
                $"Fecha/Hora:   {ts}\n" +
                $"Mercado:      {instrument}\n" +
                $"Cuenta:       {account}\n" +
                $"Estado:       BUSCANDO SETUPS";
            Task.Run(() => Post(msg));
        }

        public void SendSetupDetected(string setupType, string dir, string details)
        {
            if (!_enabled) return;
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string msg =
                $"👀 SETUP DETECTADO\n" +
                $"Fecha/Hora:   {ts}\n" +
                $"Setup:        {setupType}\n" +
                $"Dirección:    {dir}\n" +
                $"Detalle:      {details}\n" +
                $"Estado:       Validando condiciones finales.";
            Task.Run(() => Post(msg));
        }

        public void SendDiagnostics(string account, string instrument, string status)
        {
            if (!_enabled) return;
            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            bool ok = status == "OK";
            string result = ok ? "✅ LISTO PARA OPERAR" : $"⚠️ {status}";
            string msg =
                $"🔍 DIAGNÓSTICO DEL SISTEMA\n" +
                $"Fecha/Hora:   {ts}\n" +
                $"Cuenta:       {account}\n" +
                $"Instrumento:  {instrument}\n" +
                $"Telegram:     ✅ Conectado\n" +
                $"Estrategia:   ✅ Activa\n" +
                $"Resultado:    {result}";
            Task.Run(() => Post(msg));
        }

        public void SendTestSequence(string instrument)
        {
            if (!_enabled) return;
            Task.Run(() =>
            {
                Post("🧪 [TEST] Iniciando secuencia de prueba Telegram...");
                System.Threading.Thread.Sleep(600);
                Post($"👀 [TEST] Setup detectado (simulado)\nInstrumento: {instrument}\nDirección: LONG\nDetalle: Sweep PreMktL simulado | Validando condiciones finales.");
                System.Threading.Thread.Sleep(600);
                Post($"🚀 [TEST] Operación simulada ABIERTA\nInstrumento: {instrument}\nDirección: LONG\nSetup: SetupB (TEST)\nEntrada: 21000.00\nStop Loss: 20992.50  (-7.5 pts  |  -$375)\nTake Profit: 21021.00  (+21.0 pts)");
                System.Threading.Thread.Sleep(600);
                Post($"✅ [TEST] Operación simulada CERRADA\nDirección: LONG\nEntrada: 21000.00\nSalida: 21021.00\nResultado: +21.00 pts  |  +$1050.00\nDuración: 127m 00s\nMotivo: Take Profit (simulado)");
                System.Threading.Thread.Sleep(600);
                Post("🧪 [TEST] Secuencia completada. Telegram funciona correctamente ✅");
            });
        }

        // ── Mensajes heredados ───────────────────────────────────────────────

        private static string BuildLegacyMessage(Msg type, string ctx)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            switch (type)
            {
                case Msg.BotStart:           return $"🟢 [{ts}] BMO arrancó. {ctx}";
                case Msg.BotStop:            return $"🔴 [{ts}] BMO detenido. {ctx}";
                case Msg.TradeOpened:        return $"📈 [{ts}] Trade abierto. {ctx}";
                case Msg.TradeClosed:        return $"📉 [{ts}] Trade cerrado. {ctx}";
                case Msg.DailyLossWarning:   return $"⚠️ [{ts}] Daily loss warning. {ctx}";
                case Msg.ConsistencyWarning: return $"⚠️ [{ts}] Consistencia 50% — setup saltado. {ctx}";
                case Msg.StrategyError:      return $"🚨 [{ts}] ERROR en estrategia. {ctx}";
                case Msg.Heartbeat:          return $"💓 [{ts}] BMO alive. {ctx}";
                case Msg.SetupSkipped:       return $"⚠️ [{ts}] Setup descartado. {ctx}";
                case Msg.DailyReport:        return $"📊 [{ts}] Reporte diario. {ctx}";
                default:                     return $"[BMO/{ts}] {ctx}";
            }
        }

        // ── Foto (screenshot del gráfico) ───────────────────────────────────

        public void SendPhotoAsync(byte[] imageBytes, string caption = "")
        {
            if (!_enabled || imageBytes == null || imageBytes.Length == 0) return;
            Task.Run(() => PostPhoto(imageBytes, caption));
        }

        private void PostPhoto(byte[] imageBytes, string caption)
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                using (var form   = new System.Net.Http.MultipartFormDataContent())
                {
                    form.Add(new System.Net.Http.StringContent(_chatId),       "chat_id");
                    form.Add(new System.Net.Http.ByteArrayContent(imageBytes), "photo", "chart.png");
                    if (!string.IsNullOrEmpty(caption))
                        form.Add(new System.Net.Http.StringContent(caption), "caption");
                    client.PostAsync($"https://api.telegram.org/bot{_token}/sendPhoto", form)
                          .GetAwaiter().GetResult();
                }
            }
            catch { }
        }

        // ── HTTP POST ────────────────────────────────────────────────────────

        private void Post(string text)
        {
            try
            {
                string url     = $"https://api.telegram.org/bot{_token}/sendMessage";
                string payload = $"{{\"chat_id\":\"{_chatId}\",\"text\":{EscapeJson(text)}}}";
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                    wc.UploadString(url, "POST", payload);
                }
            }
            catch { /* Alerta no crítica: no crashear el bot si Telegram falla */ }
        }

        private static string EscapeJson(string s)
        {
            return "\"" + s.Replace("\\", "\\\\")
                           .Replace("\"", "\\\"")
                           .Replace("\n", "\\n")
                           .Replace("\r", "")
                 + "\"";
        }
    }
}

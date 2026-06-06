// alerts/TelegramAlerts.cs — Stream C, tarea C3
//
// Alertas Telegram para el bot NQ/MNQ. REQUIERE N8 (token + chat id del operador).
//
// CONFIGURACIÓN (antes de usar):
//   Variables de entorno (igual que el resto del proyecto — ver .env.example):
//     TELEGRAM_BOT_TOKEN=<token del bot @BotFather>
//     TELEGRAM_CHAT_ID=<chat id numérico del operador, o varios separados por coma>
//   Setear en Windows: setx TELEGRAM_BOT_TOKEN "123456:ABC..."
//                      setx TELEGRAM_CHAT_ID "111111111,222222222"   ← múltiples receptores
//   NT8 debe reiniciarse después de setx para leer los nuevos valores.
//
// INTEGRACIÓN (Stream A — ApexNqIctStrategy.cs):
//   Instanciar una vez en State.Configure:
//       _alerts = new TelegramAlerts();
//       _alerts.SendAsync(TelegramAlerts.Msg.BotStart);
//
//   Disparar en los eventos correspondientes:
//       OnExecutionUpdate → TradeOpened / TradeClosed
//       CustomStop logic  → DailyLossWarning
//       OnStateChange(Terminated) → BotStop
//       Heartbeat timer   → Heartbeat (cada 5 min, ver ejemplo abajo)
//
// HEARTBEAT (ejemplo de timer en NT8):
//   En State.Configure:
//       _heartbeatTimer = new System.Timers.Timer(5 * 60 * 1000);
//       _heartbeatTimer.Elapsed += (s, e) => _alerts.SendAsync(TelegramAlerts.Msg.Heartbeat);
//       _heartbeatTimer.AutoReset = true;
//       _heartbeatTimer.Start();
//   En State.Terminated:
//       _heartbeatTimer?.Stop(); _heartbeatTimer?.Dispose();
//
// NOTA DE SEGURIDAD: el token nunca va en el código. Solo variables de entorno.

using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TelegramAlerts
    {
        private readonly string   _token;
        private readonly string[] _chatIds; // uno o varios IDs separados por coma en la env var
        private readonly bool     _enabled;

        // Emoji prefixes para identificar tipo de alerta de un vistazo
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
        }

        public TelegramAlerts()
        {
            _token   = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN") ?? "";
            string raw = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") ?? "";
            _chatIds = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            _enabled = !string.IsNullOrEmpty(_token) && _chatIds.Length > 0;
        }

        /// <summary>
        /// Envía mensaje predefinido. No bloquea — fire-and-forget via Task.
        /// context: info libre (precio, PnL, etc.) que se añade al mensaje base.
        /// </summary>
        public void SendAsync(Msg type, string context = "")
        {
            if (!_enabled) return;
            string text = BuildMessage(type, context);
            Task.Run(() => Post(text));
        }

        /// <summary>Envía texto libre. Usar solo para errores no tipados.</summary>
        public void SendRawAsync(string text)
        {
            if (!_enabled) return;
            Task.Run(() => Post($"[BMO] {text}"));
        }

        // ── Construcción de mensajes ─────────────────────────────────────────

        private static string BuildMessage(Msg type, string ctx)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            switch (type)
            {
                case Msg.BotStart:
                    return $"🟢 [{ts}] BMO arrancó. {ctx}";
                case Msg.BotStop:
                    return $"🔴 [{ts}] BMO detenido. {ctx}";
                case Msg.TradeOpened:
                    return $"📈 [{ts}] Trade abierto. {ctx}";
                case Msg.TradeClosed:
                    return $"📉 [{ts}] Trade cerrado. {ctx}";
                case Msg.DailyLossWarning:
                    return $"⚠️ [{ts}] Daily loss warning. {ctx}";
                case Msg.ConsistencyWarning:
                    return $"⚠️ [{ts}] Consistencia 50% — setup saltado. {ctx}";
                case Msg.StrategyError:
                    return $"🚨 [{ts}] ERROR en estrategia. {ctx}";
                case Msg.Heartbeat:
                    return $"💓 [{ts}] BMO alive. {ctx}";
                default:
                    return $"[BMO/{ts}] {ctx}";
            }
        }

        // ── HTTP POST a Telegram Bot API ─────────────────────────────────────

        private void Post(string text)
        {
            string url       = $"https://api.telegram.org/bot{_token}/sendMessage";
            string escapedText = EscapeJson(text);
            foreach (string chatId in _chatIds)
            {
                try
                {
                    string payload = $"{{\"chat_id\":\"{chatId}\",\"text\":{escapedText}}}";
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                        wc.UploadString(url, "POST", payload);
                    }
                }
                catch
                {
                    // Alerta no crítica: si falla un destinatario, seguir con los demás.
                }
            }
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

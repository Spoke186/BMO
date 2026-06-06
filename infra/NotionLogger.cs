// infra/NotionLogger.cs
//
// Registra cada operación de la demo en la bitácora Notion.
// Base de datos DEMO: 9c557353-e4ee-4e97-801b-d7b25924c484
//
// CONFIGURACIÓN:
//   Variable de entorno: NOTION_API_KEY=secret_xxxx
//   (obtener en https://www.notion.so/my-integrations — integración interna)
//   El bot de integración debe tener acceso a la base de datos.
//   Setear en Windows: setx NOTION_API_KEY "secret_xxxx"
//   NT8 debe reiniciarse después de setx para leer el valor.
//
// USO (desde la estrategia, solo en State.Realtime):
//   notion = new NotionLogger();
//   // Al abrir trade:
//   notionPageTask = notion.RegistrarAperturaAsync(esFondeo, activo, dir, entry, stop, target, horaCol, fecha);
//   // Al cerrar trade:
//   notion.ActualizarCierreAsync(pageId, pnlUsd, resultado, notas);
//
// NOTA: los nombres de propiedad deben coincidir EXACTAMENTE con las columnas
// de la base de datos Notion (mayúsculas, tildes, espacios incluidos).

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class NotionLogger
    {
        private const string DatabaseId  = "9c557353-e4ee-4e97-801b-d7b25924c484";
        private const string ApiBase     = "https://api.notion.com/v1";
        private const string ApiVersion  = "2022-06-28";

        private readonly string _apiKey;
        private readonly bool   _enabled;

        public NotionLogger()
        {
            _apiKey  = Environment.GetEnvironmentVariable("NOTION_API_KEY") ?? "";
            _enabled = !string.IsNullOrEmpty(_apiKey);
        }

        // ── Apertura ─────────────────────────────────────────────────────────

        /// <summary>
        /// Crea una fila en la bitácora al lanzar la operación.
        /// Devuelve el pageId de Notion para poder actualizar el cierre después.
        /// Fire-and-forget: llamar sin await desde OnExecutionUpdate.
        /// </summary>
        public Task<string> RegistrarAperturaAsync(
            bool    esFondeo,
            string  activo,
            int     dir,           // 1 = long/compra, -1 = short/venta
            double  entryPrice,
            double  stopPrice,
            double  targetPrice,
            string  horaCol,       // "HH:mm" en hora Colombia
            DateTime fecha)
        {
            if (!_enabled) return Task.FromResult<string>(null);

            return Task.Run(() =>
            {
                try
                {
                    string direccion = dir == 1 ? "Compra" : "Venta";
                    string sesgo     = dir == 1 ? "Alcista" : "Bajista";
                    string nombre    = $"{direccion} {activo} {fecha:yyyy-MM-dd}";
                    string fechaIso  = fecha.ToString("yyyy-MM-dd");

                    string body = "{"
                        + $"\"parent\":{{\"database_id\":\"{DatabaseId}\"}},"
                        + "\"properties\":{"
                        + Prop_Title  ("Operación",       nombre)      + ","
                        + Prop_Date   ("Fecha",           fechaIso)    + ","
                        + Prop_Text   ("Activo",          activo)      + ","
                        + Prop_Select ("Sesgo",           sesgo)       + ","
                        + Prop_Select ("Dirección",       direccion)   + ","
                        + Prop_Text   ("Hora entrada (Col)", horaCol)  + ","
                        + Prop_Number ("Precio entrada",  entryPrice)  + ","
                        + Prop_Number ("Stop",            stopPrice)   + ","
                        + Prop_Number ("Target",          targetPrice) + ","
                        + Prop_Select ("Resultado",       "En curso")  + ","
                        + Prop_Text   ("RR planificado",  "1:3")       + ","
                        + Prop_Check  ("Barrida válida",  true)        + ","
                        + Prop_Check  ("Cambio estructura", true)      + ","
                        + Prop_Check  ("FVG válido",      true)        + ","
                        + Prop_Check  ("Confirmación 1m", true)
                        + "}"
                        + "}";

                    string responseJson = Request("POST", $"{ApiBase}/pages", body);
                    return ExtractPageId(responseJson);
                }
                catch
                {
                    return null;
                }
            });
        }

        // ── Cierre ────────────────────────────────────────────────────────────

        /// <summary>
        /// Actualiza la fila al cerrar la operación.
        /// </summary>
        public void ActualizarCierreAsync(string pageId, double pnlUsd, string notas)
        {
            if (!_enabled || string.IsNullOrEmpty(pageId)) return;

            string resultado = pnlUsd > 5 ? "Ganada" : pnlUsd < -5 ? "Perdida" : "Break-even";

            Task.Run(() =>
            {
                try
                {
                    string body = "{"
                        + "\"properties\":{"
                        + Prop_Select ("Resultado",           resultado)        + ","
                        + Prop_Number ("P&L USD",             pnlUsd)           + ","
                        + Prop_Select ("Emoción/Disciplina",  "Disciplinado")   + ","
                        + Prop_Check  ("Cumplió reglas",      true)             + ","
                        + Prop_Text   ("Notas / Aprendizaje", notas)
                        + "}"
                        + "}";

                    Request("PATCH", $"{ApiBase}/pages/{pageId}", body);
                }
                catch { }
            });
        }

        // ── HTTP ──────────────────────────────────────────────────────────────

        private string Request(string method, string url, string jsonBody)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method      = method;
            req.ContentType = "application/json";
            req.Headers.Add("Authorization",   $"Bearer {_apiKey}");
            req.Headers.Add("Notion-Version",  ApiVersion);

            byte[] data = Encoding.UTF8.GetBytes(jsonBody);
            req.ContentLength = data.Length;
            using (var stream = req.GetRequestStream())
                stream.Write(data, 0, data.Length);

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        // Extrae "id":"xxxx" del JSON de respuesta de Notion (sin dependencia de JSON lib)
        private static string ExtractPageId(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            const string key = "\"id\":\"";
            int start = json.IndexOf(key);
            if (start < 0) return null;
            start += key.Length;
            int end = json.IndexOf('"', start);
            return end > start ? json.Substring(start, end - start) : null;
        }

        // ── Helpers de serialización Notion ───────────────────────────────────

        private static string Prop_Title(string name, string val) =>
            $"\"{Esc(name)}\":{{\"title\":[{{\"text\":{{\"content\":{EscStr(val)}}}}}]}}";

        private static string Prop_Text(string name, string val) =>
            $"\"{Esc(name)}\":{{\"rich_text\":[{{\"text\":{{\"content\":{EscStr(val)}}}}}]}}";

        private static string Prop_Select(string name, string val) =>
            $"\"{Esc(name)}\":{{\"select\":{{\"name\":{EscStr(val)}}}}}";

        private static string Prop_Number(string name, double val) =>
            $"\"{Esc(name)}\":{{\"number\":{val.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}}}";

        private static string Prop_Date(string name, string isoDate) =>
            $"\"{Esc(name)}\":{{\"date\":{{\"start\":\"{isoDate}\"}}}}";

        private static string Prop_Check(string name, bool val) =>
            $"\"{Esc(name)}\":{{\"checkbox\":{(val ? "true" : "false")}}}";

        // Escapa comillas dentro de keys JSON
        private static string Esc(string s) => s.Replace("\"", "\\\"");

        // Escapa valor de string JSON completo con comillas
        private static string EscStr(string s) =>
            "\"" + s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "")
              + "\"";
    }
}

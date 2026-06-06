// Módulo de consistencia 50% para Apex Trader Funding.
//
// Regla Apex: ningún día puede representar más del 50% del profit acumulado
// en la ventana de evaluación. Este tracker persiste el P&L diario en un JSON
// para que la comprobación sobreviva reinicios de NT8.
//
// INTEGRACIÓN (Stream A):
//   1. Instanciar en OnStateChange (State.Configure):
//          _pnlTracker = new DailyPnlTracker();
//   2. Al cerrar un trade (OnExecutionUpdate / OnOrderUpdate cuando position = FLAT):
//          _pnlTracker.RecordTrade(closedPnl);
//   3. Antes de armar un nuevo setup, comprobar:
//          if (_pnlTracker.WouldViolateConsistency(estimatedTradePnl))
//              { Log("Setup saltado: regla consistencia 50%"); return; }
//   4. En OnStateChange (State.Terminated) → _pnlTracker.Save();  // ya auto-guarda

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Persiste P&L diario y evalúa la regla de consistencia 50% de Apex.
    /// Thread-safe para uso desde los callbacks de NT8 (OnExecutionUpdate corre en UI thread).
    /// </summary>
    public class DailyPnlTracker
    {
        // Archivo de persistencia en Documents\NinjaTrader 8\
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NinjaTrader 8", "ApexBot");
        private static readonly string DataFile = Path.Combine(DataDir, "daily_pnl.json");

        private readonly object _lock = new object();
        private PnlData _data;

        public DailyPnlTracker()
        {
            _data = Load();
            FlushOldDay(); // si NT8 lleva abierto pasando medianoche, cierra el día anterior
        }

        // ─── API pública ────────────────────────────────────────────────────────

        /// <summary>
        /// Registra el P&L de un trade cerrado (puede ser negativo).
        /// Llama esto en OnExecutionUpdate cuando la posición queda FLAT.
        /// </summary>
        public void RecordTrade(double pnl)
        {
            lock (_lock)
            {
                FlushOldDay();
                _data.TodayPnl   += pnl;
                _data.LastUpdate  = Today();
                Save();
            }
        }

        /// <summary>
        /// ¿El P&L hipotético de hoy violaría la regla 50%?
        /// Úsalo con el P&L del setup que estás a punto de tomar (TP calculado).
        /// Devuelve true → salta el setup.
        /// </summary>
        public bool WouldViolateConsistency(double hypotheticalTodayPnl)
        {
            lock (_lock)
            {
                FlushOldDay();
                double totalPnl = _data.TotalPnl;
                // Si el total acumulado es <= 0 no hay profit que proteger; deja operar.
                if (totalPnl <= 0) return false;

                double projectedToday = _data.TodayPnl + hypotheticalTodayPnl;
                // Viola si un día representa > 50% del profit total (regla Apex).
                return projectedToday > totalPnl * 0.50;
            }
        }

        /// <summary>Profit acumulado en la ventana de evaluación (solo días ganadores).</summary>
        public double TotalPnl
        {
            get { lock (_lock) { FlushOldDay(); return _data.TotalPnl; } }
        }

        /// <summary>P&L de la sesión de hoy hasta ahora.</summary>
        public double TodayPnl
        {
            get { lock (_lock) { FlushOldDay(); return _data.TodayPnl; } }
        }

        /// <summary>
        /// Fuerza escritura a disco. NT8 llama a este método en State.Terminated.
        /// RecordTrade() ya guarda automáticamente, así que esto es seguro extra.
        /// </summary>
        public void Save()
        {
            lock (_lock) { WriteFile(_data); }
        }

        // ─── Internos ───────────────────────────────────────────────────────────

        private void FlushOldDay()
        {
            // Sin lock extra; el llamador ya tiene _lock cuando viene de API pública.
            string today = Today();
            if (_data.CurrentDay != today && _data.TodayPnl != 0)
            {
                // Acumula el día que cerró.
                _data.TotalPnl  += _data.TodayPnl;
                _data.Days[_data.CurrentDay] = _data.TodayPnl;
                _data.TodayPnl   = 0;
                _data.CurrentDay = today;
                WriteFile(_data);
            }
            else if (_data.CurrentDay != today)
            {
                _data.CurrentDay = today;
            }
        }

        private static string Today() =>
            DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static PnlData Load()
        {
            try
            {
                if (!File.Exists(DataFile)) return NewData();
                string json = File.ReadAllText(DataFile, Encoding.UTF8);
                return Deserialize(json) ?? NewData();
            }
            catch { return NewData(); }
        }

        private static void WriteFile(PnlData data)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(DataFile, Serialize(data), Encoding.UTF8);
            }
            catch { /* NT8 Output podría usarse aquí si se inyecta el logger */ }
        }

        private static PnlData NewData() => new PnlData
        {
            CurrentDay = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TodayPnl   = 0,
            TotalPnl   = 0,
            Days       = new Dictionary<string, double>(),
            LastUpdate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        // Serialización manual (evita dependencias externas en NT8 Custom assembly).
        private static string Serialize(PnlData d)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"currentDay\":\"{d.CurrentDay}\",");
            sb.Append($"\"todayPnl\":{d.TodayPnl.ToString("F2", CultureInfo.InvariantCulture)},");
            sb.Append($"\"totalPnl\":{d.TotalPnl.ToString("F2", CultureInfo.InvariantCulture)},");
            sb.Append($"\"lastUpdate\":\"{d.LastUpdate}\",");
            sb.Append("\"days\":{");
            bool first = true;
            foreach (var kv in d.Days)
            {
                if (!first) sb.Append(",");
                sb.Append($"\"{kv.Key}\":{kv.Value.ToString("F2", CultureInfo.InvariantCulture)}");
                first = false;
            }
            sb.Append("}}");
            return sb.ToString();
        }

        // Parser JSON mínimo — solo lee lo que Serialize() escribe.
        private static PnlData Deserialize(string json)
        {
            try
            {
                var d   = NewData();
                string s = json.Trim().TrimStart('{').TrimEnd('}');
                // Extrae campos escalares simples con indexOf.
                d.CurrentDay = ExtractString(s, "currentDay");
                d.LastUpdate = ExtractString(s, "lastUpdate");
                d.TodayPnl   = ExtractDouble(s, "todayPnl");
                d.TotalPnl   = ExtractDouble(s, "totalPnl");

                // Extrae "days": { ... }
                int daysStart = json.IndexOf("\"days\":{", StringComparison.Ordinal);
                if (daysStart >= 0)
                {
                    int open  = json.IndexOf('{', daysStart + 7);
                    int close = json.IndexOf('}', open);
                    if (open >= 0 && close > open)
                    {
                        string inner = json.Substring(open + 1, close - open - 1);
                        foreach (string pair in inner.Split(','))
                        {
                            var parts = pair.Split(':');
                            if (parts.Length == 2)
                            {
                                string k = parts[0].Trim().Trim('"');
                                if (double.TryParse(parts[1].Trim(), NumberStyles.Any,
                                    CultureInfo.InvariantCulture, out double v))
                                    d.Days[k] = v;
                            }
                        }
                    }
                }
                return d;
            }
            catch { return null; }
        }

        private static string ExtractString(string s, string key)
        {
            int i = s.IndexOf($"\"{key}\":", StringComparison.Ordinal);
            if (i < 0) return "";
            int q1 = s.IndexOf('"', i + key.Length + 3) + 1;
            int q2 = s.IndexOf('"', q1);
            return q1 >= 0 && q2 > q1 ? s.Substring(q1, q2 - q1) : "";
        }

        private static double ExtractDouble(string s, string key)
        {
            int i = s.IndexOf($"\"{key}\":", StringComparison.Ordinal);
            if (i < 0) return 0;
            int start = i + key.Length + 3;
            int end   = s.IndexOfAny(new[] { ',', '}' }, start);
            if (end < 0) end = s.Length;
            string part = s.Substring(start, end - start).Trim();
            return double.TryParse(part, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }
    }

    // Modelo de datos interno (no serializable con DataContract para evitar deps).
    internal class PnlData
    {
        public string CurrentDay { get; set; }
        public double TodayPnl   { get; set; }
        public double TotalPnl   { get; set; }
        public string LastUpdate { get; set; }
        public Dictionary<string, double> Days { get; set; }
    }
}

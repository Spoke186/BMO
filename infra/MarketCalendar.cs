// infra/MarketCalendar.cs — Stream C, tarea C1 (versión NinjaScript/C#)
//
// Detecta festivos CME y medias sesiones para NQ futuros.
// La versión TypeScript (marketCalendar.ts) cubre el MCP; esta cubre la estrategia NT8.
//
// INTEGRACIÓN (Stream A — ApexNqIctStrategy.cs):
//   En OnBarUpdate, antes de TryArmSetup():
//       if (MarketCalendar.ShouldSkipToday(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _etZone)))
//           return; // festivo CME o fin de semana
//
//   Inicializar la zona horaria una vez en State.Configure:
//       _etZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
//
// NOTA: Fechas hardcoded 2026-2027. Actualizar cada diciembre para el año siguiente.
// Fuente: https://www.cmegroup.com/tools-information/holiday-calendar.html

using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.Strategies
{
    public static class MarketCalendar
    {
        // Festivos CME 2026 — mercado cerrado todo el día
        private static readonly HashSet<DateTime> Holidays2026 = new HashSet<DateTime>
        {
            new DateTime(2026,  1,  1), // New Year's Day
            new DateTime(2026,  1, 19), // MLK Jr. Day
            new DateTime(2026,  2, 16), // Presidents' Day
            new DateTime(2026,  4,  3), // Good Friday
            new DateTime(2026,  5, 25), // Memorial Day
            new DateTime(2026,  6, 19), // Juneteenth
            new DateTime(2026,  7,  3), // Independence Day (observed; 4 jul es sáb)
            new DateTime(2026,  9,  7), // Labor Day
            new DateTime(2026, 11, 26), // Thanksgiving
            new DateTime(2026, 12, 25), // Christmas Day
        };

        // Festivos CME 2027 — mercado cerrado todo el día
        private static readonly HashSet<DateTime> Holidays2027 = new HashSet<DateTime>
        {
            new DateTime(2027,  1,  1), // New Year's Day
            new DateTime(2027,  1, 18), // MLK Jr. Day
            new DateTime(2027,  2, 15), // Presidents' Day
            new DateTime(2027,  3, 26), // Good Friday
            new DateTime(2027,  5, 31), // Memorial Day
            new DateTime(2027,  6, 18), // Juneteenth (observed; 19 es sáb)
            new DateTime(2027,  7,  5), // Independence Day (observed; 4 es dom)
            new DateTime(2027,  9,  6), // Labor Day
            new DateTime(2027, 11, 25), // Thanksgiving
            new DateTime(2027, 12, 24), // Christmas (observed; 25 es sáb)
        };

        // Medias sesiones CME — cierre anticipado 1:00 PM ET
        // La kill zone (8:30–11:00 ET) queda dentro → el bot puede operar,
        // pero no debe armar setup si el cierre forzado cae antes de las 12:45.
        private static readonly HashSet<DateTime> HalfSessions2026 = new HashSet<DateTime>
        {
            new DateTime(2026,  7,  2), // Día antes de Independence Day
            new DateTime(2026, 11, 27), // Black Friday
            new DateTime(2026, 12, 24), // Christmas Eve
        };

        private static readonly HashSet<DateTime> HalfSessions2027 = new HashSet<DateTime>
        {
            new DateTime(2027, 11, 26), // Black Friday
            new DateTime(2027, 12, 23), // Día hábil antes de Christmas observado
        };

        // ── API pública ──────────────────────────────────────────────────────

        /// <summary>True si CME cierra todo el día (festivo federal).</summary>
        public static bool IsHoliday(DateTime etDate)
        {
            var d = etDate.Date;
            return Holidays2026.Contains(d) || Holidays2027.Contains(d);
        }

        /// <summary>
        /// True si es media sesión (cierre CME 1:00 PM ET).
        /// En media sesión el bot cierra forzado a 12:45 ET en lugar de 15:55.
        /// </summary>
        public static bool IsHalfSession(DateTime etDate)
        {
            var d = etDate.Date;
            return HalfSessions2026.Contains(d) || HalfSessions2027.Contains(d);
        }

        /// <summary>
        /// True → no operar hoy. Festivo completo o fin de semana.
        /// Usar en OnBarUpdate antes de evaluar cualquier setup.
        /// </summary>
        public static bool ShouldSkipToday(DateTime etDate)
        {
            return etDate.DayOfWeek == DayOfWeek.Saturday
                || etDate.DayOfWeek == DayOfWeek.Sunday
                || IsHoliday(etDate);
        }

        /// <summary>
        /// Hora de cierre forzado del bot en formato HHMM (entero, igual que ToTime() de NT8).
        /// Normal: 1555. Media sesión: 1245 (15 min de margen antes del cierre CME 1:00 PM ET).
        /// </summary>
        public static int BotForceCloseTime(DateTime etDate)
        {
            return IsHalfSession(etDate) ? 1245 : 1555;
        }
    }
}

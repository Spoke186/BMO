/**
 * Festivos CME y medias sesiones para futuros NQ/MNQ.
 *
 * CME cierra completamente en festivos federales y tiene cierre anticipado
 * (1:00 PM ET) en medias sesiones. Fuente: CME Group holiday calendar.
 *
 * Uso en MCP:
 *   import { isHoliday, isHalfSession, getSessionWindow } from "./marketCalendar.js";
 */

export type DateString = string; // "YYYY-MM-DD"

// Festivos CME NQ 2026 (mercado cerrado todo el día)
const HOLIDAYS_2026: DateString[] = [
  "2026-01-01", // New Year's Day
  "2026-01-19", // MLK Day
  "2026-02-16", // Presidents' Day
  "2026-04-03", // Good Friday
  "2026-05-25", // Memorial Day
  "2026-06-19", // Juneteenth
  "2026-07-03", // Independence Day (observed, 4 jul es sábado)
  "2026-09-07", // Labor Day
  "2026-11-26", // Thanksgiving
  "2026-12-25", // Christmas
];

// Festivos CME NQ 2027
const HOLIDAYS_2027: DateString[] = [
  "2027-01-01", // New Year's Day
  "2027-01-18", // MLK Day
  "2027-02-15", // Presidents' Day
  "2027-03-26", // Good Friday
  "2027-05-31", // Memorial Day
  "2027-06-18", // Juneteenth (observed, 19 es sábado)
  "2027-07-05", // Independence Day (observed, 4 es domingo)
  "2027-09-06", // Labor Day
  "2027-11-25", // Thanksgiving
  "2027-12-24", // Christmas (observed, 25 es sábado)
];

// Medias sesiones: cierre anticipado 1:00 PM ET (normativa CME)
const HALF_SESSIONS_2026: DateString[] = [
  "2026-11-27", // Black Friday
  "2026-12-24", // Christmas Eve
];

const HALF_SESSIONS_2027: DateString[] = [
  "2026-07-02", // Day before Independence Day (sesión reducida, no festivo)
  "2027-11-26", // Black Friday
  "2027-12-23", // Día antes de Christmas (24 es festivo observado)
];

const ALL_HOLIDAYS = new Set([...HOLIDAYS_2026, ...HOLIDAYS_2027]);
const ALL_HALF = new Set([...HALF_SESSIONS_2026, ...HALF_SESSIONS_2027]);

function toDateString(d: Date): DateString {
  return d.toISOString().slice(0, 10);
}

/** True si el mercado está cerrado todo el día. */
export function isHoliday(date?: Date): boolean {
  const key = toDateString(date ?? new Date());
  return ALL_HOLIDAYS.has(key);
}

/** True si es media sesión (cierre 1:00 PM ET). */
export function isHalfSession(date?: Date): boolean {
  const key = toDateString(date ?? new Date());
  return ALL_HALF.has(key);
}

/** True si es fin de semana. */
export function isWeekend(date?: Date): boolean {
  const d = date ?? new Date();
  const day = d.getUTCDay(); // 0=Dom, 6=Sáb
  return day === 0 || day === 6;
}

/** True si el mercado opera hoy (no festivo, no fin de semana). */
export function isTradingDay(date?: Date): boolean {
  return !isWeekend(date) && !isHoliday(date);
}

export interface SessionWindow {
  /** Hora de apertura kill zone en formato HH:MM ET */
  killZoneOpen: string;
  /** Hora de cierre kill zone */
  killZoneClose: string;
  /** Hora de cierre forzado del bot */
  botForceClose: string;
  /** True si el cierre anticipado limita la ventana */
  isHalfSession: boolean;
}

/**
 * Devuelve la ventana de sesión operativa.
 * En media sesión el cierre del CME (13:00 ET) anticipa el cierre forzado del bot.
 */
export function getSessionWindow(date?: Date): SessionWindow {
  const half = isHalfSession(date);
  return {
    killZoneOpen:  "08:30",
    // En media sesión la kill zone termina a las 11:00 igual, pero el bot
    // no debe abrir nuevos setups si el cierre forzado es < 12:00 ET.
    killZoneClose: "11:00",
    botForceClose: half ? "12:45" : "15:55",
    isHalfSession: half,
  };
}

/**
 * Resumen para exponer al MCP (tool check_market).
 */
export function getMarketStatus(date?: Date): {
  date: DateString;
  tradingDay: boolean;
  holiday: boolean;
  halfSession: boolean;
  session: SessionWindow | null;
} {
  const d = date ?? new Date();
  const trading = isTradingDay(d);
  return {
    date:        toDateString(d),
    tradingDay:  trading,
    holiday:     isHoliday(d),
    halfSession: isHalfSession(d),
    session:     trading ? getSessionWindow(d) : null,
  };
}

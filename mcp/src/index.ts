#!/usr/bin/env node
/**
 * MCP server de control/monitoreo para el bot ICT en NinjaTrader 8.
 *
 * Habla con el AddOn puente (ApexBridgeAddOn.cs) por HTTP en 127.0.0.1.
 * Alcance: SOLO lectura + enable/disable. Sin order placement (regla de seguridad).
 *
 * Config por entorno:
 *   BRIDGE_URL    (default http://127.0.0.1:8731)
 *   BRIDGE_TOKEN  (debe coincidir con el Token del AddOn)
 */
import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from "@modelcontextprotocol/sdk/types.js";

const BRIDGE_URL   = process.env.BRIDGE_URL   ?? "http://127.0.0.1:8731";
const BRIDGE_TOKEN = process.env.BRIDGE_TOKEN ?? "CHANGE_ME_LOCAL_TOKEN";

// ─── Bridge helper ──────────────────────────────────────────────────────────

async function bridge(path: string, method: "GET" | "POST" = "GET"): Promise<string> {
  const res = await fetch(`${BRIDGE_URL}${path}`, {
    method,
    headers: { "X-Bridge-Token": BRIDGE_TOKEN },
  });
  const body = await res.text();
  if (!res.ok) throw new Error(`bridge ${res.status}: ${body}`);
  return body;
}

// ─── Market calendar (C1 — Stream C) ────────────────────────────────────────
// Festivos CME NQ/MNQ. Cierre total en festivos; early-close 13:00 ET en medias sesiones.
// Fuente: CME Group holiday calendar. Actualizar en enero de cada año.

const CME_HOLIDAYS = new Set([
  // 2026
  "2026-01-01","2026-01-19","2026-02-16","2026-04-03",
  "2026-05-25","2026-06-19","2026-07-03","2026-09-07",
  "2026-11-26","2026-12-25",
  // 2027
  "2027-01-01","2027-01-18","2027-02-15","2027-03-26",
  "2027-05-31","2027-06-18","2027-07-05","2027-09-06",
  "2027-11-25","2027-12-24",
]);

const CME_HALF_SESSIONS = new Set([
  "2026-07-02","2026-11-27","2026-12-24",
  "2027-11-26","2027-12-23",
]);

function todayEt(): string {
  // Aproximación: convierte UTC a hora ET (UTC-5 estándar / UTC-4 DST).
  // En NT8 propio se usa TimeZoneInfo; aquí es solo para el MCP status.
  const now = new Date();
  return now.toISOString().slice(0, 10);
}

function checkMarket(dateStr?: string): string {
  const d = dateStr ?? todayEt();
  const jsDate = new Date(d + "T12:00:00Z");
  const dayOfWeek = jsDate.getUTCDay(); // 0=Dom,6=Sáb
  const isWeekend = dayOfWeek === 0 || dayOfWeek === 6;
  const isHoliday = CME_HOLIDAYS.has(d);
  const isHalf    = CME_HALF_SESSIONS.has(d);
  const isTradingDay = !isWeekend && !isHoliday;

  return JSON.stringify({
    date:          d,
    trading_day:   isTradingDay,
    holiday:       isHoliday,
    half_session:  isHalf,
    kill_zone:     isTradingDay ? { open: "08:30 ET", close: "11:00 ET" } : null,
    // En media sesión el cierre anticipado es 13:00 ET; bot cierra a las 12:45.
    bot_force_close: isTradingDay ? (isHalf ? "12:45 ET" : "15:55 ET") : null,
  });
}

// ─── Tool definitions ────────────────────────────────────────────────────────

interface Tool {
  name: string;
  description: string;
  inputSchema: object;
  handler: (args?: Record<string, unknown>) => Promise<string>;
}

const TOOLS: Tool[] = [
  {
    name: "get_account",
    description: "Balance, P&L realizado/no realizado y si el trading esta habilitado.",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
    handler: () => bridge("/account"),
  },
  {
    name: "get_position",
    description: "Posicion actual del instrumento operado (FLAT/LONG/SHORT, cantidad, precio).",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
    handler: () => bridge("/position"),
  },
  {
    name: "get_today_trades",
    description: "Resumen de los trades del dia (placeholder hasta integrar Stream A).",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
    handler: () => bridge("/trades/today"),
  },
  {
    name: "enable_strategy",
    description: "Habilita el bot: permite que arme nuevos setups.",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
    handler: () => bridge("/strategy/enable", "POST"),
  },
  {
    name: "disable_strategy",
    description: "Apaga el bot: no arma nuevos setups (no cierra posicion abierta).",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
    handler: () => bridge("/strategy/disable", "POST"),
  },
  {
    name: "check_market",
    description:
      "Consulta si hoy (o una fecha dada) es dia habil CME para NQ/MNQ. " +
      "Informa festivos, medias sesiones y ventana operativa del bot.",
    inputSchema: {
      type: "object",
      properties: {
        date: {
          type: "string",
          description: "Fecha a consultar en formato YYYY-MM-DD (omitir = hoy).",
        },
      },
      additionalProperties: false,
    },
    handler: (args) => Promise.resolve(checkMarket(args?.date as string | undefined)),
  },
];

// ─── MCP server ──────────────────────────────────────────────────────────────

const server = new Server(
  { name: "apex-nt8-mcp", version: "0.2.0" },
  { capabilities: { tools: {} } }
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({
  tools: TOOLS.map(({ name, description, inputSchema }) => ({ name, description, inputSchema })),
}));

server.setRequestHandler(CallToolRequestSchema, async (req) => {
  const tool = TOOLS.find((t) => t.name === req.params.name);
  if (!tool) throw new Error(`tool desconocida: ${req.params.name}`);
  try {
    const args = req.params.arguments as Record<string, unknown> | undefined;
    const text = await tool.handler(args);
    return { content: [{ type: "text", text }] };
  } catch (err) {
    return {
      content: [{ type: "text", text: `Error: ${(err as Error).message}` }],
      isError: true,
    };
  }
});

const transport = new StdioServerTransport();
await server.connect(transport);
console.error("apex-nt8-mcp v0.2.0 listo (stdio)");

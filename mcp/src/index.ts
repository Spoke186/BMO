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

const BRIDGE_URL = process.env.BRIDGE_URL ?? "http://127.0.0.1:8731";
const BRIDGE_TOKEN = process.env.BRIDGE_TOKEN ?? "CHANGE_ME_LOCAL_TOKEN";

async function bridge(path: string, method: "GET" | "POST" = "GET"): Promise<string> {
  const res = await fetch(`${BRIDGE_URL}${path}`, {
    method,
    headers: { "X-Bridge-Token": BRIDGE_TOKEN },
  });
  const body = await res.text();
  if (!res.ok) throw new Error(`bridge ${res.status}: ${body}`);
  return body;
}

const TOOLS = [
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
] as const;

const server = new Server(
  { name: "apex-nt8-mcp", version: "0.1.0" },
  { capabilities: { tools: {} } }
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({
  tools: TOOLS.map(({ name, description, inputSchema }) => ({ name, description, inputSchema })),
}));

server.setRequestHandler(CallToolRequestSchema, async (req) => {
  const tool = TOOLS.find((t) => t.name === req.params.name);
  if (!tool) throw new Error(`tool desconocida: ${req.params.name}`);
  try {
    const text = await tool.handler();
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
console.error("apex-nt8-mcp listo (stdio)");

# apex-nt8-mcp — MCP de control para el bot (Stream B)

MCP server (Node + TypeScript) que supervisa el bot ICT en NinjaTrader 8.
Habla con el **AddOn puente** (`../ntaddon/ApexBridgeAddOn.cs`) por HTTP en `127.0.0.1`.

**Alcance:** solo lectura + enable/disable. **Sin order placement** (regla de seguridad en cuenta fondeada).

## Arquitectura
```
Claude ──MCP(stdio)──► apex-nt8-mcp (Node) ──HTTP 127.0.0.1:8731──► ApexBridgeAddOn (NT8) ──► NinjaTrader
```

## Tools expuestos
| Tool | Acción |
|------|--------|
| `get_account` | balance, P&L realizado/no realizado, trading on/off |
| `get_position` | posición del instrumento (FLAT/LONG/SHORT) |
| `get_today_trades` | resumen trades del día (placeholder) |
| `enable_strategy` | permite armar setups |
| `disable_strategy` | bloquea nuevos setups (no cierra posición abierta) |

## Setup
```bash
cd mcp
npm install
npm run build
```
Variables de entorno (no commitear):
```
BRIDGE_URL=http://127.0.0.1:8731
BRIDGE_TOKEN=<mismo token que el AddOn>
```

## Registrar en Claude Code (`.mcp.json` del proyecto)
```json
{
  "mcpServers": {
    "apex-nt8": {
      "command": "node",
      "args": ["./mcp/dist/index.js"],
      "env": { "BRIDGE_URL": "http://127.0.0.1:8731", "BRIDGE_TOKEN": "..." }
    }
  }
}
```

## Lado NinjaTrader (AddOn)
1. Copiar `ntaddon/ApexBridgeAddOn.cs` a `Documents\NinjaTrader 8\bin\Custom\AddOns\`.
2. Editar constantes: `AccountName` (tu cuenta Apex), `InstrumentName` (NQ/MNQ), `Token`.
3. Compilar (F5). El server arranca con NinjaTrader.
4. **Integración con la estrategia:** en `ApexNqIctStrategy` el gate de setup ya consulta
   `ApexBridgeState.TradingEnabled`. Así el MCP la enciende/apaga.

## Pendiente (necesita datos Apex del operador)
- [ ] `AccountName` real (post-compra Apex).
- [ ] `InstrumentName` y contrato vigente.
- [ ] `Token` a variable de entorno (no hardcode).
- [ ] `get_today_trades` real (integrar con Stream A/C).
- [ ] ⚠️ Nada compilado/probado aún — sin NT8 en máquina de desarrollo.

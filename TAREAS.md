# TAREAS — Mapa de pendientes y necesidades (Proyecto BMO)

> Mapa accionable. Estado de verdad en `BITACORA.md`. División por persona en `PLAN.md`.
> Leyenda: ✅ hecho · 🚧 en curso · ⬜ sin iniciar · ⛔ bloqueado (espera un input)

---

## A. Lo que NECESITAMOS (inputs del operador — destraban el resto)

| # | Necesidad | De quién | Para qué destraba | Estado |
|---|-----------|----------|-------------------|--------|
| N1 | **Comprar cuenta Apex 50K** | Operador | Datos reales de cuenta → Stream B y Sim/Eval | ⬜ en proceso |
| N2 | Versión exacta NT8 (Help → About) | Operador | Verificar API del AddOn | ⬜ |
| N3 | Conexión Apex: **Rithmic o Tradovate** | Operador | Config feed/broker | ⬜ |
| N4 | Nombre/ID de cuenta en NT8 (ej `APEX-xxxxx`) | Operador | `AccountName` en el AddOn | ⛔ depende N1 |
| N5 | Símbolo + contrato vigente | Operador | `InstrumentName` en el AddOn | ✅ **NQ mini** (lo fija el bracket USD) |
| N6 | Cuál PC es **PC-LIVE** (corre NT8 24/5) | Operador | Dónde viven bot + AddOn + MCP | ⬜ |
| N7 | **2º usuario GitHub** | Operador | Invitar 2º colaborador | ✅ ptala611-oss invitado |
| N8 | Token bot **Telegram** + chat id | Operador | Alertas (Stream C) | ⬜ (después) |
| N9 | Visibilidad repo: público vs **privado** | Operador | Seguridad | ✅ **PÚBLICO** (decidido) |

---

## B. Tareas por stream

### Stream A — Estrategia & Backtest  (dueño: `ApexNqIctStrategy.cs`, `/backtest`)
| Tarea | Estado | Depende de |
|-------|--------|-----------|
| A1 Estrategia ICT base + guardas Apex + cap riesgo + toggle MCP | ✅ | — |
| A2 Compilar en NT8 (F5) y corregir errores | 🚧 | AddOn presente en `bin\Custom\` (ver B1) |
| A3 Backtest Strategy Analyzer (3–6 meses NQ 5m) | ⬜ | A2 |
| A4 Tuning displacement/FVG/pivotes ≈ ojo humano | ⬜ | A3 |
| A5 Consistencia 50% lun–vie (persistencia P&L entre días) | ✅ integrada (tiempo real); falta validar en Sim | C2 |
| A6 Fase 2: TP "siguiente liquidez" | ⬜ | A4 |

### Stream B — MCP & Bridge  (dueño: `/mcp`, `/ntaddon`)
| Tarea | Estado | Depende de |
|-------|--------|-----------|
| B1 AddOn C# HTTP (account/position/trades/enable/disable) | ✅ scaffold | — |
| B2 MCP server Node TS (5 tools) | ✅ scaffold | — |
| B3 Rellenar `AccountName`, `InstrumentName`, `Token`→env | 🚧 env listo (Sim101/NQ default + `BRIDGE_TOKEN`/`APEX_ACCOUNT`/`APEX_INSTRUMENT`); valores reales Apex esperan N4 | N4 |
| B4 `npm install` + `npm run build` + registrar en `.mcp.json` | ✅ | — |
| B5 Probar loop Claude→MCP→AddOn→NT8 (en Sim) | ⛔ | A2, B3, N6 |
| B6 `get_today_trades` real | ⬜ | integrar con A/C |

### Stream C — Infra, Riesgo & Ops  (dueño: `/infra`, `/utils`, `/alerts`)
| Tarea | Estado | Depende de |
|-------|--------|-----------|
| C1 `market_calendar` TS para MCP | ✅ `infra/marketCalendar.ts` | — |
| C1b `MarketCalendar.cs` para NinjaScript (estrategia) | ✅ `infra/MarketCalendar.cs` | — |
| C2 Módulo consistencia 50% (lógica + persistencia) | ✅ `infra/DailyPnlTracker.cs` (integrado A5) | — |
| C3 Alertas Telegram (trade/error/daily loss/heartbeat) | 🚧 `alerts/TelegramAlerts.cs` + wiring en estrategia ✅ (BotStart/Stop, TradeOpened/Closed, DailyLoss, Consistency); solo falta token N8. Heartbeat timer no cableado (opcional) | N8 |
| C4 VPS opcional (Windows, baja latencia CME) | 🚧 research ✅ (`infra/VPS_RESEARCH.md`); setup espera N6 | N6 |
| C5 Runbook operación (arranque diario, caídas, checklist) | ✅ `infra/RUNBOOK.md` | — |
| C6 Integrar `MarketCalendar.cs` en estrategia | ✅ hook en `OnBarUpdate` (skip festivo/finde + cierre forzado 12:45 en media sesion CME) | C1b |

### Infra / repo (compartido)
| Tarea | Estado |
|-------|--------|
| Repo GitHub Spoke186/BMO creado + push `main` | ✅ |
| `gh` instalado + autenticado (Spoke186) | ✅ |
| Colaboradores 2317SECH + ptala611-oss invitados (write) | ✅ (falta que acepten) |
| Decidir visibilidad repo | ⬜ N9 |

---

## C. Dependencias y ruta crítica

```
N1 (comprar Apex) ─┬─► N4 (nombre cuenta) ─► B3 ─► B5 (loop en Sim)
                   └─► Sim / Evaluación
A1 ✅ ─► A2 (compilar) ─► A3 (backtest) ─► A4 (tuning) ─► LISTO PARA SIM
                 ▲
        B1 ✅ (AddOn debe estar en bin\Custom para que A2 compile)
C2 (consistencia) ─► A5 (integrar en .cs)
N6 (PC-LIVE) ─► B5, C4
```

**Ruta crítica al primer run en Sim:** `A2 → A3 → A4` (estrategia validada) **+** `B3 → B5`
(control MCP), ambos sobre **PC-LIVE (N6)** con **Apex (N1)** o cuenta Sim de NT8.

**Se puede avanzar YA sin Apex:** A2/A3/A4 (con datos de NT8 Sim/históricos), B4, C1, C2, C5.
**Bloqueado hasta Apex/datos:** B3, B5, N4.

---

## D. Acción inmediata sugerida (esta semana)
1. **Operador:** comprar Apex (N1), pasar N2/N3/N5/N6 + 2º usuario (N7), decidir visibilidad (N9).
2. **Stream A:** poner ambos `.cs` en `bin\Custom\`, compilar (A2), arrancar backtest (A3).
3. **Stream B:** `npm install`/`build` (B4), dejar B3 listo con placeholders hasta N4.
4. **Stream C:** arrancar C1 (market calendar) y C2 (consistencia) — no dependen de Apex.

# TAREAS — Mapa de pendientes y necesidades (Proyecto BMO)

> Mapa accionable. Estado de verdad en `BITACORA.md`. División por persona en `PLAN.md`.
> **Estrategia canónica = `estrategia_liquidity_sweep_fvg.md`** (Liquidity Sweep + FVG, 15m/1m).
> TODO el código se basa en ese `.md`. Si el código contradice el `.md`, gana el `.md`.
> Leyenda: ✅ hecho · 🚧 en curso · ⬜ sin iniciar · ⛔ bloqueado (espera input) · ⚠️ gap vs `.md`

---

## A. Lo que NECESITAMOS (inputs del operador — destraban el resto)

| # | Necesidad | De quién | Para qué destraba | Estado |
|---|-----------|----------|-------------------|--------|
| N1 | **Comprar cuenta Apex 50K** | Operador | Datos reales de cuenta → Stream B y Sim/Eval | ⬜ en proceso |
| N2 | Versión exacta NT8 | Operador | Verificar API del AddOn | ✅ **8.1.7.1** |
| N3 | Conexión Apex: **Rithmic o Tradovate** | Operador | Config feed/broker | 🚧 demo en **Tradovate Sim** |
| N4 | Nombre/ID de cuenta en NT8 (ej `APEX-xxxxx`) | Operador | `AccountName` en el AddOn | ⛔ depende N1 |
| N5 | Símbolo + contrato vigente | Operador | `InstrumentName` | ✅ **NQ mini** (lo fija el bracket USD) |
| N6 | Cuál PC es **PC-LIVE** (corre NT8 24/5) | Operador | Dónde viven bot + AddOn + MCP | ⬜ |
| N7 | **2º usuario GitHub** | Operador | Colaborador | ✅ ptala611-oss + 2317SECH |
| N8 | Token bot **Telegram** + chat id | Operador | Alertas (Stream C) | ⬜ (después) |
| N9 | Visibilidad repo | Operador | Seguridad | ✅ **PÚBLICO** |
| N10 | **Valor de punto** del contrato confirmado con la firma | Operador | Fija stop/target en puntos (`.md` §2/§13) | ⬜ |

---

## B. Tareas por stream (estrategia = `estrategia_liquidity_sweep_fvg.md`)

### Stream A — Estrategia & Backtest  (dueño: `ApexNqIctStrategy.cs`, `/backtest`)
| Tarea | Estado | Depende de |
|-------|--------|-----------|
| A1 Estrategia 15m/1m sweep+CHoCH+FVG (base, rewrite SECH) | 🚧 código existe, **NO compila** | — |
| A2 **Fix compilación**: `EnterLong/Short` market sin overload `bool` (CS1501) | ⬜ | A1 |
| A3 Compilar en NT8 (F5) limpio | ⬜ | A2 |
| A4 ⚠️ Ventana entrada `.md` §3 = **9:30–14:00 ET** (código corta 11:00) | ⬜ | A3 |
| A5 ⚠️ Cierre **TOTAL** 14:00 ET (`.md` §11 "cerrar todo"); código solo bloquea nuevas entradas | ⬜ | A3 |
| A6 ⚠️ Liquidez = **rango pre-apertura** (`.md` §4); código usa swings fractales 15m | ⬜ diseño | A3 |
| A7 Backtest Strategy Analyzer (NQ, primaria 1m + 15m, 3–6 meses) | ⬜ | A3 |
| A8 Tuning params `.md` §13 (N velas swing, gap mín FVG, ratio rechazo 1m) | ⬜ | A7 |
| A9 2da operación opcional si la 1ra fue ganadora (`.md` §1/§11) | ⬜ fase 2 | A7 |
| A10 Consistencia 50% integrada (`DailyPnlTracker`) | ✅ falta validar en Sim | C2 |

### Stream B — MCP & Bridge  (dueño: `/mcp`, `/ntaddon`)
| Tarea | Estado | Depende de |
|-------|--------|-----------|
| B1 AddOn C# HTTP (account/position/trades/enable/disable) | ✅ scaffold | — |
| B2 MCP server Node TS (tools) | ✅ scaffold | — |
| B3 Rellenar `AccountName`/`InstrumentName`/`Token`→env | 🚧 env listo (Sim101/NQ); valores Apex esperan N4 | N4 |
| B4 `npm install`+`build`+registrar en `.mcp.json` | ✅ | — |
| B5 Probar loop Claude→MCP→AddOn→NT8 (en Sim) | ⛔ | A3, B3, N6 |
| B6 `get_today_trades` real | ⬜ | A/C |
| B7 Exponer estado del setup (sweep/CHoCH/FVG/armado) al MCP para monitoreo | ⬜ | A3 |

### Stream C — Infra, Riesgo & Ops  (dueño: `/infra`, `/utils`, `/alerts`)
| Tarea | Estado | Depende de |
|-------|--------|-----------|
| C1 `market_calendar` TS para MCP | ✅ `infra/marketCalendar.ts` | — |
| C1b `MarketCalendar.cs` para NinjaScript | ✅ `infra/MarketCalendar.cs` | — |
| C2 Consistencia 50% (lógica + persistencia) | ✅ `infra/DailyPnlTracker.cs` | — |
| C3 Alertas Telegram (trade/error/daily loss/heartbeat) | 🚧 `alerts/TelegramAlerts.cs` + wiring ✅; falta token N8 | N8 |
| C4 VPS opcional (Windows, baja latencia CME) | 🚧 research ✅; setup espera N6 | N6 |
| C5 Runbook operación | ✅ `infra/RUNBOOK.md` | — |
| C6 Integrar `MarketCalendar.cs` en estrategia | ✅ hook en `OnBarUpdate` | C1b |
| C7 Coherencia cierre: `.md` 14:00 ET vs media sesión CME 12:45 (`MarketCalendar`) | ⬜ | A5 |

---

## C. Gaps código ↔ `.md` (lo que falta para "que funcione según el .md")

| # | Gap | Archivo | Severidad |
|---|-----|---------|-----------|
| G1 | `EnterLong/Short(0, true, ...)` no compila (CS1501) | `ApexNqIctStrategy.cs` | 🔴 bloquea todo |
| G2 | Ventana entrada corta a 11:00; el `.md` permite hasta 14:00 ET | `ApexNqIctStrategy.cs` (`KillZoneEnd`) | 🟠 lógica |
| G3 | A las 14:00 solo bloquea entradas; el `.md` exige **cerrar todo** | `ApexNqIctStrategy.cs` (`ForcedExit`) | 🟠 lógica |
| G4 | Liquidez = swings fractales; el `.md` §4 usa **rango pre-apertura** | `ApexNqIctStrategy.cs` (`swingHighs/Lows15m`) | 🟠 diseño |

> G1 es lo único que impide compilar. G2–G4 son alineación a la estrategia del `.md`.

---

## D. Dependencias y ruta crítica

```
A1 (código) ─► A2 (fix compile G1) ─► A3 (F5 OK) ─┬─► A4/A5/A6 (alinear .md G2/G3/G4)
                                                  └─► A7 (backtest) ─► A8 (tuning)
B1 ✅ (AddOn en bin\Custom para que A3 compile)
N1 (Apex) ─► N4 ─► B3 ─► B5 (loop Sim)
N6 (PC-LIVE) ─► B5, C4
```

**Ruta crítica al primer run en Sim:** `A2 → A3 → A4/A5/A6 → A7 → A8` + `B3 → B5`, sobre **PC-LIVE (N6)** con Apex (N1) o Sim de NT8.

**Se puede avanzar YA sin Apex:** A2, A3, A4, A5, A6, A7, A8, B4, C*.
**Bloqueado hasta Apex/datos:** B3, B5, N4.

---

## E. Acción inmediata sugerida
1. **Stream A:** fix G1 (compilar), luego alinear G2/G3/G4 al `.md`, F5, backtest.
2. **Operador:** comprar Apex (N1), confirmar N3/N6, valor de punto (N10), token Telegram (N8).
3. **Stream B:** dejar B3 con placeholders hasta N4.
4. **Stream C:** C7 (coherencia cierre 14:00 vs media sesión).

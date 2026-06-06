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
| N11 | Token **Notion** (`NOTION_API_KEY`) + acceso del bot a la BD DEMO | Operador | Activa bitácora DEMO (`NotionLogger`, B8) | ⬜ |

---

## B. Tareas por stream (estrategia = `estrategia_liquidity_sweep_fvg.md`)

### Stream A — Estrategia & Backtest  (dueño: `ApexNqIctStrategy.cs`, `/backtest`)
| Tarea | Estado | Depende de |
|-------|--------|-----------|
| A1 Estrategia 15m/1m sweep+CHoCH+FVG (base, rewrite SECH) | ✅ código completo (G1–G4 + Notion) | — |
| A2 **Fix compilación**: CS1501 `EnterLong/Short` + CS0234 `DailyPnlTracker` | ✅ PR #14 / #10 | A1 |
| A3 Compilar en NT8 (F5) limpio | ✅ **confirmado** (Esteban, sesión 9: aparece en Strategy Analyzer = compiló limpio) | A2 |
| A4 Ventana entrada `.md` §3 = **9:30–14:00 ET** | ✅ `KillZoneEnd=1400` (operador: "entra cuando quiera") | A3 |
| A5 Cierre **TOTAL** 14:00 ET (`.md` §11) | ❌ operador G3: **dejar correr a TP/SL** (no cerrar total) | A3 |
| A6 Liquidez = **rango pre-apertura** (`.md` §4) | ✅ `preMarketHigh/Low` 1m hasta 9:30 ET (impl. SECH) | A3 |
| A7 Backtest Strategy Analyzer (NQ, primaria 1m + 15m, 3–6 meses) | ⬜ **compartida** (cualquier PC con NT8 + datos Globex/24h overnight) | A3 |
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
| B6 `get_today_trades` real | 🚧 **PR #19** (`/trades/today` real + `TodayTrades`); no mergeado: falta `using` (no compila) | A/C |
| B7 Exponer estado del setup (sweep/CHoCH/FVG/armado) al MCP para monitoreo | 🚧 **PR #19** (`PublishState`+`/setup`+tool `get_setup_state` v0.3.0); no mergeado: falta `using` | A3 |
| B8 Bitácora DEMO Notion (`infra/NotionLogger.cs` + wiring) | ✅ PR #16; inerte sin `NOTION_API_KEY` (N11) | — |

> **Handoff Sergio (sesión 7):** `main` limpio, contrato A↔B vivo (`ApexBridgeState.TradingEnabled`,
> ya consultado por la estrategia). **Puede avanzar YA con mock (sin NT8/Apex):** B6 (definir contrato
> real de `/trades/today`) y B7 (tool de monitoreo del setup). ⚠️ Si el Sim corre **MNQ**, exportar
> `APEX_INSTRUMENT=MNQ` o `/position` del AddOn filtra mal (`StartsWith("NQ")`, línea 196).

> **PR #19 (Sergio, B6+B7) — sesión 8:** revisado, **alinea** (`PublishState` es espejo read-only,
> no toca lógica de trading). 🔴 **NO mergear: no compila** — falta `using System.Collections.Generic;`
> en `ntaddon/ApexBridgeAddOn.cs` (usa `List<TradeSummary>`, líneas 69/245/247 → **CS0246**).
> **Sergio:** añade ese using, F5 en su NT8 para verificar (A3 ya es compartida) y mergea.

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
| C7 Coherencia cierre: `.md` 14:00 ET vs media sesión CME 12:45 (`MarketCalendar`) | ✅ coherente: `forcedExit` y el 12:45 de media sesión **solo bloquean entradas**, nunca aplanan (líneas 269–277); posición corre a TP/SL (G3). NT8 aplana en cierre de sesión | A5 | A5 |

---

## C. Gaps código ↔ `.md` (lo que falta para "que funcione según el .md")

| # | Gap | Archivo | Estado |
|---|-----|---------|--------|
| G1 | `EnterLong/Short(0, true, ...)` no compila (CS1501) | `ApexNqIctStrategy.cs` | ✅ PR #14 |
| G2 | Ventana entrada 09:30–14:00 ET (`.md` §3; operador sesión 6: "el bot entra cuando quiera") | `ApexNqIctStrategy.cs` (`KillZoneEnd=1400`) | ✅ corregido (antes 1100) |
| G3 | ForcedExit solo bloquea entradas; posición abierta corre a TP/SL (**intencional** — operador: "dejar que termine, 1 oportunidad/día"). NO cierra total pese a `.md` §11 | `ApexNqIctStrategy.cs` (`ForcedExit`) | ✅ decisión operador |
| G4 | Liquidez = rango pre-apertura 1m hasta 9:30 ET (`.md` §4) | `ApexNqIctStrategy.cs` (`preMarketHigh/Low`) | ✅ resuelto (impl. SECH) |

> G1–G4 resueltos / decididos. Falta F5 (A3) + backtest (A7).
> ⚠️ G4 requiere datos **overnight** (template Globex/24h); sin RTH-only o `preMarketReady` no se arma → cero setups.

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

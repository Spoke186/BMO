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
| A4 Ventana entrada | ✅ **`KillZoneEnd=1130`** (09:30–11:30 ET) — PR #28 de Sergio (más frecuencia); supersede el 1100 de sesión 11. Input → operador fija 1100 en runtime si quiere | A3 |
| A5 Cierre **TOTAL** 14:00 ET (`.md` §11) | ❌ operador G3: **dejar correr a TP/SL** (no cerrar total) | A3 |
| A6 Liquidez = **rango pre-apertura** (`.md` §4) | ✅ `preMarketHigh/Low` 1m hasta 9:30 ET (impl. SECH) | A3 |
| A7 Backtest Strategy Analyzer (NQ `NQ ##-##` 1m + 15m, 3–6 meses, Globex/24h) | 🚧 corre OK (sesión 10: rango pre-ap arma con ETH). Setup A daba ~0 trades (rango overnight muy ancho); Setup B (A11) genera más. Falta exportar + `analyze_backtest.py` | A3 |
| A8 Tuning params `.md` §13 (N velas swing, gap mín FVG, ratio rechazo 1m) | ⬜ | A7 |
| A9 2da operación opcional si la 1ra fue ganadora (`.md` §1/§11) | ⬜ fase 2 | A7 |
| A10 Consistencia 50% integrada (`DailyPnlTracker`) | ✅ falta validar en Sim | C2 |
| A11 **Setup B** (Opening Range Sweep: barrida 1m del rango pre-mercado → entrada directa SIN CHoCH/FVG ni filtro tendencia, flag `EnableSetupB` default ON) | ✅ PR #24 (Sergio) | A3 |
| A12 Exponer estado de **Setup B** en `PublishState`/`/setup` (hoy solo muestra Setup A) | ⬜ **Sergio** | A11, B7 |
| A13 Backtest comparativo **Setup A vs B** (win rate, PF, # trades) → decidir cuál(es) dejar | ✅ **herramienta mergeada** (PR #26): `analyze_backtest.py` separa por `Entry name` (FVG=A, Sweep=B → desglose A/B/combinado). Nota: #28 añadió Setup C (Order Block); el split solo etiqueta A/B hoy. Falta el export real | A7 |

### Stream B — MCP & Bridge  (dueño: `/mcp`, `/ntaddon`)
| Tarea | Estado | Depende de |
|-------|--------|-----------|
| B1 AddOn C# HTTP (account/position/trades/enable/disable) | ✅ scaffold | — |
| B2 MCP server Node TS (tools) | ✅ scaffold | — |
| B3 Rellenar `AccountName`/`InstrumentName`/`Token`→env | 🚧 env listo (Sim101/NQ); valores Apex esperan N4 | N4 |
| B4 `npm install`+`build`+registrar en `.mcp.json` | ✅ | — |
| B5 Probar loop Claude→MCP→AddOn→NT8 (en Sim) | ⛔ | A3, B3, N6 |
| B6 `get_today_trades` real | ✅ **PR #19 mergeado** (`/trades/today` real + `TodayTrades`); fix `using` aplicado | A/C |
| B7 Exponer estado del setup (sweep/CHoCH/FVG/armado) al MCP para monitoreo | ✅ **PR #19 mergeado** (`PublishState`+`/setup`+tool `get_setup_state`, MCP v0.3.0) | A3 |
| B8 Bitácora DEMO Notion (`infra/NotionLogger.cs` + wiring) | ✅ código PR #16; inerte sin token | — |
| B9 **Activar bitácora Notion** (Sergio): poner `NOTION_API_KEY` (N11), dar acceso del bot a la BD, validar registro apertura+cierre en Sim | ⬜ **Sergio** | N11 |

> **Handoff Sergio (sesión 7):** `main` limpio, contrato A↔B vivo (`ApexBridgeState.TradingEnabled`,
> ya consultado por la estrategia). **Puede avanzar YA con mock (sin NT8/Apex):** B6 (definir contrato
> real de `/trades/today`) y B7 (tool de monitoreo del setup). ⚠️ Si el Sim corre **MNQ**, exportar
> `APEX_INSTRUMENT=MNQ` o `/position` del AddOn filtra mal (`StartsWith("NQ")`, línea 196).

> **PR #19 (B6+B7) — mergeado sesión 10:** ✅ en `main`. Estado ICT (`PublishState` read-only) +
> `/setup` + `/trades/today` real + tool MCP `get_setup_state` (v0.3.0, 7 tools). El operador autorizó
> añadir el `using System.Collections.Generic;` faltante (CS0246) sobre la rama de Sergio. ⚠️ **Falta
> F5 en NT8 para confirmar compile** (mock MCP no se pudo correr: puerto 8731 ocupado por el AddOn).

### Stream C — Infra, Riesgo & Ops  (dueño: `/infra`, `/utils`, `/alerts`)
| Tarea | Estado | Depende de |
|-------|--------|-----------|
| C1 `market_calendar` TS para MCP | ✅ `infra/marketCalendar.ts` | — |
| C1b `MarketCalendar.cs` para NinjaScript | ✅ `infra/MarketCalendar.cs` | — |
| C2 Consistencia 50% (lógica + persistencia) | ✅ `infra/DailyPnlTracker.cs` | — |
| C3 Alertas Telegram (trade/error/daily loss/heartbeat) | 🚧 `alerts/TelegramAlerts.cs` + wiring ✅; falta token N8 | N8 |
| C8 **Bot Telegram con SEÑALES** (Alan): manda señal de entrada en vivo — dirección, precio, setup (A/B), stop/target — al canal. Formato + activación con token | ⬜ **Alan** | C3, N8 |
| C4 VPS opcional (Windows, baja latencia CME) | 🚧 research ✅; setup espera N6 | N6 |
| C5 Runbook operación | ✅ `infra/RUNBOOK.md` | — |
| C6 Integrar `MarketCalendar.cs` en estrategia | ✅ hook en `OnBarUpdate` | C1b |
| C7 Coherencia cierre: `.md` 14:00 ET vs media sesión CME 12:45 (`MarketCalendar`) | ✅ coherente: `forcedExit` y el 12:45 de media sesión **solo bloquean entradas**, nunca aplanan (líneas 269–277); posición corre a TP/SL (G3). NT8 aplana en cierre de sesión | A5 | A5 |

---

## C. Gaps código ↔ `.md` (lo que falta para "que funcione según el .md")

| # | Gap | Archivo | Estado |
|---|-----|---------|--------|
| G1 | `EnterLong/Short(0, true, ...)` no compila (CS1501) | `ApexNqIctStrategy.cs` | ✅ PR #14 |
| G2 | Ventana entrada — **`KillZoneEnd=1130`** (09:30–11:30 ET, PR #28 de Sergio; supersede 1100 de sesión 11). `.md` §3 dice hasta 14:00 → gana la decisión del operador/Sergio | `ApexNqIctStrategy.cs` (`KillZoneEnd=1130`) | ✅ decisión operador |
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

## E. Acción inmediata (sesión 13)
- **Reto Setup A vs B — A y B CONECTADOS** (como los diseñó Sergio; no se aíslan). Cada uno tunea los
  params de SU setup sobre el run combinado; A13 muestra la contribución de cada uno. Meta y tareas en **§F**.
- **Esteban (Setup A):** EA1 compilar+correr en NT8 (F5) la estrategia mergeada (#28) — catch-up.
  Plantilla **`CME US Index Futures ETH`** en el Analyzer (overnight). Luego tunear A (EA3, §F).
- **Sergio (Setup B):** tunear params de B sobre el run conectado (SB3, §F). *(Aislar B con un Input
  `EnableSetupA` queda parqueado — no se hace por ahora.)*
- **Alan (Stream C):** C8 — bot Telegram con señales (sin cambio; PR #27 pendiente de rebase).
- **Operador:** N1 (Apex), N6 (PC-LIVE), N8 (token Telegram), N10 (valor punto), N11 (token Notion).

---

## F. Reto Setup A vs B — bot que logre **≥ $3.000 / mes** (sesión 13)

> Alineado a la estrategia mergeada `ApexNqIctStrategy.cs` (#28 de Sergio). El objetivo es decidir,
> con datos, **cuál setup (A o B) rinde mejor** y dejar el bot tuneado para el profit goal Apex.

### Meta compartida (criterios de éxito del backtest)
- **Backtest:** NQ (`NQ ##-##` o front month), **1m primaria**, sesión **`CME US Index Futures ETH`**
  (Globex 24h, overnight obligatorio), **30 días (1 mes)**.
- **Frecuencia:** la estrategia es **selectiva** (1 trade/día; 2º opcional si el 1º gana vía
  `Allow2ndTradeIfWinner`) → **NO se fuerza un trade diario**. Objetivo: **≥ 10 trades buenos/mes**
  (Sergio venía en ~8-11/mes). "Bueno" = ganador (cierra en TP +$700).
- **Profit neto del mes ≥ $3.000** (= profit goal Apex). Bracket fijo: **win +$700 / loss −$250**.
  Math: a ~12 trades/mes hace falta **~53% win rate**; con más frecuencia, win rate menor alcanza.
- **Reglas Apex:** **consistencia 50%** (ningún día > 50% del profit del mes), **no romper daily loss
  $400** ni **trailing DD $2.500**. Validar con `backtest/analyze_backtest.py` (proxy local).
- **Cómo medir:** export Trades de NT8 → `python analyze_backtest.py <csv>` → registrar
  **win rate, PF, net, #trades, consistencia 50%, max DD**.

### Cómo se corren — sistema conectado A+B (como lo diseñó Sergio)
A y B van **juntos**, no aislados: A (FVG, selectivo) se evalúa primero; B (sweep) entra los días que A
no disparó (comparten `tradedToday`). El backtest corre el **sistema completo**; el desglose **A13**
(`analyze_backtest.py` por `Entry name`) muestra cuántos trades y qué rendimiento aportó **cada setup
dentro del sistema** — que es lo que pasa en la operación real. **No se aísla ni se edita el `.cs`.**

| Tarea | Resp. | Estado | Depende |
|-------|-------|--------|---------|
| EA1 Compilar+correr la estrategia #28 en NT8 (F5) — catch-up | Esteban | ⬜ | — |
| R1 Backtest del **sistema A+B** NQ 1m ETH 30d → export → `analyze_backtest.py` (baseline + split A/B) | ambos | ⬜ | EA1 |
| EA3 Tunear params de **A** (`MinFvgPoints`, `DisplacementAtrMult`, `SwingStrength15m`, `RejectionWickRatio`, `FvgValidBars`, `SweepChochMaxBars15m`) | Esteban | ⬜ | R1 |
| SB3 Tunear params de **B** (`MinSweepTicks`, `MinBodyTicks`, `SetupBRequiresTrend`, `SetupBMaxMinutes`, `EnablePdhPdl`, `Allow2ndTradeIfWinner`) | Sergio | ⬜ | R1 |
| R2 Con A y B tuneados, correr el sistema 30d → A13 split → ver contribución de cada setup + chequear meta (≥10 trades/mes, ≥$3.000, consistencia) | ambos | ⬜ | EA3, SB3 |

> **Comparación sin aislar:** el split A13 sobre el run conectado dice qué aporta cada setup en la
> operación real (B solo dispara los días que A no). Si más adelante se quiere un *bake-off* puro
> A-solo vs B-solo, queda **parqueada** la opción de un Input `EnableSetupA` (lo añadiría Sergio) —
> **hoy NO se hace**, dejamos A+B conectados.

> **Nota de alineación:** la estrategia es selectiva (~10-15 trades/mes, no diarios); el profit de
> $3.000 sale de **win rate × frecuencia**, no de forzar entradas. `Allow2ndTradeIfWinner=true` sube
> la frecuencia (2º trade tras un ganador) — palanca de tuning, no obligatorio.

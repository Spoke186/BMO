# BITÁCORA — Proyecto BMO (Bot ICT NQ/MNQ + MCP)

> **Memoria compartida de los 3 Claude Code.** Léela al iniciar sesión, junto con `CLAUDE.md`
> (contexto) y `PLAN.md` (división del trabajo). **Actualízala al final de cada sesión** con lo
> que hiciste, decisiones tomadas y pendientes. Es la fuente de verdad del estado del proyecto.

---

## 0. Qué es el proyecto
Bot de trading **ICT** para futuros del Nasdaq (**NQ/MNQ**) en **NinjaTrader 8**, bajo reglas
**Apex Trader Funding** (plan **50K**). Más un **MCP** de control/monitoreo. Lo construyen 3
personas, cada una con un Claude Code en su PC. Repo: **https://github.com/Spoke186/BMO**.

---

## 1. Decisiones LOCKED (no re-litigar sin avisar)

| Tema | Decisión |
|------|----------|
| Plataforma | **NinjaTrader 8 / NinjaScript (C#)**. Descartado: Python+API, TradingView+PickMyTrade |
| Mercado / contrato | **2 contratos NQ mini** (el bracket USD fijo lo implica; MNQ no sirve con esos USD) |
| Timeframes | **Sesgo/barrida/CHoCH/FVG 15m · gatillo (confirmación) 1m**. Serie primaria = **1m**; 15m por `AddDataSeries` |
| Tendencia (sesgo) | Estructura **HH/HL** por pivotes fractales (fuerza 3 en 15m) |
| Entrada | Sweep rango pre-apertura → CHoCH + displacement (cuerpo ≥1.5×ATR) → **FVG 15m** → retroceso al FVG + **confirmación 1m** (rechazo o mini-CHoCH) → **mercado** al cierre 1m, a favor de tendencia |
| Stop | **USD fijo $250** (`CalculationMode.Currency`). El extremo del sweep solo invalida el límite pendiente |
| Take profit | **USD fijo $700** (~1:2.8), ambas direcciones |
| Ventana | **Ejecución 09:30–14:00 ET** (08:30–13:00 Col). A 14:00 ET **deja de abrir**; posición abierta **corre a TP/SL** (operador G3, NO cierre total). NT8 aplana al cierre de sesión (`IsExitOnSessionCloseStrategy=true`). **1 setup/día** |
| Plan Apex | 50K · Trailing DD $2,500 · Profit goal $3,000 · Daily loss propio $400 |
| Consistencia | 50% lun–vie (ningún día > 50% del profit acumulado). **No en código aún** (manual) |
| MCP | **Node + TypeScript** · alcance **solo lectura + enable/disable** · corre en **localhost** |
| AddOn | C# dentro de NT8, HTTP en `127.0.0.1:8731`, auth por token, toggle `ApexBridgeState` |

### Nota de riesgo (operador)
Bracket FIJO en USD por decisión del operador: **stop $250, target $700, 2 NQ**. Con 1 trade/día,
un stop = −$250 (< daily loss $400) y lejos del trailing DD $2,500. La entrada sigue esperando
sweep/FVG; el stop ya NO es estructural (antes "detrás del sweep" + cap `MaxRiskPerTrade`, ambos
removidos). El extremo del sweep ahora solo cancela el límite si la estructura se rompe antes del fill.

---

## 2. Qué existe en el repo (archivos)

| Archivo | Stream | Estado |
|---------|--------|--------|
| `ApexNqIctStrategy.cs` | A | Estrategia ICT completa + guardas Apex + toggle MCP. ✅ revisado (compila); 🚧 falta F5/backtest en NT8 |
| `backtest/analyze_backtest.py` (+ test + README) | A | Analizador de backtest stdlib: PF, max DD, Sharpe, OOS, Monte Carlo, reglas Apex + consistencia 50%. ✅ 8 tests OK |
| `backtest/PREFLIGHT.md` | A | Checklist F5 + Strategy Analyzer + gotchas (timezone ET, primario 5m, stop tight) |
| `ntaddon/ApexBridgeAddOn.cs` | B | AddOn HTTP (account/position/trades/enable/disable). 🚧 sin compilar/probar |
| `mcp/` (+ `dist/index.js`) | B | MCP server Node TS, **6 tools** (+`check_market`). ✅ build limpio |
| `.mcp.json` + `.env.example` | B | Registro MCP + plantilla env |
| `infra/marketCalendar.ts` | C (por B) | Festivos/medias sesiones CME 2026/27 + `getMarketStatus()`. ✅ |
| `infra/DailyPnlTracker.cs` | C (por B) | Consistencia 50% Apex, persiste JSON. **API lista para que A la integre (A5)**. ✅ |
| `infra/RUNBOOK.md` | C (por B) | Runbook operación (pre/durante/post, emergencias). ✅ |
| `CLAUDE.md` | — | Contexto del proyecto |
| `PLAN.md` | — | División en 3 streams + coordinación git |
| `README.md` | — | Instalación NT8, params, reglas Apex |
| `BITACORA.md` | — | Este archivo |
| `.gitignore` | — | Secretos/build/node/csv ignorados |

### Guardas Apex en el código (estado)
- ✅ Stop obligatorio · ✅ No DCA · ✅ 1 entrada/setup · ✅ Ventana horaria · ✅ Max daily loss
- ⚠️ Trailing DD = **proxy local** (Apex manda). *(El antiguo cap riesgo/trade fue removido: el stop es USD fijo $250.)*
- ⚠️ Consistencia 50%: **integrada en tiempo real** — `ApexNqIctStrategy.cs` usa `DailyPnlTracker`
  (`RecordTrade` al cerrar, `WouldViolateConsistency(ProfitTargetUsd)` antes de armar). Solo activo en
  `State.Realtime` (en backtest se valida con `analyze_backtest.py`). Falta probar en Sim. Apex = verdad oficial.

---

## 3. Cronología

### 2026-06-06 — Sesión 7 (Claude Stream A en PC de Esteban / Spoke186)

**Tema: auditoría lógica `.cs` vs `.md`, reconciliar docs, mergear PR #17 y dejar `main` listo para Stream B (Sergio).**

- **Auditoría `ApexNqIctStrategy.cs` vs `estrategia_liquidity_sweep_fvg.md` (paso a paso):** el código
  implementa el `.md` con fidelidad. **Sin bugs que rompan la lógica.** Hallazgos:
  - 🔴 **Bloqueo de backtest (dato, no código):** con datos **RTH-only**, la ventana pre-apertura queda
    vacía → `preMarketReady` nunca pasa a true → **cero setups** (líneas 251–263). A7 OBLIGA template
    **Globex/24h con datos overnight**. Ya estaba en PREFLIGHT; reconfirmado en código.
  - 🟡 CHoCH usa el último pivote 15m (rezagado `SwingStrength15m`) → puede tomar un nivel viejo.
    Item de **tuning A8**, no bug.
  - 🟡 Pivotes sin dedup (`AddCapped`) → ruido menor en HH/HL. Tuning.
  - Stop = USD fijo $250 (no estructural); el extremo del sweep solo invalida el setup pendiente
    (`fvgInvalidPrice`). Coherente con la decisión del operador.
- **C7 resuelto (era ⬜):** `forcedExit` y el cierre 12:45 de media sesión CME **solo bloquean
  entradas**, nunca aplanan (líneas 269–277). La posición corre a TP/SL (G3); NT8 la aplana en el
  cierre de sesión (`IsExitOnSessionCloseStrategy=true`, `ExitOnSessionCloseSeconds=60`). Sin riesgo
  overnight. Coherente con G3.
- **Docs reconciliados (decisión operador: "alinear todo"):**
  - `BITACORA §1` tabla LOCKED (estaba vieja): timeframes 15m/**1m** (era 5m), entrada real
    (sweep→CHoCH→FVG→confirmación 1m, era "límite fill completo"), ventana **09:30–14:00 + run-to-TP**
    (era 8:30–11:00 + cierre 15:55), quitado "cap riesgo/trade" (removido; el stop es USD fijo).
  - `estrategia_liquidity_sweep_fvg.md` §3: **nota de override** del operador (G3) — el bot NO aplana
    a 14:00, deja correr a TP/SL. NO se borró la regla de SECH; solo se marcó la excepción.
  - `TAREAS`: C7 marcado ✅.
- **PR #17 revisado y mergeado a `main`:** único cambio de código = `KillZoneEnd 1100→1400` (G2,
  decisión operador). G3/G4/Notion intactos de SECH (#16). Verificado que alinea antes de aceptar.
- **Handoff Stream B (Sergio):** `main` limpio, contrato A↔B vivo (`ApexBridgeState.TradingEnabled`,
  la estrategia ya lo consulta en `TryDetectSetup15m`). Sergio puede avanzar **YA con mock** (sin
  NT8/Apex): **B6** (contrato real de `/trades/today`) y **B7** (tool de monitoreo del estado del
  setup: sweep/CHoCH/FVG/armado). ⚠️ Si el Sim corre **MNQ** (no NQ), exportar `APEX_INSTRUMENT=MNQ`
  o el `/position` del AddOn filtra mal (`StartsWith("NQ")`, línea 196).
- **Pendiente operador:** A3 (F5 + backtest con datos overnight), N1 Apex, N6 PC-LIVE, N10 valor punto.

### 2026-06-06 — Sesión 6 (Claude Stream A en PC de Esteban / Spoke186)

**Tema: resolver gaps G2/G3/G4 + mergear bitácora Notion de SECH (PR #16).**

- **NT8 conectado y operativo** (Tradovate Sim, MNQ JUN26 en gráfico). F5 mostró el editor con la
  estrategia cargada; A3 (compilar limpio + backtest) sigue pendiente del operador.
- **PR #16 de SECH mergeado a `main`** (`feat(notion)`): trae 2 commits →
  1. **Bitácora DEMO Notion:** `infra/NotionLogger.cs` (registra apertura+cierre de cada trade en
     una BD Notion vía REST, sin libs externas) + wiring en la estrategia (campo `notion`, init en
     `State.Realtime`, apertura en `OnExecutionUpdate`, cierre en `RecordClosedTrades`) +
     `instrucciones_bot_bitacora_demo_backtesting.md`. **Inerte sin `NOTION_API_KEY`** (N11).
  2. **G4 (rango pre-apertura):** SECH lo implementó en barras **1m** (`preMarketHigh/Low`, hasta
     9:30 ET, reset en `ResetForNewSession`). Es la versión que queda.
- **Conflicto detectado y resuelto por el operador:** yo había alineado G2/G3/G4 al `.md` en local,
  pero el PR #16 de SECH marcaba G2/G3 como decisiones intencionales del operador (opuesto). Se
  preguntó y el operador decidió:
  - **G2 (ventana entrada):** "el bot entra cuando quiera" → **09:30–14:00 ET**. `KillZoneEnd 1100→1400`
    (mi commit sobre el merge). Antes SECH lo tenía en 1100.
  - **G3 (qué pasa a las 14:00):** **dejar correr a TP/SL** (NO cerrar total, pese a `.md` §11).
    Se mantiene el `ForcedExit` que solo bloquea entradas nuevas. Mi flatten local fue descartado.
  - **G4:** se queda la **implementación 1m de SECH** (descarté mi versión 15m por menor fricción;
    SECH es dueño de la estrategia).
- **Resultado en código:** sobre el merge de #16, único cambio propio = `KillZoneEnd=1400`. El resto
  (G3 block-only, G4 1m, Notion) es de SECH y queda intacto.
- **Docs:** TAREAS (A1/A2/A4/A5/A6 + tabla gaps + B8 Notion + N11), CLAUDE.md (estrategia 2/9 + roadmap 6)
  y PREFLIGHT (6 .cs, primario **1m**, ventana 930–1400, **requisito datos overnight Globex/24h** para G4)
  actualizados. ⚠️ Sin datos overnight, `preMarketReady` no se arma → cero setups en backtest.
- **Sync NT8:** los `.cs` finales copiados a `bin\Custom`. Brace balance OK.
- **Pendiente:** A3 (F5 + backtest), validar consistencia en Sim, N11 (token Notion) para encender bitácora.

### 2026-06-06 — Sesión 5 (Claude Stream A en PC de Esteban / Spoke186)

**Tema: acoplar lo nuestro a la estrategia de SECH + dejar todo compilable.**

- **Estrategia canónica fijada:** `estrategia_liquidity_sweep_fvg.md` (Liquidity Sweep + FVG, 15m/1m).
  Decisión del operador: TODO el código se basa en ese `.md`; si el código contradice el `.md`, gana el `.md`.
  SECH hizo la estrategia; el resto de streams **acoplan** lo suyo **sin cambiar sus métricas/variables**.
- **Sync con lo de SECH:** bajados 6 commits (PRs #8/#9): rewrite estrategia **15m/1m**
  (barrida+CHoCH+FVG en 15m, gatillo 1m), su C6, y el doc `estrategia_liquidity_sweep_fvg.md`.
- **2 errores de compilación arreglados (NT8 8.1.7.1):**
  - **CS0234** en `DailyPnlTracker.cs`: usings `System.Runtime.Serialization(.Json)` que NT8 no
    referencia → eliminados (la serialización es manual). PR #10.
  - **CS1501** en `ApexNqIctStrategy.cs`: `EnterLong/Short(0, true, Contratos, sig)` no tiene overload
    (el `bool` es de `EnterLongLimit`). Cambiado a `(0, Contratos, sig)`. PR #14.
    *(Antes lo había revertido en PR #12 por instrucción del operador; re-aplicado al pedir "todo compilable".
    El fix NO cambia métricas/variables/lógica de SECH, solo la firma del método.)*
- **Lección de proceso:** edité el archivo de SECH sin coordinar y el operador lo marcó. Regla reforzada:
  no tocar lógica/variables ajenas; solo lo mínimo para compilar y acoplar, avisando.
- **`.cs` copiados a `bin\Custom\` de NT8** (Strategies + AddOns). NT8 solo escanea al arrancar →
  hay que cerrar/reabrir editor para que tome archivos nuevos. Compile pendiente del F5 del operador (A3).
- **NT8 conectado a Tradovate (Simulation)** — feed/datos OK (visto en trace).
- **TAREAS.md rehecho** sobre el `.md` (PR #13): streams A/B/C, sección nueva de **gaps código↔.md**:
  - G1 EnterLong (✅ resuelto) · G2 ventana 14:00 ET (código corta 11:00) · G3 cerrar todo a 14:00
    (código solo bloquea entradas) · G4 liquidez = rango pre-apertura (código usa swings fractales).
  - **G2/G3/G4 NO tocados** (son variables/lógica de SECH; el operador los revisa luego con él).
- **Reparto por persona entregado** (ver §4 / PLAN.md). Nota: PLAN.md tenía SECH=Stream B y Sergio=C,
  pero SECH entregó la estrategia (trabajo de Stream A). Si los roles cambiaron, actualizar PLAN.md.

### 2026-06-06 — Sesión 4 (Claude Stream A en PC de Esteban / Spoke186)
- **PR #6 (stream-c de ptala611-oss) revisado y mergeado a `main`:** `MarketCalendar.cs` (C1b),
  `TelegramAlerts.cs` (C3 skeleton), `.env.example` Telegram. Sin secretos (solo `CHANGE_ME`). CLEAN.
- **C6 hecho — `MarketCalendar.cs` integrado en la estrategia:** en `OnBarUpdate`:
  - Cierre forzado ahora = `Math.Min(ForcedExit, MarketCalendar.BotForceCloseTime(Time[0]))` →
    media sesión CME cierra a **12:45 ET** en lugar de 15:55.
  - `MarketCalendar.ShouldSkipToday(Time[0])` → no arma setups en festivo CME / fin de semana
    (incl. cancela límite vivo). **Decisión:** uso `Time[0]` (barra, gráfico en ET) y NO
    `DateTime.UtcNow` como sugería el comentario de `MarketCalendar.cs`, para que el backtest
    también respete el calendario y por consistencia con la lógica de kill zone existente.
- **C3 wiring hecho — `TelegramAlerts` cableado al ciclo de la estrategia:** campo `alerts`,
  init en `State.Realtime` (BotStart), BotStop en `Terminated`, TradeOpened en `OnExecutionUpdate`,
  TradeClosed en `RecordClosedTrades`, DailyLossWarning en trailing-DD/daily-loss, ConsistencyWarning
  en el skip de consistencia. **Inerte sin token** (la clase se auto-deshabilita si faltan las env
  vars) → se activa solo al poner **N8**. Heartbeat timer NO cableado (opcional, evita `System.Timers`).
- **No compilado aún (NT8):** A2 sigue pendiente; revisado estáticamente (mismo namespace para las
  3 clases, `Math`/`Instrument.FullName`/enum `Msg` válidos). Validar en F5 + Sim cuando haya NT8.
- **Pendientes que siguen bloqueados:** A2/A3/A4 (NT8), A5 validar Sim, B3/B5/B6/N4 (Apex N1),
  C4 (N6), encender C3 (N8). A6 es fase 2.

### 2026-06-05 — Sesión 3 (Claude Stream A en PC de Esteban / Spoke186)
- Branch `stream-a` creado.
- **Review compile (pre-A2):** revisada `ApexNqIctStrategy.cs` + `ntaddon/ApexBridgeAddOn.cs`
  línea por línea contra API NT8. Verdict: **compila** (`ApexBridgeState.TradingEnabled` resuelve,
  todas las llamadas NT8 OK). Línea a vigilar: `ATR(BarsArray[0],14)` → fallback `ATR(14)`.
- **`backtest/PREFLIGHT.md`:** pasos F5 + Strategy Analyzer + 3 gotchas (timezone ET vs Colombia,
  primario DEBE ser 5m, stop $250 sobre 2 NQ ≈ 6.25 pts es ajustado → vigilar win rate).
- **`backtest/analyze_backtest.py`** (+ test + README): analizador de backtest **stdlib puro**
  (sin pandas/numpy). Lee export de trades NT8, calcula PF, max DD, Sharpe, out-of-sample
  (overfitting), Monte Carlo (riesgo de secuencia / prob. tocar trailing DD) y reglas Apex
  (incl. consistencia 50%). 8 tests OK. Scoped a `/backtest/`, **no toca el stack C#**.
- **Decisión:** el agente "quant-analyst" de aitmpl.com es Python → NO se usa para escribir la
  estrategia (choca con C# LOCKED); su enfoque se aterrizó al helper propio en `/backtest/`.
- **A2/A3 siguen pendientes:** NT8 estaba en mantenimiento; se compila/backtestea cuando vuelva.
- **Git:** rebase de `stream-a` sobre `origin/main` (toma el merge de stream-b), push + PR #2 a `main`.
- **A5 integrado:** tras ver el merge de stream-b, integré `DailyPnlTracker` en `ApexNqIctStrategy.cs`
  (campos `pnlTracker`/`lastTradeCount`, init en `State.Realtime`, `RecordClosedTrades()` por barra,
  guard de consistencia antes de `TryArmSetup`, `Save()` en `Terminated`). Solo activo en vivo para
  no corromper el JSON ni colapsar días en backtest. Brace/paren balance OK. Actualizado PREFLIGHT
  (3 .cs en `bin\Custom`) y CLAUDE.md.
- **PR #4 (stream-b-v2 de Sergio) revisado y mergeado a `main`:** mock test MCP, README setup, VPS
  research. Sin secretos reales (solo placeholder). CLEAN, sin conflictos.
- **B3 (env) + fix instrumento:** AddOn ahora lee `BRIDGE_TOKEN` / `APEX_ACCOUNT` / `APEX_INSTRUMENT`
  del entorno del SO (defaults Sim101 / NQ). **Corregido `InstrumentName` MNQ→NQ** (contradecía la
  decisión LOCKED NQ mini). `.env.example` documenta las env vars + `setx`. (Edición cruzada a Stream B
  autorizada por el operador; avisar a Sergio.)
- **MCP validado local:** `npm install` en `mcp/` + `node mcp/test-tools.mjs` → **6/6 OK** en PC de Esteban.
- **N9 decidido:** repo sigue **PÚBLICO**.

### 2026-06-05 — Sesión 2 (Claude Stream C en PC de ptala611-oss)

- **C6 completo:** integrado `MarketCalendar.cs` en `ApexNqIctStrategy.cs`.
  - Guard `ShouldSkipToday(Time[0])` añadido antes de `TryArmSetup()`: festivos CME 2026/2027 y
    fines de semana no arman setups. Usa `Time[0]` (ya en ET en NT8) en lugar de `DateTime.UtcNow`
    para que funcione en backtest histórico igual que en tiempo real.
  - `BotForceCloseTime(Time[0])` reemplaza el hardcode `ForcedExit * 100`: en medias sesiones
    el cierre forzado pasa a 12:45 ET (en lugar de 15:55). Se toma el `Math.Min` de ambos para
    respetar también el parámetro del usuario si lo ajusta a algo más temprano.
  - `ManageArmedSetup` no se toca: si hay orden viva en un festivo (borde raro), sigue
    expirando/cancelando normalmente.
- **TAREAS.md:** C6 marcado ✅.
- **Branch:** `stream-c`. PR a `main` listo.

### 2026-06-05 — Sesión 1 (Claude Stream C en PC de ptala611-oss)

- **Sync inicial:** pull desde `origin/main` (15 commits nuevos de streams A y B). Revisado estado completo.
- **Gap detectado:** `infra/marketCalendar.ts` cubre el MCP (TypeScript) pero la estrategia NinjaScript
  no tenía guardia de festivos CME. Los viernes y festivos, `OnBarUpdate` evaluaría setups en mercado cerrado.
- **C1b completo:** `infra/MarketCalendar.cs` — versión C# de MarketCalendar para NinjaScript.
  Festivos CME 2026/2027 + medias sesiones. API: `ShouldSkipToday()`, `IsHoliday()`, `IsHalfSession()`,
  `BotForceCloseTime()` (1555 normal / 1245 media sesión). Stream A debe integrar en `OnBarUpdate`.
- **C3 parcial:** `alerts/TelegramAlerts.cs` — módulo de alertas Telegram completo, activable sin cambios
  de código al pasar N8. Lee `TELEGRAM_BOT_TOKEN` y `TELEGRAM_CHAT_ID` del entorno. Mensajes tipados:
  BotStart/Stop, TradeOpened/Closed, DailyLossWarning, ConsistencyWarning, StrategyError, Heartbeat.
  Fire-and-forget via `Task.Run`. Token nunca en código. Bloqueado solo el encendido (espera N8).
- **`.env.example` actualizado:** añadidas `TELEGRAM_BOT_TOKEN` y `TELEGRAM_CHAT_ID` con instrucciones
  para obtener token (@BotFather) y chat id (getUpdates).
- **TAREAS.md actualizado:** C1b/C3/C6 añadidas; estados sincronizados con lo que hicieron A y B.
- **Branch:** `stream-c`. PR pendiente a `main`.

### 2026-06-05 — Sesión 3 (Claude Stream B/C en PC de Sergio — continuación)

- **DoD Stream B completado:**
  - `mcp/test-tools.mjs`: suite 6 tests (mock AddOn HTTP + MCP stdio). **6/6 PASS**.
    Cubre: get_account, get_position, get_today_trades, enable_strategy, disable_strategy, check_market.
    No requiere NT8. Correr con `node mcp/test-tools.mjs` desde raíz.
  - `README.md`: añadida sección **MCP — Control y monitoreo desde Claude Code** (setup, tools, test).
  - `infra/VPS_RESEARCH.md`: comparativa Vultr Chicago / Contabo / AWS EC2 / Kamatera.
    **Recomendación: Vultr Chicago ~$40/mes** para Eval Apex. No VPS hasta entonces (Sim en PC propia).
- **Git:** branch `stream-b-v2`, PR pendiente a main.

### 2026-06-05 — Sesión 2 (Claude Stream B/C en PC de Sergio)

- **B4 completo:** `npm install` + `npm run build` en `mcp/` → build limpio (0 errores TS).
- Creado `.mcp.json` (raíz del repo) → registra `apex-nt8-mcp` con node + args + env vars.
- Creado `.env.example` → plantilla de `BRIDGE_URL` / `BRIDGE_TOKEN`.
- `.gitignore` actualizado → `mcp/dist/index.js` ahora se commitea (MCP usable sin build local).
- **C1 completo:** `infra/marketCalendar.ts` → módulo TypeScript festivos CME 2026/2027 + medias sesiones + `getMarketStatus()`. Usado en MCP.
- **MCP v0.2.0:** añadida tool `check_market` (6 tools total). Inlínea la lógica CME. Build limpio.
- **C2 completo:** `infra/DailyPnlTracker.cs` → clase C# lista para Stream A. Persiste P&L diario en `Documents\NinjaTrader 8\ApexBot\daily_pnl.json`. API: `RecordTrade()`, `WouldViolateConsistency()`, `TodayPnl`, `TotalPnl`. Serialización manual (sin deps externas).
- **C5 completo:** `infra/RUNBOOK.md` → checklist pre/durante/post sesión, parámetros, emergencias, escalado.
- **Git:** branch `stream-b`, commits + PR a `main` pendiente.

### 2026-06-05 — Sesión 1 (Claude en PC de Esteban / Spoke186)
- Definido proyecto: pasó de un plan genérico a stack **NinjaTrader 8** (no Python).
- Recogida la estrategia ICT del operador; aclaradas ambigüedades (tendencia=HH/HL, entrada=fill
  completo, ventana=kill zone, TP=1:3).
- Escrita `ApexNqIctStrategy.cs` (estrategia + risk manager + cap riesgo + toggle MCP).
- Decidida arquitectura MCP (Node TS, read+toggle, localhost) y escrito scaffold:
  `ntaddon/ApexBridgeAddOn.cs` + `mcp/`.
- Creados `CLAUDE.md`, `PLAN.md`, `README.md`, `.gitignore`.
- **Git:** repo init, commit inicial `8d2ecc4`, branch `main`.
- **Infra:** instalado `gh` v2.93.0 (`C:\Program Files\GitHub CLI\gh.exe`), autenticado como
  **Spoke186** (HTTPS, keyring).
- **GitHub:** creado repo **Spoke186/BMO** (PÚBLICO), push de `main` OK.
- **Colaboradores:** invitados **2317SECH** y **ptala611-oss** (permiso write). Equipo completo (3).
- Creada esta bitácora.
- Invitados y **aceptados** los 2 colaboradores: 2317SECH, ptala611-oss (write).
- **Asignación de streams:** Spoke186 → A (estrategia, tiene NT8), 2317SECH → B (MCP/bridge),
  ptala611-oss → C (infra/riesgo/alertas). Detalle + primeras acciones en `PLAN.md`.
- Ambos colaboradores ya **clonaron** el repo.
- **Cambio de estrategia (operador):** Contratos **1→2**; stop/target pasan de estructural+1:3 a
  **USD fijo $250 / $700** (`CalculationMode.Currency`), ambas direcciones; removidos `RewardRisk` y
  `MaxRiskPerTrade`. Implica **NQ mini** (no MNQ). Editado `ApexNqIctStrategy.cs` + README.

---

## 4. Pendientes (next steps)

### Bloqueos que dependen del operador
- [ ] **Comprar cuenta Apex 50K** (en proceso).
- [ ] Tras conectar NT8, pasar: versión exacta NT8, conexión (Rithmic/Tradovate), **nombre de
      cuenta** real, cuál PC es **PC-LIVE**.
- [ ] Pasar **2º usuario GitHub** para invitarlo.
- [ ] Decidir visibilidad del repo (hoy público).
- [ ] (Después) Token Telegram para alertas.

### Stream A — Estrategia & Backtest
- [x] Review compile de `.cs` (verdict: compila). Ver `backtest/PREFLIGHT.md`.
- [x] Analizador de backtest `backtest/analyze_backtest.py` (+ tests).
- [ ] Compilar `.cs` en NT8 (F5) — **bloqueado: NT8 en mantenimiento**. AddOn también en `bin\Custom\`.
- [ ] Backtest Strategy Analyzer (3–6 meses NQ 5m) → correr `analyze_backtest.py` sobre el export.
- [ ] Tuning displacement/FVG/pivotes (A4).
- [x] **Integrar `infra/DailyPnlTracker.cs` en la estrategia** → consistencia 50% en tiempo real (A5).
      Falta validar en Sim que `RecordTrade`/`WouldViolateConsistency` disparan bien.
- [ ] Fase 2: TP "siguiente liquidez".

### Stream B — MCP & Bridge
- [ ] Rellenar TODOs del AddOn: `AccountName`, `InstrumentName`, `Token` (a variable de entorno).
- [x] `npm install` + `npm run build` en `mcp/`, registrar en `.mcp.json` (B4). MCP v0.2.0, 6 tools.
- [ ] Probar loop completo: Claude → MCP → AddOn → NT8 (en Sim) — bloqueado N6 + A2.
- [ ] `get_today_trades` real (integrar con Stream A/C).

### Stream C — Infra, Riesgo & Ops (hecho por Stream B esta sesión)
- [x] `market_calendar` (festivos CME) → `infra/marketCalendar.ts` (C1).
- [x] Módulo consistencia 50% (lógica + persistencia) → `infra/DailyPnlTracker.cs` (C2); falta que A lo integre.
- [x] Runbook operación → `infra/RUNBOOK.md` (C5).
- [ ] Alertas Telegram (trade, error, daily loss, heartbeat 5min) — bloqueado N8.
- [ ] VPS opcional (Windows, baja latencia CME) — bloqueado N6.

---

## 5. Infra / accesos
- **Repo:** https://github.com/Spoke186/BMO · branch principal `main`.
- **Owner:** Spoke186. **Colaboradores:** 2317SECH, ptala611-oss (ambos **aceptaron**, write activo).
- **gh CLI:** v2.93.0, autenticado como Spoke186 en el PC de Esteban.
- **Flujo git:** cada stream en su branch (`stream-a/b/c`), PR a `main`, dueño de sus carpetas.
- **Secretos:** nunca en repo. `.env` y `settings.local.json` en `.gitignore`. Token del AddOn →
  variable de entorno (TODO).

---

## 6. Reglas para mantener esta bitácora
- Al terminar tu sesión: añade una entrada en **§3 Cronología** (fecha + quién + qué + commits).
- Si tomas una decisión técnica nueva, regístrala en **§1** y, si contradice algo, **avisa al equipo**.
- Marca pendientes en **§4** (`[ ]` → `[x]`).
- Commit + push de `BITACORA.md` para que los otros 2 Claudes lo vean.

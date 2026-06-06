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
| Mercado / contrato | NQ o **MNQ**. **Recomendado empezar 1 MNQ** (micro) por riesgo en 50K |
| Timeframes | Tendencia 15m · ejecución 5m |
| Tendencia (sesgo) | Estructura **HH/HL** por pivotes fractales (fuerza 3 en 15m) |
| Entrada | Sweep contra-tendencia → displacement (proxy ATR ≥1.5×) → **FVG** → límite en **fill completo** del gap, a favor de tendencia |
| Stop | Detrás del extremo del sweep + 2 ticks |
| Take profit | **1:3 RR** fijo |
| Ventana | **NY Kill Zone 8:30–11:00 ET**, cierre forzado 15:55 ET, **1 setup/día** |
| Plan Apex | 50K · Trailing DD $2,500 · Profit goal $3,000 · Daily loss propio $400 · Cap riesgo/trade $250 |
| Consistencia | 50% lun–vie (ningún día > 50% del profit acumulado). **No en código aún** (manual) |
| MCP | **Node + TypeScript** · alcance **solo lectura + enable/disable** · corre en **localhost** |
| AddOn | C# dentro de NT8, HTTP en `127.0.0.1:8731`, auth por token, toggle `ApexBridgeState` |

### Nota de riesgo (corrección del operador)
NQ mini NO "se quema en un trade" si el stop se controla: con 1 trade/día, stop ~12.5 pts = **$250**
(< daily loss $400); si gana 1:3 ≈ **$700–750**. Por eso se añadió `MaxRiskPerTrade` ($250): el bot
**salta el setup** si el stop estructural implica más USD que ese tope.

---

## 2. Qué existe en el repo (archivos)

| Archivo | Stream | Estado |
|---------|--------|--------|
| `ApexNqIctStrategy.cs` | A | Estrategia ICT completa + guardas Apex + toggle MCP. 🚧 sin compilar/backtest |
| `ntaddon/ApexBridgeAddOn.cs` | B | AddOn HTTP (account/position/trades/enable/disable). 🚧 sin compilar/probar |
| `mcp/` (package.json, tsconfig, src/index.ts, README) | B | MCP server Node TS, 5 tools. 🚧 sin probar |
| `CLAUDE.md` | — | Contexto del proyecto |
| `PLAN.md` | — | División en 3 streams + coordinación git |
| `README.md` | — | Instalación NT8, params, reglas Apex |
| `BITACORA.md` | — | Este archivo |
| `.gitignore` | — | Secretos/build/node ignorados |

### Guardas Apex en el código (estado)
- ✅ Stop obligatorio · ✅ No DCA · ✅ 1 entrada/setup · ✅ Ventana horaria · ✅ Max daily loss
- ✅ Cap riesgo/trade · ⚠️ Trailing DD = **proxy local** (Apex manda) · ❌ Consistencia 50% (manual)

---

## 3. Cronología

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
- [ ] Compilar `.cs` en NT8 (F5). Recordar: el AddOn debe estar también en `bin\Custom\` (la
      estrategia referencia `ApexBridgeState`).
- [ ] Backtest Strategy Analyzer (3–6 meses NQ 5m), tuning de displacement/FVG/pivotes.
- [ ] Implementar consistencia 50% lun–vie con persistencia P&L entre días.
- [ ] Fase 2: TP "siguiente liquidez".

### Stream B — MCP & Bridge
- [ ] Rellenar TODOs del AddOn: `AccountName`, `InstrumentName`, `Token` (a variable de entorno).
- [ ] `npm install` + `npm run build` en `mcp/`, registrar en `.mcp.json`.
- [ ] Probar loop completo: Claude → MCP → AddOn → NT8 (en Sim).
- [ ] `get_today_trades` real (integrar con Stream A/C).

### Stream C — Infra, Riesgo & Ops
- [ ] `market_calendar` (festivos CME).
- [ ] Módulo consistencia 50% (lógica + persistencia) para que A lo integre.
- [ ] Alertas Telegram (trade, error, daily loss, heartbeat 5min).
- [ ] VPS opcional (Windows, baja latencia CME) + runbook operación.

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

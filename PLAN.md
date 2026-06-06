# PLAN.md — Proyecto Bot ICT NQ/MNQ + MCP (3 Claude Codes)

> Proyecto trabajado por **3 Claude Code en 3 PCs**. Este archivo coordina el trabajo.
> Cada Claude lee `CLAUDE.md` (contexto) + este `PLAN.md` (división y estado) antes de tocar nada.

## Goal
1. Bot ICT corriendo en **NinjaTrader 8** sobre cuenta **Apex 50K**.
2. **MCP** de control/monitoreo (no ejecuta la estrategia; la supervisa).
3. Validado: Backtest → Sim → Evaluación Apex → Fondeada.

---

## Coordinación entre los 3 Claudes

### Repo compartido (obligatorio)
- Un repo git en GitHub. Cada PC hace `clone`.
- `main` protegido. Cada stream trabaja en su **branch**: `stream-a`, `stream-b`, `stream-c`.
- PR a `main` cuando un módulo esté estable. Avisar en `PLAN.md` (sección Estado) al mergear.
- **Regla anti-conflicto:** cada stream es dueño de SUS carpetas (abajo). No editar carpeta ajena sin avisar.

### Identificación de la cuenta live
- **Solo UN PC** corre NinjaTrader + Apex en vivo (PC-LIVE). Los otros 2 son desarrollo.
- El bot (`.cs`) y el AddOn corren en PC-LIVE. El MCP puede correr en PC-LIVE (localhost) o remoto (LAN/VPN).

---

## Asignación de contribuidores

| Persona (GitHub) | Stream | Por qué | Branch |
|------------------|--------|---------|--------|
| **Spoke186** (Esteban, operador) | **A — Estrategia & Backtest** | Tiene NT8, conoce la estrategia, aporta datos Apex y el "ojo" para tunear | `stream-a` |
| **2317SECH** | **B — MCP & Bridge** | Trabajo de dev (C# AddOn + Node TS MCP), arranca sin Apex | `stream-b` |
| **ptala611-oss** | **C — Infra, Riesgo & Ops** | Calendar CME, consistencia, alertas, runbook — independiente de Apex | `stream-c` |

> La asignación es por encaje lógico. Si las habilidades reales difieren, **B y C se pueden
> intercambiar** sin problema. **A debe quedarse con quien tenga NinjaTrader 8 instalado.**
> Cada persona le dice a SU Claude: *"soy stream-X, lee CLAUDE.md + PLAN.md + BITACORA.md + TAREAS.md"*.

### Sprint 1 — tareas actuales por persona

> Instrumento decidido: **NQ mini** (el bracket USD fijo $250/$700 lo implica). Ambos colaboradores
> ya clonaron. Marca `[x]` al terminar y commitea. DoD = "Definition of Done".

**Spoke186 (Stream A) — necesita NinjaTrader; aporta inputs Apex**
- [ ] Comprar **Apex 50K** (N1).
- [ ] Pasar al equipo: versión NT8 (N2), conexión Rithmic/Tradovate (N3), PC-LIVE (N6), visibilidad repo (N9).
- [ ] Poner `ApexNqIctStrategy.cs` **y** `ntaddon/ApexBridgeAddOn.cs` en `bin\Custom\`, **compilar** (A2).
- [ ] Gráfico **NQ 5m** + aplicar la estrategia. Verificar params (2 contratos, $250/$700, kill zone).
- [ ] Backtest Strategy Analyzer 3–6 meses NQ 5m (A3).
- DoD: compila sin errores + 1 backtest con métricas (win rate, profit factor, max DD) en el chat/repo.

**2317SECH (Stream B — MCP) — arranca YA, sin Apex**
- [ ] `git checkout -b stream-b`.
- [ ] `cd mcp` → `npm install` → `npm run build` (B4). Que compile sin errores TS.
- [ ] Mover el `Token` del AddOn y del MCP a **variable de entorno** (quitar hardcode).
- [ ] Probar el MCP contra un **mock HTTP** local (simular el AddOn) para validar los 5 tools.
- [ ] Preparar snippet de registro en `.mcp.json`.
- Bloqueado hasta datos: `AccountName`/símbolo reales (B3, espera N3/N4), prueba loop en NT8 (B5, espera N6 + A2).
- DoD: `npm run build` OK + 5 tools responden contra el mock + README de cómo correrlo.

**ptala611-oss (Stream C — Infra/Riesgo) — arranca YA, sin Apex**
- [ ] `git checkout -b stream-c`.
- [ ] **C1** `market_calendar`: lista de festivos/medias sesiones CME + helper `is_trading_day(date)`. Definir si vive como archivo de datos que lee la estrategia o módulo aparte.
- [ ] **C2** consistencia 50% lun–vie: diseñar persistencia (ej JSON de P&L por día) + lógica `would_violate_consistency(profit_hoy)`. Entregable para que A lo integre en el `.cs`.
- [ ] **C5** runbook: arranque diario, qué hacer si el bot cae, checklist pre-mercado.
- Bloqueado hasta datos: C3 Telegram (N8), C4 VPS (N6).
- DoD: C1 y C2 con tests básicos + C5 escrito, todo en `/infra` o `/utils`.

---

## División del trabajo

### Stream A — Estrategia & Backtest
**Dueño de:** `ApexNqIctStrategy.cs`, `/backtest/`
- [x] Estrategia base ICT (sweep → displacement → FVG → fill completo, 1:3, kill zone).
- [x] Guardas riesgo: stop obligatorio, no DCA, daily loss, cap riesgo/trade, proxy trailing DD.
- [ ] Compilar en NT8 (F5), corregir errores de compilación.
- [ ] Backtest Strategy Analyzer (3–6 meses NQ 5m).
- [ ] Tuning: displacement mult, FVG mínimo, pivotes → que ≈ ojo humano.
- [ ] Persistencia P&L entre días → **regla consistencia 50% lun-vie** real (coordinar con C).
- [ ] Fase 2: TP "siguiente liquidez".

### Stream B — MCP & Bridge
**Dueño de:** `/mcp/`, `/ntaddon/`
**Decisiones locked:** MCP en **Node + TypeScript** · alcance **solo lectura + enable/disable**
(NO order placement) · corre en **localhost** (mismo PC que NT8, `127.0.0.1`).
- [ ] **NT8 AddOn (C#):** mini servidor HTTP/WS dentro de NinjaTrader. Endpoints:
      `GET /account`, `GET /position`, `GET /trades/today`, `POST /strategy/enable`, `POST /strategy/disable`.
- [ ] **MCP server** (Node TS o Python): envuelve el AddOn. Tools expuestos a Claude:
      `get_account`, `get_position`, `get_today_trades`, `enable_strategy`, `disable_strategy`, `run_backtest` (fase 2).
- [ ] Auth entre MCP y AddOn (token local). Si remoto: TLS/VPN.
- [ ] **NO** exponer `place_order` discrecional al LLM en cuenta fondeada (ver Seguridad).
- [ ] Registrar el MCP en los `.mcp.json` de los 3 Claudes.

### Stream C — Infra, Riesgo & Ops
**Dueño de:** `/infra/`, `/utils/`, `/alerts/`
- [ ] `market_calendar`: festivos CME, medias sesiones → no operar.
- [ ] Módulo consistencia 50% (lógica + formato persistencia) que A integra en el `.cs`.
- [ ] Alertas **Telegram**: trade abierto/cerrado, error, daily loss alcanzado, heartbeat cada 5min.
- [ ] VPS (opcional): Windows, baja latencia a CME (Chicago/NY) si no corres en PC propio.
- [ ] Runbook de operación: arranque diario, qué hacer si el bot cae, checklist pre-mercado.

---

## Dependencias (orden importa)
```
A (estrategia base) ──► B necesita hooks enable/disable que A expone
C (consistencia)    ──► A integra el módulo en el .cs
C (market calendar) ──► A consulta antes de armar setup
B (AddOn estado)    ◄── lee estado que A publica (posicion, P&L, trades hoy)
```
Arrancar en paralelo: A compila+backtest, B scaffold AddOn+MCP, C calendar+alertas. Integrar en semana 2.

---

## Seguridad (no negociable)
- API keys / tokens: variables de entorno, NUNCA en código. `.env` en `.gitignore`.
- El LLM (MCP) **no coloca órdenes discrecionales** en cuenta fondeada. Solo: leer estado + enable/disable.
- Nunca primer run en cuenta fondeada. Backtest → Sim → Evaluación → Fondeada.
- AddOn server: bind a `127.0.0.1` por defecto. Exponer a LAN solo con token + IP allowlist.

---

## Estado global (actualizar al mergear)
| Stream | Branch | Último hito | Estado |
|--------|--------|-------------|--------|
| A (Spoke186) | stream-a | Estrategia base + cap riesgo + toggle MCP | 🚧 falta compilar/backtest |
| B (2317SECH) | stream-b | Scaffold AddOn C# + MCP Node TS escritos | 🚧 falta datos Apex + compilar/probar |
| C (ptala611-oss) | stream-c | — | ⬜ no iniciado |

> **Integración A↔B:** `ApexNqIctStrategy.cs` referencia `ApexBridgeState` (en el AddOn).
> Ambos `.cs` deben estar en `bin\Custom\` antes de compilar, o falla por símbolo faltante.

---

## Timeline estimado
Ver sección en la respuesta del chat. Resumen: **Sim ~1–2 semanas**, **Evaluación Apex ~2–3 semanas**
(code-ready), **Fondeada = depende de performance, no de código.**

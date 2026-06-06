# ApexNqIctStrategy — Bot ICT para NQ/MNQ (NinjaTrader 8)

Estrategia automatizada NinjaScript para NinjaTrader 8 bajo reglas **Apex Trader Funding**.
Lógica ICT (**15m** sesgo/FVG, **1m** gatillo): en tendencia, **barrida del rango pre-apertura**
contra-tendencia → **CHoCH + desplazamiento** (cuerpo ≥1.5×ATR) en 15m → **FVG** en el impulso →
retroceso al FVG + **confirmación en 1m** (rechazo o mini-CHoCH) → entrada a **mercado** al cierre 1m,
a favor de la tendencia. **Bracket fijo en USD: stop $250 / target $700**, ambas direcciones, 2 contratos.

---

## Instalación

1. Copiar `ApexNqIctStrategy.cs` a:
   ```
   Documents\NinjaTrader 8\bin\Custom\Strategies\
   ```
2. En NinjaTrader: **New → NinjaScript Editor**, abrir el archivo, **Compile** (F5).
3. Abrir un **gráfico de NQ (o MNQ) en 1 minuto**, con sesión **Globex/24h (ETH)** — requiere datos overnight.
4. **Strategies** (clic derecho en gráfico → Strategies) → añadir `ApexNqIctStrategy`.
5. Ajustar parámetros (abajo) → Enable.

> La estrategia añade sola la serie de **15m** (sesgo/barrida/CHoCH/FVG). Aplícala SIEMPRE sobre la
> primaria **1m**. ⚠️ Sin datos overnight (gráfico RTH-only) el rango pre-apertura no se arma → **cero setups**.

---

## Parámetros

| Grupo | Param | Default | Nota |
|-------|-------|---------|------|
| Tamaño | Contratos | 2 | bracket USD implica **NQ mini** (ver abajo) |
| Estrategia | Stop loss fijo (USD) | 250 | `CalculationMode.Currency`, pérdida total de la posición |
| Estrategia | Target fijo (USD) | 700 | ganancia total objetivo (~1:2.8) |
| Estrategia | Pivote tendencia 15m | 3 | velas a cada lado (fractal) |
| Estrategia | Pivote mini-CHoCH 1m | 2 | velas a cada lado, para la confirmación 1m |
| Estrategia | Displacement ATR mult | 1.5 | cuerpo ≥ 1.5×ATR(14) = "institucional" |
| Estrategia | FVG mínimo (pts) | 6.0 | filtra gaps de ruido (velas 15m) |
| Estrategia | Rechazo 1m (mecha/cuerpo) | 1.5 | ratio de la vela de confirmación 1m |
| Estrategia | Máx barras 1m retroceso FVG | 60 | expira el setup si el precio no vuelve (~1h) |
| Estrategia | Máx barras 15m CHoCH tras barrida | 4 | ventana para ver el CHoCH |
| Estrategia | Buffer invalidación sweep (ticks) | 2 | cancela el setup si se rompe el extremo |
| Horario | Kill zone inicio/fin | 930 / 1400 | ET (ventana de ejecución `.md` §3) |
| Horario | Cierre forzado | 1400 | ET — deja de ABRIR; posición abierta corre a TP/SL (G3) |
| Riesgo | Balance inicial | 50000 | para proxy trailing DD |
| Riesgo | Trailing drawdown | 2500 | Apex 50K |
| Riesgo | Max daily loss | 400 | tu límite propio |

**Horario:** NinjaTrader usa la zona del bróker/PC. Si tu gráfico ya está en ET, los valores
`830/1100/1555` van directos. Si tu PC está en hora Colombia, **es la misma hora** (UTC-5 todo
el año) — pero confirma que la sesión del instrumento (Trading Hours template) sea `CME US Index
Futures ETH` y que el reloj del gráfico sea ET. Si difiere, ajusta los `HHmm`.

---

## Contrato: el bracket USD fija NQ mini (importante)

- **NQ** (mini): 1 pt = $20, 1 tick (0.25) = $5.
- **MNQ** (micro): 1 pt = $2, 1 tick = $0.50.

Decisión del operador: **2 contratos, stop $250 / target $700 fijos** (`CalculationMode.Currency`).
Esos dólares implican **NQ mini**:
- **2 NQ** → $40/pt → stop ≈ **6.25 pts**, target ≈ **17.5 pts**. Razonable en kill zone.
- **2 MNQ** → $4/pt → stop ≈ 62.5 pts, target ≈ 175 pts. **Irreal** para el bracket. No usar MNQ con estos USD.

Riesgo vs Apex 50K: un stop = **−$250** (< daily loss $400) y queda lejos del trailing DD $2,500.
⚠️ 2 NQ es tamaño real; valida en Sim/Evaluación antes de fondeada.

---

## Reglas Apex — qué cubre el código y qué NO

| Regla Apex | Estado en el bot |
|-----------|------------------|
| Stop obligatorio | ✅ Adjunto por precio en cada entrada (`SetStopLoss`) |
| No DCA / 1 entrada por setup | ✅ `tradedToday` + `EntriesPerDirection=1`, sin añadir |
| Ventana horaria | ✅ Kill zone + cierre forzado |
| Max daily loss | ✅ Sobre P&L realizado de la sesión |
| Trailing drawdown | ⚠️ **Aproximación** (high-water local). Apex lo calcula en su server con su métrica de equity intradía. Úsalo como red de seguridad, NO como verdad. |
| Consistencia 50% | ⚠️ **Implementada en tiempo real** (`infra/DailyPnlTracker.cs`, persiste JSON): salta el setup si ganarlo violaría la regla. Solo activa en Sim/live; en backtest se valida con `analyze_backtest.py`. Apex sigue siendo la verdad oficial. |
| No HFT | ✅ Por diseño (1 trade/día, `OnBarClose`, gatillo 1m) |

---

## Limitaciones honestas

- **ICT es discrecional; esto es una aproximación.** "Desplazamiento institucional" = proxy ATR.
  El backtest se parecerá a tu ojo pero NO será idéntico. Espera divergencia.
- **Sweep + FVG simplificados:** sweep = perforar el **rango pre-apertura** (high/low hasta 9:30 ET) y recuperar; FVG = gap de
  3 velas estándar. No modela order blocks, breaker blocks ni multi-timeframe profundo.
- **TP fijo 1:3**, no "siguiente zona de liquidez" (eso es fase 2).
- **Consistencia 50% y trailing DD reales** los gobierna Apex, no este código.

---

## Flujo de validación (NO saltarse)

1. **Backtest** (Strategy Analyzer) sobre 3–6 meses de NQ **1m** (sesión Globex/24h). Revisar win rate, RR real, max DD.
2. **Tunear** displacement mult, FVG mínimo, pivotes hasta que el comportamiento ≈ tu manual.
3. **Sim / Playback** en cuenta demo, varias sesiones en vivo simuladas.
4. **Cuenta de EVALUACIÓN Apex** — nunca la fondeada primero.
5. Solo tras evaluación estable → cuenta fondeada (2 NQ, el tamaño LOCKED).

> ⚠️ **Nunca correr esto por primera vez en cuenta fondeada.**

---

## Próximos pasos (roadmap)

1. Backtest + tuning de parámetros.
2. Persistencia de P&L acumulado → implementar regla consistencia 50% en código.
3. TP "siguiente liquidez" (fase 2).
4. Alertas Telegram (vía `AddOn` o webhook externo).
5. Multi-timeframe / order blocks si el backtest lo justifica.

---

## MCP — Control y monitoreo desde Claude Code

El servidor MCP (`apex-nt8-mcp`) permite consultar el estado del bot y habilitarlo/deshabilitarlo
desde Claude Code sin tocar NinjaTrader manualmente.

### Requisitos

- Node.js LTS (18+)
- `ntaddon/ApexBridgeAddOn.cs` compilado y corriendo en NT8 (ver Instalación)

### Setup (una sola vez por PC)

**1. Copiar variables de entorno**
```bash
cp .env.example .env
# Editar .env: cambiar BRIDGE_TOKEN por un string secreto propio
```

> El mismo token debe estar en `ntaddon/ApexBridgeAddOn.cs` (campo `Token`). Cámbialo antes del
> primer uso real. No subas `.env` al repo.

**2. Instalar dependencias y compilar**
```bash
cd mcp
npm install
npm run build
```

> `mcp/dist/index.js` ya está commiteado — si no quieres instalar Node, puedes saltar este paso
> mientras no modifiques `mcp/src/index.ts`.

**3. Registrar en Claude Code**

El archivo `.mcp.json` en la raíz ya registra el servidor. Claude Code lo detecta automáticamente
al abrir el proyecto. Verifica que esté activo:

```
/mcp
```

Deberías ver `apex-nt8-mcp` en la lista con los 6 tools disponibles.

### Tools disponibles

| Tool | Qué hace |
|------|----------|
| `get_account` | Balance, P&L realizado/no realizado, trading enabled |
| `get_position` | Posición actual (FLAT/LONG/SHORT, contratos, precio) |
| `get_today_trades` | Trades del día (placeholder hasta integrar con Stream A) |
| `enable_strategy` | Habilita el bot → puede armar nuevos setups |
| `disable_strategy` | Apaga el bot → no arma setups (no cierra posición abierta) |
| `check_market` | ¿Es hoy día hábil CME? Festivos, medias sesiones, kill zone |

### Verificar que funciona (sin NT8)

```bash
# Desde la raiz del repo:
node mcp/test-tools.mjs
```

Levanta un mock del AddOn y prueba los 6 tools. Salida esperada: `6/6 tests OK`.

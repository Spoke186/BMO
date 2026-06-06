# ApexNqIctStrategy — Bot ICT para NQ/MNQ (NinjaTrader 8)

Estrategia automatizada NinjaScript para NinjaTrader 8 bajo reglas **Apex Trader Funding**.
Lógica ICT: en tendencia, esperar **barrida de liquidez** contra-tendencia → **desplazamiento**
(proxy ATR) → **FVG** → entrada por límite en el **fill completo** del gap, a favor de la
tendencia principal. **Bracket fijo en USD: stop $250 / target $700**, ambas direcciones, 2 contratos.

---

## Instalación

1. Copiar `ApexNqIctStrategy.cs` a:
   ```
   Documents\NinjaTrader 8\bin\Custom\Strategies\
   ```
2. En NinjaTrader: **New → NinjaScript Editor**, abrir el archivo, **Compile** (F5).
3. Abrir un **gráfico de NQ (o MNQ) en 5 minutos**.
4. **Strategies** (clic derecho en gráfico → Strategies) → añadir `ApexNqIctStrategy`.
5. Ajustar parámetros (abajo) → Enable.

> La estrategia añade sola la serie de **15m** para el sesgo. Aplícala SIEMPRE sobre 5m.

---

## Parámetros

| Grupo | Param | Default | Nota |
|-------|-------|---------|------|
| Tamaño | Contratos | 2 | bracket USD implica **NQ mini** (ver abajo) |
| Estrategia | Stop loss fijo (USD) | 250 | `CalculationMode.Currency`, pérdida total de la posición |
| Estrategia | Target fijo (USD) | 700 | ganancia total objetivo (~1:2.8) |
| Estrategia | Pivote tendencia 15m | 3 | velas a cada lado (fractal) |
| Estrategia | Pivote liquidez 5m | 2 | detecta swing highs/lows barridos |
| Estrategia | Displacement ATR mult | 1.5 | cuerpo ≥ 1.5×ATR(14) = "institucional" |
| Estrategia | FVG mínimo (pts) | 3.0 | filtra gaps de ruido |
| Estrategia | Buffer stop (ticks) | 2 | detrás del extremo del sweep |
| Estrategia | Velas máx retroceso FVG | 12 | expira el límite si no llena |
| Horario | Kill zone inicio/fin | 830 / 1100 | ET (NY open) |
| Horario | Cierre forzado | 1555 | ET, aplana antes del cierre |
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
| Consistencia 50% | ❌ **No implementada en código.** Necesita P&L acumulado entre días (persistencia). Vigílala manual: ningún día > 50% de tu profit acumulado. |
| No HFT | ✅ Por diseño (1 trade/día, OnBarClose 5m) |

---

## Limitaciones honestas

- **ICT es discrecional; esto es una aproximación.** "Desplazamiento institucional" = proxy ATR.
  El backtest se parecerá a tu ojo pero NO será idéntico. Espera divergencia.
- **Sweep + FVG simplificados:** sweep = perforar el último swing de 5m y recuperar; FVG = gap de
  3 velas estándar. No modela order blocks, breaker blocks ni multi-timeframe profundo.
- **TP fijo 1:3**, no "siguiente zona de liquidez" (eso es fase 2).
- **Consistencia 50% y trailing DD reales** los gobierna Apex, no este código.

---

## Flujo de validación (NO saltarse)

1. **Backtest** (Strategy Analyzer) sobre 3–6 meses de NQ 5m. Revisar win rate, RR real, max DD.
2. **Tunear** displacement mult, FVG mínimo, pivotes hasta que el comportamiento ≈ tu manual.
3. **Sim / Playback** en cuenta demo, varias sesiones en vivo simuladas.
4. **Cuenta de EVALUACIÓN Apex** — nunca la fondeada primero.
5. Solo tras evaluación estable → cuenta fondeada, 1 MNQ.

> ⚠️ **Nunca correr esto por primera vez en cuenta fondeada.**

---

## Próximos pasos (roadmap)

1. Backtest + tuning de parámetros.
2. Persistencia de P&L acumulado → implementar regla consistencia 50% en código.
3. TP "siguiente liquidez" (fase 2).
4. Alertas Telegram (vía `AddOn` o webhook externo).
5. Multi-timeframe / order blocks si el backtest lo justifica.

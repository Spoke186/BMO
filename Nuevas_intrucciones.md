# Instrucciones para Claude Code — Hipótesis "TP Parcial" (Opción 5)

> Fecha: 2026-06-12. Origen: sesión Esteban + Claude (claude.ai).
> Lee `CLAUDE.md` del repo BMO antes de tocar nada. Estas instrucciones lo complementan, no lo reemplazan.

## Contexto en 5 líneas

El primer trade Sim (hoy) llegó a ~+$450 flotantes y terminó en SL (−$379). Queremos probar como
**hipótesis** (no como cambio directo) un mecanismo de TP parcial: asegurar parte de la ganancia
cuando el trade camina, dejando un runner para preservar el edge SESSION_CLOSE (n=112, WR=68.8%,
PF=5.13, Net=+$20,559 en 5 períodos). Motivación adicional: Monte Carlo actual da ~57% de
probabilidad de quemar el trailing Apex de $2,500 con TP=1050/SL=375.

## Regla de oro (vigente, innegociable)

La hipótesis se **aprueba o se descarta** con backtest en los 5 períodos (ene-jul 2024,
jul-sep 2025, oct-dic 2025, ene-mar 2026, abr-jun 2026). Criterios de decisión en la sección 6,
escritos ANTES de ver resultados. Si no pasa, muere — no se "ajusta el gatillo a ver si ahora sí".

## 0. Checkpoint previo (hacer PRIMERO)

1. `git push origin main --tags` — los commits `1bbf025` y `4053291` y el tag
   `checkpoint-sim-2026-06-12` solo existen localmente. GitHub está en `db518d5`.
2. Crear rama de trabajo: `git checkout -b research/gestion-adaptativa`.
3. `main` queda congelado: es lo que opera la Sim en `DEMO7975145`. NO se toca.

## 1. Qué implementar en `ApexNqIctStrategy.cs`

**Antes de escribir código:** inspecciona cómo maneja la estrategia las órdenes hoy
(¿`SetStopLoss`/`SetProfitTarget` managed, o `ExitLong`/`ExitShort` explícitos?) y adapta la
implementación a ese estilo. No cambies el approach de gestión de órdenes existente.

### Parámetros nuevos (todos `[NinjaScriptProperty]`, defaults = comportamiento actual intacto)

| Parámetro | Default | Descripción |
|---|---|---|
| `EnablePartialTP` | **false** | Master switch. En false, cero cambio de comportamiento. |
| `PartialTPTriggerUsd` | 350 | Ganancia flotante total (USD, 2 contratos) que dispara el parcial. |
| `PartialTPQuantity` | 1 | Contratos a cerrar en el parcial. |
| `BreakevenOffsetUsd` | 30 | Colchón sobre la entrada para el stop del runner (cubre comisiones/slippage). |

### Lógica (solo activa si `EnablePartialTP == true`)

1. Con posición abierta, en cada cierre de barra 1m (`Calculate.OnBarClose`, como todo lo demás):
   si la ganancia flotante total ≥ `PartialTPTriggerUsd` y el parcial no se ha ejecutado:
   - Cerrar `PartialTPQuantity` contratos a mercado (signal name: `"PartialTP"`).
   - Mover el stop del runner a breakeven: precio de entrada ± `BreakevenOffsetUsd` convertido
     a puntos (MNQ = $2/punto/contrato → con 1 contrato runner, $30 = 15 puntos).
   - Flag interno `partialDone = true` (resetear en cada trade nuevo, junto al reset diario).
2. El runner conserva el TP original y el cierre de sesión normales. NO trailing, NO más movimientos
   de stop después del breakeven.
3. El parcial dispara **una sola vez por trade**.

### Restricciones duras

- **NO tocar:** Setup A, Setup B, condiciones de entrada, `KillZoneStart/End`, `SetupBMinMinutes`,
  `MaxDailyLoss`, `ForcedExit`, ni los defaults de `StopLossUsd`/`ProfitTargetUsd`.
- **NO tocar** código/variables de Sergio sin coordinar (verificar con Esteban antes si la sección
  de gestión de salidas es territorio compartido).
- Logging: extender `[TRADE-REC]` para registrar el tipo de salida **por contrato**:
  `PARTIAL_TP`, `RUNNER_BE` (breakeven), `RUNNER_TP`, `RUNNER_SC` (session close), `SL_FULL`,
  `TP_FULL`, `SESSION_CLOSE` (estos tres últimos para cuando `EnablePartialTP=false` o el trade
  muere antes del gatillo). Sin esto, el análisis posterior es imposible.
- Copiar el `.cs` a `Documents\NinjaTrader 8\bin\Custom\Strategies` en el mismo paso y recordar
  a Esteban que requiere **compile manual** en NinjaScript Editor + re-habilitar estrategia.
- Actualizar `CLAUDE.md` en el mismo commit (sección de parámetros + historial de decisiones).
- ⚠️ Recordatorio aparte: hay un pendiente crítico de compilar NT8 ANTES de 9:30 ET por el fix
  UTF-8 de Telegram (commit `4053291`). Verificar timestamp de `NinjaTrader.Custom.dll`.

## 2. Matriz de backtests (Strategy Analyzer, Esteban ejecuta y exporta)

Cada corrida = 5 períodos, exportar pestaña **Trades** a CSV. Nombrar:
`{variante}_{periodo}.csv` (ej. `D_tp700_partial_is_jan_mar_2026.csv`).

| Corrida | TP | SL | PartialTP | Propósito |
|---|---|---|---|---|
| **A** | 1050 | 375 | OFF | Baseline re-export (verificar reproducibilidad vs 237 trades conocidos) |
| **B** | 700 | 375 | OFF | Hipótesis TP=700 sola (ya acordada por el Monte Carlo) |
| **C** | 1050 | 375 | ON (350/1/30) | Aislar el efecto del parcial |
| **D** | 700 | 375 | ON (350/1/30) | Combinación candidata para Sim |

= 20 corridas de Strategy Analyzer (4 variantes × 5 períodos), 20 CSVs.
**No agregar más variantes ni gatillos.** Si surge la tentación, anotarla para después — no correrla.

## 3. Script de análisis previo (mientras Esteban exporta)

Con los CSVs del baseline A, responder con MAE/MFE (extender `analyze_intermediate_exits.py`
o script nuevo `analyze_partial_tp_feasibility.py`):

1. ¿Qué % de trades alcanza MFE ≥ $350 (el gatillo)? Desglosar por exit type y bucket ATR.
2. De los SESSION_CLOSE winners, ¿cuántos tienen MFE ≥ $350? (= cuántos serían afectados).
3. ¿Cuántos SL_FULL tienen MFE ≥ $350? (= cuántos losers de −$375 se convertirían en ≥ +$175).
4. Distribución de MFE de los losers: ¿dónde está el sweet spot del gatillo? (solo informativo —
   el gatillo NO se cambia en esta ronda).

## 4. Script de comparación post-backtest

Nuevo script `backtest/compare_variants.py`. Para cada variante (A/B/C/D), agregando los 5 períodos:

- Net total, WR, PF, MaxDD, expectancy por trade.
- Distribución de exit types (con los nuevos tags por contrato).
- Para C y D: ¿qué trades capturó `PARTIAL_TP`? ¿Cuántos `RUNNER_BE` eran SESSION_CLOSE winners
  en el baseline (canibalización) vs SL_FULL (rescate)?
- % de días de trading que terminan en verde (la métrica de consistencia que pidió Esteban).

## 5. Monte Carlo por variante

Extender `backtest/montecarlo.py` para correr sobre cada CSV agregado (A/B/C/D):

- **Métricas:** probabilidad de quemar trailing $2,500 (baseline actual ~57%), probabilidad de
  alcanzar el profit goal de la eval Apex 50K ($3,000), días esperados hasta el goal,
  equity final p5/p50/p95.
- **Dos modos de remuestreo:** (a) barajado simple de trades, (b) block bootstrap por semanas
  (los regímenes ATR crean rachas — el barajado simple subestima el riesgo de drawdown).
- Output: tabla comparativa única A vs B vs C vs D + fan charts como los existentes.

## 6. Criterios de decisión (PRE-ESCRITOS — no modificar después de ver datos)

Una variante (C o D) se aprueba para Sim paralela solo si, agregando los 5 períodos:

1. **Prob. de quemar trailing $2,500 < la del baseline A** (en ambos modos de remuestreo). Es la
   motivación principal; si no mejora esto, el cambio no tiene propósito.
2. **Net total ≥ 75% del baseline A.** Se acepta sacrificar Net por suavidad, pero no colapsarlo.
3. **PF ≥ 1.15** (baseline sistema: 1.29).
4. **Canibalización acotada:** los `RUNNER_BE` que en baseline eran SESSION_CLOSE winners no
   superan en valor a los SL_FULL rescatados (el parcial debe rescatar más de lo que mata).
5. **% de días verdes > baseline A** (el objetivo declarado de Esteban).

Si C y D pasan, gana la de menor probabilidad de quemar el trailing. Si ninguna pasa:
se documenta en `CLAUDE.md` como hipótesis descartada (como H2a/H2b/H2c) y NO se simula.

## 7. Reglas para la Sim (solo si una variante pasa la sección 6)

- **NO reemplazar** la instancia Sim actual (baseline 1050/375 en `DEMO7975145`). Esa sigue
  corriendo intacta — es el grupo de control.
- Habilitar la variante ganadora como **segunda instancia** de la estrategia en otra cuenta
  Sim/demo, en paralelo. Mismo instrumento, mismas horas.
- En Sim: solo observar. Cero ajustes sobre resultados Sim (n minúsculo).
- Commit + push de todo en `research/gestion-adaptativa` antes de habilitar nada. Tag:
  `checkpoint-partial-tp-{fecha}`.

## Entregables esperados de Claude Code

1. Push de `main` + tags (sección 0).
2. Rama `research/gestion-adaptativa` con: código del parcial (default OFF), `[TRADE-REC]`
   extendido, `CLAUDE.md` actualizado, copia a `bin\Custom`.
3. `analyze_partial_tp_feasibility.py` + resultados sobre baseline A.
4. `compare_variants.py` y `montecarlo.py` extendido, listos para correr cuando existan los CSVs.
5. Tabla final A/B/C/D + veredicto contra los criterios de la sección 6.
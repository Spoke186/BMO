# /backtest — Análisis de resultados (Stream A)

Herramienta de análisis del backtest de NinjaTrader 8 para evaluar la estrategia
ICT NQ contra reglas **Apex**. Calcula lo que el Strategy Analyzer no da masticado:
profit factor, max drawdown, Sharpe, out-of-sample (overfitting), Monte Carlo
(riesgo de secuencia) y las reglas Apex (trailing DD, daily loss, profit goal,
**consistencia 50%**).

> **Sin dependencias.** Solo Python 3 stdlib. No requiere pandas/numpy ni `pip install`.
> Corre igual en las 3 PCs del equipo.

## Por qué existe (y qué NO es)
NT8 es C# (decisión LOCKED). Esto es solo una **utilidad de análisis** de los
resultados, en Python, scoped a `/backtest/`. **No** reescribe ni reemplaza la
estrategia. Inspirado en el patrón "quant-analyst" pero aterrizado a nuestro stack.

Las reglas Apex aquí son **PROXY local** sobre trades cerrados. Apex calcula
trailing DD y consistencia en su servidor con high-water intradía. Esto es para
**tunear (A4) y vigilar (A5)**, no es la verdad oficial.

## Cómo exportar los trades desde NT8
1. Strategy Analyzer → corre el backtest (NQ 5m, 3–6 meses).
2. Pestaña **Trades** (no "Summary").
3. Click derecho en la grilla → **Export** → CSV (o Excel y "Guardar como CSV").
4. Guarda el archivo, ej `trades_nq_q1.csv`.

El script auto-detecta las columnas `Profit` y `Exit time`. Si tu versión de NT8
usa otros nombres, pásalos explícitos con `--profit-col` / `--exit-col`.

## Uso
```bash
# Análisis de un export real
python analyze_backtest.py trades_nq_q1.csv

# Si no detecta las columnas, fíjalas a mano
python analyze_backtest.py trades.csv --profit-col Profit --exit-col "Exit time"

# Probar el script sin un CSV (datos sintéticos)
python analyze_backtest.py --demo

# Ajustar parámetros Apex / análisis (defaults = plan 50K)
python analyze_backtest.py trades.csv \
    --starting-balance 50000 --trailing-dd 2500 --daily-loss 400 \
    --profit-goal 3000 --oos-frac 0.30 --mc-runs 10000
```

## Tests
```bash
python test_analyze_backtest.py      # 8 tests, sin pytest
# o, si tienes pytest:
pytest test_analyze_backtest.py
```

## Lectura de la salida
- **Profit factor** < 1.0 = pierde plata. > 1.5 decente, > 2 bueno (cuidado overfit).
- **Out-of-sample**: si OOS degrada fuerte vs IS → la estrategia está sobreajustada
  al histórico; no confiar en el backtest. La marca `[X]` lo avisa.
- **Monte Carlo / prob. tocar trailing DD**: baraja el orden de los trades. El P&L
  final NO cambia (la suma es invariante), pero el **max drawdown** sí: mide el
  riesgo de que varias pérdidas seguidas quemen la cuenta Apex. `[X]` si la
  probabilidad supera 5%.
- **Consistencia 50%**: ningún día puede valer > 50% del profit total (regla Apex).

## Archivos
- `analyze_backtest.py` — el analizador (CLI).
- `test_analyze_backtest.py` — tests stdlib.
- `PREFLIGHT.md` — checklist para compilar (A2) y correr el backtest (A3) en NT8.

> Los `*.csv` están en `.gitignore` (no commitear exports de backtest). Para
> probar sin un CSV real usa `python analyze_backtest.py --demo`.

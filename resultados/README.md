# Resultados de Trades — BMO

Carpeta para análisis de performance real/sim del bot ICT NQ/MNQ.

## Estructura

```
resultados/
├── screenshots/   → capturas de trades (entrada, SL, TP, gráfico)
├── exports/       → CSV/Excel exportados desde NT8 o Apex dashboard
└── README.md
```

## Qué subir

### screenshots/
- Captura del gráfico en el momento de entrada (1m + 15m)
- Resultado final del trade (TP hit / SL hit)
- Nombre sugerido: `YYYY-MM-DD_LONG|SHORT_resultado.png`

### exports/
- Export de ejecuciones desde NT8: **Account → Executions → Export**
- Export desde panel Apex (performance report)
- CSV de `DailyPnlTracker` si aplica
- Nombre sugerido: `YYYY-MM-DD_NT8_executions.csv`

## Para análisis

Con screenshots: puedo revisar si setup ICT fue correcto (sweep, CHoCH, FVG, confirmación 1m).
Con CSV: puedo calcular win rate, RR real, consistencia Apex, drawdown.

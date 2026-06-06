# CLAUDE.md — Bot Trading ICT NQ/MNQ (Apex)

> Contexto persistente para Claude Code. Léelo antes de tocar el repo.
> El `CLAUDE.md` de la carpeta padre (`Downloads`) es de OTRO proyecto (intérprete médico). Ignóralo.

## Qué es
Estrategia automatizada **NinjaScript (NinjaTrader 8, C#)** para futuros del Nasdaq (NQ/MNQ),
bajo reglas **Apex Trader Funding**, plan **50K**. Un único archivo: `ApexNqIctStrategy.cs`.

## Stack (decidido)
- **Plataforma:** NinjaTrader 8 (no Python). NT8 maneja scheduling, feed y broker nativo.
- **Lenguaje:** NinjaScript (C#).
- **Bróker/feed:** Rithmic o Tradovate vía Apex.
- Descartado: Python+API y TradingView+PickMyTrade.

## Estrategia (decidida)
ICT continuación de tendencia:
1. **Tendencia** = estructura HH/HL en **15m** (pivote fractal, fuerza 3).
2. **Sweep** de liquidez contra-tendencia en **5m** (perfora último swing y recupera).
3. **Desplazamiento** = vela cuerpo ≥ 1.5×ATR(14) (proxy de "institucional").
4. **FVG** de 3 velas (gap mínimo 3 pts).
5. **Entrada** = límite en **fill completo** del FVG (borde lejano), a favor de tendencia.
6. **Stop** detrás del extremo del sweep + 2 ticks. **TP = 1:3 RR** fijo.
7. **Ventana** NY kill zone 8:30–11:00 ET, cierre forzado 15:55 ET, **1 setup/día**.

## Riesgo Apex en el código
- ✅ Stop obligatorio, no DCA, 1 entrada/setup, ventana horaria, max daily loss ($400).
- ⚠️ Trailing DD = aproximación local (high-water). Apex la calcula en su server.
- ⚠️ Consistencia 50%: integrada en **tiempo real** vía `infra/DailyPnlTracker.cs` (persiste JSON).
  En **backtest** se valida post-hoc con `backtest/analyze_backtest.py` (el tracker usa `DateTime.Now`,
  inválido sobre datos históricos). Apex sigue siendo la verdad oficial.

## Convenciones
- Parámetros TODO por Inputs (`[NinjaScriptProperty]`), nada hardcodeado.
- `Calculate.OnBarClose`. Serie primaria 5m; 15m añadida por `AddDataSeries`.
- Comentar el *por qué* (proxies, reglas Apex), no el *qué*.
- Nunca primer run en cuenta fondeada. Backtest → Sim → Eval → Fondeada.

## Pendiente / roadmap
1. Backtest + tuning (Strategy Analyzer).
2. ~~Persistencia P&L → regla consistencia 50% real~~ → hecho en tiempo real (`DailyPnlTracker`).
   Falta validar en Sim que `RecordTrade`/`WouldViolateConsistency` disparan bien.
3. TP "siguiente liquidez" (fase 2).
4. Alertas Telegram.

## Reglas de trabajo
- Si una decisión técnica contradice este archivo, **pregunta antes de avanzar**.
- Si cambias la lógica, actualiza este archivo en el mismo cambio.
- Nunca secretos/credenciales en el código.

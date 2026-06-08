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

## Estrategia (sesión 16) — Setup A + Setup B activos
> **Sesión 16 (2026-06-07): Setup A (FVG) + Setup B (sweep directo) AMBOS ACTIVOS.**
> Backtest Ene–Mar 2026 (MNQ 06-26): 44–45 trades, ~$5.020 neto sin bias filter.
> Desglose mensual: ENE -$134 / FEB +$4.324 / MAR -$677.
> Febrero solo supera el objetivo Apex $3K/mes holgadamente.
> Setup C/D/E quedan apagados por default.
>
> Ventana NY: 9:30–12:00 ET = Colombia 8:30–11:00 (EDT). `KillZoneEnd=1200`.

### Configuración actual en SetDefaults
| Parámetro | Valor | Motivo |
|---|---|---|
| Contratos | 2 | RR adecuado en MNQ |
| StopLossUsd | 375 | Margen vs ruido 1m |
| ProfitTargetUsd | 1050 | 1:2.8 RR |
| EnableSetupB | **true** | +trades → más TPs potenciales |
| EnableDailyBiasFilter | **false** | Backtest: filter elimina TPs buenos |
| Allow2ndTradeIfWinner | false | 1 trade/día por defecto |
| KillZoneStart | 930 | NY open |
| KillZoneEnd | 1200 | 30min extra vs original |
| MaxDailyLoss | 400 | ~1 SL + slippage |
| TrailingDrawdown | 2500 | Apex 50K plan |
| EnableFileLog | true | Log a archivo para diagnóstico |
| ManageTrailStop | vacío | Trail off: corta TPs — peor resultado |

### Setup A — FVG (mayor calidad, requiere confluencia 15m)
Temporalidades: **15m sesgo/FVG, 1m gatillo**.
1. **Tendencia** = estructura HH/HL en **15m** (pivote fractal, fuerza 3).
2. **Sweep** de liquidez contra-tendencia en **15m** (perfora el **máx/mín del rango pre-apertura** —medido en barras 1m desde apertura de sesión hasta 9:30 ET— y el cierre recupera). Requiere datos overnight (Globex/24h) en el gráfico.
3. **CHoCH + Desplazamiento** en **15m**: tras la barrida, vela cierra más allá del último swing en dirección tendencia Y cuerpo ≥ 1.5×ATR(14). Confirma reversión institucional.
4. **FVG** de 3 velas en **15m** (gap mínimo ~0.25pts/1 tick, en el impulso del CHoCH).
5. **Retroceso al FVG** en **1m**: esperar que el precio entre a la zona del gap (+1× buffer).
6. **Confirmación en 1m** (al menos una): vela de rechazo (mecha ≥ 30% rango + cierre favorable) O mini-CHoCH en 1m dentro del FVG.
7. **Entrada** = mercado al cierre de la vela de confirmación 1m, a favor de tendencia.

### Setup B — Sweep directo (`EnableSetupB=true`, ACTIVO sesión 16)
Patrón: banco barre liquidez al abrir NY → reversión institucional. Niveles de barrida:
1. Rango pre-mercado (8:00–9:30 ET): `preMarketHigh` / `preMarketLow`
2. **PDH/PDL** (Previous Day High/Low, `EnablePdhPdl=true`): niveles ICT de mayor liquidez.

- **LONG** (sweep de mínimo): `Low[0] < nivel - minSweep` Y `Close[0] > nivel + retBuf` Y vela alcista.
- **SHORT** (sweep de máximo): `High[0] > nivel + minSweep` Y `Close[0] < nivel - retBuf` Y vela bajista.
- `EnableDailyBiasFilter=false` (backtest: filter elimina TPs buenos → resultado peor).
- `Allow2ndTradeIfWinner=false` (default): si `true`, habilita 2do trade si el primero fue ganador.
- Si Setup A ya está armado (`setupState == 1`), Setup B no dispara.
- `tradedToday` compartido con A: si A ya operó, B no dispara.

### Común a ambos setups
- **Stop $375 fijo / TP $1050 fijo** (1:2.8 RR). 2 contratos fijos siempre.
- **Ventana entrada**: `KillZoneStart=930` → `KillZoneEnd=1200` ET.
- Posición abierta antes de `KillZoneEnd` **corre hasta TP/SL**; `ForcedExit=1400` ET bloquea nuevas entradas.
- **1 setup/día** por defecto. `tradedToday` compartido entre A y B.
- Señales: `LongFVG`/`ShortFVG` (Setup A), `LongSweep`/`ShortSweep` (Setup B).

## Riesgo Apex en el código
- ✅ Stop obligatorio, no DCA, 1 entrada/setup, ventana horaria, max daily loss ($400).
- ⚠️ Trailing DD = aproximación local (high-water). Apex la calcula en su server.
- ⚠️ Consistencia 50%: integrada en **tiempo real** vía `infra/DailyPnlTracker.cs` (persiste JSON).
  En **backtest** se valida post-hoc con `backtest/analyze_backtest.py` (el tracker usa `DateTime.Now`,
  inválido sobre datos históricos). Apex sigue siendo la verdad oficial.

## Convenciones
- Parámetros TODO por Inputs (`[NinjaScriptProperty]`), nada hardcodeado.
- `Calculate.OnBarClose`. Serie primaria **1m** (gatillo); **15m** añadida por `AddDataSeries` (sesgo/FVG).
- Comentar el *por qué* (proxies, reglas Apex), no el *qué*.
- Nunca primer run en cuenta fondeada. Backtest → Sim → Eval → Fondeada.

## Historial de decisiones clave
- **Sesión 14**: Setup A solo, B apagado. Bug fix latching CHoCH+FVG.
- **Sesión 15**: chochLevel fallback fix. Parámetros máx permisivos. DailyPnlTracker.
- **Sesión 16**: Setup B reactivado. Trail stop probado (3 variantes) → **todas peores** → desactivado.
  EnableDailyBiasFilter probado → **peor resultado** ($3.513 vs $5.020) → desactivado.
  Backtest óptimo: sin trail, sin bias filter, SetupB=true.
  Backtest Ene–Mar 2026: ENE -$134 / FEB +$4.324 / MAR -$677.

## Pendiente / roadmap
1. **Apex eval (próxima sesión):** correr estrategia en MNQ Sim durante 1 mes antes de entrar a cuenta eval.
   Confirmar que los trades ejecutan correctamente en tiempo real (fills, stops, sesión close).
2. ~~Persistencia P&L → regla consistencia 50% real~~ → hecho en tiempo real (`DailyPnlTracker`).
3. TP "siguiente liquidez" (fase 2, post-eval).
4. Alertas Telegram → `alerts/TelegramAlerts.cs` inerte sin token `TELEGRAM_BOT_TOKEN`/`TELEGRAM_CHAT_ID`.
5. Calendario CME (`infra/MarketCalendar.cs`) integrado: skip festivo/finde + cierre 12:45 media sesión.
   **Mantenimiento:** fechas hardcoded 2026–2027; actualizar cada diciembre.
6. Bitácora DEMO Notion (`infra/NotionLogger.cs`): inerte sin `NOTION_API_KEY` (N11).

## Reglas de trabajo
- Si una decisión técnica contradice este archivo, **pregunta antes de avanzar**.
- Si cambias la lógica, actualiza este archivo en el mismo cambio.
- Nunca secretos/credenciales en el código.

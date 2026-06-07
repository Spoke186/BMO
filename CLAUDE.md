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

## Estrategia (decidida) — FLUJO ÚNICO: Setup A (FVG)
> **Decisión operador sesión 14 (2026-06-07): el proyecto se cierra a UN solo flujo = Setup A**
> (el de mayor calidad, confluencia 15m). **Setup B y C quedan APAGADOS por default**
> (`EnableSetupB=false`, `EnableSetupC=false`) — su código sigue **intacto y reactivable**
> (poner el Input en `true`). El bot opera SOLO con A. Se cierra el "reto A vs B".
> ⚠️ **A hoy dispara ~0 trades** (B lo pre-emptía + cadena 15m muy restrictiva); el trabajo en
> curso es **aflojar A** para que produzca con su WR de calidad (ver roadmap §1).
> 🔔 Decisión que apaga el Setup B de Sergio → **avisar al equipo** (BITACORA + TAREAS).
>
> Ventana NY: 9:30–11:00 ET = Colombia 8:30–10:00 (EDT). En EST (invierno) ajustar `KillZoneEnd` a 1000.

### Setup A — FVG (mayor calidad, requiere confluencia 15m)
Temporalidades: **15m sesgo/FVG, 1m gatillo**.
1. **Tendencia** = estructura HH/HL en **15m** (pivote fractal, fuerza 3).
2. **Sweep** de liquidez contra-tendencia en **15m** (perfora el **máx/mín del rango pre-apertura** —medido en barras 1m desde apertura de sesión hasta 9:30 ET— y el cierre recupera). Requiere datos overnight (Globex/24h) en el gráfico.
3. **CHoCH + Desplazamiento** en **15m**: tras la barrida, vela cierra más allá del último swing en dirección tendencia Y cuerpo ≥ 1.5×ATR(14). Confirma reversión institucional.
4. **FVG** de 3 velas en **15m** (gap mínimo 6 pts, en el impulso del CHoCH).
5. **Retroceso al FVG** en **1m**: esperar que el precio entre a la zona del gap.
6. **Confirmación en 1m** (al menos una): vela de rechazo (mecha/cuerpo ≥ 1.5) O mini-CHoCH en 1m dentro del FVG.
7. **Entrada** = mercado al cierre de la vela de confirmación 1m, a favor de tendencia.

### Setup B — Sweep directo (entrada inmediata, sin FVG) · ⏸️ APAGADO por default (`EnableSetupB=false`)
> Dormido desde sesión 14 (flujo único A). Código intacto; reactivable con el Input en `true`.
> Lo de abajo describe su lógica para cuando/si se reactive.

Patrón: banco barre liquidez al abrir NY → reversión institucional. Niveles de barrida:
1. Rango pre-mercado (8:00–9:30 ET): `preMarketHigh` / `preMarketLow`
2. **PDH/PDL** (Previous Day High/Low, `EnablePdhPdl=true`): niveles ICT de mayor liquidez.

- **LONG** (sweep de mínimo): `Low[0] < nivel - minSweep` Y `Close[0] > nivel` Y vela alcista.
- **SHORT** (sweep de máximo): `High[0] > nivel + minSweep` Y `Close[0] < nivel` Y vela bajista.
- **Filtro dirección**: `EnableDailyBiasFilter=true` — dia alcista → solo longs; dia bajista → solo shorts.
  `SetupBRequiresTrend=false` (default): el bias filter ya filtra dirección (evita conflictos).
- `Allow2ndTradeIfWinner=false` (default): si `true`, habilita 2do trade si el primero fue ganador.
- Si Setup A ya está armado (`setupState == 1`), Setup B no dispara.
- Desactivable con `EnableSetupB = false`.

### Común a ambos setups
- **Stop $250 fijo / TP $700 fijo** (1:3 RR). 2 contratos fijos siempre.
- **Ventana entrada**: `KillZoneStart=930` → `KillZoneEnd=1130` ET (Colombia 8:30–10:30 EDT).
- Posición abierta antes de `KillZoneEnd` **corre hasta TP/SL**; `ForcedExit=1400` ET solo bloquea nuevas entradas, no aplana posición activa.
- **1 setup/día** por defecto. `tradedToday` compartido entre A y B. Con `Allow2ndTradeIfWinner=true`: 2 trades posibles si primero gana.
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

## Pendiente / roadmap
1. **Flujo único A — hacer que A dispare (PRIORIDAD sesión 14).** Correr A aislada (B/C ya off)
   NQ 1m ETH con NinjaScript Output abierto → identificar el gate que mata a A (trend=0 / sweep
   sin confirmar / CHoCH+displacement+FVG en UNA misma barra 15m `ApexNqIctStrategy.cs:597` /
   confirmación 1m) → aflojar ESE gate preservando la calidad → re-backtest con `analyze_backtest.py`.
   Meta A: **~8-10 trades/mes a ~65-70% WR ≈ $3.000**. Cambios de lógica del `.cs` = coordinar con Sergio.
2. ~~Persistencia P&L → regla consistencia 50% real~~ → hecho en tiempo real (`DailyPnlTracker`).
   Falta validar en Sim que `RecordTrade`/`WouldViolateConsistency` disparan bien.
3. TP "siguiente liquidez" (fase 2).
4. ~~Alertas Telegram~~ → `alerts/TelegramAlerts.cs` + wiring en estrategia hecho; inerte sin token,
   se activa al poner `TELEGRAM_BOT_TOKEN`/`TELEGRAM_CHAT_ID` (N8). Heartbeat timer opcional sin cablear.
5. Calendario CME (`infra/MarketCalendar.cs`) integrado: skip festivo/finde + cierre 12:45 media sesión.
   **Mantenimiento:** fechas hardcoded 2026–2027; actualizar cada diciembre.
6. Bitácora DEMO Notion (`infra/NotionLogger.cs` + wiring, PR #16 de SECH): registra cada trade
   (apertura + cierre) en una BD Notion. Inerte sin `NOTION_API_KEY` (N11). DB id hardcoded en el `.cs`.

## Reglas de trabajo
- Si una decisión técnica contradice este archivo, **pregunta antes de avanzar**.
- Si cambias la lógica, actualiza este archivo en el mismo cambio.
- Nunca secretos/credenciales en el código.

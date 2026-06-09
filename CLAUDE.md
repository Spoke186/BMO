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

## Estrategia (sesión 18) — ENTRADAS CONGELADAS, foco en régimen ATR
> **Sesión 18 (2026-06-08): Diagnóstico de causa raíz. Proyecto cambia de "selección" a "adaptación al régimen".**
> Sesión 17 backtest IS (Ene-Mar 2026 con fix SetupBMinMinutes=15): 46 trades, $4,645 neto.
> Desglose mensual: ENE +$998 / FEB +$4,324 / MAR -$677.
> Setup C/D/E quedan apagados por default.
>
> **CAMBIO SESIÓN 18: EnableDailyTrendFilter corregido (lógica ICT AMD)**
> - ANTES: bloqueaba BEAR-longs y BULL-shorts (lógica trend-following → INCORRECTO para ICT counter-trend).
> - AHORA: bloquea BULL-longs (ICT: longs fallan después de días alcistas, mercado sigue distribuyendo).
> - Shorts NO bloqueados: datos muestran WR positivo en días alcistas Y bajistas.
> - Default: `EnableDailyTrendFilter=false` (sin cambio; corrección lista para cuando se active).
>
> **HALLAZGO CRÍTICO SESIÓN 18: ATR × Dirección = Régimen real**
> - ATR <300: sistema pierde (TP=263pts requiere 175% del rango diario → imposible).
> - ATR 300-380: breakeven (77% ATR). Longs OK si NQ sube, shorts no.
> - ATR 380-450: MEJOR bucket — 3/3 trades TP en IS (n=3, muestra pequeña).
> - ATR >450: PF=1.24 positivo pero asimétrico: longs 23.5% WR (pierden en crash), shorts 54.5% WR.
> - Correlación Pearson ATR vs P&L individual: r=0.026 (débil) porque dirección modera ATR.
> - Feb 2026 (ATR=512, NQ ↑): PF=2.63, AvgW=$997. Mar 2026 (ATR=521, NQ ↓ -12%): PF=0.82.
>
> **HALLAZGO CRÍTICO 2: Setup A no dispara**
> - IS period (46 trades): Setup A = 2 trades (4.3%). Setup B = 44 trades (95.7%).
> - Setup A no está compitiendo con B — **el sistema es fundamentalmente Setup B**.
> - Objetivo sesión 17 "convertir B → A" no aplica: A no existe en volumen suficiente.
>
> **FILTRO ATR CHOP — IMPLEMENTADO Y VALIDADO (sesión 18 fin)**
> - `EnableATRRegimeFilter=false` (default OFF), `ATRRegimeThreshold=300`.
> - IS Jan-Mar 2026: 46 trades, $4,645 — sin cambio (0 días CHOP). ✅
> - OOS Jul-Sep 2025: 34→24 trades, Net -$1,389 → +$206, PF 0.76→1.06, MaxDD $2,030→$1,281. ✅
> - Discrepancia ATR: NT8 usa barras ETH (24h), yfinance usa RTH → ATR20D NT8 > Python.
>   Mismo umbral 300 filtra 10 días en NT8 vs 25 en Python. Efecto real y positivo.
> - Default OFF: comportamiento base preservado. Activar conscientemente en live.
>
> **ENTRADAS CONGELADAS (vigente):**
> - No tocar Setup A, Setup B, stops ($375), targets ($1050), parámetros de entrada.
> - FASE 3: análisis ATR bucket cross-period (CSVs OOS Trades tab pendientes).
> - FASE 4 gestión: targets/stops adaptivos × régimen — post-FASE 3.
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
| EnableATRRegimeFilter | **false** | CHOP filter validado: ATR20D < umbral → no operar (default OFF) |
| ATRRegimeThreshold | 300 | Umbral CHOP (pts). NT8 ETH bars → ATR NT8 > ATR Python/yfinance |
| TrailingDrawdown | 2500 | Apex 50K plan |
| EnableFileLog | true | Log a archivo para diagnóstico |
| ManageTrailStop | vacío | Trail off: corta TPs — peor resultado |
| SetupBMinMinutes | **15** | B espera 15min: da chance a A de procesar la barra 15m |

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
- Si A detectó barrida 15m (`sweepState15m == 1`), Setup B no dispara (gate sesión 17).
- `SetupBMinMinutes=15`: B no puede disparar antes del minuto 15 del kill zone (9:45 ET).
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
- **Sesión 16**: Setup B reactivado. Trail stop (3 variantes) → peores → off. Bias filter → peor → off.
  Backtest óptimo: sin trail, sin bias filter, SetupB=true. $5,020 / 3 meses.
  ENE +$1,373 / FEB +$4,324 / MAR -$677 (bias filter OFF).
  Bug: preMarketHigh/Low init fijo (double.MinValue/MaxValue).
- **Sesión 17**: Bug Setup A bloqueado identificado: B disparaba a 9:31, bloqueaba A a 9:45.
  Fix: SetupBMinMinutes=15 + sweepState15m==0 gate en Setup B.
  Objetivo: WR 42% → 50%+ convirtiendo trades B en calidad A.
- **Sesión 18**: OOS backtests (Jul-Sep/Oct-Dec 2025, Apr-Jun 2026). Análisis ATR regime. 147 trades.
  EnableDailyTrendFilter corregido (ICT AMD: bloquea BULL-longs, no BEAR-longs).
  **FASE 2 COMPLETA**: ATR <300 PF=0.77 (régimen muerto). ATR 380-450 PF=3.08 (mejor).
  ATR >450 PF=1.23 (positivo pero volátil — dirección macro modera). r=0.056 (régimen, no trade).
  Setup A = 4.3% de trades (2/46 IS). **Setup B = el sistema.**
  Proyecto: "selección de trades" → "adaptación al régimen".
  **FASE 4 CHOP FILTER COMPLETA**: detect_chop.py → validate_chop_filter.py → implementación → validación IS+OOS.
  Filtro implementado: `EnableATRRegimeFilter` + `ATRRegimeThreshold=300`. Validado IS y Jul-Sep 2025.
  Scripts: `backtest/detect_chop.py`, `backtest/validate_chop_filter.py`, `backtest/analyze_regime_atr.py`.
  FASE 3 pendiente (CSVs OOS Trades tab).

## Pendiente / roadmap

### ACTIVO — FASE 3 (bloqueado por export usuario)
1. **[BLOQUEADO USUARIO]** Re-exportar OOS como pestaña "Trades" (NO Daily, NO Performance):
   NT8 Strategy Analyzer → backtest → tab "Trades" → botón Export/guardar → CSV con separador ";"
   Columnas requeridas: Trade Number, Market Pos., Entry Name, Entry Time, Exit Name, Profit, MAE, MFE.
   Períodos: Jul-Sep 2025 / Oct-Dec 2025 / Apr-Jun 2026.
2. Con los 3 CSVs: correr `backtest/analyze_regime_atr.py` con los 4 CSVs (IS + 3 OOS).
   Objetivo: matriz ATR Bucket × Dirección Macro × Setup × Long/Short.
3. **[POST-FASE 3]** FASE 4: Propuestas gestión adaptativa (targets/stops × ATR, parciales, régimen dinámico).
   NO implementar hasta cerrar FASE 3.

### FREEZE activo
- NO modificar Setup A, Setup B, stops ($375), targets ($1050), parámetros de entrada.
- NO agregar nuevos filtros más allá del ATR CHOP ya implementado.
- NO Sim ni Eval hasta completar FASE 3 + FASE 4 gestión adaptativa.

### ON HOLD durante validación
- Apex eval/Sim: pospuesto hasta resolver arquitectura de gestión por régimen.
- TP "siguiente liquidez": pospuesto.

### Infraestructura (baja prioridad)
- Alertas Telegram → `alerts/TelegramAlerts.cs` inerte sin token.
- Calendario CME → `infra/MarketCalendar.cs` hardcoded 2026–2027.
- Bitácora Notion → `infra/NotionLogger.cs` inerte sin API key.
- ~~Persistencia P&L~~ → hecho (`DailyPnlTracker`).

## Reglas de trabajo
- Si una decisión técnica contradice este archivo, **pregunta antes de avanzar**.
- Si cambias la lógica, actualiza este archivo en el mismo cambio.
- Nunca secretos/credenciales en el código.

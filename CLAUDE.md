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

## Estrategia (sesión 19) — ENTRADAS CONGELADAS, FASE 3 COMPLETA
> **Sesión 19 (2026-06-09): FASE 3 completa. 147 trades, 4 períodos. Hallazgo crítico: el edge no viene de los TPs.**
>
> **HALLAZGOS CRÍTICOS SESIÓN 19 — FASE 3 DIAGNÓSTICO (147 trades, 4 períodos IS+OOS)**
>
> **P1 — ACTIVE shorts (380-450): hipótesis, no validación concluyente**
> - n=11 total en 3 de 4 períodos (OOS1 Jul-Sep 2025 = 0 trades ACTIVE short).
> - PF=3.62 global: IS n=2 PF=∞, OOS2 n=6 PF=1.69, OOS3 n=3 PF=4.40.
> - Patrón existe. No hay evidencia estadística suficiente para afirmar ventaja estructural.
>
> **P2 — MAE/MFE distribución: ACTIVE es el bucket más eficiente**
> - ACTIVE MFE: p25=$294, p50=$732, p75=$1,050, p90=$1,050 (TP trunca). Precio llega al TP con frecuencia.
> - MAE winners/losers: winners tienen MAE 2-4x menor que losers en todos los buckets. Patrón consistente.
> - CHOP MFE p50=$181 — el precio no se mueve suficientemente ni en régimen de baja volatilidad.
>
> **P3 — Duración: tres patrones distintos**
> - CHOP: todo dura (Win avg 358min). Mercado sin dirección.
> - ACTIVE: resolución rápida (~131min mediana). El precio se mueve con decisión en ambas direcciones.
> - STRONG: losers rápidos (95min avg = SL), winners duran toda sesión (249min avg).
>
> **P4 — HALLAZGO CRÍTICO: el edge NO viene de los TPs completos**
> - 68 SL completos = -$25,500. 24 TP completos = +$25,200. Neto entre sí: -$300.
> - TPs y SLs prácticamente se cancelan. El beneficio total (+$10,716) proviene de las 38 salidas intermedias.
> - Top 20% de winners = 118% del neto total. Sin esos trades el sistema es negativo.
> - Conclusión: el edge está en la distribución de resultados intermedios (session-close parciales), no en alcanzar el TP fijo.
>
> **HALLAZGO CAUSAL DEFINITIVO — SESSION_CLOSE ES EL EDGE (sesión 19 diagnóstico final)**
>
> Análisis `analyze_intermediate_exits.py` sobre 147 trades × 4 períodos:
> - TP_FULL (n=24): +$25,200 (+235% del neto). SL_FULL (n=68): -$25,500 (-238%). Se cancelan.
> - SESSION_CLOSE (n=55): WR=69.1%, PF=5.47, Net=+$11,016 → **+103% del neto total**.
> - SESSION_CLOSE funciona en TODOS los buckets: CHOP PF=3.32, WEAK PF=9.77, ACTIVE PF=8.50, STRONG PF=6.44.
> - El 84% de SESSION_CLOSE entra en T1 (9:30–10:20 ET). Corre 374min (mediana 382min).
>
> **Contrafactual Q7 (Cat A=0, Cat B=38, Cat C=17):**
> - Cat A=0: ningún SESSION_CLOSE fue cortado antes de llegar al TP. TP y SESSION_CLOSE no compiten.
> - Cat B=38 (+$13,478): el mercado nunca llegó al TP. La sesión capturó todo lo disponible.
> - Cat C=17 (-$2,462 vs -$6,375): SESSION_CLOSE ahorró $3,913 en pérdidas vs esperar SL completo.
>
> **SESSION_CLOSE no es un exit de conveniencia. Es un mecanismo adaptativo implícito.**
> Adapta el resultado a lo que el mercado entregó durante la sesión. No corta ganadores, no abandona dinero.
>
> **RESTRICCIÓN FASE 4:** cualquier modificación debe demostrar que mejora (o no daña) el mecanismo SESSION_CLOSE.
> La carga de la prueba: quien proponga cambiar TP, SL, escalar contratos o agregar filtros debe demostrar
> que el Net SESSION_CLOSE, WR SESSION_CLOSE y PF SESSION_CLOSE no empeoran.
>
> Scripts: `backtest/analyze_regime_atr.py`, `backtest/diagnose_regime.py`, `backtest/analyze_intermediate_exits.py`.
> CSVs en `backtest/`: `is_jan_mar_2026.csv`, `oos_jul_sep_2025.csv`, `oos_oct_dec_2025.csv`, `oos_apr_jun_2026.csv`.

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
  **CHOP FILTER COMPLETO**: `EnableATRRegimeFilter` + `ATRRegimeThreshold=300`. Validado IS y Jul-Sep 2025.
  Scripts: `backtest/detect_chop.py`, `backtest/validate_chop_filter.py`, `backtest/analyze_regime_atr.py`.
- **Sesión 19**: FASE 3 completa + diagnóstico causal definitivo. 147 trades × 4 períodos.
  **HALLAZGO CENTRAL**: SESSION_CLOSE genera +103% del neto. TP y SL se cancelan entre sí.
  Contrafactual Q7: Cat A=0 (TP no corta SESSION_CLOSE), Cat B=38 (+$13,478), Cat C ahorra $3,913.
  SESSION_CLOSE es el mecanismo adaptativo implícito del sistema — no un exit accidental.
  Scripts: `backtest/diagnose_regime.py`, `backtest/analyze_intermediate_exits.py`.
  **FASE 3 CERRADA**. FASE 4: cualquier propuesta debe preservar el mecanismo SESSION_CLOSE.

## Pendiente / roadmap

### ACTIVO — FASE 4 (gestión que preserve SESSION_CLOSE)
> FASE 3 + diagnóstico causal cerrados. El edge ES SESSION_CLOSE. Regla de oro para FASE 4:
> **cualquier propuesta debe demostrar que no daña Net/WR/PF de SESSION_CLOSE antes de implementarse.**
>
> Pregunta central: "¿Cómo preservamos o mejoramos el mecanismo SESSION_CLOSE?"
>
> **ESTADO HIPÓTESIS (sesión 19 FASE 4):**
>
> **H1 — Anatomía SC losers** ✅ CERRADA
> - SC losers vs winners: CHOP 53% vs 29%, T2 41% vs 11%, MAE/MFE ratio 7.70 vs 0.34.
> - Señal: intersección CHOP ∩ T2 como zona problemática. No es filtro — es diagnóstico.
> - Script: `backtest/analyze_sc_losers.py`
>
> **H2b — Filtro T2 completo** ❌ DESCARTADA
> - Eliminar todos los T2 (n=24): WR↑ PF↑ Net SC↓$1,015. T2 sigue siendo rentable.
> - Script: `backtest/h2b_t2_filter.py`
>
> **H2a — Filtro CHOP SC completo** ❌ DESCARTADA
> - Eliminar SC CHOP (n=20): WR↑ PF↑ Net SC↓$2,885. CHOP SC PF=3.32, positivo.
> - Hallazgo clave: SC CHOP T2 = 0 winners, 3 losers (señal para H2c).
> - Script: `backtest/h2a_chop_sc.py`
>
> **H2c — Filtro CHOP∩T2 SC** ⚠️ PROMETEDORA NO VALIDADA
> - Eliminar SC CHOP∩T2 (n=3): WR+4pp ✅ PF+0.86 ✅ Net+$335 ✅ MaxDD sin cambio ✅.
> - Cross-period: OOS1 ✅ OOS2 ✅ IS/OOS3 neutro (0 trades afectados).
> - Convergencia triple: H1 (T2=41% losers) + H2b (T2 inferior) + H2a anatomy (CHOP T2=0 winners).
> - **PROBLEMA: n=3, todos perdedores. Insuficiente para implementar.**
> - Próximo paso: extender histórico NT8 pre-jul 2025 para buscar más instancias CHOP T2.
> - Script: `backtest/h2c_chop_t2_sc.py`
>
> **Mapa SC por bucket × tercio (referencia):**
> - CHOP T2: WR=0% n=3 Net=-$335 ← target H2c
> - WEAK T2: WR=67% n=3 Net=+$695 (OK)
> - ACTV T2: WR=100% n=1 Net=+$601 (OK)
> - STRG T2: WR=25% n=4 Net=+$54 (débil, posible H2d — necesita más datos)
> - STRG T1: WR=100% n=10 Net=+$4,167 (núcleo del edge en STRONG)
>
> **Regla de validación H2c antes de implementar:**
> - Extender backtesting a ≥2024 en NT8 para n≥10 instancias CHOP T2.
> - Si patrón persiste (CHOP T2 WR<30%) → implementar en NinjaScript como gate de entrada.
> - Si patrón desaparece → era ruido. Descartar.
>
> NO implementar hasta: propuesta → backtest counterfactual → validación IS+OOS.

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

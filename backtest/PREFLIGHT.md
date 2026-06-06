# PREFLIGHT — Stream A (compilar + backtest)

> Checklist para ejecutar A2 (compilar) y A3 (backtest) en la máquina con NinjaTrader 8.
> El código fue revisado estáticamente (sin NT8 en la máquina de dev). Verdict: **compila** salvo
> sorpresas de versión. Si algo no coincide con tu versión NT8, avisa (regla `external-change`).

## 0. Antes de F5

- [ ] Copiar los **seis** `.cs` a `Documents\NinjaTrader 8\bin\Custom\`:
  - `ApexNqIctStrategy.cs`  → `bin\Custom\Strategies\`
  - `ntaddon\ApexBridgeAddOn.cs` → `bin\Custom\AddOns\`
  - `infra\DailyPnlTracker.cs` → `bin\Custom\Strategies\` (namespace `...Strategies`)
  - `infra\MarketCalendar.cs` → `bin\Custom\Strategies\` (festivos CME + cierre media sesión)
  - `alerts\TelegramAlerts.cs` → `bin\Custom\Strategies\` (inerte sin token N8)
  - `infra\NotionLogger.cs` → `bin\Custom\Strategies\` (bitácora DEMO, inerte sin `NOTION_API_KEY`)
  - **Motivo:** la estrategia referencia `ApexBridgeState` (AddOn), `DailyPnlTracker`, `MarketCalendar`,
    `TelegramAlerts` y `NotionLogger`. Si falta alguno, F5 falla por símbolo inexistente. Todos
    compilan en la MISMA assembly Custom.
  - Nota: `DailyPnlTracker` solo se activa en **tiempo real** (Sim/live), no en backtest. En backtest
    la consistencia 50% la valida `analyze_backtest.py`.

## 1. A2 — Compilar (F5)

- [ ] NinjaScript Editor → F5.
- [ ] 0 errores. El ATR es `ATR(BarsArray[1], 14)` (atado a la serie 15m). Si tu versión NT8 se queja
  del overload con `BarsArray`, validar la firma (N2).
- [ ] Si error en `SetStopLoss`/`SetProfitTarget`: verificar firma contra tu versión NT8 (N2).

## 2. Aplicar a gráfico — verificar params

- [ ] Gráfico **NQ** (contrato vigente, ej `NQ 09-26`), periodicidad **1 minuto** (gatillo).
  - **Crítico:** la serie primaria DEBE ser 1m. El código hace `AddDataSeries(Minute, 15)` para el
    sesgo/sweep/CHoCH/FVG. Si el primario no es 1m, toda la lógica de ejecución se corre de timeframe.
  - **Crítico (G4):** el gráfico DEBE incluir datos **overnight** (template Globex/24h, NO RTH-only).
    El rango pre-apertura (sweep) se mide en barras 1m desde apertura de sesión hasta 09:30 ET. Sin
    overnight, `preMarketReady` nunca se arma → **cero setups** (backtest vacío).
- [ ] Aplicar `ApexNqIctStrategy`. Confirmar inputs default:
  - Contratos = **2**, Stop = **$250**, Target = **$700**, ventana **930–1400** ET (forced exit 1400:
    no abre nuevas, deja correr la abierta).

## 3. ⚠️ GOTCHA timezone (revisar SÍ o SÍ)

El código usa `ToTime(Time[0])` para la ventana (09:30–14:00 ET) y el forced exit (14:00 ET).
`ToTime` devuelve la hora **en la zona horaria del gráfico**, no en ET.

- Colombia = UTC-5 todo el año. ET = UTC-5 en invierno, **UTC-4 en horario de verano (mar–nov)**.
- Si el gráfico está en hora Colombia, en verano la kill zone dispararía **1h corrida** vs la real ET.
- [ ] En Chart Properties → **Time zone = (UTC-05:00) Eastern Time**, o validar que la sesión RTH
  del template del instrumento esté en ET. Verificar con una vela conocida que 8:30 ET = barra correcta.

## 4. A3 — Backtest (Strategy Analyzer)

- [ ] Strategy Analyzer → `ApexNqIctStrategy` → NQ **1m**, **3–6 meses**, datos **Globex/24h**.
- [ ] Data series: 1m primaria (el 15m lo añade el código solo).
- [ ] Correr. Anotar en el chat/repo: **win rate, profit factor, max drawdown, # trades, avg trade**.

## 5. Observaciones para el tuning (A4) — NO bloquean compilar

- **Stop tan ajustado:** $250 con `CalculationMode.Currency` sobre **2 NQ** (NQ = $20/pt) ≈ **6.25 pts
  totales**. Es muy ajustado para NQ; espera win rate bajo por barridos. Decisión LOCKED del operador
  (no la cambio), pero confírmalo en los resultados del backtest. Si Currency mode resulta ser
  por-contrato y no total, el stop real sería ~12.5 pts — verificar en los fills.
- **Interpretación FVG/displacement:** el código toma la vela actual (bar 0) como el desplazamiento y
  mide el gap entre `Low[0]` y `High[2]`. Es una variante válida del FVG de 3 velas; afinarla en A4 si
  el "ojo humano" la dibuja distinto.
- **Sesgo mixto 15m:** si no hay HH+HL ni LH+LL claros, mantiene el último `trend` (anti-whipsaw).
  Verificar que no quede pegado a un sesgo viejo en rangos largos.

## DoD Stream A Sprint 1
- [ ] Compila sin errores (A2).
- [ ] 1 backtest con métricas pegado aquí o en el chat (A3).

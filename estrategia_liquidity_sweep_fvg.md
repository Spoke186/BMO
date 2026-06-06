# Estrategia: Liquidity Sweep + FVG (NY Open) — Futuros de Índices

> Documento de reglas para automatización en bot.
> Todo está escrito como condiciones exactas (si / entonces) para que un bot pueda ejecutarlas.

---

## 1. Parámetros generales

| Parámetro | Valor |
|---|---|
| Tipo de cuenta | Fondeo 50k |
| Tamaño de posición | **2 contratos fijos** (siempre, no se cambia) |
| Instrumento | Futuros de índices (NQ/MNQ, ES/MES, YM/MYM — confirmar con la firma) |
| Días de operación | Lunes a viernes |
| Zona horaria de referencia | Hora Colombia (ET = Colombia + 1h en horario de verano EE.UU.) |
| Temporalidades | 15m para sesgo y FVG / 1m para gatillo de entrada |
| Operaciones por día | 1 base (opcional: 2da solo si la 1ra fue ganadora y aparece setup limpio) |

---

## 2. Riesgo fijo — Relación 1:3 (REGLA CENTRAL)

- **Stop Loss: SIEMPRE 250 USD.** No cambia, sin importar dónde sea la entrada.
- **Take Profit: SIEMPRE 700 USD.** (Relación ~1:3.)
- Estos montos son FIJOS. La entrada puede estar en cualquier punto, pero el riesgo y el objetivo en dólares no se mueven.

### Cómo se traduce a puntos con 2 contratos fijos

Como el tamaño es fijo en 2 contratos, el stop y el target en **puntos** se calculan dividiendo el monto en dólares entre el valor del punto:

```
distancia_stop_puntos   = 250 USD / (valor_punto * 2 contratos)
distancia_target_puntos = 700 USD / (valor_punto * 2 contratos)
```

**Ejemplos (confirmar valor de punto con tu firma):**

| Contrato | Valor punto (1 contrato) | Valor punto (2 contratos) | Stop en puntos (250 USD) | Target en puntos (700 USD) |
|---|---|---|---|---|
| MNQ (micro NASDAQ) | 2 USD | 4 USD | 62.5 pts | 175 pts |
| NQ (mini NASDAQ) | 20 USD | 40 USD | 6.25 pts | 17.5 pts |
| MES (micro S&P) | 5 USD | 10 USD | 25 pts | 70 pts |
| ES (mini S&P) | 50 USD | 100 USD | 2.5 pts | 7 pts |
| MYM (micro Dow) | 0.50 USD | 1 USD | 250 pts | 700 pts |
| YM (mini Dow) | 5 USD | 10 USD | 25 pts | 70 pts |

> **Nota:** con tamaño fijo y stop fijo en dólares, el stop/target en puntos es SIEMPRE el mismo número para cada contrato. La entrada solo cambia el precio, no la distancia.

---

## 3. Ventanas de tiempo

| Hora Colombia | Hora ET | Qué pasa |
|---|---|---|
| 08:30 | 09:30 | Apertura de NY. Empieza la observación de la barrida. |
| 08:30 – 13:00 | 09:30 – 14:00 | **Ventana de ejecución.** El bot puede entrar EN CUANTO el setup completo se cumpla (puede ser temprano o más tarde, no hay espera obligatoria). |
| 13:00 | 14:00 | Cerrar todo. No abrir nada nuevo. |

> El FVG NO tiene que aparecer media hora después. Se entra cuando el setup completo se forma, sea pronto o tarde dentro de la ventana.

> **Nota de implementación (operador, sesión 6 — G3):** en el bot, a las 14:00 ET el sistema **deja de ABRIR** entradas, pero una posición ya abierta **NO se cierra**: corre hasta su TP o SL (1 oportunidad/día). Si sigue viva, NinjaTrader la aplana en el **cierre de sesión** (`IsExitOnSessionCloseStrategy`, ~16:00 ET). El "Cerrar todo" de esta tabla y de §11 es la regla discrecional; la decisión automatizada es dejar correr. Para un cierre duro a 14:00 habría que reintroducir un flatten explícito.

---

## 4. Paso 1 — Definir el sesgo del día (antes de 08:30)

- 15m con **máximos y mínimos crecientes** → **sesgo ALCISTA** (solo compras).
- 15m con **máximos y mínimos decrecientes** → **sesgo BAJISTA** (solo ventas).
- Marcar el **máximo y mínimo del rango pre-apertura** (pre-market / sesión asiática-europea). Son los niveles de liquidez que el "banco" va a barrer.

---

## 5. Paso 2 — Detectar la barrida de liquidez (Liquidity Sweep)

- **Día bajista:** el precio SUBE y rompe (con mecha) el máximo previo marcado, luego vuelve a entrar por debajo de ese nivel. = barrida del máximo.
- **Día alcista:** el precio BAJA y rompe el mínimo previo, luego vuelve a entrar por encima. = barrida del mínimo.
- **Validación obligatoria:** la vela debe CERRAR de vuelta dentro del rango (rechazo del nivel). Si rompe y se queda fuera → NO es barrida válida → no hay setup ese día.

---

## 6. Paso 3 — Confirmar cambio de estructura (CHoCH / BOS)

- **Día bajista:** tras barrer el máximo, el precio debe romper a la baja el último mínimo menor relevante. Confirma reversión bajista.
- **Día alcista:** tras barrer el mínimo, el precio debe romper al alza el último máximo menor relevante.
- El impulso que produce esta ruptura es el que **genera el FVG** que se va a operar.
- Si NO hay ruptura de estructura → NO hay entrada.

---

## 7. Paso 4 — Identificar el FVG válido (Fair Value Gap)

El FVG es el hueco de 3 velas dentro del impulso del Paso 3:

- **FVG bajista (para ventas):** hueco entre la mecha baja de la vela 1 y la mecha alta de la vela 3; la vela 2 cae con fuerza. La zona del hueco es la zona de entrada.
- **FVG alcista (para compras):** hueco entre la mecha alta de la vela 1 y la mecha baja de la vela 3; la vela 2 sube con fuerza.

**Solo es válido** el FVG nacido del impulso post-barrida. Ignorar cualquier otro FVG del gráfico.

---

## 8. Paso 5 — Entrada (Gatillo)

1. El bot espera a que el precio **RETROCEDA** y toque la zona del FVG. No perseguir; esperar el regreso.
2. **Confirmación en 1m dentro del FVG** (al menos una debe cumplirse):
   - **Vela de rechazo:** mecha larga en contra de la entrada (mecha superior larga para venta / mecha inferior larga para compra), con cierre a favor del sesgo.
   - **Mini-CHoCH en 1m:** ruptura de estructura de 1m a favor del sesgo, dentro del FVG.
3. **Entrada:** al cierre de la vela de confirmación en 1m.
   - Sesgo bajista → entrar en CORTO (venta).
   - Sesgo alcista → entrar en LARGO (compra).

---

## 9. Paso 6 — Cuándo PIERDES (Stop Loss)

- **Stop fijo: 250 USD** (siempre, relación 1:3).
- Colocación en el gráfico:
  - **Venta:** stop arriba del máximo de la barrida.
  - **Compra:** stop debajo del mínimo de la barrida.
- La distancia en puntos se calcula con la fórmula del punto 2.
- **El bot NUNCA mueve el stop en contra. NUNCA promedia.**
- Si el precio toca el stop → operación PERDIDA (−250 USD).

---

## 10. Paso 7 — Cuándo GANAS (Take Profit)

- **Target fijo: 700 USD** (relación ~1:3).
- Objetivo conceptual: la siguiente piscina de liquidez en dirección del sesgo (refuerzo, pero el monto manda: cerrar en 700 USD).
- Si el precio toca el target → operación GANADA (+700 USD).

---

## 11. Paso 8 — Gestión de la cuenta de fondeo (50k)

- **2 contratos fijos siempre.** No aumentar ni reducir.
- **1 operación por día** (base). Opcional: una 2da solo si la 1ra fue ganadora y aparece otro setup limpio.
- **Stop diario:** al perder una operación (−250), parar hasta el día siguiente.
- **Habrá días sin setup → NO se opera. Es correcto y sano.** No forzar entradas.
- **Habrá días perdedores y días ganadores.** Es parte normal del sistema.
- Respetar SIEMPRE el **límite de pérdida diaria** y el **trailing drawdown** de la firma de fondeo. Esto es lo que más tumba cuentas, no el mercado.
- Cerrar todo a las **13:00 Col** sí o sí.

---

## 12. Lógica completa si/entonces (pseudocódigo para el bot)

```text
# --- CONFIGURACIÓN ---
CONTRATOS = 2 (fijo)
RIESGO_USD = 250 (fijo)
OBJETIVO_USD = 700 (fijo)  # relación 1:3
VALOR_PUNTO = (según contrato)

distancia_stop_puntos   = RIESGO_USD   / (VALOR_PUNTO * CONTRATOS)
distancia_target_puntos = OBJETIVO_USD / (VALOR_PUNTO * CONTRATOS)

# --- PREPARACIÓN (antes de 08:30 Col) ---
sesgo = ALCISTA o BAJISTA   # según estructura 15m
nivel_liquidez = máximo y mínimo del rango pre-apertura

# --- EJECUCIÓN (08:30 a 13:00 Col) ---
DENTRO de ventana 08:30–13:00 Col:

  SI barrida_válida(nivel_liquidez) == TRUE
     Y cambio_estructura(sesgo) == TRUE
     Y FVG_válido == TRUE:

        SI precio_retrocede_al_FVG == TRUE
           Y confirmación_1m(sesgo) == TRUE:

              entrada = cierre_vela_confirmación

              SI sesgo == BAJISTA:
                 abrir CORTO con 2 contratos
                 stop   = entrada + distancia_stop_puntos
                 target = entrada - distancia_target_puntos

              SI sesgo == ALCISTA:
                 abrir LARGO con 2 contratos
                 stop   = entrada - distancia_stop_puntos
                 target = entrada + distancia_target_puntos

# --- GESTIÓN DE LA OPERACIÓN ---
SI precio toca stop    → CERRAR pérdida (−250 USD)
SI precio toca target  → CERRAR ganancia (+700 USD)
SI hora == 13:00 Col   → CERRAR todo

# --- LÍMITES DIARIOS ---
SI operaciones_hoy >= límite_diario → no abrir más hoy
SI pérdida_diaria alcanzada         → no abrir más hoy
SI no hay setup                     → no operar (día sin operación, normal)
```

---

## 13. Puntos críticos a definir antes de programar

1. **Parámetros exactos para el bot.** Lo más difícil de automatizar son la barrida (Paso 2), el cambio de estructura (Paso 3) y el FVG (Paso 4). Un humano los "ve"; el bot necesita números:
   - ¿Qué cuenta como "máximo/mínimo previo relevante"? (ej: el extremo de las últimas N velas)
   - ¿Qué cuenta como "mínimo/máximo menor" para el CHoCH?
   - ¿Cuántos puntos de hueco mínimo hacen válido un FVG? (para filtrar huecos diminutos)

2. **Confirmar el valor de punto** de tu contrato con la firma de fondeo. Eso fija el stop/target en puntos.

3. **Backtest y demo primero.** Con relación 1:3, necesitas acertar aproximadamente **1 de cada 4 operaciones** solo para no perder dinero. Corre la estrategia en backtest de varios meses y en demo varias semanas para medir tu win rate real ANTES de la cuenta de fondeo.

---

## Aviso

Este documento es una formalización de tu estrategia para fines de automatización y estudio. No es asesoría financiera ni una garantía de rentabilidad. El trading de futuros con apalancamiento conlleva riesgo de pérdida. Valida siempre con datos (backtest/demo) antes de operar capital real o de una cuenta de fondeo.

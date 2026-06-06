# Trading Knowledge Base

## Objetivo

Este documento contiene las principales estrategias, conceptos y reglas utilizadas en trading discrecional y algorítmico para el desarrollo de bots de trading.

---

# 1. Market Structure

## Tendencia Alcista
Características: HH (Higher High), HL (Higher Low)
Regla: Si el mercado sigue formando HH y HL, la tendencia es alcista.

## Tendencia Bajista
Características: LL (Lower Low), LH (Lower High)
Regla: Si el mercado sigue formando LL y LH, la tendencia es bajista.

## Temporalidades
Bias General: Diario, 4H, 1H
Ejecución: 15M, 5M, 1M

---

# 2. Break of Structure (BOS)
Ruptura de un máximo o mínimo relevante que confirma continuación de tendencia.
Condiciones: Tendencia previa identificada. Ruptura clara. Cierre de vela más allá del nivel.
Uso: Confirmar continuación. Confirmar entrada.

---

# 3. Change of Character (CHOCH)
Primer indicio de cambio de tendencia.
Ejemplo Alcista: Tendencia bajista → se rompe un LH → aparece un HH.
Ejemplo Bajista: Tendencia alcista → se rompe un HL → aparece un LL.
Uso: Detectar reversión.

---

# 4. Liquidity
Buy Side Liquidity: Situada por encima de máximos (Equal Highs, Swing Highs).
Sell Side Liquidity: Situada por debajo de mínimos (Equal Lows, Swing Lows).
Objetivo institucional: Tomar liquidez antes del movimiento principal.

---

# 5. Liquidity Sweep
Movimiento que toma stops antes de continuar.
Características: Ruptura temporal. Regreso rápido. Continuación posterior.
Uso: Confirmación institucional.

---

# 6. Fair Value Gap (FVG)
Ineficiencia entre velas. Condición: Vela 1 y Vela 3 no se solapan.
Tipos: Bullish FVG, Bearish FVG.
Uso: Entradas, Mitigación, Continuación.

---

# 7. Inverse Fair Value Gap (IFVG)
FVG roto que cambia de función. Bullish → Bearish / Bearish → Bullish.
Uso: Soporte y resistencia dinámica.

---

# 8. Order Block (OB)
Última vela antes de un movimiento impulsivo.
Tipos: Bullish Order Block, Bearish Order Block.
Uso: Zona de reacción institucional.

---

# 9. Breaker Block
Order Block invalidado. Se convierte en nueva zona de soporte/resistencia.

---

# 10. Mitigation Block
Zona donde instituciones regresan a cerrar órdenes pendientes.
Uso: Entradas avanzadas.

---

# 11. Supply and Demand
Demand Zone: Movimiento explosivo alcista.
Supply Zone: Movimiento explosivo bajista.
Uso: Buscar reacciones del precio.

---

# 12. Support and Resistance
Niveles históricos donde el precio reacciona.
Tipos: Horizontal, Dinámico, Psicológico.

---

# 13. VWAP (Volume Weighted Average Price)
Precio por encima → sesgo alcista.
Precio por debajo → sesgo bajista.
Uso: Confirmación de tendencia. Mean Reversion.

---

# 14. EMA Trading
EMAs comunes: 20, 50, 100, 200.
EMA corta > EMA larga = Alcista. EMA corta < EMA larga = Bajista.

---

# 15. Mean Reversion
Retorno a la media estadística.
Herramientas: VWAP, Bollinger Bands, EMA.

---

# 16. Breakout Strategy
Condiciones: Consolidación previa. Ruptura. Confirmación.

---

# 17. Retest Strategy
1. Rompimiento. 2. Retroceso. 3. Confirmación. 4. Continuación.

---

# 18. Scalping
Temporalidades: 1M, 2M, 5M. Objetivo: Capturar movimientos pequeños.

---

# 19. Swing Trading
Temporalidades: 4H, Diario, Semanal. Duración: Días o semanas.

---

# 20. Session Trading
Asia: Menor volatilidad.
Londres: Alta volatilidad.
Nueva York: Alta volatilidad.
London-New York Overlap: Mayor liquidez del día.

---

# 21. Opening Range Breakout (ORB)
1. Definir rango inicial. 2. Esperar ruptura. 3. Confirmar. 4. Ejecutar.

---

# 22. Gestión de Riesgo
Riesgo por operación: 0.5%, 1%, 2%. Nunca arriesgar más de lo planificado.

---

# 23. Risk Reward
Mínimos recomendados: 1:2, 1:3, 1:5.

---

# 24. Trading Psychology
Principios: Disciplina, Consistencia, Paciencia, Gestión emocional.
Evitar: Revenge Trading, FOMO, Overtrading.

---

# 25. Framework SMC Completo

Proceso:
1. Identificar tendencia en 15M.
2. Marcar liquidez.
3. Esperar sweep.
4. Confirmar CHOCH.
5. Confirmar BOS.
6. Identificar FVG.
7. Esperar mitigación.
8. Ejecutar entrada.
9. Stop detrás del sweep.
10. TP en siguiente liquidez.

---

# 26. Variables para Bots

Trend Direction: Bullish, Bearish
Market Structure: HH, HL, LL, LH
Liquidity: BSL (Buy Side), SSL (Sell Side)
Institutional Concepts: BOS, CHOCH, FVG, IFVG, OB, Breaker, Mitigation
Execution: Entry, Stop Loss, Take Profit
Risk: Risk %, RR Ratio
Sessions: Asia, London, New York

---

# 27. Lógica Base de Bot Institucional

```
IF Trend = Bullish
AND Liquidity Sweep = Sell Side
AND BOS = Bullish
AND FVG = Present
THEN
  Entry = FVG Mitigation
  Stop = Below Sweep
  Target = Next Buy Side Liquidity
ELSE
  No Trade
```

---

# 28. Mapa de Implementación en ApexNqIctStrategy.cs

## Ya implementado ✅
- Market Structure 15m (HH/HL via swings)
- CHoCH detection 15m
- Liquidity Sweep (pre-market range)
- FVG detection 15m + 1m
- Order Block 1m (Setup C, desactivado)
- Stop fijo / TP fijo

## Pendiente de implementar 🔲
- BOS confirmation (después de CHoCH)
- 4H bias series (solo longs en 4H bullish)
- Previous Day High/Low como niveles de liquidez adicionales
- VWAP como filtro de sesgo
- EMA 200 como filtro macro
- London session setups (8:00-9:30 ET)
- ORB (Opening Range Breakout, primera vela 15m)
- IFVG (FVG roto que cambia función)
- Breaker Block
- TP dinámico en siguiente liquidez (en vez de fijo $700)
- Sessions filter (no operar en Asia)
```

# Instrucciones para el Bot — Bitácora DEMO + Análisis de Backtesting

> Manual de cómo el bot debe registrar cada operación en la bitácora DEMO de Notion,
> y cómo usar esos datos para analizar el backtesting mientras se afina la estrategia.
> Estrategia: **Liquidity Sweep + FVG** (futuros de índices, RR 1:3).

---

## 1. Estado actual

- **Solo se usa la bitácora DEMO por ahora.** La de fondeo se conecta después.
- El bot registra TODA operación que lance, sin importar el resultado.
- Mientras tanto, se le enseña la estrategia y se analiza el backtesting con los datos acumulados.

**Bitácora DEMO:**
- URL: https://app.notion.com/p/b15e1f0ae8d8424394135ced2043643f
- Data Source ID: `9c557353-e4ee-4e97-801b-d7b25924c484`

> El código que hace la conexión es `NotionLogger.cs` (entregado aparte). Este .md explica QUÉ y CUÁNDO registrar, y cómo leer los datos.

---

## 2. Qué debe hacer el bot — en 2 momentos

### Momento 1 — AL LANZAR la operación (sin importar resultado)
El bot llama a `RegistrarApertura(esFondeo: false, ...)` y crea una fila con:

| Campo | De dónde sale |
|---|---|
| Operación | Nombre auto: "{Dirección} {Activo} {fecha}" |
| Fecha | Fecha del sistema |
| Activo | El instrumento operado |
| Sesgo | Alcista / Bajista (el del día) |
| Dirección | Compra / Venta |
| Hora entrada (Col) | Hora del sistema en formato HH:mm |
| Precio entrada / Stop / Target | Los 3 niveles de la operación |
| Resultado | **"En curso"** (se actualiza al cerrar) |
| RR planificado | "1:3" (fijo) |
| Barrida válida | true/false según detectó el setup |
| Cambio estructura | true/false |
| FVG válido | true/false |
| Confirmación 1m | true/false |

> Esto deja registrada la operación apenas se lanza. Aunque el bot se caiga después, la entrada ya quedó guardada.

### Momento 2 — AL CERRAR la operación
El bot llama a `ActualizarCierre(pageId, ...)` y completa:

| Campo | Valor |
|---|---|
| Resultado | "Ganada" / "Perdida" / "Break-even" |
| P&L USD | +700, −250, o el real |
| Emoción/Disciplina | "Disciplinado" (el bot siempre es disciplinado; útil para comparar con tus trades manuales) |
| Cumplió reglas | true si el setup fue completo |
| Notas / Aprendizaje | Motivo del cierre (target, stop, cierre por horario 13:00) |

---

## 3. Regla clave para el backtesting: registrar también lo que NO se opera

Para que el análisis sea honesto, el bot (o tú) debe registrar también:
- **Días sin setup** → fila con Resultado = "Sin operar", nota: "No hubo barrida válida" / "No hubo FVG".
- **Señales descartadas** → si el setup apareció pero falló un filtro (ej: stop > 250 USD), registrarlo con nota.

Esto evita el sesgo de "solo cuento lo que operé" y te dice cuántas oportunidades reales hay por semana.

---

## 4. Cómo analizar el backtesting con los datos de la bitácora

Una vez tengas operaciones acumuladas (idealmente 50–100+ para que sea significativo), calcula:

### Métricas principales
| Métrica | Fórmula | Qué te dice | Umbral sano (RR 1:3) |
|---|---|---|---|
| **Win rate** | Ganadas ÷ Total operadas | % de aciertos | Necesitas > 25–30% solo para no perder |
| **Profit factor** | Suma ganancias ÷ Suma pérdidas (abs) | Salud del sistema | > 1.0 rentable; > 1.5 bueno |
| **Expectativa por trade** | (WinRate × 700) − (LossRate × 250) | USD esperados por operación | Debe ser positivo |
| **Racha máx. de pérdidas** | Mayor secuencia de "Perdida" seguidas | Cuánto drawdown soportar | Define tu tolerancia |
| **# operaciones / semana** | Contar filas operadas | Frecuencia real | — |
| **% cumplió reglas** | "Cumplió reglas" ✅ ÷ Total | Disciplina del sistema | Cuanto más alto, mejor |

### Cálculo de la expectativa (clave)
Con RR 1:3 (gana 700, pierde 250):
```
Expectativa = (WinRate × 700) − ((1 − WinRate) × 250)

Ejemplos:
  WinRate 30%:  (0.30 × 700) − (0.70 × 250) = 210 − 175 = +35 USD/trade  ✅ rentable
  WinRate 26.5%: punto de equilibrio aprox (≈ 0 USD/trade)
  WinRate 20%:  (0.20 × 700) − (0.80 × 250) = 140 − 200 = −60 USD/trade  ❌ pierde
```
**Conclusión:** con esta estrategia tu objetivo mínimo es un win rate sostenido por encima de ~27%. Por encima de 35% ya es un sistema sólido.

---

## 5. Cómo "enseñarle" al bot mientras analizas (proceso iterativo)

1. **Fase 1 — Recolección:** deja al bot operar en demo y registrar todo. No cambies nada todavía. Junta datos.
2. **Fase 2 — Análisis:** revisa la bitácora. Filtra por "Cumplió reglas = ✅" y mira el win rate solo de esos. Compáralo con los que NO cumplieron reglas.
3. **Fase 3 — Detección de patrones:** ¿pierde más en cierto activo? ¿a cierta hora? ¿cuando el FVG es muy pequeño? La bitácora te lo muestra al agrupar por Activo, por Hora, etc.
4. **Fase 4 — Ajuste:** cambia UN parámetro a la vez (ej: tamaño mínimo del FVG) y vuelve a la Fase 1. Compara el antes y después.
5. Repite. Así el sistema mejora con datos, no con corazonadas.

---

## 6. Vistas en Notion que ayudan al análisis (créalas con "+ Nueva vista")

- **Tabla filtrada:** Resultado = "Ganada" → para contar aciertos.
- **Tabla filtrada:** "Cumplió reglas" = ❌ → ver entradas fuera de reglas y cuánto te cuestan.
- **Tablero agrupado por Activo** → ver en qué instrumento rindes mejor.
- **Gráfico (chart):** P&L USD acumulado → tu curva de capital de la demo.
- **Calendario por Fecha** → ver la distribución de operaciones en el tiempo.

---

## 7. Qué NO hacer (errores comunes de backtesting)

- No juzgar el sistema con pocas operaciones (menos de ~30 no dice nada).
- No cambiar varios parámetros a la vez (no sabrías cuál ayudó).
- No borrar las operaciones perdedoras (sesgo que arruina el análisis).
- No pasar a fondeo hasta que la demo muestre expectativa positiva estable por varias semanas.

---

## Aviso

Documento de registro y estudio. No es asesoría financiera. El objetivo es validar la estrategia con datos reales en demo antes de arriesgar capital de fondeo. El trading de futuros conlleva riesgo de pérdida.

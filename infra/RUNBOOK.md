# RUNBOOK — Operación diaria del bot ICT NQ/MNQ (Apex 50K)

> Este runbook es para el operador en **PC-LIVE**. Seguir en orden.
> Estado del bot visible via MCP (Claude Code con servidor `apex-nt8-mcp` activo).

---

## 1. Checklist PRE-MERCADO (antes de las 8:00 AM ET)

### 1.1 Verificar que hoy es día hábil
```
# En Claude Code con MCP conectado:
check_market
```
- Si `trading_day: false` → NO iniciar bot. Fin.
- Si `half_session: true` → `bot_force_close` será 12:45 ET (automático).

### 1.2 Arranque de NinjaTrader 8
1. Abrir NT8 → conectar cuenta Apex (Rithmic o Tradovate).
2. Verificar que aparece `[ApexBridge] HTTP en 127.0.0.1:8731` en Output Tab.
3. Si no aparece: **Tools → Account Performance → AddOns** → confirmar que `ApexBridgeAddOn` está cargado.

### 1.3 Verificar AddOn via MCP
```
get_account
```
Esperado: `cash_value` > 0, `trading_enabled: true`.

Si error `account '...' no encontrada`: verificar `AccountName` en `ApexBridgeAddOn.cs` contra nombre real en NT8.

### 1.4 Cargar estrategia
1. NT8 → **New Chart** → instrumento: MNQ (o NQ).
2. **Strategies** → Add → `ApexNqIctStrategy`.
3. Configurar parámetros (ver sección 4).
4. Click **Enable** (o via MCP: `enable_strategy`).

### 1.5 Verificar posición inicial
```
get_position
```
Esperado: `market_position: FLAT`. Si no es FLAT: investigar antes de habilitar.

---

## 2. Durante la sesión (8:30–11:00 ET — kill zone)

### Monitoreo mínimo
- El bot opera autónomamente. No interferir salvo emergencia.
- Revisar Output Tab de NT8 si hay mensajes `[ApexBot]`.
- MCP disponible para consultas: `get_account`, `get_position`, `get_today_trades`.

### Emergencias
| Situación | Acción |
|-----------|--------|
| Bot tomó trade no deseado | `disable_strategy` via MCP → cierre manual si necesario |
| Conexión Rithmic/Tradovate caída | NT8 reconecta solo; verificar con `get_account` |
| P&L acercándose a daily loss ($400) | `disable_strategy` inmediatamente |
| NT8 se cierra inesperadamente | Re-abrir → cargar AddOn → cargar estrategia → verificar posición |

---

## 3. Checklist POST-SESIÓN (después de las 11:00 AM ET o bot cerrado)

1. Verificar `get_today_trades` → anotar resultado del día.
2. Verificar que posición es FLAT (`get_position`).
3. Si quedó posición abierta: cerrar manualmente en NT8 antes de las 15:55 ET.
4. `disable_strategy` (opcional, el bot ya no toma setups fuera de kill zone).

### Regla consistencia 50% (manual hasta que A5 esté listo)
- Calcular: ¿el P&L de hoy > 50% del profit acumulado de la semana?
- Si sí: **no operar mañana** (o dejar bot inhabilitado).
- Ejemplo: acumulado $800, hoy ganaste $430 → 430/800 = 53.75% → VIOLA → descanso.

---

## 4. Parámetros de la estrategia (valores iniciales sugeridos)

| Parámetro | Valor inicial | Notas |
|-----------|--------------|-------|
| `TrendPeriod15m` | 15 | Timeframe tendencia (no cambiar) |
| `FractalStrength` | 3 | Fuerza pivote para HH/HL |
| `DisplacementMult` | 1.5 | Multiplicador ATR(14) para displacement |
| `FvgMinPoints` | 3.0 | Gap mínimo FVG en puntos NQ |
| `RiskReward` | 3.0 | TP = 1:3 (no cambiar sin pensar) |
| `MaxDailyLossUsd` | 400.0 | Límite diario Apex (no subir) |
| `MaxRiskPerTradeUsd` | 250.0 | Cap por trade (no subir) |
| `KillZoneStartHour` | 8 | ET hora |
| `KillZoneStartMin` | 30 | ET minutos |
| `KillZoneEndHour` | 11 | ET hora |
| `ForceCloseHour` | 15 | ET hora cierre forzado |
| `ForceCloseMin` | 55 | ET minutos |

---

## 5. Qué hacer si el bot cae

### NT8 se cierra o congela
1. Forzar cierre NT8 si no responde (Task Manager).
2. Re-abrir NT8 → reconectar cuenta.
3. Verificar que el AddOn aparece en Output (`[ApexBridge] HTTP en 127.0.0.1:8731`).
4. Verificar posición: `get_position`.
5. Si hay posición abierta → decide si mantener o cerrar manualmente antes de re-habilitar.
6. Re-cargar estrategia si la sesión es antes de las 11:00 ET.

### PC-LIVE se reinicia
- Mismo flujo que §5.1. Si reinicias después de las 11:00 ET → no re-habilitar bot (kill zone terminó).

### AddOn no responde (MCP da error de conexión)
1. NT8 Output Tab → buscar `[ApexBridge] Error`.
2. Verificar que el puerto 8731 no está ocupado: `netstat -an | findstr 8731` (PowerShell).
3. Reiniciar NT8.

---

## 6. Contacto y escalado

- **Repo:** https://github.com/Spoke186/BMO
- **Issues:** abrir en GitHub si es bug del código.
- **Apex soporte:** https://apextrader.com (para problemas de cuenta/reglas).

---

## 7. Histórico de cambios

| Fecha | Cambio |
|-------|--------|
| 2026-06-05 | Versión inicial (Stream B/C) |

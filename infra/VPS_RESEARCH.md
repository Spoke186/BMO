# VPS Research — NT8 en Windows (Tarea C4)

> NinjaTrader 8 requiere Windows. El VPS debe estar cerca de los servidores CME
> (Aurora, IL) para latencia mínima. Rithmic y Tradovate también tienen sus matching
> engines en Chicago/Nueva Jersey.

---

## Opciones evaluadas

### 1. Vultr — Recomendado para empezar

| Campo | Detalle |
|-------|---------|
| Plan | Cloud Compute, 2 vCPU / 4 GB RAM / 80 GB SSD |
| OS | Windows Server 2022 |
| Precio | ~$40/mes |
| Ubicación recomendada | **Chicago** (IL) — misma ciudad que CME |
| Latencia CME aprox. | < 5 ms |
| Setup | RDP directo, IP estática, Windows Defender incluido |

**Pro:** ubicación Chicago nativa, precio razonable, fácil de escalar.
**Contra:** soporte básico; si NT8 falla en horario de mercado, resolución manual.

---

### 2. Contabo — Más barato, más lento de soporte

| Campo | Detalle |
|-------|---------|
| Plan | VPS M, 4 vCPU / 8 GB RAM / 200 GB SSD |
| OS | Windows Server 2019 |
| Precio | ~$18/mes |
| Ubicación recomendada | St. Louis, MO (el más cercano a Chicago que ofrecen) |
| Latencia CME aprox. | 10–20 ms |

**Pro:** precio muy bajo para los recursos.
**Contra:** sin datacenter en Chicago; soporte lento; no es ideal para trading en vivo.

---

### 3. AWS EC2 — Flexible pero caro para uso continuo

| Campo | Detalle |
|-------|---------|
| Instancia | t3.medium (2 vCPU / 4 GB) |
| OS | Windows Server 2022 |
| Precio | ~$60–80/mes (on-demand) · ~$35/mes (reserved 1 año) |
| Ubicación recomendada | `us-east-1` (N. Virginia) — gateway a Tradovate/Rithmic en NJ |
| Latencia CME aprox. | 15–25 ms desde us-east-1 |

**Pro:** confiabilidad enterprise, snapshots fáciles, CloudWatch para alertas.
**Contra:** más caro; billing complejo; overkill para 1 bot en Sim/Eval.

---

### 4. Kamatera — Opción intermedia con Chicago

| Campo | Detalle |
|-------|---------|
| Plan | 2 vCPU / 4 GB RAM |
| OS | Windows Server 2019 |
| Precio | ~$28/mes |
| Ubicación | Chicago, IL |
| Latencia CME aprox. | < 5 ms |

**Pro:** datacenter en Chicago, precio razonable.
**Contra:** menos conocido; documentación escasa.

---

## Recomendación por fase

| Fase | VPS sugerido | Por qué |
|------|-------------|---------|
| Sim / Backtest | **No VPS** — usar PC propia | NT8 gratuito en Sim; ahorra costos mientras se testea |
| Evaluación Apex | **Vultr Chicago** (~$40/mes) | Latencia real a CME; costo razonable durante 2–4 semanas de eval |
| Cuenta fondeada | **Vultr Chicago** (o escalar) | Misma infra; si falla, costos de erreur > costo VPS |

---

## Requerimientos mínimos NT8

| Recurso | Mínimo | Recomendado |
|---------|--------|-------------|
| CPU | 2 vCPU | 4 vCPU |
| RAM | 4 GB | 8 GB |
| Disco | 40 GB SSD | 80 GB SSD |
| OS | Windows 10 / Server 2019 | Windows Server 2022 |
| Red | 100 Mbps | 1 Gbps |
| IP | Estática | Estática (requerida por Rithmic) |

---

## Checklist de setup VPS (cuando se decida)

- [ ] Crear VPS con Windows Server + RDP habilitado.
- [ ] Instalar NinjaTrader 8 (installer desde ninjatrader.com).
- [ ] Conectar cuenta Apex (Rithmic o Tradovate) en NT8.
- [ ] Copiar `ApexNqIctStrategy.cs` + `ntaddon/ApexBridgeAddOn.cs` a `bin\Custom\`.
- [ ] Compilar (F5) y verificar según `backtest/PREFLIGHT.md`.
- [ ] Instalar Node.js LTS → `npm install` + `npm run build` en `mcp/`.
- [ ] Crear `.env` con `BRIDGE_TOKEN` real.
- [ ] Registrar MCP en Claude Code (`.mcp.json` ya está en el repo).
- [ ] Verificar que puerto 8731 NO está expuesto a internet (solo `127.0.0.1`).
- [ ] Configurar RDP con contraseña fuerte + considerar cambiar puerto RDP default (3389).

---

## Notas de seguridad VPS

- AddOn HTTP solo en `127.0.0.1:8731` — nunca abrir a internet.
- Firewall Windows: bloquear entrada al puerto 8731 por defecto.
- Si MCP corre en otro PC (LAN): usar VPN, NO exponer directamente.
- Contraseña RDP: mínimo 16 chars, alfanumérica + símbolos.

---

## Estado

| # | Tarea | Estado |
|---|-------|--------|
| C4a | Research VPS opciones | ✅ este documento |
| C4b | Decidir proveedor | ⛔ espera N6 (PC-LIVE) + que operador lo apruebe |
| C4c | Setup VPS | ⛔ espera C4b + Apex (N1) |

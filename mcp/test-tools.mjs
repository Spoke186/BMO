#!/usr/bin/env node
/**
 * Prueba automatizada de los 6 tools del MCP contra un mock del AddOn.
 * No requiere NinjaTrader. Levanta un HTTP server falso en 127.0.0.1:8731
 * y verifica que el MCP responde correctamente a cada tool.
 *
 * Uso (desde la raiz del repo):
 *   node mcp/test-tools.mjs
 *
 * Exit 0 = todo OK. Exit 1 = hay fallos (ver salida).
 */
import http from 'http';
import { spawn } from 'child_process';
import readline from 'readline';

const TOKEN = 'CHANGE_ME_LOCAL_TOKEN';
const PORT  = 8731;

// ─── Mock AddOn ──────────────────────────────────────────────────────────────

function startMock() {
  return new Promise((resolve) => {
    const srv = http.createServer((req, res) => {
      if (req.headers['x-bridge-token'] !== TOKEN) {
        res.writeHead(401); res.end('{"error":"unauthorized"}'); return;
      }
      res.writeHead(200, { 'Content-Type': 'application/json' });
      const { method, url } = req;
      if      (method === 'GET'  && url === '/account')
        res.end(JSON.stringify({ account: 'Sim101', cash_value: 50000,
          realized_pnl: 350.50, unrealized_pnl: 0, trading_enabled: true }));
      else if (method === 'GET'  && url === '/position')
        res.end(JSON.stringify({ instrument: 'NQ 09-26', market_position: 'FLAT', quantity: 0 }));
      else if (method === 'GET'  && url === '/trades/today')
        res.end(JSON.stringify({ trades: [], note: 'placeholder' }));
      else if (method === 'POST' && url === '/strategy/enable')
        res.end('{"trading_enabled":true}');
      else if (method === 'POST' && url === '/strategy/disable')
        res.end('{"trading_enabled":false}');
      else if (method === 'GET'  && url === '/setup')
        res.end(JSON.stringify({
          trend: 'BULLISH', pre_market_ready: true,
          pre_market_high: 21500.25, pre_market_low: 21420.50,
          sweep: 'DETECTED', sweep_level: 21420.50,
          setup: 'ACTIVE_LONG', fvg_lower: 21440.00, fvg_upper: 21455.75,
          price_in_fvg: false, traded_today: false,
          trading_disabled: false, trading_enabled: true,
        }));
      else { res.writeHead(404); res.end('{"error":"not found"}'); }
    });
    srv.listen(PORT, '127.0.0.1', () => resolve(srv));
  });
}

// ─── MCP client (JSON-RPC sobre stdio) ───────────────────────────────────────

let _proc, _rl;
const _pending = new Map(); // id → { resolve, reject, timer }

function initMcp() {
  _proc = spawn('node', ['mcp/dist/index.js'], {
    env: { ...process.env, BRIDGE_URL: `http://127.0.0.1:${PORT}`, BRIDGE_TOKEN: TOKEN },
    stdio: ['pipe', 'pipe', 'pipe'],
  });
  _proc.stderr.on('data', (d) => process.stderr.write(d));
  _rl = readline.createInterface({ input: _proc.stdout });
  _rl.on('line', (line) => {
    try {
      const msg = JSON.parse(line);
      const p = _pending.get(msg.id);
      if (p) { clearTimeout(p.timer); _pending.delete(msg.id); p.resolve(msg); }
    } catch { /* linea no-JSON (stderr redirigido, etc) */ }
  });
}

function send(id, method, params = {}) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      _pending.delete(id);
      reject(new Error(`timeout esperando respuesta id=${id}`));
    }, 8000);
    _pending.set(id, { resolve, reject, timer });
    _proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  });
}

function notify(method, params = {}) {
  _proc.stdin.write(JSON.stringify({ jsonrpc: '2.0', method, params }) + '\n');
}

// ─── Suite de tests ───────────────────────────────────────────────────────────

const TESTS = [
  { id: 1, name: 'get_account',      args: {},                    expect: 'cash_value'      },
  { id: 2, name: 'get_position',     args: {},                    expect: 'market_position' },
  { id: 3, name: 'get_today_trades', args: {},                    expect: 'trades'          },
  { id: 4, name: 'enable_strategy',  args: {},                    expect: 'trading_enabled' },
  { id: 5, name: 'disable_strategy', args: {},                    expect: 'trading_enabled' },
  { id: 6, name: 'check_market',     args: { date: '2026-06-05'}, expect: 'trading_day'     },
  { id: 7, name: 'get_setup_state', args: {},                    expect: 'pre_market_ready' },
];

async function main() {
  console.log('▶  Mock AddOn arrancando...');
  const srv = await startMock();
  console.log('▶  MCP server arrancando...');
  initMcp();
  await new Promise(r => setTimeout(r, 600)); // esperar init del proceso

  // Handshake MCP obligatorio antes de llamar tools
  await send(0, 'initialize', {
    protocolVersion: '2024-11-05',
    capabilities:    {},
    clientInfo:      { name: 'test-runner', version: '1.0' },
  });
  notify('notifications/initialized');

  let passed = 0, failed = 0;

  for (const t of TESTS) {
    try {
      const res  = await send(t.id, 'tools/call', { name: t.name, arguments: t.args });
      const text = res.result?.content?.[0]?.text ?? '';
      const isError = res.result?.isError;

      if (isError || !text.includes(t.expect)) {
        console.log(`  FAIL  ${t.name}`);
        console.log(`        respuesta: ${text.slice(0, 150)}`);
        failed++;
      } else {
        console.log(`  PASS  ${t.name}: ${text.slice(0, 80)}${text.length > 80 ? '...' : ''}`);
        passed++;
      }
    } catch (e) {
      console.log(`  FAIL  ${t.name}: ${e.message}`);
      failed++;
    }
  }

  console.log(`\n${passed}/${TESTS.length} tests OK${failed > 0 ? ` — ${failed} FALLO(S)` : ' — todo limpio'}`);

  _proc.kill();
  srv.close();
  process.exit(failed > 0 ? 1 : 0);
}

main().catch(e => { console.error(e); process.exit(1); });

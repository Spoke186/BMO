#!/usr/bin/env python3
"""
FASE 4 — H2c: CHOP ∩ T2 FILTER ON SESSION_CLOSE

Pregunta: Si eliminamos SESSION_CLOSE que son simultáneamente CHOP (<300) Y T2 (10:20-11:10),
¿qué ocurre con Net, WR, PF de SESSION_CLOSE?

Convergencia de 3 señales independientes:
  H1: T2=41% de SC losers | CHOP=53% de SC losers
  H2b: SC T2 inferior pero rentable (eliminar todo T2 daña Net)
  H2a: SC CHOP T2 = 0 winners, ~3 losers

Hipótesis: el daño está en la intersección, no en CHOP solo ni T2 solo.

Regla: solo descripción. No implementación. No filtros en código.

Uso: python h2c_chop_t2_sc.py IS:csv OOS1:csv OOS2:csv OOS3:csv
"""
from __future__ import annotations
import sys, csv, re, statistics
from datetime import date, datetime, timedelta

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

TP_USD = 1050.0
SL_USD =  375.0
KZ_START = 9 * 60 + 30
KZ_END   = 12 * 60
KZ_DUR   = KZ_END - KZ_START

ATR_BUCKETS = [
    (0,    300, "CHOP <300   "),
    (300,  380, "WEAK 300-380"),
    (380,  450, "ACTV 380-450"),
    (450, 9999, "STRG >450   "),
]

# ─── helpers ──────────────────────────────────────────────────────────────────

def parse_money(s):
    s = re.sub(r'[$ ]', '', s.strip()).replace('.','').replace(',','.')
    return float(s)

def parse_date(s):
    s = re.sub(r'\s+',' ', s.strip())
    m = re.match(r'(\d+)/(\d+)/(\d{4})', s)
    return date(int(m.group(3)), int(m.group(2)), int(m.group(1)))

def parse_datetime(s):
    s = re.sub(r'\s+',' ', s.strip())
    m = re.match(r'(\d+)/(\d+)/(\d{4})\s+(\d+):(\d+):(\d+)', s)
    if not m: return None
    d_,mo,y,h,mi,sec = (int(x) for x in m.groups())
    ampm = "p" in s.lower()[s.lower().rfind(str(sec)):]
    hr = h+12 if ampm and h<12 else (0 if not ampm and h==12 else h)
    try: return datetime(y,mo,d_,hr,mi,sec)
    except: return None

def kz_tercio(dt):
    if dt is None: return "?"
    mins = dt.hour*60 + dt.minute - KZ_START
    if mins < 0:         return "PRE"
    if mins < KZ_DUR/3: return "T1"
    if mins < 2*KZ_DUR/3: return "T2"
    return "T3"

def classify_exit(name):
    n = name.lower()
    if "profit target" in n: return "TP_FULL"
    if "stop loss" in n:     return "SL_FULL"
    if "session close" in n or "bar close" in n: return "SESSION_CLOSE"
    return "OTHER"

def atr_bucket(v):
    for lo,hi,lb in ATR_BUCKETS:
        if lo <= v < hi: return lb
    return "?"

def is_chop(t):    return t.get("bucket","").startswith("CHOP")
def is_t2(t):      return t.get("tercio") == "T2"
def is_chop_t2(t): return is_chop(t) and is_t2(t)

# ─── load ─────────────────────────────────────────────────────────────────────

def load_trades(path, label):
    trades = []
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        content = f.read()
    reader = csv.reader(content.splitlines(), delimiter=';')
    header = None
    for row in reader:
        if not any(c.strip() for c in row): continue
        if header is None:
            if "trade number" in " ".join(row).lower():
                header = [h.strip().lower() for h in row]
            continue
        rec = dict(zip(header, [c.strip() for c in row]))
        if not rec.get("trade number","").strip().isdigit(): continue
        try:
            entry_s = rec.get("entry time","")
            entry_t = parse_datetime(entry_s)
            t = {
                "period":    label,
                "pos":       rec.get("market pos.","").strip().lower(),
                "entry_dt":  parse_date(entry_s),
                "entry_dtt": entry_t,
                "profit":    parse_money(rec.get("profit","0")),
                "exit_type": classify_exit(rec.get("exit name","").strip()),
                "exit_name": rec.get("exit name","").strip(),
                "mae":       abs(parse_money(rec.get("mae","0"))),
                "mfe":       parse_money(rec.get("mfe","0")),
                "tercio":    kz_tercio(entry_t),
            }
            trades.append(t)
        except: pass
    return trades

def download_atr(start, end, window=20):
    import yfinance as yf
    d = yf.Ticker("NQ=F").history(start=start, end=end, interval="1d")
    dates = [x.date() for x in d.index]
    H,L,C = d["High"].tolist(), d["Low"].tolist(), d["Close"].tolist()
    trs = [H[i]-L[i] if i==0 else max(H[i]-L[i],abs(H[i]-C[i-1]),abs(L[i]-C[i-1]))
           for i in range(len(dates))]
    return {str(dates[i]): statistics.mean(trs[max(0,i-window+1):i+1])
            for i in range(len(dates))}

def tag_atr(trades, atr_map):
    for t in trades:
        for delta in range(5):
            k = str(t["entry_dt"] - timedelta(days=delta))
            if k in atr_map:
                t["atr"] = atr_map[k]; t["bucket"] = atr_bucket(t["atr"]); break
        else:
            t["atr"] = 0; t["bucket"] = "?"

# ─── stats ────────────────────────────────────────────────────────────────────

def stats(trades):
    if not trades:
        return dict(n=0, wr=0, net=0, pf=0, avg_w=0, avg_l=0, max_dd=0,
                    wins=0, losses=0)
    wins   = [t["profit"] for t in trades if t["profit"] > 0]
    losses = [t["profit"] for t in trades if t["profit"] <= 0]
    net    = sum(t["profit"] for t in trades)
    wr     = len(wins)/len(trades)
    avg_w  = statistics.mean(wins)   if wins   else 0
    avg_l  = statistics.mean(losses) if losses else 0
    pf     = sum(wins)/abs(sum(losses)) if losses and sum(losses)!=0 else float("inf")
    eq = [0.0]
    for t in sorted(trades, key=lambda x: x["entry_dt"]):
        eq.append(eq[-1]+t["profit"])
    peak = eq[0]; dd = 0.0
    for e in eq:
        if e > peak: peak = e
        if peak - e > dd: dd = peak - e
    return dict(n=len(trades), wr=wr, net=net, pf=pf,
                avg_w=avg_w, avg_l=avg_l, max_dd=dd,
                wins=len(wins), losses=len(losses))

def fmt_row(label, s, width=30):
    if s["n"] == 0:
        return f"  {label:{width}s}  n=0"
    pf_s = f"{s['pf']:5.2f}" if s['pf'] < 99 else "  inf"
    return (f"  {label:{width}s}"
            f"  n={s['n']:3d}"
            f"  WR={s['wr']*100:5.1f}%"
            f"  Net={s['net']:+8,.0f}"
            f"  PF={pf_s}"
            f"  AvgW={s['avg_w']:+6.0f}"
            f"  AvgL={s['avg_l']:+6.0f}")

def delta_row(label, base, alt, width=30):
    if base["n"] == 0 or alt["n"] == 0:
        return f"  {label:{width}s}  (insuf. datos)"
    d_wr  = (alt["wr"]  - base["wr"]) * 100
    d_net = alt["net"]  - base["net"]
    d_pf  = (alt["pf"]  - base["pf"]) if alt["pf"] < 99 and base["pf"] < 99 else None
    pf_s  = f"{d_pf:+.2f}" if d_pf is not None else "N/A"
    def flag(v, thr=0):
        if v > thr:  return "✅"
        if v < -thr: return "❌"
        return "≈"
    f_wr  = flag(d_wr,  0.5)
    f_net = flag(d_net, 50)
    f_pf  = flag(d_pf if d_pf else 0, 0.1)
    return (f"  {label:{width}s}"
            f"  ΔWR={d_wr:+5.1f}pp {f_wr}"
            f"  ΔNet={d_net:+8,.0f} {f_net}"
            f"  ΔPF={pf_s} {f_pf}")

# ─── main ─────────────────────────────────────────────────────────────────────

def main():
    if len(sys.argv) < 2:
        print("Uso: python h2c_chop_t2_sc.py LABEL:csv [LABEL2:csv ...]")
        sys.exit(1)

    periods = []
    for arg in sys.argv[1:]:
        label, path = arg.split(":", 1)
        periods.append((label, path))

    all_trades = []
    for label, path in periods:
        t = load_trades(path, label)
        print(f"  Cargado {label}: {len(t)} trades")
        all_trades.extend(t)

    print(f"\n  Total: {len(all_trades)} trades en {len(periods)} períodos")

    all_dates = [t["entry_dt"] for t in all_trades]
    start = min(all_dates) - timedelta(days=60)
    end   = max(all_dates) + timedelta(days=5)
    print(f"\n  Descargando ATR20D NQ=F {start} → {end} …")
    atr_map = download_atr(str(start), str(end))
    tag_atr(all_trades, atr_map)

    # ── clasificar ────────────────────────────────────────────────────────────
    sc_all      = [t for t in all_trades if t["exit_type"] == "SESSION_CLOSE"]
    sc_chop_t2  = [t for t in sc_all if is_chop_t2(t)]   # TARGET: eliminar
    sc_rest     = [t for t in sc_all if not is_chop_t2(t)]

    sys_base    = all_trades
    sys_alt     = [t for t in all_trades
                   if not (t["exit_type"] == "SESSION_CLOSE" and is_chop_t2(t))]

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 1 — INVENTARIO SC CHOP ∩ T2  (trades eliminados en H2c)")
    print("="*72)
    print(f"\n  SC total: {len(sc_all)}  |  CHOP∩T2: {len(sc_chop_t2)}  |  Resto: {len(sc_rest)}")
    print()

    if sc_chop_t2:
        print(f"  {'#':<4}  {'Per.':<5}  {'Fecha':<12}  {'Dir':<4}  {'ATR':>6}  "
              f"{'Tercio':<6}  {'MFE':>7}  {'MAE':>7}  {'Profit':>9}  R")
        print("  " + "-"*80)
        for i, t in enumerate(sorted(sc_chop_t2, key=lambda x: x["entry_dt"]), 1):
            r = "W" if t["profit"] > 0 else "L"
            print(f"  {i:<4}  {t['period']:<5}  {str(t['entry_dt']):<12}  "
                  f"{'L' if t['pos']=='long' else 'S':<4}  {t['atr']:>6.0f}  "
                  f"{t['tercio']:<6}  {t['mfe']:>7.0f}  {t['mae']:>7.0f}  "
                  f"{t['profit']:>+9,.0f}  {r}")
        wins_ct  = [t for t in sc_chop_t2 if t["profit"] > 0]
        loss_ct  = [t for t in sc_chop_t2 if t["profit"] <= 0]
        net_ct   = sum(t["profit"] for t in sc_chop_t2)
        print(f"\n  Ganadores: {len(wins_ct)}  Perdedores: {len(loss_ct)}")
        print(f"  Net CHOP∩T2: ${net_ct:+,.0f}")
        if wins_ct: print(f"  AvgW: ${statistics.mean(t['profit'] for t in wins_ct):+.0f}")
        if loss_ct: print(f"  AvgL: ${statistics.mean(t['profit'] for t in loss_ct):+.0f}")
    else:
        print("  (ningún SC coincide con CHOP y T2 simultáneamente)")

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 2 — SESSION_CLOSE: BASE vs H2c  [MÉTRICA PRIMARIA FASE 4]")
    print("="*72)

    s_base = stats(sc_all)
    s_alt  = stats(sc_rest)

    print("\n  BASE:")
    print(fmt_row("SC BASE", s_base))
    print("\n  H2c (sin CHOP∩T2):")
    print(fmt_row("SC H2c", s_alt))
    print("\n  DELTA:")
    print(delta_row("Δ SC", s_base, s_alt))

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 3 — SISTEMA GLOBAL")
    print("="*72)

    s_sys_b = stats(sys_base)
    s_sys_a = stats(sys_alt)

    print("\n  BASE:")
    print(fmt_row("Sistema BASE", s_sys_b))
    print(f"    MaxDD: ${s_sys_b['max_dd']:,.0f}")
    print("\n  H2c:")
    print(fmt_row("Sistema H2c", s_sys_a))
    print(f"    MaxDD: ${s_sys_a['max_dd']:,.0f}  (Δ=${s_sys_a['max_dd']-s_sys_b['max_dd']:+,.0f})")
    print("\n  DELTA:")
    print(delta_row("Δ Sistema", s_sys_b, s_sys_a))

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 4 — SC POR PERÍODO (CONSISTENCIA CROSS-PERIOD)")
    print("="*72)
    print()
    for p, _ in periods:
        sc_p_b   = [t for t in sc_all  if t["period"] == p]
        sc_p_a   = [t for t in sc_rest if t["period"] == p]
        removed  = [t for t in sc_chop_t2 if t["period"] == p]
        s_b = stats(sc_p_b)
        s_a = stats(sc_p_a)
        net_rm = sum(t["profit"] for t in removed)
        print(f"  [{p}]  eliminados: n={len(removed)}  Net eliminado=${net_rm:+,.0f}")
        print(f"    " + fmt_row("BASE", s_b))
        print(f"    " + fmt_row("H2c", s_a))
        print(f"    " + delta_row("Δ", s_b, s_a))
        print()

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 5 — MAPA COMPLETO SC POR BUCKET × TERCIO (BASE)")
    print("  Para entender distribución real y validar señal H2c")
    print("="*72)
    buckets  = ["CHOP <300   ", "WEAK 300-380", "ACTV 380-450", "STRG >450   "]
    tercios  = ["T1", "T2", "T3"]
    print()
    print(f"  {'Bucket':<14}  {'Tercio':<6}  {'n':>3}  {'W':>3}  {'L':>3}  "
          f"{'WR':>6}  {'Net':>9}  {'AvgMFE':>7}  {'AvgMAE':>7}  Nota")
    print("  " + "-"*80)
    for bk in buckets:
        for tc in tercios:
            grp = [t for t in sc_all if t["bucket"] == bk and t["tercio"] == tc]
            if not grp:
                continue
            ws = [t for t in grp if t["profit"] > 0]
            ls = [t for t in grp if t["profit"] <= 0]
            wr = len(ws)/len(grp)*100
            net = sum(t["profit"] for t in grp)
            avg_mfe = statistics.mean(t["mfe"] for t in grp)
            avg_mae = statistics.mean(t["mae"] for t in grp)
            nota = "  ← H2c elimina" if bk.startswith("CHOP") and tc == "T2" else ""
            print(f"  {bk:<14}  {tc:<6}  {len(grp):>3}  {len(ws):>3}  {len(ls):>3}  "
                  f"{wr:>5.0f}%  {net:>+9,.0f}  {avg_mfe:>7.0f}  {avg_mae:>7.0f}{nota}")
    print()

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 6 — VEREDICTO H2c")
    print("="*72)

    s_b = stats(sc_all)
    s_a = stats(sc_rest)

    d_net = s_a["net"] - s_b["net"]
    d_wr  = (s_a["wr"] - s_b["wr"]) * 100
    d_pf  = (s_a["pf"] - s_b["pf"]) if s_a["pf"] < 99 and s_b["pf"] < 99 else None

    net_ok = d_net >= 0
    wr_ok  = d_wr  >= 0
    pf_ok  = d_pf is None or d_pf >= 0

    def flag(v, thr=0):
        if v > thr:  return "✅"
        if v < -thr: return "❌"
        return "≈"

    print(f"\n  Trades eliminados (SC CHOP∩T2): n={len(sc_chop_t2)}")
    net_ct = sum(t["profit"] for t in sc_chop_t2)
    print(f"  Net eliminado: ${net_ct:+,.0f}")
    print()
    print(f"  ΔNet SC: ${d_net:+,.0f}  {flag(d_net, 50)}")
    print(f"  ΔWR  SC: {d_wr:+.1f}pp  {flag(d_wr, 0.5)}")
    if d_pf is not None:
        print(f"  ΔPF  SC: {d_pf:+.2f}     {flag(d_pf, 0.1)}")
    else:
        print(f"  ΔPF  SC: inf (sin pérdidas en grupo restante)")

    all_ok  = net_ok and wr_ok and pf_ok
    none_ok = not net_ok and not wr_ok and not pf_ok

    print("\n  VEREDICTO:")
    if all_ok:
        print("  ✅ H2c MEJORA SESSION_CLOSE (Net+WR+PF todos positivos o neutros).")
        print("     Muestra pequeña — validar cross-period antes de implementar.")
    elif none_ok:
        print("  ❌ H2c DAÑA SESSION_CLOSE. Hipótesis descartada.")
    else:
        print("  ⚠️  H2c resultado MIXTO.")
        if net_ok:
            print(f"     Net mejora ${d_net:+,.0f} → SC CHOP∩T2 destruye valor neto.")
        else:
            print(f"     Net cae ${d_net:+,.0f} → SC CHOP∩T2 contiene trades rentables.")
        if wr_ok:  print(f"     WR mejora {d_wr:+.1f}pp")
        if pf_ok and d_pf: print(f"     PF mejora {d_pf:+.2f}")

    # Contexto muestra
    n_chop_t2 = len(sc_chop_t2)
    n_sc      = len(sc_all)
    pct       = n_chop_t2/n_sc*100 if n_sc else 0
    print(f"\n  Contexto muestra: {n_chop_t2} trades eliminados = {pct:.0f}% del SC total.")
    if n_chop_t2 < 10:
        print(f"  ADVERTENCIA: n={n_chop_t2} es pequeño. Resultados estadísticamente débiles.")
        print("  Cualquier mejora requiere validación adicional antes de implementación.")
    print()


if __name__ == "__main__":
    main()

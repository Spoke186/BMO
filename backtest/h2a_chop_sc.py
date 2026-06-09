#!/usr/bin/env python3
"""
FASE 4 — H2a: CHOP FILTER ON SESSION_CLOSE

Pregunta: Si eliminamos SESSION_CLOSE en régimen CHOP (<300 ATR20D),
¿qué ocurre con Net, WR, PF de SESSION_CLOSE?

Señal previa (H1): CHOP 53% de SC losers vs 29% SC winners.
Señal previa (FASE 2): CHOP PF=0.77 sistema global.
Señal previa (FASE 3): CHOP SC PF=3.32 (positivo pero dudoso cross-period).

Regla: solo descripción. No implementación. No filtros en código.

Uso: python h2a_chop_sc.py IS:csv OOS1:csv OOS2:csv OOS3:csv
"""
from __future__ import annotations
import sys, csv, re, statistics
from datetime import date, datetime, timedelta
from collections import defaultdict

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
    if mins < 0:              return "PRE"
    if mins < KZ_DUR/3:      return "T1"
    if mins < 2*KZ_DUR/3:    return "T2"
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

def is_chop(t):
    return t.get("bucket","").startswith("CHOP")

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

def fmt_row(label, s, width=28):
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

def delta_row(label, base, alt, width=28):
    if base["n"] == 0 or alt["n"] == 0:
        return f"  {label:{width}s}  (insuf. datos)"
    d_wr  = (alt["wr"]  - base["wr"])  * 100
    d_net = alt["net"]  - base["net"]
    d_pf  = alt["pf"]   - base["pf"]  if alt["pf"] < 99 and base["pf"] < 99 else None
    pf_s  = f"{d_pf:+.2f}" if d_pf is not None else "  N/A"
    # flags
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
    if len(sys.argv) < 5:
        print("Uso: python h2a_chop_sc.py IS:path OOS1:path OOS2:path OOS3:path")
        sys.exit(1)

    periods = []
    for arg in sys.argv[1:5]:
        label, path = arg.split(":", 1)
        periods.append((label, path))

    all_trades = []
    for label, path in periods:
        t = load_trades(path, label)
        print(f"  Cargado {label}: {len(t)} trades")
        all_trades.extend(t)

    print(f"\n  Total: {len(all_trades)} trades en {len(periods)} períodos")

    # date range for ATR download
    all_dates = [t["entry_dt"] for t in all_trades]
    start = min(all_dates) - timedelta(days=60)
    end   = max(all_dates) + timedelta(days=5)
    print(f"\n  Descargando ATR20D NQ=F {start} → {end} …")
    atr_map = download_atr(str(start), str(end))
    tag_atr(all_trades, atr_map)

    not_tagged = sum(1 for t in all_trades if t["bucket"] == "?")
    if not_tagged:
        print(f"  WARN: {not_tagged} trades sin ATR tag")

    # ── clasificar ────────────────────────────────────────────────────────────
    sc_all  = [t for t in all_trades if t["exit_type"] == "SESSION_CLOSE"]
    sc_chop = [t for t in sc_all if is_chop(t)]
    sc_nochop = [t for t in sc_all if not is_chop(t)]

    # contrafactual: sistema sin SC CHOP
    sys_base    = all_trades
    sys_noscchop = [t for t in all_trades
                    if not (t["exit_type"] == "SESSION_CLOSE" and is_chop(t))]

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 1 — INVENTARIO SESSION_CLOSE CHOP")
    print("="*72)
    print(f"\n  Total SC: {len(sc_all)}  |  SC CHOP: {len(sc_chop)}  |  SC no-CHOP: {len(sc_nochop)}")
    print()
    print(f"  {'#':<4}  {'Período':<6}  {'Fecha':<12}  {'Dir':<5}  {'ATR':>6}  "
          f"{'MFE':>7}  {'MAE':>7}  {'Profit':>9}  {'Exit':<20}")
    print("  " + "-"*90)
    for i, t in enumerate(sorted(sc_chop, key=lambda x: x["entry_dt"]), 1):
        result = "WIN" if t["profit"] > 0 else "LOSS"
        print(f"  {i:<4}  {t['period']:<6}  {str(t['entry_dt']):<12}  "
              f"{'L' if t['pos']=='long' else 'S':<5}  {t['atr']:>6.0f}  "
              f"{t['mfe']:>7.0f}  {t['mae']:>7.0f}  {t['profit']:>+9,.0f}  "
              f"{t['exit_name'][:20]:<20}  {result}")

    wins_sc_chop  = [t for t in sc_chop if t["profit"] > 0]
    loss_sc_chop  = [t for t in sc_chop if t["profit"] <= 0]
    print(f"\n  SC CHOP: {len(wins_sc_chop)} ganadores  {len(loss_sc_chop)} perdedores")
    if sc_chop:
        print(f"  Net SC CHOP: ${sum(t['profit'] for t in sc_chop):+,.0f}")
        print(f"  WR SC CHOP:  {len(wins_sc_chop)/len(sc_chop)*100:.1f}%")
        if loss_sc_chop:
            pf = sum(t['profit'] for t in wins_sc_chop) / abs(sum(t['profit'] for t in loss_sc_chop))
            print(f"  PF SC CHOP:  {pf:.2f}")

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 2 — COMPARATIVA SESSION_CLOSE (BASE vs SIN SC-CHOP)")
    print("  MÉTRICA PRIMARIA DE FASE 4")
    print("="*72)

    s_sc_base   = stats(sc_all)
    s_sc_alt    = stats(sc_nochop)

    print("\n  BASE:")
    print(fmt_row("SC BASE", s_sc_base))
    print("\n  CONTRAFACTUAL (sin SC CHOP):")
    print(fmt_row("SC sin CHOP", s_sc_alt))
    print("\n  DELTA:")
    print(delta_row("Δ SC (sin SC-CHOP vs base)", s_sc_base, s_sc_alt))

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 3 — COMPARATIVA SISTEMA GLOBAL")
    print("="*72)

    s_sys_base = stats(sys_base)
    s_sys_alt  = stats(sys_noscchop)

    print("\n  BASE:")
    print(fmt_row("Sistema BASE", s_sys_base))
    print(f"    MaxDD base: ${s_sys_base['max_dd']:,.0f}")
    print("\n  CONTRAFACTUAL (sin SC CHOP):")
    print(fmt_row("Sistema sin SC-CHOP", s_sys_alt))
    print(f"    MaxDD alt:  ${s_sys_alt['max_dd']:,.0f}")
    print(f"    ΔDD: ${s_sys_alt['max_dd'] - s_sys_base['max_dd']:+,.0f}")
    print("\n  DELTA sistema:")
    print(delta_row("Δ Sistema", s_sys_base, s_sys_alt))

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 4 — SC POR ATR BUCKET (BASE)")
    print("  Muestra distribución — identifica dónde vive el valor SC")
    print("="*72)
    print()
    bk_order = ["CHOP <300   ", "WEAK 300-380", "ACTV 380-450", "STRG >450   "]
    for bk in bk_order:
        group = [t for t in sc_all if t["bucket"] == bk]
        s = stats(group)
        marker = "  ← ELIMINADO en H2a" if bk.startswith("CHOP") else ""
        print(fmt_row(bk, s) + marker)

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 5 — SC POR PERÍODO (CONSISTENCIA CROSS-PERIOD)")
    print("  Misma pregunta: ¿H2a mejora en todos los períodos?")
    print("="*72)
    print()
    period_order = [p[0] for p in periods]
    for p in period_order:
        sc_p_base = [t for t in sc_all if t["period"] == p]
        sc_p_alt  = [t for t in sc_nochop if t["period"] == p]
        sc_p_chop = [t for t in sc_chop if t["period"] == p]
        s_base = stats(sc_p_base)
        s_alt  = stats(sc_p_alt)
        n_chop = len(sc_p_chop)
        net_chop = sum(t["profit"] for t in sc_p_chop)
        print(f"  [{p}]  SC CHOP eliminados: n={n_chop}  Net SC CHOP=${net_chop:+,.0f}")
        print(f"  {'':4}" + fmt_row("BASE", s_base))
        print(f"  {'':4}" + fmt_row("SIN SC-CHOP", s_alt))
        print(f"  {'':4}" + delta_row("Δ", s_base, s_alt))
        print()

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 6 — SC CHOP ANATOMY")
    print("  Comparar ganadores vs perdedores dentro de SC CHOP")
    print("="*72)
    if sc_chop:
        wins  = [t for t in sc_chop if t["profit"] > 0]
        losses= [t for t in sc_chop if t["profit"] <= 0]
        print(f"\n  SC CHOP ganadores: n={len(wins)}")
        if wins:
            print(f"    MFE avg: {statistics.mean(t['mfe'] for t in wins):.0f}")
            print(f"    MAE avg: {statistics.mean(t['mae'] for t in wins):.0f}")
            print(f"    Net avg: {statistics.mean(t['profit'] for t in wins):+.0f}")
            tercs = [t["tercio"] for t in wins]
            for k in ["T1","T2","T3"]:
                print(f"    {k}: {tercs.count(k)} ({tercs.count(k)/len(wins)*100:.0f}%)")
        print(f"\n  SC CHOP perdedores: n={len(losses)}")
        if losses:
            print(f"    MFE avg: {statistics.mean(t['mfe'] for t in losses):.0f}")
            print(f"    MAE avg: {statistics.mean(t['mae'] for t in losses):.0f}")
            print(f"    Net avg: {statistics.mean(t['profit'] for t in losses):+.0f}")
            tercs = [t["tercio"] for t in losses]
            for k in ["T1","T2","T3"]:
                print(f"    {k}: {tercs.count(k)} ({tercs.count(k)/len(losses)*100:.0f}%)")

    # ─────────────────────────────────────────────────────────────────────────
    print("\n" + "="*72)
    print("  BLOQUE 7 — VEREDICTO H2a")
    print("="*72)

    s_sc_base = stats(sc_all)
    s_sc_alt  = stats(sc_nochop)

    d_net = s_sc_alt["net"] - s_sc_base["net"]
    d_wr  = (s_sc_alt["wr"] - s_sc_base["wr"]) * 100
    d_pf  = (s_sc_alt["pf"] - s_sc_base["pf"]) if s_sc_alt["pf"] < 99 and s_sc_base["pf"] < 99 else None

    net_ok = d_net >= 0
    wr_ok  = d_wr  >= 0
    pf_ok  = d_pf is None or d_pf >= 0

    all_ok  = net_ok and wr_ok and pf_ok
    none_ok = not net_ok and not wr_ok and not pf_ok
    mixed   = not all_ok and not none_ok

    print(f"\n  ΔNet SC: ${d_net:+,.0f}  {'✅' if net_ok else '❌'}")
    print(f"  ΔWR  SC: {d_wr:+.1f}pp  {'✅' if wr_ok else '❌'}")
    if d_pf is not None:
        print(f"  ΔPF  SC: {d_pf:+.2f}     {'✅' if pf_ok else '❌'}")
    else:
        print(f"  ΔPF  SC: N/A (inf)")

    chop_pct = len(sc_chop)/len(sc_all)*100 if sc_all else 0
    chop_net = sum(t["profit"] for t in sc_chop)

    print(f"\n  SC CHOP: {len(sc_chop)} trades ({chop_pct:.0f}% del SC total)")
    print(f"  Net SC CHOP eliminado: ${chop_net:+,.0f}")

    print("\n  VEREDICTO:")
    if all_ok:
        print("  ✅ H2a MEJORA SESSION_CLOSE (Net+WR+PF todos positivos).")
        print("     Candidato para implementación en NinjaScript.")
    elif none_ok:
        print("  ❌ H2a DAÑA SESSION_CLOSE (Net+WR+PF todos negativos).")
        print("     Hipótesis descartada.")
    else:
        print("  ⚠️  H2a resultado MIXTO. Evaluar caso a caso.")
        if net_ok:
            print("     Net mejora → SC CHOP es destructor de valor neto.")
        else:
            print("     Net cae → SC CHOP contiene trades rentables.")
        if wr_ok:
            print("     WR mejora → SC CHOP tiene más perdedores que la media.")
        if pf_ok and d_pf is not None:
            print("     PF mejora → eficiencia mejor sin SC CHOP.")

    print()


if __name__ == "__main__":
    main()

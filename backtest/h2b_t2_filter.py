#!/usr/bin/env python3
"""
FASE 4 — H2b: CONTRAFACTUAL T2 FILTER

Pregunta: Si eliminamos entradas T2 (10:20-11:10 ET),
¿qué ocurre con Net, WR, PF y SESSION_CLOSE?

Regla: solo descripción. No implementación. No filtros en código de estrategia.

Uso: python h2b_t2_filter.py IS:csv OOS1:csv OOS2:csv OOS3:csv
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
KZ_START = 9 * 60 + 30   # 9:30 ET
KZ_END   = 12 * 60
KZ_DUR   = KZ_END - KZ_START  # 150 min

ATR_BUCKETS = [
    (0,    300, "CHOP <300   "),
    (300,  380, "WEAK 300-380"),
    (380,  450, "ACTV 380-450"),
    (450, 9999, "STRG >450   "),
]

# ─── helpers ─────────────────────────────────────────────────────────────────

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
        return dict(n=0, wr=0, net=0, pf=0, avg_w=0, avg_l=0, max_dd=0)
    wins   = [t["profit"] for t in trades if t["profit"] > 0]
    losses = [t["profit"] for t in trades if t["profit"] <= 0]
    net    = sum(t["profit"] for t in trades)
    wr     = len(wins)/len(trades)
    avg_w  = statistics.mean(wins)   if wins   else 0
    avg_l  = statistics.mean(losses) if losses else 0
    pf     = sum(wins)/abs(sum(losses)) if losses and sum(losses)!=0 else float("inf")
    # Max DD (equity curve sorted by date)
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
            f"  PF={pf_s}"
            f"  Net={s['net']:+8.0f}"
            f"  AvgW={s['avg_w']:+6.0f}"
            f"  AvgL={s['avg_l']:+6.0f}"
            f"  DD={-s['max_dd']:+7.0f}")

def delta_row(label, s_base, s_new, width=28):
    d_net = s_new["net"] - s_base["net"]
    d_wr  = (s_new["wr"] - s_base["wr"]) * 100
    d_pf  = s_new["pf"] - s_base["pf"] if s_base["pf"] < 99 and s_new["pf"] < 99 else 0
    d_dd  = s_new["max_dd"] - s_base["max_dd"]
    sign_net = "▲" if d_net > 0 else "▼"
    sign_dd  = "▼" if d_dd < 0 else "▲"  # less DD is better (▼)
    return (f"  {label:{width}s}"
            f"  Δn={s_new['n']-s_base['n']:+4d}"
            f"  ΔWR={d_wr:+5.1f}pp"
            f"  ΔPF={d_pf:+5.2f}"
            f"  ΔNet={d_net:+8.0f} {sign_net}"
            f"  ΔDD={-d_dd:+7.0f} {sign_dd}")

# ─── main ─────────────────────────────────────────────────────────────────────

def main():
    if len(sys.argv) < 2:
        print("Uso: python h2b_t2_filter.py LABEL:csv [...]")
        sys.exit(1)

    all_trades = []
    for arg in sys.argv[1:]:
        label, path = arg.split(":",1) if ":" in arg else (arg,arg)
        t = load_trades(path, label)
        all_trades.extend(t)
        print(f"Loaded {len(t):3d} trades [{label}]")
    print(f"Total: {len(all_trades)}\n")

    dates = [t["entry_dt"] for t in all_trades]
    print("Descargando ATR...")
    atr_map = download_atr(str(min(dates)-timedelta(60)), str(max(dates)+timedelta(5)))
    tag_atr(all_trades, atr_map)
    print()

    # Separar T2 del resto
    t2_trades   = [t for t in all_trades if t["tercio"] == "T2"]
    no_t2_trades= [t for t in all_trades if t["tercio"] != "T2"]

    sc_base = [t for t in all_trades  if t["exit_type"]=="SESSION_CLOSE"]
    sc_no_t2= [t for t in no_t2_trades if t["exit_type"]=="SESSION_CLOSE"]

    # ─── BLOQUE 1: Inventario T2 ─────────────────────────────────────────────
    print("="*100)
    print("BLOQUE 1 — INVENTARIO COMPLETO DE TRADES T2 (10:20-11:10 ET)")
    print("="*100)
    print(f"\n  {'Fecha':12}  {'Per':5}  {'Pos':5}  {'Exit':14}  {'Bucket':14}  {'ATR':>4}  {'Profit':>7}  {'MFE':>5}  {'MAE':>5}")
    print("-"*95)
    net_t2 = 0
    for t in sorted(t2_trades, key=lambda x: x["entry_dt"]):
        net_t2 += t["profit"]
        print(f"  {str(t['entry_dt']):12}  [{t['period']:5}]  {t['pos']:5}  {t['exit_type']:14}  "
              f"{t['bucket']:14}  {t['atr']:4.0f}  {t['profit']:+7.0f}  {t['mfe']:5.0f}  {t['mae']:5.0f}")
    t2_sc = [t for t in t2_trades if t["exit_type"]=="SESSION_CLOSE"]
    t2_tp = [t for t in t2_trades if t["exit_type"]=="TP_FULL"]
    t2_sl = [t for t in t2_trades if t["exit_type"]=="SL_FULL"]
    print(f"\n  T2 total: n={len(t2_trades)}  Net={net_t2:+.0f}")
    print(f"  Breakdown: SC={len(t2_sc)}  TP={len(t2_tp)}  SL={len(t2_sl)}")

    # ─── BLOQUE 2: Sistema BASE vs SIN T2 ────────────────────────────────────
    print("\n" + "="*100)
    print("BLOQUE 2 — SISTEMA COMPLETO: CON T2 vs SIN T2")
    print("="*100)
    print()
    base_all = stats(all_trades)
    new_all  = stats(no_t2_trades)
    print(fmt_row("BASE (con T2)",   base_all))
    print(fmt_row("SIN T2",          new_all))
    print(delta_row("DELTA",         base_all, new_all))

    # ─── BLOQUE 3: SESSION_CLOSE BASE vs SIN T2 ──────────────────────────────
    print("\n" + "="*100)
    print("BLOQUE 3 — SESSION_CLOSE: CON T2 vs SIN T2  (métrica principal)")
    print("="*100)
    print()
    base_sc = stats(sc_base)
    new_sc  = stats(sc_no_t2)
    print(fmt_row("SC BASE (con T2)", base_sc))
    print(fmt_row("SC SIN T2",        new_sc))
    print(delta_row("SC DELTA",       base_sc, new_sc))

    # ─── BLOQUE 4: T2 trades por exit type ───────────────────────────────────
    print("\n" + "="*100)
    print("BLOQUE 4 — T2 DESGLOSE POR EXIT TYPE")
    print("="*100)
    print()
    for et_label, et_key in [("TP_FULL","TP_FULL"),("SL_FULL","SL_FULL"),("SESSION_CLOSE","SESSION_CLOSE")]:
        grp = [t for t in t2_trades if t["exit_type"]==et_key]
        s   = stats(grp)
        print(fmt_row(f"T2 {et_label}", s))
    print()
    print("  (Para referencia — mismos exit types fuera de T2:)")
    for et_label, et_key in [("TP_FULL","TP_FULL"),("SL_FULL","SL_FULL"),("SESSION_CLOSE","SESSION_CLOSE")]:
        grp = [t for t in no_t2_trades if t["exit_type"]==et_key]
        s   = stats(grp)
        print(fmt_row(f"No-T2 {et_label}", s))

    # ─── BLOQUE 5: Por ATR bucket — con vs sin T2 ────────────────────────────
    print("\n" + "="*100)
    print("BLOQUE 5 — SESSION_CLOSE POR ATR BUCKET: CON T2 vs SIN T2")
    print("="*100)
    print()
    for _,_,blabel in ATR_BUCKETS:
        sc_b = [t for t in sc_base  if t["bucket"]==blabel]
        sc_n = [t for t in sc_no_t2 if t["bucket"]==blabel]
        if not sc_b: continue
        t2_in_bucket = [t for t in t2_trades if t["bucket"]==blabel and t["exit_type"]=="SESSION_CLOSE"]
        sb = stats(sc_b); sn = stats(sc_n)
        print(fmt_row(f"SC {blabel.strip()} BASE", sb))
        print(fmt_row(f"SC {blabel.strip()} SIN T2", sn))
        print(f"    (T2 SC eliminados en este bucket: n={len(t2_in_bucket)})")
        print()

    # ─── BLOQUE 6: Por período — verificar consistencia ──────────────────────
    print("="*100)
    print("BLOQUE 6 — SC POR PERÍODO: CON T2 vs SIN T2 (verificar consistencia cross-period)")
    print("="*100)
    print()
    periods = sorted(set(t["period"] for t in all_trades))
    for p in periods:
        sc_b = [t for t in sc_base  if t["period"]==p]
        sc_n = [t for t in sc_no_t2 if t["period"]==p]
        t2_sc_p = [t for t in t2_sc if t["period"]==p]
        sb = stats(sc_b); sn = stats(sc_n)
        print(fmt_row(f"SC {p} BASE", sb))
        print(fmt_row(f"SC {p} SIN T2", sn))
        print(f"    (T2 SC eliminados este período: n={len(t2_sc_p)})")
        print()

    # ─── BLOQUE 7: Resumen ejecutivo ─────────────────────────────────────────
    print("="*100)
    print("BLOQUE 7 — RESUMEN EJECUTIVO H2b")
    print("="*100)
    print()
    print(f"  Trades T2 eliminados: n={len(t2_trades)}")
    print(f"  SC T2 eliminados:     n={len(t2_sc)}")
    print()
    print(f"  SISTEMA TOTAL:")
    print(f"    Base Net={base_all['net']:+.0f}  →  Sin T2 Net={new_all['net']:+.0f}"
          f"  Δ={new_all['net']-base_all['net']:+.0f}")
    print(f"    Base DD={-base_all['max_dd']:+.0f}  →  Sin T2 DD={-new_all['max_dd']:+.0f}"
          f"  Δ={-(new_all['max_dd']-base_all['max_dd']):+.0f}")
    print()
    print(f"  SESSION_CLOSE (métrica principal):")
    print(f"    Base  Net={base_sc['net']:+.0f}  WR={base_sc['wr']*100:.1f}%  PF={base_sc['pf']:.2f}")
    print(f"    Sin T2 Net={new_sc['net']:+.0f}  WR={new_sc['wr']*100:.1f}%  PF={new_sc['pf']:.2f}")
    d_net = new_sc["net"] - base_sc["net"]
    d_wr  = (new_sc["wr"] - base_sc["wr"])*100
    d_pf  = new_sc["pf"] - base_sc["pf"] if base_sc["pf"]<99 and new_sc["pf"]<99 else 0
    print(f"    Delta: ΔNet={d_net:+.0f}  ΔWR={d_wr:+.1f}pp  ΔPF={d_pf:+.2f}")
    print()
    if d_net >= 0 and d_wr >= 0 and d_pf >= 0:
        verdict = "H2b MEJORA SESSION_CLOSE en todas las métricas."
    elif d_net < 0 or d_pf < 0:
        verdict = "H2b DAÑA SESSION_CLOSE (Net o PF bajan). Hipótesis descartada en esta forma."
    else:
        verdict = "H2b muestra resultado mixto. Requiere análisis adicional."
    print(f"  VEREDICTO: {verdict}")
    print()


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""
FASE 4 — H1: ANATOMÍA SESSION_CLOSE PERDEDORES

Pregunta: ¿Qué tienen en común los 17 SC perdedores que no tienen los 38 ganadores?

Variables analizadas:
  - ATR bucket
  - Long / Short
  - Hora de entrada (tercio kill zone)
  - Día de la semana
  - Duración (minutos)
  - MFE (excursión favorable máx)
  - MAE (excursión adversa máx)
  - MAE/MFE ratio
  - Setup A/B

Regla: solo descripción. No filtros. No implementación.

Uso: python analyze_sc_losers.py IS:csv OOS1:csv OOS2:csv OOS3:csv
"""
from __future__ import annotations
import sys, csv, re, statistics
from datetime import date, datetime, timedelta
from collections import defaultdict, Counter

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

TP_USD = 1050.0
SL_USD =  375.0
KZ_START = 9 * 60 + 30
KZ_END   = 12 * 60
KZ_DUR   = KZ_END - KZ_START
DAYS = ["Lun", "Mar", "Mie", "Jue", "Vie", "Sab", "Dom"]

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
    if mins < 0: return "PRE"
    if mins < KZ_DUR/3:   return "T1 9:30-10:20"
    if mins < 2*KZ_DUR/3: return "T2 10:20-11:10"
    return "T3 11:10-12:00"

def atr_bucket(v):
    for lo,hi,lb in ATR_BUCKETS:
        if lo <= v < hi: return lb
    return "?"

def classify_exit(name):
    n = name.lower()
    if "profit target" in n: return "TP_FULL"
    if "stop loss" in n:     return "SL_FULL"
    if "session close" in n or "bar close" in n: return "SESSION_CLOSE"
    return "OTHER"

def pct(vals, p):
    if not vals: return 0
    sv = sorted(vals)
    idx = (len(sv)-1)*p/100
    lo,hi = int(idx), min(int(idx)+1, len(sv)-1)
    return sv[lo]+(sv[hi]-sv[lo])*(idx-lo)

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
            profit  = parse_money(rec.get("profit","0"))
            mae     = abs(parse_money(rec.get("mae","0")))
            mfe     = parse_money(rec.get("mfe","0"))
            exit_n  = rec.get("exit name","").strip()
            entry_s = rec.get("entry time","")
            entry_d = parse_date(entry_s)
            entry_t = parse_datetime(entry_s)
            exit_t  = parse_datetime(rec.get("exit time",""))
            dur = None
            if entry_t and exit_t:
                d = (exit_t - entry_t).total_seconds()/60
                if d >= 0: dur = d
            trades.append({
                "period":    label,
                "pos":       rec.get("market pos.","").strip().lower(),
                "setup":     "A" if rec.get("entry name","") in ("LongFVG","ShortFVG") else "B",
                "entry_dt":  entry_d,
                "entry_dtt": entry_t,
                "profit":    profit,
                "exit_type": classify_exit(exit_n),
                "mae":       mae,
                "mfe":       mfe,
                "dur_min":   dur,
                "tercio":    kz_tercio(entry_t),
                "dow":       DAYS[entry_d.weekday()],
                "month":     entry_d.strftime("%Y-%m"),
            })
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
                t["atr"] = atr_map[k]
                t["bucket"] = atr_bucket(t["atr"])
                break
        else:
            t["atr"] = 0; t["bucket"] = "?"

# ─── comparison printer ───────────────────────────────────────────────────────

def cmp_num(label, wins, loss, fn, fmt=".0f"):
    wv = [fn(t) for t in wins if fn(t) is not None]
    lv = [fn(t) for t in loss if fn(t) is not None]
    wa = statistics.mean(wv) if wv else 0
    la = statistics.mean(lv) if lv else 0
    diff = la - wa
    sign = "▲" if diff > 0 else ("▼" if diff < 0 else "=")
    print(f"  {label:25s}  WIN avg={wa:{fmt}}  LOSS avg={la:{fmt}}  diff={diff:+{fmt}} {sign}")

def cmp_cat(label, wins, loss, fn):
    wc = Counter(fn(t) for t in wins)
    lc = Counter(fn(t) for t in loss)
    all_keys = sorted(set(list(wc)+list(lc)))
    print(f"  {label}:")
    for k in all_keys:
        w_n = wc.get(k,0); l_n = lc.get(k,0)
        w_pct = w_n/len(wins)*100 if wins else 0
        l_pct = l_n/len(loss)*100 if loss else 0
        flag = " ◄" if abs(l_pct - w_pct) > 15 else ""
        print(f"    {str(k):16s}  WIN={w_n:2d} ({w_pct:4.0f}%)  LOSS={l_n:2d} ({l_pct:4.0f}%){flag}")

# ─── main ─────────────────────────────────────────────────────────────────────

def main():
    if len(sys.argv) < 2:
        print("Uso: python analyze_sc_losers.py LABEL:csv [...]")
        sys.exit(1)

    all_trades = []
    for arg in sys.argv[1:]:
        label, path = arg.split(":",1) if ":" in arg else (arg, arg)
        t = load_trades(path, label)
        all_trades.extend(t)
        print(f"Loaded {len(t):3d} trades [{label}]")

    print(f"Total: {len(all_trades)}\n")
    dates = [t["entry_dt"] for t in all_trades]
    print("Descargando ATR...")
    atr_map = download_atr(str(min(dates)-timedelta(60)), str(max(dates)+timedelta(5)))
    tag_atr(all_trades, atr_map)

    sc_all   = [t for t in all_trades if t["exit_type"] == "SESSION_CLOSE"]
    sc_wins  = [t for t in sc_all if t["profit"] > 0]
    sc_loss  = [t for t in sc_all if t["profit"] <= 0]

    print(f"SESSION_CLOSE total: {len(sc_all)}  |  Ganadores: {len(sc_wins)}  |  Perdedores: {len(sc_loss)}\n")

    # ─── BLOQUE 1: Tabla individual perdedores ────────────────────────────────
    print("="*90)
    print("BLOQUE 1 — LISTA COMPLETA SESSION_CLOSE PERDEDORES")
    print("="*90)
    print(f"  {'Fecha':12}  {'Per':5}  {'Pos':5}  {'Bucket':14}  {'ATR':>4}  "
          f"{'Profit':>7}  {'MFE':>5}  {'MAE':>5}  {'M/M':>4}  {'Dur':>5}  {'T3':14}  {'Dow':3}  {'Setup':5}")
    print("-"*110)
    for t in sorted(sc_loss, key=lambda x: x["entry_dt"]):
        mm = t["mae"]/t["mfe"] if t["mfe"] > 0 else 999
        dur = f"{t['dur_min']:.0f}" if t["dur_min"] else "?"
        print(f"  {str(t['entry_dt']):12}  [{t['period']:5}]  {t['pos']:5}  "
              f"{t['bucket']:14}  {t['atr']:4.0f}  "
              f"{t['profit']:+7.0f}  {t['mfe']:5.0f}  {t['mae']:5.0f}  {mm:4.1f}  "
              f"{dur:>5}  {t['tercio']:14}  {t['dow']:3}  S{t['setup']}")

    # ─── BLOQUE 2: Comparación cuantitativa ──────────────────────────────────
    print("\n" + "="*90)
    print("BLOQUE 2 — COMPARACIÓN CUANTITATIVA: SC GANADORES vs SC PERDEDORES")
    print("="*90)
    print()
    cmp_num("Profit ($)",   sc_wins, sc_loss, lambda t: t["profit"])
    cmp_num("MFE ($)",      sc_wins, sc_loss, lambda t: t["mfe"])
    cmp_num("MAE ($)",      sc_wins, sc_loss, lambda t: t["mae"])
    cmp_num("MAE/MFE",      sc_wins, sc_loss, lambda t: t["mae"]/t["mfe"] if t["mfe"]>0 else None, fmt=".2f")
    cmp_num("Duración (min)",sc_wins, sc_loss, lambda t: t["dur_min"], fmt=".0f")
    cmp_num("ATR",          sc_wins, sc_loss, lambda t: t["atr"])
    print()

    # ─── BLOQUE 3: Comparación categórica ────────────────────────────────────
    print("="*90)
    print("BLOQUE 3 — COMPARACIÓN CATEGÓRICA (◄ = diferencia >15pp)")
    print("="*90)
    print()
    cmp_cat("ATR Bucket",    sc_wins, sc_loss, lambda t: t["bucket"].strip())
    print()
    cmp_cat("Dirección",     sc_wins, sc_loss, lambda t: t["pos"])
    print()
    cmp_cat("Tercio KZ",     sc_wins, sc_loss, lambda t: t["tercio"])
    print()
    cmp_cat("Día semana",    sc_wins, sc_loss, lambda t: t["dow"])
    print()
    cmp_cat("Setup",         sc_wins, sc_loss, lambda t: t["setup"])
    print()
    cmp_cat("Período",       sc_wins, sc_loss, lambda t: t["period"])

    # ─── BLOQUE 4: Percentiles MAE/MFE ganadores vs perdedores ───────────────
    print("\n" + "="*90)
    print("BLOQUE 4 — DISTRIBUCIÓN MFE / MAE (percentiles)")
    print("="*90)
    for label, grp in [("SC GANADORES", sc_wins), ("SC PERDEDORES", sc_loss)]:
        maes = [t["mae"] for t in grp]
        mfes = [t["mfe"] for t in grp]
        print(f"\n  {label} (n={len(grp)}):")
        print(f"    MFE: p25={pct(mfes,25):.0f}  p50={pct(mfes,50):.0f}  p75={pct(mfes,75):.0f}  p90={pct(mfes,90):.0f}  avg={statistics.mean(mfes):.0f}")
        print(f"    MAE: p25={pct(maes,25):.0f}  p50={pct(maes,50):.0f}  p75={pct(maes,75):.0f}  p90={pct(maes,90):.0f}  avg={statistics.mean(maes):.0f}")

    # ─── BLOQUE 5: Cruce ATR bucket × Dirección para SC perdedores ───────────
    print("\n" + "="*90)
    print("BLOQUE 5 — CRUCE BUCKET × DIRECCIÓN (SC perdedores)")
    print("="*90)
    by_bucket_dir = defaultdict(list)
    for t in sc_all:
        by_bucket_dir[(t["bucket"].strip(), t["pos"])].append(t)

    print(f"\n  {'Bucket':14}  {'Dir':5}  {'Total':>5}  {'Wins':>5}  {'Loss':>5}  {'WR':>6}  {'Net':>8}  {'AvgMFE':>7}  {'AvgMAE':>7}")
    print("-"*80)
    for _,_,blabel in ATR_BUCKETS:
        for pos in ["long","short"]:
            key = (blabel.strip(), pos)
            grp = by_bucket_dir.get(key,[])
            if not grp: continue
            wins = [t for t in grp if t["profit"]>0]
            loss = [t for t in grp if t["profit"]<=0]
            net  = sum(t["profit"] for t in grp)
            wr   = len(wins)/len(grp)*100
            avg_mfe = statistics.mean(t["mfe"] for t in grp)
            avg_mae = statistics.mean(t["mae"] for t in grp)
            flag = " ◄" if len(loss)>len(wins) else ""
            print(f"  {blabel.strip():14}  {pos:5}  {len(grp):5d}  {len(wins):5d}  {len(loss):5d}  "
                  f"{wr:5.1f}%  {net:+8.0f}  {avg_mfe:7.0f}  {avg_mae:7.0f}{flag}")

    # ─── BLOQUE 6: Patrones candidatos a investigar ───────────────────────────
    print("\n" + "="*90)
    print("BLOQUE 6 — PATRONES CANDIDATOS (diferencias estadísticamente notables)")
    print("="*90)

    # CHOP perdedores
    chop_loss = [t for t in sc_loss if "CHOP" in t["bucket"]]
    chop_wins = [t for t in sc_wins if "CHOP" in t["bucket"]]
    print(f"\n  CHOP SESSION_CLOSE:")
    print(f"    Ganadores: n={len(chop_wins)}  Net={sum(t['profit'] for t in chop_wins):+.0f}"
          f"  AvgMFE={statistics.mean(t['mfe'] for t in chop_wins):.0f}" if chop_wins else "    Ganadores: n=0")
    print(f"    Perdedores: n={len(chop_loss)}  Net={sum(t['profit'] for t in chop_loss):+.0f}"
          f"  AvgMFE={statistics.mean(t['mfe'] for t in chop_loss):.0f}" if chop_loss else "    Perdedores: n=0")

    # Shorts en CHOP
    chop_short_loss = [t for t in sc_loss if "CHOP" in t["bucket"] and t["pos"]=="short"]
    chop_long_loss  = [t for t in sc_loss if "CHOP" in t["bucket"] and t["pos"]=="long"]
    print(f"\n  CHOP SC perdedores por dirección:")
    print(f"    Shorts: n={len(chop_short_loss)}  Net={sum(t['profit'] for t in chop_short_loss):+.0f}" if chop_short_loss else "    Shorts: n=0")
    print(f"    Longs:  n={len(chop_long_loss)}   Net={sum(t['profit'] for t in chop_long_loss):+.0f}" if chop_long_loss else "    Longs:  n=0")

    # MFE bajo en perdedores vs ganadores
    mfe_loss = statistics.mean(t["mfe"] for t in sc_loss)
    mfe_wins = statistics.mean(t["mfe"] for t in sc_wins)
    print(f"\n  MFE promedio: Ganadores={mfe_wins:.0f}  Perdedores={mfe_loss:.0f}"
          f"  Ratio={mfe_wins/mfe_loss:.1f}x")

    # Perdedores con MFE < 200 (precio casi no fue a favor)
    low_mfe_loss = [t for t in sc_loss if t["mfe"] < 200]
    print(f"\n  SC perdedores con MFE<$200 (precio nunca fue a favor): n={len(low_mfe_loss)}")
    for t in sorted(low_mfe_loss, key=lambda x: x["mfe"]):
        print(f"    {t['entry_dt']}  {t['pos']:5}  {t['bucket'].strip():14}"
              f"  MFE={t['mfe']:4.0f}  MAE={t['mae']:4.0f}  Profit={t['profit']:+.0f}")

    print()


if __name__ == "__main__":
    main()

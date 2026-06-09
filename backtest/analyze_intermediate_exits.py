#!/usr/bin/env python3
"""
FASE 4 — ANÁLISIS CAUSAL DE SALIDAS INTERMEDIAS

Pregunta central: ¿Por qué las salidas intermedias generan el edge que TP y SL no generan?

Preguntas:
  Q1. Exit type dominante — cuantificar: SESSION_CLOSE / TP_FULL / SL_FULL / OTHER
  Q2. ExitType × ATR Bucket — tabla n/WR/Net/PF por celda
  Q3. Tercio del kill zone — T1 9:30-10:20 / T2 10:20-11:10 / T3 11:10-12:00
  Q4. CaptureRatio = Profit/MFE por exit type
  Q5. MAE/MFE (calidad estructural) por exit type y resultado
  Q6. Comparación directa: SESSION_CLOSE vs TP_FULL vs SL_FULL en todas las métricas
  Q7. Contrafactual: para cada SESSION_CLOSE, ¿el precio tocó TP? ¿La salida fue óptima?

Uso:
  python analyze_intermediate_exits.py IS:backtest/is_jan_mar_2026.csv \\
    OOS1:backtest/oos_jul_sep_2025.csv OOS2:backtest/oos_oct_dec_2025.csv \\
    OOS3:backtest/oos_apr_jun_2026.csv

Requiere: pip install yfinance
"""
from __future__ import annotations
import sys, csv, re, statistics
from datetime import date, datetime, timedelta
from collections import defaultdict

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

TP_USD  = 1050.0
SL_USD  =  375.0
KZ_START = 9 * 60 + 30   # 9:30 ET en minutos desde medianoche
KZ_END   = 12 * 60        # 12:00 ET
KZ_DUR   = KZ_END - KZ_START  # 150 min → cada tercio = 50 min

ATR_BUCKETS = [
    (0,    300, "CHOP  <300   "),
    (300,  380, "WEAK  300-380"),
    (380,  450, "ACTV  380-450"),
    (450, 9999, "STRG  >450   "),
]

# ─── clasificación de exit type ──────────────────────────────────────────────

def classify_exit(exit_name: str, profit: float) -> str:
    e = exit_name.lower()
    if "profit target" in e:
        return "TP_FULL"
    if "stop loss" in e:
        return "SL_FULL"
    if "session close" in e or "bar close" in e or "forced" in e:
        return "SESSION_CLOSE"
    return "OTHER"

def kz_tercio(entry_dt: datetime | None) -> str:
    if entry_dt is None:
        return "?"
    mins = entry_dt.hour * 60 + entry_dt.minute
    offset = mins - KZ_START
    if offset < 0:
        return "PRE"
    if offset < KZ_DUR / 3:
        return "T1 9:30-10:20"
    if offset < 2 * KZ_DUR / 3:
        return "T2 10:20-11:10"
    return "T3 11:10-12:00"

def atr_bucket(v: float) -> str:
    for lo, hi, label in ATR_BUCKETS:
        if lo <= v < hi:
            return label
    return "?"

# ─── parsing ─────────────────────────────────────────────────────────────────

def parse_money(s: str) -> float:
    s = re.sub(r'[$ ]', '', s.strip()).replace('.', '').replace(',', '.')
    return float(s)

def parse_date(s: str) -> date:
    s = re.sub(r'\s+', ' ', s.strip())
    m = re.match(r'(\d+)/(\d+)/(\d{4})', s)
    if not m:
        raise ValueError(s)
    return date(int(m.group(3)), int(m.group(2)), int(m.group(1)))

def parse_datetime(s: str) -> datetime | None:
    s = re.sub(r'\s+', ' ', s.strip())
    m = re.match(r'(\d+)/(\d+)/(\d{4})\s+(\d+):(\d+):(\d+)', s)
    if not m:
        return None
    d_, mo, y, h, mi, sec = (int(x) for x in m.groups())
    ampm = "p" in s.lower()[s.lower().rfind(str(sec)):]
    hr = h + 12 if ampm and h < 12 else (0 if not ampm and h == 12 else h)
    try:
        return datetime(y, mo, d_, hr, mi, sec)
    except ValueError:
        return None

def load_trades(path: str, label: str) -> list[dict]:
    trades = []
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        content = f.read()
    reader = csv.reader(content.splitlines(), delimiter=';')
    header = None
    for row in reader:
        if not any(c.strip() for c in row):
            continue
        if header is None:
            if "trade number" in " ".join(row).lower():
                header = [h.strip().lower() for h in row]
            continue
        rec = dict(zip(header, [c.strip() for c in row]))
        if not rec.get("trade number", "").strip().isdigit():
            continue
        try:
            profit  = parse_money(rec.get("profit", "0"))
            mae     = abs(parse_money(rec.get("mae", "0")))
            mfe     = parse_money(rec.get("mfe",   "0"))
            exit_n  = rec.get("exit name", "").strip()
            entry_s = rec.get("entry time", "")
            entry_d = parse_date(entry_s)
            entry_t = parse_datetime(entry_s)
            exit_t  = parse_datetime(rec.get("exit time", ""))
            dur_min = None
            if entry_t and exit_t:
                d = (exit_t - entry_t).total_seconds() / 60
                if d >= 0:
                    dur_min = d
            t = {
                "period":    label,
                "pos":       rec.get("market pos.", "").strip().lower(),
                "setup":     "A" if rec.get("entry name","") in ("LongFVG","ShortFVG") else "B",
                "entry_dt":  entry_d,
                "entry_dtt": entry_t,
                "profit":    profit,
                "exit":      exit_n,
                "exit_type": classify_exit(exit_n, profit),
                "mae":       mae,
                "mfe":       mfe,
                "dur_min":   dur_min,
                "tercio":    kz_tercio(entry_t),
                "month":     entry_d.strftime("%Y-%m"),
            }
            trades.append(t)
        except Exception:
            pass
    return trades

# ─── ATR ─────────────────────────────────────────────────────────────────────

def download_atr(start: str, end: str, window=20) -> dict[str, float]:
    import yfinance as yf
    d = yf.Ticker("NQ=F").history(start=start, end=end, interval="1d")
    if d.empty:
        raise RuntimeError("yfinance sin datos")
    dates = [x.date() for x in d.index]
    H, L, C = d["High"].tolist(), d["Low"].tolist(), d["Close"].tolist()
    trs = [H[i]-L[i] if i==0 else max(H[i]-L[i], abs(H[i]-C[i-1]), abs(L[i]-C[i-1]))
           for i in range(len(dates))]
    return {str(dates[i]): statistics.mean(trs[max(0,i-window+1):i+1])
            for i in range(len(dates))}

def tag_atr(trades: list[dict], atr_map: dict):
    for t in trades:
        for delta in range(5):
            k = str(t["entry_dt"] - timedelta(days=delta))
            if k in atr_map:
                t["atr"]    = atr_map[k]
                t["bucket"] = atr_bucket(t["atr"])
                break
        else:
            t["atr"] = 0; t["bucket"] = "?"

# ─── stats helpers ───────────────────────────────────────────────────────────

def pct(vals, p):
    if not vals:
        return 0
    sv = sorted(vals)
    idx = (len(sv)-1)*p/100
    lo, hi = int(idx), min(int(idx)+1, len(sv)-1)
    return sv[lo] + (sv[hi]-sv[lo])*(idx-lo)

def grp_stats(trades):
    if not trades:
        return None
    n     = len(trades)
    wins  = [t["profit"] for t in trades if t["profit"] > 0]
    loss  = [t["profit"] for t in trades if t["profit"] <= 0]
    net   = sum(t["profit"] for t in trades)
    wr    = len(wins)/n
    avg_w = statistics.mean(wins)  if wins  else 0
    avg_l = statistics.mean(loss)  if loss  else 0
    pf    = sum(wins)/abs(sum(loss)) if loss and sum(loss)!=0 else float("inf")
    return dict(n=n, wr=wr, net=net, avg_w=avg_w, avg_l=avg_l, pf=pf,
                wins=len(wins), losses=len(loss))

def fmt(s) -> str:
    if s is None or s["n"]==0:
        return "n=0"
    return (f"n={s['n']:3d}  WR={s['wr']*100:5.1f}%  PF={s['pf']:5.2f}"
            f"  Net={s['net']:+8.0f}  AvgW={s['avg_w']:+6.0f}  AvgL={s['avg_l']:+6.0f}")

# ─── MAIN ────────────────────────────────────────────────────────────────────

def main():
    if len(sys.argv) < 2:
        print("Uso: python analyze_intermediate_exits.py LABEL:csv [...]")
        sys.exit(1)

    all_trades = []
    for arg in sys.argv[1:]:
        label, path = arg.split(":", 1) if ":" in arg else (arg, arg)
        t = load_trades(path, label)
        all_trades.extend(t)
        print(f"Loaded {len(t):3d} trades [{label}]")

    print(f"Total: {len(all_trades)}\n")
    dates = [t["entry_dt"] for t in all_trades]
    atr_map = download_atr(str(min(dates)-timedelta(60)), str(max(dates)+timedelta(5)))
    tag_atr(all_trades, atr_map)

    EXIT_TYPES = ["TP_FULL", "SL_FULL", "SESSION_CLOSE", "OTHER"]
    BUCKET_LABELS = [lb for _,_,lb in ATR_BUCKETS]

    # ─── Q1: Exit type dominante ─────────────────────────────────────────────
    print("="*80)
    print("Q1: EXIT TYPE DOMINANTE")
    print("="*80)
    by_exit = defaultdict(list)
    for t in all_trades:
        by_exit[t["exit_type"]].append(t)

    print(f"\n  {'Exit Type':16s}  {fmt({'n':0,'wr':0,'net':0,'avg_w':0,'avg_l':0,'pf':0,'wins':0,'losses':0}).replace('n=0','')}")
    print(f"  {'':16s}  {'n':>5}  {'WR':>6}  {'PF':>5}  {'Net':>8}  {'AvgW':>6}  {'AvgL':>6}")
    print("-"*70)
    for et in EXIT_TYPES:
        grp = by_exit.get(et, [])
        s = grp_stats(grp)
        if not s:
            print(f"  {et:16s}  n=0")
            continue
        net_pct = s["net"] / sum(t["profit"] for t in all_trades) * 100 if all_trades else 0
        print(f"  {et:16s}  {fmt(s)}  ({net_pct:+.0f}% del neto total)")

    # ─── Q2: ExitType × ATR Bucket ───────────────────────────────────────────
    print("\n" + "="*80)
    print("Q2: EXIT TYPE × ATR BUCKET — ¿en qué régimen funciona cada tipo de salida?")
    print("="*80)
    print(f"\n  {'':16s}  {'CHOP <300':^28}  {'WEAK 300-380':^28}  {'ACTIVE 380-450':^28}  {'STRONG >450':^28}")
    print(f"  {'Exit Type':16s}  {'n':>3}{'WR':>6}{'PF':>5}{'Net':>8}  " * 4)
    print("-"*130)
    for et in EXIT_TYPES:
        row = f"  {et:16s}"
        for _, _, blabel in ATR_BUCKETS:
            grp = [t for t in by_exit.get(et,[]) if t["bucket"] == blabel]
            s = grp_stats(grp)
            if not s:
                row += f"  {'—':>3}{'':>6}{'':>5}{'':>8}"
            else:
                pf_str = f"{s['pf']:5.2f}" if s['pf'] < 99 else "  inf"
                row += f"  {s['n']:3d}{s['wr']*100:6.1f}%{pf_str}{s['net']:+8.0f}"
        print(row)

    # ─── Q3: Tercio kill zone ────────────────────────────────────────────────
    print("\n" + "="*80)
    print("Q3: TERCIO KILL ZONE — ¿cuándo entran los trades que generan edge?")
    print("="*80)
    TERCIOS = ["T1 9:30-10:20", "T2 10:20-11:10", "T3 11:10-12:00"]
    for et in ["SESSION_CLOSE", "TP_FULL", "SL_FULL"]:
        grp_et = by_exit.get(et, [])
        print(f"\n  {et}:")
        for t3 in TERCIOS:
            grp = [t for t in grp_et if t["tercio"] == t3]
            s = grp_stats(grp)
            tag = fmt(s) if s else "n=0"
            print(f"    {t3}  {tag}")

    # ─── Q4: CaptureRatio = Profit / MFE ────────────────────────────────────
    print("\n" + "="*80)
    print("Q4: CAPTURE RATIO = Profit / MFE — ¿cuánto del movimiento disponible captura cada salida?")
    print("="*80)
    print(f"\n  {'Exit Type':16s}  {'n_pos':>5}  {'CR avg':>7}  {'CR p25':>7}  {'CR p50':>7}  {'CR p75':>7}  {'MFE avg':>8}")
    print("-"*75)
    for et in EXIT_TYPES:
        grp = [t for t in by_exit.get(et,[]) if t["profit"] > 0 and t["mfe"] > 0]
        if not grp:
            print(f"  {et:16s}  n=0")
            continue
        crs  = [t["profit"] / t["mfe"] for t in grp]
        mfes = [t["mfe"] for t in grp]
        print(f"  {et:16s}  {len(grp):5d}  "
              f"{statistics.mean(crs):7.2f}  "
              f"{pct(crs,25):7.2f}  "
              f"{pct(crs,50):7.2f}  "
              f"{pct(crs,75):7.2f}  "
              f"{statistics.mean(mfes):8.0f}")

    # ─── Q5: MAE/MFE calidad estructural ─────────────────────────────────────
    print("\n" + "="*80)
    print("Q5: MAE/MFE — calidad estructural por exit type y resultado")
    print("="*80)
    print(f"\n  {'Exit Type':16s}  {'Result':6s}  {'n':>3}  {'MAE avg':>8}  {'MFE avg':>8}  {'MAE/MFE':>8}  {'MAE p50':>7}  {'MFE p50':>7}")
    print("-"*80)
    for et in EXIT_TYPES:
        for result_label, filter_fn in [("WIN", lambda t: t["profit"]>0),
                                         ("LOSS", lambda t: t["profit"]<=0)]:
            grp = [t for t in by_exit.get(et,[]) if filter_fn(t)]
            if not grp:
                continue
            maes = [t["mae"] for t in grp]
            mfes = [t["mfe"] for t in grp]
            ratio = statistics.mean(t["mae"]/t["mfe"] if t["mfe"]>0 else 0 for t in grp)
            print(f"  {et:16s}  {result_label:6s}  {len(grp):3d}  "
                  f"{statistics.mean(maes):8.0f}  {statistics.mean(mfes):8.0f}  "
                  f"{ratio:8.2f}  {pct(maes,50):7.0f}  {pct(mfes,50):7.0f}")

    # ─── Q6: Comparación directa SESSION_CLOSE vs TP vs SL ──────────────────
    print("\n" + "="*80)
    print("Q6: COMPARACIÓN DIRECTA — SESSION_CLOSE vs TP_FULL vs SL_FULL")
    print("="*80)
    for et in ["SESSION_CLOSE", "TP_FULL", "SL_FULL"]:
        grp = by_exit.get(et, [])
        if not grp:
            continue
        wins  = [t for t in grp if t["profit"] > 0]
        loss  = [t for t in grp if t["profit"] <= 0]
        durs  = [t["dur_min"] for t in grp if t["dur_min"] is not None]
        maes  = [t["mae"] for t in grp]
        mfes  = [t["mfe"] for t in grp]
        crs   = [t["profit"]/t["mfe"] for t in wins if t["mfe"]>0]
        print(f"\n  {et}  (n={len(grp)}, wins={len(wins)}, loss={len(loss)})")
        print(f"    Net={sum(t['profit'] for t in grp):+.0f}"
              f"  WR={len(wins)/len(grp)*100:.1f}%"
              f"  AvgW={statistics.mean(t['profit'] for t in wins):+.0f}" if wins else "  AvgW=n/a",
              end="")
        print(f"  AvgL={statistics.mean(t['profit'] for t in loss):+.0f}" if loss else "  AvgL=n/a")
        print(f"    Dur: avg={statistics.mean(durs):.0f}min  p50={pct(durs,50):.0f}min" if durs else "    Dur: n/a")
        print(f"    MAE: avg={statistics.mean(maes):.0f}  p50={pct(maes,50):.0f}  p75={pct(maes,75):.0f}")
        print(f"    MFE: avg={statistics.mean(mfes):.0f}  p50={pct(mfes,50):.0f}  p75={pct(mfes,75):.0f}")
        if crs:
            print(f"    CaptureRatio (winners): avg={statistics.mean(crs):.2f}  p50={pct(crs,50):.2f}")

    # ─── Q7: Contrafactual SESSION_CLOSE ─────────────────────────────────────
    print("\n" + "="*80)
    print("Q7: CONTRAFACTUAL — SESSION_CLOSE: ¿la salida fue óptima?")
    print("    ¿El precio llegó al TP? ¿La sesión recortó ganancia o la salvó?")
    print("="*80)

    sc = by_exit.get("SESSION_CLOSE", [])
    if not sc:
        print("  Sin SESSION_CLOSE trades.")
    else:
        # Tres categorías contrafactuales:
        # A) mfe >= TP → precio llegó al TP en sesión → sesión RECORTÓ ganancia (TP habría sido mejor)
        # B) profit > 0 y mfe < TP → precio nunca llegó al TP → sesión capturó todo lo disponible
        # C) profit <= 0 → sesión cerró en pérdida (¿salvó frente a SL completo?)
        cat_a = [t for t in sc if t["mfe"] >= TP_USD]           # precio llegó al TP, sesión cortó
        cat_b = [t for t in sc if t["profit"] > 0 and t["mfe"] < TP_USD]  # ganancia parcial, nunca llegó al TP
        cat_c = [t for t in sc if t["profit"] <= 0]             # pérdida parcial, sesión cerró en rojo

        total_net_sc = sum(t["profit"] for t in sc)
        hyp_tp_a  = len(cat_a) * TP_USD   # si hubieran esperado al TP
        actual_a  = sum(t["profit"] for t in cat_a)
        saved_c   = sum(t["profit"] for t in cat_c)  # negativo, pero menos que -SL

        print(f"\n  Total SESSION_CLOSE: n={len(sc)}  Net={total_net_sc:+.0f}")
        print()
        print(f"  CAT A — Precio LLEGÓ al TP (mfe>=$1050), sesión cerró antes")
        print(f"         n={len(cat_a)}  Profit actual={actual_a:+.0f}  Si TP: {hyp_tp_a:+.0f}"
              f"  Diferencia: {hyp_tp_a-actual_a:+.0f} dejado sobre la mesa")
        for t in sorted(cat_a, key=lambda x: x["entry_dt"]):
            gap = TP_USD - t["profit"]
            print(f"    {t['entry_dt']}  [{t['period']:5s}]  {t['pos']:5s}"
                  f"  Profit={t['profit']:+7.0f}  MFE={t['mfe']:5.0f}  GAP_to_TP={gap:+5.0f}"
                  f"  ATR={t['atr']:.0f}  [{t['bucket'].strip()}]")

        print()
        print(f"  CAT B — Ganancia parcial, precio NUNCA llegó al TP (mfe<$1050)")
        print(f"         n={len(cat_b)}  Net={sum(t['profit'] for t in cat_b):+.0f}")
        for t in sorted(cat_b, key=lambda x: x["profit"], reverse=True):
            print(f"    {t['entry_dt']}  [{t['period']:5s}]  {t['pos']:5s}"
                  f"  Profit={t['profit']:+7.0f}  MFE={t['mfe']:5.0f}  (MFE={t['mfe']/TP_USD*100:.0f}% del TP)"
                  f"  ATR={t['atr']:.0f}  [{t['bucket'].strip()}]")

        print()
        print(f"  CAT C — Pérdida parcial (<SL completo), sesión salvó de SL")
        full_sl_equiv = len(cat_c) * SL_USD
        actual_c      = abs(sum(t["profit"] for t in cat_c))
        print(f"         n={len(cat_c)}  Net={sum(t['profit'] for t in cat_c):+.0f}"
              f"  Si SL completo: {-full_sl_equiv:+.0f}  Ahorro: {full_sl_equiv-actual_c:+.0f}")
        for t in sorted(cat_c, key=lambda x: x["profit"]):
            print(f"    {t['entry_dt']}  [{t['period']:5s}]  {t['pos']:5s}"
                  f"  Profit={t['profit']:+7.0f}  MAE={t['mae']:5.0f}  MFE={t['mfe']:5.0f}"
                  f"  ATR={t['atr']:.0f}  [{t['bucket'].strip()}]")

        print()
        print(f"  RESUMEN CONTRAFACTUAL:")
        cat_a_net = sum(t["profit"] for t in cat_a)
        cat_b_net = sum(t["profit"] for t in cat_b)
        cat_c_net = sum(t["profit"] for t in cat_c)
        print(f"    Si todo SESSION_CLOSE hubiera esperado al TP/SL natural:")
        hyp_total = hyp_tp_a + 0 + (-full_sl_equiv)   # Cat B: nunca llega → mismo resultado o peor; Cat C → SL completo
        print(f"    Cat A: {cat_a_net:+.0f} → {hyp_tp_a:+.0f}  (diferencia: {hyp_tp_a-cat_a_net:+.0f})")
        print(f"    Cat B: {cat_b_net:+.0f} → ??? (precio nunca llegó → sesión fue óptima)")
        print(f"    Cat C: {cat_c_net:+.0f} → {-full_sl_equiv:+.0f}  (sesión ahorró: {full_sl_equiv-actual_c:+.0f})")
        net_diff = (hyp_tp_a - cat_a_net) + (full_sl_equiv - actual_c) * (-1)
        print(f"    Efecto neto aproximado: sesión CUESTA {hyp_tp_a-cat_a_net:+.0f} (Cat A) "
              f"y AHORRA {full_sl_equiv-actual_c:+.0f} (Cat C)")

    print()


if __name__ == "__main__":
    main()

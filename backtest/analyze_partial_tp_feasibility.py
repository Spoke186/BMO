# -*- coding: utf-8 -*-
"""
FEASIBILITY TP PARCIAL (Opción 5) — análisis previo sobre BASELINE A.

Responde, con los CSVs del baseline (TP=1050/SL=375, PartialTP OFF), las 4
preguntas de Nuevas_intrucciones.md §3 ANTES de mirar las corridas C/D:

  Q1. ¿Qué % de trades alcanza MFE >= gatillo ($350)? Por exit type y bucket ATR.
  Q2. De los SESSION_CLOSE winners, ¿cuántos tienen MFE >= gatillo? (= afectados)
  Q3. ¿Cuántos SL_FULL tienen MFE >= gatillo? (= losers de -$375 que el parcial
      convertiría en ~>= +$205: +$175 del parcial + ~$30 del runner en BE+colchón;
      comisiones aparte)
  Q4. Distribución de MFE de los losers — sweet spot del gatillo (SOLO informativo:
      el gatillo NO se cambia en esta ronda)

Unidades: MFE/MAE del export NT8 vienen en USD de la POSICIÓN completa
(2 contratos), las mismas unidades del gatillo PartialTPTriggerUsd.

Uso:
  python analyze_partial_tp_feasibility.py P2024:a_tp1050_base_ene_jul_2024.csv \\
    OOS1:a_tp1050_base_oos_jul_sep_2025.csv ... [--trigger 350] [--no-atr]
"""
from __future__ import annotations
import argparse, csv, re, statistics, sys
from datetime import date, datetime, timedelta
from collections import defaultdict

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

TP_USD = 1050.0
SL_USD =  375.0

ATR_BUCKETS = [
    (0,    300, "CHOP  <300   "),
    (300,  380, "WEAK  300-380"),
    (380,  450, "ACTV  380-450"),
    (450, 9999, "STRG  >450   "),
]

# ─── parsing (mismo formato NT8 ';' que analyze_intermediate_exits.py) ────────

def parse_money(s: str) -> float:
    s = re.sub(r'[$ ]', '', s.strip()).replace('.', '').replace(',', '.')
    return float(s)

def parse_date(s: str) -> date:
    s = re.sub(r'\s+', ' ', s.strip())
    m = re.match(r'(\d+)/(\d+)/(\d{4})', s)
    if not m:
        raise ValueError(s)
    return date(int(m.group(3)), int(m.group(2)), int(m.group(1)))

def classify_exit(exit_name: str) -> str:
    e = exit_name.lower()
    if "profit target" in e:
        return "TP_FULL"
    if "stop loss" in e:
        return "SL_FULL"
    if "session close" in e or "bar close" in e or "forced" in e:
        return "SESSION_CLOSE"
    return "OTHER"

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
            trades.append({
                "period":    label,
                "pos":       rec.get("market pos.", "").strip().lower(),
                "profit":    parse_money(rec.get("profit", "0")),
                "mae":       abs(parse_money(rec.get("mae", "0"))),
                "mfe":       parse_money(rec.get("mfe", "0")),
                "exit":      rec.get("exit name", "").strip(),
                "exit_type": classify_exit(rec.get("exit name", "")),
                "entry_dt":  parse_date(rec.get("entry time", "")),
            })
        except Exception:
            pass
    return trades

# ─── ATR opcional (yfinance; si falla, se omite el desglose por bucket) ───────

def download_atr(start: str, end: str, window=20) -> dict[str, float]:
    import yfinance as yf
    d = yf.Ticker("NQ=F").history(start=start, end=end, interval="1d")
    if d.empty:
        raise RuntimeError("yfinance sin datos")
    dates = [x.date() for x in d.index]
    H, L, C = d["High"].tolist(), d["Low"].tolist(), d["Close"].tolist()
    trs = [H[i]-L[i] if i == 0 else max(H[i]-L[i], abs(H[i]-C[i-1]), abs(L[i]-C[i-1]))
           for i in range(len(dates))]
    return {str(dates[i]): statistics.mean(trs[max(0, i-window+1):i+1])
            for i in range(len(dates))}

def atr_bucket(v: float) -> str:
    for lo, hi, label in ATR_BUCKETS:
        if lo <= v < hi:
            return label
    return "?"

def tag_atr(trades: list[dict], atr_map: dict) -> None:
    for t in trades:
        for delta in range(5):
            k = str(t["entry_dt"] - timedelta(days=delta))
            if k in atr_map:
                t["atr"] = atr_map[k]
                t["bucket"] = atr_bucket(t["atr"])
                break
        else:
            t["atr"] = 0; t["bucket"] = "?"

# ─── helpers ──────────────────────────────────────────────────────────────────

def pct(vals, p):
    if not vals:
        return 0
    sv = sorted(vals)
    idx = (len(sv)-1)*p/100
    lo, hi = int(idx), min(int(idx)+1, len(sv)-1)
    return sv[lo] + (sv[hi]-sv[lo])*(idx-lo)

def ascii_hist(values, bins=14, width=40, lo=None, hi=None):
    if not values:
        return ["  (vacío)"]
    lo = min(values) if lo is None else lo
    hi = max(values) if hi is None else hi
    if hi == lo:
        hi = lo + 1
    counts = [0]*bins
    for v in values:
        counts[min(bins-1, int((v-lo)/(hi-lo)*bins))] += 1
    peak = max(counts)
    out = []
    for b, c in enumerate(counts):
        left = lo + (hi-lo)*b/bins
        bar = "#" * max(1 if c else 0, round(c/peak*width))
        out.append(f"  ${left:>6,.0f} |{bar:<{width}}| {c}")
    return out

# ─── MAIN ─────────────────────────────────────────────────────────────────────

def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("csvs", nargs="+", help="LABEL:trades.csv (export NT8, ';')")
    p.add_argument("--trigger", type=float, default=350.0,
                   help="gatillo del parcial en USD flotantes (default 350)")
    p.add_argument("--no-atr", action="store_true",
                   help="omitir desglose por bucket ATR (sin yfinance)")
    args = p.parse_args()

    trig = args.trigger
    all_trades = []
    for arg in args.csvs:
        label, path = arg.split(":", 1) if ":" in arg else (arg, arg)
        t = load_trades(path, label)
        all_trades.extend(t)
        print(f"Loaded {len(t):3d} trades [{label}]  {path}")
    if not all_trades:
        sys.exit("Sin trades. ¿Formato NT8 con ';'?")
    print(f"Total: {len(all_trades)} trades | gatillo analizado: ${trig:,.0f}\n")

    have_atr = False
    if not args.no_atr:
        try:
            dates = [t["entry_dt"] for t in all_trades]
            atr_map = download_atr(str(min(dates)-timedelta(60)),
                                   str(max(dates)+timedelta(5)))
            tag_atr(all_trades, atr_map)
            have_atr = True
        except Exception as e:
            print(f"(ATR no disponible: {e} — desglose por bucket omitido)\n")

    by_exit = defaultdict(list)
    for t in all_trades:
        by_exit[t["exit_type"]].append(t)
    EXIT_TYPES = ["TP_FULL", "SL_FULL", "SESSION_CLOSE", "OTHER"]

    # ─── Q1: % de trades con MFE >= gatillo ──────────────────────────────────
    print("="*78)
    print(f"Q1: % DE TRADES CON MFE >= ${trig:,.0f} (gatillo del parcial)")
    print("="*78)
    hit_all = [t for t in all_trades if t["mfe"] >= trig]
    print(f"\n  GLOBAL: {len(hit_all)}/{len(all_trades)} = "
          f"{len(hit_all)/len(all_trades)*100:.1f}% de trades dispararían el parcial")
    print(f"\n  {'Exit Type':16s} {'n':>4} {'>=trig':>7} {'%':>7}")
    print("-"*40)
    for et in EXIT_TYPES:
        grp = by_exit.get(et, [])
        if not grp:
            continue
        h = [t for t in grp if t["mfe"] >= trig]
        print(f"  {et:16s} {len(grp):4d} {len(h):7d} {len(h)/len(grp)*100:6.1f}%")

    if have_atr:
        print(f"\n  Por bucket ATR:")
        print(f"  {'Bucket':16s} {'n':>4} {'>=trig':>7} {'%':>7}")
        print("-"*40)
        for _, _, bl in ATR_BUCKETS:
            grp = [t for t in all_trades if t.get("bucket") == bl]
            if not grp:
                continue
            h = [t for t in grp if t["mfe"] >= trig]
            print(f"  {bl:16s} {len(grp):4d} {len(h):7d} {len(h)/len(grp)*100:6.1f}%")

    # ─── Q2: SESSION_CLOSE winners afectados ─────────────────────────────────
    print("\n" + "="*78)
    print(f"Q2: SESSION_CLOSE WINNERS CON MFE >= ${trig:,.0f} (serían afectados por el parcial)")
    print("="*78)
    sc_w = [t for t in by_exit.get("SESSION_CLOSE", []) if t["profit"] > 0]
    sc_w_hit = [t for t in sc_w if t["mfe"] >= trig]
    print(f"\n  SC winners: {len(sc_w)} | con MFE >= gatillo: {len(sc_w_hit)} "
          f"({len(sc_w_hit)/len(sc_w)*100:.1f}% de los SC winners)" if sc_w else "\n  Sin SC winners.")
    if sc_w_hit:
        nets = [t["profit"] for t in sc_w_hit]
        print(f"  Net actual de esos {len(sc_w_hit)}: {sum(nets):+,.0f}  "
              f"(avg {statistics.mean(nets):+,.0f}, p50 {pct(nets,50):+,.0f})")
        # Riesgo de canibalización: los que cerraron por debajo del flotante asegurado
        # (~trig/2 del parcial con 1 de 2 contratos) NO pierden con el parcial;
        # los que cerraron muy arriba podrían perder la mitad del tramo extra.
        floor_locked = trig/2
        below = [t for t in sc_w_hit if t["profit"] < floor_locked]
        print(f"  De esos, cerraron por DEBAJO de ${floor_locked:,.0f} (el parcial los habría "
              f"MEJORADO): {len(below)}")
        print(f"  Cerraron por ENCIMA (canibalización potencial del runner): "
              f"{len(sc_w_hit)-len(below)}")

    # ─── Q3: SL_FULL rescatables ─────────────────────────────────────────────
    print("\n" + "="*78)
    print(f"Q3: SL_FULL CON MFE >= ${trig:,.0f} (losers de -$375 que el parcial rescata)")
    print("="*78)
    sl = by_exit.get("SL_FULL", [])
    sl_hit = [t for t in sl if t["mfe"] >= trig]
    rescued_to = trig/2 + 30  # +$175 parcial + ~$30 runner BE+colchón (sin comisiones)
    print(f"\n  SL_FULL: {len(sl)} | con MFE >= gatillo: {len(sl_hit)} "
          f"({len(sl_hit)/len(sl)*100:.1f}% de los SL)" if sl else "\n  Sin SL_FULL.")
    if sl_hit:
        cur = sum(t["profit"] for t in sl_hit)
        hyp = len(sl_hit) * rescued_to
        print(f"  Net actual de esos {len(sl_hit)}: {cur:+,.0f}")
        print(f"  Con parcial (peor caso ~+${rescued_to:,.0f} c/u, runner muere en BE): {hyp:+,.0f}")
        print(f"  Mejora bruta estimada: {hyp-cur:+,.0f} (sin comisiones/slippage)")
        for t in sorted(sl_hit, key=lambda x: -x["mfe"]):
            b = f"  [{t.get('bucket','?').strip()}]" if have_atr else ""
            print(f"    {t['entry_dt']}  [{t['period']:5s}] {t['pos']:5s} "
                  f"MFE={t['mfe']:5.0f}  MAE={t['mae']:5.0f}{b}")

    # ─── Q4: distribución MFE de losers (informativo) ────────────────────────
    print("\n" + "="*78)
    print("Q4: DISTRIBUCIÓN MFE DE LOS LOSERS — sweet spot del gatillo (INFORMATIVO,")
    print("    el gatillo NO se cambia en esta ronda)")
    print("="*78)
    losers = [t for t in all_trades if t["profit"] <= 0]
    mfes = [t["mfe"] for t in losers]
    if mfes:
        print(f"\n  Losers: n={len(losers)}")
        print(f"  MFE: p10={pct(mfes,10):,.0f}  p25={pct(mfes,25):,.0f}  "
              f"p50={pct(mfes,50):,.0f}  p75={pct(mfes,75):,.0f}  "
              f"p90={pct(mfes,90):,.0f}  max={max(mfes):,.0f}")
        print(f"\n  Histograma MFE losers (0–$1,050):")
        for line in ascii_hist(mfes, bins=14, lo=0, hi=TP_USD):
            print(line)
        print(f"\n  % de losers que dispararía cada gatillo hipotético:")
        for g in (150, 200, 250, 300, 350, 400, 450, 500):
            h = sum(1 for v in mfes if v >= g)
            print(f"    gatillo ${g:>3}: {h:3d}/{len(mfes)} = {h/len(mfes)*100:5.1f}%")

    print("\nNOTA: esto es feasibility sobre el baseline. El veredicto real sale del")
    print("backtest NT8 de las corridas C/D (compare_variants.py + montecarlo.py).")


if __name__ == "__main__":
    main()

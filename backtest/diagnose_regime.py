#!/usr/bin/env python3
"""
FASE 3 DIAGNÓSTICO — Valida hallazgos antes de FASE 4.

Preguntas:
  1. ACTIVE shorts: ¿PF 3.62 se mantiene por período (IS/OOS)?
  2. MAE/MFE: distribución por bucket (percentiles, no solo media).
  3. Duración promedio por bucket (minutos).
  4. Concentración: ¿el edge viene de pocos winners extremos?

Uso:
  python diagnose_regime.py IS:backtest/is_jan_mar_2026.csv \\
                            OOS1:backtest/oos_jul_sep_2025.csv \\
                            OOS2:backtest/oos_oct_dec_2025.csv \\
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

ATR_BUCKETS = [
    (0,    300, "CHOP   <300"),
    (300,  380, "WEAK   300-380"),
    (380,  450, "ACTIVE 380-450"),
    (450, 9999, "STRONG >450"),
]

TP_USD = 1050.0
SL_USD = 375.0


# ─── helpers ─────────────────────────────────────────────────────────────────

def parse_money(s: str) -> float:
    s = re.sub(r'[$ ]', '', s.strip())
    s = s.replace('.', '').replace(',', '.')
    return float(s)

def parse_date(s: str) -> date:
    s = re.sub(r'\s+', ' ', s.strip())
    m = re.match(r'(\d+)/(\d+)/(\d{4})', s)
    if not m:
        raise ValueError(f"Fecha no reconocida: {s!r}")
    return date(int(m.group(3)), int(m.group(2)), int(m.group(1)))

def parse_datetime(s: str) -> datetime | None:
    s = re.sub(r'\s+', ' ', s.strip())
    m = re.match(r'(\d+)/(\d+)/(\d{4})\s+(\d+):(\d+):(\d+)', s)
    if not m:
        return None
    d_, mo, y, h, mi, sec = (int(x) for x in m.groups())
    # handle AM/PM marker embedded in the string
    ampm = "p" in s.lower().split(str(sec))[-1]
    hr = h
    if ampm and h < 12:
        hr = h + 12
    elif not ampm and h == 12:
        hr = 0
    try:
        return datetime(y, mo, d_, hr, mi, sec)
    except ValueError:
        return None

def pct(vals, p):
    if not vals:
        return 0
    sv = sorted(vals)
    idx = (len(sv) - 1) * p / 100
    lo, hi = int(idx), min(int(idx) + 1, len(sv) - 1)
    return sv[lo] + (sv[hi] - sv[lo]) * (idx - lo)

def atr_bucket(v: float) -> str:
    for lo, hi, label in ATR_BUCKETS:
        if lo <= v < hi:
            return label
    return "?"


# ─── data loading ─────────────────────────────────────────────────────────────

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
            joined = " ".join(row).lower()
            if "trade number" in joined or "market pos" in joined:
                header = [h.strip().lower() for h in row]
            continue
        rec = dict(zip(header, [c.strip() for c in row]))
        num_raw = rec.get("trade number", "").strip()
        if not num_raw.isdigit():
            continue
        try:
            entry_dt = parse_date(rec.get("entry time", ""))
            exit_dt  = parse_datetime(rec.get("exit time",  ""))
            entry_dtt= parse_datetime(rec.get("entry time", ""))
            dur_min  = None
            if entry_dtt and exit_dt:
                diff = exit_dt - entry_dtt
                dur_min = diff.total_seconds() / 60
                if dur_min < 0:
                    dur_min = None
            signal = rec.get("entry name", "").strip()
            pos    = rec.get("market pos.", "").strip().lower()
            t = {
                "period":   label,
                "num":      int(num_raw),
                "pos":      pos,
                "signal":   signal,
                "setup":    "A" if signal in ("LongFVG", "ShortFVG") else "B",
                "entry_dt": entry_dt,
                "profit":   parse_money(rec.get("profit", "0")),
                "exit":     rec.get("exit name", "").strip(),
                "mae":      abs(parse_money(rec.get("mae", "0"))),
                "mfe":      parse_money(rec.get("mfe",   "0")),
                "dur_min":  dur_min,
                "month":    entry_dt.strftime("%Y-%m"),
            }
            trades.append(t)
        except Exception:
            pass
    return trades


# ─── ATR download ─────────────────────────────────────────────────────────────

def download_atr(start: str, end: str, window=20) -> dict[str, float]:
    import yfinance as yf
    d = yf.Ticker("NQ=F").history(start=start, end=end, interval="1d")
    if d.empty:
        raise RuntimeError("yfinance sin datos")
    dates  = [x.date() for x in d.index]
    highs  = d["High"].tolist()
    lows   = d["Low"].tolist()
    closes = d["Close"].tolist()
    trs = []
    for i in range(len(dates)):
        hl = highs[i] - lows[i]
        trs.append(hl if i == 0 else max(hl, abs(highs[i]-closes[i-1]), abs(lows[i]-closes[i-1])))
    atr_map = {}
    for i, dd in enumerate(dates):
        lb = trs[max(0, i-window+1): i+1]
        atr_map[str(dd)] = statistics.mean(lb)
    return atr_map

def tag_atr(trades: list[dict], atr_map: dict):
    for t in trades:
        key = str(t["entry_dt"])
        for delta in range(5):
            k = str(t["entry_dt"] - timedelta(days=delta))
            if k in atr_map:
                t["atr"]    = atr_map[k]
                t["bucket"] = atr_bucket(t["atr"])
                break
        else:
            t["atr"]    = 0
            t["bucket"] = "?"


# ─── stats ────────────────────────────────────────────────────────────────────

def bucket_stats(trades: list[dict], label="") -> str:
    if not trades:
        return f"  {label:30s} n=0"
    n      = len(trades)
    wins   = [t["profit"] for t in trades if t["profit"] > 0]
    losses = [t["profit"] for t in trades if t["profit"] <= 0]
    net    = sum(t["profit"] for t in trades)
    wr     = len(wins) / n
    avg_w  = statistics.mean(wins)   if wins   else 0
    avg_l  = statistics.mean(losses) if losses else 0
    pf     = sum(wins) / abs(sum(losses)) if losses and sum(losses) != 0 else float("inf")
    return (f"  {label:30s}"
            f" n={n:3d}"
            f"  WR={wr*100:5.1f}%"
            f"  PF={pf:5.2f}"
            f"  Net={net:+8.0f}"
            f"  AvgW={avg_w:+6.0f}"
            f"  AvgL={avg_l:+6.0f}")


# ─── main ─────────────────────────────────────────────────────────────────────

def main():
    if len(sys.argv) < 2:
        print("Uso: python diagnose_regime.py LABEL:csv [LABEL:csv ...]")
        sys.exit(1)

    all_trades: list[dict] = []
    periods: list[str] = []
    for arg in sys.argv[1:]:
        if ":" in arg:
            label, path = arg.split(":", 1)
        else:
            label, path = arg, arg
        t = load_trades(path, label)
        all_trades.extend(t)
        periods.append(label)
        print(f"Loaded {len(t):3d} trades [{label}] from {path[-50:]}")

    print(f"Total: {len(all_trades)} trades\n")

    dates = [t["entry_dt"] for t in all_trades]
    d_start = str(min(dates) - timedelta(days=60))
    d_end   = str(max(dates) + timedelta(days=5))
    print(f"Descargando ATR [{d_start} → {d_end}]...")
    atr_map = download_atr(d_start, d_end)
    tag_atr(all_trades, atr_map)
    print()

    by_bucket = defaultdict(list)
    for t in all_trades:
        by_bucket[t["bucket"]].append(t)

    # ─── PREGUNTA 1: ACTIVE shorts por período ───────────────────────────────
    print("=" * 80)
    print("P1: ACTIVE (380-450) SHORTS — ¿se mantiene el edge por período?")
    print("=" * 80)
    active = by_bucket.get("ACTIVE 380-450", [])
    active_shorts = [t for t in active if t["pos"] == "short"]
    print(f"\n  Total ACTIVE shorts: {len(active_shorts)}")
    print(bucket_stats(active_shorts, "TODOS LOS PERÍODOS"))
    print()
    for period in periods:
        grp = [t for t in active_shorts if t["period"] == period]
        print(bucket_stats(grp, f"  {period}"))
    print()
    # Detail
    print(f"  Trades individuales ACTIVE short:")
    for t in sorted(active_shorts, key=lambda x: x["entry_dt"]):
        result = "W" if t["profit"] > 0 else "L"
        print(f"    {t['entry_dt']}  [{t['period']:5s}]  ATR={t['atr']:4.0f}"
              f"  Profit={t['profit']:+7.0f}  MFE={t['mfe']:5.0f}  MAE={t['mae']:5.0f}"
              f"  Exit={t['exit'][:25]}  [{result}]")

    # ─── PREGUNTA 2: MAE/MFE distribución por bucket ─────────────────────────
    print("\n" + "=" * 80)
    print("P2: MAE / MFE DISTRIBUCIÓN por bucket (percentiles en $)")
    print("=" * 80)
    print(f"\n  {'Bucket':20s}  {'n':>3}  "
          f"{'MAE p25':>7}  {'MAE p50':>7}  {'MAE p75':>7}  {'MAE p90':>7}  "
          f"{'MFE p25':>7}  {'MFE p50':>7}  {'MFE p75':>7}  {'MFE p90':>7}  "
          f"{'MFE/MAE':>7}")
    print("-" * 100)
    for _, _, label in ATR_BUCKETS:
        grp = by_bucket.get(label, [])
        if not grp:
            continue
        maes = [t["mae"] for t in grp]
        mfes = [t["mfe"] for t in grp]
        ratio = statistics.mean(t["mfe"] / t["mae"] if t["mae"] > 0 else 0 for t in grp)
        print(f"  {label:20s}  {len(grp):3d}  "
              f"{pct(maes,25):7.0f}  {pct(maes,50):7.0f}  {pct(maes,75):7.0f}  {pct(maes,90):7.0f}  "
              f"{pct(mfes,25):7.0f}  {pct(mfes,50):7.0f}  {pct(mfes,75):7.0f}  {pct(mfes,90):7.0f}  "
              f"{ratio:7.2f}x")
    print()
    # Winners vs Losers MAE
    print(f"  MAE comparado winners vs losers por bucket:")
    print(f"  {'Bucket':20s}  {'MAE Winners avg':>15}  {'MAE Losers avg':>14}  ratio")
    print("-" * 65)
    for _, _, label in ATR_BUCKETS:
        grp = by_bucket.get(label, [])
        if not grp:
            continue
        wins = [t for t in grp if t["profit"] > 0]
        loss = [t for t in grp if t["profit"] <= 0]
        mw = statistics.mean(t["mae"] for t in wins) if wins else 0
        ml = statistics.mean(t["mae"] for t in loss) if loss else 0
        ratio = ml / mw if mw > 0 else 0
        print(f"  {label:20s}  {mw:15.0f}  {ml:14.0f}  {ratio:.2f}x")

    # ─── PREGUNTA 3: Duración por bucket ─────────────────────────────────────
    print("\n" + "=" * 80)
    print("P3: DURACIÓN promedio por bucket (minutos desde entrada a salida)")
    print("=" * 80)
    print(f"\n  {'Bucket':20s}  {'n':>3}  {'min avg':>8}  {'Win avg':>8}  {'Loss avg':>9}  {'p25':>6}  {'p50':>6}  {'p75':>6}")
    print("-" * 75)
    for _, _, label in ATR_BUCKETS:
        grp = [t for t in by_bucket.get(label, []) if t["dur_min"] is not None]
        if not grp:
            continue
        durs  = [t["dur_min"] for t in grp]
        wdurs = [t["dur_min"] for t in grp if t["profit"] > 0]
        ldurs = [t["dur_min"] for t in grp if t["profit"] <= 0]
        print(f"  {label:20s}  {len(grp):3d}"
              f"  {statistics.mean(durs):8.0f}"
              f"  {statistics.mean(wdurs) if wdurs else 0:8.0f}"
              f"  {statistics.mean(ldurs) if ldurs else 0:9.0f}"
              f"  {pct(durs,25):6.0f}"
              f"  {pct(durs,50):6.0f}"
              f"  {pct(durs,75):6.0f}")

    # ─── PREGUNTA 4: Concentración de winners ────────────────────────────────
    print("\n" + "=" * 80)
    print("P4: CONCENTRACIÓN DE WINNERS — ¿el edge viene de pocos trades extremos?")
    print("=" * 80)
    for _, _, label in ATR_BUCKETS:
        grp = by_bucket.get(label, [])
        if not grp:
            continue
        wins  = [t for t in grp if t["profit"] > 0]
        net   = sum(t["profit"] for t in grp)
        gross = sum(t["profit"] for t in wins) if wins else 0

        # Categorías de ganadores
        at_tp    = [t for t in wins if t["profit"] >= TP_USD * 0.95]
        big      = [t for t in wins if TP_USD * 0.5 <= t["profit"] < TP_USD * 0.95]
        small    = [t for t in wins if t["profit"] < TP_USD * 0.5]

        at_tp_sum = sum(t["profit"] for t in at_tp)
        big_sum   = sum(t["profit"] for t in big)
        small_sum = sum(t["profit"] for t in small)

        pct_attp = at_tp_sum / gross * 100 if gross > 0 else 0
        pct_big  = big_sum  / gross * 100 if gross > 0 else 0
        pct_sml  = small_sum/ gross * 100 if gross > 0 else 0

        print(f"\n  {label}  (n={len(grp)}, Net={net:+.0f}, GrossWin={gross:+.0f})")
        print(f"    TP full (>$997):  n={len(at_tp):2d}  sum={at_tp_sum:+7.0f}  ({pct_attp:.0f}% del gross)")
        print(f"    Big (498-997):    n={len(big):2d}  sum={big_sum:+7.0f}  ({pct_big:.0f}% del gross)")
        print(f"    Small (<498):     n={len(small):2d}  sum={small_sum:+7.0f}  ({pct_sml:.0f}% del gross)")

        # Top 3 winners
        top3 = sorted(wins, key=lambda x: x["profit"], reverse=True)[:3]
        for t in top3:
            print(f"      top: {t['entry_dt']}  [{t['period']:5s}]  {t['pos']:5s}  "
                  f"Profit={t['profit']:+7.0f}  Exit={t['exit'][:30]}")

        # Concentración percentil: top 10% y top 20% de winners
        if wins:
            wins_sorted = sorted(wins, key=lambda x: x["profit"], reverse=True)
            n10 = max(1, round(len(wins_sorted) * 0.10))
            n20 = max(1, round(len(wins_sorted) * 0.20))
            sum10 = sum(t["profit"] for t in wins_sorted[:n10])
            sum20 = sum(t["profit"] for t in wins_sorted[:n20])
            p10 = sum10 / gross * 100 if gross > 0 else 0
            p20 = sum20 / gross * 100 if gross > 0 else 0
            print(f"    Top 10% winners: n={n10}  sum={sum10:+7.0f}  ({p10:.0f}% del gross)")
            print(f"    Top 20% winners: n={n20}  sum={sum20:+7.0f}  ({p20:.0f}% del gross)")

    # ─── P4b: concentración global — todos los buckets juntos ────────────────
    print("\n" + "=" * 80)
    print("P4b: CONCENTRACIÓN GLOBAL — top winners sobre el neto total del sistema")
    print("=" * 80)
    all_wins = sorted([t for t in all_trades if t["profit"] > 0],
                      key=lambda x: x["profit"], reverse=True)
    total_net   = sum(t["profit"] for t in all_trades)
    total_gross = sum(t["profit"] for t in all_wins)
    for pct_cut in [10, 20, 50]:
        n_cut  = max(1, round(len(all_wins) * pct_cut / 100))
        s_cut  = sum(t["profit"] for t in all_wins[:n_cut])
        p_of_g = s_cut / total_gross * 100 if total_gross > 0 else 0
        p_of_n = s_cut / total_net   * 100 if total_net   > 0 else 0
        print(f"  Top {pct_cut:2d}% winners (n={n_cut:2d}): sum={s_cut:+7.0f}"
              f"  = {p_of_g:.0f}% del gross  /  {p_of_n:.0f}% del neto")

    # Distribución de profits por cubo (histograma simple)
    print(f"\n  Distribución por tramo de ganancia (todos buckets):")
    ranges = [(-9999, -375, "SL completo"),
              (-374,  -1,   "Pérd parcial"),
              (0,    374,   "Win pequeño"),
              (375,  749,   "Win medio"),
              (750, 1049,   "Win grande"),
              (1050, 9999,  "TP completo")]
    for lo, hi, name in ranges:
        grp = [t for t in all_trades if lo <= t["profit"] <= hi]
        s   = sum(t["profit"] for t in grp)
        print(f"    {name:16s}: n={len(grp):3d}  sum={s:+8.0f}")

    print()


if __name__ == "__main__":
    main()

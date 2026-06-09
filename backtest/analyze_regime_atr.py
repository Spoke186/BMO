#!/usr/bin/env python3
"""
FASE 2 + 3: Análisis de régimen ATR y desglose Setup A/B.

Hipótesis: el rendimiento del sistema está gobernado por el régimen de
volatilidad (ATR diario de NQ), no por la calidad de los setups.

Uso:
    python analyze_regime_atr.py <csv1> [<csv2> ...]
    python analyze_regime_atr.py "resultados/screenshots/NinjaTrader Grid ... .csv"

Requiere: pip install yfinance
"""
from __future__ import annotations
import sys, io, csv, re, statistics
from datetime import date, timedelta

# Safe stdout reconfigure — skip if already wrapped (e.g. called via runpy)
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass
from collections import defaultdict

# stdout UTF-8 setup handled above via reconfigure

# ─────────────────────────────────────────────────────────────────────────────
# ATR buckets (NQ points/day, basado en análisis de 4 períodos)
# ─────────────────────────────────────────────────────────────────────────────
ATR_BUCKETS = [
    (0,    300, "< 300  (CHOP)   "),
    (300,  380, "300-380 (WEAK)  "),
    (380,  450, "380-450 (ACTIVE)"),
    (450, 9999, "> 450  (STRONG) "),
]

# ─────────────────────────────────────────────────────────────────────────────
# 1. Descarga datos NQ diarios y computa ATR rolling
# ─────────────────────────────────────────────────────────────────────────────

def download_daily_atr(start="2025-06-01", end="2026-07-01", window=20) -> dict[str, float]:
    """Retorna {YYYY-MM-DD: ATR_N_day} para cada día de trading."""
    import yfinance as yf
    d = yf.Ticker("NQ=F").history(start=start, end=end, interval="1d")
    if d.empty:
        raise RuntimeError("yfinance no devolvió datos")

    dates  = [x.date() for x in d.index]
    highs  = d["High"].tolist()
    lows   = d["Low"].tolist()
    closes = d["Close"].tolist()

    # True Range: max(H-L, |H-Cprev|, |L-Cprev|)
    trs = []
    for i in range(len(dates)):
        hl = highs[i] - lows[i]
        if i == 0:
            trs.append(hl)
        else:
            hcp = abs(highs[i] - closes[i-1])
            lcp = abs(lows[i]  - closes[i-1])
            trs.append(max(hl, hcp, lcp))

    # Rolling ATR (simple moving average of TR)
    atr_map = {}
    for i, d_ in enumerate(dates):
        lookback = trs[max(0, i - window + 1): i + 1]
        atr_map[str(d_)] = statistics.mean(lookback)

    return atr_map


def atr_bucket(atr_val: float) -> str:
    for lo, hi, label in ATR_BUCKETS:
        if lo <= atr_val < hi:
            return label
    return "? "


# ─────────────────────────────────────────────────────────────────────────────
# 2. Parse CSV de trades (formato NT8 D/M/YYYY, separador ;)
# ─────────────────────────────────────────────────────────────────────────────

def parse_money(s: str) -> float:
    s = re.sub(r'[$ ]', '', s.strip())
    s = s.replace('.', '').replace(',', '.')
    return float(s)

def parse_date(s: str) -> date:
    s = re.sub(r'\s+', ' ', s.strip())
    m = re.match(r'(\d+)/(\d+)/(\d{4})', s)
    if not m:
        raise ValueError(f"Fecha: {s!r}")
    day, month, year = int(m.group(1)), int(m.group(2)), int(m.group(3))
    return date(year, month, day)

def _is_daily_format(header: list[str]) -> bool:
    """Daily-performance export: has 'period' and 'mtr' columns but no 'trade number'."""
    h = " ".join(header)
    return "period" in h and "mtr" in h and "trade number" not in h

def load_trades(csv_path: str) -> list[dict]:
    """
    Handles two NT8 export formats:
    - Trades tab:   Trade number | Market pos. | Entry name | Entry time | Profit | MAE | MFE
    - Daily tab:    Period | # | Net profit | MTR | Avg. MAE | Avg. MFE   (n=1 per day assumed)
    """
    trades = []
    with open(csv_path, encoding="utf-8-sig", errors="replace") as f:
        content = f.read()

    reader = csv.reader(content.splitlines(), delimiter=';')
    header = None
    daily_fmt = False

    for row in reader:
        if not any(c.strip() for c in row):
            continue
        if header is None:
            row_joined = " ".join(row).lower()
            if ("trade number" in row_joined or "market pos" in row_joined
                    or ("period" in row_joined and "mtr" in row_joined)):
                header = [h.strip().lower() for h in row]
                daily_fmt = _is_daily_format(header)
            continue

        rec = dict(zip(header, [c.strip() for c in row]))

        try:
            if daily_fmt:
                # Daily format: one row = one trading day (n=1 assumed)
                raw_period = rec.get("period", "")
                if not raw_period or raw_period.lower() == "total":
                    continue
                entry_dt = parse_date(raw_period)
                profit   = parse_money(rec.get("net profit", "0"))
                mfe      = parse_money(rec.get("avg. mfe",  "0"))
                mae      = parse_money(rec.get("avg. mae",  "0"))
                mtr_raw  = rec.get("mtr", "0").replace(",", ".").strip()
                mtr      = float(mtr_raw) if mtr_raw else 0.0
                t = {
                    "num":      0,
                    "pos":      "unknown",
                    "signal":   "unknown",
                    "entry_dt": entry_dt,
                    "profit":   profit,
                    "exit":     "",
                    "mae":      mae,
                    "mfe":      mfe,
                    "mtr":      mtr,   # actual daily range from NT8
                    "setup":    "?",   # unknown — Daily tab has no entry name
                }
                t["month"] = entry_dt.strftime("%Y-%m")
                trades.append(t)
            else:
                # Trades format (full detail)
                num_raw = rec.get("trade number", "0").strip()
                if not num_raw.isdigit():
                    continue
                t = {
                    "num":      int(num_raw),
                    "pos":      rec.get("market pos.", "").strip().lower(),
                    "signal":   rec.get("entry name", "").strip(),
                    "entry_dt": parse_date(rec.get("entry time", "")),
                    "profit":   parse_money(rec.get("profit", "0")),
                    "exit":     rec.get("exit name", "").strip(),
                    "mae":      parse_money(rec.get("mae", "0")),
                    "mfe":      parse_money(rec.get("mfe", "0")),
                    "mtr":      0.0,
                }
                t["setup"] = "A" if t["signal"] in ("LongFVG", "ShortFVG") else "B"
                t["month"] = t["entry_dt"].strftime("%Y-%m")
                trades.append(t)
        except Exception:
            pass
    return trades, daily_fmt


# ─────────────────────────────────────────────────────────────────────────────
# 3. Estadísticas
# ─────────────────────────────────────────────────────────────────────────────

def stats(trades: list[dict]) -> dict:
    if not trades:
        return dict(n=0, wins=0, wr=0, net=0, avg_win=0, avg_loss=0,
                    expectancy=0, pf=0, max_dd=0, rr=0, avg_mfe=0)
    wins   = [t["profit"] for t in trades if t["profit"] > 0]
    losses = [t["profit"] for t in trades if t["profit"] <= 0]
    net    = sum(t["profit"] for t in trades)
    n      = len(trades)
    avg_w  = sum(wins)   / len(wins)   if wins   else 0
    avg_l  = sum(losses) / len(losses) if losses else 0
    wr     = len(wins) / n
    pf     = sum(wins) / abs(sum(losses)) if losses and sum(losses) != 0 else float('inf')
    exp    = wr * avg_w + (1 - wr) * avg_l
    rr     = abs(avg_w / avg_l) if avg_l != 0 else float('inf')

    # Max drawdown (equity curve)
    equity = [0.0]
    for t in sorted(trades, key=lambda x: x["entry_dt"]):
        equity.append(equity[-1] + t["profit"])
    peak = equity[0]
    max_dd = 0.0
    for e in equity:
        if e > peak: peak = e
        if peak - e > max_dd: max_dd = peak - e

    avg_mfe = statistics.mean(t["mfe"] for t in trades) if trades else 0

    return dict(n=n, wins=len(wins), wr=wr, net=net, avg_win=avg_w,
                avg_loss=avg_l, expectancy=exp, pf=pf, max_dd=max_dd,
                rr=rr, avg_mfe=avg_mfe)


def row(label: str, s: dict) -> str:
    if s['n'] == 0:
        return f"  {label}  n=0"
    return (f"  {label}"
            f"  n={s['n']:2d}"
            f"  WR={s['wr']*100:5.1f}%"
            f"  PF={s['pf']:5.2f}"
            f"  Exp={s['expectancy']:+6.0f}"
            f"  Net={s['net']:+7.0f}"
            f"  AvgW={s['avg_win']:+5.0f}"
            f"  AvgL={s['avg_loss']:+5.0f}"
            f"  RR={s['rr']:4.2f}"
            f"  DD={-s['max_dd']:+6.0f}"
            f"  MFE={s['avg_mfe']:5.0f}")


# ─────────────────────────────────────────────────────────────────────────────
# 4. Main
# ─────────────────────────────────────────────────────────────────────────────

def main():
    if len(sys.argv) < 2:
        print("Uso: python analyze_regime_atr.py <csv1> [<csv2> ...]")
        sys.exit(1)

    # Cargar todos los trades
    all_trades = []
    has_detail = False  # True si al menos un archivo tiene Setup/Direction info
    for path in sys.argv[1:]:
        t, daily = load_trades(path)
        fmt = "Daily" if daily else "Trades"
        if not daily:
            has_detail = True
        print(f"Loaded {len(t):3d} trades [{fmt}] from {path[-55:]}")
        all_trades.extend(t)

    if not all_trades:
        print("ERROR: ningún trade cargado. Verificar formato CSV.")
        sys.exit(1)
    print(f"Total trades: {len(all_trades)}")

    # Calcular rango de fechas
    dates_all = [t["entry_dt"] for t in all_trades]
    d_start   = str(min(dates_all) - timedelta(days=60))  # warmup para ATR
    d_end     = str(max(dates_all) + timedelta(days=5))

    print(f"Descargando ATR NQ [{d_start} → {d_end}]...")
    try:
        atr_map = download_daily_atr(d_start, d_end, window=20)
    except Exception as e:
        print(f"ERROR ATR: {e}")
        sys.exit(1)

    # Taggear trades con ATR
    no_atr = 0
    for t in all_trades:
        key = str(t["entry_dt"])
        if key in atr_map:
            t["atr"] = atr_map[key]
            t["bucket"] = atr_bucket(t["atr"])
        else:
            # Buscar día cercano
            found = False
            for delta in range(1, 5):
                k2 = str(t["entry_dt"] - timedelta(days=delta))
                if k2 in atr_map:
                    t["atr"] = atr_map[k2]
                    t["bucket"] = atr_bucket(t["atr"])
                    found = True
                    break
            if not found:
                t["atr"] = 0
                t["bucket"] = "NO_DATA"
                no_atr += 1

    if no_atr:
        print(f"[WARN] {no_atr} trades sin dato ATR")

    if not has_detail:
        print("\n[INFO] Todos los archivos en formato Daily — Setup A/B y Long/Short no disponibles.")
        print("[INFO] Para FASE 3 completa: exportar pestaña 'Trades' (no 'Daily') en NT8.")

    # ─────────────────────────────────────────────────────
    # FASE 2: ATR bucket analysis
    # ─────────────────────────────────────────────────────
    print("\n" + "="*110)
    print("FASE 2: HIPÓTESIS ATR — ¿Existe transición clara entre regímenes de volatilidad?")
    print("  Sistema: SL=$375 (93pts) / TP=$1,050 (263pts). TP requiere 263pts de movimiento favorable.")
    print("="*110)

    buckets_ordered = [label for _, _, label in ATR_BUCKETS]
    by_bucket = defaultdict(list)
    for t in all_trades:
        by_bucket[t["bucket"]].append(t)

    print(f"\n{'Bucket ATR':22}  {'n':>3}  {'WR':>6}  {'PF':>5}  {'Exp':>7}  {'Net':>8}  "
          f"{'AvgW':>6}  {'AvgL':>6}  {'RR':>4}  {'MaxDD':>7}  {'AvgMFE':>7}")
    print("-"*110)

    for label in buckets_ordered:
        group = by_bucket.get(label, [])
        s = stats(group)
        if s['n'] == 0:
            print(f"  {label}  n=0")
            continue
        # ATR promedio del grupo
        atr_avg = statistics.mean(t["atr"] for t in group)
        pct_tp  = 263 / atr_avg * 100 if atr_avg > 0 else 0
        print(f"  {label}  "
              f"n={s['n']:2d}  "
              f"WR={s['wr']*100:5.1f}%  "
              f"PF={s['pf']:5.2f}  "
              f"Exp={s['expectancy']:+6.0f}  "
              f"Net={s['net']:+7.0f}  "
              f"AvgW={s['avg_win']:+6.0f}  "
              f"AvgL={s['avg_loss']:+6.0f}  "
              f"RR={s['rr']:4.2f}  "
              f"DD={-s['max_dd']:+7.0f}  "
              f"MFE={s['avg_mfe']:5.0f}  "
              f"[ATR={atr_avg:.0f} TP={pct_tp:.0f}% ATR]")

    # ─────────────────────────────────────────────────────
    # FASE 2b: ATR vs métricas clave (correlación)
    # ─────────────────────────────────────────────────────
    print("\n" + "="*110)
    print("FASE 2b: ATR vs RR LOGRADO — correlación directa")
    print("="*110)
    print(f"\n  {'Fecha':12}  {'ATR':>5}  {'Bucket':22}  {'Dir':6}  {'Setup':6}  "
          f"{'Profit':>8}  {'MFE':>6}  {'Exit':25}")
    print("-"*90)

    for t in sorted(all_trades, key=lambda x: x["entry_dt"]):
        result = "W" if t["profit"] > 0 else "L"
        mtr_str = f"{t['mtr']:5.0f}" if t.get("mtr", 0) > 0 else "   — "
        print(f"  {str(t['entry_dt']):12}  "
              f"ATR={t['atr']:4.0f}  "
              f"MTR={mtr_str}  "
              f"{t['bucket']}  "
              f"{t['pos']:8}  "
              f"S{t['setup']}  "
              f"{t['profit']:+7.0f}  "
              f"MFE={t['mfe']:5.0f}  "
              f"[{result}]")

    # ─────────────────────────────────────────────────────
    # FASE 3: Desglose Setup A vs B por régimen
    # ─────────────────────────────────────────────────────
    print("\n" + "="*110)
    print("FASE 3: DESGLOSE SETUP A vs B × RÉGIMEN ATR")
    print("="*110)

    for label in buckets_ordered:
        group = by_bucket.get(label, [])
        if not group:
            continue
        sa = [t for t in group if t["setup"] == "A"]
        sb = [t for t in group if t["setup"] == "B"]
        sl = [t for t in group if t["pos"] == "long"]
        ss = [t for t in group if t["pos"] == "short"]
        atr_avg = statistics.mean(t["atr"] for t in group)
        print(f"\n  {label}  (ATR avg={atr_avg:.0f}  n={len(group)})")
        print(row(f"    Setup A     ", stats(sa)))
        print(row(f"    Setup B     ", stats(sb)))
        print(row(f"    Longs       ", stats(sl)))
        print(row(f"    Shorts      ", stats(ss)))
        print(row(f"    TOTAL       ", stats(group)))

    # ─────────────────────────────────────────────────────
    # FASE 3b: Desglose mensual con ATR promedio del mes
    # ─────────────────────────────────────────────────────
    print("\n" + "="*110)
    print("FASE 3b: DESGLOSE POR MES — ATR promedio del mes vs rendimiento")
    print("="*110)
    by_month = defaultdict(list)
    for t in all_trades:
        by_month[t["month"]].append(t)

    print(f"\n  {'Mes':8}  {'ATR':>5}  {'n':>3}  {'WR':>6}  {'PF':>5}  "
          f"{'Net':>7}  {'AvgW':>6}  {'RR':>4}  {'MFE':>6}")
    print("-"*75)
    for month in sorted(by_month.keys()):
        group = by_month[month]
        s = stats(group)
        if s['n'] == 0:
            continue
        atr_avg = statistics.mean(t["atr"] for t in group if t["atr"] > 0) or 0
        print(f"  {month:8}  "
              f"{atr_avg:5.0f}  "
              f"n={s['n']:2d}  "
              f"WR={s['wr']*100:5.1f}%  "
              f"PF={s['pf']:5.2f}  "
              f"Net={s['net']:+7.0f}  "
              f"AvgW={s['avg_win']:+6.0f}  "
              f"RR={s['rr']:4.2f}  "
              f"MFE={s['avg_mfe']:5.0f}")

    # ─────────────────────────────────────────────────────
    # RESUMEN EJECUTIVO
    # ─────────────────────────────────────────────────────
    print("\n" + "="*110)
    print("RESUMEN EJECUTIVO: ¿ATR es la variable dominante?")
    print("="*110)

    # Pearson correlation ATR vs Profit per trade
    atrs    = [t["atr"]    for t in all_trades if t["atr"] > 0]
    profits = [t["profit"] for t in all_trades if t["atr"] > 0]
    if len(atrs) > 5:
        n = len(atrs)
        mx = statistics.mean(atrs)
        my = statistics.mean(profits)
        num = sum((a - mx) * (p - my) for a, p in zip(atrs, profits))
        da  = (sum((a - mx)**2 for a in atrs)) ** 0.5
        dp  = (sum((p - my)**2 for p in profits)) ** 0.5
        r   = num / (da * dp) if da * dp > 0 else 0
        print(f"\n  Correlación Pearson (ATR vs Profit/trade): r = {r:.3f}")
        strength = "FUERTE" if abs(r) > 0.4 else "MODERADA" if abs(r) > 0.2 else "DÉBIL"
        print(f"  Interpretación: correlación {strength} {'positiva' if r > 0 else 'negativa'}")
        print(f"  r² = {r**2:.3f} → ATR explica ~{r**2*100:.0f}% de la varianza del P&L por trade")

    # TP alcanzability por bucket
    print(f"\n  TP=263pts requiere estos % del ATR diario:")
    for lo, hi, label in ATR_BUCKETS:
        mid = (lo + hi) / 2 if hi < 9999 else lo + 100
        pct = 263 / mid * 100
        print(f"    {label}: {pct:.0f}% del ATR diario")

    print(f"\n  INTERPRETACIÓN:")
    print(f"  ATR < 300: TP requiere >88% del rango diario → improbable → RR colapsa a ~1.1")
    print(f"  ATR 380-450: TP requiere ~63-69% del rango   → posible    → RR ~2.0")
    print(f"  ATR > 450: TP requiere <58% del rango        → frecuente  → RR ~2.5+")
    print()


if __name__ == "__main__":
    main()

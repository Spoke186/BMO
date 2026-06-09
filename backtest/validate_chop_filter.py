#!/usr/bin/env python3
"""
VALIDACIÓN FASE 4 — 3 pruebas antes de implementar filtro ATR20 < 300

Prueba 1: Curva completa de umbrales (270–330)
Prueba 2: Consistencia por trimestre
Prueba 3: Simulación operativa baseline vs filtrado
"""
from __future__ import annotations
import sys, io, re, os, statistics, glob as globmod
from datetime import date
from collections import defaultdict

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ATR_WINDOW = 20

PERIODS = {
    "Jul-Sep 2025": (date(2025, 7, 1),  date(2025, 9, 30)),
    "Oct-Dec 2025": (date(2025, 10, 1), date(2025, 12, 31)),
    "Jan-Mar 2026": (date(2026, 1, 1),  date(2026, 3, 31)),
    "Apr-Jun 2026": (date(2026, 4, 1),  date(2026, 6, 30)),
}


# ─────────────────────────────────────────────────────────────────────────────
# Datos NQ
# ─────────────────────────────────────────────────────────────────────────────

def download_atr(start="2025-01-01", end="2026-07-01") -> dict[date, float]:
    import yfinance as yf
    d = yf.Ticker("NQ=F").history(start=start, end=end, interval="1d")
    if d.empty:
        raise RuntimeError("yfinance vacío")
    rows = []
    for dt, row in d.iterrows():
        rows.append({"date": dt.date(), "h": row["High"], "l": row["Low"], "c": row["Close"]})
    closes = [r["c"] for r in rows]
    trs = []
    for i, r in enumerate(rows):
        hl = r["h"] - r["l"]
        trs.append(max(hl, abs(r["h"] - closes[i-1]), abs(r["l"] - closes[i-1])) if i > 0 else hl)
    atr_map = {}
    for i, r in enumerate(rows):
        if i >= ATR_WINDOW - 1:
            atr_map[r["date"]] = statistics.mean(trs[i - ATR_WINDOW + 1 : i + 1])
    return atr_map


def regime(atr: float) -> str:
    if atr < 300: return "CHOP"
    elif atr < 380: return "WEAK"
    elif atr < 450: return "ACTIVE"
    return "STRONG"


# ─────────────────────────────────────────────────────────────────────────────
# Carga de trades NT8
# ─────────────────────────────────────────────────────────────────────────────

def parse_date(s: str) -> date | None:
    m = re.match(r"(\d{1,2})/(\d{1,2})/(\d{4})", s.strip())
    if not m:
        return None
    d, mo, y = int(m.group(1)), int(m.group(2)), int(m.group(3))
    try:
        return date(y, mo, d)
    except ValueError:
        return None


def parse_float(s: str) -> float:
    # NT8 formato colombiano: "-$ 375,00" (negativo) o "$ 184,00" (positivo)
    s = s.strip()
    negative = s.startswith("-")
    # Quitar símbolo $, espacios y signo — reconstruir al final
    s = s.replace("-", "").replace("$", "").replace(" ", "").strip()
    # Quitar paréntesis contables: (375,00) → 375,00
    if s.startswith("(") and s.endswith(")"):
        s = s[1:-1]
        negative = True
    # Coma decimal sin punto miles: "375,00" → "375.00"
    if "," in s and "." not in s:
        s = s.replace(",", ".")
    elif "," in s and "." in s:
        # Ambos: coma es decimal si viene después del punto: "1.050,50"
        if s.rindex(",") > s.rindex("."):
            s = s.replace(".", "").replace(",", ".")
        else:
            s = s.replace(",", "")
    try:
        val = float(s)
        return -val if negative else val
    except ValueError:
        return 0.0


def load_csv(path: str) -> list[dict]:
    raw = open(path, "rb").read().decode("utf-8-sig", errors="replace")
    lines = [l.rstrip("\r\n") for l in raw.splitlines() if l.strip()]
    if not lines:
        return []
    sep = ";" if lines[0].count(";") > lines[0].count(",") else ","
    header = [h.strip().lower() for h in lines[0].split(sep)]

    def idx(*aliases):
        for a in aliases:
            try: return header.index(a)
            except ValueError: pass
        return -1

    # Formato Trades: tiene "trade #" o "trade number"
    if "trade #" not in header and "trade number" not in header:
        return []

    i_time   = idx("entry time", "entry date/time", "entry date")
    i_profit = idx("profit", "profit (usd)", "net profit")
    i_pos    = idx("market pos.", "market pos", "position")
    i_sig    = idx("entry name", "entry signal", "signal")

    if i_time < 0 or i_profit < 0:
        return []

    trades = []
    for line in lines[1:]:
        parts = [p.strip().strip('"') for p in line.split(sep)]
        if len(parts) < max(i_time, i_profit) + 1:
            continue
        dt = parse_date(parts[i_time])
        if dt is None:
            continue
        profit = parse_float(parts[i_profit])
        pos    = parts[i_pos].lower() if i_pos >= 0 and i_pos < len(parts) else ""
        signal = parts[i_sig] if i_sig >= 0 and i_sig < len(parts) else ""
        trades.append({"date": dt, "profit": profit, "pos": pos, "signal": signal})
    return trades


def load_all_trades(script_dir: str) -> list[dict]:
    # Usa _csv_paths.bin — solo los 4 archivos validados en FASE 3
    bin_path = os.path.join(script_dir, "_csv_paths.bin")
    paths = open(bin_path, "rb").read().decode("utf-8").strip().splitlines()
    paths = [p.strip() for p in paths if p.strip()]

    all_trades = []
    for path in paths:
        t = load_csv(path)
        fname = os.path.basename(path)
        print(f"  {fname[-45:]:45}  {len(t):3} trades")
        all_trades.extend(t)

    seen, unique = set(), []
    for t in all_trades:
        key = (t["date"], round(t["profit"], 2))
        if key not in seen:
            seen.add(key)
            unique.append(t)
    return sorted(unique, key=lambda x: x["date"])


# ─────────────────────────────────────────────────────────────────────────────
# Estadísticas
# ─────────────────────────────────────────────────────────────────────────────

def stats(trades: list[dict]) -> dict:
    if not trades:
        return {"n": 0, "wr": 0.0, "pf": 0.0, "net": 0.0, "avg": 0.0, "maxdd": 0.0}
    profits = [t["profit"] for t in trades]
    wins  = [p for p in profits if p > 0]
    loss  = [p for p in profits if p <= 0]
    n     = len(profits)
    wr    = len(wins) / n * 100
    gw    = sum(wins)
    gl    = abs(sum(loss))
    pf    = gw / gl if gl > 0 else float("inf")
    net   = sum(profits)
    avg   = net / n
    # Max drawdown
    eq = pk = mxdd = 0.0
    for p in profits:
        eq += p
        if eq > pk: pk = eq
        mxdd = max(mxdd, pk - eq)
    return {"n": n, "wr": wr, "pf": pf, "net": net, "avg": avg, "maxdd": mxdd}


# ─────────────────────────────────────────────────────────────────────────────
# Main
# ─────────────────────────────────────────────────────────────────────────────

def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_dir   = os.path.dirname(script_dir)
    csv_dir    = os.path.join(repo_dir, "resultados", "screenshots")

    print("Descargando ATR NQ (2025-01-01 → 2026-07-01)...")
    atr_map = download_atr()
    print(f"  {len(atr_map)} días con ATR20 calculado\n")

    print("Cargando trades NT8...")
    all_trades = load_all_trades(script_dir)

    tagged, missing = [], 0
    for t in all_trades:
        a = atr_map.get(t["date"])
        if a is None:
            missing += 1
            continue
        t2 = dict(t)
        t2["atr20"]   = a
        t2["regime"]  = regime(a)
        t2["is_chop"] = a < 300
        tagged.append(t2)

    print(f"\n  Total trades únicos con ATR: {len(tagged)}  (sin ATR: {missing})")

    base = stats(tagged)

    # ─────────────────────────────────────────────────────────────────────
    # PRUEBA 1 — Curva de umbrales
    # ─────────────────────────────────────────────────────────────────────
    W = "="*105
    print(f"\n{W}")
    print("PRUEBA 1: Curva completa de umbrales ATR20")
    print("  ¿El patrón es robusto o depende de un único punto de corte exacto?")
    print(W)
    print(f"\n  {'Umbral':>10}  {'Excluidos':>10}  {'Operados':>9}  {'Net oper. ($)':>14}  {'Net excl. ($)':>14}  {'PF oper.':>9}  {'WR oper.':>9}  {'MaxDD oper.':>12}")
    print("-"*105)

    for thr in [270, 280, 290, 300, 310, 320, 330]:
        kept = [t for t in tagged if t["atr20"] >= thr]
        excl = [t for t in tagged if t["atr20"] <  thr]
        ks   = stats(kept)
        es   = stats(excl)
        marker = "  ← PROPUESTO" if thr == 300 else ""
        print(f"  ATR < {thr:<5}  {len(excl):>10}  {len(kept):>9}  ${ks['net']:>+12,.0f}  ${es['net']:>+12,.0f}  {ks['pf']:>9.2f}  {ks['wr']:>8.1f}%  ${ks['maxdd']:>10,.0f}{marker}")

    print(f"  {'BASELINE':>10}  {'—':>10}  {base['n']:>9}  ${base['net']:>+12,.0f}  {'—':>14}  {base['pf']:>9.2f}  {base['wr']:>8.1f}%  ${base['maxdd']:>10,.0f}")

    # ─────────────────────────────────────────────────────────────────────
    # PRUEBA 2 — Consistencia por trimestre
    # ─────────────────────────────────────────────────────────────────────
    print(f"\n{W}")
    print("PRUEBA 2: Consistencia por trimestre")
    print("  ¿El filtro ayuda en todos los períodos o solo en uno que domina?")
    print(W)
    print(f"\n  {'Período':15}  {'Base N':>7}  {'CHOP N':>7}  {'CHOP %':>7}  {'Net base':>11}  {'Net filt':>11}  {'PF base':>8}  {'PF filt':>8}  {'DD base':>10}  {'DD filt':>10}")
    print("-"*105)

    total_chop = 0
    for pname, (ps, pe) in PERIODS.items():
        pt   = [t for t in tagged if ps <= t["date"] <= pe]
        chop = [t for t in pt if t["is_chop"]]
        filt = [t for t in pt if not t["is_chop"]]
        bs   = stats(pt)
        fs   = stats(filt)
        chop_pct = len(chop) / len(pt) * 100 if pt else 0
        total_chop += len(chop)
        print(f"  {pname:15}  {bs['n']:>7}  {len(chop):>7}  {chop_pct:>6.0f}%  ${bs['net']:>+9,.0f}  ${fs['net']:>+9,.0f}  {bs['pf']:>8.2f}  {fs['pf']:>8.2f}  ${bs['maxdd']:>8,.0f}  ${fs['maxdd']:>8,.0f}")

    filt_all = [t for t in tagged if not t["is_chop"]]
    fa = stats(filt_all)
    chop_pct_total = total_chop / len(tagged) * 100 if tagged else 0
    print("-"*105)
    print(f"  {'TOTAL':15}  {base['n']:>7}  {total_chop:>7}  {chop_pct_total:>6.0f}%  ${base['net']:>+9,.0f}  ${fa['net']:>+9,.0f}  {base['pf']:>8.2f}  {fa['pf']:>8.2f}  ${base['maxdd']:>8,.0f}  ${fa['maxdd']:>8,.0f}")

    # ─────────────────────────────────────────────────────────────────────
    # PRUEBA 3 — Simulación operativa
    # ─────────────────────────────────────────────────────────────────────
    print(f"\n{W}")
    print("PRUEBA 3: Simulación operativa — Baseline vs ATR20 < 300 filtrado")
    print(W)

    filt300   = stats(filt_all)
    chop_only = stats([t for t in tagged if t["is_chop"]])

    print(f"\n  {'Métrica':22}  {'Baseline':>12}  {'Filtrado (≥300)':>16}  {'CHOP excluido':>14}  {'Δ':>10}")
    print("-"*82)

    def prow(label, bv, fv, cv, fmt, pfx=""):
        delta = fv - bv
        sign  = "+" if delta >= 0 else ""
        print(f"  {label:22}  {pfx}{fmt.format(bv):>12}  {pfx}{fmt.format(fv):>16}  {pfx}{fmt.format(cv):>14}  {sign}{pfx}{fmt.format(delta):>10}")

    print(f"  {'Trades (N)':22}  {base['n']:>12}  {filt300['n']:>16}  {chop_only['n']:>14}  {filt300['n']-base['n']:>+10}")
    print(f"  {'Win Rate (%)':22}  {base['wr']:>11.1f}%  {filt300['wr']:>15.1f}%  {chop_only['wr']:>13.1f}%  {filt300['wr']-base['wr']:>+9.1f}%")
    prow("Profit Factor",     base['pf'],     filt300['pf'],     chop_only['pf'],     "{:.2f}")
    prow("Net Profit ($)",    base['net'],    filt300['net'],    chop_only['net'],    "{:>+,.0f}", "$")
    prow("Avg Trade ($)",     base['avg'],    filt300['avg'],    chop_only['avg'],    "{:>+,.0f}", "$")
    prow("Max Drawdown ($)",  base['maxdd'],  filt300['maxdd'],  0.0,                 "{:>,.0f}",  "$")

    # Desglose por régimen (para referencia)
    print(f"\n  Desglose por régimen (todos los trades):")
    for r in ["CHOP", "WEAK", "ACTIVE", "STRONG"]:
        rt = [t for t in tagged if t["regime"] == r]
        if rt:
            rs = stats(rt)
            print(f"    {r:8}  n={rs['n']:3}  WR={rs['wr']:5.1f}%  PF={rs['pf']:.2f}  Net=${rs['net']:>+8,.0f}  Avg=${rs['avg']:>+6,.0f}")

    # Veredicto formal
    print(f"\n{'='*105}")
    print("VEREDICTO FORMAL — Criterios de aprobación")
    print(f"{'='*105}")
    criterios = [
        ("PF mejoró",              filt300['pf']     > base['pf'],     f"{base['pf']:.2f} → {filt300['pf']:.2f}"),
        ("Expectancy mejoró",      filt300['avg']    > base['avg'],     f"${base['avg']:+.0f} → ${filt300['avg']:+.0f}/trade"),
        ("Drawdown bajó",          filt300['maxdd']  < base['maxdd'],   f"${base['maxdd']:,.0f} → ${filt300['maxdd']:,.0f}"),
        ("Retención ≥60% trades",  filt300['n']/base['n'] >= 0.60,     f"{filt300['n']}/{base['n']} = {filt300['n']/base['n']*100:.0f}%"),
    ]
    passed = 0
    for label, ok, detail in criterios:
        mark = "PASA" if ok else "FALLA"
        if ok: passed += 1
        print(f"  [{mark}]  {label:30}  {detail}")

    print(f"\n  RESULTADO FINAL: {passed}/{len(criterios)} criterios superados")
    if passed == len(criterios):
        print("  → FILTRO ATR20 < 300 LISTO PARA IMPLEMENTACIÓN (pendiente aprobación)")
    elif passed >= 3:
        print("  → Resultado positivo con reservas — revisar criterio fallido")
    else:
        print("  → No supera validación — revisar hipótesis")


if __name__ == "__main__":
    main()

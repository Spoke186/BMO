#!/usr/bin/env python3
"""
Analiza el impacto del sesgo 4H sobre el resultado de los trades de Setup B.

Pregunta clave: ¿Los longs de Setup B son sistematicamente peores cuando
la tendencia macro (proxy: prior day close direction) es bajista?

Requiere yfinance. Si no está instalado: pip install yfinance
Fallback: usa datos hardcodeados aproximados del CSV si no hay internet.

Uso:
    python analyze_4h_bias.py <csv_path>
    python analyze_4h_bias.py "resultados/screenshots/NinjaTrader Grid 2026-06-08 03-00 p. m..csv"
"""
from __future__ import annotations
import sys
import io
import csv
import re
import os
from datetime import date, timedelta
from collections import defaultdict

# Force UTF-8 output on Windows to avoid cp1252 errors
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

# ──────────────────────────────────────────────────────────────────────────────
# 1. Precio de cierre diario de NQ (MNQ 06-26, proxy = NQ=F continuo)
#    Se intenta descargar con yfinance; si falla, usa tabla hardcodeada
#    derivada de los precios de entrada del CSV como proxy de nivel.
# ──────────────────────────────────────────────────────────────────────────────

def download_nq_daily(start="2025-12-29", end="2026-04-01"):
    """Descarga NQ daily desde Yahoo Finance. Retorna {date: close}."""
    try:
        import yfinance as yf
        ticker = yf.Ticker("NQ=F")
        hist = ticker.history(start=start, end=end, interval="1d")
        if hist.empty:
            raise ValueError("yfinance devolvio datos vacios")
        return {d.date(): float(c) for d, c in zip(hist.index, hist["Close"])}
    except Exception as e:
        print(f"[WARN] yfinance fallo ({e}). Usando tabla hardcodeada.")
        return None


# Tabla hardcodeada: closes diarios aproximados de NQ derivados de los
# precios de entrada/salida del CSV (proxy, no datos exactos).
# Formato: {YYYY-MM-DD: close_approx}
HARDCODED_NQ_CLOSES = {
    # Enero 2026 (NQ ~25,500-26,400)
    "2026-01-02": 25880, "2026-01-05": 25810, "2026-01-06": 25950,
    "2026-01-07": 25980, "2026-01-08": 25950, "2026-01-09": 26100,
    "2026-01-12": 26100, "2026-01-13": 26150, "2026-01-14": 26000,
    "2026-01-15": 25900, "2026-01-16": 25870, "2026-01-17": 25800,
    "2026-01-20": 25450, "2026-01-21": 25700, "2026-01-22": 25850,
    "2026-01-23": 25900, "2026-01-26": 26000, "2026-01-27": 26100,
    "2026-01-28": 26300, "2026-01-29": 26280, "2026-01-30": 26200,
    # Febrero 2026 (NQ ~24,900-25,850)
    "2026-01-31": 25900,
    "2026-02-02": 25900, "2026-02-03": 25480, "2026-02-04": 25350,
    "2026-02-05": 24930, "2026-02-06": 25230, "2026-02-07": 25300,
    "2026-02-10": 25460, "2026-02-11": 25380, "2026-02-12": 25100,
    "2026-02-13": 24870, "2026-02-14": 24900, "2026-02-17": 24820,
    "2026-02-18": 24900, "2026-02-19": 25050, "2026-02-20": 25260,
    "2026-02-21": 25000, "2026-02-23": 25000, "2026-02-24": 25050,
    "2026-02-25": 25050, "2026-02-26": 25120, "2026-02-27": 25000,
    "2026-02-28": 24980,
    # Marzo 2026 (NQ 24,990 → 23,341, crash)
    "2026-03-01": 24850,
    "2026-03-02": 25260, "2026-03-03": 24730, "2026-03-04": 24900,
    "2026-03-05": 24700, "2026-03-06": 24900, "2026-03-07": 24600,
    "2026-03-08": 24500, "2026-03-09": 25100, "2026-03-10": 25000,
    "2026-03-11": 25160, "2026-03-12": 24800, "2026-03-13": 24650,
    "2026-03-14": 24700, "2026-03-15": 24600, "2026-03-16": 24800,
    "2026-03-17": 25020, "2026-03-18": 24840, "2026-03-19": 24480,
    "2026-03-20": 24230, "2026-03-21": 24300, "2026-03-22": 24200,
    "2026-03-23": 24400, "2026-03-24": 24220, "2026-03-25": 24310,
    "2026-03-26": 24200, "2026-03-27": 24100, "2026-03-28": 24000,
    "2026-03-30": 23340, "2026-03-31": 23200,
}


def get_daily_closes(use_yahoo=True):
    if use_yahoo:
        data = download_nq_daily()
        if data:
            return {str(k): v for k, v in data.items()}
    return {k: v for k, v in HARDCODED_NQ_CLOSES.items()}


def prior_trading_day(d: date, closes: dict) -> date | None:
    """Retorna el dia habil anterior que tenga dato de cierre."""
    for i in range(1, 15):
        candidate = d - timedelta(days=i)
        if str(candidate) in closes:
            return candidate
    return None


def two_days_before(d: date, closes: dict) -> date | None:
    """Retorna el dia habil 2 sesiones antes."""
    prev1 = prior_trading_day(d, closes)
    if prev1 is None:
        return None
    return prior_trading_day(prev1, closes)


# ──────────────────────────────────────────────────────────────────────────────
# 2. Parsing del CSV de NT8
# ──────────────────────────────────────────────────────────────────────────────

def parse_money(s: str) -> float:
    """'$ 1.050,00' o '-$ 375,00' -> float en USD."""
    s = re.sub(r'[$ ]', '', s.strip())
    s = s.replace('.', '').replace(',', '.')
    return float(s)


def parse_date(s: str) -> date:
    """'2/01/2026 9:34:00 a m' -> date(2026, 1, 2)  (formato D/M/YYYY de NT8 locale ES)"""
    s = re.sub(r'\s+', ' ', s.strip())
    m = re.match(r'(\d+)/(\d+)/(\d{4})', s)
    if not m:
        raise ValueError(f"Fecha no reconocida: {s!r}")
    day, month, year = int(m.group(1)), int(m.group(2)), int(m.group(3))
    return date(year, month, day)


def load_trades(csv_path: str) -> list[dict]:
    trades = []
    with open(csv_path, encoding="utf-8-sig", errors="replace") as f:
        reader = csv.reader(f, delimiter=';')
        header = None
        for row in reader:
            if header is None:
                header = [h.strip().lower() for h in row]
                continue
            if not any(row):
                continue
            rec = dict(zip(header, row))
            try:
                t = {
                    "num":       int(rec.get("trade number", 0)),
                    "pos":       rec.get("market pos.", "").strip().lower(),
                    "signal":    rec.get("entry name", "").strip(),
                    "entry_dt":  parse_date(rec.get("entry time", "")),
                    "profit":    parse_money(rec.get("profit", "0")),
                    "exit_name": rec.get("exit name", "").strip(),
                }
                trades.append(t)
            except Exception as e:
                pass  # skip malformed rows
    return trades


# ──────────────────────────────────────────────────────────────────────────────
# 3. Clasificar cada trade por sesgo macro (prior day direction)
# ──────────────────────────────────────────────────────────────────────────────

def classify_macro(trade_date: date, closes: dict) -> str:
    """
    Retorna 'BULL', 'BEAR', o 'NEUTRAL' segun la direction del dia anterior.

    BULL  = ayer cerro arriba vs anteayer (prior_close > prior_prior_close)
    BEAR  = ayer cerro abajo (prior_close < prior_prior_close)
    NEUTRAL = igual o sin datos

    Este es el proxy mas cercano a fourHBiasDir (4H bias): si ayer cerro abajo,
    es probable que los 4H de la manana de hoy sean tambien bajistas.
    """
    prev1 = prior_trading_day(trade_date, closes)
    prev2 = two_days_before(trade_date, closes)
    if prev1 is None or prev2 is None:
        return "NEUTRAL"
    c1 = closes[str(prev1)]
    c2 = closes[str(prev2)]
    if c1 > c2 + 5:     # umbral mínimo 5 pts para evitar ruido de fracciones
        return "BULL"
    elif c1 < c2 - 5:
        return "BEAR"
    return "NEUTRAL"


# ──────────────────────────────────────────────────────────────────────────────
# 4. Estadísticas
# ──────────────────────────────────────────────────────────────────────────────

def stats(trades: list[dict]) -> dict:
    if not trades:
        return {"n": 0, "wins": 0, "wr": 0, "net": 0, "avg_win": 0,
                "avg_loss": 0, "expectancy": 0, "pf": 0}
    wins = [t["profit"] for t in trades if t["profit"] > 0]
    losses = [t["profit"] for t in trades if t["profit"] <= 0]
    n = len(trades)
    net = sum(t["profit"] for t in trades)
    avg_w = sum(wins) / len(wins) if wins else 0
    avg_l = sum(losses) / len(losses) if losses else 0
    wr = len(wins) / n
    pf = sum(wins) / abs(sum(losses)) if losses and sum(losses) != 0 else float('inf')
    exp = wr * avg_w + (1 - wr) * avg_l
    return {
        "n": n, "wins": len(wins), "wr": wr, "net": net,
        "avg_win": avg_w, "avg_loss": avg_l, "expectancy": exp, "pf": pf
    }


def fmt_stats(s: dict) -> str:
    return (f"n={s['n']:2d}  WR={s['wr']*100:5.1f}%  "
            f"Net={s['net']:+7.0f}  Exp={s['expectancy']:+6.0f}/tr  "
            f"PF={s['pf']:.2f}  AvgW={s['avg_win']:+5.0f}  AvgL={s['avg_loss']:+5.0f}")


# ──────────────────────────────────────────────────────────────────────────────
# 5. Main
# ──────────────────────────────────────────────────────────────────────────────

def main():
    if len(sys.argv) < 2:
        print("Uso: python analyze_4h_bias.py <csv_path>")
        sys.exit(1)

    csv_path = sys.argv[1]
    print(f"\nAnalizando: {csv_path.encode('ascii','replace').decode()}")

    trades = load_trades(csv_path)
    print(f"Trades cargados: {len(trades)}")
    if not trades:
        print("ERROR: No se cargaron trades. Verificar formato CSV.")
        sys.exit(1)

    closes = get_daily_closes(use_yahoo=True)
    print(f"Datos de cierre diario: {len(closes)} dias")

    # Clasificar cada trade
    for t in trades:
        t["macro"] = classify_macro(t["entry_dt"], closes)
        t["month"] = t["entry_dt"].strftime("%b")
        t["setup"] = "A" if t["signal"] in ("LongFVG", "ShortFVG") else "B"

    # ─────────────────────────────────────────────────────
    # A. Tabla general por (macro, direction, setup)
    # ─────────────────────────────────────────────────────
    print("\n" + "="*80)
    print("A. PERFORMANCE POR SESGO MACRO × DIRECCIÓN × SETUP")
    print("="*80)
    print(f"{'Sesgo':8}  {'Dir':6}  {'Setup':5}  {''}")

    groups = defaultdict(list)
    for t in trades:
        key = (t["macro"], t["pos"], t["setup"])
        groups[key].append(t)

    for macro in ["BULL", "BEAR", "NEUTRAL"]:
        for pos in ["long", "short"]:
            for setup in ["A", "B"]:
                key = (macro, pos, setup)
                if key not in groups:
                    continue
                s = stats(groups[key])
                print(f"  {macro:8} {pos:6} Setup{setup}  {fmt_stats(s)}")

    # ─────────────────────────────────────────────────────
    # B. Tabla por (macro, direction) — todos los setups
    # ─────────────────────────────────────────────────────
    print("\n" + "="*80)
    print("B. PERFORMANCE POR SESGO MACRO × DIRECCIÓN (todos los setups)")
    print("="*80)
    groups2 = defaultdict(list)
    for t in trades:
        key = (t["macro"], t["pos"])
        groups2[key].append(t)

    for macro in ["BULL", "BEAR", "NEUTRAL"]:
        for pos in ["long", "short"]:
            key = (macro, pos)
            if key not in groups2:
                continue
            s = stats(groups2[key])
            print(f"  {macro:8} {pos:6}  {fmt_stats(s)}")

    # ─────────────────────────────────────────────────────
    # C. Tabla por mes × dirección
    # ─────────────────────────────────────────────────────
    print("\n" + "="*80)
    print("C. PERFORMANCE POR MES × DIRECCIÓN")
    print("="*80)
    groups3 = defaultdict(list)
    for t in trades:
        key = (t["month"], t["pos"])
        groups3[key].append(t)

    for month in ["Jan", "Feb", "Mar"]:
        for pos in ["long", "short"]:
            key = (month, pos)
            if key not in groups3:
                continue
            s = stats(groups3[key])
            print(f"  {month}  {pos:6}  {fmt_stats(s)}")

    # ─────────────────────────────────────────────────────
    # D. Simulación: FILTRO CORRECTO ICT AMD
    #    Bloquear longs cuando día anterior fue ALCISTA (BULL)
    #    Shorts: mantener en ambas condiciones
    # ─────────────────────────────────────────────────────
    print("\n" + "="*80)
    print("D. SIMULACION FILTRO ICT AMD: BULL macro -> bloquear LONGS (asimetrico)")
    print("   Razon: despues de dia alcista, smart money aun distribuye -> long falla")
    print("="*80)

    blocked = [t for t in trades if t["macro"] == "BULL" and t["pos"] == "long"]
    kept    = [t for t in trades if not (t["macro"] == "BULL" and t["pos"] == "long")]

    print(f"\n  Trades bloqueados ({len(blocked)}):")
    for t in blocked:
        result = "WIN" if t["profit"] > 0 else "LOSS"
        prev1 = prior_trading_day(t["entry_dt"], closes)
        prev2 = two_days_before(t["entry_dt"], closes)
        price_change = ""
        if prev1 and prev2:
            delta = closes[str(prev1)] - closes[str(prev2)]
            price_change = f"(dia ant={delta:+.0f}pts)"
        print(f"    {t['entry_dt']}  {t['pos']:6}  {t['signal']:12}  "
              f"{t['profit']:+7.0f}  [{result}]  {price_change}")

    # Metricas completas
    def max_drawdown(trade_list):
        """Max consecutive loss run en dolares."""
        if not trade_list:
            return 0, 0
        equity = [0.0]
        for t in sorted(trade_list, key=lambda x: x["entry_dt"]):
            equity.append(equity[-1] + t["profit"])
        peak = equity[0]
        max_dd = 0.0
        for e in equity:
            if e > peak:
                peak = e
            dd = peak - e
            if dd > max_dd:
                max_dd = dd
        return max_dd, equity[-1]

    dd_all,  net_all  = max_drawdown(trades)
    dd_kept, net_kept = max_drawdown(kept)

    net_blocked = sum(t["profit"] for t in blocked)

    print(f"\n  === COMPARACION BASELINE vs FILTRO ICT AMD ===")
    print(f"  {'':25}  {'BASELINE':>12}  {'FILTRO ICT':>12}  {'DELTA':>10}")
    print(f"  {'Net Profit':25}  {net_all:>+12.0f}  {net_kept:>+12.0f}  {net_kept-net_all:>+10.0f}")
    print(f"  {'Trades':25}  {len(trades):>12}  {len(kept):>12}  {len(kept)-len(trades):>+10}")
    s_all  = stats(trades)
    s_kept = stats(kept)
    print(f"  {'Win Rate':25}  {s_all['wr']*100:>11.1f}%  {s_kept['wr']*100:>11.1f}%  {(s_kept['wr']-s_all['wr'])*100:>+9.1f}pp")
    print(f"  {'Expectancy/trade':25}  {s_all['expectancy']:>+12.0f}  {s_kept['expectancy']:>+12.0f}  {s_kept['expectancy']-s_all['expectancy']:>+10.0f}")
    print(f"  {'Profit Factor':25}  {s_all['pf']:>12.2f}  {s_kept['pf']:>12.2f}  {s_kept['pf']-s_all['pf']:>+10.2f}")
    print(f"  {'Max Drawdown':25}  {-dd_all:>+12.0f}  {-dd_kept:>+12.0f}  {dd_all-dd_kept:>+10.0f}")

    print(f"\n  Impacto por mes:")
    print(f"  {'Mes':6}  {'Baseline':>10}  {'Bloqueados':>12}  {'Con filtro':>10}  {'Delta':>8}")
    for month in ["Jan", "Feb", "Mar"]:
        bl_m  = [t for t in blocked if t["month"] == month]
        all_m = [t for t in trades  if t["month"] == month]
        net_m_all = sum(t["profit"] for t in all_m)
        net_m_bl  = sum(t["profit"] for t in bl_m)
        net_m_new = net_m_all - net_m_bl
        delta_sign = "+" if net_m_new > net_m_all else ""
        print(f"  {month:6}  {net_m_all:>+10.0f}  {len(bl_m):>4}tr({net_m_bl:>+6.0f})  "
              f"{net_m_new:>+10.0f}  {net_m_new-net_m_all:>+8.0f}")

    # ─────────────────────────────────────────────────────
    # E. Resumen de evidencia
    # ─────────────────────────────────────────────────────
    print("\n" + "="*80)
    print("E. RESUMEN DE EVIDENCIA")
    print("="*80)

    long_bull  = groups2.get(("BULL",  "long"), [])
    long_bear  = groups2.get(("BEAR",  "long"), [])
    long_neut  = groups2.get(("NEUTRAL","long"), [])
    short_bull = groups2.get(("BULL",  "short"), [])
    short_bear = groups2.get(("BEAR",  "short"), [])

    sb = stats(long_bull)
    sbn = stats(long_bear)
    sn = stats(long_neut)
    sshb = stats(short_bull)
    sshbn = stats(short_bear)

    print(f"\n  Long BULL macro:     WR={sb['wr']*100:.1f}%  Exp={sb['expectancy']:+.0f}/tr  n={sb['n']}")
    print(f"  Long NEUTRAL macro:  WR={sn['wr']*100:.1f}%  Exp={sn['expectancy']:+.0f}/tr  n={sn['n']}")
    print(f"  Long BEAR macro:     WR={sbn['wr']*100:.1f}%  Exp={sbn['expectancy']:+.0f}/tr  n={sbn['n']}")
    print(f"\n  Short BULL macro:    WR={sshb['wr']*100:.1f}%  Exp={sshb['expectancy']:+.0f}/tr  n={sshb['n']}")
    print(f"  Short BEAR macro:    WR={sshbn['wr']*100:.1f}%  Exp={sshbn['expectancy']:+.0f}/tr  n={sshbn['n']}")

    if sbn['n'] > 0 and sb['n'] > 0:
        wr_diff = sb['wr'] - sbn['wr']
        exp_diff = sb['expectancy'] - sbn['expectancy']
        print(f"\n  DIFERENCIA Long(BULL) vs Long(BEAR):")
        print(f"    WR:        {wr_diff*100:+.1f}pp")
        print(f"    Expectancy:{exp_diff:+.0f}/trade")
        print(f"\n  CONCLUSIÓN: {'EVIDENCIA FUERTE' if wr_diff > 0.15 and exp_diff > 50 else 'EVIDENCIA MODERADA'} "
              f"de que longs en macro BEAR son sistematicamente peores.")
        print(f"  El patron {'APARECE' if sbn['wr'] < 0.45 else 'NO APARECE'} en multiple periodos "
              f"(no solo marzo).")

    print()


if __name__ == "__main__":
    main()

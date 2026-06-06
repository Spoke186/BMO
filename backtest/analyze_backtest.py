#!/usr/bin/env python3
"""Analizador de backtest para la estrategia ICT NQ (Stream A, proyecto BMO).

Lee el export de "Trades" del Strategy Analyzer de NinjaTrader 8 (CSV/Excel->CSV)
y calcula metricas que NT8 no da listas para evaluar reglas **Apex**:

  - Basicas: win rate, profit factor, expectancy, rachas.
  - Curva de equity + max drawdown ($ y %).
  - Sharpe (anualizado, sobre P&L diario / balance).
  - Out-of-sample: split cronologico IS/OOS -> deteccion de overfitting.
  - Monte Carlo: baraja el orden de trades -> distribucion de P&L final y
    probabilidad de tocar el trailing DD de Apex ("prob de quema").
  - Reglas Apex: trailing DD $2500 (proxy local), daily loss $400,
    profit goal $3000, **consistencia 50%** (ningun dia > 50% del profit total).

Disenado SIN dependencias externas (solo stdlib) para correr igual en las 3 PCs
del equipo sin instalar pandas/numpy.

POR QUE proxy y no la verdad: Apex calcula trailing DD y consistencia en SU
servidor con high-water mark intradia. Aqui usamos solo trades cerrados, asi que
es una red de seguridad / estimacion para tunear (A4) y vigilar (A5), no la regla
oficial. Ver CLAUDE.md y BITACORA.md.

Uso:
    python analyze_backtest.py trades.csv
    python analyze_backtest.py trades.csv --profit-col Profit --exit-col "Exit time"
    python analyze_backtest.py --demo            # datos sinteticos, sin CSV
    python analyze_backtest.py trades.csv --mc-runs 50000 --oos-frac 0.3
"""
from __future__ import annotations

import argparse
import csv
import math
import random
import statistics
import sys
from collections import OrderedDict
from datetime import datetime


# --------------------------------------------------------------------------- #
# Parsing tolerante
# --------------------------------------------------------------------------- #
def parse_money(raw: str) -> float:
    """'$1,234.56' / '(123.45)' / '-123' -> float. Parentesis = negativo."""
    if raw is None:
        raise ValueError("celda vacia")
    s = str(raw).strip()
    if s == "" or s.lower() in {"na", "n/a", "nan"}:
        raise ValueError(f"sin valor numerico: {raw!r}")
    neg = s.startswith("(") and s.endswith(")")
    if neg:
        s = s[1:-1]
    s = s.replace("$", "").replace(" ", "").replace(",", "")
    val = float(s)
    return -val if neg else val


_DT_FORMATS = (
    "%m/%d/%Y %I:%M:%S %p", "%m/%d/%Y %H:%M:%S", "%m/%d/%Y %I:%M %p",
    "%m/%d/%Y %H:%M", "%Y-%m-%d %H:%M:%S", "%Y-%m-%dT%H:%M:%S",
    "%d/%m/%Y %H:%M:%S", "%m/%d/%Y", "%Y-%m-%d",
)


def parse_dt(raw: str):
    """Intenta varios formatos NT8. Devuelve datetime o None (no rompe)."""
    if not raw:
        return None
    s = str(raw).strip()
    for fmt in _DT_FORMATS:
        try:
            return datetime.strptime(s, fmt)
        except ValueError:
            continue
    return None


def classify_setup(name):
    """Nombre de senal de entrada -> 'A' (FVG) | 'B' (Sweep) | None.

    Los nombres los pone la estrategia en EnterLong/Short: Setup A = LongFVG/
    ShortFVG, Setup B = LongSweep/ShortSweep. Sirve para el desglose A13.
    """
    if not name:
        return None
    n = str(name).lower()
    if "fvg" in n:
        return "A"
    if "sweep" in n:
        return "B"
    return None


def _match_col(headers, *, want, avoid=()):
    """Fuzzy match de columna por substring (case-insensitive)."""
    low = [(h, h.lower()) for h in headers]
    # 1) match exacto
    for h, hl in low:
        if hl == want:
            return h
    # 2) contiene 'want' y ninguna palabra de 'avoid'
    for h, hl in low:
        if want in hl and not any(a in hl for a in avoid):
            return h
    return None


def load_trades(path, profit_col=None, exit_col=None, entry_col=None,
                name_col=None):
    """Lee el CSV de trades de NT8. Devuelve lista ordenada por exit_time.

    Cada trade: {'profit', 'exit', 'entry', 'setup'} donde setup = 'A'|'B'|None
    segun el nombre de la senal de entrada (columna "Entry name" del export).
    """
    with open(path, "r", encoding="utf-8-sig", newline="") as f:
        sample = f.read(4096)
        f.seek(0)
        try:
            dialect = csv.Sniffer().sniff(sample, delimiters=",;\t")
        except csv.Error:
            dialect = csv.excel
        reader = csv.DictReader(f, dialect=dialect)
        headers = reader.fieldnames or []
        if not headers:
            raise SystemExit("ERROR: CSV sin encabezados legibles.")

        pcol = profit_col or _match_col(headers, want="profit", avoid=("cum", "%"))
        xcol = exit_col or _match_col(headers, want="exit time") \
            or _match_col(headers, want="exit")
        ecol = entry_col or _match_col(headers, want="entry time") \
            or _match_col(headers, want="entry")
        ncol = name_col or _match_col(headers, want="entry name") \
            or _match_col(headers, want="name", avoid=("exit",))
        if not pcol:
            raise SystemExit(
                f"ERROR: no encuentro columna de profit. Encabezados: {headers}\n"
                f"Pasa --profit-col EXACTO."
            )

        trades = []
        for row in reader:
            try:
                profit = parse_money(row.get(pcol, ""))
            except ValueError:
                continue  # fila de resumen / vacia
            trades.append({
                "profit": profit,
                "exit": parse_dt(row.get(xcol, "")) if xcol else None,
                "entry": parse_dt(row.get(ecol, "")) if ecol else None,
                "setup": classify_setup(row.get(ncol, "")) if ncol else None,
            })

    if not trades:
        raise SystemExit("ERROR: 0 trades parseados. Revisa --profit-col.")

    if all(t["exit"] for t in trades):
        trades.sort(key=lambda t: t["exit"])
    else:
        print("AVISO: no pude parsear todas las fechas de salida; mantengo el "
              "orden del archivo. Agrupacion diaria/consistencia puede degradar.",
              file=sys.stderr)
    return trades, {"profit": pcol, "exit": xcol, "entry": ecol, "name": ncol}


# --------------------------------------------------------------------------- #
# Metricas (funciones puras -> testeables)
# --------------------------------------------------------------------------- #
def basic_stats(profits):
    n = len(profits)
    wins = [p for p in profits if p > 0]
    losses = [p for p in profits if p < 0]
    gross_win = sum(wins)
    gross_loss = sum(losses)  # negativo
    net = sum(profits)
    pf = (gross_win / abs(gross_loss)) if gross_loss != 0 else float("inf")
    return {
        "trades": n,
        "wins": len(wins),
        "losses": len(losses),
        "win_rate": len(wins) / n if n else 0.0,
        "net_profit": net,
        "gross_win": gross_win,
        "gross_loss": gross_loss,
        "profit_factor": pf,
        "avg_win": (gross_win / len(wins)) if wins else 0.0,
        "avg_loss": (gross_loss / len(losses)) if losses else 0.0,
        "expectancy": net / n if n else 0.0,
        "max_consec_wins": _max_streak(profits, lambda p: p > 0),
        "max_consec_losses": _max_streak(profits, lambda p: p < 0),
    }


def _max_streak(profits, pred):
    best = cur = 0
    for p in profits:
        cur = cur + 1 if pred(p) else 0
        best = max(best, cur)
    return best


def equity_and_drawdown(profits, starting_balance):
    """Curva de equity (realizado) + max drawdown desde high-water."""
    equity = starting_balance
    peak = starting_balance
    max_dd_abs = 0.0
    curve = [equity]
    for p in profits:
        equity += p
        peak = max(peak, equity)
        max_dd_abs = max(max_dd_abs, peak - equity)
        curve.append(equity)
    max_dd_pct = (max_dd_abs / peak) if peak else 0.0  # % sobre el pico
    return {
        "final_equity": equity,
        "peak_equity": peak,
        "max_dd_abs": max_dd_abs,
        "max_dd_pct": max_dd_pct,
        "curve": curve,
    }


def daily_pnl(trades):
    """OrderedDict fecha(date) -> P&L del dia (por exit time)."""
    out = OrderedDict()
    for t in trades:
        d = t["exit"].date() if t["exit"] else None
        out[d] = out.get(d, 0.0) + t["profit"]
    return out


def consistency_50(daily):
    """Regla Apex: el mejor dia no puede ser > 50% del profit total.

    Solo significativa si el total es positivo. Devuelve pass + detalle.
    """
    vals = [v for k, v in daily.items() if k is not None]
    total = sum(vals)
    if total <= 0:
        return {"applicable": False, "total": total,
                "best_day": max(vals) if vals else 0.0, "best_pct": None,
                "passes": None}
    best = max(vals)
    pct = best / total
    return {"applicable": True, "total": total, "best_day": best,
            "best_pct": pct, "passes": pct <= 0.50}


def apex_checks(profits, daily, starting_balance, trailing_dd, daily_loss,
                profit_goal):
    dd = equity_and_drawdown(profits, starting_balance)
    worst_day = min((v for k, v in daily.items() if k is not None), default=0.0)
    net = sum(profits)
    return {
        "trailing_dd_breached": dd["max_dd_abs"] >= trailing_dd,
        "max_dd_abs": dd["max_dd_abs"],
        "trailing_dd_limit": trailing_dd,
        "daily_loss_breached": worst_day <= -daily_loss,
        "worst_day": worst_day,
        "daily_loss_limit": daily_loss,
        "profit_goal_reached": net >= profit_goal,
        "net_profit": net,
        "profit_goal": profit_goal,
    }


def sharpe_annualized(daily, starting_balance, periods=252):
    """Sharpe sobre retornos diarios (P&L_dia / balance). RFR=0."""
    rets = [v / starting_balance for k, v in daily.items() if k is not None]
    if len(rets) < 2:
        return None
    mu = statistics.mean(rets)
    sd = statistics.pstdev(rets)
    if sd == 0:
        return None
    return (mu / sd) * math.sqrt(periods)


def out_of_sample(trades, frac):
    """Split cronologico: primeros (1-frac) = IS, ultimos frac = OOS."""
    n = len(trades)
    cut = int(round(n * (1 - frac)))
    cut = max(1, min(cut, n - 1))
    is_p = [t["profit"] for t in trades[:cut]]
    oos_p = [t["profit"] for t in trades[cut:]]
    is_s, oos_s = basic_stats(is_p), basic_stats(oos_p)
    # Heuristica overfitting: OOS degrada fuerte vs IS.
    overfit = (
        oos_s["profit_factor"] < 1.0
        or (is_s["profit_factor"] != float("inf")
            and oos_s["profit_factor"] < 0.6 * is_s["profit_factor"])
    )
    return {"is": is_s, "oos": oos_s, "split_at": cut, "overfit_flag": overfit}


def monte_carlo(profits, runs, starting_balance, trailing_dd, seed=42):
    """Baraja el orden de trades N veces. Distribucion de P&L final y max DD.

    prob_blowup = fraccion de corridas que tocan el trailing DD de Apex.
    POR QUE: el orden real de los trades es UNA muestra; barajar estima el
    riesgo de secuencia (varias perdidas seguidas que queman la cuenta).
    """
    if not profits:
        return None
    # OJO: el P&L final es invariante al orden (suma de un multiset fijo). Por eso
    # NO reportamos percentiles de P&L final: serian todos identicos. Lo util del
    # barajado es la distribucion de MAX DRAWDOWN (riesgo de secuencia).
    rng = random.Random(seed)
    max_dds = []
    blowups = 0
    deck = list(profits)
    for _ in range(runs):
        rng.shuffle(deck)
        eq = starting_balance
        peak = starting_balance
        mdd = 0.0
        for p in deck:
            eq += p
            peak = max(peak, eq)
            mdd = max(mdd, peak - eq)
        max_dds.append(mdd)
        if mdd >= trailing_dd:
            blowups += 1
    max_dds.sort()

    def pct(data, q):
        i = min(len(data) - 1, max(0, int(q * (len(data) - 1))))
        return data[i]

    return {
        "runs": runs,
        "maxdd_p50": pct(max_dds, 0.50), "maxdd_p95": pct(max_dds, 0.95),
        "maxdd_p99": pct(max_dds, 0.99),
        "prob_blowup": blowups / runs,
        "trailing_dd": trailing_dd,
    }


# --------------------------------------------------------------------------- #
# A13 - Desglose por setup (A=FVG vs B=Sweep)
# --------------------------------------------------------------------------- #
_SETUP_LABELS = {"A": "Setup A (FVG)", "B": "Setup B (Sweep)",
                 "?": "Sin etiquetar"}


def split_by_setup(trades):
    """Agrupa trades por setup de entrada. Devuelve dict 'A'/'B'/'?' -> lista."""
    groups = {"A": [], "B": [], "?": []}
    for t in trades:
        groups.setdefault(t.get("setup") or "?", []).append(t)
    return groups


def setup_breakdown(trades):
    """A13: stats por setup. Lista de (label, basic_stats) para A, B,
    sin-etiquetar (si hay) y combinado. Solo incluye grupos no vacios."""
    groups = split_by_setup(trades)
    rows = []
    for key in ("A", "B", "?"):
        g = groups.get(key) or []
        if g:
            rows.append((_SETUP_LABELS[key],
                         basic_stats([t["profit"] for t in g])))
    rows.append(("Combinado (A+B)", basic_stats([t["profit"] for t in trades])))
    return rows


# --------------------------------------------------------------------------- #
# Reporte
# --------------------------------------------------------------------------- #
def _m(v):
    return f"${v:,.2f}"


def report(trades, cols, args):
    profits = [t["profit"] for t in trades]
    daily = daily_pnl(trades)
    b = basic_stats(profits)
    dd = equity_and_drawdown(profits, args.starting_balance)
    cons = consistency_50(daily)
    apex = apex_checks(profits, daily, args.starting_balance,
                       args.trailing_dd, args.daily_loss, args.profit_goal)
    shp = sharpe_annualized(daily, args.starting_balance)
    oos = out_of_sample(trades, args.oos_frac)
    mc = monte_carlo(profits, args.mc_runs, args.starting_balance,
                     args.trailing_dd, args.seed)

    L = []
    L.append("=" * 64)
    L.append("  ANALISIS BACKTEST - ICT NQ (proxy Apex)")
    L.append("=" * 64)
    if cols:
        L.append(f"  columnas: profit={cols['profit']!r} exit={cols['exit']!r}")
    pf = b["profit_factor"]
    pf_s = "inf" if pf == float("inf") else f"{pf:.2f}"
    L.append("")
    L.append("-- Basicas ----------------------------------------------------")
    L.append(f"  Trades............ {b['trades']}")
    L.append(f"  Win rate.......... {b['win_rate']*100:.1f}%  "
             f"({b['wins']}W / {b['losses']}L)")
    L.append(f"  Net profit........ {_m(b['net_profit'])}")
    L.append(f"  Profit factor..... {pf_s}")
    L.append(f"  Expectancy/trade.. {_m(b['expectancy'])}")
    L.append(f"  Avg win / loss.... {_m(b['avg_win'])} / {_m(b['avg_loss'])}")
    L.append(f"  Racha max W / L... {b['max_consec_wins']} / "
             f"{b['max_consec_losses']}")
    L.append("")
    L.append("-- Riesgo / equity --------------------------------------------")
    L.append(f"  Equity final...... {_m(dd['final_equity'])}")
    L.append(f"  Max drawdown...... {_m(dd['max_dd_abs'])} "
             f"({dd['max_dd_pct']*100:.1f}%)")
    L.append(f"  Sharpe (anual).... {shp:.2f}" if shp is not None
             else "  Sharpe (anual).... n/a")
    L.append("")
    L.append("-- Reglas Apex (PROXY local, no oficial) ----------------------")
    L.append(_flag(not apex["trailing_dd_breached"],
                   f"Trailing DD {_m(apex['max_dd_abs'])} vs limite "
                   f"{_m(apex['trailing_dd_limit'])}"))
    L.append(_flag(not apex["daily_loss_breached"],
                   f"Peor dia {_m(apex['worst_day'])} vs limite "
                   f"-{_m(apex['daily_loss_limit'])}"))
    L.append(_flag(apex["profit_goal_reached"],
                   f"Profit goal {_m(apex['net_profit'])} vs "
                   f"{_m(apex['profit_goal'])}"))
    if cons["applicable"]:
        L.append(_flag(cons["passes"],
                       f"Consistencia 50%: mejor dia {_m(cons['best_day'])} = "
                       f"{cons['best_pct']*100:.1f}% del total"))
    else:
        L.append(f"  [n/a] Consistencia 50%: total no positivo "
                 f"({_m(cons['total'])})")
    L.append("")
    L.append("-- Out-of-sample (split cronologico) --------------------------")
    pf_is = oos["is"]["profit_factor"]
    pf_oos = oos["oos"]["profit_factor"]
    L.append(f"  IS  ({oos['split_at']} trades): "
             f"PF {_pf(pf_is)}  net {_m(oos['is']['net_profit'])}  "
             f"WR {oos['is']['win_rate']*100:.0f}%")
    L.append(f"  OOS ({len(trades)-oos['split_at']} trades): "
             f"PF {_pf(pf_oos)}  net {_m(oos['oos']['net_profit'])}  "
             f"WR {oos['oos']['win_rate']*100:.0f}%")
    L.append(_flag(not oos["overfit_flag"],
                   "Overfitting: OOS no degrada fuerte vs IS"
                   if not oos["overfit_flag"]
                   else "Overfitting SOSPECHOSO: OOS degrada vs IS"))
    L.append("")
    if mc:
        L.append("-- Monte Carlo (orden barajado) -------------------------------")
        L.append(f"  Corridas.......... {mc['runs']:,}")
        L.append("  (P&L final no se baraja: la suma es invariante al orden)")
        L.append(f"  Max DD p50/p95/p99: {_m(mc['maxdd_p50'])} / "
                 f"{_m(mc['maxdd_p95'])} / {_m(mc['maxdd_p99'])}")
        L.append(_flag(mc["prob_blowup"] < 0.05,
                       f"Prob. tocar trailing DD ({_m(mc['trailing_dd'])}): "
                       f"{mc['prob_blowup']*100:.1f}%"))

    sb = setup_breakdown(trades)
    if any(lbl.startswith("Setup") for lbl, _ in sb):
        L.append("")
        L.append("-- Desglose por setup (A13: A=FVG vs B=Sweep) -----------------")
        L.append(f"  {'Grupo':<17}{'Trades':>7}{'WR':>6}{'PF':>7}"
                 f"{'Net':>12}{'Exp/trade':>12}")
        for lbl, s in sb:
            L.append(f"  {lbl:<17}{s['trades']:>7}{s['win_rate']*100:>5.0f}%"
                     f"{_pf(s['profit_factor']):>7}{_m(s['net_profit']):>12}"
                     f"{_m(s['expectancy']):>12}")
        L.append("  (mas trades + PF>1 + expectancy>0 = setup que conviene dejar)")
    L.append("=" * 64)
    return "\n".join(L)


def _flag(ok, text):
    return f"  [{'OK ' if ok else 'X  '}] {text}"


def _pf(pf):
    return "inf" if pf == float("inf") else f"{pf:.2f}"


# --------------------------------------------------------------------------- #
# Demo / CLI
# --------------------------------------------------------------------------- #
def demo_trades(seed=7):
    """Trades sinteticos para probar el script sin un CSV real.

    Simula el bracket del operador: gana ~$700, pierde ~$250, win rate ~38%
    (stop ajustado), 1 trade/dia, ~120 dias.
    """
    rng = random.Random(seed)
    base = datetime(2026, 1, 5, 9, 35, 0)
    trades = []
    from datetime import timedelta
    for i in range(120):
        win = rng.random() < 0.38
        profit = rng.gauss(700, 40) if win else -rng.gauss(250, 15)
        ts = base + timedelta(days=i)
        setup = "A" if rng.random() < 0.5 else "B"
        trades.append({"profit": round(profit, 2), "exit": ts, "entry": ts,
                       "setup": setup})
    return trades


def build_parser():
    p = argparse.ArgumentParser(description="Analizador de backtest ICT NQ (Apex).")
    p.add_argument("csv", nargs="?", help="export de trades de NT8 (CSV).")
    p.add_argument("--demo", action="store_true",
                   help="usa trades sinteticos (sin CSV).")
    p.add_argument("--profit-col", help="nombre EXACTO de la columna de profit.")
    p.add_argument("--exit-col", help="nombre EXACTO de la columna de exit time.")
    p.add_argument("--entry-col", help="nombre EXACTO de la columna de entry time.")
    p.add_argument("--name-col",
                   help="nombre EXACTO de la columna del nombre de entrada "
                        "(para el desglose A vs B). Def: autodetecta 'Entry name'.")
    p.add_argument("--starting-balance", type=float, default=50000)
    p.add_argument("--trailing-dd", type=float, default=2500)
    p.add_argument("--daily-loss", type=float, default=400)
    p.add_argument("--profit-goal", type=float, default=3000)
    p.add_argument("--oos-frac", type=float, default=0.30,
                   help="fraccion final para out-of-sample (def 0.30).")
    p.add_argument("--mc-runs", type=int, default=10000)
    p.add_argument("--seed", type=int, default=42)
    return p


def main(argv=None):
    args = build_parser().parse_args(argv)
    if args.demo:
        trades, cols = demo_trades(), None
        print("[DEMO] trades sinteticos (no es un backtest real).\n")
    elif args.csv:
        trades, cols = load_trades(args.csv, args.profit_col, args.exit_col,
                                   args.entry_col, args.name_col)
    else:
        build_parser().print_help()
        return 1
    print(report(trades, cols, args))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

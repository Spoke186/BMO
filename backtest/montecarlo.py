# -*- coding: utf-8 -*-
"""
Monte Carlo de secuencia de trades vs reglas Apex (50K).

Idea central: el P&L final de un backtest es invariante al ORDEN de los trades,
pero el max drawdown NO. Apex quema la cuenta por trailing DD ($2,500), así que
lo que importa no es solo el neto: es la probabilidad de que una mala RACHA
toque el trailing antes de que la cuenta se asiente.

Dos modos de remuestreo:
  - shuffle:   permuta el orden de los trades reales (mismo conjunto exacto).
               Responde: "con ESTOS trades, ¿qué tan peligroso era el orden?"
  - bootstrap: muestrea N trades CON reemplazo de la distribución observada.
               Responde: "si el futuro se parece a la muestra, ¿qué rango de
               resultados y drawdowns espero?" (el neto final SÍ varía aquí)

Modelo Apex 50K (proxy local — Apex calcula en su server con unrealized):
  - Trailing DD $2,500 desde el high-water de equity.
  - El trailing se CONGELA cuando el equity alcanza start + $2,600
    (regla Apex: floor queda fijo en start + $100).
  - Granularidad: cierre de trade. Si el CSV trae MAE se usa para aproximar
    el peor punto intra-trade (más conservador, más cercano a la realidad).

Uso:
    python montecarlo.py LABEL:trades.csv [LABEL2:otro.csv ...] [opciones]
    python montecarlo.py --demo                  # escenarios ACTUAL + HIPOTETICO
    python montecarlo.py --demo --scenario "ACTUAL:1050/375" --runs 20000

Opciones:
    --demo            trades sintéticos calibrados a stats sesión 20 (237 trades)
    --scenario S      demo: "NOMBRE:TP/SL" (repetible; default ACTUAL:1050/375
                      y HIPOTETICO:700/250)
    --runs N          corridas Monte Carlo (default 10000)
    --trailing-dd D   trailing drawdown Apex (default 2500)
    --lock-profit P   profit que congela el trailing (default 2600)
    --n-boot N        trades por corrida bootstrap (default: n de la muestra)
    --seed S          semilla RNG (default 42, reproducible)
    --outdir D        carpeta para PNGs (default backtest/)
    --no-png          sin gráficos

Solo lee CSVs. No modifica nada del stack C# ni de los demás scripts.
"""

import argparse
import csv
import math
import random
import re
import statistics
import sys
from datetime import date

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# ─── parsing (mismo formato NT8 que analyze_intermediate_exits.py) ────────────

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
                "period": label,
                "profit": parse_money(rec.get("profit", "0")),
                "mae":    abs(parse_money(rec.get("mae", "0"))),
                "exit":   classify_exit(rec.get("exit name", "")),
                "date":   parse_date(rec.get("entry time", "")),
            })
        except Exception:
            pass
    return trades

# ─── demo: muestra sintética calibrada a la evidencia sesión 20 ───────────────
#
# 237 trades ene2024–jun2026. Frecuencias derivadas de lo documentado en
# CLAUDE.md PARA EL SISTEMA REAL (TP=$1050, SL=$375):
#   - SESSION_CLOSE: n=112, WR=68.8% (77W/35L), PF=5.13, Net=+$20,559
#     → avg win ≈ +$331.6, avg loss ≈ -$142.2
#   - Sistema Net=+$12,159 → TP_FULL + SL_FULL netean -$8,400 en 125 trades
#     → 1050·t − 375·(125−t) = −8400 → TP n=27, SL n=98
#   - Verificación: 27·1050 − 98·375 + 20559 = +$12,159 ✓
#
# Para escenarios con OTRO TP/SL se reutilizan las MISMAS frecuencias
# (27/98/112) cambiando solo las magnitudes → escenario DESEQUILIBRADO a
# propósito: en la realidad otro TP/SL cambiaría también qué trades terminan
# en TP/SL/SC. Solo un backtest NT8 real responde eso.
# La FORMA de la distribución SC (lognormal truncada) es supuesta, no medida.

def demo_trades(rng: random.Random, tp_usd: float, sl_usd: float) -> list[dict]:
    trades = []

    def lognorm_mean(mean: float, sigma: float) -> float:
        mu = math.log(mean) - sigma * sigma / 2
        return math.exp(rng.gauss(mu, sigma))

    for _ in range(27):
        trades.append({"profit": tp_usd, "mae": rng.uniform(50, min(300, sl_usd * 0.8)),
                       "exit": "TP_FULL", "period": "DEMO"})
    for _ in range(98):
        trades.append({"profit": -sl_usd, "mae": sl_usd,
                       "exit": "SL_FULL", "period": "DEMO"})
    for _ in range(77):  # SC winners
        p = min(lognorm_mean(331.6, 0.7), tp_usd - 1)
        trades.append({"profit": p, "mae": rng.uniform(30, min(300, sl_usd * 0.8)),
                       "exit": "SESSION_CLOSE", "period": "DEMO"})
    for _ in range(35):  # SC losers
        p = -min(lognorm_mean(142.2, 0.6), sl_usd - 1)
        trades.append({"profit": p, "mae": min(abs(p) + rng.uniform(0, 100), sl_usd),
                       "exit": "SESSION_CLOSE", "period": "DEMO"})
    rng.shuffle(trades)
    return trades

# ─── núcleo Monte Carlo ──────────────────────────────────────────────────────

def run_sequence(profits: list[float], maes: list[float],
                 trailing_dd: float, lock_profit: float) -> dict:
    """Recorre una secuencia de trades y devuelve métricas de la corrida."""
    equity = 0.0
    hwm = 0.0
    locked = False          # trailing congelado al alcanzar lock_profit
    floor_offset = -trailing_dd
    max_dd = 0.0
    busted = False
    bust_at = None
    streak = 0
    worst_streak = 0
    curve = [0.0]

    for i, (p, mae) in enumerate(zip(profits, maes)):
        # peor punto intra-trade: equity previa menos el MAE de este trade
        intra_low = equity - mae if mae > 0 else min(equity, equity + p)
        floor = floor_offset if locked else hwm - trailing_dd
        if not busted and intra_low <= floor:
            busted = True
            bust_at = i + 1
        equity += p
        if not busted and equity <= floor:
            busted = True
            bust_at = i + 1
        hwm = max(hwm, equity)
        if not locked and hwm >= lock_profit:
            locked = True
            floor_offset = lock_profit - trailing_dd  # floor fijo = start+$100
        max_dd = max(max_dd, hwm - equity)
        if p < 0:
            streak += 1
            worst_streak = max(worst_streak, streak)
        else:
            streak = 0
        curve.append(equity)

    return {"final": equity, "max_dd": max_dd, "busted": busted,
            "bust_at": bust_at, "worst_streak": worst_streak, "curve": curve}

def monte_carlo(trades: list[dict], runs: int, mode: str, rng: random.Random,
                trailing_dd: float, lock_profit: float,
                n_boot: int | None = None, keep_curves: int = 2000) -> dict:
    profits = [t["profit"] for t in trades]
    maes = [t.get("mae", 0.0) for t in trades]
    n = n_boot or len(trades)
    idx = list(range(len(trades)))

    finals, dds, streaks, bust_ats = [], [], [], []
    curves, curve_busted = [], []
    n_bust = 0
    for r in range(runs):
        if mode == "shuffle":
            order = idx[:]
            rng.shuffle(order)
        else:  # bootstrap con reemplazo
            order = [rng.randrange(len(trades)) for _ in range(n)]
        res = run_sequence([profits[i] for i in order],
                           [maes[i] for i in order],
                           trailing_dd, lock_profit)
        finals.append(res["final"])
        dds.append(res["max_dd"])
        streaks.append(res["worst_streak"])
        if res["busted"]:
            n_bust += 1
            bust_ats.append(res["bust_at"])
        if r < keep_curves:
            curves.append(res["curve"])
            curve_busted.append(res["busted"])

    finals.sort(); dds.sort(); streaks.sort()

    def pct(v, q):
        return v[min(len(v) - 1, int(q * len(v)))]

    # supervivencia: fracción de cuentas vivas después del trade j
    alive = [1.0] * (n + 1)
    if bust_ats:
        from collections import Counter
        c = Counter(bust_ats)
        dead = 0
        for j in range(1, n + 1):
            dead += c.get(j, 0)
            alive[j] = 1 - dead / runs

    return {
        "mode": mode, "runs": runs, "n": n,
        "finals": finals, "dds": dds,
        "curves": curves, "curve_busted": curve_busted,
        "alive": alive,
        "final_p05": pct(finals, 0.05), "final_p50": pct(finals, 0.50),
        "final_p95": pct(finals, 0.95),
        "dd_p50": pct(dds, 0.50), "dd_p95": pct(dds, 0.95),
        "dd_p99": pct(dds, 0.99),
        "prob_bust": n_bust / runs,
        "bust_at_med": statistics.median(bust_ats) if bust_ats else None,
        "streak_p50": pct(streaks, 0.50), "streak_p95": pct(streaks, 0.95),
        "prob_neg": sum(1 for f in finals if f < 0) / runs,
    }

# ─── salida texto ────────────────────────────────────────────────────────────

def ascii_hist(values: list[float], bins: int = 20, width: int = 46,
               fmt=lambda v: f"${v:>8,.0f}") -> list[str]:
    lo, hi = min(values), max(values)
    if hi == lo:
        hi = lo + 1
    counts = [0] * bins
    for v in values:
        counts[min(bins - 1, int((v - lo) / (hi - lo) * bins))] += 1
    peak = max(counts)
    out = []
    for b, c in enumerate(counts):
        left = lo + (hi - lo) * b / bins
        bar = "█" * max(1 if c else 0, round(c / peak * width))
        out.append(f"  {fmt(left)} |{bar:<{width}}| {c}")
    return out

def report(label: str, mc: dict, trailing_dd: float, hist: bool = True) -> None:
    m = lambda v: f"${v:>10,.0f}"
    print(f"\n── Monte Carlo [{mc['mode']}] — {label} "
          f"({mc['runs']:,} corridas, n={mc['n']}) " + "─" * 20)
    if mc["mode"] == "shuffle":
        print(f"  P&L final (invariante al orden): {m(mc['final_p50'])}")
    else:
        print(f"  P&L final  p5/p50/p95: {m(mc['final_p05'])} /"
              f" {m(mc['final_p50'])} / {m(mc['final_p95'])}")
        print(f"  Prob. P&L final < 0 .......... {mc['prob_neg']*100:5.1f}%")
    print(f"  Max DD p50/p95/p99: {m(mc['dd_p50'])} /"
          f" {m(mc['dd_p95'])} / {m(mc['dd_p99'])}")
    print(f"  Racha perdedora p50/p95 ...... {mc['streak_p50']:.0f} / "
          f"{mc['streak_p95']:.0f} trades")
    flag = "[X]" if mc["prob_bust"] > 0.05 else "[ ]"
    print(f"  {flag} Prob. tocar trailing DD ${trailing_dd:,.0f} (Apex): "
          f"{mc['prob_bust']*100:5.1f}%")
    if mc["bust_at_med"] is not None:
        print(f"      Si quema, mediana al trade #{mc['bust_at_med']:.0f}")
    if hist:
        print(f"\n  Distribución de MAX DRAWDOWN ({mc['runs']:,} corridas):")
        for line in ascii_hist(mc["dds"]):
            print(line)

# ─── gráficos ────────────────────────────────────────────────────────────────

NEON = {"cyan": "#00e5ff", "magenta": "#ff2d95", "lime": "#aaff00",
        "amber": "#ffb300", "red": "#ff1744", "grid": "#333344"}

def _style(plt):
    plt.style.use("dark_background")
    plt.rcParams.update({
        "figure.facecolor": "#0d0d14", "axes.facecolor": "#11111a",
        "axes.edgecolor": NEON["grid"], "grid.color": NEON["grid"],
        "grid.alpha": 0.5, "axes.grid": True,
        "font.size": 9.5, "axes.titlesize": 12, "axes.titleweight": "bold",
    })

def _esc(s: str) -> str:
    # matplotlib interpreta $...$ como mathtext; escapar para texto literal
    return s.replace("$", r"\$")

def _bands(curves: list[list[float]]) -> dict:
    n = len(curves[0])
    out = {q: [] for q in (0.05, 0.25, 0.50, 0.75, 0.95)}
    for j in range(n):
        col = sorted(c[j] for c in curves)
        for q in out:
            out[q].append(col[min(len(col) - 1, int(q * len(col)))])
    return out

def fancy_png(label: str, mc: dict, trailing_dd: float, path: str,
              color: str) -> None:
    import matplotlib.pyplot as plt
    _style(plt)
    fig = plt.figure(figsize=(14, 9))
    gs = fig.add_gridspec(2, 2, height_ratios=[1.5, 1], hspace=0.32, wspace=0.22)

    # ── fan chart: todos los futuros posibles ──
    ax = fig.add_subplot(gs[0, :])
    for c, b in zip(mc["curves"][:400], mc["curve_busted"][:400]):
        ax.plot(c, lw=0.6, alpha=0.10 if b else 0.06,
                color=NEON["red"] if b else color, zorder=1)
    bands = _bands(mc["curves"])
    x = range(len(bands[0.50]))
    ax.fill_between(x, bands[0.05], bands[0.95], color=color, alpha=0.13,
                    label="banda 5–95%", zorder=2)
    ax.fill_between(x, bands[0.25], bands[0.75], color=color, alpha=0.25,
                    label="banda 25–75%", zorder=3)
    ax.plot(x, bands[0.50], color="white", lw=2.2, label="mediana", zorder=4)
    ax.axhline(0, color="#888899", lw=0.8, zorder=2)
    ax.axhline(-trailing_dd, color=NEON["red"], lw=1.6, ls="--",
               label=f"trailing DD −${trailing_dd:,.0f} (inicio)", zorder=4)
    ax.set_title(f"{_esc(label)} — {mc['runs']:,} futuros simulados [{mc['mode']}]   "
                 f"(rojo = cuentas que Apex quemaría)")
    ax.set_xlabel("trade #"); ax.set_ylabel("equity ($)")
    ax.legend(loc="upper left", fontsize=9, framealpha=0.25)

    # ── P&L final ──
    ax = fig.add_subplot(gs[1, 0])
    neg = [f for f in mc["finals"] if f < 0]
    pos = [f for f in mc["finals"] if f >= 0]
    bins = 60
    lo, hi = mc["finals"][0], mc["finals"][-1]
    if pos:
        ax.hist(pos, bins=bins, range=(lo, hi), color=NEON["lime"], alpha=0.85)
    if neg:
        ax.hist(neg, bins=bins, range=(lo, hi), color=NEON["red"], alpha=0.9)
    ax.axvline(mc["final_p50"], color="white", lw=1.6,
               label=f"mediana ${mc['final_p50']:,.0f}")
    ax.axvline(0, color="#888899", lw=0.8)
    ax.set_title(f"P&L final — prob. terminar perdiendo {mc['prob_neg']*100:.1f}%")
    ax.set_xlabel("P&L final ($)"); ax.set_ylabel("corridas")
    ax.legend(fontsize=8, framealpha=0.25)

    # ── max drawdown ──
    ax = fig.add_subplot(gs[1, 1])
    safe = [d for d in mc["dds"] if d < trailing_dd]
    danger = [d for d in mc["dds"] if d >= trailing_dd]
    lo, hi = mc["dds"][0], mc["dds"][-1]
    if safe:
        ax.hist(safe, bins=60, range=(lo, hi), color=color, alpha=0.8)
    if danger:
        ax.hist(danger, bins=60, range=(lo, hi), color=NEON["red"], alpha=0.9)
    ax.axvline(trailing_dd, color="white", lw=1.6, ls="--",
               label=f"trailing ${trailing_dd:,.0f}")
    ax.set_title(f"Max drawdown — PROB. QUEMA {mc['prob_bust']*100:.1f}%  "
                 f"(p95 ${mc['dd_p95']:,.0f})")
    ax.set_xlabel("max drawdown ($)"); ax.set_ylabel("corridas")
    ax.legend(fontsize=8, framealpha=0.25)

    fig.suptitle(_esc(label), y=0.995, fontsize=14, fontweight="bold")
    fig.savefig(path, dpi=115, bbox_inches="tight",
                facecolor=fig.get_facecolor())
    plt.close(fig)

def compare_png(results: list[tuple[str, dict, str]], trailing_dd: float,
                path: str) -> None:
    import matplotlib.pyplot as plt
    _style(plt)
    fig, axes = plt.subplots(1, 3, figsize=(16, 5))

    ax = axes[0]
    for label, mc, color in results:
        surv = [a * 100 for a in mc["alive"]]
        ax.plot(surv, color=color, lw=2.4,
                label=f"{_esc(label)} → vivas {surv[-1]:.0f}%")
        ax.fill_between(range(len(surv)), surv, color=color, alpha=0.12)
    ax.set_ylim(0, 102)
    ax.set_title("Supervivencia de la cuenta Apex")
    ax.set_xlabel("trade #"); ax.set_ylabel("% de cuentas vivas")
    ax.legend(fontsize=9, framealpha=0.25)

    ax = axes[1]
    for label, mc, color in results:
        ax.hist(mc["finals"], bins=70, color=color, alpha=0.55, label=_esc(label))
    ax.axvline(0, color="white", lw=1.2, ls="--")
    ax.set_title("P&L final — distribución")
    ax.set_xlabel("P&L final ($)"); ax.set_ylabel("corridas")
    ax.legend(fontsize=9, framealpha=0.25)

    ax = axes[2]
    for label, mc, color in results:
        ax.hist(mc["dds"], bins=70, color=color, alpha=0.55,
                label=f"{_esc(label)} (quema {mc['prob_bust']*100:.0f}%)")
    ax.axvline(trailing_dd, color="white", lw=1.6, ls="--",
               label=f"trailing ${trailing_dd:,.0f}")
    ax.set_title("Max drawdown — distribución")
    ax.set_xlabel("max drawdown ($)"); ax.set_ylabel("corridas")
    ax.legend(fontsize=9, framealpha=0.25)

    fig.suptitle("Comparación de escenarios — Monte Carlo bootstrap "
                 "(demo sintético)", fontsize=13, fontweight="bold")
    fig.tight_layout()
    fig.savefig(path, dpi=115, bbox_inches="tight",
                facecolor=fig.get_facecolor())
    plt.close(fig)

# ─── main ────────────────────────────────────────────────────────────────────

def parse_scenario(s: str) -> tuple[str, float, float]:
    m = re.match(r'([^:]+):(\d+(?:\.\d+)?)/(\d+(?:\.\d+)?)$', s.strip())
    if not m:
        raise argparse.ArgumentTypeError(f"escenario inválido: {s!r} "
                                         "(formato NOMBRE:TP/SL)")
    return m.group(1), float(m.group(2)), float(m.group(3))

def main() -> None:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("csvs", nargs="*", help="LABEL:trades.csv (export NT8, ';')")
    p.add_argument("--demo", action="store_true")
    p.add_argument("--scenario", action="append", type=parse_scenario,
                   help='demo: "NOMBRE:TP/SL", repetible')
    p.add_argument("--runs", type=int, default=10000)
    p.add_argument("--trailing-dd", type=float, default=2500.0)
    p.add_argument("--lock-profit", type=float, default=2600.0)
    p.add_argument("--n-boot", type=int, default=None)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--outdir", default="backtest")
    p.add_argument("--no-png", action="store_true")
    args = p.parse_args()

    have_mpl = False
    if not args.no_png:
        try:
            import matplotlib
            matplotlib.use("Agg")
            have_mpl = True
        except ImportError:
            print("(matplotlib no instalado — solo salida texto)")

    datasets: list[tuple[str, list[dict], str]] = []  # (label, trades, color)
    palette = [NEON["cyan"], NEON["magenta"], NEON["lime"], NEON["amber"]]

    if args.demo:
        scens = args.scenario or [("ACTUAL", 1050.0, 375.0),
                                  ("HIPOTETICO", 700.0, 250.0)]
        print("=" * 72)
        print("MODO DEMO — trades SINTÉTICOS. Frecuencias 27 TP / 98 SL / 112 SC")
        print("calibradas al sistema real ($1050/$375, Net +$12,159, 2024-2026).")
        print("Escenarios con otro TP/SL reusan esas frecuencias → desequilibrado")
        print("a propósito. Veredicto real → CSVs de NT8.")
        print("=" * 72)
        for k, (name, tp, sl) in enumerate(scens):
            rng = random.Random(args.seed)  # misma semilla → comparación justa
            t = demo_trades(rng, tp, sl)
            datasets.append((f"{name} (TP ${tp:,.0f} / SL ${sl:,.0f})",
                             t, palette[k % len(palette)]))
    elif args.csvs:
        trades = []
        for arg in args.csvs:
            lab, path = arg.split(":", 1) if ":" in arg else (arg, arg)
            t = load_trades(path, lab)
            print(f"  {lab}: {len(t)} trades de {path}")
            trades += t
        if not trades:
            sys.exit("No se cargaron trades. ¿Formato NT8 con ';'?")
        datasets.append((f"REAL ({len(trades)} trades)", trades, NEON["cyan"]))
    else:
        p.print_help()
        sys.exit("\nPasa CSVs (LABEL:ruta.csv) o usa --demo.")

    comparables = []
    for label, trades, color in datasets:
        net = sum(t["profit"] for t in trades)
        wins = sum(1 for t in trades if t["profit"] > 0)
        print(f"\n{'='*72}\nESCENARIO: {label}")
        print(f"Muestra: n={len(trades)}, Net={net:+,.0f}, "
              f"WR={wins/len(trades)*100:.1f}%")

        rng = random.Random(args.seed + 1)
        mc_sh = monte_carlo(trades, args.runs, "shuffle", rng,
                            args.trailing_dd, args.lock_profit, args.n_boot)
        report(label, mc_sh, args.trailing_dd, hist=False)
        mc_bs = monte_carlo(trades, args.runs, "bootstrap", rng,
                            args.trailing_dd, args.lock_profit, args.n_boot)
        report(label, mc_bs, args.trailing_dd, hist=not have_mpl)
        comparables.append((label, mc_bs, color))

        if have_mpl:
            safe = re.sub(r'\W+', '_', label.split(" (")[0].lower())
            out = f"{args.outdir}/montecarlo_{safe}.png"
            fancy_png(label, mc_bs, args.trailing_dd, out, color)
            print(f"  Gráfico: {out}")

    if have_mpl and len(comparables) > 1:
        out = f"{args.outdir}/montecarlo_comparacion.png"
        compare_png(comparables, args.trailing_dd, out)
        print(f"\nComparación: {out}")

if __name__ == "__main__":
    main()

# -*- coding: utf-8 -*-
"""
COMPARACIÓN DE VARIANTES A/B/C/D — research TP parcial (Nuevas_intrucciones.md §4 y §6).

Por variante (agregando sus 5 períodos):
  - Net total, WR, PF, MaxDD (curva cronológica), expectancy por POSICIÓN.
  - Distribución de exit types POR CONTRATO (PARTIAL_TP / RUNNER_BE / RUNNER_TP /
    RUNNER_SC / SL_FULL / TP_FULL / SESSION_CLOSE).
  - % de días de trading en verde (consistencia).
  - Para C/D vs baseline A: canibalización (RUNNER_BE que en A eran SESSION_CLOSE
    winners) vs rescate (que en A eran SL_FULL), emparejando por fecha de entrada.
  - Monte Carlo (shuffle + block bootstrap semanal) vía montecarlo.py: prob. de
    quemar trailing $2,500 y prob. de alcanzar goal $3,000.
  - Veredicto contra los criterios PRE-ESCRITOS de la sección 6.

Posición vs contrato: con PartialTP el export NT8 trae 2 filas por trade (parcial
y runner, mismo entry time). WR/PF/expectancy/MC se calculan por POSICIÓN (filas
agrupadas por entry time, profits sumados); la distribución de exits es por fila.
MAE de posición = suma de MAE de las patas (aproximación, leve sesgo conservador).

Uso:
  python compare_variants.py \\
    --variant "A:a_p1.csv,a_p2.csv,a_p3.csv,a_p4.csv,a_p5.csv" \\
    --variant "B:b_p1.csv,..." --variant "C:c_p1.csv,..." --variant "D:d_p1.csv,..." \\
    [--runs 10000] [--trailing-dd 2500] [--goal 3000] [--no-mc]

La PRIMERA variante es el baseline (A). Criterios §6 (escritos antes de ver datos):
  1. prob. quema trailing < baseline A (en AMBOS modos de remuestreo)
  2. Net total >= 75% del baseline A
  3. PF >= 1.15
  4. RUNNER_BE canibalizados (eran SC winners en A) no superan en valor a los
     SL_FULL rescatados
  5. % días verdes > baseline A
"""
from __future__ import annotations
import argparse, csv, re, statistics, sys
from datetime import date, datetime
from collections import defaultdict

sys.path.insert(0, __file__.rsplit("\\", 1)[0] if "\\" in __file__ else ".")
import montecarlo as mc_mod  # parsing idéntico + núcleo Monte Carlo

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

EXIT_TAGS = ["PARTIAL_TP", "RUNNER_BE", "RUNNER_TP", "RUNNER_SC",
             "TP_FULL", "SL_FULL", "SESSION_CLOSE", "OTHER"]

# ─── parsing con entry time COMPLETO (para agrupar patas de la misma posición) ─

def parse_entry_dt(s: str) -> datetime | None:
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

def load_rows(path: str, label: str) -> list[dict]:
    rows = []
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
            rows.append({
                "period":   label,
                "profit":   mc_mod.parse_money(rec.get("profit", "0")),
                "mae":      abs(mc_mod.parse_money(rec.get("mae", "0"))),
                "mfe":      mc_mod.parse_money(rec.get("mfe", "0")),
                "exit":     rec.get("exit name", "").strip(),
                "date":     mc_mod.parse_date(rec.get("entry time", "")),
                "entry_dt": parse_entry_dt(rec.get("entry time", "")),
            })
        except Exception:
            pass
    return rows

def tag_rows(rows: list[dict]) -> None:
    """Tag por contrato: una posición tuvo parcial si alguna de sus filas
    (mismo entry time) salió con la señal PartialTP."""
    partial_keys = {r["entry_dt"] for r in rows
                    if r["entry_dt"] and "partialtp" in r["exit"].lower()}
    for r in rows:
        e = r["exit"].lower()
        had = r["entry_dt"] in partial_keys
        if "partialtp" in e:
            r["tag"] = "PARTIAL_TP"
        elif "stop loss" in e:
            r["tag"] = "RUNNER_BE" if had else "SL_FULL"
        elif "profit target" in e:
            r["tag"] = "RUNNER_TP" if had else "TP_FULL"
        elif "session close" in e or "bar close" in e or "forced" in e or had:
            r["tag"] = "RUNNER_SC" if had else "SESSION_CLOSE"
        else:
            r["tag"] = "OTHER"

def to_positions(rows: list[dict]) -> list[dict]:
    """Agrupa filas por entry time → posición (profits/MAE sumados)."""
    groups = defaultdict(list)
    for i, r in enumerate(rows):
        key = r["entry_dt"] or (r["date"], i)  # sin hora: fila = posición
        groups[key].append(r)
    out = []
    for key, legs in groups.items():
        out.append({
            "date":   legs[0]["date"],
            "profit": sum(l["profit"] for l in legs),
            "mae":    sum(l["mae"] for l in legs),
            "tags":   sorted(l["tag"] for l in legs),
            "had_partial": any(l["tag"] == "PARTIAL_TP" for l in legs),
        })
    out.sort(key=lambda p: p["date"])
    return out

# ─── stats ────────────────────────────────────────────────────────────────────

def variant_stats(positions: list[dict]) -> dict:
    n = len(positions)
    wins = [p["profit"] for p in positions if p["profit"] > 0]
    loss = [p["profit"] for p in positions if p["profit"] <= 0]
    net = sum(p["profit"] for p in positions)
    pf = sum(wins)/abs(sum(loss)) if loss and sum(loss) != 0 else float("inf")
    # MaxDD sobre la curva cronológica real
    eq, hwm, maxdd = 0.0, 0.0, 0.0
    for p in positions:
        eq += p["profit"]
        hwm = max(hwm, eq)
        maxdd = max(maxdd, hwm - eq)
    # % días verdes
    by_day = defaultdict(float)
    for p in positions:
        by_day[p["date"]] += p["profit"]
    green = sum(1 for v in by_day.values() if v > 0)
    return dict(n=n, net=net, wr=len(wins)/n if n else 0, pf=pf,
                maxdd=maxdd, expectancy=net/n if n else 0,
                days=len(by_day), green_days=green,
                green_pct=green/len(by_day) if by_day else 0)

def cannibalization(var_pos: list[dict], base_by_day: dict) -> dict:
    """RUNNER_BE de la variante vs qué era ese día en el baseline A."""
    out = dict(be_from_sc_win=0, be_from_sc_win_usd=0.0,
               be_from_sl=0, be_from_sl_usd=0.0,
               be_from_other=0, partial_days=0)
    for p in var_pos:
        if not p["had_partial"]:
            continue
        out["partial_days"] += 1
        if "RUNNER_BE" not in p["tags"]:
            continue
        base = base_by_day.get(p["date"])
        if base is None:
            out["be_from_other"] += 1
        elif base["tag"] == "SESSION_CLOSE" and base["profit"] > 0:
            out["be_from_sc_win"] += 1
            # valor canibalizado: lo que A ganó ese día menos lo que ganó la variante
            out["be_from_sc_win_usd"] += base["profit"] - p["profit"]
        elif base["tag"] == "SL_FULL":
            out["be_from_sl"] += 1
            # valor rescatado: variante vs el SL completo de A
            out["be_from_sl_usd"] += p["profit"] - base["profit"]
        else:
            out["be_from_other"] += 1
    return out

# ─── MAIN ─────────────────────────────────────────────────────────────────────

def main():
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--variant", action="append", required=True,
                   help='"LABEL:csv1,csv2,..." — la primera es el baseline A')
    p.add_argument("--runs", type=int, default=10000)
    p.add_argument("--trailing-dd", type=float, default=2500.0)
    p.add_argument("--lock-profit", type=float, default=2600.0)
    p.add_argument("--goal", type=float, default=3000.0)
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--no-mc", action="store_true", help="omitir Monte Carlo")
    args = p.parse_args()

    import random
    variants = []  # (label, rows, positions, stats)
    for v in args.variant:
        if ":" not in v:
            sys.exit(f"--variant inválido: {v!r}")
        lab, paths = v.split(":", 1)
        rows = []
        for path in paths.split(","):
            r = load_rows(path.strip(), lab)
            print(f"  [{lab}] {len(r):3d} filas de {path.strip()}")
            rows += r
        if not rows:
            sys.exit(f"Variante {lab}: sin filas.")
        tag_rows(rows)
        pos = to_positions(rows)
        variants.append((lab, rows, pos, variant_stats(pos)))

    base_lab, base_rows, base_pos, base_st = variants[0]
    print(f"\nBaseline: {base_lab} ({base_st['n']} posiciones)")

    # baseline por día (para canibalización) — tag dominante del día
    base_by_day = {}
    for pz in base_pos:
        tag = ("SESSION_CLOSE" if "SESSION_CLOSE" in pz["tags"]
               else "SL_FULL" if "SL_FULL" in pz["tags"]
               else "TP_FULL" if "TP_FULL" in pz["tags"] else pz["tags"][0])
        base_by_day[pz["date"]] = dict(tag=tag, profit=pz["profit"])

    # ─── tabla principal ─────────────────────────────────────────────────────
    print("\n" + "="*100)
    print("MÉTRICAS POR VARIANTE (por posición, 5 períodos agregados)")
    print("="*100)
    print(f"{'Var':4s} {'n':>4} {'Net':>10} {'WR':>7} {'PF':>6} {'MaxDD':>8} "
          f"{'Expect':>8} {'días':>5} {'verdes':>7} {'%verde':>7}")
    print("-"*100)
    for lab, _, _, st in variants:
        pf = f"{st['pf']:6.2f}" if st['pf'] < 99 else "   inf"
        print(f"{lab:4s} {st['n']:4d} {st['net']:+10,.0f} {st['wr']*100:6.1f}% {pf} "
              f"{st['maxdd']:8,.0f} {st['expectancy']:+8,.0f} {st['days']:5d} "
              f"{st['green_days']:7d} {st['green_pct']*100:6.1f}%")

    # ─── distribución de exit types por contrato ─────────────────────────────
    print("\n" + "="*100)
    print("DISTRIBUCIÓN DE EXIT TYPES (por contrato/fila del export)")
    print("="*100)
    print(f"{'Var':4s} " + " ".join(f"{t:>13s}" for t in EXIT_TAGS))
    print("-"*100)
    for lab, rows, _, _ in variants:
        cnt = defaultdict(int)
        for r in rows:
            cnt[r["tag"]] += 1
        print(f"{lab:4s} " + " ".join(f"{cnt.get(t,0):>13d}" for t in EXIT_TAGS))

    # ─── canibalización vs rescate (C/D) ─────────────────────────────────────
    cann = {}
    print("\n" + "="*100)
    print("CANIBALIZACIÓN vs RESCATE — RUNNER_BE de la variante vs día equivalente en baseline")
    print("="*100)
    for lab, _, pos, _ in variants[1:]:
        c = cannibalization(pos, base_by_day)
        cann[lab] = c
        if c["partial_days"] == 0:
            print(f"  {lab}: sin días con parcial (¿PartialTP OFF en esta corrida?)")
            continue
        print(f"\n  {lab}: {c['partial_days']} posiciones con parcial")
        print(f"    RUNNER_BE que en {base_lab} eran SC WINNERS (canibalización): "
              f"n={c['be_from_sc_win']}  valor perdido={c['be_from_sc_win_usd']:+,.0f}")
        print(f"    RUNNER_BE que en {base_lab} eran SL_FULL (rescate):           "
              f"n={c['be_from_sl']}  valor rescatado={c['be_from_sl_usd']:+,.0f}")
        print(f"    RUNNER_BE sin par claro en baseline: n={c['be_from_other']}")

    # ─── Monte Carlo ─────────────────────────────────────────────────────────
    mc_res = {}
    if not args.no_mc:
        print("\n" + "="*100)
        print(f"MONTE CARLO — trailing ${args.trailing_dd:,.0f} | goal ${args.goal:,.0f} "
              f"| {args.runs:,} corridas/modo (shuffle + block semanal)")
        print("="*100)
        print(f"{'Var':4s} {'quema(sh)':>10} {'quema(bk)':>10} {'goal(sh)':>9} "
              f"{'goal(bk)':>9} {'días@goal':>10} {'final p5':>10} {'p50':>9} {'p95':>9}")
        print("-"*100)
        for lab, _, pos, _ in variants:
            trades = [{"profit": pz["profit"], "mae": pz["mae"], "date": pz["date"]}
                      for pz in pos]
            rng = random.Random(args.seed + 1)
            res = {}
            for mode in ("shuffle", "block"):
                res[mode] = mc_mod.monte_carlo(trades, args.runs, mode, rng,
                                               args.trailing_dd, args.lock_profit,
                                               goal=args.goal)
            mc_res[lab] = res
            bk = res["block"]; sh = res["shuffle"]
            dg = f"{bk['goal_days_med']:.0f}" if bk.get("goal_days_med") else "n/a"
            print(f"{lab:4s} {sh['prob_bust']*100:9.1f}% {bk['prob_bust']*100:9.1f}% "
                  f"{sh['prob_goal']*100:8.1f}% {bk['prob_goal']*100:8.1f}% {dg:>10} "
                  f"{bk['final_p05']:+10,.0f} {bk['final_p50']:+9,.0f} {bk['final_p95']:+9,.0f}")

    # ─── veredicto §6 (criterios PRE-ESCRITOS — no modificar) ────────────────
    print("\n" + "="*100)
    print("VEREDICTO — criterios sección 6 de Nuevas_intrucciones.md (pre-escritos)")
    print("="*100)
    for lab, _, _, st in variants[1:]:
        print(f"\n  Variante {lab}:")
        checks = []
        if mc_res:
            b_sh = mc_res[base_lab]["shuffle"]["prob_bust"]
            b_bk = mc_res[base_lab]["block"]["prob_bust"]
            v_sh = mc_res[lab]["shuffle"]["prob_bust"]
            v_bk = mc_res[lab]["block"]["prob_bust"]
            ok1 = v_sh < b_sh and v_bk < b_bk
            checks.append(("1. prob. quema < baseline (ambos modos)", ok1,
                           f"sh {v_sh*100:.1f}% vs {b_sh*100:.1f}% | "
                           f"bk {v_bk*100:.1f}% vs {b_bk*100:.1f}%"))
        else:
            checks.append(("1. prob. quema < baseline (ambos modos)", None, "sin MC (--no-mc)"))
        ok2 = st["net"] >= 0.75 * base_st["net"]
        checks.append(("2. Net >= 75% del baseline", ok2,
                       f"{st['net']:+,.0f} vs umbral {0.75*base_st['net']:+,.0f}"))
        ok3 = st["pf"] >= 1.15
        checks.append(("3. PF >= 1.15", ok3, f"PF={st['pf']:.2f}"))
        c = cann.get(lab)
        if c and c["partial_days"] > 0:
            ok4 = c["be_from_sl_usd"] > c["be_from_sc_win_usd"]
            checks.append(("4. rescate > canibalización (USD)", ok4,
                           f"rescata {c['be_from_sl_usd']:+,.0f} vs "
                           f"mata {c['be_from_sc_win_usd']:+,.0f}"))
        else:
            checks.append(("4. rescate > canibalización (USD)", None,
                           "sin parciales — no aplica (variante sin PartialTP)"))
        ok5 = st["green_pct"] > base_st["green_pct"]
        checks.append(("5. % días verdes > baseline", ok5,
                       f"{st['green_pct']*100:.1f}% vs {base_st['green_pct']*100:.1f}%"))

        applicable = [ok for _, ok, _ in checks if ok is not None]
        for name, ok, detail in checks:
            mark = "PASS" if ok else ("FAIL" if ok is False else "N/A ")
            print(f"    [{mark}] {name}  ({detail})")
        verdict = "APRUEBA para Sim paralela" if all(applicable) and applicable \
                  else "NO pasa — documentar como hipótesis descartada"
        print(f"    → {verdict}")

    print("\nRegla §6: si C y D pasan, gana la de MENOR prob. de quemar el trailing.")
    print("Si ninguna pasa: se documenta en CLAUDE.md (como H2a/H2b/H2c) y NO se simula.")


if __name__ == "__main__":
    main()

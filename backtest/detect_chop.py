#!/usr/bin/env python3
"""
Estudio: ¿Podemos detectar el régimen CHOP ANTES de operar?

Pregunta específica: dado información disponible a las 9:30 AM ET,
¿qué variables predicen mejor que el ATR 20D < 300 (CHOP)?

No queremos mejorar entradas. Queremos evitar operar en CHOP.
"""
from __future__ import annotations
import sys, io, statistics, math
from datetime import date, timedelta
from collections import defaultdict

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

CHOP_THRESHOLD = 300   # ATR 20D < 300 = CHOP
ATR_WINDOW     = 20
SLOPE_WINDOW   = 5     # días para calcular slope del ATR


# ─────────────────────────────────────────────────────────────────────────────
# 1. Descarga datos NQ diarios
# ─────────────────────────────────────────────────────────────────────────────

def download_nq(start="2025-04-01", end="2026-07-01"):
    import yfinance as yf
    d = yf.Ticker("NQ=F").history(start=start, end=end, interval="1d")
    if d.empty:
        raise RuntimeError("yfinance vacío")
    rows = []
    for dt, row in d.iterrows():
        rows.append({
            "date":  dt.date(),
            "open":  row["Open"],
            "high":  row["High"],
            "low":   row["Low"],
            "close": row["Close"],
        })
    return rows


# ─────────────────────────────────────────────────────────────────────────────
# 2. Calcular variables diarias
# ─────────────────────────────────────────────────────────────────────────────

def linear_slope(vals: list[float]) -> float:
    """Pendiente normalizada (por valor medio) de una serie temporal corta."""
    n = len(vals)
    if n < 2:
        return 0.0
    xs = list(range(n))
    mx = sum(xs) / n
    my = sum(vals) / n
    num = sum((x - mx) * (y - my) for x, y in zip(xs, vals))
    den = sum((x - mx) ** 2 for x in xs)
    if den == 0:
        return 0.0
    raw_slope = num / den  # pts/día
    return raw_slope / my if my != 0 else 0.0  # normalizado: cambio relativo/día


def enrich(rows: list[dict]) -> list[dict]:
    """
    Añade a cada día todas las variables predictoras conocidas ANTES de operar.
    """
    closes = [r["close"] for r in rows]
    highs  = [r["high"]  for r in rows]
    lows   = [r["low"]   for r in rows]
    opens  = [r["open"]  for r in rows]

    # True Range
    trs = []
    for i, r in enumerate(rows):
        hl = r["high"] - r["low"]
        if i == 0:
            trs.append(hl)
        else:
            hcp = abs(r["high"] - closes[i-1])
            lcp = abs(r["low"]  - closes[i-1])
            trs.append(max(hl, hcp, lcp))

    result = []
    for i, r in enumerate(rows):
        if i < ATR_WINDOW - 1:
            continue  # no hay suficiente historia

        atr20 = statistics.mean(trs[i - ATR_WINDOW + 1 : i + 1])
        atr10 = statistics.mean(trs[max(0, i - 9) : i + 1])
        atr5  = statistics.mean(trs[max(0, i - 4) : i + 1])

        # Slope del ATR 20D (últimos SLOPE_WINDOW días)
        recent_atrs = []
        for k in range(SLOPE_WINDOW):
            idx = i - k
            if idx >= ATR_WINDOW - 1:
                recent_atrs.insert(0, statistics.mean(trs[idx - ATR_WINDOW + 1 : idx + 1]))
        atr20_slope = linear_slope(recent_atrs) if len(recent_atrs) >= 2 else 0.0

        # Rango del día anterior
        prev_range = highs[i-1] - lows[i-1] if i >= 1 else 0

        # Gap overnight (open de hoy vs close de ayer)
        gap_pts = (opens[i] - closes[i-1]) if i >= 1 else 0
        gap_pct = gap_pts / closes[i-1] * 100 if i >= 1 and closes[i-1] > 0 else 0

        # Dirección del día anterior (bull/bear)
        prev_bull = 1 if (i >= 1 and closes[i-1] > opens[i-1]) else 0

        # Dirección de los últimos 5 días (cierre de hoy vs cierre hace 5)
        week_return = ((closes[i] - closes[i-5]) / closes[i-5] * 100
                       if i >= 5 and closes[i-5] > 0 else 0)

        # ATR 20D hace 5 días vs hoy (tendencia del ATR)
        atr20_5d_ago = (statistics.mean(trs[i-5 - ATR_WINDOW + 1 : i-5 + 1])
                        if i >= ATR_WINDOW + 4 else atr20)
        atr20_delta5 = atr20 - atr20_5d_ago  # positivo = ATR subiendo

        # Distancia al weekly high/low (últimas 5 sesiones)
        w_high = max(highs[max(0, i-4) : i+1])
        w_low  = min(lows[max(0, i-4)  : i+1])
        weekly_range = w_high - w_low

        # Ratio ATR10/ATR20 (>1 = volatilidad acelerando, <1 = desacelerando)
        atr_ratio = atr10 / atr20 if atr20 > 0 else 1.0

        # Overnight range (proxy de volatilidad nocturna — abs(gap))
        overnight_vol = abs(gap_pts)

        # Classify regime (conocido ANTES de abrir porque usa ATR de días anteriores)
        if   atr20 <  300: regime = "CHOP"
        elif atr20 <  380: regime = "WEAK"
        elif atr20 <  450: regime = "ACTIVE"
        else:              regime = "STRONG"

        is_chop = 1 if regime == "CHOP" else 0

        result.append({
            "date":          r["date"],
            "regime":        regime,
            "is_chop":       is_chop,
            # Variables predictoras (todas conocidas antes de operar)
            "atr20":         atr20,
            "atr10":         atr10,
            "atr5":          atr5,
            "atr20_slope":   atr20_slope,     # %/día (neg = cayendo)
            "atr20_delta5":  atr20_delta5,    # pts en 5 días (neg = cayendo)
            "atr_ratio":     atr_ratio,       # atr10/atr20 (<1 = desacelerando)
            "prev_range":    prev_range,      # rango día anterior
            "gap_pts":       gap_pts,
            "gap_pct":       gap_pct,
            "prev_bull":     prev_bull,
            "week_return":   week_return,     # retorno 5 días (%)
            "weekly_range":  weekly_range,    # rango semanal
            "overnight_vol": overnight_vol,   # abs(gap)
        })
    return result


# ─────────────────────────────────────────────────────────────────────────────
# 3. Ranking predictivo de variables
# ─────────────────────────────────────────────────────────────────────────────

def threshold_scan(days: list[dict], var: str, chop_when: str = "low") -> dict:
    """
    Escanea umbrales para 'var' y encuentra el que mejor predice CHOP.
    chop_when='low': CHOP cuando var < umbral (ej. atr20 < 300)
    chop_when='high': CHOP cuando var > umbral (ej. slope muy negativo)
    """
    vals = sorted(set(d[var] for d in days))
    if len(vals) < 3:
        return {}

    best = {"accuracy": 0, "threshold": 0, "tp": 0, "fp": 0, "tn": 0, "fn": 0,
            "precision": 0, "recall": 0, "f1": 0}

    # Escanear percentiles como umbrales candidatos
    n = len(vals)
    candidates = [vals[int(n * p / 100)] for p in range(5, 95, 3)]

    for thr in candidates:
        tp = fp = tn = fn = 0
        for d in days:
            predicted_chop = (d[var] < thr) if chop_when == "low" else (d[var] > thr)
            actual_chop    = d["is_chop"]
            if predicted_chop and actual_chop:     tp += 1
            elif predicted_chop and not actual_chop: fp += 1
            elif not predicted_chop and actual_chop: fn += 1
            else:                                    tn += 1

        total = tp + fp + tn + fn
        accuracy  = (tp + tn) / total if total > 0 else 0
        precision = tp / (tp + fp) if (tp + fp) > 0 else 0
        recall    = tp / (tp + fn) if (tp + fn) > 0 else 0
        f1        = 2 * precision * recall / (precision + recall) if (precision + recall) > 0 else 0

        if f1 > best["f1"]:
            best = {"accuracy": accuracy, "threshold": thr, "tp": tp, "fp": fp,
                    "tn": tn, "fn": fn, "precision": precision, "recall": recall, "f1": f1}
    return best


def mean_by_regime(days: list[dict], var: str) -> dict:
    groups = defaultdict(list)
    for d in days:
        groups[d["regime"]].append(d[var])
    return {k: statistics.mean(v) for k, v in groups.items()}


# ─────────────────────────────────────────────────────────────────────────────
# 4. Main
# ─────────────────────────────────────────────────────────────────────────────

def main():
    print("Descargando datos NQ...")
    rows = download_nq("2025-04-01", "2026-07-01")
    print(f"  {len(rows)} días de trading descargados")

    days = enrich(rows)
    print(f"  {len(days)} días con variables completas")

    chop_days    = [d for d in days if d["is_chop"]]
    nonchop_days = [d for d in days if not d["is_chop"]]
    print(f"  CHOP: {len(chop_days)} días | No-CHOP: {len(nonchop_days)} días")

    # ─────────────────────────────────────────────────────
    # Tabla completa de días con régimen
    # ─────────────────────────────────────────────────────
    print("\n" + "="*115)
    print("TABLA DIARIA: Variables pre-apertura × Régimen ATR")
    print("="*115)
    print(f"  {'Fecha':12}  {'Régimen':8}  {'ATR20':>6}  {'ATR10':>6}  {'ATR5':>5}  "
          f"{'Slope%':>7}  {'Δ5d':>6}  {'Ratio':>5}  {'PrevRng':>7}  {'Gap$':>6}  "
          f"{'PrvBull':>7}  {'Wk%Ret':>6}  {'WkRng':>6}")
    print("-"*115)
    for d in days:
        flag = " ◄CHOP" if d["is_chop"] else ""
        print(f"  {str(d['date']):12}  {d['regime']:8}  "
              f"{d['atr20']:6.0f}  {d['atr10']:6.0f}  {d['atr5']:5.0f}  "
              f"{d['atr20_slope']*100:+7.3f}  {d['atr20_delta5']:+6.0f}  "
              f"{d['atr_ratio']:5.2f}  {d['prev_range']:7.0f}  {d['gap_pts']:+6.0f}  "
              f"{'BULL' if d['prev_bull'] else 'BEAR':>7}  {d['week_return']:+6.2f}  "
              f"{d['weekly_range']:6.0f}{flag}")

    # ─────────────────────────────────────────────────────
    # Media por régimen para cada variable
    # ─────────────────────────────────────────────────────
    print("\n" + "="*115)
    print("MEDIA POR RÉGIMEN — ¿Qué variables separan mejor CHOP de los demás?")
    print("="*115)

    variables = [
        ("atr20",        "ATR 20D",              "low"),
        ("atr10",        "ATR 10D",              "low"),
        ("atr5",         "ATR 5D",               "low"),
        ("atr20_slope",  "Slope ATR20 (%/día)",  "high"),   # más negativo = CHOP
        ("atr20_delta5", "ΔAtr20 5 días (pts)",  "high"),   # más negativo = CHOP
        ("atr_ratio",    "Ratio ATR10/ATR20",    "high"),   # <1 = desacelerando = CHOP
        ("prev_range",   "Rango día anterior",   "low"),
        ("overnight_vol","Volatilidad overnight", "low"),
        ("weekly_range", "Rango semanal 5D",     "low"),
        ("week_return",  "Retorno 5D (%)",       ""),
    ]

    print(f"\n  {'Variable':25}  {'CHOP':>7}  {'WEAK':>7}  {'ACTIVE':>8}  {'STRONG':>8}  {'Diferencia CHOP-STRONG':>22}")
    print("-"*90)
    for var, label, _ in variables:
        means = mean_by_regime(days, var)
        chop   = means.get("CHOP",   0)
        weak   = means.get("WEAK",   0)
        active = means.get("ACTIVE", 0)
        strong = means.get("STRONG", 0)
        diff   = chop - strong
        print(f"  {label:25}  {chop:7.2f}  {weak:7.2f}  {active:8.2f}  {strong:8.2f}  {diff:+.2f}")

    # ─────────────────────────────────────────────────────
    # Ranking estadístico de predictores de CHOP
    # ─────────────────────────────────────────────────────
    print("\n" + "="*115)
    print("RANKING: ¿Cuál variable predice mejor CHOP? (umbral óptimo por F1-score)")
    print("  F1 = media armónica de Precision y Recall. Penaliza FP y FN por igual.")
    print("="*115)

    results = []
    for var, label, direction in variables:
        if direction == "":
            # Probar ambas direcciones
            r_low  = threshold_scan(days, var, "low")
            r_high = threshold_scan(days, var, "high")
            r = r_low if r_low.get("f1", 0) >= r_high.get("f1", 0) else r_high
            direction = "low" if r == r_low else "high"
        else:
            r = threshold_scan(days, var, direction)
        if r:
            results.append((label, var, direction, r))

    results.sort(key=lambda x: -x[3]["f1"])

    print(f"\n  Rank  {'Variable':25}  {'Umbral':>8}  {'Cond':>5}  "
          f"{'Acc':>5}  {'Prec':>5}  {'Recall':>6}  {'F1':>5}  "
          f"{'TP':>3}  {'FP':>3}  {'TN':>3}  {'FN':>3}")
    print("-"*105)
    for i, (label, var, direction, r) in enumerate(results, 1):
        cond = f"< {r['threshold']:.0f}" if direction == "low" else f"> {r['threshold']:.4f}"
        print(f"  #{i:<4}  {label:25}  {r['threshold']:8.3f}  {cond:>5}  "
              f"{r['accuracy']*100:5.1f}%  "
              f"{r['precision']*100:5.1f}%  "
              f"{r['recall']*100:6.1f}%  "
              f"{r['f1']*100:5.1f}  "
              f"{r['tp']:3d}  {r['fp']:3d}  {r['tn']:3d}  {r['fn']:3d}")

    # ─────────────────────────────────────────────────────
    # Combinación: ATR20 + Slope (regla compuesta)
    # ─────────────────────────────────────────────────────
    print("\n" + "="*115)
    print("REGLA COMPUESTA: ¿Mejor usar ATR20 solo, o ATR20 + Slope juntos?")
    print("="*115)

    # Regla 1: ATR20 < umbral
    # Regla 2: ATR20 < umbral + 30 AND slope negativo (ATR cayendo)
    thresholds = [260, 270, 280, 290, 300, 310, 320, 330, 340]
    print(f"\n  {'Regla':45}  {'Acc':>5}  {'Prec':>5}  {'Recall':>6}  {'F1':>5}  {'TP':>3}  {'FP':>3}  {'FN':>3}")
    print("-"*95)

    for thr in [280, 300, 320]:
        # Regla simple
        tp=fp=tn=fn=0
        for d in days:
            pred = d["atr20"] < thr
            if pred and d["is_chop"]:       tp+=1
            elif pred and not d["is_chop"]: fp+=1
            elif not pred and d["is_chop"]: fn+=1
            else:                           tn+=1
        total = tp+fp+tn+fn
        acc = (tp+tn)/total; prec = tp/(tp+fp) if tp+fp else 0
        rec = tp/(tp+fn) if tp+fn else 0; f1 = 2*prec*rec/(prec+rec) if prec+rec else 0
        print(f"  ATR20 < {thr:<3}                                   "
              f"  {acc*100:5.1f}%  {prec*100:5.1f}%  {rec*100:6.1f}%  {f1*100:5.1f}  {tp:3d}  {fp:3d}  {fn:3d}")

        # Regla compuesta: ATR20 < thr+20 AND slope negativo
        thr2 = thr + 20
        tp=fp=tn=fn=0
        for d in days:
            pred = (d["atr20"] < thr2) and (d["atr20_slope"] < 0)
            if pred and d["is_chop"]:       tp+=1
            elif pred and not d["is_chop"]: fp+=1
            elif not pred and d["is_chop"]: fn+=1
            else:                           tn+=1
        total = tp+fp+tn+fn
        acc = (tp+tn)/total; prec = tp/(tp+fp) if tp+fp else 0
        rec = tp/(tp+fn) if tp+fn else 0; f1 = 2*prec*rec/(prec+rec) if prec+rec else 0
        print(f"  ATR20 < {thr2:<3} AND slope < 0                   "
              f"  {acc*100:5.1f}%  {prec*100:5.1f}%  {rec*100:6.1f}%  {f1*100:5.1f}  {tp:3d}  {fp:3d}  {fn:3d}")
        print()

    # ─────────────────────────────────────────────────────
    # Análisis de transiciones CHOP ↔ NO-CHOP
    # ─────────────────────────────────────────────────────
    print("="*115)
    print("TRANSICIONES: ¿Cuántos días de warning hay antes de entrar/salir de CHOP?")
    print("  (¿Cuántos días ANTES de cruzar ATR=300, el slope ya era negativo?)")
    print("="*115)

    transitions_in  = []  # días donde ATR cruza de >300 a <300
    transitions_out = []  # días donde ATR cruza de <300 a >300
    prev_chop = days[0]["is_chop"] if days else 0
    for i, d in enumerate(days[1:], 1):
        curr_chop = d["is_chop"]
        if not prev_chop and curr_chop:
            # Entrando en CHOP — ¿cuántos días atrás el slope se volvió negativo?
            warning = 0
            for k in range(1, min(15, i)):
                if days[i-k]["atr20_slope"] < 0:
                    warning += 1
                else:
                    break
            transitions_in.append({"date": d["date"], "atr20": d["atr20"],
                                    "slope": d["atr20_slope"], "warning_days": warning})
        elif prev_chop and not curr_chop:
            transitions_out.append({"date": d["date"], "atr20": d["atr20"],
                                    "slope": d["atr20_slope"]})
        prev_chop = curr_chop

    print(f"\n  Entradas en CHOP ({len(transitions_in)} transiciones):")
    for t in transitions_in:
        print(f"    {t['date']}  ATR={t['atr20']:.0f}  slope={t['slope']*100:+.3f}%/d  "
              f"días de warning previos: {t['warning_days']}")

    print(f"\n  Salidas de CHOP ({len(transitions_out)} transiciones):")
    for t in transitions_out:
        print(f"    {t['date']}  ATR={t['atr20']:.0f}  slope={t['slope']*100:+.3f}%/d")

    if transitions_in:
        avg_warning = statistics.mean(t["warning_days"] for t in transitions_in)
        print(f"\n  Promedio días de warning antes de entrar en CHOP: {avg_warning:.1f}")

    # ─────────────────────────────────────────────────────
    # Conclusión
    # ─────────────────────────────────────────────────────
    print("\n" + "="*115)
    print("CONCLUSIÓN: ¿Podemos detectar CHOP ANTES de operar?")
    print("="*115)
    print()
    best_var, _, _, best_r = results[0]
    print(f"  Mejor predictor individual: {best_var}")
    print(f"    F1={best_r['f1']*100:.1f}  Acc={best_r['accuracy']*100:.1f}%  "
          f"Prec={best_r['precision']*100:.1f}%  Recall={best_r['recall']*100:.1f}%")
    print(f"    TP={best_r['tp']}  FP={best_r['fp']}  TN={best_r['tn']}  FN={best_r['fn']}")
    print()
    print("  Interpretación:")
    print("    TP = CHOP detectado correctamente (días que evitamos operar y debíamos)")
    print("    FP = Falsa alarma (días que evitamos pero el sistema habría ganado)")
    print("    FN = CHOP no detectado (días que operamos y perdimos)")
    print("    TN = Correcto — no-CHOP identificado como no-CHOP")


if __name__ == "__main__":
    main()

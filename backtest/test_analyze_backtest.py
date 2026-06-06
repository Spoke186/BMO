#!/usr/bin/env python3
"""Tests del analizador. Corre con pytest O como script (sin pytest):

    python test_analyze_backtest.py
"""
from __future__ import annotations

import analyze_backtest as a


def test_parse_money():
    assert a.parse_money("$1,234.56") == 1234.56
    assert a.parse_money("(123.45)") == -123.45
    assert a.parse_money("-250") == -250.0
    assert a.parse_money(" 700.00 ") == 700.0


def test_basic_stats():
    s = a.basic_stats([700, -250, 700, -250, -250])
    assert s["trades"] == 5
    assert s["wins"] == 2 and s["losses"] == 3
    assert abs(s["net_profit"] - 650) < 1e-9
    assert abs(s["profit_factor"] - (1400 / 750)) < 1e-9
    assert s["max_consec_losses"] == 2


def test_profit_factor_no_losses():
    assert a.basic_stats([1, 2, 3])["profit_factor"] == float("inf")


def test_drawdown():
    # +1000 (peak 51000), -2000 (49000) -> DD 2000 desde el pico
    dd = a.equity_and_drawdown([1000, -2000, 500], 50000)
    assert abs(dd["max_dd_abs"] - 2000) < 1e-9
    assert abs(dd["final_equity"] - 49500) < 1e-9


def test_consistency_50():
    from datetime import datetime
    daily = a.OrderedDict()
    daily[datetime(2026, 1, 1).date()] = 600   # 60% del total -> falla
    daily[datetime(2026, 1, 2).date()] = 400
    c = a.consistency_50(daily)
    assert c["applicable"] and not c["passes"]
    assert abs(c["best_pct"] - 0.6) < 1e-9

    daily2 = a.OrderedDict()
    daily2[datetime(2026, 1, 1).date()] = 500   # 50% exacto -> pasa
    daily2[datetime(2026, 1, 2).date()] = 500
    assert a.consistency_50(daily2)["passes"]


def test_consistency_not_applicable_when_unprofitable():
    from datetime import datetime
    daily = a.OrderedDict()
    daily[datetime(2026, 1, 1).date()] = -100
    assert a.consistency_50(daily)["applicable"] is False


def test_monte_carlo_pnl_invariant_and_blowup():
    # Perdidas grandes consecutivas posibles -> alguna corrida toca el DD.
    profits = [500, 500, -800, -800, -800, 500]
    mc = a.monte_carlo(profits, runs=2000, starting_balance=50000,
                       trailing_dd=2000, seed=1)
    assert 0.0 <= mc["prob_blowup"] <= 1.0
    assert mc["maxdd_p95"] >= mc["maxdd_p50"]


def test_out_of_sample_split():
    trades = [{"profit": float(p), "exit": None, "entry": None}
              for p in range(10)]
    oos = a.out_of_sample(trades, 0.3)
    assert oos["split_at"] == 7
    assert oos["is"]["trades"] == 7 and oos["oos"]["trades"] == 3


def _run_all():
    fns = [v for k, v in globals().items()
           if k.startswith("test_") and callable(v)]
    passed = 0
    for fn in fns:
        fn()
        print(f"  ok  {fn.__name__}")
        passed += 1
    print(f"\n{passed}/{len(fns)} tests OK")


if __name__ == "__main__":
    _run_all()

"""
BMO OS Dashboard API — FastAPI + SQLite
Recibe eventos de BMO (C#) y sirve datos al frontend.
Si este servidor cae, BMO sigue operando (calls son fire-and-forget en C#).
"""

from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel
from typing import Optional
import sqlite3, json
from datetime import datetime, timedelta
from pathlib import Path

BASE     = Path(__file__).parent
DB_PATH  = BASE / ".." / ".." / "data" / "bmo.db"
FRONTEND = BASE / ".." / "frontend"

app = FastAPI(title="BMO OS", docs_url=None, redoc_url=None)

# ── Database ─────────────────────────────────────────────────────────────────

def get_conn():
    DB_PATH.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(str(DB_PATH))
    conn.row_factory = sqlite3.Row
    return conn

def init_db():
    with get_conn() as c:
        c.executescript("""
            CREATE TABLE IF NOT EXISTS events (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                ts      TEXT NOT NULL,
                type    TEXT NOT NULL,
                payload TEXT
            );
            CREATE TABLE IF NOT EXISTS trades (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                entry_time   TEXT,
                exit_time    TEXT,
                direction    TEXT,
                setup_type   TEXT,
                sweep_source TEXT,
                entry_price  REAL,
                exit_price   REAL,
                stop_price   REAL,
                tp_price     REAL,
                pnl_usd      REAL,
                pnl_pts      REAL,
                close_reason TEXT,
                duration_min INTEGER,
                is_open      INTEGER DEFAULT 1,
                account      TEXT,
                instrument   TEXT
            );
            CREATE TABLE IF NOT EXISTS status (
                id         INTEGER PRIMARY KEY DEFAULT 1,
                is_live    INTEGER DEFAULT 0,
                account    TEXT,
                instrument TEXT,
                version    TEXT,
                start_time TEXT,
                heartbeat  TEXT,
                updated    TEXT
            );
            INSERT OR IGNORE INTO status (id, is_live) VALUES (1, 0);
        """)
        # Migration: add sweep_source to existing DBs that were created before this column existed
        try:
            c.execute("ALTER TABLE trades ADD COLUMN sweep_source TEXT")
        except Exception:
            pass  # Column already exists

init_db()

# ── Models ────────────────────────────────────────────────────────────────────

class EventReq(BaseModel):
    event_type: str
    data: Optional[dict] = {}

# ── API endpoints (MUST be declared before static mount) ─────────────────────

@app.post("/api/events")
def post_event(ev: EventReq):
    ts = datetime.now().isoformat()
    d  = ev.data or {}
    with get_conn() as c:
        c.execute("INSERT INTO events(ts,type,payload) VALUES(?,?,?)",
                  (ts, ev.event_type, json.dumps(d)))

        if ev.event_type == "bot_start":
            c.execute("""UPDATE status SET is_live=1,account=?,instrument=?,version=?,
                         start_time=?,heartbeat=?,updated=? WHERE id=1""",
                      (d.get("account"), d.get("instrument"), d.get("version"), ts, ts, ts))

        elif ev.event_type == "bot_stop":
            c.execute("UPDATE status SET is_live=0,heartbeat=?,updated=? WHERE id=1", (ts, ts))
            c.execute("""UPDATE trades SET is_open=0, exit_time=?, close_reason='BOT_STOP'
                         WHERE id=(SELECT id FROM trades WHERE is_open=1 ORDER BY id DESC LIMIT 1)""", (ts,))

        elif ev.event_type == "heartbeat":
            c.execute("UPDATE status SET heartbeat=?,updated=?,is_live=1 WHERE id=1", (ts, ts))

        elif ev.event_type == "trade_open":
            c.execute("""INSERT INTO trades
                (entry_time,direction,setup_type,sweep_source,entry_price,stop_price,tp_price,is_open,account,instrument)
                VALUES(?,?,?,?,?,?,?,1,?,?)""",
                (d.get("entry_time", ts), d.get("direction"), d.get("setup_type"),
                 d.get("sweep_source", ""), d.get("entry_price"), d.get("stop_price"),
                 d.get("tp_price"), d.get("account"), d.get("instrument")))

        elif ev.event_type == "trade_close":
            c.execute("""UPDATE trades SET exit_time=?,exit_price=?,pnl_usd=?,pnl_pts=?,
                         close_reason=?,duration_min=?,is_open=0
                         WHERE id=(SELECT id FROM trades WHERE is_open=1 ORDER BY id DESC LIMIT 1)""",
                      (d.get("exit_time", ts), d.get("exit_price"), d.get("pnl_usd"),
                       d.get("pnl_pts"), d.get("close_reason"), d.get("duration_min")))

    return {"ok": True}


@app.get("/api/status")
def api_status():
    with get_conn() as c:
        row = dict(c.execute("SELECT * FROM status WHERE id=1").fetchone())

    now = datetime.now()
    row["uptime_sec"]        = 0
    row["heartbeat_age_sec"] = 999999

    if row.get("start_time") and row.get("is_live"):
        try:
            row["uptime_sec"] = int((now - datetime.fromisoformat(row["start_time"])).total_seconds())
        except Exception:
            pass

    if row.get("heartbeat"):
        try:
            row["heartbeat_age_sec"] = int((now - datetime.fromisoformat(row["heartbeat"])).total_seconds())
        except Exception:
            pass

    return row


def _where(period: str) -> str:
    base = "is_open=0"
    if period == "today":
        return f"{base} AND entry_time >= '{datetime.now().strftime('%Y-%m-%d')}'"
    if period == "week":
        return f"{base} AND entry_time >= '{(datetime.now()-timedelta(days=7)).isoformat()}'"
    if period == "month":
        return f"{base} AND entry_time >= '{(datetime.now()-timedelta(days=30)).isoformat()}'"
    return base


@app.get("/api/trades")
def api_trades(period: str = "all"):
    with get_conn() as c:
        rows = c.execute(
            f"SELECT * FROM trades WHERE {_where(period)} ORDER BY entry_time DESC"
        ).fetchall()
    return [dict(r) for r in rows]


@app.get("/api/trades/open")
def api_open_trade():
    with get_conn() as c:
        row = c.execute("SELECT * FROM trades WHERE is_open=1 ORDER BY id DESC LIMIT 1").fetchone()
    return dict(row) if row else None


@app.get("/api/stats")
def api_stats(period: str = "all"):
    with get_conn() as c:
        rows = c.execute(
            f"SELECT pnl_usd, close_reason FROM trades WHERE {_where(period)}"
        ).fetchall()

    if not rows:
        return dict(total=0, wins=0, losses=0, wr=0, pf=0, net=0,
                    avg_win=0, avg_loss=0, by_reason={})

    pnls   = [r["pnl_usd"] or 0 for r in rows]
    wins   = [p for p in pnls if p > 0]
    losses = [p for p in pnls if p < 0]
    gw     = sum(wins)
    gl     = abs(sum(losses))

    by_reason: dict = {}
    for r in rows:
        k = r["close_reason"] or "UNKNOWN"
        if k not in by_reason:
            by_reason[k] = {"n": 0, "net": 0.0, "wins": 0}
        p = r["pnl_usd"] or 0
        by_reason[k]["n"]   += 1
        by_reason[k]["net"] = round(by_reason[k]["net"] + p, 2)
        if p > 0:
            by_reason[k]["wins"] += 1

    return dict(
        total   = len(pnls),
        wins    = len(wins),
        losses  = len(losses),
        wr      = round(100 * len(wins) / len(pnls), 1) if pnls else 0,
        pf      = round(gw / gl, 2) if gl > 0 else (999 if gw > 0 else 0),
        net     = round(sum(pnls), 2),
        avg_win = round(gw / len(wins), 2) if wins else 0,
        avg_loss= round(gl / len(losses), 2) if losses else 0,
        by_reason = by_reason,
    )


@app.get("/api/equity")
def api_equity():
    with get_conn() as c:
        rows = c.execute(
            "SELECT entry_time, pnl_usd FROM trades WHERE is_open=0 ORDER BY entry_time"
        ).fetchall()
    eq, out = 0.0, []
    for r in rows:
        p   = r["pnl_usd"] or 0
        eq += p
        out.append({"t": r["entry_time"], "eq": round(eq, 2), "pnl": round(p, 2)})
    return out


@app.get("/api/alerts")
def api_alerts(limit: int = 150):
    with get_conn() as c:
        rows = c.execute(
            "SELECT * FROM events WHERE type != 'heartbeat' ORDER BY ts DESC LIMIT ?", (limit,)
        ).fetchall()
    return [dict(r) for r in rows]


# Static files — LAST (must be after all API routes)
app.mount("/", StaticFiles(directory=str(FRONTEND), html=True), name="static")

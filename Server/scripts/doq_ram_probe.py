#!/usr/bin/env python3
"""D-4 — DoQ RAM measurement harness. Run ON THE SERVER before any DoQ listener is written.

The gate for D-3: aioquic's real memory footprint on THIS box decides whether a DoQ listener ships
or is deferred. No DoQ listener code exists until this prints real numbers and a ship/defer result
from THIS machine — hardcoded or estimated figures fail the bar (Protocol §5: no fabricated measured
values).

What it does
------------
Stands up a minimal aioquic echo server (ALPN "doq", the LE cert), holds N idle connections, then
drives active queries, sampling the SERVER process RSS from /proc/<pid>/status:VmRSS and CPU from
/proc/<pid>/stat. Computes per-connection cost, checks linearity, reads `free -m` headroom, and
applies a 50% margin to decide ship vs defer.

Usage (on the box, in a venv with aioquic):
    pip install aioquic
    python3 scripts/doq_ram_probe.py --cert /etc/letsencrypt/live/syxx.app/fullchain.pem \\
        --key /etc/letsencrypt/live/syxx.app/privkey.pem --n 100 500 1000

The decision logic (compute_result) is pure and unit-tested (tests/test_doq_probe.py); the aioquic
server/client only run here, on the box.
"""
import argparse
import os
import sys


# ---- pure decision logic (tested without aioquic) -------------------------------------------------

# Reserve for the co-resident HTTP server, DoT listener, leak collector, nginx, and SQLite WAL. A
# conservative fixed floor in MB; the harness also prints live figures so a human can sanity-check.
DEFAULT_CORESIDENT_RESERVE_MB = 400
SAFETY_MARGIN = 0.5   # ship only if realistic peak is well under the computed max


def per_conn_mb(rss_0_mb, rss_n_mb, n):
    """Marginal RSS per idle connection, in MB. n must be > 0."""
    if n <= 0:
        raise ValueError("n must be positive")
    return (rss_n_mb - rss_0_mb) / n


def is_linear(per_conn_by_n, tolerance=0.5):
    """True if per-connection cost stays roughly constant across N (linear scaling). A super-linear
    curve (per-conn growing with N) means buffer/stream bloat and is itself a DEFER signal. Compares
    the max per-conn against the min; within `tolerance` relative spread = linear."""
    vals = [v for v in per_conn_by_n.values() if v is not None]
    if len(vals) < 2:
        return None  # cannot judge linearity from one point
    lo, hi = min(vals), max(vals)
    if lo <= 0:
        return hi <= 0.05  # near-zero per-conn: treat tiny absolute cost as linear-enough
    return (hi - lo) / lo <= tolerance


def compute_result(rss_0_mb, per_conn_by_n, mem_available_mb, realistic_peak_conns,
                    coresident_reserve_mb=DEFAULT_CORESIDENT_RESERVE_MB):
    """Ship-or-defer, with the numbers that produced it. This is the honesty gate: a bad measurement
    must DEFER, and 'could not measure' must never read as 'ship'."""
    linear = is_linear(per_conn_by_n)
    worst_per_conn = max((v for v in per_conn_by_n.values() if v is not None), default=None)

    if worst_per_conn is None:
        return {"conclusion": "defer", "reason": "no per-connection measurement — cannot judge",
                "max_safe_conns": None, "linear": linear}
    if linear is False:
        return {"conclusion": "defer",
                "reason": "per-connection RSS scales super-linearly (buffer/stream bloat)",
                "max_safe_conns": None, "linear": False}

    budget = mem_available_mb - coresident_reserve_mb
    if budget <= 0 or worst_per_conn <= 0:
        max_safe = 0
    else:
        max_safe = int((budget / worst_per_conn) * SAFETY_MARGIN)

    ship = max_safe > 0 and realistic_peak_conns <= max_safe and linear is not False
    return {
        "conclusion": "ship" if ship else "defer",
        "reason": ("realistic peak %d <= max_safe %d with 50%% margin, linear scaling"
                   % (realistic_peak_conns, max_safe)) if ship
                  else ("realistic peak %d exceeds max_safe %d (or budget too tight)"
                        % (realistic_peak_conns, max_safe)),
        "worst_per_conn_mb": round(worst_per_conn, 3),
        "budget_mb": budget,
        "max_safe_conns": max_safe,
        "linear": linear,
    }


# ---- live measurement (Linux /proc; runs on the box) ---------------------------------------------

def _rss_mb(pid):
    with open("/proc/%d/status" % pid) as f:
        for line in f:
            if line.startswith("VmRSS:"):
                return int(line.split()[1]) / 1024.0   # kB -> MB
    raise RuntimeError("VmRSS not found for pid %d" % pid)


def _mem_available_mb():
    with open("/proc/meminfo") as f:
        for line in f:
            if line.startswith("MemAvailable:"):
                return int(line.split()[1]) / 1024.0
    raise RuntimeError("MemAvailable not found")


def main(argv=None):
    ap = argparse.ArgumentParser(description="D-4 DoQ RAM measurement harness")
    ap.add_argument("--cert", required=True)
    ap.add_argument("--key", required=True)
    ap.add_argument("--n", type=int, nargs="+", default=[100, 500, 1000])
    ap.add_argument("--peak", type=int, default=200,
                    help="realistic expected peak concurrent DoQ connections for the ship/defer gate")
    args = ap.parse_args(argv)

    try:
        import aioquic  # noqa: F401
    except ImportError:
        print("aioquic is not installed. In a venv on the box:\n    pip install aioquic\n"
              "then re-run. This harness will NOT print estimated numbers — it measures or it "
              "refuses.", file=sys.stderr)
        return 2

    # The aioquic echo server + client driver land here when this is run on the box with aioquic
    # present. Left as an explicit TODO rather than a fake: the numbers must come from a real run.
    print("aioquic present. Live echo-server measurement is run on the box; see the plan D-4 "
          "protocol. RSS sampling helpers (_rss_mb / _mem_available_mb) and the ship/defer gate "
          "(compute_result) are ready and tested.", file=sys.stderr)
    print("MemAvailable now: %.0f MB" % _mem_available_mb())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

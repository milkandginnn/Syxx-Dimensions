"""Background scheduler for periodic monitoring.

Daemon thread (pattern from _start_beta_digest_thread in app.py) that loops
every 60 s, runs due monitors (investigate / baseline / warp), persists
results, and emails alerts on changes.
"""

import json
import threading
import time

from . import config, db, investigate, licensing, logging_util, mailer, warp

# A197 Fix 3: sentinel returned by _diff_previous when the stored history is
# unreadable. Distinct from None (no changes) and from a change list.
_UNREADABLE_HISTORY = ["history_unreadable"]


def _diff_previous(monitor_id, check_type, new_raw):
    """Compare *new_raw* against the latest stored result; return change list or None.

    A197 Fix 3: returns _UNREADABLE_HISTORY sentinel (not None) when the stored
    history is corrupt JSON. None means "compared, no changes"; _UNREADABLE_HISTORY
    means "cannot compare — missing data, not clean data". The caller persists
    the run as 'unknown' rather than silently comparing against nothing.
    """
    prev = db.monitor_results_for(monitor_id, check_type, limit=1)
    if not prev:
        return None
    try:
        old = json.loads(prev[0].get("raw_json") or "{}")
        new = json.loads(new_raw)
    except (json.JSONDecodeError, TypeError):
        # A197 Fix 3: missing/corrupt history is NOT the same as "no changes".
        # Return a sentinel so the caller can persist 'unknown' and log the gap.
        logging_util.warn("monitor_history_unreadable", monitor_id=monitor_id, check_type=check_type)
        return _UNREADABLE_HISTORY

    changes = []
    if check_type == "investigate":
        if old.get("consensus") != new.get("consensus"):
            changes.append(f"consensus changed: {old.get('consensus')} -> {new.get('consensus')}")
        if old.get("ipv4Consensus") != new.get("ipv4Consensus"):
            changes.append("ipv4 consensus changed")
        if old.get("ipv6Consensus") != new.get("ipv6Consensus"):
            changes.append("ipv6 consensus changed")
    elif check_type == "warp":
        old_h, new_h = old.get("scriptHashes", {}), new.get("scriptHashes", {})
        if old_h != new_h:
            diff = [u for u in set(old_h) | set(new_h) if old_h.get(u) != new_h.get(u)]
            changes.append(f"{len(diff)} script(s) changed")
        if old.get("inlineScriptHashes") != new.get("inlineScriptHashes"):
            changes.append("inline script(s) changed")
    return changes if changes else None


def _run_one(monitor):
    """Execute all configured checks for a single monitor.  Returns change list."""
    mid, domain = monitor["id"], monitor["domain"]
    checks = json.loads(monitor.get("checks", "[]"))
    all_changes = []

    runners = {
        "investigate": ("investigate", lambda: investigate.investigate(domain)),
        "baseline": ("warp", lambda: (warp.build_baseline(f"https://{domain}"), None)),
        "warp": ("warp", lambda: (warp.build_baseline(f"https://{domain}"), None)),
    }

    for name in checks:
        if name not in runners:
            continue
        check_type, fn = runners[name]
        try:
            result, err = fn()
        except Exception as exc:
            logging_util.error("monitor_check_error", monitor_id=mid, check=name, err=str(exc))
            db.monitor_result_save(mid, check_type, "error", raw_json=json.dumps({"error": str(exc)}))
            continue

        if err:
            db.monitor_result_save(mid, check_type, "error", raw_json=json.dumps({"error": err}))
            continue

        raw = json.dumps(result, default=str)
        # A197 Fix 1: derive the verdict from the check type, never default to
        # "ok". investigate.investigate() always returns a dict with "consensus";
        # warp.build_baseline() returns a plain baseline dict with no consensus
        # key, so the verdict must come from _diff_previous ("changed"/"unchanged").
        if isinstance(result, dict) and "consensus" in result:
            verdict = result["consensus"]
        else:
            # Warp/baseline: verdict is derived from the diff, not fabricated.
            verdict = "unknown"  # will be refined after _diff_previous
        changes = _diff_previous(mid, check_type, raw)
        # A197 Fix 1: for warp/baseline checks, derive verdict from diff result.
        if verdict == "unknown":
            if changes is _UNREADABLE_HISTORY:
                verdict = "unknown"
            elif changes:
                verdict = "changed"
            else:
                verdict = "unchanged"
        # A197 Fix 3: when history is unreadable, persist as "unknown" not clean.
        if changes is _UNREADABLE_HISTORY:
            verdict = "unknown"
        db.monitor_result_save(
            mid,
            check_type,
            verdict,
            changes=json.dumps(changes) if changes and changes is not _UNREADABLE_HISTORY else None,
            raw_json=raw,
        )
        if changes and changes is not _UNREADABLE_HISTORY:
            all_changes.extend(changes)
    return all_changes


def _send_alert(monitor, changes):
    """Email the monitor owner about detected changes (best-effort).

    A197 Fix 4: returns the boolean from mailer.send() so the caller can
    distinguish delivered from undelivered alerts. Previously the return value
    was discarded and monitor_alerts counted attempts as sends.
    """
    email = monitor.get("notify_email")
    if not email or not changes:
        return False
    domain = monitor["domain"]
    lines = "\n".join(f"  - {c}" for c in changes)
    body = (
        f"Syxx Monitor Alert: {domain}\n\n"
        f"Detected {len(changes)} change(s):\n{lines}\n\n"
        f"View details at https://syxx.app\n"
    )
    return mailer.send(email, f"Syxx Alert: changes detected for {domain}", body)


def _tick():
    """Process one batch of due monitors."""
    for monitor in db.monitors_due(int(time.time())):
        try:
            changes = _run_one(monitor)
            if changes:
                delivered = _send_alert(monitor, changes)
                if delivered:
                    logging_util.incr("monitor_alerts")
                else:
                    # A197 Fix 4: count undelivered alerts separately so a broken
                    # SMTP path is visible in metrics rather than silently dropped.
                    logging_util.incr("monitor_alerts_undelivered")
        except Exception as exc:
            logging_util.error("monitor_tick_error", monitor_id=monitor["id"], err=str(exc))
        finally:
            db.monitor_advance(monitor["id"])  # always advance to prevent retry loops


# --- API key sync (daily sweep) -------------------------------------------

_last_api_key_sync = 0.0
_last_db_backup = 0.0


def _backup_db():
    """Daily backup of the SQLite database. G-8: the license DB must survive
    a dead disk. Uses SQLite's online backup API for a consistent snapshot
    without blocking writers."""
    try:
        # A173: db.backup() is the actual function name — backup_database() never existed.
        path = db.backup()
        logging_util.info("db_backup_completed", path=path)
    except (OSError, Exception) as exc:
        logging_util.error("db_backup_failed", err=str(exc))


def _sync_api_keys():
    """Daily sweep: check Keygen license status for all subjects with active API keys.
    Revokes keys for any subject whose license is no longer ACTIVE in Keygen.
    Safety net for the webhook — catches missed webhooks or edge cases."""
    subjects = db.api_key_subjects_with_active_keys()
    if not subjects:
        return
    revoked_count = 0
    for subject in subjects:
        status = licensing.check_keygen_license_status(subject)
        if status is False:
            # License is revoked/suspended/expired — revoke all API keys
            db.api_key_revoke_all_for_subject(subject)
            revoked_count += 1
            logging_util.info("api_key_sync_revoked", subject=subject)
        elif status is None:
            # Transient error — skip this subject, try again next sweep
            logging_util.warn("api_key_sync_transient", subject=subject)
        # status is True → license still active, nothing to do
    if revoked_count:
        logging_util.info("api_key_sync_complete", checked=len(subjects), revoked=revoked_count)


def start():
    """Launch the scheduler daemon thread.  Called once from app.serve()."""
    global _last_api_key_sync, _last_db_backup
    _last_api_key_sync = time.time()  # first sync runs after one full interval
    _last_db_backup = time.time()  # first backup runs after one full interval

    sync_interval_sec = config.API_KEY_SYNC_INTERVAL_HOURS * 3600
    backup_interval_sec = 86400  # daily

    def _loop():
        global _last_api_key_sync, _last_db_backup
        while True:
            time.sleep(60)
            try:
                _tick()
            except Exception as exc:
                logging_util.error("scheduler_loop_error", err=str(exc))
            # Daily API key sync (safety net for Keygen webhook)
            now = time.time()
            if now - _last_api_key_sync >= sync_interval_sec:
                _last_api_key_sync = now
                try:
                    _sync_api_keys()
                except Exception as exc:
                    logging_util.error("api_key_sync_error", err=str(exc))
            # Daily DB backup (G-8: license DB survives dead disk)
            if now - _last_db_backup >= backup_interval_sec:
                _last_db_backup = now
                try:
                    _backup_db()
                except Exception as exc:
                    logging_util.error("db_backup_error", err=str(exc))

    threading.Thread(target=_loop, name="syxx-scheduler", daemon=True).start()
    logging_util.info("scheduler_started", api_key_sync_interval_hours=config.API_KEY_SYNC_INTERVAL_HOURS)

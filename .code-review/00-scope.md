# Code Review — Scope

**Commit:** e01a4c2
**Date:** 2026-08-11
**Effort:** ultra (6 agents, 4 phases)
**Files:** 74 Syxx source files

## File breakdown

| Domain | Files | Net lines |
|---|---|---|
| Server / Python (`syxx_server/`) | 27 | ~+3200 |
| C# Syxx.Kit (`Windows/dotNET/`) | 25 | ~+2100 |
| Swift SyxxKit / SyxxApp (`macOS/Sources/`) | 22 | ~+1200 |

## High-risk areas identified in scope pass

1. **MatrixDeterminativeEngine.cs** — Synthesis scoring refactor (contradictory-indicator scoring, section gating, margin computation)
2. **app.py** — Lockout/backoff reordering, regex promotion, NFC normalisation, domain redaction, OTLP endpoint
3. **leak.py / leak_repo.py** — Token hashing paths (SHA-256), HMAC compare_digest
4. **db.py / kv_repo.py** — backup() finally-cleanup, kv_set created_at preservation
5. **WitnessKeyManager.swift / DNSResolver.swift** — Key handling, force-unwrap removal
6. **Synthesizer.cs** — latency workstream mapping (Unreachable→Degraded), C012 section gating

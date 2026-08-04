---
description: Syxx honesty spine + Cline lane — always-on workspace rule for the DNS-integrity toolkit.
author: Syxx
version: 1.0
---

# Syxx — honesty spine (binding, all Cline modes)

The binding rules are in `AGENTS.md` and `PROJECT-STATE.md` — **read those first;
never edit `AGENTS.md`.** This rule is the short doctrine every Cline mode shares.

## Mission

Syxx (macOS `SyxxKit` / Windows `Syxx.Kit` / Ubuntu `Server/`) **detects DNS
tampering, hijacking, spoofing, and censorship and produces evidence a user can
actually trust.** Six toolsets — Prophet, Mirror, Portal, Glutton, Cage, Witness.
Always **honest-inconclusive over unearned "secure."**

## Cline's lane (own these; nothing here overlaps the engine)

- `Server/**` — Dimension server, collector, GroundTruth production.
- Trust/attestation **consumers**: `WitnessSigner.{swift,cs}`,
  `LeakDetector.{swift,cs}`, `DimensionClient.{swift,cs}`.
- **Probes** (not the engine): `WildcardSynthesisProbe.{swift,cs}`,
  `DNSCanaries.{swift,cs}`, resolver quorum (`Server/syxx_server/resolver.py`,
  `DNSResolver.{swift,cs}`).
- The **labeled-capture corpus** + the Phase-0 golden-vector runner scaffold.

**Never touch** `**/Synthesis/**`, the `Report.*` extractor data shape, or
`REASONING-SYSTEM-DESIGN.md` — Claude's engine lane. Propose shape changes in
output; don't edit `DESIGN.md` yourself unless it's your artifact.

## Honesty spine (every change, every intermediate commit)

1. **No verdict before evidence** — never emit a verdict/confidence/label before
   inputs are queried, parsed, validated.
2. **Missing data is not clean data** — missing/invalid/degraded/timed-out →
   inconclusive/unknown, never a silent clean. Emit typed trust levels, never a bool.
3. **Tri-state, never boolean** — preserve secure/tampered/inconclusive (and
   success/failure/unknown) end to end.
4. **No fabricated value shown as measured** — every user-visible number traces to
   live evidence, not a mock/default.
5. **The bar is a real run with honest output, not green tests** — one real run per
   item, per platform. Never edit, disable, re-flag, soften, or delete a test to go
   green; a failing honesty test is data.
6. **Platform parity** — a `SyxxKit` change needs its `Syxx.Kit` twin; a
   trust-material change needs `Server/` too.

## MCP tools available (wire your lane to them)

Three Syxx MCP servers are installed under `~/Documents/Cline/MCP/`:
- **syxx-dns** — `dns_query`, `dns_doh_query` (multi-vantage DNS evidence for probes).
- **syxx-sqlite** — `sqlite_query`, `syxx_db_stats`, `syxx_db_inspect_{audit,kv,leak_tokens,passive_dns}` (corpus + server DBs).
- **syxx-http** — `http_request`, `syxx_server_health`, `syxx_api_{startup,diff,leak_verify,pubkey}` (Python-server endpoint testing).

Use these to gather real evidence for CL-1/CL-2/CL-3 rather than reasoning from code.

## Reference library

Papers are extracted to text at `research/reference-library/` (`INDEX.md` =
manifest). **Grep it before external web search**; cite the original PDF filename.
Run `/reference-lookup.md` for the workflow.

## Discipline

Always `file:line`. Explicit-path commits only — `git diff --cached --name-only`
before every commit; **never `git add -A`**. Catalog every user-facing string into
`outputs-catalog.csv` (`file:line,trigger,module,task,output,change`).

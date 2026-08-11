# Inherently-Live Command Surfaces

Thirteen command surfaces that are **excluded from deterministic parity coverage by design**, not by
omission. Each entry states why a recorded fixture cannot substitute for a live execution.

This list is the complement of the offline (001–009) and fixture (010–020) behavioral vectors.
When the behavioral vector runner reports coverage, these surfaces are the acknowledged gap.

| # | Command | Sub-action | Why no fixture can cover it |
|---|---|---|---|
| 1 | leak | local-only | Per-run test subdomain dispatched to whatever resolvers the host currently has; the result is a statement about live reachability. |
| 2 | leak | `--collector <url>` | Out-of-band correlation: a fresh label queried on one path, an independent collector attests which resolver saw it on another. Recording either side supplies the fact the test exists to discover. Also reads the stored license key, HTTPS to collector. |
| 3 | witness | `create <domain>` | Seals a live vantage comparison and stamps `Date()` + 30-day expiry into the signed payload with no clock seam on the sign path — signature bytes differ every run even with DNS fixtures. Refuses when no system resolver is detectable. |
| 4 | witness | `verify --fetch-pubkey <url>` | Retrieves the trust anchor over live HTTPS from a caller-supplied URL; that retrieval is the on-path-adversary surface the flag exercises. |
| 5 | key | whole command | Live interface/VPN/captive-portal/encrypted-DNS/gateway state, three of them active probes. There is no recorded artifact of "the network this machine is on right now." |
| 6 | route | trace execution | Hop list is a property of the live path at that instant; two runs a second apart legitimately disagree. |
| 7 | lan | whole command | The ARP cache is volatile kernel state, neither stable across runs nor comparable across machines. |
| 8 | sentinel | default / `--set-home` / `--home` / `--reset` | Captures current configuration and diffs against it; a fixture would compare a baseline against itself. |
| 9 | cage | whole command | Packets on a real interface during a wall-clock window under elevation; the post-capture report generation IS coverable, but only through `forensics --analyze`. |
| 10 | warp | `<url>` (and `--diff`) | Structurally un-fixturable: fresh replay nonce echoed inside the signed manifest rejects any captured manifest by design; plus live licensing and vantage fetches. |
| 11 | speed | whole command | The deliverable is measured latency; pinning it asserts the result. |
| 12 | unbound | all actions | Real resolver process, live DNSSEC controls, host state, elevated config mutation. |
| 13 | filter | whole command | Enforcement and bypass are live properties of the resolver and egress path. |

## Partial exception

`firstrun` is the union of every live discipline above, plus a licensing call on the Swift path.
Only its argument-validation refusals are deterministic (covered by offline vector 009-equivalent
paths). The scan execution itself is inherently live.

## Evidence

Full file:line evidence for each entry is in the track's
[command-triage.md](../../conductor/tracks/cross-platform-parity_20260805/command-triage.md).

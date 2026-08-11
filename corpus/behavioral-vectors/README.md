# Behavioral Vectors

CLI-level behavioral parity vectors for Syxx cross-platform testing.

These vectors define **what the CLI dispatcher should do** when given a specific command invocation — exit code, output phrases, and known cross-platform divergences. They are distinct from the engine-conclusion vectors in `../parity-vectors/` which exercise the synthesis engine directly.

## Format

Every vector is a JSON file with these fields:

| Field | Type | Required | Description |
|---|---|---|---|
| `schemaVersion` | int | yes | `1` |
| `vectorId` | string | yes | Unique identifier, typically `command-subaction` |
| `command` | string | yes | CLI command name (forensics, route, witness, etc.) |
| `args` | string[] | yes | Arguments to pass after the command name |
| `group` | string | yes | `offline` or `fixture` or `live` |
| `timeoutSeconds` | int | yes | Wall-clock budget for the invocation |
| `fixtureRequirements` | object | fixture only | Describes the fixture artifact needed |
| `fixtureRequirements.description` | string | fixture only | Human-readable description of the fixture |
| `fixtureRequirements.artifactPath` | string | fixture only | Path to the fixture artifact (relative to corpus root) |
| `fixtureRequirements.artifactFormat` | string | fixture only | Format tag (dns-response-json, witness-json, cage-ndjson, directory-state, syxx-state-dir) |
| `expectedExitCode` | int | yes | Expected process exit code |
| `expectedOutputPhrases` | string[] | yes | Substrings that must appear in stdout/stderr (empty = no assertion) |
| `expectedOutputAbsent` | string[] | yes | Substrings that must NOT appear in stdout/stderr (empty = no assertion) |
| `knownParityDivergences` | string[] | yes | Documented differences between Swift and C# behavior (empty = no known divergence) |
| `notes` | string | no | Additional context |

## Groups

### Offline (001–009)

No network or filesystem dependencies. The command runs purely against in-memory data (bundled blocklists, hardcoded constants, CLI argument parsing). These are the most deterministic — same binary, same args, same result, always.

### Fixture (010–020)

Requires a pre-built artifact (DNS response JSON, witness JSON, cage NDJSON, seeded directory state). Deterministic given the fixture, but the runner must supply the artifact before invocation. Fixture paths in `args` are relative to a working directory that the runner configures.

### Live (not authored — excluded by design)

Requires a live network or filesystem. Inherently non-deterministic — the runner can only assert structural properties (exit code range, output format), not exact content. These surfaces are **not covered by behavioral vectors** and never will be. See [INHERENTLY-LIVE.md](./INHERENTLY-LIVE.md) for the 13-entry list with per-item reasons.

## Runner contract

A behavioral vector runner (not yet built) must:

1. Load the JSON file.
2. If `group` is `fixture`, locate and prepare the artifact at `fixtureRequirements.artifactPath`.
3. Invoke `syxx <command> <args...>` on the target platform.
4. Assert `expectedExitCode` matches the process exit code.
5. Assert every phrase in `expectedOutputPhrases` appears in the combined stdout+stderr.
6. Assert no phrase in `expectedOutputAbsent` appears in the combined stdout+stderr.
7. Log any `knownParityDivergences` as expected skips rather than failures.

## Relationship to parity-vectors

| | `corpus/parity-vectors/` | `corpus/behavioral-vectors/` |
|---|---|---|
| **Exercises** | Synthesis engine (`Synthesizer.conclude()`) | CLI dispatcher (full `syxx` invocation) |
| **Format** | `PARITY_VECTOR_V1` (sectionInputs) | This format (command + args) |
| **Existing gate** | `parity-local-gate.sh` | Not yet built |
| **Scope** | Engine conclusions only | Full CLI surface (exit codes, output, flags) |

## Index

| # | Vector | Command | Group | Key assertion |
|---|---|---|---|---|
| 001 | forensics-lists | forensics --lists | offline | Exit 0, lists printed |
| 002 | forensics-classify-clean | forensics --classify example.com | offline | Exit 0, not flagged |
| 003 | forensics-classify-flagged | forensics --classify doubleclick.net | offline | Exit 1, flagged by blocklists |
| 004 | forensics-beacon-regular | forensics --beacon 1h 3600 | offline | Exit 0, regular cadence detected |
| 005 | forensics-beacon-insufficient | forensics --beacon 1h 3 | offline | Exit 0, insufficient data |
| 006 | forensics-exfil-short-label | forensics --exfil a.b | offline | Exit 0, short label not flagged |
| 007 | route-unsafe-target | route --check 0.0.0.0 | offline | Exit 1, unsafe target |
| 008 | unbound-unknown-action | unbound --action NOPE | offline | Exit 1, unknown action |
| 009 | witness-usage | witness | offline | Exit 0, usage printed |
| 010 | nslook-basic-a | nslook example.test @8.8.8.8 | fixture | Exit 0, domain in output |
| 011 | nslook-dnssec | nslook example.test --dnssec @8.8.8.8 | fixture | Exit 0, DNSSEC in output |
| 012 | mirror-authoritative-match | mirror example.test --local | fixture | Exit 0, domain in output |
| 013 | forensics-analyze-clean | forensics --analyze fixture | fixture | Exit 0 |
| 014 | forensics-analyze-empty | forensics --analyze empty fixture | fixture | Exit 2 (inconclusive) |
| 015 | witness-gen-keypair-new | witness --gen-keypair (new path) | fixture | Exit 0 |
| 016 | witness-gen-keypair-exists | witness --gen-keypair (existing path) | fixture | Exit 3 (refusal) |
| 017 | witness-verify-valid | witness verify valid fixture | fixture | Exit 0 (clock-seam gap on C#) |
| 018 | witness-verify-invalid-sig | witness verify tampered fixture | fixture | Exit 1 |
| 019 | witness-report-valid | witness report valid fixture | fixture | Exit 0 (trust-anchor divergence) |
| 020 | sentinel-forget | sentinel --forget | fixture | Exit 0 |

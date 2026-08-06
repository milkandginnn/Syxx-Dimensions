# Command triage — Task 4.1 (and the Task 4.4 draft)

**Produced by the cmd-triage seat, 2026-08-05.** Read-only reconnaissance; every claim carries
file:line evidence. The lead persisted this verbatim; the sixteen cross-platform differences at the
bottom are finding candidates for Phase 7 filing, not yet individually filed.

**Headline:** the deterministic surface is much smaller than the command count suggests. Only
`forensics` is genuinely offline today. Nine of fifteen commands are inherently-live in every
sub-action. The single most consequential finding for the fixture work is a missing clock seam on
the C# witness verifier — Swift's `verifySnapshotDetailed` takes an injectable `now`, C#'s does
not, so a fixture-driven `witness verify` can be made hermetic on macOS but not on Windows without
a signature change.

Groups: **offline** = deterministic today with no fixture; **fixture** = deterministic if a named
recorded artifact existed (the artifact is specified per row); **live** = cannot be made
deterministic, with the reason (these rows are the Task 4.4 list).

Coverage check: all 15 commands, every sub-action in exactly one group. Offline = 9 sub-actions
(forensics ×4 plus the route/unbound/firstrun/witness refusal-and-usage paths). Fixture = 7
(nslook, mirror, forensics --analyze, witness --gen-keypair/verify/report, sentinel --forget).
Live = 21. Commands with no offline or fixture-able sub-action at all: key, lan, cage, warp,
speed, filter — plus leak, unbound, firstrun, whose only non-live paths are usage/validation stubs.

Path abbreviations: **S** = `macOS/Sources/SyxxCLI/Commands/` (except firstrun =
`macOS/Sources/SyxxCLI/Synthesis/`), **W** = `Windows/dotNET/Syxx/Commands/`,
**SK** = `macOS/Sources/SyxxKit/`, **WK** = `Windows/dotNET/Syxx.Kit/`.

| Command | Sub-action | Group | Reason | Evidence |
|---|---|---|---|---|
| nslook | whole command | fixture | Resolver is explicit (`@server`, default 8.8.8.8), so the only input is the DNS wire response. Fixture: raw DNS response bytes keyed by (qname, qtype, server) for UDP; `application/dns-message` body keyed by URL for DoH; DNSKEY/DS/RRSIG responses for `--dnssec`. TTLs come off the wire, so the fixture pins them. | S: SyxxNSLook.swift:6,71,75,88 · W: NslookCommand.cs:12,103,109,127 |
| mirror | whole command | fixture | Comparison logic is pure over query outcomes. Fixture: A-record responses from the local resolver plus every `VantageComparer.publicResolvers` entry, and a stubbed system-resolver list — on Swift the system read happens before `--local` is parsed, so `--local` alone cannot make it hermetic. | S: SyxxMirror.swift:8,27,35,51 · W: MirrorCommand.cs:13,32,58 |
| leak | local-only | live | Per-run test subdomain dispatched to whatever resolvers the host currently has; the result is a statement about live reachability. | S: SyxxLeak.swift:7,14,145,148 · W: LeakCommand.cs:13,25,162,166 |
| leak | `--collector <url>` | live | Out-of-band correlation: a fresh label queried on one path, an independent collector attests which resolver saw it on another. Recording either side pre-supposes the answer. Also reads the stored license key, HTTPS to collector. | S: SyxxLeak.swift:60-82,64 · W: LeakCommand.cs:91-121,97 |
| witness | `--gen-keypair` | fixture | Guard/permission/exit-code behavior drivable from a temp dir seeded with/without a key file. Caveat: printed public key is fresh CSPRNG output — assert paths/modes/refusals/exit codes, never key bytes. | S: SyxxWitness.swift:51,72,78,98-100 · W: WitnessCommand.cs:85,124,144,157-171 |
| witness | `create <domain>` | live | Seals a live vantage comparison; stamps `Date()` + 30-day expiry into the signed payload with no clock seam on the sign path — signature bytes differ every run even with DNS fixtures. Refuses when no system resolver is detectable. | S: SyxxWitness.swift:117,187-190,193 + SK/WitnessSigner.swift:182,189 · W: WitnessCommand.cs:213,309-317,322 |
| witness | `verify <file>` | fixture | Ed25519 verification + display-integrity rebuild are pure over the file. Fixture: pinned witness.json cases (valid / invalid-signature / display-mismatch / unsupported-format / malformed), trust-anchor env var, and a pinned `now` — Swift has the `now:` seam; C# does not (difference 1 below). | S: SyxxWitness.swift:212,302-346 + SK/WitnessSigner.swift:246-251,350-360 · W: WitnessCommand.cs:345,478-546 + WK/WitnessSigner.cs:232-233 |
| witness | `verify --fetch-pubkey <url>` | live | Fetches the trust anchor over HTTPS from a caller-supplied URL; the anchor is live third-party state. | S: SyxxWitness.swift:253-282 · W: WitnessCommand.cs:407-458 |
| witness | `report <file>` | fixture | Verify-then-render over files only. Same pinned-`now` requirement. Fixture additionally needs a Conclusion JSON for the `--conclusion` path and each non-valid refusal banner. | S: SyxxWitness.swift:387,423-430,440 · W: WitnessCommand.cs:558,611-620,635 |
| witness | usage / unknown | offline | Pure arg dispatch to printUsage, exit 3. | S: SyxxWitness.swift:112-115,449-452 · W: WitnessCommand.cs:202-209,647-651 |
| key | whole command | live | Live interface/VPN enumeration plus active captive-portal, encrypted-DNS and gateway probes against the current network. | S: SyxxKey.swift:6,12,45-58 · W: KeyCommand.cs:11,17,56-67 |
| route | trace execution | live | Spawns platform traceroute; the hop list is a property of the path at that instant. | S: SyxxRoute.swift:6,25 · W: RouteCommand.cs:14,33 |
| route | unsafe-target refusal | offline | `isSafeTraceTarget` is a pure validator; refusal returns before any spawn. | S: SyxxRoute.swift:19-22,36-42 · W: RouteCommand.cs:25-29,61-68 |
| lan | whole command | live | Kernel ARP cache — volatile per-host state reflecting only recent peers; cross-referenced against current resolvers. | S: SyxxLan.swift:6,14,21,83 · W: LanCommand.cs:20,38,51 |
| forensics | `--lists` | offline | Enumerates blocklists hardcoded/bundled in the binary. | S: SyxxForensics.swift:32-38 + SK/Forensics/SieveBlocklistLoader.swift:13-25 · W: ForensicsCommand.cs:48-53 |
| forensics | `--classify <domain...>` | offline | Pure set membership of supplied domains against bundled lists. | S: SyxxForensics.swift:39-58 · W: ForensicsCommand.cs:55-68 |
| forensics | `--beacon t1,t2,...` | offline | Pure statistics over supplied epoch timestamps. | S: SyxxForensics.swift:59-78 + SK/Forensics/BeaconAnalyzer.swift:35-47 · W: ForensicsCommand.cs:70-78 |
| forensics | `--exfil <domain> [--times]` | offline | Entropy + label length over the supplied domain; cadence over supplied timestamps; blocklist from bundle. | S: SyxxForensics.swift:79-103 + SK/Forensics/ExfilAnalyzer.swift:77-91 · W: ForensicsCommand.cs:80-95 |
| forensics | `--analyze <logfile>` | fixture | Pure analysis of a supplied file, but no such file is committed. Fixture: a cage-format NDJSON query log (one DNSCaptureRecord per line) + an endpoint activity log sample + a zero-record file to pin the "no records parsed" inconclusive path. | S: SyxxForensics.swift:104-133 · W: ForensicsCommand.cs:97-119 |
| sentinel | `--forget` | fixture | Deletes the three `~/.syxx` state files; no network. Fixture: a seeded `~/.syxx`; assert all three gone. | S: SyxxSentinel.swift:13-19 · W: SentinelCommand.cs:32-43 |
| sentinel | default / `--set-home` / `--home` / `--reset` | live | Every path captures the host's current DNS-relevant configuration and stores or diffs it. | S: SyxxSentinel.swift:20-75,100,126 · W: SentinelCommand.cs:45-92,108,123 |
| cage | whole command | live | Observes packets on a real interface over a wall-clock window under root/Administrator; output named from current time; Swift also spawns /sbin/route. | S: SyxxCage.swift:59,129-134,202-220,446 · W: CageCommand.cs:27,83-87,114,192 |
| warp | `<url>` (and `--diff`) | live | Live license gate; a fresh per-run replay nonce must be echoed inside the server-signed manifest, so a recorded manifest is rejected by construction; `--diff` adds a live clean-copy fetch. | S: SyxxWarp.swift:7,21,33-37,72-89 + SK/LicenseManager.swift:306-331 · W: WarpCommand.cs:14,33,47-48,88-112 |
| speed | whole command | live | The product is measured latency and its ranking; pinning them asserts the answer instead of measuring it. | S: SyxxSpeed.swift:7,18,52-56 · W: SpeedCommand.cs:11,20,53 |
| unbound | `verify` / `verify-dnscrypt` / `status` / `enable` / `disable` | live | Real Unbound process + live DNSSEC control pair; dnscrypt chain end-to-end (Swift only); host binary presence + current DNS; resolver-config mutation under elevation. | S: SyxxUnbound.swift:24,39-73,75-91,93-122 + SK/UnboundManager.swift:548,557-577 · W: UnboundCommand.cs:21-72 |
| unbound | unknown action | offline | Pure dispatch default printing the accepted action list. | S: SyxxUnbound.swift:31-33 · W: UnboundCommand.cs:27 |
| filter | whole command | live | Enforcement is a live property of the resolver in front of the host; bypass canary needs real egress (UDP:53 to 8.8.8.8; reachable public DoH). | S: SyxxFilter.swift:11,18,28,65 · W: FilterCommand.cs:13,19,27,59 |
| firstrun | scan execution | live | The union of every live discipline above, plus a licensing call on the Swift path. | S: Synthesis/FirstRunScan.swift:29,82-86 · W: FirstRunCommand.cs:13,75 |
| firstrun | argument-validation refusals | offline | Reached before any scan work. | S: Synthesis/FirstRunScan.swift:39-42,57-60 · W: FirstRunCommand.cs:32-38,43-48 |

## Task 4.4 draft — inherently-live, explicitly uncovered

Excluded from deterministic parity coverage by design, not by omission; each entry states why a
recorded fixture cannot substitute.

1. **leak** (both modes) — out-of-band correlation across two network paths; recording either side supplies the fact the test exists to discover. (S: SyxxLeak.swift:145-148,76-82 · W: LeakCommand.cs:162-166,115-121)
2. **witness create** — seals a live comparison and stamps wall-clock time into the signed payload; no clock seam on the sign path. (SK/WitnessSigner.swift:182,189)
3. **witness verify --fetch-pubkey** — retrieves the trust anchor over live HTTPS; that retrieval is the on-path-adversary surface the flag exercises. (S: SyxxWitness.swift:253-282 · W: WitnessCommand.cs:407-458)
4. **key** — live interface/VPN/captive-portal/encrypted-DNS/gateway state, three of them active probes. There is no recorded artifact of "the network this machine is on right now."
5. **route** — hop list is a property of the live path at that instant; two runs a second apart legitimately disagree.
6. **lan** — the ARP cache is volatile kernel state, neither stable across runs nor comparable across machines.
7. **sentinel** (default/--set-home/--home/--reset) — captures current configuration and diffs against it; a fixture would compare a baseline against itself.
8. **cage** — packets on a real interface during a wall-clock window under elevation; the post-capture report generation IS coverable, but only through `forensics --analyze`.
9. **warp** — structurally un-fixturable: fresh replay nonce echoed inside the signed manifest rejects any captured manifest by design; plus live licensing and vantage fetches. (S: SyxxWarp.swift:33-37)
10. **speed** — the deliverable is measured latency; pinning it asserts the result.
11. **unbound** (all actions) — real resolver process, live DNSSEC controls, host state, elevated config mutation.
12. **filter** — enforcement and bypass are live properties of the resolver and egress path.
13. **firstrun** — the union of every live discipline; only its argument-validation refusals are deterministic.

## Cross-platform effect-surface differences (finding candidates, ordered by consequence)

1. **witness verify has no clock seam on C#.** Swift `verifySnapshotDetailed(…, now: Date = Date())` (SK/WitnessSigner.swift:246-251); C# `VerifySnapshotDetailed(snapshot, maxAge, trustedPublicKeys)` with no `now` (WK/WitnessSigner.cs:232-233). Expired/stale/future-dated read the wall clock on Windows with no injection point. Blocks hermetic fixtures on one platform; a code change, not a test change.
2. **unbound verify-dnscrypt exists only on macOS.** Swift dispatches (SyxxUnbound.swift:24) and implements (:39-73); the C# switch has no case (UnboundCommand.cs:21-28), usage omits it (:18).
3. **witness trust-anchor sources differ.** Swift: CollectorPinning.configuredTrustedSigningKeys() (SyxxWitness.swift:304,344,422); C#: WitnessPinning.ConfiguredTrustedWitnessKeys() (WitnessCommand.cs:735). Documented on the Swift side (:501-507) — the same fixture can verify `valid` on one platform and `untrustedSigner` on the other.
4. **mirror with no detectable system resolver:** Swift refuses, inconclusive (SyxxMirror.swift:28-32); C# silently defaults to 127.0.0.1 and queries it (MirrorCommand.cs:29-36). Swift's system read also precedes `--local` parsing (:27 vs :35).
5. **Exit codes largely absent on C#.** Only mirror/leak/warp/witness/firstrun set Environment.ExitCode; ten commands inherit the seeded 2 (Program.cs:25). Inversions: `key` with a detected captive portal exits 1 on macOS (SyxxKey.swift:60-62) vs 2 on Windows; `forensics --classify` on a flagged domain 1 vs 2 (SyxxForensics.swift:57).
6. **cage capture mechanisms differ** (libpcap+PKTAP vs raw sockets), with flag divergence: Swift-only `--interface/--owner-uid/--owner-gid` (SyxxCage.swift:84-108); `--output` names a file on Swift (:89-94) but a directory on C# (CageCommand.cs:71-76, documented at :68-70).
7. **cage raw-capture deletion guard is Swift-only.** Swift retains the raw capture when the queries-JSON export failed (SyxxCage.swift:402-407); C# deletes regardless (CageCommand.cs:292-319). Evidence-loss divergence.
8. **cage transport-coverage disclosure is Swift-only** (SyxxCage.swift:336-338); an empty Windows capture reads as "no DNS activity occurred."
9. **cage capture counters reach the report only on Swift** (SyxxCage.swift:343-358 vs CageCommand.cs:245-251).
10. **sentinel Swift-only behaviors:** corrupt-baseline quarantine (SyxxSentinel.swift:89-94 vs SentinelCommand.cs:95-100), append-only JSONL change log (:121,139-159, no C# equivalent), tri-state dnsEncrypted (:27 vs :51 boolean), persistence failures surfaced (:36,72,85) vs every Save result ignored (:56,89,103,123).
11. **lan:** Swift persists the snapshot (SyxxLan.swift:83), prints the ARP-unauthenticated caveat (:65-66), hedges absent resolvers (:76-78); C# states categorically "reached through the gateway (off-LAN)" (LanCommand.cs:106), which is unsound.
12. **leak collector-path output differs materially:** Swift prints real observed-resolver count including zero, a four-state signature conclusion, signer-trust state, transport pinning (SyxxLeak.swift:88-135); C# hardcodes "1 resolver observed" on NotDetected (LeakCommand.cs:132), prints no signature-validity state (:147-151), and collapses `.invalid` into the default branch (:135-138) where Swift maps it to inconclusive (:101-103).
13. **witness --gen-keypair presence probe is tri-state only on C#** (Present/Absent/Undetermined, refuses on Undetermined even with --force — WitnessCommand.cs:124-142,796-819); Swift fileExists is the documented fail-open mode "M-01 on macOS" (SyxxWitness.swift:72).
14. **witness verify trust-set threading differs:** Swift resolves at call site and passes in (SyxxWitness.swift:305,346,423); C# relies on verifier auto-load (WitnessCommand.cs:480,515,611). Confirm the auto-load resolves the same set, given difference 3.
15. **route:** Swift timeout 20s (SyxxRoute.swift:25) vs C# waitMs 1000 (RouteCommand.cs:33); C# labels failure reason + truncation (:36-46,54-58), Swift one generic line (:27); target validation is character-allowlist vs TryParse/CheckHostName (:40-41 vs :67).
16. **firstrun license state not threaded on Windows:** Swift passes isPro (Synthesis/FirstRunScan.swift:82-86); C# RunAsync(domain, progress) without it (FirstRunCommand.cs:75). Banner "CONCLUSIONS" vs "RESULTS" (:91 vs :87); suppressed-signals line format differs (:146 vs :143).

Smaller notes: nslook renders DNSSEC status via `status.description` (S :89) vs `DNSSECVerifier.Describe` (W :128) — transcript-parity risk; filter output is byte-comparable but sets no exit code on C#.

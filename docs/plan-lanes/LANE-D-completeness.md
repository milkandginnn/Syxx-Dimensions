# LANE D — Parity Completeness Audit (Seat A)

**Scope.** The closed, file-by-file inventory of every `macOS/Sources/SyxxKit/**` and
`macOS/Sources/SyxxCLI/**` artifact against its C# twin in `Windows/dotNET/Syxx.Kit/**` /
`Windows/dotNET/Syxx/**` (the ONE engine and ONE CLI shared by Windows and Linux), plus the
non-aesthetic capabilities that live in `SyxxApp`, `SyxxTelemetry` and `DeepSyxx` on macOS and
have no Linux home at any layer.

**Method.** Every "MISSING" claim below was confirmed by **symbol grep**, not by filename. Where
the C# side legitimately consolidates several Swift files into one, the table says
`CONSOLIDATED INTO <file>` and that is *not* a gap. Where a macOS type exists but has **no
producer on macOS either**, it is recorded as `DORMANT — not a gap` so nobody is sent to
implement dead weight.

**Lane seams.** Findings already owned by Lane A (encrypted-DNS transport / DNSCrypt), Lane B
(CLI exit codes + tri-state honesty) and Lane C (packaging/CI/tests) are cross-referenced only,
never re-planned. They appear in the coverage table marked `→ LANE A/B/C` so the inventory stays
closed.

**Headline.** The three known lanes do **not** close parity. This audit found **14 unclaimed
engine-level defects**, of which **five are detection-correctness divergences** — Linux/Windows
either fabricate a positive that macOS does not, or miss a positive that macOS catches. Two of
them (**D1.1 CNAME false-positive redirect**, **D1.2 DNSSEC zone-binding bypass**) are severe
enough to be the top of the queue regardless of lane ordering.

---

## 1. Coverage table — `macOS/Sources/SyxxKit/**`

| macOS file | C# twin | Status |
|---|---|---|
| `AnswerStabilityProbe.swift` | `Syxx.Kit/AnswerStabilityProbe.cs` | present, **behaviour diverges** → D1.7 |
| `AuthoritativeVantage.swift` | `Syxx.Kit/AuthoritativeVantage.cs` | present |
| `CageHelpers.swift` | — | **MISSING** — `DEFERRED — reserved stream` (D6) |
| `CageRedundancyCorrelator.swift` | — | **MISSING** — `DEFERRED — reserved stream` (D6) |
| `CNAMEChainProbe.swift` | `Syxx.Kit/CNAMEChainProbe.cs` | present, **behaviour diverges** → D1.3 |
| `ContentWarp.swift` | `Syxx.Kit/ContentWarp.cs` | present — `DEFERRED — reserved stream` (Warp) |
| `DenialOfExistenceProbe.swift` | `Syxx.Kit/DenialOfExistenceProbe.cs` | present, **behaviour diverges** → D1.4, D1.5 |
| `DimensionClient.swift` | `Syxx.Kit/DimensionClient.cs` | present, **type diverges** → D1.12 |
| `DNSCapture.swift` | `Syxx.Kit/DNSCapture.cs` | present, **field missing** → D5.3 |
| `DNSCaptureReportGenerator.swift` | `Syxx.Kit/DNSCaptureReportGenerator.cs` | present |
| `DNSCryptProxyConfig.swift` | — | **MISSING** → **LANE A** |
| `DNSCryptProxyManager.swift` | — | **MISSING** → **LANE A** |
| `DNSPathConfirmation.swift` | — | **MISSING** — but **DORMANT on macOS too** → D1.14 |
| `DNSResolver.swift` | `Syxx.Kit/DNSResolver.cs` | present, **behaviour diverges** → D1.6, D5.1, D5.2 |
| `DNSSECVerifier.swift` | `Syxx.Kit/DNSSECVerifier.cs` | present, **behaviour diverges** → D1.2 |
| `DNSTransportMonitor.swift` | — | **MISSING** → **LANE A** |
| `DNSTransportState.swift` | `Syxx.Kit/DNSTransportState.cs` | present (zero producers) → **LANE A** |
| `DomainCanonical.swift` | `Syxx.Kit/LAN/DomainCanonical.cs` | present (relocated) |
| `DualStackProbe.swift` | `Syxx.Kit/DualStackProbe.cs` | present |
| `ECSProbe.swift` | `Syxx.Kit/ECSProbe.cs` | present |
| `EDNS0Probe.swift` | `Syxx.Kit/EDNS0Probe.cs` | present, **behaviour diverges** → D1.6 |
| `EncryptedDNSConfig.swift` | `Syxx.Kit/EncryptedDNSConfig.cs` | present → **LANE A** (3 vs 11 catalog) |
| `EncryptedTransportProbe.swift` | `Syxx.Kit/EncryptedTransportProbe.cs` | present |
| `FilteringProbe.swift` | `Syxx.Kit/FilteringProbe.cs` | present |
| `FlowTransportObserver.swift` | — | **MISSING** → **LANE A** |
| `Forensics/BeaconAnalyzer.swift` | `Syxx.Kit/Forensics/Forensics.cs` | CONSOLIDATED (`BeaconAnalyzer`, `BeaconScore`, `BeaconClassification` confirmed) |
| `Forensics/Blocklist.swift` | `Syxx.Kit/Forensics/Forensics.cs` | CONSOLIDATED (`NamedDomainList`, `DomainCoverage`, `BlocklistAnalyzer` confirmed) |
| `Forensics/DNSLogAnalyzer.swift` | `Syxx.Kit/Forensics/DNSLogAnalyzer.cs` | present |
| `Forensics/EndpointLogAnalyzer.swift` | `Syxx.Kit/Forensics/EndpointLogAnalyzer.cs` | present |
| `Forensics/ExfilAnalyzer.swift` | `Syxx.Kit/Forensics/Forensics.cs` | CONSOLIDATED (`ExfilAnalyzer`, `ExfilAssessment`, `ExfilSuspicion` confirmed) |
| `Forensics/SieveBlocklistLoader.swift` | `Syxx.Kit/Forensics/Forensics.cs` | CONSOLIDATED (`SieveBlocklist*` confirmed) |
| `IPClassifier.swift` | `Syxx.Kit/IPClassifier.cs` | present |
| `LAN/ARPTableParser.swift` | `Syxx.Kit/LAN/LanCartography.cs` | CONSOLIDATED (`ARPEntry`, `ARPEntryState`, `ARPParseResult` confirmed; class name `ARPTableParser` retired) |
| `LAN/CommandRunner.swift` | `Syxx.Kit/LAN/LanCartography.cs` | CONSOLIDATED (`CommandResult`, `SystemCommandRunner` confirmed) |
| `LAN/LANCartographer.swift` | `Syxx.Kit/LAN/LanCartography.cs` | present, **persistence missing** → D5.4 |
| `LAN/NetworkAddressClassifier.swift` | `Syxx.Kit/LAN/LanCartography.cs` | CONSOLIDATED (`LANAddressKind`, `NetworkAddressClassifier` confirmed) |
| `LeakDetector.swift` | `Syxx.Kit/LeakDetector.cs` | present |
| `LicenseManager.swift` | `Syxx.Kit/LicenseManager.cs` | present, **path diverges** → D5.5 |
| `LookalikeDetector.swift` | `Syxx.Kit/LookalikeDetector.cs` | present, **behaviour diverges** → D1.8 |
| `MonitorReportAnalyzer.swift` | `Syxx.Kit/MonitorReportAnalyzer.cs` | present |
| `NetworkCohort.swift` | `Syxx.Kit/NetworkCohort.cs` | present |
| `NetworkKeyState.swift` | `Syxx.Kit/NetworkKeyState.cs` | present → **LANE B** (tri-state collapses) |
| `OnboardingLifecycle.swift` | `Syxx.Kit/OnboardingLifecycle.cs` | present |
| `OpenResolverProbe.swift` | `Syxx.Kit/OpenResolverProbe.cs` | present, **behaviour diverges** → D1.10 |
| `PktapHeader.swift` | — | **MISSING** — Apple-only BPF/pktap header → **platform-inapplicable**, see §7 |
| `PrivilegedHelperProtocol.swift` | — | **MISSING** — privilege model → D4 |
| `ProbeBudget.swift` | `Syxx.Kit/ProbeBudget.cs` | present |
| `ProbePlan.swift` | `Syxx.Kit/ProbePlan.cs` | present |
| `ProbeRunner.swift` | `Syxx.Kit/ProbeRunner.cs` | present, **API missing** → D1.11 |
| `RemoteUrlPolicy.swift` | `Syxx.Kit/RemoteUrlPolicy.cs` | present |
| `ResolverAttribution.swift` | `Syxx.Kit/ResolverAttribution.cs` | present |
| `ResolverConsensusEngine.swift` | `Syxx.Kit/ResolverConsensusEngine.cs` | present |
| `ResolverConsensusStore.swift` | `Syxx.Kit/ResolverConsensusStore.cs` | present |
| `ResolverContext.swift` | `Syxx.Kit/ResolverContext.cs` | present |
| `ResolverLatencyProbe.swift` | `Syxx.Kit/ResolverLatencyProbe.cs` | present |
| `ResponseSizeProbe.swift` | `Syxx.Kit/ResponseSizeProbe.cs` | present |
| `SignedEvidencePayload.swift` | `Syxx.Kit/SignedEvidencePayload.cs` | present |
| `StubEntropyProbe.swift` | `Syxx.Kit/StubEntropyProbe.cs` | present |
| `SVCBProbe.swift` | `Syxx.Kit/SVCBProbe.cs` | present, **behaviour diverges** → D1.9 |
| `Synthesis/Conclusion.swift` | `Syxx.Kit/Synthesis/Conclusion.cs` | present |
| `Synthesis/CrossVantageConsensus.swift` | `Syxx.Kit/Synthesis/CrossVantageConsensus.cs` | present |
| `Synthesis/DNSCanaries.swift` | `Syxx.Kit/Synthesis/DNSCanaries.cs` | present |
| `Synthesis/FirstRunScan.swift` | `Syxx.Kit/Synthesis/FirstRunScan.cs` | present |
| `Synthesis/MatrixDeterminativeEngine.swift` | `Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs` | present |
| `Synthesis/PlainExplanation.swift` | `Syxx.Kit/Synthesis/PlainExplanation.cs` | present |
| `Synthesis/Report.swift` | `Syxx.Kit/Synthesis/Report.cs` | present |
| `Synthesis/ReportCard.swift` | `Syxx.Kit/Synthesis/ReportCard.cs` | present |
| `Synthesis/ResultAction.swift` | `Syxx.Kit/Synthesis/ResultAction.cs` | present |
| `Synthesis/SignedIncidentReport.swift` | `Syxx.Kit/Synthesis/SignedIncidentReport.cs` | present |
| `Synthesis/Stage5Presentation.swift` | *(archived: `archive/dst-engine-windows/`)* | **DORMANT — not a gap** → D1.13 |
| `Synthesis/SynthesisEngine.swift` | `Syxx.Kit/Synthesis/SynthesisEngine.cs` | present |
| `Synthesis/Synthesizer.swift` | `Syxx.Kit/Synthesis/Synthesizer.cs` | present |
| `Synthesis/SyxxMatrixIntegration.swift` | `Syxx.Kit/Synthesis/SyxxMatrixIntegration.cs` | present |
| `Synthesis/TrayIndicator.swift` | `Syxx.Kit/Synthesis/TrayIndicator.cs` | present |
| `SyxxConfig.swift` | `Syxx.Kit/SyxxConfig.cs` | present |
| `SyxxDiagnostics.swift` | `Syxx.Kit/SyxxDiagnostics.cs` | present |
| `SyxxSettings.swift` | `Syxx.Kit/SyxxSettings.cs` | present |
| `SyxxTracer.swift` | `Syxx.Kit/SyxxTracer.cs` | present, **API missing** → D3.1 |
| `TextDiff.swift` | `Syxx.Kit/TextDiff.cs` | present |
| `TracerouteRunner.swift` | `Syxx.Kit/TracerouteRunner.cs` | present |
| `TriState.swift` | `Syxx.Kit/TriState.cs` | present |
| `Trust/AnswerLedger.swift` | `Syxx.Kit/Trust/Trust.cs` | CONSOLIDATED (`AnswerEntry`, `AnswerDrift`, `AnswerLedger` confirmed) |
| `Trust/ContinuousMonitor.swift` | `Syxx.Kit/Trust/ContinuousMonitor.cs` | present |
| `Trust/IntegrityTag.swift` | `Syxx.Kit/Trust/Trust.cs` | CONSOLIDATED (`IntegrityTag` confirmed); **`CacheIntegrityTag` / `CacheIntegrityVerification` absent** → D1.15 |
| `Trust/MonitorScheduler.swift` | `Syxx.Kit/Trust/MonitorScheduler.cs` | present, **persistence missing** → D5.6 |
| `Trust/Sentinel.swift` | `Syxx.Kit/Trust/Trust.cs` | CONSOLIDATED (`Sentinel`, `NetworkConfigSnapshot`, `ConfigChange`, `ConfigChangeKind`, `HomeBaseline` confirmed) |
| `Trust/WitnessTestifyPlanner.swift` | `Syxx.Kit/Trust/WitnessTestifyPlanner.cs` | present |
| `TTLAnomalyProbe.swift` | `Syxx.Kit/TTLAnomalyProbe.cs` | present |
| `UnboundManager.swift` | `Syxx.Kit/UnboundManager.cs` | present → **LANE A** (`NullResolverSwitcher`) |
| `UnboundResolver.swift` | `Syxx.Kit/UnboundResolver.cs` | present |
| `UpdateService.swift` | `Syxx.Kit/UpdateService.cs` | present (C# ahead: `pkexec` path) |
| `VantageComparer.swift` | `Syxx.Kit/VantageComparer.cs` | present, **behaviour diverges** → D1.1 |
| `VersionBindProbe.swift` | `Syxx.Kit/VersionBindProbe.cs` | present but **structurally dead** → D1.6 |
| `WarpEngine.swift` | `Syxx.Kit/WarpEngine.cs` | present — `DEFERRED — reserved stream` |
| `WarpManifestPinning.swift` | `Syxx.Kit/WarpManifestPinning.cs` | present — `DEFERRED — reserved stream` |
| `WildcardSynthesisProbe.swift` | `Syxx.Kit/WildcardSynthesisProbe.cs` | present, **behaviour diverges** → D1.9 |
| `WitnessKeyManager.swift` | `Syxx.Kit/WitnessKeyStore.cs` | present (renamed; `WitnessKeyManager` referenced in C# comments/UI) |
| `WitnessSigner.swift` | `Syxx.Kit/WitnessSigner.cs` | present |

## 2. Coverage table — `macOS/Sources/SyxxCLI/**`

| macOS file | C# twin | Status |
|---|---|---|
| `SyxxCLI.swift` | `Syxx/Program.cs` | present; top-level subcommand set is identical (nslook, mirror, leak, witness, key, route, lan, forensics, sentinel, cage, warp, speed, unbound, filter, firstrun, parity-transcript). Flag-level gaps → **LANE B** |
| `Commands/ParityTranscript.swift` | `Syxx/Commands/ParityTranscriptCommand.cs` | present |
| `Commands/SyxxCage.swift` | `Syxx/Commands/CageCommand.cs` | present — `DEFERRED — reserved stream` |
| `Commands/SyxxFilter.swift` | `Syxx/Commands/FilterCommand.cs` | present |
| `Commands/SyxxForensics.swift` | `Syxx/Commands/ForensicsCommand.cs` | present → **LANE B** |
| `Commands/SyxxKey.swift` | `Syxx/Commands/KeyCommand.cs` | present → **LANE B** |
| `Commands/SyxxLan.swift` | `Syxx/Commands/LanCommand.cs` | present → **LANE B** |
| `Commands/SyxxLeak.swift` | `Syxx/Commands/LeakCommand.cs` | present → **LANE B** |
| `Commands/SyxxMirror.swift` | `Syxx/Commands/MirrorCommand.cs` | present — `DEFERRED — reserved stream` |
| `Commands/SyxxNSLook.swift` | `Syxx/Commands/NslookCommand.cs` | present |
| `Commands/SyxxRoute.swift` | `Syxx/Commands/RouteCommand.cs` | present |
| `Commands/SyxxSentinel.swift` | `Syxx/Commands/SentinelCommand.cs` | present → **LANE B** |
| `Commands/SyxxSpeed.swift` | `Syxx/Commands/SpeedCommand.cs` | present |
| `Commands/SyxxUnbound.swift` | `Syxx/Commands/UnboundCommand.cs` | present → **LANE A** |
| `Commands/SyxxWarp.swift` | `Syxx/Commands/WarpCommand.cs` | present — `DEFERRED — reserved stream` |
| `Commands/SyxxWitness.swift` | `Syxx/Commands/WitnessCommand.cs` | present → **LANE B** |
| `Synthesis/FirstRunScan.swift` | `Syxx/Commands/FirstRunCommand.cs` | present |

**Reverse direction (C# has no macOS twin).** These are Linux/Windows-ahead, not gaps:
`Syxx.Kit/SyxxPaths.cs` (owner-only 0700/0600 creation helpers — macOS inlines `posixPermissions`),
`Syxx.Kit/DiagnosticRedaction.cs`, `Syxx.Kit/WitnessWireJson.cs`,
`Syxx.Kit/{ISecretStore,PlatformSecretStore,LinuxSecretStore,DpapiSecretStore}.cs`,
`Syxx.Kit/Platform/{LinuxDistro,MachineFingerprint}.cs`, `Syxx.Kit/Capture/{CaptureEngine,CaptureEvents}.cs`,
`Syxx.Kit/{ProphetConsensus,ProphetPresentation}.cs`, `Syxx.Kit/{EncryptedDNSConfigurator,FirstRunCoordinator}.cs`
(macOS keeps the last two in `SyxxApp`, i.e. GUI-only — see D2.2/D2.3).
One **macOS-side** gap surfaces from the reverse check: `Program.cs:250-252` offers an
`[S] Scan` first-run-scan entry in the interactive menu that `SyxxCLI.swift:213-270` does not.
Not a Linux gap; logged for the owner.

---

## D1 — Engine-level gaps in `SyxxKit` with no C# representation or divergent behaviour

Ordered by severity. Every item confirmed by reading both implementations.

### D1.1 — CNAME rdata is fed to the "blocked answer" classifier ⇒ guaranteed false-positive redirect (C055/C117)
- **macOS:** `VantageComparer.swift:195-200` filters to address records (`type == 1 || type == 28`)
  before `IPClassifier.isBlockingAnswer`, and `:250-274` (C117) applies the same address-only
  filter to every overlap/corroboration set.
- **C#:** `VantageComparer.cs:264` — `localOutcome.Records.Any(r => IPClassifier.IsBlockingAnswer(r.Rdata))`
  with **no type filter**. `VantageComparer.cs:256-262` builds `publicUnion`, `localRdatas` and
  `resolverOverlapCount` from raw `Records` with no filter either.
- **Why it fires:** `DNSResolver.cs:818-821` deliberately surfaces CNAME (type 5) and RRSIG
  (type 46) in `Records`. `IPClassifier.cs:23-27` returns `IPAddressClass.Invalid` for anything
  `IPAddress.TryParse` rejects — i.e. every domain-name and hex rdata — and
  `IPClassifier.cs:48` maps `Invalid ⇒ IsBlockingAnswer == true`.
- **Consequence:** on Linux/Windows **any domain fronted by a CNAME** (most CDN-hosted sites)
  sets `localLooksBlocked = true` ⇒ `redirection = true`. A fabricated tampering positive on
  ordinary traffic. Symmetrically, C117's absence lets identical CNAME strings inflate the
  overlap count and *suppress* a genuine redirection.
- **Layer:** engine (`Syxx.Kit/VantageComparer.cs`). **Effort: S.** **Required for parity — and a release blocker on its own.**

### D1.2 — DNSSEC verifier has no zone binding and no DNSKEY field validation (C059 + C125)
- **macOS:** `DNSSECVerifier.swift:100-127` — `classify(records:now:queriedName:)` accepts a DNSKEY
  only when its owner normalises to the queried zone **and** rdata Protocol == 3 (RFC 4034 §2.1.2),
  ZONE flag `0x0100` set (§2.1.1) and algorithm != 0 (§2.1.3); RRSIGs must own the queried zone and
  carry a signer that is the zone or an ancestor.
- **C#:** `DNSSECVerifier.cs:96-101` — `Classify(IEnumerable<DNSRecord>, DateTime)`. **No
  `queriedName` parameter at all**; `dnskeyPresent = recs.Any(r => r.Type == 48)`; no Protocol /
  ZONE-flag / algorithm check anywhere.
- **Consequence:** an on-path attacker appends a valid, in-window DNSKEY+RRSIG pair harvested from
  *any* signed zone and the Linux/Windows verifier reports `RecordsPresentUnverified`. The
  signature-stripping canary is defeated by construction. macOS is immune.
- **Layer:** engine. **Effort: M** (signature change ripples to `DNSSECVerifier.cs:88` and its
  tests). **Required for parity.**

### D1.3 — Hostile CNAME chains grade as ordinary chains (B075 + C072)
- **macOS:** `CNAMEChainProbe.swift:88-124` returns `(chain, ChainCompletion)` with
  `terminal | loop | capExceeded | conflictingCNAME`; `:59-81` surfaces `conflictingCNAME` as
  `divergent` ("injection signature") and loop/cap as `inconclusive` ("malformed evidence, not
  comparable"), including for reference chains.
- **C#:** `CNAMEChainProbe.cs:56-68` — `ReconstructChain` returns a bare `List<string>`; a loop is
  silently `break`-ed, a cap hit silently truncates, and `cnameMap[owner] = target` **overwrites**
  a conflicting CNAME (last-wins) with no detection.
- **Consequence:** a cyclic or overlong chain compares as a normal chain (arbitrary
  Consistent/Divergent/NoCNAME), and two conflicting CNAMEs for one owner — invalid DNS and a plain
  injection signature — are invisible on Linux.
- **Layer:** engine. **Effort: S.** **Required for parity.**

### D1.4 — Injected NSEC bypasses the SOA-ancestry test (C126)
- **macOS:** `DenialOfExistenceProbe.swift:105-116` evaluates SOA ancestry **first**; a
  wrong-zone SOA stays `.contradictory` regardless of `hasNSEC`. NSEC may upgrade a consistent
  denial, never rescue an inconsistent one.
- **C#:** `DenialOfExistenceProbe.cs:66-74` — `if (hasNSEC) return NsecProven;` **short-circuits
  before** the ancestry check.
- **Consequence:** an attacker attaches any NSEC/NSEC3 record and the wrong-zone SOA — the only
  real test — is never reached. Linux/Windows report `NsecProven` (authenticated denial) on an
  injected denial. This is the exact bug macOS fixed as C126.
- **Layer:** engine. **Effort: S.** **Required for parity.**

### D1.5 — NODATA graded as a suspicious bare denial (C127)
- **macOS:** `DenialOfExistenceProbe.swift:33-38, 126-133` carries `NegativeKind`
  (`nameDoesNotExist` / `typeHasNoData`) and restricts the "likely injected" `bareDenial` framing
  to NXDOMAIN; a NODATA with no SOA is `inconclusive` (RFC 2308 §5 makes the SOA a SHOULD for
  authoritative answers only; many resolvers omit it on cached NODATA).
- **C#:** `DenialOfExistenceProbe.cs:9-19` — the `DenialOfExistence` record has **no
  `NegativeKind`**, and `:83` returns `BareDenial` for every SOA-less negative including NODATA.
- **Consequence:** routine cached NODATA answers are reported as suspicious on Linux. False
  positive; also a wire-field gap (D5.3).
- **Layer:** engine. **Effort: S.** **Required for parity.**

### D1.6 — `DNSResponseStatus.rcodeOther` does not exist in C#, with two downstream failures (C069)
- **macOS:** `DNSResolver.swift:107-111` defines `rcodeOther` — "the resolver answered with a
  recognised but non-success RCODE (FORMERR=1, NOTIMP=4, BADVERS=16)" — explicitly distinct from
  `transportError` ("no answer was received"). `:968-999` populates EDNS/authority/byte-length
  evidence on every non-success branch.
- **C#:** `DNSResolver.cs:92-104` — the enum stops at `TransportError`. `DNSResolver.cs:691-693`
  collapses every non-0/3/5 RCODE into `TransportError` and returns a response with **no `Edns`,
  no `AuthorityRecords`, no `ResponseByteLength`**.
- **Downstream failure 1 (false positive):** `EDNS0Probe.swift:54-59` has a BADVERS branch that
  reports "resolver rejected the advertised EDNS version". `EDNS0Probe.cs:30-58` cannot have it —
  BADVERS becomes `TransportError` ⇒ `DidFail` ⇒ plain fallback succeeds ⇒ reported as
  **`Broken` — "OPT is being dropped on-path (middlebox)"**. A version-negotiation refusal is
  reported as on-path interference.
- **Downstream failure 2 (wire break):** `DNSResponseStatus` is JSON-serialised
  (`DNSResolver.cs:91` `[JsonConverter(typeof(DNSResponseStatusJsonConverter))]`). A macOS artefact
  containing `"rcodeOther"` has no C# counterpart. **Not covered by
  `scripts/cross-platform-parity-gate.py`** (its `VOCABULARIES` list stops at seven enums).
- **Layer:** engine. **Effort: S** (enum + one branch + the EDNS0 branch). **Required for parity.**

### D1.6b — `version.bind` fingerprinting is structurally dead on Linux/Windows (C057)
Same file, separate defect, high value because it is a whole feature that cannot ever return a
result.
- **macOS:** `DNSResolver.swift:1046-1052` extracts QCLASS from the question and filters answers
  against the **queried** class.
- **C#:** `DNSResolver.cs:803` — `if (r.Cls != 1) continue;` hardcodes class IN.
- **Consequence:** `VersionBindProbe.cs:43` sends `qclass: ClassCHAOS` (3); every CHAOS answer is
  discarded by the class filter, `answerRecords.Count == 0`, and `DNSResolver.cs:843` returns
  `NoData`. The probe can **only ever** report "the CHAOS version.bind query got no usable
  response". The macOS twin works.
- **Layer:** engine. **Effort: S.** **Required for parity.**

### D1.7 — TTL monotonicity checked at the endpoints only; disjoint CDN rotation called unstable (B078 + B079)
- **macOS:** `AnswerStabilityProbe.swift:81-88` requires **every adjacent pair** to be
  non-increasing (endpoint-only comparison misses `100, 500, 90`); `:103-107` classifies disjoint
  rotation as `inconclusive`, not `unstable`, because CDNs rotate between disjoint healthy edge
  pools.
- **C#:** `AnswerStabilityProbe.cs:66` — `bool ttlHonored = (minTTLs.Last() <= minTTLs.First());`
  and `:84-85` returns `Unstable` for the disjoint case.
- **Consequence:** an impossible intermediate TTL rise (a cache-rewrite signature) reads clean
  (false negative), and ordinary CDN load balancing reads as instability (false positive).
  Secondary: `minTTLs.Last()` on an empty list throws `InvalidOperationException`; the
  `sets.Count >= 2` guard does not constrain `minTTLs`.
- **Layer:** engine. **Effort: S.** **Required for parity.**

### D1.8 — Lookalike/typosquat detection has no transposition term (B140)
- **macOS:** `LookalikeDetector.swift:83-108` implements true Damerau-Levenshtein with the
  adjacent-transposition recurrence (`d[i-2][j-2] + 1`), keeping a full matrix because two rows
  cannot express it. Its own comment: *"an adjacent swap like 'payapl' had distance 2, exceeded the
  cap of 1, and returned no suspicious finding for a documented typosquat shape."*
- **C#:** `LookalikeDetector.cs:57-77` — self-described "Damerau-ish (Levenshtein)", two-row
  recurrence, **no transposition term**.
- **Consequence:** transposition typosquats (`payapl.com`, `gogole.com`) are missed on
  Linux/Windows at the standard cap of 1 and detected on macOS. Reproducible false negative.
- **Layer:** engine. **Effort: S.** **Required for parity.**

### D1.9 — Two vantage-thinness guards missing (B074 SVCB, B057 wildcard)
- **B074 — SVCB.** `SVCBProbe.swift:80-89` requires **≥ 2** usable vantages and degrades to
  `inconclusive` when **any** configured reference failed ("a present/absent result with failed
  vantages is not 'all responding resolvers', it is a subset"). `SVCBProbe.cs:64-65` requires only
  `present.Count + absent.Count >= 1` and has **no failed-vantage degradation** — a single
  responder, or a spoofer answering while dropping the rest, yields a clean present/absent signal.
- **B057 — wildcard synthesis.** `WildcardSynthesisProbe.swift:79-93` rejects a reference equal to
  the resolver-under-test and falls back to a distinct one, because a self-comparison makes
  `nxHijack` impossible to fire and emits a **false-clean `.negative`**.
  `WildcardSynthesisProbe.cs:36,47` takes `reference = "1.1.1.1"` with **no such guard** — and
  1.1.1.1 is a routinely tested resolver, so the vacuous case is common.
- **Layer:** engine. **Effort: S each.** **Required for parity.**

### D1.10 — Unmeasured amplification reported as ~0.0× low-amp (B080 + C068)
- **macOS:** `OpenResolverProbe.swift:94-99` — `amplificationFactor` returns `Double?`; `nil`
  (missing byte count) ⇒ `.untested` "unmeasured, not clean", never `recursiveLowAmp` at ~0.0×.
  `:74` sends the amplification probe with EDNS0 4096 (C068) so the answer can fit in UDP, and
  `:88-91` returns `.untested` when the answer arrived via TCP fallback, because a TCP message is
  not a reflectable UDP datagram.
- **C#:** `OpenResolverProbe.cs:59-64` — `AmplificationFactor` returns `double` and **`return 0;`**
  on failure ⇒ `Classify(target, 0)` ⇒ `RecursiveLowAmp` with "~0.0×". The amp query at
  `OpenResolverProbe.cs:50` carries **no `edns0UDPSize`**, so the response is systematically
  truncated at 512 bytes and the factor under-measured; there is **no `IsTruncated` guard**.
- **Consequence:** unmeasured and truncated amplification both render as a benign low-amp result.
- **Layer:** engine. **Effort: S.** **Required for parity.**

### D1.11 — `ProbeOutcome` does not exist in C#; probe failure reasons collapse to `null` (C066)
- **macOS:** `ProbeRunner.swift:36-49, 201-215` — `ProbeOutcome<Output>` with
  `produced | untested(reason:) | deadline | transportError(_:)`, plus `runTyped`. Its comment:
  *"Callers (ProbePlan) can use this to record WHY a probe produced no result in SectionInputs,
  rather than letting Synthesis guess from a bare nil."*
- **C#:** `ProbeRunner.cs:35, 51` — only `Task<T?> RunAsync<T>()`.
- **Consequence:** on Linux/Windows, Synthesis cannot distinguish "budget exhausted ⇒ untested"
  from "timed out ⇒ deadline" from "threw ⇒ transport error" from "produced nil". This is the
  untested-vs-inconclusive honesty spine failing at the probe boundary — the same defect class
  Lane B is fixing at the CLI boundary, one layer down. **Coordinate with Lane B.**
- **Layer:** engine. **Effort: M.** **Required for parity.**

### D1.12 — `DimensionOutcome<T>` missing; Dimension failures collapse to `null` (D081)
- **macOS:** `DimensionClient.swift:8-16` — `DimensionOutcome<T>` =
  `fetched | transportFailure | decodeFailure`, precisely so callers can tell "server unreachable"
  from "server responded with undecodable data" from "server confirmed ground truth".
- **C#:** `DimensionClient.cs:75, 107` — `Task<GroundTruth?>` / `Task<DiffAssessment?>`.
  All three states are `null`.
- **Consequence:** the Dimension ground-truth path — the one attacker-independent reference the
  product has — cannot report *why* it produced nothing on Linux.
- **Layer:** engine. **Effort: M.** **Required for parity.**

### D1.13 — `Stage5Presentation` — **DORMANT, not a gap. Do not implement.**
Recorded so the owner sees it was checked and closed, not missed. The Swift file
`Synthesis/Stage5Presentation.swift:9` states *"Production writes `Conclusion.stage5 = nil`"*; the
only macOS constructor call is the preview fixture (`SyxxApp/Presentation/Stage5PreviewFixture.swift:64`).
The C# side retired the DTO deliberately — `SyxxUI/Views/ConclusionsView.xaml.cs:293-305` records
the migration to `archive/dst-engine-windows/`. The ten `Stage5*` string enums that appear as
"Swift-only" (`AnswerStatus`, `MechanismClass`, `CredalMeasureKind`, `DataRegime`, `DecisionBasis`,
`DeterminationState`, `ObservationValueKind`, `UnavailableReason`, `VantageKind`,
`AnswerComparisonClass`) are all this dormant schema. **Genuinely out.**

### D1.14 — `DNSPathConfirmation.swift` — **MISSING but also unwired on macOS**
`DNSPathConfirmation.swift:174-260` (`DNSPathConfirmationEngine`, `DNSPathSnapshot`,
`EncryptedDNSFlavor`, `DNSPathReason`) has no C# twin, and its only macOS reference outside itself
is a comment in `CageRedundancyCorrelator.swift:19` — nothing calls it. It is the *definitive-tier*
form of the transport question, designed to be gathered root-side by `DeepSyxx` and returned over
XPC (`DNSPathConfirmation.swift:16-22`). Its own header notes the classifier is platform-agnostic
and only the snapshot gathering is macOS-specific.
- **Disposition:** **defer until Lane A lands and D4 has a privilege model.** Porting the classifier
  before Linux has a privileged tier that can observe the DNSCrypt proxy process would produce a
  second unwired module. **Cross-ref: LANE A** owns `EncryptedDNSFlavor`'s DNSCrypt member.
- **Layer:** engine (classifier) + D4 (snapshot gathering). **Effort: M.** **Required for full parity, sequenced after Lane A + D4.**

### D1.15 — `CacheIntegrityTag` / `CacheIntegrityVerification` absent from `Trust.cs`
`Trust/IntegrityTag.swift:14` (`CacheIntegrityVerification`) and `:129` (`CacheIntegrityTag`) have
no C# symbol; only the internal `IntegrityTag` (`:20`) was consolidated into
`Syxx.Kit/Trust/Trust.cs`. Confirmed by symbol grep across `Windows/dotNET` and `Linux/Ubuntu`.
- **Layer:** engine. **Effort: S.** **Required for parity** (audit the macOS call sites first —
  if `CacheIntegrityTag` is itself unwired on macOS this downgrades to a D1.13-style closure).

---

## D2 — `SyxxApp` capabilities with no Linux home at ANY layer

The Linux GUI is off-limits, so each item names the layer that must own it on Linux
(engine `Syxx.Kit` or CLI `Syxx`), never the Avalonia project.

### D2.1 — DNS cache flush — **no implementation anywhere on Windows or Linux**
- **macOS:** `SyxxApp/DashboardView.swift:143` (button) → `SyxxApp/AppModel.swift:651` →
  `SyxxApp/PrivilegeManager.swift:439-458` → XPC → `DeepSyxx/main.swift:101-132`
  (`dscacheutil -flushcache` + `killall -HUP mDNSResponder`).
- **Linux/Windows:** a repo-wide grep for cache-flush across `Syxx.Kit`, `Syxx` and
  `Linux/Ubuntu/SyxxUI.Avalonia` returns **nothing**. No `resolvectl flush-caches`, no
  `ipconfig /flushdns`, no `systemd-resolve --flush-caches`.
- **Owner on Linux:** engine (`Syxx.Kit`, a `DnsCacheFlusher` with a `resolvectl flush-caches` /
  `nscd -i hosts` / `unbound-control flush_zone` ladder) **plus a CLI verb** so it is reachable
  without a GUI. **Effort: S.** **Required for parity** — it is a user-facing action on the macOS
  dashboard.

### D2.2 — Broadcast / MOTD delivery and dismissal — Linux has none, and the type lives in a Windows-only project
- **macOS:** `SyxxApp/DimensionService.swift:117` `fetchBroadcast()`, `:133` `hasDismissed(_:)`,
  `:138` `markDismissed(_:)`, `:148` `isVersionInRange(_:minVersion:maxVersion:)`, rendered by
  `SyxxApp/BroadcastPanelView.swift` and `SyxxApp/NSPanelManager.swift:25`.
- **Windows:** present, but the `Broadcast` record is declared at
  `Windows/dotNET/SyxxUI/Services/DimensionService.cs:16` — **inside the WinUI project**, consumed
  by `SyxxUI/MainWindow.xaml.cs:123-125,156,220`.
- **Linux:** `Linux/Ubuntu/SyxxUI.Avalonia/**` has `MotdLoadController.cs` and
  `WhatsNewViewModel.cs` but **no broadcast fetch, no dismissal persistence, no version gating**,
  and it cannot reference the Windows GUI project to get them.
- **Owner on Linux:** engine. The `Broadcast` record, `FetchBroadcast`, `HasDismissed`,
  `MarkDismissed` and `IsVersionInRange` belong in `Syxx.Kit` (next to `DimensionClient.cs`), which
  both GUIs and the CLI can consume. **Effort: M.** **Required for parity.**

### D2.3 — Already covered, recorded for closure
- `PrivilegeManager.swift` → **D4** (privilege model).
- `EncryptedDNSConfigurator.swift` → C# twin exists at `Syxx.Kit/EncryptedDNSConfigurator.cs`
  (`resolvectl` path, `:103-111`); catalog/transport gaps → **LANE A**.
- `FirstRunCoordinator.swift` → C# twin exists at `Syxx.Kit/FirstRunCoordinator.cs`; `--isPro`
  gap → **LANE B**.
- `UpdatePanel.swift` / `UpdateService.swift` → `Syxx.Kit/UpdateService.cs` is **ahead** of macOS
  (873 vs 661 lines, `pkexec` install path at `UpdateService.cs:409`).
- `RemoteContentView.swift` → `SyxxUI/Presentation/MotdLoadController.cs` and
  `Linux/Ubuntu/SyxxUI.Avalonia/Presentation/MotdLoadController.cs` both exist; the WKWebView
  fallback chain is presentation, **aesthetic — out of scope**.
- `GuidedSetup.swift`, `NSPanelManager.swift`, `AboutPanel.swift`, `EULAPanel.swift`,
  `DashboardWardenState.swift`, `PreviewSupport.swift`, `Views/Components/**` → presentation.
  `BundledAgreement.swift` has a Linux twin at
  `Linux/Ubuntu/SyxxUI.Avalonia/Services/BundledAgreement.cs`. **Aesthetic — out of scope.**
- `AppModel.swift` (2125 lines) → orchestration over engine calls that all exist; the only
  non-aesthetic residue is D2.1.

---

## D3 — `SyxxTelemetry` gaps the parity gate does not cover

`scripts/cross-platform-parity-gate.py` asserts exactly three things: OTel/ActivitySource **span
names**, seven **wire vocabularies** (`determinative_state`, `tri_state`, `tracer_outcome`,
`section_status`, `section`, `diagnosis_confidence`), and one signed-payload key (`"conclusion"`,
`SIGNED_PAYLOAD_FILES`/`REQUIRED_SIGNED_PAYLOAD_KEYS`). It scopes to `SyxxKit` vs `Syxx.Kit` only —
`SyxxTelemetry` vs `Syxx.Telemetry` is **entirely outside the gate**. What it misses:

### D3.1 — Consent withdrawal cannot stop OTel span recording on Linux/Windows
- **macOS:** `SyxxTelemetry/OTelBootstrap.swift:40-52` `discardOnConsentWithdrawal()` calls
  `SyxxTracer.disable()` **first** (so a span racing in from a scan thread finds the tracer
  disabled and creates nothing), then swaps in `DefaultTracerProvider`, and **deliberately does
  not flush**. `:53-55` `resumeOnConsentGranted()` re-enables without backfilling. `:34-38`
  `flush()` is documented as shutdown-only.
- **C#:** `Syxx.Telemetry/OTelBootstrap.cs` exposes only `Register(...)` (`:39`) and
  `CurrentOsType()` (`:90`). `Syxx.Kit/SyxxTracer.cs` has **no `Disable` / `Enable` / `IsDisabled`**
  (confirmed by grep). The only teardown anywhere is
  `Linux/Ubuntu/SyxxUI.Avalonia/Services/LinuxReadyServices.cs:38` `_tracerProvider?.Dispose()` — and
  disposal runs `Shutdown`, which **drains the batch processor**, i.e. it *transmits* the buffered
  spans. That is exactly what macOS's comment forbids on opt-out.
- **Consequence:** on Linux, withdrawing telemetry consent mid-session neither stops recording nor
  discards the buffer; the shutdown path exports it. Sentry is correct on both platforms
  (`SentryDiagnosticSink.cs:119-122` discards), so the defect is OTel-only.
- **Layer:** `Syxx.Kit/SyxxTracer.cs` (add `Disable`/`Enable`/`IsDisabled`) +
  `Syxx.Telemetry/OTelBootstrap.cs` (add `DiscardOnConsentWithdrawal` / `ResumeOnConsentGranted`).
  **Effort: S.** **Required for parity** — the Settings toggle and the privacy-policy claim depend on it.

### D3.2 — No `SentryBootstrap` twin
`SyxxTelemetry/SentryBootstrap.swift:8-30` is a single consent-gated `setup(ifAllowed:)` seam.
C# has `SentryDiagnosticSink.Install(surface, platform, ifAllowed)` (`:43`) which covers the same
ground; **no gap** — recorded so the file is not re-reported.

### D3.3 — Gate coverage gap (process, not code)
`DNSResponseStatus` (D1.6), `DNSProtocol` (D5.1), `AttributionConfidence` (D5.3) and every
`SignedEvidencePayload` key other than `"conclusion"` are serialised wire contracts that the gate
does **not** assert. Recommend extending `VOCABULARIES` and `REQUIRED_SIGNED_PAYLOAD_KEYS` once
D1.6/D5.1/D5.3 land, so the fix cannot silently regress. **Effort: S.** Coordinate with **LANE C**
(CI ownership).

---

## D4 — Privileged helper: macOS has a signed root daemon, Linux has nothing

**What macOS ships.** `DeepSyxx` is an `SMJobBless` launchd **root daemon** embedded in the app
bundle, brokered over XPC by `SyxxApp/PrivilegeManager.swift` (`ensureHelperInstalled` `:188`,
`installHelper` `:297`, `connectToHelper` `:357`, `uninstallHelper` `:239`). Its contract is
`SyxxKit/PrivilegedHelperProtocol.swift:5-36`, with five privileged operations:

| Operation | macOS impl | Linux status |
|---|---|---|
| `ping` (liveness, D094b) | `DeepSyxx/main.swift:21` | n/a — no daemon |
| `startCageCapture` | `DeepSyxx/main.swift:25-99` | CLI re-exec under `sudo` — `Syxx/Commands/CageCommand.cs:124-136` |
| `flushDNSCache` | `DeepSyxx/main.swift:101-132` | **absent everywhere** → D2.1 |
| `installPfRules` / `removePfRules` | `DeepSyxx/main.swift:134-187` | **absent**; no nftables/iptables equivalent. **Unused by the macOS app today** — no caller outside the protocol. |
| `setDNSServers` | `DeepSyxx/main.swift:189-264` | partial: `Syxx.Kit/EncryptedDNSConfigurator.cs:103-111` (`resolvectl`), gated `ElevationRequired`. `unbound` switching → **LANE A** (`NullResolverSwitcher`) |

**The delivery gap.** Linux has **no postinst, no systemd unit, no PolicyKit action, no `pkexec`
policy** for anything except the update installer (`UpdateService.cs:409`). Elevation is entirely
"tell the user to re-run under `sudo`" (`Program.cs:915-918`, `CageCommand.cs:124`). That is honest
and acceptable for a CLI, but it means:
- there is **no way for a GUI-initiated privileged action to work at all** on Linux;
- D2.1 (cache flush) and D1.14 (definitive-tier DNS path confirmation, which requires root to tie
  the loopback resolver to the DNSCrypt proxy process) have nowhere to live.

**Recommendation.** Do **not** port an XPC daemon. Adopt the platform-native model: a PolicyKit
action file plus `pkexec`-invoked helper verbs on the existing CLI, so the privileged surface stays
the one already-audited binary. Two named privileged verbs cover everything currently needed:
DNS-cache flush and resolver switching.
- **Layer:** packaging + CLI. **Effort: M.** **Required for full parity.**
  **Cross-ref: LANE C owns the `.deb`/snap that must ship the policy file; LANE A owns the
  resolver-switch verb.** This lane names the requirement; it does not plan the packaging.
- **`installPfRules` / `removePfRules`: recommend ratifying as OUT.** They are unreferenced on
  macOS. Porting `pfctl` semantics to nftables to satisfy a dead interface is scope creep.

---

## D5 — Wire / format contracts with no C# counterpart

Silent interchange breakers: an enum raw value, JSON field or on-disk path that one platform
writes and the other cannot read.

| ID | Contract | macOS | C# | Impact | Effort |
|---|---|---|---|---|---|
| **D5.1** | `DNSProtocol` raw values | `NetworkKeyState.swift:50-53` — `case doh = "DoH"`, `case dot = "DoT"` (wire **"DoH"/"DoT"**) | `NetworkKeyState.cs:55-59` — `enum DnsProtocol { Doh, Dot }`, no `JsonStringEnumConverter`, no `JsonPropertyName` ⇒ serialises as **integer** (or `"Doh"/"Dot"` if a converter is applied globally) | Any artefact carrying the configured transport is unreadable across platforms. **Not covered by the parity gate.** | S |
| **D5.2** | `DNSResponseStatus.rcodeOther` | `DNSResolver.swift:111` | absent (`DNSResolver.cs:92-104`) | see D1.6 — serialised enum, macOS artefacts fail to decode on Linux | S |
| **D5.3** | Two answer-provenance fields | `DNSCapture.swift:21-27` `AttributionConfidence { high, low }` with `CodingKeys` mapping `signingID → "signing ID"`, `isAttributed → "attributed"`; `DenialOfExistenceProbe.swift:33-38` `NegativeKind` | neither symbol exists in `Windows/dotNET` (grep-confirmed) | macOS emits `confidence`/`negativeKind`; the C# readers drop them. D031's "best-effort guess, not a fact" annotation is lost on Linux — the attribution reads as certain. | S |
| **D5.4** | `~/.syxx/lan-snapshot.json` | `LAN/LANCartographer.swift:446-466` — `snapshotURL`, `saveSnapshot` (0600), `loadSnapshot`; header at `:442` states the purpose is cross-run detection | `LAN/LanCartography.cs:441-505` has the `LANSnapshot` type but **no save/load and no path** | Cross-run LAN diffing (a device newly appearing on-link) is impossible on Linux. The type exists; only persistence is missing. | S |
| **D5.5** | License file path | `LicenseManager.swift:76` — `~/.config/syxx/license.json` (XDG) | `LicenseManager.cs:94` — `%AppData%/Syxx/license.json`; on Linux .NET maps `ApplicationData` to `~/.config`, so verify it lands on `~/.config/Syxx/license.json` — **`Syxx` vs `syxx`, case-divergent** | A user moving a home directory between platforms loses the license; also a second file for the same secret. Confirm before changing — a path move needs a migration read. | S |
| **D5.6** | `~/.syxx/monitor-last-change.json` | `Trust/MonitorScheduler.swift:52` `latchedChangeURL`, `:167` `saveLatchedChange`, `:176` `loadLatchedChange`, `:185` `clearLatchedChange`; `:68` reloads the latch in `init` and `:96` never clears it on a later `noChange` | `Trust/MonitorScheduler.cs:42` — `public MonitorTick? LastChange { get; private set; }` is **in-memory only**; `:209 AcknowledgeChange()` just nulls it | **The latch is the feature.** On Linux a Sentinel-detected network-config change is lost the moment the process exits, so the user is never shown the alert they were not present for. On macOS it survives restart until acknowledged. | S |

All six: **required for parity.** D5.4 and D5.6 are behavioural, not merely format.

---

## D6 — Deferred inventory (reserved streams — `Cage`, `Mirror`, `Warp`)

One line each, no detail, so the full-parity picture is visible without this team touching the
files. Every item below was symbol-grep-confirmed.

- `SyxxKit/CageHelpers.swift` (`CageHelpers`, :4) — no C# symbol. `DEFERRED — reserved stream`
- `SyxxKit/CageRedundancyCorrelator.swift` (`CageQueryKey` :25, `CageSeenQuery` :38, `CageRedundancyEvidence` :54, `CageRedundancyVerdict` :110, `CageRedundancyCorrelator` :125) — no C# symbols. `DEFERRED — reserved stream`
- `SyxxKit/PktapHeader.swift` (`PktapHeader` :15) — no C# symbol; Apple pktap capture-header parsing. `DEFERRED — reserved stream` **and** platform-inapplicable (§7)
- `SyxxKit/DNSCapture.swift` — present as `Syxx.Kit/DNSCapture.cs`; `AttributionConfidence` field gap is D5.3. `DEFERRED — reserved stream` for anything further
- `SyxxKit/DNSCaptureReportGenerator.swift` — present as `Syxx.Kit/DNSCaptureReportGenerator.cs`. `DEFERRED — reserved stream`
- `SyxxCLI/Commands/SyxxCage.swift` ↔ `Syxx/Commands/CageCommand.cs` — present. `DEFERRED — reserved stream`
- `SyxxKit/ContentWarp.swift` ↔ `Syxx.Kit/ContentWarp.cs` — present. `DEFERRED — reserved stream`
- `SyxxKit/WarpEngine.swift` ↔ `Syxx.Kit/WarpEngine.cs` — present. `DEFERRED — reserved stream`
- `SyxxKit/WarpManifestPinning.swift` ↔ `Syxx.Kit/WarpManifestPinning.cs` — present. `DEFERRED — reserved stream`
- `SyxxKit/TextDiff.swift` ↔ `Syxx.Kit/TextDiff.cs` — present. `DEFERRED — reserved stream` (Warp/Mirror consumer)
- `SyxxCLI/Commands/SyxxWarp.swift` ↔ `Syxx/Commands/WarpCommand.cs` — present. `DEFERRED — reserved stream`
- `SyxxCLI/Commands/SyxxMirror.swift` ↔ `Syxx/Commands/MirrorCommand.cs` — present. `DEFERRED — reserved stream`
- `~/.syxx/warp_manifest_trusted_keys.txt` (macOS) ↔ `warp_manifest_trusted_keys.txt` (C#) — filename matches; **directory prefix unverified**. `DEFERRED — reserved stream`
- `~/.syxx/captures/` (macOS capture output directory) — no C# path constant found. `DEFERRED — reserved stream`

Note for the reserved-stream owners: **D1.1 (`VantageComparer` CNAME false positive) sits directly
under Mirror's comparison path.** Whoever holds Mirror should be told it exists before they
diagnose a false redirect as a Mirror bug.

---

## 7. Platform-inapplicable — recommend ratifying as permanently OUT

Each of these CANNOT and SHOULD NOT exist on Linux. Ratifying them closes the list; leaving them
unstated leaves permanent phantom gaps in every future parity audit.

| Item | Evidence | Why it is permanently out |
|---|---|---|
| `SMJobBless` / launchd privileged-helper *mechanism* | `DeepSyxx/main.swift:342-354`, `PrivilegeManager.swift:7-19` (`legacySMJobBless`), `com.milkandgin.deepsyxx.plist` | An Apple installation and lifecycle API. Linux's equivalent is PolicyKit + systemd. **The capability is NOT out — see D4. Only the mechanism is.** |
| NSXPC audit-token code-signature validation | `DeepSyxx/main.swift:290-337` (`SecCodeCopyGuestWithAttributes`, `SecRequirementCreateWithString`), `main.swift:50-68` | Depends on `Security.framework` code-signing identity and a Developer-ID OU. Linux has no per-process signing identity to check. The equivalent trust boundary on Linux is the PolicyKit action + the fact that the helper *is* the packaged binary. |
| Developer-ID / notarisation identity (`994XAF8MYH`, `com.milkandgin.syxx.cli`) | `DeepSyxx/main.swift:58, 291` | Apple certificate-chain semantics. Linux's counterpart is the distro package signature — **LANE C** territory, not an engine feature. |
| `NEDNSSettings` / Network Extension entitlements | `SyxxApp/EncryptedDNSConfigurator.swift:122` `isEntitlementError` | An Apple entitlement class. `resolvectl` needs root, not an entitlement; `EncryptedDNSConfigurator.cs:14-16` already models this honestly as `ElevationRequired`. |
| Keychain-specific APIs | `SyxxCLI.swift:250` ("could not be saved to Keychain"), `LicenseManager.swift`, `WitnessKeyManager.swift` | Linux/Windows already have the correct abstraction: `ISecretStore` / `PlatformSecretStore` / `LinuxSecretStore` / `DpapiSecretStore`. The **capability** (secret at rest, owner-only) is present and arguably better factored than macOS. Keychain itself is out. |
| `pktap` / BPF capture headers | `SyxxKit/PktapHeader.swift:15` | `pktap` is a Darwin-only BPF DLT that carries in-flow kernel PID attribution. Linux's equivalent path is `AF_PACKET` + `/proc` socket-inode lookup, which is a different data source, not a port of this struct. The C# side already has its own `Capture/CaptureEngine.cs`. **The capability (process attribution) is in scope; this header is not.** |
| `pfctl` packet-filter anchors | `DeepSyxx/main.swift:134-187`; `/etc/pf.anchors/syxx.unbound*` | Unreferenced on macOS today (no caller outside the protocol). Porting to nftables to satisfy a dead interface is scope creep. **Ratify OUT; revisit only if a caller appears.** |
| `dscacheutil` / `mDNSResponder` specifics | `DeepSyxx/main.swift:104, 122` | The *tools* are Apple-only. **The capability is NOT out — D2.1 requires a `resolvectl`/`nscd` equivalent.** Listed here only so the tool names are not mistaken for the requirement. |
| `NSPanel` / SwiftUI / WKWebView presentation | `SyxxApp/NSPanelManager.swift`, `RemoteContentView.swift`, `Views/Components/**` | Presentation framework. Linux GUI is out of scope by mandate and Avalonia has its own idioms. |

---

## 8. Suggested execution order (Lane D only)

1. **D1.1** — CNAME false-positive redirect (S). Fabricates positives on ordinary traffic today.
2. **D1.2** — DNSSEC zone binding + DNSKEY validation (M). Security bypass.
3. **D1.4 / D1.5** — denial-of-existence ordering and NODATA grading (S). One file, two defects.
4. **D1.3 / D1.6 / D1.6b** — CNAME chain completion; `rcodeOther` + EDNS0 BADVERS; CHAOS QCLASS (S each).
5. **D1.7 / D1.8 / D1.9 / D1.10** — probe-level false positives and negatives (S each).
6. **D5.1–D5.6** — wire and on-disk contracts (S each); D5.6 and D5.4 restore lost behaviour.
7. **D3.1** — telemetry consent withdrawal (S). Privacy-claim integrity.
8. **D1.11 / D1.12** — `ProbeOutcome` / `DimensionOutcome` (M each). **Sequence after Lane B**, which
   is establishing the tri-state vocabulary one layer up.
9. **D2.1 / D2.2** — cache flush, broadcast to engine (S / M).
10. **D4** — PolicyKit privilege model (M). **Blocked on Lane C** for packaging.
11. **D1.14** — `DNSPathConfirmation` port (M). **Blocked on Lane A + D4.**
12. **D3.3** — extend the parity gate so 1–6 cannot regress. **Coordinate with Lane C.**

Items 1–7 are all `S`/one-file changes in `Windows/dotNET/Syxx.Kit/**`. None of them touch a
reserved-stream file, none touch a Lane A/B/C-owned file, and none require a build-system or
packaging change.

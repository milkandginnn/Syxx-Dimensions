# C# field shapes — PARITY_VECTOR_V1

**Track:** `cross-platform-parity_20260805` · **Task:** 1.4 · **Created:** 2026-08-05
**Loader:** `Windows/dotNET/Syxx.Kit/Synthesis/ParityVectorLoader.cs`
**Twin:** `macOS/Sources/SyxxKit/Synthesis/ParityVectorLoader.swift` → `swift-field-shapes.md`

The exact JSON the C# loader accepts for each of the 32 shared `sectionInputs` fields. Written to be
diffed against the Swift twin's document: where the two disagree, the corpus can only load on one
engine, and that is a finding rather than a formatting difference.

## Conventions used below

| Notation | Meaning |
|---|---|
| **req** | Member must be present and non-null. Refusal names the member and its path |
| **opt** | Member may be omitted (omitted → `null`). Present-but-`null` is also a refusal |
| **def `x`** | Member may be omitted and defaults to `x` — used only where the Swift decoder defaults |
| **unrep** | Accepted, type-checked, then carried by nothing (see [Unrepresented members](#unrepresented-members)) |
| **refused** | Present → rejection by name |

Three rules hold everywhere and are not repeated per field:

1. **Requiredness mirrors the Swift model member for member.** A non-optional Swift stored property
   is **req** here; a Swift `T?` is **opt**. This is what makes "loads on one engine" equal "loads on
   both", and it is also what closes the C#-side default hazard: several C# models pre-initialise
   their members (`NetworkStateReport` sets `Vpn = No`, `CaptivePortal`/`EncryptedDNS` = `Unknown`,
   `NetworkKeyState.cs:151-155`), so a partial payload would otherwise inherit a posture nothing
   measured. Requiring what Swift requires means a partial payload is refused instead.
2. **Enum values are the Swift `rawValue` spelling**, never a lowercased C# member name. Four enums
   are not camelCase at all and are tabled explicitly below.
3. **Unknown members are refused at every depth**, naming the member and its path. This is stricter
   than the Swift twin — see [Divergences](#divergences-from-the-swift-twin).

Numbers are JSON integers unless the C# member is `double`. A float where an integer is required
(`1.0`, `1e3`) is a refusal, as is an integer outside the target type's range.

---

## Envelope

```json
{ "schemaVersion": 1, "vectorId": "<filename remainder>", "pinnedUnixSeconds": 1752268477, "sectionInputs": { } }
```

Exactly those four keys. A fifth is refused by name; a missing one is refused by name.
`pinnedUnixSeconds` becomes `DateTimeOffset.FromUnixTimeSeconds(...)` and nothing else.

---

## The 32 fields

### 1. `resolution` — `DNSResponse` (DNSResolver.cs:129)

The one field with a **tolerant** shape on both sides: the Swift model hand-writes
`init(from:)` (DNSResolver.swift:240-261) where only `status` is required. Mirrored exactly,
including every default.

| Member | Shape |
|---|---|
| `status` | **req** — `DNSResponseStatus` (below) |
| `records`, `authorityRecords` | **def `[]`** — array of `DNSRecord` |
| `edns` | **opt** — `EDNSResponseInfo` |
| `ednsMalformed`, `authorityMalformed` | **def `false`** |
| `offTypeAnswerCount`, `offNameAnswerCount` | **def `0`** |
| `responseCode` | **def `0`** — `UInt16` |
| `isAuthoritative`, `isTruncated`, `recursionAvailable`, `authenticatedData` | **def `false`** |
| `errorMessage` | **opt** — string |
| `responseByteLength` | **opt** — int |
| `offClassAnswerCount`, `conflictingCNAMEOwnerCount`, `spoofedPacketCount`, `attemptCount` | **unrep** — int |

`DNSRecord`: `name` **req**, `type` **req** `UInt16`, `ttl` **req** `UInt32`, `rdata` **req**.
`EDNSResponseInfo`: `udpPayloadSize` **req** `UInt16`, `version` **req** `byte`,
`extendedRCode` **req** `byte`, `ecsScopePrefixLength` **opt** `byte`.

`DNSResponseStatus` values (`WitnessWireJson.cs:60-73`, pinned to `DNSResolver.swift:96-107`):
`success`, `noData`, `nxDomain`, `servFail`, `refused`, `timeout`, `truncated`,
`responseIdMismatch`, `malformedResponse`, `transportError`.

> **Asymmetry.** Swift's enum additionally carries `rcodeOther` (used in `DNSResponse.didFail`,
> DNSResolver.swift:268). The C# enum has no such member and the wire table has no such spelling, so
> `"rcodeOther"` is refused here and accepted there. Not currently reachable from any authored case.

### 2. `dnssec` — `DNSSECStatus` (DNSSECVerifier.cs:36)

A bare string: `unsigned`, `recordsPresentUnverified`, `signatureExpiredOrNotYetValid`,
`inconclusive`. Table derived from `DNSSECStatusExtensions.RawValue` (DNSSECVerifier.cs:52-59), which
is pinned to `DNSSECVerifier.swift:39-49`.

### 3–4. `nxCanary`, `dnssecCanary` — `DNSCanaries.CanaryResult` (DNSCanaries.cs:15)

| Member | Shape |
|---|---|
| `section` | **req** — `prophet`\|`mirror`\|`portal`\|`glutton`\|`cage`\|`witness` |
| `status` | **req** — `untested`\|`invalidated`\|`inconclusive`\|`degraded`\|`positive`\|`negative` |
| `evidence` | **opt** — `EvidenceItem` |
| `detail` | **req** — string |

`EvidenceItem` (Conclusion.cs:70): `section` **req**, `evidenceId` **req**, `observation` **req**,
`rationale` **req**, `source` **opt**. `evidenceId` values are the 40-odd camelCase raw values from
`EvidenceIdExtensions.RawValue` (Conclusion.cs:186+) — e.g. `resolverStatus`, `nxCanaryHijack`,
`gatewayDhcpOffLinkDNS`.

> Neither type is `Codable` on the Swift side; its wire shape exists only in the Swift loader's
> `CanaryResultShim` (ParityVectorLoader.swift:472-480). The shapes agree.

### 5. `comparison` — `ComparisonResult` (VantageComparer.cs:23)

| Member | Shape |
|---|---|
| `localServer` | **req** — string |
| `localRecords` | **req** — array of `DNSRecord` |
| `publicResolverResults` | **req** — object, resolver name → array of `DNSRecord` |
| `isRedirectionDetected`, `isInconclusive`, `isDegraded` | **req** — bool |
| `localOutcome` | **req** — `ResolverQueryOutcome` |
| `publicResolverOutcomes` | **req** — object, resolver name → `ResolverQueryOutcome` |
| `dohOutcomes` | **opt** — same shape |
| `result` | **refused** — C#-only |

`ResolverQueryOutcome`: `records` **req**, `status` **req** (`DNSResponseStatus`),
`errorDescription` **opt**. The wire name is `errorDescription`, matching
`ResolverQueryOutcome.errorDescription` on macOS; the C# member is `ErrorMessage` with a
`[JsonPropertyName]` rename (VantageComparer.cs:79-80). The pre-B251 alias `errorMessage`
(VantageComparer.cs:87-91) is **refused** — C#-only.

**At most one of the three booleans may be true.** They are write-only-when-true shims over the
`Result` enum (VantageComparer.cs:39-56), so a payload asserting two would resolve to whichever the
loader happened to assign last. Two or three true is a rejection naming the count.

### 6. `targetDomain` — string. **7. `targetQueryType`** — `UInt16`.

Bare scalars, not objects.

### 8. `warp` — `WarpResult` (ContentWarp.cs:25) — retired display shim

In the shared 32-field set and therefore decoded, but `Synthesizer.Conclude` never reads it (the
Mirror adapter takes `warpManifest`). A vector populating `warp` exercises the loader and nothing
else; it cannot change a conclusion.

All members **req** except `inconclusiveReason` (**opt**):
`targetURL`, `localHTML`, `remoteHTML`, `localScriptURLs`, `remoteScriptURLs`,
`isScriptStructureIdentical`, `localStylesheetURLs`, `remoteStylesheetURLs`,
`isStylesheetStructureIdentical`, `localIframeURLs`, `remoteIframeURLs`,
`isIframeStructureIdentical`, `scriptHashes`, `mismatchedScriptHashes`,
`localInlineScriptHashes`, `remoteInlineScriptHashes`, `isInlineScriptIdentical`,
`isSecure`, `isInconclusive`.

`scriptHashes` is an object, URL → `{ localHash, remoteHash, isMatch }` (all **req**).

> **Spelling hazard.** The wire uses the SWIFT spellings `localHTML` / `remoteHTML`, which are *not*
> what lowercasing the C# member (`LocalHtml`) produces. `targetURL` and the `*URLs` members happen
> to agree.

### 9. `warpManifest` — `WarpManifestResult` (WarpEngine.cs:63)

| Member | Shape |
|---|---|
| `originURL` | **req** — string (C# member `OriginUrl`) |
| `finalURL` | **opt** — string (C# member `FinalUrl`) |
| `keyId` | **opt** — string |
| `signatureState` | **req** — `verified`\|`untrustedSigner`\|`invalidSignature`\|`malformed` |
| `manifestState` | **req** — `parsed`\|`malformed`\|`notEvaluated` |
| `freshnessState` | **req** — `fresh`\|`expired`\|`futureDated`\|`originMismatch`\|`replayNonceMismatch`\|`notEvaluated` |
| `htmlState` | **req** — `match`\|`mismatch`\|`fetchError`\|`fetchFailedDeclared`\|`notEvaluated` |
| `declaredHtmlSha256`, `fetchedHtmlSha256` | **opt** — string |
| `assets` | **req** — array of `WarpAssetResult` |
| `conclusion` | **req** — `TAMPERING_DETECTED`\|`INCONCLUSIVE`\|`VERIFIED_MATCH` |
| `inconclusiveReason` | **opt** — string |
| `rawManifestJSON` | **opt** — string (C# member `RawManifestJson`) |
| `localHtmlBytes` | **opt** — array of integers 0–255 |

`WarpAssetResult`: `url` **req**, `declaredSha256`/`fetchedSha256`/`byteLen` **opt**,
`state` **req** (`WarpContentState`), `assetClass` **req**, `fetchedBytes` **opt**.

> Byte arrays are JSON **arrays of numbers**, not base64: Swift declares `[UInt8]`, and Swift's
> `JSONEncoder` base64-encodes `Data` but not `[UInt8]`. `conclusion` is the only SCREAMING_SNAKE
> enum in the format — those are the WARP protocol tokens (WarpEngine.swift:57-61).
> `originURL`/`finalURL`/`rawManifestJSON` are Swift spellings that lowercasing the C# member would
> not reproduce.

### 10. `network` — `NetworkStateReport` (NetworkKeyState.cs:145)

All members **req** except `gateway` (**opt**).

| Member | Shape |
|---|---|
| `dnsServers`, `activeInterfaces` | array of string |
| `vpnActive`, `captivePortalDetected` | bool |
| `vpn` | `{ state, tunnelInterfaces, evidence }` — all **req** |
| `captivePortal` | `{ state, evidence }` — both **req** |
| `encryptedDNS` | `{ state, evidence, configuredTransport? }` |
| `scopedResolvers` | array of `ScopedResolver` |
| `gateway` | `{ ip, isDNS, adminReachable, dhcpPushedDNS, detail }` — all **req** |

`ConditionState` values: `yes`, `no`, `unknown` (NetworkKeyState.cs:60-64, twin of the Swift
`ConditionState: String`).

`DNSProtocol` values are **`DoH`** and **`DoT`** — *capitalised*, not `doh`/`dot`
(NetworkKeyState.swift:54-57). The C# members are `Doh`/`Dot`, so no naming heuristic reaches the
right spelling here.

`ScopedResolver`: `address` **req**, `interface` **opt**, `matchDomains` **req**, `isScoped` **req**,
`dnsProtocol` **opt** (note: `dnsProtocol`, not `protocol`; the C# member is `Protocol`),
`port` **opt** `UInt16`; `isSupplemental` / `isMDNS` / `isReachable` **unrep**;
`doHReadState` **refused** — C#-only (NetworkKeyState.cs:139).

> **Authoring note.** The C# model's pre-initialised defaults (`Vpn = No`) are unreachable through
> this loader precisely because every member is required. A `network` payload is all-or-nothing.

### 11. `leak` — `LeakTestResult` (LeakDetector.cs:201)

| Member | Shape |
|---|---|
| `result` | **req** — `confirmed`\|`notDetected`\|`inconclusive`\|`invalid`\|`error` |
| `observedResolvers` | **req** — array of string |
| `transportPinning` | **req** — `enforced`\|`notConfigured`\|`failed` |
| `signature`, `publicKey`, `keyId`, `signedPayload`, `errorReason` | **opt** — string |
| `signatureValid`, `signatureKeyTrusted` | **opt** — bool |
| `collectorFailure` | **opt** — see below |
| `signatureVerification` | **refused** — C#-only (E049, LeakDetector.cs:232) |

`collectorFailure` is `{ "kind": <string>, "status": <int>? }`. `kind` is one of
`entitlementRequired`, `authenticationFailed`, `rateLimited`, `serverError`, `unexpectedStatus`,
`malformedResponse`, `collectorIdentityUnverified`, `transportFailure`. The first **five require
`status`** and the last three must omit it — mirroring the Swift enum's associated values
(LeakDetector.swift:54-72, shim at ParityVectorLoader.swift:485-513). The C# type is a class
`{ Kind, HttpStatus }` (LeakDetector.cs:102-116); the wire name is `status`, not `httpStatus`.

### 12. `exfil` — array of `ExfilAssessment` (Forensics/Forensics.cs:300)

All members **req**: `domain`, `longestLabel`, `labelLength` (int),
`entropyBitsPerChar` (double), `beacon`, `beaconEvaluated` (bool), `flaggedByLists` (array of
string), `suspicion`, `rationale` (array of string). `listsEvaluated` is **unrep**.

`BeaconClassification` and `ExfilSuspicion` raw values are **display strings**, not camelCase
(BeaconAnalyzer.swift:18-23, ExfilAnalyzer.swift:9-14):

| Wire | `BeaconClassification` |   | Wire | `ExfilSuspicion` |
|---|---|---|---|---|
| `Insufficient data` | `InsufficientData` |   | `Inconclusive` | `Inconclusive` |
| `Highly regular` | `HighlyRegular` |   | `Benign` | `Benign` |
| `Regular` | `Regular` |   | `Suspicious` | `Suspicious` |
| `Irregular` | `Irregular` |   | `Likely DNS exfiltration / tunneling` | `LikelyExfil` |

### 13. `cageAttributionRate` — double (0…1). A bare number; must be finite.

### 14. `drifts` — **REFUSED in v1**

Any payload is rejected with:

> `drifts is unrepresentable in v1: AnswerDrift carries a date
> (Windows/dotNET/Syxx.Kit/Trust/Trust.cs:33) and PARITY_VECTOR_V1 admits no dates; use
> configChanges for witness vectors`

**This is a live divergence — see [Divergences](#divergences-from-the-swift-twin) item 2.**

### 15. `configChanges` — array of `ConfigChange` (Trust.cs:257)

`{ "kind": <string>, "detail": <string> }`, both **req**. `kind` raw values are display strings
(Sentinel.swift:39-44), identical to the C# `ConfigChangeKindExtensions.Label` table
(Trust.cs:248-254): `Resolvers changed`, `Active interfaces changed`, `VPN state changed`,
`Encrypted-DNS state changed`.

### 16–31. The Workstream H probe records

Every member of every probe record is **req** — each is a non-optional `let` in Swift — except the
five marked **opt** below. Every `status` is a bare Swift-`rawValue` string.

| Field | Type | Members (all **req** unless noted) | `status` values |
|---|---|---|---|
| `latency` | `ResolverLatency` | `sampleCount`, `successCount` int; `minMs`, `medianMs`, `maxMs` double; `detail` | `fast` `moderate` `slow` `degraded` `unreachable` `untested` |
| `stability` | `AnswerStability` | `sampleCount`, `usableCount`, `distinctAnswerSets` int; `ttlHonored` bool; `detail` | `stable` `rotating` `unstable` `ttlAnomaly` `inconclusive` `untested` |
| `wildcardSynthesis` | `WildcardSynthesis` | `probedLabels`, `syntheticAddresses` string[]; `usableCount` int; `detail` | `noSynthesis` `wildcardZone` `nxHijack` `inconclusive` `untested` |
| `denial` | `DenialOfExistence` | `queriedName`; `soaOwner` **opt**; `hasNSEC` bool; `negativeKind` **opt**; `detail` | `notADenial` `nsecProven` `soaBacked` `bareDenial` `contradictory` `inconclusive` `untested` |
| `dualStack` | `DualStackAudit` | `aAddresses`, `aaaaAddresses` string[]; `dns64Count` int; `detail` | `dualStack` `ipv4Only` `dns64Synthesis` `ipv4Suppressed` `inconclusive` `untested` |
| `edns0` | `EDNS0Capability` | `udpPayloadSize` **opt** `UInt16`; `version` **opt** byte; `detail` | `supported` `notSupported` `broken` `inconclusive` `untested` |
| `versionBind` | `VersionBindResult` | `banner` **opt**; `server`; `forwarderOnPublicPath` bool; `detail` | `disclosed` `withheld` `untested` |
| `stubEntropy` | `StubEntropyAudit` | `sampleCount`, `distinctSourcePorts`, `distinctTransactionIDs` int; `detail` | `wellRandomized` `weakRandomization` `insufficientData` `untested` |
| `authoritative` | `AuthoritativeComparison` | `authoritativeAnswers`, `resolverAnswers`, `nameservers` string[]; `detail` | `matches` `diverges` `inconclusive` `untested` |
| `svcb` | `SVCBFinding` | `presentAt`, `absentAt`, `failedAt` string[]; `detail` | `present` `absent` `divergent` `inconclusive` `untested` |
| `ttlAnomaly` | `TTLAnomaly` | `ttlsByResolver` object (resolver → `UInt32`); `detail` | `independentCaching` `lockstepTTL` `extremeTTL` `inconclusive` `untested` |
| `cnameChain` | `CNAMEChainAudit` | `testChain` string[]; `referenceChains` array of string[]; `detail` | `consistent` `noCNAME` `divergent` `inconclusive` `untested` |
| `encryptedTransport` | `EncryptedTransport` | `endpoint`; `detail` | `working` `blocked` `inconclusive` `untested` |
| `openResolver` | `OpenResolverFinding` | `target`; `recursive` bool; `amplificationFactor` double; `detail` | `notRecursive` `recursiveLowAmp` `recursiveHighAmp` `untested` |
| `ecs` | `ECSLeak` | `scopePrefixLength` **opt** byte; `detail` | `forwarded` `notForwarded` `inconclusive` `untested` |
| `responseSize` | `ResponseSizeAudit` | `sampleCount`, `maxBytes`, `oversizedCount` int; `detail` | `normal` `oversized` `insufficientData` `untested` |

`negativeKind` values: `nameDoesNotExist`, `typeHasNoData`.

> `denial.hasNSEC` and `stubEntropy.distinctTransactionIDs` keep their Swift capitalisation; the
> naive first-char-lowercasing of the C# members (`HasNSEC`, `DistinctTransactionIDs`) happens to
> agree, but the loader tables them rather than deriving them.

### 32. `serverGroundTruth` — `GroundTruth` (DimensionClient.cs:16)

**Wire names, not member names** — this is a server contract, and the most likely place for the
twins to disagree:

| Wire key | C# member | Shape |
|---|---|---|
| `name` | `Domain` | **req** string |
| `type` | `Type` | **req** string |
| `conclusion` | `Result` | **req** string (`authoritative` \| `inconclusive`) |
| `authoritative` | `Authoritative` | **opt** `{ answers string[] req, rcode int req, signed bool? opt }` |

Both platforms carry the same two renames (C# `[JsonPropertyName]` at DimensionClient.cs:17-24;
Swift `CodingKeys` at DimensionClient.swift:35-39), so the twins agree here.

---

## Unrepresented members

Eight members the shared wire shape carries that the C# model has nowhere to put. Each is a
non-optional stored property on the Swift twin — so the Swift loader **requires** it and the
canonical corpus therefore writes it — while the C# type has no corresponding member at all.

```
exfil[].listsEvaluated                      Swift ExfilAnalyzer.swift:32 (C082)
network.scopedResolvers[].isMDNS            Swift NetworkKeyState.swift:73 (C113)
network.scopedResolvers[].isReachable       Swift NetworkKeyState.swift:77 (C113)
network.scopedResolvers[].isSupplemental    Swift NetworkKeyState.swift:70 (C113)
resolution.attemptCount                     Swift DNSResolver.swift:194 (C070)
resolution.conflictingCNAMEOwnerCount       Swift DNSResolver.swift:176 (C072)
resolution.offClassAnswerCount              Swift DNSResolver.swift:172 (C057)
resolution.spoofedPacketCount               Swift DNSResolver.swift:190 (C062)
```

They are accepted, type-checked, and then dropped. **That is a drop**, and it is recorded here and
pinned by a test (`UnrepresentedMembersAreExactlyTheKnownEight`) rather than left to be discovered.

Refusing them instead was the first implementation and was reverted for cause:
`corpus/parity-vectors/009_unfetchable-resolution-timeout.json` writes all four `resolution` members
(`attemptCount: 3` among them), so refusal made the canonical corpus unloadable on this engine —
trading a named asymmetry for a corpus neither engine can share.

**The drop is safe today for one reason and only that reason: none of the eight is read by
`Synthesizer.Conclude` on either platform.** The first code on either side that starts reading one
turns this list into a live divergence. That is why the list is asserted, not commented.

The mirror-image case — a member only the *C#* model has — is **refused**, because the Swift twin's
synthesized decoders silently ignore unknown members, so such a vector could only ever mean
different things on the two engines. Those four are `comparison.result`,
`comparison.*Outcomes[].errorMessage`, `leak.signatureVerification`, and
`network.scopedResolvers[].doHReadState`.

---

## Divergences from the Swift twin

Four, in descending order of how much they matter. **None is resolved unilaterally; all four are
raised for the lead.**

### 1. `drifts`: the Swift twin accepts it, this loader refuses it

The Swift loader decodes `[AnswerDrift]` (ParityVectorLoader.swift:410) through the type's
synthesized `Codable`, whose `Date` uses Foundation's default seconds-since-2001 double. This loader
refuses any `drifts` payload as v1-unrepresentable, per the Task 1.4 brief.

Both readings are defensible and they cannot both stand:

- **For refusing:** the spec's own rationale says the format has "no dates … no numbers whose
  formatting varies by runtime". `AnswerDrift.DetectedAt` is a `DateTime`, and the two runtimes do
  not agree on its wire form — Swift writes a seconds-since-2001 `Double`, and the only C# reader for
  that convention is `AppleReferenceDateJsonConverter`, which exists specifically because a naive
  conversion lands on the wrong millisecond about one time in a thousand (WitnessWireJson.cs:148-163).
- **Additional asymmetry, independent of the date:** Swift's `AnswerDrift` carries an
  `id: UUID` (AnswerLedger.swift:39) that the C# type does not have at all (Trust.cs:25-33). A
  Swift-authored `drifts` case must therefore include a member C# cannot hold, and it is not one of
  the inert eight above.
- **Against refusing:** the Witness section's other input is `configChanges`, which is dateless, so
  refusing `drifts` costs the corpus one of Witness's two evidence paths.

**Consequence today:** no `drifts` case can be in a corpus both engines load. The current corpus has
none (007 uses `configChanges`), so nothing is blocked — but Task 1.6's "cover each of the 6
sections" is satisfied for Witness by `configChanges` alone.

**Decision needed:** either the Swift twin refuses `drifts` too, or v1 admits a dateless drift shape
(the `id`/`detectedAt` members omitted), or `drifts` is explicitly recorded as out of v1 scope in the
spec the way `resolverSubstituted` is.

### 2. Unknown nested members: this loader refuses, the Swift twin ignores

Swift's synthesized `Decodable` conformances silently ignore members they do not recognise, so the
Swift loader's unknown-key sweep covers the top level of `sectionInputs` and nothing below it. This
loader refuses an unknown member at any depth, naming it and its path.

That makes the C# side strictly stricter. The practical effect is one-directional and, I think,
benign: a misspelled nested member (`scopePrefixLenght`) is caught here and silently dropped there,
so a corpus that passes C# passes Swift, but not conversely. It is still a divergence in the
contract, and it is the same defect class the spec's rule 3 exists to prevent — one level down.

**Suggestion:** the twin adopts the same rule rather than this loader relaxing, since the relaxed
behaviour is exactly "silently dropping an input", which the spec calls the single most important
rule in the format.

### 3. Duplicate JSON members: this loader refuses, the Swift twin takes last-wins

`System.Text.Json` and `JSONSerialization` both resolve a repeated member by last-wins. This loader
scans for duplicates before building the tree and refuses. Same shape as item 2 — stricter here,
one-directional, and only reachable on a malformed vector.

### 4. `DNSResponseStatus.rcodeOther`

Swift's enum has an eleventh case the C# enum does not (DNSResolver.swift, used at `didFail`). A
vector with `"status": "rcodeOther"` loads on Swift and is refused here. Not reachable from any
authored case; filed so it is not discovered by a red CI job later.

---

## Contract ambiguities encountered (not resolved here)

1. **The spec says "no nesting beyond one level"** (docs/parity-vector-v1-spec.md:14), but 26 of the
   32 fields are objects whose own members are objects or arrays of objects — `network.gateway` and
   `comparison.publicResolverOutcomes.<name>.records[]` are three and five levels deep. Both loaders
   nest freely. The sentence describes the ENVELOPE, not `sectionInputs`; worth amending so a later
   reader does not take it as a constraint that was violated.
2. **The spec fixes the 32 top-level names but says nothing about nested member spellings.** Both
   loaders have independently chosen the Swift spelling (`localHTML`, `originURL`, `dnsProtocol`,
   `errorDescription`), which is consistent with rule "named in lowerCamelCase (the Swift spelling)"
   — but that sentence is scoped to the field set. Worth stating explicitly, because four of those
   spellings are not what lowercasing the C# member produces.
3. **Requiredness is unstated.** Both loaders chose "non-optional Swift property → required". It is
   the right rule and it is now implemented twice; it should be written down before a third reader
   guesses differently.
4. **The layout block shows `002_.../`** (a directory) while the prose says "One case per file".
   Both loaders refuse a directory-shaped entry rather than skipping it. Task 1.6 resolved this in
   favour of flat files; the spec's layout block still shows the other thing.

---

## Rejection messages

Message text is matched to the Swift twin's wherever the same rejection exists on both sides —
verified by real run for the unknown-key case, where the two are byte-identical:

```
parity-transcript: case 002_prophet-dnssec-unsigned.json: unknown sectionInputs key
'resolverSubstituted' — outside the PARITY_VECTOR_V1 shared field set, so it cannot be
expressed in v1 and is refused rather than dropped
```

C#-only rejections (nested unknown member, duplicate member, unrepresentable `drifts`, C#-only
member spelling) have no twin to match and are worded to name the case, the key, and the reason.

Exit code is **3** on every rejection, matching the twin. Rejections go to stderr; the `--list-only`
stdout is written only on success, so a failed load never emits a partial case list.

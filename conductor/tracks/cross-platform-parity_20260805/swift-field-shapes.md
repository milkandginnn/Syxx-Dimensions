# Swift field shapes — PARITY_VECTOR_V1 `sectionInputs`

**Status:** Derived from code · **Created:** 2026-08-05 · **Track:** `cross-platform-parity_20260805` (Task 1.3)
**Authority:** `macOS/Sources/SyxxKit/Synthesis/ParityVectorLoader.swift`
**Contract:** [`docs/parity-vector-v1-spec.md`](../../../docs/parity-vector-v1-spec.md)

What the **Swift** loader accepts, field by field. This is a description of code that exists, not a
proposal. It is written to be diffed against the C# twin's equivalent document: every asymmetry
between the two files is a wire-shape divergence that would otherwise be discovered as a mysterious
engine disagreement, or — worse — never discovered at all because both sides quietly emitted
`untested`.

Where this document says "required", it means the loader **rejects the case** if the member is
absent. There is no defaulting anywhere in this loader.

---

## 1. The envelope

```json
{
  "schemaVersion": 1,
  "vectorId": "empty-section-inputs",
  "pinnedUnixSeconds": 1752268477,
  "sectionInputs": {}
}
```

| Key | Type | Rule |
|---|---|---|
| `schemaVersion` | integer | Must be exactly `1`. Any other value, and any non-integer, is a rejection |
| `vectorId` | string | Must equal the filename remainder after `NNN_` |
| `pinnedUnixSeconds` | integer | Absent, `null`, fractional, or string ⇒ rejection |
| `sectionInputs` | object | Required. `{}` is legal and means every field absent |

All four are required and **no fifth key is permitted** — an unknown top-level key is named and
refused. Filenames are `NNN_<vectorId>.json` (three ASCII digits, underscore, id). A `.json` file
without that prefix is refused rather than skipped; a directory-shaped entry is refused too (see
§6.1). Duplicate `NNN` prefixes are refused because they make case order ambiguous.

---

## 2. The universal shape rule

> **A stored property that is non-optional in Swift is a REQUIRED JSON member.
> A stored property of type `T?` is an OPTIONAL member; absent means `nil`.**

This holds for all 32 fields, with exactly one exception, called out in §3 under `resolution`.

**The rule that trips authors most often:** Swift's *synthesized* `Decodable` conformance does not
run the type's memberwise `init`, so **default values in an `init` do not apply when decoding.**
`ResolverLatency.init` defaults `sampleCount` to `0`, but a `latency` vector omitting
`sampleCount` is **rejected**. Every type below that has a defaulted init is affected:
`ComparisonResult`, `WarpResult`, `ScopedResolver`, `ResolverQueryOutcome`, and all ten probe
result structs.

**Enums** are Swift `String` raw-value enums unless stated otherwise; the JSON value is the raw
string, which is *not always* the Swift case name (§5).

**Absent vs empty array is load-bearing** for `exfil`, `drifts` and `configChanges`. Omitting the
key means "this check never ran" (`untested`); supplying `[]` means "it ran and found nothing"
(a clean datapoint). Getting this backwards fabricates either a coverage claim or a gap. See
`Synthesizer.swift:684-685` (B160) and `:616-620`.

---

## 3. The 32 fields

Hand-decoded types — whose wire shape exists **only** in `ParityVectorLoader.swift` and is
therefore the highest-risk surface for divergence — are marked **[HAND]**.

### Prophet

#### `resolution` — `DNSResponse`

**The one exception to §2.** `DNSResponse` carries a hand-written tolerant decoder
(`DNSResolver.swift:240-261`): **only `status` is required**; every other member defaults on
absence. This is deliberate in the product (persisted responses predate several fields), but it
means a `resolution` vector is the one place where an omitted member is silently defaulted rather
than refused. **Asymmetry risk: high** — if the C# twin requires these members, identical vectors
will load on one platform and fail on the other.

| Member | Type | Required | Default when absent |
|---|---|---|---|
| `status` | `DNSResponseStatus` enum | **yes** | — |
| `records` | array of `DNSRecord` | no | `[]` |
| `authorityRecords` | array of `DNSRecord` | no | `[]` |
| `edns` | `EDNSResponseInfo` | no | `null` |
| `ednsMalformed` | bool | no | `false` |
| `authorityMalformed` | bool | no | `false` |
| `offTypeAnswerCount` | integer | no | `0` |
| `offNameAnswerCount` | integer | no | `0` |
| `offClassAnswerCount` | integer | no | `0` |
| `conflictingCNAMEOwnerCount` | integer | no | `0` |
| `responseCode` | integer (UInt16) | no | `0` |
| `isAuthoritative` | bool | no | `false` |
| `isTruncated` | bool | no | `false` |
| `recursionAvailable` | bool | no | `false` |
| `authenticatedData` | bool | no | `false` |
| `errorMessage` | string | no | `null` |
| `responseByteLength` | integer | no | `null` |
| `spoofedPacketCount` | integer | no | `0` |
| `attemptCount` | integer | no | **`1`**, not `0` |

#### `dnssec` — `DNSSECStatus`

A bare string: `"unsigned"` · `"recordsPresentUnverified"` · `"signatureExpiredOrNotYetValid"` ·
`"inconclusive"`.

#### `nxCanary`, `dnssecCanary` — `DNSCanaries.CanaryResult` **[HAND]**

Not `Codable` in the product; shape defined by `CanaryResultShim`.

| Member | Type | Required |
|---|---|---|
| `section` | `Section` enum | **yes** |
| `status` | `SectionStatus` enum | **yes** |
| `detail` | string | **yes** |
| `evidence` | `EvidenceItem` object | no |

`section` is always `"prophet"` in practice but is **required**, not defaulted — the loader will
not invent it.

#### `latency` — `ResolverLatency`

All required: `status` (enum: `fast`, `moderate`, `slow`, `degraded`, `unreachable`, `untested`),
`sampleCount` int, `successCount` int, `minMs` double, `medianMs` double, `maxMs` double,
`detail` string.

#### `stability` — `AnswerStability`

All required: `status` (`stable`, `rotating`, `unstable`, `ttlAnomaly`, `inconclusive`,
`untested`), `sampleCount` int, `usableCount` int, `distinctAnswerSets` int, `ttlHonored` bool,
`detail` string.

#### `wildcardSynthesis` — `WildcardSynthesis`

All required: `status` (`noSynthesis`, `wildcardZone`, `nxHijack`, `inconclusive`, `untested`),
`probedLabels` array of string, `syntheticAddresses` array of string, `usableCount` int,
`detail` string.

#### `denial` — `DenialOfExistence`

Required: `status` (`notADenial`, `nsecProven`, `soaBacked`, `bareDenial`, `contradictory`,
`inconclusive`, `untested`), `queriedName` string, `hasNSEC` bool, `detail` string.
Optional: `soaOwner` string, `negativeKind` enum (`nameDoesNotExist`, `typeHasNoData`).

#### `dualStack` — `DualStackAudit`

All required: `status` (`dualStack`, `ipv4Only`, `dns64Synthesis`, `ipv4Suppressed`,
`inconclusive`, `untested`), `aAddresses` array of string, `aaaaAddresses` array of string,
`dns64Count` int, `detail` string.

#### `edns0` — `EDNS0Capability`

Required: `status` (`supported`, `notSupported`, `broken`, `inconclusive`, `untested`),
`detail` string. Optional: `udpPayloadSize` int (UInt16), `version` int (UInt8).

#### `versionBind` — `VersionBindResult`

Required: `status` (`disclosed`, `withheld`, `untested`), `server` string,
`forwarderOnPublicPath` bool, `detail` string. Optional: `banner` string.

**Inert unless `forwarderOnPublicPath` is `true` AND `banner` is non-null** — F2 is tilt-only, so
any other combination emits no evidence and no sub-status (`Synthesizer.swift:273`). A vector that
sets only `status` exercises the loader and nothing in the engine.

#### `stubEntropy` — `StubEntropyAudit`

All required: `status` (`wellRandomized`, `weakRandomization`, `insufficientData`, `untested`),
`sampleCount` int, `distinctSourcePorts` int, `distinctTransactionIDs` int, `detail` string.

### Mirror

#### `comparison` — `ComparisonResult`

| Member | Type | Required |
|---|---|---|
| `localServer` | string | **yes** |
| `localRecords` | array of `DNSRecord` | **yes** |
| `publicResolverResults` | object: string → array of `DNSRecord` | **yes** |
| `isRedirectionDetected` | bool | **yes** |
| `isInconclusive` | bool | **yes** |
| `isDegraded` | bool | **yes** |
| `localOutcome` | `ResolverQueryOutcome` | **yes** |
| `publicResolverOutcomes` | object: string → `ResolverQueryOutcome` | **yes** |
| `dohOutcomes` | object: string → `ResolverQueryOutcome` | no |

`localOutcome` and `publicResolverOutcomes` are **derived by the Swift memberwise init but required
on the wire**, per §2. **Asymmetry risk: high** — the natural C# implementation derives them and
would accept a vector that Swift rejects.

#### `targetDomain` — string · `targetQueryType` — integer (UInt16, e.g. `1` for A)

#### `warp` — `WarpResult` — **exercises nothing**

In the shared 32-field set and therefore decoded, but `Synthesizer.conclude` never reads it: the
Mirror adapter takes `warpManifest` (`Synthesizer.swift:750`). `WarpResult` is a retired display
shim (`ContentWarp.swift:19-22`). A vector populating `warp` cannot change a conclusion on either
platform; it only proves the two loaders parse the same bytes.

All required: `targetURL`, `localHTML`, `remoteHTML` (strings); `localScriptURLs`,
`remoteScriptURLs`, `localStylesheetURLs`, `remoteStylesheetURLs`, `localIframeURLs`,
`remoteIframeURLs`, `mismatchedScriptHashes`, `localInlineScriptHashes`,
`remoteInlineScriptHashes` (arrays of string); `isScriptStructureIdentical`,
`isStylesheetStructureIdentical`, `isIframeStructureIdentical`, `isInlineScriptIdentical`,
`isSecure`, `isInconclusive` (bools); `scriptHashes` (object: string → `ScriptHashCheck`).
Optional: `inconclusiveReason` string.

#### `warpManifest` — `WarpManifestResult`

Required: `originURL` string, `signatureState`, `manifestState`, `freshnessState`, `htmlState`
(enums, §5), `assets` array of `WarpAssetResult`, `conclusion` enum.
Optional: `finalURL`, `keyId`, `declaredHtmlSha256`, `fetchedHtmlSha256`, `inconclusiveReason`,
`rawManifestJSON` (strings), `localHtmlBytes` (array of integers 0–255).

`conclusion` uses **SCREAMING_SNAKE** raw values (`"TAMPERING_DETECTED"`, `"INCONCLUSIVE"`,
`"VERIFIED_MATCH"`) while every sibling enum uses lowerCamelCase. This is the §5 hazard most likely
to be typed wrong by hand.

#### `authoritative` — `AuthoritativeComparison`

All required: `status` (`matches`, `diverges`, `inconclusive`, `untested`),
`authoritativeAnswers`, `resolverAnswers`, `nameservers` (arrays of string), `detail` string.

#### `svcb` — `SVCBFinding`

All required: `status` (`present`, `absent`, `divergent`, `inconclusive`, `untested`),
`presentAt`, `absentAt`, `failedAt` (arrays of string), `detail` string.

#### `ttlAnomaly` — `TTLAnomaly`

All required: `status` (`independentCaching`, `lockstepTTL`, `extremeTTL`, `inconclusive`,
`untested`), `ttlsByResolver` (object: string → integer UInt32), `detail` string.

#### `cnameChain` — `CNAMEChainAudit`

All required: `status` (`consistent`, `noCNAME`, `divergent`, `inconclusive`, `untested`),
`testChain` (array of string), `referenceChains` (array of arrays of string), `detail` string.

#### `serverGroundTruth` — `GroundTruth` — **wire names, not Swift names**

`GroundTruth` declares custom `CodingKeys` (`DimensionClient.swift:35-39`) that rename two members
so the server contract stays byte-stable:

| JSON key | Swift property |
|---|---|
| `name` | `domain` |
| `conclusion` | `result` |
| `type` | `type` |
| `authoritative` | `authoritative` |

`name`, `type`, `conclusion` are required; `authoritative` is optional. Nested `Authoritative`:
`answers` (array of string) and `rcode` (integer) required, `signed` (bool) optional.

`result` carries `"authoritative"` or `"inconclusive"`; only `"authoritative"` with a non-empty,
disjoint answer set upgrades a Mirror `.diverges` to server-confirmed (`Synthesizer.swift:373-377`).

**Asymmetry risk: highest single field in the set.** Two renames, both to names that collide with
plausible alternative spellings, on the one type whose wire form is dictated by a third system
(the Dimension server). A C# vector using `domain`/`result` decodes to two absent members here.

### Portal

#### `network` — `NetworkStateReport`

Required: `dnsServers`, `activeInterfaces` (arrays of string), `vpnActive`,
`captivePortalDetected` (bools), `vpn`, `captivePortal`, `encryptedDNS` (objects, §4),
`scopedResolvers` (array of `ScopedResolver`). Optional: `gateway` (`GatewayInfo`).

The two plain booleans and the three typed views are **independent members** — the loader does not
reconcile them. A vector with `vpnActive: false` and `vpn.state: "yes"` loads without complaint.

#### `encryptedTransport` — `EncryptedTransport`

All required: `status` (`working`, `blocked`, `inconclusive`, `untested`), `endpoint` string,
`detail` string.

#### `openResolver` — `OpenResolverFinding` — **advisory only**

All required: `status` (`notRecursive`, `recursiveLowAmp`, `recursiveHighAmp`, `untested`),
`target` string, `recursive` bool, `amplificationFactor` double, `detail` string.

Emits an `Advisory`, never evidence and never a section-status change
(`Synthesizer.swift:539-551`). It cannot move a conclusion; it can only add an advisory.

### Glutton

#### `leak` — `LeakTestResult` **[HAND]**

Neither `LeakTestResult` nor its `LeakResult` / `CollectorTransportPinning` enums are `Codable`;
shape defined by `LeakTestResultShim`.

| Member | Type | Required |
|---|---|---|
| `result` | string: `confirmed` · `notDetected` · `inconclusive` · `invalid` · `error` | **yes** |
| `observedResolvers` | array of string | **yes** |
| `transportPinning` | string: `enforced` · `notConfigured` · `failed` | **yes** |
| `signature` | string | no |
| `publicKey` | string | no |
| `keyId` | string | no |
| `signedPayload` | string | no |
| `signatureValid` | bool | no |
| `errorReason` | string | no |
| `signatureKeyTrusted` | bool | no |
| `collectorFailure` | object, below | no |

An unrecognised `result` or `transportPinning` string is refused by name — it is not coerced to
`error` or `notConfigured`.

**`signature` is load-bearing beyond its own value.** B164 (`Synthesizer.swift:577`): a `result` of
`"inconclusive"` with `signature` absent means the probe never ran (`untested`); with a signature
present it means it ran and did not resolve (`inconclusive`). The same `result` string yields two
different section statuses depending on an *optional* member. Any twin that defaults `signature` to
an empty string instead of null silently converts one into the other.

**`collectorFailure`** — `CollectorFailure` is a Swift enum with associated values, flattened to:

```json
{"kind": "entitlementRequired", "status": 402}
```

`kind` is required. `status` (integer) is **required for** `entitlementRequired`,
`authenticationFailed`, `rateLimited`, `serverError`, `unexpectedStatus`, and **must be omitted or
ignored for** `malformedResponse`, `collectorIdentityUnverified`, `transportFailure`. An unknown
`kind` is refused by name.

#### `ecs` — `ECSLeak`

Required: `status` (`forwarded`, `notForwarded`, `inconclusive`, `untested`), `detail` string.
Optional: `scopePrefixLength` integer (UInt8).

### Cage

#### `exfil` — array of `ExfilAssessment` **[HAND]**

`ExfilAssessment` has only an **internal** memberwise init (`Forensics/ExfilAnalyzer.swift:16-37`),
which is why the loader lives in SyxxKit rather than SyxxCLI. All ten members required:

| Member | Type |
|---|---|
| `domain` | string |
| `longestLabel` | string |
| `labelLength` | integer |
| `entropyBitsPerChar` | double |
| `beacon` | `BeaconClassification` enum (§5 — **space-bearing raw values**) |
| `beaconEvaluated` | bool |
| `flaggedByLists` | array of string |
| `listsEvaluated` | bool |
| `suspicion` | `ExfilSuspicion` enum (§5 — **space-bearing raw values**) |
| `rationale` | array of string |

`[]` means "the capture ran and found nothing suspicious" — a clean Cage datapoint. Omitting the
key means the capture never ran. Not interchangeable (`Synthesizer.swift:616-620`).

#### `responseSize` — `ResponseSizeAudit`

All required: `status` (`normal`, `oversized`, `insufficientData`, `untested`), `sampleCount`,
`maxBytes`, `oversizedCount` (integers), `detail` string.

#### `cageAttributionRate` — double — **inert on its own**

`EvidenceAdapters.cage` returns `.untested` immediately when both `exfil` and `responseSize` are
absent (`Synthesizer.swift:616`), and `attributionRate` is only read *after* that guard. A vector
setting `cageAttributionRate` alone changes nothing. To exercise the C058 degraded-cap, pair it
with `exfil` (`[]` suffices). **This is a live coverage trap: the field looks exercised and is
not.**

Threshold: `< 0.5` forces `degraded` with a disclosing evidence item; `>= 0.5` is an evaluated
negative (`Synthesizer.swift:609`, `:659-669`).

### Witness

#### `drifts` — array of `AnswerDrift` — **contains a `Date` and a `UUID`**

All required: `id`, `domain`, `queryType`, `previousAnswer`, `newAnswer`, `resolver`, `detectedAt`.

- `id` is a **`UUID`**, decoded from its RFC 4122 string form
  (`"E621E1F8-C36C-495A-93FC-0C247A3E6E5F"`). An invalid string is a rejection.
- `detectedAt` is a **`Date`** decoded under `JSONDecoder`'s **default `.deferredToDate`
  strategy**, i.e. a JSON *number* of seconds since the **2001-01-01 reference date** — **not**
  Unix epoch. `978307200` is 2001-01-01T00:00:00Z under this strategy, not 2001; `0` is
  2001-01-01, not 1970.

**This contradicts the contract.** The spec's rationale says the format has "no dates, no numbers
whose formatting varies by runtime" (`docs/parity-vector-v1-spec.md:12-14`), yet `drifts` is in the
shared 32-field set and carries both a date and a UUID. See §7.1.

`[]` means "a baseline existed and nothing drifted"; absent means "no prior baseline" — B160,
`Synthesizer.swift:684-685`.

#### `configChanges` — array of `ConfigChange`

Both members required: `kind` and `detail` (string). `kind` raw values contain **spaces and a
hyphen** (§5).

Same absent-vs-empty rule as `drifts`: `witness` returns `.untested` only when **both** `drifts`
and `configChanges` are absent.

---

## 4. Nested object shapes

| Type | Required | Optional |
|---|---|---|
| `DNSRecord` | `name` string, `type` int (UInt16), `ttl` int (UInt32), `rdata` string | — |
| `ResolverQueryOutcome` | `records` array of `DNSRecord`, `status` `DNSResponseStatus` | `errorDescription` string |
| `EDNSResponseInfo` | `udpPayloadSize` int, `version` int, `extendedRCode` int | `ecsScopePrefixLength` int |
| `EvidenceItem` | `section`, `evidenceId`, `observation`, `rationale` | `source` string |
| `ScriptHashCheck` | `localHash`, `remoteHash` strings, `isMatch` bool | — |
| `WarpAssetResult` | `url` string, `state` `WarpContentState`, `assetClass` string | `declaredSha256`, `fetchedSha256` strings, `byteLen` int, `fetchedBytes` array of int |
| `VPNStatus` | `state` `ConditionState`, `tunnelInterfaces` array of string, `evidence` string | — |
| `CaptivePortalStatus` | `state` `ConditionState`, `evidence` string | — |
| `EncryptedDNSStatus` | `state` `ConditionState`, `evidence` string | `configuredTransport` `DNSProtocol` |
| `ScopedResolver` | `address` string, `matchDomains` array of string, `isScoped`, `isSupplemental`, `isMDNS`, `isReachable` bools | `interface` string, `dnsProtocol` `DNSProtocol`, `port` int |
| `GatewayInfo` | `ip` string, `isDNS` bool, `adminReachable` `ConditionState`, `dhcpPushedDNS` array of string, `detail` string | — |
| `GroundTruth.Authoritative` | `answers` array of string, `rcode` int | `signed` bool |

**`EvidenceItem.source` does not self-populate on decode.** Built in code, the memberwise init
derives `source` from `EvidenceItem.correlationGroup(for:)` (`Conclusion.swift:79`). Decoded from a
vector, the *synthesized* decoder runs instead and `source` is exactly what the JSON said —
`null` when omitted. An evidence item that would carry `"nxdomain_hijack"` in a live run carries
`null` when it arrives through a vector.

---

## 5. Enum raw values

Values that differ from the Swift case name, or that contain characters easy to mistype, are
**bolded**. These are the strings a C# `enum` mapping must reproduce exactly.

| Enum | Values |
|---|---|
| `DNSResponseStatus` | `success`, `noData`, `nxDomain`, `servFail`, `refused`, `timeout`, `truncated`, `responseIdMismatch`, `malformedResponse`, `transportError`, `rcodeOther` |
| `DNSSECStatus` | `unsigned`, `recordsPresentUnverified`, `signatureExpiredOrNotYetValid`, `inconclusive` |
| `Section` | `prophet`, `mirror`, `portal`, `glutton`, `cage`, `witness` |
| `SectionStatus` | `untested`, `invalidated`, `inconclusive`, `degraded`, `positive`, `negative` |
| `EvidenceId` | `resolverStatus`, `dnssecExpired`, `nxCanaryClean`, `nxCanaryHijack`, `nxCanaryAtypical`, `dnssecCanarySecure`, `dnssecCanaryStripped`, `dnssecCanaryExpired`, `redirection`, `scriptMismatch`, `captivePortalActive`, `dnsUnencrypted`, `dnsEncrypted`, `dnsLeakConfirmed`, `exfilLikely`, `exfilSuspicious`, `answerDrift`, `configChange`, `authoritativeDiverges`, `svcbStripped`, `answerUnstable`, `encryptedDnsBlocked`, `nxSubdomainHijack`, `denialBare`, `denialInjected`, `ipv4Suppressed`, `ttlLockstep`, `ttlExtreme`, `cnameChainDivergent`, `ednsBlocked`, `ecsSubnetLeak`, `resolverBannerAnomaly`, `weakStubEntropy`, `responseSizeAnomaly`, `authoritativeDivergesConfirmed`, `gatewayDhcpOffLinkDNS`, `attributionIncomplete` |
| `ConditionState` | `yes`, `no`, `unknown` |
| `DNSProtocol` | **`DoH`**, **`DoT`** (uppercase, not the case names `doh`/`dot`) |
| `WarpSignatureState` | `verified`, `untrustedSigner`, `invalidSignature`, `malformed` |
| `WarpManifestState` | `parsed`, `malformed`, `notEvaluated` |
| `WarpFreshnessState` | `fresh`, `expired`, `futureDated`, `originMismatch`, `replayNonceMismatch`, `notEvaluated` |
| `WarpContentState` | `match`, `mismatch`, `fetchError`, `fetchFailedDeclared`, `notEvaluated` |
| `WarpConclusion` | **`TAMPERING_DETECTED`**, **`INCONCLUSIVE`**, **`VERIFIED_MATCH`** |
| `LeakResult` **[HAND]** | `confirmed`, `notDetected`, `inconclusive`, `invalid`, `error` |
| `CollectorTransportPinning` **[HAND]** | `enforced`, `notConfigured`, `failed` |
| `ExfilSuspicion` | **`Inconclusive`**, **`Benign`**, **`Suspicious`**, **`Likely DNS exfiltration / tunneling`** |
| `BeaconClassification` | **`Insufficient data`**, **`Highly regular`**, **`Regular`**, **`Irregular`** |
| `ConfigChangeKind` | **`Resolvers changed`**, **`Active interfaces changed`**, **`VPN state changed`**, **`Encrypted-DNS state changed`** |
| `DenialOfExistence.NegativeKind` | `nameDoesNotExist`, `typeHasNoData` |

Per-probe `Status` enums are listed inline with their field in §3.

---

## 6. Rejection behaviour

Every rejection names the case file; where a key is implicated it names the key. There is no
partial success — the first rejection throws and nothing is returned.

### 6.1 Beyond the spec's five rules

The spec's "Rejection behavior — summary" lists five rules. The Swift loader enforces four more,
because each closes a way a case could vanish quietly. **These are candidates for the C# twin to
mirror, and each is an asymmetry until it does.**

| Rejection | Why it is not a skip |
|---|---|
| Unreadable directory | Must not surface as an empty case list |
| Empty directory (zero cases) | An empty corpus rendering as a clean parity run is the exact defect this track exists to close |
| Duplicate `NNN` sort prefix, naming both files | Case order would be ambiguous, so the two runtimes could legitimately disagree |
| A `.json` file not matching `^\d\d\d_`, or a directory-shaped entry | A case someone believed was in the corpus |

Rejecting duplicate prefixes has a useful consequence: case order is then fully determined by three
ASCII digits, so it is **collation-independent** — no locale-aware vs ordinal sort question can
arise between the runtimes.

### 6.2 Determinism of the message

When a case has several problems, the reported one is deterministic: unknown top-level keys first
(lowest name by ordinal sort), then missing top-level keys, then `schemaVersion`, then `vectorId`,
then `pinnedUnixSeconds`, then `sectionInputs` — and inside `sectionInputs`, all unknown keys are
swept before any value is decoded, reporting the lowest name. A C# twin that reports a different
problem first will produce a spurious diff on a case that both correctly refuse.

---

## 7. What v1 cannot express

### 7.1 Contradictions between this shape set and the spec's premises

Recorded, not resolved — resolving them is the lead's call.

1. **`drifts` carries a `Date` and a `UUID`** while the spec's rationale says the format has "no
   dates" and "no numbers whose formatting varies by runtime"
   (`docs/parity-vector-v1-spec.md:12-14`). Under `.deferredToDate`, `detectedAt` is seconds since
   **2001-01-01**, which is exactly the runtime-specific numeric convention the spec set out to
   avoid. Any C# twin will almost certainly use Unix epoch or ISO-8601. **Highest-probability
   silent divergence in the whole set.** Options: pin an explicit `dateDecodingStrategy` on both
   sides, or drop `drifts` from v1.
2. **"No nesting beyond one level"** (`:13`) does not hold for the field values. `network` nests
   four levels (`network.gateway.adminReachable`), `comparison.publicResolverResults` is an object
   of arrays of objects, `warpManifest.assets[].fetchedBytes` is an array of integers. The
   one-level claim describes the *envelope*, not `sectionInputs`.
3. **File-per-case vs directory-per-case.** The spec's prose says "One case per file"; its layout
   block shows `002_.../` with a trailing slash; plan Task 1.6 says to "mirror the
   `warp-manifest-vectors` per-case **directory** layout". The Swift loader implements
   file-per-case and refuses a directory-shaped entry loudly, so the disagreement surfaces at the
   moment it would otherwise cost a case rather than silently.

### 7.2 Fields that exist in Swift and cannot be expressed

`resolverSubstituted` (`Synthesizer.swift:82`) and `transportObservation` (`:67`) are Swift-only
and **outside** the 32-field set. Naming either in a vector is a rejection by name — never a
partial accept. `resolverSubstituted` is the specific case the spec calls out, and it is the one
with teeth: it caps Mirror at `degraded` (`Synthesizer.swift:772-777`), so a loader that accepted
it on one platform and ignored it on the other would produce a divergence blamed on the engine.

### 7.3 Fields in the set that exercise nothing

Three fields decode but cannot change a conclusion. A corpus can carry them and gain no coverage —
they must not be counted as exercising the engine.

| Field | Why inert |
|---|---|
| `warp` | Never read by `Synthesizer.conclude`; the Mirror adapter reads `warpManifest` |
| `cageAttributionRate` alone | Guarded out unless `exfil` or `responseSize` is also present |
| `versionBind` unless `forwarderOnPublicPath && banner != null` | F2 is tilt-only; no sub-status in any other combination |

`openResolver` is a fourth, partial case: it can add an `Advisory` but never changes a section
status or the determination.

---

## 8. Asymmetry watch-list

Ordered by the probability that the twins disagree without anyone noticing. Each is a specific
thing to check in the C# document, not a general concern.

1. **`drifts.detectedAt` date convention** — reference-date seconds here (§7.1.1).
2. **`serverGroundTruth` wire renames** — `name`/`conclusion`, not `domain`/`result` (§3).
3. **`resolution` is the sole tolerant decoder** — only `status` required; every other member
   defaults. If C# requires them, identical vectors load on one platform only (§3).
4. **`comparison.localOutcome` / `publicResolverOutcomes` required despite being derivable** (§3).
5. **Init defaults do not apply on decode** anywhere else either — ten probe structs, `WarpResult`,
   `ScopedResolver`, `ResolverQueryOutcome` (§2).
6. **`leak.signature` null-vs-empty** flips an `inconclusive` between `untested` and
   `inconclusive` (§3, B164).
7. **Space-bearing enum raw values** — `ExfilSuspicion`, `BeaconClassification`,
   `ConfigChangeKind`; and `WarpConclusion`'s SCREAMING_SNAKE; and `DNSProtocol`'s uppercase (§5).
8. **Absent vs `[]`** for `exfil`, `drifts`, `configChanges` — the difference between "did not run"
   and "ran clean" (§2).
9. **`EvidenceItem.source` does not self-derive on decode** (§4).
10. **The four extra rejections** in §6.1, and the message-precedence order in §6.2.
11. **`resolution.attemptCount` defaults to `1`, not `0`** — the only non-zero default (§3).

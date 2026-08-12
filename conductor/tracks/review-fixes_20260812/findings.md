# Recovered /code-review xhigh findings — session f93531fc (2026-08-11/12)

Recovered verbatim from the session transcript. Three runs, all level=xhigh, 15 CONFIRMED findings each.

## Run 1 — Windows/.NET (Syxx.Kit) — 2026-08-11 19:42 local

ReportFindings timestamp (UTC): 2026-08-12T00:42:05.617Z — level: xhigh — findings: 15

### 1. DNSSEC canary success hard-excludes spoofing diagnoses

- **Location:** `Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:326`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** New mapping `DnssecCanarySecure → TrustedWitnessOK (0.75)` makes a DNSSEC control-canary success hard-exclude the `dnsSpoofing` and `dnsInterception` diagnoses, because TrustedWitnessOK is listed in both diagnoses' `exclusionaryIndicators` and 0.75 clears MatrixDeterminativeEngine's `ExclusionFiringThreshold` (0.5) and `ExclusionQualityFloor` (0.5).
- **Failure scenario:** DNSCanaries.DnssecControlAsync (DNSCanaries.cs:90) probes the *control* domain cloudflare.com, not the target. On a network where an on-path attacker spoofs answers for the user's target domain but leaves cloudflare.com's DNSSEC intact (trivial — the attacker only rewrites the names it cares about), Prophet emits DnssecCanarySecure → TrustedWitnessOK 0.75 → MatrixDeterminativeEngine.CheckHardExclusion sets `r.HardExclusion` on both dnsSpoofing and dnsInterception. Both threats are suppressed with reason HardExcluded no matter how strongly AuthoritativeMismatch or SignedAnswerMismatch fired, so the named threat never reaches the user; combined with ReportCards.For (which ignores HardExcluded suppressions when computing `hasThreatOverhang`) the run is reported as Grade B, "This network looks clean", Severity.Clean. Before this change TrustedWitnessOK had no EvidenceId mapping and was never observed, so the hard exclusion could not fire.

### 2. Inconclusive graded B/Clean with "looks clean" headline

- **Location:** `Windows/dotNET/Syxx.Kit/Synthesis/ReportCard.cs:88`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The B310 branch grades `DeterminativeState.Inconclusive` as Grade.B with Severity.Clean and the headline "This network looks clean…", directly contradicting this file's own HONESTY CONTRACT header (lines 16-19: "`inconclusive` … therefore returns `Ungraded`"); the `hasThreatOverhang` guard that is supposed to protect this path skips every suppression whose Reason is HardExcluded, which is exactly how the engine suppresses confirmed-threat diagnoses.
- **Failure scenario:** A run in which dnsSpoofing is suppressed with SuppressionReason.HardExcluded (e.g. via the new DnssecCanarySecure → TrustedWitnessOK mapping) reaches MatrixDeterminativeEngine with `clean` blocked by the overhang, so the determination is Inconclusive. In ReportCards.For, `hasThreatOverhang` filters `s.Reason != HardExcluded`, so the suppressed spoofing signal is invisible and the guard evaluates false. The card returned is (Grade.B, "B", "This network looks clean, but confidence is limited…", Severity.Clean) with the reason line "the checks that ran found no signs of tampering or leakage" — a green letter grade and a clean-severity colour rendered over a suppressed tampering signal. The same path also fires when every section timed out (no Negative statuses at all), producing the same "found no signs of tampering" claim from zero successful measurements.

### 3. REFUSED/censored resolver mapped to clean support

- **Location:** `Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:296`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** New mapping `ResolverStatus → ResolverAgreement (0.80)` turns a block/censorship signal into affirmative clean-support: Synthesizer.cs:185 emits EvidenceId.ResolverStatus ONLY when the resolver returned NXDOMAIN / REFUSED / SERVFAIL, and ResolverAgreement is a Supports rule (Weight 1.00, EvidenceClass.Major) of the `clean` diagnosis.
- **Failure scenario:** On a censoring network the local resolver answers REFUSED (or SERVFAIL) for the target domain. Synthesizer.Prophet emits ResolverStatus with the observation "Resolver returned Refused for the target domain … can be a block" and marks the sub-status Inconclusive. MapEvidenceToIndicators then feeds ResolverAgreement at strength 0.80 into the matrix engine, where it is a Major supporting observation for `clean` — so the very evidence that the domain was blocked pushes the engine toward a Clean determination and counts toward the `MajorCount >= 2` requirement for a Conclusive clean. The user is shown a clean/limited-confidence-clean result for a network that refused to resolve the domain, violating the "missing data is not clean data" rule.

### 4. Empty DoH set returns Clean; Swift twin returns tampered

- **Location:** `Windows/dotNET/Syxx.Kit/VantageComparer.cs:453`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** DetectUnanimousTampering's rewrite returns UnanimousTamperingResult.Clean when dohOutcomes is null/empty and when fewer than 2 DoH endpoints returned records, while the Swift twin it claims parity with (VantageComparer.swift:333, :340) returns .tampered in both of those cases.
- **Failure scenario:** On a network where the attacker also blocks DoH (or the run simply has <2 DoH endpoints with records), all UDP public resolvers unanimously return the attacker's address and the local resolver matches. macOS returns .tampered, Evaluate sets redirection = true and the user is told DNS redirection was detected. Windows/Linux hit VantageComparer.cs:453/460 instead, return Clean, and Evaluate leaves redirection = false — the same capture yields 'no redirection' on one platform and 'redirection detected' on the other. UnanimousTamperingResult.Undetermined is never returned on either path, so `inconclusiveFromUnanimous` (VantageComparer.cs:311) is dead and the documented tri-state honesty fallback can never fire.

### 5. Whitespace pin file shadows env pins, yields empty set

- **Location:** `Windows/dotNET/Syxx.Kit/CollectorPinning.cs:41`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** `ConfiguredPins()` now prefers the persisted pin file and gates the env-var fallback on `string.IsNullOrEmpty(raw)`, so a whitespace-only pin file shadows `SYXX_COLLECTOR_PINS` and silently yields an empty pin set — which `Accept()` treats as "unpinned, accept anything".
- **Failure scenario:** An operator deploys with `SYXX_COLLECTOR_PINS` set, and `%LocalAppData%/Syxx/collector_spki_pins.txt` exists but holds only a newline (left behind by a `type nul >` / cleared-pins step). `PersistedCollectorPins()` returns "\n", `IsNullOrEmpty("\n")` is false, so the env var is never read; `Split(..., RemoveEmptyEntries)` produces zero parts and `ConfiguredPins()` returns an empty `HashSet`. `CollectorPinning.Accept(chainOk, leafPin, pins)` then returns true for any chain-valid certificate, and the leak result is reported with `TransportPinning = NotConfigured` instead of `Failed` — an on-path attacker with a CA-trusted cert collects the leak test and the run renders as a normal (unpinned) result rather than refusing fail-closed. `IsNullOrWhiteSpace` is the correct guard.

### 6. Manifest key file read before env var, shadowing it

- **Location:** `Windows/dotNET/Syxx.Kit/WarpManifestPinning.cs:41`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** `ConfiguredTrustedManifestKeys()` was inverted to read the persisted file FIRST and the `SYXX_WARP_MANIFEST_KEYS` environment variable only as a fallback, while the XML doc comment three lines above still documents the opposite order ("Sources, in order: the SYXX_WARP_MANIFEST_KEYS environment variable, else a persisted key file"). A stale on-disk pin file now permanently shadows an operator-supplied env-var pin.
- **Failure scenario:** An operator rotates a compromised Warp manifest signing key by setting SYXX_WARP_MANIFEST_KEYS to the new key. Because %LocalAppData%/Syxx/warp_manifest_trusted_keys.txt still exists from the previous install, `PersistedTrustedManifestKeys()` returns non-empty and the env var is never read. Every manifest signed with the new key verifies against a key that is not in the pinned map, so WarpEngine reports UntrustedSigner → INCONCLUSIVE for every origin, and Mirror's content-integrity check never produces a VERIFIED_MATCH again until the file is manually deleted. The user sees a permanently inconclusive Warp section with no indication that the pin source was overridden.

### 7. DPAPI keystore root moved with no legacy read fallback

- **Location:** `Windows/dotNET/Syxx.Kit/DpapiSecretStore.cs:51`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The DPAPI store's keystore root moved from Roaming ApplicationData to LocalApplicationData with no legacy-path read fallback, so Retrieve/Exists route to a directory no prior build ever wrote — even though the sibling E043 entropy change kept a legacy fallback for exactly this migration.
- **Failure scenario:** A Windows user upgrades. Their witness Ed25519 secret and licence key sit in %APPDATA%\Syxx\keystore (written by the previous build). GetStorePath now resolves under %LOCALAPPDATA%\Syxx\keystore, so Retrieve returns null and Exists("witness") enumerates an empty/absent directory and returns SecretExistence.Absent (not Unknown). WitnessKeyStore.AbsentUnlessStoreHoldsWitnessSecret takes the Absent arm — the one arm documented as "safe to mint" — and mints a second identity over the still-present old one; every attestation the user sealed before the upgrade now verifies against a different public key, with a successful-looking seal on screen. LicenseManager.GetStoredLicenseKey() likewise returns null, so a paid user's Pro features re-lock with no error explaining why.

### 8. IPv6 non-initial fragments parsed as UDP/DNS records

- **Location:** `Windows/dotNET/Syxx.Kit/DNSCapture.cs:323`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The new IPv6 extension-header walk (B315) steps over a Fragment header (next-header 44) and then parses the following bytes as a UDP header, so non-initial IPv6 fragments are decoded into fabricated ports and DNS records — the exact case the sibling IPv4 guard added in the same change (D146, DNSCapture.cs:269-274, "parsing it as a transport header fabricates evidence") rejects.
- **Failure scenario:** A captured IPv6 packet carries a Fragment header with Fragment Offset = 185 and Next Header = 17 (the original upper-layer protocol is preserved in every fragment). The `case 44:` arm reads `nextHeader = data[extOffset]` (= 17), advances `extOffset += 8`, and exits the loop with `nextHeader == 17`. `udpStart = extOffset` then reads four arbitrary payload-continuation bytes as source/destination ports and the next bytes as a DNS header, so Cage emits a DNSCaptureRecord with an invented query name/rcode. The IPv4 path returns null for the same shape.

### 9. Any RCODE disagreement graded as confirmed tampering

- **Location:** `Windows/dotNET/Syxx.Kit/VantageComparer.cs:451`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** `if (udpStatuses.Count > 1) return UnanimousTamperingResult.Tampered;` treats any RCODE disagreement among public resolvers as confirmed tampering, but `answeredOutcomes` is filtered with `ResolverQueryOutcome.DidFail`, which does NOT include NXDomain, Refused, ServFail or NoData — so ordinary per-resolver RCODE variation is graded as an on-path attack with no DoH cross-validation at all.
- **Failure scenario:** Twelve public resolvers are queried. Ten return NoError with identical A records (so `udpSets` is unanimous and passes the SetEquals gate); one returns SERVFAIL (DNSSEC-validating resolver on a temporarily bogus zone) and one returns REFUSED (ACL/rate-limit). Neither status is in ResolverQueryOutcome.DidFail, so both stay in `answeredOutcomes`, `udpStatuses` = {Success, ServFail, Refused}, count 3 > 1, and the method returns Tampered. Evaluate sets `redirection = true`, the Mirror/Prophet surface renders "DNS Redirection Detected" as a Positive finding, and the user is told their network is tampering with DNS on the basis of two benign resolver refusals — a fabricated accusation.

### 10. Same-provider CDN divergence hard-excludes clean

- **Location:** `Windows/dotNET/Syxx.Kit/AuthoritativeVantage.cs:225`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** K-16 changed the same-provider disjoint case from `Inconclusive` to `Diverges`, which makes routine CDN/geo routing emit EvidenceId.AuthoritativeDiverges → SyxxIndicator.AuthoritativeMismatch at 0.70 — above the 0.5 ExclusionFiringThreshold — and AuthoritativeMismatch is an `exclusionaryIndicator` of the `clean` diagnosis as well as a Decisive Supports rule of `dnsSpoofing`.
- **Failure scenario:** A user scans an ordinary CDN-fronted domain (the first-run default is a CDN-fronted host) on a healthy network. The recursive resolver returns one set of Cloudflare/Akamai addresses and the authoritative servers return a disjoint set from the app's vantage — both inside the same provider's ranges, the normal shape for anycast/geo routing. Classify now returns Diverges instead of Inconclusive, Synthesizer marks Mirror Positive with "Recursive answer diverges from the domain's authoritative nameservers", and the matrix engine hard-excludes `clean` (AuthoritativeMismatch 0.70 ≥ 0.5) while feeding a Decisive-class support rule of dnsSpoofing. The user on a clean network is shown a divergence finding and can never receive a clean determination for any CDN-hosted domain.

### 11. CLI Warp call sites never pass the replay nonce

- **Location:** `Windows/dotNET/Syxx.Kit/WarpEngine.cs:577`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** RunVantageCheckAsync gained a `nonce` parameter defaulted to null and ProbePlan.cs:164-166 was updated to pass one, but the two CLI call sites (Windows/dotNET/Syxx/Program.cs:701 and Windows/dotNET/Syxx/Commands/WarpCommand.cs:48) were not, and EvaluateFreshness skips the replay check entirely when clientNonce is null.
- **Failure scenario:** A user runs `syxx warp https://example.com`. WarpCommand.cs:48 calls RunVantageCheckAsync(origin, base, licenseKey) with nonce defaulting to null, so DimensionClient.WarpManifestAsync requests a manifest with no nonce and WarpEngine.cs:491 (`if (clientNonce != null && ...)`) never compares manifestNonce. An on-path attacker who captured a valid signed manifest for that origin can replay it inside the 600 s freshness window; the CLI verifies the Ed25519 signature, finds the origin matches, and prints VERIFIED_MATCH for content the server signed at a different moment. The same command on macOS (SyxxWarp.swift:37) and the same check run from the GUI (ProbePlan.cs:164) do pass a nonce and would report ReplayNonceMismatch.

### 12. WaitForExit bound not applied; pipe reads can deadlock

- **Location:** `Windows/dotNET/Syxx.Kit/LinuxSecretStore.SecretTool.cs:125`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** SecretTool.Exists reads stdout and then stderr to end BEFORE calling WaitForExit(5000), so the 5-second bound is never actually applied to a hung child process; the sequential ReadToEnd can also deadlock when the process fills the other pipe.
- **Failure scenario:** On a Linux desktop where `secret-tool search --all` blocks on a keyring-unlock prompt (or a wedged D-Bus Secret Service), `proc.StandardOutput.ReadToEnd()` blocks forever. Control never reaches the WaitForExit(5000) guard on the next line, so the documented "never throws, returns Unknown on timeout" contract of ISecretStore.Exists is not honoured. WitnessKeyStore.LoadKey -> SecretStore.Exists is on the app-startup path, so the GUI/CLI hangs indefinitely at launch with no conclusion and no error rather than returning SecretExistence.Unknown and continuing. Separately, if secret-tool writes more than the pipe buffer to stderr while stdout is still being drained, the child blocks on stderr and the parent blocks on stdout — the same permanent hang without any external trigger.

### 13. Teredo/6to4 now silently rejected by public-IP gates

- **Location:** `Windows/dotNET/Syxx.Kit/IPClassifier.cs:118`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** B304 moves Teredo (2001::/32) and 6to4 (2002::/16) out of `PublicRoutable` into new classes, but every SSRF/public-address gate in the Kit is an allow-list written as `== IPAddressClass.PublicRoutable` (DimensionClient.cs:200/211, AuthoritativeVantage.cs:133, FilteringProbe.cs:44), so these addresses are now silently rejected rather than merely annotated as the comment claims ("non-blocking but flagged for diagnostic context").
- **Failure scenario:** A Dimension/Warp origin (or a domain's authoritative nameserver) resolves only to a 6to4 or Teredo IPv6 address. `DimensionClient.ResolveValidatedPublicIpAsync` classifies it as SixToFour/Teredo, fails the `!= PublicRoutable` check and returns null; WarpEngine's ConnectCallback then throws IOException("SSRF protection: host does not resolve to a public IP address") and the manifest fetch fails. The user sees the Warp/Mirror check report INCONCLUSIVE (fetch error) for a reachable host, and AuthoritativeVantage drops those nameservers from `nsIPs`, so with fewer than two authoritative responders the comparison degrades to "could not obtain authoritative ground truth" instead of a real answer.

### 14. DNSSEC validity span differs across C#, Swift, Python

- **Location:** `Windows/dotNET/Syxx.Kit/DNSSECVerifier.cs:192`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** MaxPlausibleValiditySpanSeconds was changed from 63_072_000 (2 years) to 94_608_000 (3 years), but the Swift twin at macOS/Sources/SyxxKit/DNSSECVerifier.swift:185 is 157_680_000 (5 years) and Server/syxx_server/resolver.py:118 is still 63_072_000 — three different values for the same honesty-critical plausibility gate.
- **Failure scenario:** A ccTLD publishes an RRSIG with a 4-year validity window. RRSIGValidityWindow accepts it on macOS (span <= 157_680_000) and the user sees a valid DNSSEC canary; on Windows and Linux the same record fails the 94_608_000 span check, the window is discarded, and Classify falls through to DNSSECStatus.Bogus/SignatureExpiredOrNotYetValid — the same domain on the same network reports a DNSSEC problem on Windows and a clean canary on macOS, and the server's own view disagrees with both.

### 15. Trial HMAC key change invalidates prior signatures

- **Location:** `Windows/dotNET/Syxx.Kit/LicenseManager.cs:296`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** E205 changed the trial HMAC key from `GetSystemFingerprint()` to `fp.Value + ":" + installSalt`, but both verification candidates (`expectedSig` and `legacySig`) are computed with the new key, so no path can validate a signature written by any prior build.
- **Failure scenario:** An existing installation has `trialState` signed with the old key (the machine fingerprint alone). After upgrading, `state.Signature` matches neither `expectedSig` nor `legacySig` — the `legacySig` fallback only varies the MaxSeenMillis field, not the key — so the record is discarded, `trial.signature.mismatch` is logged as a "Windows reinstall", and the offline branch re-anchors `finalLaunchMillis` to now. Every existing trial user silently gets a fresh trial window on upgrade; the same path is an intentional bypass (delete the install salt from the secret store).

## Run 2 — Windows/.NET (Syxx.Kit), second pass — 2026-08-12 01:55 local

ReportFindings timestamp (UTC): 2026-08-12T06:55:25.507Z — level: xhigh — findings: 15

### 1. Inconclusive graded B/Clean with "looks clean" headline

- **Location:** `Windows/dotNET/Syxx.Kit/Synthesis/ReportCard.cs:88`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The B310 branch grades `DeterminativeState.Inconclusive` as Grade.B with Severity.Clean and the headline "This network looks clean…", directly contradicting this file's own HONESTY CONTRACT header (lines 16-19: "`inconclusive` … therefore returns `Ungraded`"); the `hasThreatOverhang` guard that is supposed to protect this path skips every suppression whose Reason is HardExcluded, which is exactly how the engine suppresses confirmed-threat diagnoses. [same root cause also at ReportCard.cs:83]
- **Failure scenario:** A run in which dnsSpoofing is suppressed with SuppressionReason.HardExcluded (e.g. via the new DnssecCanarySecure → TrustedWitnessOK mapping) reaches MatrixDeterminativeEngine with `clean` blocked by the overhang, so the determination is Inconclusive. In ReportCards.For, `hasThreatOverhang` filters `s.Reason != HardExcluded`, so the suppressed spoofing signal is invisible and the guard evaluates false. The card returned is (Grade.B, "B", "This network looks clean, but confidence is limited…", Severity.Clean) with the reason line "the checks that ran found no signs of tampering or leakage" — a green letter grade and a clean-severity colour rendered over a suppressed tampering signal. The same path also fires when every section timed out (no Negative statuses at all), producing the same "found no signs of tampering" claim from zero successful measurements.

### 2. DNSSEC canary success hard-excludes spoofing diagnoses

- **Location:** `Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:326`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** New mapping `DnssecCanarySecure → TrustedWitnessOK (0.75)` makes a DNSSEC control-canary success hard-exclude the `dnsSpoofing` and `dnsInterception` diagnoses, because TrustedWitnessOK is listed in both diagnoses' `exclusionaryIndicators` and 0.75 clears MatrixDeterminativeEngine's `ExclusionFiringThreshold` (0.5) and `ExclusionQualityFloor` (0.5).
- **Failure scenario:** DNSCanaries.DnssecControlAsync (DNSCanaries.cs:90) probes the *control* domain cloudflare.com, not the target. On a network where an on-path attacker spoofs answers for the user's target domain but leaves cloudflare.com's DNSSEC intact (trivial — the attacker only rewrites the names it cares about), Prophet emits DnssecCanarySecure → TrustedWitnessOK 0.75 → MatrixDeterminativeEngine.CheckHardExclusion sets `r.HardExclusion` on both dnsSpoofing and dnsInterception. Both threats are suppressed with reason HardExcluded no matter how strongly AuthoritativeMismatch or SignedAnswerMismatch fired, so the named threat never reaches the user; combined with ReportCards.For the run is reported as Grade B, "This network looks clean", Severity.Clean. Before this change TrustedWitnessOK had no EvidenceId mapping and was never observed, so the hard exclusion could not fire.

### 3. REFUSED/censored resolver mapped to clean support

- **Location:** `Windows/dotNET/Syxx.Kit/Synthesis/SynthesisEngine.cs:296`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** New mapping `ResolverStatus → ResolverAgreement (0.80)` turns a block/censorship signal into affirmative clean-support: Synthesizer.cs:185 emits EvidenceId.ResolverStatus ONLY when the resolver returned NXDOMAIN / REFUSED / SERVFAIL, and ResolverAgreement is a Supports rule (Weight 1.00, EvidenceClass.Major) of the `clean` diagnosis. [same root cause also at SynthesisEngine.cs:305]
- **Failure scenario:** On a censoring network the local resolver answers REFUSED (or SERVFAIL) for the target domain. Synthesizer.Prophet emits ResolverStatus with the observation "Resolver returned Refused for the target domain … can be a block" and marks the sub-status Inconclusive. MapEvidenceToIndicators then feeds ResolverAgreement at strength 0.80 into the matrix engine, where it is a Major supporting observation for `clean` — so the very evidence that the domain was blocked pushes the engine toward a Clean determination and counts toward the `MajorCount >= 2` requirement for a Conclusive clean. The user is shown a clean/limited-confidence-clean result for a network that refused to resolve the domain, violating the "missing data is not clean data" rule.

### 4. Any RCODE disagreement graded as confirmed tampering

- **Location:** `Windows/dotNET/Syxx.Kit/VantageComparer.cs:451`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** `if (udpStatuses.Count > 1) return UnanimousTamperingResult.Tampered;` treats any RCODE disagreement among public resolvers as confirmed tampering, but `answeredOutcomes` is filtered with `ResolverQueryOutcome.DidFail`, which does NOT include NXDomain, Refused, ServFail or NoData — so ordinary per-resolver RCODE variation is graded as an on-path attack with no DoH cross-validation at all.
- **Failure scenario:** Twelve public resolvers are queried. Ten return NoError with identical A records (so `udpSets` is unanimous and passes the SetEquals gate); one returns SERVFAIL (DNSSEC-validating resolver on a temporarily bogus zone) and one returns REFUSED (ACL/rate-limit). Neither status is in ResolverQueryOutcome.DidFail, so both stay in `answeredOutcomes`, `udpStatuses` = {Success, ServFail, Refused}, count 3 > 1, and the method returns Tampered. Evaluate sets `redirection = true`, the Mirror/Prophet surface renders "DNS Redirection Detected" as a Positive finding, and the user is told their network is tampering with DNS on the basis of two benign resolver refusals — a fabricated accusation.

### 5. Undetermined never returned; sparse data reads as clean

- **Location:** `Windows/dotNET/Syxx.Kit/VantageComparer.cs:332`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** UnanimousTamperingResult.Undetermined is declared, documented as the fix for the boolean that "silently treated insufficient data as clean", and handled at line 332 — but DetectUnanimousTampering never returns it; all five insufficient-evidence paths still return Clean, so inconclusiveFromUnanimous is permanently false.
- **Failure scenario:** A captive-portal / DoH-filtered network lets fewer than two DoH endpoints answer. DetectUnanimousTampering hits `if (dohSets.Count < 2) return UnanimousTamperingResult.Clean;` (line 464), the Undetermined arm at line 332 is dead, inconclusiveFromUnanimous stays false, and isInconclusive (line 401) is not raised. The comparison is reported to the user as a completed clean vantage check even though the cross-transport corroboration it depends on never happened — the exact false-clean the tri-state was introduced to prevent.

### 6. 198.18/15 reclassified; rogue-DHCP test no longer matches

- **Location:** `Windows/dotNET/Syxx.Kit/IPClassifier.cs:73`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** 198.18.0.0/15 was reclassified from IPAddressClass.PrivateUse to the new IPAddressClass.Benchmarking, but the rogue-DHCP off-link test in Synthesizer.cs:601 still matches on `== IPAddressClass.PrivateUse` only, so that range is no longer recognised as a private DNS push. [same root cause also at IPClassifier.cs:118]
- **Failure scenario:** A hijacked router (or carrier CPE, which commonly uses 198.18.0.0/15) pushes a DHCP DNS server at 198.18.4.4 that is neither the gateway IP nor on the gateway's /24. Before, Classify returned PrivateUse and the offLink list was non-empty, so Portal emitted EvidenceId.GatewayDhcpOffLinkDNS ("DHCP pushed a private DNS server off your gateway's subnet") and SectionStatus.Positive. Now Classify returns Benchmarking, offLink is empty, and Synthesizer.cs:611 records SectionStatus.Negative — the audited-clean branch — so the user is shown a clean Portal section for a live rogue-DHCP redirect. ProbePlan.IsOwnNetworkAddress (ProbePlan.cs:199) loses the same range.

### 7. Same-provider CDN divergence emits a tampering finding

- **Location:** `Windows/dotNET/Syxx.Kit/AuthoritativeVantage.cs:216`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** K-16 changes the same-provider disjoint branch from `Inconclusive` to `Diverges`, and Synthesizer.cs:450-485 maps `Diverges` to `SectionStatus.Positive` plus an `AuthoritativeDiverges` evidence item — so ordinary CDN geo-routing now produces an affirmative tampering finding. [same root cause also at AuthoritativeVantage.cs:225]
- **Failure scenario:** Verified by running the built Syxx.Kit: `AuthoritativeVantage.Classify({"104.16.132.229"}, {"104.17.25.14"}, ["ns1.cloudflare.com"], "example-cdn.com")` — two ordinary Cloudflare edge IPs, the normal shape when the recursive resolver and the direct-to-authority probe land on different edges — returns `Diverges`, and `EvidenceAdapters.Mirror` then returns `Mirror section status=Positive` with the user-facing observation "Recursive answer diverges from the domain's authoritative nameservers ... evidence of an upstream rewrite". Any user scanning a Cloudflare/Akamai/Fastly-fronted domain on a perfectly healthy network sees Mirror flip to a positive finding accusing their ISP of rewriting DNS.

### 8. DPAPI keystore moved to LocalAppData with no migration

- **Location:** `Windows/dotNET/Syxx.Kit/DpapiSecretStore.cs:51`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The static ctor moves `StoreDir` from Roaming `%APPDATA%\Syxx\keystore` to `%LOCALAPPDATA%\Syxx\keystore` with no path migration (the only migration added is entropy-level, `GetLegacyStaticEntropy`, line 118), while `WitnessKeyStore`/`LicenseManager` token files stay where they were.
- **Failure scenario:** On any existing Windows install upgrading to this branch: `LicenseManager.Unprotect` reads the surviving `__syxx_ls:<keyId>` token from config, calls `SecretStore.Retrieve("license", keyId)`, which now looks under LOCALAPPDATA where no blob exists, and returns null — the user's paid licence silently reverts to unlicensed/trial. Worse for Witness: if `witness.key` is also absent, `DpapiSecretStore.Exists` (line 168-172) sees no StoreDir, returns `SecretExistence.Absent` rather than `Unknown`, and `AbsentUnlessStoreHoldsWitnessSecret` returns `KeyPresence.Absent`, licensing the app to mint a second Ed25519 identity over a key that is still on disk at the old path — every previously sealed attestation now verifies against a different public key.

### 9. Two CLI call sites run without the Warp replay nonce

- **Location:** `Windows/dotNET/Syxx.Kit/WarpEngine.cs:577`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The §4.5 replay nonce was added as an optional parameter defaulting to null, and `EvaluateFreshness` (line 490) skips the nonce check entirely when `clientNonce == null`, so the two call sites that were not updated silently run with replay protection disabled.
- **Failure scenario:** `ProbePlan.cs:164-166` generates a nonce, but `Windows/dotNET/Syxx/Commands/WarpCommand.cs:48` and `Windows/dotNET/Syxx/Program.cs:701` still call `RunVantageCheckAsync(origin, SyxxConfig.DimensionBaseURL, licenseKey)` with no nonce. An on-path attacker who captured one signed manifest for the origin can replay it to those paths for the whole freshness window (up to the new 600 s ceiling): the signature verifies, `EvaluateFreshness` returns `Fresh` because the nonce arm is skipped, and `syxx warp <domain>` prints `VERIFIED_MATCH` for content that was swapped after the manifest was minted.

### 10. Manifest key file read before env var, shadowing it

- **Location:** `Windows/dotNET/Syxx.Kit/WarpManifestPinning.cs:41`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** `ConfiguredTrustedManifestKeys()` was inverted to read the persisted file FIRST and the `SYXX_WARP_MANIFEST_KEYS` environment variable only as a fallback, while the XML doc comment three lines above still documents the opposite order ("Sources, in order: the SYXX_WARP_MANIFEST_KEYS environment variable, else a persisted key file"). A stale on-disk pin file now permanently shadows an operator-supplied env-var pin.
- **Failure scenario:** An operator rotates a compromised Warp manifest signing key by setting SYXX_WARP_MANIFEST_KEYS to the new key. Because %LocalAppData%/Syxx/warp_manifest_trusted_keys.txt still exists from the previous install, `PersistedTrustedManifestKeys()` returns non-empty and the env var is never read. Every manifest signed with the new key verifies against a key that is not in the pinned map, so WarpEngine reports UntrustedSigner → INCONCLUSIVE for every origin, and Mirror's content-integrity check never produces a VERIFIED_MATCH again until the file is manually deleted. The user sees a permanently inconclusive Warp section with no indication that the pin source was overridden.

### 11. Ephemeral salt makes trial signature unverifiable

- **Location:** `Windows/dotNET/Syxx.Kit/LicenseManager.cs:338`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** GetInstallationSalt()'s failure path returns `ephemeral:{DateTimeOffset.UtcNow.Ticks}` — a different value on every invocation — so any trial signature produced under it can never be reproduced or verified. [same root cause also at LicenseManager.cs:295]
- **Failure scenario:** On a machine where SecretStore.Retrieve/Store throws or silently no-ops (the LinuxSecretStore file-fallback case the csproj D004 comment describes: "a silent failure in StoreWithFile is invisible — Retrieve returns null with no diagnostic"), every call to TrialSignature gets a fresh ephemeral salt. GetFirstLaunchDateAsync calls it three times per launch (expectedSig, legacySig, and the write at line 456), so the stored signature never matches on the next launch, localLaunchMillis is always null, and finalLaunchMillis resets to now on every start — an unbounded free trial, plus a "trial.signature.mismatch" log entry blaming a Windows reinstall on every run.

### 12. Default-domain guard wired into Avalonia but not WinUI

- **Location:** `Windows/dotNET/Syxx.Kit/Synthesis/FirstRunScan.cs:60`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The new `fallbackToDefaultDomain` guard (C009: never silently grade the well-known default domain) was wired into only one of the two Conclusions surfaces — the Linux Avalonia view passes `false`, the WinUI view was not updated.
- **Failure scenario:** `Linux/Ubuntu/SyxxUI.Avalonia/ViewModels/ConclusionsViewModel.cs:82-83` calls `RunAsync(domain: Domain.Trim(), fallbackToDefaultDomain: false, ...)`, but `Windows/dotNET/SyxxUI/Views/ConclusionsView.xaml.cs:251` still calls `RunAsync(_vm.Domain, isPro: ..., progress: ...)` and so keeps the `true` default. On Windows, a user who clicks Scan with the domain box blank gets a full scan of `FirstRunScan.DefaultDomain` (github.com) rendered as their session's Conclusions grade and evidence, with nothing on screen saying a different domain was measured — the precise silent substitution the parameter was introduced to forbid.

### 13. Non-usable section's weight counted toward coverage

- **Location:** `Windows/dotNET/Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:1054`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The new B026 branch adds `observedSupportWeight += rule.Weight` for indicators produced by a NON-usable section; the cited Swift twin (MatrixDeterminativeEngine.swift:785, B162) `continue`s before that line, so the rule contributes to neither coverage nor contradiction.
- **Failure scenario:** Prophet is Untested/Invalidated but still carries an observed indicator with weight 0.4. Swift: observedSupportWeight excludes it → coverage = usableWeight/total, contradiction = mass/(mass+usableWeight). C#: coverage numerator is inflated by 0.4 (evidence from a section that produced no usable output now counts as "observed"), and `r.Contradiction = mass/(mass + observedSupportWeight + 1e-9)` is correspondingly deflated, so `maxContradictionForConclusive` is easier to clear. Coverage — the "how much of the required evidence did we actually get" measure the honesty spine relies on — is credited for evidence that was never usable.

### 14. --verbose returns early, short-circuiting --help/--version

- **Location:** `Windows/dotNET/Syxx.Kit/SyxxConfig.cs:44`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The new `--verbose`/`-v` branch in `HandleGlobalFlags` does an early `return false`, short-circuiting the `--version` and `--help` branches that follow it.
- **Failure scenario:** `syxx prophet --verbose --help` (or any `-v … -h` combination) sets `IsVerbose = true` and returns `false` at SyxxConfig.cs:44 before reaching the `--help` block at line 54. The caller treats `false` as "no terminal flag", so usage is never printed and the command runs for real — firing live DNS probes at the user's resolvers when all they asked for was help text. `-v --version` likewise prints nothing and runs the command instead.

### 15. IPv6 fragments parsed as UDP while IPv4 rejects them

- **Location:** `Windows/dotNET/Syxx.Kit/DNSCapture.cs:323`
- **Category:** cleanup  |  **Result:** CONFIRMED
- **Summary:** The IPv6 path follows Fragment headers (proto 44) and parses the payload as UDP, while the same commit rejects all IPv4 fragments (D146) for exactly that reason.
- **Failure scenario:** A non-initial IPv6 fragment (fragment offset != 0) has arbitrary payload bytes where the UDP header is assumed. The `case 44` arm skips the 8-byte fragment header and continues to nextHeader, so a second-or-later fragment of a large IPv6 packet is parsed as a UDP+DNS packet and yields a fabricated capture record. The IPv4 branch three lines earlier explicitly returns null for any fragment with the comment "parsing it as a transport header fabricates evidence"; the IPv6 branch does not carry that guard.

## Run 3 — macOS/Swift (SyxxKit, SyxxApp, CLI) — 2026-08-12 08:06 local

ReportFindings timestamp (UTC): 2026-08-12T13:06:06.881Z — level: xhigh — findings: 15

### 1. IPv6 ::1 passes the restricted-DNS-address gate

- **Location:** `macOS/Sources/DeepSyxx/main.swift:427`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The IPv6 loopback rejection `bytes.allSatisfy({ $0 == 0 }) && bytes.last == 1` is a contradiction that can never be true, so ::1 passes the restricted-DNS-address gate the root helper uses to prevent DNS hijacking.
- **Failure scenario:** A compromised or spoofed XPC client calls setDNSServers(servers: ["::1"]). inet_pton parses it, giving bytes = [0,0,…,0,1]. `bytes.allSatisfy({ $0 == 0 })` is false because the last byte is 1, so the `&& bytes.last == 1` clause is unreachable and ::1 is not rejected. fe80::/10, ff00::/8, fc00::/7 and the all-zero `::` checks all miss it too, so isValidRestrictedDNSAddress returns true and the root helper points the system resolver at an attacker-controlled listener on IPv6 loopback — precisely the hijack the function's own doc comment (line 391) says it blocks. The IPv4 equivalent 127.0.0.1 is correctly rejected at line 404, so the guard is trivially bypassed by using the IPv6 form.

### 2. Contradicting indicators promoted to decisive support

- **Location:** `macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:864`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The `if effective > 0` block that records supporting indicators, independence groups, hasDecisive and majorCount was moved outside the non-contradictory `else` branch, so an indicator observed as CONTRADICTING a diagnosis now counts as evidence FOR it — including promoting it to the decisive tier — and also adds half its contribution to alignedSupport. [same root cause also at MatrixDeterminativeEngine.swift:852]
- **Failure scenario:** An indicator wired as `supports` for the DNS-hijack diagnosis is observed with `contradictory == true` (the probe affirmatively measured the opposite). Previously it added only contradictionMass and could never register as support. Now line 854 adds `contribution * 0.5` to `acc.alignedSupport`, and because line 864's `if effective > 0` sits outside the else, the same contradictory indicator appends to `supportingIndicators`, inserts its `independenceGroup`, and sets `hasDecisive = true` when its evidenceClass is `.decisive`. A run whose only firing indicators contradict the diagnosis can therefore clear the decisive-or-two-majors tier gate and emit `state: .threatDetected`, so the Conclusions pane accuses the user's network of tampering on evidence that measured the opposite.

### 3. DoH unavailable now returns tampered, not undetermined

- **Location:** `macOS/Sources/SyxxKit/VantageComparer.swift:336`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** C046/C056 removed: `detectUnanimousTampering` now returns `.tampered` instead of `.undetermined` when DoH is unavailable (line 336) or when fewer than 2 DoH endpoints returned records (line 344), so unverified UDP unanimity is promoted to a confirmed positive.
- **Failure scenario:** On any network where DNS-over-HTTPS is blocked or unreachable (corporate TLS filter, captive portal, Little Snitch, or simply a budget-truncated run that yields <2 DoH answers), a completely ordinary lookup — local answer overlapping the public union, >=2 UDP resolvers agreeing on the same address, one RCODE — takes the `guard let dohOutcomes ... else { return .tampered }` path. `.tampered` maps straight to `redirection = true` (VantageComparer.swift:236), and the Mirror surface prints "TAMPERING / REDIRECTION DETECTED" for a clean domain. The deleted `.undetermined` arm at the same site is the only thing that previously routed this into `inconclusiveFromUnanimous`. Every scan on a DoH-blocked network now accuses the user's network of tampering.

### 4. Unmeasured Mirror renders green 0%/Identical badge

- **Location:** `macOS/Sources/SyxxApp/MirrorView.swift:456`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** buildTerminalData() fabricates a green "0% / Identical" degradation badge and a "no differences detected" panel on the two branches where no content was ever compared, and those are the only branches reachable in production because sectionInputs.warp is documented as "retired, never populated". [same root cause also at MirrorView.swift:468]
- **Failure scenario:** User runs Mirror on a domain whose Warp check returned TAMPERING_DETECTED (or that never ran at all). `warp` is nil (MirrorView.swift:9-10 says the legacy WarpResult path is "retired, never populated"; AppModel+Mirror.swift only writes `sectionInputs.warpManifest`), so buildTerminalData() falls into the manifest-placeholder branch at line 444 (or the no-data branch at line 460) and hard-assigns `mirrorDiffResult = DiffResult(htmlDiff: [], cssDiff: [], jsDiff: [])` and `mirrorDegradation = DegradationScore(score: 0.0)`. DegradationScore.Classification.classify maps 0.0 to `.identical`, so MirrorDegradationBadge renders a green "0%  DEGRADATION  Identical" and MirrorDiffView renders the green checkmark line "No differences detected — remote and local content are identical." The user is shown an affirmative clean content-integrity result that was never measured, directly contradicting the tampering result shown elsewhere on the same pane.

### 5. displayMismatch and untrustedSigner render as signed

- **Location:** `macOS/Sources/SyxxApp/AppModel+WitnessRun.swift:150`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The C211 self-verify gate routes `.displayMismatch` and `.untrustedSigner` into the `default:` branch that sets `witnessTestimonyState = .signed`, so a snapshot whose display fields do not match its own signed evidence is promoted to signed testimony.
- **Failure scenario:** `WitnessSigner.verifySnapshot(snap)` returns `.displayMismatch` — the snapshot's rendered comparison fields disagree with the signed payload, which `SyxxWitness.describeVerification` classifies as `[TAMPERED] DISPLAY MISMATCH` on the CLI. In `runWitness()` only `.invalidSignature`, `.malformedInput` and `.unsupportedFormat` are caught as failures; `.displayMismatch` falls to `default:` at line 150 and the Witness tab shows `.signed`. `.untrustedSigner` takes the same path, contradicting the D011 rule the codebase states elsewhere ("valid-but-untrusted must not render green"). The warning text only reaches the user in the returned string body, not the state the card colours itself from.

### 6. HMAC compare accepts length deltas that are 256 multiples

- **Location:** `macOS/Sources/SyxxKit/Trust/IntegrityTag.swift:129`
- **Category:** cleanup  |  **Result:** CONFIRMED
- **Summary:** constantTimeEqual folds the length difference through UInt8(truncatingIfNeeded:), so length deltas that are multiples of 256 contribute 0.
- **Failure scenario:** For a = 32 bytes and b = 288 bytes, `32 ^ 288` is 0x100 and truncates to 0. The zero-padded loop then compares equal on every index when b is a followed by 256 zero bytes, so constantTimeEqual returns true for two different Data values and verifyStatus reports .valid for a mismatched HMAC. `result |= a.count == b.count ? 0 : 1` is the correct length term.

### 7. Helper allowlist resolves to /Resources/, blocking Cage

- **Location:** `macOS/Sources/DeepSyxx/main.swift:47`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The client-supplied CLI path allowlist prefix is now derived from Bundle.main.bundlePath of a bare SMJobBless daemon, which yields "/Resources/" instead of the app bundle's Resources directory, so every privileged Cage capture request is rejected. [same root cause also at main.swift:43]
- **Failure scenario:** The helper is installed at /Library/PrivilegedHelperTools/com.milkandgin.deepsyxx. For a non-bundled executable Bundle.main.bundlePath is the containing directory, "/Library/PrivilegedHelperTools" (the file's own comment at line 36-38 states this). Two `deletingLastPathComponent()` calls reduce it to "/", and appending "Resources" gives "/Resources", so allowedPrefix becomes "/Resources/". AppModel+Cage.startRealCapture passes bin.path = "/Applications/Syxx.app/Contents/Resources/syxx", which fails `cliPath.hasPrefix(allowedPrefix)`. Every Cage capture in a shipped build returns "rejected: CLI path must be under /Resources/, got /Applications/Syxx.app/Contents/Resources/syxx", and the Cage pane shows "Could not start capture: …" — packet capture is completely non-functional. The previous hard-coded "/Applications/Syxx.app/Contents/Resources/" was correct.

### 8. Wrapped Witness key never reads back after restart

- **Location:** `macOS/Sources/SyxxKit/WitnessKeyManager.swift:120`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** E029 key wrapping writes `"wrapped:v1:" + AES-GCM binary` but `loadKey()` detects the prefix by UTF-8-decoding the whole blob, which fails on the random ciphertext, so every wrapped key reads back as nil. [same root cause also at WitnessKeyManager.swift:136]
- **Failure scenario:** A user generates or pastes a Witness signing key. `saveKey` (line 164) stores `Data("wrapped:v1:".utf8) + sealed.combined`, where `sealed.combined` is a 12-byte nonce + ciphertext + 16-byte tag of random bytes; the write-through cache keeps the session working. On the next app launch `loadKey()` calls `String(data: raw, encoding: .utf8)` at line 120 over the full prefix+binary blob, which returns nil because the ciphertext is not valid UTF-8, so the wrapped branch is skipped and the "legacy plain base64" fallback returns `String(data: raw, encoding: .utf8)` — also nil. Meanwhile `checkKeyPresence()` still reports `.present`. AppModel+Witness.swift:189 therefore passes the `.present` gate and then fails at `guard let key = WitnessKeyManager.loadKey()`, showing "Could not load signing key from Keychain." Witness attestation and `syxx witness` signing are permanently broken after the first restart, with no way for the user to recover short of deleting and regenerating the key (which fails the same way).

### 9. B157 quality floor deleted; thin sections never reach tier

- **Location:** `macOS/Sources/SyxxKit/Synthesis/MatrixDeterminativeEngine.swift:846`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The B157 quality floor was deleted — `effectiveQuality` is now the raw clamped quality instead of `max(clamp01(obs.quality), obs.quality > 0 ? minimumFitForProbable : 0)` — reintroducing the exact defect the removed comment described.
- **Failure scenario:** The deleted comment (still visible in the diff) states the failure verbatim: "A collector-confirmed DNS leak (alignment 1.0) on a 50%-complete Glutton section yields fit = 0.5 < 0.62 ⇒ not emitted." With the floor gone, a user on a leaking VPN whose Glutton section completed only half its probe catalog gets `effective = 1.0 * 0.5 = 0.5`, so `r.fit = 0.5`, below `minimumFitForProbable` (0.62). `tier(for:margin:)` returns nil, the diagnosis is suppressed, and the Conclusions pane reports inconclusive/clean instead of the confirmed DNS leak the collector actually observed — a missed detection on the product's headline check.

### 10. mappableIndicators nil makes whole diagnoses unreachable

- **Location:** `macOS/Sources/SyxxKit/Synthesis/SyxxMatrixIntegration.swift:227`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** B156 removed: `SyxxMatrixBuilder.buildDefaultSpec()` now passes `mappableIndicators: nil`; the computed set of indicators that actually have an EvidenceId mapping was deleted, so unmappable rules return to the coverage denominator.
- **Failure scenario:** With `mappableIndicators == nil`, `accumulateSupportMetrics` takes the legacy branch and sums every support rule into `totalSupportWeight`, including rules for indicators (resolverAgreement, remoteLocalDivergence, trustedWitnessOK) that no EvidenceId maps to and that therefore can never be observed. Coverage = observedSupportWeight/totalSupportWeight is hard-capped below `minimumCoverage`, so `strength = fit * coverage` never clears a tier. A genuine DNS hijack that fires every indicator it can fire scores below tier and is suppressed: the Conclusions pane reports "Inconclusive" instead of naming the diagnosis. This is exactly the regression the deleted comment describes ("makes entire diagnoses unreachable").

### 11. Silent third-party DoH fallback reports blocked path working

- **Location:** `macOS/Sources/SyxxKit/EncryptedTransportProbe.swift:74`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The single-endpoint contract was removed: when the caller's endpoint fails, the probe now silently retries three hardcoded third-party DoH endpoints and reports `.working` if any of them answers.
- **Failure scenario:** A network that filters the user's configured/primary DoH provider (targeted per-provider blocking is the common censorship shape) but leaves dns.google reachable now returns `classify(status: .success, ..., controlSucceeded: true, endpoint: "dns.google")` → `.working`. The `encryptedDnsBlocked` EvidenceItem — which maps to `forcedPlainDnsFallback` at strength 0.85 — is never emitted, so Glutton tells the user "encrypted path works" on a network that is demonstrably blocking their encrypted DNS. Secondarily, a privacy tool now sends the user's control query to Google and Quad9 without the user having selected either endpoint.

### 12. Signed payload hardcodes ttl 0, breaking C# twin parity

- **Location:** `macOS/Sources/SyxxKit/SignedEvidencePayload.swift:277`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** `build()` now hardcodes `ttl: 0` for every per-resolver canonical answer record (lines 277 and 283) while the C# twin `SignedEvidencePayload.cs:348` still writes `Ttl = r.Ttl`, so the same evidence produces different signed bytes on the two platforms — and the signed receipt attests a fabricated TTL of 0 for records that had real TTLs.
- **Failure scenario:** macOS signs a WitnessSnapshot for example.com whose Cloudflare answer carried TTL 300. `perResolverAnswers["Cloudflare"].records[0].ttl` is written as 0, so the canonical JSON (and its Ed25519 signature) differs from what `Syxx.Kit` would produce for the identical `ComparisonResult`. `syxx witness verify` on Windows/Linux recomputes the canonical bytes with the real TTL, gets a different digest, and reports `[TAMPERED] INVALID SIGNATURE` on an honest snapshot. The same break applies in reverse to any macOS snapshot sealed by a previous build. No `formatVersion` bump accompanies the change, so v2/v3/v4 signed bytes all shifted silently, and `authoritativeTtls` a few lines above still carries the real measured TTLs — the payload now contains both the true value and a zeroed copy of it.

### 13. Undeterminable ECS state reported as notForwarded

- **Location:** `macOS/Sources/SyxxKit/ECSProbe.swift:71`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The terminal branch was changed from `.inconclusive` to `.notForwarded`, converting an explicitly undeterminable observation into an affirmative privacy-preserving finding.
- **Failure scenario:** A resolver echoes an EDNS0 OPT record with no ECS option. The replaced code and comment stated the truth — "whether it forwards the subnet upstream cannot be determined from the client" — and returned `.inconclusive`. The new code returns `.notForwarded` with "subnet is not being forwarded". A resolver that silently forwards the client subnet upstream while stripping ECS from its reply (common for forwarders) is now reported to the user as privacy-preserving in the Glutton/Conclusions surface, and any synthesis rule keyed on `.notForwarded` scores an unmeasured condition as clean.

### 14. CDN edge rotation reclassified from inconclusive to unstable

- **Location:** `macOS/Sources/SyxxKit/AnswerStabilityProbe.swift:104`
- **Category:** correctness  |  **Result:** CONFIRMED
- **Summary:** The B079 classification was reverted: disjoint answer-set rotation now returns `.unstable` instead of `.inconclusive`, and the corroboration caveat was deleted from the detail string.
- **Failure scenario:** The user scans the default domain github.com (AppModel.swift:domain = "github.com") or any other CDN-fronted host. The CDN legitimately rotates between disjoint healthy edge pools, so consecutive samples share no address. The removed comment stated the reason this was previously inconclusive: "CDNs routinely rotate between disjoint healthy edge pools; without ASN/CNAME/authority corroboration, ordinary load balancing is indistinguishable from instability." Prophet now reports the resolver's answers as `.unstable` for ordinary CDN traffic, and that instability signal feeds the synthesis matrix as support for a tampering diagnosis on a perfectly healthy connection.

### 15. Flag loop starts at 0, so argv[0] is used as the domain

- **Location:** `macOS/Sources/SyxxCLI/Commands/SyxxMirror.swift:25`
- **Category:** cleanup  |  **Result:** CONFIRMED
- **Summary:** D325 flag-parse loop starts at index 0, so argv[0] ("syxx-mirror") becomes the first positional and is used as the domain.
- **Failure scenario:** SyxxCLI.swift:1050 calls SyxxMirrorCLI.run(["syxx-mirror"] + args), so args[0] is the subcommand token. The new loop begins at `var idx = 0` and appends every non-flag token, so positionalArgs.first == "syxx-mirror". Running `syxx mirror example.com` now canonicalizes "syxx-mirror" instead of example.com and prints either "Error: Invalid domain 'syxx-mirror'" (exit 3) or compares resolution for a nonexistent single-label name — the mirror subcommand no longer works for any domain.

# CODEFIX.md — Remediation Plan: Making Syxx Do What the Design Docs Allege

**Purpose.** An engineering plan (not an implementation) that maps every place where `DESIGN.md`'s
claims — and the CLI/GUI screen output — diverge from what the code actually does, and for each
gives: the problematic code, why it violates the Syxx values / Verification & Honesty Protocol, the
concrete fix, the research that makes the fix *correct*, platform-parity notes, and how to **prove**
it works with a real run.

**Every code block is labelled by provenance:**

- `— extracted: <file:line>` — real code copied verbatim from the working tree (the defect, or a
  pre-existing correct pattern to copy).
- `— proposed` — new code sketched here; kept minimal and modelled on a pre-existing pattern wherever
  one exists in the tree.

**Method.** Source-grounded against branch `feat/beta-signup-and-server-hardening` via read-only
extraction (three parallel audits + web research); no source was modified. Where the user's own
terminal run is the evidence (Cage), it is cited as such.

**Nothing here has been applied.** Per the delta-vetting rule this is for review; it does not touch
`DELTAS.md` / `ROADMAP.md`.

**Honesty caveat.** Two items (CF-03 scoring, CF-09 attestation) are design judgments, not
black-and-white bugs — flagged as such. One item I first suspected (CUR-001, CUR-005) is **already
fixed in code**; the only "delta" there is a stale ledger. I have tried not to inflate.

---

## 0. Recommended implementation order

Ordered by the `AGENTS.md` value precedence (Security → Efficacy → Efficiency → Ingenuity →
Simplicity), quick honesty-fixes pulled forward where they cost almost nothing.

| # | Item | Discipline | Kind | Effort | Why this rank |
|---|------|-----------|------|--------|---------------|
| 1 | **CF-01** Ed25519 for Witness + Leak evidence | Witness/Glutton | Structural / Security | M | All "signed" evidence rests on this; symmetric HMAC is not trustworthy evidence. Fixes two disciplines. |
| 2 | ✅ **CF-02** Cage capture actually captures (macOS) | Cage | Structural / Efficacy | M–L | **DONE** (`91bae82`+`3357d0b`): libpcap; verified 4 records on a real run. |
| 3 | **CF-04** `resolveSecurely` returns `inconclusive`, never throws | Prophet | Honesty / tri-state | **S** | One `catch`; the correct pattern already sits in the same function. |
| 4 | **CF-05** GUI stops printing ✅ on a degraded baseline | Mirror | Honesty / wording | **S** | Two lines; the honest wording already exists in the CLI. |
| 5 | **CF-06** No verdict on a truncated/unusable baseline | Mirror/Portal | Efficacy | S–M | Both platforms issue redirection verdicts on one-legged baselines. |
| 6 | ✅ **CF-03** Scoring: earn confidence, don't start at 100 | (cross-cut) | Structural / §3 | L | **DONE** (`a403f2c`/`a33b418` DST both platforms + B020–B023 calibration). |
| 7 | **CF-07** `syxx-route` real path-anomaly analysis | Portal | Efficacy / feature | M | Tool claims analysis it does not perform. |
| 8 | **CF-08** DNSSEC real DS→DNSKEY→RRSIG validation | Prophet | Efficacy / feature | L | Honest-but-incomplete today; completes A2. |
| 9 | **CF-09** Collector egress attestation | Glutton | Ingenuity / feature | L | Closes A3; depends on CF-01's signing. |
| 10 | **CF-10** Reconcile `DESIGN.md` + ledgers with reality | docs | Honesty | S | Do continuously as each fix lands. |

"S" ≈ <1 day · "M" ≈ 1–3 days · "L" ≈ multi-day / design-note-first.

---

## 1. Cross-cutting research (citations in Appendix A)

1. **macOS/BSD raw sockets cannot sniff UDP.** Darwin/FreeBSD never deliver TCP or UDP to a
   `SOCK_RAW` socket — only ICMP/IGMP and protocols with no kernel handler. Port-53 sniffing on
   macOS requires **BPF (`/dev/bpf*`)** or **libpcap**, or `NEFilterDataProvider`/EndpointSecurity
   for per-process identity. The current approach is structurally impossible on macOS.

2. **HMAC proves nothing to a third party.** HMAC is symmetric: whoever can *verify* can also
   *forge*, and the blob cannot demonstrate provenance outside the key-sharing pair. Evidence a user
   could post to r/AskNetSec, or present forensically, needs an **asymmetric signature (Ed25519)** —
   private key signs, public key (freely distributable) verifies.

3. **Dempster–Shafer keeps "I don't know" as first-class mass.** A mass function `m: 2^Θ→[0,1]`,
   `m(∅)=0`, `Σm(A)=1`, leaves unassigned belief on the whole frame `Θ` = ignorance. On zero
   evidence all mass is on `Θ`, so belief in every specific hypothesis is 0 and derived confidence is
   ~0. Combination `m₁,₂(A)=(1/(1−K))·Σ_{B∩C=A≠∅} m₁(B)m₂(C)`, conflict `K=Σ_{B∩C=∅} m₁(B)m₂(C)`;
   `Bel(A)=Σ_{B⊆A} m(B)`, `Pl(A)=Σ_{B∩A≠∅} m(B)`.

---

## Part I — Structural / foundational fixes

### CF-01 — Replace symmetric HMAC with Ed25519 for all evidence (Witness #6 + Leak/Glutton #4)

**Alleged.** `DESIGN.md` §6 marks witness-signing hardening **done**; §4 calls the leak result an
"HMAC-signed … chain-of-custody evidence."

**Reality — extracted.**

```swift
// — extracted: macOS/Sources/SyxxKit/WitnessSigner.swift:83-84 (signing = symmetric MAC)
CCHmac(
    CCHmacAlgorithm(kCCHmacAlgSHA256),
```

```python
# — extracted: Server/syxx_server/leak.py:27-30 (collector signs leak verdict with a shared secret)
def sign(obj):
    return base64.urlsafe_b64encode(
        hmac.new(licensing.secret(), _canon(obj), hashlib.sha256).digest()
    ).decode("ascii").rstrip("=")
```

The Windows side is the same (`Windows/Syxx.Kit/WitnessSigner.cs:91` `new HMACSHA256(keyBytes)`), and
the client **never verifies** the leak signature — it stores it as an opaque string
(`LeakDetector.swift:13-31`, `Windows/Syxx.Kit/LeakDetector.cs:22-34`).

**Why it violates the mission (Security, value #1).** Symmetric MACs make the evidence
**not third-party-verifiable** (a user can't hand it to r/AskNetSec — the explicit Delta-of-the-Week

\# 1 goal — or to a court), and prove **no provenance** (can't show which machine/collector produced

it). The client storing an unverified signature is theatre. This makes §6 "done" and §4
"chain-of-custody" false in the adversarial sense.

**Pre-existing precedent in the tree** — the codebase's own ledger already prescribes Ed25519 for the
analogous license-verification hole, and CryptoKit is already a dependency:

```
— extracted: Server/SERVER_DELTAS.md:65
SD-25 · … validate-key reads the "valid" boolean straight from JSON with no response-signature
check … Fix: use Keygen's cryptographic (Ed25519-signed) license/verification and verify the
signature, not just the flag.
```

```swift
// — extracted: macOS/Sources/SyxxKit/ContentWarp.swift:2  (CryptoKit already linked — Ed25519 is available)
import CryptoKit
```

**Fix — proposed (Witness, macOS; mirror on collector + Windows).**

```swift
// — proposed: sign with Curve25519.Signing (Ed25519), emit sig + public key
let sig = try privateKey.signature(for: payloadData)               // 64-byte Ed25519 signature
// artifact: { payload, "sig": base64(sig), "keyId": id, "pubKey": base64(privateKey.publicKey.rawRepresentation) }

// — proposed: anyone verifies with only the public key → map Bool to tri-state
let status: WitnessStatus = publicKey.isValidSignature(sig, for: payloadData) ? .verified : .tampered
```

Store the **private** key with the OS protection already used for the license (Keychain on macOS,
DPAPI on Windows); the verifier must never hold signing power. Publish the collector's public key at
a stable URL (or pin it in the client) so leak attestations verify offline. Add a `keyId` for
rotation.

**Parity.** `WitnessSigner.{swift,cs}`; `LeakDetector.{swift,cs}` gain `verify()`; `leak.py` swaps
`sign()`. Note `LicenseManager.swift:304` also uses `CCHmac` — audit whether it needs the same
treatment (ties to SD-25).

**Verify (real run).** Sign on machine A, verify on B **with only the public key** and no secret
present; flip one payload byte → `tampered`; confirm a public-key holder cannot mint a passing
signature. **Effort.** M.

---

## CF-02 — Cage: capture actually captures on macOS (stop printing `[SUCCESS]` over zero packets)

**STATUS: DONE — verified capturing real traffic (2026-07-09).** libpcap bridge landed as `91bae82`
(`Sources/Ccage/`), then made to actually run in `3357d0b`: `cage_pcap_lookupdev` returned
`pcap_lookupdev()`'s STATIC buffer while the header promised the caller must `free()` it, so
`SyxxCage`'s `free(device)` corrupted the heap and crashed on **every** launch (build + tests were
green throughout — runtime verification caught it). Shim now uses `pcap_findalldevs` + `strdup`;
auto-selection picks the **default-route** interface (was choosing dead pseudo-interfaces
ap1/awdl0 → 0 packets), with a `--interface`/`-i` override. VERIFIED by a real `sudo` run on `en5`:
4 DNS events captured (query+response ×2), report `Parsed records: 4`, files owner-only;
zero-records path still → `INCONCLUSIVE`. Open follow-ups: process attribution reads `unknown`;
the report Interpretation prints "unusually long / encoded" caveats even when the report itself
says "None observed".

**Alleged.** `DESIGN.md` §5 marks capture **done**. Screen output: `[SUCCESS] Sniffer started …` +
a full forensic report.

**Reality — extracted + user's run.** Even with `sudo`, every run reports `Parsed records: 0` while
claiming success.

```swift
// — extracted: macOS/Sources/SyxxCage/SyxxCage.swift:68-69  (wrong socket type on BSD)
let sockV4 = socket(AF_INET, SOCK_RAW, IPPROTO_UDP)
let sockV6 = socket(AF_INET6, SOCK_RAW, IPPROTO_UDP)
```

```swift
// — extracted: macOS/Sources/SyxxCage/SyxxCage.swift:80  (unconditional success)
print("\n[SUCCESS] Sniffer started. Capturing packet streams...")
```

The report generator only warns when *lines fail to parse*, never when **zero** records were
captured:

```swift
// — extracted: macOS/Sources/SyxxKit/DNSCaptureReportGenerator.swift:100-102
let unparsedWarning = unparsedCount > 0 && parsedRecords == 0
    ? "\n⚠️  WARNING: every line failed to parse — this report reflects NO capture data, not a clean network.\n"
    : ""
```

So the whole `Interpretation` block (`:156-161`, "The capture contains 0 parsed DNS event
record(s)…") prints as if the run were valid. Secondary bugs: bare duration arg dropped (`syxx-cage
90` runs 10s, `:28-38`); under `sudo`, output lands in root's home (`:51`).

**Windows is NOT broken — the working pattern already lives in the tree:**

```csharp
// — extracted: Windows/SyxxCage/Program.cs:183-214  (promiscuous capture that actually works)
socket = new Socket(family, SocketType.Raw, protocol);            // ProtocolType.IP, not UDP
socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, family == AddressFamily.InterNetwork);
byte[] byIn = new byte[] { 1, 0, 0, 0 };
byte[] byOut = new byte[] { 1, 0, 0, 0 };
socket.IOControl(IOControlCode.ReceiveAll, byIn, byOut);          // SIO_RCVALL → all IP traffic
```

This is a **parity break**: the design is realized on Windows and impossible-as-written on macOS.

**Why.** Protocol §4 (mocked success), §1 (report before evidence), Efficacy (value #2), and §5
"done" is false on macOS.

**Fix — proposed.**

- **Capture:** replace the macOS raw socket with **libpcap** (ships with macOS):
  `pcap_open_live(dev, 65535, 0, 250, err)` → `pcap_compile(&bpf, "udp or tcp port 53", 1, PCAP_NETMASK_UNKNOWN)`
  → `pcap_setfilter` → `pcap_loop`; parse from the datalink header (`DLT_EN10MB` vs loopback
  `DLT_NULL`) to the DNS payload the existing `DNSCapture` parser already expects. (Alt: `/dev/bpf*`
  with `BIOCSETIF`/`BIOCIMMEDIATE`/`BIOCSETF`.) For real "which process" attribution, phase-2 a
  `NEFilterDataProvider`/EndpointSecurity flow extension instead of guessing PID from a port table.
- **Honesty guard — model it on the pattern already at `DNSCaptureReportGenerator.swift:100-102`,
  broadened to zero records:**

  ```swift
  // — proposed: extend the existing warning to the zero-record case → return inconclusive
  guard parsedRecords > 0 else {
      return "⚠️  INCONCLUSIVE — no packets were captured. This is NOT evidence of a quiet network; "
           + "capture may have failed or required privileges/BPF access were missing."
  }
  ```

- Move/replace `[SUCCESS]` to after the first parsed packet; accept a bare/`90s` duration; resolve
  the output dir from `SUDO_USER` so captures aren't hidden in `/var/root`.

**Parity.** Fix macOS `SyxxCage.swift`; keep Windows capture; add the zero-record→inconclusive guard
to `Windows/Syxx.Kit/DNSCaptureReportGenerator.cs` too.

**Verify (real run).** Capture while `dig example.com` runs → `Parsed records > 0`, query visible; no
traffic → **inconclusive** report, not a clean-looking zero report; `-d 3` and `3s` ≈ 3s. **Effort.**
M–L.

---

### CF-03 — Determinative engine: earn confidence, don't start at 100 (Delta-of-Week #1, §3, V-2)

**STATUS: DONE — Dempster–Shafer engine on BOTH platforms (2026-07-09).** The softmax
"confidence-from-100" engine was replaced with a DST belief model (macOS `a403f2c`, Windows 1:1 port
`a33b418`, build-fix `0558fe4`): empty/degenerate input → confidence ≈ 0 **by construction**;
`inconclusive` is the unassigned-mass epistemic state, not a scored class; `uncertainty` (m(Θ)) and
`conflict` (K) are surfaced and K forces a referral. Calibration follow-ups also fixed:
**B020** (`fd47a07` — removed the mis-weighted `authoritativeDiverges→censorship 1.2` that made
spoofing evidence read as *censorship*; diagnosed as a weight, not naming, bug),
**B021** (`854a789` — additive `belief` = committed `Bel` per hypothesis, 0 where nothing points,
distinct from the pignistic "Likelihood" that spreads set-mass onto uncovered threats),
**B022/B023** (`8c2aadc` — inconclusive verdict → confidence 0; doc "seven threats").
Suites: macOS **302/0**, Windows **159/0**. Open: the parallel `authoritativeDivergesConfirmed→
censorship 2.0` weight (same class); B3 approach (a) — belief as the *primary* distribution — if wanted.

**Alleged.** The six sections feed a conclusion + confidence + per-hypothesis probability that
**cannot be confident on partial/missing data**, referring to Reddit when confidence < 75% or margin
< 30.

**Reality — extracted.** `SynthesisEngine.synthesize` is real, explainable, and — credit where due —
**already has a referral safety net** that always fires on an inconclusive verdict:

```swift
// — extracted: macOS/Sources/SyxxKit/Synthesis/SynthesisEngine.swift:86-92
let top = ranked.first?.probability ?? 0
let second = ranked.count > 1 ? ranked[1].probability : 0
let margin = (top - second) * 100
let referralTriggered = confInt < config.confidenceReferralBelow
    || margin < config.marginReferralBelow
    || verdict == .inconclusive
```

But confidence is built **top-down** — start at 100, subtract reliability penalties — and
`inconclusive` is mixed into the same softmax simplex as the hypotheses:

```swift
// — extracted: SynthesisEngine.swift:63-67  (inconclusive is a softmax class)
scored[.inconclusive] = Double(inconclusiveCount) * 0.8 + Double(degradedCount) * 0.4
    + ((totalThreat < 1.0 && (scored[.clean] ?? 0) < 1.0) ? 1.0 : 0.0)
let ranked = softmax(scored, temperature: config.softmaxTemperature).sorted { $0.probability > $1.probability }
```

```swift
// — extracted: SynthesisEngine.swift:78-84  (confidence = 100 × ∏(1 − penalty); thin-evidence only if totalThreat < 1.0)
var confidence = 100.0
confidence *= pow(1 - config.degradedPenalty, Double(degradedCount))
confidence *= pow(1 - config.inconclusivePenalty, Double(inconclusiveCount))
if threatHypotheses.contains(verdict) && totalThreat < 1.0 { confidence *= (1 - config.thinEvidencePenalty) }
let confInt = Int(max(0, min(100, confidence.rounded())))
```

**Honest assessment of what's actually wrong (this is a judgment call, not a lie):**

1. **Single-signal 100% threat verdicts, no referral.** One `(.redirection, .spoofing) = 2.0`
   (`Conclusion.swift`) gives `totalThreat = 2.0 ≥ 1.0`, so the thin-evidence penalty never fires;
   with no degraded/inconclusive sections, confidence stays 100 and the margin is wide → **no
   referral**. One uncorroborated vantage disagreement → maximally confident "MALICIOUSLY
   REDIRECTED." Confidence is tied to *reliability penalties*, not to *corroboration breadth*.
2. **`inconclusive` as a softmax class** — Protocol §3 names this exactly ("treats 'inconclusive' as
   a softmax class with a probability"). Empty input → `inconclusive` at **100%** confidence; the
   referral still fires (good), but a `WitnessSigner`-signed artifact can read "inconclusive,
   confidence 100%," which is self-contradictory out of context.
3. **Uncalibrated probabilities.** `softmax` of raw summed weights at `T=1.0` yields numbers with no
   defined meaning; §3 requires documenting *what each score means* and its no/partial/full-data
   behavior. The 0.4/0.6/2.0/3.0 weight tiers are undocumented magic constants.

**Fix — proposed (recommended: a belief model; keep the referral net you already have).**

- Model each section as a **mass function** over `Θ = {clean, spoofing, censorship, interception,
  contentTampering, privacyLeak, localCompromise}`, with a section that didn't run contributing
  `m(Θ)=1` (pure ignorance) — it *cannot* raise confidence.
- Combine sections with **Dempster's rule** (App. A #4). Report **conclusion** = argmax `Bel`;
  **confidence** = committed `Bel(conclusion)` (0 on empty input *by construction*, so #1 and #2
  disappear); **per-hypothesis probability** = the pignistic transform `BetP`; show `Pl − Bel` as the
  honest uncertainty band, and surface the conflict `K` as "sections disagree."
- Keep the extracted 75/30 referral gate; feed it `Bel`/`BetP`. Document every mass value and its
  no/partial/full-data behavior in a `SCORING.md`/ADR (satisfies §3), replacing the magic tiers.
- *Interim, if the DST refactor is deferred:* build confidence **up** from corroboration (cap below
  75% until ≥2 independent sections agree) and remove `inconclusive` from the softmax — make it the
  explicit residual when committed mass is below a floor. This alone kills the single-signal 100%.

**Parity.** `SynthesisEngine.{swift,cs}` (identical today); `WeightRubric`/`Conclusion.{swift, …}`.

**Verify (real run).** (a) all sections untested → confidence ≈ 0, referral fires; (b) one 2.0 signal
alone → stays below the threat threshold / refers; (c) 3 independent decisive signals agreeing →
high; (d) two sections in direct conflict → confidence drops and `K` is shown. Encode these as
requirement-level tests. **Effort.** L.

---

## Part II — Honesty / tri-state fixes (small, high-value)

### CF-04 — `resolveSecurely` must return `inconclusive`, not throw (CUR-002, Prophet)

**Reality — extracted.** The loopback path already returns a correct tri-state; the DoH path throws:

```swift
// — extracted: macOS/Sources/SyxxKit/DNSResolver.swift:217-229
if servers.contains(where: { IPClassifier.classify($0) == .loopback }) {
    return SecureResolutionResponse(records: [], isSecure: false, provenance: "Local Loopback Proxy",
                                    isInconclusive: true,
                                    reason: "Local loopback DNS proxy present; upstream transport security cannot be verified.")
}
guard let dohURL = URL(string: "https://cloudflare-dns.com/dns-query") else { throw DNSResolverError.transport("Invalid DoH URL") }
let records = try await queryDoH(name: query.name, type: query.type, url: dohURL)   // ← network failure escapes as a throw
```

A DoH timeout/error escapes the `throws` function instead of becoming `inconclusive`. Same on
`Windows/Syxx.Kit/DNSResolver.cs:207-227`.

**Why.** Protocol §1 — a transport failure must render `inconclusive`, never bubble up where a caller
could treat "threw" as "insecure" or crash the run.

**Fix — proposed, modelled on the loopback branch above:**

```swift
// — proposed: wrap the DoH call in the SAME shape the loopback branch already uses
do {
    let records = try await queryDoH(name: query.name, type: query.type, url: dohURL)
    return SecureResolutionResponse(records: records, isSecure: true, provenance: "DoH (Cloudflare)", isInconclusive: false, reason: nil)
} catch {
    return SecureResolutionResponse(records: [], isSecure: false, provenance: "DoH (Cloudflare)",
                                    isInconclusive: true, reason: "Secure transport could not be verified: \(error.localizedDescription)")
}
```

**Verify.** Point at an unreachable DoH host → `isInconclusive == true`, no throw. **Effort.** S.

### CF-05 — GUI must not print ✅ on a degraded baseline (CUR-004, Mirror)

**Reality — extracted.** The CLI is honest; the GUI is not.

```swift
// — extracted: macOS/Sources/SyxxCLI/SyxxCLI.swift:227-228  (honest — copy this)
if result.isDegraded {
    print("⚠️  No tampering detected, but the baseline was DEGRADED.")
```

```swift
// — extracted: macOS/Sources/SyxxApp/AppModel.swift:235  (misleading — ✅ + "No tampering")
else if result.isDegraded { verdict = "✅ No tampering (baseline degraded — some resolvers failed)" }
```

The ✅ + "No tampering" prefix reads as *secure* to a novice, contradicting the caveat.

**Fix — proposed, modelled on the CLI wording at `SyxxCLI.swift:227-228`:**

```swift
// — proposed
else if result.isDegraded { verdict = "⚠️ Inconclusive-leaning — baseline degraded (some resolvers failed); not a clean bill of health." }
```

`VantageComparer`'s data is already correct; this is presentation only.

**Verify.** Force one public resolver to fail in the GUI path → ⚠️, never ✅. **Effort.** S.

### CF-06 — No verdict on a truncated / unusable baseline (CUR-003, Mirror/Portal)

**Reality — extracted.** Baselines are selected by record count, so a truncated response
(`DidFail=true`, 0 records) is silently dropped, yet if even one *other* resolver returned records a
redirection verdict still issues:

```csharp
// — extracted: Windows/Syxx.Kit/VantageComparer.cs:72-73 (macOS VantageComparer.swift:~122 is identical)
var publicBaselines = publicOutcomes.Values.Where(outcome => outcome.Records.Count > 0).ToList();
bool anyPublicBaseline = publicBaselines.Count > 0;
```

**Why.** Efficacy + §1 — a "malicious redirection" verdict off a single surviving vantage is the
"compare against a resolver the attacker may control" failure mode.

**Fix — proposed.** Treat truncation as first-class; require a **minimum viable baseline** (≥2
independent public vantages with usable records) before any redirection verdict, else
`degraded`/`inconclusive`. Optionally re-query truncated responders over TCP first.

**Verify.** 2 of 3 public resolvers truncated → `inconclusive/degraded`, not redirection. **Effort.**
S–M. **Parity:** both platforms.

---

## Part III — Efficacy feature-completions

### CF-07 — `syxx-route`: perform the path-anomaly analysis it advertises (CUR-006, Portal)

**Reality — extracted.** The binary prints raw traceroute verbatim; there is no analysis:

```swift
// — extracted: macOS/Sources/SyxxRoute/SyxxRoute.swift:50-57
print("\nTrace Route Hops:")
for line in lines {
    let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
    if !trimmed.isEmpty { print("  \(trimmed)") }
}
```

Yet `PortalView.swift:18` claims it "geolocates route hops … to find path anomalies … highlighting
where transparent proxies or middleboxes intercept queries." No Windows `SyxxRoute` exists.

**Why.** Efficacy + §6 — labelling raw traceroute as "path-anomaly analysis" is a capability claim
the code doesn't back.

**Fix — proposed (A4 made real).** Parse hops (index, RTTs, IP, name, `*`); classify each IP with the
**pre-existing `IPClassifier`** (already used at `DNSResolver.swift:217` and `SyxxCLI.swift:220`),
flagging the first unexpected private hop as a possible transparent middlebox; persist a
per-destination baseline and diff subsequent runs for route shift. Until then, **relabel** the output
honestly so the screen text stops overclaiming.

**Verify.** Two runs to a known host + an injected extra-hop fixture → "route shift"; early private
hop flagged. **Effort.** M. **Parity:** add a Windows equivalent or drop the cross-platform claim.

### CF-08 — DNSSEC: real DS→DNSKEY→RRSIG chain validation (A2, Prophet)

**Reality — extracted.** `DNSSECVerifier.checkStatus` checks record **presence** + RRSIG validity
window, no cryptography — and is honest about it (so this is *incomplete*, not false-secure):

```swift
// — extracted: macOS/Sources/SyxxKit/DNSSECVerifier.swift (status is explicitly "not cryptographically verified")
/// DNSKEY and RRSIG records are present and at least one RRSIG is within its validity
/// window, but the signatures were not cryptographically verified by this client.
case recordsPresentUnverified
```

One sub-bug: the back-compat `verifyDNSSEC(...) -> Bool` returns `true` for
`signatureExpiredOrNotYetValid`, conflating a stale/replay signal with "OK."

**Why.** Efficacy — Prophet cannot actually attest DNSSEC without verifying signatures; today it can
only say "records exist." The `rrsigValidityWindow` parser already in the file is the seam to build
on.

**Fix — proposed.** Canonicalize the RRset (RFC 4034 §6); verify the RRSIG over it with the DNSKEY
public key per algorithm (ECDSAP256SHA256=13 and ED25519=15 via **CryptoKit** — already linked, see
CF-01; RSASHA256=8 via Security.framework); authenticate the DNSKEY against the parent **DS** hash
and chain DS→DNSKEY to the pinned IANA **root** KSK. Return typed
`secureValidated`/`bogus`/`insecure`/`inconclusive`; fix the bool so expired ≠ valid.

**Verify.** `cloudflare.com` → validated; `dnssec-failed.org` → bogus; unsigned → insecure.
**Effort.** L. **Parity:** both platforms.

### CF-09 — Collector egress attestation (A3, Glutton)

**Reality — extracted.** The collector attests which resolver IPs were *observed*, and
`leaked = len(resolvers) > 0` — not the resolver's egress interface, upstream, protocol, or timing:

```python
# — extracted: Server/syxx_server/leak.py:70-75
resolvers = sorted({o["resolver_ip"] for o in obs})
result = { "token": label, "zone": rec["zone"], "issuedAt": rec["issued_at"],
           "leaked": len(resolvers) > 0, "observedResolvers": resolvers, ... }
```

A hostile resolver could *claim* forwarding it didn't do, and the collector can't tell. (Also depends
on CF-01: the attestation must be Ed25519-signed to be worth anything.)

**Fix — proposed (design-first).** Bind observations to more independent facts (source IP + interface
at the collector, protocol/port, first-seen timing, canary-label↔token linkage); stretch goal: have
cooperating resolvers sign their own egress observation. Sign the enriched attestation with the
collector Ed25519 key (CF-01) and publish the public key.

**Verify.** Signed enriched attestation verifies with the public key; a forged `observedResolvers`
fails. **Effort.** L (server + protocol).

---

## Part IV — Ledger / doc reconciliation

### CF-10 — Make `DESIGN.md` and the ledgers tell the truth

- **DZ-1** — the determinative engine (`SynthesisEngine`, `Conclusion`/`WeightRubric`,
  `MonitorReportAnalyzer`) is **absent from `DESIGN.md`'s map and runtime table**, though it's the
  layer Delta-of-the-Week #1 is about. Add it.
- **DZ-2** — §4 says leak is "**fully implemented … None open**" *and* that A3 "would close the above
  gap." CF-01/CF-09 confirm there **is** a gap. Resolve the contradiction.
- **DZ-3** — F1/F2/F3 (§1) and F9 (§5) are listed **Proposed** while adjacent comments say they
  shipped. Remove them from the Proposed lines (as §2 already did for F5/F6/F8).
- **DZ-4** — per-discipline **Shipped** bullets, the runtime table, and the HTML comments disagree
  about which probes shipped. Reconcile to one source of truth.
- **DZ-5** — docs say `syxx-cages` (plural); the built target is **`syxx-cage`** (singular,
  `Package.swift:21`); stale `syxx-cages`/`syxx-cap` binaries linger in `.build/` (`swift build`
  won't remove them — note as a Gotcha). Pick the canonical name.
- **Stale-open** — `DESIGN.md`/ROADMAP list **CUR-001** and **CUR-005** as open, but the code already
  rejects QR=0 (`DNSResolver.swift:633-637`) and preserves tri-state (`NetworkStateReport`,
  `SyxxCLI.swift:496-502`). Close them.
- **False-done → NOW TRUE** — §5 "capture … done" (CF-02) and §6 "witness-signing hardened" (CF-01)
  were not true in the adversarial sense when written. Both have since landed: CF-01 Ed25519 in
  `bedc0c9`; CF-02 libpcap capture in `91bae82` + `3357d0b`, verified capturing real traffic.

I cannot edit `AGENTS.md`; note that its canonical CLI list omits `syxx-lan`/`syxx-forensics`/
`syxx-sentinel` (which exist) and still says `syxx-cages` — for the user to reconcile.

---

## Appendix A — Research sources

1. Raw sockets & UDP on BSD/macOS — [Apple Developer Forums: raw socket recvfrom](https://developer.apple.com/forums/thread/724580); [raw(7)](https://man7.org/linux/man-pages/man7/raw.7.html)
2. macOS capture options (BPF/libpcap/NEFilterDataProvider) — [tcpdump/Wireshark on macOS](https://inventivehq.com/knowledge-base/macos/how-to-capture-network-traffic-macos-tcpdump); [NEFilterDataProvider](https://developer.apple.com/documentation/networkextension/nefilterdataprovider); [Filtering DNS via Network Extension](https://developer.apple.com/forums/thread/708327)
3. libpcap DNS sniffer sequence & `udp port 53` filter — [thentenaar/simple-dns-sniffer](https://github.com/thentenaar/simple-dns-sniffer); [Building a packet sniffer with libpcap](https://aidanvidal.github.io/posts/Packet_Sniffer.html)
4. Dempster–Shafer combination rule, Bel/Pl, mass on Θ — [Wikipedia: Dempster–Shafer theory](https://en.wikipedia.org/wiki/Dempster%E2%80%93Shafer_theory); [GeeksforGeeks: DST](https://www.geeksforgeeks.org/machine-learning/ml-dempster-shafer-theory/)
5. HMAC forgeable by any key-holder; not non-repudiable — [Digital Signatures, HMACs and Hashing](https://maxwelldulin.com/BlogPost/Digital-Signatures-HMACs-and-Hashing); [WorkOS: HMAC vs RSA vs ECDSA](https://workos.com/blog/hmac-vs-rsa-vs-ecdsa-which-algorithm-should-you-use-to-sign-jwts)
6. Ed25519 asymmetric signing / non-repudiation — [cryptography.io Ed25519](https://cryptography.io/en/latest/hazmat/primitives/asymmetric/ed25519/)
7. Overconfidence with missing/partial data — [Modeling Individual-Level Uncertainty From Missing Data](https://ascopubs.org/doi/10.1200/PO-25-00852); [IBM: Uncertainty quantification](https://www.ibm.com/think/topics/uncertainty-quantification)
8. Bayesian vs DST for combining sources — [Bayesian and Dempster-Shafer models for combining multiple sources](https://arxiv.org/pdf/2104.07440)
9. DNSSEC chain of trust (DS/DNSKEY/RRSIG) — [Cloudflare: How DNSSEC works](https://www.cloudflare.com/learning/dns/dnssec/how-dnssec-works/); [Akamai: DNSSEC](https://www.akamai.com/blog/trends/dnssec-how-it-works-key-considerations)
10. Validating DNSSEC with dig/delv — [oneuptime: validate DNSSEC](https://oneuptime.com/blog/post/2026-01-15-validate-dnssec-signatures-dig-delv/view)

## Appendix B — Verification matrix (prove each fix on real input)

| Fix | Real-run proof it works |
|-----|--------------------------|
| CF-01 | Sign on A, verify on B with public key only; tamper 1 byte → `tampered`; public-key holder cannot forge. |
| CF-02 | Capture while `dig` runs → records > 0; no traffic → **inconclusive** report; `-d 3`/`3s` ≈ 3s. |
| CF-03 | Empty → conf ≈ 0; one 2.0 signal → below threat threshold / refers; 3 agreeing decisive → high; conflict → conf drops + `K` shown. |
| CF-04 | Unreachable DoH → `isInconclusive`, no throw. |
| CF-05 | GUI degraded path → ⚠️, never ✅. |
| CF-06 | 2/3 public resolvers truncated → `inconclusive/degraded`, not redirection. |
| CF-07 | Injected extra hop fixture → "route shift"; early private hop flagged. |
| CF-08 | `cloudflare.com` → validated; `dnssec-failed.org` → bogus; unsigned → insecure. |
| CF-09 | Signed enriched attestation verifies with public key; forged `observedResolvers` fails. |
| CF-10 | `DESIGN.md`/ROADMAP diff shows engine added, contradictions gone, CUR-001/005 closed, false-dones downgraded. |

# CL-1/CL-2/CL-3 Implementation Plan — syxx-neural-planner

**Generated:** 2026-07-11 · **Agent:** syxx-neural-planner  
**Spec:** [`HANDOFF-CLINE.md`](HANDOFF-CLINE-IMPLEMENTATION.md) · **Master:** [`REASONING-SYSTEM-DESIGN.md`](REASONING-SYSTEM-DESIGN.md)  
**Status:** Plan ready for syxx-neural-executor

---

## Executive Summary

This plan implements three work items that feed the determinative core:

1. **CL-1 — Signed GroundTruth is decisive-grade** (Server + client trust consumers)
2. **CL-2 — Probe robustness** (DNSCanaries + WildcardSynthesisProbe hardening)
3. **CL-3 — Labeled adversarial capture corpus + Phase-0 harness**

**Current State:** No Ed25519 signing, no GroundTruth endpoint, no freshness/binding/quorum logic exists in the server. CL-1 is completely greenfield. CL-2 requires hardening existing probes. CL-3 requires building corpus infrastructure from scratch.

**Execution Order:** CL-1 → CL-2 → CL-3 (per HANDOFF-CLINE.md directive)

---

## CL-1 — Signed GroundTruth is Decisive-Grade

### Requirements (from HANDOFF-CLINE.md)

A GroundTruth may only be treated as **decisive** when it carries:

1. **Signed provenance** — Ed25519 signature over the payload
2. **Freshness** — timestamp/nonce, bounded age (e.g., ≤5 minutes)
3. **Domain/record binding** — signature covers qname+qtype+answer
4. **Authenticated authoritative acquisition** — server queried authoritative nameservers directly
5. **Quorum** — ≥2 independent authoritative IPs agree
6. **Replay protection** — nonce or timestamp window prevents replay attacks

**Done bar:** A replayed/stale/wrong-domain-bound signed payload is rejected as non-decisive on a **real run**; a genuine fresh quorum-backed signed answer qualifies; mutation removing the freshness or binding check REDs a test.

### Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│ Server: /api/authoritative endpoint                                  │
│   - Accepts: domain, qtype (A/AAAA)                                  │
│   - Queries: ≥2 authoritative nameservers (from zone NS records)     │
│   - Validates: quorum agreement, freshness, binding                  │
│   - Signs: Ed25519 over (domain, qtype, answers, timestamp, nonce)   │
│   - Returns: {answers, signature, pubkey, timestamp, nonce, quorum}  │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Client: DimensionClient.{swift,cs}                                   │
│   - Fetches: /api/authoritative                                      │
│   - Verifies: signature, freshness, binding, quorum                  │
│   - Emits: GroundTrustLevel enum (decisive/standard/inconclusive)    │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Client: WitnessSigner.{swift,cs}                                     │
│   - Consumes: GroundTrustLevel from DimensionClient                  │
│   - Signs: evidence with trust level metadata                        │
│   - Ensures: decisive acts require decisive-grade GroundTruth        │
└─────────────────────────────────────────────────────────────────────┘
```

### Implementation Steps

#### Step 1.1 — Server: Ed25519 signing infrastructure

**Files:** `Server/syxx_server/signing.py` (new), `Server/syxx_server/config.py`

**Do:**

- Generate Ed25519 keypair on first run, persist to `~/.syxx/server_ed25519.pem`
- Load private key at startup, expose public key via `/api/pubkey`
- Implement `sign_groundtruth(domain, qtype, answers, timestamp, nonce)` → signature bytes
- Implement `verify_signature(pubkey, message, signature)` → bool

**Config additions:**

```python
# config.py
ED25519_KEY_PATH = _s("SYXX_ED25519_KEY_PATH", os.path.join(BASE_DIR, "server_ed25519.pem"))
GROUNDTRUTH_MAX_AGE_SECONDS = _i("SYXX_GROUNDTRUTH_MAX_AGE", 300)  # 5 minutes
GROUNDTRUTH_MIN_QUORUM = _i("SYXX_GROUNDTRUTH_MIN_QUORUM", 2)
```

**Done bar:** `/api/pubkey` returns the server's Ed25519 public key; signing/verification round-trips correctly.

#### Step 1.2 — Server: Authoritative nameserver discovery + quorum query

**Files:** `Server/syxx_server/resolver.py` (extend), `Server/syxx_server/authoritative.py` (new)

**Do:**

- Implement `discover_authoritative_nameservers(domain)` → list of NS IPs
  - Query NS records for the domain's zone
  - Resolve NS hostnames to IPs
  - Return ≥2 IPs (or fail if zone has <2 NS records)
- Implement `query_authoritative_quorum(domain, qtype, ns_ips)` → (answers, quorum_met)
  - Query each NS IP directly (bypass recursive resolvers)
  - Compare answer sets
  - `quorum_met = len(agreeing_ips) >= GROUNDTRUTH_MIN_QUORUM`

**Done bar:** For `cloudflare.com`, discovers NS records, queries ≥2 authoritative IPs, returns quorum status.

#### Step 1.3 — Server: `/api/authoritative` endpoint

**Files:** `Server/syxx_server/app.py` (add route)

**Do:**

- Route: `GET /api/authoritative?domain=example.com&qtype=A`
- Logic:
  1. Discover authoritative nameservers
  2. Query quorum
  3. Generate timestamp (ISO-8601) and nonce (UUID4)
  4. Build message: `domain|qtype|answers_json|timestamp|nonce`
  5. Sign with Ed25519
  6. Return JSON:

     ```json
     {
       "domain": "example.com",
       "qtype": "A",
       "answers": ["93.184.216.34"],
       "timestamp": "2026-07-11T22:00:00Z",
       "nonce": "550e8400-e29b-41d4-a716-446655440000",
       "signature": "base64...",
       "pubkey": "base64...",
       "quorum": {
         "nameservers": ["1.1.1.1", "1.0.0.1"],
         "agreeing": 2,
         "met": true
       },
       "trustLevel": "decisive"
     }
     ```

- **Trust level logic:**
  - `decisive` if: signature valid AND freshness ≤ max_age AND binding covers domain+qtype+answers AND quorum met
  - `standard` if: signature valid but quorum not met OR freshness borderline
  - `inconclusive` if: signature invalid OR freshness expired OR binding mismatch

**Done bar:** Real query to `/api/authoritative?domain=cloudflare.com&qtype=A` returns signed, quorum-backed GroundTruth with `trustLevel: "decisive"`.

#### Step 1.4 — Client: DimensionClient.{swift,cs}

**Files:** `macOS/Sources/SyxxKit/DimensionClient.swift` (new), `Windows/Syxx.Kit/DimensionClient.cs` (new)

**Do:**

- Implement `fetchGroundTruth(domain, qtype)` → `GroundTruth?`
- Implement `verifyGroundTruth(gt)` → `GroundTrustLevel`
  - Verify Ed25519 signature over (domain, qtype, answers, timestamp, nonce)
  - Check freshness: `now - timestamp ≤ maxAge`
  - Check binding: signature covers the exact domain+qtype+answers
  - Check quorum: `gt.quorum.met == true`
- Emit tri-state:

  ```swift
  enum GroundTrustLevel: String, Codable {
      case decisive      // all checks pass
      case standard      // signature valid but quorum/freshness borderline
      case inconclusive  // signature invalid, expired, or binding mismatch
  }
  ```

**Done bar:** Client fetches GroundTruth, verifies all six properties, emits correct trust level.

#### Step 1.5 — Client: WitnessSigner integration

**Files:** `macOS/Sources/SyxxKit/WitnessSigner.swift` (extend), `Windows/Syxx.Kit/WitnessSigner.cs` (extend)

**Do:**

- Add `groundTrustLevel: GroundTrustLevel` to evidence metadata
- Ensure decisive acts (`authoritativeDivergesConfirmed`) require `groundTrustLevel == .decisive`
- Sign evidence with trust level included in the signed payload

**Done bar:** Evidence signed by WitnessSigner includes trust level; decisive acts rejected if trust level is not decisive.

#### Step 1.6 — Tests: Failure cases

**Files:** `Server/tests/test_groundtruth.py` (new), `macOS/Tests/GroundTruthTests.swift` (new), `Windows/Syxx.Tests/GroundTruthTests.cs` (new)

**Do:**

- Test replay attack: same signature+nonce+timestamp submitted twice → second rejected
- Test stale timestamp: timestamp > max_age → `inconclusive`
- Test wrong domain binding: signature for `example.com` used for `evil.com` → `inconclusive`
- Test quorum failure: only 1 of 2 NS IPs agrees → `standard` (not decisive)
- Test signature mutation: flip one bit in signature → `inconclusive`

**Done bar:** All failure cases RED (test fails) before implementation; all GREEN after.

---

## CL-2 — Probe Robustness

### Requirements (from HANDOFF-CLINE.md)

1. **NXDOMAIN (structural):** A resolver returning **any** answer for a provably-nonexistent name is a hijack (RFC 8020), independent of whether the IP matches a known block page.
2. **DNSSEC (quorum + DO-bit):** Replace single hardcoded `cloudflare.com` control with multi-control quorum and gate on DO bit (RFC 4035/6840).
3. **Wildcard reference (encrypted transport):** Reference lookup must not ride plain-UDP; use encrypted-transport reference or map to inconclusive.

**Done bar:** Real round-robin block page → hijack (Positive); benign single DNSSEC lapse → not-decisive; real transparent Do53 proxy → wildcard no longer reads benign. Parity across both platforms.

### Implementation Steps

#### Step 2.1 — DNSCanaries: Structural NXDOMAIN rule (RFC 8020)

**Files:** `macOS/Sources/SyxxKit/DNSCanaries.swift` (modify), `Windows/Syxx.Kit/DNSCanaries.cs` (modify)

**Do:**

- Current logic: NX canary checks if returned IP matches known block page IPs
- New logic: **Any** A/AAAA record for a provably-nonexistent name is a hijack
  - Generate canary domain: `nx-canary-{random}.syxx.app` (guaranteed nonexistent)
  - Query resolver
  - If resolver returns **any** A/AAAA record → `nxCanaryHijack` (Positive)
  - If resolver returns NXDOMAIN (rcode=3) → clean
  - If resolver returns SERVFAIL/timeout → inconclusive

**Done bar:** Real round-robin block page (different IPs per query) still triggers hijack detection.

#### Step 2.2 — DNSCanaries: Multi-control DNSSEC quorum + DO-bit gate

**Files:** `macOS/Sources/SyxxKit/DNSCanaries.swift` (modify), `Windows/Syxx.Kit/DNSCanaries.cs` (modify)

**Do:**

- Current logic: Single hardcoded `cloudflare.com` DNSSEC canary
- New logic:
  - Query ≥3 DNSSEC-signed domains (e.g., `cloudflare.com`, `dnssec-deployment.org`, `verisigninc.com`)
  - For each, check if RRSIG records are present
  - Set DO bit (DNSSEC OK) in query (RFC 4035 §3.1.4, RFC 6840 §5.6)
  - Quorum: `dnssecCanaryStripped` is decisive only if ≥2 of 3 controls show stripping
  - Single control lapse → `standard` (not decisive)

**Done bar:** Benign single DNSSEC lapse (one control temporarily unsigned) does not fire decisive stripping.

#### Step 2.3 — WildcardSynthesisProbe: Encrypted-transport reference

**Files:** `macOS/Sources/SyxxKit/WildcardSynthesisProbe.swift` (modify), `Windows/Syxx.Kit/WildcardSynthesisProbe.cs` (modify)

**Do:**

- Current logic: Reference lookup uses plain-UDP (vulnerable to transparent Do53 proxy rewrite)
- New logic:
  - Use DoH (DNS-over-HTTPS) or DoT (DNS-over-TLS) for reference lookup
  - If encrypted transport unavailable, map `wildcardZone` to **inconclusive** (never benign clean-support)
  - Never emit `.negative` (clean-support) unless encrypted reference confirms wildcard behavior

**Done bar:** Real transparent Do53 proxy no longer causes wildcard to read as benign.

#### Step 2.4 — Tests: Failure cases

**Files:** `macOS/Tests/DNSCanariesTests.swift` (extend), `Windows/Syxx.Tests/DNSCanariesTests.cs` (extend)

**Do:**

- Test round-robin block page: different IPs per query → still hijack
- Test single DNSSEC lapse: 1 of 3 controls unsigned → not decisive
- Test DO bit: query without DO bit → inconclusive (not decisive)
- Test wildcard with plain-UDP reference: transparent proxy rewrites → inconclusive (not benign)

**Done bar:** All failure cases RED before implementation; all GREEN after.

---

## CL-3 — Labeled Adversarial Capture Corpus + Phase-0 Harness

### Requirements (from HANDOFF-CLINE.md)

Build a labeled corpus with **≥1 real capture per mechanism class**:

- Poisoned-local
- Transparent-proxy
- Captive-portal
- DNSSEC-strip
- Benign-CDN/geo divergence
- Benign-captive-portal

Build the **shared golden-vector runner** that replays `(evidence, sectionStatuses) → {verdict,confidence,belief}` identically on macOS and Windows and fails CI on divergence.

**Done bar:** The corpus reproduces every NE1–NE7 failing scenario as a **red** golden vector on both platforms; the runner executes on a real Windows host and a real Mac.

### Implementation Steps

#### Step 3.1 — Lab harness: dnsmasq + CoreDNS adversarial rigs

**Files:** `corpus/lab/dnsmasq.conf` (new), `corpus/lab/coredns.yml` (new), `corpus/lab/README.md` (new)

**Do:**

- **NX-hijack rig:** dnsmasq configured to return sinkhole IP for nonexistent domains
- **DNSSEC-strip rig:** CoreDNS configured to strip RRSIG records
- **Transparent-proxy rig:** iptables + dnsmasq to simulate on-path interception
- **Captive-portal rig:** dnsmasq + nginx to return captive portal page for all queries

**Done bar:** Each rig produces reproducible adversarial DNS behavior.

#### Step 3.2 — Capture collection: Real adversarial traces

**Files:** `corpus/captures/` (new directory), `corpus/captures/README.md` (new)

**Do:**

- Run Syxx against each lab rig
- Capture: raw DNS packets (pcap), parsed evidence (JSON), section statuses (JSON)
- Label each capture with mechanism class:

  ```json
  {
    "captureId": "nx-hijack-001",
    "mechanismClass": "NXDOMAIN_HIJACK",
    "timestamp": "2026-07-11T22:00:00Z",
    "labRig": "dnsmasq-nx-hijack",
    "evidence": [...],
    "sectionStatuses": [...]
  }
  ```

- Collect ≥1 capture per mechanism class (6 classes × 1 capture = 6 minimum)

**Done bar:** Corpus contains ≥6 labeled captures, one per mechanism class.

#### Step 3.3 — Golden-vector runner: Shared test harness

**Files:** `corpus/golden-vectors/runner.py` (new), `corpus/golden-vectors/schema.json` (new)

**Do:**

- Implement Python runner that:
  1. Loads golden vector JSON: `{evidence, sectionStatuses, expected: {verdict, confidence, belief}}`
  2. Invokes macOS `SynthesisEngine.synthesize()` via subprocess or FFI
  3. Invokes Windows `SynthesisEngine.Synthesize()` via subprocess or FFI
  4. Compares outputs: verdict, confidence, belief distribution
  5. Fails CI if platforms diverge or output doesn't match expected

**Schema:**

```json
{
  "vectorId": "nx-hijack-001",
  "evidence": [
    {"evidenceId": "nxCanaryHijack", "section": "prophet", "weight": 3.0}
  ],
  "sectionStatuses": [
    {"section": "prophet", "status": "positive"}
  ],
  "expected": {
    "verdict": "spoofing",
    "confidencePct_exact": 95,
    "belThreatSet_eps6": 0.95
  }
}
```

**Done bar:** Runner executes on macOS and Windows; fails CI if platforms diverge by >1% on any gate value.

#### Step 3.4 — NE1–NE7 failing scenarios: Red vectors

**Files:** `corpus/golden-vectors/ne1-nx-washout.json` (new), `corpus/golden-vectors/ne2-dnssec-strip.json` (new), etc.

**Do:**

- Translate each NE1–NE7 failing scenario into a golden vector
- Expected behavior: current engine **fails** (red vector) because it produces wrong verdict
- After Claude's engine remediation, vectors should pass (green)

**Example (NE1 Δ1 — confirmed threat washed to CLEAN):**

```json
{
  "vectorId": "ne1-delta1-nx-washout",
  "description": "Confirmed DNSSEC strip washed to CLEAN by 3 clean sections",
  "evidence": [
    {"evidenceId": "dnssecCanaryStripped", "section": "prophet", "weight": 3.0},
    {"evidenceId": "cleanSupport", "section": "mirror", "weight": 2.0},
    {"evidenceId": "cleanSupport", "section": "glutton", "weight": 2.0},
    {"evidenceId": "cleanSupport", "section": "witness", "weight": 2.0}
  ],
  "sectionStatuses": [
    {"section": "prophet", "status": "positive"},
    {"section": "mirror", "status": "negative"},
    {"section": "glutton", "status": "negative"},
    {"section": "witness", "status": "negative"}
  ],
  "expected": {
    "verdict": "REWRITE",
    "confidencePct_exact": 95,
    "belThreatSet_eps6": 0.95
  },
  "currentEngineBehavior": "CLEAN at 74% (WRONG — should be REWRITE)"
}
```

**Done bar:** All NE1–NE7 failing scenarios reproduced as red vectors; runner reports them as failures.

---

## Dependency Graph

```
CL-1.1 (Server signing infra)
  ↓
CL-1.2 (Authoritative discovery + quorum)
  ↓
CL-1.3 (/api/authoritative endpoint)
  ↓
CL-1.4 (DimensionClient)
  ↓
CL-1.5 (WitnessSigner integration)
  ↓
CL-1.6 (Tests)

CL-2.1 (NXDOMAIN structural rule)
  ↓
CL-2.2 (DNSSEC quorum + DO-bit)
  ↓
CL-2.3 (Wildcard encrypted transport)
  ↓
CL-2.4 (Tests)

CL-3.1 (Lab harness)
  ↓
CL-3.2 (Capture collection)
  ↓
CL-3.3 (Golden-vector runner)
  ↓
CL-3.4 (NE1–NE7 red vectors)
```

**Cross-dependencies:**

- CL-1.3 depends on CL-1.1 and CL-1.2
- CL-2.2 depends on CL-1.2 (quorum logic reused)
- CL-3.2 depends on CL-2 (probes must be hardened before capturing)

---

## Acceptance Criteria (Real-Run Bar)

### CL-1 Acceptance

1. **Real run:** `curl https://syxx-server/api/authoritative?domain=cloudflare.com&qtype=A` returns signed, quorum-backed GroundTruth with `trustLevel: "decisive"`
2. **Replay attack:** Submit same payload twice → second rejected
3. **Stale timestamp:** Submit payload with timestamp > 5 minutes old → `inconclusive`
4. **Wrong domain binding:** Submit signature for `example.com` with domain=`evil.com` → `inconclusive`

### CL-2 Acceptance

1. **Real round-robin block page:** Query resolver that returns different IPs per query → still hijack (Positive)
2. **Benign single DNSSEC lapse:** One control temporarily unsigned → not decisive (standard)
3. **Real transparent Do53 proxy:** Wildcard reference over plain-UDP → inconclusive (not benign)

### CL-3 Acceptance

1. **Corpus completeness:** ≥6 labeled captures, one per mechanism class
2. **Runner parity:** macOS and Windows produce identical verdicts on all golden vectors
3. **NE1–NE7 reproduction:** All failing scenarios reproduced as red vectors

---

## Risk Register

| Risk | Mitigation |
|---|---|
| Ed25519 library not available on Windows | Use `NSec.Cryptography` (pure C#) or `BouncyCastle` |
| Authoritative NS discovery fails for some domains | Fallback to recursive resolver with `AA` bit check |
| Lab harness doesn't reproduce real-world adversarial behavior | Supplement with public datasets (OONI, Censored Planet) |
| Golden-vector runner can't invoke both platforms from one host | Use CI matrix (GitHub Actions: macos-latest + windows-latest) |

---

## Next Steps

**syxx-neural-executor** should:

1. Begin CL-1.1 (Server signing infrastructure)
2. Batch 25–30 tool calls per AGENTS.md
3. Update CHANGELOG.md after each CL item
4. Report network/dependency failures immediately

**Plan approved for execution.**

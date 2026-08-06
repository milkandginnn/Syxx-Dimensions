# Stage-0 Golden-Vector Contract — the shared fixture format (Claude ↔ Cline)

**Generated:** 2026-07-11 · Derived from `REASONING-SYSTEM-DESIGN` v2.1 §4/§10 + decisions D2/O3, R4, R8 · Owner of the *schema*: Claude (engine). Builder of the *runner + real captures*: Cline (CL-3).

**What this is:** the format for the labeled corpus + the cross-platform golden-vector runner Cline builds in CL-3, and the contract the engine (Claude) is scored against. Build the corpus and the macOS/Windows runner to **exactly this schema**. Claude's engine, once landed, must turn every RED seed vector GREEN without changing the schema.

## Vector shape (one fixture)

```jsonc
{
  "id": "NE1-d1-dnssec-strip-washed-to-clean",
  "label": "dnssec_strip",                 // mechanism-class label (see classes below)
  "capture": "real|synthetic",             // >=1 REAL capture required per class (design O1)
  "greensAtPhase": 1,                       // phase at which this vector's _exact assertions must GO GREEN; Phase-1 CI gates only on greensAtPhase<=1
  "expectedRedUntil": "phase3",             // naming/confidence vectors (NE1 Δ1 REWRITE, NE1 Δ2 inversion) stay RED until the fusion pass — but NEVER because they emit a false-clean (see invariant)
  "input": {
    "evidence": [                          // R4: each item carries FULL LINEAGE, not just act class
      { "actClass": "dnssec_strip",
        "source": "prophet", "vantage": "local",
        "rawObsLineage": "canary:dnssec:cloudflare.com:RRSIG-absent",
        "probeControl": "DNSCanaries.dnssecControl",
        "clusterId": "prophet:local:canary",   // R4 lineage cluster: SAME clusterId => combine CAUTIOUSLY (idempotent, no over-count); DIFFERENT => Dempster (independent). Assign per {source,vantage,rawObsLineage,probeControl}. Lets the runner assert within- vs across-cluster combination for SV-1/SV-2.
        "status": "positive" }
    ],
    "sectionStatuses": { "prophet":"positive","mirror":"negative","portal":"negative",
                         "glutton":"untested","cage":"negative","witness":"negative" },
    "unobserved": [ { "section":"glutton","datapoint":"leak","reason":"untested" } ]
  },
  "expected": {
    "verdict": "REWRITE",                  // MechanismClass | "inconclusive" | "referral"
    "withinClass": ["spoofing","interceptionOnPath"],
    "confidence": 0.95,                    // Double | null (null = no-data, NOT 0)  [D2/R8]
    "completeness": { "k": 5, "m": 6 },    // separate dimension, never a Bel multiplier [D3]
    "conflict": null, "uncertainty": 0.05,
    "hardGate": null,
    "belief": { "REWRITE": 0.95 }          // optional, for the _epsN tier only
  }
}
```

## Two assertion tiers (D2 / O3 — this is the load-bearing determinism rule)

- **`_exact` tier — the GATES.** `verdict`, `withinClass` membership, `referral`, and `completeness` are asserted with **exact equality on rounded integer percentages** — NO epsilon. Confidence is compared as `round(100·x)` integer. A vector fails CI if any platform's gate differs by one integer.
- **`_epsN` tier — display values only.** Non-gate numeric fields (`belief[]`, raw `uncertainty`/`conflict`) may differ within tolerance **`ε`**. **`ε` is NOT pre-set** — it is an **output of the Phase-0 fusion-rule + determinism bake-off** (measured for whichever rule wins; R5 defers rule selection). Until the bake-off runs, mark `_epsN` assertions `pending-bakeoff`.

## Mechanism-class labels (the corpus must cover each, ≥1 real capture)

`REWRITE` (poisoned-local / on-path spoof) · `BLOCK` (censorship / NXDOMAIN policy) · `TRANSPORT` (transparent-proxy / DoH downgrade) · `CONTENT` (Warp injection) · `LEAK` (VPN DNS leak) · `HOST` (local compromise) — plus **benign controls**: `benign_cdn` (multi-homed/geo divergence) and `benign_captive` (portal, no tampering). Benign vectors must expect **clean/inconclusive**, never a threat class.

## Seed corpus = the NE1–NE7 failing scenarios as RED vectors

Encode each confirmed defect as a fixture whose `expected` is the **design-correct** output and whose **current-engine output is wrong** (so it REDs today, GREENs when the fix lands). Minimum seed set:

- NE1 Δ1 wash-to-CLEAN (above) · NE1 Δ2 two-mechanism confidence inversion (74→31) · NE2 Δ3 clean-support conflation · NE2 Δ5 / NE7 Δ2 invalidated laundered to negative · NE7 Δ1 critical-inconclusive→CLEAN · NE1 Δ4 / NE4 Δ1 captive-portal cancels confirmed redirection · NE3 Δ1 publicResolverResults dropped · NE6 Δ1 discrepancy display-only · NE5 Δ1 slow-resolver→false-censorship (macOS vs Windows divergence — a **parity** vector) · NE7 Δ5 rounding straddles the gate (boundary vector at `round(100·BetP)=74.5`).

## Never-false-secure invariant (universal — every vector, every phase)

**No vector may EVER assert a `clean`/`secure` verdict for an input that contains tampering — at any phase, red or green.** A later-phase seed whose `greensAtPhase` is the fusion pass stays RED because its *naming/confidence* isn't correct yet — but its `expected.verdict` must be `inconclusive` / `referral` / the-threat-class, **never `clean`**. The runner enforces a universal assertion: for every tampering-labelled vector (any non-benign `label`), `expected.verdict != clean` at every phase. Phase-1 CI gates on the `greensAtPhase<=1` subset; all other seeds stay red but must still satisfy this invariant. This is the honesty spine — it catches a false-clean the instant any change introduces one, independent of which phase a vector greens at.

## Runner requirements (CL-3)

- One corpus, replayed on **both** platforms (Swift + C#); any `_exact`-tier divergence **fails CI** (this is the artifact that catches NE5 Δ1 and NE7 Δ5).
- Runner reads a fixture, feeds `input` to the engine, asserts `expected` per the two tiers.
- The **expected oracle** comes from the v2.1 design until the engine lands; when Claude ships `Conclusion`/`Report`, the runner asserts against the real output.

## Lanes

- **Cline (CL-3):** builds the runner, the real captures, and the seed RED vectors to this schema. Owns `Server/**` + test-fixture files.
- **Claude (engine):** owns this schema; lands the engine so the RED vectors go GREEN; sets `ε` from the Phase-0 bake-off.
- If you need a schema field that isn't here, **flag it in your output** for Claude — do not redefine the schema unilaterally.

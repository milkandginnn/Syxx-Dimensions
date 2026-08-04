# PROPOSAL — Witness verify: crypto-first parity + combined golden vectors (2026-08-03)

Engine-lane (Claude) proposal. **No source edited; no commits.** This drafts **Phase 0**
(combined golden vectors) and **Phase 1a** (the Windows `Syxx.Kit` crypto-first reorder +
B099 keyId port) from the consolidated Witness cross-platform fix plan. Approval-gated
items are flagged; the fail-closed posture decision (Phase 1b) is recorded here but drafted
as its own gated changeset because of its self-verify blast radius.

Decisions locked by owner (2026-08-03):
1. **H1 trust posture → fail-CLOSED on empty set (Option A).** (Phase 1b, see §3.)
2. **B099 keyId-recompute parity gap → folded into Phase 1a.**
3. **Scope of this proposal → Phase 0 + Phase 1a.**

Root cause recap (confirmed, read-only debug): macOS `SyxxKit` verifies **crypto-first**
(B184 invariant, `WitnessSigner.swift:268-288`); Windows `Syxx.Kit` verifies expiry/future/
stale/trust/display **before** the Ed25519 check, which runs **last** (`WitnessSigner.cs:
260-346`). Linux/Avalonia `ProjectReference`s the Windows `Syxx.Kit`
(`SyxxUI.Avalonia.csproj:44`), so it inherits the Windows ordering — **one Kit fix repairs
both**. macOS Swift is the reference and is **not touched**.

---

## 1. Phase 1a — reorder `Windows/dotNET/Syxx.Kit/WitnessSigner.cs` `VerifySnapshotDetailed`

Single method, single file. Target = the macOS ordering (`WitnessSigner.swift:251-368`).
Behavior is **identical for every honest snapshot** — the checks are the same, only their
order changes — and differs only for adversarial input, where a non-crypto verdict currently
masks a forgery.

### Current order (crypto LAST)
| # | Check | line |
|---|---|---|
| 0 | auto-load trust set (B211) | :240 |
| 1 | MalformedInput (empty PublicKey) | :242-245 |
| 2 | UnsupportedFormat | :250-255 |
| 3 | **Expired** | :260-263 |
| 4 | **FutureDated** | :268-272 |
| 5 | **Stale** | :274-281 |
| 6 | **UntrustedSigner** | :283-286 |
| 7 | **DisplayMismatch** (B037/B098/B252/D083) | :288-332 |
| 8 | C070 empty-payload guard | :342-343 |
| 9 | **Ed25519 Verify → Valid / InvalidSignature** | :334-346 |

### Target order (crypto FIRST — mirrors macOS)
| # | Check | macOS ref |
|---|---|---|
| 0 | auto-load trust set (B211) — unchanged | :254-257 |
| 1 | UnsupportedFormat | :261-266 |
| 2 | Parse pubkey + sig + `CanonicalBytes()`; empty-PublicKey **and** C070 empty-payload → MalformedInput (one block) | :274-283 |
| 3 | **Ed25519 Verify → InvalidSignature — LOAD-BEARING, moved up** | :286-288 |
| 4 | **B099 keyId recompute → DisplayMismatch — NEW on Windows (§2)** | :293-296 |
| 5 | DisplayMismatch guard (B037/B098/B252/D083) — moved down | :302-337 |
| 6 | UntrustedSigner — moved down (see §3, becomes fail-closed) | :340-344 |
| 7 | Expired | :350-352 |
| 8 | Stale | :356-361 |
| 9 | FutureDated | :362-366 |
| — | Valid | :368 |

### Invariant comment to add above step 3 (mirrors `WitnessSigner.swift:268-271`)
> `// B184: cryptographic verification MUST precede time-based and trust checks. A wholly`
> `// fabricated snapshot with a past ExpiresAt previously returned Expired — asserting the`
> `// file is an authentic-but-lapsed document. No verdict before evidence: the signature`
> `// must be valid before any claim about the snapshot's age or trust anchor.`

**Notes**
- The empty-`PublicKey` guard (current :242) moves to *after* the format check, folding into
  the parse block with the C070 empty-payload guard — matching macOS `:274-283`. No state is
  lost; MalformedInput still covers both.
- **Linux:** no separate edit. It inherits this via `SyxxUI.Avalonia.csproj:44`. Confirm by
  running the Avalonia suite (§5).
- **macOS Swift:** unchanged — it is the correct reference.

---

## 2. B099 keyId-recompute — port to Windows (folded into 1a)

macOS rejects a relabeled snapshot by recomputing the keyId from the verified public key
(`WitnessSigner.swift:293-296`): `SHA256(pubKeyData).prefix(8)` as lowercase hex, compared to
`snapshot.keyId`. **Windows has no equivalent check** — `WitnessSnapshot.KeyId` exists
(`WitnessSigner.cs:37`, set at sign-time `:164`) but is never re-derived at verify.

Add, as step 4 (immediately after the Ed25519 verify, before the display guard):
```csharp
// B099: derive the key ID from the VERIFIED public key and reject a serialized mismatch.
// An attacker cannot relabel a valid snapshot with a different keyId without this catching it.
var recomputedKeyId = Convert.ToHexString(SHA256.HashData(pubKeyData))[..16].ToLowerInvariant();
if (!string.Equals(snapshot.KeyId, recomputedKeyId, StringComparison.Ordinal))
    return SnapshotVerification.DisplayMismatch;
```
**Confirm before landing:** the Windows sign side (`WitnessSigner.cs` ~:150-164) derives
`KeyId` with the identical `SHA256(pubKey).first-8-bytes → hex` formula as macOS `:180`
(first 8 bytes = 16 hex chars). If the sign-side derivation differs, match *it*, not macOS's
literal — the recompute must equal what the honest signer wrote. This is pure logic, low
risk, and closes a real parity gap; a dedicated in-suite test asserts a relabeled-but-valid
snapshot → `DisplayMismatch` on both platforms.

---

## 3. Phase 1b — fail-CLOSED on empty trust set (decision locked; drafted separately, gated)

Recorded here because it reshapes step 6 and GV-C, but **not bundled into 1a** — its blast
radius touches self-verify on every platform and must clear its own approval + a surface
decision first.

**The change (Kit, both platforms):** today the trust guard fires only on a *non-empty* set
(macOS `:340` `!trusted.isEmpty`; Windows `:283` `Count > 0`) and B211 maps empty→nil, so an
unconfigured deployment accepts any cryptographically valid signer (fail-open, *documented*
as intended, `WitnessSigner.cs:359-361`). Fail-closed = when **no** trust set is configured,
return `UntrustedSigner` instead of proceeding to `Valid`.

**This deliberately inverts a pinned invariant — surface it, do not silently flip it.**
`testNoTrustSetConfiguredDoesNotImplyUntrusted` (`WitnessVerificationDetailedTests.swift:
105-111`) asserts `nil` and `[]` → `.valid`. Under Option A it must assert `.untrustedSigner`.
Per honesty-spine rule 5 this is a **contract change**, not a test being softened to go green:
rewrite the test to encode the new contract with a comment citing this proposal, and add the
Windows twin. A failing honesty test is data — here the owner has changed the honest answer.

**Blast radius (the reason 1b is gated behind 1a):** self-verify paths pass no trust set and
would now read their *own* freshly-signed seal as `UntrustedSigner`:
- macOS testify self-verify `AppModel.swift:1348`; macOS/Windows/Linux GUI verify; Linux
  signs with a fresh ephemeral key (`WitnessViewModel.cs:142`) then self-verifies (`:167`).

**Required resolution before 1b lands (surface decision):** either (a) self-verify includes
the local signing public key in the effective trusted set, or (b) surfaces the
"crypto-valid + no configured trust anchor" state **distinctly and honestly** — "signature
valid; signer not in a configured trust set" (amber, not tamper-red) — never a bare green
VALID and never conflated with a forged/tampered red. This is the H1 *disclosure* work the
Windows CLI already models via `TrustPostureLabel`. It is a Copilot-lane + proposal-gated
decision; flag to that lane.

---

## 4. Phase 0 — combined golden vectors (this proposal ships these three)

Every existing test exercises expired / forged / untrusted **in isolation** (macOS
`WitnessVerificationDetailedTests.swift:46,142`; Windows `WitnessSignerTests.cs:138`), so the
ordering bug is invisible. These combine two conditions so the masking is caught. They go in
**new, explicitly-named methods** — never in the `vectorNames` "must be Valid" set
(`WitnessCrossPlatformGoldenVectorTests.swift:21`), so a forged fixture can never be folded
into the valid set.

| ID | Construction | Lives in | Verdict TODAY | Verdict AFTER 1a | Pins |
|---|---|---|---|---|---|
| **GV-A** | forged sig (`sig[0]^=0xFF`) + past `Evidence.ExpiresAt` | corpus `witness-forged-expired.json` read by both `WitnessCrossPlatformGoldenVectorTests.{cs,swift}` (new method) + in-suite twins | macOS `.invalidSignature` **vs** Win/Linux `.Expired` | all three `.invalidSignature` | C175 prong 1 |
| **GV-A′** | forged sig, fresh, `trustedPublicKeys:[otherKey]` (signer not in set) | in-suite, both detailed suites | macOS `.invalidSignature` **vs** Win/Linux `.UntrustedSigner` | all three `.invalidSignature` | C175 prong 2 (trust-before-crypto) |
| **GV-B** | honest sig over evidence, past `ExpiresAt`, a **display-only** field mutated post-sign (e.g. `Timestamp += 1m`) | corpus `witness-display-tampered-expired.json` + in-suite twins | macOS `.displayMismatch` **vs** Win/Linux `.Expired` | all three `.displayMismatch` | display-before-time ordering |

**Generation:** real signer, via the gated `_WitnessCrossPlatformFixtureGenerator.swift:38`
pattern (`SYXX_GENERATE_FIXTURES=1`) using the committed `fixturePrivateKey`. Each fixture
self-asserts its pre-corruption state, then applies the single documented mutation, before
being written — no hand-authored signatures.

**GV-C (attacker foreign key, valid sig, empty trust)** belongs to the **Phase 1b**
changeset, not this one: its honest verdict depends on the fail-closed decision
(`.untrustedSigner` at the Kit under Option A). Authoring it here would commit a red vector
ahead of its fix; it ships with 1b so the suite stays green-after-each-phase.

---

## 5. Verification for this slice

**Mutation (RED-before-restore, per honesty spine):**
- After 1a lands green, **revert the reorder** (move the time/trust/display checks back ahead
  of `Algorithm.Verify`) → GV-A/A′/B flip back to `.Expired`/`.UntrustedSigner` (RED). Restore.
- Simultaneously confirm the **isolated** honest paths stay green — `WitnessSignerTests.cs`
  expired-alone still `.Expired`, forged-alone still `.InvalidSignature` — proving the reorder
  did not mislabel an honestly-expired record.
- B099: **revert the keyId recompute** → the relabeled-but-valid test goes RED. Restore.

**Real-run bar (one honest run per platform, not green suites alone):**
- macOS Kit: `cd macOS && swift test --filter WitnessVerificationDetailedTests` and
  `--filter WitnessCrossPlatformGoldenVectorTests`; full `swift test` at close-out.
- Windows + Linux Kit on this host: `dotnet test Windows/Syxx.Linux.slnf` (bundles `Syxx.Kit`
  + `Syxx.Tests` + the Avalonia projects). ⚠️ `dotnet build Windows/Syxx.sln` FAILS here
  (NETSDK1100 — no Windows targets); the slnf is the supported path.
- Real CLI, both engines, on the forged+expired artifact:
  `syxx witness verify corpus/witness-crossplatform/witness-forged-expired.json` — macOS via
  the SwiftPM CLI, .NET via `Syxx/Commands/WitnessCommand.cs`. After the fix both must print
  **InvalidSignature / forged**, never "expired." That run is the acceptance signal.

---

## 6. Proposal gates, parity, bookkeeping

- **Proposal-gated:** none of Phase 1a introduces a new user-visible string, but it **does
  change one user-visible verdict** on adversarial input (forged+expired: `Expired` →
  `InvalidSignature`). Surface this in the approval even though it is a strict correctness
  gain. Phase 1b (fail-closed) and its inverted invariant test **are** gated and drafted
  separately.
- **Platform parity:** 1a restores parity *to* the existing macOS reference — **no Swift twin
  needed** (leave `WitnessSigner.swift` untouched). Linux is repaired transitively via the
  shared Kit. B099 port brings Windows up to macOS; add the twin test both sides.
- **Blast radius:** all Windows/Linux `VerifySnapshot(Detailed)` callers, but observable
  change is confined to forged/tampered/relabeled input. Any existing test asserting
  `Expired`/`Stale`/`UntrustedSigner` on a *forged* snapshot must be reconciled to the new
  crypto-first verdict (none found in the isolated suites; confirm on the real run).
- **DELTAS bookkeeping (on 1a landing + vectors green + mutation-verified):**
  - **C175** (`DELTAS.md:2072`) → CLOSE; its audit-appendix predecessor **E183**
    (`DELTAS.md:3831`, same bug) resolves with it — mark both.
  - **C176** — do not touch here; verify separately (`PerResolverAnswers` already referenced
    at `WitnessSigner.cs:318-319`).
  - **B184** — extend the invariant's scope note to record that Windows/Linux now honor it.
  - B099 parity gap — file/annotate as its own line, closed by §2.
- **Not touched by this proposal:** any GUI/surface file, `WitnessSigner.swift`, the fail-open
  posture (until 1b), C173 (does not reproduce).

---

### Immediate next actions (for approval)
1. Approve Phase 1a (reorder + B099) + the three Phase 0 vectors → engine lane implements,
   mutation-verifies, runs the real-CLI bar.
2. Separately approve Phase 1b (fail-closed) **after** the self-verify disclosure mechanism
   (§3 resolution a/b) is chosen with the Copilot/surface lane.

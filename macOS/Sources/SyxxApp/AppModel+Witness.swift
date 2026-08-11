import Foundation
import SyxxKit

// MARK: - Witness testimony, verification, and key management

extension AppModel {

    /// M-01: the gate every signing-key WRITE passes through. Reads the Keychain tri-state
    /// (`checkKeyPresence`) rather than the collapsed `hasKey`, because a write is irreversible:
    /// overwriting the key silently invalidates every testimony ever sealed with the old one.
    ///
    /// Linux (`WitnessViewModel.LoadOrCreatePersistentSigningKey`, RG-2) refuses outright on
    /// `Undetermined` — "the identity exists and is simply out of reach." macOS adopts that, and
    /// additionally requires explicit confirmation for the `.present` case, which had no check at
    /// all: `saveWitnessKey` called `saveKey` straight through.
    enum WitnessKeyWriteGate: Equatable {
        /// No key on this machine (`errSecItemNotFound`) — a first write destroys nothing.
        case proceed
        /// A key is present. The write would replace it; the user must say so explicitly.
        case needsOverwriteConfirmation
        /// The Keychain could not be read. Refuse: a key may exist and would be destroyed.
        case refusedStoreUnreachable(OSStatus)
    }

    /// M-01 copy. Never says "no key" — the whole point is that we do not know.
    static let witnessKeyStoreUnreachableMessage =
        "Your signing key could not be checked because the Keychain could not be read (it may be "
        + "locked, or the request was cancelled). This does not mean you have no key. Nothing was "
        + "saved and your existing identity is untouched — unlock the Keychain and try again."

    /// M-01: consult the Keychain tri-state before any write to the signing key.
    func witnessKeyWriteGate() -> WitnessKeyWriteGate {
        switch WitnessKeyManager.checkKeyPresence() {
        case .absent: return .proceed
        case .present: return .needsOverwriteConfirmation
        case .undetermined(let status): return .refusedStoreUnreachable(status)
        }
    }

    // MARK: - Witness Testimony Flow (interactive)

    /// Persists a user-supplied signing key into the Keychain via WitnessKeyManager.
    /// The plaintext key never lives on the model; only `hasWitnessKey` is exposed.
    ///
    /// D052: the Keychain write no longer fails silently. On a real save error the failure is
    /// surfaced in the Testimony card (amber operational failure) and `false` is returned so the
    /// caller does NOT proceed to testify/verify (which would otherwise mislead with a generic
    /// "a signing key is required" message) and does NOT clear the input field.
    /// - Returns: `true` when the key was saved or there was nothing to save (empty input);
    ///   `false` when the Keychain write threw, was refused, or is awaiting confirmation.
    ///
    /// M-01 (Linux RG-2 adoption): the write is now gated on the Keychain PRESENCE tri-state. It
    /// previously called `saveKey` unconditionally, so a user following the old "no signing key —
    /// enter your key above" message overwrote the key their existing testimonies verify against,
    /// with no prompt and no way back. `confirmOverwrite` is the user's explicit "yes, replace it";
    /// an unreadable Keychain is refused outright, never guessed at.
    /// - Parameter confirmOverwrite: pass `true` only after the user has confirmed replacement of
    ///   an existing key. Never default this to `true`.
    @discardableResult
    func saveWitnessKey(_ base64Key: String, confirmOverwrite: Bool = false) -> Bool {
        let trimmed = base64Key.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return true }
        switch witnessKeyWriteGate() {
        case .proceed:
            break
        case .needsOverwriteConfirmation:
            guard confirmOverwrite else {
                witnessKeyOverwritePending = true
                return false
            }
        case .refusedStoreUnreachable:
            let msg = Self.witnessKeyStoreUnreachableMessage
            output = msg
            showValidation = false
            showTestimony = true
            witnessTestimonyState = .failed(msg)
            return false
        }
        do {
            try WitnessKeyManager.saveKey(trimmed)
        } catch {
            // G-17: do NOT re-derive `hasWitnessKey` from `hasKey` here. On an unreadable Keychain
            // that reports "no key on this machine" from a state where we could not ask — the same
            // collapse M-01 closes. Only a determinate answer may move the flag.
            refreshHasWitnessKeyIfDeterminate()
            let detail = ErrorPresenter.userDetail(for: error, component: "witness.saveKey")
            let msg = detail.map { "Could not save the signing key to the Keychain: \($0)" }
                ?? "Could not save the signing key to the Keychain."
            output = msg
            showValidation = false
            showTestimony = true
            witnessTestimonyState = .failed(msg)
            return false
        }
        // The write succeeded, so a key is present by construction — no need to re-ask a Keychain
        // that might now refuse and make the UI claim there is no key one line after storing one.
        hasWitnessKey = true
        return true
    }

    /// G-17: move `hasWitnessKey` only on a determinate Keychain answer. `.undetermined` leaves the
    /// flag as it was — an unreadable Keychain is not evidence that the key is gone.
    func refreshHasWitnessKeyIfDeterminate() {
        switch WitnessKeyManager.checkKeyPresence() {
        case .present: hasWitnessKey = true
        case .absent: hasWitnessKey = false
        case .undetermined: break
        }
    }

    // E045: generate a new Ed25519 keypair and store the private key in the Keychain.
    // Surfaces the public key so the user can share it for trust-set configuration.
    // Previously, WitnessKeyManager.generateAndStoreKey() existed but was never exposed
    // through the GUI, forcing users to CLI tools or external generators.
    ///
    /// M-01: gated identically to `saveWitnessKey`. `generateAndStoreKey()` calls the same
    /// unconditional `saveKey`, so this button was the shorter road to the same irreversible loss —
    /// one click, no typing, every previously sealed testimony stops verifying.
    /// - Returns: `true` when a keypair was generated; `false` when the write was refused or is
    ///   awaiting the user's explicit confirmation.
    @discardableResult
    func generateWitnessKey(confirmOverwrite: Bool = false) -> Bool {
        switch witnessKeyWriteGate() {
        case .proceed:
            break
        case .needsOverwriteConfirmation:
            guard confirmOverwrite else {
                witnessKeyOverwritePending = true
                return false
            }
        case .refusedStoreUnreachable:
            let msg = Self.witnessKeyStoreUnreachableMessage
            output = msg
            showValidation = false
            showTestimony = true
            witnessTestimonyState = .failed(msg)
            return false
        }
        do {
            let publicKey = try WitnessKeyManager.generateAndStoreKey()
            hasWitnessKey = true
            output = "New Ed25519 keypair generated. Public key: \(publicKey)"
            showValidation = false
            showTestimony = false
        } catch {
            let detail = ErrorPresenter.userDetail(for: error, component: "witness.generateKey")
            let msg = detail.map { "Could not generate signing key: \($0)" }
                ?? "Could not generate signing key."
            output = msg
            showValidation = false
            showTestimony = true
            witnessTestimonyState = .failed(msg)
            return false
        }
        return true
    }

    func witnessTestify() {
        // Cancel any prior in-flight testify so two rapid clicks can't race — the last finisher must
        // not win regardless of click order (e.g. domain B's UI ending up with domain A's signature),
        // and a completion must never fire after witnessFree() and resurrect cleared state. The task
        // body guards on Task.isCancelled before writing final state back.
        witnessTask?.cancel()
        showValidation = false
        showTestimony = true
        witnessSignature = ""
        witnessSnapshot = nil
        witnessValid = false
        witnessResult = .notVerified
        witnessTestimonyState = .running
        let name = domain.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else {
            let msg = "Enter a domain to attest."
            output = msg; witnessTestimonyState = .failed(msg); witnessTask = nil; return
        }
        // G-17: same tri-state as witnessVerify. "I could not read the Keychain" must not be
        // reported as "you have no signing key" — that reading is what sends the user to the
        // overwrite.
        switch WitnessKeyManager.checkKeyPresence() {
        case .present:
            break
        case .absent:
            let msg = "A signing key is required — a snapshot signed with a default/empty key would be forgeable."
            output = msg; witnessTestimonyState = .failed(msg); witnessTask = nil; return
        case .undetermined:
            let msg = Self.witnessKeyStoreUnreachableMessage
            output = msg; witnessTestimonyState = .failed(msg); witnessTask = nil; return
        }
        guard let key = WitnessKeyManager.loadKey() else {
            let msg = "Could not load signing key from Keychain."
            output = msg; witnessTestimonyState = .failed(msg); witnessTask = nil; return
        }
        let srv = server
        witnessTask = Task {
            do {
                let local = NetworkKeyState.getSystemDNSServers().first ?? srv
                let comparison: SyxxKit.ComparisonResult
                switch self.witnessVantageSeam {
                case .production:
                    comparison = try await VantageComparer.compareVantagePoints(domain: name, localServer: local)
                case .fixed(let value):
                    comparison = value
                case .failed(let detail):
                    throw NSError(domain: "Syxx.WitnessVantageSeam", code: 1,
                                  userInfo: [NSLocalizedDescriptionKey: detail])
                case .noResult:
                    let msg = "Witness failed: vantage comparison did not return a result."
                    guard !Task.isCancelled else { return }
                    self.output = msg
                    self.witnessTestimonyState = .failed(msg)
                    return
                }
                // Steps 5-7 (sign → interpret redirection → build drift) are the pure determinative
                // core, single-sourced in SyxxKit.WitnessTestifyPlanner and shared with runWitness()
                // so the drift-population step can never again be dropped on one path (D093).
                let outcome = try WitnessTestifyPlanner.seal(domain: name, local: local, comparison: comparison, privateKeyBase64: key)
                // Cancelled (superseded by a newer testify, or freed) → do not write stale state back.
                guard !Task.isCancelled else { return }
                self.witnessSnapshot = outcome.snapshot
                self.witnessSignature = outcome.snapshot.signature   // Ed25519 signature
                self.witnessTestimonyState = .signed
                self.sectionInputs.drifts = outcome.drift.map { [$0] } ?? []
            } catch {
                guard !Task.isCancelled else { return }
                let detail = ErrorPresenter.userDetail(for: error, component: "witness.testify")
                let msg = detail.map { "Could not create witness snapshot: \($0)" }
                    ?? "Could not create witness snapshot."
                self.output = msg
                self.witnessTestimonyState = .failed(msg)
            }
        }
    }

    func witnessVerify() {
        // D069/B049: distinguish "nothing to verify" from a real signature failure, and never set a
        // valid/green state unless the Ed25519 check actually passed. A missing snapshot is NOT a
        // signature mismatch; a missing key is NOT tampering. Each branch sets an honest result.
        guard let snap = witnessSnapshot else {
            witnessValid = false
            witnessResult = .notVerified
            showValidation = true
            return
        }
        // M-01: read the D084 tri-state, not `hasKey`. `hasKey` maps `.undetermined -> false`
        // (WitnessKeyManager.swift:38-44) and its own doc comment tells callers that need to tell
        // "no key" from "could not ask" to use `checkKeyPresence()` — this call site ignored that,
        // so a merely locked Keychain rendered as a definite "no signing key on this machine.
        // Enter your key above" and walked the user into overwriting the key they still had.
        switch WitnessKeyManager.checkKeyPresence() {
        case .present:
            break
        case .absent:
            witnessValid = false
            witnessResult = .noKey(.confirmedAbsent)
            showValidation = true
            return
        case .undetermined(let status):
            witnessValid = false
            witnessResult = .noKey(.undetermined(status))
            showValidation = true
            return
        }
        // D011/B081: pass configured trusted WITNESS keys so verification checks the witness
        // signer against the correct trust set, not just the snapshot's own embedded key.
        // Previously this read the COLLECTOR trust set (CollectorPinning), so an honest witness
        // seal rendered .untrustedSigner whenever a collector key was configured. Windows/Linux
        // pass no trust arg and auto-load the witness set (WitnessSigner.swift:254-257); passing
        // the witness keys here (empty -> nil) reproduces that same auto-load behavior.
        let trustedKeys = Array(WitnessPinning.configuredTrustedWitnessKeys())
        let result = WitnessSigner.verifySnapshotDetailed(snap, trustedPublicKeys: trustedKeys.isEmpty ? nil : trustedKeys)
        applyWitnessVerification(result)
    }

    /// 5(b): verify a testimony pasted from another machine. Unlike `witnessVerify` (which checks the
    /// SELF snapshot), this decodes an external `WitnessSnapshot` from JSON and routes it through the
    /// SAME `verifySnapshotDetailed` + result switch. No local signing key is required — verification
    /// uses the snapshot's embedded public key against the configured witness trust set, exactly as
    /// the CLI (`SyxxWitness.swift`) does. Malformed JSON yields an honest `.malformedInput` result,
    /// never a crash.
    func witnessVerifyExternal() {
        let trimmed = externalSnapshotJson.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty, let data = trimmed.data(using: .utf8) else {
            witnessSnapshot = nil
            witnessValid = false
            witnessResult = .malformedInput
            showValidation = true
            return
        }
        guard let snap = try? JSONDecoder().decode(WitnessSnapshot.self, from: data) else {
            // Decode failure is an honest malformed/inconclusive result — not a tamper accusation
            // and never a crash. Clear any stale self-snapshot so the report renders the paste's
            // outcome alone.
            witnessSnapshot = nil
            witnessValid = false
            witnessResult = .malformedInput
            showValidation = true
            return
        }
        // Surface the pasted snapshot's own metadata/signature in the SAME validation report the
        // self-verify path renders.
        witnessSnapshot = snap
        witnessSignature = snap.signature
        let trustedKeys = Array(WitnessPinning.configuredTrustedWitnessKeys())
        let result = WitnessSigner.verifySnapshotDetailed(snap, trustedPublicKeys: trustedKeys.isEmpty ? nil : trustedKeys)
        applyWitnessVerification(result)
    }

    /// Shared mapping from the forensic tri-state to the honest, non-contradictory `witnessResult`
    /// the view renders. Both self-verify and external-paste verify funnel through here so the two
    /// paths can never drift into rendering the same outcome differently (E021 / D069/B049).
    func applyWitnessVerification(_ result: WitnessSigner.SnapshotVerification) {
        switch result {
        case .valid:
            witnessValid = true
            witnessResult = .valid
        case .expired:
            witnessValid = false
            witnessResult = .expired
        case .stale:
            witnessValid = false
            witnessResult = .stale
        case .futureDated:
            witnessValid = false
            witnessResult = .futureDated
        case .untrustedSigner:
            witnessValid = false
            witnessResult = .untrustedSigner
        case .displayMismatch:
            witnessValid = false
            witnessResult = .displayMismatch
        case .invalidSignature:
            witnessValid = false
            witnessResult = .invalidSignature
        case .malformedInput:
            witnessValid = false
            witnessResult = .malformedInput
        case .unsupportedFormat:
            witnessValid = false
            witnessResult = .unsupportedFormat
        }
        showValidation = true
    }

    func witnessFree() {
        // Cancel any in-flight testify first so its completion handler can't fire afterward and
        // resurrect the state we're about to clear (the task body guards on Task.isCancelled).
        witnessTask?.cancel()
        witnessTask = nil
        showTestimony = false
        showValidation = false
        witnessTestimonyState = .idle
        // Actually free the evidence: without this a later Verify (no re-Testify) still showed the
        // old snapshot/signature/result. All four are now reset so "Free" truly clears.
        witnessSnapshot = nil
        witnessSignature = ""
        witnessValid = false
        witnessResult = .notVerified
    }
}

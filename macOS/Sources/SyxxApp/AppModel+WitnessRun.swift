import Foundation
import SyxxKit

// MARK: - Witness run (Run All Wardens path)

extension AppModel {

    /// Sendable error carrier so a caught error's message can be threaded OUT of a ProbeRunner
    /// closure (whose Output must be Sendable — `any Error` is not). Fixes the three catch blocks in
    /// `runWitness()` that used to discard the real error and report a fixed generic string.
    struct WitnessProbeError: Error, Sendable { let message: String }

    /// Resolve the Witness vantage comparison through the B255 seam. Production uses live
    /// `VantageComparer` under ProbeRunner; injected seams skip the network and return the
    /// exact classification the caller asserts. Budget is owned locally so it is not a
    /// MainActor-isolated `inout` crossing into nonisolated `ProbeRunner.run`.
    func resolveWitnessComparison(domain name: String, local: String) async -> Result<SyxxKit.ComparisonResult, WitnessProbeError>? {
        switch witnessVantageSeam {
        case .production:
            var probeBudget = ProbeBudget.defaultMirror
            return await ProbeRunner.run(label: "vantage", budget: &probeBudget) {
                do {
                    return .success(try await VantageComparer.compareVantagePoints(domain: name, localServer: local))
                } catch {
                    return .failure(WitnessProbeError(message: ErrorPresenter.userDetail(for: error, component: "witness.vantage") ?? ""))
                }
            }
        case .fixed(let comparison):
            return .success(comparison)
        case .failed(let detail):
            return .failure(WitnessProbeError(message: detail))
        case .noResult:
            return nil
        }
    }

    func runWitness() async -> String {
        // B195 parity: route through the SAME published state the interactive Witness tab uses, so
        // "Run All Wardens" and the generic runner reflect what actually happened instead of leaving
        // the tab stale. Mirrors witnessTestify()'s state discipline (idle/running/signed/failed).
        showValidation = false
        showTestimony = true
        witnessSignature = ""
        witnessSnapshot = nil
        witnessValid = false
        witnessResult = .notVerified
        witnessTestimonyState = .running

        let name = domain.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else {
            let msg = "Enter a domain."
            witnessTestimonyState = .failed(msg); return msg
        }
        // G-17: tri-state, not `hasKey` — see witnessTestify().
        switch WitnessKeyManager.checkKeyPresence() {
        case .present:
            break
        case .absent:
            let msg = "A signing key is required — use syxx witness --gen-keypair or WitnessSigner.generateKeyPair() to create one."
            witnessTestimonyState = .failed(msg); return msg
        case .undetermined:
            let msg = Self.witnessKeyStoreUnreachableMessage
            witnessTestimonyState = .failed(msg); return msg
        }
        let local = NetworkKeyState.getSystemDNSServers().first ?? server

        let comparisonResult = await resolveWitnessComparison(domain: name, local: local)
        let comparison: SyxxKit.ComparisonResult
        switch comparisonResult {
        case .success(let value):
            comparison = value
        case .failure(let err):
            let msg = err.message.isEmpty ? "Could not create witness snapshot." : "Could not create witness snapshot: \(err.message)"
            witnessTestimonyState = .failed(msg); return msg
        case .none:
            let msg = "Witness failed: vantage comparison did not return a result."
            witnessTestimonyState = .failed(msg); return msg
        }

        // Load key from Keychain momentarily for signing, then release.
        guard let key = WitnessKeyManager.loadKey() else {
            let msg = "Witness failed: signing key could not be loaded from Keychain."
            witnessTestimonyState = .failed(msg); return msg
        }
        // createSignedSnapshot and verifySnapshot are synchronous local operations;
        // ProbeRunner wrapping provides budget accounting even though timeout is N/A for sync work.
        var signBudget = ProbeBudget.defaultMirror
        let snapResult: Result<WitnessSnapshot, WitnessProbeError>? = await ProbeRunner.run(label: "signSnapshot", budget: &signBudget) {
            do {
                return .success(try WitnessSigner.createSignedSnapshot(domain: name, comparison: comparison, privateKeyBase64: key))
            } catch {
                return .failure(WitnessProbeError(message: ErrorPresenter.userDetail(for: error, component: "witness.sign") ?? ""))
            }
        }
        let snap: WitnessSnapshot
        switch snapResult {
        case .success(let value):
            snap = value
        case .failure(let err):
            let msg = err.message.isEmpty ? "Could not create witness snapshot." : "Could not create witness snapshot: \(err.message)"
            witnessTestimonyState = .failed(msg); return msg
        case .none:
            let msg = "Witness failed: could not create signed snapshot."
            witnessTestimonyState = .failed(msg); return msg
        }

        // Self-verify. D050: verifySnapshot returns a tri-state SnapshotVerification (never Bool),
        // so each forensic reason is rendered distinctly — an expired honest snapshot is never "❌ INVALID".
        var verifyBudget = ProbeBudget.defaultMirror
        let verifyResult: WitnessSigner.SnapshotVerification? = await ProbeRunner.run(label: "verifySnapshot", budget: &verifyBudget) {
            WitnessSigner.verifySnapshot(snap)
        }
        let selfVerifyLine: String
        if let verification = verifyResult {
            switch verification {
            case .valid:
                selfVerifyLine = "✅ VALID"
            case .expired:
                selfVerifyLine = "⚠️ EXPIRED — snapshot has passed its expiry date"
            case .stale:
                selfVerifyLine = "⚠️ STALE — snapshot exceeds max age"
            case .futureDated:
                selfVerifyLine = "⚠️ FUTURE DATED — snapshot timestamp ahead of local clock"
            case .untrustedSigner:
                selfVerifyLine = "⚠️ UNTRUSTED SIGNER — key not in trust set"
            case .displayMismatch:
                selfVerifyLine = "⚠️ DISPLAY MISMATCH — snapshot display fields differ from signed evidence"
            case .invalidSignature:
                selfVerifyLine = "❌ INVALID SIGNATURE"
            case .malformedInput:
                selfVerifyLine = "⚠️ MALFORMED INPUT — could not decode key or signature"
            case .unsupportedFormat:
                selfVerifyLine = "⚠️ UNSUPPORTED FORMAT — snapshot version not recognized"
            }
        } else {
            selfVerifyLine = "could not verify — self-verification did not return a result."
        }

        // C211: Only promote to .signed if self-verification passed. A failed self-verify
        // (invalid signature, malformed input) must never be elevated to signed testimony —
        // the snapshot is still surfaced for forensic inspection, but the state is honest.
        witnessSnapshot = snap
        witnessSignature = snap.signature
        if let verification = verifyResult {
            switch verification {
            case .valid:
                witnessTestimonyState = .signed
            case .invalidSignature, .malformedInput, .unsupportedFormat:
                witnessTestimonyState = .failed("Self-verification failed: \(selfVerifyLine)")
            default:
                // Warnings (expired, stale, futureDated, untrustedSigner, displayMismatch):
                // the snapshot is still structurally valid — surface it as signed with
                // the warning visible in the output. These are informational, not failures.
                witnessTestimonyState = .signed
            }
        } else {
            witnessTestimonyState = .failed("Self-verification did not return a result")
        }

        // Same drift-population step as witnessTestify(), single-sourced in the Kit planner (D093):
        // signing here stays ProbeRunner-wrapped above for budget accounting, so only steps 6-7 are
        // shared, but the drift can no longer diverge between the interactive and Run-All paths.
        self.sectionInputs.drifts = WitnessTestifyPlanner
            .driftIfRedirected(domain: name, local: local, comparison: comparison)
            .map { [$0] } ?? []
        return """
        Signed witness snapshot for \(name)
        Redirection detected: \(snap.comparison.isRedirectionDetected)
        Inconclusive: \(snap.comparison.isInconclusive)
        Signature: \(snap.signature)
        Self-verify: \(selfVerifyLine)
        """
    }
}

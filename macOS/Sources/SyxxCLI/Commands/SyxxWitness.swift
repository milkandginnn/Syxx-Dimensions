import Foundation
import CryptoKit
import SyxxKit

/// B253 — the machine-readable half of `syxx witness`.
///
/// The PRINTED text was already honest ("VALID" vs "DISPLAY MISMATCH"); the exit status was not.
/// macOS returned 2 for every outcome including `--help`, so the code carried no information, and
/// Windows returned 0 for a TAMPERED snapshot, so any CI gate reading `$?` treated forged evidence
/// as verified. The exit status is the channel automation trusts, which makes it the worse of the
/// two to get wrong.
///
/// Values follow the CLI-wide contract documented at `SyxxCLI.swift:7`:
/// `0 = clean, 1 = threat, 2 = inconclusive, 3 = error`. Byte-for-byte twin of the C#
/// `WitnessCommand.ExitStatus` (`Windows/dotNET/Syxx/Commands/WitnessCommand.cs`).
enum WitnessExitStatus: Int32 {
    /// The command completed: a signature verified, or a non-adjudicating command (help, keygen,
    /// create, report) finished its work.
    case clean = 0
    /// The snapshot is TAMPERED. Distinct from `.error` on purpose: a CI gate must be able to tell
    /// forged evidence from a missing file.
    case tampered = 1
    /// The snapshot could not be judged either way — expired, stale, future-dated, untrusted
    /// signer, unsupported format, malformed content. Not clean, and not proof of tampering.
    case inconclusive = 2
    /// Operational failure: unreadable/unparseable input, a failed trust-anchor fetch, or misuse.
    case error = 3

    /// The one place `SnapshotVerification` becomes an exit status. Adding a verification state
    /// without deciding its code is a compile error here, by design.
    static func forVerification(_ result: WitnessSigner.SnapshotVerification) -> WitnessExitStatus {
        switch result {
        case .valid:
            return .clean
        case .invalidSignature, .displayMismatch:
            // Both mean someone altered signed evidence. Forgery is a threat, not an error.
            return .tampered
        case .expired, .stale, .futureDated, .untrustedSigner, .unsupportedFormat, .malformedInput:
            // The snapshot may be perfectly honest; we simply cannot adjudicate it. Never 0.
            return .inconclusive
        }
    }
}

struct SyxxWitnessCLI {
    @discardableResult
    static func run(_ argv: [String]) async -> WitnessExitStatus {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx witness",
            usage: "Usage: syxx witness create <domain> --key-file <path> [--output f] | verify <file> [--pubkey <b64>] [--fetch-pubkey <url>] [--detailed] [--max-age <seconds>] | report <file> [--conclusion <file>] | --gen-keypair [--key-file <path>]") { return .clean }
        let args = argv
        if args.contains("--gen-keypair") {
            // D086: never print the private key to stdout — it lands in scrollback, CI logs,
            // tmux buffers, and tee files, making every snapshot forgeable. Write to a 0600
            // file instead; print only the path and the public key.
            // Override the default path with --key-file <path> if provided.
            var keyFilePath: String? = nil
            var i = 0
            while i < args.count {
                if args[i] == "--key-file" && i + 1 < args.count {
                    keyFilePath = args[i + 1]
                    i += 1
                }
                i += 1
            }
            let homeDir = FileManager.default.homeDirectoryForCurrentUser
            let syxxDir = homeDir.appendingPathComponent(".syxx")
            let keyFile = keyFilePath.map { URL(fileURLWithPath: $0) }
                ?? syxxDir.appendingPathComponent("witness.key")
            // B308: validate --key-file path is within allowed directories.
            // Without this, --key-file /etc/passwd with --force would attempt to write
            // to an arbitrary system path. Restrict to ~/.syxx/ or the user's home directory.
            let resolvedPath = keyFile.standardizedFileURL.path
            let homePath = homeDir.standardizedFileURL.path
            if !resolvedPath.hasPrefix(homePath) {
                print("[ERROR] --key-file path must be within your home directory (\(homePath))")
                print("[ERROR] Got: \(resolvedPath)")
                return .error
            }
            // B249: refuse to overwrite an existing key — silently destroying the user's
            // signing identity makes previously signed snapshots orphaned from their signer.
            let forceOverwrite = args.contains("--force")
            if FileManager.default.fileExists(atPath: keyFile.path), !forceOverwrite {
                print("[ERROR] Private key already exists at: \(keyFile.path)")
                print("[ERROR] Re-running --gen-keypair would destroy your current signing identity.")
                print("[ERROR] Use --force to overwrite, or --key-file <path> to write to a different location.")
                return .error
            }
            let (priv, pub) = WitnessSigner.generateKeyPair()
            do {
                // B189: create ~/.syxx with owner-only permissions (0700) from the start.
                if !FileManager.default.fileExists(atPath: syxxDir.path) {
                    try FileManager.default.createDirectory(at: syxxDir,
                        withIntermediateDirectories: true,
                        attributes: [.posixPermissions: 0o700])
                }
                // B249: also ensure the parent directory exists for custom --key-file paths.
                let parentDir = keyFile.deletingLastPathComponent()
                if !FileManager.default.fileExists(atPath: parentDir.path) {
                    try FileManager.default.createDirectory(at: parentDir,
                        withIntermediateDirectories: true,
                        attributes: [.posixPermissions: 0o700])
                }
                let privData = Data((priv + "\n").utf8)
                // B189: write with 0600 permissions atomically — no chmod-after-write window.
                // Use a temporary file with restricted perms, then move atomically.
                let tempPath = parentDir
                    .appendingPathComponent(".witness.key.tmp.\(ProcessInfo.processInfo.processIdentifier)")
                FileManager.default.createFile(atPath: tempPath.path, contents: privData,
                    attributes: [.posixPermissions: 0o600])
                try FileManager.default.moveItem(at: tempPath, to: keyFile)
                print("Private key written to: \(keyFile.path)  (permissions 0600 — keep it secret)")
                print("Public key  (share freely): \(pub)")
            } catch {
                print("[ERROR] Could not write private key to \(keyFile.path): \(error.localizedDescription)")
                print("[ERROR] Refusing to print the private key to stdout — use --key-file <path> to choose a location.")
                // The key file is the whole point of the command; if it could not be written the
                // command did not succeed, and automation must not read 0.
                return .error
            }
            return .clean
        }
        guard args.count >= 3 else {
            printUsage()
            return .error
        }
        let cmd = args[1]
        if cmd == "create" {
            let domain = args[2]
            var key: String? = nil
            var keyFilePath: String? = nil
            var insecureKeyArg = false
            var outputPath = "witness.json"
            var idx = 3
            while idx < args.count {
                // B189: --key-file reads the key from a file (preferred; owner-only enforced).
                if args[idx] == "--key-file" {
                    if idx + 1 < args.count {
                        keyFilePath = args[idx + 1]
                        idx += 1
                    }
                } else if args[idx] == "--insecure-key-arg" {
                    // B189: gate legacy --key behind an explicit opt-in with a warning.
                    insecureKeyArg = true
                } else if args[idx] == "--key" || args[idx] == "-k" {
                    if idx + 1 < args.count {
                        key = args[idx + 1]
                        idx += 1
                    }
                } else if args[idx] == "--output" || args[idx] == "-o" {
                    if idx + 1 < args.count {
                        outputPath = args[idx + 1]
                        idx += 1
                    }
                }
                idx += 1
            }
            // B189: --key-file takes precedence — read from an owner-only file, never argv.
            if let path = keyFilePath {
                do {
                    let attrs = try FileManager.default.attributesOfItem(atPath: path)
                    let perms = (attrs[.posixPermissions] as? Int) ?? 0o777
                    if perms & 0o077 != 0 {
                        print("[WARNING] Key file \(path) has permissions \(String(perms, radix: 8)) — should be 0600 (owner-only).")
                    }
                    key = try String(contentsOfFile: path, encoding: .utf8).trimmingCharacters(in: .whitespacesAndNewlines)
                } catch {
                    print("[ERROR] Could not read key file \(path): \(error.localizedDescription)")
                    return .error
                }
            }
            // B189: --key on argv requires --insecure-key-arg (ps -ww exposes argv to local users).
            if key != nil && keyFilePath == nil && !insecureKeyArg {
                print("[WARNING] Passing the private key on argv exposes it via `ps -ww`, shell history, and CI logs.")
                print("  Use --key-file <path> or set SYXX_WITNESS_KEY instead. To suppress this warning, pass --insecure-key-arg.")
            }
            if key == nil { key = ProcessInfo.processInfo.environment["SYXX_WITNESS_KEY"] }
            // E040: scrub the key from the process environment after reading it.
            // `/proc/self/environ` (Linux) and `ps e` (macOS) expose env vars to
            // other local users; overwriting with an empty string removes the
            // private key from the live environment as early as possible.
            setenv("SYXX_WITNESS_KEY", "", 1)
            guard let key, !key.isEmpty else {
                print("[ERROR] Private key required (use --key-file <path>, set SYXX_WITNESS_KEY, or --gen-keypair to create a keypair).")
                printUsage()
                return .error
            }
            // E079 (ported from the C# twin `WitnessCommand.cs:212-219`): REFUSE to sign a snapshot
            // measured against a substituted resolver.
            //
            // This previously warned and then fell back to 8.8.8.8 — and signed anyway. The result
            // was an Ed25519 signature over a comparison the user never made, sealed into a
            // `WitnessSnapshot` whose `localServer` field presents Google's resolver as the user's
            // own local vantage. That is spine #1 (a result emitted before its input was
            // established) and spine #4 (a fabricated value shipped as measured) at once, with a
            // cryptographic signature on top making the fabrication look authoritative to whoever
            // the evidence is later shown to.
            //
            // A warning on stdout is not a mitigation: the snapshot outlives the terminal it was
            // printed in, and `witness verify` on the resulting file reports a perfectly valid
            // signature — because the signature IS valid. It attests to the wrong measurement.
            // The only honest outcome is to produce no snapshot at all.
            guard let resolver = NetworkKeyState.getSystemDNSServers().first else {
                print("INCONCLUSIVE: No system DNS resolvers were detected. Refusing to fall back to public resolvers for a witness snapshot.")
                return .inconclusive
            }
            print("Resolving vantage data for \(domain) via \(resolver)...")
            do {
                let comparison = try await VantageComparer.compareVantagePoints(domain: domain, localServer: resolver)
                let snapshot = try WitnessSigner.createSignedSnapshot(domain: domain, comparison: comparison, privateKeyBase64: key)
                let encoder = JSONEncoder()
                encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
                let data = try encoder.encode(snapshot)
                try data.write(to: URL(fileURLWithPath: outputPath))
                print("[SUCCESS] Witness snapshot successfully created and signed!")
                print("Snapshot signature: \(snapshot.signature)")
                print("Public key (share for verification): \(snapshot.publicKey)")
                print("Key ID: \(snapshot.keyId)")
                print("Saved to: \(outputPath)")
                // `create` SEALS evidence, it does not adjudicate it — the printed output carries
                // no secure/tampered claim, so 0 here means "the snapshot was written", nothing
                // more. The adjudicating channel is `verify`, whose codes are mapped above.
                return .clean
            } catch {
                print("Failed to create witness snapshot: \(error.localizedDescription)")
                return .error
            }
        } else if cmd == "verify" {
            let filePath = args[2]
            var externalPubKey: String? = nil
            var fetchPubKeyURL: String? = nil
            var detailed = false
            // D090: no CLI path wired maxAge/--max-age through to the verifier, even though
            // verifySnapshot/verifySnapshotDetailed already support it and .stale is tested.
            // Same numeric-seconds convention as `syxx cage --duration <seconds>`.
            var maxAge: TimeInterval? = nil
            var idx = 3
            while idx < args.count {
                if args[idx] == "--pubkey" {
                    if idx + 1 < args.count {
                        externalPubKey = args[idx + 1]
                        idx += 1
                    }
                } else if args[idx] == "--fetch-pubkey" {
                    if idx + 1 < args.count {
                        fetchPubKeyURL = args[idx + 1]
                        idx += 1
                    }
                } else if args[idx] == "--detailed" {
                    detailed = true
                } else if args[idx] == "--max-age" {
                    if idx + 1 < args.count, let val = Double(args[idx + 1]), val.isFinite, val >= 0 {
                        maxAge = val
                        idx += 1
                    }
                }
                idx += 1
            }
            do {
                let data = try Data(contentsOf: URL(fileURLWithPath: filePath))
                let snapshot = try JSONDecoder().decode(WitnessSnapshot.self, from: data)

                // Resolve the public key to use for verification
                var pubKey = snapshot.publicKey
                var keySource = "embedded"
                if let pk = externalPubKey {
                    pubKey = pk
                    keySource = "user-supplied"
                } else if let urlStr = fetchPubKeyURL {
                    // D087: require HTTPS — a plain HTTP trust anchor is trivially MITMable.
                    guard urlStr.lowercased().hasPrefix("https://") else {
                        print("[ERROR] --fetch-pubkey requires an https:// URL (got: \(urlStr)). A plain-HTTP trust anchor can be replaced in transit.")
                        return .error
                    }
                    guard let url = URL(string: urlStr) else {
                        print("[ERROR] Invalid URL for --fetch-pubkey: \(urlStr)")
                        return .error
                    }
                    let (keyData, response) = try await URLSession.shared.data(from: url)
                    guard let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode == 200 else {
                        let code = (response as? HTTPURLResponse)?.statusCode ?? -1
                        print("[ERROR] --fetch-pubkey server returned HTTP \(code) — refusing to use a non-200 response as a trust anchor.")
                        return .error
                    }
                    guard let rawKey = String(data: keyData, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines),
                          !rawKey.isEmpty,
                          let keyBytes = Data(base64Encoded: rawKey),
                          keyBytes.count == 32 else {
                        print("[ERROR] --fetch-pubkey: response did not contain a valid 32-byte Ed25519 public key.")
                        return .error
                    }
                    pubKey = rawKey
                    keySource = "fetched (https) from \(urlStr)"
                    // Print fingerprint so the user can confirm they got the right key.
                    let fingerprint = SHA256.hash(data: keyBytes).map { String(format: "%02x", $0) }.joined()
                    print("  Fetched public key fingerprint (SHA-256): \(fingerprint)")
                    print("  Confirm this fingerprint matches the publisher's published value before proceeding.")
                }

                // Build verification snapshot (with external key if provided)
                let verifySnapshot: WitnessSnapshot
                if pubKey != snapshot.publicKey {
                    verifySnapshot = WitnessSnapshot(
                        formatVersion: snapshot.formatVersion,
                        timestamp: snapshot.timestamp,
                        expiresAt: snapshot.expiresAt,
                        targetDomain: snapshot.targetDomain,
                        comparison: snapshot.comparison,
                        evidence: snapshot.evidence,
                        signature: snapshot.signature,
                        publicKey: pubKey,
                        keyId: snapshot.keyId
                    )
                } else {
                    verifySnapshot = snapshot
                }

                if detailed {
                    // D011: pass configured trusted keys so verification checks the signer
                    let trustedKeys = Array(CollectorPinning.configuredTrustedSigningKeys())
                    let result = WitnessSigner.verifySnapshotDetailed(verifySnapshot, maxAge: maxAge, trustedPublicKeys: trustedKeys.isEmpty ? nil : trustedKeys)
                    let (resultLabel, detailText) = Self.describeVerification(result)

                    print("\nWitness Verification Report:")
                    print("  File: \(filePath)")
                    print("  Target Domain: \(snapshot.targetDomain)")
                    print("  Timestamp: \(snapshot.timestamp)")
                    print("  Key ID: \(snapshot.keyId)")
                    print("  Public Key: \(pubKey)")
                    if keySource != "embedded" {
                        print("  Key Source: \(keySource)")
                    }
                    print("  Redirection Detected: \(snapshot.comparison.isRedirectionDetected)")
                    // B100 (ported from `WitnessCommand.cs:397-398`): surface network-evidence
                    // quality alongside the cryptographic result. A valid signature over degraded
                    // or inconclusive evidence must not read as a clean bill of health — the
                    // signature attests that the measurement was not altered, never that the
                    // measurement succeeded. `--detailed` printed neither of these.
                    print("  Inconclusive: \(snapshot.comparison.isInconclusive)")
                    print("  Degraded: \(snapshot.comparison.isDegraded)")
                    print("  Signature: \(snapshot.signature)")
                    print("  Verification: \(resultLabel)")
                    print("  Detailed Result: \(detailText)")
                    // B211 (ported from `WitnessCommand.cs:402-405`): disclose trust posture on the
                    // detailed path too. `--detailed` is the forensic surface, and it was the one
                    // path that never said whether the signer was anchored — so the most careful
                    // reader got the least information about who signed.
                    if result == .valid {
                        print("  Trust: \(Self.trustPostureLabel(pubKey, trustedKeys: trustedKeys))")
                    }
                    return WitnessExitStatus.forVerification(result)
                } else {
                    // D099: this used to reduce verification to a bare Bool via the throwing
                    // verifySnapshot(_:), so an honest expired/stale/future-dated snapshot printed
                    // the exact same "❌ INVALID / TAMPERED MESSAGE" as an actually-forged signature
                    // — the two are forensically different per SnapshotVerification's own doc
                    // comment. Call the detailed verifier here too (same result the --detailed path
                    // would show for this file) and report the real reason, still as a short summary.
                    // D011: pass configured trusted keys so verification checks the signer
                    let trustedKeys = Array(CollectorPinning.configuredTrustedSigningKeys())
                    let trustAnchorsConfigured = !trustedKeys.isEmpty
                    let result = WitnessSigner.verifySnapshotDetailed(verifySnapshot, maxAge: maxAge, trustedPublicKeys: trustedKeys.isEmpty ? nil : trustedKeys)
                    let isValid = (result == .valid)
                    let (resultLabel, detailText) = Self.describeVerification(result)

                    print("\nWitness Verification Report:")
                    print("  File: \(filePath)")
                    print("  Target Domain: \(snapshot.targetDomain)")
                    print("  Timestamp: \(snapshot.timestamp)")
                    print("  Key ID: \(snapshot.keyId)")
                    print("  Public Key: \(pubKey)")
                    if keySource != "embedded" {
                        print("  Key Source: \(keySource)")
                    }
                    print("  Redirection Detected: \(snapshot.comparison.isRedirectionDetected)")
                    print("  Inconclusive: \(snapshot.comparison.isInconclusive)")
                    // B100: `isDegraded` was disclosed on neither macOS path, though the C# twin
                    // prints it on both (`WitnessCommand.cs:398`, `:434`). Degraded evidence is a
                    // distinct state from inconclusive and must not be collapsed into it.
                    print("  Degraded: \(snapshot.comparison.isDegraded)")
                    print("  Signature: \(snapshot.signature)")
                    // B081: distinguish pinned vs unpinned verification. A crypto-valid signature
                    // by an unanchored key is self-attestation, not Syxx-endorsed validity.
                    if isValid {
                        if trustAnchorsConfigured {
                            print("  Verification: [VALID] Tamper-evident — Ed25519 signed, key anchored")
                        } else {
                            print("  Verification: [WARNING] SIGNATURE VALID — unpinned self-attestation (not anchored to a known Syxx key)")
                        }
                        print("  (Verified with \(keySource) public key\(trustAnchorsConfigured ? "" : " — no trust anchors configured") — no secret required.)")
                    } else {
                        print("  Verification: \(resultLabel)")
                        print("  Detailed Result: \(detailText)")
                    }
                    return WitnessExitStatus.forVerification(result)
                }
            } catch {
                // Unreadable or unparseable input. NEVER 0: automation must not read "could not
                // open the file" as "the evidence checked out".
                print("Failed to verify witness snapshot: \(error.localizedDescription)")
                return .error
            }
        } else if cmd == "report" {
            // B1 — Shareable signed incident report
            let filePath = args[2]
            var conclusionPath: String? = nil
            // B254: same freshness control as `verify`, so a report can be gated on the snapshot
            // still being current. No flag = no staleness check, matching `verify`.
            var maxAge: TimeInterval? = nil
            var idx = 3
            while idx < args.count {
                if args[idx] == "--conclusion" || args[idx] == "-c" {
                    if idx + 1 < args.count {
                        conclusionPath = args[idx + 1]
                        idx += 1
                    }
                } else if args[idx] == "--max-age" {
                    if idx + 1 < args.count, let val = Double(args[idx + 1]), val.isFinite, val >= 0 {
                        maxAge = val
                        idx += 1
                    }
                }
                idx += 1
            }
            do {
                let data = try Data(contentsOf: URL(fileURLWithPath: filePath))
                let snapshot = try JSONDecoder().decode(WitnessSnapshot.self, from: data)

                // B254: VERIFY BEFORE RENDERING. This command previously decoded a snapshot and
                // handed it straight to the renderer, so a snapshot whose displayed answers had
                // been edited after signing printed the ATTACKER's answers under a "SYXX SIGNED
                // INCIDENT REPORT" header, and exited 0. The header is an assertion of integrity;
                // printing unverified content under it is an affirmative false claim, which is
                // worse than having no report command at all. The verification must happen before
                // ANY other work in this branch — including reading --conclusion — so that a
                // second, unrelated failure cannot mask the tampering with an operational code.
                // D011: pass configured trusted keys so verification checks the signer
                let trustedKeys = Array(CollectorPinning.configuredTrustedSigningKeys())
                let result = WitnessSigner.verifySnapshotDetailed(snapshot, maxAge: maxAge, trustedPublicKeys: trustedKeys.isEmpty ? nil : trustedKeys)
                guard result == .valid else {
                    Self.printReportRefusal(filePath: filePath, result: result)
                    // Same mapping `verify` uses: DISPLAY MISMATCH / INVALID SIGNATURE -> 1
                    // (THREAT DETECTED, deliberate), expired / stale / future-dated / untrusted /
                    // unsupported / malformed -> 2. Never 0.
                    return WitnessExitStatus.forVerification(result)
                }

                // Optionally load a Conclusion for the A2 explanation.
                var explanation: PlainExplanation? = nil
                if let cPath = conclusionPath {
                    let cData = try Data(contentsOf: URL(fileURLWithPath: cPath))
                    let conclusion = try JSONDecoder().decode(Conclusion.self, from: cData)
                    explanation = PlainExplanations.For(conclusion)
                }

                let report = SignedIncidentReport.render(snapshot: snapshot, explanation: explanation)
                print(report)
                // Reached only when `verifySnapshotDetailed` returned `.valid`, so 0 here now
                // means "this snapshot verified AND the report was produced".
                return .clean
            } catch {
                print("Failed to generate incident report: \(error.localizedDescription)")
                return .error
            }
        } else {
            printUsage()
            return .error
        }
    }
    /// B254 — what `report` prints INSTEAD OF a report when the snapshot does not verify.
    ///
    /// Deliberately prints NOTHING that came out of the snapshot: not the conclusion, not the
    /// answers, not the redirection flags, not the signature, key ID or public key. Every one of
    /// those is attacker-controlled once verification has failed, and the incident report is a
    /// SHAREABLE artifact whose intended reader is a third party (an ISP, a regulator, a court).
    /// Terminal output is screenshotted and forwarded stripped of context, so a "refused" banner
    /// above attacker-chosen answers is one crop away from becoming the forged report it was
    /// supposed to prevent. The only fields echoed here are the path the USER typed and the
    /// verification result Syxx itself computed.
    ///
    /// A user diagnosing their own file is not left stranded: `witness verify --detailed` already
    /// prints the domain, timestamp, key ID, public key and the specific failure reason, under a
    /// header that makes no integrity claim.
    ///
    /// Byte-for-byte twin of `WitnessCommand.PrintReportRefusal` on Windows — and as of E14 that is
    /// actually TRUE. It was not: this function's literals matched, but the `Result:` line is
    /// rendered by `describeVerification`, which returned `❌ EXPIRED` here against `[INCONCLUSIVE]
    /// EXPIRED` there. The two refusals therefore differed on every non-valid state, which is every
    /// state this function can be reached in. The claim is now backed by both platforms returning
    /// the same bracketed tri-state labels; if either side's `describeVerification` changes alone,
    /// this comment becomes false again.
    static func printReportRefusal(filePath: String, result: WitnessSigner.SnapshotVerification) {
        let (label, detail) = Self.describeVerification(result)
        print("")
        print("[REFUSED] Snapshot did not verify. No incident report was produced.")
        print("")
        print("  File:   \(filePath)")
        print("  Result: \(label)")
        print("  Detail: \(detail)")
        print("")
        print("  Syxx will not print unverified content under a signed incident report")
        print("  header, so none of this file's findings, answers or seal are shown.")
        print("")
        print("  To inspect the file, run:  syxx witness verify <file> --detailed")
        print("")
    }

    /// B211 (ported from `WitnessCommand.TrustPostureLabel`): the trust posture as a third state
    /// alongside the cryptographic result. A snapshot whose signature verifies but whose signer is
    /// not in the trust set must not read identically to one whose signer IS trusted — an
    /// unqualified "VALID" is a reassuring claim about provenance that the signature alone does not
    /// support.
    ///
    /// Takes the already-resolved trust set rather than fetching it, so the label cannot disagree
    /// with the set that was actually passed to the verifier a few lines earlier.
    ///
    /// NOTE, deliberately not fixed here: macOS resolves this set from
    /// `CollectorPinning.configuredTrustedSigningKeys()` (`collector_trusted_keys` /
    /// `SYXX_COLLECTOR_TRUSTED_KEYS`) while the C# twin reads `SYXX_WITNESS_TRUSTED_KEYS`
    /// (`WitnessSigner.cs:402`). The same environment variable does not anchor both platforms, and
    /// macOS conflates the COLLECTOR trust set with the WITNESS SIGNING trust set. That is tracked
    /// as re-diff row 11 and collides with an open `key_id` alignment question; changing it here
    /// would pre-empt that decision.
    static func trustPostureLabel(_ publicKey: String, trustedKeys: [String]) -> String {
        if trustedKeys.isEmpty { return "signer unverified (no trust set configured)" }
        return trustedKeys.contains(publicKey) ? "signer trusted" : "signer NOT in your trust set"
    }

    /// Map a SnapshotVerification to a (label, detail) pair for user-facing output.
    ///
    /// The bracketed prefix carries the TRI-STATE CLASSIFICATION, not decoration. This previously
    /// rendered every non-valid state as `❌ <NAME>`, so `❌ EXPIRED` — a snapshot that may be
    /// perfectly honest and merely out of date — was visually identical to `❌ INVALID SIGNATURE`,
    /// which is an accusation that someone forged evidence. D099 fixed the *reason text* but left
    /// the two classes wearing the same glyph, which is spine #3: a tri-state collapsed to a
    /// boolean at the last hop before the user.
    ///
    /// The prefix now names the class the exit code will carry, so the printed line and `$?` cannot
    /// tell different stories: `[VALID]` -> 0, `[TAMPERED]` -> 1, `[INCONCLUSIVE]` -> 2. Twin of
    /// `WitnessCommand.DescribeVerification`.
    ///
    /// `.malformedInput` and `.unsupportedFormat` are `[INCONCLUSIVE]`, not `[INVALID]`. The C#
    /// twin labelled them `[INVALID]` while `ForVerification` mapped them to exit 2 — a label that
    /// reads as an accusation on a state the contract classifies as unjudgeable. Both sides were
    /// corrected together; see the E14 report.
    static func describeVerification(_ result: WitnessSigner.SnapshotVerification) -> (String, String) {
        switch result {
        case .valid:
            return ("[VALID]", "Tamper-evident — Ed25519 signed content matches.")
        case .malformedInput:
            return ("[INCONCLUSIVE] MALFORMED INPUT", "Could not parse snapshot or public key.")
        case .unsupportedFormat:
            return ("[INCONCLUSIVE] UNSUPPORTED FORMAT", "Snapshot format version not recognized.")
        case .expired:
            return ("[INCONCLUSIVE] EXPIRED", "The snapshot's expiry date has passed.")
        case .stale:
            return ("[INCONCLUSIVE] STALE", "Snapshot is older than the allowed freshness window.")
        case .futureDated:
            return ("[INCONCLUSIVE] FUTURE-DATED", "Snapshot timestamp is ahead of system clock.")
        case .untrustedSigner:
            return ("[INCONCLUSIVE] UNTRUSTED SIGNER", "Public key is not in the trust set.")
        case .displayMismatch:
            return ("[TAMPERED] DISPLAY MISMATCH", "Display fields do not match the signed evidence — tampered presentation.")
        case .invalidSignature:
            return ("[TAMPERED] INVALID SIGNATURE", "Ed25519 signature does not match the content.")
        }
    }

    static func printUsage() {
        print("""
        Usage: swift run syxx witness create <domain> --key-file <path> [--output f] | verify <file> [--pubkey <b64>] [--fetch-pubkey <url>] [--detailed] [--max-age <seconds>] | report <file> [--conclusion <file>] | --gen-keypair [--key-file <path>]
          create <domain>       Create a signed witness snapshot for the given domain
            --key-file <path>   Read Ed25519 private key from a 0600 file (preferred)
            --key, -k           Base64-encoded key on argv (requires --insecure-key-arg; use SYXX_WITNESS_KEY or --key-file instead)
            --output, -o        Output path (default: witness.json)
          verify <file>         Verify a witness snapshot
            --pubkey <b64>      Verify with an externally-supplied public key (instead of embedded)
            --fetch-pubkey <url>Fetch public key from URL for verification
            --detailed          Show detailed tri-state verification result
            --max-age <seconds> Reject snapshots older than this many seconds (no flag = no staleness check)
          report <file>         Generate a shareable signed incident report from a snapshot
            --conclusion, -c    Optional path to a Conclusion JSON for the plain-language explanation
          --gen-keypair         Generate a new Ed25519 keypair, write it to a private key file, and exit
            --key-file <path>   Override the default key file location (~/.syxx/witness.key)
          --help, -h            Show this help
        """)
    }
}

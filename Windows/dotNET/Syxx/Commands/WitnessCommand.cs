using System;
using System.IO;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx witness` — signed evidence snapshots. Body moved verbatim from the former
    /// SyxxWitness tool; args[0] is the first real argument (the subcommand token was stripped).</summary>
    internal static class WitnessCommand
    {
        /// <summary>
        /// B253 — the machine-readable half of `syxx witness`.
        ///
        /// The PRINTED text was already honest ("VALID" vs "DISPLAY MISMATCH"); the exit status was
        /// not. This command returned 0 for a TAMPERED snapshot AND for an unreadable one, so any
        /// script, CI gate, or automation checking $LASTEXITCODE read forged evidence as verified.
        /// The exit status is the channel automation trusts, which makes it the worse of the two to
        /// get wrong.
        ///
        /// Values follow the CLI-wide contract documented on macOS at `SyxxCLI.swift:7`
        /// (0 = clean, 1 = threat, 2 = inconclusive, 3 = error) and are the twin of the Swift
        /// `WitnessExitStatus` in `macOS/Sources/SyxxCLI/Commands/SyxxWitness.swift`.
        /// </summary>
        internal enum ExitStatus
        {
            /// The command completed: a signature verified, or a non-adjudicating command
            /// (help, keygen, create, report) finished its work.
            Clean = 0,
            /// The snapshot is TAMPERED. Distinct from Error on purpose: a CI gate must be able to
            /// tell forged evidence from a missing file.
            Tampered = 1,
            /// Could not be judged either way — expired, stale, future-dated, untrusted signer,
            /// unsupported format, malformed content. Not clean, and not proof of tampering.
            Inconclusive = 2,
            /// Operational failure: unreadable/unparseable input, a failed trust-anchor fetch, or
            /// command misuse.
            Error = 3
        }

        /// The one place SnapshotVerification becomes an exit status. Mirrors
        /// `WitnessExitStatus.forVerification` on macOS case for case.
        internal static ExitStatus ForVerification(SnapshotVerification result) => result switch
        {
            SnapshotVerification.Valid => ExitStatus.Clean,
            // Both mean someone altered signed evidence. Forgery is a threat, not an error.
            SnapshotVerification.InvalidSignature => ExitStatus.Tampered,
            SnapshotVerification.DisplayMismatch => ExitStatus.Tampered,
            // The snapshot may be perfectly honest; we simply cannot adjudicate it. Never 0.
            SnapshotVerification.Expired => ExitStatus.Inconclusive,
            SnapshotVerification.Stale => ExitStatus.Inconclusive,
            SnapshotVerification.FutureDated => ExitStatus.Inconclusive,
            SnapshotVerification.UntrustedSigner => ExitStatus.Inconclusive,
            SnapshotVerification.UnsupportedFormat => ExitStatus.Inconclusive,
            SnapshotVerification.MalformedInput => ExitStatus.Inconclusive,
            // An unmapped state must never read as clean.
            _ => ExitStatus.Error
        };

        /// Records the outcome on the process exit status. Every `return` in RunAsync goes through
        /// this, so "fell out of the method without deciding" is not reachable.
        private static void Exit(ExitStatus status) => Environment.ExitCode = (int)status;

        public static async Task RunAsync(string[] args)
        {
            // E012: emit a diagnostic when UTF-8 encoding fails on legacy console hosts so
            // mojibake in evidence output (IDN domains, dashes, arrows) is explainable.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[warning] Console UTF-8 encoding unavailable: {ex.Message}");
            }
            if (SyxxConfig.HandleGlobalFlags(args, "syxx witness",
                "Usage: syxx witness create <domain> --key <b64-private-key> [--output f] | verify <file> [--pubkey <b64>] [--fetch-pubkey <url>] [--detailed] [--max-age <seconds>] | report <file> [--conclusion <file>] | --gen-keypair [--key-file <path>]"))
            {
                Exit(ExitStatus.Clean);
                return;
            }

            if (args.Length >= 1 && args[0] == "--gen-keypair")
            {
                // D086 (Windows regression of the same defect already fixed on macOS
                // SyxxWitness.swift:9-40): never print the private key to stdout — it lands in
                // shell scrollback, CI logs, and tmux buffers, making every future snapshot
                // forgeable. Write it to an owner-only-permission file instead; print only the
                // file path and the public key. Override the default location with --key-file.
                string? keyFilePath = null;
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--key-file" && i + 1 < args.Length)
                    {
                        keyFilePath = args[i + 1];
                        i++;
                    }
                }
                string keyFile = keyFilePath ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".syxx", "witness.key");

                var (priv, pub) = WitnessSigner.GenerateKeyPair();
                try
                {
                    string? dir = Path.GetDirectoryName(keyFile);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                        // E078: restrict ~/.syxx to owner-only (0700) — it holds every Syxx secret.
                        Syxx.Kit.SyxxPaths.EnsurePrivateDir(dir);
                    }

                    // E078: create the key file atomically with owner-only permissions — the old
                    // code wrote at 0644 and then narrowed, leaving a world-readable window.
                    var keyBytes = System.Text.Encoding.UTF8.GetBytes(priv + "\n");
                    if (!OperatingSystem.IsWindows())
                    {
                        using var fs = new FileStream(keyFile, new FileStreamOptions
                        {
                            Mode = FileMode.Create,
                            Access = FileAccess.Write,
                            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                        });
                        await fs.WriteAsync(keyBytes);
                    }
                    else
                    {
                        await File.WriteAllBytesAsync(keyFile, keyBytes);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Could not write private key to {keyFile}: {ex.Message}");
                    Console.WriteLine("[ERROR] Refusing to print the private key to stdout — use --key-file <path> to choose a location.");
                    Exit(ExitStatus.Error);
                    return;
                }

                // E078: split the chmod into its own try so a permission-narrowing failure reports
                // honestly — the key IS on disk, and calling it absent is worse than calling it leaked.
                try
                {
                    RestrictKeyFileToCurrentUser(keyFile);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] Private key was written to {keyFile} but its permissions could not be restricted: {ex.Message}");
                    Console.WriteLine("[WARNING] Treat this key as compromised and regenerate with --gen-keypair after fixing filesystem permissions.");
                    Console.WriteLine($"Public key  (share freely): {pub}");
                    Exit(ExitStatus.Error);
                    return;
                }

                Console.WriteLine($"Private key written to: {keyFile}  (owner-only permissions — keep it secret)");
                Console.WriteLine($"Public key  (share freely): {pub}");
                Exit(ExitStatus.Clean);
                return;
            }

            if (args.Length < 2)
            {
                // Was 1, which under the shared contract means THREAT DETECTED. Command misuse is
                // an operational error (3), not a finding about the network.
                PrintUsage();
                Exit(ExitStatus.Error);
                return;
            }

            string cmd = args[0];

            if (cmd.Equals("create", StringComparison.OrdinalIgnoreCase))
            {
                string domain = args[1];
                string? key = null;
                string outputPath = "witness.json";

                int idx = 2;
                while (idx < args.Length)
                {
                    if (args[idx] == "--key" || args[idx] == "-k")
                    {
                        if (idx + 1 < args.Length)
                        {
                            key = args[idx + 1];
                            idx++;
                        }
                    }
                    else if (args[idx] == "--output" || args[idx] == "-o")
                    {
                        if (idx + 1 < args.Length)
                        {
                            outputPath = args[idx + 1];
                            idx++;
                        }
                    }

                    idx++;
                }

                key ??= Environment.GetEnvironmentVariable("SYXX_WITNESS_KEY");
                if (string.IsNullOrEmpty(key))
                {
                    Console.WriteLine("[ERROR] --key/-k is required (pass --key, set SYXX_WITNESS_KEY, or use --gen-keypair to create a new keypair).");
                    PrintUsage();
                    Exit(ExitStatus.Error);
                    return;
                }

                string systemResolver = NetworkKeyState.GetSystemDNSServers().Count > 0 ? NetworkKeyState.GetSystemDNSServers()[0] : null;
                if (systemResolver == null)
                {
                    // E079: refuse to sign a snapshot measured against a substituted resolver —
                    // an Ed25519 signature over 8.8.8.8 attributed to the user is forged evidence.
                    Console.WriteLine("INCONCLUSIVE: No system DNS resolvers were detected. Refusing to fall back to public resolvers for a witness snapshot.");
                    Exit(ExitStatus.Inconclusive);
                    return;
                }
                Console.WriteLine($"Resolving vantage data for {domain} via {systemResolver}...");

                try
                {
                    var comparison = await VantageComparer.CompareVantagePointsAsync(domain, localServer: systemResolver);
                    var snapshot = WitnessSigner.CreateSignedSnapshot(domain, comparison, key);

                    var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                    string json = JsonSerializer.Serialize(snapshot, options);
                    await File.WriteAllTextAsync(outputPath, json);

                    Console.WriteLine("[SUCCESS] Witness snapshot successfully created and signed!");
                    Console.WriteLine($"Snapshot signature: {snapshot.Signature}");
                    Console.WriteLine($"Public key (share for verification): {snapshot.PublicKey}");
                    Console.WriteLine($"Key ID: {snapshot.KeyId}");
                    Console.WriteLine($"Saved to: {outputPath}");
                    // `create` SEALS evidence, it does not adjudicate it — the printed output
                    // carries no secure/tampered claim, so 0 here means "the snapshot was written",
                    // nothing more. The adjudicating channel is `verify`.
                    Exit(ExitStatus.Clean);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to create witness snapshot: {ex.Message}");
                    Exit(ExitStatus.Error);
                }
            }
            else if (cmd.Equals("verify", StringComparison.OrdinalIgnoreCase))
            {
                string filePath = args[1];
                string? externalPubKey = null;
                string? fetchPubKeyUrl = null;
                bool detailed = false;
                // D090: no CLI path wired maxAge/--max-age through to the verifier, even though
                // WitnessSigner.VerifySnapshot/VerifySnapshotDetailed already support it and
                // SnapshotVerification.Stale is tested. Same numeric-seconds convention as
                // `syxx cage --duration <seconds>`.
                TimeSpan? maxAge = null;

                int vi = 2;
                while (vi < args.Length)
                {
                    if (args[vi] == "--pubkey" && vi + 1 < args.Length)
                    {
                        externalPubKey = args[vi + 1];
                        vi++;
                    }
                    else if (args[vi] == "--fetch-pubkey" && vi + 1 < args.Length)
                    {
                        fetchPubKeyUrl = args[vi + 1];
                        vi++;
                    }
                    else if (args[vi] == "--detailed")
                    {
                        detailed = true;
                    }
                    else if (args[vi] == "--max-age" && vi + 1 < args.Length
                        && double.TryParse(args[vi + 1], out double maxAgeSeconds)
                        && double.IsFinite(maxAgeSeconds) && maxAgeSeconds >= 0)
                    {
                        maxAge = TimeSpan.FromSeconds(maxAgeSeconds);
                        vi++;
                    }
                    vi++;
                }

                try
                {
                    string json = await File.ReadAllTextAsync(filePath);
                    var snapshot = JsonSerializer.Deserialize<WitnessSnapshot>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (snapshot == null)
                    {
                        Console.WriteLine("Error: Failed to deserialize snapshot JSON.");
                        Exit(ExitStatus.Error);
                        return;
                    }

                    // Resolve the public key to use for verification
                    string pubKey = snapshot.PublicKey;
                    string keySource = "embedded";
                    if (externalPubKey is not null)
                    {
                        pubKey = externalPubKey;
                        keySource = "user-supplied";
                    }
                    else if (fetchPubKeyUrl is not null)
                    {
                        // D087 (Windows regression of the same defect already fixed on macOS
                        // SyxxWitness.swift:126-155): require HTTPS, check the HTTP status, and
                        // validate the response decodes to a 32-byte Ed25519 key before trusting it
                        // as a verification anchor — a plain-HTTP or non-200 response is exactly the
                        // on-path adversary Syxx exists to detect.
                        if (!fetchPubKeyUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"[ERROR] --fetch-pubkey requires an https:// URL (got: {fetchPubKeyUrl}). A plain-HTTP trust anchor can be replaced in transit.");
                            Exit(ExitStatus.Error);
                            return;
                        }
                        if (!Uri.TryCreate(fetchPubKeyUrl, UriKind.Absolute, out var fetchUri))
                        {
                            Console.WriteLine($"[ERROR] Invalid URL for --fetch-pubkey: {fetchPubKeyUrl}");
                            Exit(ExitStatus.Error);
                            return;
                        }
                        try
                        {
                            using var http = new HttpClient();
                            var response = await http.GetAsync(fetchUri);
                            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                            {
                                Console.WriteLine($"[ERROR] --fetch-pubkey server returned HTTP {(int)response.StatusCode} — refusing to use a non-200 response as a trust anchor.");
                                Exit(ExitStatus.Error);
                                return;
                            }
                            string rawKey = (await response.Content.ReadAsStringAsync()).Trim();
                            byte[]? keyBytes = null;
                            try { keyBytes = Convert.FromBase64String(rawKey); } catch (FormatException) { }
                            if (string.IsNullOrEmpty(rawKey) || keyBytes == null || keyBytes.Length != 32)
                            {
                                Console.WriteLine("[ERROR] --fetch-pubkey: response did not contain a valid 32-byte Ed25519 public key.");
                                Exit(ExitStatus.Error);
                                return;
                            }
                            pubKey = rawKey;
                            keySource = $"fetched (https) from {fetchPubKeyUrl}";
                            // Print fingerprint so the user can confirm they got the right key.
                            string fingerprint = Convert.ToHexString(SHA256.HashData(keyBytes)).ToLowerInvariant();
                            Console.WriteLine($"  Fetched public key fingerprint (SHA-256): {fingerprint}");
                            Console.WriteLine("  Confirm this fingerprint matches the publisher's published value before proceeding.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error: Failed to fetch public key from {fetchPubKeyUrl}: {ex.Message}");
                            Exit(ExitStatus.Error);
                            return;
                        }
                    }

                    // Build verification snapshot copy with the resolved public key
                    var verifySnapshot = snapshot;
                    if (pubKey != snapshot.PublicKey)
                    {
                        verifySnapshot = new WitnessSnapshot
                        {
                            FormatVersion = snapshot.FormatVersion,
                            Timestamp = snapshot.Timestamp,
                            ExpiresAt = snapshot.ExpiresAt,
                            TargetDomain = snapshot.TargetDomain,
                            Comparison = snapshot.Comparison,
                            Evidence = snapshot.Evidence,
                            Signature = snapshot.Signature,
                            PublicKey = pubKey,
                            KeyId = snapshot.KeyId
                        };
                    }

                    if (detailed)
                    {
                        var result = WitnessSigner.VerifySnapshotDetailed(verifySnapshot, maxAge);
                        var (resultLabel, detailText) = DescribeVerification(result);

                        Console.WriteLine("\nWitness Verification Report:");
                        Console.WriteLine($"  File: {filePath}");
                        Console.WriteLine($"  Target Domain: {snapshot.TargetDomain}");
                        Console.WriteLine($"  Timestamp: {snapshot.Timestamp}");
                        Console.WriteLine($"  Key ID: {snapshot.KeyId}");
                        Console.WriteLine($"  Public Key: {pubKey}");
                        if (keySource != "embedded")
                            Console.WriteLine($"  Key Source: {keySource}");
                        Console.WriteLine($"  Redirection Detected: {snapshot.Comparison.IsRedirectionDetected}");
                        // B100: surface network-evidence quality state alongside the
                        // cryptographic result — a valid signature over degraded or
                        // inconclusive evidence must not read as a clean bill of health.
                        Console.WriteLine($"  Inconclusive: {snapshot.Comparison.IsInconclusive}");
                        Console.WriteLine($"  Degraded: {snapshot.Comparison.IsDegraded}");
                        Console.WriteLine($"  Signature: {snapshot.Signature}");
                        Console.WriteLine($"  Verification: {resultLabel}");
                        Console.WriteLine($"  Detailed Result: {detailText}");
                        // B211: render trust posture so the --detailed output always discloses
                        // whether the signer was actually trusted, not just that the signature verified.
                        if (result == SnapshotVerification.Valid)
                            Console.WriteLine($"  Trust: {TrustPostureLabel(pubKey)}");
                        Exit(ForVerification(result));
                    }
                    else
                    {
                        // D099: this used to reduce verification to a bare bool via
                        // WitnessSigner.VerifySnapshot(...), so an honest expired/stale/
                        // future-dated snapshot printed the exact same "❌ INVALID / TAMPERED
                        // MESSAGE" as an actually-forged signature — the two are forensically
                        // different per SnapshotVerification's own doc comment. Call the detailed
                        // verifier here too (same result --detailed would show for this file) and
                        // report the real reason, still as a short summary.
                        var result = WitnessSigner.VerifySnapshotDetailed(verifySnapshot, maxAge);
                        bool isValid = result == SnapshotVerification.Valid;
                        var (resultLabel, detailText) = DescribeVerification(result);

                        Console.WriteLine("\nWitness Verification Report:");
                        Console.WriteLine($"  File: {filePath}");
                        Console.WriteLine($"  Target Domain: {snapshot.TargetDomain}");
                        Console.WriteLine($"  Timestamp: {snapshot.Timestamp}");
                        Console.WriteLine($"  Key ID: {snapshot.KeyId}");
                        Console.WriteLine($"  Public Key: {pubKey}");
                        if (keySource != "embedded")
                            Console.WriteLine($"  Key Source: {keySource}");
                        Console.WriteLine($"  Redirection Detected: {snapshot.Comparison.IsRedirectionDetected}");
                        // B100: surface network-evidence quality state alongside the
                        // cryptographic result — a valid signature over degraded or
                        // inconclusive evidence must not read as a clean bill of health.
                        Console.WriteLine($"  Inconclusive: {snapshot.Comparison.IsInconclusive}");
                        Console.WriteLine($"  Degraded: {snapshot.Comparison.IsDegraded}");
                        Console.WriteLine($"  Signature: {snapshot.Signature}");
                        Console.WriteLine($"  Verification: {(isValid ? "[VALID] Tamper-evident — Ed25519 signed" : resultLabel)}");
                        if (isValid)
                        {
                            Console.WriteLine($"  (Verified with {keySource} public key — no secret required.)");
                            // B211: render trust posture so an unqualified "VALID" is never
                            // emitted without disclosing whether the signer was actually trusted.
                            Console.WriteLine($"  Trust: {TrustPostureLabel(pubKey)}");
                        }
                        else
                        {
                            Console.WriteLine($"  Detailed Result: {detailText}");
                        }
                        Exit(ForVerification(result));
                    }
                }
                catch (Exception ex)
                {
                    // Unreadable or unparseable input. NEVER 0: automation must not read "could not
                    // open the file" as "the evidence checked out". This was the second half of
                    // B253 — an unreadable snapshot exited 0 exactly like a verified one.
                    Console.WriteLine($"Failed to verify witness snapshot: {ex.Message}");
                    Exit(ExitStatus.Error);
                }
            }
            else if (cmd.Equals("report", StringComparison.OrdinalIgnoreCase))
            {
                // B1 — Shareable signed incident report
                string filePath = args[1];
                string? conclusionPath = null;
                // B254: same freshness control as `verify`, so a report can be gated on the
                // snapshot still being current. No flag = no staleness check, matching `verify`.
                TimeSpan? reportMaxAge = null;
                int ci = 2;
                while (ci < args.Length)
                {
                    if (args[ci] == "--conclusion" || args[ci] == "-c")
                    {
                        if (ci + 1 < args.Length)
                        {
                            conclusionPath = args[ci + 1];
                            ci++;
                        }
                    }
                    else if (args[ci] == "--max-age" && ci + 1 < args.Length
                        && double.TryParse(args[ci + 1], out double reportMaxAgeSeconds)
                        && double.IsFinite(reportMaxAgeSeconds) && reportMaxAgeSeconds >= 0)
                    {
                        reportMaxAge = TimeSpan.FromSeconds(reportMaxAgeSeconds);
                        ci++;
                    }
                    ci++;
                }

                try
                {
                    string json = await File.ReadAllTextAsync(filePath);
                    var snapshot = JsonSerializer.Deserialize<WitnessSnapshot>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (snapshot == null)
                    {
                        Console.WriteLine("Error: Failed to deserialize snapshot JSON.");
                        Exit(ExitStatus.Error);
                        return;
                    }

                    // B254: VERIFY BEFORE RENDERING. This command previously deserialized a
                    // snapshot and handed it straight to the renderer, so a snapshot whose
                    // displayed answers had been edited after signing printed the ATTACKER's
                    // answers under a "SYXX SIGNED INCIDENT REPORT" header, and exited 0. The
                    // header is an assertion of integrity; printing unverified content under it is
                    // an affirmative false claim, which is worse than having no report command at
                    // all. The verification must happen before ANY other work in this branch —
                    // including reading --conclusion — so that a second, unrelated failure cannot
                    // mask the tampering with an operational code.
                    var reportVerification = WitnessSigner.VerifySnapshotDetailed(snapshot, reportMaxAge);
                    if (reportVerification != SnapshotVerification.Valid)
                    {
                        PrintReportRefusal(filePath, reportVerification);
                        // Same mapping `verify` uses: DisplayMismatch / InvalidSignature -> 1
                        // (THREAT DETECTED, deliberate), Expired / Stale / FutureDated /
                        // UntrustedSigner / UnsupportedFormat / MalformedInput -> 2. Never 0.
                        Exit(ForVerification(reportVerification));
                        return;
                    }

                    // Optionally load a Conclusion for the A2 explanation.
                    PlainExplanation? explanation = null;
                    if (conclusionPath is not null)
                    {
                        string cJson = await File.ReadAllTextAsync(conclusionPath);
                        var conclusion = JsonSerializer.Deserialize<Conclusion>(cJson, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (conclusion is not null)
                            explanation = PlainExplanations.For(conclusion);
                    }

                    string report = SignedIncidentReport.Render(snapshot, explanation);
                    Console.WriteLine(report);
                    // Reached only when VerifySnapshotDetailed returned Valid, so 0 here now means
                    // "this snapshot verified AND the report was produced".
                    Exit(ExitStatus.Clean);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to generate incident report: {ex.Message}");
                    Exit(ExitStatus.Error);
                }
            }
            else
            {
                PrintUsage();
                Exit(ExitStatus.Error);
            }
        }

        /// <summary>
        /// B254 — what `report` prints INSTEAD OF a report when the snapshot does not verify.
        ///
        /// Deliberately prints NOTHING that came out of the snapshot: not the conclusion, not the
        /// answers, not the redirection flags, not the signature, key ID or public key. Every one
        /// of those is attacker-controlled once verification has failed, and the incident report is
        /// a SHAREABLE artifact whose intended reader is a third party (an ISP, a regulator, a
        /// court). Terminal output is screenshotted and forwarded stripped of context, so a
        /// "refused" banner above attacker-chosen answers is one crop away from becoming the forged
        /// report it was supposed to prevent. The only fields echoed here are the path the USER
        /// typed and the verification result Syxx itself computed.
        ///
        /// A user diagnosing their own file is not left stranded: `witness verify --detailed`
        /// already prints the domain, timestamp, key ID, public key and the specific failure
        /// reason, under a header that makes no integrity claim.
        ///
        /// Byte-for-byte twin of `SyxxWitnessCLI.printReportRefusal` on macOS.
        /// </summary>
        static void PrintReportRefusal(string filePath, SnapshotVerification result)
        {
            var (label, detail) = DescribeVerification(result);
            Console.WriteLine();
            Console.WriteLine("[REFUSED] Snapshot did not verify. No incident report was produced.");
            Console.WriteLine();
            Console.WriteLine($"  File:   {filePath}");
            Console.WriteLine($"  Result: {label}");
            Console.WriteLine($"  Detail: {detail}");
            Console.WriteLine();
            Console.WriteLine("  Syxx will not print unverified content under a signed incident report");
            Console.WriteLine("  header, so none of this file's findings, answers or seal are shown.");
            Console.WriteLine();
            Console.WriteLine("  To inspect the file, run:  syxx witness verify <file> --detailed");
            Console.WriteLine();
        }

        /// Map a SnapshotVerification to a (label, detail) pair for user-facing output.
        static (string Label, string Detail) DescribeVerification(SnapshotVerification result)
        {
            return result switch
            {
                SnapshotVerification.Valid => ("[VALID]", "Tamper-evident — Ed25519 signed content matches."),
                SnapshotVerification.MalformedInput => ("[INVALID] MALFORMED INPUT", "Could not parse snapshot or public key."),
                SnapshotVerification.UnsupportedFormat => ("[INVALID] UNSUPPORTED FORMAT", "Snapshot format version not recognized."),
                SnapshotVerification.Expired => ("[INCONCLUSIVE] EXPIRED", "The snapshot's expiry date has passed."),
                SnapshotVerification.Stale => ("[INCONCLUSIVE] STALE", "Snapshot is older than the allowed freshness window."),
                SnapshotVerification.FutureDated => ("[INCONCLUSIVE] FUTURE-DATED", "Snapshot timestamp is ahead of system clock."),
                SnapshotVerification.UntrustedSigner => ("[INCONCLUSIVE] UNTRUSTED SIGNER", "Public key is not in the trust set."),
                SnapshotVerification.DisplayMismatch => ("[TAMPERED] DISPLAY MISMATCH", "Display fields do not match the signed evidence — tampered presentation."),
                SnapshotVerification.InvalidSignature => ("[TAMPERED] INVALID SIGNATURE", "Ed25519 signature does not match the content."),
                _ => ("[ERROR] UNKNOWN", "Unexpected verification state.")
            };
        }

        /// B211: render the trust posture as a third state alongside the cryptographic result.
        /// A Valid snapshot whose signer is not in the trust set (or no trust set is configured)
        /// must not read identically to one whose signer IS trusted — the unqualified
        /// "VALID (Tamper-evident)" label is a reassuring claim emitted before the trust input
        /// was ever obtained.
        static string TrustPostureLabel(string publicKey)
        {
            var trustSet = WitnessPinning.ConfiguredTrustedWitnessKeys();
            if (trustSet.Count == 0)
                return "signer unverified (no trust set configured)";
            return trustSet.Contains(publicKey)
                ? "signer trusted"
                : "signer NOT in your trust set";
        }

        /// <summary>
        /// Restricts a freshly-written key file to the current user only — the Windows/Linux
        /// equivalent of macOS's chmod 0600 (D086 fix, ported from SyxxWitness.swift's --gen-keypair
        /// handler). Windows: replaces the file's ACL with a single FullControl rule for the current
        /// user and disables inheritance. Linux (this CLI also ships linux-x64/linux-arm64 RIDs, see
        /// Syxx.csproj RuntimeIdentifiers): sets POSIX mode 0600 directly via File.SetUnixFileMode.
        /// Best-effort; the key file has already been written by the time this runs, so a failure
        /// here is surfaced to the caller rather than silently ignored.
        /// </summary>
        private static void RestrictKeyFileToCurrentUser(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                var security = new FileSecurity();
                security.SetAccessRuleProtection(true, false);
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser is not null)
                {
                    security.AddAccessRule(new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
                }
                new FileInfo(path).SetAccessControl(security);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine(@"Usage: syxx witness create <domain> --key <b64-private-key> [--output f] | verify <file> [--pubkey <b64>] [--fetch-pubkey <url>] [--detailed] [--max-age <seconds>] | report <file> [--conclusion <file>] | --gen-keypair [--key-file <path>]

  create <domain>       Create a signed witness snapshot for the given domain
    --key, -k           Base64-encoded Ed25519 private key (required; set SYXX_WITNESS_KEY env var)
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
  --help, -h            Show this help");
        }
    }
}

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
                "Usage: syxx witness create <domain> --key-file <path> [--output f] | verify <file> [--pubkey <b64>] [--fetch-pubkey <url>] [--detailed] [--max-age <seconds>] | report <file> [--conclusion <file>] | --gen-keypair [--key-file <path>] [--force]"))
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
                bool forceOverwrite = false;
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--key-file" && i + 1 < args.Length)
                    {
                        keyFilePath = args[i + 1];
                        i++;
                    }
                    // B249: opt in to destroying the current signing identity.
                    else if (args[i] == "--force")
                    {
                        forceOverwrite = true;
                    }
                }
                string keyFile = keyFilePath ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".syxx", "witness.key");

                // B249 (C-10) — the Windows/Linux half of the guard macOS has carried since
                // SyxxWitness.swift:69-77. Without it this command minted a fresh keypair straight
                // over an existing one: `FileMode.Create` truncates, `File.WriteAllBytesAsync`
                // overwrites, and neither asks first. Every snapshot ever sealed with the old key
                // is then orphaned from its signer — the signatures still verify against a public
                // key nothing on the machine can produce again, and no warning was printed.
                //
                // The probe is TRI-STATE on purpose (RG-2 / spine #2). `File.Exists` cannot be used
                // here: it returns false for permission-denied and IO failure exactly as it does for
                // a missing file, so it converts "I could not look" into "there is nothing there" —
                // which is the same class of defect as G-02a in WitnessKeyStore and M-01 on macOS.
                // An Undetermined probe refuses EVEN WITH --force: --force means "I know a key is
                // there and I accept destroying it", not "proceed without looking". The way out is
                // --key-file <path>, which the message names.
                var presence = ProbeKeyFilePresence(keyFile);
                if (presence.Presence == KeyPresence.Undetermined)
                {
                    Console.WriteLine($"[ERROR] Could not determine whether a private key already exists at: {keyFile}");
                    Console.WriteLine($"[ERROR] Reason: {presence.Error?.Message ?? "unknown"}");
                    Console.WriteLine("[ERROR] Refusing to mint a keypair without establishing that nothing is there — overwriting an existing key would orphan every snapshot you have already signed from its signer.");
                    Console.WriteLine("[ERROR] Fix the path's permissions, or use --key-file <path> to write to a location Syxx can read.");
                    Exit(ExitStatus.Error);
                    return;
                }
                if (presence.Presence == KeyPresence.Present && !forceOverwrite)
                {
                    Console.WriteLine($"[ERROR] Private key already exists at: {keyFile}");
                    Console.WriteLine("[ERROR] Re-running --gen-keypair would destroy your current signing identity.");
                    Console.WriteLine("[ERROR] Every snapshot you have already sealed would be orphaned from its signer.");
                    Console.WriteLine("[ERROR] Use --force to overwrite, or --key-file <path> to write to a different location.");
                    Exit(ExitStatus.Error);
                    return;
                }

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
                string? createKeyFilePath = null;
                bool insecureKeyArg = false;
                string outputPath = "witness.json";

                int idx = 2;
                while (idx < args.Length)
                {
                    // B189 (C-11): --key-file reads the key from an owner-only file. This is the
                    // preferred form and the one the help documents; it never puts key material on
                    // argv. macOS has had it since SyxxWitness.swift:125-130 — this side had only
                    // --key, so the leaking form was the ONLY form the shared CLI offered, and
                    // `--gen-keypair` wrote a key file that `create` had no way to read back.
                    if (args[idx] == "--key-file")
                    {
                        if (idx + 1 < args.Length)
                        {
                            createKeyFilePath = args[idx + 1];
                            idx++;
                        }
                    }
                    // B189: explicit opt-in that silences the argv-exposure warning below.
                    else if (args[idx] == "--insecure-key-arg")
                    {
                        insecureKeyArg = true;
                    }
                    else if (args[idx] == "--key" || args[idx] == "-k")
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

                // B189: --key-file takes precedence — read from an owner-only file, never argv.
                // Twin of SyxxWitness.swift:147-160.
                if (createKeyFilePath is not null)
                {
                    try
                    {
                        WarnIfKeyFileIsGroupOrWorldAccessible(createKeyFilePath);
                        key = (await File.ReadAllTextAsync(createKeyFilePath)).Trim();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Could not read key file {createKeyFilePath}: {ex.Message}");
                        Exit(ExitStatus.Error);
                        return;
                    }
                }

                // B189 (C-11): a private key on argv is readable by any local account for as long
                // as the process lives, and it persists afterwards in shell history and CI logs.
                // This is WORSE on Linux than on the platform the warning was written for: stock
                // Ubuntu mounts /proc without `hidepid`, so /proc/<pid>/cmdline is world-readable
                // and `ps -ww` needs no privilege at all. The shared CLI's help used to LEAD with
                // this form, which made the leaking path the documented one.
                //
                // Deliberately a WARNING and not a refusal, byte-for-byte with macOS
                // SyxxWitness.swift:161-165: the two CLIs are one contract, and hardening only this
                // side would mean the same command line succeeds on macOS and fails on Linux. See
                // the E14 report — whether BOTH should refuse is an owner call, not a seat call.
                if (key is not null && createKeyFilePath is null && !insecureKeyArg)
                {
                    Console.WriteLine("[WARNING] Passing the private key on argv exposes it via `ps -ww`, shell history, and CI logs.");
                    if (OperatingSystem.IsLinux())
                    {
                        Console.WriteLine("  On Linux /proc/<pid>/cmdline is world-readable unless /proc is mounted with hidepid — any local account can read this key while the command runs.");
                    }
                    Console.WriteLine("  Use --key-file <path> or set SYXX_WITNESS_KEY instead. To suppress this warning, pass --insecure-key-arg.");
                }

                key ??= Environment.GetEnvironmentVariable("SYXX_WITNESS_KEY");
                if (string.IsNullOrEmpty(key))
                {
                    Console.WriteLine("[ERROR] Private key required (use --key-file <path>, set SYXX_WITNESS_KEY, or --gen-keypair to create a keypair).");
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
        /// Byte-for-byte twin of `SyxxWitnessCLI.printReportRefusal` on macOS — and as of E14 that
        /// is actually TRUE. It was not: this method's literals matched, but the `Result:` line is
        /// rendered by <see cref="DescribeVerification"/>, which returned `[INCONCLUSIVE] EXPIRED`
        /// here against macOS's `❌ EXPIRED`. The two refusals therefore differed on every non-valid
        /// state, which is every state this method can be reached in. The claim is now backed by
        /// both platforms returning the same bracketed tri-state labels; if either side's
        /// DescribeVerification changes alone, this comment becomes false again.
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

        /// <summary>
        /// Map a SnapshotVerification to a (label, detail) pair for user-facing output.
        ///
        /// The bracketed prefix names the class the EXIT CODE will carry, so the printed line and
        /// `$?` cannot tell different stories: `[VALID]` -> 0, `[TAMPERED]` -> 1,
        /// `[INCONCLUSIVE]` -> 2. Byte-for-byte twin of `SyxxWitnessCLI.describeVerification`.
        ///
        /// E14: MalformedInput and UnsupportedFormat were labelled `[INVALID]` while
        /// <see cref="ForVerification"/> mapped both to <see cref="ExitStatus.Inconclusive"/> — so
        /// the text read as an accusation ("this file is invalid") on exactly the two states the
        /// contract classifies as UNJUDGEABLE, and no other label used `[INVALID]` at all. A
        /// snapshot Syxx cannot parse may be perfectly honest and merely from a newer format; that
        /// is the same claim as EXPIRED, not the same claim as INVALID SIGNATURE. Corrected on both
        /// platforms in the same change. No test asserted the prefix — the suites assert the bare
        /// tokens ("MALFORMED INPUT", "DISPLAY MISMATCH"), which are unchanged.
        /// </summary>
        static (string Label, string Detail) DescribeVerification(SnapshotVerification result)
        {
            return result switch
            {
                SnapshotVerification.Valid => ("[VALID]", "Tamper-evident — Ed25519 signed content matches."),
                SnapshotVerification.MalformedInput => ("[INCONCLUSIVE] MALFORMED INPUT", "Could not parse snapshot or public key."),
                SnapshotVerification.UnsupportedFormat => ("[INCONCLUSIVE] UNSUPPORTED FORMAT", "Snapshot format version not recognized."),
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
        /// B189 (C-11): warn when a key file is readable by anyone but its owner — the twin of
        /// macOS's <c>perms &amp; 0o077 != 0</c> check at <c>SyxxWitness.swift:150-154</c>.
        ///
        /// POSIX-only by construction. Windows has no POSIX mode, and
        /// <see cref="File.GetUnixFileMode(string)"/> is documented to return
        /// <see cref="UnixFileMode.None"/> there — which would render as a reassuring "0" and read
        /// as MORE secure than 0600. So the check is skipped on Windows and says nothing, rather
        /// than printing a number it did not measure (spine #4). Windows key-file ACLs are enforced
        /// at write time by <see cref="RestrictKeyFileToCurrentUser"/>.
        ///
        /// Best-effort: a mode we cannot read is not evidence the file is safe, but it is also not
        /// evidence it is unsafe, so this stays silent and lets the caller's own read fail if the
        /// file is genuinely unreachable.
        /// </summary>
        private static void WarnIfKeyFileIsGroupOrWorldAccessible(string path)
        {
            if (OperatingSystem.IsWindows()) return;
            UnixFileMode mode;
            try { mode = File.GetUnixFileMode(path); }
            catch { return; }
            const UnixFileMode GroupAndOther =
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            if ((mode & GroupAndOther) != 0)
            {
                Console.WriteLine($"[WARNING] Key file {path} has permissions {Convert.ToString((int)(mode & (UnixFileMode)0x1FF), 8)} — should be 600 (owner-only).");
            }
        }

        /// <summary>What <see cref="ProbeKeyFilePresence"/> established, plus the failure that
        /// prevented it establishing anything. <see cref="KeyPresence"/> is reused from
        /// <c>Syxx.Kit.WitnessKeyStore</c> so the CLI and the GUI key store speak one vocabulary
        /// for the same three states.</summary>
        private readonly record struct KeyFileProbe(KeyPresence Presence, Exception? Error);

        /// <summary>
        /// B249 (C-10): does a private key already sit at <paramref name="path"/>?
        ///
        /// Returns <see cref="KeyPresence.Present"/> only on a file that opened and holds bytes,
        /// <see cref="KeyPresence.Absent"/> only on POSITIVE proof of absence (the file is not
        /// there, its directory is not there, or it opened and is zero-length — an interrupted or
        /// blank write holds no key material, so nothing is destroyed by writing over it), and
        /// <see cref="KeyPresence.Undetermined"/> for everything else: permission denied, a failing
        /// mount, a path that is a directory, a locked file.
        ///
        /// It opens rather than calling <see cref="File.Exists"/> deliberately. <c>File.Exists</c>
        /// returns false on <see cref="UnauthorizedAccessException"/> and on IO failure exactly as
        /// it does for a missing file, so a key that is sitting right there behind a traversal
        /// permission reads as "no key" — and the caller mints over it. That is G-02a in
        /// <c>WitnessKeyStore</c> and M-01 on macOS, and it is the specific way this guard would
        /// fail open if written the obvious way. Never throws.
        /// </summary>
        private static KeyFileProbe ProbeKeyFilePresence(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return new KeyFileProbe(
                    stream.Length == 0 ? KeyPresence.Absent : KeyPresence.Present, null);
            }
            catch (FileNotFoundException)
            {
                return new KeyFileProbe(KeyPresence.Absent, null);
            }
            catch (DirectoryNotFoundException)
            {
                return new KeyFileProbe(KeyPresence.Absent, null);
            }
            catch (Exception ex)
            {
                // UnauthorizedAccessException (no read/traverse permission, or the path names a
                // directory), IOException (failing mount, locked file), PathTooLongException, ...
                // The key may well be there; we simply could not look.
                return new KeyFileProbe(KeyPresence.Undetermined, ex);
            }
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
            Console.WriteLine(@"Usage: syxx witness create <domain> --key-file <path> [--output f] | verify <file> [--pubkey <b64>] [--fetch-pubkey <url>] [--detailed] [--max-age <seconds>] | report <file> [--conclusion <file>] | --gen-keypair [--key-file <path>] [--force]

  create <domain>       Create a signed witness snapshot for the given domain
    --key-file <path>   Read the Ed25519 private key from a 0600 file (preferred)
    --key, -k           Base64-encoded key on argv — INSECURE, exposed via `ps -ww`,
                        /proc/<pid>/cmdline, shell history and CI logs. Prefer --key-file
                        or the SYXX_WITNESS_KEY env var; pass --insecure-key-arg to silence
                        the warning.
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
    --force             Overwrite an existing private key. Without it, --gen-keypair REFUSES
                        when a key is already there: replacing it orphans every snapshot you
                        have already sealed from its signer.
  --help, -h            Show this help");
        }
    }
}

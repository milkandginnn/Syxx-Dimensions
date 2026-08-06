using System;
using System.Diagnostics;
using System.IO;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    /// <summary>
    /// B249 / B189 (plan items C-10, C-11) — the two ways the shared CLI could lose a user's
    /// signing identity or hand it to a bystander.
    ///
    /// C-10: `syxx witness --gen-keypair` had NO existence check. `FileMode.Create` truncates and
    /// `File.WriteAllBytesAsync` overwrites, so a second run minted a fresh identity straight over
    /// the first. Every snapshot sealed with the old key is then orphaned from its signer — the
    /// signature still verifies against a public key that no longer exists anywhere. macOS has
    /// refused this since `SyxxWitness.swift:69-77`; this side did not, even though
    /// `WitnessCommand.cs` already cites D086, an EARLIER port of a key-safety fix from that very
    /// file. The pairing had been ported once already and B249 was missed.
    ///
    /// C-11: `--key` put the private key on argv with no gate, and the usage string LED with it —
    /// the leaking form was the documented one. On Linux that is worse than on the platform the
    /// macOS warning was written for: stock Ubuntu mounts /proc without `hidepid`, so
    /// /proc/&lt;pid&gt;/cmdline is world-readable to every local account. This side also had no
    /// `--key-file` on `create` at all, so `--gen-keypair` wrote a key file the same CLI could not
    /// read back.
    ///
    /// THESE TESTS SPAWN THE REAL CLI, for the same reason `WitnessVerifyExitCodeTests` does: the
    /// guard is only worth anything as a process that refuses, and a unit test on a helper would
    /// not have caught either defect. Every assertion here is on a real exit code or real stdout,
    /// and — critically for C-10 — on the BYTES STILL ON DISK after the refusal.
    ///
    /// Mutation-checked 2026-08-04: with the C-10 guard block deleted,
    /// <see cref="ExistingKeyWithoutForceRefusesAndDoesNotOverwrite"/> and
    /// <see cref="UndeterminedPresenceRefusesRatherThanMintingOverIt"/> both fail; with the C-11
    /// warning block deleted, <see cref="KeyOnArgvWarnsAboutProcessListExposure"/> fails.
    /// </summary>
    public sealed class WitnessKeypairGuardTests : IDisposable
    {
        private readonly string _dir;

        public WitnessKeypairGuardTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "c10-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        // ────────────────────────────────────────────────────────────────────
        // C-10 — --gen-keypair must not destroy an existing signing identity
        // ────────────────────────────────────────────────────────────────────

        /// THE DEFECT ITSELF. Before the fix this overwrote the key and exited 0.
        /// The disk assertion is the one that matters: an exit code can be fixed without the
        /// destruction being fixed.
        [Fact]
        public void ExistingKeyWithoutForceRefusesAndDoesNotOverwrite()
        {
            string keyFile = Path.Combine(_dir, "existing.key");
            var (original, _) = WitnessSigner.GenerateKeyPair();
            File.WriteAllText(keyFile, original + "\n");

            var run = RunSyxx("witness", "--gen-keypair", "--key-file", keyFile);

            Assert.True(run.Code == 3,
                $"re-running --gen-keypair over an existing key must exit 3 (error), got {run.Code}.\n{run.Output}");
            Assert.Contains("already exists", run.Output);
            Assert.Contains("--force", run.Output);
            // The whole point: the identity is still there, byte for byte.
            Assert.Equal(original + "\n", File.ReadAllText(keyFile));
        }

        /// --force is the opt-in, and it must actually work — a guard that cannot be overridden
        /// would just move the harm to "user cannot rotate their key".
        [Fact]
        public void ExistingKeyWithForceOverwrites()
        {
            string keyFile = Path.Combine(_dir, "rotate.key");
            var (original, _) = WitnessSigner.GenerateKeyPair();
            File.WriteAllText(keyFile, original + "\n");

            var run = RunSyxx("witness", "--gen-keypair", "--key-file", keyFile, "--force");

            Assert.True(run.Code == 0, $"--force must succeed, got {run.Code}.\n{run.Output}");
            string after = File.ReadAllText(keyFile).Trim();
            Assert.NotEqual(original, after);
            // And what replaced it is real key material, not a truncation artifact.
            WitnessSigner.ValidatePrivateKey(after);
        }

        /// SPINE #2 — "could not look" must never be reported as "nothing is there". A path the
        /// CLI cannot open as a file (here: a directory, which throws
        /// UnauthorizedAccessException on every platform and needs no chmod, so the test is
        /// meaningful even when the suite runs as root) is UNDETERMINED, and the command refuses
        /// rather than minting over whatever may be behind it.
        ///
        /// This is the assertion that separates the fix from a `File.Exists` version of it:
        /// `File.Exists` returns false here, exactly as it does for a missing file, so a
        /// File.Exists-based guard would sail past this case and mint.
        [Fact]
        public void UndeterminedPresenceRefusesRatherThanMintingOverIt()
        {
            string keyPath = Path.Combine(_dir, "is-a-directory.key");
            Directory.CreateDirectory(keyPath);

            var run = RunSyxx("witness", "--gen-keypair", "--key-file", keyPath);

            Assert.True(run.Code == 3,
                $"an undeterminable key path must exit 3, got {run.Code}.\n{run.Output}");
            Assert.Contains("Could not determine", run.Output);
            // It must not have claimed absence, and must not have claimed success.
            Assert.DoesNotContain("Private key written to", run.Output);
            Assert.True(Directory.Exists(keyPath), "the guard must not have replaced the path");
        }

        /// --force does NOT override Undetermined: it means "I know a key is there and I accept
        /// destroying it", not "proceed without looking". Pinned deliberately — this is the arm a
        /// later change is most likely to relax.
        [Fact]
        public void ForceDoesNotOverrideUndeterminedPresence()
        {
            string keyPath = Path.Combine(_dir, "also-a-directory.key");
            Directory.CreateDirectory(keyPath);

            var run = RunSyxx("witness", "--gen-keypair", "--key-file", keyPath, "--force");

            Assert.True(run.Code == 3,
                $"--force must not defeat the undetermined-presence refusal, got {run.Code}.\n{run.Output}");
            Assert.Contains("Could not determine", run.Output);
        }

        /// The happy path still works: a first run on a clean path mints and writes real material.
        [Fact]
        public void FirstRunOnAClearPathMintsAKeyFile()
        {
            string keyFile = Path.Combine(_dir, "nested", "first.key");

            var run = RunSyxx("witness", "--gen-keypair", "--key-file", keyFile);

            Assert.True(run.Code == 0, $"a first --gen-keypair must exit 0, got {run.Code}.\n{run.Output}");
            Assert.Contains("Private key written to", run.Output);
            WitnessSigner.ValidatePrivateKey(File.ReadAllText(keyFile).Trim());
        }

        /// A zero-length file is an interrupted or blank write: it holds no key material, so
        /// nothing is destroyed by writing over it. Same reasoning WitnessKeyStore applies to an
        /// empty token file. Pinned so the "empty means absent" decision stays a decision.
        [Fact]
        public void ZeroLengthKeyFileCountsAsAbsentAndIsMintedOver()
        {
            string keyFile = Path.Combine(_dir, "blank.key");
            File.WriteAllBytes(keyFile, Array.Empty<byte>());

            var run = RunSyxx("witness", "--gen-keypair", "--key-file", keyFile);

            Assert.True(run.Code == 0, $"an empty key file holds no identity, got {run.Code}.\n{run.Output}");
            WitnessSigner.ValidatePrivateKey(File.ReadAllText(keyFile).Trim());
        }

        // ────────────────────────────────────────────────────────────────────
        // C-11 — the private key must not be the documented way in
        // ────────────────────────────────────────────────────────────────────

        /// The argv-exposure warning. An empty --key value is deliberate: the parser stores it
        /// (non-null), so the warning fires, and the emptiness then trips the "key required" exit
        /// BEFORE any DNS work — so this asserts on the real code path without a network round
        /// trip. Before the fix this printed nothing at all.
        [Fact]
        public void KeyOnArgvWarnsAboutProcessListExposure()
        {
            var run = RunSyxx("witness", "create", "example.com", "--key", "");

            Assert.Contains("Passing the private key on argv", run.Output);
            Assert.Contains("ps -ww", run.Output);
            Assert.Contains("--key-file", run.Output);
        }

        /// --insecure-key-arg is the acknowledgement, and it silences the warning — matching
        /// macOS SyxxWitness.swift:161-165 exactly. If this ever starts refusing instead, BOTH
        /// platforms must change together; see the E14 report.
        [Fact]
        public void InsecureKeyArgSuppressesTheWarning()
        {
            var run = RunSyxx("witness", "create", "example.com", "--key", "", "--insecure-key-arg");

            Assert.DoesNotContain("Passing the private key on argv", run.Output);
        }

        /// --key-file is wired into `create` and takes PRECEDENCE over --key. Proven without a
        /// network round trip: the file holds only whitespace, so if the file won, the key is
        /// empty and the command exits 3 with "Private key required". That outcome is reachable
        /// only if --key-file clobbered the argv value.
        [Fact]
        public void KeyFileTakesPrecedenceOverKeyOnArgv()
        {
            string keyFile = Path.Combine(_dir, "whitespace.key");
            File.WriteAllText(keyFile, "   \n");

            var run = RunSyxx("witness", "create", "example.com",
                "--key", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "--key-file", keyFile);

            Assert.True(run.Code == 3, $"expected the empty key file to win, got {run.Code}.\n{run.Output}");
            Assert.Contains("Private key required", run.Output);
            // ...and the argv path must not have been silently used behind the scenes.
            Assert.DoesNotContain("Resolving vantage data", run.Output);
        }

        /// An unreadable --key-file is an operational error with a named cause, never a fallback
        /// to some other key. Twin of SyxxWitness.swift:156-159.
        [Fact]
        public void UnreadableKeyFileIsAnErrorNotAFallback()
        {
            var run = RunSyxx("witness", "create", "example.com",
                "--key-file", Path.Combine(_dir, "no-such.key"));

            Assert.True(run.Code == 3, $"an unreadable key file must exit 3, got {run.Code}.\n{run.Output}");
            Assert.Contains("Could not read key file", run.Output);
        }

        /// A key file readable by group or world is called out — the twin of macOS's
        /// `perms & 0o077` check. POSIX-only: Windows has no POSIX mode and
        /// File.GetUnixFileMode returns None there, which would render as a reassuring "0".
        [Fact]
        public void GroupOrWorldReadableKeyFileIsFlagged()
        {
            if (OperatingSystem.IsWindows()) return;   // no POSIX mode to measure

            string keyFile = Path.Combine(_dir, "loose.key");
            File.WriteAllText(keyFile, "   \n");
            File.SetUnixFileMode(keyFile,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.OtherRead);   // 0644

            var run = RunSyxx("witness", "create", "example.com", "--key-file", keyFile);

            Assert.Contains("should be 600", run.Output);
        }

        /// Half of C-11 is the documentation itself: the old usage string LED with
        /// `create <domain> --key <b64-private-key>`, so the leaking form was the one the tool
        /// told people to use.
        ///
        /// `--help` is served by SyxxConfig.HandleGlobalFlags, which prints only the one-line
        /// usage — the detailed listing below it is reached on misuse. Both surfaces are asserted
        /// because a user hits whichever one they hit.
        [Fact]
        public void HelpUsageLineNoLongerLeadsWithTheArgvKeyForm()
        {
            var run = RunSyxx("witness", "--help");

            Assert.Equal(0, run.Code);
            Assert.Contains("create <domain> --key-file <path>", run.Output);
            Assert.DoesNotContain("create <domain> --key <b64-private-key>", run.Output);
        }

        /// The detailed flag listing (PrintUsage) must name --key-file as preferred, mark --key
        /// insecure, and document --force. Reached here through command misuse, which is the only
        /// path that prints it.
        [Fact]
        public void DetailedUsageMarksArgvKeyInsecureAndDocumentsForce()
        {
            var run = RunSyxx("witness", "create");

            Assert.Equal(3, run.Code);
            Assert.Contains("--key-file <path>   Read the Ed25519 private key from a 0600 file (preferred)", run.Output);
            Assert.Contains("INSECURE", run.Output);
            Assert.Contains("--force", run.Output);
            Assert.DoesNotContain("create <domain> --key <b64-private-key>", run.Output);
        }

        // ---- process harness (same shape as WitnessVerifyExitCodeTests) ----

        private readonly record struct Run(int Code, string Output);

        private static Run RunSyxx(params string[] args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add(CliAssemblyPath());
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException("could not start the Syxx CLI");
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return new Run(p.ExitCode, stdout + stderr);
        }

        /// Throws rather than skipping when the CLI is missing: a silently-skipped process test is
        /// indistinguishable from a passing one, which is how B253 survived.
        private static string CliAssemblyPath()
        {
            var tfmDir = new DirectoryInfo(AppContext.BaseDirectory);
            string tfm = tfmDir.Name;
            string config = tfmDir.Parent!.Name;
            var solutionDir = tfmDir.Parent!.Parent!.Parent!.Parent!;
            string candidate = Path.Combine(solutionDir.FullName, "Syxx", "bin", config, tfm, "Syxx.dll");
            if (File.Exists(candidate)) return candidate;

            throw new FileNotFoundException(
                $"the Syxx CLI was not found at {candidate}. Build it with " +
                "`dotnet build Syxx/Syxx.csproj` first. Failing rather than skipping on purpose.",
                candidate);
        }
    }
}

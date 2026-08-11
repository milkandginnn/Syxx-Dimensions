using System;
using System.IO;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    // The fingerprint's QUALITY contract. The bug being killed here: GetSystemFingerprint() fell
    // back to Environment.MachineName (the HOSTNAME) whenever the registry read failed — which is
    // ALWAYS, off Windows. Every Linux box fingerprinted as its hostname: two machines named
    // "ubuntu" were one machine to a licence server, and renaming a host burned an activation.
    //
    // A fingerprint that silently degrades into a hostname is worse than none: it is confidently
    // wrong, and node-locking would bind licences to the wrong things.
    public class MachineFingerprintTests
    {
        // MARK: - Quality travels with the value

        [Fact]
        public void ABlankRawId_IsUnavailable_NeverHashedIntoAConfidentLookingValue()
        {
            // The failure mode in one test: hashing "" produces a perfectly plausible 64-hex string
            // that is IDENTICAL on every machine. Every device would share one fingerprint.
            Assert.Equal(MachineFingerprint.Unavailable,
                         MachineFingerprints.From("", FingerprintQuality.Hardware, "x"));
            Assert.Equal(MachineFingerprint.Unavailable,
                         MachineFingerprints.From("   ", FingerprintQuality.Hardware, "x"));
            Assert.Equal(MachineFingerprint.Unavailable,
                         MachineFingerprints.From(null, FingerprintQuality.Hardware, "x"));
        }

        [Fact]
        public void OnlyHardwareIsNodeLockable()
        {
            Assert.True(MachineFingerprints.From("uuid", FingerprintQuality.Hardware, "s").IsNodeLockable);
            // Install-scoped must NOT be node-lockable: a reinstall would look like a new machine
            // and eat another activation.
            Assert.False(MachineFingerprints.From("id", FingerprintQuality.InstallScoped, "s").IsNodeLockable);
            Assert.False(MachineFingerprint.Unavailable.IsNodeLockable);
        }

        [Fact]
        public void TheRawIdentifierNeverLeavesTheDevice()
        {
            // Privacy: the raw id is stable and cross-correlatable with other services, so only a
            // salted hash is exposed.
            var raw = "4C4C4544-0037-5A10-8046-C4C04F4E4A32";
            var fp = MachineFingerprints.From(raw, FingerprintQuality.Hardware, "dmi");
            Assert.DoesNotContain(raw, fp.Value);
            Assert.Equal(64, fp.Value.Length);          // sha256 hex
        }

        [Fact]
        public void HashingIsStableAndDistinct()
        {
            Assert.Equal(MachineFingerprints.Hash("a"), MachineFingerprints.Hash("a"));
            Assert.NotEqual(MachineFingerprints.Hash("a"), MachineFingerprints.Hash("b"));
        }

        // MARK: - Linux: the DMI placeholder trap

        [Fact]
        public void AnAllZeroDmiUuid_IsUnavailable_NotAnIdentityEveryVmShares()
        {
            // Kernels print placeholder UUIDs on hardware/VMs with no real one. Accepting it would
            // give every such box the SAME fingerprint — worse than the hostname bug, because it
            // would look like a legitimate hardware id.
            var dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                var p = Path.Combine(dir, "product_uuid");
                File.WriteAllText(p, "00000000-0000-0000-0000-000000000000\n");
                Assert.Equal(FingerprintQuality.Unavailable, MachineFingerprints.LinuxHardware(p).Quality);

                File.WriteAllText(p, "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF\n");
                Assert.Equal(FingerprintQuality.Unavailable, MachineFingerprints.LinuxHardware(p).Quality);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ARealDmiUuid_IsHardwareQuality()
        {
            // Mutation guard: without this, "always Unavailable" would pass every test above.
            var dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                var p = Path.Combine(dir, "product_uuid");
                File.WriteAllText(p, "4C4C4544-0037-5A10-8046-C4C04F4E4A32\n");
                var fp = MachineFingerprints.LinuxHardware(p);

                Assert.Equal(FingerprintQuality.Hardware, fp.Quality);
                Assert.True(fp.IsNodeLockable);
                Assert.Equal("dmi:product_uuid", fp.Source);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void AnUnreadableDmiPath_IsUnavailable_NotAThrowAndNotAFallback()
        {
            // The realistic unprivileged case: DMI is 0400, so File.ReadAllText throws EACCES. That
            // must surface as "we do not know", never as a weaker guess.
            var fp = MachineFingerprints.LinuxHardware("/nonexistent/dmi/product_uuid");
            Assert.Equal(FingerprintQuality.Unavailable, fp.Quality);
            Assert.False(fp.IsNodeLockable);
        }

        // MARK: - /etc/machine-id is INSTALL-scoped, and must say so

        [Fact]
        public void MachineId_IsInstallScoped_NeverPromotedToHardware()
        {
            // The counter-intuitive one. /etc/machine-id is generated PER INSTALLATION, so it is
            // exactly the wrong identifier for "the same box across distros": dual-booting Ubuntu
            // and Arch yields TWO ids, burning two activations, and the family logic never fires.
            // It is usable as a trial anchor; it must never masquerade as hardware.
            var dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                var p = Path.Combine(dir, "machine-id");
                File.WriteAllText(p, "9f8c2b1e4d5a4c3b8e7f6a5d4c3b2a19\n");
                var fp = MachineFingerprints.LinuxInstallScoped(p);

                Assert.Equal(FingerprintQuality.InstallScoped, fp.Quality);
                Assert.NotEqual(FingerprintQuality.Hardware, fp.Quality);
                Assert.False(fp.IsNodeLockable, "an install-scoped id must never be node-lockable");
            }
            finally { Directory.Delete(dir, true); }
        }

        // MARK: - The bug itself

        [Fact]
        public void TheFingerprintIsNeverTheHostname()
        {
            // The regression guard. On this build host (macOS) there is no MachineGuid and no
            // /sys/class/dmi, so the OLD code returned hash(Environment.MachineName). If that ever
            // comes back, this fails.
            var fp = LicenseManager.GetMachineFingerprint();
            var hostnameHash = MachineFingerprints.Hash(Environment.MachineName);

            Assert.NotEqual(hostnameHash, fp.Value);
            if (fp.Quality == FingerprintQuality.Unavailable)
                Assert.Equal("", fp.Value);
        }

        [Fact]
        public void AnUnavailableFingerprintIsEmpty_NotAPlausibleHash()
        {
            // A caller must be able to tell "no fingerprint" at a glance. An Unavailable that still
            // carried 64 hex chars would be indistinguishable from a real one.
            Assert.Equal("", MachineFingerprint.Unavailable.Value);
            Assert.Equal(FingerprintQuality.Unavailable, MachineFingerprint.Unavailable.Quality);
        }

        // MARK: - The privileged helper (scripts/syxx-capture-hardware-id.sh)

        // <repo>/Windows/dotNET/Syxx.Tests/<file> — three levels below the repo root. The Windows
        // tree moved Windows/Syxx.Tests → Windows/dotNET/Syxx.Tests during the matrix cutover, and
        // the old dir/../.. resolved to <repo>/Windows, so every helper-script test failed.
        private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string f = "")
            => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, "..", "..", ".."));

        [Fact]
        public void TheHelperScriptAgreesWithTheKitHash()
        {
            // THE test for this feature. The helper hashes at install time (as root); the Kit hashes
            // a direct DMI read when Syxx happens to run as root. If those two disagree, the SAME
            // machine reports two different fingerprints depending on how it was read — silently
            // burning a second activation. The salt lives in two languages, so pin them together.
            var script = Path.Combine(RepoRoot(), "scripts", "syxx-capture-hardware-id.sh");
            Assert.True(File.Exists(script), $"helper script missing at {script}");

            var dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                const string raw = "4C4C4544-0037-5A10-8046-C4C04F4E4A32";
                var dmi = Path.Combine(dir, "product_uuid");
                var outFile = Path.Combine(dir, "hardware-id");
                File.WriteAllText(dmi, raw + "\n");

                var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh", $"\"{script}\" \"{outFile}\"")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                psi.Environment["SYXX_DMI_PATH"] = dmi;
                using var proc = System.Diagnostics.Process.Start(psi)!;
                proc.WaitForExit(30_000);

                Assert.True(File.Exists(outFile), "helper wrote no hardware-id file");
                var fromScript = File.ReadAllText(outFile).Trim();

                // 1. The two implementations agree.
                Assert.Equal(MachineFingerprints.Hash(raw), fromScript);

                // 2. And the Kit's reader accepts it as Hardware, giving the same value a root-run
                //    direct DMI read would produce.
                var viaHelper = MachineFingerprints.LinuxHelperCaptured(outFile);
                var viaDirect = MachineFingerprints.LinuxHardware(dmi);
                Assert.Equal(FingerprintQuality.Hardware, viaHelper.Quality);
                Assert.Equal(viaDirect.Value, viaHelper.Value);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void TheHelperNeverWritesTheRawDmiUuid()
        {
            // product_uuid is 0400 ON PURPOSE — it is a stable, cross-correlatable hardware id.
            // Copying it into a world-readable file would defeat the kernel's protection and hand
            // every process on the box an identifier it currently cannot get. That would be a real
            // privacy regression shipped in the name of licensing.
            var script = Path.Combine(RepoRoot(), "scripts", "syxx-capture-hardware-id.sh");
            var dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                const string raw = "4C4C4544-0037-5A10-8046-C4C04F4E4A32";
                var dmi = Path.Combine(dir, "product_uuid");
                var outFile = Path.Combine(dir, "hardware-id");
                File.WriteAllText(dmi, raw + "\n");

                var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh", $"\"{script}\" \"{outFile}\"")
                { RedirectStandardError = true, RedirectStandardOutput = true };
                psi.Environment["SYXX_DMI_PATH"] = dmi;
                using var proc = System.Diagnostics.Process.Start(psi)!;
                proc.WaitForExit(30_000);

                var contents = File.ReadAllText(outFile);
                Assert.DoesNotContain(raw, contents);
                Assert.DoesNotContain("4C4C4544", contents);
                Assert.DoesNotContain("-", contents.Trim());   // a UUID has hyphens; a hex digest does not
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void ACorruptOrHandEditedHelperFile_IsUnavailable_NotTrusted()
        {
            // The file is root-owned 0644, but a corrupt write or a hand-edit must not become a
            // machine identity we vouch for. Only a well-formed lowercase sha256 hex digest counts.
            var dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                var p = Path.Combine(dir, "hardware-id");
                foreach (var junk in new[] { "", "   ", "not-a-hash", "ZZZZ", new string('a', 63), new string('a', 65), "4C4C4544-0037-5A10-8046-C4C04F4E4A32" })
                {
                    File.WriteAllText(p, junk);
                    Assert.Equal(FingerprintQuality.Unavailable,
                                 MachineFingerprints.LinuxHelperCaptured(p).Quality);
                }
                // Uppercase is not what the helper writes -> treat as suspect, not "close enough".
                File.WriteAllText(p, MachineFingerprints.Hash("x").ToUpperInvariant());
                Assert.Equal(FingerprintQuality.Unavailable, MachineFingerprints.LinuxHelperCaptured(p).Quality);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void TheHelperSkipsPlaceholderUuids_RatherThanGivingEveryVmOneIdentity()
        {
            var script = Path.Combine(RepoRoot(), "scripts", "syxx-capture-hardware-id.sh");
            var dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                foreach (var placeholder in new[] { "00000000-0000-0000-0000-000000000000",
                                                    "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF" })
                {
                    var dmi = Path.Combine(dir, "product_uuid");
                    var outFile = Path.Combine(dir, "hardware-id-" + placeholder[..4]);
                    File.WriteAllText(dmi, placeholder + "\n");

                    var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh", $"\"{script}\" \"{outFile}\"")
                    { RedirectStandardError = true, RedirectStandardOutput = true };
                    psi.Environment["SYXX_DMI_PATH"] = dmi;
                    using var proc = System.Diagnostics.Process.Start(psi)!;
                    proc.WaitForExit(30_000);

                    Assert.Equal(0, proc.ExitCode);   // must never fail a package install
                    Assert.False(File.Exists(outFile),
                        $"a {placeholder[..4]} placeholder must produce NO file — every such box would share one identity");
                }
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void AnUnreadableDmi_ExitsZero_SoAPackageInstallNeverFails()
        {
            // Unprivileged (the 0400 case) or a container with no DMI. Licensing must not be able
            // to break `apt install`.
            var script = Path.Combine(RepoRoot(), "scripts", "syxx-capture-hardware-id.sh");
            var dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                var outFile = Path.Combine(dir, "hardware-id");
                var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh", $"\"{script}\" \"{outFile}\"")
                { RedirectStandardError = true, RedirectStandardOutput = true };
                psi.Environment["SYXX_DMI_PATH"] = "/nonexistent/dmi/product_uuid";
                using var proc = System.Diagnostics.Process.Start(psi)!;
                proc.WaitForExit(30_000);

                Assert.Equal(0, proc.ExitCode);
                Assert.False(File.Exists(outFile));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void TheChainPrefersHelperThenDirectThenInstallScoped()
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                string helper = Path.Combine(dir, "hardware-id");
                string dmi = Path.Combine(dir, "product_uuid");
                string mid = Path.Combine(dir, "machine-id");
                File.WriteAllText(mid, "9f8c2b1e4d5a4c3b8e7f6a5d4c3b2a19\n");

                // Only machine-id -> InstallScoped, and NOT node-lockable.
                var fp = MachineFingerprints.Linux(helper, dmi, mid);
                Assert.Equal(FingerprintQuality.InstallScoped, fp.Quality);
                Assert.False(fp.IsNodeLockable);

                // Add a readable DMI (the running-as-root case) -> Hardware.
                File.WriteAllText(dmi, "4C4C4544-0037-5A10-8046-C4C04F4E4A32\n");
                fp = MachineFingerprints.Linux(helper, dmi, mid);
                Assert.Equal(FingerprintQuality.Hardware, fp.Quality);
                Assert.Equal("dmi:product_uuid", fp.Source);

                // Add the helper file -> preferred, and the SAME value (no privilege needed).
                File.WriteAllText(helper, MachineFingerprints.Hash("4C4C4544-0037-5A10-8046-C4C04F4E4A32") + "\n");
                var viaHelper = MachineFingerprints.Linux(helper, dmi, mid);
                Assert.Equal("helper:hardware-id", viaHelper.Source);
                Assert.Equal(fp.Value, viaHelper.Value);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}

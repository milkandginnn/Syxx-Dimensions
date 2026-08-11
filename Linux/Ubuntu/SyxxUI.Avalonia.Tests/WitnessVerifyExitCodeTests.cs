using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    /// <summary>
    /// B253 — `syxx witness verify` must tell automation the truth through the EXIT STATUS, not
    /// only through the printed report.
    ///
    /// Measured on this host before the fix (2026-07-29), against real snapshots:
    ///
    ///     Windows  witness verify &lt;valid&gt;              -> exit 0
    ///     Windows  witness verify &lt;display-mismatch&gt;   -> exit 0   &lt;-- false clean
    ///     Windows  witness verify &lt;unreadable&gt;         -> exit 0   &lt;-- false clean
    ///
    /// A CI gate reading the exit code accepted forged evidence as verified. The printed text was
    /// already honest; only the machine-readable channel lied, which is the worse of the two.
    ///
    /// THESE TESTS SPAWN THE REAL `Syxx` CLI AND ASSERT ON THE PROCESS EXIT CODE. Deliberate: a
    /// unit test on `WitnessCommand.ForVerification` would not have caught this defect, because
    /// nothing ever assigned `Environment.ExitCode` on the verify path. The bug lived in the gap
    /// between the function and the process.
    ///
    /// Fixtures are built here with `WitnessSigner` from a synthetic `ComparisonResult`, so no
    /// network is needed. Tamper mutations are applied to the on-disk JSON, exactly as an attacker
    /// editing a shared snapshot would.
    ///
    /// Contract under test (macOS `SyxxCLI.swift:7`, mirrored by `WitnessCommand.ExitStatus`):
    /// 0 = clean, 1 = threat, 2 = inconclusive, 3 = error.
    /// </summary>
    public sealed class WitnessVerifyExitCodeTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _validPath;

        public WitnessVerifyExitCodeTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "b253-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);

            var (priv, _) = WitnessSigner.GenerateKeyPair();
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", HonestComparison(), priv);
            _validPath = Path.Combine(_dir, "valid.json");
            File.WriteAllText(_validPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        // ---- the result channel ----

        /// HAPPY PATH: a snapshot whose signature checks out exits 0, and still prints VALID.
        [Fact]
        public void ValidSnapshotExitsZero()
        {
            var run = RunSyxx("witness", "verify", _validPath);
            Assert.True(run.Code == 0, $"a verified snapshot must exit 0, got {run.Code}.\n{run.Output}");
            Assert.Contains("VALID", run.Output);
        }

        /// THE DEFECT ITSELF: this exited 0 before the fix. It must be non-zero AND distinct from
        /// the operational-error code, so a gate can tell forgery from a missing file.
        [Fact]
        public void TamperedDisplayFieldExitsTamperedNotZeroAndNotError()
        {
            string path = MutateJson("tampered-display.json", root =>
            {
                // Attacker swaps the answer the reader sees, leaving the signed evidence alone.
                root["comparison"]!["localOutcome"]!["records"]![0]!["rdata"] = "203.0.113.66";
            });

            var run = RunSyxx("witness", "verify", path);
            Assert.True(run.Code == 1,
                $"a tampered snapshot must exit 1 (threat), never 0. Got {run.Code}.\n{run.Output}");
            Assert.NotEqual(3, run.Code);
            Assert.Contains("DISPLAY MISMATCH", run.Output);
        }

        /// A forged Ed25519 signature is the other half of "tampered" and must share code 1.
        [Fact]
        public void ForgedSignatureExitsTampered()
        {
            string path = MutateJson("tampered-sig.json", root =>
            {
                byte[] sig = Convert.FromBase64String(root["signature"]!.GetValue<string>());
                sig[0] ^= 0xFF;
                root["signature"] = Convert.ToBase64String(sig);
            });

            var run = RunSyxx("witness", "verify", path);
            Assert.True(run.Code == 1, $"a forged signature must exit 1, got {run.Code}.\n{run.Output}");
            Assert.Contains("INVALID SIGNATURE", run.Output);
        }

        /// A snapshot that cannot be adjudicated is INCONCLUSIVE: not clean, and not an accusation
        /// of tampering.
        [Fact]
        public void StaleSnapshotExitsInconclusive()
        {
            var run = RunSyxx("witness", "verify", _validPath, "--max-age", "0");
            Assert.True(run.Code == 2,
                $"a snapshot outside the freshness window is inconclusive, got {run.Code}.\n{run.Output}");
            Assert.Contains("STALE", run.Output);
        }

        /// A gate must not be able to change the machine-readable answer by changing verbosity.
        [Fact]
        public void DetailedFlagDoesNotChangeTheExitCode()
        {
            string path = MutateJson("tampered-sig-detailed.json", root =>
            {
                byte[] sig = Convert.FromBase64String(root["signature"]!.GetValue<string>());
                sig[0] ^= 0xFF;
                root["signature"] = Convert.ToBase64String(sig);
            });

            Assert.Equal(0, RunSyxx("witness", "verify", _validPath, "--detailed").Code);
            Assert.Equal(1, RunSyxx("witness", "verify", path, "--detailed").Code);
        }

        // ---- the operational channel ----

        /// Unreadable input exited 0 before the fix, i.e. "I could not read this" was reported to
        /// automation as "this evidence checked out".
        [Fact]
        public void UnreadableAndMalformedInputExitError()
        {
            var missing = RunSyxx("witness", "verify", Path.Combine(_dir, "does-not-exist.json"));
            Assert.True(missing.Code == 3, $"a missing file is an error, got {missing.Code}.\n{missing.Output}");

            string malformedPath = Path.Combine(_dir, "malformed.json");
            File.WriteAllText(malformedPath, "{ \"formatVersion\": 2, \"truncated");
            var malformed = RunSyxx("witness", "verify", malformedPath);
            Assert.True(malformed.Code == 3,
                $"unparseable input is an error, got {malformed.Code}.\n{malformed.Output}");

            Assert.NotEqual(1, missing.Code);
            Assert.NotEqual(1, malformed.Code);
        }

        /// Asking for help is not an inconclusive network measurement.
        [Fact]
        public void HelpExitsZero()
        {
            Assert.Equal(0, RunSyxx("witness", "--help").Code);
            Assert.Equal(0, RunSyxx("--help").Code);
            Assert.Equal(0, RunSyxx("--version").Code);
        }

        /// Command misuse is an operational error (3), not a network finding. It must NOT be 1,
        /// which the contract reserves for THREAT DETECTED — a typo must never look like tampering.
        [Fact]
        public void MisuseExitsErrorNotThreat()
        {
            Assert.Equal(3, RunSyxx("witness").Code);
            Assert.Equal(3, RunSyxx("witness", "bogus", _validPath).Code);
            Assert.Equal(3, RunSyxx("definitely-not-a-subcommand").Code);
        }

        // ---- fixtures ----

        /// A comparison in which the local resolver and both public vantage points agree. Synthetic
        /// on purpose: these tests assert exit codes, not this host's DNS reachability.
        private static ComparisonResult HonestComparison()
        {
            var answer = new List<DNSRecord>
            {
                new DNSRecord { Name = "example.com", Type = 1, Ttl = 300, Rdata = "93.184.216.34" }
            };
            return new ComparisonResult
            {
                LocalServer = "192.0.2.53",
                LocalRecords = answer,
                PublicResolverResults = new Dictionary<string, List<DNSRecord>>
                {
                    ["Cloudflare"] = answer,
                    ["Google"] = answer
                },
                Result = VantageComparisonResult.Consistent,
                LocalOutcome = new ResolverQueryOutcome { Records = answer, Status = DNSResponseStatus.Success },
                PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome>
                {
                    ["Cloudflare"] = new ResolverQueryOutcome { Records = answer, Status = DNSResponseStatus.Success },
                    ["Google"] = new ResolverQueryOutcome { Records = answer, Status = DNSResponseStatus.Success }
                }
            };
        }

        /// Copy the valid snapshot, apply an attacker-style edit to the raw JSON, return the path.
        private string MutateJson(string name, Action<JsonNode> mutate)
        {
            var root = JsonNode.Parse(File.ReadAllText(_validPath))!;
            mutate(root);
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, root.ToJsonString());
            return path;
        }

        // ---- process harness ----

        private readonly record struct Run(int Code, string Output);

        /// Run the real CLI and return its PROCESS exit code.
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

        /// Locate the CLI built alongside this test assembly. `Syxx.Tests.csproj` carries a
        /// ProjectReference to `Syxx.csproj` (ReferenceOutputAssembly=false) purely so this exists
        /// by the time the tests run.
        ///
        /// Throws rather than skipping when it is missing: a silently-skipped exit-code test is
        /// indistinguishable from a passing one, which is how B253 survived.
        private static string CliAssemblyPath()
        {
            // .../Syxx.Tests/bin/<Config>/<Tfm>/  ->  .../Syxx/bin/<Config>/<Tfm>/Syxx.dll
            var tfmDir = new DirectoryInfo(AppContext.BaseDirectory);
            string tfm = tfmDir.Name;
            string config = tfmDir.Parent!.Name;
            var solutionDir = tfmDir.Parent!.Parent!.Parent!.Parent!;   // bin -> Syxx.Tests -> dotNET
            string candidate = Path.Combine(solutionDir.FullName, "Syxx", "bin", config, tfm, "Syxx.dll");
            if (File.Exists(candidate)) return candidate;

            throw new FileNotFoundException(
                $"the Syxx CLI was not found at {candidate}. Build it with " +
                "`dotnet build Syxx/Syxx.csproj` first. Failing rather than skipping on purpose.",
                candidate);
        }
    }
}

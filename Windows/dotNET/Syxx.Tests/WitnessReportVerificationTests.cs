using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    /// <summary>
    /// B254 — `syxx witness report` must VERIFY a snapshot before it renders one.
    ///
    /// Measured on this host BEFORE the fix (2026-07-29), using the B253 display-mismatch fixture
    /// (a snapshot whose displayed answer was edited after signing):
    ///
    ///     syxx witness verify tampered-display.json  ->  "❌ DISPLAY MISMATCH"          exit 1
    ///     syxx witness report tampered-display.json  ->  "SYXX SIGNED INCIDENT REPORT"
    ///                                                    "Local Answer(s): 203.0.113.66"
    ///                                                    "⚠ Tamper-evident: altering any data
    ///                                                     above will break the Ed25519 signature"
    ///                                                                                  exit 0
    ///
    /// `verify` did the right thing; `report` simply never called it. The command printed the
    /// ATTACKER's answers under a header that asserts integrity, told the reader the content was
    /// tamper-evident, and exited 0. That is not a missing feature, it is an affirmative false
    /// claim, and the artifact is meant to be handed to an ISP, a regulator, or a court.
    ///
    /// THESE TESTS SPAWN THE REAL `Syxx` CLI AND ASSERT ON THE PROCESS EXIT CODE, for the same
    /// reason B253's do: an in-process unit test on a helper's return value would not have caught
    /// this, because the defect was the ABSENCE of a call, in the gap between the function and the
    /// process.
    ///
    /// Exit-code contract, shared with `verify` (`WitnessCommand.ExitStatus`):
    /// VALID 0 | DISPLAY MISMATCH 1 | INVALID SIGNATURE 1 | STALE 2 | EXPIRED 2 | malformed 3 |
    /// missing 3. 1 means THREAT DETECTED, so a tampered snapshot exiting 1 is correct and
    /// deliberate. Twin of macOS `WitnessReportVerificationTests.swift`.
    /// </summary>
    public sealed class WitnessReportVerificationTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _validPath;
        private readonly string _privateKey;

        /// The answer an attacker substitutes for the signed one. Asserted ABSENT from every refusal.
        private const string AttackerAnswer = "203.0.113.66";

        public WitnessReportVerificationTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "b254-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);

            var (priv, _) = WitnessSigner.GenerateKeyPair();
            _privateKey = priv;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", HonestComparison(), priv);
            _validPath = Write(snapshot, "valid.json");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        // ---- the defect ----

        /// THE DEFECT ITSELF. A snapshot whose displayed answer was edited after signing must not
        /// be rendered as a report at all, and must exit 1 (THREAT DETECTED).
        ///
        /// FAILURE CASE / MUTATION: delete the `VerifySnapshotDetailed` guard in the `report` branch
        /// of WitnessCommand.cs and this goes RED on every assertion — the header comes back, the
        /// attacker's answer comes back, and the code drops to 0.
        [Fact]
        public void TamperedDisplayIsRefusedNotRendered()
        {
            string path = TamperedDisplayFixture("tampered-display.json");
            var run = RunSyxx("witness", "report", path);

            Assert.True(run.Code == 1,
                $"a display-tampered snapshot must exit 1 (threat), never 0. Got {run.Code}.\n{run.Output}");
            Assert.DoesNotContain("SIGNED INCIDENT REPORT", run.Output);
            Assert.DoesNotContain(AttackerAnswer, run.Output);
            Assert.Contains("DISPLAY MISMATCH", run.Output);
        }

        /// A forged Ed25519 signature is the other half of "tampered" and shares code 1.
        [Fact]
        public void ForgedSignatureIsRefusedNotRendered()
        {
            string path = MutateJson("tampered-sig.json", root =>
            {
                byte[] sig = Convert.FromBase64String(root["signature"]!.GetValue<string>());
                sig[0] ^= 0xFF;
                root["signature"] = Convert.ToBase64String(sig);
            });

            var run = RunSyxx("witness", "report", path);
            Assert.True(run.Code == 1, $"a forged signature must exit 1, got {run.Code}.\n{run.Output}");
            Assert.DoesNotContain("SIGNED INCIDENT REPORT", run.Output);
            Assert.Contains("INVALID SIGNATURE", run.Output);
        }

        /// The refusal must leak NOTHING that came out of the snapshot. Once verification has
        /// failed, every field in the file is attacker-controlled, and this output is a shareable
        /// artifact that gets screenshotted and forwarded stripped of its banner.
        ///
        /// FAILURE CASE: reinstate any snapshot-derived line (signature, key ID, public key, domain,
        /// conclusion, answers) in `PrintReportRefusal` and this goes RED.
        [Fact]
        public void RefusalLeaksNoSnapshotContent()
        {
            string path = TamperedDisplayFixture("leak-check.json");
            var snapshot = JsonSerializer.Deserialize<WitnessSnapshot>(
                File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            var run = RunSyxx("witness", "report", path);

            foreach (string forbidden in new[]
            {
                snapshot.Signature, snapshot.KeyId, snapshot.PublicKey, snapshot.TargetDomain,
                AttackerAnswer, "93.184.216.34", "DIVERGES", "Tamper-evident"
            })
            {
                Assert.False(run.Output.Contains(forbidden),
                    $"refusal output must not echo attacker-controlled snapshot content: {forbidden}\n{run.Output}");
            }
            // What it SHOULD contain: only the path the user typed, and Syxx's own finding.
            Assert.Contains(path, run.Output);
            Assert.Contains("REFUSED", run.Output);
        }

        // ---- the inconclusive band ----

        /// A snapshot outside the caller's freshness window cannot be adjudicated. Not clean, and
        /// not an accusation of tampering either: code 2, and still no report.
        [Fact]
        public void StaleSnapshotIsRefusedInconclusive()
        {
            var run = RunSyxx("witness", "report", _validPath, "--max-age", "0");
            Assert.True(run.Code == 2,
                $"a stale snapshot is inconclusive, not clean and not forged. Got {run.Code}.\n{run.Output}");
            Assert.DoesNotContain("SIGNED INCIDENT REPORT", run.Output);
            Assert.Contains("STALE", run.Output);
        }

        /// An honestly-signed snapshot whose own expiry has passed. Reachable with no flags at all,
        /// so this covers the inconclusive band even for a caller that passes nothing.
        [Fact]
        public void ExpiredSnapshotIsRefusedInconclusive()
        {
            var expired = WitnessSigner.CreateSignedSnapshot(
                "example.com", HonestComparison(), _privateKey, DateTime.UtcNow.AddHours(-1));
            string path = Write(expired, "expired.json");

            var run = RunSyxx("witness", "report", path);
            Assert.True(run.Code == 2, $"an expired snapshot must not exit 0, got {run.Code}.\n{run.Output}");
            Assert.DoesNotContain("SIGNED INCIDENT REPORT", run.Output);
            Assert.Contains("EXPIRED", run.Output);
        }

        // ---- ordering ----

        /// Verification must run BEFORE the `--conclusion` file is read. Otherwise a tampered
        /// snapshot paired with an unreadable conclusion reports 3 ("could not open a file") instead
        /// of 1 ("this evidence is forged"), and the more serious finding is masked by the lesser.
        ///
        /// FAILURE CASE / MUTATION: move the verification guard below the `--conclusion` load and
        /// this reads 3.
        [Fact]
        public void TamperingIsReportedEvenWhenTheConclusionFileIsUnreadable()
        {
            string path = TamperedDisplayFixture("tampered-with-bad-conclusion.json");
            var run = RunSyxx("witness", "report", path, "--conclusion", Path.Combine(_dir, "nope.json"));

            Assert.True(run.Code == 1,
                $"forgery must not be downgraded to an operational error by a second failure. Got {run.Code}.\n{run.Output}");
            Assert.Contains("DISPLAY MISMATCH", run.Output);
        }

        // ---- the happy path must survive ----

        /// A snapshot that verifies still renders exactly as before, and still exits 0. The fix must
        /// not have turned the command into one that refuses everything.
        [Fact]
        public void ValidSnapshotStillRendersAndExitsZero()
        {
            var run = RunSyxx("witness", "report", _validPath);
            Assert.True(run.Code == 0, $"a verified snapshot must still render, got {run.Code}.\n{run.Output}");
            Assert.Contains("SIGNED INCIDENT REPORT", run.Output);
            Assert.Contains("93.184.216.34", run.Output);
            Assert.DoesNotContain("REFUSED", run.Output);
        }

        /// Unreadable input is an operational error (3), distinct from forgery (1), on this path too.
        [Fact]
        public void UnreadableAndMalformedInputExitError()
        {
            var missing = RunSyxx("witness", "report", Path.Combine(_dir, "does-not-exist.json"));
            Assert.True(missing.Code == 3, $"a missing file is an error, got {missing.Code}.\n{missing.Output}");
            Assert.DoesNotContain("SIGNED INCIDENT REPORT", missing.Output);

            string malformedPath = Path.Combine(_dir, "malformed.json");
            File.WriteAllText(malformedPath, "{ \"formatVersion\": 2, \"truncated");
            var malformed = RunSyxx("witness", "report", malformedPath);
            Assert.True(malformed.Code == 3,
                $"unparseable input is an error, got {malformed.Code}.\n{malformed.Output}");
            Assert.DoesNotContain("SIGNED INCIDENT REPORT", malformed.Output);
        }

        // ---- fixtures ----

        /// A comparison in which the local resolver and both public vantage points agree. Synthetic
        /// on purpose: these tests assert exit codes and rendering, not this host's DNS reachability.
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

        /// The B253 display-mismatch mutation: the attacker swaps the answer the READER sees while
        /// leaving the signed evidence block alone, and flips the finding to match.
        private string TamperedDisplayFixture(string name) => MutateJson(name, root =>
        {
            root["comparison"]!["localOutcome"]!["records"]![0]!["rdata"] = AttackerAnswer;
            root["comparison"]!["localRecords"]![0]!["rdata"] = AttackerAnswer;
            root["comparison"]!["isRedirectionDetected"] = true;
        });

        private string Write(WitnessSnapshot snapshot, string name)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
            return path;
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

        /// Locate the CLI built alongside this test assembly. Throws rather than skipping when it is
        /// missing: a silently-skipped exit-code test is indistinguishable from a passing one, which
        /// is how this defect class survives.
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

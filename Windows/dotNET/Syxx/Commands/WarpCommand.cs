using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Syxx.Kit;

namespace Syxx.Commands
{
    /// <summary>`syxx warp` — WARP_MANIFEST_V1 signed-manifest content-integrity check. Twin of macOS
    /// SyxxWarpCLI; args[0] is the first real argument (the subcommand token was stripped by the
    /// dispatcher).</summary>
    internal static class WarpCommand
    {
        public static async Task RunAsync(string[] args)
        {
            // B253: parity with macOS SyxxWarpCLI — 0=match, 1=tampering, 2=inconclusive, 3=error.
            if (SyxxConfig.HandleGlobalFlags(args, "syxx warp",
                "Usage: syxx warp <url> [--diff]   (WARP_MANIFEST_V1 signed-manifest content-integrity check; --diff shows the local-vs-server-clean HTML diff)"))
            {
                Environment.ExitCode = 0;
                return;
            }
            bool showDiff = args.Contains("--diff");
            string? targetURL = args.FirstOrDefault(a => !a.StartsWith("--"));
            if (string.IsNullOrEmpty(targetURL))
            {
                Console.WriteLine("Usage: syxx warp <url> [--diff]");
                Console.WriteLine("Example: syxx warp https://example.com --diff");
                Environment.ExitCode = 3;
                return;
            }

            var check = await LicenseManager.IsFeatureUnlockedAsync();
            if (!check.Unlocked)
            {
                Console.WriteLine($"\n🚨 ERROR: {check.Message}");
                Console.WriteLine("💡 This is a server-anchored feature. To unlock, register a license key in the main launcher (Option L).");
                Console.WriteLine("💡 Subscriptions are $3.95/month to cover hosting costs.\n");
                Environment.ExitCode = 3;
                return;
            }

            string origin = targetURL.Contains("://") ? targetURL : $"https://{targetURL}";
            Console.WriteLine($"Starting Syxx Warp (signed-manifest) check for: {origin}");
            Console.WriteLine("Fetching the signed manifest and verifying content bytes from your vantage...");

            string? licenseKey = LicenseManager.GetStoredLicenseKey();
            var result = await WarpEngine.RunVantageCheckAsync(origin, SyxxConfig.DimensionBaseURL, licenseKey);

            Console.WriteLine("\n----------------------------------------------------------------------------------");
            Console.WriteLine("🔍 WARP CONTENT DIAGNOSTICS REPORT (WARP_MANIFEST_V1):");
            Console.WriteLine("----------------------------------------------------------------------------------");
            Console.WriteLine($"  • Signature:  {result.SignatureState} (key_id: {result.KeyId ?? "—"})");
            Console.WriteLine($"  • Manifest:   {result.ManifestState}");
            Console.WriteLine($"  • Freshness:  {result.FreshnessState}");
            Console.WriteLine($"  • HTML bytes: {StateLabel(result.HtmlState)}");
            if (result.Assets.Count == 0)
            {
                Console.WriteLine("  • Assets:     (none evaluated)");
            }
            else
            {
                Console.WriteLine("  • Assets:");
                foreach (var a in result.Assets)
                    Console.WriteLine($"      {StateLabel(a.State)}  [{a.AssetClass}] {Shorten(a.Url)}");
            }

            Console.WriteLine("\n🔮 CONTENT INTEGRITY CONCLUSION:");
            switch (result.Conclusion)
            {
                case WarpConclusion.VERIFIED_MATCH:
                    Console.WriteLine("✅ VERIFIED_MATCH — a pinned, fresh signed manifest matches the bytes your vantage fetched (HTML + every declared asset).");
                    Environment.ExitCode = 0;
                    break;
                case WarpConclusion.TAMPERING_DETECTED:
                    Console.WriteLine("🚨 TAMPERING_DETECTED — a verified, fresh manifest disagrees byte-for-byte with what your vantage fetched (or the signature is invalid).");
                    Console.WriteLine("   💡 This is positive interference evidence. Benign per-request/dynamic content is possible; see --diff before concluding.");
                    Environment.ExitCode = 1;
                    break;
                default:
                    Console.WriteLine("⚠️  INCONCLUSIVE — Syxx could not conclusively verify content integrity.");
                    Console.WriteLine($"   💡 Details: {result.InconclusiveReason ?? "untrusted/unpinned signer, stale/forged freshness, or unfetchable content."}");
                    Console.WriteLine("   💡 Conclusion: This is not evidence that the page is secure or tampered.");
                    Environment.ExitCode = 2;
                    break;
            }

            if (showDiff)
            {
                Console.WriteLine("\n🧬 RAW HTML DIFF (your vantage ⟵ vs ⟶ the server's clean copy; inline JavaScript is included here):");
                (byte[] Bytes, string? Sha256)? clean = result.HtmlState == WarpContentState.Mismatch && result.LocalHtmlBytes != null
                    ? await WarpEngine.FetchServerCleanAssetAsync(SyxxConfig.DimensionBaseURL, origin, licenseKey)
                    : null;
                if (clean.HasValue && result.LocalHtmlBytes != null)
                {
                    string local = Encoding.UTF8.GetString(result.LocalHtmlBytes);
                    string remote = Encoding.UTF8.GetString(clean.Value.Bytes);
                    var diff = TextDiff.Unified(local, remote);
                    if (diff.Count == 0) Console.WriteLine("   (no line-level differences to show)");
                    else foreach (var line in diff) Console.WriteLine($"   {line}");
                }
                else
                {
                    Console.WriteLine("   (a diff is shown only for a fetched HTML byte-mismatch against a retrievable server clean copy)");
                }
                var mismatched = result.Assets.Where(a => a.State == WarpContentState.Mismatch).ToList();
                if (mismatched.Count > 0)
                {
                    Console.WriteLine("\n   Assets that differ by hash (fetch the server clean copy via /api/warp/raw for a full body diff):");
                    foreach (var a in mismatched) Console.WriteLine($"     • {a.Url}");
                }
            }
            Console.WriteLine("----------------------------------------------------------------------------------\n");
        }

        private static string StateLabel(WarpContentState s) => s switch
        {
            WarpContentState.Match => "✅ match",
            WarpContentState.Mismatch => "🚨 mismatch",
            WarpContentState.FetchError => "⚠️ fetch-error",
            WarpContentState.FetchFailedDeclared => "⚠️ server-fetch-failed",
            _ => "— not-evaluated"
        };

        private static string Shorten(string url)
            => url.Length > 60 ? url.Substring(0, 30) + "..." + url.Substring(url.Length - 27) : url;
    }
}

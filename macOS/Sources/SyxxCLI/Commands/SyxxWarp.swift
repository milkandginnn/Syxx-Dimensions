import Foundation
import SyxxKit

struct SyxxWarpCLI {
    /// B253: 0=match, 1=content difference (threat signal), 2=inconclusive, 3=error/usage/locked.
    @discardableResult
    static func run(_ argv: [String]) async -> Int32 {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx warp",
            usage: "Usage: syxx warp <url> [--diff]   (WARP_MANIFEST_V1 signed-manifest content-integrity check; --diff shows the local-vs-server-clean HTML diff)") {
            return CLIExitStatus.completed.rawValue
        }
        let args = argv
        let showDiff = args.contains("--diff")
        let positional = args.dropFirst().filter { !$0.hasPrefix("--") }
        guard let targetURL = positional.first, !targetURL.isEmpty else {
            print("Usage: syxx warp <url> [--diff]")
            print("Example: syxx warp https://example.com --diff")
            return CLIExitStatus.failure.rawValue
        }

        let check = await LicenseManager.isFeatureUnlocked()
        if !check.unlocked {
            print("\n🚨 ERROR: \(check.message)")
            print("💡 This is a server-anchored feature. To unlock, register a license key in the main launcher (Option L).")
            print("💡 Subscriptions are $3.95/month to cover hosting costs.\n")
            return CLIExitStatus.failure.rawValue
        }

        let origin = targetURL.contains("://") ? targetURL : "https://\(targetURL)"
        print("Starting Syxx Warp (signed-manifest) check for: \(origin)")
        print("Fetching the signed manifest and verifying content bytes from your vantage...")

        let client = DimensionClient(baseURL: SyxxConfig.dimensionBaseURL, licenseKey: LicenseManager.getStoredLicenseKey())
        // Fresh per-run replay nonce (§4.5): the server must echo it inside the signed manifest,
        // so a captured manifest cannot be replayed into a later run.
        let result = await WarpEngine.runVantageCheck(origin: origin, client: client,
                                                      nonce: WarpEngine.makeNonce())

        print("\n----------------------------------------------------------------------------------")
        print("🔍 WARP CONTENT DIAGNOSTICS REPORT (WARP_MANIFEST_V1):")
        print("----------------------------------------------------------------------------------")
        print("  • Signature:  \(result.signatureState) (key_id: \(result.keyId ?? "—"))")
        print("  • Manifest:   \(result.manifestState)")
        print("  • Freshness:  \(result.freshnessState)")
        print("  • HTML bytes: \(stateLabel(result.htmlState))")
        if result.assets.isEmpty {
            print("  • Assets:     (none evaluated)")
        } else {
            print("  • Assets:")
            for a in result.assets {
                print("      \(stateLabel(a.state))  [\(a.assetClass)] \(shorten(a.url))")
            }
        }

        print("\n🔮 CONTENT INTEGRITY CONCLUSION:")
        let exitCode: Int32
        switch result.conclusion {
        case .verifiedMatch:
            print("✅ VERIFIED_MATCH — a pinned, fresh signed manifest matches the bytes your vantage fetched (HTML + every declared asset).")
            exitCode = CLIExitStatus.completed.rawValue
        case .tamperingDetected:
            print("🚨 TAMPERING_DETECTED — a verified, fresh manifest disagrees byte-for-byte with what your vantage fetched (or the signature is invalid).")
            print("   💡 This is positive interference evidence. Benign per-request/dynamic content is possible; see --diff before concluding.")
            exitCode = CLIExitStatus.threat.rawValue
        case .inconclusive:
            print("⚠️  INCONCLUSIVE — Syxx could not conclusively verify content integrity.")
            print("   💡 Details: \(result.inconclusiveReason ?? "untrusted/unpinned signer, stale/forged freshness, or unfetchable content.")")
            print("   💡 Conclusion: This is not evidence that the page is secure or tampered.")
            exitCode = CLIExitStatus.inconclusive.rawValue
        }

        if showDiff {
            print("\n🧬 RAW HTML DIFF (your vantage ⟵ vs ⟶ the server's clean copy; inline JavaScript is included here):")
            if result.htmlState == .mismatch, let localBytes = result.localHtmlBytes,
               let clean = await WarpEngine.fetchServerCleanAsset(url: origin, client: client) {
                let local = String(decoding: localBytes, as: UTF8.self)
                let remote = String(decoding: clean.bytes, as: UTF8.self)
                let diff = TextDiff.unified(local, remote)
                if diff.isEmpty { print("   (no line-level differences to show)") }
                else { for line in diff { print("   \(line)") } }
            } else {
                print("   (a diff is shown only for a fetched HTML byte-mismatch against a retrievable server clean copy)")
            }
            let mismatchedAssets = result.assets.filter { $0.state == .mismatch }
            if !mismatchedAssets.isEmpty {
                print("\n   Assets that differ by hash (fetch the server clean copy via /api/warp/raw for a full body diff):")
                for a in mismatchedAssets { print("     • \(a.url)") }
            }
        }
        print("----------------------------------------------------------------------------------\n")
        return exitCode
    }

    private static func stateLabel(_ s: WarpContentState) -> String {
        switch s {
        case .match: return "✅ match"
        case .mismatch: return "🚨 mismatch"
        case .fetchError: return "⚠️ fetch-error"
        case .fetchFailedDeclared: return "⚠️ server-fetch-failed"
        case .notEvaluated: return "— not-evaluated"
        }
    }

    private static func shorten(_ url: String) -> String {
        url.count > 60 ? String(url.prefix(30)) + "..." + String(url.suffix(27)) : url
    }
}

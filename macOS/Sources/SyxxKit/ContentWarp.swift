import Foundation

// =====================================================================================
// WARP — RETIRED CLIENT ENGINE (docs/warp-manifest-v1-spec.md §11).
//
// The client raw-concatenation assembler/scanner, `HTMLAssetParser`, `decodeServerResponse`, and the
// legacy `comparePageContent` are RETIRED. A 5-agent parity+security audit proved three hand-written
// HTML parsers / URL resolvers cannot hold byte-parity (even C# vs Swift disagreed) and that the
// prior proxy response was unsigned (a MITM could forge the declared hash into a false match). The
// signed-manifest protocol (WARP_MANIFEST_V1) moves ALL parsing + URL resolution to the trusted
// server and authenticates the reference with Ed25519. The CLIENT engine now lives in `WarpEngine`
// (+ `WarpManifestPinning`); the diff overlay reuses `TextDiff`.
//
// This file retains ONLY the legacy `WarpResult` / `ScriptHashCheck` display DTOs so the Mirror View
// surface (owned by the UI stream) keeps compiling while it migrates to `WarpManifestResult`. Nothing
// here performs any comparison, parsing, or fetching. It is inert data.
// =====================================================================================

/// LEGACY display DTO — retained as a compile shim for the Mirror View surface only. Populated by
/// nothing; the live result is now `WarpManifestResult`. The UI stream migrates the Mirror View to
/// read `SectionInputs.warpManifest` and this type is then deleted.
public struct WarpResult: Sendable, Codable {
    public let targetURL: String

    /// Raw HTML as fetched locally and via the remote proxy — retained so callers can diff the
    /// actual content (markup + inline JavaScript), not just compare hashes.
    public let localHTML: String
    public let remoteHTML: String

    // Structural checks
    public let localScriptURLs: [String]
    public let remoteScriptURLs: [String]
    public let isScriptStructureIdentical: Bool

    public let localStylesheetURLs: [String]
    public let remoteStylesheetURLs: [String]
    public let isStylesheetStructureIdentical: Bool

    public let localIframeURLs: [String]
    public let remoteIframeURLs: [String]
    public let isIframeStructureIdentical: Bool

    // External-script hash checks
    public let scriptHashes: [String: ScriptHashCheck]
    public let mismatchedScriptHashes: [String]

    // Inline-script (<script> without src) hash checks — ordered SHA-256 of each inline body.
    public let localInlineScriptHashes: [String]
    public let remoteInlineScriptHashes: [String]
    public let isInlineScriptIdentical: Bool

    public let isSecure: Bool
    public let isInconclusive: Bool
    public let inconclusiveReason: String?

    public init(targetURL: String, localScriptURLs: [String], remoteScriptURLs: [String], isScriptStructureIdentical: Bool, localStylesheetURLs: [String], remoteStylesheetURLs: [String], isStylesheetStructureIdentical: Bool, localIframeURLs: [String], remoteIframeURLs: [String], isIframeStructureIdentical: Bool, scriptHashes: [String: ScriptHashCheck], mismatchedScriptHashes: [String], isSecure: Bool, isInconclusive: Bool = false, inconclusiveReason: String? = nil,                 localInlineScriptHashes: [String] = [], remoteInlineScriptHashes: [String] = [], isInlineScriptIdentical: Bool = false, localHTML: String = "", remoteHTML: String = "") {
        self.targetURL = targetURL
        self.localHTML = localHTML
        self.remoteHTML = remoteHTML
        self.localScriptURLs = localScriptURLs
        self.remoteScriptURLs = remoteScriptURLs
        self.isScriptStructureIdentical = isScriptStructureIdentical
        self.localStylesheetURLs = localStylesheetURLs
        self.remoteStylesheetURLs = remoteStylesheetURLs
        self.isStylesheetStructureIdentical = isStylesheetStructureIdentical
        self.localIframeURLs = localIframeURLs
        self.remoteIframeURLs = remoteIframeURLs
        self.isIframeStructureIdentical = isIframeStructureIdentical
        self.scriptHashes = scriptHashes
        self.mismatchedScriptHashes = mismatchedScriptHashes
        self.isSecure = isSecure
        self.isInconclusive = isInconclusive
        self.inconclusiveReason = inconclusiveReason
        self.localInlineScriptHashes = localInlineScriptHashes
        self.remoteInlineScriptHashes = remoteInlineScriptHashes
        self.isInlineScriptIdentical = isInlineScriptIdentical
    }
}

/// LEGACY display DTO — see `WarpResult`.
public struct ScriptHashCheck: Sendable, Codable {
    public let localHash: String
    public let remoteHash: String
    public let isMatch: Bool

    public init(localHash: String, remoteHash: String, isMatch: Bool) {
        self.localHash = localHash
        self.remoteHash = remoteHash
        self.isMatch = isMatch
    }
}

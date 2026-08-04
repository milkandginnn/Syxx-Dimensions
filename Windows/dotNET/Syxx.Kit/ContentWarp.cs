using System.Collections.Generic;

namespace Syxx.Kit
{
    // =====================================================================================
    // WARP — RETIRED CLIENT ENGINE (docs/warp-manifest-v1-spec.md §11).
    //
    // The client raw-concatenation assembler/scanner, HtmlAssetParser, DecodeServerResponse, and the
    // legacy ComparePageContentAsync are RETIRED. A 5-agent parity+security audit proved three
    // hand-written HTML parsers / URL resolvers cannot hold byte-parity (even C# vs Swift disagreed)
    // and that the prior proxy response was unsigned (a MITM could forge the declared hash into a
    // false match). The signed-manifest protocol (WARP_MANIFEST_V1) moves ALL parsing + URL
    // resolution to the trusted server and authenticates the reference with Ed25519. The CLIENT engine
    // now lives in WarpEngine (+ WarpManifestPinning); the diff overlay reuses TextDiff.
    //
    // This file retains ONLY the legacy WarpResult / ScriptHashCheck display DTOs so the Mirror View
    // surface (owned by the UI stream) keeps compiling while it migrates to WarpManifestResult.
    // Nothing here performs any comparison, parsing, or fetching. It is inert data.
    // =====================================================================================

    /// <summary>LEGACY display DTO — retained as a compile shim for the Mirror View surface only.
    /// Populated by nothing; the live result is now <see cref="WarpManifestResult"/>. The UI stream
    /// migrates the Mirror View to read <c>SectionInputs.WarpManifest</c> and this type is then
    /// deleted.</summary>
    public class WarpResult
    {
        public string TargetURL { get; set; } = "";

        /// Raw HTML as fetched locally and via the remote proxy — retained so callers can diff the
        /// actual content (markup + inline JavaScript), not just compare hashes.
        public string LocalHtml { get; set; } = "";
        public string RemoteHtml { get; set; } = "";

        // Structural checks
        public List<string> LocalScriptURLs { get; set; } = new List<string>();
        public List<string> RemoteScriptURLs { get; set; } = new List<string>();
        public bool IsScriptStructureIdentical { get; set; }

        public List<string> LocalStylesheetURLs { get; set; } = new List<string>();
        public List<string> RemoteStylesheetURLs { get; set; } = new List<string>();
        public bool IsStylesheetStructureIdentical { get; set; }

        public List<string> LocalIframeURLs { get; set; } = new List<string>();
        public List<string> RemoteIframeURLs { get; set; } = new List<string>();
        public bool IsIframeStructureIdentical { get; set; }

        // External-script hash checks
        public Dictionary<string, ScriptHashCheck> ScriptHashes { get; set; } = new Dictionary<string, ScriptHashCheck>();
        public List<string> MismatchedScriptHashes { get; set; } = new List<string>();

        // Inline-script (<script> without src) hash checks — ordered SHA-256 of each inline body.
        public List<string> LocalInlineScriptHashes { get; set; } = new List<string>();
        public List<string> RemoteInlineScriptHashes { get; set; } = new List<string>();
        public bool IsInlineScriptIdentical { get; set; } = false;

        public bool IsSecure { get; set; }
        public bool IsInconclusive { get; set; }
        public string? InconclusiveReason { get; set; }
    }

    /// <summary>LEGACY display DTO — see <see cref="WarpResult"/>.</summary>
    public class ScriptHashCheck
    {
        public string LocalHash { get; set; } = "";
        public string RemoteHash { get; set; } = "";
        public bool IsMatch { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NSec.Cryptography;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Windows half of the WARP_MANIFEST_V1 client oracle (docs/warp-manifest-v1-spec.md §§4–9). Loads the
/// shared <c>warp-manifest-vectors/</c> corpus — the SAME directory the Swift half
/// (macOS/Tests/SyxxKitTests/WarpEngineTests.swift) and the Python server load — and asserts this
/// platform's <see cref="WarpEngine"/> reaches the EXACT expected conclusion + per-asset states +
/// signature/manifest/freshness/HTML states. Fetch, clock, and pinned keys are injected so the suite
/// is fully offline. This .NET half exercises BOTH gzip and brotli transport identity (013/014); the
/// redirect fail-closed paths (corpus uses redirect_policy "none") are covered by injected-fetch tests.
/// A missing corpus THROWS — a vacuously-green oracle is the green that lies.
/// </summary>
public class WarpEngineTests
{
    private static string CorpusDir([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile)
                  ?? throw new InvalidOperationException("warp manifest vectors: cannot resolve source directory.");
        return Path.Combine(Path.GetFullPath(Path.Combine(dir, "..", "..", "..")), "warp-manifest-vectors");
    }

    private static IEnumerable<string> CaseNames()
        => Directory.GetDirectories(CorpusDir())
            .Select(Path.GetFileName)
            .Where(n => n != null && n.Length > 3 && char.IsDigit(n[0]) && n[3] == '_')
            .OrderBy(n => n, StringComparer.Ordinal)!;

    public static IEnumerable<object[]> Cases() => CaseNames().Select(n => new object[] { n! });

    private static string UrlForInput(string filename)
    {
        int p = filename.IndexOf("__", StringComparison.Ordinal);
        if (p >= 0)
        {
            string host = filename.Substring(0, p);
            string rest = filename.Substring(p + 2);
            return $"https://{host}/{rest}";
        }
        return $"https://example.test/{filename}";
    }

    private static byte[] Decode(byte[] data, string? encoding)
    {
        if (string.IsNullOrEmpty(encoding) || encoding == "identity") return data;
        using var src = new MemoryStream(data);
        using var outp = new MemoryStream();
        Stream dec = encoding.ToLowerInvariant() switch
        {
            "gzip" => new GZipStream(src, CompressionMode.Decompress),
            "br" => new BrotliStream(src, CompressionMode.Decompress),
            _ => throw new NotSupportedException($"unknown Content-Encoding '{encoding}'")
        };
        using (dec) dec.CopyTo(outp);
        return outp.ToArray();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CorpusOracle(string name)
    {
        string dir = Path.Combine(CorpusDir(), name);
        byte[] envelope = File.ReadAllBytes(Path.Combine(dir, "envelope.json"));
        string origin = File.ReadAllText(Path.Combine(dir, "requested_origin")).Trim();
        ulong nowMs = ulong.Parse(File.ReadAllText(Path.Combine(dir, "now_ms")).Trim());
        string? nonce = File.Exists(Path.Combine(dir, "client_nonce"))
            ? File.ReadAllText(Path.Combine(dir, "client_nonce")).Trim() : null;

        // Pinned keys: trusted_key_ids are lowercase-hex raw public keys → pubkey = hexdecode(key_id).
        using var pinnedDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "pinned_keys.json")));
        var ids = pinnedDoc.RootElement.GetProperty("trusted_key_ids").EnumerateArray()
            .Select(e => e.GetString()!).ToList();
        var pinned = WarpManifestPinning.Parse(string.Join(" ", ids));

        var fetchMeta = new Dictionary<string, string>();
        var fmPath = Path.Combine(dir, "fetch_meta.json");
        if (File.Exists(fmPath))
            using (var fm = JsonDocument.Parse(File.ReadAllText(fmPath)))
                foreach (var p in fm.RootElement.EnumerateObject())
                    fetchMeta[p.Name] = p.Value.GetString() ?? "";

        var bytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[] originHtml = Array.Empty<byte>();
        foreach (var path in Directory.GetFiles(Path.Combine(dir, "inputs")))
        {
            string fname = Path.GetFileName(path);
            byte[] data = Decode(File.ReadAllBytes(path), fetchMeta.GetValueOrDefault(fname));
            if (fname == "origin.html") originHtml = data;
            else bytesByUrl[UrlForInput(fname)] = data;
        }

        Task<WarpFetchOutcome> Fetch(WarpAssetRequest req)
        {
            if (req.IsHtml)
                return Task.FromResult(originHtml.Length == 0
                    ? WarpFetchOutcome.FailedOutcome : WarpFetchOutcome.Fetched(originHtml, req.Url));
            return Task.FromResult(bytesByUrl.TryGetValue(req.Url, out var b)
                ? WarpFetchOutcome.Fetched(b, req.Url) : WarpFetchOutcome.FailedOutcome);
        }

        var result = await WarpEngine.VerifyAsync(origin, envelope, pinned, nonce, nowMs, Fetch);

        using var exp = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "expected.json")));
        var root = exp.RootElement;
        Assert.Equal(root.GetProperty("conclusion").GetString(), result.Conclusion.ToString());
        Assert.Equal(root.GetProperty("signatureState").GetString(), Camel(result.SignatureState.ToString()));
        if (root.TryGetProperty("manifestState", out var ms))
            Assert.Equal(ms.GetString(), Camel(result.ManifestState.ToString()));
        Assert.Equal(root.GetProperty("freshnessState").GetString(), Camel(result.FreshnessState.ToString()));
        Assert.Equal(root.GetProperty("htmlState").GetString(), Camel(result.HtmlState.ToString()));

        var expAssets = root.GetProperty("assets").EnumerateArray().ToList();
        Assert.Equal(expAssets.Count, result.Assets.Count);
        for (int i = 0; i < expAssets.Count; i++)
        {
            Assert.Equal(expAssets[i].GetProperty("url").GetString(), result.Assets[i].Url);
            Assert.Equal(expAssets[i].GetProperty("state").GetString(), Camel(result.Assets[i].State.ToString()));
        }
    }

    /// <summary>Enum names are PascalCase; corpus state strings are camelCase (except the SCREAMING
    /// conclusion, handled separately). Lower the first character.</summary>
    private static string Camel(string pascal)
        => string.IsNullOrEmpty(pascal) ? pascal : char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);

    // ---- Parity anchor (C#-vs-Swift): identical tuple from the identical fixture ----

    [Fact]
    public async Task AllGood_IsVerifiedMatch()
    {
        string dir = Path.Combine(CorpusDir(), "001_all_good");
        byte[] envelope = File.ReadAllBytes(Path.Combine(dir, "envelope.json"));
        string origin = File.ReadAllText(Path.Combine(dir, "requested_origin")).Trim();
        ulong nowMs = ulong.Parse(File.ReadAllText(Path.Combine(dir, "now_ms")).Trim());
        using var pinnedDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "pinned_keys.json")));
        var pinned = WarpManifestPinning.Parse(string.Join(" ",
            pinnedDoc.RootElement.GetProperty("trusted_key_ids").EnumerateArray().Select(e => e.GetString()!)));
        var bytesByUrl = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        byte[] html = Array.Empty<byte>();
        foreach (var path in Directory.GetFiles(Path.Combine(dir, "inputs")))
        {
            string f = Path.GetFileName(path);
            if (f == "origin.html") html = File.ReadAllBytes(path);
            else bytesByUrl[UrlForInput(f)] = File.ReadAllBytes(path);
        }
        var r = await WarpEngine.VerifyAsync(origin, envelope, pinned, null, nowMs, req =>
            Task.FromResult(req.IsHtml ? WarpFetchOutcome.Fetched(html, req.Url)
                : bytesByUrl.TryGetValue(req.Url, out var b) ? WarpFetchOutcome.Fetched(b, req.Url)
                : WarpFetchOutcome.FailedOutcome));

        Assert.Equal(WarpConclusion.VERIFIED_MATCH, r.Conclusion);
        Assert.Equal(WarpSignatureState.Verified, r.SignatureState);
        Assert.Equal(WarpManifestState.Parsed, r.ManifestState);
        Assert.Equal(WarpFreshnessState.Fresh, r.FreshnessState);
        Assert.Equal(WarpContentState.Match, r.HtmlState);
        Assert.All(r.Assets, a => Assert.Equal(WarpContentState.Match, a.State));
    }

    // ---- Pure lattice (§5) ----

    [Fact]
    public void Lattice_Precedence()
    {
        Assert.Equal(WarpConclusion.TAMPERING_DETECTED, WarpEngine.Conclude(WarpSignatureState.InvalidSignature,
            WarpManifestState.NotEvaluated, WarpFreshnessState.NotEvaluated, WarpContentState.NotEvaluated,
            Array.Empty<WarpContentState>()));
        Assert.Equal(WarpConclusion.INCONCLUSIVE, WarpEngine.Conclude(WarpSignatureState.UntrustedSigner,
            WarpManifestState.NotEvaluated, WarpFreshnessState.NotEvaluated, WarpContentState.NotEvaluated,
            Array.Empty<WarpContentState>()));
        Assert.Equal(WarpConclusion.TAMPERING_DETECTED, WarpEngine.Conclude(WarpSignatureState.Verified,
            WarpManifestState.Parsed, WarpFreshnessState.Fresh, WarpContentState.Match,
            new[] { WarpContentState.Match, WarpContentState.Mismatch }));
        Assert.Equal(WarpConclusion.INCONCLUSIVE, WarpEngine.Conclude(WarpSignatureState.Verified,
            WarpManifestState.Parsed, WarpFreshnessState.Fresh, WarpContentState.Match,
            new[] { WarpContentState.Match, WarpContentState.FetchFailedDeclared }));
        Assert.Equal(WarpConclusion.VERIFIED_MATCH, WarpEngine.Conclude(WarpSignatureState.Verified,
            WarpManifestState.Parsed, WarpFreshnessState.Fresh, WarpContentState.Match,
            new[] { WarpContentState.Match }));
    }

    // ---- Injected redirect fail-closed tests (§6) — not covered by the corpus ----

    private static (byte[] Envelope, IReadOnlyDictionary<string, byte[]> Pinned) SignEnvelope(string manifestJson)
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var pubRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        string keyId = Convert.ToHexString(pubRaw).ToLowerInvariant();
        byte[] b = Encoding.UTF8.GetBytes(manifestJson);
        var sig = SignatureAlgorithm.Ed25519.Sign(key, b);
        string env = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["manifest_b64"] = Convert.ToBase64String(b),
            ["signature_ed25519"] = Convert.ToBase64String(sig),
            ["key_id"] = keyId
        });
        return (Encoding.UTF8.GetBytes(env), new Dictionary<string, byte[]> { [keyId] = pubRaw });
    }

    [Fact]
    public async Task SameOriginRedirect_TerminalMismatch_IsFetchError_Inconclusive()
    {
        const string origin = "https://example.test/";
        byte[] html = Encoding.UTF8.GetBytes("<html>ok</html>");
        byte[] asset = Encoding.UTF8.GetBytes("console.log(1)");
        string manifest = "{" +
            "\"warp_manifest_version\":\"WARP_MANIFEST_V1\"," +
            "\"origin_url\":\"" + origin + "\",\"final_url\":\"" + origin + "\"," +
            "\"html_sha256\":\"" + WarpEngine.Sha256Hex(html) + "\"," +
            "\"assets\":[{\"url\":\"https://example.test/app.js\",\"final_url\":\"https://example.test/app.js\"," +
            "\"class\":\"JS\",\"sha256\":\"" + WarpEngine.Sha256Hex(asset) + "\",\"bytes\":" + asset.Length + "}]," +
            "\"timestamp\":1754179200000,\"freshness_seconds\":300,\"redirect_policy\":\"same-origin\"," +
            "\"allowed_redirects\":[],\"nonce\":null}";
        var (env, pinned) = SignEnvelope(manifest);
        var r = await WarpEngine.VerifyAsync(origin, env, pinned, null, 1754179200000UL, req =>
            Task.FromResult(req.IsHtml
                ? WarpFetchOutcome.Fetched(html, origin)
                : WarpFetchOutcome.Fetched(asset, "https://example.test/app.v2.js")));  // redirected terminal
        Assert.Equal(WarpSignatureState.Verified, r.SignatureState);
        Assert.Equal(WarpFreshnessState.Fresh, r.FreshnessState);
        Assert.Equal(WarpContentState.FetchError, r.Assets.Single().State);
        Assert.Equal(WarpConclusion.INCONCLUSIVE, r.Conclusion);
    }

    [Fact]
    public async Task NoneRedirect_IsFetchError_Inconclusive()
    {
        const string origin = "https://example.test/";
        byte[] html = Encoding.UTF8.GetBytes("<html>ok</html>");
        byte[] asset = Encoding.UTF8.GetBytes("body{}");
        string manifest = "{" +
            "\"warp_manifest_version\":\"WARP_MANIFEST_V1\"," +
            "\"origin_url\":\"" + origin + "\",\"final_url\":\"" + origin + "\"," +
            "\"html_sha256\":\"" + WarpEngine.Sha256Hex(html) + "\"," +
            "\"assets\":[{\"url\":\"https://example.test/x.css\",\"class\":\"CSS\"," +
            "\"sha256\":\"" + WarpEngine.Sha256Hex(asset) + "\",\"bytes\":" + asset.Length + "}]," +
            "\"timestamp\":1754179200000,\"freshness_seconds\":300,\"redirect_policy\":\"none\"," +
            "\"allowed_redirects\":[],\"nonce\":null}";
        var (env, pinned) = SignEnvelope(manifest);
        var r = await WarpEngine.VerifyAsync(origin, env, pinned, null, 1754179200000UL, req =>
            Task.FromResult(req.IsHtml
                ? WarpFetchOutcome.Fetched(html, origin)
                : WarpFetchOutcome.Fetched(asset, "https://example.test/x.css?v=2")));  // redirect under "none"
        Assert.Equal(WarpContentState.FetchError, r.Assets.Single().State);
        Assert.Equal(WarpConclusion.INCONCLUSIVE, r.Conclusion);
    }

    [Fact]
    public void Pinning_HexKeyId_RoundTrips()
    {
        using var key = Key.Create(SignatureAlgorithm.Ed25519,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var pubRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        string keyId = WarpManifestPinning.DeriveKeyId(pubRaw);
        Assert.Equal(64, keyId.Length);
        var parsed = WarpManifestPinning.Parse(keyId);
        Assert.True(parsed.ContainsKey(keyId));
        Assert.Equal(pubRaw, parsed[keyId]);
    }
}

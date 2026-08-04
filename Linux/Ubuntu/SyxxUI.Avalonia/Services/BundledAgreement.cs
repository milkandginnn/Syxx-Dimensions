using System.Security.Cryptography;
using System.Text;
using Avalonia.Platform;
using Syxx.Kit;

namespace SyxxUI.Avalonia.Services;

public sealed record BundledAgreement(AgreementDocument Document, string Text)
{
    public const string Version = "syxx-eula-draft-2026-07-30";
    public const string ResourceUri = "avares://Syxx/Assets/Legal/Syxx-EULA.txt";

    public static BundledAgreementLoadResult Load() =>
        LoadFrom(() => AssetLoader.Open(new Uri(ResourceUri)));

    public static BundledAgreementLoadResult LoadFrom(Func<Stream> open)
    {
        try
        {
            using var stream = open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var text = new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new BundledAgreementLoadResult.Loaded(
                new BundledAgreement(
                    new AgreementDocument(Version, digest),
                    text));
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or DecoderFallbackException
            or InvalidOperationException)
        {
            return new BundledAgreementLoadResult.Unknown();
        }
    }
}

public abstract record BundledAgreementLoadResult
{
    public sealed record Loaded(BundledAgreement Agreement) : BundledAgreementLoadResult;
    public sealed record Unknown : BundledAgreementLoadResult;
}

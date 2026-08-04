using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SyxxUI.Presentation;

/// <summary>Terminal states for one MOTD load attempt. First valid terminal wins.</summary>
public enum MotdLoadState
{
    InitializingRenderer,
    LoadingRemote,
    ShowingRemote,
    ShowingFallback,
    RendererUnavailable,
}

/// <summary>
/// Pure generation-token race for the shared five-second remote MOTD contract.
/// Twin of macOS <c>MotdLoadController</c>. Packaged fallback is always
/// <c>fallback-motd.html</c> — never motd-local or fallback-mac / fallback-win.
/// </summary>
public sealed class MotdLoadController
{
    public const double DeadlineSeconds = 5.0;
    public const string FallbackResourceName = "fallback-motd.html";
    public const string ContentRoleMarker = "motd-timeout-fallback";

    public static readonly HashSet<string> ForbiddenFallbackNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "motd-local.html",
        "fallback-mac.html",
        "fallback-win.html",
    };

    public ulong Generation { get; private set; } = 1;
    public MotdLoadState State { get; private set; } = MotdLoadState.InitializingRenderer;

    public ulong BeginRemoteLoad()
    {
        unchecked { Generation++; }
        State = MotdLoadState.LoadingRemote;
        return Generation;
    }

    public bool RemoteSucceeded(ulong gen)
    {
        if (gen != Generation) return false;
        if (State is not (MotdLoadState.LoadingRemote or MotdLoadState.InitializingRenderer)) return false;
        State = MotdLoadState.ShowingRemote;
        return true;
    }

    public bool ActivateFallback(ulong gen)
    {
        if (gen != Generation) return false;
        if (State is MotdLoadState.ShowingRemote or MotdLoadState.ShowingFallback or MotdLoadState.RendererUnavailable)
            return false;
        State = MotdLoadState.ShowingFallback;
        return true;
    }

    public bool MarkRendererUnavailable(ulong gen)
    {
        if (gen != Generation) return false;
        if (State is MotdLoadState.ShowingRemote or MotdLoadState.ShowingFallback) return false;
        State = MotdLoadState.RendererUnavailable;
        return true;
    }

    public bool Accepts(ulong gen) => gen == Generation;

    /// <summary>Read packaged fallback-motd.html from the executing assembly embedded resources or content files.</summary>
    public static string? ReadPackagedFallbackHtml(Assembly? asm = null)
    {
        asm ??= Assembly.GetExecutingAssembly();
        var name = Array.Find(asm.GetManifestResourceNames(),
            n => n.EndsWith(FallbackResourceName, StringComparison.OrdinalIgnoreCase));
        if (name is not null)
        {
            // Refuse forbidden substitutes (resource name must end with fallback-motd.html).
            if (!name.EndsWith(FallbackResourceName, StringComparison.OrdinalIgnoreCase)) return null;
            foreach (var forbidden in ForbiddenFallbackNames)
                if (name.EndsWith(forbidden, StringComparison.OrdinalIgnoreCase)) return null;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            var html = reader.ReadToEnd();
            return HtmlIsTimeoutFallback(html) ? html : null;
        }

        // Content file next to the assembly (Assets copied to output).
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "Assets", FallbackResourceName);
        if (!File.Exists(path)) path = Path.Combine(baseDir, FallbackResourceName);
        if (!File.Exists(path)) return null;
        if (ForbiddenFallbackNames.Contains(Path.GetFileName(path))) return null;
        var text = File.ReadAllText(path);
        return HtmlIsTimeoutFallback(text) ? text : null;
    }

    public static bool HtmlIsTimeoutFallback(string html) =>
        html.Contains("syxx-content-role", StringComparison.Ordinal)
        && html.Contains(ContentRoleMarker, StringComparison.Ordinal);
}

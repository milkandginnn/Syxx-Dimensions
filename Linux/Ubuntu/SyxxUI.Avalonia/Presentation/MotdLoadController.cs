// Linked twin lives under Windows; this file is a thin re-export path for Linux-only tests.
// Prefer compiling the Windows Presentation/MotdLoadController.cs via project link when possible.
// Kept as a namespace-compatible copy so Avalonia builds without a WinUI project reference cycle.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Platform;

namespace SyxxUI.Presentation;

public enum MotdLoadState
{
    InitializingRenderer,
    LoadingRemote,
    ShowingRemote,
    ShowingFallback,
    RendererUnavailable,
}

/// <summary>Linux twin of the five-second MOTD generation-token controller.</summary>
public sealed class MotdLoadController
{
    public const double DeadlineSeconds = 5.0;
    public const string FallbackResourceName = "fallback-motd.html";
    public const string ContentRoleMarker = "motd-timeout-fallback";
    public const string AvaresFallbackUri = "avares://Syxx/Assets/fallback-motd.html";

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

    public static string? ReadPackagedFallbackHtml()
    {
        try
        {
            var uri = new Uri(AvaresFallbackUri);
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var html = reader.ReadToEnd();
            if (!HtmlIsTimeoutFallback(html)) return null;
            var leaf = Path.GetFileName(uri.AbsolutePath);
            if (ForbiddenFallbackNames.Contains(leaf)) return null;
            return html;
        }
        catch
        {
            // Also try assembly-relative content for unit tests without Avalonia runtime.
            var asm = Assembly.GetExecutingAssembly();
            var name = Array.Find(asm.GetManifestResourceNames(),
                n => n.EndsWith(FallbackResourceName, StringComparison.OrdinalIgnoreCase));
            if (name is null) return null;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            var html = reader.ReadToEnd();
            return HtmlIsTimeoutFallback(html) ? html : null;
        }
    }

    public static bool HtmlIsTimeoutFallback(string html) =>
        html.Contains("syxx-content-role", StringComparison.Ordinal)
        && html.Contains(ContentRoleMarker, StringComparison.Ordinal);
}

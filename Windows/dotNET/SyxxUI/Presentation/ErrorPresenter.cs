using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Syxx.Kit;

namespace SyxxUI.Presentation;

/// <summary>
/// Turns a caught <see cref="Exception"/> (or a raw reason string from the core) into plain, honest,
/// user-facing text, and routes the full technical detail to the diagnostic sink so nothing is lost.
/// Twin of macOS <c>SyxxApp/ErrorPresenter.swift</c> — same boundary, same rules, same vocabulary.
///
/// <para>WHY THIS EXISTS. <c>ex.Message</c> on a .NET exception with no product-authored message
/// yields crash-reporter material — "Object reference not set to an instance of an object.",
/// "Exception of type 'Syxx.Kit.WitnessSignerException' was thrown.", an HRESULT, or a stack-trace
/// fragment — which a user cannot act on and which leaks internal type structure. Rendering it as
/// product copy is a defect. This presenter is the single boundary a caught exception passes through
/// before it can reach the screen: the screen gets a sentence a person can read; the diagnostic sink
/// gets the full <c>ToString()</c> (redacted there by <c>DiagnosticRedaction</c>).</para>
///
/// <para>HONESTY. When an exception carries no human-readable cause, <see cref="UserDetail"/> returns
/// <c>null</c> so the caller states the failure WITHOUT a cause. We never invent why something
/// failed. An honest "this could not be completed" beats a confident wrong explanation. A null
/// detail is NOT a success: callers must keep a separate flag or state for "did it fail",
/// never infer it from an empty detail string.</para>
///
/// <para>LINKED, NOT FORKED. This file is framework-neutral C# (System + Syxx.Kit only) and is
/// compiled into BOTH the WinUI client and the Avalonia client via a &lt;Compile Include&gt; link in
/// <c>Ubuntu/SyxxUI.Avalonia/SyxxUI.Avalonia.csproj</c>, exactly as <c>Services/DimensionService.cs</c>
/// already is. Two copies of an honesty boundary drift, and the drift is invisible.</para>
/// </summary>
public static class ErrorPresenter
{
    /// <summary>
    /// A human-readable cause for <paramref name="error"/>, or <c>null</c> when the exception carries
    /// none — in which case the caller should state the failure plainly rather than invent a reason.
    /// Always routes the full technical detail (type, message, stack) to the diagnostic sink, which
    /// redacts hostnames/IPs/user paths before anything leaves the machine.
    /// </summary>
    /// <param name="component">Dotted breadcrumb subject, e.g. "witness.saveKey", "portal.trace".</param>
    public static string? UserDetail(Exception error, string component)
    {
        if (error is null) { return null; }

        // Full technical detail to the diagnostic sink FIRST, so it survives even if the caller
        // decides to show nothing. Never deleted — only separated from the screen.
        SyxxDiagnostics.Breadcrumb(
            category: "gui.error",
            message: $"{component}: {error}",
            level: DiagnosticSeverity.Error,
            data: new Dictionary<string, string>
            {
                ["type"] = error.GetType().FullName ?? error.GetType().Name,
                ["message"] = error.Message ?? string.Empty,
            });

        return CleanReason(error);
    }

    /// <summary>
    /// Sanitize a raw reason STRING (e.g. <c>LeakTestResult.ErrorReason</c>, which the core may have
    /// built from an exception's message — see <c>Syxx.Kit/LeakDetector.cs:207,271</c>): returns it
    /// unchanged when it is genuine human-readable text, or <c>null</c> when it is crash-reporter
    /// boilerplate that must not be shown. Pure and side-effect free, so it is safe to call from a
    /// view's render path.
    /// </summary>
    public static string? DisplayReason(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) { return null; }
        var trimmed = raw.Trim();
        return IsBoilerplate(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Compose a user-facing sentence: <paramref name="baseSentence"/> alone when there is no
    /// presentable cause, or "<c>base: detail</c>" when there is. The base sentence MUST already end
    /// in a full stop; the colon form drops it. Mirrors the macOS call sites'
    /// <c>detail.map { "base: \($0)" } ?? "base."</c> shape so the two platforms read identically.
    /// </summary>
    public static string Compose(string baseSentence, string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) { return baseSentence; }
        var stem = baseSentence.TrimEnd();
        if (stem.EndsWith('.')) { stem = stem[..^1]; }
        return $"{stem}: {detail!.Trim()}";
    }

    private static string? CleanReason(Exception error)
    {
        // AggregateException's own message is boilerplate ("One or more errors occurred.");
        // the first inner exception is where a real cause, if any, lives.
        if (error is AggregateException agg && agg.InnerExceptions.Count > 0)
        {
            return CleanReason(agg.InnerExceptions[0]);
        }
        var msg = error.Message;
        if (string.IsNullOrWhiteSpace(msg)) { return null; }
        var trimmed = msg.Trim();
        if (!IsBoilerplate(trimmed)) { return trimmed; }
        // The outer frame said nothing usable; a wrapped inner exception often does
        // (e.g. an InvalidOperationException wrapping a SocketException "Connection refused").
        return error.InnerException is { } inner ? CleanReason(inner) : null;
    }

    // ── Boilerplate detection ───────────────────────────────────────────────────────────────────
    // Twin of macOS ErrorPresenter.isBoilerplate, translated to the shapes the .NET BCL actually
    // emits. Deliberately a BLACKLIST, like macOS: a message we do not recognise as crash-reporter
    // material is shown, because runtime messages such as "No such host is known." or
    // "Connection refused" are genuinely the most useful thing we can tell the user.

    private const RegexOptions Opts = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// A namespace-qualified exception type name in the text — "System.Exception",
    /// "Syxx.Kit.WitnessSignerException", "System.Net.Sockets.SocketException". The .NET analogue of
    /// macOS's module-qualified type dump. At least one dot, and the final segment ends in
    /// "Exception", so a bare product word like "Exception" alone is not caught.
    private static readonly Regex QualifiedExceptionType =
        new(@"\b[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)*\.[A-Za-z0-9_]*Exception\b", Opts);

    /// A memory address or HRESULT — "0x80070005", "0x1a2b3c4d".
    private static readonly Regex HexAddress = new(@"0x[0-9a-fA-F]{4,}", Opts);

    /// A stack-trace frame: "   at Namespace.Type.Method(...)".
    private static readonly Regex StackFrame = new(@"(^|\n)\s+at\s+\S+\(", Opts);

    /// BCL fragments that name no condition a user could act on. Each is the default message of a
    /// programming-error exception, not a product-authored sentence.
    ///
    /// Matched by CONTAINS, not by prefix or equality, because the core routinely prepends its own
    /// label before an exception message — <c>Syxx.Kit/LeakDetector.cs:207</c> composes
    /// <c>"Token request failed: " + ex.Message</c>, which puts the boilerplate in the MIDDLE of the
    /// string. A prefix-only rule let exactly that shape through; the ErrorPresenterTests case that
    /// caught it is the reason this is a contains list.
    private static readonly string[] BclBoilerplate =
    {
        "exception of type '",
        "object reference not set to an instance of an object",
        "unable to cast object of type '",
        "specified cast is not valid.",
        "index was outside the bounds of the array",
        "index was out of range.",
        "value cannot be null.",
        "one or more errors occurred.",
        "arithmetic operation resulted in an overflow.",
        "attempted to divide by zero.",
        "the given key '",
        "attempted to read or write protected memory",
        "specified argument was out of the range of valid values.",
        "the operation couldn't be completed",     // macOS-hosted .NET surfaces this too
        "the operation couldn’t be completed",
        "unknown context at",
        "--- end of stack trace",
        "stack trace:",
    };

    /// <summary>
    /// True when <paramref name="s"/> is crash-reporter material rather than product copy: a BCL
    /// programming-error message, a namespace-qualified type dump, a memory address/HRESULT, a
    /// stack-trace fragment, or an "(unknown context at …)" fragment.
    /// </summary>
    public static bool IsBoilerplate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return true; }
        var t = s.Trim();
        var lower = t.ToLowerInvariant();

        foreach (var fragment in BclBoilerplate)
        {
            if (lower.Contains(fragment, StringComparison.Ordinal)) { return true; }
        }

        if (HexAddress.IsMatch(t)) { return true; }
        if (QualifiedExceptionType.IsMatch(t)) { return true; }
        if (StackFrame.IsMatch(t)) { return true; }

        return false;
    }
}

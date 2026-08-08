using System;
using System.Diagnostics;
using Syxx.Kit;

namespace SyxxUI.Avalonia.Services;

/// <summary>
/// One-way boundary between first-run presentation and the normal application.
/// The callback is never evaluated before setup is durably complete.
/// </summary>
public sealed class ReadyApplicationGate
{
    private bool _opened;

    /// <summary>
    /// L2-005: human-readable reason the gate refused to open, so the UI can surface a
    /// meaningful error instead of silently returning null.
    /// </summary>
    public string? FailureReason { get; private set; }

    public T? TryOpen<T>(
        OnboardingState state,
        SyxxSettings settings,
        Func<SyxxSettings, T> open)
        where T : class
    {
        if (_opened)
        {
            FailureReason = "The application has already been initialized this session.";
            Trace.TraceWarning("[Syxx] Gate refused: already opened.");
            return null;
        }

        if (state != OnboardingState.Ready)
        {
            FailureReason = $"Onboarding is not complete (state: {state}). Complete setup before launching the main application.";
            Trace.TraceWarning("[Syxx] Gate refused: onboarding state is {0}.", state);
            return null;
        }

        _opened = true;
        FailureReason = null;
        return open(settings);
    }
}

using Syxx.Kit;

namespace SyxxUI.Avalonia.Services;

/// <summary>
/// One-way boundary between first-run presentation and the normal application.
/// The callback is never evaluated before setup is durably complete.
/// </summary>
public sealed class ReadyApplicationGate
{
    private bool _opened;

    public T? TryOpen<T>(
        OnboardingState state,
        SyxxSettings settings,
        Func<SyxxSettings, T> open)
        where T : class
    {
        if (_opened || state != OnboardingState.Ready)
        {
            return null;
        }

        _opened = true;
        return open(settings);
    }
}

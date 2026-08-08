using Avalonia.Media;
using Syxx.Kit;

namespace SyxxUI.Avalonia.Presentation;

/// <summary>
/// Avalonia twin of the Windows <c>SeverityBrush</c> — the ONE Presentation file that cannot be
/// linked verbatim, because the Windows original returns WinUI brush types. The mapping itself is
/// identical and is the honesty-critical part:
///
/// <b>Clean is the only green.</b> Critical is red, High is orange, Warning is amber, Info is
/// purple, and Unknown/ungraded is a neutral grey. A state we did not establish must never render
/// in the success color — the green-on-inconclusive defect class this project has shipped before.
///
/// <para>Takes <see cref="Severity"/> (the Kit enum) directly, matching the Windows
/// <c>SeverityBrush.Key</c> signature case-for-case. It previously routed through the DST-era
/// <c>Stage5Severity</c>, which the matrix cutover retired; that indirection collapsed five Kit
/// severities into three tones and is gone.</para>
/// </summary>
public static class SeverityColor
{
    public static IBrush For(Severity severity) => severity switch
    {
        Severity.Clean    => new SolidColorBrush(Color.Parse("#3FD08A")), // success green
        Severity.Info     => new SolidColorBrush(Color.Parse("#A79CC4")), // info purple
        Severity.Warning  => new SolidColorBrush(Color.Parse("#F2B33D")), // warning amber
        Severity.High     => new SolidColorBrush(Color.Parse("#FF9A3D")), // high orange (NOT red)
        Severity.Critical => new SolidColorBrush(Color.Parse("#FF5C7A")), // danger red
        // D275: Unknown AND any future/unmapped case fall here: neutral GREY, never green and
        // never the info-purple reserved for Severity.Info. A state we did not establish must
        // render in the most semantically neutral tone available.
        _                 => new SolidColorBrush(Color.Parse("#808080")),
    };
}

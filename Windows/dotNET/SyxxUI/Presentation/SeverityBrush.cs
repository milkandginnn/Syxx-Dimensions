using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Syxx.Kit;

namespace SyxxUI.Presentation;

/// <summary>
/// The single Severity → colour mapping for the whole Windows client — the twin of macOS
/// <c>Color.ioSeverity</c> (IoDesignSystem.swift). It exists for the same reason the macOS side
/// folded three independent switches into one: two mappings eventually disagree, and a result that
/// grades a different colour on each surface is exactly the false-confidence this app refuses.
///
/// <para><c>Unknown</c> is deliberately NOT green and deliberately not alarming: an unmeasured state
/// is neither clean nor a threat, and colouring it either way is the false-clean this codebase
/// exists to refuse. Info→purple, Warning→amber, High→orange (NOT red), Critical→red.</para>
/// </summary>
public static class SeverityBrush
{
    /// The design-system brush resource key for a severity. Route every severity→colour through here.
    public static string Key(Severity severity) => severity switch
    {
        Severity.Clean    => "IoSuccessGreenBrush",
        Severity.Info     => "IoInfoPurpleBrush",
        Severity.Warning  => "IoWarningAmberBrush",
        Severity.High     => "IoHighOrangeBrush",
        Severity.Critical => "IoCriticalRedBrush",
        _                 => "IoTextSecondaryBrush",   // Unknown / Ungraded — never green
    };

    /// The resolved brush for a severity, from the merged design-system dictionary.
    public static Brush Of(Severity severity) =>
        (Brush)Application.Current.Resources[Key(severity)];
}

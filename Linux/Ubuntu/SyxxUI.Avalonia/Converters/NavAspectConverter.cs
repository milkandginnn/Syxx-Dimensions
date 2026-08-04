using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SyxxUI.Avalonia.Converters;

/// <summary>
/// Maps a sidebar nav LABEL (the plain strings held by <c>ShellViewModel.NavItems</c>) to the
/// warden-theme presentation aspects the mockup specifies per item (IMPLEMENTATION_GUIDE §2.3 / §4 and
/// WardenSidebar.dc.html). This is deliberately a VIEW-LAYER converter with zero Syxx.Kit dependency,
/// so the sidebar can be re-skinned without touching the ViewModel's shape (it stays
/// <c>ObservableCollection&lt;string&gt;</c> — task constraint).
///
/// Aspect is passed as ConverterParameter:
///   "glyph"      -> the rune/symbol glyph (string)
///   "glyphBrush" -> the glyph color (IBrush): gold for Dimensions, muted-violet for runes, else muted
///   "runeSize"   -> font size (double): 17 for Elder-Futhark runes, 15 for geometric symbols
///   "divider"    -> bool: a gold group divider is drawn ABOVE this item
///   "locked"     -> bool: the item shows the "PRO" lock pill (Mirror / Glutton / Witness)
/// </summary>
public sealed class NavAspectConverter : IValueConverter
{
    private record Meta(string Glyph, bool IsRune, bool DivBefore, bool Locked);

    // §2.3 nav order + §4 iconography. Runes are the six wardens; symbols per §4 for the rest.
    private static readonly Dictionary<string, Meta> Map = new()
    {
        ["What's New"]  = new("✦", false, false, false),   // ✦
        ["Dashboard"]   = new("◧", false, false, false),   // ◧
        ["Prophet"]     = new("ᚨ", true,  true,  false),   // ᚨ  (divider before the wardens)
        ["Mirror"]      = new("ᛞ", true,  false, true),    // ᛞ  (Pro)
        ["Portal"]      = new("ᛜ", true,  false, false),   // ᛜ
        ["Glutton"]     = new("ᚦ", true,  false, true),    // ᚦ  (Pro)
        ["Cage"]        = new("ᚾ", true,  false, false),   // ᚾ
        ["Witness"]     = new("ᛉ", true,  false, true),    // ᛉ  (Pro)
        ["Conclusions"] = new("⟐", false, true,  false),   // ⟐  (divider before the result)
        ["Settings"]    = new("⚙", false, true,  false),   // ⚙  (divider before app-level items)
        ["License"]     = new("⚷", false, false, false),   // ⚷
    };

    private static readonly IBrush GoldGlyph  = new SolidColorBrush(Color.Parse("#e0b45a"));
    private static readonly IBrush RuneGlyph  = new SolidColorBrush(Color.Parse("#9b8ac9"));
    private static readonly IBrush PlainGlyph = new SolidColorBrush(Color.Parse("#8f83b8"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var label = value as string ?? "";
        var aspect = parameter as string ?? "glyph";
        if (!Map.TryGetValue(label, out var m))
            m = new Meta("•", false, false, false); // • fallback, never throws

        return aspect switch
        {
            "glyph"      => m.Glyph,
            "glyphBrush" => m.IsRune ? RuneGlyph : PlainGlyph,
            "runeSize"   => m.IsRune ? 17.0 : 15.0,
            "divider"    => m.DivBefore,
            "locked"     => m.Locked,
            _            => m.Glyph,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

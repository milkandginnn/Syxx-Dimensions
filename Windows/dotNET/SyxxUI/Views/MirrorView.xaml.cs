using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using SyxxUI.ViewModels;
using Syxx.Kit;

namespace SyxxUI.Views;

public sealed partial class MirrorView : Page
{
    public ObservableCollection<ResolverRow> ResolverRows { get; } = [];

    private AppViewModel? _vm;

    public MirrorView()
    {
        InitializeComponent();
        ResolverRowsList.ItemsSource = ResolverRows;
    }

    // E155: subscribe to PropertyChanged so the pane refreshes when data changes while visible.
    private System.ComponentModel.PropertyChangedEventHandler? _pcHandler;

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = e.Parameter as AppViewModel;
        if (_vm is not null)
        {
            _pcHandler = (_, _) => Refresh();
            _vm.PropertyChanged += _pcHandler;
        }
        Refresh();
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (_vm is not null && _pcHandler is not null)
            _vm.PropertyChanged -= _pcHandler;
        base.OnNavigatedFrom(e);
    }

    private async void CheckBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Domain = DomainBox.Text;
        CheckBtn.IsEnabled = false;
        try
        {
            await _vm.RunMirrorCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            // E147: an unhandled exception in an async void handler kills the process.
            System.Diagnostics.Debug.WriteLine($"[Syxx] Mirror check failed: {ex}");
        }
        finally
        {
            CheckBtn.IsEnabled = true;
        }
        Refresh();
    }

    private static Color Col(string key) => (Color)Application.Current.Resources[key];
    private static Brush Solid(Color c) => new SolidColorBrush(c);
    private static Brush Faint(Color c, double a) => new SolidColorBrush(Color.FromArgb((byte)(a * 255), c.R, c.G, c.B));

    private void Refresh()
    {
        if (_vm is null) return;
        RefreshComparison();
        RefreshWarp();
    }

    private void RefreshComparison()
    {
        var comp = _vm!.SectionInputs.Comparison;
        ResolverRows.Clear();

        if (comp is null)
        {
            // E132: a probe that failed must not render as one that never ran.
            ComparisonEmptyText.Text = _vm!.MirrorRunState == AppViewModel.ProbeRunState.Failed
                ? $"The check could not complete: {(SyxxUI.Presentation.ErrorPresenter.DisplayReason(_vm.MirrorRunMessage) ?? "the check could not complete").TrimEnd('.')}. This is not a clean result."
                : "Run a check to see resolver comparison results.";
            ComparisonEmptyText.Visibility = Visibility.Visible;
            ComparisonContent.Visibility = Visibility.Collapsed;
            return;
        }

        ComparisonEmptyText.Visibility = Visibility.Collapsed;
        ComparisonContent.Visibility = Visibility.Visible;

        var failColor = Col("IoCriticalRedColor");
        var secondary = Col("IoTextSecondaryColor");

        // Public (remote) resolvers, sorted by name — parity with macOS ordering.
        foreach (var name in comp.PublicResolverOutcomes.Keys.OrderBy(k => k))
        {
            var o = comp.PublicResolverOutcomes[name];
            ResolverRows.Add(new ResolverRow(
                "", // globe
                name,
                DisplayIPs(o),
                // Defect-2: DNSResponse.ErrorMessage is built from ex.Message in the core
                // (Syxx.Kit/DNSResolver.cs:208,397,402,407,412,770), so it can be crash-reporter
                // material. Fall back to the response STATUS, which is a real classified outcome —
                // strictly more useful to the user than an exception's internal text.
                o.DidFail
                    ? $"Failed: {SyxxUI.Presentation.ErrorPresenter.DisplayReason(o.ErrorMessage) ?? o.Status.ToString()}"
                    : "Public resolver",
                Solid(o.DidFail ? failColor : secondary)));
        }

        // Local resolver.
        ResolverRows.Add(new ResolverRow(
            "", // devices / desktop
            "Local resolver",
            DisplayIPs(comp.LocalOutcome),
            // Defect-2: same ErrorMessage-from-ex.Message path as the public rows above.
            comp.LocalOutcome.DidFail
                ? $"Failed: {SyxxUI.Presentation.ErrorPresenter.DisplayReason(comp.LocalOutcome.ErrorMessage) ?? comp.LocalOutcome.Status.ToString()}"
                : $"Server: {comp.LocalServer}",
            Solid(comp.LocalOutcome.DidFail ? failColor : secondary)));

        // Result — label/colour/detail mirror macOS. Only genuine agreement is green.
        string label = comp.IsInconclusive ? "INCONCLUSIVE"
                     : comp.IsRedirectionDetected ? "TAMPERING DETECTED"
                     : comp.IsDegraded ? "CONSISTENT (DEGRADED)"
                     : "RESOLVERS AGREE";
        Color color = comp.IsRedirectionDetected ? Col("IoCriticalRedColor")
                    : comp.IsInconclusive ? Col("IoWarningAmberColor")
                    : comp.IsDegraded ? Col("IoWarningAmberColor")
                    : Col("IoSuccessGreenColor");
        string detail = ComparisonDetail(comp);

        ComparisonResultLabel.Text = label;
        ComparisonResultLabel.Foreground = Solid(color);
        ComparisonResultDetail.Text = detail;
        ComparisonResultBox.Background = Faint(color, 0.10);
        ComparisonResultBox.BorderBrush = Faint(color, 0.35);
    }

    private static string ComparisonDetail(ComparisonResult c)
    {
        if (c.IsInconclusive) return "No trustworthy public baseline available.";
        if (c.IsDegraded) return "No tampering detected — some public resolvers were unreachable.";
        if (c.IsRedirectionDetected)
        {
            var localIPs = c.LocalOutcome.Records.Select(r => r.Rdata).OrderBy(x => x).ToList();
            var disagreements = c.PublicResolverOutcomes
                .Where(kvp => !kvp.Value.DidFail)
                .OrderBy(kvp => kvp.Key)
                .Select(kvp =>
                {
                    var publicIPs = kvp.Value.Records.Select(r => r.Rdata).OrderBy(x => x).ToList();
                    return !publicIPs.SequenceEqual(localIPs) && publicIPs.Count > 0
                        ? $"{kvp.Key}: {string.Join(", ", publicIPs)}"
                        : null;
                })
                .Where(s => s is not null)
                .ToList();
            return disagreements.Count > 0
                ? $"Local ({string.Join(", ", localIPs)}) differs from: {string.Join("; ", disagreements)}"
                : "Local and public resolver answers disagree — details unavailable.";
        }
        return "No DNS-level redirection detected.";
    }

    private static string DisplayIPs(ResolverQueryOutcome o)
    {
        if (o.DidFail) return "[failed]";
        if (o.Records.Count == 0) return $"[no records — {o.Status}]";
        return string.Join(", ", o.Records.Select(r => r.Rdata));
    }

    private void RefreshWarp()
    {
        var comp = _vm!.SectionInputs.Comparison;
        var warp = _vm.SectionInputs.Warp;

        if (warp is not null)
        {
            WarpEmptyText.Visibility = Visibility.Collapsed;
            WarpProGate.Visibility = Visibility.Collapsed;
            WarpContent.Visibility = Visibility.Visible;

            RemoteStructureText.Text =
                $"{warp.RemoteScriptURLs.Count} scripts, {warp.RemoteStylesheetURLs.Count} stylesheets, {warp.RemoteIframeURLs.Count} iframes";
            LocalStructureText.Text =
                $"{warp.LocalScriptURLs.Count} scripts, {warp.LocalStylesheetURLs.Count} stylesheets, {warp.LocalIframeURLs.Count} iframes";

            string label = warp.IsInconclusive ? "INCONCLUSIVE"
                         : warp.IsSecure ? "CONTENT INTACT"
                         : "TAMPERING DETECTED";
            Color color = warp.IsInconclusive ? Col("IoWarningAmberColor")
                        : warp.IsSecure ? Col("IoSuccessGreenColor")
                        : Col("IoCriticalRedColor");
            string detail = warp.IsInconclusive
                ? (warp.InconclusiveReason ?? "content check could not be completed")
                : warp.IsSecure
                    ? "Local and remote structures match."
                    : "Local and remote structures differ — possible content injection.";

            WarpResultLabel.Text = label;
            WarpResultLabel.Foreground = Solid(color);
            WarpResultDetail.Text = detail;
            WarpResultBox.Background = Faint(color, 0.10);
            WarpResultBox.BorderBrush = Faint(color, 0.35);
        }
        else if (comp is not null)
        {
            // Mirror ran but warp is nil — license gate (parity with macOS).
            WarpEmptyText.Visibility = Visibility.Collapsed;
            WarpContent.Visibility = Visibility.Collapsed;
            WarpProGate.Visibility = Visibility.Visible;
        }
        else
        {
            WarpEmptyText.Visibility = Visibility.Visible;
            WarpContent.Visibility = Visibility.Collapsed;
            WarpProGate.Visibility = Visibility.Collapsed;
        }
    }
}

// Settable properties, not a positional record: WinUI compiled x:Bind assigns these via setter,
// which a record's init-only properties reject (CS8852). Positional constructor kept for call sites.
public sealed class ResolverRow
{
    public string Glyph { get; set; }
    public string Name { get; set; }
    public string Ips { get; set; }
    public string StatusLine { get; set; }
    public Brush StatusBrush { get; set; }
    public ResolverRow(string glyph, string name, string ips, string statusLine, Brush statusBrush)
    {
        Glyph = glyph; Name = name; Ips = ips; StatusLine = statusLine; StatusBrush = statusBrush;
    }
}

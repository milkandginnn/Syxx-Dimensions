using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Syxx.Kit;

namespace SyxxUI.Views;

/// <summary>
/// G-4 settings pane (Windows). Backed by the typed <see cref="SyxxSettings"/> store — no scattered
/// registry reads. HONESTY: a toggle records intent; the Encrypted-DNS switch calls the real
/// <see cref="EncryptedDNSConfigurator"/> and shows the actual result — on an UNELEVATED GUI that is
/// "needs Administrator", never a fake "on". Notifications are a stored preference (delivery not built
/// yet). Data removal really erases the stored baselines.
/// </summary>
public sealed partial class SettingsView : Page
{
    private SyxxSettings _settings = SyxxSettingsStore.Load();
    private bool _loading = true;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Hydrate();
    }

    private void Hydrate()
    {
        _loading = true;
        ResolverPicker.Items.Clear();
        foreach (var r in EncryptedDNSConfig.Catalog)
            ResolverPicker.Items.Add(new ComboBoxItem { Content = r.DisplayName, Tag = r.Id });
        ResolverPicker.SelectedIndex = System.Math.Max(0,
            EncryptedDNSConfig.Catalog.ToList().FindIndex(r => r.Id == _settings.EncryptedDnsResolverId));
        TransportPicker.SelectedIndex = _settings.EncryptedDnsTransport == EncryptedDNSTransport.Dot ? 1 : 0;
        EncryptToggle.IsOn = _settings.EncryptedDnsEnabled;
        // E133: on re-entry, read the live system encryption state for the caption rather than
        // leaving the XAML default "Not enabled." The stored intent (_settings.EncryptedDnsEnabled)
        // drives the toggle; the caption shows what the OS actually reports.
        var liveEnc = NetworkKeyState.EncryptedDNSStatus();
        EncryptState.Text = liveEnc.State switch
        {
            ConditionState.Yes => "Encrypted: the system DNS is currently using an encrypted transport.",
            ConditionState.No => "Not encrypted: the system DNS is currently using plaintext.",
            _ => "Could not determine the live system encryption state.",
        };
        TelemetryToggle.IsOn = _settings.TelemetryEnabled;
        CrashReportingToggle.IsOn = _settings.CrashReportingEnabled;
        UpdateCheckToggle.IsOn = _settings.UpdateCheckEnabled;
        NotifyToggle.IsOn = _settings.NotificationsEnabled;
        _loading = false;
    }

    private void Persist() => SaveNote.Text = SyxxSettingsStore.Save(_settings) ? "Saved." : "Could not save settings.";

    private async void EncryptToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.EncryptedDnsEnabled = EncryptToggle.IsOn;
        Persist();
        if (EncryptToggle.IsOn)
        {
            var r = await EncryptedDNSConfigurator.EnableAsync(_settings.EncryptedDnsResolverId, _settings.EncryptedDnsTransport);
            EncryptState.Text = Describe(r, turningOn: true);
        }
        else
        {
            // E133: Syxx has no DisableAsync — do not fabricate a result that claims encryption was
            // turned off when no system change was made. State the honest truth.
            EncryptState.Text = "Preference saved. Syxx cannot undo the system DNS change from here — revert it in Settings › Network, or run Syxx elevated.";
        }
    }

    private void Resolver_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ResolverPicker.SelectedItem is not ComboBoxItem it) return;
        _settings.EncryptedDnsResolverId = (string)it.Tag;
        Persist();
    }

    private void Transport_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TransportPicker.SelectedItem is not ComboBoxItem it) return;
        _settings.EncryptedDnsTransport = (string)it.Tag == "dot" ? EncryptedDNSTransport.Dot : EncryptedDNSTransport.Doh;
        Persist();
    }

    private void Telemetry_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.TelemetryEnabled = TelemetryToggle.IsOn; Persist();
    }

    private void CrashReporting_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.CrashReportingEnabled = CrashReportingToggle.IsOn; Persist();
    }

    private void UpdateCheck_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.UpdateCheckEnabled = UpdateCheckToggle.IsOn; Persist();
    }

    private void Notify_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.NotificationsEnabled = NotifyToggle.IsOn; Persist();
    }

    private void RemoveData_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Sentinel.Forget();
        HomeBaseline.Forget();
        new AnswerLedger().Forget();
        DataNote.Text = "Erased: Sentinel baseline, home baseline, and the passive-DNS ledger.";
    }

    /// Honest tri-state rendering — the whole point of the feature.
    private static string Describe(EncryptedDNSApplyResult r, bool turningOn) => r.Kind switch
    {
        EncryptedDNSApplyKind.Enabled => "Encrypted: every system DNS query is going over the chosen transport.",
        EncryptedDNSApplyKind.ElevationRequired =>
            "Changing system DNS needs Administrator. The setting is saved; run Syxx elevated to apply it.",
        // Defect-2: EncryptedDNSApplyResult.Reason is set verbatim from ex.Message in the core
        // (Syxx.Kit/EncryptedDNSConfigurator.cs:49), so it can be crash-reporter material. Route it
        // through the presenter, and drop the ": <reason>" clause entirely when there is nothing
        // readable to put there — the previous "(r.Reason ?? "")" produced a dangling
        // "Could not enable: . Your DNS was left unchanged." whenever the reason was absent.
        EncryptedDNSApplyKind.Inconclusive =>
            "Saved, but not yet confirmed encrypted"
            + Clause(r.Reason) + ". Treated as NOT protected until confirmed.",
        // E133: the off-path is now handled separately with honest copy (no DisableAsync exists).
        // This default arm should only be reached for the on-path's unexpected Kind values.
        _ => "Could not enable" + Clause(r.Reason) + ". Your DNS was left unchanged.",
    };

    /// ": &lt;readable reason&gt;" when the core supplied one a person can act on, otherwise nothing.
    private static string Clause(string? reason)
    {
        var shown = SyxxUI.Presentation.ErrorPresenter.DisplayReason(reason);
        return shown is null ? "" : ": " + shown.TrimEnd('.');
    }
}

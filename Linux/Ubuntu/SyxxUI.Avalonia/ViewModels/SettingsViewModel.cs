using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Syxx.Kit;

namespace SyxxUI.Avalonia.ViewModels;

/// <summary>
/// G-4 settings pane (Linux). Backed by the typed <see cref="SyxxSettings"/> store — no scattered
/// reads. HONESTY: a toggle records intent; the Encrypted-DNS switch calls the real
/// <see cref="EncryptedDNSConfigurator"/> (Linux systemd-resolved DoT) and shows the actual result —
/// on an unelevated app that is "needs root", never a fake "on". Notifications are a stored
/// preference (delivery not built). Data removal really erases the stored baselines.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private SyxxSettings _settings = SyxxSettingsStore.Load();
    private bool _loading = true;

    public ObservableCollection<string> Resolvers { get; } = new();

    [ObservableProperty] private int _resolverIndex;
    [ObservableProperty] private bool _useDot;
    [ObservableProperty] private bool _encryptEnabled;
    [ObservableProperty] private bool _telemetryEnabled;
    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private string _encryptState = "Not enabled.";
    [ObservableProperty] private string _dataNote = "";
    [ObservableProperty] private string _saveNote = "";

    /// <summary>Authenticated G-1 updater surface (Phase 3.2).</summary>
    public UpdateViewModel Update { get; } = new();

    public SettingsViewModel()
    {
        foreach (var r in EncryptedDNSConfig.Catalog) Resolvers.Add(r.DisplayName);
        ResolverIndex = System.Math.Max(0,
            System.Array.FindIndex(EncryptedDNSConfig.Catalog.ToArray(), r => r.Id == _settings.EncryptedDnsResolverId));
        UseDot = _settings.EncryptedDnsTransport == EncryptedDNSTransport.Dot;
        EncryptEnabled = _settings.EncryptedDnsEnabled;
        TelemetryEnabled = _settings.TelemetryEnabled;
        NotificationsEnabled = _settings.NotificationsEnabled;
        _loading = false;
    }

    private string CurrentResolverId =>
        EncryptedDNSConfig.Catalog[System.Math.Clamp(ResolverIndex, 0, EncryptedDNSConfig.Catalog.Count - 1)].Id;

    private void Persist()
    {
        _settings.EncryptedDnsResolverId = CurrentResolverId;
        _settings.EncryptedDnsTransport = UseDot ? EncryptedDNSTransport.Dot : EncryptedDNSTransport.Doh;
        _settings.EncryptedDnsEnabled = EncryptEnabled;
        _settings.TelemetryEnabled = TelemetryEnabled;
        _settings.NotificationsEnabled = NotificationsEnabled;
        SaveNote = SyxxSettingsStore.Save(_settings) ? "Saved." : "Could not save settings.";
    }

    partial void OnResolverIndexChanged(int value) { if (!_loading) Persist(); }
    partial void OnUseDotChanged(bool value) { if (!_loading) Persist(); }
    partial void OnTelemetryEnabledChanged(bool value) { if (!_loading) Persist(); }
    partial void OnNotificationsEnabledChanged(bool value) { if (!_loading) Persist(); }

    async partial void OnEncryptEnabledChanged(bool value)
    {
        if (_loading) return;
        Persist();
        var r = value
            ? await EncryptedDNSConfigurator.EnableAsync(CurrentResolverId,
                UseDot ? EncryptedDNSTransport.Dot : EncryptedDNSTransport.Doh)
            : new EncryptedDNSApplyResult(EncryptedDNSApplyKind.Failed, Reason: "disabled");
        EncryptState = Describe(r, value);
    }

    [RelayCommand]
    private void RemoveData()
    {
        Sentinel.Forget();
        HomeBaseline.Forget();
        new AnswerLedger().Forget();
        DataNote = "Erased: Sentinel baseline, home baseline, and the passive-DNS ledger.";
    }

    private static string Describe(EncryptedDNSApplyResult r, bool turningOn) => r.Kind switch
    {
        EncryptedDNSApplyKind.Enabled => "Encrypted: every system DNS query is going over the chosen transport.",
        EncryptedDNSApplyKind.ElevationRequired =>
            "Changing system DNS needs root. The setting is saved; run Syxx with privilege (pkexec/sudo) to apply it.",
        // Defect-2: EncryptedDNSApplyResult.Reason is set verbatim from ex.Message in the core
        // (Syxx.Kit/EncryptedDNSConfigurator.cs:49), so it can be crash-reporter material. Route it
        // through the presenter, and drop the ": <reason>" clause entirely when there is nothing
        // readable to put there — the previous "(r.Reason ?? "")" produced a dangling
        // "Could not enable: . Your DNS was left unchanged." whenever the reason was absent.
        // Character-for-character parity with Windows SettingsView.xaml.cs's Describe/Clause.
        EncryptedDNSApplyKind.Inconclusive =>
            "Saved, but not yet confirmed encrypted"
            + Clause(r.Reason) + ". Treated as NOT protected until confirmed.",
        _ => turningOn
            ? "Could not enable" + Clause(r.Reason) + ". Your DNS was left unchanged."
            : "Encryption is off.",
    };

    /// ": &lt;readable reason&gt;" when the core supplied one a person can act on, otherwise nothing.
    private static string Clause(string? reason)
    {
        var shown = SyxxUI.Presentation.ErrorPresenter.DisplayReason(reason);
        return shown is null ? "" : ": " + shown.TrimEnd('.');
    }
}

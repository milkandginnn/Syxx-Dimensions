using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using SyxxUI.Presentation;
using SyxxUI.ViewModels;
using Syxx.Kit;

namespace SyxxUI.Views;

/// <summary>
/// Cage (DNS capture) surface — Windows twin of macOS CageView.swift.
///
/// The Windows AppViewModel exposes only passive Cage state (CageCapturing, CageEventsCount,
/// CageRows); there is no GUI command to start a live capture, because packet capture on Windows
/// needs elevation and is driven from the CLI (`syxx cage`). This view renders the real current
/// state honestly — the live status pill, the events counter, and the captured-query grid — and
/// directs the user to the command line for the capture itself. It never fabricates a running
/// capture or a captured-query result.
/// </summary>
public sealed partial class CageView : Page
{
    public ObservableCollection<CageDisplayRow> CaptureRows { get; } = [];

    private AppViewModel? _vm;

    public CageView()
    {
        InitializeComponent();
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

    private void Refresh()
    {
        if (_vm is null) return;

        // Live status pill: CAPTURING (red) vs IDLE (neutral). Never green.
        CapturingPill.Visibility = _vm.CageCapturing ? Visibility.Visible : Visibility.Collapsed;
        IdlePill.Visibility = _vm.CageCapturing ? Visibility.Collapsed : Visibility.Visible;

        EventsCountText.Text = _vm.CageEventsCount.ToString();

        // Captured-query rows, with the severity accent + badge routed through the shared
        // SeverityBrush source (never a new independent mapping). Empty until a capture populates
        // them; the empty notice stays visible so the pane never implies a clean, finished result.
        CaptureRows.Clear();
        foreach (var r in _vm.CageRows)
            CaptureRows.Add(new CageDisplayRow(r));

        CaptureEmptyText.Visibility = _vm.CageRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // MARK: - B-5 GUI-driven capture (macOS AppModel.toggleCapture / startRealCapture twin).
    //
    // The privileged capture runs as the unified `syxx cage` CLI under a UAC prompt (an elevated
    // child cannot be launched in-process from an unelevated GUI). The CLI streams one JSON record
    // per captured packet to %LOCALAPPDATA%\Syxx\captures\ with per-line flush and FileShare.Read,
    // so this pane TAILS the newest capture file while the capture runs — the same
    // stream-and-tail architecture macOS uses with osascript. Rows come only from real records;
    // a declined UAC prompt, missing binary, or quiet network shows an honest message and nothing.

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _tailTimer;
    private System.Diagnostics.Process? _captureProc;
    private string? _tailPath;
    private long _tailOffset;
    private string _tailPartial = "";
    private DateTime _captureStart;
    // C058 + B190: raw records + per-domain OUTBOUND query timestamps (epoch seconds) retained across the
    // capture, mirroring macOS AppModel's `records`/`byDomain`. Feed the beacon cadence (B190) at the row
    // level and the attribution-completeness + full-cadence section input (C058/B190) when the capture ends.
    private readonly List<DNSCaptureRecord> _captureRecords = [];
    private readonly Dictionary<string, List<double>> _byDomain = [];

    private static string CapturesDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syxx", "captures");

    /// The unified syxx CLI, if present: ONLY next to the GUI exe (installed layout).
    /// D102: the %PATH% search was dropped — launching a PATH-resolved binary with
    /// UseShellExecute+runas is a user-to-admin escalation vector (attacker-writable PATH
    /// entry could ride the app's own consent prompt to Administrator).
    private static string? LocateCageBinary()
    {
        foreach (var name in new[] { "syxx.exe", "Syxx.exe" })
        {
            var local = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(local)) return local;
        }
        return null;
    }

    private void CaptureBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (_vm.CageCapturing) StopCapture(userRequested: true);
        else StartCapture();
    }

    private void StartCapture()
    {
        if (_vm is null) return;
        var bin = LocateCageBinary();
        if (bin is null)
        {
            // Honest unavailability, macOS locateCageBinary parity — never a fake running state.
            CaptureStatusText.Text = "Live capture is unavailable: the syxx capture binary isn't installed alongside the app. Run `syxx cage` from an elevated command prompt.";
            return;
        }

        _captureStart = DateTime.Now;
        _tailPath = null;
        _tailOffset = 0;
        _tailPartial = "";
        try
        {
            // D108: pass --out with OUR resolved captures directory so the elevated child writes
            // to the same profile we tail. Without this, UAC over-the-shoulder elevation runs the
            // child under a different token whose LocalApplicationData is the admin's profile.
            Directory.CreateDirectory(CapturesDir);
            // UseShellExecute + runas → the OS's own consent prompt, mirroring macOS's
            // "with administrator privileges". --keep retains the NDJSON stream we tail.
            _captureProc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = bin,
                Arguments = $"cage --duration {_vm.CageDuration} --keep --out \"{CapturesDir}\"",
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The user declined the elevation prompt. Say so; fabricate nothing.
            CaptureStatusText.Text = "Capture not started: administrator authorization was declined.";
            return;
        }

        _vm.CageRows.Clear();
        _captureRecords.Clear();
        _byDomain.Clear();
        _vm.CageEventsCount = 0;
        _vm.CageCapturing = true;
        // Do NOT name a port here. The capture is not bound to 53: a machine resolving over DoH
        // carries its DNS on 443, and a capture bound to a particular interface may differ again.
        // Asserting "port 53" is a claim this view has not established. macOS dropped the port for
        // exactly this reason (CageView.swift:139); this is the Windows twin of that string.
        CaptureStatusText.Text = "Listening for DNS traffic…";
        CaptureBtn.Content = "Stop Capture";
        CaptureBtn.Background = (Brush)Application.Current.Resources["IoCriticalRedBrush"];
        Refresh();

        _tailTimer = DispatcherQueue.CreateTimer();
        _tailTimer.Interval = TimeSpan.FromMilliseconds(500);
        _tailTimer.Tick += (_, _) => TailTick();
        _tailTimer.Start();
    }

    private void StopCapture(bool userRequested)
    {
        _tailTimer?.Stop();
        _tailTimer = null;
        // C058 + B190: commit the capture's exfil assessments + attribution completeness to the shared
        // SectionInputs before tearing down, so a later Conclusions synthesis is degraded (not silently
        // clean) on a mostly-unattributed capture and can surface a beaconing exfil channel.
        BuildCageSectionInput();
        if (_vm is not null) _vm.CageCapturing = false;
        CaptureBtn.Content = "Close the Cages";
        CaptureBtn.Background = (Brush)Application.Current.Resources["IoBlueBrush"];
        // An elevated child cannot be killed from this unelevated process; if it is still
        // running, say so instead of implying it stopped.
        // E142: a capture that produced nothing must state the outcome honestly — never clear to
        // an empty string that reads as idle. _tailPath staying null means the CLI never wrote a
        // file; zero records from a tailed file means a quiet network.
        if (userRequested && _captureProc is { HasExited: false })
        {
            CaptureStatusText.Text = "Stopped watching. The elevated capture window finishes on its own timer.";
        }
        else if (_tailPath is null)
        {
            CaptureStatusText.Text = "The capture produced no output file — it may not have started. This is not evidence of a quiet network.";
        }
        else if (_captureRecords.Count == 0)
        {
            var elapsed = (int)(DateTime.Now - _captureStart).TotalSeconds;
            CaptureStatusText.Text = $"Capture ran for {elapsed}s and observed no DNS. Confirm traffic was generated.";
        }
        else
        {
            CaptureStatusText.Text = userRequested
                ? "Capture stopped."
                : "Capture finished.";
        }
        _captureProc = null;
        Refresh();
    }

    private void TailTick()
    {
        if (_vm is null) { StopCapture(userRequested: false); return; }

        // Bind to the newest capture file the elevated CLI created after we launched it.
        if (_tailPath is null)
        {
            try
            {
                _tailPath = Directory.Exists(CapturesDir)
                    ? Directory.GetFiles(CapturesDir, "Syxx Cage Capture *.txt")
                        .Where(f => File.GetLastWriteTime(f) >= _captureStart.AddSeconds(-5))
                        .OrderByDescending(File.GetLastWriteTime)
                        .FirstOrDefault()
                    : null;
            }
            catch (IOException) { /* transient — retry next tick */ }
            catch (UnauthorizedAccessException) { /* transient — retry next tick */ }
        }

        if (_tailPath is not null)
        {
            try
            {
                using var fs = new FileStream(_tailPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length > _tailOffset)
                {
                    fs.Seek(_tailOffset, SeekOrigin.Begin);
                    using var reader = new StreamReader(fs);
                    _tailPartial += reader.ReadToEnd();
                    _tailOffset = fs.Length;

                    int nl;
                    while ((nl = _tailPartial.IndexOf('\n')) >= 0)
                    {
                        var line = _tailPartial[..nl].TrimEnd('\r');
                        _tailPartial = _tailPartial[(nl + 1)..];
                        if (line.Length == 0) continue;
                        AppendRecord(line);
                    }
                }
            }
            catch (IOException) { /* transient — retry next tick */ }
            catch (UnauthorizedAccessException) { /* file still root-owned — retry */ }
        }

        // End conditions: the elevated process exited, or the duration (plus grace) elapsed.
        bool timedOut = DateTime.Now > _captureStart.AddSeconds((_vm.CageDuration) + 20);
        if (_captureProc is { HasExited: true } || timedOut)
            StopCapture(userRequested: false);
    }

    private void AppendRecord(string jsonLine)
    {
        if (_vm is null) return;
        DNSCaptureRecord? rec;
        try { rec = JsonSerializer.Deserialize<DNSCaptureRecord>(jsonLine); }
        catch (JsonException) { return; }   // torn or foreign line — never a fabricated row
        if (rec is null) return;

        _captureRecords.Add(rec);   // retained for the C058/B190 section input built when the capture ends

        var q = rec.Packet.Questions.FirstOrDefault();
        string direction = rec.Network.Direction;
        string rcode = rec.Packet.Rcode;
        string proc = rec.Process.Name is { Length: > 0 } n
            ? (rec.Process.Pid is { } pid ? $"{n} · {pid}" : n)
            : "unknown";
        // The resolver is the port-53 side of the exchange.
        string resolver = direction == "Outbound Query" ? rec.Network.DestIP : rec.Network.SourceIP;
        string domain = q?.QuestionName is { Length: > 0 } d ? d : "unknown";

        // B190: accumulate this record's per-domain OUTBOUND query time (epoch seconds) so the row —
        // and the section input below — evaluate the beacon cadence instead of the dead empty-timestamp
        // path that made LikelyExfil structurally unreachable and scored beaconing channels as benign.
        if (direction.Contains("outbound", StringComparison.OrdinalIgnoreCase) && domain != "unknown")
        {
            double? ts = ParseCaptureSeconds(rec.Timestamp);
            if (ts is double t)
            {
                if (!_byDomain.TryGetValue(domain, out var series)) { series = []; _byDomain[domain] = series; }
                series.Add(t);
            }
        }

        // C059 + B190: route the row badge through the REAL ExfilAnalyzer assessment (entropy / blocklist /
        // per-domain cadence), exactly as macOS AppModel.cageRow does — never the constant Severity.Info this
        // used to hardcode. The tri-state mapping matches macOS: LikelyExfil→Critical, Suspicious→Warning,
        // Benign→Info, Inconclusive→Unknown (a "not judged" state, muted grey — never green, never info-purple).
        var assessment = ExfilAnalyzer.Assess(
            domain, _byDomain.TryGetValue(domain, out var known) ? known : null);
        Severity severity = assessment.Suspicion switch
        {
            ExfilSuspicion.LikelyExfil => Severity.Critical,
            ExfilSuspicion.Suspicious  => Severity.Warning,
            ExfilSuspicion.Benign      => Severity.Info,
            _                          => Severity.Unknown,   // Inconclusive → not-judged, never a reassuring INFO
        };

        string note = $"{direction} · {(rcode == "NoError" ? "No error" : rcode)}";
        if (assessment.Suspicion is ExfilSuspicion.LikelyExfil or ExfilSuspicion.Suspicious)
            note += $" — {(assessment.Rationale.Count > 0 ? assessment.Rationale[0] : assessment.Suspicion.Label())}";

        _vm.CageRows.Insert(0, new CageRow(
            Guid.NewGuid(),
            rec.Timestamp.Length >= 19 ? rec.Timestamp.Substring(11, 8) : rec.Timestamp,
            domain,
            note,
            proc,
            resolver,
            severity));
        _vm.CageEventsCount = _vm.CageRows.Count;
        Refresh();
    }

    /// C058 + B190: build the Cage section input from the retained capture records when the capture ends,
    /// mirroring macOS AppModel's completion path so two signals the row grid alone would discard reach
    /// synthesis: per-domain OUTBOUND beacon cadence (B190) and process-attribution completeness (C058).
    /// Reuses the canonical Kit grouping (MonitorReportAnalyzer.SectionInputsFrom → Exfil) so the GUI and
    /// log-analysis paths cannot drift. A zero-record / no-outbound capture leaves Exfil null so Cage stays
    /// UNTESTED rather than falsely clean. The Conclusions surface re-reads SectionInputs when it synthesises.
    private void BuildCageSectionInput()
    {
        if (_vm is null) return;
        // H2 — always (re)assign, mirroring macOS AppModel.swift:1095-1102. An empty capture must clear
        // Exfil/CageAttributionRate to null so a prior run's stale evidence cannot linger and be
        // synthesised as this run's clean; a zero-record capture then reads UNTESTED, never falsely clean.
        if (_captureRecords.Count == 0)
        {
            _vm.SectionInputs.Exfil = null;
            _vm.SectionInputs.CageAttributionRate = null;
            return;
        }
        _vm.SectionInputs.Exfil = MonitorReportAnalyzer.SectionInputsFrom(_captureRecords).Exfil;
        _vm.SectionInputs.CageAttributionRate = AttributionRate(_captureRecords);
    }

    /// C058 — fraction (0..1) of capture records that carried a real process identity (IsAttributed), or
    /// null when there were none. Computed over every record (both directions) to match the Synthesizer.Cage
    /// contract wording; inbound responses are frequently unattributable, biasing the rate toward degraded —
    /// the honest-safe direction. Twin of macOS AppModel.attributionRate(from:).
    private static double? AttributionRate(IReadOnlyList<DNSCaptureRecord> records)
    {
        if (records.Count == 0) return null;
        int attributed = records.Count(r => r.Process.IsAttributed);
        return (double)attributed / records.Count;
    }

    /// "yyyy-MM-dd HH:mm:ss.fff" (or without millis) capture timestamp → Unix epoch seconds for the beacon
    /// cadence, matching MonitorReportAnalyzer.ParseTimestamp's format + epoch conversion. Null on an
    /// unparseable stamp, so a bad line contributes no cadence sample rather than a fabricated one.
    private static double? ParseCaptureSeconds(string s)
    {
        if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            || DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return (dt - DateTime.UnixEpoch).TotalSeconds;
        return null;
    }
}

/// A captured-query row prepared for display. Severity colour comes from the single SeverityBrush
/// source; the badge background is a faint tint of that same colour.
public sealed class CageDisplayRow
{
    public string Time { get; }
    public string Domain { get; }
    public string Note { get; }
    public string Process { get; }
    public string Resolver { get; }
    public string SeverityLabel { get; }
    public Brush AccentBrush { get; }
    public Brush BadgeBackground { get; }
    public Visibility NoteVisibility { get; }

    public CageDisplayRow(CageRow row)
    {
        Time = row.Time;
        Domain = row.Domain;
        Note = row.Note;
        Process = row.Process;
        Resolver = row.Resolver;
        SeverityLabel = row.Severity.ToString().ToUpperInvariant();
        AccentBrush = SeverityBrush.Of(row.Severity);
        NoteVisibility = string.IsNullOrEmpty(row.Note) ? Visibility.Collapsed : Visibility.Visible;

        var c = (Color)Application.Current.Resources[ColorKeyFor(row.Severity)];
        BadgeBackground = new SolidColorBrush(Color.FromArgb((byte)(0.15 * 255), c.R, c.G, c.B));
    }

    private static string ColorKeyFor(Severity s) => s switch
    {
        Severity.Clean    => "IoSuccessGreenColor",
        Severity.Info     => "IoInfoPurpleColor",
        Severity.Warning  => "IoWarningAmberColor",
        Severity.High     => "IoHighOrangeColor",
        Severity.Critical => "IoCriticalRedColor",
        _                 => "IoTextSecondaryColor",
    };
}

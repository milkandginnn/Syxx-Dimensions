using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Syxx.Kit;
using SyxxUI.ViewModels;

namespace SyxxUI.Views;

/// <summary>
/// Witness (signed resolution) surface — Windows twin of macOS WitnessView.swift.
///
/// HONESTY GATE (B049 / B053 / C028 / B063): the validation result is computed here from the
/// REAL verification result. There is deliberately no static "signature valid" element in the
/// XAML. A check that was not run, has no signed testimony, has no signing key, or failed
/// verification must render as a caution ("[NOT VERIFIED]"), visually and textually distinct from
/// a green valid pass and from a definitive tamper accusation. Only WitnessValid == true (with a
/// snapshot and a key present) renders green.
/// </summary>
public sealed partial class WitnessView : Page
{
    private AppViewModel? _vm;

    public WitnessView()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm = e.Parameter as AppViewModel;
        if (_vm is not null) DomainBox.Text = _vm.Domain;
        // A key saved in a previous session (or by a re-navigate after external DPAPI-store
        // change) must be reflected immediately, not just after an explicit re-save this visit.
        _vm?.RefreshWitnessKeyStatus();
        Refresh();
    }

    private async void TestifyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Domain = DomainBox.Text;
        SaveKeyBoxToViewModel();
        // WitnessTestify now performs a live network probe (D051) — disable the button for the
        // duration so a second click cannot start a concurrent sealing attempt.
        TestifyBtn.IsEnabled = false;
        try
        {
            await _vm.WitnessTestifyCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            // E147: an unhandled exception in an async void handler kills the process.
            System.Diagnostics.Debug.WriteLine($"[Syxx] Witness testify failed: {ex}");
        }
        finally
        {
            TestifyBtn.IsEnabled = true;
        }
        Refresh();
    }

    private void VerifyBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.Domain = DomainBox.Text;
        SaveKeyBoxToViewModel();
        _vm.WitnessVerifyCommand.Execute(null);
        Refresh();
    }

    private void FreeBtn_Click(object sender, RoutedEventArgs e)
    {
        _vm?.WitnessFreeCommand.Execute(null);
        Refresh();
    }

    // E045: generate a new Ed25519 keypair and store the private key via DPAPI.
    // Parity with macOS generateWitnessKey() which calls WitnessKeyManager.generateAndStoreKey().
    private void GenerateKeyBtn_Click(object sender, RoutedEventArgs e)
    {
        _vm?.GenerateWitnessKey();
        Refresh();
    }

    /// Saves the key box contents into the view-model, then clears the box once the save is
    /// confirmed to have actually taken (a key now present on disk and no reported error) — a
    /// signing secret should not linger, readable, in a UI control after it has been persisted.
    /// Leaves the box untouched (nothing to clear, nothing to hide) when it was empty or the save
    /// failed, so the user can see and retry what they typed.
    private void SaveKeyBoxToViewModel()
    {
        if (_vm is null) return;
        var keyInput = KeyBox.Password;
        _vm.SaveWitnessKey(keyInput);
        // Ask the FAILURE flag, not the detail string. Defect-2 made WitnessKeyError the presented
        // cause, which is legitimately empty when the exception carried no readable one — reading
        // emptiness as success here would clear the user's typed key after a save that failed.
        if (!string.IsNullOrEmpty(keyInput) && _vm.HasWitnessKey && !_vm.WitnessKeySaveFailed)
            KeyBox.Password = string.Empty;
    }

    private void Refresh()
    {
        if (_vm is null) return;

        // D052 (Windows half): a failed DPAPI key save is a real, distinct failure — surfaced here
        // instead of being silently discarded. Approved wording (owner, 2026-07-22): "Could not save
        // the signing key" (not "to the Keychain" — Windows uses DPAPI, not a Keychain; the two
        // platforms' strings are intentionally allowed to differ on this point).
        //
        // Defect-2: visibility is driven by WitnessKeySaveFailed (did it fail?), NOT by whether the
        // detail string is empty. The detail is now ErrorPresenter output and is empty whenever the
        // exception carried no readable cause; a failure with no cause must still render as a
        // failure, stated bare, never suppressed into a silent success.
        if (!_vm.WitnessKeySaveFailed)
        {
            KeyErrorText.Visibility = Visibility.Collapsed;
            KeyErrorText.Text = string.Empty;
        }
        else
        {
            KeyErrorText.Visibility = Visibility.Visible;
            KeyErrorText.Text = string.IsNullOrEmpty(_vm.WitnessKeyError)
                ? "Could not save the signing key."
                : $"Could not save the signing key: {_vm.WitnessKeyError}";
        }

        // Testimony panel — the "creating testimony for … via …" line + the signature, shown as-is
        // (or an honest "not yet signed"). Parity with macOS WitnessView.
        // E140: never fabricate a domain or resolver. When Domain is empty, hide the line.
        // Take the resolver from the sealed snapshot once one exists; fall back to the live
        // system only for the pre-seal prompt, labelled "will use".
        TestimonyPanel.Visibility = _vm.ShowTestimony ? Visibility.Visible : Visibility.Collapsed;
        if (string.IsNullOrEmpty(_vm.Domain))
        {
            CreatingTestimonyText.Text = "Enter a domain to witness.";
        }
        else
        {
            var domain = _vm.Domain;
            // Use the sealed snapshot's resolver when available — it is the resolver the testimony
            // was actually taken through. Only fall back to live system for pre-seal, labelled.
            var snapResolver = _vm.WitnessSnapshot?.Comparison?.LocalServer;
            var resolver = !string.IsNullOrEmpty(snapResolver)
                ? snapResolver
                : (NetworkKeyState.GetSystemDNSServers().FirstOrDefault() ?? "system default") + " (will use)";
            CreatingTestimonyText.Text = $"Creating signed Witness Testimony for {domain} via {resolver}";
        }
        SignatureText.Text = string.IsNullOrEmpty(_vm.WitnessSignature)
            ? "Not yet signed"
            : _vm.WitnessSignature;

        // B195: a dead/aborted sealing probe renders as a DISTINCT failed row, never the pending
        // "Not yet signed" placeholder a still-running probe shows. Parity with macOS WitnessView's
        // `.failed` branch. The two states are mutually exclusive.
        var failed = _vm.WitnessTestimony == WitnessTestimonyState.Failed;
        TestimonyPendingPanel.Visibility = failed ? Visibility.Collapsed : Visibility.Visible;
        TestimonyFailurePanel.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        TestimonyFailureText.Text = _vm.WitnessTestimonyMessage;

        // Validation panel — tri-state result, never a hardcoded pass.
        ValidationPanel.Visibility = _vm.ShowValidation ? Visibility.Visible : Visibility.Collapsed;
        if (_vm.ShowValidation) UpdateValidation();
    }

    private void UpdateValidation()
    {
        if (_vm is null) return;

        // Real snapshot metadata (neutral color — this is context, not a result). Parity with
        // macOS: Domain + Timestamp.
        ValidationMetaPanel.Children.Clear();
        var snap = _vm.WitnessSnapshot;
        AddMetaRow("Domain", string.IsNullOrEmpty(snap?.TargetDomain) ? _vm.Domain : snap!.TargetDomain);
        if (snap is not null)
            AddMetaRow("Timestamp", snap.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));

        // The one and only result — single honest line + seal icon, driven entirely by real
        // view-model state. Wording and colour mirror macOS WitnessView.witnessResultDisplay
        // EXACTLY (D069/B049/E021): every failure reason is distinct, tamper/forgery states render
        // critical red, freshness/trust/keyless states render amber, and only a real valid seal
        // renders green — a checkmark on an unverified snapshot is exactly the false-confidence this
        // surface refuses. A CheckMark glyph for valid, a Cancel glyph for every non-valid state.
        const string checkGlyph = "";   // CheckMark (≈ checkmark.seal.fill)
        const string xGlyph = "";        // Cancel    (≈ xmark.seal.fill)

        string glyph, text, colorKey;

        if (snap is null)
        {
            // No snapshot to compare — WitnessVerify() was never given evidence to check. This is
            // a fact about WitnessSnapshot itself (kept in lockstep by WitnessTestify/WitnessFree),
            // not an independently re-derived flag.
            (glyph, colorKey, text) = (xGlyph, "IoWarningAmberBrush",
                "No signed snapshot available for comparison — press Witness Testimony first.");
        }
        else if (_vm.WitnessValid)
        {
            (glyph, colorKey, text) = (checkGlyph, "IoSuccessGreenBrush",
                "Testimony validated — signature matches; no tampering detected.");
        }
        else if (_vm.WitnessVerification is null)
        {
            // A snapshot exists but WitnessVerify() produced no detailed forensic outcome. Given a
            // non-null snapshot, that combination is only reached by WitnessVerify()'s missing-key
            // guard (AppViewModel.cs) — single-sourced from the verify outcome itself, not from a
            // separately re-checked (and previously stale) HasWitnessKey flag.
            (glyph, colorKey, text) = (xGlyph, "IoWarningAmberBrush",
                "Cannot verify — no signing key on this machine. Enter your key above, then verify again.");
        }
        else
        {
            (glyph, colorKey, text) = _vm.WitnessVerification switch
            {
                SnapshotVerification.Expired => (xGlyph, "IoWarningAmberBrush",
                    "Signature valid but the snapshot has expired — the evidence is genuine but no longer current."),
                SnapshotVerification.Stale => (xGlyph, "IoWarningAmberBrush",
                    "Signature valid but the snapshot is too old — evidence freshness threshold exceeded."),
                SnapshotVerification.FutureDated => (xGlyph, "IoCriticalRedBrush",
                    "Signature valid but the timestamp is future-dated — possible clock manipulation or forged evidence."),
                SnapshotVerification.UntrustedSigner => (xGlyph, "IoCriticalRedBrush",
                    "Signature valid but the signer is not in your trust set — evidence may be authentic but is unattested."),
                SnapshotVerification.DisplayMismatch => (xGlyph, "IoCriticalRedBrush",
                    "Signature valid but display fields do not match the signed evidence — presentation was tampered."),
                SnapshotVerification.InvalidSignature => (xGlyph, "IoCriticalRedBrush",
                    "Testimony invalid — signature mismatch; the snapshot was altered after signing."),
                SnapshotVerification.MalformedInput => (xGlyph, "IoCriticalRedBrush",
                    "Cannot verify — the snapshot is malformed or the public key is missing."),
                SnapshotVerification.UnsupportedFormat => (xGlyph, "IoCriticalRedBrush",
                    "Cannot verify — the snapshot format version is not supported by this verifier."),
                _ => (xGlyph, "IoCriticalRedBrush",
                    "Cannot verify — the snapshot is malformed or the public key is missing."),
            };
        }

        var brush = (Brush)Application.Current.Resources[colorKey];
        ResultIcon.Glyph = glyph;
        ResultIcon.Foreground = brush;
        ResultText.Text = text;
        ResultText.Foreground = brush;
    }

    private void AddMetaRow(string label, string value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock
        {
            Text = label + ":",
            FontFamily = (FontFamily)Application.Current.Resources["IoFontGrotesk"],
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["IoTextSecondaryBrush"]
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            FontFamily = (FontFamily)Application.Current.Resources["IoFontMono"],
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["IoTextBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        ValidationMetaPanel.Children.Add(row);
    }
}

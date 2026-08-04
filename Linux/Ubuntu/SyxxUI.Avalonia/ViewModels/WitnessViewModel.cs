using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Syxx.Kit;
using SyxxUI.Presentation;   // linked from Windows/dotNET/SyxxUI/Presentation/ErrorPresenter.cs

namespace SyxxUI.Avalonia.ViewModels;

/// <summary>
/// B195: the honest lifecycle state of the Witness Testimony (sealing) action. Parity twin of
/// macOS AppModel.WitnessTestimonyState and Windows AppViewModel.WitnessTestimonyState — this was
/// the reference shape (see DELTAS.md B195); macOS/Windows were retrofitted to match Ubuntu's
/// distinct-failure-message behavior, and this enum makes that state machine explicit here too
/// instead of leaving it implicit in the IsRunning/HasResult bool pair. As on Windows, the reason
/// travels in <see cref="WitnessViewModel.SignatureLine"/> rather than as an enum payload (C#
/// enums carry no associated value) — SignatureLine already carried the distinct failure/success
/// text for every branch before this change.
/// </summary>
public enum WitnessTestimonyState
{
    Idle,
    Running,
    Signed,
    Failed
}

/// <summary>
/// Witness — can the evidence itself be trusted? Signs a resolution of the domain with a persistent
/// Ed25519 key and verifies the seal. The crypto (NSec) is cross-platform, so signing AND
/// verification genuinely work on Linux.
///
/// HONESTY: the signature is over the REAL comparison result; a run that could not resolve is not
/// signed as if it were. The verify result is the full forensic tri-state, so an expired or
/// untrusted seal never reads the same as a valid one. The signing key is PERSISTED across launches
/// via the shared <see cref="WitnessKeyStore"/>, backed on Linux by <see cref="LinuxSecretStore"/>
/// (system keyring, or an encrypted file when the keyring is unavailable) — so the same identity
/// signs every testimony, and only the PUBLIC identity (public key + key id) is ever displayed; the
/// private key never appears on screen.
///
/// The same forensic verification also runs over an externally-supplied testimony via
/// <see cref="VerifyExternalCommand"/> (paste JSON), driven through the identical severity switch as
/// self-verify so a third party's seal is judged by the same rules.
/// </summary>
public partial class WitnessViewModel : ObservableObject
{
    [ObservableProperty] private string _domain = "";

    // B195: TestimonyState is now the source of truth for the run/result lifecycle; IsRunning and
    // HasResult below are derived from it so existing view bindings (IsEnabled="{Binding
    // !IsRunning}", IsVisible="{Binding HasResult}") keep working unchanged.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private WitnessTestimonyState _testimonyState = WitnessTestimonyState.Idle;

    [ObservableProperty] private string _signatureLine = "";

    /// The PUBLIC signing identity (public key + key id) shown after a successful seal. Never holds
    /// the private key — the secret stays inside <see cref="WitnessKeyStore"/> / the platform secret
    /// store and is never surfaced to the view.
    [ObservableProperty] private string _identityLine = "";

    [ObservableProperty] private string _verifyLine = "";
    [ObservableProperty] private IBrush _verifyBrush = Brushes.Gray;

    /// The testimony JSON a user pastes to verify a seal made elsewhere (drives
    /// <see cref="VerifyExternalCommand"/>). Kept separate from the self-verify path; both render
    /// into the shared <see cref="VerifyLine"/> / <see cref="VerifyBrush"/>.
    [ObservableProperty] private string _externalSnapshotJson = "";

    /// D105: the answer-redirection drift implied by the SAME comparison that was just signed —
    /// <c>null</c> when no redirection was detected, otherwise the <see cref="AnswerDrift"/> derived
    /// through the shared Syxx.Kit <see cref="WitnessTestifyPlanner"/>, the single source macOS
    /// (AppModel) and Windows (AppViewModel) already route their <c>SectionInputs.Drifts</c> through.
    /// Populated only on a successful seal; reset to <c>null</c> at the start of every attempt and left
    /// <c>null</c> on every failure branch. Not yet rendered — the Linux client has no drift-consuming
    /// Conclusions surface yet (DELTAS.md D105) — but computed and available so that when one is built
    /// it cannot silently drop the drift the way Windows once did (the D093 bug class).
    public AnswerDrift? Drift { get; private set; }

    /// True only while a sign attempt is in flight. Derived from <see cref="TestimonyState"/>.
    public bool IsRunning => TestimonyState == WitnessTestimonyState.Running;

    /// True once a sign attempt has concluded, successfully (Signed) or not (Failed). Derived from
    /// <see cref="TestimonyState"/> — a Failed outcome still HAS a result (the failure message) and
    /// must not fall back to the pending "not yet run" appearance (B195).
    public bool HasResult => TestimonyState is WitnessTestimonyState.Signed or WitnessTestimonyState.Failed;

    public string PersistenceNote =>
        "Your signing key is stored securely on this machine (system keyring, or an encrypted file "
        + "if the keyring is unavailable). It never leaves this device, and the same identity signs "
        + "every testimony.";

    // Test seam (Problem 1, DELTAS B195 follow-up): SignAsync's two external calls — resolving the
    // live comparison and signing the snapshot — are injected via delegates so unit tests can force
    // each failure branch deterministically, without a real network resolve or NSec keypair. The
    // parameterless constructor (used by WitnessView) wires the real Syxx.Kit implementations, so
    // production behavior is unchanged.
    private readonly Func<string, string, bool, Task<SectionInputs>> _runMirror;
    private readonly Func<string, ComparisonResult, string, WitnessSnapshot> _createSnapshot;

    // G16 / #21 persistence seam: returns the base64 Ed25519 PRIVATE key to sign with, loading the
    // persisted key or minting-and-persisting one on first use. Production (the parameterless ctor)
    // wires this to the shared WitnessKeyStore, so the SAME identity signs every launch. Injectable
    // so tests exercise persistence WITHOUT touching the real on-disk key file — see the note on the
    // test-seam constructor. Nothing here ever hands the private key to the view.
    private readonly Func<string> _loadOrCreateSigningKey;

    public WitnessViewModel() : this(
        (domain, resolver, isPro) => ProbePlan.RunMirrorAsync(domain, resolver, isPro),
        (domain, comparison, privateKey) => WitnessSigner.CreateSignedSnapshot(domain, comparison, privateKey),
        LoadOrCreatePersistentSigningKey)
    {
    }

    /// <param name="loadOrCreateSigningKey">
    /// Optional signing-key source. Left null, tests get a fresh EPHEMERAL in-memory keypair per
    /// construction (identical to this ViewModel's pre-persistence behavior) so failure-branch tests
    /// never write a real key file. The <see cref="WitnessKeyStore.KeyPath"/> file location is NOT
    /// injectable and resolves to the real per-user data dir, so persistence tests inject a closure
    /// that simulates a persisted store rather than exercising the on-disk path directly.
    /// </param>
    public WitnessViewModel(
        Func<string, string, bool, Task<SectionInputs>> runMirror,
        Func<string, ComparisonResult, string, WitnessSnapshot> createSnapshot,
        Func<string>? loadOrCreateSigningKey = null)
    {
        _runMirror = runMirror;
        _createSnapshot = createSnapshot;
        _loadOrCreateSigningKey = loadOrCreateSigningKey
            ?? (() => WitnessSigner.GenerateKeyPair().PrivateKey);
    }

    /// Production key source: load the persisted Ed25519 private key, or mint one and persist it via
    /// the shared <see cref="WitnessKeyStore"/> (LinuxSecretStore-backed on Linux). Load-or-create so
    /// the first sign establishes a durable identity and every later launch reuses it.
    private static string LoadOrCreatePersistentSigningKey()
    {
        var priv = WitnessKeyStore.HasKey ? WitnessKeyStore.LoadKey() : null;
        if (priv is null)
        {
            (priv, _) = WitnessSigner.GenerateKeyPair();
            WitnessKeyStore.SaveKey(priv);
        }
        return priv;
    }

    // Forensic-severity brushes shared by self-verify and external verify. 3-tier, parity with the
    // macOS twin: Valid → green; an honest-but-stale/expired seal → amber (not forged, just old); a
    // forged / future-dated / display-tampered / untrusted seal → critical red (App.axaml
    // SyxRedColor #FF4D6D). Anything unreadable folds into the critical tier — a seal we cannot read
    // is never shown as merely cautionary.
    private static readonly IBrush ValidBrush = new SolidColorBrush(Color.Parse("#3FD08A"));
    private static readonly IBrush CautionBrush = new SolidColorBrush(Color.Parse("#F2B33D"));
    private static readonly IBrush CriticalBrush = new SolidColorBrush(Color.Parse("#FF4D6D"));

    // Canonical snapshot reader options — the SAME contract the CLI verify path and the golden-vector
    // tests use (WitnessCommand.cs / WitnessCrossPlatformGoldenVectorTests.cs). The date/status wire
    // converters are attribute-bound on the types, so no extra registration is needed here.
    private static readonly JsonSerializerOptions ExternalVerifyReaderOptions =
        new() { PropertyNameCaseInsensitive = true };

    [RelayCommand]
    private async Task SignAsync()
    {
        if (IsRunning || string.IsNullOrWhiteSpace(Domain)) return;
        TestimonyState = WitnessTestimonyState.Running;
        SignatureLine = "Resolving and sealing…"; VerifyLine = ""; IdentityLine = ""; Drift = null;

        var servers = NetworkKeyState.GetSystemDNSServers();
        if (servers.Count == 0)
        {
            // D278 parity (macOS AppModel): never fabricate a resolver. A refused seal renders as
            // Failed (never Signed), so the run can never be mistaken for a completed, clean
            // testimony — same state discipline as the catch branches below.
            SignatureLine = "System resolver could not be determined. Check your network connection, then retry.";
            TestimonyState = WitnessTestimonyState.Failed;
            return;
        }

        string local = servers[0];

        SectionInputs r;
        try { r = await _runMirror(Domain.Trim(), local, false); }
        // Defect-2 parity: raw detail to the diagnostic sink, readable text (or the bare failure,
        // with no invented cause) to the screen. TestimonyState carries the failure, so an empty
        // detail can never be mistaken for a seal that succeeded.
        catch (System.Exception ex)
        {
            SignatureLine = ErrorPresenter.Compose(
                "Could not resolve to witness.", ErrorPresenter.UserDetail(ex, "witness.vantage"));
            TestimonyState = WitnessTestimonyState.Failed; return;
        }

        var comparison = r.Comparison;
        if (comparison is null)
        {
            // No real evidence to seal — do NOT sign an empty result as testimony.
            SignatureLine = "Nothing to witness — the domain did not resolve, so there is no result to seal.";
            VerifyLine = "";
            TestimonyState = WitnessTestimonyState.Failed;
            return;
        }

        // G16 / #21: sign with the PERSISTENT identity (load-or-create), not a fresh throwaway key.
        // A failure to reach the key store is an honest Failed outcome, never a silent fall-through
        // to an unsigned or ephemerally-signed seal.
        string priv;
        try { priv = _loadOrCreateSigningKey(); }
        catch (System.Exception ex)
        {
            SignatureLine = ErrorPresenter.Compose(
                "Could not access the signing key.", ErrorPresenter.UserDetail(ex, "witness.key"));
            TestimonyState = WitnessTestimonyState.Failed; return;
        }

        WitnessSnapshot snap;
        try { snap = _createSnapshot(Domain.Trim(), comparison, priv); }
        // Defect-2 parity: WitnessSignerException's default message is a namespace-qualified type
        // dump, exactly the crash-reporter material this boundary exists to keep off the screen.
        catch (System.Exception ex)
        {
            SignatureLine = ErrorPresenter.Compose(
                "Signing failed.", ErrorPresenter.UserDetail(ex, "witness.sign"));
            TestimonyState = WitnessTestimonyState.Failed; return;
        }

        SignatureLine = $"Sealed. Signature: {Truncate(snap.Signature, 44)}";
        // Show the PUBLIC identity only — anyone can verify with it, and it discloses nothing secret.
        // The private key never reaches the view (it stays inside WitnessKeyStore / the secret store).
        IdentityLine = $"Signing identity (public): {snap.PublicKey} · key {snap.KeyId}";

        // D105: derive the redirection drift from the SAME comparison, single-sourced through the
        // shared Syxx.Kit WitnessTestifyPlanner — exactly the step macOS (AppModel.swift) and Windows
        // (AppViewModel.cs) run off the planner's Outcome. Signing stays behind the injected seam above
        // (so the offline sign-throws test still fires); only the drift step, the block D093 silently
        // dropped, is routed through the planner here. On this Linux client the drift is only STORED for
        // now (no Conclusions surface consumes it yet), but it EXISTS and is populated identically to
        // the other two twins instead of being absent.
        Drift = WitnessTestifyPlanner.DriftIfRedirected(Domain.Trim(), local, comparison);

        // Verify the seal we just made — the full forensic tri-state, through the SAME switch the
        // paste-JSON external verify uses.
        ApplyVerification(WitnessSigner.VerifySnapshotDetailed(snap));

        TestimonyState = WitnessTestimonyState.Signed;
    }

    /// <summary>
    /// #22: verify an externally-supplied testimony (paste the snapshot JSON). Deserializes with the
    /// canonical reader contract and routes the outcome through the identical severity switch as
    /// self-verify, so a third party's seal is judged by exactly the same rules and rendered into the
    /// shared <see cref="VerifyLine"/> / <see cref="VerifyBrush"/>. Synchronous: parsing and Ed25519
    /// verification are fast and CPU-bound, matching the inline self-verify above (no await work).
    /// </summary>
    [RelayCommand]
    private void VerifyExternal()
    {
        if (string.IsNullOrWhiteSpace(ExternalSnapshotJson))
        {
            VerifyLine = "Paste a signed testimony (JSON) above, then verify it.";
            VerifyBrush = CautionBrush;
            return;
        }

        WitnessSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<WitnessSnapshot>(
                ExternalSnapshotJson, ExternalVerifyReaderOptions);
        }
        catch (JsonException)
        {
            // Unreadable input is a critical-tier outcome, not a cautionary one: we render it as
            // MalformedInput rather than guessing a verdict for bytes we could not parse.
            ApplyVerification(SnapshotVerification.MalformedInput);
            return;
        }

        ApplyVerification(snapshot is null
            ? SnapshotVerification.MalformedInput
            : WitnessSigner.VerifySnapshotDetailed(snapshot));
    }

    /// The single source of the verification message + severity brush, shared by self-verify and
    /// external verify so the two paths can never drift into different words or colors for the same
    /// outcome. Every non-Valid state is forensically distinct (E021) and MUST NOT collapse.
    /// <c>internal</c> so the test assembly can assert the full severity matrix directly, including
    /// the states (Stale, FutureDated, UntrustedSigner) that are awkward to reach end-to-end.
    internal void ApplyVerification(SnapshotVerification result)
    {
        VerifyLine = result switch
        {
            SnapshotVerification.Valid => "Verified: the seal is valid and the testimony is intact.",
            SnapshotVerification.Expired => "Expired: an honest but out-of-date seal (not forged).",
            SnapshotVerification.Stale => "Stale: older than the freshness window.",
            SnapshotVerification.FutureDated => "Future-dated: rejected (clock skew beyond tolerance).",
            SnapshotVerification.UntrustedSigner => "Signature valid, but the signer is not in the trust set.",
            SnapshotVerification.DisplayMismatch => "Display mismatch: the shown values do not match the signed bytes.",
            SnapshotVerification.InvalidSignature => "INVALID signature: the seal does not verify (tampering).",
            SnapshotVerification.UnsupportedFormat => "Unsupported format: this testimony version cannot be read.",
            SnapshotVerification.MalformedInput => "Could not read that as a signed testimony (malformed input).",
            _ => "Could not verify (inconclusive)."
        };
        VerifyBrush = result switch
        {
            SnapshotVerification.Valid => ValidBrush,
            SnapshotVerification.Expired or SnapshotVerification.Stale => CautionBrush,
            _ => CriticalBrush,
        };
    }

    private static string Truncate(string? s, int n) =>
        string.IsNullOrEmpty(s) ? "(none)" : (s.Length <= n ? s : s.Substring(0, n) + "…");
}

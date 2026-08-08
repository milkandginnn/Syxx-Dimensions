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
/// private key is masked with a password character in the import field and never displayed in cleartext.
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
    /// <see cref="VerifyExternalCommand"/>). Kept separate from the self-verify path.
    [ObservableProperty] private string _externalSnapshotJson = "";

    /// G-14: external-verify result message, rendered in its own TextBlock below the verify button
    /// so that a user who only opens the app to check someone else's testimony sees the conclusion
    /// without needing to trigger a self-sign first. Separate from <see cref="VerifyLine"/> so the
    /// two output sinks cannot overwrite each other (also helps with G-12 stale-snapshot concerns).
    [ObservableProperty] private string _externalVerifyLine = "";
    [ObservableProperty] private IBrush _externalVerifyBrush = Brushes.Gray;

    /// 5(c): the base64 Ed25519 private key a user pastes to import (bring-your-own-key), bound to the
    /// import TextBox. Cleared once persisted so the secret does not linger in the input.
    [ObservableProperty] private string _importKeyText = "";

    /// 5(c): result of the last import attempt (success or the specific failure reason). Rendered on
    /// its own status line so a failed import is never mistaken for a saved one.
    [ObservableProperty] private string _importStatusLine = "";
    [ObservableProperty] private IBrush _importStatusBrush = Brushes.Gray;

    /// 5(b): the most recent successfully-sealed snapshot, retained so <see cref="SelfVerifyCommand"/>
    /// can re-run the forensic verification over it. Null until the first successful seal.
    private WitnessSnapshot? _lastSnapshot;

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
    ///
    /// RG-2: routes through <see cref="WitnessKeyStore.LoadKeyResult"/> so a transient store outage is
    /// never mistaken for "no key". Present → reuse; Absent → mint-and-persist (first run); Undetermined
    /// → throw <see cref="WitnessKeyStoreUnavailableException"/> and NEVER mint/overwrite — the identity
    /// exists and is simply out of reach (keyring locked, headless/SSH). The old code treated
    /// <c>LoadKey() == null</c> as Absent and silently rotated the identity on any store hiccup.
    private static string LoadOrCreatePersistentSigningKey()
    {
        var result = WitnessKeyStore.LoadKeyResult();
        switch (result.Presence)
        {
            case KeyPresence.Present:
                return result.Key!;
            case KeyPresence.Absent:
                var (priv, _) = WitnessSigner.GenerateKeyPair();
                WitnessKeyStore.SaveKey(priv);
                return priv;
            default: // Undetermined — a key exists but the store is unreachable. Do not touch it.
                throw new WitnessKeyStoreUnavailableException(
                    "The witness signing key exists but the secure store could not be reached; "
                    + "refusing to overwrite the persisted identity.");
        }
    }

    // Forensic-severity brushes shared by self-verify and external verify. 3-tier, parity with the
    // macOS twin: Valid → green; an honest-but-stale/expired seal → amber (not forged, just old); a
    // forged / future-dated / display-tampered / untrusted seal → critical red (App.axaml
    // SyxRedColor #FF4D6D). Anything unreadable folds into the critical tier — a seal we cannot read
    // is never shown as merely cautionary.
    private static readonly IBrush ValidBrush = new SolidColorBrush(Color.Parse("#3FD08A"));
    private static readonly IBrush CautionBrush = new SolidColorBrush(Color.Parse("#F2B33D"));
    private static readonly IBrush CriticalBrush = new SolidColorBrush(Color.Parse("#FF4D6D"));

    // ── Proposal-gated user copy (RG-2 store-unreachable, bring-your-own-key, self-verify) ─────────
    // Distinct from the generic "Could not access the signing key" branch: this reassures the user
    // their identity is intact and points at the real fix (unlock the keyring / run with a session),
    // instead of implying the key is lost or must be re-created.
    internal const string KeyStoreUnavailableMessage =
        "Your signing key is stored on this machine, but the secure store could not be reached "
        + "(keyring locked, or unavailable over SSH/headless). Your existing identity is untouched — "
        + "unlock the keyring and try again.";

    internal const string ImportKeyEmptyMessage =
        "Paste your base64 Ed25519 signing key above, then choose Save signing key.";
    internal const string ImportKeyInvalidMessage =
        "That is not a valid Ed25519 signing key. An Ed25519 private key is exactly 32 bytes, written "
        + "as base64. Nothing was saved.";
    internal const string ImportKeySaveFailedMessage = "Could not save the signing key.";
    internal const string ImportKeySuccessMessage =
        "Signing key saved. This identity will now sign every testimony on this machine.";

    internal const string SelfVerifyNoSnapshotMessage =
        "Seal a testimony first — there is no last seal to re-verify yet.";

    /// G-12 Route B: shown when the retained seal belongs to a different domain than the one now
    /// entered. Deliberately contains no form of the word "Verified" — this line renders beside the
    /// CURRENT domain, and any positive-sounding word here reads as a claim about it.
    internal const string SelfVerifyDomainMismatchFormat =
        "The last seal belongs to {0}, not {1}. Seal {1} first to check it.";

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

        // L2-033: track whether we reached a terminal state. If an unexpected exception escapes
        // between catch branches, the finally block forces Failed so IsRunning doesn't stick true.
        bool terminalStateReached = false;
        try
        {

        var servers = NetworkKeyState.GetSystemDNSServers();
        if (servers.Count == 0)
        {
            // D278 parity (macOS AppModel): never fabricate a resolver. A refused seal renders as
            // Failed (never Signed), so the run can never be mistaken for a completed, clean
            // testimony — same state discipline as the catch branches below.
            SignatureLine = "System resolver could not be determined. Check your network connection, then retry.";
            TestimonyState = WitnessTestimonyState.Failed;
            terminalStateReached = true;
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
        // RG-2: a key EXISTS but the store is unreachable. This is caught BEFORE the generic branch so
        // it renders a DISTINCT message — the identity is intact, not lost — and it is impossible for
        // this path to have minted/overwritten a key (LoadOrCreatePersistentSigningKey threw instead).
        catch (WitnessKeyStoreUnavailableException)
        {
            SignatureLine = KeyStoreUnavailableMessage;
            TestimonyState = WitnessTestimonyState.Failed; return;
        }
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

        // 5(b): retain the last successfully-sealed snapshot so the user can re-verify it on demand
        // (SelfVerifyCommand) without re-resolving/re-signing. Only a genuine seal updates it, so
        // "Re-verify last seal" always re-checks the most recent real testimony.
        _lastSnapshot = snap;

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
        // C211: the state must reflect the verification outcome — a seal that fails its own
        // post-sign verification must never be reported as Signed.
        var verifyResult = WitnessSigner.VerifySnapshotDetailed(snap);
        ApplyVerification(verifyResult);

        TestimonyState = verifyResult == SnapshotVerification.Valid
            ? WitnessTestimonyState.Signed
            : WitnessTestimonyState.Failed;
        terminalStateReached = true;

        } // end try
        // L2-033: if an unexpected exception escaped the inner catch branches, force Failed
        // so IsRunning (derived from TestimonyState == Running) doesn't stick true.
        finally
        {
            if (!terminalStateReached)
            {
                TestimonyState = WitnessTestimonyState.Failed;
                if (string.IsNullOrEmpty(SignatureLine) || SignatureLine == "Resolving and sealing…")
                    SignatureLine = "An unexpected error occurred. The testimony was not sealed.";
            }
        }
    }

    /// <summary>
    /// #22: verify an externally-supplied testimony (paste the snapshot JSON). Deserializes with the
    /// canonical reader contract and routes the outcome through <see cref="ApplyExternalVerification"/>,
    /// which uses the identical severity switch as self-verify so a third party's seal is judged by
    /// exactly the same rules, but renders into <see cref="ExternalVerifyLine"/> /
    /// <see cref="ExternalVerifyBrush"/> so the conclusion is visible without a prior self-sign.
    /// Synchronous: parsing and Ed25519 verification are fast and CPU-bound, matching the inline
    /// self-verify above (no await work).
    /// </summary>
    [RelayCommand]
    private void VerifyExternal()
    {
        if (string.IsNullOrWhiteSpace(ExternalSnapshotJson))
        {
            ExternalVerifyLine = "Paste a signed testimony (JSON) above, then verify it.";
            ExternalVerifyBrush = CautionBrush;
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
            // MalformedInput rather than guessing a result for bytes we could not parse.
            ApplyExternalVerification(SnapshotVerification.MalformedInput);
            return;
        }

        ApplyExternalVerification(snapshot is null
            ? SnapshotVerification.MalformedInput
            : WitnessSigner.VerifySnapshotDetailed(snapshot));
    }

    /// <summary>
    /// G-14: external-verify counterpart of <see cref="ApplyVerification"/>. Uses the same severity
    /// switch and message wording so a third-party seal is judged by identical rules, but writes to
    /// <see cref="ExternalVerifyLine"/> / <see cref="ExternalVerifyBrush"/> so the conclusion is
    /// visible in the external-verify panel regardless of whether a self-sign has occurred.
    /// </summary>
    internal void ApplyExternalVerification(SnapshotVerification result)
    {
        ExternalVerifyLine = result switch
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
        ExternalVerifyBrush = result switch
        {
            SnapshotVerification.Valid => ValidBrush,
            SnapshotVerification.Expired or SnapshotVerification.Stale => CautionBrush,
            _ => CriticalBrush,
        };
    }

    /// <summary>
    /// 5(c) bring-your-own-key: persist a user-supplied base64 Ed25519 signing key via the shared
    /// <see cref="WitnessKeyStore.SaveKey"/>, which validates with the SAME
    /// <see cref="WitnessSigner.ValidatePrivateKey"/> the signer uses before writing anything. On an
    /// invalid key nothing is persisted and the failure is surfaced on <see cref="ImportStatusLine"/>;
    /// the method returns without proceeding. On success the input is cleared so the raw secret does
    /// not linger on screen, and the status line confirms the identity is now saved.
    /// </summary>
    [RelayCommand]
    private void ImportKey()
    {
        var trimmed = (ImportKeyText ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            ImportStatusLine = ImportKeyEmptyMessage;
            ImportStatusBrush = CautionBrush;
            return;
        }

        try
        {
            // SaveKey validates via WitnessSigner.ValidatePrivateKey and throws on invalid material,
            // writing nothing in that case — save-time and sign-time acceptance cannot drift apart.
            WitnessKeyStore.SaveKey(trimmed);
        }
        catch (WitnessSignerException)
        {
            // Invalid key material — the one failure with an honest, specific cause. Do NOT proceed.
            ImportStatusLine = ImportKeyInvalidMessage;
            ImportStatusBrush = CriticalBrush;
            return;
        }
        catch (System.Exception ex)
        {
            // Store write failed (keyring/disk). Readable text on screen, raw detail to the sink.
            ImportStatusLine = ErrorPresenter.Compose(
                ImportKeySaveFailedMessage, ErrorPresenter.UserDetail(ex, "witness.key.import"));
            ImportStatusBrush = CriticalBrush;
            return;
        }

        ImportKeyText = "";  // clear the pasted secret from the input now that it is persisted
        ImportStatusLine = ImportKeySuccessMessage;
        ImportStatusBrush = ValidBrush;
    }

    /// <summary>
    /// 5(b) manual self-verify: re-run the full forensic verification over the LAST successfully-sealed
    /// snapshot and render it through the shared <see cref="ApplyVerification"/> switch. Guarded on a
    /// non-null <see cref="_lastSnapshot"/> — with no seal yet, it prompts rather than asserting a
    /// result over nothing.
    /// </summary>
    [RelayCommand]
    private void SelfVerify()
    {
        var snap = _lastSnapshot;
        if (snap is null)
        {
            VerifyLine = SelfVerifyNoSnapshotMessage;
            VerifyBrush = CautionBrush;
            return;
        }
        // G-12 Route B (SelfVerify_StaleSnapshot_DoesNotShowGreen): the retained seal may belong
        // to a different domain than the one now shown — seal a.com, switch to b.com, fail the
        // b.com seal, self-verify. Verifying a.com's snapshot here would render a green "Verified"
        // that the user reads as a claim about b.com: a false-confidence defect. The nullity guard
        // above is not enough; the seal must also be ABOUT the current domain.
        var current = Domain?.Trim() ?? "";
        if (!string.Equals(snap.TargetDomain, current, StringComparison.OrdinalIgnoreCase))
        {
            VerifyLine = string.Format(SelfVerifyDomainMismatchFormat, snap.TargetDomain, current);
            VerifyBrush = CautionBrush;
            return;
        }
        ApplyVerification(WitnessSigner.VerifySnapshotDetailed(snap));
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

namespace Syxx.Kit;

/// <summary>
/// Platform-aware factory for <see cref="ISecretStore"/> implementations.
///
/// Use <see cref="Create"/> for the default platform-appropriate store (DPAPI on Windows,
/// <see cref="LinuxSecretStore"/> on Linux, file-fallback on other platforms).
///
/// For dependency-injection hosts (WinUI GUI, Windows Service), register the result of
/// <see cref="Create"/> as a singleton and inject <see cref="ISecretStore"/> into
/// consumers such as <see cref="LicenseManager.SetSecretStore"/> and
/// <see cref="WitnessKeyStore.SetSecretStore"/>.
///
/// All methods use runtime platform checks (<c>OperatingSystem.IsWindows()</c>,
/// <c>OperatingSystem.IsLinux()</c>) — never preprocessor directives — so the same
/// binary runs on every platform.
/// </summary>
public static class PlatformSecretStore
{
    /// <summary>
    /// Creates the platform-appropriate <see cref="ISecretStore"/> implementation.
    /// </summary>
    /// <returns>
    ///   <list type="bullet">
    ///     <item><description><b>Windows:</b> <c>DpapiSecretStore</c> (Win32 DPAPI via crypt32.dll)</description></item>
    ///     <item><description><b>Linux:</b> <c>LinuxSecretStore</c> (libsecret P/Invoke + AES-256-GCM file fallback)</description></item>
    ///     <item><description><b>Other:</b> <c>LinuxSecretStore</c> (file-only fallback — degrades gracefully)</description></item>
    ///   </list>
    /// </returns>
    public static ISecretStore Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindowsStore();
        }

        // Linux and other Unix platforms: use LinuxSecretStore.
        // On macOS this will use the file-fallback only (no libsecret on macOS by default),
        // which is acceptable for the .NET Kit (the Swift SyxxKit is primary on macOS).
        return CreateLinuxStore();
    }

    /// <summary>
    /// Creates the Windows DPAPI-backed store. Marked as Windows-only for clarity;
    /// the DpapiSecretStore type itself is [SupportedOSPlatform("windows")].
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static ISecretStore CreateWindowsStore()
    {
        return new DpapiSecretStore();
    }

    /// <summary>Creates the Linux/libsecret-backed store (also used as fallback
    /// on other non-Windows platforms).</summary>
    private static ISecretStore CreateLinuxStore()
    {
        return new LinuxSecretStore();
    }

    /// <summary>
    /// Convenience: initialises both <see cref="LicenseManager"/> and
    /// <see cref="WitnessKeyStore"/> with the platform-appropriate store.
    /// Call once at application startup before any store operations.
    ///
    /// <para>BLAST RADIUS — verified, and worth stating because it is easy to read this subsystem as
    /// witness-only. ONE store instance is handed to BOTH consumers on the two lines below. On Linux
    /// that instance is <see cref="LinuxSecretStore"/>, so the licence key rides exactly the same
    /// backend chain, the same key derivation and the same file permissions as the Ed25519 signing
    /// key. Every property claimed of one holds of the other, including the weak ones: before the
    /// P-04 repairs, the licence key was also wrapped with a machine-derived key over a constant
    /// salt and was also written into a world-readable file before being narrowed.</para>
    ///
    /// <para>The two differ in consequence, not in exposure. A stolen licence key is a commercial
    /// loss; a stolen signing key means testimony forged in the owner's name. That is why §8's
    /// passphrase is described in terms of the signing key — but a passphrase installed via
    /// <see cref="KeystorePassphraseSession.SetProvider"/> protects both, because there is only
    /// one store.</para>
    /// </summary>
    public static void Initialise()
    {
        var store = Create();
        LicenseManager.SetSecretStore(store);
        WitnessKeyStore.SetSecretStore(store);
    }

    /// <summary>
    /// Initialises both stores with a specific implementation (for testing or DI).
    /// </summary>
    public static void Initialise(ISecretStore store)
    {
        LicenseManager.SetSecretStore(store);
        WitnessKeyStore.SetSecretStore(store);
    }
}

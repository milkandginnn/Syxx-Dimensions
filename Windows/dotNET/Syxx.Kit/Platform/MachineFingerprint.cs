using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Syxx.Kit;

// Device fingerprinting for licensing, with the ONE thing the old implementation lacked: an honest
// statement of how much the fingerprint is worth.
//
// THE BUG THIS REPLACES: LicenseManager.GetSystemFingerprint() read the Windows registry MachineGuid
// and, on ANY failure, silently fell back to `Environment.MachineName` — the HOSTNAME. Off Windows
// the registry read always fails, so on Linux every machine fingerprinted as its hostname. Two boxes
// both named "ubuntu" are ONE machine to a licence server, and renaming a host burns an activation.
// A hostname is not a machine identity, and a fingerprint that silently degrades into one is worse
// than no fingerprint: it is confidently wrong.
//
// HONESTY CONTRACT (Protocol §1/§10 — tri-state, never boolean): the QUALITY of the identifier
// travels WITH it. A caller deciding whether to node-lock must be able to see that it is holding an
// install-scoped id rather than a hardware one. There is deliberately no "good enough" fallback:
// when we cannot identify the hardware we say Unavailable and let the caller refuse.
//
// PLATFORM REALITY (why Linux is the hard one):
//   Windows  HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid  -> Hardware. Survives reinstall.
//   macOS    IOPlatformUUID (ioreg)                            -> Hardware. Survives reinstall.
//   Linux    /sys/class/dmi/id/product_uuid                    -> Hardware, but ROOT-ONLY (0400)
//            /etc/machine-id                                   -> INSTALL-SCOPED, not hardware
//
// The Linux trap, stated plainly because it is counter-intuitive: /etc/machine-id is generated PER
// INSTALLATION. It is exactly the wrong identifier for "the same box across distros" — dual-booting
// Ubuntu and Arch yields TWO machine-ids, so a licence meant to travel across sibling distros would
// burn two activations and the family logic would never fire. It is offered here only as an
// explicitly InstallScoped value, never as Hardware, and never silently.

/// How much a fingerprint is worth. This is not decoration — a caller must not node-lock on
/// anything below Hardware without deciding to.
public enum FingerprintQuality
{
    /// Bound to the physical machine; survives an OS reinstall and is stable across distros.
    Hardware,
    /// Bound to THIS OS INSTALL only. A reinstall or a sibling distro on the same box yields a
    /// DIFFERENT value. Usable for a trial anchor; NOT sound for node-locking hardware.
    InstallScoped,
    /// No identifier could be established. Never a hostname, never a guess.
    Unavailable,
}

/// A fingerprint plus the provenance a caller needs in order to trust it. <see cref="Value"/> is a
/// salted hash — the raw identifier is stable and cross-correlatable, so only the hash leaves the
/// device.
public sealed record MachineFingerprint(string Value, FingerprintQuality Quality, string Source)
{
    public static readonly MachineFingerprint Unavailable =
        new("", FingerprintQuality.Unavailable, "none");

    /// Whether this is sound to node-lock a licence to. Only Hardware qualifies: node-locking to an
    /// install-scoped id means a reinstall looks like a new machine and eats another activation.
    public bool IsNodeLockable => Quality == FingerprintQuality.Hardware;
}

public static class MachineFingerprints
{
    private const string Salt = "com.milkandgin.syxx.fingerprint.v1:";

    /// Privacy: hash the raw identifier with an app-specific salt so only Syxx can recognise a
    /// repeat install on the same device, and the raw id is never transmitted.
    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Salt + raw))).ToLowerInvariant();

    /// PURE: build a fingerprint from an already-read raw identifier. Split out so the quality
    /// contract is testable without a registry, a filesystem, or a specific OS.
    /// A blank/whitespace raw id is Unavailable — never hashed into a confident-looking value.
    public static MachineFingerprint From(string? raw, FingerprintQuality quality, string source)
    {
        if (string.IsNullOrWhiteSpace(raw)) return MachineFingerprint.Unavailable;
        return new MachineFingerprint(Hash(raw.Trim()), quality, source);
    }

    /// Read the Linux hardware id. Root-only (0400) on modern kernels, so this returns Unavailable
    /// for an unprivileged process — which is the honest answer, not a reason to fall back.
    /// The privileged helper that Cage's raw capture already needs is the intended path here.
    public static MachineFingerprint LinuxHardware(string dmiPath = "/sys/class/dmi/id/product_uuid")
    {
        try
        {
            if (!File.Exists(dmiPath)) return MachineFingerprint.Unavailable;
            var raw = File.ReadAllText(dmiPath).Trim();
            // The kernel prints all-zero/all-f placeholders on hardware with no real UUID (common
            // in VMs and some OEM boards). A constant shared by every such box is not an identity.
            if (raw.Length == 0
                || raw.Replace("-", "", StringComparison.Ordinal).Trim('0').Length == 0
                || raw.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant().Trim('f').Length == 0)
            {
                return MachineFingerprint.Unavailable;
            }
            return From(raw, FingerprintQuality.Hardware, "dmi:product_uuid");
        }
        catch
        {
            // Almost always EACCES: DMI is root-only. Unavailable, not a fallback.
            return MachineFingerprint.Unavailable;
        }
    }

    /// Read /etc/machine-id. INSTALL-SCOPED by construction — see the header note. Offered so a
    /// caller can make a documented, deliberate choice; never promoted to Hardware.
    public static MachineFingerprint LinuxInstallScoped(string path = "/etc/machine-id")
    {
        try
        {
            if (!File.Exists(path)) return MachineFingerprint.Unavailable;
            return From(File.ReadAllText(path), FingerprintQuality.InstallScoped, "etc:machine-id");
        }
        catch
        {
            return MachineFingerprint.Unavailable;
        }
    }

    /// The privileged helper's output: a hash captured at INSTALL time, when the packager was root
    /// and could read DMI. The app reads it with no privilege at all.
    ///
    /// The file contains ONLY the salted hash — never the raw DMI UUID. product_uuid is 0400 on
    /// purpose (it is a stable, cross-correlatable hardware id); copying it somewhere world-readable
    /// would defeat that protection and hand every process on the box an identifier the kernel
    /// deliberately withholds. See scripts/syxx-capture-hardware-id.sh.
    ///
    /// Dual-boot property: the FILE is per-install, but its VALUE is per-hardware — Ubuntu and Arch
    /// on one box each get their own file containing the SAME hash. That is precisely what
    /// /etc/machine-id cannot do.
    public static MachineFingerprint LinuxHelperCaptured(string path = "/var/lib/syxx/hardware-id")
    {
        try
        {
            if (!File.Exists(path)) return MachineFingerprint.Unavailable;
            var hash = File.ReadAllText(path).Trim();

            // The helper writes a lowercase sha256 hex digest. Anything else means a corrupt or
            // hand-edited file: treat it as absent rather than trusting a value we cannot vouch for.
            if (hash.Length != 64 || !IsLowerHex(hash)) return MachineFingerprint.Unavailable;

            // Already salted+hashed by the helper — do NOT hash again, or the value would differ
            // from a root-run direct DMI read of the same machine.
            return new MachineFingerprint(hash, FingerprintQuality.Hardware, "helper:hardware-id");
        }
        catch
        {
            return MachineFingerprint.Unavailable;
        }
    }

    private static bool IsLowerHex(string s)
    {
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }

    /// Best available Linux fingerprint, strongest first. NEVER the hostname.
    ///
    ///   1. helper-captured hash   (install-time DMI; the normal path — no privilege needed now)
    ///   2. direct DMI read        (only when Syxx itself runs as root, e.g. Cage capture)
    ///   3. /etc/machine-id        (explicitly InstallScoped — NOT node-lockable)
    ///   4. Unavailable
    ///
    /// 1 and 2 produce the SAME value for the same box: the helper applies the same salt+hash, so a
    /// machine that later runs privileged does not suddenly look like a different machine.
    public static MachineFingerprint Linux(
        string helperPath = "/var/lib/syxx/hardware-id",
        string dmiPath = "/sys/class/dmi/id/product_uuid",
        string machineIdPath = "/etc/machine-id")
    {
        var helper = LinuxHelperCaptured(helperPath);
        if (helper.Quality == FingerprintQuality.Hardware) return helper;

        var direct = LinuxHardware(dmiPath);
        if (direct.Quality == FingerprintQuality.Hardware) return direct;

        return LinuxInstallScoped(machineIdPath);
    }
}

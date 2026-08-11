using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Syxx.Kit;

// Linux distro + FAMILY identification from /etc/os-release (os-release(5)).
//
// Why family and not distro: a licence is meant to travel across sibling distros on one machine
// (Ubuntu -> Mint -> Kali) but families are a commercial boundary. So the family is the unit that
// matters for entitlement, and the distro id is what we RECORD per activation.
//
// HONESTY CONTRACT (Protocol §1/§10): an unrecognised distro resolves to `Unknown` and is handled
// EXPLICITLY by the caller. It must never be defaulted into a family — silently bucketing an
// unknown distro into Debian is the licensing analogue of a false clean: a confident answer we did
// not earn. `Unknown` is a first-class outcome, not an error.
//
// Parsing is deliberately split from IO: `Parse` is pure and takes the file's text, so the whole
// family matrix is testable from fixtures with no filesystem and no Linux host.

/// The licensing/entitlement family a distro belongs to. `Unknown` is first-class: it means "we
/// could not establish the family", NEVER "assume the common case".
public enum LinuxFamily { Debian, Arch, Unknown }

/// What /etc/os-release told us. `Id`/`VersionId` are recorded per activation (the component);
/// `Family` is what entitlement is scoped to.
public sealed record LinuxDistro(string Id, string VersionId, LinuxFamily Family, string PrettyName)
{
    /// No /etc/os-release, unreadable, or nothing usable in it. Distinct from a distro we read but
    /// do not recognise — that one has an Id and Family.Unknown.
    public static readonly LinuxDistro Unreadable = new("", "", LinuxFamily.Unknown, "");

    /// A stable component identifier for this activation, e.g. "ubuntu-24.04". Lowercase and
    /// hyphenated so it is safe as a Keygen component fingerprint.
    public string ComponentId =>
        string.IsNullOrEmpty(Id) ? "" :
        string.IsNullOrEmpty(VersionId) ? Id : $"{Id}-{VersionId}";
}

public static class LinuxDistros
{
    /// Distro ids that ARE the root of their own family. Debian and Arch carry NO `ID_LIKE` (they
    /// are what everything else is "like"), so an ID_LIKE-only resolver misses them entirely.
    private static readonly Dictionary<string, LinuxFamily> FamilyRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["debian"] = LinuxFamily.Debian,
        ["arch"] = LinuxFamily.Arch,
    };

    /// ID_LIKE tokens that indicate a family. `ubuntu` appears because Mint reports
    /// ID_LIKE="ubuntu debian" and some derivatives report only `ubuntu`.
    private static readonly Dictionary<string, LinuxFamily> LikeTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["debian"] = LinuxFamily.Debian,
        ["ubuntu"] = LinuxFamily.Debian,
        ["arch"] = LinuxFamily.Arch,
    };

    /// Resolve the family from `ID` + `ID_LIKE`.
    ///
    /// Order matters: ID-as-root FIRST (Debian/Arch have no ID_LIKE), then ID_LIKE tokens. If the
    /// tokens point at MORE THAN ONE family the answer is Unknown, not a coin flip — a distro
    /// claiming to be like both Debian and Arch has told us its family is not decidable from
    /// os-release, and guessing would put a licence in the wrong bucket.
    public static LinuxFamily FamilyFor(string? id, string? idLike)
    {
        if (!string.IsNullOrWhiteSpace(id) && FamilyRoots.TryGetValue(id.Trim(), out var rootFamily))
            return rootFamily;

        if (string.IsNullOrWhiteSpace(idLike)) return LinuxFamily.Unknown;

        var families = idLike
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => LikeTokens.TryGetValue(t, out var f) ? (LinuxFamily?)f : null)
            .Where(f => f is not null)
            .Select(f => f!.Value)
            .Distinct()
            .ToList();

        return families.Count == 1 ? families[0] : LinuxFamily.Unknown;
    }

    /// PURE parse of /etc/os-release content (os-release(5)): `KEY=value`, values optionally
    /// quoted, `#` comments, blank lines. Unknown keys ignored. Never throws.
    public static LinuxDistro Parse(string? osReleaseText)
    {
        if (string.IsNullOrWhiteSpace(osReleaseText)) return LinuxDistro.Unreadable;

        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in osReleaseText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            // os-release permits single or double quotes; strip a MATCHED pair only.
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }
            kv[key] = value;
        }

        kv.TryGetValue("ID", out var id);
        kv.TryGetValue("ID_LIKE", out var idLike);
        kv.TryGetValue("VERSION_ID", out var versionId);
        kv.TryGetValue("PRETTY_NAME", out var pretty);

        // A file with no ID tells us nothing usable, even if it parsed.
        if (string.IsNullOrWhiteSpace(id)) return LinuxDistro.Unreadable;

        return new LinuxDistro(
            Id: id.Trim().ToLowerInvariant(),
            VersionId: (versionId ?? "").Trim(),
            Family: FamilyFor(id, idLike),
            PrettyName: (pretty ?? "").Trim());
    }

    /// Read and parse /etc/os-release. Returns <see cref="LinuxDistro.Unreadable"/> off Linux, or
    /// when the file is missing/unreadable — never throws, and never guesses a family.
    public static LinuxDistro Detect(string path = "/etc/os-release")
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : LinuxDistro.Unreadable;
        }
        catch
        {
            return LinuxDistro.Unreadable;
        }
    }
}

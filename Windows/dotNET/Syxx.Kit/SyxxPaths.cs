// E077/E078/E087: shared helper for owner-only file/directory creation on Linux.
// On Windows the NTFS profile ACL handles this; on Linux the umask default (0755/0644)
// leaves sensitive files world-readable. Every writer of identity-bearing or secret data
// must call these helpers instead of bare Directory.CreateDirectory / File.WriteAllText.
using System;
using System.IO;

namespace Syxx.Kit;

public static class SyxxPaths
{
    /// <summary>
    /// Creates <paramref name="dir"/> (and parents) and, on non-Windows, restricts it to
    /// owner-only access (0700). On Windows the per-user profile ACL is inherited.
    /// Idempotent — safe to call on every save.
    /// </summary>
    public static void EnsurePrivateDir(string dir)
    {
        Directory.CreateDirectory(dir);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(dir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch
            {
                // Best-effort: some filesystems (exFAT, overlay) do not support chmod.
            }
        }
    }

    /// <summary>
    /// Restricts an already-written file to owner-only access (0600) on non-Windows.
    /// Call immediately after writing identity-bearing or secret data.
    /// </summary>
    public static void RestrictToOwner(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(filePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // Best-effort: some filesystems do not support chmod.
            }
        }
    }
}

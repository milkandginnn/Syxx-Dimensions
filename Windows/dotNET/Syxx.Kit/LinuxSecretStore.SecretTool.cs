using System.Diagnostics;

namespace Syxx.Kit;

public sealed partial class LinuxSecretStore
{
    // ── secret-tool CLI fallback ──────────────────────────────────────────────────

    /// <summary>Shells out to <c>secret-tool</c> (from <c>libsecret-tools</c> package)
    /// when the native library P/Invoke fails or is unavailable.</summary>
    internal static class SecretTool
    {
        private static bool? _available;

        /// <summary>Returns true when secret-tool is on $PATH and responds to --help.</summary>
        public static bool IsAvailable()
        {
            if (_available.HasValue) return _available.Value;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    Arguments = "--help",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) { _available = false; return false; }
                proc.WaitForExit(2000);
                _available = proc.ExitCode == 0;
                return _available.Value;
            }
            catch
            {
                _available = false;
                return false;
            }
        }

        public static void Store(string keyType, string keyId, string secret)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    ArgumentList =
                    {
                        "store",
                        $"--label=Syxx {keyType}",
                        "syxx-key-type", keyType,
                        "syxx-key-id", keyId
                    },
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return;
                proc.StandardInput.Write(secret);
                proc.StandardInput.Close();
                proc.WaitForExit(5000);
            }
            catch
            {
                // fall through to file store
            }
        }

        public static string? Retrieve(string keyType, string keyId)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    ArgumentList = { "lookup", "syxx-key-type", keyType, "syxx-key-id", keyId },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                return proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                    ? output.TrimEnd('\n', '\r')
                    : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>G-02: asks whether the keyring holds any secret filed under
        /// <c>syxx-key-type = keyType</c>, which <c>secret-tool search</c> can answer without a keyId.
        /// Only an unambiguous "found nothing" is reported as <see cref="SecretExistence.Absent"/>;
        /// a non-zero exit we cannot attribute to an empty result, or a timeout, is
        /// <see cref="SecretExistence.Unknown"/>, because the D-Bus service being unreachable is not
        /// the same as the keyring being empty.</summary>
        public static SecretExistence Exists(string keyType)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    ArgumentList = { "search", "--all", "syxx-key-type", keyType },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return SecretExistence.Unknown;

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                if (!proc.WaitForExit(5000)) return SecretExistence.Unknown;

                if (proc.ExitCode == 0)
                {
                    // ASSUMPTION, and the ONE place this probe could manufacture a false Absent — the
                    // worst outcome in this subsystem, since Absent is what licenses minting over an
                    // identity. It reads "the process ran, the service answered, it printed no matches"
                    // as proof of absence. That holds if a locked-but-populated keyring can never
                    // produce the same signature (exit 0, empty stdout) — libsecret's search returns
                    // locked items too, and secret-tool's search asks to unlock and load values, so a
                    // locked collection should fail loudly rather than answer emptily. UNVERIFIED
                    // against a live keyring: there is none on the authoring host. It costs less than
                    // it did, because LibSecretNative.Exists now answers for the same keyring without
                    // asking it to unlock, and Present from any backend wins. It still matters, and it
                    // is the FIRST item for the real-keyring CI leg: populate the keyring, lock it,
                    // and assert this returns Present or Unknown — never Absent.
                    return string.IsNullOrWhiteSpace(stdout)
                        ? SecretExistence.Absent
                        : SecretExistence.Present;
                }

                // secret-tool exits non-zero both for "no such secret" and for real failures
                // (no session bus, service refused). Only the former is proof of absence.
                return stderr.Contains("No such secret", StringComparison.OrdinalIgnoreCase)
                    ? SecretExistence.Absent
                    : SecretExistence.Unknown;
            }
            catch
            {
                return SecretExistence.Unknown;
            }
        }

        public static void Delete(string keyType, string keyId)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    ArgumentList = { "clear", "syxx-key-type", keyType, "syxx-key-id", keyId },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
            }
            catch
            {
                // best-effort
            }
        }
    }
}

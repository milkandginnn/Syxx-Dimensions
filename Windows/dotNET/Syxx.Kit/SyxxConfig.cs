using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit
{
    public static class SyxxConfig
    {
        /// <summary>Keygen account this build validates licenses against.</summary>
        public const string AccountID = "4b50cc83-1664-4626-99b1-fbd4e76e35c3";

        /// <summary>Product version, reported by every CLI via --version. Keep in step with the server.</summary>
        public const string Version = "2.0.0";

        /// <summary>
        /// Handles the universal --version/-V/-v and --help/-h flags. Returns true if a flag was
        /// handled and the caller should exit immediately.
        /// </summary>
        public static bool HandleGlobalFlags(string[] args, string tool, string? usage = null)
        {
            // Every CLI calls this first, so it is the one place to force UTF-8 output — Windows
            // consoles default to the legacy OEM code page and render the report glyphs as "??".
            // E012: emit a diagnostic when UTF-8 encoding fails on legacy console hosts so
            // mojibake in evidence output (IDN domains, dashes, arrows) is explainable.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[warning] Console UTF-8 encoding unavailable: {ex.Message}");
            }

            var flags = new HashSet<string>(args);
            // E088: accept both -v and -V for --version to match the top-level dispatcher.
            if (flags.Contains("--version") || flags.Contains("-V") || flags.Contains("-v"))
            {
                Console.WriteLine($"{tool} {Version}");
                return true;
            }
            if (flags.Contains("--help") || flags.Contains("-h"))
            {
                Console.WriteLine($"{tool} {Version}");
                if (!string.IsNullOrEmpty(usage))
                {
                    Console.WriteLine();
                    Console.WriteLine(usage);
                }
                return true;
            }
            return false;
        }
        /// <summary>Build environment label (e.g. "production", "staging", "development").
        /// Read from the SYXX_ENVIRONMENT environment variable; defaults to "production".</summary>
        public static string BuildEnvironment =>
            Environment.GetEnvironmentVariable("SYXX_ENVIRONMENT") ?? "production";

        /// <summary>Dimension server base URL — overridable via the SYXX_DIMENSION_URL environment variable.</summary>
        public static string DimensionBaseURL =>
            Environment.GetEnvironmentVariable("SYXX_DIMENSION_URL") ?? "https://syxx.app";

        /// <summary>Sentry DSN for crash/error reporting. Read from SENTRY_DSN environment variable.
        /// Returns null if not configured (Sentry disabled).</summary>
        public static string? SentryDsn
        {
            get
            {
                var dsn = Environment.GetEnvironmentVariable("SENTRY_DSN");
                return string.IsNullOrEmpty(dsn) ? null : dsn;
            }
        }
    }
}

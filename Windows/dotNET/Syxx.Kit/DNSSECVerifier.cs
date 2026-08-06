using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit
{
    public class DNSKEYRecord
    {
        public ushort Flags { get; set; }
        public byte Protocol { get; set; }
        public byte Algorithm { get; set; }
        public string PublicKey { get; set; } = string.Empty;
    }

    public class RRSIGRecord
    {
        public ushort TypeCovered { get; set; }
        public byte Algorithm { get; set; }
        public byte Labels { get; set; }
        public uint OriginalTtl { get; set; }
        public DateTime Expiration { get; set; }
        public DateTime Inception { get; set; }
        public ushort KeyTag { get; set; }
        public string SignerName { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    /// <summary>
    /// The DNSSEC posture of a domain, as far as this client can determine it. Deliberately has
    /// no "validated / protected" case: this client observes DNSKEY/RRSIG records and checks
    /// RRSIG validity windows, but does not perform full cryptographic signature verification, so
    /// reporting "protected" on record presence would be a false-secure claim. Full chain
    /// validation is tracked as proposal A2.
    /// </summary>
    public enum DNSSECStatus
    {
        Unsigned,
        RecordsPresentUnverified,
        SignatureExpiredOrNotYetValid,
        Inconclusive
    }

    /// Ph6.2: helpers for DNSSECStatus quorum gates.
    public static class DNSSECStatusExtensions
    {
        /// true for RecordsPresentUnverified — should NOT count toward decisive quorum.
        public static bool IsUnverified(this DNSSECStatus s) => s == DNSSECStatus.RecordsPresentUnverified;

        /// Returns the wire-format camelCase rawValue matching the macOS DNSSECStatus rawValue
        /// (DNSSECVerifier.swift:39-49). Used for datapoint values and report text.
        public static string RawValue(this DNSSECStatus s) => s switch
        {
            DNSSECStatus.Unsigned => "unsigned",
            DNSSECStatus.RecordsPresentUnverified => "recordsPresentUnverified",
            DNSSECStatus.SignatureExpiredOrNotYetValid => "signatureExpiredOrNotYetValid",
            DNSSECStatus.Inconclusive => "inconclusive",
            _ => throw new ArgumentOutOfRangeException(nameof(s)),
        };
    }

    public static class DNSSECVerifier
    {
        public static string Describe(DNSSECStatus status) => status switch
        {
            DNSSECStatus.Unsigned => "NO DNSSEC RECORDS OBSERVED (zone appears unsigned)",
            DNSSECStatus.RecordsPresentUnverified => "DNSSEC RECORDS OBSERVED, signature window valid (cryptographic validation not performed by this client)",
            DNSSECStatus.SignatureExpiredOrNotYetValid => "DNSSEC RECORDS OBSERVED but all RRSIGs are expired/not-yet-valid (stale or broken signing)",
            _ => "DNSSEC status INCONCLUSIVE (records could not be retrieved)"
        };

        /// <summary>
        /// Returns a typed, honest DNSSEC posture for <paramref name="domain"/>, including an RRSIG
        /// validity-window (inception/expiration) check.
        ///
        /// A030/A036: the query sets the EDNS DO bit and asks for DNSKEY (type 48) once. The RRSIG that
        /// covers the DNSKEY RRset is delivered in that same DO-enabled answer, so there is no separate
        /// (and unanswered) bare QTYPE=RRSIG query, which is why the old path reported every signed zone
        /// as unsigned.
        /// </summary>
        public static async Task<DNSSECStatus> CheckStatusAsync(string domain, string resolver = "8.8.8.8", DateTime? nowUtc = null)
        {
            DateTime now = nowUtc ?? DateTime.UtcNow;

            var response = await DNSResolver.QueryUDPDetailedAsync(
                new DNSQuery(domain, 48, resolver, edns0UDPSize: 1232, edns0DODNSSEC: true));
            if (Failed(response.Status)) return DNSSECStatus.Inconclusive;
            return Classify(response.Records, now, domain);
        }

        /// <summary>
        /// Pure, testable classifier over the records of a DO-enabled DNSKEY response. Kept separate
        /// from the network call so every posture (unsigned / stripped / valid / expired) can be
        /// exercised deterministically and so a DoH or authoritative caller can reuse the same rules.
        /// </summary>
        /// <param name="queriedName">
        /// C059: the zone the answer is supposed to be about. Binding the records to it is what makes
        /// the signature-stripping canary work at all — without it, an on-path attacker appends a
        /// DNSKEY+RRSIG pair harvested from ANY signed zone (they are public data) and the verifier
        /// reports a valid signature window for a zone whose signatures were removed. Pass empty only
        /// where the zone genuinely is not known; the binding checks are then skipped and the C125
        /// field checks still apply. Twin of macOS <c>DNSSECVerifier.classify(records:now:queriedName:)</c>.
        /// </param>
        internal static DNSSECStatus Classify(IEnumerable<DNSRecord> records, DateTime now, string queriedName = "")
        {
            var recs = records.ToList();
            string normalizedQuery = NormalizeDomainName(queriedName);

            // C059 + C125: a DNSKEY counts only if it belongs to the queried zone AND its rdata is a
            // structurally valid zone-signing key. DNSKEY rdata layout: flags(2) protocol(1)
            // algorithm(1) publicKey(var).
            bool dnskeyPresent = recs.Any(r =>
            {
                if (r.Type != 48) return false;
                if (normalizedQuery.Length > 0 && NormalizeDomainName(r.Name) != normalizedQuery) return false;
                var bytes = HexToBytes(r.Rdata);
                if (bytes == null || bytes.Length < 4) return false;
                ushort flags = (ushort)((bytes[0] << 8) | bytes[1]);
                byte protocolField = bytes[2];
                byte algorithm = bytes[3];
                if (protocolField != 3) return false;          // RFC 4034 2.1.2: Protocol MUST be 3.
                if ((flags & 0x0100) == 0) return false;       // RFC 4034 2.1.1: ZONE flag (bit 7) must be set.
                if (algorithm == 0) return false;              // RFC 4034 2.1.3: algorithm 0 is reserved.
                return true;
            });
            // A036: with the DO bit set, genuinely no DNSKEY is an honest "unsigned" observation.
            if (!dnskeyPresent) return DNSSECStatus.Unsigned;

            // RRSIGs must cover the DNSKEY RRset (KDNS-05), own the queried zone, and carry a signer
            // that is the queried zone or an ancestor of it (C059).
            var windows = recs
                .Where(r => r.Type == 46)
                .Where(r => normalizedQuery.Length == 0 || NormalizeDomainName(r.Name) == normalizedQuery)
                .Where(r =>
                {
                    if (normalizedQuery.Length == 0) return true;
                    var signer = ParseSignerName(r.Rdata);
                    // An unparseable signer is not evidence against the record; the validity-window
                    // parse below rejects malformed rdata on its own.
                    return signer is null || IsSignerValid(normalizedQuery, NormalizeDomainName(signer));
                })
                .Select(r => RRSIGValidityWindow(r.Rdata, 48))
                .Where(w => w.HasValue)
                .Select(w => w!.Value)
                .ToList();

            // B046/A030: DNSKEY present but the DO-enabled answer carried no covering RRSIG - the
            // signatures were stripped on this path, or the resolver ignored the DO bit / returned no
            // OPT. We can neither confirm signing nor call the zone cleanly unsigned, so this is
            // INCONCLUSIVE. Never "signed", and never a silent "unsigned".
            if (windows.Count == 0) return DNSSECStatus.Inconclusive;

            bool anyCurrentlyValid = windows.Any(w => RRSIGWindowCurrentlyValid(w.Inception, w.Expiration, now));
            return anyCurrentlyValid ? DNSSECStatus.RecordsPresentUnverified : DNSSECStatus.SignatureExpiredOrNotYetValid;
        }

        private static bool Failed(DNSResponseStatus status) =>
            status == DNSResponseStatus.Timeout ||
            status == DNSResponseStatus.TransportError ||
            status == DNSResponseStatus.MalformedResponse ||
            status == DNSResponseStatus.ResponseIdMismatch ||
            status == DNSResponseStatus.Truncated ||
            status == DNSResponseStatus.ServFail ||
            status == DNSResponseStatus.Refused;

        /// <summary>
        /// Parses the raw 32-bit inception/expiration fields out of an RRSIG record's wire-format rdata
        /// (surfaced as a hex string for non-A/AAAA types). Pure and testable. The values are returned
        /// as raw uint seconds (not DateTime) because RFC 4034 3.1.5 compares them with the current time
        /// using RFC 1982 serial-number arithmetic, not absolute epochs. Layout: typeCovered(2)
        /// algorithm(1) labels(1) originalTTL(4) sigExpiration(4) sigInception(4) keyTag(2) ...
        /// </summary>
        public static (uint Inception, uint Expiration)? RRSIGValidityWindow(string rdataHex, ushort expectedTypeCovered)
        {
            var bytes = HexToBytes(rdataHex);
            if (bytes == null || bytes.Length < 18) return null;
            ushort typeCovered = (ushort)((bytes[0] << 8) | bytes[1]);
            if (typeCovered != expectedTypeCovered) return null;
            uint expiration = BeUInt32(bytes, 8);
            uint inception = BeUInt32(bytes, 12);
            return (inception, expiration);
        }

        /// <summary>
        /// Maximum plausible RRSIG validity span, in seconds (2 years). Real signers use windows of days
        /// to a few months. A span approaching the 2^31 serial half-space is implausible and is exactly
        /// what a spoofed local clock would need to wrap a long-expired signature back into "valid" under
        /// serial arithmetic, so we refuse to treat such a window as currently valid (B047).
        /// </summary>
        internal const uint MaxPlausibleValiditySpanSeconds = 63_072_000;

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        internal static uint ToUnixSeconds(DateTime now) =>
            unchecked((uint)(long)(DateTime.SpecifyKind(now, DateTimeKind.Utc) - UnixEpoch).TotalSeconds);

        /// <summary>
        /// RFC 1982 (SERIAL_BITS = 32) "a is at or before b": the forward distance from a to b, computed
        /// mod 2^32, is under 2^31.
        /// </summary>
        internal static bool SerialLessThanOrEqual(uint a, uint b) => unchecked(b - a) < 0x8000_0000u;

        /// <summary>
        /// A042/B047: is <paramref name="now"/> within [inception, expiration] using RFC 1982
        /// serial-number arithmetic (mod 2^32), not absolute-epoch comparison - so a window that
        /// straddles the 2106 epoch wrap is evaluated correctly. Guards against local-clock spoofing by
        /// rejecting an implausibly wide window and a window whose expiration precedes its inception.
        /// This checks the validity window only; it does not cryptographically verify the signature.
        /// </summary>
        internal static bool RRSIGWindowCurrentlyValid(uint inception, uint expiration, DateTime now)
        {
            uint nowEpoch = ToUnixSeconds(now);
            // Expiration must be at or after inception under serial arithmetic, else malformed.
            if (!SerialLessThanOrEqual(inception, expiration)) return false;
            // B047: reject an implausibly wide window (clock-spoof / forged validity).
            if (unchecked(expiration - inception) > MaxPlausibleValiditySpanSeconds) return false;
            return SerialLessThanOrEqual(inception, nowEpoch) && SerialLessThanOrEqual(nowEpoch, expiration);
        }

        // ---- C059: owner / signer binding helpers. Twins of the Swift functions of the same names. ----

        /// <summary>
        /// Parses the signer name out of RRSIG wire-format rdata (surfaced as hex). Layout:
        /// typeCovered(2) algorithm(1) labels(1) originalTTL(4) sigExpiration(4) sigInception(4)
        /// keyTag(2) signerName(var) signature(var) — so the name starts at byte 18 and is a run of
        /// <c>&lt;length-byte&gt;&lt;label-bytes&gt;</c> terminated by a zero-length (root) label.
        /// Returns null when the rdata is too short or a length byte runs off the end.
        /// </summary>
        internal static string? ParseSignerName(string rdataHex)
        {
            var bytes = HexToBytes(rdataHex);
            if (bytes == null || bytes.Length < 18) return null;
            var labels = new List<string>();
            int i = 18;
            while (i < bytes.Length)
            {
                int len = bytes[i];
                i++;
                if (len == 0) break;                       // root label terminates the name
                if (i + len > bytes.Length) return null;
                labels.Add(System.Text.Encoding.UTF8.GetString(bytes, i, len));
                i += len;
            }
            return labels.Count == 0 ? "." : string.Join(".", labels);
        }

        /// <summary>
        /// C059: is <paramref name="signer"/> the queried zone itself, or an ancestor of it?
        /// "www.example.com" signed by "example.com" is valid (the parent zone signs); "example.com"
        /// signed by "evil.com" is not. The bare root (".") is rejected — it cannot vouch for a
        /// specific zone here. Both arguments must already be normalised.
        /// </summary>
        internal static bool IsSignerValid(string queriedName, string signer)
        {
            if (signer == queriedName) return true;
            var q = queriedName.EndsWith(".", StringComparison.Ordinal) ? queriedName[..^1] : queriedName;
            var s = signer.EndsWith(".", StringComparison.Ordinal) ? signer[..^1] : signer;
            if (s.Length == 0) return false;
            return q.EndsWith("." + s, StringComparison.Ordinal);
        }

        /// <summary>Lowercase, trim, strip one trailing dot — so "Example.COM." and "example.com" compare equal.</summary>
        internal static string NormalizeDomainName(string name)
        {
            var n = name.Trim().ToLowerInvariant();
            if (n.EndsWith(".", StringComparison.Ordinal)) n = n[..^1];
            return n;
        }

        private static uint BeUInt32(byte[] b, int o) =>
            ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

        private static byte[]? HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length % 2 != 0) return null;
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!byte.TryParse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out bytes[i]))
                {
                    return null;
                }
            }
            return bytes;
        }
    }
}

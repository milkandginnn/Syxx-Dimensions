// DNSResolver.Parsing.cs — DNS wire-format building and parsing (RFC 1035, RFC 6891).
// B005: extracted from DNSResolver.cs to separate wire-format concerns from transport and orchestration.

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Syxx.Kit
{
    public static partial class DNSResolver
    {
        public static byte[] MakeQueryPacket(string name, ushort type)
        {
            return MakeQueryPacket(name, type, out _);
        }

        public static byte[] MakeQueryPacket(string name, ushort type, out ushort transactionId)
        {
            return MakeQueryPacket(name, type, out transactionId, qclass: 1);
        }

        public static byte[] MakeQueryPacket(string name, ushort type, out ushort transactionId,
            ushort qclass = 1, ushort? edns0UDPSize = null, string? edns0ClientSubnetV4 = null,
            bool dnssecOK = false)
        {
            name = ToAsciiName(name);
            ValidateDomainName(name);
            transactionId = (ushort)RandomNumberGenerator.GetInt32(0, 65536);

            using (var ms = new MemoryStream())
            {
                // Header
                ms.WriteByte((byte)((transactionId >> 8) & 0xff));
                ms.WriteByte((byte)(transactionId & 0xff));
                ms.Write(new byte[] { 0x01, 0x00 }, 0, 2); // Flags: Standard query, recursion desired
                ms.Write(new byte[] { 0x00, 0x01 }, 0, 2); // Questions: 1
                ms.Write(new byte[] { 0x00, 0x00 }, 0, 2); // Answer RRs: 0
                ms.Write(new byte[] { 0x00, 0x00 }, 0, 2); // Authority RRs: 0
                // B317: simplified from no-op conditional (both branches produced 0x00 for high byte).
                ms.Write(new byte[] { 0x00,
                                      edns0UDPSize.HasValue ? (byte)0x01 : (byte)0x00 }, 0, 2); // Additional RRs: 0 or 1

                // C116: trim trailing dots before splitting — "example.com." must produce
                // the same wire bytes as "example.com". An untrimmed trailing dot creates an
                // empty final label that writes a premature root terminator, shifting QTYPE/QCLASS.
                var labels = name.Trim('.').Split('.');
                foreach (var label in labels)
                {
                    var bytes = Encoding.UTF8.GetBytes(label);
                    ms.WriteByte((byte)bytes.Length);
                    ms.Write(bytes, 0, bytes.Length);
                }
                ms.WriteByte(0x00); // Null byte terminating domain name

                // Question: Type & Class
                ms.WriteByte((byte)((type >> 8) & 0xff));
                ms.WriteByte((byte)(type & 0xff));
                ms.WriteByte((byte)((qclass >> 8) & 0xff));
                ms.WriteByte((byte)(qclass & 0xff));

                // EDNS0 OPT pseudo-record in the additional section (datapoint F1) - minimal probe:
                // root name, advertised UDP payload size, version 0, DO bit set only when DNSSEC
                // records are requested, no options.
                if (edns0UDPSize.HasValue)
                {
                    ushort udp = edns0UDPSize.Value;
                    ms.WriteByte(0x00);                                                     // NAME: root
                    ms.Write(new byte[] { 0x00, 0x29 }, 0, 2);                             // TYPE: OPT (41)
                    ms.Write(new byte[] { (byte)((udp >> 8) & 0xff), (byte)(udp & 0xff) }, 0, 2); // CLASS: UDP payload size
                    // TTL: ext-rcode 0, version 0, flags. DO bit (0x8000 in the flags word) asks the
                    // resolver to return DNSSEC RRSIG records (A030/A036); otherwise flags are 0.
                    ms.Write(dnssecOK ? new byte[] { 0x00, 0x00, 0x80, 0x00 } : new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0, 4);

                    if (!string.IsNullOrEmpty(edns0ClientSubnetV4))
                    {
                        var octets = Ipv4OctetsForEcs(edns0ClientSubnetV4);
                        if (octets != null)
                        {
                            var ecsOption = new byte[] { 0x00, 0x08,    // OPTION-CODE: ECS (8)
                                                         0x00, 0x07,    // OPTION-LENGTH: 7
                                                         0x00, 0x01,    // FAMILY: IPv4
                                                         // B320: /24 is deliberate — probes whether the resolver forwards ECS
                                                         // to authoritative servers. Using the real client's prefix would
                                                         // leak PII; /24 provides enough entropy for leak detection.
                                                         0x18,          // SOURCE PREFIX-LENGTH: 24
                                                         0x00,          // SCOPE PREFIX-LENGTH: 0 (query)
                                                         octets[0], octets[1], octets[2] };
                            ms.Write(new byte[] { (byte)((ecsOption.Length >> 8) & 0xff), (byte)(ecsOption.Length & 0xff) }, 0, 2); // RDLENGTH
                            ms.Write(ecsOption, 0, ecsOption.Length);
                        }
                        else
                        {
                            ms.Write(new byte[] { 0x00, 0x00 }, 0, 2); // RDLENGTH: 0
                        }
                    }
                    else
                    {
                        ms.Write(new byte[] { 0x00, 0x00 }, 0, 2); // RDLENGTH: 0
                    }
                }

                return ms.ToArray();
            }
        }

        /// Returns the first three octets of a dotted IPv4 address (for a /24 ECS option); null if not valid.
        private static byte[]? Ipv4OctetsForEcs(string ip)
        {
            if (!IPAddress.TryParse(ip, out var addr)) return null;
            var bytes = addr.GetAddressBytes();
            if (bytes.Length != 4) return null;
            return new[] { bytes[0], bytes[1], bytes[2] };
        }

        internal static bool ResponseQuestionMatches(byte[] data, string expectedName, ushort expectedType)
        {
            if (data == null || data.Length < 12) return false;
            try
            {
                var (qname, typeOffset) = ParseName(data, 12);
                string qnameLower = qname.ToLowerInvariant();
                // C016: the wire echoes the PUNYCODED name; canonicalize the expected name the
                // same way, or every Unicode IDN reads as a spoofed response (hard Prophet gate).
                string expected = ToAsciiName(expectedName.Trim('.')).ToLowerInvariant();
                if (qnameLower != expected) return false;
                if (typeOffset + 4 > data.Length) return false;
                ushort qtype = (ushort)((data[typeOffset] << 8) | data[typeOffset + 1]);
                return qtype == expectedType;
            }
            catch
            {
                return false;
            }
        }

        public static List<DNSRecord> ParseResponsePacket(byte[] data)
        {
            return ParseResponsePacketDetailed(data).Records;
        }

        public static DNSResponse ParseResponsePacketDetailed(byte[] data, ushort? requestedType = null, ushort? expectedTransactionId = null)
        {
            var records = new List<DNSRecord>();
            if (data.Length < 12)
            {
                return new DNSResponse { Status = DNSResponseStatus.MalformedResponse, ErrorMessage = "DNS response shorter than header." };
            }

            ushort transactionId = (ushort)((data[0] << 8) | data[1]);
            if (expectedTransactionId.HasValue && transactionId != expectedTransactionId.Value)
            {
                return new DNSResponse { Status = DNSResponseStatus.ResponseIdMismatch, ErrorMessage = "DNS transaction ID mismatch." };
            }

            ushort flags = (ushort)((data[2] << 8) | data[3]);
            bool qr = (flags & 0x8000) != 0;
            if (!qr)
            {
                return new DNSResponse { Status = DNSResponseStatus.MalformedResponse, ErrorMessage = "Not a response packet (QR bit is 0)." };
            }
            bool truncated = (flags & 0x0200) != 0;
            ushort responseCode = (ushort)(flags & 0x000F);
            bool isAuthoritative = (flags & 0x0400) != 0;
            bool recursionAvailable = (flags & 0x0080) != 0;
            bool authenticatedData = (flags & 0x0020) != 0;

            int questionCount = (data[4] << 8) | data[5];
            int answerCount = (data[6] << 8) | data[7];
            int authorityCount = (data[8] << 8) | data[9];
            int additionalCount = (data[10] << 8) | data[11];

            // D147: cap answer count at 256 to prevent crafted packets with enormous ANCOUNT
            // from causing excessive parsing work (parity with macOS D020 budget).
            if (answerCount > 256)
                return new DNSResponse { Status = DNSResponseStatus.MalformedResponse, ErrorMessage = $"Answer count {answerCount} exceeds maximum 256." };

            // Best-effort authority-section parse (A6): a failure here never downgrades a valid answer
            // to MalformedResponse; it yields an empty authority set. Mirrors the macOS parser.
            List<DNSRecord> ParseAuthority() => ParseAuthorityBestEffort(data, questionCount, answerCount, authorityCount);

            // Best-effort EDNS0 OPT parse (F1): walk to the additional section and read the resolver's
            // OPT pseudo-record, if any. (null, false) = OPT genuinely absent; (null, true) = the
            // additional section declared records but the OPT/section could not be parsed → UNKNOWN.
            (EDNSResponseInfo? Info, bool Malformed) ParseEDNS()
            {
                var afterQuestions = SkipQuestions(data, 12, questionCount);
                // additionalCount declared records but we cannot even reach the additional section →
                // the packet is corrupt and EDNS support is UNKNOWN, never a confident "absent".
                if (afterQuestions == null) return (null, true);
                var afterAnswers = ParseRecordsBestEffort(data, afterQuestions.Value, answerCount).Next;
                var afterAuthority = ParseRecordsBestEffort(data, afterAnswers, authorityCount).Next;
                return FindOPTRecord(data, afterAuthority, additionalCount);
            }

            // B072: combine the EDNS extended RCODE (upper 8 bits from the OPT TTL) with the
            // base RCODE so BADVERS (16) is not read as success (RCODE 0).
            // B073/B071: distinguish "no OPT" (absent — a legacy resolver) from "additional section
            // declared records but the OPT could not be parsed" (malformed → EDNS support UNKNOWN).
            // ednsMalformed carries that third state so a malformed OPT is never rendered as a
            // confident "EDNS absent / not supported".
            (EDNSResponseInfo? Info, bool Malformed) ednsResult = additionalCount > 0 ? ParseEDNS() : (null, false);
            EDNSResponseInfo? ednsInfo = ednsResult.Info;
            bool ednsMalformed = ednsResult.Malformed;
            ushort combinedResponseCode = (ushort)(responseCode | ((ednsInfo?.ExtendedRCode ?? 0) << 4));

            if (truncated)
            {
                // B305: parse available authority section before returning on truncation.
                // A truncated response may still contain a partial authority section with a
                // useful SOA record. Best-effort parse; failures yield empty authority.
                var truncAuth = ParseAuthority();
                return new DNSResponse
                {
                    Status = DNSResponseStatus.Truncated,
                    ResponseCode = combinedResponseCode,
                    IsTruncated = true,
                    AuthorityRecords = truncAuth,
                    AuthorityMalformed = false, // best-effort; don't flag partial parse as malformed
                };
            }
            if (combinedResponseCode == 2)
            {
                return new DNSResponse { Status = DNSResponseStatus.ServFail, ResponseCode = combinedResponseCode };
            }
            if (combinedResponseCode == 3)
            {
                var authRecs = ParseAuthority();
                return new DNSResponse
                {
                    Status = DNSResponseStatus.NXDomain,
                    ResponseCode = combinedResponseCode,
                    IsAuthoritative = isAuthoritative,
                    RecursionAvailable = recursionAvailable,
                    AuthenticatedData = authenticatedData,
                    AuthorityRecords = authRecs,
                    AuthorityMalformed = authorityCount > 0 && authRecs.Count < authorityCount,
                    Edns = ednsInfo,
                    EdnsMalformed = ednsMalformed,
                    ResponseByteLength = data.Length
                };
            }
            if (combinedResponseCode == 5)
            {
                return new DNSResponse { Status = DNSResponseStatus.Refused, ResponseCode = combinedResponseCode };
            }
            if (combinedResponseCode != 0)
            {
                return new DNSResponse { Status = DNSResponseStatus.TransportError, ResponseCode = combinedResponseCode, ErrorMessage = $"Unhandled DNS RCODE {combinedResponseCode}." };
            }

            int offTypeCount = 0;
            int offNameCount = 0;
            if (answerCount == 0)
            {
                var authRecs = ParseAuthority();
                return new DNSResponse
                {
                    Status = DNSResponseStatus.NoData,
                    ResponseCode = combinedResponseCode,
                    IsAuthoritative = isAuthoritative,
                    RecursionAvailable = recursionAvailable,
                    AuthenticatedData = authenticatedData,
                    AuthorityRecords = authRecs,
                    AuthorityMalformed = authorityCount > 0 && authRecs.Count < authorityCount,
                    Edns = ednsInfo,
                    EdnsMalformed = ednsMalformed,
                    ResponseByteLength = data.Length
                };
            }

            int offset = 12;
            try
            {
                // Capture the first question's owner name (B070 — the root of the valid owner set) while
                // skipping the rest of the question section.
                string? questionName = null;
                for (int i = 0; i < questionCount; i++)
                {
                    var q = ParseName(data, offset);
                    if (i == 0) questionName = q.Name.ToLowerInvariant();
                    offset = q.NextOffset;
                    if (offset + 4 > data.Length)
                    {
                        return new DNSResponse { Status = DNSResponseStatus.MalformedResponse, ErrorMessage = "Question section exceeds packet bounds." };
                    }
                    offset += 4; // Skip type and class
                }

                // Phase 1 — parse every answer RR (owner name, type, class, ttl, rdata, and for a CNAME
                // its decoded target name). A strict bounds/parse failure fails the whole response.
                var raws = new List<(string Name, string OwnerLower, ushort Type, ushort Cls, uint Ttl, string Rdata, string? CnameTarget)>();
                int parsedCount = 0;
                for (int i = 0; i < answerCount; i++)
                {
                    if (offset >= data.Length) break;
                    var parsedNameResult = ParseName(data, offset);
                    string name = parsedNameResult.Name;
                    offset = parsedNameResult.NextOffset;

                    if (offset + 10 > data.Length)
                    {
                        return new DNSResponse { Status = DNSResponseStatus.MalformedResponse, ErrorMessage = "Answer header exceeds packet bounds." };
                    }
                    ushort type = (ushort)((data[offset] << 8) | data[offset + 1]);
                    ushort cls = (ushort)((data[offset + 2] << 8) | data[offset + 3]);
                    uint ttl = (uint)((data[offset + 4] << 24) |
                                      (data[offset + 5] << 16) |
                                      (data[offset + 6] << 8) |
                                      data[offset + 7]);
                    int rdLength = (data[offset + 8] << 8) | data[offset + 9];
                    offset += 10;

                    if (offset + rdLength > data.Length)
                    {
                        return new DNSResponse { Status = DNSResponseStatus.MalformedResponse, ErrorMessage = "RDATA exceeds packet bounds." };
                    }

                    string rdataStr = FormatRData(data, offset, rdLength, type);
                    // A CNAME's RDATA is a single (possibly compressed) domain name — decode it so the
                    // owner chain can be tracked (B070). Non-CNAME RRs carry no chain target.
                    string? cnameTarget = null;
                    if (cls == 1 && type == 5)
                    {
                        try { cnameTarget = ParseName(data, offset).Name.ToLowerInvariant(); }
                        catch (FormatException) { cnameTarget = null; }
                    }
                    raws.Add((name, name.ToLowerInvariant(), type, cls, ttl, rdataStr, cnameTarget));

                    offset += rdLength;
                    parsedCount++;
                }
                // B068/A077: declared ANCOUNT > actually parsed records -> corrupt/truncated
                if (parsedCount < answerCount)
                    return new DNSResponse { Status = DNSResponseStatus.MalformedResponse, ErrorMessage = $"Declared ANCOUNT {answerCount} exceeds parsed records {parsedCount}." };

                // B070 — build the set of owner names that legitimately belong to this answer: the question
                // name plus every CNAME target reachable from it (fixpoint, so an out-of-order CNAME chain
                // is still resolved). An in-class address RR whose owner is outside this set is a smuggled
                // off-name record and must not be surfaced as a resolved address.
                var validOwners = new HashSet<string>();
                if (questionName != null) validOwners.Add(questionName);
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (var r in raws)
                    {
                        if (r.Cls == 1 && r.Type == 5 && validOwners.Contains(r.OwnerLower)
                            && r.CnameTarget != null && !validOwners.Contains(r.CnameTarget))
                        {
                            validOwners.Add(r.CnameTarget);
                            grew = true;
                        }
                    }
                }

                // Phase 2 — apply the class (B069), owner-name (B070) and type filters.
                foreach (var r in raws)
                {
                    // B069: reject off-class RRs to prevent class-smuggled addresses.
                    if (r.Cls != 1) continue;
                    // B070: an in-class A/AAAA whose owner is not in the question/CNAME chain is off-name.
                    // (Only enforced when the question name is known, so a parse gap never drops everything.)
                    if ((r.Type == 1 || r.Type == 28) && validOwners.Count > 0 && !validOwners.Contains(r.OwnerLower))
                    {
                        offNameCount++;
                        continue;
                    }
                    // Surface answers of the requested type (M04), CNAMEs (needed for chain-following) and
                    // RRSIGs (the DNSSEC signature companion of a DO-enabled answer - it carries no address
                    // and never substitutes the queried answer, so it is not off-type poisoning; the DNSSEC
                    // verifier needs it to check the covering signature over the DNSKEY RRset - A030/A036).
                    // Count off-type answers separately so callers can detect poisoning attempts (KDNS-06).
                    if (!requestedType.HasValue || r.Type == requestedType.Value || r.Type == 5 || r.Type == 46)
                    {
                        records.Add(new DNSRecord { Name = r.Name, Type = r.Type, Ttl = r.Ttl, Rdata = r.Rdata });
                    }
                    else
                    {
                        offTypeCount++;
                    }
                }
            }
            catch (FormatException ex)
            {
                // A malformed compression pointer/loop in a question or answer name must yield a
                // typed MalformedResponse, never an unhandled throw (matches the macOS parser).
                return new DNSResponse { Status = DNSResponseStatus.MalformedResponse, ErrorMessage = ex.Message };
            }

            var answerRecords = !requestedType.HasValue
                ? records
                : records.Where(r => r.Type == requestedType.Value).ToList();

            // `offset` now sits at the start of the authority section (after the last answer).
            var authAtEnd = ParseRecordsBestEffort(data, offset, authorityCount);
            return new DNSResponse
            {
                Status = answerRecords.Count == 0 ? DNSResponseStatus.NoData : DNSResponseStatus.Success,
                Records = records,
                AuthorityRecords = authAtEnd.Records,
                AuthorityMalformed = authorityCount > 0 && authAtEnd.Records.Count < authorityCount,
                Edns = ednsInfo,
                EdnsMalformed = ednsMalformed,
                OffTypeAnswerCount = offTypeCount,
                OffNameAnswerCount = offNameCount,
                ResponseCode = combinedResponseCode,
                IsAuthoritative = isAuthoritative,
                IsTruncated = truncated,
                RecursionAvailable = recursionAvailable,
                AuthenticatedData = authenticatedData,
                ResponseByteLength = data.Length
            };
        }

        /// Renders RDATA for display/comparison. A/AAAA get dotted / canonical `IPAddress` form; every
        /// other type is a lowercase hex dump. Shared by the answer loop and the authority parser.
        private static string FormatRData(byte[] data, int offset, int rdLength, ushort type)
        {
            if (type == 1 && rdLength == 4) // A Record (IPv4)
            {
                return $"{data[offset]}.{data[offset + 1]}.{data[offset + 2]}.{data[offset + 3]}";
            }
            // C017: name-typed RDATA (NS=2, CNAME=5, PTR=12, DNAME=39) must be decoded against the
            // COMPLETE packet, not hex-dumped. Compression-pointer offsets are packet-local, so the
            // hex of two equal targets need not compare equal — chain reconstruction keyed on hex
            // could not detect real CNAME rewriting, and NS hostnames were unreadable downstream.
            if (type is 2 or 5 or 12 or 39)
            {
                try { return ParseName(data, offset).Name.ToLowerInvariant(); }
                catch (FormatException) { /* fall through to the hex rendering below */ }
            }
            if (type == 28 && rdLength == 16) // AAAA Record (IPv6)
            {
                // Canonical, ::-compressed lowercase form via IPAddress, so the same address always
                // compares equal to a reference resolver's rendering (a naive per-group hex join does
                // not compress zero runs and yields spurious mismatches).
                var v6 = new byte[16];
                Array.Copy(data, offset, v6, 0, 16);
                return new IPAddress(v6).ToString();
            }
            var sb = new StringBuilder();
            for (int j = 0; j < rdLength; j++)
            {
                sb.Append(data[offset + j].ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// Skips `questionCount` question entries (name + type + class), returning the offset just past
        /// them, or null if the section runs out of bounds. Used to locate the authority section.
        private static int? SkipQuestions(byte[] data, int offset, int questionCount)
        {
            int off = offset;
            for (int i = 0; i < questionCount; i++)
            {
                int next;
                try { next = SkipName(data, off); }
                catch (FormatException) { return null; }
                off = next + 4; // QTYPE + QCLASS
                if (off > data.Length) return null;
            }
            return off;
        }

        /// Parses up to `count` resource records from `offset`, stopping early on any bounds/parse error
        /// (best-effort — used for the authority section, which must never fail the whole response).
        /// Returns the records parsed and the offset just past them.
        private static (List<DNSRecord> Records, int Next) ParseRecordsBestEffort(byte[] data, int offset, int count)
        {
            var records = new List<DNSRecord>();
            int off = offset;
            for (int i = 0; i < count; i++)
            {
                if (off >= data.Length) break;
                string name;
                int p;
                try
                {
                    var parsed = ParseName(data, off);
                    name = parsed.Name;
                    p = parsed.NextOffset;
                }
                catch (FormatException) { break; }
                if (p + 10 > data.Length) break;
                ushort type = (ushort)((data[p] << 8) | data[p + 1]);
                uint ttl = (uint)((data[p + 4] << 24) | (data[p + 5] << 16) | (data[p + 6] << 8) | data[p + 7]);
                int rdLength = (data[p + 8] << 8) | data[p + 9];
                p += 10;
                if (p + rdLength > data.Length) break;
                records.Add(new DNSRecord { Name = name, Type = type, Ttl = ttl, Rdata = FormatRData(data, p, rdLength, type) });
                off = p + rdLength;
            }
            return (records, off);
        }

        /// Walks past the question + answer sections to the authority records. Best-effort; empty on
        /// any parse failure.
        private static List<DNSRecord> ParseAuthorityBestEffort(byte[] data, int questionCount, int answerCount, int authorityCount)
        {
            int? afterQuestions = SkipQuestions(data, 12, questionCount);
            if (afterQuestions == null) return new List<DNSRecord>();
            int afterAnswers = ParseRecordsBestEffort(data, afterQuestions.Value, answerCount).Next;
            return ParseRecordsBestEffort(data, afterAnswers, authorityCount).Records;
        }

        /// Scans up to `count` additional-section records from `offset` for an EDNS0 OPT record (type 41).
        /// For OPT, the CLASS field carries the requestor UDP size and the TTL carries version + extended
        /// RCODE (RFC 6891). Best-effort; null if no OPT is present.
        private static (EDNSResponseInfo? Info, bool Malformed) FindOPTRecord(byte[] data, int offset, int count)
        {
            int off = offset;
            for (int i = 0; i < count; i++)
            {
                // The additional section declared `count` records but one could not be parsed within
                // bounds. We cannot confirm an OPT is absent → malformed (EDNS support UNKNOWN),
                // never a confident "no EDNS".
                if (off >= data.Length) return (null, true);
                string name;
                int p;
                try
                {
                    var parsed = ParseName(data, off);
                    name = parsed.Name;
                    p = parsed.NextOffset;
                }
                catch (FormatException) { return (null, true); }
                if (p + 10 > data.Length) return (null, true);
                ushort type = (ushort)((data[p] << 8) | data[p + 1]);
                ushort cls = (ushort)((data[p + 2] << 8) | data[p + 3]);
                uint ttl = (uint)((data[p + 4] << 24) | (data[p + 5] << 16) | (data[p + 6] << 8) | data[p + 7]);
                int rdLength = (data[p + 8] << 8) | data[p + 9];
                p += 10;
                if (p + rdLength > data.Length) return (null, true);
                if (type == 41) // OPT
                {
                    return (new EDNSResponseInfo(
                        UdpPayloadSize: cls,
                        Version: (byte)((ttl >> 16) & 0xff),
                        ExtendedRCode: (byte)((ttl >> 24) & 0xff),
                        EcsScopePrefixLength: EcsScope(data, p, rdLength)), false);
                }
                off = p + rdLength;
            }
            // Walked every declared additional record cleanly and found no OPT → genuinely absent.
            return (null, false);
        }

        /// Scans an OPT record's RDATA (a sequence of [code(2)][len(2)][data] options) for an EDNS
        /// Client Subnet option (code 8) and returns its SCOPE PREFIX-LENGTH (F10). A non-zero scope means
        /// the resolver applied/forwarded the subnet. null if no ECS option is present.
        private static byte? EcsScope(byte[] data, int rdataStart, int rdLength)
        {
            int o = rdataStart;
            int end = rdataStart + rdLength;
            while (o + 4 <= end)
            {
                int code = (data[o] << 8) | data[o + 1];
                int len = (data[o + 2] << 8) | data[o + 3];
                int dataStart = o + 4;
                if (dataStart + len > end) break;
                if (code == 8 && len >= 4) // ECS: family(2) + srcPrefix(1) + scopePrefix(1) + address
                {
                    return data[dataStart + 3]; // SCOPE PREFIX-LENGTH
                }
                o = dataStart + len;
            }
            return null;
        }

        private static int SkipName(byte[] data, int offset)
        {
            int current = offset;
            var visited = new HashSet<int>();
            while (current < data.Length)
            {
                if (!visited.Add(current))
                {
                    throw new FormatException("DNS name compression loop detected.");
                }
                int len = data[current];
                if (len == 0) return current + 1;
                if ((len & 0xC0) == 0xC0)
                {
                    if (current + 1 >= data.Length)
                    {
                        throw new FormatException("DNS compression pointer exceeds packet bounds.");
                    }
                    return current + 2;
                }
                if ((len & 0xC0) != 0 || len > 63 || current + 1 + len > data.Length)
                {
                    throw new FormatException("Malformed DNS label.");
                }
                current += len + 1;
            }
            throw new FormatException("DNS name exceeds packet bounds.");
        }

        private static (string Name, int NextOffset) ParseName(byte[] data, int offset)
        {
            var parts = new List<string>();
            int current = offset;
            bool jumped = false;
            int nextOffset = offset;
            bool terminated = false;

            var visited = new HashSet<int>();
            while (current < data.Length)
            {
                if (!visited.Add(current))
                {
                    throw new FormatException("DNS name compression loop detected.");
                }
                int len = data[current];
                if (len == 0)
                {
                    if (!jumped) nextOffset = current + 1;
                    terminated = true;
                    break;
                }
                else if ((len & 0xC0) == 0xC0)
                {
                    if (current + 1 >= data.Length)
                    {
                        throw new FormatException("DNS compression pointer exceeds packet bounds.");
                    }
                    int pointer = ((len & 0x3F) << 8) | data[current + 1];
                    if (pointer >= data.Length)
                    {
                        throw new FormatException("DNS compression pointer targets outside packet.");
                    }
                    if (!jumped)
                    {
                        nextOffset = current + 2;
                        jumped = true;
                    }
                    current = pointer;
                }
                else
                {
                    if ((len & 0xC0) != 0 || len > 63 || current + 1 + len > data.Length)
                    {
                        throw new FormatException("Malformed DNS label.");
                    }
                    // E022: reject (not substitute) labels that are not valid UTF-8. DNS labels
                    // are arbitrary octets (RFC 4343); silently substituting U+FFFD fabricates a
                    // different name that can bypass the B070 off-name owner filter. A hostile
                    // response must produce a malformed result, never a colliding name. Parity
                    // with macOS DNSResolver.swift which throws on invalid UTF-8.
                    var labelBytes = new ReadOnlySpan<byte>(data, current + 1, len);
                    if (!System.Text.Unicode.Utf8.IsValid(labelBytes))
                    {
                        throw new FormatException("DNS label contains non-UTF-8 bytes — rejecting to prevent name fabrication");
                    }
                    string label = Encoding.UTF8.GetString(data, current + 1, len);
                    parts.Add(label);
                    current += len + 1;
                    if (!jumped) nextOffset = current;
                }
            }

            // E018: unterminated or truncated name (no 0x00 terminator before end of buffer)
            if (!terminated)
            {
                throw new FormatException("DNS name exceeds packet bounds or is unterminated.");
            }

            return (string.Join(".", parts), nextOffset);
        }

        /// Converts an internationalized (non-ASCII) domain name to its IDNA/punycode ASCII form
        /// so IDN test domains are queried on the wire exactly as a resolver expects, letting
        /// tampering/censorship of non-ASCII domains be detected. ASCII names pass through unchanged.
        ///
        /// NOTE: lowercasing is applied at the DomainCanonical.CanonicalizeOrNull boundary, NOT
        /// inside this method. ToAsciiName returns pure-ASCII input un-lowercased (it only
        /// transforms non-ASCII labels), so case-sensitive DNS callers (e.g. 0x20 encoding)
        /// are unaffected. The Swift twin has the same contract — see
        /// DomainCanonical.swift:54-58.
        public static string ToAsciiName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            bool isAscii = true;
            foreach (char c in name)
            {
                if (c > 127) { isAscii = false; break; }
            }
            if (isAscii) return name;
            // Normalize full IDNA separator set to ASCII dot before encoding.
            // Swift's hand-rolled RFC 3492 encoder splits on U+3002, U+FF0E, U+FF61
            // in addition to U+002E; IdnMapping only recognizes ASCII dot.
            name = name.Replace('\u3002', '.').Replace('\uFF0E', '.').Replace('\uFF61', '.');
            try
            {
                return new IdnMapping().GetAscii(name.TrimEnd('.'));
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"Invalid internationalized domain name: '{name}' — contains non-ASCII characters that cannot be encoded as IDNA.");
            }
        }

        private static void ValidateDomainName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new FormatException("DNS name is empty.");
            }

            // K-19: trim leading dots as well as trailing. `MakeQueryPacket` below already splits on
            // `name.Trim('.')`, so a leading dot was encoded correctly on the wire while THIS
            // validator rejected the same input — the guard was stricter than the encoder it guards.
            // macOS `validateDomainName` trims both ends too, so ".example.com" was accepted there
            // and refused here for identical bytes. (C116 covers the trailing-dot half.)
            string trimmed = name.Trim('.');
            if (Encoding.UTF8.GetByteCount(trimmed) > 253)
            {
                throw new FormatException("DNS name exceeds 253 bytes.");
            }

            foreach (var label in trimmed.Split('.'))
            {
                int length = Encoding.UTF8.GetByteCount(label);
                if (length == 0 || length > 63)
                {
                    throw new FormatException("DNS label length is invalid.");
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Syxx.Kit
{
    /// A three-valued answer for network conditions that genuinely can be unknown. Reporting
    /// Unknown (with evidence) is the point of the condition-state rework: a probe error must not be
    /// silently reported as "no VPN" / "no captive portal" / "encrypted" (W11/W12/W13).
    ///
    /// <para>Distinct from <see cref="TriState"/> (success/failure/inconclusive), which describes
    /// whether a check reached a conclusion rather than what the answer was.</para>
    [JsonConverter(typeof(ConditionStateJsonConverter))]
    public enum ConditionState
    {
        Yes,
        No,
        Unknown
    }

    /// <summary>T-01: the explicit wire converter <see cref="ConditionState"/> was missing.
    ///
    /// <para>It was the one tri-state in the kit with no converter, so System.Text.Json fell back
    /// to its default — the enum's ORDINAL NUMBER. `Trust.NetworkConfigSnapshot.VpnActive` is
    /// persisted with a bare <c>JsonSerializer.Serialize</c> and read back by
    /// <c>ContinuousMonitor</c> to compare VPN state across samples, so the meaning of that field
    /// was positional: inserting or reordering a member would silently reinterpret every stored
    /// baseline, turning "VPN was Unknown" into "VPN was active" with nothing to notice it. The
    /// values here are the Swift twin's raw values (<c>ConditionState: String</c> — yes/no/unknown
    /// in NetworkKeyState.swift), so the two platforms now agree on the spelling.</para>
    ///
    /// <para>Reading also accepts the legacy numeric form, because baselines written before this
    /// converter existed are on disk today and a failed read is reported as a CORRUPTED baseline
    /// (<c>Trust.Load</c>), i.e. a visible loss of the user's home baseline. Accepting the old
    /// encoding on read is a migration, not laxity: an unrecognised value still throws rather
    /// than being quietly folded into Unknown.</para></summary>
    public sealed class ConditionStateJsonConverter : JsonConverter<ConditionState>
    {
        public override ConditionState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                // Legacy: the ordinal System.Text.Json wrote before this converter existed.
                return reader.GetInt32() switch
                {
                    0 => ConditionState.Yes,
                    1 => ConditionState.No,
                    2 => ConditionState.Unknown,
                    var n => throw new JsonException($"unknown ConditionState ordinal '{n}'"),
                };
            }
            return reader.GetString() switch
            {
                "yes" => ConditionState.Yes,
                "no" => ConditionState.No,
                "unknown" => ConditionState.Unknown,
                var s => throw new JsonException($"unknown ConditionState '{s}'"),
            };
        }

        public override void Write(Utf8JsonWriter writer, ConditionState value, JsonSerializerOptions options)
            => writer.WriteStringValue(value switch
            {
                ConditionState.Yes => "yes",
                ConditionState.No => "no",
                ConditionState.Unknown => "unknown",
                _ => throw new JsonException($"unhandled ConditionState '{value}'"),
            });
    }

    public class VPNStatus
    {
        public ConditionState State { get; set; }
        public List<string> TunnelInterfaces { get; set; } = new List<string>();
        public string Evidence { get; set; } = string.Empty;
    }

    public class CaptivePortalStatus
    {
        public ConditionState State { get; set; } // Yes = portal intercepting, No = clear, Unknown = offline/error
        public string Evidence { get; set; } = string.Empty;
    }

    public class EncryptedDNSStatus
    {
        public ConditionState State { get; set; }
        public string Evidence { get; set; } = string.Empty;

        /// The transport the system is CONFIGURED to use, when the OS actually named one, and null
        /// when it did not. A surface may name a protocol only when this is non-null, and must say
        /// that the naming is configuration, not observation.
        ///
        /// PR-06 (publication audit 2026-07-24) / D279: the macOS dashboard printed "DoT · TLS" for
        /// every Yes, so a machine configured for DoH was told it was using DoT. DoH, DoT, DoQ and
        /// DNSCrypt are different transports with different exposure, and the code did not know
        /// which was in use. Populated only from an observed ScopedResolver.Protocol; never guessed.
        /// macOS twin: EncryptedDNSStatus.configuredTransport (NetworkKeyState.swift:28).
        public DnsProtocol? ConfiguredTransport { get; set; }
    }

    /// The protocol used for DNS transport as reported by the OS (scutil, DoH registry, resolvectl).
    public enum DnsProtocol
    {
        Doh,
        Dot
    }

    /// A DNS resolver together with the interface it was discovered on.
    public class ScopedResolver
    {
        public string Address { get; set; } = string.Empty;
        public string? Interface { get; set; }
        public List<string> MatchDomains { get; set; } = new List<string>();
        public bool IsScoped { get; set; }
        public DnsProtocol? Protocol { get; set; }
        public ushort? Port { get; set; }
        /// D100: whether the encrypted-transport configuration was readable, and what it said.
        /// Yes = an encrypted transport is configured. No = the configuration was read and names
        /// none. Unknown = the read failed, so the result must be Unknown, not No.
        ///
        /// <para>Sources per platform: Windows reads the Dnscache DoH template registry key;
        /// Linux reads the `Protocols:` / `DNSOverTLS setting:` field of `resolvectl status`.
        /// systemd-resolved's <c>opportunistic</c> DoT mode maps to Unknown, because it silently
        /// falls back to cleartext and the client cannot tell which happened. `/etc/resolv.conf`
        /// carries no transport information at all, so resolvers sourced from it are Unknown.</para>
        ///
        /// <para><b>The default is Unknown, not No.</b> It used to default to No, which meant a
        /// resolver nobody had filled in asserted "the transport configuration was read and names
        /// no encryption" — a positive factual claim obtained by leaving a field alone. Spine #3
        /// requires the tri-state to survive inside fakes and test doubles too, and that default
        /// collapsed Unknown into No in exactly the doubles. A caller that HAS read the
        /// configuration must now say so explicitly.</para>
        public ConditionState DoHReadState { get; set; } = ConditionState.Unknown;
    }

    public class NetworkStateReport
    {
        public List<string> DnsServers { get; set; } = new List<string>();
        public List<string> ActiveInterfaces { get; set; } = new List<string>();
        public bool VpnActive { get; set; }
        public bool CaptivePortalDetected { get; set; }
        // Typed views (preferred). The plain booleans above are retained for compatibility.
        public VPNStatus Vpn { get; set; } = new VPNStatus { State = ConditionState.No, Evidence = string.Empty };
        public CaptivePortalStatus CaptivePortal { get; set; } = new CaptivePortalStatus { State = ConditionState.Unknown, Evidence = string.Empty };
        public EncryptedDNSStatus EncryptedDNS { get; set; } = new EncryptedDNSStatus { State = ConditionState.Unknown, Evidence = string.Empty };
        public List<ScopedResolver> ScopedResolvers { get; set; } = new List<ScopedResolver>();
        // C1 — Router/gateway DNS audit info. Null when detection could not run.
        public GatewayInfo? Gateway { get; set; }
    }

    /// Where a Linux resolver reading came from — and, when it produced nothing, why.
    ///
    /// This is not decoration. On the majority of Ubuntu Desktop installs `/etc/resolv.conf`
    /// names only the systemd-resolved stub listener (127.0.0.53), which forwards to upstream
    /// servers it does not disclose there. Accepting the stub as "the system resolver" points
    /// every comparison, leak and attribution probe at a loopback forwarder instead of the
    /// resolver actually answering, and the result looks measured. Distinguishing
    /// <see cref="SystemdStubOnly"/> from <see cref="Resolvectl"/> is what lets the caller
    /// report inconclusive instead.
    public enum LinuxResolverSource
    {
        /// `resolvectl status` named real upstream servers (Global and/or per-link).
        Resolvectl,
        /// `/etc/resolv.conf` named real (non-stub) servers.
        ResolvConf,
        /// The only resolver found was the systemd-resolved stub. The real upstream is UNKNOWN —
        /// this must never be reported as a measured resolver.
        SystemdStubOnly,
        /// Nothing readable at all.
        Undetermined,
    }

    /// The structured outcome of reading Linux resolver configuration from TEXT — the seam that
    /// separates parsing from process execution and platform detection, so the parsers can be
    /// driven by a golden corpus of real-shaped `resolvectl status` output on any host.
    public sealed class LinuxResolverReading
    {
        public List<ScopedResolver> Resolvers { get; set; } = new List<ScopedResolver>();
        public LinuxResolverSource Source { get; set; } = LinuxResolverSource.Undetermined;
        /// Human-readable account of what was read and what could not be determined. Never empty.
        public string Evidence { get; set; } = string.Empty;
    }

    /// C1 — detected state of the local gateway/router (macOS GatewayInfo twin).
    public class GatewayInfo
    {
        /// The default gateway IP address (e.g. "192.168.1.1"). Empty when undeterminable.
        public string Ip { get; set; } = string.Empty;
        /// Whether the gateway IP appears as one of the system's DNS servers.
        public bool IsDNS { get; set; }
        /// Whether the router admin panel (HTTP on port 80) is reachable.
        public ConditionState AdminReachable { get; set; } = ConditionState.Unknown;
        /// The DNS servers pushed by DHCP. On Windows this is the interface's registry
        /// DhcpNameServer value — genuinely DHCP-sourced, distinct from a static override.
        public List<string> DhcpPushedDNS { get; set; } = new List<string>();
        /// Human-readable probe detail.
        public string Detail { get; set; } = string.Empty;
    }

    public static class NetworkKeyState
    {
        // Captive-portal probe must not silently follow a redirect to a portal login page —
        // that would hide the interception this check exists to catch.
        // Timeout is consistent with DNSResolver._httpClient (8s) and ContentWarp.client (10s).
        // A mid-point 9s ensures no single component is an outlier under constrained networks.
        private static readonly HttpClient _probeClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = TimeSpan.FromSeconds(9)
        };

        // Adapter description substrings that indicate a hypervisor/loopback pseudo-adapter,
        // never a real VPN tunnel, even though some also contain "virtual" (W11).
        private static readonly string[] HypervisorMarkers =
        {
            "hyper-v", "vmware", "virtualbox", "virtual switch", "loopback", "npcap", "bluetooth"
        };

        // MARK: - Resolver discovery (W10)

        /// Returns discovered DNS servers ordered so resolvers on an interface with a default
        /// gateway (i.e. on the active route) come first, instead of an unordered dump of every
        /// up interface's resolvers regardless of whether that interface carries traffic.
        public static List<string> GetSystemDNSServers()
        {
            var scoped = GetScopedResolvers();
            var ordered = new List<string>();
            foreach (var resolver in scoped.OrderBy(r => r.IsScoped ? 1 : 0))
            {
                if (!ordered.Contains(resolver.Address))
                {
                    ordered.Add(resolver.Address);
                }
            }
            return ordered;
        }

        /// Parses per-interface resolver configuration, preserving which interface each resolver
        /// belongs to and whether that interface is on the active default route.
        public static List<ScopedResolver> GetScopedResolvers()
        {
            if (OperatingSystem.IsLinux())
                return GetScopedResolversFromResolvectl();
            return GetScopedResolversFromNetworkInterfaces();
        }

        /// Windows (and non-Linux fallback): enumerate adapters and attach DoH template protocol when configured.
        internal static List<ScopedResolver> GetScopedResolversFromNetworkInterfaces()
        {
            var resolvers = new List<ScopedResolver>();
            // C137: if the adapter enumeration fails, return empty — callers should treat this
            // as "could not determine" via VpnStatus/EncryptedDNSStatus Unknown paths.
            var (enumerated, interfaces) = SafeGetInterfaces();
            if (!enumerated) return resolvers;
            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;

                IPInterfaceProperties props;
                try
                {
                    props = iface.GetIPProperties();
                }
                catch
                {
                    continue;
                }

                bool onActiveRoute = props.GatewayAddresses.Any(g => g.Address != null && !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
                var domains = string.IsNullOrEmpty(props.DnsSuffix) ? new List<string>() : new List<string> { props.DnsSuffix };

                foreach (var dns in props.DnsAddresses)
                {
                    string address = dns.ToString();
                    if (!IsUsableDNSServer(address)) continue;

                    DnsProtocol? protocol = null;
                    ushort? port = null;
                    // Only Windows actually reads a transport configuration on this path (the
                    // Dnscache DoH template registry key). Everywhere else this code path inspects
                    // adapter addresses and nothing else, so the transport is genuinely
                    // undetermined — it used to claim No, which reads as "we checked and your DNS
                    // is not encrypted" on a platform where nothing was checked.
                    ConditionState dohReadState = ConditionState.Unknown;
                    if (OperatingSystem.IsWindows())
                    {
                        dohReadState = HasConfiguredDoHTemplate(address);
                        if (dohReadState == ConditionState.Yes)
                        {
                            protocol = DnsProtocol.Doh;
                            port = 443;
                        }
                    }

                    resolvers.Add(new ScopedResolver
                    {
                        Address = address,
                        Interface = iface.Name,
                        MatchDomains = domains,
                        IsScoped = !onActiveRoute,
                        Protocol = protocol,
                        Port = port,
                        DoHReadState = dohReadState
                    });
                }
            }
            return resolvers;
        }

        // MARK: - Linux resolver discovery seam (E1)
        //
        // Everything below this line up to ParseResolvConfNameservers is deliberately split into
        // three layers so the parsing can be tested without a Linux host:
        //
        //   GetLinuxResolverReading()  — I/O:   runs `resolvectl status`, reads /etc/resolv.conf
        //   ReadLinuxResolvers(...)    — PURE:  text in, structured resolver reading out
        //   ParseResolvectlStatus(...) — PURE:  one `resolvectl status` document
        //   ParseResolvConfNameservers — PURE:  one resolv.conf document
        //
        // The defect this seam exists to make testable: before it, the only tests covering Linux
        // resolver discovery hand-wrote `resolvectl` formats that systemd has never emitted, so
        // they were green against a parser that could not read a real machine (P-22).

        /// Linux: read resolver configuration from `resolvectl status`, falling back to
        /// `/etc/resolv.conf`. I/O only — all parsing and all decisions live in
        /// <see cref="ReadLinuxResolvers"/>.
        internal static LinuxResolverReading GetLinuxResolverReading()
            => ReadLinuxResolvers(
                RunProcess("resolvectl", new[] { "status", "--no-pager" }, timeoutSeconds: 3.0),
                ReadTextFileOrNull("/etc/resolv.conf"));

        /// Linux: read per-link DNS transport from `resolvectl status` when systemd-resolved is present.
        internal static List<ScopedResolver> GetScopedResolversFromResolvectl()
            => GetLinuxResolverReading().Resolvers;

        /// PURE. Composes the two Linux resolver documents into one reading. No process
        /// execution, no filesystem, no platform detection — pass either argument null to model
        /// "that source was unavailable".
        internal static LinuxResolverReading ReadLinuxResolvers(string? resolvectlStatusOutput, string? resolvConfContent)
        {
            if (!string.IsNullOrWhiteSpace(resolvectlStatusOutput))
            {
                var parsed = ParseResolvectlStatus(resolvectlStatusOutput);
                if (parsed.Count > 0)
                {
                    return new LinuxResolverReading
                    {
                        Resolvers = parsed,
                        Source = LinuxResolverSource.Resolvectl,
                        Evidence = $"resolvectl status reported {parsed.Count} resolver(s): {string.Join(", ", parsed.Select(r => r.Address))}"
                    };
                }
            }

            var nameservers = ParseResolvConfNameservers(resolvConfContent);
            var upstream = nameservers.Where(n => !IsSystemdResolvedStub(n)).ToList();
            if (upstream.Count > 0)
            {
                return new LinuxResolverReading
                {
                    Resolvers = upstream
                        .Select(address => new ScopedResolver
                        {
                            Address = address,
                            Interface = null,
                            IsScoped = false,
                            // resolv.conf names an address and nothing else. Whether that server is
                            // reached over TLS is not knowable from this file, so the transport read
                            // is Unknown — not a silent "plaintext".
                            DoHReadState = ConditionState.Unknown
                        })
                        .ToList(),
                    Source = LinuxResolverSource.ResolvConf,
                    Evidence = $"/etc/resolv.conf named {upstream.Count} nameserver(s): {string.Join(", ", upstream)}"
                };
            }

            if (nameservers.Count > 0)
            {
                // Every nameserver in resolv.conf is a systemd-resolved stub listener, and
                // resolvectl either did not run or told us nothing. The real upstream resolvers
                // exist and are simply not visible from here.
                //
                // Returning them would be the false-clean this seat exists to remove: every
                // comparison, leak and attribution probe would query the loopback forwarder,
                // succeed, and print a result that looks measured while saying nothing about the
                // resolver actually answering. Honesty spine #2 — missing data is not clean data —
                // so this reports nothing and says why.
                return new LinuxResolverReading
                {
                    Source = LinuxResolverSource.SystemdStubOnly,
                    Evidence = $"/etc/resolv.conf names only the systemd-resolved stub listener "
                             + $"({string.Join(", ", nameservers)}), which forwards to upstream servers it does not "
                             + "disclose. `resolvectl status` did not report the upstream servers, so the system "
                             + "resolver is undetermined — not measured. Run `resolvectl status --no-pager` to see it."
                };
            }

            return new LinuxResolverReading
            {
                Source = LinuxResolverSource.Undetermined,
                Evidence = "Neither resolvectl status nor /etc/resolv.conf yielded a usable resolver"
            };
        }

        /// The systemd-resolved local stub listeners. `127.0.0.53` is the standard stub; `127.0.0.54`
        /// is the "proxy" stub added in systemd v248, which forwards verbatim without local
        /// resolution. Neither is a resolver in any sense Syxx can measure — both are forwarders to
        /// an upstream they do not name.
        ///
        /// <para>This is deliberately NOT a blanket loopback exclusion. A loopback address that is
        /// not one of these is a local resolver the user genuinely runs — dnsmasq, unbound, pihole,
        /// stubby — and that IS the resolver answering their queries. Dropping it would blind the
        /// comparison and leak probes on exactly the machines whose owners took the most care.
        /// <see cref="EncryptedDNSStatusCore"/> already reports such a resolver as Unknown ("upstream
        /// transport not verifiable"), which is the honest treatment.</para>
        internal static bool IsSystemdResolvedStub(string address)
            => string.Equals(address, "127.0.0.53", StringComparison.Ordinal)
            || string.Equals(address, "127.0.0.54", StringComparison.Ordinal);

        /// PURE. Returns the `nameserver` entries of a resolv.conf document, in file order,
        /// de-duplicated, and filtered to addresses that could be a real resolver.
        internal static List<string> ParseResolvConfNameservers(string? content)
        {
            var servers = new List<string>();
            if (string.IsNullOrWhiteSpace(content)) return servers;
            foreach (var rawLine in content.Split('\n'))
            {
                var line = rawLine.Trim();
                // resolv.conf comments are ';' or '#' in the first column (resolv.conf(5)).
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                if (!line.StartsWith("nameserver", StringComparison.Ordinal)) continue;
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !string.Equals(parts[0], "nameserver", StringComparison.Ordinal)) continue;
                var address = parts[1];
                if (!IsUsableDNSServer(address)) continue;
                if (!servers.Contains(address)) servers.Add(address);
            }
            return servers;
        }

        /// Reads a text file, returning null when it cannot be read. Separated so
        /// <see cref="ReadLinuxResolvers"/> can stay pure.
        private static string? ReadTextFileOrNull(string path)
        {
            try { return File.ReadAllText(path); }
            catch { return null; }
        }

        /// PURE. Parses a `resolvectl status` document into scoped resolvers.
        ///
        /// <para><b>P-06 (Aug 2026).</b> The parser this replaces could not read the output
        /// systemd-resolved actually emits. Four defects, each independently sufficient to make
        /// Linux resolver discovery return nothing:</para>
        /// <list type="number">
        /// <item>`if (currentInterface == null) continue;` skipped every line before the first
        ///   `Link` header, which made the <b>Global</b> block unreachable — and the Global block
        ///   is exactly where `resolved.conf` / `resolvectl dns ""` servers appear.</item>
        /// <item>The `DNSOverHTTPS:` branch was dead by construction: systemd-resolved implements
        ///   DoT and has no DoH support at all, so that literal cannot occur.</item>
        /// <item>The DoT branch matched the literal `"DNS Over TLS:"`, which systemd has never
        ///   printed. Real output is an inline `Protocols:` token (`+DNSOverTLS` / `-DNSOverTLS` /
        ///   `DNSOverTLS=opportunistic`, systemd v249+) or a `DNSOverTLS setting:` line (v245-era).</item>
        /// <item>Multi-server lines were taken whole: `DNS Servers: 192.168.1.1 8.8.8.8` became one
        ///   token that parses as no valid address, so the link contributed <b>zero</b> resolvers.
        ///   systemd renders that row with `TABLE_STRV_WRAPPED`, i.e. space-separated on one line,
        ///   wrapping onto unlabelled continuation lines when the terminal is narrow — both forms
        ///   are handled here.</item>
        /// </list>
        ///
        /// <para>Format authority: systemd `src/resolve/resolvectl.c` (`print_configuration`,
        /// `dump_list`, `strv_extend_extended_bool`). Corpus and provenance:
        /// <c>corpus/resolvectl-status/</c>.</para>
        internal static List<ScopedResolver> ParseResolvectlStatus(string output)
        {
            var resolvers = new List<ScopedResolver>();

            // Per-block accumulator. A "block" is the Global section or one `Link N (name)` section.
            string? blockInterface = null;
            bool inBlock = false;
            bool onDefaultRoute = true;      // Global is by definition not scoped to one link.
            bool dnsScopeActive = true;      // Until a `Current Scopes:` line says otherwise.
            DnsProtocol? blockProtocol = null;
            // Tri-state transport read. Unknown is the correct default: until a Protocols /
            // DNSOverTLS line is seen we have NOT determined the transport, and "we did not look"
            // must never render as "we looked and it is plaintext".
            ConditionState transportRead = ConditionState.Unknown;
            var blockServers = new List<string>();
            var blockCurrentServer = new List<string>();
            var blockDomains = new List<string>();
            // Which multi-value field the next unlabelled continuation line belongs to. Without
            // this, the continuation lines of `DNSSEC NTA:` would be appended to `DNS Servers:`.
            string? continuationField = null;

            void FlushBlock()
            {
                if (inBlock && dnsScopeActive)
                {
                    // `Current DNS Server:` is the one systemd has currently selected out of
                    // `DNS Servers:`; using both would double-count it. It only adds information
                    // when the full list is absent.
                    var tokens = blockServers.Count > 0 ? blockServers : blockCurrentServer;
                    foreach (var token in tokens)
                    {
                        SplitServerToken(token, out var address, out var port, out _);
                        if (!IsUsableDNSServer(address)) continue;

                        resolvers.Add(new ScopedResolver
                        {
                            Address = address,
                            Interface = blockInterface,
                            // Search domains come from the `DNS Domain:` row. The `#name` suffix on
                            // a server token is the DoT certificate/SNI hostname, NOT a match
                            // domain — the previous parser filed it as one, which turned every
                            // DoT-configured resolver into a bogusly domain-scoped resolver.
                            MatchDomains = new List<string>(blockDomains),
                            IsScoped = !onDefaultRoute,
                            Protocol = blockProtocol,
                            Port = port ?? (blockProtocol == DnsProtocol.Dot ? (ushort?)853 : null),
                            DoHReadState = transportRead
                        });
                    }
                }
                blockServers.Clear();
                blockCurrentServer.Clear();
                blockDomains.Clear();
                blockInterface = null;
                inBlock = false;
                onDefaultRoute = true;
                dnsScopeActive = true;
                blockProtocol = null;
                transportRead = ConditionState.Unknown;
                continuationField = null;
            }

            foreach (var rawLine in output.Split('\n'))
            {
                // systemd highlights section headers with ANSI when stdout is a tty. Piped output
                // has none, but stripping costs nothing and prevents a whole class of silent
                // parse failure if the pack is captured with colour forced on.
                var line = StripAnsi(rawLine).Trim();
                if (line.Length == 0) continue;

                if (string.Equals(line, "Global", StringComparison.Ordinal))
                {
                    FlushBlock();
                    inBlock = true;
                    blockInterface = null;
                    continue;
                }
                if (line.StartsWith("Link ", StringComparison.Ordinal))
                {
                    FlushBlock();
                    inBlock = true;
                    int open = line.IndexOf('(', StringComparison.Ordinal);
                    int close = line.IndexOf(')', StringComparison.Ordinal);
                    if (open >= 0 && close > open)
                        blockInterface = line[(open + 1)..close];
                    continue;
                }
                if (line.StartsWith("Delegate ", StringComparison.Ordinal))
                {
                    // systemd v257+ prints per-delegate blocks. Their resolver semantics are not
                    // established from documentation here, so they are skipped rather than guessed
                    // at — see corpus/resolvectl-status/PENDING-OWNER-VERIFICATION.md.
                    FlushBlock();
                    continue;
                }
                if (!inBlock) continue;

                if (TrySplitResolvectlField(line, out var label, out var value))
                {
                    continuationField = null;
                    if (label.Equals("DNS Servers", StringComparison.OrdinalIgnoreCase))
                    {
                        blockServers.AddRange(SplitTokens(value));
                        continuationField = "DNS Servers";
                    }
                    else if (label.Equals("Current DNS Server", StringComparison.OrdinalIgnoreCase))
                    {
                        blockCurrentServer.AddRange(SplitTokens(value));
                    }
                    else if (label.Equals("DNS Domain", StringComparison.OrdinalIgnoreCase))
                    {
                        blockDomains.AddRange(SplitTokens(value));
                        continuationField = "DNS Domain";
                    }
                    else if (label.Equals("Protocols", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyProtocolsTokens(SplitTokens(value), ref blockProtocol, ref transportRead, ref onDefaultRoute);
                        continuationField = "Protocols";
                    }
                    else if (label.Equals("DNSOverTLS setting", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyDotSetting(value, ref blockProtocol, ref transportRead);
                    }
                    else if (label.Equals("DefaultRoute setting", StringComparison.OrdinalIgnoreCase))
                    {
                        onDefaultRoute = value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (label.Equals("Current Scopes", StringComparison.OrdinalIgnoreCase))
                    {
                        // Tokenised, never substring-matched: "mDNS/IPv4" contains "DNS" but is not
                        // a unicast-DNS scope, and a link with no DNS scope is one systemd will
                        // never send a DNS query to.
                        dnsScopeActive = SplitTokens(value)
                            .Any(t => t.Equals("DNS", StringComparison.OrdinalIgnoreCase));
                    }
                    // `Fallback DNS Servers:` is deliberately NOT collected. Those are systemd's
                    // compiled-in last resort, used only when nothing else is configured; treating
                    // them as the system's resolvers would attribute queries to servers that are
                    // not answering them.
                    continue;
                }

                // Unlabelled line = wrapped continuation of the last multi-value field.
                if (continuationField == "DNS Servers") blockServers.AddRange(SplitTokens(line));
                else if (continuationField == "DNS Domain") blockDomains.AddRange(SplitTokens(line));
                else if (continuationField == "Protocols")
                    ApplyProtocolsTokens(SplitTokens(line), ref blockProtocol, ref transportRead, ref onDefaultRoute);
            }
            FlushBlock();
            return resolvers;
        }

        /// Applies one batch of `Protocols:` tokens. systemd emits `+X` / `-X` for boolean settings
        /// and `X=value` for non-boolean ones (`strv_extend_extended_bool`).
        private static void ApplyProtocolsTokens(
            IEnumerable<string> tokens,
            ref DnsProtocol? protocol,
            ref ConditionState transportRead,
            ref bool onDefaultRoute)
        {
            foreach (var token in tokens)
            {
                if (token.Equals("+DNSOverTLS", StringComparison.OrdinalIgnoreCase))
                {
                    protocol = DnsProtocol.Dot;
                    transportRead = ConditionState.No;   // read, and DoH is not a systemd-resolved option
                }
                else if (token.Equals("-DNSOverTLS", StringComparison.OrdinalIgnoreCase))
                {
                    protocol = null;
                    transportRead = ConditionState.No;   // read, and definitely not encrypted
                }
                else if (token.StartsWith("DNSOverTLS=", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyDotSetting(token["DNSOverTLS=".Length..], ref protocol, ref transportRead);
                }
                else if (token.Equals("+DefaultRoute", StringComparison.OrdinalIgnoreCase))
                {
                    onDefaultRoute = true;
                }
                else if (token.Equals("-DefaultRoute", StringComparison.OrdinalIgnoreCase))
                {
                    onDefaultRoute = false;
                }
            }
        }

        /// Applies a DNSOverTLS mode string (`yes` / `no` / `opportunistic`).
        ///
        /// <b>opportunistic is not encryption.</b> systemd-resolved in opportunistic mode attempts
        /// DoT and silently falls back to cleartext when the server does not answer on 853 or
        /// presents a bad certificate, with no signal to the client. Reporting it as encrypted
        /// would be a fabricated assurance; reporting it as plaintext would be a fabricated alarm.
        /// It is genuinely Unknown, and it is recorded as Unknown.
        private static void ApplyDotSetting(string value, ref DnsProtocol? protocol, ref ConditionState transportRead)
        {
            var mode = value.Trim();
            if (mode.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("true", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                protocol = DnsProtocol.Dot;
                transportRead = ConditionState.No;
            }
            else if (mode.Equals("no", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("false", StringComparison.OrdinalIgnoreCase)
                || mode.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                protocol = null;
                transportRead = ConditionState.No;
            }
            else
            {
                // opportunistic, or a mode this build does not know.
                protocol = null;
                transportRead = ConditionState.Unknown;
            }
        }

        /// Splits `Label: value` where the label is a real resolvectl field label, and returns
        /// false for a wrapped continuation line.
        ///
        /// A continuation line can contain colons (bare IPv6 servers do), so "has a colon" is not
        /// a usable test. What distinguishes a field line is that the text before the FIRST colon
        /// is label-shaped — starting with a letter, otherwise only letters, digits, spaces, '.',
        /// '-' and '_' — and the colon is followed by whitespace or end of line. `2620:fe::fe`
        /// fails on the second condition; `8.8.4.4 2001:4860::8888` fails on the first.
        private static bool TrySplitResolvectlField(string line, out string label, out string value)
        {
            label = string.Empty;
            value = string.Empty;
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) return false;
            if (colon + 1 < line.Length && line[colon + 1] != ' ' && line[colon + 1] != '\t') return false;

            var candidate = line[..colon];
            if (!char.IsLetter(candidate[0])) return false;
            foreach (var c in candidate)
            {
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '.' && c != '-' && c != '_') return false;
            }

            label = candidate.Trim();
            value = line[(colon + 1)..].Trim();
            return true;
        }

        private static List<string> SplitTokens(string value)
            => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

        /// Splits a systemd DNS server token: `ADDRESS[:PORT][#SERVERNAME]`, with an IPv6 literal
        /// bracketed when a port is present (`[2001:db8::1]:9953#dns.example`). The `#SERVERNAME`
        /// part is the DoT certificate name, not a domain the resolver is scoped to.
        private static void SplitServerToken(string token, out string address, out ushort? port, out string? serverName)
        {
            port = null;
            serverName = null;
            var rest = token;

            int hash = rest.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                serverName = rest[(hash + 1)..];
                if (serverName.Length == 0) serverName = null;
                rest = rest[..hash];
            }

            if (rest.StartsWith("[", StringComparison.Ordinal))
            {
                int close = rest.IndexOf(']', StringComparison.Ordinal);
                if (close > 0)
                {
                    address = rest[1..close];
                    var tail = rest[(close + 1)..];
                    if (tail.StartsWith(":", StringComparison.Ordinal)
                        && ushort.TryParse(tail[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bracketPort))
                        port = bracketPort;
                    return;
                }
            }

            // A bare IPv6 literal has two or more colons; a single colon is host:port.
            int first = rest.IndexOf(':', StringComparison.Ordinal);
            if (first >= 0 && rest.IndexOf(':', first + 1) < 0
                && ushort.TryParse(rest[(first + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var plainPort))
            {
                address = rest[..first];
                port = plainPort;
                return;
            }

            address = rest;
        }

        /// Removes CSI escape sequences (systemd's `ansi_highlight()` on a tty).
        private static string StripAnsi(string line)
        {
            const char Esc = '\u001b';
            if (line.IndexOf(Esc, StringComparison.Ordinal) < 0) return line;
            var sb = new System.Text.StringBuilder(line.Length);
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == Esc && i + 1 < line.Length && line[i + 1] == '[')
                {
                    i += 2;
                    while (i < line.Length && !char.IsLetter(line[i])) i++;
                    continue;
                }
                sb.Append(line[i]);
            }
            return sb.ToString();
        }

        /// Runs a short-lived process and returns stdout, killing it if it exceeds `timeoutSeconds`.
        internal static string? RunProcess(string executable, string[] arguments, double timeoutSeconds = 3.0)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var arg in arguments)
                    process.StartInfo.ArgumentList.Add(arg);
                process.Start();
                if (!process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)))
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                    return null;
                }
                return process.StandardOutput.ReadToEnd();
            }
            catch
            {
                return null;
            }
        }

        /// Whether an address could be a real DNS resolver worth querying.
        ///
        /// <para><b>P-07 (Aug 2026): LinkLocal is deliberately NOT excluded</b>, aligning with the
        /// macOS twin (`isUsableDNSServer` in NetworkKeyState.swift — checked in the code, not only
        /// in its comment). RFC 8106 RDNSS explicitly permits link-local resolver addresses and home
        /// routers commonly advertise `fe80::` resolvers, so a blanket exclusion drops the real
        /// system resolver and blinds the comparison and leak probes. The exclusion that was here
        /// justified itself with the deprecated site-local placeholder case — but IPClassifier maps
        /// `fec0::/10` to Reserved, so excluding Reserved is what actually drops those placeholders.
        /// The two were conflated, and dropping LinkLocal cost real resolvers for nothing.</para>
        ///
        /// <para>This changes Windows behaviour as well as Linux: a Windows adapter advertising an
        /// `fe80::` resolver now yields that resolver instead of an empty list.</para>
        ///
        /// <para>Loopback is not excluded either, and never was — see
        /// <see cref="IsSystemdResolvedStub"/> for why only the systemd stub listeners are dropped
        /// and a user's own dnsmasq/unbound on 127.0.0.1 is kept.</para>
        private static bool IsUsableDNSServer(string server)
        {
            if (string.IsNullOrEmpty(server)) return false;
            var category = IPClassifier.Classify(server);
            return category != IPAddressClass.Documentation
                && category != IPAddressClass.Unspecified
                && category != IPAddressClass.Invalid
                && category != IPAddressClass.Multicast
                && category != IPAddressClass.Reserved;
        }

        // MARK: - Interfaces (W10)

        // C137: tri-state interface enumeration — an exception from the OS adapter list is
        // "could not determine", not "no adapters exist". Callers must distinguish the two.
        private static (bool Enumerated, IEnumerable<NetworkInterface> Interfaces) SafeGetInterfaces()
        {
            try
            {
                return (true, NetworkInterface.GetAllNetworkInterfaces());
            }
            catch
            {
                return (false, Array.Empty<NetworkInterface>());
            }
        }

        /// Interface names that are up and carry at least one usable unicast address — not
        /// merely present in the adapter list.
        public static List<string> GetActiveInterfaces()
        {
            var (enumerated, interfaces) = SafeGetInterfaces();
            var names = new List<string>();
            if (!enumerated) return names; // C137: could not enumerate — return empty (caller should check VpnStatus for Unknown)
            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                IPInterfaceProperties props;
                try
                {
                    props = iface.GetIPProperties();
                }
                catch
                {
                    continue;
                }
                if (props.UnicastAddresses.Count == 0) continue;
                if (!names.Contains(iface.Name)) names.Add(iface.Name);
            }
            return names;
        }

        // MARK: - VPN (W11)

        /// Classifies VPN state using the actual OS-reported adapter type (Tunnel/Ppp) rather
        /// than name-substring guessing, and explicitly excludes hypervisor/loopback pseudo
        /// adapters so Hyper-V/VMware/VirtualBox virtual NICs are never reported as a VPN.
        ///
        /// <para><b>Platform dispatch (Aug 2026 Linux parity fix)</b>: on Windows the OS-reported
        /// <see cref="NetworkInterfaceType"/> is reliable — W11 correctly classifies VPN adapters
        /// as <see cref="NetworkInterfaceType.Tunnel"/>/<see cref="NetworkInterfaceType.Ppp"/>.
        /// On Linux, .NET's <see cref="NetworkInterfaceType"/> for <c>wg0</c>/<c>tun0</c>/
        /// <c>tap0</c>/<c>ppp0</c>/<c>ipsec0</c> is reported as <see cref="NetworkInterfaceType.Ethernet"/>
        /// or <see cref="NetworkInterfaceType.Unknown"/>: the kernel reports
        /// <c>ARPHRD_NONE</c> (65534) for virtual software interfaces and .NET does not map that
        /// to <c>Tunnel</c>. So on Linux, tunnel detection MUST use interface-name prefix matching
        /// — the same approach as macOS <c>NetworkKeyState.swift:441-476</c>, which uses the
        /// equivalent <c>utun</c>/<c>ppp</c>/<c>ipsec</c>/<c>tun</c>/<c>tap</c>/<c>gpd</c>/<c>wg</c>
        /// prefixes. Without this Linux branch, the Windows-only type check silently misreported an
        /// active WireGuard tunnel as "no VPN detected", which gated the whole Glutton leak test
        /// off — exactly the B030 false-negative class the leak module was written to refuse.</para>
        public static VPNStatus VpnStatus()
        {
            // C137: if the adapter enumeration fails, VPN state is Unknown — not "no VPN".
            var (enumerated, interfaces) = SafeGetInterfaces();
            if (!enumerated)
            {
                return new VPNStatus
                {
                    State = ConditionState.Unknown,
                    Evidence = "Could not enumerate network adapters — VPN status could not be determined"
                };
            }

            var activeTunnels = new List<string>();
            var presentTunnels = new List<string>();

            foreach (var iface in interfaces)
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                string desc = iface.Description?.ToLowerInvariant() ?? string.Empty;
                if (HypervisorMarkers.Any(marker => desc.Contains(marker, StringComparison.Ordinal))) continue;

                // Linux: name-prefix match (macOS NetworkKeyState.swift:462 twin). On Linux the
                // enumeration of interfaces from /sys/class/net via .NET's
                // NetworkInterface.GetAllNetworkInterfaces() reliably returns real kernel interface
                // names, so tunnel-prefix matching is sound. The Windows-specific Tunnel/Ppp enum
                // check below is bypassed because .NET reports Linux virtual interfaces as
                // Ethernet/Unknown rather than Tunnel.
                bool isTunnelType = OperatingSystem.IsLinux()
                    ? IsTunnelInterface(iface.Name)
                    : iface.NetworkInterfaceType == NetworkInterfaceType.Tunnel
                      || iface.NetworkInterfaceType == NetworkInterfaceType.Ppp;
                if (!isTunnelType) continue;

                presentTunnels.Add(iface.Name);

                IPInterfaceProperties props;
                try
                {
                    props = iface.GetIPProperties();
                }
                catch
                {
                    continue;
                }
                bool hasRoutableAddress = props.UnicastAddresses.Any(a =>
                    IPClassifier.Classify(a.Address.ToString()) != IPAddressClass.LinkLocal &&
                    IPClassifier.Classify(a.Address.ToString()) != IPAddressClass.Invalid &&
                    IPClassifier.Classify(a.Address.ToString()) != IPAddressClass.Unspecified);
                if (hasRoutableAddress) activeTunnels.Add(iface.Name);
            }

            if (activeTunnels.Count > 0)
            {
                return new VPNStatus
                {
                    State = ConditionState.Yes,
                    TunnelInterfaces = activeTunnels,
                    Evidence = $"Tunnel interface(s) with routable addresses: {string.Join(", ", activeTunnels)}"
                };
            }
            if (presentTunnels.Count > 0)
            {
                return new VPNStatus
                {
                    State = ConditionState.No,
                    TunnelInterfaces = presentTunnels,
                    Evidence = $"Tunnel interface(s) present but without a routable address (dormant): {string.Join(", ", presentTunnels)}"
                };
            }
            return new VPNStatus { State = ConditionState.No, Evidence = "No tunnel interfaces detected" };
        }

        /// Back-compat boolean. Only true for a confidently-active tunnel.
        public static bool IsVPNActive() => VpnStatus().State == ConditionState.Yes;

        // MARK: - Captive portal (W12)

        /// Distinguishes an intercepting portal (Yes) from a clear network (No) from an
        /// offline/errored probe (Unknown). A failed request is not evidence of "no portal", and
        /// redirects are not silently followed since that would hide the interception itself.
        public static async Task<CaptivePortalStatus> CaptivePortalStatusAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "http://www.msftconnecttest.com/connecttest.txt");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await _probeClient.SendAsync(request, cts.Token);

                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                {
                    return new CaptivePortalStatus
                    {
                        State = ConditionState.Yes,
                        Evidence = $"Probe redirected (HTTP {(int)response.StatusCode}) instead of returning the expected payload"
                    };
                }
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return new CaptivePortalStatus
                    {
                        State = ConditionState.Yes,
                        Evidence = $"Probe intercepted (HTTP {(int)response.StatusCode}, expected 200)"
                    };
                }

                string content = (await response.Content.ReadAsStringAsync()).Trim();
                if (content == "Microsoft Connect Test")
                {
                    return new CaptivePortalStatus { State = ConditionState.No, Evidence = "Received Microsoft connect-test success payload" };
                }
                return new CaptivePortalStatus
                {
                    State = ConditionState.Yes,
                    Evidence = "Probe intercepted (HTTP 200, unexpected body)"
                };
            }
            catch (Exception ex)
            {
                // Offline or blocked — cannot conclude either way.
                return new CaptivePortalStatus { State = ConditionState.Unknown, Evidence = $"Probe failed: {ex.Message}" };
            }
        }

        /// Back-compat boolean. Only true for a confirmed portal.
        public static async Task<bool> DetectCaptivePortalAsync() => (await CaptivePortalStatusAsync()).State == ConditionState.Yes;

        // MARK: - Encrypted DNS (W13)

        private static readonly string[] TunnelInterfacePrefixes = { "utun", "tap", "tun", "ppp", "ipsec", "gpd", "wg" };

        /// <summary>
        /// Whether <paramref name="name"/> is a tunnel interface by Linux/macOS naming convention
        /// (the kernel gives VPN virtual interfaces names that start with <c>tun</c>, <c>wg</c>,
        /// <c>ppp</c>, <c>tap</c>, <c>ipsec</c>, <c>gpd</c>, or <c>utun</c>). Used by
        /// <see cref="VpnStatus"/> on Linux (where .NET's <see cref="NetworkInterfaceType"/> does
        /// not classify virtual interfaces as <see cref="NetworkInterfaceType.Tunnel"/>) and by
        /// <see cref="EncryptedDNSStatusCore"/> to flag a scoped resolver as a tunnel interface.
        ///
        /// <para>Exposed as <c>internal</c> so the parity tests in
        /// <c>Syxx.Tests.NetworkKeyStateTests.VpnPrefixParityTests</c> can pin the prefix set —
        /// a dropped prefix here would silently re-introduce the B030 false-negative class the
        /// Linux Glutton leak test was written to catch.</para>
        /// </summary>
        internal static bool IsTunnelInterface(string name)
            => TunnelInterfacePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        /// Whether the transport to the *reported* resolver is provably encrypted. A loopback
        /// proxy forwards securely, but its upstream can't be verified from here, so that case is
        /// reported Unknown rather than asserted as encrypted.
        public static EncryptedDNSStatus EncryptedDNSStatus()
            => EncryptedDNSStatusCore(GetSystemDNSServers(), GetScopedResolvers());

        /// Pure-core extraction for testability (macOS twin: encryptedDNSStatus(servers:scoped:)).
        internal static EncryptedDNSStatus EncryptedDNSStatusCore(IReadOnlyList<string> servers, IReadOnlyList<ScopedResolver> scoped)
        {
            if (servers.Count == 0)
            {
                return new EncryptedDNSStatus { State = ConditionState.Unknown, Evidence = "No resolvers discovered" };
            }

            bool hasLoopback = servers.Any(s => IPClassifier.Classify(s) == IPAddressClass.Loopback);
            if (hasLoopback)
            {
                return new EncryptedDNSStatus
                {
                    State = ConditionState.Unknown,
                    Evidence = "Local resolver proxy present (loopback); upstream transport not verifiable from the client"
                };
            }

            // D056: require ALL active resolvers to be encrypted before reporting Yes.
            // A mixed configuration (one DoH resolver + one plain UDP/53 resolver) leaks
            // queries in plaintext while the old any-match logic reported the transport as
            // secure — a false-confidence result.
            var encryptedResolvers = scoped
                .Where(r => r.Protocol.HasValue)
                .Select(r => $"{r.Address} ({ProtocolLabel(r.Protocol!.Value)})")
                .ToList();
            var plainResolvers = scoped
                .Where(r => !r.Protocol.HasValue)
                .Select(r => r.Address)
                .ToList();
            if (encryptedResolvers.Count > 0 && plainResolvers.Count == 0)
            {
                // PR-06/D279: name the transport ONLY when every encrypted resolver reports the
                // same one. A mixed DoH+DoT configuration is genuinely two transports, so no single
                // name is true and the field stays null rather than picking one.
                var namedProtocols = scoped
                    .Where(r => r.Protocol.HasValue)
                    .Select(r => r.Protocol!.Value)
                    .Distinct()
                    .ToList();
                return new EncryptedDNSStatus
                {
                    State = ConditionState.Yes,
                    Evidence = $"All resolvers use encrypted transport: {string.Join(", ", encryptedResolvers)}",
                    ConfiguredTransport = namedProtocols.Count == 1 ? namedProtocols[0] : (DnsProtocol?)null
                };
            }
            if (encryptedResolvers.Count > 0 && plainResolvers.Count > 0)
            {
                return new EncryptedDNSStatus
                {
                    State = ConditionState.No,
                    Evidence = $"Mixed DNS transport — encrypted: {string.Join(", ", encryptedResolvers)}; plaintext leaks: {string.Join(", ", plainResolvers)}"
                };
            }

            var tunnelResolvers = scoped
                .Where(r => r.Interface != null && IsTunnelInterface(r.Interface))
                .Select(r => $"{r.Address} on {r.Interface}")
                .ToList();
            if (tunnelResolvers.Count > 0)
            {
                return new EncryptedDNSStatus
                {
                    State = ConditionState.Unknown,
                    Evidence = $"DNS queries tunneled through VPN interface(s); DNS-level encryption unverifiable: {string.Join(", ", tunnelResolvers)}"
                };
            }

            // D100: if any resolver's transport configuration could not be read, the result must
            // be Unknown — a registry permission error, an unparsed resolvectl block or a
            // systemd-resolved "opportunistic" mode is not evidence of "not encrypted".
            var unreadable = scoped.Where(r => r.DoHReadState == ConditionState.Unknown).ToList();
            if (unreadable.Count > 0)
            {
                return new EncryptedDNSStatus
                {
                    State = ConditionState.Unknown,
                    Evidence = $"Could not determine the DNS transport configuration for {string.Join(", ", unreadable.Select(r => r.Address))}"
                };
            }

            // C138: resolvers were discovered but carry no per-resolver detail at all, so nothing
            // about their transport was actually read.
            //
            // E1 (Aug 2026) narrowed this guard. It used to fire whenever every resolver had a null
            // Protocol, which is true both when the configuration was read and found to be
            // plaintext AND when it was never read — collapsing those made every genuinely
            // unencrypted machine report "unknown" and left the plaintext branch below unreachable.
            // The readable/unreadable distinction is DoHReadState's job and is handled above; this
            // guard now covers only the case where there is no resolver detail to read.
            if (scoped.Count == 0)
            {
                return new EncryptedDNSStatus
                {
                    State = ConditionState.Unknown,
                    Evidence = $"Encrypted-DNS status is unknown on this system; Syxx could not read the {(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "macOS")} resolver transport configuration"
                };
            }

            return new EncryptedDNSStatus
            {
                State = ConditionState.No,
                Evidence = $"Resolvers are plain IPs over UDP/53: {string.Join(", ", servers)}"
            };
        }

        private static string ProtocolLabel(DnsProtocol protocol) => protocol switch
        {
            DnsProtocol.Doh => "DoH",
            DnsProtocol.Dot => "DoT",
            _ => protocol.ToString()
        };

        /// D100: tri-state DoH configuration read — an unreadable registry key is Unknown,
        /// not a definite "not configured" that would render as "your DNS is not encrypted".
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static ConditionState HasConfiguredDoHTemplate(string serverIP)
        {
            if (!OperatingSystem.IsWindows()) return ConditionState.Unknown;
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DohInterfaceSettings\Doh\{serverIP}");
                if (key == null) return ConditionState.No;
                var flags = key.GetValue("DohFlags");
                return (flags != null && Convert.ToInt64(flags, CultureInfo.InvariantCulture) > 0)
                    ? ConditionState.Yes : ConditionState.No;
            }
            catch
            {
                return ConditionState.Unknown;  // D100: unreadable ≠ "not configured"
            }
        }

        /// Used by `DNSResolver.ResolveSecurelyAsync` and Sentinel. Only true when transport is provably encrypted.
        public static bool IsDNSEncrypted() => EncryptedDNSStatus().State == ConditionState.Yes;

        // MARK: - Report

        public static async Task<NetworkStateReport> GenerateReportAsync()
        {
            var captive = await CaptivePortalStatusAsync();
            var vpn = VpnStatus();
            var encrypted = EncryptedDNSStatus();
            return new NetworkStateReport
            {
                DnsServers = GetSystemDNSServers(),
                ActiveInterfaces = GetActiveInterfaces(),
                VpnActive = vpn.State == ConditionState.Yes,
                CaptivePortalDetected = captive.State == ConditionState.Yes,
                Vpn = vpn,
                CaptivePortal = captive,
                EncryptedDNS = encrypted,
                ScopedResolvers = GetScopedResolvers(),
                Gateway = await DetectGatewayAsync()
            };
        }

        // MARK: - C1 gateway/router audit (macOS detectGateway twin)

        /// C1 — probes the local gateway/router. Checks: default gateway IP, whether the gateway
        /// acts as a DNS server, the DHCP-pushed DNS list (registry DhcpNameServer — genuinely
        /// DHCP-sourced, distinct from a static override), and whether the router admin panel
        /// (HTTP port 80) answers. An undeterminable gateway returns an empty Ip with detail —
        /// the synthesis adapter treats that as inconclusive, never clean.
        public static async Task<GatewayInfo> DetectGatewayAsync()
        {
            string gwIp = string.Empty;
            string ifaceId = string.Empty;
            try
            {
                foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (iface.OperationalStatus != OperationalStatus.Up) continue;
                    var props = iface.GetIPProperties();
                    var gw = props.GatewayAddresses.FirstOrDefault(g =>
                        g.Address != null
                        && g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !g.Address.Equals(IPAddress.Any));
                    if (gw != null) { gwIp = gw.Address.ToString(); ifaceId = iface.Id; break; }
                }
            }
            catch (NetworkInformationException) { /* fall through to the honest empty result */ }

            if (string.IsNullOrEmpty(gwIp))
                return new GatewayInfo { Detail = "Could not determine default gateway" };

            bool isDns = GetSystemDNSServers().Contains(gwIp);
            var dhcpDns = DhcpPushedDNS(ifaceId);
            bool dhcpDnsUnreadable = dhcpDns is null;

            // Admin panel reachability — any HTTP answer under 500 counts as reachable; a
            // connection error is a real "no". Redirects are NOT followed (shared _probeClient).
            ConditionState adminReachable;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var resp = await _probeClient.GetAsync($"http://{gwIp}/", cts.Token);
                adminReachable = (int)resp.StatusCode < 500 ? ConditionState.Yes : ConditionState.No;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                adminReachable = ConditionState.No;
            }

            string detail = $"Gateway: {gwIp}";
            if (isDns) detail += "; acts as DNS server";
            if (adminReachable == ConditionState.Yes) detail += "; admin panel reachable on port 80";
            // D113: distinguish "no DHCP DNS" from "couldn't read the registry" — previously both
            // produced identical output (the clause was simply omitted).
            if (dhcpDnsUnreadable)
                detail += "; DHCP pushed DNS: could not be determined";
            else if (dhcpDns!.Count > 0)
                detail += $"; DHCP pushed DNS: {string.Join(", ", dhcpDns)}";

            return new GatewayInfo
            {
                Ip = gwIp,
                IsDNS = isDns,
                AdminReachable = adminReachable,
                DhcpPushedDNS = dhcpDns ?? new List<string>(),
                Detail = detail
            };
        }

        /// The interface's DHCP-pushed DNS servers from the TCP/IP registry key. Unlike the live
        /// DnsAddresses list, DhcpNameServer only ever holds what DHCP supplied, so a user's
        /// static override cannot be mislabelled as router-pushed.
        /// D113: returns null when the registry is unreadable (distinct from empty — "no servers
        /// pushed" vs "couldn't check").
        public static List<string>? DhcpPushedDNS(string interfaceId)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(interfaceId)) return new List<string>();
            var servers = new List<string>();
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{interfaceId}");
                var raw = key?.GetValue("DhcpNameServer") as string;
                if (!string.IsNullOrWhiteSpace(raw))
                    servers.AddRange(raw.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries));
            }
            // D113: broaden the catch — OpenSubKey and GetValue can also raise
            // UnauthorizedAccessException, IOException (key marked for deletion), etc.
            catch (Exception ex) when (ex is System.Security.SecurityException
                or UnauthorizedAccessException or IOException)
            {
                return null;  // unreadable — not the same as "no servers pushed"
            }
            return servers;
        }
    }
}

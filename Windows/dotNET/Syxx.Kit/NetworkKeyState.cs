using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace Syxx.Kit
{
    /// A three-valued answer for network conditions that genuinely can be unknown. Reporting
    /// Unknown (with evidence) is the point of the condition-state rework: a probe error must not be
    /// silently reported as "no VPN" / "no captive portal" / "encrypted" (W11/W12/W13).
    public enum ConditionState
    {
        Yes,
        No,
        Unknown
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
        /// D100: whether the encrypted-transport configuration was readable. Unknown means
        /// the registry/config read failed — the result must be Unknown, not No.
        public ConditionState DoHReadState { get; set; } = ConditionState.No;
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
                    ConditionState dohReadState = ConditionState.No;
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

        /// Linux: read per-link DNS transport from `resolvectl status` when systemd-resolved is present.
        internal static List<ScopedResolver> GetScopedResolversFromResolvectl()
        {
            var output = RunProcess("resolvectl", new[] { "status" }, timeoutSeconds: 3.0);
            if (string.IsNullOrWhiteSpace(output))
                return GetScopedResolversFromNetworkInterfaces();
            var parsed = ParseResolvectlStatus(output);
            return parsed.Count > 0 ? parsed : GetScopedResolversFromNetworkInterfaces();
        }

        /// Parses `resolvectl status` link blocks. Test seam for Linux resolver transport discovery (E061).
        internal static List<ScopedResolver> ParseResolvectlStatus(string output)
        {
            var resolvers = new List<ScopedResolver>();
            string? currentInterface = null;
            bool linkScoped = false;
            DnsProtocol? linkProtocol = null;
            ushort? linkPort = null;
            var linkServers = new List<string>();

            void FlushLink()
            {
                foreach (var server in linkServers)
                {
                    string address = server;
                    string? domain = null;
                    int hash = server.IndexOf('#', StringComparison.Ordinal);
                    if (hash >= 0)
                    {
                        address = server[..hash];
                        domain = server[(hash + 1)..];
                    }
                    if (!IsUsableDNSServer(address)) continue;

                    var matchDomains = string.IsNullOrEmpty(domain) ? new List<string>() : new List<string> { domain };
                    resolvers.Add(new ScopedResolver
                    {
                        Address = address,
                        Interface = currentInterface,
                        MatchDomains = matchDomains,
                        IsScoped = linkScoped,
                        Protocol = linkProtocol,
                        Port = linkPort
                    });
                }
                linkServers.Clear();
                currentInterface = null;
                linkScoped = false;
                linkProtocol = null;
                linkPort = null;
            }

            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("Link ", StringComparison.Ordinal))
                {
                    FlushLink();
                    int open = line.IndexOf('(', StringComparison.Ordinal);
                    int close = line.IndexOf(')', StringComparison.Ordinal);
                    if (open >= 0 && close > open)
                        currentInterface = line[(open + 1)..close];
                    continue;
                }
                if (currentInterface == null) continue;

                if (line.StartsWith("DNS Servers:", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("Current DNS Server:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();
                    if (!string.IsNullOrEmpty(value))
                        linkServers.Add(value);
                    continue;
                }
                if (line.StartsWith("DNSOverHTTPS:", StringComparison.OrdinalIgnoreCase)
                    && IsResolvectlYes(line))
                {
                    linkProtocol = DnsProtocol.Doh;
                    linkPort ??= 443;
                    continue;
                }
                if (line.StartsWith("DNS Over TLS:", StringComparison.OrdinalIgnoreCase)
                    && IsResolvectlYes(line))
                {
                    linkProtocol = DnsProtocol.Dot;
                    linkPort ??= 853;
                }
            }
            FlushLink();
            return resolvers;
        }

        private static bool IsResolvectlYes(string line)
        {
            var value = line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();
            return value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase);
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

        private static bool IsUsableDNSServer(string server)
        {
            if (string.IsNullOrEmpty(server)) return false;
            var category = IPClassifier.Classify(server);
            return category != IPAddressClass.Documentation
                && category != IPAddressClass.Unspecified
                && category != IPAddressClass.Invalid
                && category != IPAddressClass.Multicast
                // Deprecated site-local (fec0::/10) shows up only as Windows placeholder
                // resolvers, and a link-local address can never be an off-link recursive
                // resolver — neither is a real comparison/leak target.
                && category != IPAddressClass.Reserved
                && category != IPAddressClass.LinkLocal;
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

            // C138: generalize the "no transport could be determined" guard to all platforms.
            // Previously gated on Linux only — on Windows, an all-null Protocol set fell through
            // to a definite "plain UDP/53" result even when the transport was genuinely undetermined.
            if (scoped.All(r => !r.Protocol.HasValue))
            {
                return new EncryptedDNSStatus
                {
                    State = ConditionState.Unknown,
                    Evidence = $"Encrypted-DNS status is unknown on this system; Syxx could not read the {(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "macOS")} resolver transport configuration"
                };
            }

            // D100: if any resolver's DoH configuration could not be read, the result must
            // be Unknown — a registry permission error is not evidence of "not encrypted".
            var unreadable = scoped.Where(r => r.DoHReadState == ConditionState.Unknown).ToList();
            if (unreadable.Count > 0)
            {
                return new EncryptedDNSStatus
                {
                    State = ConditionState.Unknown,
                    Evidence = $"Could not read the Windows DoH configuration for {string.Join(", ", unreadable.Select(r => r.Address))}"
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

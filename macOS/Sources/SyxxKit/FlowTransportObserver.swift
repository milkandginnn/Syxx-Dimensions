import Foundation

// MARK: - Tier 1: unprivileged, process-attributed flow observation
//
// Answers part of Q3 ("are MY DNS queries actually leaving this machine encrypted?") without root,
// by reading the kernel's own per-process socket/flow table (`nettop`) and diffing consecutive
// samples. What it can and cannot establish is a property of the socket API, not of this code:
//
//   CAN confirm CLEARTEXT — a TCP (or QUIC) flow from a resolver process to remote port 53. The
//        peer is on the socket, so it is directly observable.
//   CAN confirm ENCRYPTED — a flow to remote port 853 (DoT) or 8853 (DoQ), or to port 443 at an
//        address belonging to a configured encrypted resolver. Affirmative evidence that DNS left
//        over an encrypted transport.
//   CANNOT confirm or exclude cleartext UNICAST UDP/53 — the ordinary case. An unconnected UDP
//        socket has no peer: the destination exists only inside each `sendto` call and is never a
//        property of the socket, so NO socket-level tool on any OS can report it. Verified live on
//        2026-08-02: of 22 UDP rows in a full `nettop` sample, 0 carried a remote endpoint.
//        That case resolves to `transportUnobservable` ⇒ UNDETERMINED, and is answerable only by
//        the privileged packet-observation tier.
//
// SCOPE AND RETENTION (this is a privacy product; the narrowest observation that answers the
// question is the default):
//   • Inspected: transport metadata only — protocol, local port, peer address, peer port,
//     interface, socket state, byte counters, and the kernel's process attribution.
//   • NEVER inspected: packet payload, DNS question names, TLS contents. This tier cannot see them
//     even in principle; it never opens a packet.
//   • Retained: per-tick counts in `TransportEvidence` plus, transiently, the previous sample's
//     flow keys so the next tick can be diffed. Rows that are not DNS-relevant are dropped during
//     parsing and never stored.
//   • Written to disk: nothing. This type performs no file I/O.
//
// SELF-EXCLUSION IS LOAD-BEARING: Syxx sends cleartext Do53 on purpose during every audit
// (`DNSResolver.queryUDPDetailed`) and sends DoH on purpose during every Portal run
// (`EncryptedTransportProbe.probe()`, EncryptedTransportProbe.swift:48). Counting either would let
// Syxx confirm a state out of its own traffic — a fabricated measurement. Flows attributed to
// Syxx's own binaries are excluded before classification.

/// One row of the kernel's flow table, reduced to the fields this determination uses.
public struct ObservedFlow: Sendable, Equatable, Hashable {
    /// Process name as attributed by the kernel. `nettop` truncates to ~15 characters.
    public let processName: String
    public let pid: Int
    /// `tcp4`, `tcp6`, `udp4`, `udp6`, `quic4`, `quic6`.
    public let networkProtocol: String
    public let localPort: UInt16?
    /// `nil` when the socket has no peer (unconnected UDP, or a listener).
    public let remoteAddress: String?
    public let remotePort: UInt16?
    public let interfaceName: String?
    public let state: String?
    public let bytesIn: UInt64?
    public let bytesOut: UInt64?

    public init(processName: String, pid: Int, networkProtocol: String,
                localPort: UInt16?, remoteAddress: String?, remotePort: UInt16?,
                interfaceName: String?, state: String?, bytesIn: UInt64?, bytesOut: UInt64?) {
        self.processName = processName
        self.pid = pid
        self.networkProtocol = networkProtocol
        self.localPort = localPort
        self.remoteAddress = remoteAddress
        self.remotePort = remotePort
        self.interfaceName = interfaceName
        self.state = state
        self.bytesIn = bytesIn
        self.bytesOut = bytesOut
    }

    /// Identity of the flow, independent of its counters — used to detect a flow that is NEW since
    /// the previous sample.
    public var key: String {
        "\(pid)|\(networkProtocol)|\(localPort.map(String.init) ?? "*")|\(remoteAddress ?? "*")|\(remotePort.map(String.init) ?? "*")"
    }

    public var isUDP: Bool { networkProtocol.hasPrefix("udp") }
}

public enum FlowTransportObserver {

    /// Processes that resolve DNS on the host's behalf. A flow from one of these is part of the
    /// user's DNS path. Prefix-matched because `nettop` truncates long names.
    public static let resolverProcessPrefixes = [
        "mDNSResponder",    // macOS system resolver
        "dnscrypt-proxy",   // DNSCrypt / Anonymized DNSCrypt forwarder
        "unbound",          // local validating resolver
        "stubby",           // DoT stub
        "cloudflared",      // DoH proxy
        "dnsmasq",
        "named",
        "systemd-resolve"   // Linux twin
    ]

    /// Syxx's own binaries. Their traffic is deliberately cleartext (audit queries) or deliberately
    /// encrypted (the DoH reachability probe); counting either would fabricate a measurement out of
    /// Syxx's own behaviour.
    public static let selfProcessPrefixes = ["syxx", "Syxx", "DeepSyxx", "SyxxGUI"]

    /// Ports that carry encrypted DNS by definition.
    public static let dotPort: UInt16 = 853
    public static let doqPort: UInt16 = 8853
    public static let dohPort: UInt16 = 443
    public static let do53Port: UInt16 = 53
    /// Multicast DNS and NAT-PMP/PCP — resolver-process sockets that are NOT unicast DNS, so they
    /// must not be counted as "DNS activity whose transport we could not see".
    public static let nonUnicastResolverPorts: Set<UInt16> = [5353, 5350, 5351]

    // MARK: - Parsing

    /// Parse `nettop -x -n -l 1 -J interface,state,bytes_in,bytes_out` output.
    ///
    /// Format (verified live 2026-08-02):
    ///   process line: `mDNSResponder.1328                       1327577   1198732`
    ///   flow line:    `   tcp4 10.2.0.2:61724<->17.57.144.138:5223   utun4  Established  14423  145745`
    ///                 `   tcp6 2601:…:90ac.61868<->2601:…::1.53      en6    Closed`
    ///                 `   udp4 *:60090<->*:*                         utun4               0      46`
    ///
    /// IPv4 rows separate address and port with `:`; IPv6 rows use `.`. Rows that are not
    /// DNS-relevant are dropped here and never retained.
    public static func parse(_ output: String) -> [ObservedFlow] {
        var flows: [ObservedFlow] = []
        var currentProcess: (name: String, pid: Int)? = nil

        for rawLine in output.split(separator: "\n", omittingEmptySubsequences: false) {
            let line = String(rawLine)
            if line.isEmpty { continue }
            let isFlowLine = line.hasPrefix(" ") || line.hasPrefix("\t")
            let fields = line.split(whereSeparator: { $0 == " " || $0 == "\t" }).map(String.init)
            if fields.isEmpty { continue }

            if !isFlowLine {
                // Process header: `name.pid  <counters>`; the header row of the table has no pid.
                currentProcess = parseProcessToken(fields[0])
                continue
            }
            guard let proc = currentProcess else { continue }
            guard fields.count >= 2 else { continue }
            let proto = fields[0]
            guard proto.hasPrefix("tcp") || proto.hasPrefix("udp") || proto.hasPrefix("quic") else { continue }
            guard let endpoints = fields.first(where: { $0.contains("<->") }) else { continue }
            let isIPv6 = proto.hasSuffix("6")
            let parts = endpoints.components(separatedBy: "<->")
            guard parts.count == 2 else { continue }
            let local = splitEndpoint(parts[0], ipv6: isIPv6)
            let remote = splitEndpoint(parts[1], ipv6: isIPv6)

            // Remaining tokens after the endpoints: interface, optional state, optional counters.
            let endpointIndex = fields.firstIndex(of: endpoints) ?? 1
            let tail = Array(fields.dropFirst(endpointIndex + 1))
            let interfaceName = tail.first.flatMap { $0.allSatisfy(\.isNumber) ? nil : $0 }
            let state = tail.dropFirst().first.flatMap { tok -> String? in
                tok.allSatisfy(\.isNumber) ? nil : tok
            }
            let numbers = tail.compactMap { UInt64($0) }
            let bytesIn = numbers.first
            let bytesOut = numbers.dropFirst().first

            flows.append(ObservedFlow(processName: proc.name, pid: proc.pid,
                                      networkProtocol: proto,
                                      localPort: local.port,
                                      remoteAddress: remote.address,
                                      remotePort: remote.port,
                                      interfaceName: interfaceName,
                                      state: state,
                                      bytesIn: bytesIn, bytesOut: bytesOut))
        }
        return flows
    }

    /// `mDNSResponder.1328` → ("mDNSResponder", 1328). Names can themselves contain dots
    /// (`com.apple.WebKi.908`), so the pid is taken from the LAST dot-separated component.
    static func parseProcessToken(_ token: String) -> (name: String, pid: Int)? {
        guard let lastDot = token.lastIndex(of: ".") else { return nil }
        let pidPart = String(token[token.index(after: lastDot)...])
        guard let pid = Int(pidPart) else { return nil }
        return (String(token[token.startIndex..<lastDot]), pid)
    }

    /// `10.2.0.2:61724` / `2601:…::1.53` / `*:*` / `*.*` → (address, port), `nil` for wildcards.
    static func splitEndpoint(_ text: String, ipv6: Bool) -> (address: String?, port: UInt16?) {
        let separator: Character = ipv6 ? "." : ":"
        guard let idx = text.lastIndex(of: separator) else { return (nil, nil) }
        let addr = String(text[text.startIndex..<idx])
        let portText = String(text[text.index(after: idx)...])
        let address: String? = (addr == "*" || addr.isEmpty) ? nil : addr
        let port: UInt16? = UInt16(portText)
        return (address, port)
    }

    // MARK: - Diffing

    /// Flows that carried traffic between two samples: newly present, or with a byte counter that
    /// increased. A flow that merely persists unchanged is NOT activity — counting the historical
    /// rows `nettop` keeps for closed sockets would turn old traffic into a current claim, which is
    /// exactly the staleness failure this design forbids.
    public static func activeFlows(previous: [ObservedFlow], current: [ObservedFlow]) -> [ObservedFlow] {
        var previousByKey: [String: ObservedFlow] = [:]
        for f in previous { previousByKey[f.key] = f }
        return current.filter { flow in
            guard let old = previousByKey[flow.key] else { return true } // new since last sample
            let grewIn = (flow.bytesIn ?? 0) > (old.bytesIn ?? 0)
            let grewOut = (flow.bytesOut ?? 0) > (old.bytesOut ?? 0)
            return grewIn || grewOut
        }
    }

    // MARK: - Classification

    /// Turn active flows into DNS-transport evidence.
    ///
    /// - Parameters:
    ///   - encryptedResolverAddresses: addresses of resolvers the system is configured to reach
    ///     over an encrypted transport. A port-443 flow counts as encrypted DNS ONLY to one of
    ///     these; counting any HTTPS flow would make every web request look like encrypted DNS.
    public static func classify(activeFlows flows: [ObservedFlow],
                                encryptedResolverAddresses: Set<String>) -> TransportEvidence {
        var cleartextOffHost = 0
        var cleartextLoopback = 0
        var encryptedDNS = 0
        var unobservable = 0
        var interfaces: Set<String> = []
        var details: [String] = []

        for flow in flows {
            if selfProcessPrefixes.contains(where: { flow.processName.hasPrefix($0) }) { continue }
            let isResolverProcess = resolverProcessPrefixes.contains { flow.processName.hasPrefix($0) }

            if let remotePort = flow.remotePort, let remoteAddress = flow.remoteAddress {
                switch remotePort {
                case do53Port:
                    if let iface = flow.interfaceName { interfaces.insert(iface) }
                    if IPClassifier.classify(remoteAddress) == .loopback {
                        cleartextLoopback += 1
                        details.append("\(flow.processName) \(flow.networkProtocol) to loopback:53")
                    } else {
                        cleartextOffHost += 1
                        details.append("\(flow.processName) \(flow.networkProtocol) to \(remoteAddress):53 on \(flow.interfaceName ?? "unknown interface")")
                    }
                case dotPort, doqPort:
                    if let iface = flow.interfaceName { interfaces.insert(iface) }
                    encryptedDNS += 1
                    details.append("\(flow.processName) \(flow.networkProtocol) to \(remoteAddress):\(remotePort)")
                case dohPort where encryptedResolverAddresses.contains(remoteAddress):
                    if let iface = flow.interfaceName { interfaces.insert(iface) }
                    encryptedDNS += 1
                    details.append("\(flow.processName) \(flow.networkProtocol) to configured encrypted resolver \(remoteAddress):443")
                default:
                    continue
                }
                continue
            }

            // No peer on the socket. Only a resolver process's unicast UDP socket with bytes sent
            // counts as DNS activity we could not see the transport of; mDNS/NAT-PMP do not.
            if isResolverProcess, flow.isUDP, (flow.bytesOut ?? 0) > 0,
               let localPort = flow.localPort, !nonUnicastResolverPorts.contains(localPort) {
                if let iface = flow.interfaceName { interfaces.insert(iface) }
                unobservable += 1
                details.append("\(flow.processName) unconnected \(flow.networkProtocol) socket :\(localPort) sent \(flow.bytesOut ?? 0) bytes on \(flow.interfaceName ?? "unknown interface") — peer not observable at socket level")
            }
        }

        return TransportEvidence(method: .flowObservation,
                                 cleartextOffHost: cleartextOffHost,
                                 encryptedDNS: encryptedDNS,
                                 cleartextLoopback: cleartextLoopback,
                                 transportUnobservable: unobservable,
                                 interfaces: interfaces.sorted(),
                                 detail: details.isEmpty
                                    ? "no DNS-relevant flow activity in the interval"
                                    : details.joined(separator: "; "))
    }

    // MARK: - Sampling

    /// Result of running the sampler once. `flows == nil` means the sampler could not run at all —
    /// which is `observerUnavailable`, never an answer about encryption.
    public struct SampleResult: Sendable {
        public let flows: [ObservedFlow]?
        public let failureDetail: String?
        public var available: Bool { flows != nil }
    }

    /// The sampler command. No argument is client-supplied: the column set and flags are constants,
    /// so nothing here can be steered into observing more than the design allows.
    static let samplerPath = "/usr/bin/nettop"
    static let samplerArguments = ["-x", "-n", "-l", "1",
                                   "-J", "interface,state,bytes_in,bytes_out"]

    /// Take one flow sample. Measured cost on an idle Apple-silicon host, 2026-08-02:
    /// 0.04–0.18 s wall, 0.04–0.08 s user CPU per sample (three consecutive runs). At the 5 s
    /// cadence that is roughly 1–2 % of one core, against a continuous unfiltered PKTAP capture
    /// which would move every packet on every interface to userspace for the life of the process.
    /// - Parameter runner: injection point for tests. `nil` resolves, inside the body, to the
    ///   hardened hard-timeout subprocess runner already used for `scutil` and `ifconfig`
    ///   (`NetworkKeyState.runProcess`, NetworkKeyState.swift:118, rewritten today under D278 to
    ///   tie the exit signal to child death so a fast process cannot leave the caller blocked).
    ///   Resolving it here rather than in a default argument keeps `runProcess` internal: it is a
    ///   privileged subprocess path and does not belong on the public surface.
    public static func sample(runner: ((String, [String]) -> String?)? = nil) -> SampleResult {
        let run = runner ?? { path, args in NetworkKeyState.runProcess(path, args, timeout: 4.0) }
        return sampleUsing(run)
    }

    private static func sampleUsing(_ runner: (String, [String]) -> String?) -> SampleResult {
        guard FileManager.default.isExecutableFile(atPath: samplerPath) else {
            return SampleResult(flows: nil, failureDetail: "flow sampler not present at \(samplerPath)")
        }
        guard let output = runner(samplerPath, samplerArguments) else {
            return SampleResult(flows: nil, failureDetail: "flow sampler did not complete")
        }
        return SampleResult(flows: parse(output), failureDetail: nil)
    }
}

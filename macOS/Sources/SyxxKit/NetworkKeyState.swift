import Foundation
import Network

#if canImport(Darwin)
import Darwin
#endif

/// A three-valued answer for network conditions that genuinely can be unknown. Reporting
/// `unknown` (with evidence) is the point of the condition-state rework: a probe error must not be
/// silently reported as "no VPN" / "no captive portal" / "encrypted" (M14/M15/M16, W11/W12/W13).
public enum ConditionState: String, Codable, Sendable {
    case yes
    case no
    case unknown
}

// G-16b: added Equatable so DNSPathSnapshot's synthesised conformance works.
// All stored properties (ConditionState: raw-value enum, [String], String) are Equatable.
public struct VPNStatus: Codable, Sendable, Equatable {
    public let state: ConditionState
    public let tunnelInterfaces: [String]
    public let evidence: String
}

public struct CaptivePortalStatus: Codable, Sendable {
    public let state: ConditionState   // yes = portal intercepting, no = clear, unknown = offline/error
    public let evidence: String
}

// G-16b: added Equatable so DNSPathSnapshot's synthesised conformance works.
// All stored properties (ConditionState, String, DNSProtocol?) are Equatable.
public struct EncryptedDNSStatus: Codable, Sendable, Equatable {
    public let state: ConditionState
    public let evidence: String
    /// The transport the system is CONFIGURED to use, when `scutil --dns` actually named one, and
    /// `nil` when it did not. A surface may name a protocol only when this is non-nil, and must say
    /// that the naming is configuration, not observation.
    ///
    /// PR-06 (publication audit 2026-07-24) / D279: `DashboardView.swift:434` printed "DoT · TLS"
    /// for every `.yes`, so a machine configured for DoH — or one whose `.yes` came from a path
    /// that never named a protocol at all — was told it was using DoT. DoH, DoT, DoQ and DNSCrypt
    /// are different transports with different exposure, and the code did not know which was in
    /// use. Populated only from an observed `ScopedResolver.dnsProtocol`; never guessed.
    public let configuredTransport: DNSProtocol?

    public init(state: ConditionState, evidence: String, configuredTransport: DNSProtocol? = nil) {
        self.state = state
        self.evidence = evidence
        self.configuredTransport = configuredTransport
    }
}

/// The protocol used for DNS transport as reported by `scutil --dns`.
public enum DNSProtocol: String, Codable, Sendable {
    case doh = "DoH"
    case dot = "DoT"
}

/// A DNS resolver together with the interface/scope it was discovered on.
public struct ScopedResolver: Codable, Sendable, Hashable {
    public let address: String
    public let interface: String?
    public let matchDomains: [String]
    public let isScoped: Bool
    public let dnsProtocol: DNSProtocol?
    public let port: UInt16?
    // C113: scutil --dns flags/options that affect whether a resolver should be queried.
    /// True when the `flags` line contains "Supplemental" — a split-DNS override that decides
    /// which server answers a given domain (a classic redirection vector).
    public let isSupplemental: Bool
    /// True when the `options` line contains "mdns" — a multicast-DNS pseudo-resolver that
    /// answers `.local` queries and is not a real DNS server.
    public let isMDNS: Bool
    /// False when the `reach` field is `0x00000000 (Not Reachable)` — macOS itself considers
    /// this resolver unreachable and it should not be queried.
    public let isReachable: Bool

    public init(address: String, interface: String? = nil, matchDomains: [String] = [],
                isScoped: Bool = false, dnsProtocol: DNSProtocol? = nil, port: UInt16? = nil,
                isSupplemental: Bool = false, isMDNS: Bool = false, isReachable: Bool = true) {
        self.address = address
        self.interface = interface
        self.matchDomains = matchDomains
        self.isScoped = isScoped
        self.dnsProtocol = dnsProtocol
        self.port = port
        self.isSupplemental = isSupplemental
        self.isMDNS = isMDNS
        self.isReachable = isReachable
    }
}

/// Represents the network and DNS state at a point in time.
public struct NetworkStateReport: Codable, Sendable {
    public let dnsServers: [String]
    public let activeInterfaces: [String]
    public let vpnActive: Bool
    public let captivePortalDetected: Bool
    // Typed views (preferred). The plain booleans above are retained for compatibility.
    public let vpn: VPNStatus
    public let captivePortal: CaptivePortalStatus
    public let encryptedDNS: EncryptedDNSStatus
    public let scopedResolvers: [ScopedResolver]
    // C1 — Router/gateway DNS audit info. nil when detection could not run.
    public let gateway: GatewayInfo?
}

/// C1 — detected state of the local gateway/router.
public struct GatewayInfo: Codable, Sendable {
    /// The default gateway IP address (e.g. "192.168.1.1").
    public let ip: String
    /// Whether the gateway IP appears as one of the system's DNS servers.
    public let isDNS: Bool
    /// Whether the router admin panel (HTTP on port 80) is reachable.
    public let adminReachable: ConditionState
    /// The DNS servers pushed by DHCP (from the DHCP lease, if readable).
    public let dhcpPushedDNS: [String]
    /// Human-readable probe detail.
    public let detail: String
}

public enum NetworkKeyState {

    // MARK: - Process helper with a hard timeout (M11)

    /// Runs a short-lived process and returns its stdout, killing it if it exceeds `timeout`.
    /// Prevents `scutil`/`ifconfig` from hanging the whole tool indefinitely.
    ///
    /// KDNS-11: after SIGTERM, waits a 0.5 s grace period then SIGKILLs so a SIGTERM-ignoring
    /// child does not leave the drain thread blocking forever. Surfaces timeouts as `nil`.
    ///
    /// D278: the previous design signalled a semaphore only from inside a cancellable
    /// `DispatchWorkItem`; the fast path cancelled that work item and then waited on the
    /// semaphore unconditionally, so a subprocess that exited before the deadline left the
    /// calling thread blocked forever. This design ties the exit signal to child death via
    /// `terminationHandler` (no cancellation surface) and drains stdout concurrently, so a fast
    /// process costs its real duration instead of the full timeout.
    static func runProcess(_ path: String, _ arguments: [String], timeout: TimeInterval = 3.0) -> String? {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: path)
        process.arguments = arguments
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice

        // F047: Swift 6 strict concurrency rejects mutable captures inside the termination and
        // drain closures; wrap the mutable state in a final class so only the reference is
        // captured.
        final class RunProcessState: @unchecked Sendable {
            let exited = DispatchSemaphore(value: 0)
            let drained = DispatchSemaphore(value: 0)
            var outputData = Data()
        }
        let state = RunProcessState()

        // D278: signal exit from child death itself, never from a cancellable timer. The handler
        // has no cancellation surface, so a fast exit cannot leave an unsignalled wait behind.
        process.terminationHandler = { _ in state.exited.signal() }

        // D278: drain stdout CONCURRENTLY with the wait, not after it. Foundation Process has no
        // internal pipe drain; a wait-then-read design would let a chatty child (brew install,
        // >64 KB output) fill the 64 KB OS pipe buffer and block forever. The read handle is
        // opened/read/closed ONLY on this drain thread, honoring C112: never close the read
        // FileHandle from the timer/kill thread, which would race an in-flight read and raise an
        // uncatchable ObjC exception. EOF arrives naturally when the child dies, so the drain
        // unblocks on both the fast path and the kill path.
        DispatchQueue.global(qos: .userInitiated).async {
            let data = pipe.fileHandleForReading.readDataToEndOfFile()
            state.outputData = data
            try? pipe.fileHandleForReading.close()
            state.drained.signal()
        }

        do {
            try process.run()
        } catch {
            return nil
        }

        // The bounded wait's result IS the status: the macOS analog of the Windows twin
        // NetworkKeyState.cs:318 WaitForExit(TimeSpan). No DispatchWorkItem anywhere.
        let waitResult = state.exited.wait(timeout: .now() + timeout)
        var didKill = false
        if waitResult == .timedOut {
            if process.isRunning { process.terminate() }   // SIGTERM first
            Thread.sleep(forTimeInterval: 0.5)             // grace period
            if process.isRunning { kill(process.processIdentifier, SIGKILL) }
            didKill = true
        }

        // Reaps the child on both paths (no zombie). After a fired terminationHandler this is a
        // safe immediate return on Darwin: Foundation has already collected the exit status.
        process.waitUntilExit()

        // Bounded defensively; the drain signals at EOF, which the kill path guarantees. If it
        // (structurally impossibly) still times out, return nil, never a string whose
        // completeness cannot be verified.
        if state.drained.wait(timeout: .now() + 2.0) == .timedOut { return nil }

        if didKill { return nil }
        return String(data: state.outputData, encoding: .utf8)
    }

    // MARK: - Resolver discovery (M12)

    /// Reads the current system DNS servers. On macOS, `/etc/resolv.conf` is not the
    /// authoritative resolver configuration, so prefer `scutil --dns` and only fall back to
    /// resolv.conf when scutil has no usable nameservers.
    public static func getSystemDNSServers() -> [String] {
        let scoped = getScopedResolvers()
        // Prefer primary (unscoped) resolvers first, then scoped ones, de-duplicated.
        // C113: exclude mDNS pseudo-resolvers (options: mdns) and unreachable resolvers
        // (reach: 0x00000000) — these are not real DNS servers and must not be queried.
        var ordered: [String] = []
        for resolver in scoped.sorted(by: { !$0.isScoped && $1.isScoped }) {
            guard !resolver.isMDNS, resolver.isReachable else { continue }
            if !ordered.contains(resolver.address), isUsableDNSServer(resolver.address) {
                ordered.append(resolver.address)
            }
        }
        if !ordered.isEmpty { return ordered }
        return getSystemDNSServersFromResolvConf()
    }

    /// Parses `scutil --dns` resolver blocks, preserving the interface and match-domain scope
    /// so scoped/supplemental resolvers are not flattened into an ambiguous flat list.
    public static func getScopedResolvers() -> [ScopedResolver] {
        guard let output = runProcess("/usr/sbin/scutil", ["--dns"], timeout: 3.0) else {
            return []
        }

        var resolvers: [ScopedResolver] = []
        var currentServers: [String] = []
        var currentInterface: String? = nil
        var currentDomains: [String] = []
        var currentScoped = false
        var currentProtocol: DNSProtocol? = nil
        var currentPort: UInt16? = nil
        // C113: track Supplemental flag, mDNS option, and reachability.
        var currentSupplemental = false
        var currentMDNS = false
        var currentReachable = true

        func flush() {
            for server in currentServers {
                resolvers.append(ScopedResolver(
                    address: server,
                    interface: currentInterface,
                    matchDomains: currentDomains,
                    isScoped: currentScoped,
                    dnsProtocol: currentProtocol,
                    port: currentPort,
                    isSupplemental: currentSupplemental,
                    isMDNS: currentMDNS,
                    isReachable: currentReachable
                ))
            }
            currentServers = []
            currentInterface = nil
            currentDomains = []
            currentScoped = false
            currentProtocol = nil
            currentPort = nil
            currentSupplemental = false
            currentMDNS = false
            currentReachable = true
        }

        for rawLine in output.components(separatedBy: .newlines) {
            let line = rawLine.trimmingCharacters(in: .whitespaces)
            if line.hasPrefix("resolver #") {
                flush()
                continue
            }
            // C113: detect section headers so both "DNS configuration" and
            // "DNS configuration (for scoped queries)" sections are parsed.
            if line.hasPrefix("DNS configuration") {
                continue
            }
            if line.hasPrefix("nameserver[") || line.hasPrefix("nameserver") {
                if let colon = line.firstIndex(of: ":") {
                    let value = line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
                    if !value.isEmpty { currentServers.append(value) }
                }
            } else if line.hasPrefix("if_index") {
                if let open = line.firstIndex(of: "("), let close = line.firstIndex(of: ")"), open < close {
                    currentInterface = String(line[line.index(after: open)..<close])
                }
            } else if line.hasPrefix("domain[") || line.hasPrefix("search domain[") {
                if let colon = line.firstIndex(of: ":") {
                    currentDomains.append(line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces))
                }
            } else if line.hasPrefix("domain") && line.contains(":") {
                // C113: handle the unbracketed "domain : local" form that mDNS pseudo-resolvers
                // emit — the old parser only matched "domain[" so these were silently dropped.
                if let colon = line.firstIndex(of: ":") {
                    let value = line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
                    if !value.isEmpty { currentDomains.append(value) }
                }
            } else if line.contains("flags") {
                // C113: parse the full flags line — Supplemental and Scoped are both significant.
                if line.contains("Scoped") { currentScoped = true }
                if line.contains("Supplemental") { currentSupplemental = true }
            } else if line.hasPrefix("options") {
                // C113: parse options for mDNS pseudo-resolver detection.
                if line.contains("mdns") { currentMDNS = true }
            } else if line.hasPrefix("reach") {
                // C113: parse reach — 0x00000000 means macOS considers this resolver unreachable.
                if line.contains("0x00000000") || line.contains("Not Reachable") {
                    currentReachable = false
                }
            } else if line.hasPrefix("Protocol") {
                // C011: scutil --dns outputs "Protocol : 1 (DNS over HTTPS)" or
                // "Protocol : 2 (DNS over TLS)". Splitting by ":" yields 2 components
                // (["Protocol ", " 1 (DNS over HTTPS)"]), not 3 — the old `>= 3` guard
                // silently rejected every Protocol line, so DoH/DoT was never detected and
                // the Prophet always reported "Unencrypted" for users with encrypted DNS.
                let components = line.components(separatedBy: ":").map { $0.trimmingCharacters(in: .whitespaces) }
                if components.count >= 2 {
                    // Extract the protocol code (first token of the value, e.g. "1" from "1 (DNS over HTTPS)")
                    let value = components[1]
                    let code = value.components(separatedBy: .whitespaces).first ?? value
                    if code == "1" { currentProtocol = .doh }
                    else if code == "2" { currentProtocol = .dot }
                }
            } else if line.hasPrefix("Port") {
                // e.g. "Port : 443"
                if let colon = line.firstIndex(of: ":") {
                    let value = line[line.index(after: colon)...].trimmingCharacters(in: .whitespaces)
                    if let portNum = UInt16(value) {
                        currentPort = portNum
                    }
                }
            }
        }
        flush()
        return resolvers
    }

    private static func getSystemDNSServersFromResolvConf() -> [String] {
        do {
            let content = try String(contentsOfFile: "/etc/resolv.conf", encoding: .utf8)
            var servers: [String] = []
            for line in content.components(separatedBy: .newlines) {
                let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
                if trimmed.hasPrefix("nameserver") {
                    let parts = trimmed.components(separatedBy: .whitespaces)
                    if parts.count >= 2, isUsableDNSServer(parts[1]), !servers.contains(parts[1]) {
                        servers.append(parts[1])
                    }
                }
            }
            return servers
        } catch {
            return []
        }
    }

    private static func isUsableDNSServer(_ server: String) -> Bool {
        guard !server.isEmpty else { return false }
        switch IPClassifier.classify(server) {
        case .documentation, .unspecified, .invalid, .multicast, .reserved:
            // E008: added .reserved to match Windows parity. Link-local (fe80::, 169.254.x) is
            // deliberately NOT excluded here despite Windows excluding it — RFC 8106 RDNSS
            // explicitly permits link-local resolver addresses, and home routers commonly
            // advertise fe80:: resolvers. Blanket exclusion would drop the real system resolver
            // and blind comparison/leak probes.
            return false
        default:
            return true
        }
    }

    // MARK: - Interfaces (M13)

    /// Fetches interface names that are actually up, running, and carry a usable unicast
    /// address — not merely present in the adapter list.
    public static func getActiveInterfaces() -> [String] {
        var names: [String] = []
        var ifaddr: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&ifaddr) == 0, let firstAddr = ifaddr else { return [] }
        defer { freeifaddrs(ifaddr) }

        let upRunning = UInt32(IFF_UP) | UInt32(IFF_RUNNING)
        for ptr in sequence(first: firstAddr, next: { $0.pointee.ifa_next }) {
            let flags = UInt32(ptr.pointee.ifa_flags)
            guard (flags & upRunning) == upRunning else { continue }
            guard let addr = ptr.pointee.ifa_addr else { continue }
            let family = addr.pointee.sa_family
            guard family == UInt8(AF_INET) || family == UInt8(AF_INET6) else { continue }

            let name = String(cString: ptr.pointee.ifa_name)
            if !names.contains(name) {
                names.append(name)
            }
        }
        return names
    }

    /// All interfaces (any state) with the addresses assigned to them. Used for VPN inspection.
    /// C091: reports `getifaddrs` failure distinctly from "no interfaces" (the old code returned
    /// `[]` for both, so a syscall failure was indistinguishable from an empty machine) and tracks
    /// `getnameinfo` failures so a live tunnel whose address cannot be formatted is not silently
    /// dropped.
    private static func interfaceAddresses() -> (
        addresses: [(name: String, address: String, family: Int32)],
        enumerationFailed: Bool,
        getnameinfoFailures: [String]
    ) {
        var result: [(name: String, address: String, family: Int32)] = []
        var ifaddr: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&ifaddr) == 0, let firstAddr = ifaddr else {
            return ([], true, [])
        }
        defer { freeifaddrs(ifaddr) }

        var getnameinfoFailures: [String] = []
        let upRunning = UInt32(IFF_UP) | UInt32(IFF_RUNNING)
        for ptr in sequence(first: firstAddr, next: { $0.pointee.ifa_next }) {
            let flags = UInt32(ptr.pointee.ifa_flags)
            guard (flags & upRunning) == upRunning else { continue }
            guard let addr = ptr.pointee.ifa_addr else { continue }
            let family = Int32(addr.pointee.sa_family)
            guard family == AF_INET || family == AF_INET6 else { continue }

            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            let saLen = socklen_t(addr.pointee.sa_len)
            if getnameinfo(addr, saLen, &host, socklen_t(host.count), nil, 0, NI_NUMERICHOST) == 0 {
                let name = String(cString: ptr.pointee.ifa_name)
                let ip = host.withUnsafeBufferPointer { chars in
                    let end = chars.firstIndex(of: 0) ?? chars.endIndex
                    return String(decoding: chars[..<end].map { UInt8(bitPattern: $0) }, as: UTF8.self)
                }
                result.append((name, ip, family))
            } else {
                let name = String(cString: ptr.pointee.ifa_name)
                if !getnameinfoFailures.contains(name) { getnameinfoFailures.append(name) }
            }
        }
        return (result, false, getnameinfoFailures)
    }

    // MARK: - VPN (M14)

    private static let tunnelPrefixes = ["utun", "ppp", "ipsec", "tun", "tap", "gpd", "wg"]

    /// Inspects tunnel-type interfaces and only counts them as an active VPN when they carry a
    /// usable (non-link-local) address, so a dormant `utun0` is not mistaken for a live tunnel.
    /// C091: returns `.unknown` when `getifaddrs` failed (could not enumerate) or when the only
    /// tunnel interfaces could not have their addresses read via `getnameinfo` — a syscall failure
    /// must not be reported as "no VPN active".
    public static func vpnStatus() -> VPNStatus {
        let (addresses, enumerationFailed, getnameinfoFailures) = interfaceAddresses()

        // C091: if the network stack could not be enumerated at all, we have no basis for a
        // conclusion — reporting `.no` here would fabricate a measurement that never happened.
        if enumerationFailed {
            return VPNStatus(state: .unknown, tunnelInterfaces: [],
                             evidence: "Could not enumerate network interfaces (getifaddrs failed)")
        }

        var activeTunnels: [String] = []
        var presentTunnels: [String] = []

        for (name, ip, _) in addresses {
            guard tunnelPrefixes.contains(where: { name.hasPrefix($0) }) else { continue }
            if !presentTunnels.contains(name) { presentTunnels.append(name) }
            let cls = IPClassifier.classify(ip)
            if cls != .linkLocal && cls != .invalid && cls != .unspecified {
                if !activeTunnels.contains(name) { activeTunnels.append(name) }
            }
        }

        if !activeTunnels.isEmpty {
            return VPNStatus(state: .yes, tunnelInterfaces: activeTunnels,
                             evidence: "Tunnel interface(s) with routable addresses: \(activeTunnels.joined(separator: ", "))")
        }
        if !presentTunnels.isEmpty {
            return VPNStatus(state: .no, tunnelInterfaces: presentTunnels,
                             evidence: "Tunnel interface(s) present but without a routable address (dormant): \(presentTunnels.joined(separator: ", "))")
        }

        // C091: if any interface's address could not be read and no active tunnel was found,
        // the absence of a tunnel may be an artefact of the failed read, not a measurement.
        if !getnameinfoFailures.isEmpty {
            let failedTunnels = getnameinfoFailures.filter { name in
                tunnelPrefixes.contains(where: { name.hasPrefix($0) })
            }
            if !failedTunnels.isEmpty {
                return VPNStatus(state: .unknown, tunnelInterfaces: failedTunnels,
                                 evidence: "Could not read addresses for tunnel interface(s): \(failedTunnels.joined(separator: ", "))")
            }
        }

        return VPNStatus(state: .no, tunnelInterfaces: [], evidence: "No tunnel interfaces detected")
    }

    // E051: deprecated — collapses .unknown into false, which hides VPN-state transitions
    // from Sentinel.diff(). Use vpnStatus().state directly (ConditionState tri-state).
    @available(*, deprecated, message: "Use vpnStatus().state for tri-state; isVPNActive() collapses .unknown to false")
    public static func isVPNActive() -> Bool {
        vpnStatus().state == .yes
    }

    // MARK: - Captive portal (M15)

    // C030: URLSession delegate that refuses redirects so the captive-portal probe does not
    // follow a 302 to a login page (which downloads the full login resource hierarchy, slows the
    // probe, and can exhaust the timeout → .unknown instead of .yes). A 3xx response is itself
    // evidence of a portal; the probe evaluates it directly.
    private final class NoRedirectDelegate: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
        func urlSession(_ session: URLSession, task: URLSessionTask,
                        willPerformHTTPRedirection response: HTTPURLResponse,
                        newRequest request: URLRequest,
                        completionHandler: @escaping (URLRequest?) -> Void) {
            completionHandler(nil) // refuse the redirect
        }
    }

    private static let noRedirectSession: URLSession = {
        let cfg = URLSessionConfiguration.ephemeral
        cfg.timeoutIntervalForRequest = 3.0
        cfg.timeoutIntervalForResource = 5.0
        cfg.requestCachePolicy = .reloadIgnoringLocalAndRemoteCacheData
        return URLSession(configuration: cfg, delegate: NoRedirectDelegate(), delegateQueue: nil)
    }()

    /// Distinguishes an intercepting portal (yes) from a clear network (no) from an
    /// offline/errored probe (unknown). A failed request is not evidence of "no portal".
    public static func captivePortalStatus() async -> CaptivePortalStatus {
        guard let url = URL(string: "http://captive.apple.com/hotspot-detect.html") else {
            return CaptivePortalStatus(state: .unknown, evidence: "Could not construct probe URL")
        }
        var request = URLRequest(url: url)
        request.timeoutInterval = 3.0
        request.cachePolicy = .reloadIgnoringLocalAndRemoteCacheData

        do {
            let (data, response) = try await noRedirectSession.data(for: request)
            guard let httpResponse = response as? HTTPURLResponse else {
                return CaptivePortalStatus(state: .unknown, evidence: "Non-HTTP response to probe")
            }
            // C030: a 3xx response (redirect refused by our delegate) is itself evidence of a
            // captive portal intercepting the probe — evaluate it directly as portal-present.
            if (300...399).contains(httpResponse.statusCode) {
                return CaptivePortalStatus(state: .yes,
                                           evidence: "Probe redirected (HTTP \(httpResponse.statusCode)) — captive portal detected")
            }
            let text = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            if httpResponse.statusCode == 200 && text.contains("<TITLE>Success</TITLE>") {
                return CaptivePortalStatus(state: .no, evidence: "Received Apple hotspot success payload")
            }
            return CaptivePortalStatus(state: .yes,
                                       evidence: "Probe intercepted (HTTP \(httpResponse.statusCode), unexpected body)")
        } catch {
            // Offline or blocked — cannot conclude either way.
            return CaptivePortalStatus(state: .unknown, evidence: "Probe failed: \(error.localizedDescription)")
        }
    }

    // MARK: - Encrypted DNS (M16)

    private static let tunnelInterfacePrefixes = ["utun", "tap", "tun", "ppp", "ipsec", "gpd", "wg"]

    private static func isTunnelInterface(_ name: String) -> Bool {
        tunnelInterfacePrefixes.contains { name.hasPrefix($0) }
    }

    /// Whether the transport to the *reported* resolver is provably encrypted. Checks three
    /// signals:
    /// 1. scutil --dns explicitly reports a DoH/DoT protocol (macOS 14+ profile config).
    /// 2. The resolver lives on a VPN/tunnel interface (traffic is tunnel-encrypted — but the
    ///    DNS queries themselves may be plain UDP/53 inside the tunnel, so this is `.unknown`,
    ///    not `.yes` — B032 correction).
    /// 3. A loopback proxy resolves locally (upstream not verifiable → `.unknown`).
    public static func encryptedDNSStatus() -> EncryptedDNSStatus {
        encryptedDNSStatus(servers: getSystemDNSServers(), scoped: getScopedResolvers())
    }

    /// Pure-core extraction for testability. Takes server list and scoped resolvers directly
    /// instead of reading system state, so unit tests can provide known inputs.
    internal static func encryptedDNSStatus(servers: [String], scoped: [ScopedResolver]) -> EncryptedDNSStatus {
        if servers.isEmpty {
            return EncryptedDNSStatus(state: .unknown, evidence: "No resolvers discovered")
        }
        let hasLoopback = servers.contains { IPClassifier.classify($0) == .loopback }
        if hasLoopback {
            return EncryptedDNSStatus(state: .unknown,
                evidence: "Local resolver proxy present (loopback); upstream transport not verifiable from the client")
        }
        // Check scoped resolvers for protocol or tunnel interface.
        // D056: require ALL active resolvers to be encrypted before reporting .yes.
        // A mixed configuration (one DoH resolver + one plain UDP/53 resolver) leaks
        // queries in plaintext while the old any-match logic reported the transport as
        // secure — a false-confidence result.
        let encryptedResolvers = scoped.compactMap { r -> String? in
            guard let p = r.dnsProtocol else { return nil }
            return "\(r.address) (\(p.rawValue))"
        }
        let plainResolvers = scoped.filter { $0.dnsProtocol == nil }.map { $0.address }
        if !encryptedResolvers.isEmpty && plainResolvers.isEmpty {
            // PR-06/D279: name the transport ONLY when every encrypted resolver reports the same
            // one. A mixed DoH+DoT configuration is genuinely two transports, so no single name is
            // true and the field stays nil rather than picking one.
            let namedProtocols = Set(scoped.compactMap { $0.dnsProtocol })
            let agreedTransport: DNSProtocol? = namedProtocols.count == 1 ? namedProtocols.first : nil
            return EncryptedDNSStatus(state: .yes,
                evidence: "All resolvers use encrypted transport: \(encryptedResolvers.joined(separator: ", "))",
                configuredTransport: agreedTransport)
        }
        if !encryptedResolvers.isEmpty && !plainResolvers.isEmpty {
            return EncryptedDNSStatus(state: .no,
                evidence: "Mixed DNS transport — encrypted: \(encryptedResolvers.joined(separator: ", ")); plaintext leaks: \(plainResolvers.joined(separator: ", "))")
        }
        let tunnelResolvers = scoped.compactMap { r -> String? in
            guard let iface = r.interface, isTunnelInterface(iface) else { return nil }
            return "\(r.address) on \(iface)"
        }
        if !tunnelResolvers.isEmpty {
            // B032: tunneled DNS may be plain UDP/53 inside the tunnel; we cannot confirm
            // DNS-level encryption from resolver config alone.
            return EncryptedDNSStatus(state: .unknown,
                evidence: "DNS queries tunneled through VPN interface(s); DNS-level encryption unverifiable: \(tunnelResolvers.joined(separator: ", "))")
        }
        return EncryptedDNSStatus(state: .no,
            evidence: "Resolvers are plain IPs over UDP/53: \(servers.joined(separator: ", "))")
    }

    // C090: deprecated — collapses .unknown into false, which hides DNS-encryption-state
    // transitions from Sentinel.diff() (e.g. loopback proxy → "plaintext" alert).
    // Use encryptedDNSStatus().state directly (ConditionState tri-state).
    @available(*, deprecated, message: "Use encryptedDNSStatus().state for tri-state; isDNSEncrypted() collapses .unknown to false")
    public static func isDNSEncrypted() -> Bool {
        encryptedDNSStatus().state == .yes
    }

    // MARK: - C1 — Gateway / Router audit

    /// Returns the default gateway IPv4 address via `route -n get default`.
    public static func defaultGatewayIP() -> String? {
        guard let output = runProcess("/sbin/route", ["-n", "get", "default"]) else { return nil }
        return parseGatewayFromRouteOutput(output)
    }

    /// Pure parser for `route -n get default` output. `route` INDENTS its fields
    /// ("    gateway: 10.0.0.1"), so each line must be trimmed before the prefix match —
    /// the untrimmed `hasPrefix` version never matched and silently reported no gateway.
    public static func parseGatewayFromRouteOutput(_ output: String) -> String? {
        guard let gatewayLine = output
            .components(separatedBy: "\n")
            .map({ $0.trimmingCharacters(in: .whitespaces) })
            .first(where: { $0.hasPrefix("gateway:") }) else {
            return nil
        }
        let ip = gatewayLine
            .replacingOccurrences(of: "gateway:", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return ip.isEmpty || ip == "0.0.0.0" ? nil : ip
    }

    /// Returns DNS server IPs pushed by DHCP for the primary interface.
    /// Uses `ipconfig getoption <iface> domain_name_server` on the default-route interface.
    public static func dhcpPushedDNS() -> [String] {
        guard let defaultIface = defaultRouteInterface() else { return [] }
        guard let output = runProcess("/usr/sbin/ipconfig", ["getoption", defaultIface, "domain_name_server"]) else { return [] }
        return output
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .components(separatedBy: .whitespaces)
            .filter { !$0.isEmpty }
    }

    /// Returns the primary default-route interface name.
    private static func defaultRouteInterface() -> String? {
        guard let output = runProcess("/sbin/route", ["-n", "get", "default"]),
              let ifaceLine = output.components(separatedBy: "\n").first(where: { $0.hasPrefix("interface:") }) else {
            return nil
        }
        return ifaceLine
            .replacingOccurrences(of: "interface:", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// C1 — probes the local gateway/router and returns a `GatewayInfo` struct.
    /// Checks: gateway IP, whether the gateway acts as DNS server, DHCP-pushed DNS,
    /// and whether the router admin panel (HTTP port 80) is reachable.
    public static func detectGateway() async -> GatewayInfo {
        // C108: when the gateway cannot be determined, the detail string clarifies that
        // isDNS is unknown (not a definite "no") — structured consumers should treat an
        // empty `ip` as unknown.
        guard let gwIP = defaultGatewayIP() else {
            return GatewayInfo(ip: "", isDNS: false, adminReachable: .unknown,
                               dhcpPushedDNS: [],
                               detail: "Could not determine default gateway — isDNS is unknown, not false")
        }
        let systemDNS = getSystemDNSServers()
        let isDNS = systemDNS.contains(gwIP)
        let dhcpDNS = dhcpPushedDNS()

        // Check admin panel reachability via HTTP GET to port 80.
        let adminReachable: ConditionState
        if let url = URL(string: "http://\(gwIP)/") {
            var req = URLRequest(url: url)
            req.timeoutInterval = 3.0
            req.cachePolicy = .reloadIgnoringLocalAndRemoteCacheData
            do {
                let (_, resp) = try await URLSession.shared.data(for: req)
                if let httpResp = resp as? HTTPURLResponse {
                    // C108: 5xx from an intermediate proxy is not a definite "not reachable" —
                    // the gateway may be reachable but its admin panel is behind a broken proxy.
                    if (200..<400).contains(httpResp.statusCode) {
                        adminReachable = .yes
                    } else if httpResp.statusCode >= 500 {
                        adminReachable = .unknown
                    } else {
                        // 4xx: server responded but refused — admin panel exists but restricted.
                        adminReachable = .yes
                    }
                } else {
                    adminReachable = .unknown
                }
            } catch {
                // C108: a timeout, TLS error, or network failure is .unknown, not .no.
                // Reserve .no for a definite connection refusal (ECONNREFUSED).
                adminReachable = .unknown
            }
        } else {
            adminReachable = .unknown
        }

        var detail = "Gateway: \(gwIP)"
        if isDNS { detail += "; acts as DNS server" }
        if adminReachable == .yes { detail += "; admin panel reachable on port 80" }
        if !dhcpDNS.isEmpty { detail += "; DHCP pushed DNS: \(dhcpDNS.joined(separator: ", "))" }

        return GatewayInfo(ip: gwIP, isDNS: isDNS, adminReachable: adminReachable,
                           dhcpPushedDNS: dhcpDNS, detail: detail)
    }

    // MARK: - Report

    public static func generateReport() async -> NetworkStateReport {
        let captive = await captivePortalStatus()
        let vpn = vpnStatus()
        let encrypted = encryptedDNSStatus()
        let gateway = await detectGateway()
        return NetworkStateReport(
            dnsServers: getSystemDNSServers(),
            activeInterfaces: getActiveInterfaces(),
            vpnActive: vpn.state == .yes,
            captivePortalDetected: captive.state == .yes,
            vpn: vpn,
            captivePortal: captive,
            encryptedDNS: encrypted,
            scopedResolvers: getScopedResolvers(),
            gateway: gateway
        )
    }
}

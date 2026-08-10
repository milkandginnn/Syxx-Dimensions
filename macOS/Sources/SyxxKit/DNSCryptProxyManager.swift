import Foundation

/// macOS glue for `DNSCryptProxyConfig`: owns the running `dnscrypt-proxy` child process and turns raw
/// observations into the honest `DNSCryptResult`. Kept separate from the pure config core (toml gen /
/// classify) so that logic stays testable without spawning a process or touching the network.
///
/// SERVICE MODEL: like `UnboundManager`, the proxy runs as an app-managed background child process tied to
/// the enable/disable toggle — not an installed launchd daemon. It listens only on a private loopback port
/// that Unbound forwards to; nothing else (including the OS resolver) ever talks to it, so there is no
/// port-53 conflict and no pf redirect on this leg.
public final class DNSCryptProxyManager: @unchecked Sendable {

    /// Default private loopback port for the proxy (distinct from Unbound's 5335 and the verify high ports).
    public static let loopbackPort: UInt16 = 5354

    private let queue = DispatchQueue(label: "syxx.dnscrypt.manager")
    private var process: Process?

    /// Directory for the generated toml + query log.
    public let stateDir: String

    public init(stateDir: String? = nil) {
        // Machine-wide by default, same rationale as UnboundManager (never NSHomeDirectory under sudo).
        self.stateDir = stateDir ?? "/Library/Application Support/Syxx/dnscrypt"
    }

    public var isRunning: Bool { queue.sync { process?.isRunning ?? false } }

    // MARK: - Start (honest tri-state; never a silent fallback)

    /// Starts `dnscrypt-proxy` for `resolver` on `port`, waits until it actually reaches a resolver, and
    /// reports the honest state — distinguishing relayed (full-strength) from direct (weaker) and from
    /// unreachable. Does NOT touch system DNS. The caller (UnboundManager) decides whether a non-anonymized
    /// or unreachable state is acceptable; this method never pretends the weaker state is the stronger one.
    public func start(resolver: EncryptedResolver,
                      port: UInt16 = DNSCryptProxyManager.loopbackPort) async -> DNSCryptProxyConfig.DNSCryptResult {
        guard let bin = DNSCryptProxyConfig.locateBinary() else {
            return .notInstalled(hint: DNSCryptProxyConfig.installHint)
        }
        guard let serverName = resolver.dnscryptServerName, let stamp = resolver.dnscryptStamp else {
            return .failed(reason: "resolver \(resolver.id) has no DNSCrypt profile (no pinned stamp)")
        }
        do {
            try FileManager.default.createDirectory(atPath: stateDir, withIntermediateDirectories: true)
        } catch {
            return .failed(reason: "could not create dnscrypt state directory: \(error.localizedDescription)")
        }
        let tomlPath = stateDir + "/dnscrypt-proxy.toml"
        let queryLogPath = stateDir + "/query.log"
        // Truncate the query log so relay detection reads only THIS run's lines.
        try? "".write(toFile: queryLogPath, atomically: true, encoding: .utf8)

        let cfg = DNSCryptProxyConfig.ProxyConfig(
            port: port, resolverName: serverName, resolverStamp: stamp,
            relays: resolver.dnscryptRelays, queryLogPath: queryLogPath)
        do {
            try DNSCryptProxyConfig.generateToml(cfg).write(toFile: tomlPath, atomically: true, encoding: .utf8)
        } catch {
            return .failed(reason: "could not write dnscrypt-proxy.toml: \(error.localizedDescription)")
        }

        let p = Process()
        p.executableURL = URL(fileURLWithPath: bin)
        p.arguments = ["-config", tomlPath]
        let errPipe = Pipe()
        p.standardError = errPipe
        p.standardOutput = FileHandle.nullDevice
        do { try p.run() } catch {
            return .failed(reason: "could not launch dnscrypt-proxy: \(error.localizedDescription)")
        }
        queue.sync { self.process = p }

        try? await Task.sleep(nanoseconds: 400_000_000)
        if !p.isRunning {
            let err = String(data: errPipe.fileHandleForReading.availableData, encoding: .utf8) ?? ""
            queue.sync { self.process = nil }
            return .failed(reason: err.isEmpty ? "dnscrypt-proxy exited immediately" : err.trimmingCharacters(in: .whitespacesAndNewlines))
        }

        // Readiness: a cold DNSCrypt handshake (cert fetch + relay probe) can take several seconds; poll a
        // real query through the proxy for up to ~20s before declaring it unreachable.
        let relaysConfigured = !resolver.dnscryptRelays.isEmpty
        var reached = false
        for _ in 0..<20 {
            if await queryReaches(port: port, domain: "example.com") { reached = true; break }
            try? await Task.sleep(nanoseconds: 1_000_000_000)
        }
        if !reached {
            stop()
            return DNSCryptProxyConfig.classifyStart(
                port: port, resolver: serverName, relay: nil, binaryFound: true, processStarted: true,
                resolverReached: false, viaRelay: false, relaysConfigured: relaysConfigured)
        }
        // A resolver was reached. Now determine whether it went THROUGH a relay by reading the query log
        // (dnscrypt-proxy records the relay it used as the trailing column of each PASS line).
        let relayConclusion = relayFromQueryLog(queryLogPath, knownRelays: resolver.dnscryptRelays.map { $0.name })
        switch relayConclusion {
        case .relay(let name):
            return DNSCryptProxyConfig.classifyStart(
                port: port, resolver: serverName, relay: name, binaryFound: true, processStarted: true,
                resolverReached: true, viaRelay: true, relaysConfigured: relaysConfigured)
        case .direct:
            return DNSCryptProxyConfig.classifyStart(
                port: port, resolver: serverName, relay: nil, binaryFound: true, processStarted: true,
                resolverReached: true, viaRelay: false, relaysConfigured: relaysConfigured)
        case .undetermined(let reason):
            // D093: an unreadable query log is NOT "direct" — it is "we don't know."
            // Report inconclusive rather than fabricating a definite "NOT anonymized" conclusion.
            return .inconclusive(reason: "reached resolver \(serverName) but could not read the query log to determine relay use: \(reason)")
        }
    }

    public func stop() {
        let p = queue.sync { () -> Process? in let x = self.process; self.process = nil; return x }
        guard let p = p, p.isRunning else { return }
        p.terminate()
        Thread.sleep(forTimeInterval: 0.3)
        if p.isRunning { kill(p.processIdentifier, SIGKILL) }
    }

    // MARK: - Readiness helpers

    /// A real query to the proxy that returns a usable answer proves it reached a DNSCrypt resolver.
    private func queryReaches(port: UInt16, domain: String) async -> Bool {
        let q = DNSQuery(name: domain, type: 1, server: "127.0.0.1", port: port)
        let r = await DNSResolver.queryUDPDetailed(q)
        return !r.didNotSucceed && !r.records.isEmpty
    }

    /// D093: tri-state outcome of reading the query log, distinguishing "no relay used" (direct)
    /// from "could not read the log" (undetermined). The old nil-means-both collapsed an unreadable
    /// log into "direct, NOT anonymized" — a fabricated definite.
    enum RelayLogResult {
        case relay(String)
        case direct
        case undetermined(reason: String)
    }

    /// Parses dnscrypt-proxy's tab-separated query log.
    /// Log line: `time <tab> client <tab> name <tab> type <tab> PASS <tab> rtt <tab> resolver <tab> relay`.
    private func relayFromQueryLog(_ path: String, knownRelays: [String]) -> RelayLogResult {
        // No relays configured — the question is moot; this is a genuine direct.
        guard !knownRelays.isEmpty else { return .direct }
        // D093: distinguish "could not read the file" from "read it, no relay found."
        guard let text = try? String(contentsOfFile: path, encoding: .utf8) else {
            return .undetermined(reason: "could not read query log at \(path)")
        }
        // A zero-byte log after a PASS is itself worth reporting — the proxy may not have logged.
        guard !text.isEmpty else {
            return .undetermined(reason: "query log is empty despite a successful query")
        }
        let relaySet = Set(knownRelays)
        for line in text.split(separator: "\n").reversed() {
            let cols = line.split(separator: "\t").map { $0.trimmingCharacters(in: .whitespaces) }
            if let last = cols.last, relaySet.contains(last) { return .relay(last) }
        }
        return .direct
    }
}

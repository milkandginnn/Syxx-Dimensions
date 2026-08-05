import Foundation

#if canImport(Darwin)
import Darwin
#endif

// MARK: - DNS path confirmation (the definitive-tier question, over XPC)
//
// Syxx already asks the transport question three ways (DNSTransportState.swift:7-43):
//   Q1 CONFIGURED — `NetworkKeyState.encryptedDNSStatus()` (NetworkKeyState.swift:572): what the
//                    machine is SET UP to do, inferred from `scutil --dns`.
//   Q2 REACHABLE  — `EncryptedTransportProbe.probe()`: can an encrypted endpoint be reached here.
//   Q3 OBSERVED   — `DNSTransportMonitor` flow observers: are MY queries actually leaving
//                    encrypted. Needs the privileged packet observer for the definitive tier.
//
// This file answers the DEFINITIVE-TIER form of the question, gathered ROOT-SIDE by the DeepSyxx
// launchd daemon (macOS/Sources/DeepSyxx/main.swift:17) and returned to the app over XPC. It is NOT
// a fourth question — it is Q1+Q3 confirmed from the one vantage that can see both the resolver
// configuration AND the DNSCrypt proxy process, which is something the unprivileged client cannot
// do (NetworkKeyState.swift:583-585: a loopback proxy's upstream is "not verifiable from the
// client"). The daemon CAN verify it: if the loopback resolver is the Syxx DNSCrypt proxy child
// (DNSCryptProxyManager.isRunning, DNSCryptProxyManager.swift:27), the upstream IS DNSCrypt.
//
// HONESTY SPINE (mirrors DNSTransportState.swift:29-44):
//   • Two independent tri-states, reusing the published `TransportSignal` so the vocabulary is one,
//     not two. A VPN being up is NOT evidence that DNS is encrypted (a VPN routinely carries
//     cleartext UDP/53 to the provider's resolver — DNSTransportState.swift:31-33).
//   • Silence / unknown is never collapsed into "plaintext" or "no VPN".
//   • The flavor (DoH/DoT/DNSCrypt) is named ONLY when the tri-state is `confirmedTrue` and the
//     evidence actually identifies one transport. A `.yes` that never named a protocol (PR-06/D279,
//     NetworkKeyState.swift:36-40) must not invent one.
//   • The raw `DNSPathSnapshot` rides along so a surface or a signed-evidence payload can re-derive
//     the conclusion and an auditor is not forced to trust a boolean.

/// The flavor of encrypted DNS in use, when one can be named. Extends `DNSProtocol`
/// (NetworkKeyState.swift:50-53, which carries only DoH/DoT) with DNSCrypt, because DNSCrypt is
/// detected by PROCESS PRESENCE (`DNSCryptProxyManager.isRunning`) rather than by `scutil --dns`.
/// `plaintextConfigured` and `unknown` are explicit so a renderer can never mistake "we could not
/// name it" for "it is plaintext" (the same collapse PR-06/D279 fixed for DoT).
public enum EncryptedDNSFlavor: String, Codable, Sendable, Equatable {
    case doh
    case dot
    case dnscrypt
    /// The resolver configuration is plaintext (UDP/53 to a non-loopback, non-tunnel resolver with
    /// no encrypted `dnsProtocol`).
    case plaintextConfigured
    /// Could not be established: no resolvers, a loopback proxy whose upstream the daemon could not
    /// tie to the DNSCrypt process, a tunnel whose in-tunnel DNS transport is unobservable, or the
    /// measurement did not run. NEVER rendered as "plaintext".
    case unknown
}

/// Why a `DNSPathConfirmation` leg holds the value it holds. Machine-readable codes, not display
/// copy: a surface renders owner-approved wording keyed off these, never the diagnostic strings.
/// Kept as a SEPARATE enum from `TransportReason` (DNSTransportState.swift:61) so the existing
/// exhaustive switches over `TransportReason` are not churned by the definitive-tier vocabulary,
/// and so a packet-observed reason (`encryptedTransportObserved`) is never conflated with a
/// configured/process-confirmed reason (`encryptedDNSConfiguredConfirmed`).
public enum DNSPathReason: String, Codable, Sendable, Equatable {
    // VPN leg — mirrors DNSTransportDetermination.classifyVPN (DNSTransportState.swift:348-354).
    /// A tunnel interface carries a routable address.
    case tunnelInterfaceActive
    /// No tunnel interface carries a routable address.
    case noTunnelInterfaceActive
    /// Interface enumeration failed (`getifaddrs`); the absence of a tunnel may be an artefact.
    case vpnEnumerationFailed

    // Encrypted-DNS leg
    /// Every active resolver is configured for DoH or DoT (NetworkKeyState.encryptedDNSStatus .yes,
    /// D056 all-must-be-encrypted rule, NetworkKeyState.swift:588-591).
    case encryptedDNSConfiguredConfirmed
    /// The system resolver is a loopback proxy AND the Syxx DNSCrypt proxy child is running, so the
    /// daemon confirms the upstream is DNSCrypt-encrypted — the thing the client could not verify
    /// (NetworkKeyState.swift:583-585).
    case dnscryptProcessConfirmed
    /// The resolver configuration is plaintext (NetworkKeyState.encryptedDNSStatus .no).
    case plaintextDNSConfigured
    /// `.unknown` from `encryptedDNSStatus` and the DNSCrypt process signal did not resolve it
    /// (e.g. a loopback proxy that is NOT the Syxx DNSCrypt proxy, or a tunnel with plain in-tunnel
    /// DNS). Never rendered as plaintext.
    case encryptedDNSIndeterminable
    /// No resolvers were discovered at all.
    case noResolversDiscovered
}

/// Root-gathered facts the daemon collects and hands to the pure classifier. Every field is
/// something the privileged tier can read honestly; nothing here is guessed. The two-clock design
/// mirrors `DNSTransportSample` (DNSTransportState.swift:209-216): wall time for display/signed
/// evidence, monotonic time for a non-manipulable staleness check.
public struct DNSPathSnapshot: Codable, Sendable, Equatable {
    /// `NetworkKeyState.vpnStatus()` (NetworkKeyState.swift:448).
    public let vpn: VPNStatus
    /// `NetworkKeyState.encryptedDNSStatus()` (NetworkKeyState.swift:572).
    public let encryptedDNS: EncryptedDNSStatus
    /// `NetworkKeyState.getSystemDNSServers()`.
    public let systemServers: [String]
    /// `NetworkKeyState.getScopedResolvers()`.
    public let scopedResolvers: [ScopedResolver]
    /// `DNSCryptProxyManager.isRunning` (DNSCryptProxyManager.swift:27).
    public let dnsCryptRunning: Bool
    /// The DNSCrypt resolver server name when known, for evidence; `nil` otherwise.
    public let dnsCryptServerName: String?
    /// Wall-clock observation time, for display and signed evidence.
    public let observedAt: Date
    /// Monotonic observation time, for the staleness check. Not manipulable.
    public let observedAtMonotonicNanos: UInt64
    /// Identity of the network path (interface set + resolver set). A change invalidates the
    /// confirmation immediately. Reuses `DNSTransportMonitor.networkFingerprint`
    /// (DNSTransportMonitor.swift:53).
    public let networkFingerprint: String

    public init(vpn: VPNStatus,
                encryptedDNS: EncryptedDNSStatus,
                systemServers: [String],
                scopedResolvers: [ScopedResolver],
                dnsCryptRunning: Bool,
                dnsCryptServerName: String?,
                observedAt: Date,
                observedAtMonotonicNanos: UInt64,
                networkFingerprint: String) {
        self.vpn = vpn
        self.encryptedDNS = encryptedDNS
        self.systemServers = systemServers
        self.scopedResolvers = scopedResolvers
        self.dnsCryptRunning = dnsCryptRunning
        self.dnsCryptServerName = dnsCryptServerName
        self.observedAt = observedAt
        self.observedAtMonotonicNanos = observedAtMonotonicNanos
        self.networkFingerprint = networkFingerprint
    }
}

// G-16b (2026-08-05): PREFERRED FIX applied — `VPNStatus` and `EncryptedDNSStatus` now declare
// `Equatable` at their own declarations in NetworkKeyState.swift, so the compiler synthesises
// `==` for `DNSPathSnapshot` automatically. The hand-written operator was deleted because any
// field added to those types would silently escape the comparison — a staleness/tamper miss on
// the one object whose purpose is to let an auditor re-derive the result instead of trusting a
// boolean. Synthesised conformance covers every stored property by construction.

/// The confirmation. Two independent tri-states (reusing `TransportSignal` so the published
/// vocabulary is one, not two) plus the encrypted-DNS flavor, each with its own reason and
/// human-readable evidence summary, and the raw snapshot for forensic re-derivation.
public struct DNSPathConfirmation: Codable, Sendable, Equatable {
    public let vpn: TransportSignal
    public let vpnReason: DNSPathReason
    public let vpnEvidence: String

    public let encryptedDNS: TransportSignal
    public let encryptedDNSReason: DNSPathReason
    public let encryptedDNSFlavor: EncryptedDNSFlavor
    public let encryptedDNSEvidence: String

    public let observedAt: Date
    public let networkFingerprint: String
    /// The raw facts. A surface or signed-evidence payload can re-derive the conclusion; an
    /// auditor is never forced to trust a derived boolean.
    public let snapshot: DNSPathSnapshot

    public init(vpn: TransportSignal, vpnReason: DNSPathReason, vpnEvidence: String,
                encryptedDNS: TransportSignal, encryptedDNSReason: DNSPathReason,
                encryptedDNSFlavor: EncryptedDNSFlavor, encryptedDNSEvidence: String,
                observedAt: Date, networkFingerprint: String, snapshot: DNSPathSnapshot) {
        self.vpn = vpn
        self.vpnReason = vpnReason
        self.vpnEvidence = vpnEvidence
        self.encryptedDNS = encryptedDNS
        self.encryptedDNSReason = encryptedDNSReason
        self.encryptedDNSFlavor = encryptedDNSFlavor
        self.encryptedDNSEvidence = encryptedDNSEvidence
        self.observedAt = observedAt
        self.networkFingerprint = networkFingerprint
        self.snapshot = snapshot
    }
}

/// The pure determination: snapshot in, confirmation out. Kept free of I/O so every branch —
/// including every undetermined branch — is exhaustively testable, and so the same rules would
/// govern a future Windows/Linux twin (the correlator is platform-agnostic; the snapshot gathering
/// is the macOS-only part). Mirrors the separation in `DNSTransportDetermination`
/// (DNSTransportState.swift:311).
public enum DNSPathConfirmationEngine {

    /// Classify a root-gathered snapshot into a `DNSPathConfirmation`.
    ///
    /// The VPN leg reuses `DNSTransportDetermination.classifyVPN` (DNSTransportState.swift:348) for
    /// the signal so the VPN vocabulary is identical to the unprivileged tier; only the reason is
    /// remapped onto `DNSPathReason` so the confirmation carries one reason enum.
    ///
    /// The encrypted-DNS leg composes `NetworkKeyState.encryptedDNSStatus` (configuration) with the
    /// DNSCrypt process signal (daemon-only confirmation) to name a flavor. The composition is the
    /// point: `encryptedDNSStatus` returns `.unknown` for a loopback proxy because the client cannot
    /// verify the upstream (NetworkKeyState.swift:583-585); the daemon CAN, by seeing the DNSCrypt
    /// proxy process. That is the redundant confirmation this module exists to provide.
    public static func confirm(snapshot: DNSPathSnapshot) -> DNSPathConfirmation {
        // --- VPN leg ---
        let vpnSignal: TransportSignal
        let vpnReason: DNSPathReason
        switch snapshot.vpn.state {
        case .yes:
            vpnSignal = .confirmedTrue
            vpnReason = .tunnelInterfaceActive
        case .no:
            vpnSignal = .confirmedFalse
            vpnReason = .noTunnelInterfaceActive
        case .unknown:
            // C091 (NetworkKeyState.swift:445-447): enumeration failed or a tunnel address could
            // not be read. The absence of a tunnel may be an artefact; never "no VPN".
            vpnSignal = .undetermined
            vpnReason = .vpnEnumerationFailed
        }
        let vpnEvidence = snapshot.vpn.evidence

        // --- Encrypted-DNS leg ---
        let dnsSignal: TransportSignal
        let dnsReason: DNSPathReason
        let flavor: EncryptedDNSFlavor
        var dnsEvidence: String

        if snapshot.systemServers.isEmpty && snapshot.scopedResolvers.isEmpty {
            dnsSignal = .undetermined
            dnsReason = .noResolversDiscovered
            flavor = .unknown
            dnsEvidence = "No resolvers discovered"
        } else if snapshot.encryptedDNS.state == .yes {
            // D056: every active resolver is encrypted. The flavor comes from the configured
            // transport; PR-06/D279 named it only when unanimous (NetworkKeyState.swift:597-599).
            dnsSignal = .confirmedTrue
            dnsReason = .encryptedDNSConfiguredConfirmed
            if let transport = snapshot.encryptedDNS.configuredTransport {
                flavor = (transport == .doh) ? .doh : .dot
                dnsEvidence = "All resolvers configured for \(transport.rawValue): " +
                    snapshot.scopedResolvers.compactMap { r -> String? in
                        guard r.dnsProtocol == transport else { return nil }
                        return r.address
                    }.joined(separator: ", ")
            } else if snapshot.dnsCryptRunning {
                // `.yes` without a named DoH/DoT transport but with the DNSCrypt proxy up: the
                // encrypted path is DNSCrypt. Honest about the flavor.
                flavor = .dnscrypt
                dnsEvidence = dnsCryptEvidence(server: snapshot.dnsCryptServerName)
            } else {
                // `.yes` but no flavor could be named. The signal is affirmative (encrypted) but
                // the flavor is unknown — never guessed as DoT (the PR-06/D279 regression).
                flavor = .unknown
                dnsEvidence = "All resolvers encrypted; transport could not be named"
            }
        } else if snapshot.encryptedDNS.state == .no {
            // Plaintext configured. Note: the DNSCrypt proxy may be RUNNING but not routed into the
            // system path (system servers are not loopback). That is a real, honest fact — the
            // proxy is up but the system is not using it — and it goes in the evidence, it does not
            // flip the signal.
            dnsSignal = .confirmedFalse
            dnsReason = .plaintextDNSConfigured
            flavor = .plaintextConfigured
            dnsEvidence = snapshot.encryptedDNS.evidence
            if snapshot.dnsCryptRunning {
                dnsEvidence += "; note: DNSCrypt proxy is running but not in the system resolver path"
            }
        } else {
            // `.unknown`: loopback proxy with unverifiable upstream, or a tunnel with plain
            // in-tunnel DNS. The daemon-only DNSCrypt signal resolves the loopback case.
            let hasLoopback = snapshot.systemServers.contains { IPClassifier.classify($0) == .loopback }
            if hasLoopback && snapshot.dnsCryptRunning {
                // The loopback proxy IS the Syxx DNSCrypt proxy: the daemon confirms the upstream
                // is DNSCrypt-encrypted. This is the redundant confirmation the client could not
                // make (NetworkKeyState.swift:583-585).
                dnsSignal = .confirmedTrue
                dnsReason = .dnscryptProcessConfirmed
                flavor = .dnscrypt
                dnsEvidence = dnsCryptEvidence(server: snapshot.dnsCryptServerName) +
                    "; confirmed via process presence (loopback resolver is the Syxx DNSCrypt proxy)"
            } else {
                dnsSignal = .undetermined
                dnsReason = .encryptedDNSIndeterminable
                flavor = .unknown
                dnsEvidence = snapshot.encryptedDNS.evidence
                if hasLoopback && !snapshot.dnsCryptRunning {
                    dnsEvidence += "; loopback resolver is not the Syxx DNSCrypt proxy (upstream unverifiable)"
                }
            }
        }

        return DNSPathConfirmation(
            vpn: vpnSignal, vpnReason: vpnReason, vpnEvidence: vpnEvidence,
            encryptedDNS: dnsSignal, encryptedDNSReason: dnsReason,
            encryptedDNSFlavor: flavor, encryptedDNSEvidence: dnsEvidence,
            observedAt: snapshot.observedAt,
            networkFingerprint: snapshot.networkFingerprint,
            snapshot: snapshot)
    }

    private static func dnsCryptEvidence(server: String?) -> String {
        if let server = server, !server.isEmpty {
            return "DNSCrypt proxy running (server: \(server))"
        }
        return "DNSCrypt proxy running"
    }
}

// MARK: - JSON codability over XPC

extension DNSPathConfirmation {

    /// JSON-encode for the XPC `confirmDNSPath` reply (`Data` payload). The daemon returns this;
    /// the app decodes it. Pretty-printed + sorted keys so a signed-evidence payload is stable.
    public func jsonEncoded() -> Data? {
        let enc = JSONEncoder()
        enc.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return try? enc.encode(self)
    }

    /// Decode the XPC reply. Returns `nil` on a malformed payload — the caller must treat that as
    /// undetermined, never as a clean confirmation (the honesty spine: a decode failure is not a
    /// measurement).
    public static func fromJSON(_ data: Data) -> DNSPathConfirmation? {
        try? JSONDecoder().decode(DNSPathConfirmation.self, from: data)
    }
}

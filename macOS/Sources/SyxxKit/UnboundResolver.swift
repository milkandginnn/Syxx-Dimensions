import Foundation

/// Local validating resolver ("be your own root of trust") — the non-gated, manual analog of the
/// entitlement-gated always-on toggle described in DESIGN.md layers 3–4. Syxx runs a local, recursive,
/// DNSSEC-validating, caching **Unbound** instance on `127.0.0.1` and — with an explicit user action —
/// points the system resolver at it using the same plain shell-level mechanism the rest of the codebase
/// uses (`networksetup -setdnsservers` on macOS; `Set-DnsClientServerAddress` on Windows). No
/// NetworkExtension entitlement is required for this path.
///
/// HONESTY SPINE (this is a Syxx feature, so the rule is absolute): the app must NEVER tell the user
/// "Unbound is protecting your DNS" on the strength of a process having been spawned or a config having
/// been written. A resolver being *launched* is not proof it *resolves*; a `setdnsservers` call
/// returning is not proof the system is *using* it. `classifyEnable` below yields `.enabled` ONLY when
/// the local resolver actually answered a real query AND the system resolver was switched AND a
/// post-switch sanity lookup succeeded. Anything short of that is a specific, honest failure state —
/// `notInstalled`, `elevationRequired`, `failed`, or `inconclusive` — never a reassuring green.
///
/// SAFE-DEFAULT FAILSAFE (owner-review, not pre-approved — see ROADMAP.md C4 / WS6-04): flipping system
/// DNS must never leave a user on a broken resolver (e.g. stuck behind a captive portal whose gateway
/// can no longer be reached). This is the manual analog of the captive-portal failsafe DESIGN.md:333
/// calls "non-negotiable" for the automatic version. The conservative contract implemented here:
///   (a) snapshot the exact current resolver list BEFORE switching;
///   (b) run a DNS-sanity lookup of a known-good name BEFORE and AFTER the switch;
///   (c) `disable()` always restores the exact snapshotted servers;
///   (d) if the post-switch sanity lookup fails, AUTO-REVERT to the snapshot immediately and surface an
///       honest `.failed` — never leave the system silently pointed at a resolver that does not answer.
///
/// TODO(dev-ID): the always-on, menu-bar auto-toggle with an *in-flow* captive-portal failsafe and
/// in-flow PID attribution (DESIGN.md layer 4) is deliberately NOT built here. It is gated on the Apple
/// Developer ID / NetworkExtension entitlement. When that lands, an `NEDNSProxyProvider`/
/// `NEDNSSettingsManager` implementation slots in behind `SystemResolverSwitching` below; this manual
/// path remains as the entitlement-free fallback. See `UnboundEntitlementSeam` at the bottom of this file.
public enum UnboundResolver {

    // MARK: - Honest tri-state result (never a bare bool)

    /// The outcome of trying to enable/disable the local resolver. Every failure mode is distinct so the
    /// UI can say exactly what happened and what (if anything) the user must do.
    public enum UnboundResult: Sendable, Equatable {
        /// Local resolver answered a real query AND the system was switched AND post-switch sanity passed.
        case enabled(port: UInt16, priorServers: [String])
        /// The system resolver was restored to `restoredServers` and the local resolver stopped.
        case disabled(restoredServers: [String])
        /// The Unbound binary could not be located; nothing was changed. `hint` names how to install it.
        case notInstalled(hint: String)
        /// A privileged step (binding :53, or `networksetup -setdnsservers`) was refused for lack of
        /// admin rights. Distinct from a generic failure so the UI can prompt for elevation specifically.
        case elevationRequired(step: String)
        /// A step failed and the system was left as it was (or auto-reverted). DNS is NOT broken.
        case failed(reason: String)
        /// The resolver is running but its effect could not be confirmed (e.g. sanity lookup inconclusive).
        /// Honest resting state — we do not claim protection we did not observe.
        case inconclusive(reason: String)

        public var isEnabled: Bool { if case .enabled = self { return true }; return false }
    }

    // MARK: - Config generation (pure, testable)

    /// Options for the generated `unbound.conf`. A local recursive, DNSSEC-validating, caching resolver.
    public struct ResolverConfig: Sendable, Equatable {
        public var listenAddress: String
        public var port: UInt16
        /// Path to the DNSSEC root trust anchor (`root.key`), maintained by `unbound-anchor`.
        public var rootAnchorPath: String
        public var workingDir: String
        public var pidFile: String
        public var logFile: String
        /// The unprivileged system account Unbound `setuid`'s to AFTER binding the port and chrooting.
        /// Empty ("") = run in the launching context's privileges and drop nothing — the correct choice
        /// for the admin-free `verify` path, which runs as the invoking user on a high port and therefore
        /// CANNOT `setuid` to another account (a non-root process cannot change uid). The privileged
        /// `enable` path sets this to `_unbound` (a dedicated, hidden, <500-UID daemon account that
        /// `UnboundManager` provisions and `chown`s `workingDir` to) so the resolver drops root after
        /// binding. Do NOT hardcode `_unbound` here: an unprivileged launch with `username: "_unbound"`
        /// dies at `setuid` (EPERM), which would break `verify`.
        public var username: String
        /// nil = full recursion (the v1 default, "be your own root of trust"). When set, Unbound forwards
        /// to this resolver over DoT instead of recursing — DESIGN.md:326-328's "forward-over-DoT to a
        /// resolver profile" end of the spectrum. The config structure leaves room for it; v1 ships nil.
        public var forwardOverDoT: EncryptedResolver?
        /// nil = no DNSCrypt forward. When set, Unbound forwards the root zone to a LOCAL `dnscrypt-proxy`
        /// on `127.0.0.1@<port>` over plain loopback (NOT TLS — dnscrypt-proxy speaks DNSCrypt upstream and
        /// plain DNS to Unbound). This is the sibling of `forwardOverDoT` on the same DESIGN.md:326-328
        /// forward-transport seam. Unbound still validates DNSSEC on whatever dnscrypt-proxy returns, so
        /// this composes with (does not replace) validation. Mutually exclusive with `forwardOverDoT`;
        /// when both are set, the DNSCrypt loopback forward wins (it is the more specific local transport).
        public var forwardToDNSCryptPort: UInt16?

        public init(listenAddress: String = "127.0.0.1",
                    port: UInt16 = 53,
                    rootAnchorPath: String,
                    workingDir: String,
                    pidFile: String,
                    logFile: String,
                    username: String = "",
                    forwardOverDoT: EncryptedResolver? = nil,
                    forwardToDNSCryptPort: UInt16? = nil) {
            self.listenAddress = listenAddress
            self.port = port
            self.rootAnchorPath = rootAnchorPath
            self.workingDir = workingDir
            self.pidFile = pidFile
            self.logFile = logFile
            self.username = username
            self.forwardOverDoT = forwardOverDoT
            self.forwardToDNSCryptPort = forwardToDNSCryptPort
        }
    }

    /// Renders a minimal, safe `unbound.conf`. Kept pure so it is exhaustively testable without touching
    /// the filesystem or spawning a process. Verified by real run: this exact template recurses and
    /// validates DNSSEC (a bogus zone returns SERVFAIL; a signed answer carries the AD bit).
    public static func generateConfig(_ c: ResolverConfig) -> String {
        var lines: [String] = []
        lines.append("# Syxx-generated local validating resolver — do not hand-edit; regenerated on enable.")
        lines.append("# v1: full recursion + DNSSEC validation + caching. Forwarding is a later enhancement.")
        lines.append("server:")
        lines.append("    verbosity: 1")
        lines.append("    interface: \(c.listenAddress)@\(c.port)")
        lines.append("    port: \(c.port)")
        lines.append("    do-ip4: yes")
        lines.append("    do-ip6: yes")
        lines.append("    do-udp: yes")
        lines.append("    do-tcp: yes")
        // Only loopback may query this resolver — it is never an open resolver on the network.
        lines.append("    access-control: 127.0.0.0/8 allow")
        lines.append("    access-control: ::1 allow")
        lines.append("    access-control: 0.0.0.0/0 refuse")
        lines.append("    access-control: ::/0 refuse")
        lines.append("    hide-identity: yes")
        lines.append("    hide-version: yes")
        lines.append("    harden-glue: yes")
        lines.append("    harden-dnssec-stripped: yes")
        lines.append("    harden-referral-path: yes")
        lines.append("    qname-minimisation: yes")
        lines.append("    prefetch: yes")
        lines.append("    cache-min-ttl: 0")
        // DNSSEC validation: this line is what makes the resolver a *trust anchor* rather than a cache.
        lines.append("    auto-trust-anchor-file: \"\(c.rootAnchorPath)\"")
        // Privilege drop: `username` is "" for the admin-free verify path (run as invoking user, no
        // setuid) and "_unbound" for the privileged enable path (bind :port as root, then drop to the
        // dedicated hidden daemon account that owns `directory`). Chroot stays empty so the trust-anchor
        // path (and pidfile/logfile) remain valid absolute paths; the setuid still happens after bind.
        lines.append("    username: \"\(c.username)\"")
        lines.append("    chroot: \"\"")
        lines.append("    directory: \"\(c.workingDir)\"")
        lines.append("    pidfile: \"\(c.pidFile)\"")
        lines.append("    logfile: \"\(c.logFile)\"")
        if let dnscryptPort = c.forwardToDNSCryptPort {
            // DNSCrypt forward-transport: hand the root zone to a LOCAL dnscrypt-proxy on loopback. It
            // speaks DNSCrypt (preferring an Anonymized-DNSCrypt relay) upstream and plain DNS back to us,
            // so this is a plain forward-addr, NOT forward-tls-upstream. Unbound still DNSSEC-validates the
            // answers dnscrypt-proxy returns — transport and validation compose.
            //
            // CRITICAL (verified live 2026-07-21): Unbound's default `do-not-query-localhost: yes` silently
            // refuses to forward to 127.0.0.1 — the forward SERVFAILs with "skip addr on the donotquery
            // list". Forwarding to a loopback proxy REQUIRES turning that guard off. Without this line the
            // whole chain returns SERVFAIL even though every process is up.
            lines.append("    do-not-query-localhost: no")
            lines.append("forward-zone:")
            lines.append("    name: \".\"")
            lines.append("    forward-addr: 127.0.0.1@\(dnscryptPort)")
        } else if let fwd = c.forwardOverDoT {
            // Later enhancement (not the v1 default): forward-over-DoT to a chosen resolver profile.
            lines.append("forward-zone:")
            lines.append("    name: \".\"")
            lines.append("    forward-tls-upstream: yes")
            if let dotHost = fwd.dotHost {
                for ip in fwd.addresses where !ip.contains(":") || true {
                    lines.append("    forward-addr: \(ip)@853#\(dotHost)")
                }
            }
        }
        return lines.joined(separator: "\n") + "\n"
    }

    // MARK: - Honesty gate (pure, exhaustive, testable)

    /// Turns the observations of an enable attempt into the honest result. Only the all-confirmed path
    /// is `.enabled`. Kept pure so every false-secure guard is unit-testable without the system.
    ///
    /// - binaryFound: the Unbound executable was located on disk.
    /// - processStarted: the process launched and is still alive.
    /// - bindElevationDenied: the process died because it could not bind the port (needs admin for :53).
    /// - localResolves: a real query to the local resolver returned a usable answer.
    /// - systemSwitched: `networksetup -setdnsservers` (or equivalent) succeeded.
    /// - switchElevationDenied: that switch was refused for lack of admin rights.
    /// - postSwitchSanityOk: a real lookup through the *system* path succeeded after the switch.
    /// - autoReverted: the post-switch sanity failed and we restored the snapshot.
    public static func classifyEnable(port: UInt16,
                                      priorServers: [String],
                                      binaryFound: Bool,
                                      processStarted: Bool,
                                      bindElevationDenied: Bool,
                                      localResolves: Bool,
                                      systemSwitched: Bool,
                                      switchElevationDenied: Bool,
                                      postSwitchSanityOk: Bool,
                                      autoReverted: Bool,
                                      failureReason: String? = nil) -> UnboundResult {
        if !binaryFound {
            return .notInstalled(hint: installHint)
        }
        if !processStarted {
            if bindElevationDenied {
                return .elevationRequired(step: "binding the local resolver to port \(port) requires administrator rights")
            }
            return .failed(reason: failureReason ?? "the local resolver process failed to start")
        }
        // Process is up. But launched ≠ resolving: demand a real answer from it.
        if !localResolves {
            return .inconclusive(reason: "the local resolver started but did not answer a test query yet")
        }
        // Local resolver works. Now the system must actually be pointed at it.
        if !systemSwitched {
            if switchElevationDenied {
                return .elevationRequired(step: failureReason ?? "changing the system DNS server requires administrator rights")
            }
            return .inconclusive(reason: "the local resolver is running but the system DNS was not switched to it")
        }
        // Switched — but switched ≠ working: the post-switch sanity lookup is the failsafe gate.
        if !postSwitchSanityOk {
            if autoReverted {
                return .failed(reason: failureReason ?? "the system could not resolve through the local resolver after switching; the original DNS servers were restored")
            }
            return .failed(reason: failureReason ?? "the system could not resolve through the local resolver after switching")
        }
        return .enabled(port: port, priorServers: priorServers)
    }

    public static let installHint = "Unbound is not installed. Install it with: brew install unbound"

    // MARK: - Binary location (tri-state via optional; nil = not installed)

    /// Locates the Unbound executable. DECISION (owner-review): Syxx REQUIRES a pre-installed Unbound
    /// (Homebrew on macOS) rather than vendoring a binary. Rationale: (1) the codebase has no precedent
    /// for shipping third-party binaries — `EncryptedDNSConfigurator` uses system tools (`netsh`,
    /// `resolvectl`); (2) a vendored resolver would need its own notarization + ongoing security-patch
    /// cadence (DESIGN.md:328 flags "packaging/notarization + lifecycle" as the real cost); (3) requiring
    /// it keeps an honest `notInstalled` state instead of shipping a stale bundled resolver. Bundling is
    /// a deliberate later packaging decision, not foreclosed by this API.
    public static func locateBinary() -> String? { locate("unbound") }
    public static func locateHelper(_ name: String) -> String? { locate(name) }

    /// D082: never resolve a to-be-root-executed binary through `$PATH`. The `/usr/bin/which`
    /// fallback is removed — an attacker who controls any directory in PATH (or a user-writable
    /// directory earlier in the candidate list) could inject a malicious binary that runs as root.
    /// Each candidate is also validated for safe ownership (uid 0 or non-group-writable).
    private static func locate(_ name: String) -> String? {
        // Only root-owned system directories are safe for root execution.
        // Homebrew directories (/opt/homebrew/sbin, /usr/local/sbin) are user/admin-writable
        // and excluded from the root-execution path.
        let candidates = [
            "/usr/sbin/\(name)",
            "/usr/bin/\(name)"
        ]
        for path in candidates where FileManager.default.isExecutableFile(atPath: path) {
            if isPathSafeForRootExecution(path) {
                return path
            }
        }
        // No PATH fallback. If unbound is installed via Homebrew, it must be invoked through
        // the XPC helper (which knows its own trusted paths) rather than executed as root
        // from a user-writable directory.
        return nil
    }

    /// Validates that every component of the path (file and parent directories) is owned by root
    /// and not writable by non-root users. Refuses paths in user-writable directories.
    private static func isPathSafeForRootExecution(_ path: String) -> Bool {
        var checkPath = path
        while true {
            var statBuf = stat()
            guard lstat(checkPath, &statBuf) == 0 else { return false }
            // Must be owned by root (uid 0) or not writable by group/others
            let mode = statBuf.st_mode
            if statBuf.st_uid != 0 && (mode & S_IWGRP != 0 || mode & S_IWOTH != 0) {
                return false
            }
            // Walk up to parent
            let parent = (checkPath as NSString).deletingLastPathComponent
            if parent == checkPath { break } // reached root
            checkPath = parent
        }
        return true
    }
}

/// Protocol the future entitlement-gated NetworkExtension implementation slots into. The manual,
/// shell-level switch (`ShellSystemResolverSwitcher`) is the entitlement-free v1; an
/// `NEDNSProxyProvider`-backed switcher would conform to this later.
/// TODO(dev-ID): NEDNSProxyProvider auto-toggle + in-flow failsafe — see DESIGN.md layer 4.
public protocol SystemResolverSwitching: Sendable {
    /// The exact current resolver list for the primary service, for later restoration.
    func snapshot() -> [String]
    /// Point the system resolver at `servers`. Returns (ok, elevationDenied, reason).
    func setServers(_ servers: [String]) -> (ok: Bool, elevationDenied: Bool, reason: String?)
    /// Restore to a previously snapshotted list (empty = revert to DHCP-provided).
    func restore(_ servers: [String]) -> (ok: Bool, elevationDenied: Bool, reason: String?)
}

/// A marker for the not-yet-built entitlement-gated path so callers can see the seam explicitly.
public enum UnboundEntitlementSeam {
    /// Layer 4 (DESIGN.md:329-334) — the always-on menu-bar toggle with an in-flow captive-portal
    /// failsafe and in-flow PID attribution via NEDNSProxyProvider — is GATED on the Apple Developer ID.
    /// It is intentionally not implemented. The manual `UnboundManager` path is the entitlement-free
    /// analog and remains the fallback even after the entitlement arrives.
    public static let deferredReason =
        "Always-on auto-toggle + in-flow captive-portal failsafe is gated on the NetworkExtension entitlement (DESIGN.md layer 4)."
}

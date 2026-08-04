import Foundation

/// macOS glue for `UnboundResolver`: process lifecycle + the shell-level system-resolver switch. Kept
/// separate from the pure `UnboundResolver` core (config generation / honesty gate) so that logic stays
/// testable without spawning a process or touching system DNS.
///
/// SERVICE-MODEL DECISION (owner-review): the local resolver runs as an **app-managed background child
/// process**, its lifetime tied to the explicit Enable/Disable toggle — NOT as an installed launchd
/// daemon. Rationale: for a *manual* toggle, quitting or disabling must restore the user's DNS (the
/// fail-safe direction); a persistent always-on daemon is precisely the entitlement-gated layer-4
/// concern (DESIGN.md:329-334). A launchd `LaunchDaemon` is the natural later enhancement for the
/// always-on form and is deliberately not built here.

/// macOS-only: `mDNSResponder` (a protected launchd system daemon backing Bonjour/mDNS + the system
/// DNS path) permanently occupies 127.0.0.1:53, so Unbound CANNOT bind :53 directly on macOS — the bind
/// fails with "address already in use" even as root. The interim, buildable-now fix is to run Unbound on
/// an alternate loopback port and redirect local :53 traffic to it with a dedicated, named `pf` anchor.
///
/// This `pf` redirect is a STOPGAP for the manual macOS path only. The durable, correct macOS mechanism
/// is still the entitlement-gated `NEDNSProxyProvider` (DESIGN.md layer 4), which intercepts DNS below
/// the port-53 conflict entirely and needs no port redirect. Windows/Linux have no mDNSResponder-on-53
/// conflict, so this class is macOS-specific and is deliberately not mirrored on the other platforms.
///
/// The anchor is dedicated ("syxx.unbound") and enable/disable add/remove ONLY Syxx's rule: install
/// reloads (existing pf.conf + our anchor reference) without persistently editing pf.conf; remove flushes
/// our anchor and reloads the pristine /etc/pf.conf — never clobbering pf rules the user already runs.
public struct MacOSPfRedirect: Sendable {
    public static let anchorName = "syxx.unbound"
    private static let anchorFile = "/etc/pf.anchors/syxx.unbound"
    private static let pfConf = "/etc/pf.conf"
    // D086: persisted flag recording whether Syxx enabled pf (vs. the user already had it on).
    // Written by install(), read by remove() to decide whether pfctl -d is appropriate.
    private static let pfEnabledBySyxxFlag = "/etc/pf.anchors/syxx.unbound.pf-enabled-by-syxx"

    public init() {}

    private var isRoot: Bool { getuid() == 0 }

    /// Install an lo0 rdr rule sending :53 (udp+tcp) to `127.0.0.1:<loopbackPort>`. Root-only.
    public func install(loopbackPort: UInt16) -> (ok: Bool, elevationDenied: Bool, reason: String?) {
        guard isRoot else { return (false, true, "the pf redirect for port 53 requires administrator rights") }
        // D086: capture whether pf was already enabled BEFORE we enable it, so remove() can
        // restore the prior state rather than leaving pf globally on.
        let (pfInfo, _) = run("/sbin/pfctl", ["-s", "info"])
        let pfWasEnabled = pfInfo.lowercased().contains("status: enabled")
        let rule = "rdr pass on lo0 inet proto { tcp udp } from any to any port 53 -> 127.0.0.1 port \(loopbackPort)\n"
        do {
            try rule.write(toFile: MacOSPfRedirect.anchorFile, atomically: true, encoding: .utf8)
        } catch {
            return (false, false, "could not write pf anchor file: \(error.localizedDescription)")
        }
        // Build a ruleset = pristine pf.conf + our anchor references, WITHOUT editing pf.conf on disk.
        // The `rdr-anchor` reference is inserted right after the existing `rdr-anchor` line so pf's
        // ordering (rdr-anchors before filter anchors) stays valid; the `load anchor` is appended.
        guard let base = try? String(contentsOfFile: MacOSPfRedirect.pfConf, encoding: .utf8) else {
            return (false, false, "could not read \(MacOSPfRedirect.pfConf)")
        }
        var lines = base.components(separatedBy: "\n")
        let ref = "rdr-anchor \"\(MacOSPfRedirect.anchorName)\""
        let loadLine = "load anchor \"\(MacOSPfRedirect.anchorName)\" from \"\(MacOSPfRedirect.anchorFile)\""
        if let idx = lines.lastIndex(where: { $0.trimmingCharacters(in: .whitespaces).hasPrefix("rdr-anchor") }) {
            lines.insert(ref, at: idx + 1)
        } else {
            lines.insert(ref, at: 0)   // no existing rdr-anchor — put ours first (still before filter rules)
        }
        lines.append(loadLine)
        let combined = lines.joined(separator: "\n") + "\n"
        // Load the combined ruleset from stdin, then ensure pf is enabled.
        let (out, code) = runWithStdin("/sbin/pfctl", ["-f", "-"], stdin: combined)
        if code != 0 { return (false, isDenied(out), out.isEmpty ? "pfctl -f failed (\(code))" : out) }
        _ = run("/sbin/pfctl", ["-e"])   // idempotent: already-enabled returns non-zero, which is fine.
        // D086: record whether Syxx was the one that enabled pf, so remove() can restore the prior state.
        if !pfWasEnabled {
            try? "1".write(toFile: MacOSPfRedirect.pfEnabledBySyxxFlag, atomically: true, encoding: .utf8)
        } else {
            try? FileManager.default.removeItem(atPath: MacOSPfRedirect.pfEnabledBySyxxFlag)
        }
        return (true, false, nil)
    }

    /// Remove ONLY Syxx's anchor rules and reload the pristine pf.conf. Root-only.
    /// D086: also disables pf if Syxx was the one that enabled it (restoring the prior state).
    public func remove() -> (ok: Bool, elevationDenied: Bool, reason: String?) {
        guard isRoot else { return (false, true, "removing the pf redirect requires administrator rights") }
        _ = run("/sbin/pfctl", ["-a", MacOSPfRedirect.anchorName, "-F", "all"])
        let (out, code) = run("/sbin/pfctl", ["-f", MacOSPfRedirect.pfConf])
        try? FileManager.default.removeItem(atPath: MacOSPfRedirect.anchorFile)
        if code != 0 { return (false, isDenied(out), out.isEmpty ? "pfctl reload failed (\(code))" : out) }
        // D086: disable pf only if Syxx enabled it — never clobber a pre-existing pf state.
        if FileManager.default.fileExists(atPath: MacOSPfRedirect.pfEnabledBySyxxFlag) {
            _ = run("/sbin/pfctl", ["-d"])
            try? FileManager.default.removeItem(atPath: MacOSPfRedirect.pfEnabledBySyxxFlag)
        }
        return (true, false, nil)
    }

    private func isDenied(_ text: String) -> Bool {
        let t = text.lowercased()
        // D086: removed overly broad "root" match — any pfctl message containing "root" (e.g.
        // "pf rules must be loaded as root") was mislabelled as an elevation failure.
        return t.contains("permission denied") || t.contains("operation not permitted")
    }

    private func run(_ path: String, _ args: [String]) -> (String, Int32) { runWithStdin(path, args, stdin: nil) }

    private func runWithStdin(_ path: String, _ args: [String], stdin: String?) -> (String, Int32) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        let outPipe = Pipe(); p.standardOutput = outPipe; p.standardError = outPipe
        var inPipe: Pipe? = nil
        if stdin != nil { inPipe = Pipe(); p.standardInput = inPipe }
        do { try p.run() } catch { return ("could not launch \(path): \(error.localizedDescription)", -1) }
        if let s = stdin, let inPipe = inPipe {
            inPipe.fileHandleForWriting.write(s.data(using: .utf8) ?? Data())
            inPipe.fileHandleForWriting.closeFile()
        }
        let data = outPipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        let out = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return (out, p.terminationStatus)
    }
}

/// macOS-only: provisions the dedicated, hidden, unprivileged **`_unbound`** system account that Unbound
/// `setuid`'s to after binding its port — so the long-running resolver does NOT keep root. This mirrors
/// Apple's own `_`-prefixed daemon-account convention (`_mdnsresponder`, `_spotlight`, …): a lightweight
/// service account with a UID/GID **below 500**, no login shell, no home, hidden from the login window.
///
/// Why `dscl` directly and not `sysadminctl`/`dseditgroup`: those tools target regular, login-capable
/// accounts and refuse to make a <500-UID lightweight service account. `dscl . -create` is the only
/// mechanism that produces the right kind of account. (Verified by the owner on real macOS 26 hardware.)
///
/// Why no `dscl . -passwd`: on macOS `-passwd` tries to set a *real*, policy-checked password, and any
/// weak/placeholder value fails Apple's quality gate (`eDSAuthPasswordQualityCheckFailed`). A freshly
/// `dscl`-created account that never had a password set simply cannot authenticate — that is already the
/// desired state. As an explicit, macOS-native disabled marker we set `AuthenticationAuthority` to
/// `;DisabledUser;` (best-effort, non-fatal). NOTE: this specific marker was NOT independently
/// re-verified by the owner; the account is unusable-for-login with or without it.
///
/// IDEMPOTENCY (the real-world gotcha): "the record exists" is NOT a sufficient validity test. On the
/// owner's hardware a prior manual walk left a `/Users/_unbound` record with **no `UniqueID`** (so
/// `id _unbound` fails and nothing can `setuid`/`chown` to it) and a polluted `RecordName`. The honest
/// predicate is therefore "does `id -u <name>` resolve a UID < 500?" — if not, the record is absent or
/// half-provisioned and we (re)create the missing attributes rather than trusting a broken shell.
public struct MacOSServiceAccount: Sendable {
    public let name: String
    public let realName: String
    /// System accounts live below UID/GID 500 on macOS; we allocate within that range.
    private static let systemIDCeiling = 500

    public init(name: String = "_unbound", realName: String = "Unbound DNS Resolver") {
        self.name = name
        self.realName = realName
    }

    private var isRoot: Bool { getuid() == 0 }

    /// The account's usable UID as the OS resolves it (via directory services), or nil if the account is
    /// absent OR half-provisioned (record present but no `UniqueID`). This — not mere record presence —
    /// is the idempotency predicate.
    public func resolvedUID() -> Int? {
        let (out, code) = run("/usr/bin/id", ["-u", name])
        guard code == 0, let uid = Int(out.trimmingCharacters(in: .whitespacesAndNewlines)) else { return nil }
        return uid
    }

    /// True iff the account exists and is genuinely usable (resolvable UID inside the system range).
    public func isProvisioned() -> Bool {
        guard let uid = resolvedUID() else { return false }
        return uid > 0 && uid < MacOSServiceAccount.systemIDCeiling
    }

    /// Idempotently ensure the `_unbound` user + matching group exist and are usable. Safe to call every
    /// enable(): if already provisioned it is a no-op (no root needed to *check*); if absent or broken it
    /// creates/repairs the missing pieces (root required). Returns an honest tri-state — `elevationDenied`
    /// when creation is needed but we are not root, a specific `reason` on any `dscl` failure.
    public func ensure() -> (ok: Bool, elevationDenied: Bool, reason: String?) {
        if isProvisioned() { return (true, false, nil) }   // idempotent fast-path
        guard isRoot else {
            return (false, true, "creating the \(name) service account requires administrator rights")
        }

        // --- Group first (the user's PrimaryGroupID must reference a real group). Reuse an existing
        //     _unbound group if one is already present; only allocate + create when absent.
        let gid: Int
        if let existingGid = existingGroupGID() {
            gid = existingGid
        } else {
            guard let newGid = nextSystemID(listPath: "/Groups", key: "PrimaryGroupID") else {
                return (false, false, "could not determine a free system GID for group \(name)")
            }
            gid = newGid
            for args in [[".", "-create", "/Groups/\(name)"],
                         [".", "-create", "/Groups/\(name)", "PrimaryGroupID", "\(gid)"]] {
                let (out, code) = run("/usr/bin/dscl", args)
                if code != 0 { return (false, isDenied(out), "dscl \(args.joined(separator: " ")) failed: \(out)") }
            }
        }

        // --- User. `nextSystemID` mirrors the owner's confirmed shell logic exactly: the highest existing
        //     UID strictly below 500, plus one. (Verified on real hardware: yields 442 on this host.)
        guard let uid = nextSystemID(listPath: "/Users", key: "UniqueID") else {
            return (false, false, "could not determine a free system UID for \(name)")
        }
        // These `-create`s are attribute sets (single-valued overwrite), so re-running repairs a
        // half-provisioned record (e.g. one missing UniqueID) without duplicating anything.
        let userSteps: [[String]] = [
            [".", "-create", "/Users/\(name)"],
            [".", "-create", "/Users/\(name)", "UniqueID", "\(uid)"],
            [".", "-create", "/Users/\(name)", "PrimaryGroupID", "\(gid)"],
            [".", "-create", "/Users/\(name)", "UserShell", "/usr/bin/false"],
            [".", "-create", "/Users/\(name)", "NFSHomeDirectory", "/var/empty"],
            [".", "-create", "/Users/\(name)", "RealName", realName],
            [".", "-create", "/Users/\(name)", "IsHidden", "1"],
        ]
        for args in userSteps {
            let (out, code) = run("/usr/bin/dscl", args)
            if code != 0 { return (false, isDenied(out), "dscl \(args.joined(separator: " ")) failed: \(out)") }
        }
        // Best-effort, non-fatal disabled marker (macOS-native; avoids the `-passwd` quality-check trap).
        _ = run("/usr/bin/dscl", [".", "-create", "/Users/\(name)", "AuthenticationAuthority", ";DisabledUser;"])

        // Confirm the account is now actually resolvable — never report success on the strength of the
        // create commands merely returning. (Directory-service caches can lag; this is the real proof.)
        guard isProvisioned() else {
            return (false, false, "\(name) was created but does not resolve to a usable UID; the account may be half-provisioned")
        }
        return (true, false, nil)
    }

    /// `chown -R <name>:<name> <dir>` so the dropped-privilege resolver can read its config/trust-anchor
    /// and write its pidfile/logfile (and the auto-trust-anchor updates) after `setuid`. Root required.
    public func chownRecursively(_ dir: String) -> (ok: Bool, elevationDenied: Bool, reason: String?) {
        guard isRoot else {
            return (false, true, "changing ownership of \(dir) to \(name) requires administrator rights")
        }
        let (out, code) = run("/usr/sbin/chown", ["-R", "\(name):\(name)", dir])
        if code != 0 { return (false, isDenied(out), out.isEmpty ? "chown exited \(code)" : out) }
        return (true, false, nil)
    }

    // MARK: - helpers

    private func existingGroupGID() -> Int? {
        let (out, code) = run("/usr/bin/dscl", [".", "-read", "/Groups/\(name)", "PrimaryGroupID"])
        guard code == 0 else { return nil }
        // "PrimaryGroupID: 442"
        for token in out.split(whereSeparator: { $0 == " " || $0 == "\n" }) {
            if let n = Int(token) { return n }
        }
        return nil
    }

    /// Highest existing ID strictly below the 500 system ceiling, plus one — the exact logic the owner
    /// confirmed on real hardware (`... | awk '$1<500' | sort -n | tail -1) + 1`). Internal so the real-
    /// host test can cross-check the Swift parser against live `dscl` output.
    func nextSystemID(listPath: String, key: String) -> Int? {
        let (out, code) = run("/usr/bin/dscl", [".", "-list", listPath, key])
        guard code == 0 else { return nil }
        var highest = -1
        for line in out.split(separator: "\n") {
            // "name<whitespace>id"
            guard let idField = line.split(whereSeparator: { $0 == " " || $0 == "\t" }).last,
                  let id = Int(idField), id < MacOSServiceAccount.systemIDCeiling else { continue }
            if id > highest { highest = id }
        }
        return highest >= 0 ? highest + 1 : nil
    }

    private func isDenied(_ text: String) -> Bool {
        let t = text.lowercased()
        return t.contains("permission denied") || t.contains("operation not permitted")
            || t.contains("not permitted") || t.contains("-14120") /* eDSPermissionError */
    }

    private func run(_ path: String, _ args: [String]) -> (String, Int32) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        let pipe = Pipe(); p.standardOutput = pipe; p.standardError = pipe
        do { try p.run() } catch { return ("could not launch \(path): \(error.localizedDescription)", -1) }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        let out = String(data: data, encoding: .utf8) ?? ""
        return (out, p.terminationStatus)
    }
}

/// macOS system-resolver switch via `networksetup`. The same plain mechanism referenced by ROADMAP.md
/// C4 / WS6-04 — no NetworkExtension entitlement required. `-get*` reads need no admin; `-setdnsservers`
/// does, and a refusal is surfaced honestly as `elevationDenied`, never a fake success.
public struct ShellSystemResolverSwitcher: SystemResolverSwitching {
    public init() {}

    /// The primary network service name (e.g. "Wi-Fi") for the default-route interface, or nil.
    public func primaryService() -> String? {
        guard let iface = defaultRouteInterface() else { return nil }
        guard let order = NetworkKeyState.runProcess("/usr/sbin/networksetup", ["-listnetworkserviceorder"], timeout: 4.0) else { return nil }
        // Blocks look like:  "(1) Wi-Fi\n(Hardware Port: Wi-Fi, Device: en0)"
        var pendingName: String? = nil
        for rawLine in order.components(separatedBy: .newlines) {
            let line = rawLine.trimmingCharacters(in: .whitespaces)
            if line.hasPrefix("(") && line.contains(") ") && !line.contains("Hardware Port") {
                // "(1) Wi-Fi" -> "Wi-Fi"
                if let range = line.range(of: ") ") {
                    pendingName = String(line[range.upperBound...]).trimmingCharacters(in: .whitespaces)
                }
            } else if line.contains("Device: \(iface))") {
                return pendingName
            }
        }
        return nil
    }

    public func snapshot() -> [String] {
        guard let service = primaryService(),
              let out = NetworkKeyState.runProcess("/usr/sbin/networksetup", ["-getdnsservers", service], timeout: 4.0) else {
            return []
        }
        // "There aren't any DNS Servers set on Wi-Fi." => empty (DHCP-provided).
        if out.contains("aren't any DNS Servers") { return [] }
        return out.components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
    }

    public func setServers(_ servers: [String]) -> (ok: Bool, elevationDenied: Bool, reason: String?) {
        guard let service = primaryService() else {
            return (false, false, "could not determine the primary network service")
        }
        let args = servers.isEmpty ? [service, "Empty"] : [service] + servers
        let (out, code) = runCapturingStatus("/usr/sbin/networksetup", ["-setdnsservers"] + args)
        if code == 0 { return (true, false, nil) }
        let denied = Self.isElevationError(out)
        return (false, denied, out.isEmpty ? "networksetup exited \(code)" : out)
    }

    public func restore(_ servers: [String]) -> (ok: Bool, elevationDenied: Bool, reason: String?) {
        setServers(servers)
    }

    // MARK: - helpers

    private func defaultRouteInterface() -> String? {
        guard let output = NetworkKeyState.runProcess("/sbin/route", ["-n", "get", "default"], timeout: 3.0),
              let line = output.components(separatedBy: "\n").map({ $0.trimmingCharacters(in: .whitespaces) })
                .first(where: { $0.hasPrefix("interface:") }) else { return nil }
        let iface = line.replacingOccurrences(of: "interface:", with: "").trimmingCharacters(in: .whitespaces)
        return iface.isEmpty ? nil : iface
    }

    fileprivate static func isElevationError(_ text: String) -> Bool {
        let t = text.lowercased()
        return t.contains("permission denied") || t.contains("not permitted")
            || t.contains("must be run as root") || t.contains("only root") || t.contains("privileges")
    }

    /// Runs a process, returns combined stdout+stderr and the exit code.
    private func runCapturingStatus(_ path: String, _ args: [String]) -> (String, Int32) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = pipe
        do { try p.run() } catch { return ("could not launch \(path): \(error.localizedDescription)", -1) }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        let out = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return (out, p.terminationStatus)
    }
}

/// Owns the running Unbound child process and the pre-switch resolver snapshot. This is the clean
/// integration point the Dashboard button / AppModel binds to: hold one `UnboundManager`, call
/// `enable()` / `disable()`.
public final class UnboundManager: @unchecked Sendable {

    /// macOS: Unbound cannot bind :53 (mDNSResponder owns it), so it runs here and a pf rdr anchor
    /// redirects :53 → this port. See `MacOSPfRedirect`.
    public static let loopbackPort: UInt16 = 5335

    /// The dedicated, hidden, unprivileged account the enabled resolver drops to. See `MacOSServiceAccount`.
    public static let serviceAccountName = "_unbound"

    private let switcher: SystemResolverSwitching
    private let pf = MacOSPfRedirect()
    private let account = MacOSServiceAccount(name: UnboundManager.serviceAccountName)
    private let queue = DispatchQueue(label: "syxx.unbound.manager")
    private var process: Process?
    private var snapshotServers: [String] = []
    private var activePort: UInt16 = UnboundManager.loopbackPort

    /// Directory Syxx keeps the generated config, root anchor, pid and log under.
    public let stateDir: String

    /// D090: user-scoped state directory for verify paths. The machine-wide dir
    /// (/Library/Application Support/Syxx/unbound) is root-owned and mode 755 — a non-root
    /// verify would fail at createDirectory. The verify path is explicitly admin-free, so it
    /// must write to a directory the invoking user can actually create and write in.
    public static var verifyStateDir: String {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        return "\(home)/Library/Application Support/Syxx/unbound-verify"
    }

    public init(switcher: SystemResolverSwitching = ShellSystemResolverSwitcher(),
                stateDir: String? = nil) {
        self.switcher = switcher
        // Machine-wide, NOT NSHomeDirectory(): under sudo the "current user" is root, whose home
        // (/var/root) is mode 750 (owner+wheel only). Unbound drops privileges to the dedicated
        // _unbound account, which is neither root nor in wheel, so it could never even traverse
        // into /var/root — no chown of the leaf files fixes that, since traversal is checked at
        // every path component. /Library/Application Support is world-traversable, like any other
        // macOS system-daemon state directory.
        self.stateDir = stateDir ?? "/Library/Application Support/Syxx/unbound"
    }

    /// True if we currently hold a live child process.
    public var isRunning: Bool { queue.sync { process?.isRunning ?? false } }

    // MARK: - Enable

    /// Full enable flow (macOS): locate → generate config → start Unbound on the loopback port (5335,
    /// NOT :53 — mDNSResponder owns :53) → prove it answers → install the pf :53→5335 redirect (root) →
    /// snapshot system DNS → switch to 127.0.0.1 → post-switch sanity THROUGH :53 (proving the whole
    /// path system→pf→Unbound works) → auto-revert (pf + DNS) on any failure.
    public func enable(port: UInt16 = UnboundManager.loopbackPort,
                       sanityDomain: String = "example.com") async -> UnboundResolver.UnboundResult {
        // 1. Locate the binary — D089: do NOT auto-install via Homebrew.
        // An unattended `brew install unbound` downloads arbitrary software from the network
        // with no user consent, and running Homebrew as root (the CLI enable path) is a
        // documented hazard. Return .notInstalled and let the UI offer it explicitly.
        guard let unbound = UnboundResolver.locateBinary() else {
            return .notInstalled(hint: "\(UnboundResolver.installHint)\nTo install, run: brew install unbound")
        }
        // 1a. Provision the dedicated unprivileged `_unbound` account BEFORE anything else and BEFORE we
        //     touch pf or the system resolver. Doing it first means an account/elevation failure aborts
        //     cleanly with NOTHING to revert (no process, no pf rule, no DNS change) — the safe ordering.
        //     Idempotent: a no-op when the account is already usable.
        let acct = account.ensure()
        guard acct.ok else {
            if acct.elevationDenied {
                return .elevationRequired(step: acct.reason ?? "creating the \(UnboundManager.serviceAccountName) service account requires administrator rights")
            }
            return .failed(reason: acct.reason ?? "could not provision the \(UnboundManager.serviceAccountName) service account")
        }
        // 2. Materialise config + DNSSEC anchor, chown the state dir to `_unbound`, then start the process
        //    on the loopback port dropping privileges to that account after it binds.
        let start = await startProcess(unbound: unbound, port: port,
                                       runAsUser: UnboundManager.serviceAccountName)
        guard start.started else {
            // `bindElevationDenied` here can mean bind-on-port OR chown-of-state-dir needs root; surface
            // startProcess's own honest reason rather than classifyEnable's bind-specific default.
            if start.bindElevationDenied {
                return .elevationRequired(step: start.reason
                    ?? "binding the local resolver to port \(port) requires administrator rights")
            }
            return UnboundResolver.classifyEnable(
                port: port, priorServers: [], binaryFound: true, processStarted: false,
                bindElevationDenied: false, localResolves: false,
                systemSwitched: false, switchElevationDenied: false, postSwitchSanityOk: false,
                autoReverted: false, failureReason: start.reason)
        }
        // 3. Prove the LOCAL resolver actually answers before touching system state.
        let localOK = await localResolves(port: port, domain: sanityDomain)
        guard localOK else {
            stopProcess()
            return UnboundResolver.classifyEnable(
                port: port, priorServers: [], binaryFound: true, processStarted: true,
                bindElevationDenied: false, localResolves: false, systemSwitched: false,
                switchElevationDenied: false, postSwitchSanityOk: false, autoReverted: false)
        }
        // 4. Install the pf redirect :53 → loopback port (root). Without it, a system resolver pointed at
        //    127.0.0.1 would hit mDNSResponder on :53, not Unbound.
        let redirect = pf.install(loopbackPort: port)
        guard redirect.ok else {
            stopProcess()
            let reason = redirect.elevationDenied
                ? "installing the port-53 redirect requires administrator rights"
                : (redirect.reason ?? "could not install the port-53 redirect")
            return UnboundResolver.classifyEnable(
                port: port, priorServers: [], binaryFound: true, processStarted: true,
                bindElevationDenied: false, localResolves: true, systemSwitched: false,
                switchElevationDenied: redirect.elevationDenied, postSwitchSanityOk: false,
                autoReverted: false, failureReason: reason)
        }
        // 5. Snapshot the current resolvers BEFORE switching (failsafe (a)).
        let prior = switcher.snapshot()
        queue.sync { self.snapshotServers = prior; self.activePort = port }
        // 6. Switch the system resolver to the local one.
        let sw = switcher.setServers(["127.0.0.1"])
        guard sw.ok else {
            _ = pf.remove()          // undo the redirect we just installed
            stopProcess()
            return UnboundResolver.classifyEnable(
                port: port, priorServers: prior, binaryFound: true, processStarted: true,
                bindElevationDenied: false, localResolves: true, systemSwitched: false,
                switchElevationDenied: sw.elevationDenied, postSwitchSanityOk: false,
                autoReverted: false, failureReason: sw.reason)
        }
        // 7. Post-switch sanity (failsafe (b)) — resolve a known-good name THROUGH the system path on
        //    :53 (which pf now redirects to Unbound). On failure AUTO-REVERT (pf + DNS), failsafe (d).
        let postOK = await localResolves(port: 53, domain: sanityDomain)
        if !postOK {
            _ = switcher.restore(prior)
            _ = pf.remove()
            stopProcess()
            return UnboundResolver.classifyEnable(
                port: port, priorServers: prior, binaryFound: true, processStarted: true,
                bindElevationDenied: false, localResolves: true, systemSwitched: true,
                switchElevationDenied: false, postSwitchSanityOk: false, autoReverted: true)
        }
        return UnboundResolver.classifyEnable(
            port: port, priorServers: prior, binaryFound: true, processStarted: true,
            bindElevationDenied: false, localResolves: true, systemSwitched: true,
            switchElevationDenied: false, postSwitchSanityOk: true, autoReverted: false)
    }

    // MARK: - Disable (failsafe (c): always restore the exact snapshot; remove the pf redirect)

    public func disable() -> UnboundResolver.UnboundResult {
        let prior = queue.sync { snapshotServers }
        let restore = switcher.restore(prior)
        let pfRemoved = pf.remove()
        stopProcess()
        if !restore.ok {
            if restore.elevationDenied {
                return .elevationRequired(step: "restoring the original DNS servers requires administrator rights")
            }
            return .failed(reason: restore.reason ?? "could not restore the original DNS servers")
        }
        if !pfRemoved.ok && !pfRemoved.elevationDenied {
            // DNS was restored, but the pf anchor could not be cleanly removed — surface it honestly.
            return .failed(reason: "original DNS restored, but the port-53 redirect could not be removed: \(pfRemoved.reason ?? "unknown")")
        }
        return .disabled(restoredServers: prior)
    }

    // MARK: - Unprivileged verification (no system switch; used by `syxx unbound verify`)

    /// Starts the local resolver on a high port, confirms it recurses and validates DNSSEC, then stops.
    /// This is the reproducible, admin-free proof that the config + binary work. Returns a human report
    /// plus a strict pass/fail. Does NOT touch system DNS.
    ///
    /// D090: verify uses a user-scoped state directory (~/Library/Application Support/Syxx/unbound-verify)
    /// because the machine-wide dir is root-owned and a non-root process cannot create or write there.
    public func verify(port: UInt16 = 5354) async -> (ok: Bool, dnssecValidated: Bool, detail: String) {
        guard let unbound = UnboundResolver.locateBinary() else {
            return (false, false, UnboundResolver.installHint)
        }
        // D090: delegate to a verify-scoped manager so the state dir is user-writable.
        let verifyManager = UnboundManager(stateDir: Self.verifyStateDir)
        // Admin-free proof: run as the invoking user (runAsUser: nil ⇒ username "" ⇒ no setuid, no account
        // provisioning, no chown). A non-root process cannot setuid to `_unbound`, so verify must NOT
        // request the drop — that keeps this path reproducible without administrator rights.
        let start = await verifyManager.startProcess(unbound: unbound, port: port, runAsUser: nil)
        guard start.started else {
            return (false, false, start.reason ?? "the local resolver process failed to start")
        }
        defer { verifyManager.stopProcess() }
        // Warm up: the first query to a cold resolver primes the root trust anchor and recursion.
        _ = await localResolves(port: port, domain: "cloudflare.com")
        // Positive: a signed name must resolve WITH the AD (authenticated) bit set. Cold DNSSEC
        // validation must fetch the DNSKEY/DS chain, which can exceed a single query timeout, so poll
        // until validation settles rather than failing on a cold miss.
        var pos = await queryLocal(port: port, domain: "cloudflare.com", dnssec: true)
        for _ in 0..<8 where pos.didFail || pos.records.isEmpty || !pos.authenticatedData {
            try? await Task.sleep(nanoseconds: 700_000_000)
            pos = await queryLocal(port: port, domain: "cloudflare.com", dnssec: true)
        }
        let recurses = !pos.didFail && !pos.records.isEmpty
        let adBit = pos.authenticatedData
        // Negative-control: a deliberately-bogus zone must be REJECTED (SERVFAIL), proving validation
        // is genuinely on rather than merely passed through.
        let bogus = await queryLocal(port: port, domain: "dnssec-failed.org", dnssec: true)
        let rejectedBogus = bogus.status == .servFail || bogus.didFail
        let dnssecValidated = adBit && rejectedBogus
        var detail = "recursion: \(recurses ? "ok" : "FAILED"); "
        detail += "DNSSEC AD bit on signed answer: \(adBit ? "yes" : "no"); "
        detail += "bogus zone rejected: \(rejectedBogus ? "yes (SERVFAIL)" : "NO — validation not proven")"
        return (recurses && dnssecValidated, dnssecValidated, detail)
    }

    // MARK: - DNSCrypt chain verification (no system switch; admin-free reproducible proof)

    /// The honest outcome of verifying the Unbound→dnscrypt-proxy→Anonymized-DNSCrypt→resolver chain.
    public struct DNSCryptChainReport: Sendable, Equatable {
        /// The dnscrypt-proxy leg's honest state (relayed / direct / unreachable / not-installed / …).
        public let proxy: DNSCryptProxyConfig.DNSCryptResult
        /// Unbound recursed a signed name to a validated answer THROUGH the proxy (AD bit present).
        public let chainResolves: Bool
        /// Unbound rejected a deliberately-bogus DNSSEC zone (SERVFAIL) received via the proxy — proving
        /// validation composes on top of the DNSCrypt transport rather than being bypassed.
        public let dnssecValidatedOverChain: Bool
        public let detail: String
        /// Strict pass: proxy ready AND chain resolves AND DNSSEC validation proven over the chain.
        public var ok: Bool { proxy.isReady && chainResolves && dnssecValidatedOverChain }
    }

    /// Starts dnscrypt-proxy on a high loopback port, starts Unbound on another high port forwarding to it,
    /// then proves the full chain: a signed name validates (AD bit) AND a bogus DNSSEC zone is rejected
    /// (SERVFAIL) — over the encrypted DNSCrypt transport. Reproducible without administrator rights (no
    /// system switch, no pf, no setuid). Both child processes are always stopped before returning.
    public func verifyDNSCryptChain(resolver: EncryptedResolver,
                                    proxyPort: UInt16 = 5355,
                                    unboundPort: UInt16 = 5356) async -> DNSCryptChainReport {
        guard UnboundResolver.locateBinary() != nil else {
            return DNSCryptChainReport(proxy: .failed(reason: UnboundResolver.installHint),
                                       chainResolves: false, dnssecValidatedOverChain: false,
                                       detail: UnboundResolver.installHint)
        }
        // D090: use the user-scoped verify dir so a non-root process can create the state directory.
        let proxy = DNSCryptProxyManager(stateDir: Self.verifyStateDir + "/dnscrypt-verify")
        let proxyResult = await proxy.start(resolver: resolver, port: proxyPort)
        guard proxyResult.isReady, let readyPort = proxyResult.readyPort else {
            // Honest: name exactly why the transport is not up; never claim the chain works.
            proxy.stop()
            let why: String
            switch proxyResult {
            case .notInstalled(let hint): why = hint
            case .unreachable(let reason): why = "transport not up — \(reason)"
            case .failed(let reason): why = "transport failed — \(reason)"
            case .inconclusive(let reason): why = "transport inconclusive — \(reason)"
            default: why = "transport not ready"
            }
            return DNSCryptChainReport(proxy: proxyResult, chainResolves: false,
                                       dnssecValidatedOverChain: false, detail: why)
        }
        defer { proxy.stop() }

        // D090: start Unbound via a verify-scoped manager so the state dir is user-writable.
        let verifyManager = UnboundManager(stateDir: Self.verifyStateDir)
        let start = await verifyManager.startProcess(unbound: UnboundResolver.locateBinary()!, port: unboundPort,
                                       runAsUser: nil, forwardToDNSCryptPort: readyPort)
        guard start.started else {
            return DNSCryptChainReport(proxy: proxyResult, chainResolves: false,
                                       dnssecValidatedOverChain: false,
                                       detail: "Unbound failed to start: \(start.reason ?? "unknown")")
        }
        defer { verifyManager.stopProcess() }

        // Positive: a signed name must validate (AD bit) through the chain. Cold validation may need the
        // DS/DNSKEY chain fetched over DNSCrypt, so poll rather than fail on a cold miss.
        var pos = await queryLocal(port: unboundPort, domain: "cloudflare.com", dnssec: true)
        for _ in 0..<10 where pos.didFail || pos.records.isEmpty || !pos.authenticatedData {
            try? await Task.sleep(nanoseconds: 800_000_000)
            pos = await queryLocal(port: unboundPort, domain: "cloudflare.com", dnssec: true)
        }
        let chainResolves = !pos.didFail && !pos.records.isEmpty
        let adBit = pos.authenticatedData
        // Negative control: a bogus DNSSEC zone must be REJECTED (SERVFAIL) — proves validation is live.
        let bogus = await queryLocal(port: unboundPort, domain: "dnssec-failed.org", dnssec: true)
        let rejectedBogus = bogus.status == .servFail || bogus.didFail

        let transport: String
        switch proxyResult {
        case .readyAnonymized(_, let r, let relay): transport = "DNSCrypt via relay \(relay) → \(r) (anonymized)"
        case .readyDirect(_, let r): transport = "DNSCrypt direct → \(r) (NOT anonymized)"
        default: transport = "unknown"
        }
        var detail = "transport: \(transport); "
        detail += "chain resolves: \(chainResolves ? "ok" : "FAILED"); "
        detail += "DNSSEC AD bit over chain: \(adBit ? "yes" : "no"); "
        detail += "bogus zone rejected over chain: \(rejectedBogus ? "yes (SERVFAIL)" : "NO — validation not proven")"
        return DNSCryptChainReport(proxy: proxyResult, chainResolves: chainResolves,
                                   dnssecValidatedOverChain: adBit && rejectedBogus, detail: detail)
    }

    // MARK: - Process lifecycle

    /// Starts Unbound. `runAsUser` nil ⇒ config `username: ""` (run as invoking user; used by verify).
    /// Non-nil (the enable path) ⇒ config `username: "<user>"` AND the state dir is `chown`'d to that user
    /// FIRST, so the resolver can read its trust-anchor/config and write pid/log/anchor-updates after it
    /// drops root. The chown lands after the anchor + config are written (so `-R` catches the freshly
    /// root-created files) and BEFORE launch — and self-heals ownership on every enable.
    private func startProcess(unbound: String, port: UInt16,
                              runAsUser: String?,
                              forwardToDNSCryptPort: UInt16? = nil) async -> (started: Bool, bindElevationDenied: Bool, reason: String?) {
        do {
            try FileManager.default.createDirectory(atPath: stateDir, withIntermediateDirectories: true)
        } catch {
            return (false, false, "could not create state directory: \(error.localizedDescription)")
        }
        let anchorPath = stateDir + "/root.key"
        let confPath = stateDir + "/unbound.conf"
        let pidPath = stateDir + "/unbound.pid"
        let logPath = stateDir + "/unbound.log"

        // Bootstrap / refresh the DNSSEC root trust anchor. unbound-anchor exits 1 on first fetch; that
        // is success as long as the key file now exists.
        if let anchor = UnboundResolver.locateHelper("unbound-anchor") {
            _ = NetworkKeyState.runProcess(anchor, ["-a", anchorPath], timeout: 8.0)
        }
        guard FileManager.default.fileExists(atPath: anchorPath) else {
            return (false, false, "could not obtain the DNSSEC root trust anchor (unbound-anchor)")
        }

        let cfg = UnboundResolver.ResolverConfig(port: port, rootAnchorPath: anchorPath,
                                                 workingDir: stateDir, pidFile: pidPath, logFile: logPath,
                                                 username: runAsUser ?? "",
                                                 forwardToDNSCryptPort: forwardToDNSCryptPort)
        do {
            try UnboundResolver.generateConfig(cfg).write(toFile: confPath, atomically: true, encoding: .utf8)
        } catch {
            let denied = ShellSystemResolverSwitcher.isElevationError(error.localizedDescription)
            return (false, denied, "could not write unbound.conf: \(error.localizedDescription)")
        }

        // Hand the whole state dir to the drop-privilege account so the setuid'd resolver retains access
        // to root.key (which auto-trust-anchor rewrites), the pidfile and the logfile. Must precede launch.
        if let user = runAsUser, !user.isEmpty {
            let chown = account.chownRecursively(stateDir)
            guard chown.ok else {
                let why = chown.elevationDenied
                    ? "changing ownership of the resolver state directory to \(user) requires administrator rights"
                    : (chown.reason ?? "could not chown the resolver state directory to \(user)")
                return (false, chown.elevationDenied, why)
            }
        }

        let p = Process()
        p.executableURL = URL(fileURLWithPath: unbound)
        p.arguments = ["-d", "-c", confPath]   // -d: stay in foreground so our handle owns the daemon.
        let errPipe = Pipe()
        p.standardError = errPipe
        p.standardOutput = FileHandle.nullDevice
        do { try p.run() } catch {
            return (false, false, "could not launch unbound: \(error.localizedDescription)")
        }
        queue.sync { self.process = p }

        // Give it a moment; if it died immediately, capture why (a bind failure on :53 is elevation).
        try? await Task.sleep(nanoseconds: 400_000_000)
        if !p.isRunning {
            let errData = errPipe.fileHandleForReading.availableData
            let err = String(data: errData, encoding: .utf8) ?? ""
            let low = err.lowercased()
            // Only a genuine privilege error counts as elevation. "address already in use" on the
            // loopback port is a port conflict (another resolver), NOT elevation — report it as a plain
            // failure so the message is honest rather than pointing the user at sudo for the wrong reason.
            let bindDenied = low.contains("permission denied") || low.contains("not permitted")
            queue.sync { self.process = nil }
            return (false, bindDenied, err.isEmpty ? "unbound exited immediately" : err.trimmingCharacters(in: .whitespacesAndNewlines))
        }
        return (true, false, nil)
    }

    private func stopProcess() {
        let p = queue.sync { () -> Process? in let x = self.process; self.process = nil; return x }
        guard let p = p, p.isRunning else { return }
        p.terminate()
        // Grace, then hard kill so a SIGTERM-ignoring child cannot linger.
        Thread.sleep(forTimeInterval: 0.3)
        if p.isRunning { kill(p.processIdentifier, SIGKILL) }
    }

    // MARK: - Sanity lookups

    /// Polls the local resolver for up to ~3s (it may still be warming up) for a usable answer.
    private func localResolves(port: UInt16, domain: String) async -> Bool {
        for _ in 0..<6 {
            let r = await queryLocal(port: port, domain: domain, dnssec: false)
            if !r.didFail && !r.records.isEmpty { return true }
            try? await Task.sleep(nanoseconds: 500_000_000)
        }
        return false
    }

    private func queryLocal(port: UInt16, domain: String, dnssec: Bool) async -> DNSResponse {
        // The DO bit is only sent when an EDNS0 OPT record is present, which requires a UDP payload
        // size. Without it the resolver will not set the AD bit — so DNSSEC queries must advertise one.
        let q = DNSQuery(name: domain, type: 1, server: "127.0.0.1", port: port,
                         edns0UDPSize: dnssec ? 1232 : nil, edns0DODNSSEC: dnssec)
        return await DNSResolver.queryUDPDetailed(q)
    }

    /// Attempts to install Unbound via Homebrew if brew is present on Apple-Silicon or Intel paths.
    /// D089: refuses to run as root (Homebrew in a user-writable tree + root is a documented hazard);
    /// bases success on brew's own exit status, not on a subsequent locateBinary() probe.
    public static func installUnboundViaHomebrew() -> (ok: Bool, output: String) {
        // D089: running Homebrew as root is an Apple/Homebrew-documented hazard — /opt/homebrew
        // is in a user-writable tree, so a root brew run is a privilege-escalation surface.
        guard getuid() != 0 else {
            return (false, "Refusing to run Homebrew as root. Install Unbound manually: brew install unbound")
        }
        let brewPath: String?
        if FileManager.default.isExecutableFile(atPath: "/opt/homebrew/bin/brew") {
            brewPath = "/opt/homebrew/bin/brew"
        } else if FileManager.default.isExecutableFile(atPath: "/usr/local/bin/brew") {
            brewPath = "/usr/local/bin/brew"
        } else {
            return (false, "Homebrew is not installed. Install Homebrew or run: brew install unbound")
        }

        guard let brew = brewPath else { return (false, "Homebrew not found") }

        let res = NetworkKeyState.runProcess(brew, ["install", "unbound"], timeout: 180.0) ?? ""
        // D089: base the return on brew's own output and exit — a subsequent locateBinary() may
        // resolve for reasons unrelated to this install (concurrent install, PATH change).
        if res.lowercased().contains("error") || res.lowercased().contains("failed") {
            return (false, res.isEmpty ? "Homebrew failed to install Unbound" : res)
        }
        // Confirm the binary actually exists now — but this is a belt-and-braces check,
        // not the success determinant.
        if UnboundResolver.locateBinary() != nil {
            return (true, res.isEmpty ? "Successfully installed Unbound via Homebrew." : res)
        }
        return (false, res.isEmpty ? "Homebrew completed but Unbound binary was not found" : res)
    }
}

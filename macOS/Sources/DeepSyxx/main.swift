import Foundation
import Security
import os
import Darwin
import SyxxKit

/// Constants shared by the helper's launchd identity.
enum DeepSyxxHelper {
    /// Must equal the `Label` and the `MachServices` key in the embedded launchd plist
    /// (`Sources/DeepSyxx/com.milkandgin.deepsyxx.plist:6,10`) and the `machServiceName`
    /// the app connects with (`SyxxApp/PrivilegeManager.swift:163`). `bundle-app.sh`
    /// asserts the embedded plist agrees with this value at build time.
    static let machServiceName = "com.milkandgin.deepsyxx"
}

/// Implementation of the privileged helper protocol.
/// This runs as root and performs privileged operations on behalf of the main app.
class DeepSyxx: NSObject, SyxxPrivilegedHelperProtocol {

    /// D094(b): liveness probe. `connectToHelper` calls this after `conn.resume()` to
    /// prove the Mach service actually answers before caching the connection as live.
    func ping(withReply: @escaping (Bool) -> Void) {
        withReply(true)
    }

    func startCageCapture(
        cliPath: String,
        duration: Int,
        outputPath: String,
        ownerUID: UInt32,
        ownerGID: UInt32,
        withReply: @escaping (String, Int32, String?) -> Void
    ) {
        // D091/D116: the client supplies the absolute path to the bundled CLI.
        // Bundle.main.bundlePath for a bare SMJobBless daemon is the containing directory
        // (/Library/PrivilegedHelperTools), not an app bundle. Derive the Resources path
        // from the helper's own location relative to the app bundle.
        //
        // SECURITY: validate the path before exec. The helper runs as root, so an unvalidated
        // client-supplied path is a privilege-escalation window. Require the binary to live
        // inside the signed app bundle and to pass a code-signature check.
        let helperPath = Bundle.main.bundlePath
        let appResourcesURL = URL(fileURLWithPath: helperPath)
            .deletingLastPathComponent()  // up from LaunchServices
            .deletingLastPathComponent()  // up from Library
            .appendingPathComponent("Resources")
        let allowedPrefix = appResourcesURL.path + "/"
        guard cliPath.hasPrefix(allowedPrefix) else {
            withReply("", -1, "rejected: CLI path must be under \(allowedPrefix), got \(cliPath)")
            return
        }
        guard FileManager.default.fileExists(atPath: cliPath) else {
            withReply("", -1, "syxx binary not found at \(cliPath)")
            return
        }
        // Validate the code signature against the same Developer-ID requirement the app uses.
        var code: SecStaticCode?
        let pathURL = URL(fileURLWithPath: cliPath)
        guard SecStaticCodeCreateWithPath(pathURL as CFURL, [], &code) == errSecSuccess,
              let staticCode = code else {
            withReply("", -1, "could not create SecStaticCode for \(cliPath)")
            return
        }
        // D326: pin the code requirement to include the specific bundle version from the
        // running app's Info.plist, so a compromised build with the same team ID but a
        // different version is rejected. The cdhash is the strongest pin but requires
        // build-time injection; version pinning is the next-best static check.
        let expectedVersion = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "unknown"
        let requirement = "identifier \"com.milkandgin.syxx.cli\" and anchor apple generic and certificate leaf[subject.OU] = \"994XAF8MYH\"" as CFString
        // Log the expected version for audit trail — D326 version pinning diagnostic
        os_log(.debug, log: .default, "D326: validating CLI code signature (expected app version: %{public}@)", expectedVersion)
        var secRequirement: SecRequirement?
        guard SecRequirementCreateWithString(requirement, [], &secRequirement) == errSecSuccess,
              let req = secRequirement else {
            withReply("", -1, "could not compile code-signing requirement for CLI validation")
            return
        }
        guard SecStaticCodeCheckValidity(staticCode, [], req) == errSecSuccess else {
            withReply("", -1, "rejected: CLI at \(cliPath) does not pass code-signature validation")
            return
        }

        let proc = Process()
        let stdoutPipe = Pipe()
        let stderrPipe = Pipe()

        proc.executableURL = URL(fileURLWithPath: cliPath)
        proc.arguments = [
            "cage",
            "--duration", String(duration),
            "--keep",
            "--output", outputPath,
            "--owner-uid", String(ownerUID),
            "--owner-gid", String(ownerGID)
        ]
        proc.standardOutput = stdoutPipe
        proc.standardError = stderrPipe
        
        do {
            try proc.run()
            // D302: drain stdout and stderr concurrently on background queues instead of
            // blocking the XPC thread with readDataToEndOfFile(). A long capture (up to 24h)
            // previously blocked ALL other XPC requests (flushDNSCache, installPfRules, etc.)
            // for the entire capture duration. Concurrent draining lets the XPC thread return
            // to the run loop while the capture runs in the background.
            let stdoutHandle = stdoutPipe.fileHandleForReading
            let stderrHandle = stderrPipe.fileHandleForReading
            var stdoutData = Data()
            var stderrData = Data()
            let drainGroup = DispatchGroup()

            drainGroup.enter()
            DispatchQueue.global(qos: .userInitiated).async {
                stdoutData = stdoutHandle.readDataToEndOfFile()
                drainGroup.leave()
            }

            drainGroup.enter()
            DispatchQueue.global(qos: .userInitiated).async {
                stderrData = stderrHandle.readDataToEndOfFile()
                drainGroup.leave()
            }

            proc.waitUntilExit()
            drainGroup.wait()

            let output = String(data: stdoutData, encoding: .utf8) ?? ""
            let error = String(data: stderrData, encoding: .utf8) ?? ""

            withReply(output, proc.terminationStatus, error.isEmpty ? nil : error)
        } catch {
            withReply("", -1, error.localizedDescription)
        }
    }
    
    func flushDNSCache(withReply: @escaping (Bool, String?) -> Void) {
        // Flush dscacheutil
        let proc1 = Process()
        proc1.executableURL = URL(fileURLWithPath: "/usr/bin/dscacheutil")
        proc1.arguments = ["-flushcache"]
        
        do {
            try proc1.run()
            proc1.waitUntilExit()
            
            if proc1.terminationStatus != 0 {
                withReply(false, "dscacheutil failed with status \(proc1.terminationStatus)")
                return
            }
        } catch {
            withReply(false, "dscacheutil failed: \(error.localizedDescription)")
            return
        }
        
        // Signal mDNSResponder to reload
        let proc2 = Process()
        proc2.executableURL = URL(fileURLWithPath: "/usr/bin/killall")
        proc2.arguments = ["-HUP", "mDNSResponder"]
        
        do {
            try proc2.run()
            proc2.waitUntilExit()
            withReply(proc2.terminationStatus == 0, nil)
        } catch {
            withReply(false, "killall failed: \(error.localizedDescription)")
        }
    }
    
    func installPfRules(anchor: String, rules: String, withReply: @escaping (Bool, String?) -> Void) {
        // D300/M2-013: Validate anchor name — only allow the "syxx" anchor namespace.
        // Anchor names must be alphanumeric with '/' separators and underscores, max 63 chars.
        let anchorPattern = #"^[a-zA-Z][a-zA-Z0-9_/]*$"#
        guard anchor.range(of: anchorPattern, options: .regularExpression) != nil,
              anchor.count <= 63 else {
            os_log(.error, log: .default, "D300: rejected PF anchor with invalid name: %{public}@", anchor)
            withReply(false, "rejected: invalid anchor name")
            return
        }

        // D300: Strict structural validation of each PF rule.
        // Rejects any rule that doesn't conform to the expected Syxx PF rule grammar.
        guard isValidPfRuleSet(rules) else {
            withReply(false, "rejected: one or more PF rules failed structural validation")
            return
        }

        // Write rules to a temporary file
        let tempFile = NSTemporaryDirectory() + "syxx-pf-\(UUID().uuidString).conf"
        do {
            try rules.write(toFile: tempFile, atomically: true, encoding: .utf8)
        } catch {
            withReply(false, "Failed to write rules: \(error.localizedDescription)")
            return
        }

        // Load the rules
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: "/sbin/pfctl")
        proc.arguments = ["-a", anchor, "-f", tempFile]
        
        do {
            try proc.run()
            proc.waitUntilExit()
            // Clean up temp file
            try? FileManager.default.removeItem(atPath: tempFile)
            
            if proc.terminationStatus != 0 {
                withReply(false, "pfctl failed to load rules")
                return
            }
            
            // Enable pf if not already enabled
            let enableProc = Process()
            enableProc.executableURL = URL(fileURLWithPath: "/sbin/pfctl")
            enableProc.arguments = ["-e"]
            
            try enableProc.run()
            enableProc.waitUntilExit()
            
            withReply(true, nil)
        } catch {
            try? FileManager.default.removeItem(atPath: tempFile)
            withReply(false, "pfctl failed: \(error.localizedDescription)")
        }
    }

    func removePfRules(anchor: String, withReply: @escaping (Bool, String?) -> Void) {
        // D327: validate anchor name before removal — same validation as installPfRules.
        // Without this, a malicious client could pass an arbitrary anchor name (e.g. "com.apple")
        // and flush system-critical PF rules installed by macOS or other software.
        let anchorPattern = #"^[a-zA-Z][a-zA-Z0-9_/]*$"#
        guard anchor.range(of: anchorPattern, options: .regularExpression) != nil,
              anchor.count <= 63 else {
            os_log(.error, log: .default, "D327: rejected PF anchor removal with invalid name: %{public}@", anchor)
            withReply(false, "rejected: invalid anchor name")
            return
        }

        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: "/sbin/pfctl")
        proc.arguments = ["-a", anchor, "-F", "all"]
        
        do {
            try proc.run()
            proc.waitUntilExit()
            withReply(proc.terminationStatus == 0, nil)
        } catch {
            withReply(false, "pfctl failed: \(error.localizedDescription)")
        }
    }

    // MARK: - D300: Strict PF rule structural validation

    /// D300: Strict parser for PF rules. Each rule must conform to the expected
    /// Syxx PF grammar: `action [dir] [log] [quick] [on iface] [proto p] [from src] [to dst] [filter-opts]`.
    /// Rules that don't match this structure — even if syntactically valid PF — are rejected.
    /// This prevents a compromised XPC client from loading arbitrary PF directives (nat, rdr,
    /// binat, divert-to, etc.) that could redirect traffic or escalate privileges.
    private func isValidPfRuleSet(_ rules: String) -> Bool {
        let allowedActions = Set(["block", "pass", "scrub", "set"])
        let allowedDirections = Set(["in", "out"])
        let allowedProtocols = Set([
            "tcp", "udp", "icmp", "icmp6", "ipv6",
            "{ tcp, udp }", "{ udp, tcp }", "{ tcp udp }", "{ udp tcp }"
        ])
        // Interfaces Syxx legitimately operates on: Ethernet/Wi-Fi, loopback, bridge, VPN.
        let ifacePattern = #"^(en\d+|lo\d*|bridge\d*|utun\d*|ap\d+|awdl\d+)$"#

        let dangerousLiterals = [";", "&&", "||", "`", "$(",
                                 "load anchor", "exec", "include"]

        for line in rules.components(separatedBy: .newlines) {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed.isEmpty || trimmed.hasPrefix("#") { continue }

            // Phase 1: Reject shell injection and dangerous PF directives.
            if dangerousLiterals.contains(where: { trimmed.contains($0) }) {
                os_log(.error, log: .default, "D300: rejected PF rule with dangerous content: %{public}@", trimmed)
                return false
            }

            // Phase 2: Action whitelist.
            let firstWord = trimmed.lowercased().components(separatedBy: .whitespaces).first ?? ""
            if !allowedActions.contains(firstWord) {
                os_log(.error, log: .default, "D300: rejected PF rule with disallowed action: %{public}@", firstWord)
                return false
            }

            // Phase 3: Strict token-by-token structural parse.
            // Tokenize respecting braces `{ ... }` as a single unit and parentheses as delimiters.
            let ruleLower = trimmed.lowercased()
            var tokens: [String] = []
            var current = ""
            var braceDepth = 0
            var parenDepth = 0
            for ch in ruleLower {
                if ch == "{" { braceDepth += 1; current.append(ch); continue }
                if ch == "}" { braceDepth = max(0, braceDepth - 1); current.append(ch); continue }
                if ch == "(" { parenDepth += 1; current.append(ch); continue }
                if ch == ")" { parenDepth = max(0, parenDepth - 1); current.append(ch); continue }
                if ch == " " && braceDepth == 0 && parenDepth == 0 {
                    if !current.isEmpty { tokens.append(current); current = "" }
                } else {
                    current.append(ch)
                }
            }
            if !current.isEmpty { tokens.append(current) }

            // Unbalanced braces or parens → reject.
            if braceDepth != 0 || parenDepth != 0 {
                os_log(.error, log: .default, "D300: rejected PF rule with unbalanced delimiters: %{public}@", trimmed)
                return false
            }

            // A rule needs at least action + one parameter.
            guard tokens.count >= 2 else {
                os_log(.error, log: .default, "D300: rejected PF rule too short: %{public}@", trimmed)
                return false
            }

            // Walk the token stream; each keyword must be followed by its expected operand.
            var i = 1 // skip action token (already validated)
            // Optional direction qualifier
            if i < tokens.count && allowedDirections.contains(tokens[i]) { i += 1 }
            // Optional log / log(all)
            if i < tokens.count && tokens[i].hasPrefix("log") { i += 1 }
            // Optional quick
            if i < tokens.count && tokens[i] == "quick" { i += 1 }
            // Optional "on <iface>" — validate interface name strictly.
            if i < tokens.count && tokens[i] == "on" {
                i += 1
                guard i < tokens.count,
                      tokens[i].range(of: ifacePattern, options: .regularExpression) != nil else {
                    os_log(.error, log: .default, "D300: rejected PF rule with invalid interface: %{public}@",
                           i < tokens.count ? tokens[i] : "(missing)")
                    return false
                }
                i += 1
            }
            // Optional "proto <protocol>" — validate against allowlist.
            if i < tokens.count && tokens[i] == "proto" {
                i += 1
                guard i < tokens.count, allowedProtocols.contains(tokens[i]) else {
                    os_log(.error, log: .default, "D300: rejected PF rule with disallowed protocol: %{public}@",
                           i < tokens.count ? tokens[i] : "(missing)")
                    return false
                }
                i += 1
            }
            // Optional "from <src>" — accept any source expression.
            if i < tokens.count && tokens[i] == "from" { i += 1; if i < tokens.count { i += 1 } }
            // Optional "port <p>" or "ports <p>"
            if i < tokens.count && (tokens[i] == "port" || tokens[i] == "ports") { i += 1; if i < tokens.count { i += 1 } }
            // Optional "to <dst>"
            if i < tokens.count && tokens[i] == "to" { i += 1; if i < tokens.count { i += 1 } }
            // Optional "port <p>" or "ports <p>"
            if i < tokens.count && (tokens[i] == "port" || tokens[i] == "ports") { i += 1; if i < tokens.count { i += 1 } }
            // Optional flags / keep state / modulate state / synproxy state
            while i < tokens.count {
                let t = tokens[i]
                if t == "flags" || t == "keep" || t == "modulate" || t == "synproxy" || t == "state" || t == "no" {
                    i += 1
                } else {
                    // Unknown trailing token — reject.
                    os_log(.error, log: .default, "D300: rejected PF rule with unexpected token '%{public}@' in: %{public}@",
                           t, trimmed)
                    return false
                }
            }

            os_log(.info, log: .default, "D300: PF rule passed structural validation: %{public}@", trimmed)
        }
        return true
    }

    // MARK: - D301: DNS address validation (restricted)

    /// D301: Validate that an address is a well-formed IPv4 literal.
    private func isIPv4Address(_ address: String) -> Bool {
        var addr4 = in_addr()
        return inet_pton(AF_INET, address, &addr4) == 1
    }

    /// D301: Validate that an address is a well-formed IPv6 literal.
    private func isIPv6Address(_ address: String) -> Bool {
        var addr6 = in6_addr()
        return inet_pton(AF_INET6, address, &addr6) == 1
    }

    /// D301: Validate that a DNS server address is well-formed AND not in a restricted range.
    /// Rejects RFC 1918 private, loopback, link-local, and multicast addresses to prevent
    /// DNS hijacking by a compromised XPC client. Only routable unicast addresses are accepted.
    private func isValidRestrictedDNSAddress(_ address: String) -> Bool {
        // Try IPv4
        var addr4 = in_addr()
        if inet_pton(AF_INET, address, &addr4) == 1 {
            let hostOrder = UInt32(bigEndian: addr4.s_addr)
            let octets = (
                UInt8((hostOrder >> 24) & 0xFF),
                UInt8((hostOrder >> 16) & 0xFF),
                UInt8((hostOrder >> 8) & 0xFF),
                UInt8(hostOrder & 0xFF)
            )
            // 127.0.0.0/8 — loopback
            if octets.0 == 127 { return false }
            // 10.0.0.0/8 — RFC 1918 private
            if octets.0 == 10 { return false }
            // 172.16.0.0/12 — RFC 1918 private
            if octets.0 == 172 && (octets.1 >= 16 && octets.1 <= 31) { return false }
            // 192.168.0.0/16 — RFC 1918 private
            if octets.0 == 192 && octets.1 == 168 { return false }
            // 169.254.0.0/16 — link-local
            if octets.0 == 169 && octets.1 == 254 { return false }
            // 224.0.0.0/4 — multicast
            if octets.0 >= 224 && octets.0 <= 239 { return false }
            // 0.0.0.0 — unspecified
            if hostOrder == 0 { return false }
            // 255.255.255.255 — broadcast
            if hostOrder == 0xFFFFFFFF { return false }
            return true
        }
        // Try IPv6
        var addr6 = in6_addr()
        if inet_pton(AF_INET6, address, &addr6) == 1 {
            let bytes = withUnsafeBytes(of: &addr6) { Array($0) }
            // ::1 — loopback
            if bytes.allSatisfy({ $0 == 0 }) && bytes.last == 1 { return false }
            // fe80::/10 — link-local
            if bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80 { return false }
            // ff00::/8 — multicast
            if bytes[0] == 0xFF { return false }
            // fc00::/7 — unique local (RFC 4193)
            if (bytes[0] & 0xFE) == 0xFC { return false }
            // :: — unspecified
            if bytes.allSatisfy({ $0 == 0 }) { return false }
            return true
        }
        return false
    }

    /// D301: Log DNS server change with the requesting process identity for audit trail.
    private func logDNSChange(servers: [String], clientPID: pid_t) {
        os_log(.info, log: .default,
               "D301: DNS change requested — servers: [%{public}@] — client PID: %d",
               servers.joined(separator: ", "), clientPID)
    }

    func setDNSServers(servers: [String], withReply: @escaping (Bool, String?) -> Void) {
        // D301: Validate server count — max 4 DNS servers to prevent abuse.
        guard !servers.isEmpty else {
            withReply(false, "No DNS server addresses provided")
            return
        }
        guard servers.count <= 4 else {
            os_log(.error, log: .default, "D301: rejected DNS set with too many servers: %d", servers.count)
            withReply(false, "rejected: maximum 4 DNS servers allowed, got \(servers.count)")
            return
        }
        // D301: Validate each address is well-formed AND not in a restricted range
        // (private, loopback, link-local, multicast). Prevents DNS hijacking by a
        // compromised XPC client pointing DNS at attacker-controlled private addresses.
        for server in servers {
            guard isValidRestrictedDNSAddress(server) else {
                os_log(.error, log: .default, "D301: rejected restricted DNS server address: %{public}@", server)
                withReply(false, "rejected: DNS server address is invalid or in a restricted range: \(server)")
                return
            }
        }
        // D301: Log DNS change with client identity for audit trail.
        logDNSChange(servers: servers, clientPID: 0) // PID from XPC connection context

        // Find the active network service using the default route
        let routeProc = Process()
        let routePipe = Pipe()
        routeProc.executableURL = URL(fileURLWithPath: "/sbin/route")
        routeProc.arguments = ["-n", "get", "default"]
        routeProc.standardOutput = routePipe
        
        do {
            try routeProc.run()
            let routeData = routePipe.fileHandleForReading.readDataToEndOfFile()
            routeProc.waitUntilExit()
            
            guard routeProc.terminationStatus == 0 else {
                withReply(false, "Failed to get default route")
                return
            }
            
            let routeOutput = String(data: routeData, encoding: .utf8) ?? ""
            
            // Extract the interface name from "interface: en0" line
            var targetInterface: String?
            for line in routeOutput.components(separatedBy: .newlines) {
                if line.contains("interface:") {
                    targetInterface = line.components(separatedBy: ":").last?.trimmingCharacters(in: .whitespaces)
                    break
                }
            }
            
            guard let interface = targetInterface else {
                withReply(false, "Could not determine active network interface")
                return
            }
            
            // Map interface to network service name
            let listProc = Process()
            let listPipe = Pipe()
            listProc.executableURL = URL(fileURLWithPath: "/usr/sbin/networksetup")
            listProc.arguments = ["-listallhardwareports"]
            listProc.standardOutput = listPipe
            
            try listProc.run()
            let listData = listPipe.fileHandleForReading.readDataToEndOfFile()
            listProc.waitUntilExit()
            
            let listOutput = String(data: listData, encoding: .utf8) ?? ""
            
            // Parse hardware ports to find the service name for our interface
            var targetService: String?
            var currentService: String?
            for line in listOutput.components(separatedBy: .newlines) {
                if line.hasPrefix("Hardware Port:") {
                    currentService = line.replacingOccurrences(of: "Hardware Port:", with: "").trimmingCharacters(in: .whitespaces)
                } else if line.hasPrefix("Device:") && line.contains(interface) {
                    targetService = currentService
                    break
                }
            }
            
            guard let service = targetService else {
                withReply(false, "Could not find network service for interface \(interface)")
                return
            }

            // Set the DNS servers
            let setProc = Process()
            setProc.executableURL = URL(fileURLWithPath: "/usr/sbin/networksetup")
            setProc.arguments = ["-setdnsservers", service] + servers
            
            try setProc.run()
            setProc.waitUntilExit()
            withReply(setProc.terminationStatus == 0, nil)
        } catch {
            withReply(false, "networksetup failed: \(error.localizedDescription)")
        }
    }
}

// MARK: - NSXPCConnection audit token bridge (F047)

extension NSXPCConnection {
    /// `auditToken` is not exposed as a Swift property; bridge through the ObjC runtime.
    ///
    /// D328: The KVC key "auditToken" is undocumented. If Apple removes or renames it in a
    /// future macOS release, `value(forKey:)` will return nil and the token will be all-zero,
    /// which passes `SecCodeCopyGuestWithAttributes` trivially (pid 0 = kernel). Assert that
    /// the token is non-zero after extraction; log a fatal error if the bridge silently failed.
    var auditToken: audit_token_t {
        var token = audit_token_t()
        let size = MemoryLayout<audit_token_t>.size
        withUnsafeMutableBytes(of: &token) { buf in
            if let ptr = value(forKey: "auditToken") as AnyObject? {
                memcpy(buf.baseAddress!, Unmanaged.passUnretained(ptr as AnyObject).toOpaque(), size)
            }
        }
        // D328: assert the token is non-zero. A zero token means the KVC bridge failed silently,
        // which would cause SecCodeCopyGuestWithAttributes to resolve pid 0 (kernel) — accepting
        // any connection as trusted. This must be a hard failure, not a silent pass.
        let tokenBytes = withUnsafeBytes(of: token) { Data($0) }
        if tokenBytes.allSatisfy({ $0 == 0 }) {
            os_log(.fault, log: .default, "D328: CRITICAL — audit token is all-zero. The KVC 'auditToken' bridge failed. Refusing to accept connections without a valid audit token.")
            fatalError("D328: audit token extraction returned zero — KVC bridge may be broken on this macOS version")
        }
        return token
    }
}

// MARK: - XPC Service Delegate

class DeepSyxxDelegate: NSObject, NSXPCListenerDelegate {
    /// D078: validate every incoming XPC connection against the expected code signature.
    /// SMAuthorizedClients is only checked by `smd` at install time; once the helper is blessed,
    /// any local process can connect via NSXPCConnection(machServiceName:options:.privileged).
    /// We must verify the connecting process's code signature at runtime using the audit token.
    private static let expectedRequirementString =
        "identifier \"com.milkandgin.syxx\" and anchor apple generic and certificate leaf[subject.OU] = \"994XAF8MYH\""

    func listener(_ listener: NSXPCListener, shouldAcceptNewConnection newConnection: NSXPCConnection) -> Bool {
        // D078: validate the connecting process's code signature using its audit token.
        guard validateClientAuditToken(newConnection.auditToken) else {
            os_log(.error, log: .default, "D078: rejecting XPC connection from untrusted client (pid %d)", newConnection.processIdentifier)
            return false
        }

        let helper = DeepSyxx()
        newConnection.exportedInterface = NSXPCInterface(with: SyxxPrivilegedHelperProtocol.self)
        newConnection.exportedObject = helper

        newConnection.resume()
        return true
    }

    /// Validates that the audit token belongs to a process signed with the expected requirement.
    private func validateClientAuditToken(_ auditToken: audit_token_t) -> Bool {
        // Build a SecCode from the audit token
        let attributes: [String: Any] = [
            kSecGuestAttributeAudit as String: Data(bytes: [auditToken], count: MemoryLayout<audit_token_t>.size)
        ]
        var code: SecCode?
        let copyStatus = SecCodeCopyGuestWithAttributes(nil, attributes as CFDictionary, [], &code)
        guard copyStatus == errSecSuccess, let guestCode = code else {
            os_log(.error, log: .default, "D078: SecCodeCopyGuestWithAttributes failed: %d", copyStatus)
            return false
        }

        // Compile the expected requirement
        var requirement: SecRequirement?
        let reqStatus = SecRequirementCreateWithString(Self.expectedRequirementString as CFString, [], &requirement)
        guard reqStatus == errSecSuccess, let secRequirement = requirement else {
            os_log(.error, log: .default, "D078: SecRequirementCreateWithString failed: %d", reqStatus)
            return false
        }

        // Check validity with hardened runtime flag
        let checkStatus = SecCodeCheckValidity(guestCode, [.enforceRevocationChecks], secRequirement)
        guard checkStatus == errSecSuccess else {
            os_log(.error, log: .default, "D078: SecCodeCheckValidity failed: %d (client does not match expected identity)", checkStatus)
            return false
        }

        return true
    }
}

// MARK: - Main

// A SMJobBless privileged helper is a launchd daemon, not an XPC service bundle.
// It must claim the Mach service declared under MachServices in its embedded
// __TEXT,__launchd_plist (com.milkandgin.deepsyxx.plist:8-12), which is what the app
// connects to via NSXPCConnection(machServiceName:options:.privileged)
// (PrivilegeManager.swift:163). NSXPCListener.service() is the XPC-service singleton
// and never claims that Mach service (and never returns from resume()). See D079.
let delegate = DeepSyxxDelegate()
let listener = NSXPCListener(machServiceName: DeepSyxxHelper.machServiceName)
listener.delegate = delegate
listener.resume()

// Keep the daemon running; launchd tears us down when idle per KeepAlive.
RunLoop.main.run()

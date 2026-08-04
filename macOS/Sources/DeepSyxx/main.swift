import Foundation
import Security
import os
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
        // (/Library/PrivilegedHelperTools), not an app bundle — deriving the path from it
        // always produced a nonexistent path.
        //
        // SECURITY: validate the path before exec. The helper runs as root, so an unvalidated
        // client-supplied path is a privilege-escalation window. Require the binary to live
        // inside the signed app bundle and to pass a code-signature check.
        let allowedPrefix = "/Applications/Syxx.app/Contents/Resources/"
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
        let requirement = "identifier \"com.milkandgin.syxx.cli\" and anchor apple generic and certificate leaf[subject.OU] = \"994XAF8MYH\"" as CFString
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
            let stdoutData = stdoutPipe.fileHandleForReading.readDataToEndOfFile()
            let stderrData = stderrPipe.fileHandleForReading.readDataToEndOfFile()
            proc.waitUntilExit()
            
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

    func setDNSServers(servers: [String], withReply: @escaping (Bool, String?) -> Void) {
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
    var auditToken: audit_token_t {
        var token = audit_token_t()
        let size = MemoryLayout<audit_token_t>.size
        withUnsafeMutableBytes(of: &token) { buf in
            if let ptr = value(forKey: "auditToken") as AnyObject? {
                memcpy(buf.baseAddress!, Unmanaged.passUnretained(ptr as AnyObject).toOpaque(), size)
            }
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

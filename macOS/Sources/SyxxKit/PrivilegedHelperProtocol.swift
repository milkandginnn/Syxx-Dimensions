import Foundation

/// XPC protocol for privileged operations.
/// The helper tool implements this protocol and runs with root privileges.
@objc public protocol SyxxPrivilegedHelperProtocol {
    /// D094(b): cheap round-trip liveness check. `connectToHelper` calls this after
    /// `conn.resume()` to prove the Mach service actually answers before caching the
    /// connection. `NSXPCConnection.resume()` succeeds lazily — without this, a stale
    /// or missing Mach service is silently adopted as "connected".
    @objc func ping(withReply: @escaping (Bool) -> Void)

    /// Start cage capture with root privileges.
    /// D091/D116: `cliPath` is the absolute path of the bundled `syxx` CLI, supplied by the
    /// client (which knows the app bundle layout). The helper validates it before exec —
    /// never derive the path from Bundle.main inside a bare SMJobBless daemon.
    @objc func startCageCapture(
        cliPath: String,
        duration: Int,
        outputPath: String,
        ownerUID: UInt32,
        ownerGID: UInt32,
        withReply: @escaping (String, Int32, String?) -> Void
    )
    
    /// Flush the DNS cache (requires root)
    @objc func flushDNSCache(withReply: @escaping (Bool, String?) -> Void)
    
    /// Install packet filter rules (requires root)
    @objc func installPfRules(anchor: String, rules: String, withReply: @escaping (Bool, String?) -> Void)
    
    /// Remove packet filter rules (requires root)
    @objc func removePfRules(anchor: String, withReply: @escaping (Bool, String?) -> Void)
    
    /// Change DNS servers (requires root)
    @objc func setDNSServers(servers: [String], withReply: @escaping (Bool, String?) -> Void)
}

/// XPC error domain
public let SyxxPrivilegedHelperErrorDomain = "com.milkandgin.syxx.privilegedhelper"

/// XPC service name
public let SyxxPrivilegedHelperServiceName = "com.milkandgin.syxx.privilegedhelper"

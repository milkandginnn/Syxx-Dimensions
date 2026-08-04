import Foundation

/// The gate every client must pass a SERVER-SUPPLIED url through before handing it to the OS opener
/// (`NSWorkspace.open` or equivalent).
public enum RemoteUrlPolicy {
    public static let defaultAllowedDomains = ["syxx.app", "milkandgin.com"]
    
    public enum Decision: String, Codable {
        case allowed
        case refusedScheme
        case refusedHost
        case refusedMalformed
    }
    
    public struct Result {
        public let decision: Decision
        public let reason: String?
        
        public var isAllowed: Bool {
            return decision == .allowed
        }
    }
    
    public static func evaluate(_ urlString: String?, allowedDomains: [String] = defaultAllowedDomains) -> Result {
        guard let urlString = urlString, !urlString.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return Result(decision: .refusedMalformed, reason: "The link was empty.")
        }
        
        guard let url = URL(string: urlString), let scheme = url.scheme, let host = url.host else {
            return Result(decision: .refusedMalformed, reason: "The link could not be read as a web address.")
        }
        
        // HTTPS only
        if scheme.lowercased() != "https" {
            return Result(decision: .refusedScheme, reason: "Syxx only opens https links; this one was “\(scheme)”")
        }
        
        // Embedded credentials check
        if url.user != nil || url.password != nil {
            return Result(decision: .refusedMalformed, reason: "The link carried embedded credentials.")
        }
        
        // Non-standard port check
        if let port = url.port, port != 443 {
            return Result(decision: .refusedMalformed, reason: "The link used a non-standard port.")
        }
        
        let lowercasedHost = host.lowercased()
        for domain in allowedDomains {
            let lowercasedDomain = domain.lowercased()
            if lowercasedHost == lowercasedDomain || lowercasedHost.hasSuffix("." + lowercasedDomain) {
                return Result(decision: .allowed, reason: nil)
            }
        }
        
        return Result(decision: .refusedHost, reason: "Syxx only opens its own pages; “\(host)” is not one.")
    }
}

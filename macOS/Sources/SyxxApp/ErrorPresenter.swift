import Foundation
import SyxxKit

/// Turns a caught `Error` (or a raw reason string from the core) into plain, honest, user-facing
/// text, and routes the full technical detail to the diagnostic sink so nothing is lost.
///
/// WHY THIS EXISTS. `NSError.localizedDescription` on a Swift error with no `LocalizedError`
/// conformance yields crash-reporter material — "The operation couldn't be completed. (Module.Type
/// error N.)", sometimes carrying a memory address — which a user cannot act on and which leaks
/// internal type structure. Rendering it as product copy is a defect. This presenter is the single
/// boundary a caught error passes through before it can reach the screen: the screen gets a sentence
/// a person can read; the diagnostic sink gets the raw description (redacted there).
///
/// HONESTY. When an error carries no human-readable cause, `userDetail` returns `nil` so the caller
/// states the failure WITHOUT a cause. We never invent why something failed. An honest "this could
/// not be completed" beats a confident wrong explanation.
enum ErrorPresenter {

    /// A human-readable cause for `error`, or `nil` when the error carries none — in which case the
    /// caller should state the failure plainly rather than invent a reason. Always routes the full
    /// technical detail to the diagnostic sink (the sink redacts hostnames/IPs/paths).
    static func userDetail(for error: Error, component: String) -> String? {
        SyxxDiagnostics.breadcrumb(
            category: "gui.error",
            message: "\(component): \(String(reflecting: error))",
            level: .error,
            data: ["localized": error.localizedDescription])
        return cleanReason(for: error)
    }

    /// Sanitize a raw reason STRING (e.g. `LeakTestResult.errorReason`, which the core may have built
    /// from a Swift error's description): returns it unchanged when it is genuine human-readable text,
    /// or `nil` when it is crash-reporter boilerplate that must not be shown. Pure and side-effect
    /// free, so it is safe to call from a SwiftUI view body.
    static func displayReason(_ raw: String) -> String? {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return nil }
        return isBoilerplate(trimmed) ? nil : trimmed
    }

    private static func cleanReason(for error: Error) -> String? {
        if let localized = error as? LocalizedError,
           let described = localized.errorDescription, !isBoilerplate(described) {
            return described
        }
        let ns = error as NSError
        if let described = ns.userInfo[NSLocalizedDescriptionKey] as? String, !isBoilerplate(described) {
            return described
        }
        let loc = error.localizedDescription
        return isBoilerplate(loc) ? nil : loc
    }

    /// True when `s` is crash-reporter material rather than product copy: the Cocoa
    /// "operation couldn't be completed" template, a memory address (`$1a2b…`), a module-qualified
    /// type dump ("SyxxKit.WitnessSignerError error 1"), or an "(unknown context at …)" fragment.
    static func isBoilerplate(_ s: String) -> Bool {
        // Both the straight apostrophe and the typographic one Foundation actually emits.
        if s.contains("operation couldn't be completed") || s.contains("operation couldn\u{2019}t be completed") {
            return true
        }
        if s.contains("unknown context at") { return true }
        // Memory address: `$` followed by hex.
        if s.range(of: "\\$[0-9a-fA-F]{4,}", options: .regularExpression) != nil { return true }
        // Module-qualified type dump: `Some.Nested.Type error 0` / `error -1`.
        if s.range(of: "[A-Za-z_][A-Za-z0-9_]*(\\.[A-Za-z0-9_]+)+ error -?[0-9]+",
                   options: .regularExpression) != nil {
            return true
        }
        return false
    }
}

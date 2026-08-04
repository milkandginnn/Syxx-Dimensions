import Foundation

/// Canonical result tri-state, shared across the Syxx kits (macOS, Windows, Server).
///
/// The three raw values ("success", "failure", "inconclusive") are the ONLY wire-format
/// strings the client kits accept. Any other spelling breaks byte-identical JSON parity
/// with the Windows Syxx.Kit twin and the Python server.
///
/// `inconclusive` is NOT a negative result: it means the check could not reach a
/// conclusion (timeout, network error, missing data, ...). Surfaces that display it
/// must NEVER read it as "the condition did not hold" — that is what `failure` is for.
///
/// Distinct from `ConditionState` (yes/no/unknown), which describes network conditions
/// that can genuinely be unknown (VPN active, captive portal intercepting, DNS encrypted).
public enum TriState: String, Codable, Sendable {
    case success
    case failure
    case inconclusive
}

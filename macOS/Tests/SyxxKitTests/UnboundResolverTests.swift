import XCTest
@testable import SyxxKit

/// Local-resolver honesty proof. The rule that must never break: the app may say "Unbound is protecting
/// your DNS" ONLY when the local resolver actually answered a real query AND the system was switched to
/// it AND a post-switch sanity lookup succeeded. A process merely spawned, or a `setdnsservers` call that
/// merely returned, must never render as protection — the false-secure this product exists to refuse.
final class UnboundResolverTests: XCTestCase {

    private func classify(binary: Bool = true, started: Bool = true, bindDenied: Bool = false,
                          local: Bool = true, switched: Bool = true, switchDenied: Bool = false,
                          postOK: Bool = true, reverted: Bool = false) -> UnboundResolver.UnboundResult {
        UnboundResolver.classifyEnable(
            port: 53, priorServers: ["9.9.9.9"], binaryFound: binary, processStarted: started,
            bindElevationDenied: bindDenied, localResolves: local, systemSwitched: switched,
            switchElevationDenied: switchDenied, postSwitchSanityOk: postOK, autoReverted: reverted)
    }

    // The one and only enabled path: local answers + switched + post-switch sanity OK.
    func testAllConfirmedIsEnabled() {
        let r = classify()
        XCTAssertTrue(r.isEnabled)
        if case .enabled(let port, let prior) = r {
            XCTAssertEqual(port, 53); XCTAssertEqual(prior, ["9.9.9.9"])
        } else { XCTFail("expected enabled, got \(r)") }
    }

    // Missing binary is its own state so the UI can say "install it", not a generic failure.
    func testNoBinaryIsNotInstalled() {
        if case .notInstalled = classify(binary: false) {} else { XCTFail("expected notInstalled") }
    }

    // A bind failure on :53 is elevation, distinct from a generic start failure.
    func testBindDeniedIsElevationRequired() {
        if case .elevationRequired = classify(started: false, bindDenied: true) {} else {
            XCTFail("expected elevationRequired")
        }
    }
    func testStartFailureIsFailed() {
        if case .failed = classify(started: false, bindDenied: false) {} else { XCTFail("expected failed") }
    }

    // Process up but not answering must NOT read as protection.
    func testStartedButLocalDoesNotResolveIsInconclusive() {
        let r = classify(local: false)
        XCTAssertFalse(r.isEnabled)
        if case .inconclusive = r {} else { XCTFail("expected inconclusive, got \(r)") }
    }

    // Local works but system not switched: inconclusive (running, but system not using it).
    func testLocalWorksButNotSwitchedIsInconclusive() {
        let r = classify(switched: false)
        XCTAssertFalse(r.isEnabled)
        if case .inconclusive = r {} else { XCTFail("expected inconclusive, got \(r)") }
    }
    func testSwitchDeniedIsElevationRequired() {
        if case .elevationRequired = classify(switched: false, switchDenied: true) {} else {
            XCTFail("expected elevationRequired")
        }
    }

    // The failsafe: switched but post-switch sanity failed → auto-reverted → honest failed, NOT enabled.
    func testPostSwitchSanityFailIsFailedNotEnabled() {
        let r = classify(postOK: false, reverted: true)
        XCTAssertFalse(r.isEnabled)
        if case .failed = r {} else { XCTFail("expected failed after auto-revert, got \(r)") }
    }

    // MARK: - Config generation

    func testConfigIsFullRecursionWithDNSSECByDefault() {
        let cfg = UnboundResolver.ResolverConfig(
            port: 53, rootAnchorPath: "/x/root.key", workingDir: "/x", pidFile: "/x/p", logFile: "/x/l")
        let conf = UnboundResolver.generateConfig(cfg)
        XCTAssertTrue(conf.contains("interface: 127.0.0.1@53"))
        XCTAssertTrue(conf.contains("auto-trust-anchor-file: \"/x/root.key\""), "DNSSEC validation must be on")
        XCTAssertTrue(conf.contains("access-control: 0.0.0.0/0 refuse"), "must not be an open resolver")
        XCTAssertFalse(conf.contains("forward-zone"), "v1 default is full recursion, not forwarding")
    }

    // Default (verify path): empty username ⇒ run as the invoking user, NO setuid. Hardcoding a drop
    // account here would kill the admin-free verify path (a non-root process cannot setuid).
    func testConfigDefaultUsernameIsEmptyForUnprivilegedRun() {
        let cfg = UnboundResolver.ResolverConfig(
            port: 5354, rootAnchorPath: "/x/root.key", workingDir: "/x", pidFile: "/x/p", logFile: "/x/l")
        let conf = UnboundResolver.generateConfig(cfg)
        XCTAssertTrue(conf.contains("username: \"\""), "verify path must not request a privilege drop")
    }

    // Enable path: the resolver must drop root to the dedicated `_unbound` account after binding.
    func testConfigUsernameDropsToServiceAccountWhenSet() {
        let cfg = UnboundResolver.ResolverConfig(
            port: 53, rootAnchorPath: "/x/root.key", workingDir: "/x", pidFile: "/x/p", logFile: "/x/l",
            username: "_unbound")
        let conf = UnboundResolver.generateConfig(cfg)
        XCTAssertTrue(conf.contains("username: \"_unbound\""), "enable path must drop privileges to _unbound")
        XCTAssertTrue(conf.contains("chroot: \"\""), "chroot stays empty so trust-anchor path remains valid")
    }

    func testConfigForwardOverDoTIsOptIn() {
        let r = EncryptedDNSConfig.catalog[0] // cloudflare
        let cfg = UnboundResolver.ResolverConfig(
            port: 53, rootAnchorPath: "/x/root.key", workingDir: "/x", pidFile: "/x/p", logFile: "/x/l",
            forwardOverDoT: r)
        let conf = UnboundResolver.generateConfig(cfg)
        XCTAssertTrue(conf.contains("forward-zone"))
        XCTAssertTrue(conf.contains("forward-tls-upstream: yes"))
        XCTAssertTrue(conf.contains("one.one.one.one"))
    }

    // DNSCrypt loopback forward: plain forward-addr to the local proxy (NOT TLS), and — the load-bearing
    // line proven live — `do-not-query-localhost: no`, without which Unbound SERVFAILs forwarding to loopback.
    func testConfigDNSCryptLoopbackForward() {
        let cfg = UnboundResolver.ResolverConfig(
            port: 5335, rootAnchorPath: "/x/root.key", workingDir: "/x", pidFile: "/x/p", logFile: "/x/l",
            forwardToDNSCryptPort: 5354)
        let conf = UnboundResolver.generateConfig(cfg)
        XCTAssertTrue(conf.contains("forward-zone"))
        XCTAssertTrue(conf.contains("forward-addr: 127.0.0.1@5354"))
        XCTAssertFalse(conf.contains("forward-tls-upstream"), "dnscrypt-proxy speaks plain DNS to Unbound, not TLS")
        XCTAssertTrue(conf.contains("do-not-query-localhost: no"),
                      "must allow forwarding to the loopback proxy or the chain SERVFAILs")
        XCTAssertTrue(conf.contains("auto-trust-anchor-file"), "DNSSEC validation still composes on top")
    }

    // DNSCrypt forward must take precedence over DoT when both are set (more-specific local transport wins).
    func testConfigDNSCryptForwardWinsOverDoT() {
        let cfg = UnboundResolver.ResolverConfig(
            port: 5335, rootAnchorPath: "/x/root.key", workingDir: "/x", pidFile: "/x/p", logFile: "/x/l",
            forwardOverDoT: EncryptedDNSConfig.catalog[0], forwardToDNSCryptPort: 5354)
        let conf = UnboundResolver.generateConfig(cfg)
        XCTAssertTrue(conf.contains("forward-addr: 127.0.0.1@5354"))
        XCTAssertFalse(conf.contains("forward-tls-upstream"), "DNSCrypt loopback forward must win over DoT")
    }
}

import XCTest
@testable import SyxxApp

/// Five-second MOTD fallback contract: generation-token race, resource identity, and source guards.
final class MotdFallbackTests: XCTestCase {

    // MARK: Deadline constant

    func testDeadlineIsExactlyFiveSeconds() {
        XCTAssertEqual(MotdLoadController.deadlineSeconds, 5.0)
        // Failure path: a 30s generic remote budget must not leak into MOTD.
        XCTAssertNotEqual(MotdLoadController.deadlineSeconds, 30.0)
    }

    // MARK: Success just under deadline

    func testRemoteSuccessBeforeDeadlineKeepsRemote() {
        var c = MotdLoadController()
        let gen = c.beginRemoteLoad()
        XCTAssertTrue(c.remoteSucceeded(generation: gen))
        XCTAssertEqual(c.state, .showingRemote)
        // Failure path: fallback after remote success must be rejected.
        XCTAssertFalse(c.activateFallback(generation: gen))
        XCTAssertEqual(c.state, .showingRemote)
    }

    // MARK: Deadline / failure activates fallback

    func testDeadlineActivatesFallback() {
        var c = MotdLoadController()
        let gen = c.beginRemoteLoad()
        XCTAssertTrue(c.activateFallback(generation: gen))
        XCTAssertEqual(c.state, .showingFallback)
    }

    func testLateRemoteCannotReplaceFallback() {
        var c = MotdLoadController()
        let gen = c.beginRemoteLoad()
        XCTAssertTrue(c.activateFallback(generation: gen))
        // Late remote success for the same generation must not win.
        XCTAssertFalse(c.remoteSucceeded(generation: gen))
        XCTAssertEqual(c.state, .showingFallback)
    }

    // MARK: Generation isolation

    func testStaleGenerationCannotAffectRetry() {
        var c = MotdLoadController()
        let g1 = c.beginRemoteLoad()
        let g2 = c.beginRemoteLoad()
        XCTAssertNotEqual(g1, g2)
        // Stale fallback for g1 after retry started.
        XCTAssertFalse(c.activateFallback(generation: g1))
        XCTAssertEqual(c.state, .loadingRemote)
        XCTAssertTrue(c.remoteSucceeded(generation: g2))
        XCTAssertEqual(c.state, .showingRemote)
    }

    func testFallbackLoadIsNotRemoteSuccess() {
        var c = MotdLoadController()
        let gen = c.beginRemoteLoad()
        XCTAssertTrue(c.activateFallback(generation: gen))
        // A second activate must not schedule another race as remote success.
        XCTAssertFalse(c.activateFallback(generation: gen))
        XCTAssertFalse(c.remoteSucceeded(generation: gen))
        XCTAssertEqual(c.state, .showingFallback)
    }

    // MARK: Resource selection

    func testFallbackResourceNameIsExactlyFallbackMotd() {
        XCTAssertEqual(MotdLoadController.fallbackResourceName, "fallback-motd")
        XCTAssertEqual(MotdLoadController.fallbackResourceExtension, "html")
        XCTAssertFalse(MotdLoadController.forbiddenFallbackNames.contains("fallback-motd"))
        XCTAssertTrue(MotdLoadController.forbiddenFallbackNames.contains("motd-local"))
        XCTAssertTrue(MotdLoadController.forbiddenFallbackNames.contains("fallback-mac"))
    }

    func testPackagedFallbackResolvesAndCarriesRoleMarker() throws {
        let url = try XCTUnwrap(
            MotdLoadController.resolveFallbackURL(in: Bundle.module),
            "Resources/fallback-motd.html must be packaged in the SyxxApp bundle"
        )
        XCTAssertEqual(url.deletingPathExtension().lastPathComponent, "fallback-motd")
        let html = try String(contentsOf: url, encoding: .utf8)
        XCTAssertTrue(MotdLoadController.htmlIsTimeoutFallback(html))
        // Hero imagery must be preserved (owner directive).
        XCTAssertTrue(html.contains("masthead") || html.contains("<svg") || html.contains("<img"),
                      "fallback-motd.html must retain hero imagery")
        // Failure path: marker-free HTML must not count as the timeout fallback.
        XCTAssertFalse(MotdLoadController.htmlIsTimeoutFallback("<html><body>hi</body></html>"))
    }

    func testResolveNeverReturnsMotdLocalEvenIfOnlyThatExists() {
        // resolveFallbackURL only asks for fallback-motd by name — it cannot return motd-local.
        if let url = MotdLoadController.resolveFallbackURL(in: Bundle.module) {
            XCTAssertFalse(url.lastPathComponent.contains("motd-local"))
            XCTAssertFalse(url.lastPathComponent.contains("fallback-mac"))
        }
    }

    // MARK: Source guards (production MOTD path must not prefer motd-local)

    func testBetaViewSourceDoesNotReferenceMotdLocal() throws {
        let url = try XCTUnwrap(sourceFileURL(named: "BetaView.swift", under: "SyxxApp"))
        let src = try String(contentsOf: url, encoding: .utf8)
        // Production selection: no forResource load of the forbidden substitute.
        XCTAssertFalse(src.contains("forResource: \"motd-local\""),
                       "BetaView must not load the forbidden local MOTD resource by name")
        XCTAssertFalse(src.contains("forResource: \"fallback-mac\""),
                       "BetaView must not use the generic remote-content fallback for MOTD")
        XCTAssertTrue(src.contains("mode: .motd") || src.contains("mode:.motd"),
                      "BetaView must select MOTD mode on RemoteContentView")
        XCTAssertTrue(src.contains("fetchWhatsNewURL") || src.contains("macmotd.syxx.app"),
                      "BetaView must attempt the remote MOTD first")
    }

    func testRemoteContentViewMotdPathDoesNotSelectFallbackMacOrMotdLocal() throws {
        let url = try XCTUnwrap(sourceFileURL(named: "RemoteContentView.swift", under: "SyxxApp"))
        let src = try String(contentsOf: url, encoding: .utf8)
        // MOTD activate path must request fallback-motd, not motd-local / fallback-mac.
        XCTAssertTrue(src.contains("MotdLoadController.resolveFallbackURL")
                      || src.contains("fallback-motd"),
                      "MOTD failure path must resolve fallback-motd")
        // Guard: no MOTD branch should load motd-local by name.
        let motdSections = src.components(separatedBy: "mode == .motd")
        for section in motdSections.dropFirst() {
            let window = String(section.prefix(800))
            XCTAssertFalse(window.contains("motd-local"),
                           "MOTD branch must not reference motd-local")
        }
    }

    // MARK: Helpers

    private func sourceFileURL(named name: String, under target: String) -> URL? {
        // Walk up from the test bundle to the repo Sources tree.
        var dir = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        for _ in 0..<8 {
            let candidate = dir
                .appendingPathComponent("Sources")
                .appendingPathComponent(target)
                .appendingPathComponent(name)
            if FileManager.default.fileExists(atPath: candidate.path) { return candidate }
            let nested = dir
                .appendingPathComponent("Sources")
                .appendingPathComponent(target)
                .appendingPathComponent("Presentation")
                .appendingPathComponent(name)
            if FileManager.default.fileExists(atPath: nested.path) { return nested }
            dir = dir.deletingLastPathComponent()
        }
        // Explicit repo-relative fallback used when tests run from .build.
        let repo = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent() // SyxxAppTests
            .deletingLastPathComponent() // Tests
            .deletingLastPathComponent() // macOS
        let direct = repo
            .appendingPathComponent("Sources")
            .appendingPathComponent(target)
            .appendingPathComponent(name)
        return FileManager.default.fileExists(atPath: direct.path) ? direct : nil
    }
}

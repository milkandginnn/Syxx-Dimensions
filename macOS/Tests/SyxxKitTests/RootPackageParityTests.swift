import XCTest

/// The repository has TWO Swift manifests over ONE set of sources:
///   • `macOS/Package.swift` — canonical; what CI and `build_dmg.sh` build.
///   • `Package.swift` (repo root) — the same targets with `path:` pointing into `macOS/`, so the
///     repository root opens as a single project and the .NET twins, server, corpora and ledgers
///     are visible beside the Swift code.
///
/// Two manifests over one source tree is a drift hazard: a target, dependency, resource or linker
/// setting added to one and not the other yields a package that builds from `macOS/` and fails
/// from the root — or, worse, builds BOTH ways with different settings, which surfaces later as an
/// unreproducible bug. On a project whose central rule is that a change needs its twin, an
/// unguarded second manifest is the same defect shape in the build system.
///
/// These tests compare the two manifests directly. They are text-level rather than
/// PackageDescription-level on purpose: SwiftPM does not expose a manifest parser to test code,
/// and a regex over the declarations is enough to catch the drift that actually happens (a target
/// or dependency added to one file only).
final class RootPackageParityTests: XCTestCase {

    /// `<repo>/macOS/Tests/SyxxKitTests/<this file>` → up four to the repo root.
    private func repoRoot(_ file: StaticString = #filePath) -> URL {
        URL(fileURLWithPath: "\(file)")
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
    }

    private func manifest(_ relative: String) throws -> String {
        let url = repoRoot().appendingPathComponent(relative)
        guard let text = try? String(contentsOf: url, encoding: .utf8) else {
            // Refuse to pass vacuously: a missing manifest is a failure, not a skip.
            XCTFail("manifest not found at \(url.path) — parity cannot be checked")
            throw CocoaError(.fileNoSuchFile)
        }
        return text
    }

    /// Every `name: "X"` that introduces a target, in declaration order.
    private func targetNames(_ manifest: String) -> Set<String> {
        var names = Set<String>()
        let pattern = #"\.(?:testTarget|executableTarget|target)\(\s*\n?\s*name:\s*"([^"]+)""#
        let re = try! NSRegularExpression(pattern: pattern)
        let ns = manifest as NSString
        for m in re.matches(in: manifest, range: NSRange(location: 0, length: ns.length)) {
            names.insert(ns.substring(with: m.range(at: 1)))
        }
        return names
    }

    /// Every external package dependency URL.
    private func dependencyURLs(_ manifest: String) -> Set<String> {
        var urls = Set<String>()
        let re = try! NSRegularExpression(pattern: #"\.package\(url:\s*"([^"]+)""#)
        let ns = manifest as NSString
        for m in re.matches(in: manifest, range: NSRange(location: 0, length: ns.length)) {
            // Normalize the .git suffix — the two manifests may spell the same repo either way.
            var u = ns.substring(with: m.range(at: 1))
            if u.hasSuffix(".git") { u.removeLast(4) }
            urls.insert(u)
        }
        return urls
    }

    /// Every `.product(name: "X", package: "Y")` pairing.
    private func products(_ manifest: String) -> Set<String> {
        var out = Set<String>()
        let re = try! NSRegularExpression(pattern: #"\.product\(name:\s*"([^"]+)",\s*package:\s*"([^"]+)"\)"#)
        let ns = manifest as NSString
        for m in re.matches(in: manifest, range: NSRange(location: 0, length: ns.length)) {
            out.insert("\(ns.substring(with: m.range(at: 1)))@\(ns.substring(with: m.range(at: 2)))")
        }
        return out
    }

    // MARK: - Tests

    /// Failure case: add a target to one manifest only → the symmetric difference is non-empty → red.
    func testBothManifestsDeclareTheSameTargets() throws {
        let root = targetNames(try manifest("Package.swift"))
        let mac = targetNames(try manifest("macOS/Package.swift"))

        XCTAssertFalse(root.isEmpty, "root manifest parsed zero targets — the check would be vacuous")
        XCTAssertEqual(root, mac,
                       "target sets diverged. only-in-root: \(root.subtracting(mac).sorted()); "
                       + "only-in-macOS: \(mac.subtracting(root).sorted())")
    }

    /// Failure case: bump or add a dependency in one manifest only → red.
    func testBothManifestsDeclareTheSameDependencies() throws {
        let root = dependencyURLs(try manifest("Package.swift"))
        let mac = dependencyURLs(try manifest("macOS/Package.swift"))

        XCTAssertFalse(root.isEmpty, "root manifest parsed zero dependencies — vacuous")
        XCTAssertEqual(root, mac,
                       "dependency sets diverged. only-in-root: \(root.subtracting(mac).sorted()); "
                       + "only-in-macOS: \(mac.subtracting(root).sorted())")
    }

    /// The subtlest drift: same targets, same packages, but a target wired to a different product.
    /// Failure case: give SyxxKit the SDK in one manifest and not the other → red.
    func testBothManifestsWireTheSameProducts() throws {
        let root = products(try manifest("Package.swift"))
        let mac = products(try manifest("macOS/Package.swift"))

        XCTAssertFalse(root.isEmpty, "root manifest parsed zero products — vacuous")
        XCTAssertEqual(root, mac,
                       "product wiring diverged. only-in-root: \(root.subtracting(mac).sorted()); "
                       + "only-in-macOS: \(mac.subtracting(root).sorted())")
    }

    /// The root manifest must reach INTO macOS/ rather than expecting sources beside it. Without
    /// explicit paths SwiftPM looks for `Sources/<target>` at the root, silently builds nothing,
    /// and the root project appears to work while compiling none of the real code.
    /// Failure case: drop the `path:` entries from the root manifest → red.
    func testRootManifestPathsPointIntoMacOSSubtree() throws {
        let root = try manifest("Package.swift")

        for target in ["SyxxKit", "SyxxCLI", "SyxxApp", "SyxxGUI", "Ccage"] {
            XCTAssertTrue(root.contains("path: \"macOS/Sources/\(target)\""),
                          "root manifest must declare path macOS/Sources/\(target)")
        }
        for testTarget in ["SyxxKitTests", "SyxxAppTests"] {
            XCTAssertTrue(root.contains("path: \"macOS/Tests/\(testTarget)\""),
                          "root manifest must declare path macOS/Tests/\(testTarget)")
        }
        // And the canonical manifest must NOT carry those paths — its targets are relative to
        // macOS/ already, so a stray `macOS/` prefix there would double the path.
        let mac = try manifest("macOS/Package.swift")
        XCTAssertFalse(mac.contains("path: \"macOS/"),
                       "macOS/Package.swift must not prefix paths with macOS/ — it already lives there")
    }
}

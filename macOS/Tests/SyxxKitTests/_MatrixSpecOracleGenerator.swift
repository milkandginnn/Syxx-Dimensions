import XCTest
@testable import SyxxKit

/// Regenerates corpus/matrix/default-spec.frozen.json from the production emitter.
///
/// GATED: does nothing unless SYXX_REGENERATE_MATRIX_ORACLE=1, because regeneration is an
/// explicit spec-version-bump action (docs/MATRIX-MIGRATION-PLAN.md:358 — "reviewed by hand
/// against this table's intent, never accepted blind; regeneration requires an explicit
/// spec-version bump commit"). Running it casually would freeze whatever the code currently
/// emits and turn a failing oracle green without anyone checking the ~100 constants.
///
/// Follows the repo's checked-in generator pattern (_ProbeParityFixtureGenerator.swift,
/// _WitnessCrossPlatformFixtureGenerator.swift). macOS is the designated reference platform.
///
///   SYXX_REGENERATE_MATRIX_ORACLE=1 swift test --package-path macOS --filter MatrixSpecOracleGenerator
final class MatrixSpecOracleGenerator: XCTestCase {

    func testRegenerateFrozenOracle() throws {
        guard ProcessInfo.processInfo.environment["SYXX_REGENERATE_MATRIX_ORACLE"] == "1" else {
            throw XCTSkip("Set SYXX_REGENERATE_MATRIX_ORACLE=1 to regenerate — a spec-version-bump action, never casual.")
        }

        let oraclePath = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent()   // SyxxKitTests
            .deletingLastPathComponent()   // Tests
            .deletingLastPathComponent()   // macOS
            .deletingLastPathComponent()   // repo root
            .appendingPathComponent("corpus")
            .appendingPathComponent("matrix")
            .appendingPathComponent("default-spec.frozen.json")

        // No trailing newline — matches the existing file's convention (last byte is `}`).
        let json = try SyxxMatrixIntegration.defaultSpecJSON()
        try json.write(to: oraclePath, atomically: true, encoding: .utf8)
        print("Oracle regenerated at \(oraclePath.path) (\(json.utf8.count) bytes)")
    }
}

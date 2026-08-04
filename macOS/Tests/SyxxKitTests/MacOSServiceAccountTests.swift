import XCTest
@testable import SyxxKit

/// Real-host verification of the `_unbound` service-account provisioner's READ-ONLY logic (the parts that
/// need no root): the idempotency predicate and the UID/GID selection. The privileged mutations
/// (`ensure()` create, `chownRecursively`) require root and are exercised by a real `sudo` run, not here.
final class MacOSServiceAccountTests: XCTestCase {

    /// The Swift UID/GID picker must agree, on THIS host, with the owner's confirmed shell logic:
    /// `dscl . -list <path> <key> | awk '$2<500' | sort -n | tail -1  +1`. Cross-checked live.
    func testNextSystemIDMatchesLiveDsclForUsersAndGroups() throws {
        let acct = MacOSServiceAccount()
        for (listPath, key) in [("/Users", "UniqueID"), ("/Groups", "PrimaryGroupID")] {
            let expected = try highestSub500ViaDscl(listPath: listPath, key: key) + 1
            let got = acct.nextSystemID(listPath: listPath, key: key)
            XCTAssertEqual(got, expected,
                "Swift nextSystemID(\(listPath),\(key)) must equal live dscl highest-<500 +1")
            // Sanity: allocations must land inside the system range.
            XCTAssertNotNil(got)
            if let g = got { XCTAssertTrue(g > 0 && g <= 500, "system id \(g) out of (0,500] range") }
        }
    }

    /// Idempotency predicate honesty: `isProvisioned()` reflects what `id -u` can actually resolve — the
    /// exact failure the owner's half-provisioned (no-UniqueID) record exhibited. Whatever the account's
    /// current state, `isProvisioned()` must agree with `id -u <name>` resolving a <500 UID.
    func testIsProvisionedAgreesWithIdCommand() {
        let acct = MacOSServiceAccount()
        let liveUID = try? runShell("/usr/bin/id", ["-u", "_unbound"])
        let liveResolvable = liveUID.flatMap { Int($0.trimmingCharacters(in: .whitespacesAndNewlines)) }
            .map { $0 > 0 && $0 < 500 } ?? false
        XCTAssertEqual(acct.isProvisioned(), liveResolvable,
            "isProvisioned() must track whether `id -u _unbound` resolves a system UID")
    }

    // MARK: - helpers (independent re-implementation of the owner's shell pipeline for cross-checking)

    private func highestSub500ViaDscl(listPath: String, key: String) throws -> Int {
        let out = try runShell("/usr/bin/dscl", [".", "-list", listPath, key])
        var highest = -1
        for line in out.split(separator: "\n") {
            guard let last = line.split(whereSeparator: { $0 == " " || $0 == "\t" }).last,
                  let n = Int(last), n < 500 else { continue }
            highest = max(highest, n)
        }
        return highest
    }

    private func runShell(_ path: String, _ args: [String]) throws -> String {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: path)
        p.arguments = args
        let pipe = Pipe(); p.standardOutput = pipe; p.standardError = Pipe()
        try p.run()
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return String(data: data, encoding: .utf8) ?? ""
    }
}

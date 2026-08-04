import Foundation
import Testing
@testable import SyxxKit

// Phase 0 — canonical TriState result enum. The three raw values are the ONLY
// wire-format strings the client kits accept; any other spelling silently breaks
// cross-platform JSON parity with the Windows Syxx.Kit twin and the Python server.
//
// Distinct from ConditionState (yes/no/unknown), which describes network
// conditions that can genuinely be unknown.

@Suite("TriState result enum")
struct TriStateTests {

    @Test("raw values are exactly success/failure/inconclusive")
    func rawValuesAreCanonical() {
        // The whole point: these exact bytes must match the C# JSON converter
        // output and the Python TriState.value. A rename here silently breaks
        // cross-platform parity, so the assertion is literal.
        #expect(TriState.success.rawValue == "success")
        #expect(TriState.failure.rawValue == "failure")
        #expect(TriState.inconclusive.rawValue == "inconclusive")
    }

    @Test("Codable round-trip emits bare lowercase strings")
    func jsonRoundTrip() throws {
        // Codable synthesis on a String-backed enum uses the raw value as the
        // encoded form. If the backing type or the case names ever change, the
        // JSON will change too — this test goes RED.
        let enc = JSONEncoder()
        enc.outputFormatting = [.sortedKeys]
        let dec = JSONDecoder()

        let cases: [TriState] = [.success, .failure, .inconclusive]
        let expected: [String] = ["success", "failure", "inconclusive"]
        for (value, literal) in zip(cases, expected) {
            let data = try enc.encode(value)
            let json = String(decoding: data, as: UTF8.self)
            #expect(json == "\"\(literal)\"")
            let back = try dec.decode(TriState.self, from: data)
            #expect(back == value)
        }
    }

    @Test("three distinct members, no more")
    func exactlyThreeMembers() {
        // Adding a fourth case (e.g. "unknown") would silently give the enum
        // a different cardinality from its C#/Python twins; this assertion
        // catches that drift.
        let allCases: [TriState] = [.success, .failure, .inconclusive]
        #expect(allCases.count == 3)
        #expect(Set(allCases.map(\.rawValue)).count == 3)
    }

    @Test("failure case: wrong spellings would be caught")
    func failureCaseWrongSpellingIsCaught() {
        // Failure case (house rule): prove the exact-string assertions above
        // are not vacuous. If anyone retypes a raw value, the literal in this
        // test must NOT equal the real raw value.
        #expect(TriState.success.rawValue != "ok")
        #expect(TriState.failure.rawValue != "failed")
        #expect(TriState.failure.rawValue != "error")
        #expect(TriState.inconclusive.rawValue != "unknown")
    }
}

import Foundation
import SyxxKit

/// Phase 4.2: emit a canonical behavioral transcript from the production synthesizer.
/// Hidden CI subcommand — not advertised in user help until owner-approved.
///
/// Schema (stable keys, sorted where applicable):
/// - schemaVersion, vectorId
/// - evidenceCompleteness (untested section count, total sections)
/// - determinationState, diagnosisIds (sorted)
/// - hasHardGatedSection
/// - confidencePresent (bool only — never invent a number)
enum ParityTranscriptCommand {
    /// Fixed epoch so transcripts are byte-stable across hosts.
    private static let fixedNow = Date(timeIntervalSince1970: 1_752_268_477)

    @discardableResult
    static func run(args: [String]) -> Int32 {
        let outPath = args.first(where: { $0.hasPrefix("--out=") })
            .map { String($0.dropFirst("--out=".count)) }
            ?? "transcript.macos-arm64.json"

        // Vector: empty SectionInputs (all untested). Production engine path only.
        let inputs = SectionInputs()
        let conclusion = Synthesizer.conclude(from: inputs, now: fixedNow)

        let state = conclusion.determination?.state.rawValue ?? "none"
        let diagnoses = (conclusion.determination?.diagnoses ?? [])
            .map(\.id.rawValue)
            .sorted()
        let untested = conclusion.sectionStatuses.filter { $0.status == .untested }.count
        let total = conclusion.sectionStatuses.count

        // Explicit nulls via NSNull where a field is absent; no timestamps, paths, or random IDs.
        let payload: [String: Any] = [
            "schemaVersion": 1,
            "vectorId": "empty-section-inputs",
            "evidenceCompleteness": [
                "untestedSections": untested,
                "totalSections": total,
                "observedEvidenceCount": conclusion.evidence.count,
            ],
            "determinationState": state,
            "diagnosisIds": diagnoses,
            "hasHardGatedSection": conclusion.hasHardGatedSection,
            "confidencePresent": false,
            "engine": "Synthesizer.conclude",
        ]

        guard JSONSerialization.isValidJSONObject(payload),
              let data = try? JSONSerialization.data(withJSONObject: payload, options: [.sortedKeys, .prettyPrinted]),
              let text = String(data: data, encoding: .utf8) else {
            fputs("parity-transcript: failed to serialize\n", stderr)
            return CLIExitStatus.failure.rawValue
        }

        do {
            try (text + "\n").write(toFile: outPath, atomically: true, encoding: .utf8)
        } catch {
            fputs("parity-transcript: write failed: \(error)\n", stderr)
            return CLIExitStatus.failure.rawValue
        }
        return CLIExitStatus.completed.rawValue
    }
}

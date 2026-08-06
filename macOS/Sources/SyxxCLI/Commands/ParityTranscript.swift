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
        // Task 1.3 — corpus mode. Additive: when `--vectors=` is absent, nothing below this block
        // runs and the single-record path emits exactly the bytes it emitted before, so the
        // existing CI job cannot regress while the corpus path is being built (plan Task 2.4).
        if let vectorDir = args.first(where: { $0.hasPrefix("--vectors=") })
            .map({ String($0.dropFirst("--vectors=".count)) }) {
            return runVectors(directory: vectorDir, listOnly: args.contains("--list-only"))
        }

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

    /// Phase 1 scope is LISTING ONLY: prove both runtimes read one directory into the same case
    /// count and the same sorted id list. Emitting a record per case is Phase 2 (plan Task 2.2), so
    /// `--vectors` without `--list-only` fails rather than quietly falling back to the
    /// single-vector transcript — a silent fallback would publish a one-case transcript under a
    /// flag that asked for a whole corpus.
    private static func runVectors(directory: String, listOnly: Bool) -> Int32 {
        guard listOnly else {
            fputs("""
                parity-transcript: --vectors requires --list-only in Phase 1. \
                Full-corpus record emission arrives with Phase 2; refusing to fall back to \
                single-vector mode.

                """, stderr)
            return CLIExitStatus.failure.rawValue
        }
        do {
            let cases = try ParityVectorLoader.load(directory: URL(fileURLWithPath: directory))
            print("caseCount=\(cases.count)")
            for c in cases { print(c.vectorId) }
            return CLIExitStatus.completed.rawValue
        } catch {
            fputs("parity-transcript: \(String(describing: error))\n", stderr)
            return CLIExitStatus.failure.rawValue
        }
    }
}

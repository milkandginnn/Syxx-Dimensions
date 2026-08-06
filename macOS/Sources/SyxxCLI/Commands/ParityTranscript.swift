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
            let corpusOut = args.first(where: { $0.hasPrefix("--out=") })
                .map { String($0.dropFirst("--out=".count)) }
                ?? "transcript.macos-arm64.json"
            return runVectors(directory: vectorDir, listOnly: args.contains("--list-only"), outPath: corpusOut)
        }

        let outPath = args.first(where: { $0.hasPrefix("--out=") })
            .map { String($0.dropFirst("--out=".count)) }
            ?? "transcript.macos-arm64.json"

        // Vector: empty SectionInputs (all untested). Production engine path only.
        let payload = record(for: SectionInputs(), vectorId: "empty-section-inputs", now: fixedNow)
        return write(payload, to: outPath)
    }

    /// One canonical record per case — the single shared builder for both the single-vector path
    /// and the corpus path, so the two shapes cannot drift (plan Task 2.4).
    private static func record(for inputs: SectionInputs, vectorId: String, now: Date) -> [String: Any] {
        let conclusion = Synthesizer.conclude(from: inputs, now: now)

        let state = conclusion.determination?.state.rawValue ?? "none"
        // Ordinal (UTF-8 byte) order, matching C# StringComparer.Ordinal — Swift's plain sorted()
        // is Unicode-canonical, which coincides with ordinal only while every id is ASCII
        // (spec "Output record" ordering rule).
        let diagnoses = (conclusion.determination?.diagnoses ?? [])
            .map(\.id.rawValue)
            .sorted { $0.utf8.lexicographicallyPrecedes($1.utf8) }
        let untested = conclusion.sectionStatuses.filter { $0.status == .untested }.count
        let total = conclusion.sectionStatuses.count

        // Explicit nulls via NSNull where a field is absent; no timestamps, paths, or random IDs.
        return [
            "schemaVersion": 1,
            "vectorId": vectorId,
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
    }

    private static func write(_ payload: [String: Any], to outPath: String) -> Int32 {
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

    /// Corpus mode. `--list-only` proves both runtimes read one directory into the same case count
    /// and the same sorted id list (Phase 1 verification); without it, one canonical record is
    /// emitted per case inside the transcript envelope (plan Task 2.2). The envelope's `evaluated`
    /// is written from a counter incremented per case actually concluded — never from the input
    /// list's length — and the emitter refuses to write a transcript whose counter disagrees with
    /// its case count (plan Task 2.3; spec "Transcript envelope").
    private static func runVectors(directory: String, listOnly: Bool, outPath: String) -> Int32 {
        let cases: [ParityVectorLoader.LoadedCase]
        do {
            cases = try ParityVectorLoader.load(directory: URL(fileURLWithPath: directory))
        } catch {
            fputs("parity-transcript: \(String(describing: error))\n", stderr)
            return CLIExitStatus.failure.rawValue
        }

        if listOnly {
            print("caseCount=\(cases.count)")
            for c in cases { print(c.vectorId) }
            return CLIExitStatus.completed.rawValue
        }

        var evaluated = 0
        var records: [[String: Any]] = []
        for c in cases {
            records.append(record(for: c.inputs, vectorId: c.vectorId, now: c.pinnedNow))
            evaluated += 1
        }

        guard evaluated == cases.count, evaluated > 0 else {
            fputs("parity-transcript: evaluated \(evaluated) of \(cases.count) cases — refusing to write a transcript that does not cover its corpus\n", stderr)
            return CLIExitStatus.failure.rawValue
        }

        let envelope: [String: Any] = [
            "schemaVersion": 1,
            "caseCount": cases.count,
            "evaluated": evaluated,
            "records": records,
        ]
        return write(envelope, to: outPath)
    }
}

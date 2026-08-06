import Foundation

// PARITY_VECTOR_V1 — Swift side of the shared conclusion-vector loader
// (contract: docs/parity-vector-v1-spec.md; track: cross-platform-parity_20260805, Task 1.3).
//
// WHY THIS LIVES IN SyxxKit AND NOT SyxxCLI
// ----------------------------------------
// The plan says "Swift loader in SyxxCLI". It is here instead, deliberately, for two reasons:
//
//  1. `ExfilAssessment` (Forensics/ExfilAnalyzer.swift:16-37) has only the INTERNAL memberwise
//     init, so the `exfil` field cannot be constructed from outside this module at all. A loader
//     in SyxxCLI could not decode a field that is in the shared 32-field set.
//  2. There is no SyxxCLITests target. SyxxKit placement is what makes the loader unit-testable
//     in-process (SyxxKitTests/ParityVectorLoaderTests.swift). A loader whose rejection rules are
//     only reachable through a subprocess is a loader whose rejection rules do not get tested.
//
// SyxxCLI keeps only the flag parsing and the printing; the contract lives here.
//
// FAIL-LOUD DISCIPLINE
// --------------------
// There is no partial-success path. The first rejection throws and nothing is returned — a corpus
// loads whole or fails by name. An unreadable directory and an EMPTY directory are both errors,
// not empty lists: "zero cases loaded" rendering as success is the exact shape of the silent-skip
// defect this track exists to close.

/// Loads a PARITY_VECTOR_V1 case directory into engine-ready `SectionInputs`.
///
/// ## Wire-shape rules
///
/// - The case file is `NNN_<vectorId>.json` — three ASCII digits, an underscore, then the id.
///   `NNN` is the sort key and `<vectorId>` must equal what the file declares.
/// - Case order is the sorted filename list. Because duplicate `NNN` prefixes are rejected, the
///   order is fully determined by three ASCII digits and is therefore collation-independent — the
///   C# twin cannot order the same corpus differently.
/// - Every stored property that is NON-optional in Swift is REQUIRED in the JSON; every `T?`
///   property may be omitted (omitted → `nil`). No field in this loader has a default value, so
///   there is no defaulting behaviour for the C# twin to mirror incorrectly. The one exception is
///   `resolution` (`DNSResponse`), which carries a hand-written tolerant decoder of its own
///   (DNSResolver.swift:240-261) where only `status` is required.
/// - `serverGroundTruth` decodes through `GroundTruth`'s custom `CodingKeys`
///   (DimensionClient.swift:35-39), which rename `domain` → `"name"` and `result` → `"conclusion"`.
///   A vector must use the WIRE names. This is a server-contract spelling, not a Swift spelling,
///   and it is the most likely place for the twins to disagree.
/// - Three field types are NOT `Codable` and are decoded by hand here, so their wire shape is
///   defined by this file and nothing else: `nxCanary` / `dnssecCanary`
///   (`DNSCanaries.CanaryResult`), `leak` (`LeakTestResult`), and `exfil` (`[ExfilAssessment]`).
/// - `warp` (the legacy `WarpResult` shim, Synthesizer.swift:44) is in the shared 32-field set and
///   is therefore decoded, but `Synthesizer.conclude` never reads it — the Mirror adapter takes
///   `warpManifest`. A vector that populates `warp` exercises the loader and nothing else; it
///   cannot change a conclusion.
///
/// Full per-field shapes: `conductor/tracks/cross-platform-parity_20260805/swift-field-shapes.md`.
public enum ParityVectorLoader {

    /// The only `schemaVersion` this loader recognises. Any other value is a rejection — never a
    /// best-effort parse (spec, "Rejection behavior — summary" rule 1).
    public static let supportedSchemaVersion = 1

    /// The 32 shared field names, in the spec's order. `resolverSubstituted` and
    /// `transportObservation` are Swift-only and are deliberately ABSENT: a vector naming either is
    /// rejected by name so the asymmetry stays loud instead of being silently dropped by whichever
    /// twin cannot express it.
    public static let sharedFieldNames: Set<String> = [
        "resolution", "dnssec", "nxCanary", "dnssecCanary", "comparison", "targetDomain",
        "targetQueryType", "warp", "warpManifest", "network", "leak", "exfil",
        "cageAttributionRate", "drifts", "configChanges", "latency", "stability",
        "wildcardSynthesis", "denial", "dualStack", "edns0", "versionBind", "stubEntropy",
        "authoritative", "svcb", "ttlAnomaly", "cnameChain", "encryptedTransport", "openResolver",
        "ecs", "responseSize", "serverGroundTruth",
    ]

    /// The four keys a case file may carry at top level — exactly these, no more and no fewer.
    static let topLevelKeys: Set<String> = ["schemaVersion", "vectorId", "pinnedUnixSeconds", "sectionInputs"]

    /// One loaded case: the id, its sort key, the clock both engines must pin to, and the inputs.
    public struct LoadedCase: Sendable {
        /// The filename remainder after `NNN_`, cross-checked against the file's own `vectorId`.
        public let vectorId: String
        /// The three-digit filename prefix that fixes this case's position in the corpus.
        public let sortKey: String
        /// `Date(timeIntervalSince1970:)` built from `pinnedUnixSeconds` and nothing else.
        public let pinnedNow: Date
        public let inputs: SectionInputs
        /// The file this case came from, so a downstream failure can name it too.
        public let caseFile: String
    }

    /// Every way a corpus can be refused. Each case names the offending file, and where a key is
    /// implicated, the key. One case per rule in the spec's "Rejection behavior — summary", plus
    /// the two structural refusals the spec's "loads whole or fails loudly" sentence requires
    /// (unreadable directory, empty directory) and the two the filename layout requires
    /// (duplicate sort key, malformed case filename).
    public enum LoadError: Error, CustomStringConvertible, Equatable {
        /// The vector directory could not be enumerated. Never an empty case list.
        case unreadableDirectory(path: String, reason: String)
        /// The directory was readable and contained no case files. Zero cases is a failure, not a
        /// vacuous pass — an empty corpus must not be loadable-whole.
        case emptyDirectory(path: String)
        /// Two case files share a three-digit prefix, so their relative order is undefined.
        case duplicateSortKey(sortKey: String, caseFiles: [String])
        /// A `.json` entry that does not match `^\d\d\d_`, or a directory-shaped entry. Ignoring
        /// either would silently drop a case someone believed was in the corpus.
        case malformedCaseFilename(caseFile: String, reason: String)
        /// The file could not be read off disk.
        case unreadableFile(caseFile: String, reason: String)
        /// Spec rule 4 — the file is not valid JSON.
        case invalidJSON(caseFile: String, reason: String)
        /// A top-level key outside the four the contract allows.
        case unknownTopLevelKey(caseFile: String, key: String)
        /// One of the four required top-level keys is absent. (`pinnedUnixSeconds` has its own
        /// case; spec rule 5 names it specifically.)
        case missingTopLevelKey(caseFile: String, key: String)
        /// Spec rule 1 — `schemaVersion` is a value this loader does not recognise.
        case unrecognisedSchemaVersion(caseFile: String, found: String)
        /// Spec rule 2 — the declared `vectorId` disagrees with the filename remainder.
        case vectorIdMismatch(caseFile: String, declared: String, fromFilename: String)
        /// Spec rule 5 — `pinnedUnixSeconds` is absent, null, or not an integer.
        case unusablePinnedUnixSeconds(caseFile: String, reason: String)
        /// Spec rule 3 — a `sectionInputs` key outside the shared 32-field set.
        case unknownSectionInputsKey(caseFile: String, key: String)
        /// A known field whose payload does not match the field's type. Named, never skipped.
        case undecodableSectionInputsValue(caseFile: String, key: String, reason: String)

        public var description: String {
            switch self {
            case .unreadableDirectory(let path, let reason):
                return "parity vector directory could not be read: \(path) — \(reason)"
            case .emptyDirectory(let path):
                return "parity vector directory contains no case files: \(path) — an empty corpus is a failure, not zero cases"
            case .duplicateSortKey(let sortKey, let caseFiles):
                return "duplicate sort prefix '\(sortKey)' shared by \(caseFiles.joined(separator: ", ")) — case order would be ambiguous"
            case .malformedCaseFilename(let caseFile, let reason):
                return "case \(caseFile): unusable case filename — \(reason)"
            case .unreadableFile(let caseFile, let reason):
                return "case \(caseFile): could not be read — \(reason)"
            case .invalidJSON(let caseFile, let reason):
                return "case \(caseFile): not valid JSON — \(reason)"
            case .unknownTopLevelKey(let caseFile, let key):
                return "case \(caseFile): unknown top-level key '\(key)' — a case carries exactly schemaVersion, vectorId, pinnedUnixSeconds, sectionInputs"
            case .missingTopLevelKey(let caseFile, let key):
                return "case \(caseFile): missing required top-level key '\(key)'"
            case .unrecognisedSchemaVersion(let caseFile, let found):
                return "case \(caseFile): unrecognised schemaVersion \(found) — this loader recognises \(ParityVectorLoader.supportedSchemaVersion) only, and never best-effort parses"
            case .vectorIdMismatch(let caseFile, let declared, let fromFilename):
                return "case \(caseFile): vectorId '\(declared)' disagrees with the filename remainder '\(fromFilename)'"
            case .unusablePinnedUnixSeconds(let caseFile, let reason):
                return "case \(caseFile): pinnedUnixSeconds is unusable — \(reason)"
            case .unknownSectionInputsKey(let caseFile, let key):
                return "case \(caseFile): unknown sectionInputs key '\(key)' — outside the PARITY_VECTOR_V1 shared field set, so it cannot be expressed in v1 and is refused rather than dropped"
            case .undecodableSectionInputsValue(let caseFile, let key, let reason):
                return "case \(caseFile): sectionInputs key '\(key)' could not be decoded — \(reason)"
            }
        }
    }

    // MARK: - Loading

    /// Load every case file in `directory`, in sorted case order.
    ///
    /// Throws on the first rejection and returns nothing — there is no partial success. An empty
    /// or unreadable directory throws rather than returning `[]`.
    public static func load(directory: URL) throws -> [LoadedCase] {
        let entries: [URL]
        do {
            entries = try FileManager.default.contentsOfDirectory(
                at: directory,
                includingPropertiesForKeys: [.isDirectoryKey],
                options: [])
        } catch {
            throw LoadError.unreadableDirectory(path: directory.path, reason: shortReason(error))
        }

        // Enumeration order differs by filesystem and runtime, so nothing downstream may depend on
        // it. Candidates are collected, checked for filename legality, then sorted by sort key.
        var candidates: [(sortKey: String, vectorId: String, url: URL)] = []
        for entry in entries {
            let name = entry.lastPathComponent
            let isDirectory = (try? entry.resourceValues(forKeys: [.isDirectoryKey]))?.isDirectory ?? false
            if isDirectory {
                // The spec's layout block shows `002_.../`, and plan Task 1.6 says to mirror the
                // per-case DIRECTORY layout of warp-manifest-vectors, but the spec's prose says
                // "One case per file". v1 here is file-per-case; a directory-shaped entry is
                // refused loudly rather than skipped, so the disagreement surfaces at the moment
                // it would otherwise cost a case.
                if name.hasPrefix(".") { continue }
                throw LoadError.malformedCaseFilename(
                    caseFile: name,
                    reason: "directory-shaped entry; PARITY_VECTOR_V1 is one case per NNN_<vectorId>.json file")
            }
            guard entry.pathExtension == "json" else { continue }   // README.md, .DS_Store, …
            guard let parsed = parseCaseFilename(name) else {
                throw LoadError.malformedCaseFilename(
                    caseFile: name,
                    reason: "a .json entry must be named NNN_<vectorId>.json with a three-digit sort prefix")
            }
            candidates.append((parsed.sortKey, parsed.vectorId, entry))
        }

        guard !candidates.isEmpty else { throw LoadError.emptyDirectory(path: directory.path) }

        var seen: [String: String] = [:]
        for c in candidates {
            if let first = seen[c.sortKey] {
                throw LoadError.duplicateSortKey(sortKey: c.sortKey,
                                                 caseFiles: [first, c.url.lastPathComponent].sorted())
            }
            seen[c.sortKey] = c.url.lastPathComponent
        }

        candidates.sort { $0.sortKey < $1.sortKey }

        return try candidates.map { candidate in
            try loadCase(url: candidate.url, sortKey: candidate.sortKey, vectorIdFromFilename: candidate.vectorId)
        }
    }

    /// `NNN_<vectorId>.json` → `(NNN, vectorId)`. `nil` when the name does not match.
    static func parseCaseFilename(_ name: String) -> (sortKey: String, vectorId: String)? {
        guard name.hasSuffix(".json") else { return nil }
        let stem = String(name.dropLast(".json".count))
        guard stem.count >= 5 else { return nil }              // NNN + "_" + at least one id char
        let sortKey = String(stem.prefix(3))
        guard sortKey.allSatisfy({ $0.isASCII && $0.isNumber }) else { return nil }
        guard stem[stem.index(stem.startIndex, offsetBy: 3)] == "_" else { return nil }
        let vectorId = String(stem.dropFirst(4))
        guard !vectorId.isEmpty else { return nil }
        return (sortKey, vectorId)
    }

    private static func loadCase(url: URL, sortKey: String, vectorIdFromFilename: String) throws -> LoadedCase {
        let caseFile = url.lastPathComponent
        let data: Data
        do {
            data = try Data(contentsOf: url)
        } catch {
            throw LoadError.unreadableFile(caseFile: caseFile, reason: shortReason(error))
        }

        let decoder = JSONDecoder()
        decoder.userInfo[.parityCaseFile] = caseFile
        let decoded: DecodedCase
        do {
            decoded = try decoder.decode(DecodedCase.self, from: data)
        } catch let e as LoadError {
            throw e
        } catch let e as DecodingError {
            // A DecodingError reaching here is a structural JSON problem (the document is not an
            // object, or it is not JSON at all); every field-level failure is already converted to
            // a named LoadError inside DecodedCase.
            throw LoadError.invalidJSON(caseFile: caseFile, reason: shortReason(e))
        } catch {
            throw LoadError.invalidJSON(caseFile: caseFile, reason: shortReason(error))
        }

        guard decoded.vectorId == vectorIdFromFilename else {
            throw LoadError.vectorIdMismatch(caseFile: caseFile,
                                             declared: decoded.vectorId,
                                             fromFilename: vectorIdFromFilename)
        }

        return LoadedCase(vectorId: decoded.vectorId,
                          sortKey: sortKey,
                          pinnedNow: Date(timeIntervalSince1970: Double(decoded.pinnedUnixSeconds)),
                          inputs: decoded.inputs,
                          caseFile: caseFile)
    }

    /// One line, no file paths or locale-dependent text — these strings reach CI logs and a diff.
    private static func shortReason(_ error: Error) -> String {
        if let d = error as? DecodingError {
            switch d {
            case .dataCorrupted(let ctx):       return ctx.debugDescription
            case .keyNotFound(let key, _):      return "missing key '\(key.stringValue)'"
            case .typeMismatch(_, let ctx):     return ctx.debugDescription
            case .valueNotFound(_, let ctx):    return ctx.debugDescription
            @unknown default:                   return "\(d)"
            }
        }
        return (error as NSError).localizedDescription
    }
}

extension CodingUserInfoKey {
    /// Carries the case filename into `Decodable` so a rejection can name the file it came from.
    static let parityCaseFile = CodingUserInfoKey(rawValue: "syxx.parity.caseFile")!
}

/// A `CodingKey` over arbitrary JSON member names, so unknown keys can be OBSERVED and named
/// rather than being invisible (Swift's synthesized decoders silently ignore unknown keys — the
/// precise behaviour PARITY_VECTOR_V1 forbids).
struct ParityVectorKey: CodingKey {
    let stringValue: String
    var intValue: Int? { nil }
    init(stringValue: String) { self.stringValue = stringValue }
    init(_ s: String) { self.stringValue = s }
    init?(intValue: Int) { return nil }
}

/// The decoded form of one case file. Every rejection rule in the contract is enforced here.
private struct DecodedCase: Decodable {
    let vectorId: String
    let pinnedUnixSeconds: Int
    let inputs: SectionInputs

    init(from decoder: Decoder) throws {
        let caseFile = decoder.userInfo[.parityCaseFile] as? String ?? "<unknown>"
        let top = try decoder.container(keyedBy: ParityVectorKey.self)

        // Unknown top-level keys first, smallest name first, so a case with two of them fails the
        // same way on every run and on both runtimes.
        let unknownTop = top.allKeys.map(\.stringValue)
            .filter { !ParityVectorLoader.topLevelKeys.contains($0) }
            .sorted()
        if let key = unknownTop.first {
            throw ParityVectorLoader.LoadError.unknownTopLevelKey(caseFile: caseFile, key: key)
        }
        for required in ["schemaVersion", "vectorId", "sectionInputs"]
        where !top.contains(ParityVectorKey(required)) {
            throw ParityVectorLoader.LoadError.missingTopLevelKey(caseFile: caseFile, key: required)
        }

        // schemaVersion — a value we do not recognise is refused, including a non-integer one.
        let versionKey = ParityVectorKey("schemaVersion")
        let version: Int
        do {
            version = try top.decode(Int.self, forKey: versionKey)
        } catch {
            throw ParityVectorLoader.LoadError.unrecognisedSchemaVersion(
                caseFile: caseFile, found: "(not an integer)")
        }
        guard version == ParityVectorLoader.supportedSchemaVersion else {
            throw ParityVectorLoader.LoadError.unrecognisedSchemaVersion(
                caseFile: caseFile, found: String(version))
        }

        // vectorId — the filename cross-check happens in the loader, which knows the filename.
        do {
            vectorId = try top.decode(String.self, forKey: ParityVectorKey("vectorId"))
        } catch {
            throw ParityVectorLoader.LoadError.undecodableSectionInputsValue(
                caseFile: caseFile, key: "vectorId", reason: "expected a string")
        }

        // pinnedUnixSeconds — spec rule 5. Absent, null and non-integer are all refusals; both
        // engines build their clock from this and nothing else, so a fabricated fallback would be
        // a clock neither engine measured.
        let pinnedKey = ParityVectorKey("pinnedUnixSeconds")
        guard top.contains(pinnedKey) else {
            throw ParityVectorLoader.LoadError.unusablePinnedUnixSeconds(
                caseFile: caseFile, reason: "absent")
        }
        if (try? top.decodeNil(forKey: pinnedKey)) == true {
            throw ParityVectorLoader.LoadError.unusablePinnedUnixSeconds(
                caseFile: caseFile, reason: "null")
        }
        do {
            pinnedUnixSeconds = try top.decode(Int.self, forKey: pinnedKey)
        } catch {
            throw ParityVectorLoader.LoadError.unusablePinnedUnixSeconds(
                caseFile: caseFile, reason: "not an integer")
        }

        let section: KeyedDecodingContainer<ParityVectorKey>
        do {
            section = try top.nestedContainer(keyedBy: ParityVectorKey.self,
                                              forKey: ParityVectorKey("sectionInputs"))
        } catch {
            throw ParityVectorLoader.LoadError.undecodableSectionInputsValue(
                caseFile: caseFile, key: "sectionInputs", reason: "expected an object")
        }
        inputs = try DecodedCase.decodeSectionInputs(from: section, caseFile: caseFile)
    }

    /// Decode the `sectionInputs` object into `SectionInputs`, refusing any key outside the shared
    /// set by name. Keys are visited in sorted order so the named rejection is deterministic.
    private static func decodeSectionInputs(from c: KeyedDecodingContainer<ParityVectorKey>,
                                            caseFile: String) throws -> SectionInputs {
        let names = c.allKeys.map(\.stringValue).sorted()
        if let unknown = names.first(where: { !ParityVectorLoader.sharedFieldNames.contains($0) }) {
            throw ParityVectorLoader.LoadError.unknownSectionInputsKey(caseFile: caseFile, key: unknown)
        }

        var inputs = SectionInputs()
        for name in names {
            let key = ParityVectorKey(name)
            func value<T: Decodable>(_ type: T.Type) throws -> T {
                do {
                    return try c.decode(type, forKey: key)
                } catch {
                    throw ParityVectorLoader.LoadError.undecodableSectionInputsValue(
                        caseFile: caseFile, key: name,
                        reason: ParityVectorLoader.decodeFailureReason(error))
                }
            }
            switch name {
            case "resolution":           inputs.resolution = try value(DNSResponse.self)
            case "dnssec":               inputs.dnssec = try value(DNSSECStatus.self)
            case "nxCanary":             inputs.nxCanary = try value(CanaryResultShim.self).model
            case "dnssecCanary":         inputs.dnssecCanary = try value(CanaryResultShim.self).model
            case "comparison":           inputs.comparison = try value(ComparisonResult.self)
            case "targetDomain":         inputs.targetDomain = try value(String.self)
            case "targetQueryType":      inputs.targetQueryType = try value(UInt16.self)
            case "warp":                 inputs.warp = try value(WarpResult.self)
            case "warpManifest":         inputs.warpManifest = try value(WarpManifestResult.self)
            case "network":              inputs.network = try value(NetworkStateReport.self)
            case "leak":                 inputs.leak = try value(LeakTestResultShim.self)
                                                            .model(caseFile: caseFile, key: name)
            case "exfil":                inputs.exfil = try value([ExfilAssessmentShim].self).map(\.model)
            case "cageAttributionRate":  inputs.cageAttributionRate = try value(Double.self)
            case "drifts":               inputs.drifts = try value([AnswerDrift].self)
            case "configChanges":        inputs.configChanges = try value([ConfigChange].self)
            case "latency":              inputs.latency = try value(ResolverLatency.self)
            case "stability":            inputs.stability = try value(AnswerStability.self)
            case "wildcardSynthesis":    inputs.wildcardSynthesis = try value(WildcardSynthesis.self)
            case "denial":               inputs.denial = try value(DenialOfExistence.self)
            case "dualStack":            inputs.dualStack = try value(DualStackAudit.self)
            case "edns0":                inputs.edns0 = try value(EDNS0Capability.self)
            case "versionBind":          inputs.versionBind = try value(VersionBindResult.self)
            case "stubEntropy":          inputs.stubEntropy = try value(StubEntropyAudit.self)
            case "authoritative":        inputs.authoritative = try value(AuthoritativeComparison.self)
            case "svcb":                 inputs.svcb = try value(SVCBFinding.self)
            case "ttlAnomaly":           inputs.ttlAnomaly = try value(TTLAnomaly.self)
            case "cnameChain":           inputs.cnameChain = try value(CNAMEChainAudit.self)
            case "encryptedTransport":   inputs.encryptedTransport = try value(EncryptedTransport.self)
            case "openResolver":         inputs.openResolver = try value(OpenResolverFinding.self)
            case "ecs":                  inputs.ecs = try value(ECSLeak.self)
            case "responseSize":         inputs.responseSize = try value(ResponseSizeAudit.self)
            case "serverGroundTruth":    inputs.serverGroundTruth = try value(GroundTruth.self)
            default:
                // Unreachable: the unknown-key sweep above already refused anything outside the
                // shared set. Asserted rather than assumed so a future edit that adds a name to
                // `sharedFieldNames` without adding its arm here cannot decode to a silent nil —
                // which would render an untested field as covered.
                throw ParityVectorLoader.LoadError.unknownSectionInputsKey(caseFile: caseFile, key: name)
            }
        }
        return inputs
    }
}

extension ParityVectorLoader {
    /// Field-level failures are reported with the decoder's own explanation, trimmed to one line.
    static func decodeFailureReason(_ error: Error) -> String {
        guard let d = error as? DecodingError else { return (error as NSError).localizedDescription }
        switch d {
        case .keyNotFound(let key, let ctx):
            return "missing required member '\(key.stringValue)'\(pathSuffix(ctx))"
        case .typeMismatch(let type, let ctx):
            return "wrong type for \(type)\(pathSuffix(ctx))"
        case .valueNotFound(let type, let ctx):
            return "null where \(type) is required\(pathSuffix(ctx))"
        case .dataCorrupted(let ctx):
            return "\(ctx.debugDescription)\(pathSuffix(ctx))"
        @unknown default:
            return "\(d)"
        }
    }

    private static func pathSuffix(_ ctx: DecodingError.Context) -> String {
        let path = ctx.codingPath.map(\.stringValue).filter { !$0.isEmpty }
        return path.isEmpty ? "" : " at \(path.joined(separator: "."))"
    }
}

// MARK: - Hand-decoded field types
//
// These three types are not `Codable`, so their wire shape exists only here. Same rule as
// everywhere else in this loader: non-optional Swift property → required JSON member; `T?` →
// optional member. No defaults, so there is nothing for the C# twin to default differently.

/// Wire shape for `DNSCanaries.CanaryResult` (Synthesis/DNSCanaries.swift:18-29).
private struct CanaryResultShim: Decodable {
    let section: Section
    let status: SectionStatus
    let evidence: EvidenceItem?
    let detail: String
    var model: DNSCanaries.CanaryResult {
        .init(section: section, status: status, evidence: evidence, detail: detail)
    }
}

/// Wire shape for `CollectorFailure` (LeakDetector.swift:54-72), an enum with associated values.
/// `kind` names the case; `status` carries the HTTP status for the five cases that have one and is
/// omitted for the three that do not.
private struct CollectorFailureShim: Decodable {
    let kind: String
    let status: Int?

    func model(caseFile: String, key: String) throws -> CollectorFailure {
        func httpStatus() throws -> Int {
            guard let status else {
                throw ParityVectorLoader.LoadError.undecodableSectionInputsValue(
                    caseFile: caseFile, key: key,
                    reason: "collectorFailure kind '\(kind)' requires a 'status' member")
            }
            return status
        }
        switch kind {
        case "entitlementRequired":        return .entitlementRequired(status: try httpStatus())
        case "authenticationFailed":       return .authenticationFailed(status: try httpStatus())
        case "rateLimited":                return .rateLimited(status: try httpStatus())
        case "serverError":                return .serverError(status: try httpStatus())
        case "unexpectedStatus":           return .unexpectedStatus(try httpStatus())
        case "malformedResponse":          return .malformedResponse
        case "collectorIdentityUnverified": return .collectorIdentityUnverified
        case "transportFailure":           return .transportFailure
        default:
            throw ParityVectorLoader.LoadError.undecodableSectionInputsValue(
                caseFile: caseFile, key: key,
                reason: "unknown collectorFailure kind '\(kind)'")
        }
    }
}

/// Wire shape for `LeakTestResult` (LeakDetector.swift:153-217). `result` and `transportPinning`
/// name `LeakResult` / `CollectorTransportPinning` cases, neither of which is `Codable`, so both
/// are matched against their raw values here.
private struct LeakTestResultShim: Decodable {
    let result: String
    let observedResolvers: [String]
    let signature: String?
    let publicKey: String?
    let keyId: String?
    let signedPayload: String?
    let signatureValid: Bool?
    let errorReason: String?
    let transportPinning: String
    let signatureKeyTrusted: Bool?
    let collectorFailure: CollectorFailureShim?

    func model(caseFile: String, key: String) throws -> LeakTestResult {
        guard let leakResult = LeakResult(rawValue: result) else {
            throw ParityVectorLoader.LoadError.undecodableSectionInputsValue(
                caseFile: caseFile, key: key, reason: "unknown leak result '\(result)'")
        }
        guard let pinning = CollectorTransportPinning(rawValue: transportPinning) else {
            throw ParityVectorLoader.LoadError.undecodableSectionInputsValue(
                caseFile: caseFile, key: key,
                reason: "unknown transportPinning '\(transportPinning)'")
        }
        return LeakTestResult(result: leakResult,
                              observedResolvers: observedResolvers,
                              signature: signature,
                              publicKey: publicKey,
                              keyId: keyId,
                              signedPayload: signedPayload,
                              signatureValid: signatureValid,
                              errorReason: errorReason,
                              transportPinning: pinning,
                              signatureKeyTrusted: signatureKeyTrusted,
                              collectorFailure: try collectorFailure?.model(caseFile: caseFile, key: key))
    }
}

/// Wire shape for `ExfilAssessment` (Forensics/ExfilAnalyzer.swift:16-37). Its memberwise init is
/// internal, which is why this loader has to live inside SyxxKit.
private struct ExfilAssessmentShim: Decodable {
    let domain: String
    let longestLabel: String
    let labelLength: Int
    let entropyBitsPerChar: Double
    let beacon: BeaconClassification
    let beaconEvaluated: Bool
    let flaggedByLists: [String]
    let listsEvaluated: Bool
    let suspicion: ExfilSuspicion
    let rationale: [String]

    var model: ExfilAssessment {
        ExfilAssessment(domain: domain,
                        longestLabel: longestLabel,
                        labelLength: labelLength,
                        entropyBitsPerChar: entropyBitsPerChar,
                        beacon: beacon,
                        beaconEvaluated: beaconEvaluated,
                        flaggedByLists: flaggedByLists,
                        listsEvaluated: listsEvaluated,
                        suspicion: suspicion,
                        rationale: rationale)
    }
}

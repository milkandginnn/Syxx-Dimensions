import Foundation

/// The canonical, cross-platform byte-stable representation of the evidence a witness snapshot
/// signs (E011). Its field names, casing, null-omission, enum-as-string encoding, key sort, and
/// array sort are IDENTICAL to the C# `SignedEvidencePayload`, so an Ed25519 signature produced on
/// one platform verifies on the other.
///
/// Why a dedicated DTO rather than signing the wire `ComparisonResult`: the wire types diverge
/// across platforms (Swift `errorDescription` vs C# `ErrorMessage`; a C#-only `DohOutcomes`; a
/// `DNSResponseStatus` enum that Swift and C# encode differently). Signing them directly makes
/// byte-parity impossible and every cross-platform verify reads a false TAMPERED. This DTO uses
/// unified names and computes `success` once at sign time, so nothing platform-specific reaches
/// the signed bytes.
///
/// **Byte-parity rules (must hold identically in C#):**
/// - camelCase keys; deterministic recursive key sort.
/// - null optionals are omitted (Swift `JSONEncoder` omits nil by default; C# uses
///   `JsonIgnoreCondition.WhenWritingNull`).
/// - `/` is emitted unescaped and non-ASCII is emitted as raw UTF-8 (Swift `.withoutEscapingSlashes`
///   / C# `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`).
/// - set-valued fields are serialized as arrays sorted by UTF-8/ordinal order (NOT a Swift `Set`,
///   whose JSON array order is nondeterministic — that would break even same-platform stability).
/// - `resolverQueryOutcomes` is sorted by `resolverId` for a deterministic array order.
public struct SignedEvidencePayload: Codable, Equatable, Sendable {
    /// Signed payload schema version. Currently 2.
    public let formatVersion: Int
    /// Epoch milliseconds (UTC).
    public let timestamp: UInt64
    /// The subject of the witnessed query (the domain).
    public let queryId: String
    public let resolverQueryOutcomes: [ResolverQueryOutcomeCanonical]
    public let comparisonResult: ComparisonResultCanonical
    /// Epoch milliseconds (UTC); omitted when absent.
    public let expiresAt: UInt64?
    /// Present for evidentiary completeness; the FIELD exists on both platforms so byte-parity
    /// holds when nil (both omit it). Currently always nil until DoH outcomes are plumbed
    /// symmetrically into the witnessed comparison on both platforms.
    public let dohOutcomes: DoHOutcomesCanonical?
    /// B098: per-resolver typed record lists (name/type/TTL/rdata) that bind answer ownership
    /// to the specific resolver that produced them. Without this, equal aggregate answers can be
    /// swapped between resolvers without invalidating the signature. Sorted by resolverId for
    /// deterministic array order; omitted when empty.
    public let perResolverAnswers: [ResolverAnswerRecordsCanonical]?

    public init(
        formatVersion: Int,
        timestamp: UInt64,
        queryId: String,
        resolverQueryOutcomes: [ResolverQueryOutcomeCanonical],
        comparisonResult: ComparisonResultCanonical,
        expiresAt: UInt64?,
        dohOutcomes: DoHOutcomesCanonical?,
        perResolverAnswers: [ResolverAnswerRecordsCanonical]? = nil
    ) {
        self.formatVersion = formatVersion
        self.timestamp = timestamp
        self.queryId = queryId
        self.resolverQueryOutcomes = resolverQueryOutcomes
        self.comparisonResult = comparisonResult
        self.expiresAt = expiresAt
        self.dohOutcomes = dohOutcomes
        self.perResolverAnswers = perResolverAnswers
    }
}

public struct ResolverQueryOutcomeCanonical: Codable, Equatable, Sendable {
    public let resolverId: String
    public let success: Bool
    /// Unified name (NOT `errorDescription`); omitted when nil.
    public let errorMessage: String?
    /// Omitted when nil. Left nil today (outcomes carry no timing); if ever populated it must be an
    /// integral millisecond value to avoid Double-formatting divergence across platforms.
    public let responseTimeMs: Double?

    public init(resolverId: String, success: Bool, errorMessage: String?, responseTimeMs: Double?) {
        self.resolverId = resolverId
        self.success = success
        self.errorMessage = errorMessage
        self.responseTimeMs = responseTimeMs
    }
}

// B214: comparison helper that strips platform-divergent errorMessage for v2 display-integrity
// checks. At v2, errorMessage is free text from Swift `errorDescription` or .NET `ex.Message`
// — the same failure produces different strings, so byte-equality on this field is not
// meaningful across platforms. At v3+, errorMessage is nil on both sides.
extension Array where Element == ResolverQueryOutcomeCanonical {
    var strippingErrorMessage: [ResolverQueryOutcomeCanonical] {
        map { ResolverQueryOutcomeCanonical(resolverId: $0.resolverId, success: $0.success, errorMessage: nil, responseTimeMs: $0.responseTimeMs) }
    }
}

public struct ComparisonResultCanonical: Codable, Equatable, Sendable {
    /// "matches" | "diverges" | "inconclusive".
    public let result: String
    /// Reference (authoritative/public-consensus) answer IPs, sorted-unique; omitted when empty.
    public let authoritativeSet: [String]?
    /// Local-vantage answer IPs, sorted-unique; omitted when empty.
    public let vantageSet: [String]?
    /// Ph6.1/B098: per-record TTLs for authoritative set (same ordinal order as authoritativeSet); omitted when empty.
    public let authoritativeTtls: [UInt32]?
    /// Ph6.1/B098: per-record types (1=A, 28=AAAA) for authoritative set; omitted when empty.
    public let authoritativeTypes: [UInt16]?

    /// Wire key "conclusion" (E011 Ed25519 cross-platform parity); the Swift
    /// symbol is `result` per the code-naming ruling. `.sortedKeys` still orders on the wire key
    /// string, so the signed bytes are unchanged. Twin: C# ComparisonResultCanonical.Result.
    enum CodingKeys: String, CodingKey {
        case result = "conclusion"
        case authoritativeSet, vantageSet, authoritativeTtls, authoritativeTypes
    }

    public init(result: String, authoritativeSet: [String]?, vantageSet: [String]?,
                authoritativeTtls: [UInt32]? = nil, authoritativeTypes: [UInt16]? = nil) {
        self.result = result
        self.authoritativeSet = authoritativeSet
        self.vantageSet = vantageSet
        self.authoritativeTtls = authoritativeTtls
        self.authoritativeTypes = authoritativeTypes
    }
}

public struct DoHOutcomesCanonical: Codable, Equatable, Sendable {
    public let outcomes: [ResolverQueryOutcomeCanonical]

    public init(outcomes: [ResolverQueryOutcomeCanonical]) {
        self.outcomes = outcomes
    }
}

/// B098: per-resolver typed record list binding answer ownership to a specific resolver.
/// Records are sorted by (name, type, ttl, rdata) for deterministic byte-stable encoding.
/// This prevents equal aggregate answers from being swapped between resolvers without
/// invalidating the signature.
public struct ResolverAnswerRecordsCanonical: Codable, Equatable, Sendable {
    public let resolverId: String
    public let records: [CanonicalRecord]

    public struct CanonicalRecord: Codable, Equatable, Sendable, Comparable {
        public let name: String
        public let type: UInt16
        public let ttl: UInt32
        public let rdata: String

        public init(name: String, type: UInt16, ttl: UInt32, rdata: String) {
            self.name = name; self.type = type; self.ttl = ttl; self.rdata = rdata
        }

        public static func < (l: CanonicalRecord, r: CanonicalRecord) -> Bool {
            if l.name != r.name { return l.name.utf8.lexicographicallyPrecedes(r.name.utf8) }
            if l.type != r.type { return l.type < r.type }
            if l.ttl != r.ttl { return l.ttl < r.ttl }
            return l.rdata.utf8.lexicographicallyPrecedes(r.rdata.utf8)
        }
    }

    public init(resolverId: String, records: [CanonicalRecord]) {
        self.resolverId = resolverId
        self.records = records.sorted()
    }
}

public extension SignedEvidencePayload {

    /// Deterministic UTF-8/ordinal sort shared by every set-valued field, so Swift and C# order
    /// identical string sets identically. (String set members are ASCII in practice — IP literals
    /// and resolver IDs — so byte-ordinal and code-unit ordinal agree.)
    ///
    /// NOTE: bare `sorted()` on [String] in Swift uses Unicode canonical equivalence
    /// (NFD-aware), while C# `StringComparer.Ordinal` sorts by UTF-16 code unit. This
    /// divergence is currently dormant because all sorted strings are ASCII. If non-ASCII
    /// strings are ever added, prefer this explicit UTF-8 ordinal comparator over bare
    /// `sorted()` to maintain cross-platform parity.
    static func sortedUnique(_ values: [String]) -> [String] {
        Array(Set(values)).sorted { $0.utf8.lexicographicallyPrecedes($1.utf8) }
    }

    /// E020 format version that drops `errorMessage` from the signed bytes. Both platforms'
    /// verifiers must accept this version before either signer emits it.
    static let errorMessageFreeFormatVersion = 3

    /// B252: current signed format. v4 = v3 semantics (errorMessage-free) PLUS canonical
    /// `dohOutcomes` (normalized DoH evidence, formerly display-only and unsigned) and
    /// `perResolverAnswers` (C# twin closed, restoring byte parity for the Swift-only field).
    static let currentFormatVersion = 4

    /// Builds the canonical payload from the wire `ComparisonResult`. `success` and the result are
    /// computed here (at sign time) from the in-memory typed status, so no platform-specific wire
    /// encoding reaches the signed bytes.
    ///
    /// B214/E020: when `formatVersion >= errorMessageFreeFormatVersion`, `errorMessage` is
    /// excluded from outcomes — it is platform-divergent free text (Swift `errorDescription` vs
    /// .NET `ex.Message`) that breaks cross-platform byte parity and display-integrity checks.
    static func build(
        domain: String,
        comparison: ComparisonResult,
        timestampMillis: UInt64,
        expiresAtMillis: UInt64?,
        formatVersion: Int = 2
    ) -> SignedEvidencePayload {
        // B214: drop platform-divergent error text from signed bytes at v3+.
        let includeErrorText = formatVersion < Self.errorMessageFreeFormatVersion
        var outcomes: [ResolverQueryOutcomeCanonical] = []
        outcomes.append(ResolverQueryOutcomeCanonical(
            resolverId: comparison.localServer,
            success: !comparison.localOutcome.didFail,
            errorMessage: includeErrorText ? comparison.localOutcome.errorDescription : nil,
            responseTimeMs: nil))
        for (rid, outcome) in comparison.publicResolverOutcomes {
            outcomes.append(ResolverQueryOutcomeCanonical(
                resolverId: rid,
                success: !outcome.didFail,
                errorMessage: includeErrorText ? outcome.errorDescription : nil,
                responseTimeMs: nil))
        }
        outcomes.sort { $0.resolverId.utf8.lexicographicallyPrecedes($1.resolverId.utf8) }

        // No result before evidence: a degraded or inconclusive comparison never reads "matches".
        // E027: bind isInconclusive and isDegraded into the guard as distinct tokens so the
        // degraded-vs-inconclusive nuance is not flippable on a VALID snapshot. Previously
        // both collapsed to "inconclusive", so an attacker could flip the nuance without
        // invalidating the signature. The load-bearing result is protected; only the nuance
        // was exposed.
        let result: String
        if comparison.isRedirectionDetected {
            result = "diverges"
        } else if comparison.isInconclusive {
            result = "inconclusive"
        } else if comparison.isDegraded {
            result = "degraded"
        } else {
            result = "matches"
        }

        let vantage = sortedUnique(comparison.localOutcome.records.map { $0.rdata })
        let authoritativeRecords = comparison.publicResolverOutcomes.values.flatMap { outcome in
            outcome.records
        }
        let authoritative = sortedUnique(authoritativeRecords.map { $0.rdata })
        // Ph6.1/B098/D100: extract TTLs and types from authoritative records for canonical signing.
        // Records are sorted by rdata (same order as authoritativeSet) for deterministic inclusion.
        // D100 fix: `comparison.publicResolverOutcomes.values` is a Dictionary's value sequence,
        // whose enumeration order Swift explicitly randomizes per process. When multiple resolvers
        // agree on the same rdata but report different (independently-counting-down) TTLs, the
        // representative record picked for that rdata MUST NOT depend on that enumeration order —
        // otherwise the same honest, unmodified evidence resigns differently across runs and later
        // fails its own verification by chance. Within each rdata group we therefore sort candidates
        // by a fully-stable key (ttl ascending, then type, then name as final tie-breakers) and take
        // the first: this deterministically picks the MINIMUM TTL among agreeing resolvers — the
        // conservative, meaningful choice (the answer expires soonest per the freshest-reporting
        // resolver) — regardless of input ordering.
        let authByRdata = Dictionary(grouping: authoritativeRecords, by: { $0.rdata })
            .mapValues { candidates in
                candidates.sorted {
                    if $0.ttl != $1.ttl { return $0.ttl < $1.ttl }
                    if $0.type != $1.type { return $0.type < $1.type }
                    return $0.name.utf8.lexicographicallyPrecedes($1.name.utf8)
                }.first!
            }
            .sorted { $0.key.utf8.lexicographicallyPrecedes($1.key.utf8) }
        let authoritativeTtls: [UInt32]? = authByRdata.isEmpty ? nil : authByRdata.map { UInt32($0.value.ttl) }
        let authoritativeTypes: [UInt16]? = authByRdata.isEmpty ? nil : authByRdata.map { $0.value.type }

        let cmp = ComparisonResultCanonical(
            result: result,
            authoritativeSet: authoritative.isEmpty ? nil : authoritative,
            vantageSet: vantage.isEmpty ? nil : vantage,
            authoritativeTtls: authoritativeTtls,
            authoritativeTypes: authoritativeTypes)

        // B098: per-resolver answer records bind ownership — each resolver's records are
        // signed individually so swapping answers between resolvers invalidates the signature.
        var perResolver: [ResolverAnswerRecordsCanonical] = []
        perResolver.append(ResolverAnswerRecordsCanonical(
            resolverId: comparison.localServer,
            records: comparison.localOutcome.records.map {
                .init(name: $0.name, type: $0.type, ttl: 0, rdata: $0.rdata)
            }))
        for (rid, outcome) in comparison.publicResolverOutcomes {
            perResolver.append(ResolverAnswerRecordsCanonical(
                resolverId: rid,
                records: outcome.records.map {
                    .init(name: $0.name, type: $0.type, ttl: 0, rdata: $0.rdata)
                }))
        }
        perResolver.sort { $0.resolverId.utf8.lexicographicallyPrecedes($1.resolverId.utf8) }

        // B252: canonical DoH evidence at v4+ (v2/v3 signed bytes must stay exactly as before, and
        // pre-v4 evidence never carried dohOutcomes — the display banner marks it UNSIGNED).
        // Mirror of the C# build(): typed status → success, resolverId-ordered, errorMessage
        // subject to the same v3+ suppression as resolver outcomes.
        let dohCanonical: DoHOutcomesCanonical?
        if formatVersion >= Self.currentFormatVersion,
           let doh = comparison.dohOutcomes, !doh.isEmpty {
            dohCanonical = DoHOutcomesCanonical(outcomes: doh
                .map { rid, outcome in
                    ResolverQueryOutcomeCanonical(
                        resolverId: rid,
                        success: !outcome.didFail,
                        errorMessage: includeErrorText ? outcome.errorDescription : nil,
                        responseTimeMs: nil)
                }
                .sorted { $0.resolverId.utf8.lexicographicallyPrecedes($1.resolverId.utf8) })
        } else {
            dohCanonical = nil
        }

        return SignedEvidencePayload(
            formatVersion: formatVersion,
            timestamp: timestampMillis,
            queryId: domain,
            resolverQueryOutcomes: outcomes,
            comparisonResult: cmp,
            expiresAt: expiresAtMillis,
            dohOutcomes: dohCanonical,
            perResolverAnswers: perResolver.isEmpty ? nil : perResolver)
    }

    /// The canonical signed bytes. Deterministic across platforms: camelCase keys are omitted when
    /// their value is nil, `/` is left unescaped, non-ASCII is raw UTF-8, and keys are sorted.
    func canonicalBytes() throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        return try encoder.encode(self)
    }
}

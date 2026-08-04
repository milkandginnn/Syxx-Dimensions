import Foundation
import OpenTelemetryApi

/// Lightweight OTel span helper for SyxxKit operations.
/// Creates spans via the registered TracerProvider (set up by OTelBootstrap in the app layer).
/// When no TracerProvider is registered (e.g. in tests), all operations are no-ops.
public enum SyxxTracer {
    nonisolated(unsafe) private static var _tracer: Tracer?

    /// Lazily obtain the tracer. Returns nil if OTel is not initialized.
    private static var tracer: Tracer? {
        if isDisabled { return nil }        // consent withdrawn — record nothing
        if let t = _tracer { return t }
        let t = OpenTelemetry.instance.tracerProvider
            .get(instrumentationName: "SyxxKit", instrumentationVersion: SyxxConfig.version)
        _tracer = t
        return t
    }

    /// Spans that have had `setOutcome` called on them. Identity-keyed and cleared on `end`,
    /// so this never grows: a span is inserted at most once and removed when it finishes.
    nonisolated(unsafe) private static var _recorded = Set<ObjectIdentifier>()
    private static let _recordedLock = NSLock()

    private static func markRecorded(_ span: Span) {
        _recordedLock.lock(); _recorded.insert(ObjectIdentifier(span as AnyObject)); _recordedLock.unlock()
    }

    /// Stamp `syxx.outcome="unrecorded"` on a span whose body never classified itself, then end it.
    ///
    /// Without this, an uninstrumented span is indistinguishable from a deliberate `failure` or
    /// `untested` — all three are OTel `Unset`, and `Unset` renders as "fine" in every default APM
    /// view. That silently recreates the defect one level up: "nobody measured this" reading as
    /// "nothing wrong". Making the omission visible is what turns the doctrine from aspiration
    /// into something a collector query can enforce.
    private static func finalize(_ span: Span) {
        _recordedLock.lock()
        let didRecord = _recorded.remove(ObjectIdentifier(span as AnyObject)) != nil
        _recordedLock.unlock()
        if !didRecord {
            span.setAttribute(key: "syxx.outcome", value: "unrecorded")
        }
        span.end()
    }

    /// Drop the memoized tracer so the NEXT span is built from whatever provider is registered now.
    ///
    /// The memo above caches forever. That has two consequences worth naming:
    ///   • Tests: whichever test touches a span first pins the whole process to that provider, so
    ///     span assertions are otherwise order-dependent and silently vacuous.
    ///   • Consent withdrawal: re-registering a no-op provider does NOT stop export, because the
    ///     cached tracer still belongs to the old provider and its BatchSpanProcessor is still
    ///     live. Anything opting telemetry out must call this too, or data flows after opt-out.
    public static func resetTracerForTesting() { _tracer = nil }

    /// Hard off-switch. While disabled, no span is created at all — bodies run against `NoOpSpan`.
    ///
    /// This exists because re-registering a no-op TracerProvider does NOT stop telemetry, for two
    /// independent reasons, both verified: `OpenTelemetry.registerTracerProvider` is a ONE-SHOT
    /// singleton (a second call is silently ignored), and even if it took effect the memo above
    /// still holds the previous provider's tracer, whose BatchSpanProcessor is still live. So the
    /// consent-withdrawal path cannot be built on provider swapping — it needs this.
    nonisolated(unsafe) private static var _disabled = false

    /// Consent withdrawn: stop recording immediately. Idempotent.
    public static func disable() {
        _recordedLock.lock(); _disabled = true; _tracer = nil; _recorded.removeAll(); _recordedLock.unlock()
    }

    /// Consent granted again. Nothing from the disabled period is backfilled — data gathered
    /// without consent stays unsent even after consent arrives.
    public static func enable() {
        _recordedLock.lock(); _disabled = false; _tracer = nil; _recordedLock.unlock()
    }

    public static var isDisabled: Bool {
        _recordedLock.lock(); defer { _recordedLock.unlock() }; return _disabled
    }

    /// Create and manage a span for a named operation. The span is ended when the closure returns.
    /// Usage:
    /// ```swift
    /// let result = await SyxxTracer.span("dns.query.udp") { span in
    ///     span.setAttribute(key: "dns.target_class", value: "userSupplied")   // NEVER the name
    ///     return await performQuery()
    /// }
    /// ```
    public static func span<T>(
        _ name: String,
        kind: SpanKind = .internal,
        attributes: [String: AttributeValue] = [:],
        body: (Span) async throws -> T
    ) async rethrows -> T {
        guard let tracer = tracer else {
            // No OTel provider — run body without tracing
            return try await body(NoOpSpan())
        }
        let span = tracer.spanBuilder(spanName: name)
            .setSpanKind(spanKind: kind)
            .startSpan()
        for (key, value) in attributes {
            span.setAttribute(key: key, value: value)
        }
        defer { Self.finalize(span) }
        do {
            // Not throwing is not evidence of success — see the non-throwing variant below.
            // Status comes from `setOutcome`; a body that records nothing stays Unset, not Ok.
            return try await body(span)
        } catch {
            span.status = .error(description: "\(error)")
            throw error
        }
    }

    /// Non-throwing variant for operations that return tri-state results instead of throwing.
    public static func span<T>(
        _ name: String,
        kind: SpanKind = .internal,
        attributes: [String: AttributeValue] = [:],
        body: (Span) async -> T
    ) async -> T {
        guard let tracer = tracer else {
            return await body(NoOpSpan())
        }
        let span = tracer.spanBuilder(spanName: name)
            .setSpanKind(spanKind: kind)
            .startSpan()
        for (key, value) in attributes {
            span.setAttribute(key: key, value: value)
        }
        defer { Self.finalize(span) }
        let result = await body(span)
        // Do NOT assign .ok here. Returning without throwing is not evidence of success:
        // a probe that timed out, a DNS query that returned .timeout, and a synthesis that
        // produced no determination all return normally. Marking them STATUS_CODE_OK is
        // exactly the "reports success having measured nothing" defect this project exists
        // to refuse — reproduced inside the telemetry that is supposed to detect it.
        // Status is derived from the `syxx.outcome` the body sets; absent that, it stays
        // Unset, which is honest (unknown) rather than a false Ok.
        return result
    }

    /// Span variant that classifies the body's RESULT.
    ///
    /// Exists because the honesty-critical span bodies (`dns.query.udp`, `dns.resolve.secure`,
    /// synthesis) have many early returns, each carrying its own tri-state. Threading a
    /// `setOutcome` call through every one of them invites the case that gets missed — and a
    /// missed one is indistinguishable from a deliberate Unset. One classifier per call site
    /// applies to every return path by construction.
    public static func span<T>(
        _ name: String,
        kind: SpanKind = .internal,
        attributes: [String: AttributeValue] = [:],
        classify: @escaping (T) -> (Outcome, String?),
        body: (Span) async throws -> T
    ) async rethrows -> T {
        try await span(name, kind: kind, attributes: attributes) { span in
            let result = try await body(span)
            let (outcome, reason) = classify(result)
            setOutcome(span, outcome, reason: reason,
                       evidence: outcome == .success || outcome == .failure)
            return result
        }
    }

    /// The honest outcome of a measurement, mirroring the engine's own vocabulary so the wire
    /// format cannot drift from the product state: `success`/`failure`/`inconclusive` are
    /// `TriState`'s cases, `untested` is `DeterminativeState.untested`.
    public enum Outcome: String, Sendable {
        case success, failure, inconclusive, untested
        /// The operation ran and returned a value this layer is NOT COMPETENT TO JUDGE.
        /// `ProbeRunner` receives an opaque `Output`; many probe results carry their own
        /// tri-state (`DNSResponse.status == .timeout`, `CanaryResult.evidence == nil`), so
        /// "returned non-nil" is no more evidence of success than "did not throw" was.
        /// Claiming `.success` there just relocates the defect. The probe itself must classify
        /// — until it does, say so rather than guess.
        case unclassified
    }

    /// Record the outcome AND derive the OTel status from it, in one call, so the two can never
    /// disagree. Every SyxxKit span body should call this exactly once.
    ///   success      → ok
    ///   failure      → unset  (a measured negative is the product working, not a telemetry error)
    ///   inconclusive → error  (could-not-measure must be as loud as a crash in the error rate)
    ///   untested     → unset  (nothing ran; `syxx.attempted=false` carries the distinction)
    public static func setOutcome(_ span: Span, _ outcome: Outcome,
                                  reason: String? = nil, evidence: Bool? = nil) {
        markRecorded(span)
        span.setAttribute(key: "syxx.outcome", value: outcome.rawValue)
        span.setAttribute(key: "syxx.attempted", value: outcome != .untested)
        if let evidence { span.setAttribute(key: "syxx.evidence", value: evidence) }
        if let reason { span.setAttribute(key: "syxx.outcome.reason", value: reason) }
        switch outcome {
        case .success:      span.status = .ok
        case .inconclusive: span.status = .error(description: reason ?? "inconclusive")
        // Unset, never a fabricated ok. `.unclassified` is deliberately NOT ok: an unjudged
        // result must not render green in a collector's default view.
        case .failure, .untested, .unclassified: break
        }
    }

    /// Synchronous variant for non-async contexts.
    public static func spanSync<T>(
        _ name: String,
        kind: SpanKind = .internal,
        attributes: [String: AttributeValue] = [:],
        body: (Span) throws -> T
    ) rethrows -> T {
        guard let tracer = tracer else {
            return try body(NoOpSpan())
        }
        let span = tracer.spanBuilder(spanName: name)
            .setSpanKind(spanKind: kind)
            .startSpan()
        for (key, value) in attributes {
            span.setAttribute(key: key, value: value)
        }
        defer { Self.finalize(span) }
        do {
            // As above: a non-throwing return is not evidence of success. Status comes from
            // `setOutcome`; a body that records nothing leaves the span Unset, not Ok.
            return try body(span)
        } catch {
            span.status = .error(description: "\(error)")
            throw error
        }
    }
}

/// Minimal no-op span for when OTel is not initialized.
private final class NoOpSpan: Span {
    var kind: SpanKind { .internal }
    var context: SpanContext { SpanContext.create(traceId: TraceId(), spanId: SpanId(), traceFlags: TraceFlags(), traceState: TraceState()) }
    var isRecording: Bool { false }
    var status: Status {
        get { .unset }
        set {}
    }
    var name: String {
        get { "" }
        set {}
    }
    var description: String { "NoOpSpan" }

    func setAttribute(key: String, value: AttributeValue?) {}
    func setAttributes(_ attributes: [String: AttributeValue]) {}
    func addEvent(name: String) {}
    func addEvent(name: String, timestamp: Date) {}
    func addEvent(name: String, attributes: [String: AttributeValue]) {}
    func addEvent(name: String, attributes: [String: AttributeValue], timestamp: Date) {}
    func addLink(spanContext: SpanContext) {}
    func addLink(spanContext: SpanContext, attributes: [String: AttributeValue]) {}
    func recordException(_ exception: SpanException) {}
    func recordException(_ exception: SpanException, timestamp: Date) {}
    func recordException(_ exception: SpanException, attributes: [String: AttributeValue]) {}
    func recordException(_ exception: SpanException, attributes: [String: AttributeValue], timestamp: Date) {}
    func end() {}
    func end(time: Date) {}
}

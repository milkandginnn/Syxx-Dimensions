using System;
using System.Collections.Generic;

namespace Syxx.Kit
{
    /// <summary>
    /// Collector TLS-transport pinning posture for a leak test (D003). Describes the
    /// <em>transport identity</em> check on the collector's HTTPS endpoint — distinct from
    /// the Ed25519 attestation-signature check (<see cref="LeakTestResult.SignatureValid"/>).
    /// </summary>
    public enum CollectorTransportPinning
    {
        /// <summary>SPKI pins were configured and the collector's leaf certificate matched one.</summary>
        Enforced,
        /// <summary>No SPKI pins were configured; only standard system TLS validation was applied.</summary>
        NotConfigured,
        /// <summary>
        /// SPKI pins were configured but the collector's leaf certificate did NOT match
        /// (or the chain failed PKI validation). The transport was refused, fail-closed.
        /// </summary>
        Failed
    }

    /// <summary>The collector-verified outcome of a DNS leak test.</summary>
    public enum LeakResult
    {
        /// <summary>The collector confirmed at least one query reached an unexpected resolver.</summary>
        Confirmed,
        /// <summary>The collector observed no queries for this token (no leak detected).</summary>
        NotDetected,
        /// <summary>The test could not complete or be verified (network error, no collector, etc.).</summary>
        Inconclusive,
        /// <summary>P-5 D1: the result payload is structurally impossible (malformed, missing fields).</summary>
        Invalid,
        /// <summary>P-5 D1: an execution error prevented the test from completing.</summary>
        Error
    }

    /// <summary>
    /// Which collector request could not be completed. The step matters to honesty: a failure at
    /// <see cref="Token"/> means no DNS query was ever dispatched, while a failure at
    /// <see cref="Verify"/> means the queries WERE sent but their result could not be collected.
    /// Neither is evidence of no leak. Twin of macOS <c>CollectorStep</c>.
    /// </summary>
    public enum CollectorStep
    {
        /// <summary><c>POST /api/leak/token</c> — before any DNS query is dispatched.</summary>
        Token,
        /// <summary><c>GET /api/leak/verify</c> — after the DNS queries were dispatched.</summary>
        Verify
    }

    /// <summary>
    /// Why a collector-confirmed leak test produced no evidence. Twin of macOS
    /// <c>CollectorFailure</c>.
    ///
    /// The collector's HTTP status is a DISTINCT state, not noise: "you are not licensed" (402),
    /// "you asked too often" (429), "your credentials were rejected" (401/403) and "the collector
    /// fell over" (5xx) demand different user outcomes. This client used to fold every one of them
    /// into a single <c>HttpRequestException("HTTP nnn")</c> that nothing ever classified, so a
    /// working paywall read exactly like a collapsed server.
    ///
    /// This is never a leak/no-leak signal. Every kind means the same thing about DNS: nothing was
    /// observed, therefore nothing can be concluded.
    /// </summary>
    public enum CollectorFailureKind
    {
        /// <summary>HTTP 402 — the collector requires a Syxx Pro entitlement for this route.
        /// Nothing broke; the feature is gated. A correct answer from a correct server.</summary>
        EntitlementRequired,
        /// <summary>HTTP 401 / 403 — the collector rejected the supplied credentials.</summary>
        AuthenticationFailed,
        /// <summary>HTTP 429 — the collector is rate limiting this caller.</summary>
        RateLimited,
        /// <summary>HTTP 5xx — the collector reported a server-side error.</summary>
        ServerError,
        /// <summary>Any other non-200 status, carried verbatim; no cause is inferred.</summary>
        UnexpectedStatus,
        /// <summary>The collector answered 200 but the body could not be decoded.</summary>
        MalformedResponse,
        /// <summary>D003 — the collector's TLS identity could not be verified.</summary>
        CollectorIdentityUnverified,
        /// <summary>The request never reached the collector (timeout, offline, reset).</summary>
        TransportFailure
    }

    /// <summary>
    /// A classified collector failure and the HTTP status that produced it. The status is preserved
    /// so nothing downstream has to guess. Twin of macOS <c>CollectorFailure</c>.
    /// </summary>
    public sealed class CollectorFailure : IEquatable<CollectorFailure>
    {
        public CollectorFailureKind Kind { get; }
        /// <summary>
        /// The HTTP status the collector returned, when the failure was an HTTP response at all.
        /// <c>null</c> for transport, identity and decode failures, where no status exists to report.
        /// </summary>
        public int? HttpStatus { get; }

        public CollectorFailure(CollectorFailureKind kind, int? httpStatus = null)
        {
            Kind = kind;
            HttpStatus = httpStatus;
        }

        /// <summary>
        /// True only for HTTP 402: the request was refused because the caller lacks a Syxx Pro
        /// entitlement. Surfaces should offer an upgrade path here, NOT an error state — nothing
        /// about the product failed.
        /// </summary>
        public bool RequiresEntitlement => Kind == CollectorFailureKind.EntitlementRequired;

        // MARK: - User-visible copy

        /// <summary>
        /// Plain-English reason for this failure at <paramref name="step"/>.
        ///
        /// PENDING OWNER APPROVAL (proposed 2026-07-29) — every sentence below reaches the screen
        /// and the CLI, and is byte-identical to the macOS twin. The one exception is
        /// <see cref="CollectorPinning.PinFailureReason"/>, approved 2026-07-11 and reused verbatim.
        ///
        /// Two rules hold every string here:
        /// 1. No cause is asserted that the code has not established. An unrecognised status
        ///    reports the status, not a guess about what it meant.
        /// 2. Every sentence closes the honesty gap the status opens: the reader is told that no
        ///    leak evidence exists, so no case can be misread as "no leak detected".
        /// </summary>
        public string Reason(CollectorStep step)
        {
            if (Kind == CollectorFailureKind.CollectorIdentityUnverified)
            {
                // Approved 2026-07-11; already self-contained, so it takes no step suffix.
                return CollectorPinning.PinFailureReason;
            }
            return Cause + " " + Consequence(step);
        }

        /// <summary>The half of the sentence that states WHAT the collector did.</summary>
        private string Cause => Kind switch
        {
            CollectorFailureKind.EntitlementRequired =>
                "A Syxx Pro license is required for the collector-confirmed leak test.",
            CollectorFailureKind.AuthenticationFailed =>
                $"The collector rejected the credentials supplied (HTTP {HttpStatus}).",
            CollectorFailureKind.RateLimited =>
                $"The collector is rate limiting requests from this device (HTTP {HttpStatus}).",
            CollectorFailureKind.ServerError =>
                $"The collector reported a server error (HTTP {HttpStatus}).",
            // Deliberately states the status and nothing else: the code has established no cause.
            CollectorFailureKind.UnexpectedStatus =>
                $"The collector request failed with HTTP status {HttpStatus}.",
            CollectorFailureKind.MalformedResponse =>
                "The collector's response could not be read.",
            CollectorFailureKind.TransportFailure =>
                "The collector could not be reached.",
            _ => CollectorPinning.PinFailureReason
        };

        /// <summary>
        /// The half of the sentence that states what it means for the evidence. This is the part
        /// that stops any failure from reading as a clean result.
        /// </summary>
        private static string Consequence(CollectorStep step) => step == CollectorStep.Token
            ? "No leak test was performed, so nothing is known about your DNS resolvers."
            : "The test queries were sent but their result could not be collected, so nothing is known about your DNS resolvers.";

        public bool Equals(CollectorFailure? other) =>
            other is not null && Kind == other.Kind && HttpStatus == other.HttpStatus;
        public override bool Equals(object? obj) => Equals(obj as CollectorFailure);
        public override int GetHashCode() => HashCode.Combine(Kind, HttpStatus);
        public override string ToString() =>
            HttpStatus is null ? Kind.ToString() : $"{Kind}({HttpStatus})";
    }

    // E049: detailed signature verification outcome, replacing the collapsed bool that
    // previously made unsigned, malformed, tampered, and crypto-error evidence indistinguishable.
    public enum LeakSignatureVerification
    {
        /// <summary>The Ed25519 signature is cryptographically valid for the attested payload.</summary>
        Valid,
        /// <summary>No signature data was provided (collector sent no signature).</summary>
        NoSignature,
        /// <summary>The signature failed cryptographic verification — evidence was tampered.</summary>
        InvalidSignature,
        /// <summary>Signature or key data were malformed (bad base64, wrong key length, etc.).</summary>
        MalformedData
    }

    /// <summary>Full result returned by <see cref="LeakDetector.RunCollectorTestAsync"/>.</summary>
    public class LeakTestResult
    {
        public LeakResult Result { get; set; }
        /// <summary>Resolver IPs the collector recorded as having forwarded the test query.</summary>
        public List<string> ObservedResolvers { get; set; } = new List<string>();
        /// <summary>
        /// Ed25519 signature produced by the collector — tamper-evident evidence.
        /// <c>null</c> when the collector was not reachable or did not return a signature.
        /// </summary>
        public string? Signature { get; set; }
        /// <summary>The collector's public key (base64 raw 32-byte Ed25519 public key).</summary>
        public string? PublicKey { get; set; }
        /// <summary>Opaque key identifier (first 8 bytes of public key, base64-encoded).</summary>
        public string? KeyId { get; set; }
        /// <summary>Canonical JSON payload bytes that were signed (base64-encoded).</summary>
        public string? SignedPayload { get; set; }
        /// <summary>Whether the signature (if present) passed verification with the supplied key.</summary>
        public bool? SignatureValid { get; set; }
        // E049: detailed verification outcome distinguishing no-signature, malformed, invalid,
        // and valid states — previously collapsed into SignatureValid's bool?. Kept alongside
        // SignatureValid so existing consumers continue to work.
        public LeakSignatureVerification? SignatureVerification { get; set; }
        public string? ErrorReason { get; set; }
        /// <summary>
        /// Collector TLS-transport pinning posture for this run (D003). A <see
        /// cref="CollectorTransportPinning.Failed"/> value means the collector's identity could
        /// not be verified, so the result is forced to <see cref="LeakResult.Inconclusive"/> —
        /// a leak/no-leak result cannot be trusted.
        /// </summary>
        public CollectorTransportPinning TransportPinning { get; set; } = CollectorTransportPinning.NotConfigured;
        /// <summary>
        /// D011: whether the signing public key was present in the configured trust set.
        /// <c>null</c> — no trust set was configured (transparently untracked),
        /// <c>true</c>  — the signer's key is a member of the trust set,
        /// <c>false</c> — the signer's key is NOT in the trust set (signature is crypto-valid
        /// but the signer is not trusted). Never <c>true</c> when <see cref="SignatureValid"/>
        /// is not also <c>true</c>.
        /// </summary>
        public bool? SignatureKeyTrusted { get; set; }
        /// <summary>
        /// Why the collector produced no evidence, when it produced none. <c>null</c> when the
        /// collector answered the request (whatever the leak outcome was), and <c>null</c> when the
        /// run never reached the collector at all (no resolvers configured, every dispatch failed).
        ///
        /// Read this INSTEAD OF pattern-matching <see cref="ErrorReason"/>: it is the structured,
        /// testable form of the same fact, and it is the only thing that lets a surface tell
        /// "requires Syxx Pro" apart from "the collector fell over". Twin of macOS
        /// <c>LeakTestResult.collectorFailure</c>.
        /// </summary>
        public CollectorFailure? CollectorFailure { get; set; }
    }
}

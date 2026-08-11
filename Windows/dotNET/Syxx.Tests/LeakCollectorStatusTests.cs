using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    /// <summary>
    /// A throwaway plaintext HTTP/1.1 server that answers each request path with a caller-chosen
    /// status and body. Exists so the leak-test status branches are exercised through the REAL
    /// HttpClient -> HTTP response -> classification path, not through a stubbed classifier.
    /// Purely a test fixture; loopback only. Twin of the macOS LocalStatusHTTPServer fixture.
    /// </summary>
    internal sealed class LocalStatusHttpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<string, (int Status, string Body)> _respond;
        private volatile bool _running = true;

        public int Port { get; }

        private LocalStatusHttpServer(TcpListener listener, int port,
                                      Func<string, (int, string)> respond)
        {
            _listener = listener;
            Port = port;
            _respond = respond;
        }

        public static LocalStatusHttpServer Start(Func<string, (int Status, string Body)> respond)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = new LocalStatusHttpServer(listener, port, respond);
            new Thread(server.AcceptLoop) { IsBackground = true }.Start();
            return server;
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { return; }
                using (client)
                {
                    try { Handle(client); }
                    catch { /* fixture: a dropped client is not a test failure */ }
                }
            }
        }

        private void Handle(TcpClient client)
        {
            var stream = client.GetStream();
            // Read the request head. Content-Length is 0 on every route this fixture serves, so the
            // blank line that ends the headers ends the request.
            var head = new StringBuilder();
            var buffer = new byte[2048];
            while (!head.ToString().Contains("\r\n\r\n"))
            {
                int n = stream.Read(buffer, 0, buffer.Length);
                if (n <= 0) break;
                head.Append(Encoding.UTF8.GetString(buffer, 0, n));
            }
            var parts = head.ToString().Split(' ');
            string path = parts.Length > 1 ? parts[1] : "/";
            var (status, body) = _respond(path);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            byte[] headBytes = Encoding.UTF8.GetBytes(
                $"HTTP/1.1 {status} Status\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Connection: close\r\n\r\n");
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        public void Dispose()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
        }
    }

    /// <summary>
    /// The collector's HTTP status is a DISTINCT state (DIAG-glutton-2026-07-29).
    ///
    /// Before this fix, Syxx.Kit threw <c>HttpRequestException($"HTTP {code}")</c> for every non-200
    /// and nothing ever classified it: a working paywall (402), a rate limit (429), rejected
    /// credentials (403) and a collapsed collector (5xx) all produced one identical Inconclusive
    /// carrying one identical string. These tests are the twin of the macOS testCollector* /
    /// testLive* suite in SyxxKitTests.swift and must stay behaviourally identical to it.
    /// </summary>
    public class LeakCollectorStatusTests
    {
        // Mirrors the macOS assertNotBridgeDump helper. On .NET an exception message is already
        // plain text, so Windows never had the macOS bridge-dump defect; the SHAPE of the check is
        // kept identical across platforms so a future regression on either side is caught the same way.
        private static void AssertNotBridgeDump(string s, string message)
        {
            Assert.False(s.Contains("operation couldn't be completed"), message);
            Assert.False(s.Contains("operation couldn’t be completed"), message);
            Assert.False(s.Contains("unknown context at"), message);
            Assert.False(Regex.IsMatch(s, @"\$[0-9a-fA-F]{4,}"), message);
            Assert.False(
                Regex.IsMatch(s, @"[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)+ error -?[0-9]+"),
                message);
        }

        /// The core of the defect: 402 (paywall), 401/403 (auth), 429 (rate limit) and 5xx
        /// (collector down) are DIFFERENT answers and must not classify alike.
        [Fact]
        public void ClassifyStatus_KeepsDistinctStatesDistinct()
        {
            Assert.Equal(CollectorFailureKind.EntitlementRequired, LeakDetector.ClassifyStatus(402).Kind);
            Assert.Equal(CollectorFailureKind.AuthenticationFailed, LeakDetector.ClassifyStatus(401).Kind);
            Assert.Equal(CollectorFailureKind.AuthenticationFailed, LeakDetector.ClassifyStatus(403).Kind);
            Assert.Equal(CollectorFailureKind.RateLimited, LeakDetector.ClassifyStatus(429).Kind);
            Assert.Equal(CollectorFailureKind.ServerError, LeakDetector.ClassifyStatus(500).Kind);
            Assert.Equal(CollectorFailureKind.ServerError, LeakDetector.ClassifyStatus(503).Kind);
            Assert.Equal(CollectorFailureKind.ServerError, LeakDetector.ClassifyStatus(599).Kind);

            // Adversarial / unrecognised: the status is preserved verbatim and NO cause is asserted.
            // A collector, or an on-path proxy pretending to be one, can return anything at all.
            foreach (int odd in new[] { 404, 418, 302, 600, 0 })
            {
                Assert.True(LeakDetector.ClassifyStatus(odd).Kind == CollectorFailureKind.UnexpectedStatus,
                            $"HTTP {odd} must classify as UnexpectedStatus, not be guessed at");
                Assert.Equal(odd, LeakDetector.ClassifyStatus(odd).HttpStatus);
            }

            // No two of these may be equal, or the collapse is back.
            var all = new[] { 402, 403, 429, 503, 418 }.Select(LeakDetector.ClassifyStatus).ToList();
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                    Assert.False(all[i].Equals(all[j]),
                                 $"distinct collector states collapsed into one: {all[i]}");
        }

        /// The status must survive to the caller. It used to exist only inside an English message.
        [Fact]
        public void CollectorFailure_PreservesTheHttpStatus()
        {
            Assert.Equal(402, LeakDetector.ClassifyStatus(402).HttpStatus);
            Assert.Equal(429, LeakDetector.ClassifyStatus(429).HttpStatus);
            Assert.Equal(507, LeakDetector.ClassifyStatus(507).HttpStatus);

            // No status exists for these, so none is invented.
            Assert.Null(new CollectorFailure(CollectorFailureKind.TransportFailure).HttpStatus);
            Assert.Null(new CollectorFailure(CollectorFailureKind.MalformedResponse).HttpStatus);
            Assert.Null(new CollectorFailure(CollectorFailureKind.CollectorIdentityUnverified).HttpStatus);

            Assert.True(LeakDetector.ClassifyStatus(402).RequiresEntitlement);
            foreach (int other in new[] { 403, 429, 500, 418 })
                Assert.False(LeakDetector.ClassifyStatus(other).RequiresEntitlement,
                             $"HTTP {other} is not a paywall and must not offer an upgrade path");
        }

        /// No collector failure may ever resolve to a clean result. Absence of observation is not
        /// evidence of absence: a 402 means the test never ran, not that DNS is fine.
        [Fact]
        public void CollectorFailure_NeverResolvesToACleanResult()
        {
            var every = new[]
            {
                new CollectorFailure(CollectorFailureKind.EntitlementRequired, 402),
                new CollectorFailure(CollectorFailureKind.AuthenticationFailed, 403),
                new CollectorFailure(CollectorFailureKind.RateLimited, 429),
                new CollectorFailure(CollectorFailureKind.ServerError, 500),
                new CollectorFailure(CollectorFailureKind.UnexpectedStatus, 418),
                new CollectorFailure(CollectorFailureKind.MalformedResponse),
                new CollectorFailure(CollectorFailureKind.CollectorIdentityUnverified),
                new CollectorFailure(CollectorFailureKind.TransportFailure)
            };
            foreach (var failure in every)
            {
                var result = LeakDetector.ResultFor(failure);
                Assert.True(result != LeakResult.NotDetected,
                            $"{failure} must never read as 'no leak detected'");
                Assert.True(result != LeakResult.Confirmed,
                            $"{failure} attests no resolver and cannot confirm a leak");
            }
            // A paywall is a correct answer from a correct server, not a broken product.
            Assert.True(
                LeakDetector.ResultFor(new CollectorFailure(CollectorFailureKind.EntitlementRequired, 402))
                    == LeakResult.Inconclusive,
                "HTTP 402 is the intended paywall; rendering it as an execution error tells the user "
                + "their product is faulty when it is not");
            Assert.Equal(LeakResult.Error,
                LeakDetector.ResultFor(new CollectorFailure(CollectorFailureKind.ServerError, 500)));
            Assert.Equal(LeakResult.Error,
                LeakDetector.ResultFor(new CollectorFailure(CollectorFailureKind.TransportFailure)));
        }

        /// Classification of a thrown exception. The transport-identity check must WIN: a pin/PKI
        /// refusal downgraded to a generic "the collector could not be reached" would hide an
        /// on-path impostor behind a message that reads like a flaky network, and would lose the
        /// fail-closed CollectorTransportPinning.Failed posture with it. Twin of the macOS
        /// testCollectorErrorClassificationPrefersIdentityFailure; on macOS this branch is also
        /// covered by a live TLS handshake (testCollectorPinMismatchFailsClosedAgainstLiveTLS),
        /// which Windows has no equivalent of, so this unit test is the only guard here.
        [Fact]
        public void Classify_PrefersTransportIdentityOverEverythingElse()
        {
            // Exactly the shape .NET produces when the pinning callback refuses the certificate.
            var pinRefusal = new HttpRequestException(
                "The SSL connection could not be established.",
                new AuthenticationException("The remote certificate was rejected."));
            Assert.Equal(CollectorFailureKind.CollectorIdentityUnverified,
                         LeakDetector.Classify(pinRefusal).Kind);
            Assert.Equal(CollectorPinning.PinFailureReason,
                         LeakDetector.Classify(pinRefusal).Reason(CollectorStep.Token));

            // An unwrapped AuthenticationException must be recognised just the same.
            Assert.Equal(CollectorFailureKind.CollectorIdentityUnverified,
                         LeakDetector.Classify(new AuthenticationException("rejected")).Kind);

            // Everything else keeps its own classification and must NOT be promoted to an identity
            // failure, or a routine timeout would accuse the collector of impersonation.
            Assert.Equal(CollectorFailureKind.TransportFailure,
                         LeakDetector.Classify(new TaskCanceledException("timeout")).Kind);
            Assert.Equal(CollectorFailureKind.TransportFailure,
                         LeakDetector.Classify(new HttpRequestException("connection refused")).Kind);
            Assert.Equal(CollectorFailureKind.EntitlementRequired,
                         LeakDetector.Classify(new LeakDetector.CollectorStatusException(402)).Kind);
            Assert.Equal(CollectorFailureKind.RateLimited,
                         LeakDetector.Classify(new LeakDetector.CollectorStatusException(429)).Kind);
            Assert.Equal(CollectorFailureKind.MalformedResponse,
                         LeakDetector.Classify(new LeakDetector.CollectorMalformedResponseException()).Kind);
            // A status exception wrapped by a transport exception still yields its status.
            Assert.Equal(503, LeakDetector.Classify(
                new HttpRequestException("outer", new LeakDetector.CollectorStatusException(503))).HttpStatus);
        }

        /// The reason a user reads must name a real cause or a real status, never boilerplate, and
        /// must always close the honesty gap: no evidence was gathered.
        [Fact]
        public void CollectorFailure_ReasonIsHonestCopy()
        {
            foreach (var step in new[] { CollectorStep.Token, CollectorStep.Verify })
            {
                string unrecognised =
                    new CollectorFailure(CollectorFailureKind.UnexpectedStatus, 418).Reason(step);
                AssertNotBridgeDump(unrecognised, $"unexpectedStatus reason is boilerplate: {unrecognised}");
                Assert.True(unrecognised.Contains("418"),
                            $"an unrecognised status must be reported verbatim, not guessed at: {unrecognised}");

                var failures = new[]
                {
                    new CollectorFailure(CollectorFailureKind.EntitlementRequired, 402),
                    new CollectorFailure(CollectorFailureKind.AuthenticationFailed, 403),
                    new CollectorFailure(CollectorFailureKind.RateLimited, 429),
                    new CollectorFailure(CollectorFailureKind.ServerError, 500),
                    new CollectorFailure(CollectorFailureKind.UnexpectedStatus, 418),
                    new CollectorFailure(CollectorFailureKind.MalformedResponse),
                    new CollectorFailure(CollectorFailureKind.TransportFailure)
                };
                foreach (var failure in failures)
                {
                    string reason = failure.Reason(step);
                    AssertNotBridgeDump(reason, $"{failure} reason is boilerplate: {reason}");
                    Assert.True(reason.Contains("nothing is known about your DNS resolvers"),
                                $"{failure} must state that no evidence exists, or it can be read as "
                                + $"a clean result: {reason}");
                }
            }

            // The two steps are not interchangeable: at Token no query was ever sent; at Verify the
            // queries WERE sent and only their result is missing. Saying "no test was performed"
            // after dispatching real queries would be false.
            var serverError = new CollectorFailure(CollectorFailureKind.ServerError, 500);
            string token = serverError.Reason(CollectorStep.Token);
            string verify = serverError.Reason(CollectorStep.Verify);
            Assert.False(token == verify, "the token and verify steps must not share one sentence");
            Assert.True(token.Contains("No leak test was performed"), token);
            Assert.True(verify.Contains("test queries were sent"), verify);

            // The pin-failure reason is owner-approved copy (2026-07-11) and passes through intact.
            Assert.Equal(CollectorPinning.PinFailureReason,
                new CollectorFailure(CollectorFailureKind.CollectorIdentityUnverified).Reason(CollectorStep.Token));
        }

        /// CROSS-PLATFORM PARITY. These strings are the user-visible copy, asserted verbatim against
        /// the macOS twin (macOS/Sources/SyxxKit/LeakDetector.swift, CollectorFailure.cause /
        /// consequence). A silent divergence means two customers on two platforms get two different
        /// explanations of the same server answer.
        [Fact]
        public void CollectorFailure_CopyIsByteIdenticalToMacOS()
        {
            Assert.Equal(
                "A Syxx Pro license is required for the collector-confirmed leak test. "
                + "No leak test was performed, so nothing is known about your DNS resolvers.",
                new CollectorFailure(CollectorFailureKind.EntitlementRequired, 402).Reason(CollectorStep.Token));
            Assert.Equal(
                "The collector rejected the credentials supplied (HTTP 403). "
                + "No leak test was performed, so nothing is known about your DNS resolvers.",
                new CollectorFailure(CollectorFailureKind.AuthenticationFailed, 403).Reason(CollectorStep.Token));
            Assert.Equal(
                "The collector is rate limiting requests from this device (HTTP 429). "
                + "No leak test was performed, so nothing is known about your DNS resolvers.",
                new CollectorFailure(CollectorFailureKind.RateLimited, 429).Reason(CollectorStep.Token));
            Assert.Equal(
                "The collector reported a server error (HTTP 503). "
                + "No leak test was performed, so nothing is known about your DNS resolvers.",
                new CollectorFailure(CollectorFailureKind.ServerError, 503).Reason(CollectorStep.Token));
            Assert.Equal(
                "The collector request failed with HTTP status 418. "
                + "No leak test was performed, so nothing is known about your DNS resolvers.",
                new CollectorFailure(CollectorFailureKind.UnexpectedStatus, 418).Reason(CollectorStep.Token));
            Assert.Equal(
                "The collector's response could not be read. "
                + "No leak test was performed, so nothing is known about your DNS resolvers.",
                new CollectorFailure(CollectorFailureKind.MalformedResponse).Reason(CollectorStep.Token));
            Assert.Equal(
                "The collector could not be reached. The test queries were sent but their result "
                + "could not be collected, so nothing is known about your DNS resolvers.",
                new CollectorFailure(CollectorFailureKind.TransportFailure).Reason(CollectorStep.Verify));
        }

        // MARK: Real HTTP runs through the full pipeline

        private static async Task<LeakTestResult> RunAgainstLocalCollector(
            int tokenStatus, string tokenBody = "", int verifyStatus = 200,
            List<string>? servers = null)
        {
            using var server = LocalStatusHttpServer.Start(path =>
                path.StartsWith("/api/leak/token") ? (tokenStatus, tokenBody) : (verifyStatus, ""));
            return await LeakDetector.RunCollectorTestAsync(
                "leak.syxx.app",
                servers ?? new List<string> { "127.0.0.1" },
                $"http://127.0.0.1:{server.Port}",
                pollDelayMs: 1);
        }

        /// REAL RUN, token step: a live HTTP 402 from a local collector, through HttpClient, must
        /// surface as an entitlement gate. This is the exact behaviour of the production collector
        /// (POST https://syxx.app/api/leak/token returns 402 to an unlicensed caller).
        [Fact]
        public async Task Live402_SurfacesAsEntitlementRequiredNotAnOpaqueError()
        {
            var result = await RunAgainstLocalCollector(tokenStatus: 402);

            Assert.NotNull(result.CollectorFailure);
            Assert.Equal(CollectorFailureKind.EntitlementRequired, result.CollectorFailure!.Kind);
            Assert.Equal(402, result.CollectorFailure.HttpStatus);
            Assert.True(result.CollectorFailure.RequiresEntitlement);
            Assert.True(result.Result == LeakResult.Inconclusive,
                        $"a working paywall must not render as a failure of the product; got {result.Result}");
            Assert.True(result.Result != LeakResult.NotDetected);
            Assert.Empty(result.ObservedResolvers);
            Assert.Null(result.Signature);
            Assert.NotNull(result.ErrorReason);
            AssertNotBridgeDump(result.ErrorReason!, $"the 402 reason is boilerplate: {result.ErrorReason}");
            Assert.Contains("Syxx Pro", result.ErrorReason!);
        }

        /// REAL RUN: rate limit, server error and an unrecognised status must reach the caller as
        /// three different things. Before the fix all three were one Inconclusive with one string.
        [Fact]
        public async Task LiveNon200Statuses_DoNotCollapseIntoOneState()
        {
            var rateLimited = await RunAgainstLocalCollector(429);
            var serverDown = await RunAgainstLocalCollector(503);
            var teapot = await RunAgainstLocalCollector(418);
            var paywall = await RunAgainstLocalCollector(402);
            var forbidden = await RunAgainstLocalCollector(403);

            Assert.Equal(new CollectorFailure(CollectorFailureKind.RateLimited, 429), rateLimited.CollectorFailure);
            Assert.Equal(new CollectorFailure(CollectorFailureKind.ServerError, 503), serverDown.CollectorFailure);
            Assert.Equal(new CollectorFailure(CollectorFailureKind.UnexpectedStatus, 418), teapot.CollectorFailure);
            Assert.Equal(new CollectorFailure(CollectorFailureKind.AuthenticationFailed, 403), forbidden.CollectorFailure);

            var reasons = new[] { rateLimited, serverDown, teapot, paywall, forbidden }
                .Select(r => r.ErrorReason ?? "").ToList();
            Assert.True(reasons.Distinct().Count() == reasons.Count,
                        "distinct collector states produced identical user-facing text: "
                        + string.Join(" | ", reasons));
            Assert.Contains("418", teapot.ErrorReason!);

            foreach (var r in new[] { rateLimited, serverDown, teapot, forbidden })
            {
                Assert.False(r.CollectorFailure!.RequiresEntitlement);
                Assert.Equal(LeakResult.Error, r.Result);
                Assert.True(r.Result != LeakResult.NotDetected);
            }
        }

        /// REAL RUN: a 200 with an undecodable body is NOT a status failure and must not be reported
        /// as one. It is also not a clean result.
        [Fact]
        public async Task LiveMalformedTokenBody_IsDistinctFromAStatusFailure()
        {
            var result = await RunAgainstLocalCollector(tokenStatus: 200, tokenBody: "not json at all");
            Assert.Equal(CollectorFailureKind.MalformedResponse, result.CollectorFailure!.Kind);
            Assert.True(result.CollectorFailure.HttpStatus is null,
                        "no status failure occurred; the collector returned 200");
            Assert.Equal(LeakResult.Error, result.Result);
            Assert.True(result.Result != LeakResult.NotDetected);
            AssertNotBridgeDump(result.ErrorReason!, "malformed-body reason is boilerplate");
        }

        /// PARITY with macOS: a 2xx that is not 200 carries no token and must fail on BOTH platforms.
        /// Windows previously accepted any IsSuccessStatusCode here, so a 204 would have proceeded
        /// to a decode failure on Windows while macOS reported the status.
        [Fact]
        public async Task Live204NoContent_IsNotTreatedAsSuccess()
        {
            var result = await RunAgainstLocalCollector(tokenStatus: 204);
            Assert.Equal(new CollectorFailure(CollectorFailureKind.UnexpectedStatus, 204),
                         result.CollectorFailure);
            Assert.True(result.Result != LeakResult.NotDetected);
            Assert.Contains("204", result.ErrorReason!);
        }

        /// REAL RUN, verify step: VerifyTokenAsync had the identical collapse. A 402 on verify, after
        /// the DNS queries have really been dispatched, must classify the same way AND say so
        /// differently, because queries WERE sent.
        ///
        /// NETWORK-DEPENDENT. Reaching the verify step requires at least one DNS dispatch to
        /// actually succeed, so this test sends a real query to a public resolver. When no resolver
        /// answers, the run stops at the dispatch guard and there is nothing to assert; the test
        /// returns early rather than asserting a state it never reached. That is a documented
        /// coverage gap on a network-less host, not a pass. Same substitute-skip convention as
        /// DNSCaptureAttributionTests.cs:96.
        [Fact]
        public async Task LiveVerifyStepStatus_IsDistinguishedToo()
        {
            var probe = await LeakDetector.TriggerQueriesAsync(
                LeakDetector.GenerateTestSubdomain(), new List<string> { "1.1.1.1" });
            if (!probe.Values.Any(sent => sent)) return;   // no reachable resolver; verify unreachable

            const string tokenBody =
                "{\"token\":\"t-verify-step\",\"subdomain\":\"id-verifystep.leak.syxx.app\"}";
            var result = await RunAgainstLocalCollector(
                tokenStatus: 200, tokenBody: tokenBody, verifyStatus: 402,
                servers: new List<string> { "1.1.1.1" });

            Assert.NotNull(result.CollectorFailure);
            Assert.Equal(CollectorFailureKind.EntitlementRequired, result.CollectorFailure!.Kind);
            Assert.Equal(LeakResult.Inconclusive, result.Result);
            Assert.True(result.Result != LeakResult.NotDetected);
            AssertNotBridgeDump(result.ErrorReason!, "the verify-step reason is boilerplate");
            Assert.True(result.ErrorReason!.Contains("test queries were sent"),
                        "the verify step dispatched real queries and must not claim no test ran: "
                        + result.ErrorReason);
        }
    }
}

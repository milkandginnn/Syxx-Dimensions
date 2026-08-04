using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Syxx.Kit;

// ──────────────────────────────────────────────────────────────────────────
// MARK: CaptureEngine — pure packet-capture core extracted from the cage CLI
// ──────────────────────────────────────────────────────────────────────────
//
// AUGUST 2026 — extracted from `Syxx.Commands.CageCommand` as the first checkpoint of the
// daemon refactor. The cage CLI command (and the macOS / Windows GUI tail paths) called the
// raw-socket open + per-packet parse-build loop directly. That logic is the SHARED CORE every
// capture surface needs (CLI, daemon, GUI client of daemon, animated glyph subscriber of
// daemon events). It now lives here, with NO console I/O and NO file I/O — consumers iterate
// an `IAsyncEnumerable<DNSCaptureRecord>` and decide how to render / persist each record.
//
// Behavior-preserving extraction: the original `CageCommand` ran two concurrent `CaptureLoop`
// tasks (v4 + v6) writing into a shared, lock-protected stream. The engine preserves that
// concurrency via an unbounded `Channel<DNSCaptureRecord>` — neither socket blocks the other,
// and a single consumer drains both in arrival order. Per-record CPU work (ParseIPUDPHeader,
// ParseDNSPacket, D107 local-side attribution, GetProcessInfo) is byte-identical to the
// original CLI; only the I/O shell moved out.
//
// HONESTY invariants preserved (and now testable in isolation):
//   E047 — the consumer, not the engine, decides when to emit the "first packet" success
//          signal. The engine yields records; the consumer counts.
//   D107 — every record's `Process` field is set to `ProcessInfoReport.Unknown` when the
//          packet's local-side IP is not in this host's unicast set, EXACTLY as the original
//          did. Promiscuous-mode captures of a neighbor's traffic are not attributed to a
//          local process that happens to hold the same ephemeral port.
//   C041 — a capture-loop abort is surfaced to the consumer via `Outcome` (Aborted carries
//          the failure reason) so an aborted capture is never rendered as ordinary partial
//          evidence. The other loop is allowed to finish; the consumer's await foreach exits
//          when the channel completes (both loops done).
//   E075 — platform-aware raw-socket binding (Windows SIO_RCVALL + HeaderIncluded for v4 only;
//          Linux/macOS SOCK_RAW inherits the IP header by default). Carried verbatim from the
//          original `OpenRawSocket` / `FindRoutedAddress` helpers.
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Why a raw-socket open produced fewer than two sockets. <c>BothFailed</c> halts the capture
/// (parity with the original CLI's hard error). <c>V4Only</c> / <c>V6Only</c> allow a degraded
/// capture to proceed — the consumer surfaces the missing family honestly (F029 parity).
/// </summary>
public enum OpenSocketFailureKind { BothFailed, V4Only, V6Only }

/// <summary>Structured open-socket failure — the caller (CLI / daemon / GUI) formats it without
/// having to re-derive it from a caught exception. Preserves the exact byte shape of the
/// original CLI's per-family error message (<c>{Type}: {Message}</c>).</summary>
public sealed record OpenSocketFailure(OpenSocketFailureKind Kind, string Message);

/// <summary>
/// Result of opening the v4 + v6 raw sockets. Either both are opened, one is missing (degraded),
/// or both failed. The consumer decides how to surface each state — the engine asserts nothing
/// the sockets did not establish.
/// </summary>
public sealed record OpenSocketResult(Socket? V4, Socket? V6, OpenSocketFailure? Failure)
{
    public bool BothFailed => Failure is { Kind: OpenSocketFailureKind.BothFailed };
    public bool V4Only => Failure is { Kind: OpenSocketFailureKind.V4Only };
    public bool V6Only => Failure is { Kind: OpenSocketFailureKind.V6Only };
}

/// <summary>
/// The end state of a capture session. <see cref="Completed"/> when both capture loops finished
/// normally (timer expired or caller cancelled). <see cref="Aborted"/> when at least one loop
/// threw before the cancellation token fired — the consumer must mark the run as inconclusive
/// and never render it as an ordinary partial record (C041 parity).
/// </summary>
public abstract record EngineOutcome
{
    private EngineOutcome() { }
    public sealed record Completed : EngineOutcome;
    public sealed record Aborted(string Reason) : EngineOutcome;
}

/// <summary>
/// A live capture session: two raw sockets (v4 + v6) drained by two concurrent capture loops
/// into a single merged record stream. Construct via <see cref="CaptureEngine.Start"/>. Dispose
/// to cancel and release the sockets. Iterating <see cref="Records"/> yields parsed
/// <see cref="DNSCaptureRecord"/> objects in arrival order; awaiting <see cref="Outcome"/>
/// returns the final state (Completed vs Aborted) for honesty-correct reporting.
/// </summary>
public sealed class CaptureSession : IDisposable
{
    private readonly OpenSocketResult _sockets;
    private readonly CancellationTokenSource _cts;
    private readonly Channel<DNSCaptureRecord> _channel;
    private readonly Task _captureTask;
    private readonly List<Exception> _loopErrors = new();

    internal CaptureSession(OpenSocketResult sockets, TimeSpan duration)
    {
        _sockets = sockets;
        _cts = new CancellationTokenSource(duration);
        _channel = Channel.CreateUnbounded<DNSCaptureRecord>(new UnboundedChannelOptions
        {
            SingleReader = false,   // both loops write concurrently
            SingleWriter = false,
        });
        var locals = CaptureEngine.GetLocalAddresses();
        var tasks = new List<Task>(2);
        if (sockets.V4 is { } s4) tasks.Add(CaptureLoop(s4, locals, _channel.Writer, _cts.Token, _loopErrors));
        if (sockets.V6 is { } s6) tasks.Add(CaptureLoop(s6, locals, _channel.Writer, _cts.Token, _loopErrors));
        // When both loops finish, complete the channel so the reader's await foreach exits.
        _captureTask = Task.WhenAll(tasks).ContinueWith(_ => _channel.Writer.TryComplete(), TaskScheduler.Default);
    }

    /// <summary>
    /// The merged record stream from both sockets. Iterating exits when both capture loops finish
    /// (duration elapsed, or the session was disposed). Wrap with <c>WithCancellation</c> to
    /// stop early from the consumer side.
    /// </summary>
    public IAsyncEnumerable<DNSCaptureRecord> Records => _channel.Reader.ReadAllAsync();

    /// <summary>
    /// The final session state. Await AFTER the <see cref="Records"/> stream has drained — this
    /// task completes when both capture loops have finished. <see cref="EngineOutcome.Aborted"/>
    /// carries a join of every loop's exception message (C041 parity).
    /// </summary>
    public Task<EngineOutcome> Outcome => OutcomeAsync();

    /// <summary>Cancel the capture and release both sockets. Safe to call multiple times.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        try { _sockets.V4?.Dispose(); } catch { }
        try { _sockets.V6?.Dispose(); } catch { }
        _cts.Dispose();
    }

    private async Task<EngineOutcome> OutcomeAsync()
    {
        await _captureTask.ConfigureAwait(false);
        Exception[] errs;
        lock (_loopErrors) errs = _loopErrors.ToArray();
        return errs.Length > 0
            ? new EngineOutcome.Aborted(string.Join("; ", errs.Select(e => e.Message)))
            : new EngineOutcome.Completed();
    }

    // ──────────────────────────────────────────────────────────────────────
    // MARK: - Capture loop (extracted byte-for-byte from the former CageCommand.CaptureLoop)
    // ──────────────────────────────────────────────────────────────────────

    private static async Task CaptureLoop(
        Socket socket, HashSet<string> locals,
        ChannelWriter<DNSCaptureRecord> writer, CancellationToken ct,
        List<Exception> errs)
    {
        byte[] buffer = new byte[65535];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (socket.Poll(100000, SelectMode.SelectRead))  // 100ms poll
                {
                    int bytesRead = socket.Receive(buffer, 0, buffer.Length, SocketFlags.None);
                    var rec = TryBuildRecord(buffer, bytesRead, locals);
                    if (rec is { } r) await writer.WriteAsync(r, ct).ConfigureAwait(false);
                }
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* normal end — timer or caller cancel */ }
        catch (Exception ex)
        {
            // C041 parity: surface the abort so the consumer can mark the run inconclusive.
            // The other loop continues; both contribute to Outcome when they finish.
            lock (errs) errs.Add(ex);
        }
    }

    private static DNSCaptureRecord? TryBuildRecord(byte[] buffer, int bytesRead, HashSet<string> locals)
    {
        byte[] packetData = new byte[bytesRead];
        Array.Copy(buffer, packetData, bytesRead);

        var ctx = DNSCapture.ParseIPUDPHeader(packetData);
        if (ctx is null || (ctx.SourcePort != 53 && ctx.DestPort != 53)) return null;
        if (ctx.PayloadOffset >= packetData.Length) return null;

        byte[] dnsPayload = new byte[packetData.Length - ctx.PayloadOffset];
        Array.Copy(packetData, ctx.PayloadOffset, dnsPayload, 0, dnsPayload.Length);
        var dns = DNSCapture.ParseDNSPacket(dnsPayload);
        if (dns is null) return null;

        // D107 attribution — verify the packet's local-side IP belongs to this host before
        // attributing. Promiscuous-mode captures of a neighbour's query would otherwise be
        // blamed on a local process that happens to hold the same ephemeral port.
        string localSideIP = ctx.SourcePort == 53 ? ctx.DestIP : ctx.SourceIP;
        bool isLocalPacket = locals.Count == 0 || locals.Contains(localSideIP);
        ushort localPort = ctx.SourcePort == 53 ? ctx.DestPort : ctx.SourcePort;
        var proc = isLocalPacket ? DNSCapture.GetProcessInfo(localPort) : ProcessInfoReport.Unknown;
        string direction = dns.Qr == "Query" ? "Outbound Query" : "Inbound Response";

        // D101: invariant culture — non-Gregorian calendars (th-TH Buddhist, ar-SA Umm al-Qura)
        // would produce unparseable timestamps. Matches the original CLI's format verbatim so
        // existing tailing/golden-vector files continue to round-trip.
        return new DNSCaptureRecord
        {
            Process = proc,
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            Packet = dns,
            Network = new NetworkContextReport
            {
                IpVersion = ctx.IpVersion,
                SourceIP = ctx.SourceIP,
                DestIP = ctx.DestIP,
                SourcePort = ctx.SourcePort,
                DestPort = ctx.DestPort,
                Direction = direction,
            },
        };
    }
}

/// <summary>
/// Pure packet-capture engine — opens raw sockets, drains them, builds
/// <see cref="DNSCaptureRecord"/> objects. No console or file I/O. Platform-aware (Windows
/// SIO_RCVALL + HeaderIncluded for v4; Linux/macOS SOCK_RAW with default IP-header inclusion).
/// </summary>
public static class CaptureEngine
{
    /// <summary>
    /// Open v4 + v6 raw sockets bound to the adapter carrying the default gateway (D106 parity —
    /// NOT a DNS-resolved local hostname, which would route through the resolver under
    /// investigation and pick the wrong adapter). Returns <c>Both</c> on success, <c>V4Only</c>
    /// / <c>V6Only</c> when one family failed (degraded capture), or <c>BothFailed</c> when
    /// neither socket could be opened.
    /// </summary>
    public static OpenSocketResult OpenSockets()
    {
        Socket? v4 = null, v6 = null;
        Exception? v4Err = null, v6Err = null;
        try { v4 = OpenRawSocket(AddressFamily.InterNetwork, ProtocolType.IP); }
        catch (Exception ex) { v4Err = ex; v4 = null; }
        try { v6 = OpenRawSocket(AddressFamily.InterNetworkV6, ProtocolType.IPv6); }
        catch (Exception ex) { v6Err = ex; v6 = null; }

        if (v4 is not null && v6 is not null) return new OpenSocketResult(v4, v6, null);
        if (v4 is not null && v6 is null)
            // V4 is the only socket that opened — capture is V4-only (v6 failed).
            return new OpenSocketResult(v4, null,
                new OpenSocketFailure(OpenSocketFailureKind.V4Only, FormatErr(v6Err)));
        if (v6 is not null && v4 is null)
            // V6 is the only socket that opened — capture is V6-only (v4 failed).
            return new OpenSocketResult(null, v6,
                new OpenSocketFailure(OpenSocketFailureKind.V6Only, FormatErr(v4Err)));
        return new OpenSocketResult(null, null,
            new OpenSocketFailure(OpenSocketFailureKind.BothFailed, FormatErr(v4Err ?? v6Err)));
    }

    /// <summary>
    /// Build the set of this host's unicast addresses for D107 attribution. Best-effort: an
    /// empty set (rather than an exception) on enumeration failure, matching the original CLI's
    /// silent-fallback policy. Pure / offline-testable given a stubbed
    /// <see cref="NetworkInterface.GetAllNetworkInterfaces"/>.
    /// </summary>
    public static HashSet<string> GetLocalAddresses()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in iface.GetIPProperties().UnicastAddresses)
                    set.Add(ua.Address.ToString());
            }
        }
        catch { /* best-effort; if we can't enumerate, attribute cautiously below */ }
        return set;
    }

    /// <summary>
    /// Start a capture session over the supplied sockets with a duration-controlled cancellation.
    /// Caller iterates <see cref="CaptureSession.Records"/>, then awaits
    /// <see cref="CaptureSession.Outcome"/> for the final state. Disposing cancels and releases
    /// everything.
    /// </summary>
    public static CaptureSession Start(OpenSocketResult sockets, TimeSpan duration)
        => new CaptureSession(sockets, duration);

    // ──────────────────────────────────────────────────────────────────────
    // MARK: - Raw-socket helpers (verbatim from the former CageCommand.OpenRawSocket)
    // ──────────────────────────────────────────────────────────────────────

    private static string FormatErr(Exception? ex) =>
        ex is null ? "" : $"{ex.GetType().Name}: {ex.Message}";

    private static Socket OpenRawSocket(AddressFamily family, ProtocolType protocol)
    {
        // D081 parity: surface the actual error (this throw propagates to OpenSockets, which
        // captures it into OpenSocketFailure.Message). The original CLI swallowed it inside a
        // try-catch that printed to stderr and returned null; the engine surfaces structured
        // failure so the CLI / daemon / GUI can each format it appropriately.
        Socket socket = new Socket(family, SocketType.Raw, protocol);

        IPAddress bindAddress = FindRoutedAddress(family)
            ?? (family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any);

        socket.Bind(new IPEndPoint(bindAddress, 0));

        if (OperatingSystem.IsWindows())
        {
            // F029: HeaderIncluded is an IPv4-only option (IP_HDRINCL). Applying it to an IPv6
            // socket raises on Windows. For IPv6 raw sockets, SIO_RCVALL alone captures all
            // packets with headers included — no IP_HDRINCL equivalent is needed.
            if (family == AddressFamily.InterNetwork)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
            }
            // E075: SIO_RCVALL is Windows-only. On Linux/macOS, SOCK_RAW + IPPROTO_UDP already
            // receives all UDP packets (including DNS) on the host with CAP_NET_RAW / root.
            byte[] byIn = new byte[] { 1, 0, 0, 0 };
            byte[] byOut = new byte[] { 1, 0, 0, 0 };
            socket.IOControl(IOControlCode.ReceiveAll, byIn, byOut);
        }

        return socket;
    }

    /// <summary>
    /// D106: find a unicast address on the adapter carrying the default gateway — the interface
    /// that actually routes DNS traffic, not the first non-loopback address from
    /// <c>Dns.GetHostEntry</c> (which routes through the resolver under investigation and
    /// returns whichever adapter comes first — Hyper-V, WSL, VPN adapters often win).
    /// </summary>
    private static IPAddress? FindRoutedAddress(AddressFamily family)
    {
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (iface.OperationalStatus != OperationalStatus.Up) continue;
                var props = iface.GetIPProperties();
                bool hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address != null
                    && !g.Address.Equals(IPAddress.Any)
                    && !g.Address.Equals(IPAddress.IPv6Any));
                if (!hasGateway) continue;
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == family && !IPAddress.IsLoopback(ua.Address))
                        return ua.Address;
                }
            }
        }
        catch { /* fall through to the wildcard fallback in OpenRawSocket */ }
        return null;
    }
}
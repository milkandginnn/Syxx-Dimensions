using System;
using System.Diagnostics;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// E035/E036/E046 — process-attribution twin of the macOS D031/D107 fix. The old path shelled
/// out to `cmd.exe /c netstat -ano | findstr /R ":{port} .*UDP"` per unique port; the regex could
/// never match real netstat output (the "UDP" token precedes the port), so attribution was
/// structurally dead — every record read `unknown` while `IsAttributed` stayed honestly false
/// only by luck. Attribution now reads the kernel UDP owner table in-process
/// (`GetExtendedUdpTable`). These tests pin the pure byte math and the honesty decision table;
/// the live table read itself is Windows-only and is owed a real elevated capture run on a
/// Windows host before the delta family closes.
public class DNSCaptureAttributionTests
{
    // MARK: - MIB_UDP*ROW_OWNER_PID port byte math (E046 replacement path)

    [Fact]
    public void PortFromUdpTableEntry_NetworkOrderLowWord()
    {
        // Port 53 (0x0035) arrives network-order in the DWORD's low 16 bits: bytes 00 35 read
        // little-endian as 0x3500.
        Assert.Equal(53, DNSCapture.PortFromUdpTableEntry(0x3500));
        // Port 65535: 0xFFFF either way.
        Assert.Equal(65535, DNSCapture.PortFromUdpTableEntry(0xFFFF));
        // Port 5353 (0x14E9) → network order bytes 14 E9 → LE DWORD 0xE914.
        Assert.Equal(5353, DNSCapture.PortFromUdpTableEntry(0xE914));
        // Port 1 → bytes 00 01 → LE DWORD 0x0100.
        Assert.Equal(1, DNSCapture.PortFromUdpTableEntry(0x0100));
    }

    [Fact]
    public void PortFromUdpTableEntry_IgnoresUndefinedUpperBytes()
    {
        // MSDN: the upper 16 bits of dwLocalPort are undefined and may contain garbage.
        Assert.Equal(53, DNSCapture.PortFromUdpTableEntry(0xDEAD3500));
        Assert.Equal(5353, DNSCapture.PortFromUdpTableEntry(0xFFFFE914));
    }

    // MARK: - E035: partial attribution must not be discarded

    [Fact]
    public void BuildDetailsReport_NullPathKeepsPidAndName()
    {
        // The exact E035 case: svchost-style process where MainModule.FileName is inaccessible.
        // The known pid+name must survive; only path/signingID stay null.
        var report = DNSCapture.BuildDetailsReport(1234, "svchost", null);
        Assert.Equal(1234, report.Pid);
        Assert.Equal("svchost", report.Name);
        Assert.Null(report.Path);
        Assert.Null(report.SigningID);
        Assert.True(report.IsAttributed);
    }

    [Fact]
    public void BuildDetailsReport_NothingKnown_IsUnknownNeverFabricated()
    {
        var report = DNSCapture.BuildDetailsReport(1234, null, null);
        Assert.Null(report.Pid);
        Assert.Null(report.Name);
        Assert.Null(report.Path);
        Assert.False(report.IsAttributed);
    }

    [Fact]
    public void BuildDetailsReport_PathWithoutName_DerivesNameFromPath()
    {
        var report = DNSCapture.BuildDetailsReport(77, null, @"C:\Windows\System32\svchost.exe");
        Assert.Equal(77, report.Pid);
        Assert.Equal("svchost", report.Name);
        Assert.Equal(@"C:\Windows\System32\svchost.exe", report.Path);
        Assert.True(report.IsAttributed);
    }

    // MARK: - honest fallbacks in the live lookup path

    [Fact]
    public void GetProcessInfo_UnattributablePort_IsUnknownNotAGuess()
    {
        // On non-Windows hosts the kernel table is unavailable, so this MUST be Unknown (the
        // capture itself is Windows-only). On Windows, a port nobody is bound to must also be
        // Unknown. Either way: no fabricated identity, IsAttributed stays false.
        var report = DNSCapture.GetProcessInfo(1);   // port 1/udp: never a real local DNS client
        Assert.False(report.IsAttributed);
        Assert.Null(report.Pid);
        Assert.Null(report.Name);
    }

    [SkippableFactAttributeSubstitute]
    public void GetProcessInfo_WindowsLiveTable_AttributesOwnSocket()
    {
        // Windows-only adversarial-realistic check: bind a UDP socket ourselves, then ask the
        // new snapshot path to attribute its port — it must find THIS process.
        if (!OperatingSystem.IsWindows()) return;   // owed a real Windows-host run; see class doc
        using var udp = new System.Net.Sockets.UdpClient(0);
        ushort port = (ushort)((System.Net.IPEndPoint)udp.Client.LocalEndPoint!).Port;
        var report = DNSCapture.GetProcessInfo(port);
        Assert.True(report.IsAttributed, "the kernel UDP table must attribute our own bound socket");
        Assert.Equal(Environment.ProcessId, report.Pid);
    }
}

/// xunit has no built-in runtime skip; this alias keeps the intent explicit without adding a
/// package dependency. The fact still runs everywhere and self-gates on OS inside.
public sealed class SkippableFactAttributeSubstitute : FactAttribute { }

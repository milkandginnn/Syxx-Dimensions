using Syxx.Kit;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace Syxx.Tests;

public sealed class LanLifecycleTests
{
    [Theory]
    [InlineData("Schnittstelle:", "dynamisch")]
    [InlineData("インターフェイス:", "静的")]
    public void Parse_UnrecognizedLocalizedType_IsUnknown(string headerLabel, string localizedType)
    {
        string output = $"""
            {headerLabel} 192.168.50.10 --- 0x6
              192.168.50.1          aa-bb-cc-dd-ee-ff     {localizedType}
            """;

        ARPEntry entry = Assert.Single(WindowsArpParser.Parse(output));

        Assert.Equal("192.168.50.10", entry.Interface);
        Assert.Equal(ARPEntryState.Unknown, entry.State);
    }

    [Fact]
    public void Parse_InvalidEntry_IsIncompleteRatherThanReachable()
    {
        const string output = """
            Interface: 192.168.50.10 --- 0x6
              Internet Address      Physical Address      Type
              192.168.50.44         00-00-00-00-00-00     invalid
            """;

        ARPEntry entry = Assert.Single(WindowsArpParser.Parse(output));

        Assert.Equal(ARPEntryState.Incomplete, entry.State);
    }

    [Fact]
    public void Parse_RecognizedEnglishTypes_RetainMeasuredStates()
    {
        const string output = """
            Interface: 192.168.50.10 --- 0x6
              Internet Address      Physical Address      Type
              192.168.50.1          aa-bb-cc-dd-ee-01     dynamic
              192.168.50.2          aa-bb-cc-dd-ee-02     static
            """;

        IReadOnlyList<ARPEntry> entries = WindowsArpParser.Parse(output);

        Assert.Equal(ARPEntryState.Reachable, entries[0].State);
        Assert.Equal(ARPEntryState.Permanent, entries[1].State);
    }

    [Fact]
    public void Parse_MixedValidAndMalformedRows_ReportsRejectedCandidate()
    {
        const string output = """
            Interface: 192.168.50.10 --- 0x6
              Internet Address      Physical Address      Type
              192.168.50.1          aa-bb-cc-dd-ee-01     dynamic
              192.168.50.2          aa-bb-cc-dd-ee-gg     dynamic
            """;

        ARPParseResult result = WindowsArpParser.ParseWithDiagnostics(output);

        Assert.Single(result.Entries);
        Assert.Equal(2, result.CandidateCount);
        Assert.Equal(1, result.RejectedCandidateCount);
    }

    [Fact]
    public void SystemCommandRunner_CapturesStdoutStderrAndExitCode()
    {
        (string executable, IReadOnlyList<string> arguments) = ShellCommand(
            unix: "printf stdout-value; printf stderr-value >&2; exit 7",
            windows: "(echo stdout-value)&(echo stderr-value 1>&2)&exit /b 7");

        CommandResult result = new SystemCommandRunner().Run(executable, arguments, timeoutMs: 2_000);

        Assert.Contains("stdout-value", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("stderr-value", result.Stderr, StringComparison.Ordinal);
        Assert.Equal(7, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public void SystemCommandRunner_TerminatesAtDeadline()
    {
        (string executable, IReadOnlyList<string> arguments) = ShellCommand(
            unix: "sleep 5",
            windows: "ping 127.0.0.1 -n 6 > nul");
        var stopwatch = Stopwatch.StartNew();

        CommandResult result = new SystemCommandRunner().Run(executable, arguments, timeoutMs: 100);

        stopwatch.Stop();
        Assert.True(result.TimedOut);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"runner took {stopwatch.Elapsed}");
    }

    [Fact]
    public void Snapshot_NonPositiveTimeout_IsUnreadableWithoutInvokingRunner()
    {
        var runner = new StubCommandRunner(new CommandResult(
            stdout: """
                Interface: 192.168.50.10 --- 0x6
                  192.168.50.1          aa-bb-cc-dd-ee-ff     dynamic
                """,
            stderr: "",
            exitCode: 0,
            timedOut: false));
        var cartographer = new LANCartographer(runner);

        LANSnapshot zero = cartographer.Snapshot(timeoutMs: 0);
        LANSnapshot negative = cartographer.Snapshot(timeoutMs: -1);

        Assert.Equal(LANSnapshotStatus.Unreadable, zero.Status);
        Assert.Equal(LANSnapshotStatus.Unreadable, negative.Status);
        Assert.Equal(0, runner.InvocationCount);
    }

    [Fact]
    public void Snapshot_NonEmptyUnparseableOutput_IsUnreadableAndPassesTimeout()
    {
        var runner = new StubCommandRunner(new CommandResult(
            stdout: "localized output that contains no neighbour rows",
            stderr: "",
            exitCode: 0,
            timedOut: false));

        LANSnapshot snapshot = new LANCartographer(runner).Snapshot(timeoutMs: 321);

        Assert.Equal(321, runner.ObservedTimeoutMs);
        Assert.Equal(LANSnapshotStatus.Unreadable, snapshot.Status);
        Assert.NotEmpty(snapshot.Reason ?? "");
        Assert.Empty(snapshot.Devices);
    }

    [Fact]
    public void Snapshot_NonZeroExit_PreservesDiagnosticAsUnreadableReason()
    {
        var runner = new StubCommandRunner(new CommandResult(
            stdout: "",
            stderr: "access denied",
            exitCode: 5,
            timedOut: false));

        LANSnapshot snapshot = new LANCartographer(runner).Snapshot();

        Assert.Equal(LANSnapshotStatus.Unreadable, snapshot.Status);
        Assert.Contains("5", snapshot.Reason ?? "", StringComparison.Ordinal);
        Assert.Contains("access denied", snapshot.Reason ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_TimeoutWithParsedRows_PreservesPartialEvidence()
    {
        var runner = new StubCommandRunner(new CommandResult(
            stdout: """
                Interface: 192.168.50.10 --- 0x6
                  192.168.50.1          aa-bb-cc-dd-ee-ff     dynamic
                """,
            stderr: "",
            exitCode: null,
            timedOut: true));

        LANSnapshot snapshot = new LANCartographer(runner).Snapshot();

        Assert.Equal(LANSnapshotStatus.Partial, snapshot.Status);
        Assert.NotEmpty(snapshot.Reason ?? "");
        Assert.Single(snapshot.Devices);
    }

    [Fact]
    public void Snapshot_SuccessWithParsedRows_IsComplete()
    {
        var runner = new StubCommandRunner(new CommandResult(
            stdout: """
                Interface: 192.168.50.10 --- 0x6
                  192.168.50.1          aa-bb-cc-dd-ee-ff     dynamic
                """,
            stderr: "",
            exitCode: 0,
            timedOut: false));

        LANSnapshot snapshot = new LANCartographer(runner).Snapshot();

        Assert.Equal(LANSnapshotStatus.Complete, snapshot.Status);
        Assert.Null(snapshot.Reason);
        Assert.Single(snapshot.Devices);
    }

    [Fact]
    public void Snapshot_MixedValidAndMalformedRows_IsPartial()
    {
        var runner = new StubCommandRunner(new CommandResult(
            stdout: """
                Interface: 192.168.50.10 --- 0x6
                  Internet Address      Physical Address      Type
                  192.168.50.1          aa-bb-cc-dd-ee-01     dynamic
                  192.168.50.2          aa-bb-cc-dd-ee-gg     dynamic
                """,
            stderr: "",
            exitCode: 0,
            timedOut: false));

        LANSnapshot snapshot = new LANCartographer(runner).Snapshot();

        Assert.Equal(LANSnapshotStatus.Partial, snapshot.Status);
        Assert.Single(snapshot.Devices);
        Assert.Equal(1, snapshot.RejectedCandidateCount);
        Assert.Contains("1", snapshot.Reason ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Diff_UsesFullDeviceIdentityAndReportsDuplicateMacBindings()
    {
        const string capture = """
            Interface: 192.168.50.20 --- 0x6
              Internet Address      Physical Address      Type
              192.168.50.10         aa-bb-cc-dd-ee-ff     dynamic
              192.168.50.2          aa-bb-cc-dd-ee-ff     dynamic
            """;
        List<LANDevice> devices = WindowsArpParser.Parse(capture)
            .Select(entry => new LANDevice(entry))
            .ToList();
        LANDevice first = devices[0];
        LANSnapshot previous = LANSnapshot.Complete(new[] { first });
        LANSnapshot current = LANSnapshot.Complete(devices);

        LANDiff diff = LANCartographer.Diff(previous, current);

        Assert.Equal(2, devices.Count);
        Assert.Equal("192.168.50.2", Assert.Single(diff.NewDevices).IPAddress);
        Assert.Empty(diff.MissingDevices);
        Assert.Empty(diff.ChangedAddresses);
        DuplicateMacBinding binding = Assert.Single(diff.DuplicateMacBindings);
        Assert.Equal("aa:bb:cc:dd:ee:ff", binding.MacAddress);
        Assert.Equal(new[] { "192.168.50.2", "192.168.50.10" }, binding.IPAddresses);
    }

    [Fact]
    public void Diff_DuplicateMacInPreviousSnapshot_DoesNotThrow()
    {
        LANDevice first = Device("192.168.50.1", "aa:bb:cc:dd:ee:ff");
        LANDevice second = Device("192.168.50.2", "aa:bb:cc:dd:ee:ff");
        LANSnapshot previous = LANSnapshot.Complete(new[] { first, second });
        LANSnapshot current = LANSnapshot.Complete(new[] { first, second });

        LANDiff diff = LANCartographer.Diff(previous, current);

        Assert.Empty(diff.NewDevices);
        Assert.Empty(diff.MissingDevices);
        Assert.Single(diff.DuplicateMacBindings);
    }

    [Fact]
    public void Diff_NonCompleteSnapshots_DoNotInferMissingDevices()
    {
        LANDevice device = Device("192.168.50.1", "aa:bb:cc:dd:ee:ff");
        LANDevice other = Device("192.168.50.2", "aa:bb:cc:dd:ee:02");
        LANSnapshot complete = LANSnapshot.Complete(new[] { device });
        LANSnapshot partial = LANSnapshot.Partial(new[] { other }, "truncated");
        LANSnapshot unreadable = LANSnapshot.Unreadable("unreadable");

        Assert.Empty(LANCartographer.Diff(complete, partial).MissingDevices);
        Assert.Empty(LANCartographer.Diff(partial, complete).MissingDevices);
        Assert.Empty(LANCartographer.Diff(complete, unreadable).MissingDevices);
        Assert.Empty(LANCartographer.Diff(unreadable, complete).MissingDevices);
    }

    [Fact]
    public void Diff_IncompletePlaceholders_AreNotDuplicateMacBindings()
    {
        LANDevice first = Device(
            "192.168.50.1",
            "00:00:00:00:00:00",
            state: ARPEntryState.Incomplete);
        LANDevice second = Device(
            "192.168.50.2",
            "00:00:00:00:00:00",
            state: ARPEntryState.Incomplete);
        LANSnapshot snapshot = LANSnapshot.Complete(new[] { first, second });

        LANDiff diff = LANCartographer.Diff(snapshot, snapshot);

        Assert.Empty(diff.DuplicateMacBindings);
    }

    [Fact]
    public void Diff_MalformedMacs_AreNotDuplicateMacBindings()
    {
        LANDevice first = Device(
            "192.168.50.1",
            "aa:bb:cc:dd:ee:gg",
            state: ARPEntryState.Unknown);
        LANDevice second = Device(
            "192.168.50.2",
            "aa:bb:cc:dd:ee:gg",
            state: ARPEntryState.Unknown);
        LANSnapshot snapshot = LANSnapshot.Complete(new[] { first, second });

        LANDiff diff = LANCartographer.Diff(snapshot, snapshot);

        Assert.Empty(diff.DuplicateMacBindings);
    }

    [Fact]
    public void SnapshotFactories_RejectCompleteEmptyAndContradictoryStates()
    {
        LANDevice device = Device("192.168.50.1", "aa:bb:cc:dd:ee:ff");

        Assert.Throws<ArgumentException>(() => LANSnapshot.Complete(Array.Empty<LANDevice>()));
        Assert.Throws<ArgumentException>(() => LANSnapshot.Partial(
            Array.Empty<LANDevice>(),
            "truncated"));
        Assert.Throws<ArgumentException>(() => LANSnapshot.Partial(new[] { device }, ""));
        Assert.Throws<ArgumentException>(() => LANSnapshot.Create(
            new[] { device },
            LANSnapshotStatus.Complete,
            "contradictory reason"));
        Assert.Throws<ArgumentException>(() => LANSnapshot.Create(
            new[] { device },
            LANSnapshotStatus.Unreadable,
            "contradictory devices"));
    }

    [Fact]
    public void SnapshotFactories_DefensivelyCopyDeviceEvidence()
    {
        var devices = new List<LANDevice>
        {
            Device("192.168.50.1", "aa:bb:cc:dd:ee:ff")
        };

        LANSnapshot snapshot = LANSnapshot.Complete(devices);
        devices.Clear();

        Assert.Single(snapshot.Devices);
        Assert.Equal(LANSnapshotStatus.Complete, snapshot.Status);
    }

    private static LANDevice Device(
        string ipAddress,
        string macAddress,
        string iface = "192.168.50.10",
        ARPEntryState state = ARPEntryState.Reachable)
    {
        return new LANDevice(new ARPEntry(
            host: null,
            ipAddress: ipAddress,
            macAddress: macAddress,
            iface: iface,
            state: state,
            rawLine: ""));
    }

    private static (string Executable, IReadOnlyList<string> Arguments) ShellCommand(
        string unix,
        string windows)
    {
        if (OperatingSystem.IsWindows())
            return ("cmd.exe", new[] { "/d", "/s", "/c", windows });
        return ("/bin/sh", new[] { "-c", unix });
    }

    private sealed class StubCommandRunner : ICommandRunner
    {
        private readonly CommandResult _result;

        public int? ObservedTimeoutMs { get; private set; }
        public int InvocationCount { get; private set; }

        public StubCommandRunner(CommandResult result)
        {
            _result = result;
        }

        public CommandResult Run(string executable, IReadOnlyList<string> arguments, int timeoutMs)
        {
            InvocationCount += 1;
            ObservedTimeoutMs = timeoutMs;
            return _result;
        }
    }
}

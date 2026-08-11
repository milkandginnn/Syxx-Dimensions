using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    public class WindowsArpParserTests
    {
        private const string Sample =
            "\r\nInterface: 192.168.1.5 --- 0xb\r\n" +
            "  Internet Address      Physical Address      Type\r\n" +
            "  192.168.1.1           aa-bb-cc-dd-ee-01     dynamic\r\n" +
            "  192.168.1.42          aa-bb-cc-dd-ee-02     dynamic\r\n" +
            "  192.168.1.255         ff-ff-ff-ff-ff-ff     static\r\n" +
            "  224.0.0.22            01-00-5e-00-00-16     static\r\n";

        [Fact]
        public void ParsesRowsAndNormalizesMacToColonLowercase()
        {
            var entries = WindowsArpParser.Parse(Sample);
            Assert.Equal(4, entries.Count);
            var gw = entries[0];
            Assert.Equal("192.168.1.1", gw.IPAddress);
            Assert.Equal("aa:bb:cc:dd:ee:01", gw.MacAddress);
            Assert.Equal("192.168.1.5", gw.Interface);
            Assert.Equal(ARPEntryState.Reachable, gw.State);
        }

        [Fact]
        public void SkipsHeaderAndInterfaceLinesButKeepsBroadcastAndMulticast()
        {
            var entries = WindowsArpParser.Parse(Sample);
            Assert.Contains(entries, e => e.IPAddress == "192.168.1.255");
            Assert.Contains(entries, e => e.IPAddress == "224.0.0.22");
            Assert.DoesNotContain(entries, e => e.IPAddress.StartsWith("Internet"));
        }

        [Fact]
        public void ClassifiesAddressKinds()
        {
            Assert.Equal(LANAddressKind.PrivateUnicast, NetworkAddressClassifier.KindForIPv4("192.168.1.1"));
            Assert.Equal(LANAddressKind.Broadcast, NetworkAddressClassifier.KindForIPv4("255.255.255.255"));
            Assert.Equal(LANAddressKind.Multicast, NetworkAddressClassifier.KindForIPv4("224.0.0.22"));
            Assert.Equal(LANAddressKind.Loopback, NetworkAddressClassifier.KindForIPv4("127.0.0.1"));
            Assert.Equal(LANAddressKind.LinkLocal, NetworkAddressClassifier.KindForIPv4("169.254.1.1"));
        }

        [Fact]
        public void SnapshotDiffDetectsNewMissingAndMovedDevices()
        {
            var prev = LANSnapshot.Complete(WindowsArpParser.Parse(Sample).ConvertAll(e => new LANDevice(e)));
            // Device .42 moves to a new IP; a new device appears; the broadcast row disappears.
            const string next =
                "Interface: 192.168.1.5 --- 0xb\r\n" +
                "  192.168.1.1           aa-bb-cc-dd-ee-01     dynamic\r\n" +
                "  192.168.1.77          aa-bb-cc-dd-ee-02     dynamic\r\n" +
                "  192.168.1.99          aa-bb-cc-dd-ee-03     dynamic\r\n";
            var cur = LANSnapshot.Complete(WindowsArpParser.Parse(next).ConvertAll(e => new LANDevice(e)));
            var diff = LANCartographer.Diff(prev, cur);
            Assert.Contains(diff.NewDevices, d => d.MacAddress == "aa:bb:cc:dd:ee:03");
            Assert.Contains(diff.ChangedAddresses, s => s.Contains("192.168.1.42 -> 192.168.1.77"));
            Assert.True(diff.HasChanges);
        }

        // F037: PlannedProbes was removed from LANDevice; test archived.
    }

    public class TrustLedgerAndSentinelTests
    {
        [Fact]
        public void AnswerLedgerFlagsDriftOnChangedAnswer()
        {
            var ledger = new AnswerLedger();
            Assert.Null(ledger.Record("bank.example", "A", "203.0.113.10", "1.1.1.1", 300));
            Assert.Null(ledger.Record("bank.example", "A", "203.0.113.10", "1.1.1.1", 300)); // same -> no drift
            var drift = ledger.Record("bank.example", "A", "45.9.9.9", "1.1.1.1", 60);        // changed -> drift
            Assert.NotNull(drift);
            Assert.Equal("203.0.113.10", drift!.PreviousAnswer);
            Assert.Equal("45.9.9.9", drift.NewAnswer);
            Assert.Equal(3, ledger.History("bank.example").Count);
        }

        [Fact]
        public void SentinelDiffDetectsResolverChangeAndStability()
        {
            var a = new NetworkConfigSnapshot
            {
                Resolvers = new List<string> { "1.1.1.1" },
                ActiveInterfaces = new List<string> { "eth0" },
                VpnActive = ConditionState.No,
                DnsEncrypted = false
            };
            var b = new NetworkConfigSnapshot
            {
                Resolvers = new List<string> { "8.8.8.8" },
                ActiveInterfaces = new List<string> { "eth0" },
                VpnActive = ConditionState.Yes,
                DnsEncrypted = false
            };
            var changes = Sentinel.Diff(a, b);
            Assert.Contains(changes, c => c.Kind == ConfigChangeKind.ResolversChanged);
            Assert.Contains(changes, c => c.Kind == ConfigChangeKind.VpnChanged);
            Assert.Empty(Sentinel.Diff(a, a));
        }
    }

    public class TextDiffTests
    {
        [Fact]
        public void IdenticalIsEmpty()
        {
            Assert.Empty(TextDiff.Unified("a\nb\nc", "a\nb\nc"));
        }

        [Fact]
        public void ShowsAddedAndRemovedLines()
        {
            var d = TextDiff.Unified("a\nb\nc", "a\nX\nc");
            Assert.Contains("- b", d);
            Assert.Contains("+ X", d);
        }

        [Fact]
        public void DetectsInjectedScriptLine()
        {
            var local = "<html>\n<body>hi</body>\n</html>";
            var remote = "<html>\n<script>steal()</script>\n<body>hi</body>\n</html>";
            var d = TextDiff.Unified(local, remote);
            Assert.Contains(d, line => line.StartsWith("+ ") && line.Contains("steal()"));
        }
    }

    public class DNSLogAnalyzerTests
    {
        private const string Sample =
            "method '-[DNSProxyProvider startProxyWithOptions:completionHandler:]' invoked\n" +
            "no block list specified, all traffic will be allowed\n" +
            "PROCESS:\n{\n    name = Mail;\n    path = \"/System/Applications/Mail.app\";\n    pid = 670;\n    \"signing ID\" = \"com.apple.mail\";\n}\n" +
            "PACKET:\nXid: 5516\nQR: Query\nServer: -nil-\nRcode: No error\nQuestion (1):\nimap.mail.me.com.akadns.net IN A\nAnswer (0):\nAuthority (0):\nAdditional records (0):\n" +
            "ERROR: 'nw_connection_set_state_changed_handler' failed with 57\n" +
            "PROCESS:\n{\n    name = mDNSResponder;\n    path = \"/usr/sbin/mDNSResponder\";\n    pid = 454;\n    \"signing ID\" = \"com.apple.mDNSResponder\";\n}\n" +
            "PACKET:\nXid: 29650\nQR: Reply\nRcode: Name error\nQuestion (1):\nb._dns-sd._udp.42.2.0.192.in-addr.arpa IN PTR\nAnswer (0):\nAuthority (1):\n192.in-addr.arpa IN SOA 442 z.arin.net dns-ops.arin.net 2017041035\nAdditional records (0):\n" +
            "PROCESS:\n{\n    name = \"Codex (Service)\";\n    path = \"/Applications/Codex.app/x/Codex (Service).app\";\n    pid = 1015;\n    \"signing ID\" = \"com.openai.codex.helper\";\n}\n" +
            "PACKET:\nXid: 16623\nQR: Reply\nRcode: No error\nQuestion (1):\nmtalk.google.com IN A\nAnswer (2):\nmtalk.google.com IN CNAME 63 mobile-gtalk.l.google.com\nmobile-gtalk.l.google.com IN A 63 172.253.135.188\nAuthority (0):\nAdditional records (0):\n" +
            "method '-[DNSProxyProvider stopProxyWithReason:completionHandler:]' invoked\n";

        [Fact]
        public void ParserExtractsProcessesTypesAndRcodes()
        {
            var records = CaptureLogParser.Parse(Sample);
            Assert.Equal(3, records.Count);
            Assert.Contains(records, r => r.Process == "Codex (Service)");
            Assert.Contains(records, r => r.QuestionType == "PTR");
            Assert.Contains(records, r => r.QuestionName == "mtalk.google.com" && r.QuestionType == "A");
            Assert.Contains(records, r => r.IsReply && r.Rcode == "Name error");
        }

        [Fact]
        public void AnalyzerPopulatesWhatTheOldReportLeftEmpty()
        {
            var a = DNSLogAnalyzer.Analyze(CaptureLogParser.Parse(Sample));
            Assert.NotEmpty(a.ProcessCounts);
            Assert.NotEmpty(a.TypeCounts);
            Assert.Contains(a.ProcessCounts, p => p.Name == "Mail");
            Assert.Contains(a.StatusCounts, s => s.Name == "nxdomain");
            Assert.Contains(a.StatusCounts, s => s.Name == "successful");
            Assert.Contains(a.RegistrableCounts, r => r.Name == "google.com");
        }
    }
}

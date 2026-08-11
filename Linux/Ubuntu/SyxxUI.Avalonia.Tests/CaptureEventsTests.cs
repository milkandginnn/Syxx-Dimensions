using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    // B021 — tests for CaptureEvents.cs: event parsing, filtering, classification, and edge cases.
    public class CaptureEventsTests
    {
        // ── ParseTimestamp ─────────────────────────────────────────────────────────

        [Fact]
        public void ParseTimestamp_WithMilliseconds_ReturnsUtc()
        {
            var ts = CaptureEventClassifier.ParseTimestamp("2026-08-10 14:30:45.123");
            Assert.NotNull(ts);
            Assert.Equal(new DateTime(2026, 8, 10, 14, 30, 45, 123), ts!.Value);
        }

        [Fact]
        public void ParseTimestamp_WithoutMilliseconds_ReturnsUtc()
        {
            var ts = CaptureEventClassifier.ParseTimestamp("2026-08-10 14:30:45");
            Assert.NotNull(ts);
            Assert.Equal(new DateTime(2026, 8, 10, 14, 30, 45), ts!.Value);
        }

        [Fact]
        public void ParseTimestamp_EmptyOrNull_ReturnsNull()
        {
            Assert.Null(CaptureEventClassifier.ParseTimestamp(""));
            Assert.Null(CaptureEventClassifier.ParseTimestamp(null!));
        }

        [Fact]
        public void ParseTimestamp_GarbageFormat_ReturnsNull()
        {
            Assert.Null(CaptureEventClassifier.ParseTimestamp("not-a-date"));
            Assert.Null(CaptureEventClassifier.ParseTimestamp("2026/08/10 14:30:45"));
        }

        // ── ClassifyWindow — empty / insufficient data ───────────────────────────

        [Fact]
        public void ClassifyWindow_EmptyRecords_ReturnsNoEvents()
        {
            var events = CaptureEventClassifier.ClassifyWindow(
                Array.Empty<DNSCaptureRecord>(), DateTime.UtcNow);
            Assert.Empty(events);
        }

        [Fact]
        public void ClassifyWindow_RecordsOutsideWindow_ReturnsNoEvents()
        {
            var now = new DateTime(2026, 8, 10, 12, 0, 0);
            var records = new[]
            {
                MakeRecord("2026-08-10 11:58:00.000", "Outbound Query"),
                MakeRecord("2026-08-10 11:57:00.000", "Inbound Response", "NOERROR"),
            };
            // Default window is 30s — these are 2+ minutes old.
            var events = CaptureEventClassifier.ClassifyWindow(records, now);
            Assert.Empty(events);
        }

        // ── ClassifyWindow — PlainTextLeak ────────────────────────────────────────

        [Fact]
        public void ClassifyWindow_PlaintextOutbound_WhenEncryptedExpected_EmitsPlainTextLeak()
        {
            var now = new DateTime(2026, 8, 10, 12, 0, 0);
            var records = new[]
            {
                MakeRecord(FormatTs(now.AddSeconds(-5)), "Outbound Query"),
            };
            var events = CaptureEventClassifier.ClassifyWindow(records, now, encryptedDnsExpected: true);
            Assert.Single(events);
            Assert.Equal(CaptureEventKind.PlainTextLeak, events[0].Kind);
            Assert.Equal(CaptureEventSeverity.Critical, events[0].Severity);
        }

        [Fact]
        public void ClassifyWindow_PlaintextOutbound_WhenEncryptedNotExpected_NoLeakEvent()
        {
            var now = new DateTime(2026, 8, 10, 12, 0, 0);
            var records = new[]
            {
                MakeRecord(FormatTs(now.AddSeconds(-5)), "Outbound Query"),
            };
            var events = CaptureEventClassifier.ClassifyWindow(records, now, encryptedDnsExpected: false);
            Assert.DoesNotContain(events, e => e.Kind == CaptureEventKind.PlainTextLeak);
        }

        // ── ClassifyWindow — NXDomainFlood ────────────────────────────────────────

        [Fact]
        public void ClassifyWindow_NXDomainFlood_AboveThresholds_EmitsEvent()
        {
            var now = new DateTime(2026, 8, 10, 12, 0, 0);
            var records = new List<DNSCaptureRecord>();
            // 12 NXDOMAIN out of 15 responses = 80% — well above the 50% / 10-count threshold.
            for (int i = 0; i < 12; i++)
                records.Add(MakeRecord(FormatTs(now.AddSeconds(-(25 - i))), "Inbound Response", "NXDOMAIN"));
            for (int i = 0; i < 3; i++)
                records.Add(MakeRecord(FormatTs(now.AddSeconds(-(12 - i))), "Inbound Response", "NOERROR"));

            var events = CaptureEventClassifier.ClassifyWindow(records, now);
            Assert.Contains(events, e => e.Kind == CaptureEventKind.NXDomainFlood);
            var flood = events.First(e => e.Kind == CaptureEventKind.NXDomainFlood);
            Assert.Equal(CaptureEventSeverity.High, flood.Severity);
            Assert.Equal("12", flood.Data?["nxdomain_count"]);
        }

        [Fact]
        public void ClassifyWindow_NXDomainFlood_BelowMinCount_NoEvent()
        {
            var now = new DateTime(2026, 8, 10, 12, 0, 0);
            var records = new List<DNSCaptureRecord>();
            // 5 NXDOMAIN out of 5 — 100% ratio but below the 10-count floor.
            for (int i = 0; i < 5; i++)
                records.Add(MakeRecord(FormatTs(now.AddSeconds(-5)), "Inbound Response", "NXDOMAIN"));

            var events = CaptureEventClassifier.ClassifyWindow(records, now);
            Assert.DoesNotContain(events, e => e.Kind == CaptureEventKind.NXDomainFlood);
        }

        // ── ClassifyWindow — ServFailSpike ────────────────────────────────────────

        [Fact]
        public void ClassifyWindow_ServFailSpike_AboveThresholds_EmitsEvent()
        {
            var now = new DateTime(2026, 8, 10, 12, 0, 0);
            var records = new List<DNSCaptureRecord>();
            // 8 SERVFAIL out of 20 responses = 40% — above the 25% / 5-count threshold.
            for (int i = 0; i < 8; i++)
                records.Add(MakeRecord(FormatTs(now.AddSeconds(-10)), "Inbound Response", "SERVFAIL"));
            for (int i = 0; i < 12; i++)
                records.Add(MakeRecord(FormatTs(now.AddSeconds(-10)), "Inbound Response", "NOERROR"));

            var events = CaptureEventClassifier.ClassifyWindow(records, now);
            Assert.Contains(events, e => e.Kind == CaptureEventKind.ServFailSpike);
        }

        // ── ClassifyWindow — ResponseLossHigh ─────────────────────────────────────

        [Fact]
        public void ClassifyWindow_ResponseLossHigh_LowReplyRate_EmitsEvent()
        {
            var now = new DateTime(2026, 8, 10, 12, 0, 0);
            var records = new List<DNSCaptureRecord>();
            // 15 outbound, only 3 inbound responses = 20% reply rate (below 50% threshold).
            for (int i = 0; i < 15; i++)
                records.Add(MakeRecord(FormatTs(now.AddSeconds(-5)), "Outbound Query"));
            for (int i = 0; i < 3; i++)
                records.Add(MakeRecord(FormatTs(now.AddSeconds(-5)), "Inbound Response", "NOERROR"));

            var events = CaptureEventClassifier.ClassifyWindow(records, now);
            Assert.Contains(events, e => e.Kind == CaptureEventKind.ResponseLossHigh);
            Assert.Equal(CaptureEventSeverity.Warning, events.First(e => e.Kind == CaptureEventKind.ResponseLossHigh).Severity);
        }

        // ── ClassifyWindow — Rcode matching (numeric + textual) ───────────────────

        [Fact]
        public void ClassifyWindow_NXDomain_ByNumericCode3_Counted()
        {
            var now = new DateTime(2026, 8, 10, 12, 0, 0);
            var records = new List<DNSCaptureRecord>();
            // 12 responses with rcode "3" (numeric NXDOMAIN) out of 15 total.
            for (int i = 0; i < 12; i++)
                records.Add(MakeRecord(FormatTs(now.AddSeconds(-5)), "Inbound Response", "3"));
            for (int i = 0; i < 3; i++)
                records.Add(MakeRecord(FormatTs(now.AddSeconds(-5)), "Inbound Response", "NOERROR"));

            var events = CaptureEventClassifier.ClassifyWindow(records, now);
            Assert.Contains(events, e => e.Kind == CaptureEventKind.NXDomainFlood);
        }

        // ── EncryptedDnsTransitionEvent ──────────────────────────────────────────

        [Fact]
        public void EncryptedDnsTransition_NoToYes_EmitsDetected()
        {
            var now = DateTime.UtcNow;
            var prev = new EncryptedDNSStatus { State = ConditionState.No, Evidence = "" };
            var curr = new EncryptedDNSStatus { State = ConditionState.Yes, Evidence = "DoH" };
            var ev = CaptureEventClassifier.EncryptedDnsTransitionEvent(prev, curr, now);
            Assert.NotNull(ev);
            Assert.Equal(CaptureEventKind.EncryptedDnsDetected, ev!.Kind);
            Assert.Equal(CaptureEventSeverity.Info, ev.Severity);
        }

        [Fact]
        public void EncryptedDnsTransition_YesToNo_EmitsLost()
        {
            var now = DateTime.UtcNow;
            var prev = new EncryptedDNSStatus { State = ConditionState.Yes, Evidence = "DoT" };
            var curr = new EncryptedDNSStatus { State = ConditionState.No, Evidence = "" };
            var ev = CaptureEventClassifier.EncryptedDnsTransitionEvent(prev, curr, now);
            Assert.NotNull(ev);
            Assert.Equal(CaptureEventKind.EncryptedDnsLost, ev!.Kind);
            Assert.Equal(CaptureEventSeverity.Warning, ev.Severity);
        }

        [Fact]
        public void EncryptedDnsTransition_NoChange_ReturnsNull()
        {
            var now = DateTime.UtcNow;
            var prev = new EncryptedDNSStatus { State = ConditionState.Yes, Evidence = "DoH" };
            var curr = new EncryptedDNSStatus { State = ConditionState.Yes, Evidence = "DoH" };
            Assert.Null(CaptureEventClassifier.EncryptedDnsTransitionEvent(prev, curr, now));
        }

        [Fact]
        public void EncryptedDnsTransition_NullPrevious_ToYes_EmitsDetected()
        {
            var now = DateTime.UtcNow;
            var curr = new EncryptedDNSStatus { State = ConditionState.Yes, Evidence = "DoQ" };
            var ev = CaptureEventClassifier.EncryptedDnsTransitionEvent(null, curr, now);
            Assert.NotNull(ev);
            Assert.Equal(CaptureEventKind.EncryptedDnsDetected, ev!.Kind);
        }

        // ── CaptureEventGate — cooldown dedup ────────────────────────────────────

        [Fact]
        public void CaptureEventGate_FirstEvent_Fires()
        {
            var gate = new CaptureEventGate();
            var now = DateTime.UtcNow;
            var ev = new CaptureEvent(CaptureEventKind.NXDomainFlood, CaptureEventSeverity.High, "test", now);
            var fired = gate.Filter(new[] { ev }, now);
            Assert.Single(fired);
        }

        [Fact]
        public void CaptureEventGate_DuplicateWithinCooldown_Suppressed()
        {
            var gate = new CaptureEventGate();
            var now = DateTime.UtcNow;
            var ev = new CaptureEvent(CaptureEventKind.NXDomainFlood, CaptureEventSeverity.High, "test", now);
            gate.Filter(new[] { ev }, now);
            var fired2 = gate.Filter(new[] { ev }, now.AddSeconds(10));
            Assert.Empty(fired2);
        }

        [Fact]
        public void CaptureEventGate_AfterCooldown_FiresAgain()
        {
            var gate = new CaptureEventGate(cooldown: TimeSpan.FromSeconds(10));
            var now = DateTime.UtcNow;
            var ev = new CaptureEvent(CaptureEventKind.ServFailSpike, CaptureEventSeverity.High, "test", now);
            gate.Filter(new[] { ev }, now);
            var fired2 = gate.Filter(new[] { ev }, now.AddSeconds(11));
            Assert.Single(fired2);
        }

        [Fact]
        public void CaptureEventGate_DifferentKinds_IndependentCooldowns()
        {
            var gate = new CaptureEventGate();
            var now = DateTime.UtcNow;
            var flood = new CaptureEvent(CaptureEventKind.NXDomainFlood, CaptureEventSeverity.High, "flood", now);
            var spike = new CaptureEvent(CaptureEventKind.ServFailSpike, CaptureEventSeverity.High, "spike", now);
            var fired = gate.Filter(new[] { flood, spike }, now);
            Assert.Equal(2, fired.Count);
        }

        [Fact]
        public void CaptureEventGate_LastEmitted_TracksCorrectly()
        {
            var gate = new CaptureEventGate();
            var now = DateTime.UtcNow;
            Assert.Null(gate.LastEmitted(CaptureEventKind.PlainTextLeak));
            var ev = new CaptureEvent(CaptureEventKind.PlainTextLeak, CaptureEventSeverity.Critical, "leak", now);
            gate.Filter(new[] { ev }, now);
            Assert.Equal(now, gate.LastEmitted(CaptureEventKind.PlainTextLeak));
        }

        // ── Thresholds are pinned (tests break if someone drifts them) ───────────

        [Fact]
        public void Thresholds_AreConservative()
        {
            Assert.Equal(10, CaptureThresholds.NXDomainFloodMinCount);
            Assert.Equal(0.50, CaptureThresholds.NXDomainFloodMinRatio);
            Assert.Equal(5, CaptureThresholds.ServFailSpikeMinCount);
            Assert.Equal(0.25, CaptureThresholds.ServFailSpikeMinRatio);
            Assert.Equal(10, CaptureThresholds.ResponseLossMinOutbound);
            Assert.Equal(0.5, CaptureThresholds.ResponseLossMaxRatio);
            Assert.Equal(TimeSpan.FromSeconds(30), CaptureThresholds.DefaultWindow);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static string FormatTs(DateTime dt) =>
            dt.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

        private static DNSCaptureRecord MakeRecord(string timestamp, string direction, string rcode = "")
        {
            return new DNSCaptureRecord
            {
                Timestamp = timestamp,
                Network = new NetworkContextReport { Direction = direction },
                Packet = new DNSPacketReport { Rcode = rcode },
            };
        }
    }
}

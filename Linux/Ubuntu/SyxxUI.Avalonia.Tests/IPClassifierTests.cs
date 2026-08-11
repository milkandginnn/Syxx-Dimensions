using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    public class IPClassifierTests
    {
        [Theory]
        [InlineData("10.0.0.1", IPAddressClass.PrivateUse)]
        [InlineData("172.16.0.1", IPAddressClass.PrivateUse)]
        [InlineData("192.168.1.1", IPAddressClass.PrivateUse)]
        [InlineData("127.0.0.1", IPAddressClass.Loopback)]
        [InlineData("::1", IPAddressClass.Loopback)]
        [InlineData("100.64.0.1", IPAddressClass.CarrierGradeNAT)]
        [InlineData("169.254.1.1", IPAddressClass.LinkLocal)]
        [InlineData("192.0.2.1", IPAddressClass.Documentation)]
        [InlineData("198.51.100.1", IPAddressClass.Documentation)]
        [InlineData("203.0.113.1", IPAddressClass.Documentation)]
        [InlineData("2001:db8::1", IPAddressClass.Documentation)]
        [InlineData("8.8.8.8", IPAddressClass.PublicRoutable)]
        [InlineData("2606:4700:4700::1111", IPAddressClass.PublicRoutable)]
        [InlineData("not-an-ip", IPAddressClass.Invalid)]
        // E013 / B025 — the RFC 2544 benchmarking range is the whole 198.18.0.0/15
        // (198.18.0.0–198.19.255.255). The truncated `ip[1] == 18` test let the 198.19.x.x half leak
        // through as public; both halves must classify as special-use, and the neighbours stay public.
        [InlineData("198.18.0.1", IPAddressClass.PrivateUse)]
        [InlineData("198.18.255.254", IPAddressClass.PrivateUse)]
        [InlineData("198.19.0.1", IPAddressClass.PrivateUse)]
        [InlineData("198.19.255.254", IPAddressClass.PrivateUse)]
        [InlineData("198.17.0.1", IPAddressClass.PublicRoutable)]
        [InlineData("198.20.0.1", IPAddressClass.PublicRoutable)]
        // C013 — IPv4-mapped IPv6 (::ffff:0:0/96) keeps the embedded IPv4's class. Before the
        // mapped branch these all fell through to PublicRoutable, so a mapped sinkhole answer
        // was never flagged as blocking.
        [InlineData("::ffff:127.0.0.1", IPAddressClass.Loopback)]
        [InlineData("::ffff:10.0.0.1", IPAddressClass.PrivateUse)]
        [InlineData("::ffff:192.168.1.1", IPAddressClass.PrivateUse)]
        [InlineData("::ffff:198.19.0.1", IPAddressClass.PrivateUse)]
        [InlineData("::ffff:0.0.0.0", IPAddressClass.Unspecified)]
        // Failure case: a mapped PUBLIC address must stay public — the branch must not over-block.
        [InlineData("::ffff:8.8.8.8", IPAddressClass.PublicRoutable)]
        public void ClassifyIdentifiesRange(string ip, IPAddressClass expected)
        {
            Assert.Equal(expected, IPClassifier.Classify(ip));
        }

        [Theory]
        [InlineData("192.168.1.1", true)]
        [InlineData("127.0.0.1", true)]
        [InlineData("8.8.8.8", false)]
        // E013 / B025 — a proxy/sinkhole answer in 198.19.x.x must read as a blocking answer.
        [InlineData("198.19.0.1", true)]
        [InlineData("198.18.255.254", true)]
        // C013 — a mapped private/loopback answer is a blocking answer; a mapped public one is not.
        [InlineData("::ffff:192.168.1.1", true)]
        [InlineData("::ffff:127.0.0.1", true)]
        [InlineData("::ffff:8.8.8.8", false)]
        public void IsBlockingAnswerFlagsPrivateAndLoopback(string ip, bool expected)
        {
            Assert.Equal(expected, IPClassifier.IsBlockingAnswer(ip));
        }
    }

    public class ConditionStateTests
    {
        // (removed ThreeDistinctValues: HashSet{Yes,No,Unknown}.Count==3 is a compile-time enum
        //  property that can never RED — parity with the removed Swift testTriStateDescribesAllThreeCases.
        //  Real condition-state "Unknown" reachability is covered by the probe/classifier suites.)

        [Fact]
        public void LoopbackClassifiesAsLoopbackNotEncrypted()
        {
            // Regression coverage: loopback resolver must not be classified as "encrypted DNS".
            Assert.Equal(IPAddressClass.Loopback, IPClassifier.Classify("127.0.0.1"));
        }
    }

    public class SiteLocalResolverTests
    {
        [Fact]
        public void DeprecatedSiteLocalIsReservedNotPublic()
        {
            // Windows seeds fec0:0:0:ffff::1/2/3 as placeholder resolvers; they must never be
            // treated as public routable addresses (that produced phantom leak-probe resolvers).
            Assert.Equal(IPAddressClass.Reserved, IPClassifier.Classify("fec0:0:0:ffff::1"));
            Assert.NotEqual(IPAddressClass.PublicRoutable, IPClassifier.Classify("fec0:0:0:ffff::2"));
        }
    }
}

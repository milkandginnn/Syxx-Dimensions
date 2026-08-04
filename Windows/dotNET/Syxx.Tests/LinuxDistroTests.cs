using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // LinuxFamily resolution from /etc/os-release. The fixtures below are REAL os-release content
    // shapes, not invented ones — the parser's job is to survive what distros actually ship.
    //
    // The honesty rule under test: an unrecognised distro resolves to Unknown and NEVER defaults
    // into a family. Silently bucketing an unknown distro into Debian is the licensing analogue of
    // a false clean — a confident answer we did not earn, and one that would put a licence in the
    // wrong commercial bucket.
    public class LinuxDistroTests
    {
        // MARK: - The Debian family

        [Fact]
        public void Ubuntu_IsDebianFamily()
        {
            var d = LinuxDistros.Parse("""
                PRETTY_NAME="Ubuntu 24.04.1 LTS"
                NAME="Ubuntu"
                VERSION_ID="24.04"
                ID=ubuntu
                ID_LIKE=debian
                """);
            Assert.Equal(LinuxFamily.Debian, d.Family);
            Assert.Equal("ubuntu", d.Id);
            Assert.Equal("24.04", d.VersionId);
            Assert.Equal("ubuntu-24.04", d.ComponentId);
        }

        [Fact]
        public void Mint_IsDebianFamily_ViaAMultiTokenIdLike()
        {
            // Mint reports ID_LIKE="ubuntu debian" — two tokens, both Debian-family, so this must
            // resolve rather than trip the ambiguity guard.
            var d = LinuxDistros.Parse("""
                NAME="Linux Mint"
                VERSION_ID="22"
                ID=linuxmint
                ID_LIKE="ubuntu debian"
                PRETTY_NAME="Linux Mint 22"
                """);
            Assert.Equal(LinuxFamily.Debian, d.Family);
            Assert.Equal("linuxmint", d.Id);
            Assert.Equal("linuxmint-22", d.ComponentId);
        }

        [Fact]
        public void Kali_IsDebianFamily()
        {
            var d = LinuxDistros.Parse("""
                PRETTY_NAME="Kali GNU/Linux Rolling"
                ID=kali
                VERSION_ID="2024.3"
                ID_LIKE=debian
                """);
            Assert.Equal(LinuxFamily.Debian, d.Family);
        }

        [Fact]
        public void Debian_IsDebianFamily_DespiteHavingNoIdLike()
        {
            // The trap an ID_LIKE-only resolver falls into: Debian carries NO ID_LIKE, because it
            // is the thing everything else is "like". Resolving by ID-as-root is what catches it.
            var d = LinuxDistros.Parse("""
                PRETTY_NAME="Debian GNU/Linux 12 (bookworm)"
                NAME="Debian GNU/Linux"
                VERSION_ID="12"
                ID=debian
                """);
            Assert.Equal(LinuxFamily.Debian, d.Family);
        }

        // MARK: - The Arch family

        [Fact]
        public void Arch_IsArchFamily_DespiteHavingNoIdLike()
        {
            var d = LinuxDistros.Parse("""
                NAME="Arch Linux"
                PRETTY_NAME="Arch Linux"
                ID=arch
                BUILD_ID=rolling
                """);
            Assert.Equal(LinuxFamily.Arch, d.Family);
            Assert.Equal("arch", d.ComponentId);   // rolling release: no VERSION_ID
        }

        [Fact]
        public void Manjaro_IsArchFamily()
        {
            var d = LinuxDistros.Parse("""
                NAME="Manjaro Linux"
                ID=manjaro
                ID_LIKE=arch
                PRETTY_NAME="Manjaro Linux"
                """);
            Assert.Equal(LinuxFamily.Arch, d.Family);
        }

        [Fact]
        public void EndeavourOS_IsArchFamily()
        {
            var d = LinuxDistros.Parse("""
                NAME="EndeavourOS"
                ID=endeavouros
                ID_LIKE=arch
                """);
            Assert.Equal(LinuxFamily.Arch, d.Family);
        }

        // MARK: - SteamOS: the same NAME is in different families by version

        [Fact]
        public void SteamOS3_IsArchFamily_ButSteamOS2_IsDebianFamily()
        {
            // "Steam" is NOT stably one family. SteamOS 3 (Holo) is Arch-based; SteamOS 1/2 were
            // Debian-based. Keying entitlement to the NAME would put these two in the same bucket.
            // Resolving via ID_LIKE gets it right for free — which is exactly why we must not
            // hardcode a distro->family table by name.
            var holo = LinuxDistros.Parse("""
                NAME="SteamOS"
                ID=steamos
                ID_LIKE=arch
                VERSION_ID="3.5.7"
                """);
            var brewmaster = LinuxDistros.Parse("""
                NAME="SteamOS GNU/Linux"
                ID=steamos
                ID_LIKE=debian
                VERSION_ID="2"
                """);

            Assert.Equal(LinuxFamily.Arch, holo.Family);
            Assert.Equal(LinuxFamily.Debian, brewmaster.Family);
            Assert.NotEqual(holo.Family, brewmaster.Family);
        }

        // MARK: - Unknown must stay Unknown

        [Fact]
        public void AnUnrecognisedDistro_IsUnknown_NeverDefaultedIntoAFamily()
        {
            // The load-bearing test. Fedora is a real, popular distro in NEITHER family. It must
            // resolve Unknown so the caller handles it explicitly — not fall into Debian because
            // Debian is the common case.
            var d = LinuxDistros.Parse("""
                NAME="Fedora Linux"
                VERSION_ID="40"
                ID=fedora
                """);
            Assert.Equal(LinuxFamily.Unknown, d.Family);
            Assert.NotEqual(LinuxFamily.Debian, d.Family);
            // ...but we still RECORD which distro it was.
            Assert.Equal("fedora", d.Id);
            Assert.Equal("fedora-40", d.ComponentId);
        }

        [Fact]
        public void AnUnknownIdLikeToken_IsUnknown()
        {
            var d = LinuxDistros.Parse("ID=void\nID_LIKE=musl\n");
            Assert.Equal(LinuxFamily.Unknown, d.Family);
        }

        [Fact]
        public void ADistroClaimingBothFamilies_IsUnknown_NotACoinFlip()
        {
            // If os-release says a distro is like BOTH debian and arch, the family is not decidable
            // from os-release. Picking one would put a licence in a bucket on the strength of token
            // ORDER. Unknown is the honest answer.
            var d = LinuxDistros.Parse("ID=frankenstein\nID_LIKE=\"debian arch\"\n");
            Assert.Equal(LinuxFamily.Unknown, d.Family);
        }

        // MARK: - Malformed / hostile input never throws and never guesses

        [Theory]
        [InlineData("")]
        [InlineData("   \n\t\n")]
        [InlineData("garbage without an equals sign")]
        [InlineData("=novalue")]
        [InlineData("ID_LIKE=debian")]              // ID_LIKE but no ID -> nothing to record
        [InlineData("#ID=ubuntu\n# all commented")]
        [InlineData("\0\x01binary junk")]
        public void MalformedContent_IsUnreadable_NotAFamily(string text)
        {
            var d = LinuxDistros.Parse(text);
            Assert.Equal(LinuxFamily.Unknown, d.Family);
            Assert.Equal("", d.ComponentId);
        }

        [Fact]
        public void NullContent_IsUnreadable()
        {
            Assert.Equal(LinuxDistro.Unreadable, LinuxDistros.Parse(null));
        }

        [Fact]
        public void Detect_OnAMissingFile_IsUnreadable_NotAThrow()
        {
            // Off Linux (this build host) /etc/os-release does not exist. That must be an honest
            // "we do not know", not an exception and not a default family.
            var d = LinuxDistros.Detect("/nonexistent/path/os-release");
            Assert.Equal(LinuxDistro.Unreadable, d);
            Assert.Equal(LinuxFamily.Unknown, d.Family);
        }

        // MARK: - Parsing details os-release actually ships

        [Fact]
        public void QuotesAreStripped_ButOnlyMatchedPairs()
        {
            Assert.Equal("ubuntu", LinuxDistros.Parse("ID=\"ubuntu\"").Id);
            Assert.Equal("ubuntu", LinuxDistros.Parse("ID='ubuntu'").Id);
            // A stray quote is part of the value, not a delimiter to be silently eaten.
            Assert.Equal("\"ubuntu", LinuxDistros.Parse("ID=\"ubuntu").Id);
        }

        [Fact]
        public void IdIsCaseNormalised_SoComponentIdsAreStable()
        {
            Assert.Equal("ubuntu", LinuxDistros.Parse("ID=Ubuntu").Id);
            Assert.Equal(LinuxFamily.Debian, LinuxDistros.Parse("ID=Debian").Family);
        }

        [Fact]
        public void CommentsAndBlankLinesAreIgnored()
        {
            var d = LinuxDistros.Parse("""
                # this is a comment

                ID=ubuntu
                # ID=arch
                ID_LIKE=debian
                """);
            Assert.Equal(LinuxFamily.Debian, d.Family);
            Assert.Equal("ubuntu", d.Id);
        }
    }
}

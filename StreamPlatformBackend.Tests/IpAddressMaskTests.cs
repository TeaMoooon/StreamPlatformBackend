using StreamPlatformBackend.Services;
using Xunit;

namespace StreamPlatformBackend.Tests
{
    public class IpAddressMaskTests
    {
        [Theory]
        [InlineData("192.168.1.100", "*.*.1.100")]
        [InlineData("10.0.0.1", "*.*.0.1")]
        [InlineData("203.0.113.50", "*.*.113.50")]
        [InlineData("unknown", "unknown")]
        [InlineData("", "unknown")]
        [InlineData(null, "unknown")]
        [InlineData("not-an-ip", "*.*.*.*")]
        public void Mask_ShouldHideLeadingIpv4Octets(string? input, string expected)
        {
            Assert.Equal(expected, IpAddressMask.Mask(input));
        }

        [Fact]
        public void Mask_ShouldKeepOnlyLastIpv6Hextet()
        {
            var masked = IpAddressMask.Mask("2001:db8::1");
            Assert.EndsWith(":1", masked);
            Assert.StartsWith("****:", masked);
            Assert.DoesNotContain("2001", masked);
            Assert.DoesNotContain("db8", masked);
        }
    }
}

using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;
using static PortAtlas.Tests.Export.FirewallMatrixSamples;

namespace PortAtlas.Tests.Export;

public sealed class AuditedServerDirectoryTests
{
    [Fact]
    public void FromEntries_WhenALogHoldsALocalAddress_NamesTheServerOfThatLog()
    {
        // Act
        var directory = AuditedServerDirectory.FromEntries([Entry("nas", "192.168.1.50")]);

        // Assert
        directory.TryGetServerName("192.168.1.50", out var name).Should().BeTrue();
        name.Should().Be("nas");
        directory.TryGetServerName("192.168.1.10", out var unknown).Should().BeFalse();
        unknown.Should().BeEmpty();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("::1")]
    public void FromEntries_WhenTheLocalAddressNamesNoMachine_LeavesItOut(string localIp)
    {
        // Act
        var directory = AuditedServerDirectory.FromEntries([Entry("nas", localIp)]);

        // Assert
        directory.TryGetServerName(localIp, out _).Should().BeFalse();
    }

    [Fact]
    public void FromEntries_WhenAnAddressMovedToAnotherServer_KeepsTheServerThatHeldItLastWhateverTheOrder()
    {
        // Arrange
        var old = Entry("old-nas", "192.168.1.50", Monday);
        var recent = Entry("nas", "192.168.1.50", Monday.AddDays(1));

        // Act
        var inOrder = AuditedServerDirectory.FromEntries([old, recent]);
        var reversed = AuditedServerDirectory.FromEntries([recent, old]);

        // Assert
        inOrder.TryGetServerName("192.168.1.50", out var first).Should().BeTrue();
        reversed.TryGetServerName("192.168.1.50", out var second).Should().BeTrue();
        first.Should().Be("nas");
        second.Should().Be("nas");
    }
}

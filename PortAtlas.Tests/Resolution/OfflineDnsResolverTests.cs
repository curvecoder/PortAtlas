using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Resolution;

public sealed class OfflineDnsResolverTests
{
    private static readonly IPAddress HomeServer = IPAddress.Parse("192.168.1.20");
    private static readonly IPAddress PublicServer = IPAddress.Parse("52.123.242.181");
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
    private static readonly IpOwnerMetadata Owner = new("Example Networks", "US", "52.112.0.0 - 52.127.255.255", "NET-EXAMPLE");

    [Fact]
    public async Task ResolveSingleAsync_WhenDnsDoesNotAnswerInTime_ReturnsNoEntry()
    {
        // Arrange
        var resolver = CreateResolver(ReverseDns.Hangs());

        // Act
        var entry = await resolver.ResolveSingleAsync(HomeServer);

        // Assert
        entry.Should().BeNull("a DNS server that did not answer in time has not said that the address has no name");
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenDnsReportsATemporaryFailure_ReturnsNoEntry()
    {
        // Arrange
        var resolver = CreateResolver(ReverseDns.Fails(SocketError.TryAgain));

        // Act
        var entry = await resolver.ResolveSingleAsync(HomeServer);

        // Assert
        entry.Should().BeNull();
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenNoPtrRecordAndRdapDoesNotAnswer_ReturnsNoEntry()
    {
        // Arrange
        var resolver = CreateResolver(ReverseDns.Fails(SocketError.HostNotFound), OwnerLookup.Unavailable());

        // Act
        var entry = await resolver.ResolveSingleAsync(PublicServer);

        // Assert
        entry.Should().BeNull("an RDAP registry that did not answer has not said that the address has no owner");
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenDnsDoesNotAnswerInTimeAndNoRegisteredOwner_ReturnsNoEntry()
    {
        // Arrange
        var resolver = CreateResolver(ReverseDns.Hangs(), OwnerLookup.Returns(null));

        // Act
        var entry = await resolver.ResolveSingleAsync(PublicServer);

        // Assert
        entry.Should().BeNull("the address may still have a name that the DNS server did not give in time");
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenDnsDoesNotAnswerInTimeAndRdapKnowsTheOwner_ReturnsTheOwner()
    {
        // Arrange
        var resolver = CreateResolver(ReverseDns.Hangs(), OwnerLookup.Returns(Owner));

        // Act
        var entry = await resolver.ResolveSingleAsync(PublicServer);

        // Assert
        entry.Should().NotBeNull();
        entry!.HostName.Should().Be("Example Networks");
        entry.Source.Should().Be("RDAP");
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenPtrNameFoundAndRdapDoesNotAnswer_ReturnsTheNameWithoutOrganization()
    {
        // Arrange
        var resolver = CreateResolver(ReverseDns.Returns("edge.example.test"), OwnerLookup.Unavailable());

        // Act
        var entry = await resolver.ResolveSingleAsync(PublicServer);

        // Assert
        entry.Should().NotBeNull();
        entry!.HostName.Should().Be("edge.example.test");
        entry.Source.Should().Be("DNS");
        entry.Organization.Should().BeNull();
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenNoPtrRecordAndNoRegisteredOwner_ReturnsANoNameEntryStampedByTheClock()
    {
        // Arrange
        var resolver = CreateResolver(ReverseDns.Fails(SocketError.HostNotFound), OwnerLookup.Returns(null));

        // Act
        var entry = await resolver.ResolveSingleAsync(PublicServer);

        // Assert
        entry.Should().NotBeNull();
        entry!.HostName.Should().BeEmpty();
        entry.Source.Should().Be("None");
        entry.ResolvedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenPtrNameFound_StampsTheEntryWithTheClock()
    {
        // Arrange
        var resolver = CreateResolver(ReverseDns.Returns("nas.home.arpa"));

        // Act
        var entry = await resolver.ResolveSingleAsync(HomeServer);

        // Assert
        entry.Should().NotBeNull();
        entry!.ResolvedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenCallerCancelsDuringTheDnsLookup_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var resolver = CreateResolver(ReverseDns.CancelsCallerThenHangs(cts), dnsTimeout: TimeSpan.FromMinutes(1));

        // Act
        var act = () => resolver.ResolveSingleAsync(HomeServer, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenCallerCancelsDuringTheRdapLookup_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var resolver = CreateResolver(ReverseDns.Fails(SocketError.HostNotFound), OwnerLookup.CancelsCallerThenHangs(cts));

        // Act
        var act = () => resolver.ResolveSingleAsync(PublicServer, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenLoopbackAddress_ReturnsLocalSourceImmediately()
    {
        // Arrange
        var resolver = new OfflineDnsResolver();

        // Act
        var entry = await resolver.ResolveSingleAsync(IPAddress.Loopback);

        // Assert
        entry.Should().NotBeNull();
        entry!.HostName.Should().Be("127.0.0.1");
        entry.Source.Should().Be("Local");
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenUnspecifiedIpv4Address_ReturnsExcludedSourceImmediatelyWithoutCallingDns()
    {
        // Arrange
        var resolver = new OfflineDnsResolver();

        // Act
        var entry = await resolver.ResolveSingleAsync(IPAddress.Any);

        // Assert
        entry.Should().NotBeNull();
        entry!.HostName.Should().BeEmpty();
        entry.Source.Should().Be("Excluded");
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenUnspecifiedIpv6Address_ReturnsExcludedSourceImmediatelyWithoutCallingDns()
    {
        // Arrange
        var resolver = new OfflineDnsResolver();

        // Act
        var entry = await resolver.ResolveSingleAsync(IPAddress.IPv6Any);

        // Assert
        entry.Should().NotBeNull();
        entry!.HostName.Should().BeEmpty();
        entry.Source.Should().Be("Excluded");
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenBroadcastAddress_ReturnsExcludedSourceImmediatelyWithoutCallingDns()
    {
        // Arrange
        var resolver = new OfflineDnsResolver();

        // Act
        var entry = await resolver.ResolveSingleAsync(IPAddress.None);

        // Assert
        entry.Should().NotBeNull();
        entry!.HostName.Should().BeEmpty();
        entry.Source.Should().Be("Excluded");
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenRdapReturnsOrganization_PopulatesEntryWithRdapSource()
    {
        // Arrange
        var targetIp = IPAddress.Parse("52.123.242.181");
        var metadata = new IpOwnerMetadata(
            Organization: "Microsoft Corporation",
            Country: "US",
            NetworkRange: "52.112.0.0 - 52.127.255.255",
            Handle: "NET-52-112-0-0-1");

        var stubOrgResolver = new StubIpOrganizationResolver(targetIp, metadata);

        var loggedMessages = new List<string>();
        var resolver = new OfflineDnsResolver(
            organizationResolver: stubOrgResolver,
            rdapThrottleDelay: TimeSpan.FromMilliseconds(10),
            logThrottlingNotice: loggedMessages.Add);

        // Act
        var entry = await resolver.ResolveSingleAsync(targetIp);

        // Assert
        entry.Should().NotBeNull();
        entry!.Organization.Should().Be("Microsoft Corporation");
        entry.Country.Should().Be("US");
        loggedMessages.Should().Contain(msg => msg.Contains("rate limiting"));
    }

    [Fact]
    public async Task ResolveSingleAsync_WhenPublicIpWithDnsHostName_AlsoQueriesRdapToEnrichOrganization()
    {
        // Arrange: 8.8.8.8 has a well-known reverse DNS PTR ("dns.google")
        var targetIp = IPAddress.Parse("8.8.8.8");
        var metadata = new IpOwnerMetadata(
            Organization: "Google LLC",
            Country: "US",
            NetworkRange: "8.8.8.0/24",
            Handle: "GOGL");

        var stubOrgResolver = new StubIpOrganizationResolver(targetIp, metadata);
        var resolver = new OfflineDnsResolver(
            organizationResolver: stubOrgResolver,
            rdapThrottleDelay: TimeSpan.FromMilliseconds(5));

        // Act
        var entry = await resolver.ResolveSingleAsync(targetIp);

        // Assert
        entry.Should().NotBeNull();
        entry!.Organization.Should().Be("Google LLC");
        entry.Country.Should().Be("US");
        entry.NetworkRange.Should().Be("8.8.8.0/24");
        stubOrgResolver.CallCount.Should().Be(1);
    }

    private static OfflineDnsResolver CreateResolver(
        IReverseDnsLookup dns,
        IIpOrganizationResolver? owners = null,
        TimeSpan? dnsTimeout = null) =>
        new(
            organizationResolver: owners,
            rdapThrottleDelay: TimeSpan.Zero,
            resolutionTimeout: dnsTimeout ?? TimeSpan.FromMilliseconds(50),
            reverseDnsLookup: dns,
            timeProvider: new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ReverseDns(Func<CancellationToken, Task<string>> answer) : IReverseDnsLookup
    {
        public static ReverseDns Returns(string hostName) => new(_ => Task.FromResult(hostName));

        public static ReverseDns Fails(SocketError error) => new(_ => Task.FromException<string>(new SocketException((int)error)));

        // A DNS server that never answers: only the cancellation of the lookup ends the wait.
        public static ReverseDns Hangs() => new(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return string.Empty;
        });

        public static ReverseDns CancelsCallerThenHangs(CancellationTokenSource caller) => new(async token =>
        {
            await caller.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return string.Empty;
        });

        public Task<string> LookupAsync(IPAddress address, CancellationToken cancellationToken) => answer(cancellationToken);
    }

    private sealed class OwnerLookup(Func<CancellationToken, Task<IpOwnerMetadata?>> answer) : IIpOrganizationResolver
    {
        public static OwnerLookup Returns(IpOwnerMetadata? owner) => new(_ => Task.FromResult(owner));

        public static OwnerLookup Unavailable() =>
            new(_ => Task.FromException<IpOwnerMetadata?>(new IpOwnerLookupUnavailableException("RDAP registry did not answer.")));

        public static OwnerLookup CancelsCallerThenHangs(CancellationTokenSource caller) => new(async token =>
        {
            await caller.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });

        public async Task<string?> ResolveOrganizationAsync(IPAddress address, CancellationToken cancellationToken = default) =>
            (await answer(cancellationToken))?.Organization;

        public Task<IpOwnerMetadata?> ResolveOwnerDetailsAsync(IPAddress address, CancellationToken cancellationToken = default) =>
            answer(cancellationToken);

        public void Dispose()
        {
            // Nothing to release: the answer is a delegate.
        }
    }

    private sealed class StubIpOrganizationResolver(IPAddress expectedIp, IpOwnerMetadata metadata) : IIpOrganizationResolver
    {
        public int CallCount { get; private set; }

        public Task<string?> ResolveOrganizationAsync(IPAddress address, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(address.Equals(expectedIp) ? metadata.Organization : null);
        }

        public Task<IpOwnerMetadata?> ResolveOwnerDetailsAsync(IPAddress address, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(address.Equals(expectedIp) ? metadata : null);
        }

        public void Dispose()
        {
        }
    }
}

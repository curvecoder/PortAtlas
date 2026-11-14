using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Resolution;

public sealed class RdapIpOrganizationResolverTests
{
    private sealed class DelegatingTestHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }

    [Fact]
    public async Task ResolveOrganizationAsync_WhenAddressIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        using var resolver = new RdapIpOrganizationResolver();

        // Act
        var act = () => resolver.ResolveOrganizationAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.1.25")]
    [InlineData("192.168.1.1")]
    [InlineData("172.16.5.10")]
    [InlineData("169.254.1.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    public async Task ResolveOrganizationAsync_WhenAddressIsPrivateOrLoopback_ReturnsNullImmediatelyWithoutHttpCall(string ipString)
    {
        // Arrange
        var httpCalled = false;
        var handler = new DelegatingTestHandler(_ =>
        {
            httpCalled = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var client = new HttpClient(handler);
        using var resolver = new RdapIpOrganizationResolver(client);
        var ip = IPAddress.Parse(ipString);

        // Act
        var result = await resolver.ResolveOrganizationAsync(ip);

        // Assert
        result.Should().BeNull();
        httpCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveOrganizationAsync_WhenHttpReturnsValidRdapJson_ReturnsExtractedOrganization()
    {
        // Arrange
        var validJson = """
        {
          "name": "MSFT",
          "entities": [
            {
              "roles": [ "registrant" ],
              "vcardArray": [ "vcard", [ [ "fn", {}, "text", "Microsoft Corporation" ] ] ]
            }
          ]
        }
        """;

        var handler = new DelegatingTestHandler(req =>
        {
            req.RequestUri!.ToString().Should().Contain("52.123.242.181");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(validJson)
            };
        });

        using var client = new HttpClient(handler);
        using var resolver = new RdapIpOrganizationResolver(client);
        var ip = IPAddress.Parse("52.123.242.181");

        // Act
        var result = await resolver.ResolveOrganizationAsync(ip);

        // Assert
        result.Should().Be("Microsoft Corporation");
    }

    [Fact]
    public async Task ResolveOwnerDetailsAsync_WhenHttpThrowsNetworkException_ThrowsIpOwnerLookupUnavailableException()
    {
        // Arrange
        var handler = new DelegatingTestHandler(_ => throw new HttpRequestException("Network unreachable in air-gapped server"));

        using var client = new HttpClient(handler);
        using var resolver = new RdapIpOrganizationResolver(client, circuitBreakerThreshold: 2);
        var ip = IPAddress.Parse("52.123.242.181");

        // Act
        var act = () => resolver.ResolveOwnerDetailsAsync(ip);

        // Assert
        await act.Should().ThrowAsync<IpOwnerLookupUnavailableException>()
            .WithInnerException(typeof(HttpRequestException));
    }

    [Fact]
    public async Task ResolveOwnerDetailsAsync_WhenCircuitBreakerTrips_ThrowsUnavailableWithoutHttpCall()
    {
        // Arrange: Threshold set to 2 consecutive errors
        var httpCallCount = 0;
        var handler = new DelegatingTestHandler(_ =>
        {
            httpCallCount++;
            throw new HttpRequestException("Connection refused (offline)");
        });

        using var client = new HttpClient(handler);
        using var resolver = new RdapIpOrganizationResolver(client, circuitBreakerThreshold: 2, circuitBreakerCooldown: TimeSpan.FromMinutes(5));
        var ip1 = IPAddress.Parse("52.123.242.181");
        var ip2 = IPAddress.Parse("52.123.242.182");
        var ip3 = IPAddress.Parse("52.123.242.183");
        await AttemptLookupAsync(resolver, ip1);
        await AttemptLookupAsync(resolver, ip2);

        // Act
        var act = () => resolver.ResolveOwnerDetailsAsync(ip3);

        // Assert
        await act.Should().ThrowAsync<IpOwnerLookupUnavailableException>();
        httpCallCount.Should().Be(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ResolveOwnerDetailsAsync_WhenServerAnswersErrorStatus_ThrowsIpOwnerLookupUnavailableException(HttpStatusCode status)
    {
        // Arrange
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(status));
        using var client = new HttpClient(handler);
        using var resolver = new RdapIpOrganizationResolver(client);

        // Act
        var act = () => resolver.ResolveOwnerDetailsAsync(IPAddress.Parse("52.123.242.181"));

        // Assert
        await act.Should().ThrowAsync<IpOwnerLookupUnavailableException>();
    }

    [Fact]
    public async Task ResolveOwnerDetailsAsync_WhenServerKeepsAnsweringErrorStatus_OpensCircuitBreaker()
    {
        // Arrange
        var httpCallCount = 0;
        var handler = new DelegatingTestHandler(_ =>
        {
            httpCallCount++;
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        });
        using var client = new HttpClient(handler);
        using var resolver = new RdapIpOrganizationResolver(client, circuitBreakerThreshold: 2, circuitBreakerCooldown: TimeSpan.FromMinutes(5));
        await AttemptLookupAsync(resolver, IPAddress.Parse("52.123.242.181"));
        await AttemptLookupAsync(resolver, IPAddress.Parse("52.123.242.182"));

        // Act
        var act = () => resolver.ResolveOwnerDetailsAsync(IPAddress.Parse("52.123.242.183"));

        // Assert
        await act.Should().ThrowAsync<IpOwnerLookupUnavailableException>();
        httpCallCount.Should().Be(2);
    }

    [Fact]
    public async Task ResolveOwnerDetailsAsync_WhenServerAnswersNotFound_ReturnsNull()
    {
        // Arrange
        var handler = new DelegatingTestHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        using var resolver = new RdapIpOrganizationResolver(client);

        // Act
        var result = await resolver.ResolveOwnerDetailsAsync(IPAddress.Parse("52.123.242.181"));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOwnerDetailsAsync_WhenRequestExceedsTimeout_ThrowsIpOwnerLookupUnavailableException()
    {
        // Arrange
        using var client = new HttpClient(new HangingTestHandler());
        using var resolver = new RdapIpOrganizationResolver(client, requestTimeout: TimeSpan.FromMilliseconds(50));

        // Act
        var act = () => resolver.ResolveOwnerDetailsAsync(IPAddress.Parse("52.123.242.181"));

        // Assert
        await act.Should().ThrowAsync<IpOwnerLookupUnavailableException>();
    }

    [Fact]
    public async Task ResolveOwnerDetailsAsync_WhenCallerCancels_ThrowsOperationCanceledException()
    {
        // Arrange
        using var client = new HttpClient(new HangingTestHandler());
        using var resolver = new RdapIpOrganizationResolver(client, requestTimeout: TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act
        var act = () => resolver.ResolveOwnerDetailsAsync(IPAddress.Parse("52.123.242.181"), cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static async Task AttemptLookupAsync(RdapIpOrganizationResolver resolver, IPAddress address)
    {
        try
        {
            await resolver.ResolveOwnerDetailsAsync(address);
        }
        catch (IpOwnerLookupUnavailableException)
        {
            // The failed lookup is what the test sets up: the circuit breaker counts it.
        }
    }

    private sealed class HangingTestHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

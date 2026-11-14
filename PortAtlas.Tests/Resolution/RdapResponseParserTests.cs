using AwesomeAssertions;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Resolution;

public sealed class RdapResponseParserTests
{
    [Fact]
    public void ExtractOrganization_WhenJsonHasRegistrantVcard_ReturnsRegistrantName()
    {
        // Arrange
        var json = """
        {
          "name": "MSFT",
          "entities": [
            {
              "roles": [ "technical" ],
              "vcardArray": [ "vcard", [ [ "fn", {}, "text", "Prachi Singh" ] ] ]
            },
            {
              "roles": [ "registrant" ],
              "vcardArray": [ "vcard", [ [ "fn", {}, "text", "Microsoft Corporation" ] ] ]
            }
          ]
        }
        """;

        // Act
        var result = RdapResponseParser.ExtractOrganization(json);

        // Assert
        result.Should().Be("Microsoft Corporation");
    }

    [Fact]
    public void ExtractOwnerDetails_WhenJsonHasFullDetails_ReturnsCompleteMetadata()
    {
        // Arrange
        var json = """
        {
          "name": "MSFT",
          "handle": "NET-52-112-0-0-1",
          "startAddress": "52.112.0.0",
          "endAddress": "52.127.255.255",
          "country": "US",
          "entities": [
            {
              "roles": [ "registrant" ],
              "vcardArray": [ "vcard", [ [ "fn", {}, "text", "Microsoft Corporation" ] ] ]
            }
          ]
        }
        """;

        // Act
        var details = RdapResponseParser.ExtractOwnerDetails(json);

        // Assert
        details.Should().NotBeNull();
        details!.Organization.Should().Be("Microsoft Corporation");
        details.Country.Should().Be("US");
        details.NetworkRange.Should().Be("52.112.0.0 - 52.127.255.255");
        details.Handle.Should().Be("NET-52-112-0-0-1");
    }

    [Fact]
    public void ExtractOrganization_WhenJsonHasOnlyNameProperty_ReturnsName()
    {
        // Arrange
        var json = """
        {
          "name": "CLOUDFLARE-NET",
          "entities": []
        }
        """;

        // Act
        var result = RdapResponseParser.ExtractOrganization(json);

        // Assert
        result.Should().Be("CLOUDFLARE-NET");
    }

    [Fact]
    public void ExtractOrganization_WhenJsonHasOrgPropertyInVcard_ReturnsOrgName()
    {
        // Arrange
        var json = """
        {
          "entities": [
            {
              "roles": [ "administrative" ],
              "vcardArray": [
                "vcard",
                [
                  [ "fn", {}, "text", "John Doe" ],
                  [ "org", {}, "text", "Acme Corporation" ]
                ]
              ]
            }
          ]
        }
        """;

        // Act
        var result = RdapResponseParser.ExtractOrganization(json);

        // Assert
        result.Should().Be("Acme Corporation");
    }

    [Fact]
    public void ExtractOrganization_WhenJsonHasNestedEntities_ReturnsNestedRegistrant()
    {
        // Arrange
        var json = """
        {
          "entities": [
            {
              "roles": [ "noc" ],
              "entities": [
                {
                  "roles": [ "registrant" ],
                  "vcardArray": [ "vcard", [ [ "fn", {}, "text", "Google LLC" ] ] ]
                }
              ]
            }
          ]
        }
        """;

        // Act
        var result = RdapResponseParser.ExtractOrganization(json);

        // Assert
        result.Should().Be("Google LLC");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not valid json")]
    [InlineData("{}")]
    public void ExtractOrganization_WhenJsonIsNullOrEmptyOrInvalid_ReturnsNull(string? invalidJson)
    {
        // Act
        var result = invalidJson is not null
            ? RdapResponseParser.ExtractOrganization(invalidJson)
            : RdapResponseParser.ExtractOrganization(string.Empty);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "entities": [ { "roles": [ "registrant" ], "vcardArray": [ "vcard", [ [ 42, {}, "text", "Wrong" ], [ "fn", {}, "text", "Example Networks" ] ] ] } ] }""")]
    [InlineData("""{ "entities": [ { "roles": [ "registrant" ], "vcardArray": [ "vcard", [ [ "org", {}, "text", [ "Example Holding", "Division" ] ], [ "fn", {}, "text", "Example Networks" ] ] ] } ] }""")]
    [InlineData("""{ "entities": [ { "roles": [ "registrant" ], "vcardArray": [ "vcard", [ [ "fn", {}, "text", 7 ], [ "org", {}, "text", "Example Networks" ] ] ] } ] }""")]
    [InlineData("""{ "entities": [ { "roles": [ "registrant" ], "vcardArray": [ "vcard", [ [ "org", {}, "text", "Example Networks" ], [ "org", {}, "text", [ "Example Holding" ] ], [ "fn", {}, "text", "Wrong" ] ] ] } ] }""")]
    [InlineData("""{ "entities": [ { "roles": [ 3, "registrant" ], "vcardArray": [ "vcard", [ [ "fn", {}, "text", "Example Networks" ] ] ] } ] }""")]
    [InlineData("""{ "entities": [ "not an entity", { "roles": [ "registrant" ], "vcardArray": [ "vcard", [ [ "fn", {}, "text", "Example Networks" ] ] ] } ] }""")]
    public void ExtractOrganization_WhenVcardHoldsUnexpectedValueTypes_SkipsThemAndReturnsTheTextName(string json)
    {
        // Act
        var act = () => RdapResponseParser.ExtractOrganization(json);

        // Assert
        act.Should().NotThrow().Which.Should().Be("Example Networks");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public void ExtractOwnerDetails_WhenRootIsNotAnObject_ReturnsNull(string json)
    {
        // Act
        var act = () => RdapResponseParser.ExtractOwnerDetails(json);

        // Assert
        act.Should().NotThrow().Which.Should().BeNull();
    }
}

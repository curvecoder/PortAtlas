using AwesomeAssertions;
using PortAtlas.Cli.Configuration;
using PortAtlas.Core.Enrichment;
using Xunit;

namespace PortAtlas.Tests.Configuration;

// The files written without a configured path are named after the product since 5.0; 4.x wrote netflow_* files.
public sealed class DefaultFileNameTests
{
    [Fact]
    public void AppConfiguration_WhenCreatedWithoutSettings_NamesTheAuditLogAndTheDnsCacheAfterPortAtlas()
    {
        // Act
        var configuration = new AppConfiguration();

        // Assert
        configuration.OutputCsvPath.Should().Be("portatlas_audit.csv");
        configuration.DnsCacheFilePath.Should().Be("portatlas_dns_cache.json");
    }

    [Fact]
    public void OfflineAuditEnrichmentOptions_WhenCreatedWithoutSettings_NamesTheMergedCacheAfterPortAtlas()
    {
        // Act
        var options = new OfflineAuditEnrichmentOptions();

        // Assert
        options.OutputCachePath.Should().Be("portatlas_dns_cache.json");
    }

    [Fact]
    public void Load_WhenFileIsTheTemplateShippedWithTheExecutable_NamesTheFilesAfterPortAtlas()
    {
        // Arrange
        var template = Path.Combine(AppContext.BaseDirectory, AppConfigurationFile.DefaultPath);

        // Act
        var loaded = AppConfigurationFile.Load(template);

        // Assert
        loaded.Configuration.OutputCsvPath.Should().Be("portatlas_audit.csv");
        loaded.Configuration.DnsCacheFilePath.Should().Be("portatlas_dns_cache.json");
    }
}

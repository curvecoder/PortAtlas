using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Resolution;

public sealed class PersistentDnsCacheStoreTests : IAsyncLifetime
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "PortAtlas_DnsCache_" + Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_testDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task SaveAsync_WhenAReaderIsHalfwayThroughTheFile_ReplacesItWithoutChangingWhatTheReaderGets()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "dns_cache.json");
        var original = Encoding.UTF8.GetBytes("""{ "192.168.1.10": { "HostName": "nas.home.arpa", "Source": "DNS" } }""");
        await File.WriteAllBytesAsync(filePath, original);
        var store = new PersistentDnsCacheStore(filePath);
        await using var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
        var firstHalf = new byte[original.Length / 2];
        reader.ReadExactly(firstHalf);

        // Act
        await store.SaveAsync(TwoEntryCache());
        using var rest = new MemoryStream();
        await reader.CopyToAsync(rest);

        // Assert
        firstHalf.Concat(rest.ToArray()).Should().Equal(original);
        store.Load().Keys.Should().BeEquivalentTo("192.168.1.10", "192.168.1.20");
    }

    [Fact]
    public void Save_WhenAReaderIsHalfwayThroughTheFile_ReplacesItWithoutChangingWhatTheReaderGets()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "dns_cache.json");
        var original = Encoding.UTF8.GetBytes("""{ "192.168.1.10": { "HostName": "nas.home.arpa", "Source": "DNS" } }""");
        File.WriteAllBytes(filePath, original);
        var store = new PersistentDnsCacheStore(filePath);
        using var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
        var firstHalf = new byte[original.Length / 2];
        reader.ReadExactly(firstHalf);

        // Act
        store.Save(TwoEntryCache());
        using var rest = new MemoryStream();
        reader.CopyTo(rest);

        // Assert
        firstHalf.Concat(rest.ToArray()).Should().Equal(original);
        store.Load().Keys.Should().BeEquivalentTo("192.168.1.10", "192.168.1.20");
    }

    private static Dictionary<string, DnsCacheEntry> TwoEntryCache() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["192.168.1.10"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "nas.home.arpa", Source = "DNS" },
        ["192.168.1.20"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "pve.home.arpa", Source = "DNS" }
    };

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsEmptyDictionary()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "non_existent.json");
        var store = new PersistentDnsCacheStore(filePath);

        // Act
        var loaded = store.Load();

        // Assert
        loaded.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAsync_WhenRichEntriesSupplied_PersistsJsonAndCanBeReloaded()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "dns_cache.json");
        var store = new PersistentDnsCacheStore(filePath);
        var expected = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["140.82.121.4"] = new()
            {
                ResolvedAtUtc = DateTimeOffset.UtcNow,
                HostName = "lb-140-82-121-4-iad.github.com",
                Source = "DNS"
            },
            ["52.123.242.181"] = new()
            {
                ResolvedAtUtc = DateTimeOffset.UtcNow,
                HostName = "Microsoft Corporation",
                Organization = "Microsoft Corporation",
                Country = "US",
                NetworkRange = "52.112.0.0 - 52.127.255.255",
                Source = "RDAP"
            },
            ["192.0.2.1"] = new()
            {
                ResolvedAtUtc = DateTimeOffset.UtcNow,
                HostName = "",
                Source = "None"
            }
        };

        // Act
        await store.SaveAsync(expected);
        var loaded = store.Load();

        // Assert
        loaded.Should().HaveCount(3);
        loaded["140.82.121.4"].HostName.Should().Be("lb-140-82-121-4-iad.github.com");
        loaded["140.82.121.4"].Source.Should().Be("DNS");

        loaded["52.123.242.181"].HostName.Should().Be("Microsoft Corporation");
        loaded["52.123.242.181"].Organization.Should().Be("Microsoft Corporation");
        loaded["52.123.242.181"].Country.Should().Be("US");
        loaded["52.123.242.181"].NetworkRange.Should().Be("52.112.0.0 - 52.127.255.255");
        loaded["52.123.242.181"].Source.Should().Be("RDAP");

        loaded["192.0.2.1"].HostName.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WhenLegacyStringJsonFormatProvided_LoadsEntriesWithBackwardCompatibility()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "legacy_cache.json");
        const string legacyJson = """
        {
          "52.123.242.181": "Microsoft Corporation",
          "8.8.8.8": "dns.google",
          "10.0.0.1": ""
        }
        """;
        await File.WriteAllTextAsync(filePath, legacyJson);
        var store = new PersistentDnsCacheStore(filePath);

        // Act
        var loaded = store.Load();

        // Assert
        loaded.Should().HaveCount(3);
        loaded["52.123.242.181"].HostName.Should().Be("Microsoft Corporation");
        loaded["8.8.8.8"].HostName.Should().Be("dns.google");
        loaded["10.0.0.1"].HostName.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WhenLegacyStringJsonFormatProvided_DatesTheEntriesWithTheGivenClock()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "legacy_dated_cache.json");
        await File.WriteAllTextAsync(filePath, """{ "8.8.8.8": "dns.google" }""");
        var loadTime = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var store = new PersistentDnsCacheStore(filePath, new FakeTimeProvider(loadTime));

        // Act
        var loaded = store.Load();

        // Assert
        loaded["8.8.8.8"].ResolvedAtUtc.Should().Be(loadTime);
    }

    [Fact]
    public async Task Load_WhenAnEntryHasNoResolutionTime_KeepsItDatedWithTheGivenClock()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "undated_cache.json");
        await File.WriteAllTextAsync(filePath, """{ "192.168.1.10": { "HostName": "nas.home.arpa", "Source": "DNS" } }""");
        var loadTime = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var store = new PersistentDnsCacheStore(filePath, new FakeTimeProvider(loadTime));

        // Act
        var loaded = store.Load();

        // Assert
        loaded.Should().ContainKey("192.168.1.10");
        loaded["192.168.1.10"].HostName.Should().Be("nas.home.arpa");
        loaded["192.168.1.10"].ResolvedAtUtc.Should().Be(loadTime);
    }

    [Fact]
    public async Task Load_WhenSomeEntriesAreBadlyTyped_SkipsThemAndKeepsTheOthers()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "mixed_cache.json");
        const string mixedJson = """
        {
          "192.168.1.10": { "HostName": "nas.home.arpa", "Source": "DNS", "ResolvedAtUtc": "2026-11-01T08:00:00+00:00" },
          "192.168.1.11": { "HostName": "pihole.home.arpa", "Source": "DNS", "ResolvedAtUtc": "yesterday" },
          "192.168.1.12": { "HostName": 42, "Source": "DNS" },
          "192.168.1.13": { "HostName": null, "Source": "DNS" },
          "192.168.1.14": { "HostName": "pve.home.arpa", "Source": null },
          "192.168.1.15": { "HostName": "printer.home.arpa", "Source": "DNS", "ResolvedAtUtc": "2026-11-01T08:00:00+00:00" }
        }
        """;
        await File.WriteAllTextAsync(filePath, mixedJson);
        var store = new PersistentDnsCacheStore(filePath);

        // Act
        var loaded = store.Load();

        // Assert
        loaded.Keys.Should().BeEquivalentTo("192.168.1.10", "192.168.1.15");
        loaded["192.168.1.10"].HostName.Should().Be("nas.home.arpa");
    }

    [Fact]
    public async Task Load_WhenJsonIsCorrupted_ReturnsEmptyDictionaryWithoutThrowing()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "corrupted.json");
        await File.WriteAllTextAsync(filePath, "{ invalid_json_syntax: true }");
        var store = new PersistentDnsCacheStore(filePath);

        // Act
        var loaded = store.Load();

        // Assert
        loaded.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WhenReadingTheFileIsDenied_ReturnsEmptyDictionaryWithoutThrowing()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "source_read_denied.json");
        await File.WriteAllTextAsync(filePath, """{ "192.168.1.10": { "HostName": "nas.home.arpa", "Source": "DNS" } }""");
        var file = new FileInfo(filePath);
        using var identity = WindowsIdentity.GetCurrent();
        var denyRead = new FileSystemAccessRule(identity.User ?? throw new InvalidOperationException("The test account has no security identifier."), FileSystemRights.ReadData, AccessControlType.Deny);
        var security = file.GetAccessControl();
        security.AddAccessRule(denyRead);
        file.SetAccessControl(security);
        var store = new PersistentDnsCacheStore(filePath);
        try
        {
            // Act
            var act = () => store.Load();

            // Assert
            act.Should().NotThrow().Which.Should().BeEmpty();
        }
        finally
        {
            security.RemoveAccessRule(denyRead);
            file.SetAccessControl(security);
        }
    }

    [Fact]
    public async Task LoadOrThrow_WhenReadingTheFileIsDenied_ThrowsDnsCacheUnreadableExceptionNamingTheFile()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "read_denied.json");
        await File.WriteAllTextAsync(filePath, """{ "192.168.1.10": { "HostName": "nas.home.arpa", "Source": "DNS" } }""");
        var file = new FileInfo(filePath);
        using var identity = WindowsIdentity.GetCurrent();
        var denyRead = new FileSystemAccessRule(identity.User ?? throw new InvalidOperationException("The test account has no security identifier."), FileSystemRights.ReadData, AccessControlType.Deny);
        var security = file.GetAccessControl();
        security.AddAccessRule(denyRead);
        file.SetAccessControl(security);
        var store = new PersistentDnsCacheStore(filePath);
        try
        {
            // Act
            var act = () => store.LoadOrThrow();

            // Assert
            act.Should().Throw<DnsCacheUnreadableException>().WithMessage($"*'{filePath}'*");
        }
        finally
        {
            security.RemoveAccessRule(denyRead);
            file.SetAccessControl(security);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenTheFileIsReadOnly_KeepsThePreviousVersionAndReturnsFalse()
    {
        // Arrange
        const string previous = """{ "192.168.1.10": { "HostName": "nas.home.arpa", "Source": "DNS" } }""";
        var filePath = Path.Combine(_testDir, "read_only.json");
        await File.WriteAllTextAsync(filePath, previous);
        File.SetAttributes(filePath, FileAttributes.ReadOnly);
        var store = new PersistentDnsCacheStore(filePath);
        try
        {
            // Act
            var saved = await store.SaveAsync(PiHoleEntry());

            // Assert
            saved.Should().BeFalse();
            (await File.ReadAllTextAsync(filePath)).Should().Be(previous);
        }
        finally
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenTheFileIsWritten_ReturnsTrue()
    {
        // Arrange
        var store = new PersistentDnsCacheStore(Path.Combine(_testDir, "written.json"));

        // Act
        var saved = await store.SaveAsync(PiHoleEntry());

        // Assert
        saved.Should().BeTrue();
        store.Load().Should().ContainKey("192.168.1.2");
    }

    [Fact]
    public async Task Save_WhenTheFileIsReadOnly_KeepsThePreviousVersion()
    {
        // Arrange
        const string previous = """{ "192.168.1.10": { "HostName": "nas.home.arpa", "Source": "DNS" } }""";
        var filePath = Path.Combine(_testDir, "read_only_sync.json");
        await File.WriteAllTextAsync(filePath, previous);
        File.SetAttributes(filePath, FileAttributes.ReadOnly);
        var store = new PersistentDnsCacheStore(filePath);
        try
        {
            // Act
            var act = () => store.Save(PiHoleEntry());

            // Assert
            act.Should().NotThrow();
            (await File.ReadAllTextAsync(filePath)).Should().Be(previous);
        }
        finally
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task SaveAsync_WhenTheFolderRefusesNewFiles_KeepsThePreviousVersionAndReturnsFalse()
    {
        // Arrange
        const string previous = """{ "192.168.1.10": { "HostName": "nas.home.arpa", "Source": "DNS" } }""";
        var folder = Directory.CreateDirectory(Path.Combine(_testDir, "locked_async"));
        var filePath = Path.Combine(folder.FullName, "dns_cache.json");
        await File.WriteAllTextAsync(filePath, previous);
        var store = new PersistentDnsCacheStore(filePath);
        using var denial = FolderCreationDenial.Apply(folder);

        // Act
        var saved = await store.SaveAsync(PiHoleEntry());

        // Assert
        saved.Should().BeFalse();
        (await File.ReadAllTextAsync(filePath)).Should().Be(previous);
    }

    [Fact]
    public async Task Save_WhenTheFolderRefusesNewFiles_KeepsThePreviousVersion()
    {
        // Arrange
        const string previous = """{ "192.168.1.10": { "HostName": "nas.home.arpa", "Source": "DNS" } }""";
        var folder = Directory.CreateDirectory(Path.Combine(_testDir, "locked_sync"));
        var filePath = Path.Combine(folder.FullName, "dns_cache.json");
        await File.WriteAllTextAsync(filePath, previous);
        var store = new PersistentDnsCacheStore(filePath);
        using var denial = FolderCreationDenial.Apply(folder);

        // Act
        var act = () => store.Save(PiHoleEntry());

        // Assert
        act.Should().NotThrow();
        (await File.ReadAllTextAsync(filePath)).Should().Be(previous);
    }

    private static Dictionary<string, DnsCacheEntry> PiHoleEntry() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["192.168.1.2"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "pihole.home.arpa", Source = "DNS" }
        };
}

using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class MarkdownMatrixFormatterTests
{
    private static readonly MatrixDescription Description = new(
        "Sample matrix",
        "One row per sample.",
        ["First use.", "Second use."],
        "Samples",
        [new("Name", "Sample name"), new("Port", "Sample port")]);

    private static readonly SampleLayout Layout = new();

    [Fact]
    public void Format_WhenInvoked_StartsWithTheTitleTheSummaryAndTheUses()
    {
        // Act
        var lines = new MarkdownMatrixFormatter().Format(Description, Layout, [("nas", "443")]);

        // Assert
        lines[0].Should().Be("# Sample matrix");
        lines.Should().ContainInConsecutiveOrder("One row per sample.");
        lines.Should().ContainInConsecutiveOrder("## What it can be used for", "", "- First use.", "- Second use.");
    }

    [Fact]
    public void Format_WhenInvoked_WritesTheColumnLegendBeforeTheRows()
    {
        // Act
        var lines = new MarkdownMatrixFormatter().Format(Description, Layout, [("nas", "443")]);

        // Assert
        lines.Should().ContainInConsecutiveOrder(
            "## Columns",
            "",
            "| Column | Content |",
            "| :--- | :--- |",
            "| `Name` | Sample name |",
            "| `Port` | Sample port |");
        lines.ToList().IndexOf("## Columns").Should().BeLessThan(lines.ToList().IndexOf("## Samples (1)"));
    }

    [Fact]
    public void Format_WhenRowsAreGiven_WritesOneTableRowPerItemUnderTheRowCount()
    {
        // Act
        var lines = new MarkdownMatrixFormatter().Format(Description, Layout, [("nas", "443"), ("pihole", "53")]);

        // Assert
        lines.Should().ContainInConsecutiveOrder(
            "## Samples (2)",
            "",
            "| Name | Port |",
            "| --- | --- |",
            "| nas | 443 |",
            "| pihole | 53 |");
        lines[^1].Should().Be("| pihole | 53 |");
    }

    [Fact]
    public void Format_WhenNoRowIsGiven_WritesThatNoRowPassedTheFiltersInsteadOfATable()
    {
        // Act
        var lines = new MarkdownMatrixFormatter().Format(Description, Layout, []);

        // Assert
        lines.Should().ContainInConsecutiveOrder("## Samples (0)", "", "No row passed the filters.");
        lines.Should().NotContain("| Name | Port |");
    }

    [Theory]
    [InlineData("a|b", @"a\|b")]
    [InlineData("<script>alert(1)</script>", @"\<script\>alert(1)\</script\>")]
    [InlineData("*bold* _it_ `code` [link](x)", @"\*bold\* \_it\_ \`code\` \[link\](x)")]
    [InlineData(@"C:\Tools\curl.exe", @"C:\\Tools\\curl.exe")]
    [InlineData("line\r\nbreak\ttab", "line  break tab")]
    [InlineData("&amp; ~x~", @"\&amp; \~x\~")]
    public void Format_WhenAValueHoldsMarkdownSyntax_WritesItAsPlainText(string value, string expected)
    {
        // Act
        var lines = new MarkdownMatrixFormatter().Format(Description, Layout, [(value, "443")]);

        // Assert
        lines[^1].Should().Be($"| {expected} | 443 |");
    }

    private sealed class SampleLayout : IMatrixColumnLayout<(string Name, string Port)>
    {
        public IReadOnlyList<string> ColumnNames { get; } = ["Name", "Port"];

        public IReadOnlyList<string> FieldsOf((string Name, string Port) item) => [item.Name, item.Port];
    }
}

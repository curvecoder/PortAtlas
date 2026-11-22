using AwesomeAssertions;

namespace PortAtlas.Tests.Documentation;

public sealed class MarkdownExampleExtractorTests
{
    [Fact]
    public void ExtractCommandLines_WhenShellBlockHoldsCommands_ReturnsTheirArgumentsAndLines()
    {
        // Arrange
        const string markdown = """
            # Page

            ```bash
            PortAtlas map -i collected --html homelab.html
            sudo ./PortAtlas audit
            ```

            ```powershell
            .\PortAtlas.exe export rules -o rules.csv
            ```
            """;

        // Act
        var lines = MarkdownExampleExtractor.ExtractCommandLines(markdown);

        // Assert
        lines.Should().Equal(
            new DocumentedCommandLine(4, "map -i collected --html homelab.html"),
            new DocumentedCommandLine(5, "audit"),
            new DocumentedCommandLine(9, "export rules -o rules.csv"));
    }

    [Fact]
    public void ExtractCommandLines_WhenLineStartsAServiceOrNamesAPath_ReturnsTheArguments()
    {
        // Arrange
        const string markdown = """
            ```ini
            Description=PortAtlas network flow audit
            ExecStart=/opt/portatlas/PortAtlas audit --no-tui
            ```

            Run `C:\Tools\PortAtlas\PortAtlas.exe audit` from another folder.
            """;

        // Act
        var lines = MarkdownExampleExtractor.ExtractCommandLines(markdown);

        // Assert
        lines.Should().Equal(
            new DocumentedCommandLine(3, "audit --no-tui"),
            new DocumentedCommandLine(6, "audit"));
    }

    [Fact]
    public void ExtractCommandLines_WhenLineIsASynopsisOrProse_SkipsIt()
    {
        // Arrange
        const string markdown = """
            ```text
            PortAtlas map [-i <path>]... [options]
            ```

            The banner reads `PortAtlas.exe was not found next to the GUI.` or `PortAtlas was not found …`, the title `PortAtlas v<version>`.
            PortAtlas writes its files in the working directory; check with `PortAtlas lookup 192.168.1.25`.
            """;

        // Act
        var lines = MarkdownExampleExtractor.ExtractCommandLines(markdown);

        // Assert
        lines.Should().Equal(new DocumentedCommandLine(6, "lookup 192.168.1.25"));
    }

    [Fact]
    public void ExtractJsonBlocks_WhenBlockIsIndentedInAList_ReturnsItsContentWithoutIndentation()
    {
        // Arrange
        const string markdown = """
            - Or declare the name:

              ```json
              {
                "CustomHostAliases": { "192.168.1.25": "nas" }
              }
              ```

            ```bash
            PortAtlas audit
            ```
            """;

        // Act
        var blocks = MarkdownExampleExtractor.ExtractJsonBlocks(markdown);

        // Assert
        blocks.Should().ContainSingle()
            .Which.Should().Be(new DocumentedJsonBlock(3, "{\n  \"CustomHostAliases\": { \"192.168.1.25\": \"nas\" }\n}"));
    }

    [Fact]
    public void ExtractOutputExcerpts_WhenAMarkerPrecedesATable_ReturnsTheScenarioTheFileTheColumnsAndTheRows()
    {
        // Arrange
        const string markdown = """
            # Page

            <!-- example-output: rules/rules.csv -->

            | RuleId | Direction | DestinationPort |
            | :--- | :--- | ---: |
            | RULE-001 | Inbound | 22 |
            | RULE-006 | Outbound | 53 |

            After the table.
            """;

        // Act
        var excerpts = MarkdownExampleExtractor.ExtractOutputExcerpts(markdown);

        // Assert
        var excerpt = excerpts.Should().ContainSingle().Subject;
        excerpt.LineNumber.Should().Be(3);
        excerpt.Scenario.Should().Be("rules");
        excerpt.FileName.Should().Be("rules.csv");
        excerpt.IsComplete.Should().BeFalse();
        excerpt.Columns.Should().Equal("RuleId", "Direction", "DestinationPort");
        excerpt.Rows.Should().HaveCount(2);
        excerpt.Rows[0].Should().Equal("RULE-001", "Inbound", "22");
        excerpt.Rows[1].Should().Equal("RULE-006", "Outbound", "53");
    }

    [Fact]
    public void ExtractOutputExcerpts_WhenTheMarkerSaysComplete_ReturnsAnExcerptMarkedComplete()
    {
        // Arrange
        const string markdown = """
            <!-- example-output: all-matrices/pairs.csv complete -->
            | PairId |
            | :--- |
            | PAIR-001 |
            """;

        // Act
        var excerpts = MarkdownExampleExtractor.ExtractOutputExcerpts(markdown);

        // Assert
        excerpts.Should().ContainSingle().Which.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void ExtractOutputExcerpts_WhenCellsHoldInlineCodeOrAnEscapedPipe_ReturnsTheRawValues()
    {
        // Arrange
        const string markdown = """
            <!-- example-output: all-matrices/pairs.csv -->
            | Ports | Note |
            | :--- | :--- |
            | `22/TCP, 445/TCP` | a \| b |
            """;

        // Act
        var excerpts = MarkdownExampleExtractor.ExtractOutputExcerpts(markdown);

        // Assert
        excerpts.Should().ContainSingle().Which.Rows.Should().ContainSingle()
            .Which.Should().Equal("22/TCP, 445/TCP", "a | b");
    }

    [Fact]
    public void ExtractOutputExcerpts_WhenNoTableFollowsTheMarker_ReturnsAnExcerptWithoutColumns()
    {
        // Arrange
        const string markdown = """
            <!-- example-output: rules/rules.csv -->

            Prose instead of a table.
            """;

        // Act
        var excerpts = MarkdownExampleExtractor.ExtractOutputExcerpts(markdown);

        // Assert
        var excerpt = excerpts.Should().ContainSingle().Subject;
        excerpt.Columns.Should().BeEmpty();
        excerpt.Rows.Should().BeEmpty();
    }
}

using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class FirewallMatrixDescriptionsTests
{
    public static TheoryData<string, MatrixDescription, IReadOnlyList<string>> Matrices => new()
    {
        { "rules", FirewallMatrixDescriptions.Rules, new FirewallRuleCsvFormatter().ColumnNames },
        { "listeners", FirewallMatrixDescriptions.Listeners, new FirewallRuleCsvFormatter().ColumnNames },
        { "flows", FirewallMatrixDescriptions.Flows, new FirewallFlowCsvFormatter().ColumnNames },
        { "pairs", FirewallMatrixDescriptions.Pairs, new FirewallRulePairCsvFormatter().ColumnNames },
        { "host rules", FirewallMatrixDescriptions.HostRules, new FirewallHostRuleCsvFormatter().ColumnNames },
        { "group rules", FirewallMatrixDescriptions.GroupRules, new FirewallGroupRuleCsvFormatter().ColumnNames }
    };

    [Theory]
    [MemberData(nameof(Matrices))]
    public void Columns_ForEachMatrix_DescribeEveryColumnOfTheMatrixInItsOrder(string matrix, MatrixDescription description, IReadOnlyList<string> columnNames)
    {
        // Assert
        description.Columns.Select(column => column.Name).Should().Equal(columnNames, matrix);
        description.Columns.Should().OnlyContain(column => column.Content.Length > 0, matrix);
        description.Uses.Should().NotBeEmpty(matrix);
    }

    [Fact]
    public void Uses_OfTheMatricesForCloudRules_NameNetworkSecurityGroupsAndSecurityLists()
    {
        // Assert
        FirewallMatrixDescriptions.HostRules.Uses.Should().Contain(use => use.Contains("network security groups", StringComparison.Ordinal));
        FirewallMatrixDescriptions.GroupRules.Uses.Should().Contain(use => use.Contains("security lists", StringComparison.Ordinal));
    }
}

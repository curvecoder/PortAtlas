namespace PortAtlas.Core.Export;

/// <summary>
/// A row of the group rule matrix: the rules of every address of a source group towards a destination group, on one
/// protocol and port.
/// </summary>
/// <param name="GroupRuleId">Row identifier (<c>GROUP-001</c>), in the order of the matrix.</param>
/// <param name="SourceGroup">Group of the source; its address when it is in no group, <c>Any</c> for a listening socket.</param>
/// <param name="DestinationGroup">Group of the destination; its address when it is in no group.</param>
/// <param name="Protocol">Transport protocol (<c>TCP</c> or <c>UDP</c>).</param>
/// <param name="DestinationPort">Destination port.</param>
/// <param name="RuleCount">Number of rules of the detailed matrix merged into the row.</param>
/// <param name="ObservationCount">Sum of the observations of those rules.</param>
/// <param name="FirstSeenUtc">Earliest first observation of those rules.</param>
/// <param name="LastSeenUtc">Latest last observation of those rules.</param>
/// <param name="Processes">Distinct process names of those rules, in ordinal order.</param>
public sealed record FirewallGroupRuleItem(
    string GroupRuleId,
    string SourceGroup,
    string DestinationGroup,
    string Protocol,
    int DestinationPort,
    int RuleCount,
    long ObservationCount,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    IReadOnlyList<string> Processes);

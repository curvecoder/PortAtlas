namespace PortAtlas.Core.Export;

/// <summary>
/// Explanations written at the top of each matrix of <c>export rules</c> in Markdown.
/// </summary>
public static class FirewallMatrixDescriptions
{
    private static readonly MatrixColumnDescription ObservationCount = new("ObservationCount", "Sum of the observation counts of the merged flows");
    private static readonly MatrixColumnDescription FirstSeen = new("FirstSeenUtc", "Earliest first observation (UTC)");
    private static readonly MatrixColumnDescription LastSeen = new("LastSeenUtc", "Latest observation (UTC)");
    private static readonly MatrixColumnDescription Protocol = new("Protocol", "`TCP` or `UDP`");

    private static readonly MatrixColumnDescription[] RuleColumns =
    [
        new("RuleId", "`RULE-001`, `RULE-002`… in sort order"),
        new("Direction", "`Outbound` when the audited server opened the connection, `Inbound` when it accepted it"),
        new("SourceIp", "Client address; `0.0.0.0` or `::` for a listening socket"),
        new("SourceHost", "Client name; `Any` for a listening socket"),
        new("DestinationIp", "Server address"),
        new("DestinationHost", "Server name, or its address when no name is known"),
        new("DestinationZone", "`Internal` (private, loopback or link-local address) or `Public`"),
        Protocol,
        new("DestinationPort", "Service port"),
        new("ServiceName", "Name deduced from the port and the process (for example `Oracle-TNS` for 1521)"),
        new("ProcessName", "Process owning the socket"),
        new("ExecutablePath", "Path of that process"),
        new("UserName", "Account of that process"),
        ObservationCount,
        FirstSeen,
        LastSeen
    ];

    /// <summary>
    /// Gets the explanation of the firewall rule matrix (<c>--output</c>).
    /// </summary>
    public static MatrixDescription Rules { get; } = new(
        "Firewall rule matrix",
        "One row per direction, source address, destination address, protocol, destination port and process: each row is a network flow that an audited server opened or accepted while PortAtlas recorded its connections.",
        [
            "Review the flows with the owners of the applications before opening or closing them.",
            "Write the allow rules of a network firewall, or a firewall change request, from the source, destination, protocol and port columns.",
            "Write per-program rules of a host firewall (Windows Defender Firewall, nftables, iptables) from the process and executable columns.",
            "Check the rules in place: a rule that matches no row was not used during the audit."
        ],
        "Rules",
        RuleColumns);

    /// <summary>
    /// Gets the explanation of the listening socket matrix (<c>--listeners-output</c>, or <c>--output</c> with
    /// <c>--listeners-only</c>).
    /// </summary>
    public static MatrixDescription Listeners { get; } = new(
        "Listening socket matrix",
        "One row per listening socket (TCP listener or bound UDP socket) of the audited servers, written as an inbound rule from any source: the services each server offers, whether or not a client reached them during the audit.",
        [
            "Write the inbound rules of host firewalls, network security groups and security lists for the services a server must offer.",
            "Review the exposed services: close a service that no client needs, or bind it to a private address.",
            "Compare with the rule matrix: a listening socket without an inbound flow was not used during the audit."
        ],
        "Listening sockets",
        RuleColumns);

    /// <summary>
    /// Gets the explanation of the flow matrix (<c>--flows-output</c>).
    /// </summary>
    public static MatrixDescription Flows { get; } = new(
        "Flow matrix",
        "One row per source address, destination address, protocol and destination port: a connection recorded by the logs of both its ends is one row, with the processes and the observations of each end.",
        [
            "Map which server talks to which, for a migration, a segmentation project or a dependency review.",
            "Write one network firewall rule per flow, between a source and a destination.",
            "Find gaps in the audit: a flow between two audited servers seen from one end only (`SeenFrom`) points to logs that cover different periods, to address translation or to a second network card."
        ],
        "Flows",
        [
            new("FlowId", "`FLOW-001`, `FLOW-002`… in sort order"),
            new("SourceIp", "Client address"),
            new("SourceHost", "Client name"),
            new("DestinationIp", "Server address"),
            new("DestinationHost", "Server name"),
            new("DestinationZone", "`Internal` or `Public`, as in the rule matrix"),
            Protocol,
            new("DestinationPort", "Service port"),
            new("ServiceName", "Service named by the rules of the flow; `port/protocol` when they name several services"),
            new("SourceProcesses", "Processes that opened the flow; empty when only the destination saw it"),
            new("DestinationProcesses", "Processes that accepted the flow; empty when only the source saw it"),
            new("SeenFrom", "`Source`, `Destination` or `Both`: the end whose log recorded the flow"),
            new("SourceObservationCount", "Observations recorded by the source log"),
            new("DestinationObservationCount", "Observations recorded by the destination log"),
            new("FirstSeenUtc", "Earliest first observation on either end (UTC)"),
            new("LastSeenUtc", "Latest observation on either end (UTC)")
        ]);

    /// <summary>
    /// Gets the explanation of the pair matrix (<c>--pairs-output</c>).
    /// </summary>
    public static MatrixDescription Pairs { get; } = new(
        "Pair matrix",
        "One row per source address and destination address, with the destination ports of their flows: the two directions between two servers stay on two rows, as firewall rules do.",
        [
            "Summarize the dependencies between servers for an architecture or a migration review.",
            "Write one rule per pair with a list of ports, where a rule accepts several ports: security lists, network security groups, service groups of a network firewall."
        ],
        "Pairs",
        [
            new("PairId", "`PAIR-001`, `PAIR-002`… in sort order"),
            new("SourceIp", "Client address"),
            new("SourceHost", "Client name"),
            new("DestinationIp", "Server address"),
            new("DestinationHost", "Server name"),
            new("FlowCount", "Number of flows merged into the pair"),
            new("Ports", "Distinct destination ports, `port/protocol`, sorted by port"),
            new("ObservationCount", "Sum of the observations of the merged flows, each flow counted once"),
            FirstSeen,
            LastSeen,
            new("SeenFrom", "Ends whose logs recorded the merged flows: `Source`, `Destination`, `Both`")
        ]);

    /// <summary>
    /// Gets the explanation of the host rule matrix (<c>--host-rules-output</c>).
    /// </summary>
    public static MatrixDescription HostRules { get; } = new(
        "Host rule matrix",
        "Each flow of a private address becomes an outbound rule on its source and an inbound rule on its destination: the rules each host needs, per direction, as network security groups and host firewalls set them. A public address shows only as the peer of a private host.",
        [
            "Write the network security group of each virtual machine or network interface: Azure network security groups, OCI network security groups, AWS security groups.",
            "Write the host firewall of each server: Windows Defender Firewall, nftables, iptables, firewalld.",
            "Review the inferred rules (`Evidence` is `Inferred`): rules of a host whose own log was not read, deduced from the log of its peer."
        ],
        "Host rules",
        [
            new("HostRuleId", "`HOST-001`, `HOST-002`… in sort order"),
            new("Host", "Host that carries the rule"),
            new("HostIp", "Address of that host"),
            new("Audited", "`Yes` when a log of this address was read, otherwise `No`"),
            new("Direction", "`Inbound` when the host accepts the connection, `Outbound` when it opens it"),
            new("PeerIp", "Address of the other end of the flow"),
            new("PeerHost", "Name of the other end of the flow"),
            new("PeerZone", "`Internal` or `Public`"),
            Protocol,
            new("Port", "Destination port: on the host for an inbound rule, on the peer for an outbound rule"),
            new("ServiceName", "As in the flow matrix"),
            new("LocalProcesses", "Processes of the host that use the flow; empty when the host's log did not record it"),
            new("Evidence", "`Observed` when the host's own log recorded the flow, `Inferred` when only the peer's log did"),
            new("ObservationCount", "Observations recorded by the host's log, or by the peer's log for an inferred rule"),
            new("FirstSeenUtc", "Earliest first observation of the flow (UTC)"),
            new("LastSeenUtc", "Latest observation of the flow (UTC)"),
            new("FlowId", "Row of the flow matrix the rule comes from")
        ]);

    /// <summary>
    /// Gets the explanation of the group rule matrix (<c>--group-rules-output</c>).
    /// </summary>
    public static MatrixDescription GroupRules { get; } = new(
        "Group rule matrix",
        "One row per source group, destination group, protocol and destination port, the addresses merged by the address groups given (`AddressGroups`, `--address-group`): an address in no group stands for itself.",
        [
            "Write subnet-level rules, where one rule covers a CIDR block such as a VLAN or a tier: OCI security lists, network ACLs.",
            "Write the rules between address objects of a network firewall, or between Azure application security groups.",
            "Check the segmentation: the flows that cross from one group to another."
        ],
        "Group rules",
        [
            new("GroupRuleId", "`GROUP-001`, `GROUP-002`… in sort order"),
            new("SourceGroup", "Group of the client address, its address when it is in no group, or `Any`"),
            new("DestinationGroup", "Group of the server address, or its address when it is in no group"),
            Protocol,
            new("DestinationPort", "Destination port"),
            new("RuleCount", "Number of firewall rule matrix rows merged into the row"),
            new("ObservationCount", "Sum of the observations of those rules"),
            FirstSeen,
            LastSeen,
            new("Processes", "Distinct process names of those rules")
        ]);
}

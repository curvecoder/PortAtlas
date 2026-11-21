> **Language / Langue**: **English** · [Français](export-firewall-rules.fr.md)

# Export a firewall rule matrix

Goal: a CSV list of the flows to allow, one row per source, destination, port and process, to prepare firewall rules.

## Export the observed flows

```bash
PortAtlas export rules -i collected -o firewall_rules.csv
```

`-i` reads CSV and NDJSON logs (a folder, a pattern or files). Ephemeral destination ports (49152 and above) are left out.

## Remove noise

Keep flows seen at least five times during the last 14 days, towards internal addresses, without DNS and NTP:

```bash
PortAtlas export rules -i collected -o internal_rules.csv --min-observations 5 --active-within-days 14 --zone internal --exclude-ports 53,123
```

`--zone public` keeps only destinations outside the private ranges, for example to list the Internet services a server needs.

## Summarize by server pair

```bash
PortAtlas export rules -i collected -o firewall_rules.csv --pairs-output firewall_pairs.csv
```

`--pairs-output` writes a second file beside the detailed matrix, with the same filters: one row per source and destination, with the number of flows and their ports ([column list](../reference/output-formats.md#pair-matrix)). It gives an overview of who talks to whom before reading the rules one by one.

## Give a flow matrix to a network team

When several servers are audited, a connection between two of them is in both logs: the detailed matrix shows it twice, as an `Outbound` rule with the client process and an `Inbound` rule with the server process.

```bash
PortAtlas export rules -i collected -o firewall_rules.csv --flows-output firewall_flows.csv
```

`--flows-output` writes one row per flow, with the processes of each end and the end whose log saw it (`SeenFrom`: `Source`, `Destination` or `Both`) ([column list](../reference/output-formats.md#flow-matrix)). Between two audited servers, a flow seen from one end only points to logs that do not cover the same period.

## Prepare network security group rules

```bash
PortAtlas export rules -i collected -o firewall_rules.csv --host-rules-output firewall_host_rules.csv
```

`--host-rules-output` writes the rules each host carries: an `Outbound` rule on the source and an `Inbound` rule on the destination of each flow, for every private address ([column list](../reference/output-formats.md#host-rule-matrix)). Filter the `Host` column to get the rules of one server. `Evidence` is `Inferred` when only the other end's log saw the flow, for example for a host without a log.

## List the services offered

```bash
PortAtlas export rules -i collected -o services.csv --listeners-only
```

Each listening socket, TCP listener or bound UDP socket, becomes an inbound rule from any source (`0.0.0.0`, host `Any`) to its address and port: the starting point of an inbound rule set.

`--listeners-output services.csv` writes the same file while `-o` keeps the flows, so that one run reads the logs once for every file:

```bash
PortAtlas export rules -i collected -o firewall_rules.csv --flows-output firewall_flows.csv --pairs-output firewall_pairs.csv --host-rules-output firewall_host_rules.csv --listeners-output services.csv
```

## Read the result

The command prints the number of files read, flows scanned and rules written. Each row carries the process, its path and the observation dates, so that each rule can be justified ([column list](../reference/output-formats.md#firewall-rule-matrix)).

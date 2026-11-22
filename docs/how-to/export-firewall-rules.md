> **Language / Langue**: **English** · [Français](export-firewall-rules.fr.md)

# Export a firewall rule matrix

Goal: a CSV list of the flows to allow, one row per source, destination, port and process, to prepare firewall rules.

Every result on this page comes from the [example homelab](../examples/homelab/README.md): three audited machines, `nas` (`192.168.1.10`), `pve` (`192.168.1.20`) and `pihole` (`192.168.1.2`), and a laptop (`192.168.1.50`) that has no audit log. Run the commands in `docs/examples/homelab` of the repository to get the same files; the tables below show a few columns of each file, the [output formats reference](../reference/output-formats.md#firewall-rule-matrix) lists them all.

## Export the observed flows

```bash
PortAtlas export rules -i logs -o rules.csv
```

`-i` reads CSV and NDJSON logs (a folder, a pattern or files). The three logs give `rules.csv`:

<!-- example-output: rules/rules.csv complete -->

| RuleId | Direction | SourceHost | DestinationHost | Protocol | DestinationPort | ProcessName | ObservationCount |
| :--- | :--- | :--- | :--- | :--- | ---: | :--- | ---: |
| RULE-001 | Inbound | laptop.home.arpa | nas | TCP | 22 | sshd | 1 |
| RULE-002 | Inbound | laptop.home.arpa | pihole | TCP | 80 | lighttpd | 35 |
| RULE-003 | Inbound | laptop.home.arpa | nas | TCP | 445 | smbd | 240 |
| RULE-004 | Inbound | pve.home.arpa | nas | TCP | 873 | rsync | 96 |
| RULE-005 | Inbound | laptop.home.arpa | pve | TCP | 8006 | pveproxy | 310 |
| RULE-006 | Outbound | nas | pihole.home.arpa | UDP | 53 | systemd-resolved | 2016 |
| RULE-007 | Outbound | pve | pihole.home.arpa | UDP | 53 | systemd-resolved | 2016 |
| RULE-008 | Outbound | pve | localhost | TCP | 85 | pveproxy | 620 |
| RULE-009 | Outbound | pve | nas.home.arpa | TCP | 873 | rsync | 96 |
| RULE-010 | Outbound | pve | 0.debian.pool.ntp.org | UDP | 123 | chronyd | 48 |
| RULE-011 | Outbound | pihole | 198.51.100.53 | TCP | 853 | unbound | 2016 |

How to read it:

- Each log describes its own machine. The nightly backup from `pve` to `nas` is in both logs, so it gives two rules: `RULE-004`, the connection `nas` accepted, and `RULE-009`, the connection `pve` opened.
- The laptop has no log: its connections show only as `Inbound` rules on the machines it reaches (`RULE-001`, `RULE-002`, `RULE-003`, `RULE-005`).
- An audited machine is named by its machine name (`nas`); the other end by the name its DNS gave (`nas.home.arpa`), or by its address when there is none (`198.51.100.53`).
- `RULE-008` is a connection inside `pve`, to `127.0.0.1`.
- Two things are missing on purpose: the listening sockets ([List the services offered](#list-the-services-offered)) and a connection from `pve` to port 51234 of `nas` ([Keep the ephemeral ports](#keep-the-ephemeral-ports)).

## Remove noise

Keep the flows seen at least five times, without DNS and NTP:

```bash
PortAtlas export rules -i logs -o rules.csv --min-observations 5 --exclude-ports 53,123
```

<!-- example-output: rules-noise/rules.csv complete -->

| RuleId | Direction | SourceHost | DestinationHost | DestinationPort | ObservationCount |
| :--- | :--- | :--- | :--- | ---: | ---: |
| RULE-001 | Inbound | laptop.home.arpa | pihole | 80 | 35 |
| RULE-002 | Inbound | laptop.home.arpa | nas | 445 | 240 |
| RULE-003 | Inbound | pve.home.arpa | nas | 873 | 96 |
| RULE-004 | Inbound | laptop.home.arpa | pve | 8006 | 310 |
| RULE-005 | Outbound | pve | localhost | 85 | 620 |
| RULE-006 | Outbound | pve | nas.home.arpa | 873 | 96 |
| RULE-007 | Outbound | pihole | 198.51.100.53 | 853 | 2016 |

The SSH session seen once, the two DNS rules and the NTP rule are gone. The rules are numbered again after filtering: `RULE-001` is not the same rule as in the first export, so compare two exports on their columns, not on `RuleId`.

`--active-within-days 14` keeps the flows seen during the last 14 days, counted from the day the command runs: on these logs, written in November 2026, its result depends on the date you run it.

## Keep one zone

```bash
PortAtlas export rules -i logs -o rules.csv --zone public
```

<!-- example-output: rules-public/rules.csv complete -->

| RuleId | SourceHost | DestinationIp | DestinationHost | DestinationZone | Protocol | DestinationPort |
| :--- | :--- | :--- | :--- | :--- | :--- | ---: |
| RULE-001 | pve | 203.0.113.10 | 0.debian.pool.ntp.org | Public | UDP | 123 |
| RULE-002 | pihole | 198.51.100.53 | 198.51.100.53 | Public | TCP | 853 |

These are the Internet services the homelab needs: time for `pve`, DNS over TLS for `pihole`. `--zone internal` keeps the other rules, those towards private and loopback addresses:

```bash
PortAtlas export rules -i logs -o rules.csv --zone internal
```

## Keep one server

```bash
PortAtlas export rules -i logs -o rules.csv --include-hosts "nas*"
```

<!-- example-output: rules-nas/rules.csv complete -->

| RuleId | Direction | SourceHost | DestinationHost | DestinationPort |
| :--- | :--- | :--- | :--- | ---: |
| RULE-001 | Inbound | laptop.home.arpa | nas | 22 |
| RULE-002 | Inbound | laptop.home.arpa | nas | 445 |
| RULE-003 | Inbound | pve.home.arpa | nas | 873 |
| RULE-004 | Outbound | nas | pihole.home.arpa | 53 |
| RULE-005 | Outbound | pve | nas.home.arpa | 873 |

The pattern is tested on both ends of each rule: `RULE-005` comes from the log of `pve`, and is kept because its destination, `nas.home.arpa`, matches `nas*`. `--include-ips 192.168.1.10` selects the machine by its address instead of its name.

## Keep one direction

```bash
PortAtlas export rules -i logs -o rules.csv --direction out
```

<!-- example-output: rules-outbound/rules.csv complete -->

| RuleId | SourceHost | DestinationHost | Protocol | DestinationPort |
| :--- | :--- | :--- | :--- | ---: |
| RULE-001 | nas | pihole.home.arpa | UDP | 53 |
| RULE-002 | pve | pihole.home.arpa | UDP | 53 |
| RULE-003 | pve | localhost | TCP | 85 |
| RULE-004 | pve | nas.home.arpa | TCP | 873 |
| RULE-005 | pve | 0.debian.pool.ntp.org | UDP | 123 |
| RULE-006 | pihole | 198.51.100.53 | TCP | 853 |

Only the connections that the audited machines opened remain: the egress rules of each server. `--direction in` keeps the connections they accepted.

## Keep the ephemeral ports

Destination ports 49152 and above are left out by default: a server picks such a port at random, and a rule written for it would not hold after a restart. `--include-ephemeral-ports` keeps them:

```bash
PortAtlas export rules -i logs -o rules.csv --include-ephemeral-ports
```

<!-- example-output: rules-ephemeral/rules.csv -->

| RuleId | Direction | SourceHost | DestinationHost | Protocol | DestinationPort | ProcessName | ObservationCount |
| :--- | :--- | :--- | :--- | :--- | ---: | :--- | ---: |
| RULE-010 | Outbound | pve | nas.home.arpa | TCP | 51234 | mount.nfs | 3 |

This connection is the NFS mount of `pve` asking `nas` for a share, on the port `rpc.mountd` chose at start. A firewall rule for it needs a fixed `mountd` port on the NAS first.

## List the services offered

```bash
PortAtlas export rules -i logs -o services.csv --listeners-only
```

<!-- example-output: listeners/services.csv complete -->

| RuleId | SourceHost | DestinationIp | DestinationHost | Protocol | DestinationPort | ProcessName |
| :--- | :--- | :--- | :--- | :--- | ---: | :--- |
| RULE-001 | Any | 0.0.0.0 | nas | TCP | 22 | sshd |
| RULE-002 | Any | 0.0.0.0 | pve | TCP | 22 | sshd |
| RULE-003 | Any | 0.0.0.0 | pihole | TCP | 53 | pihole-FTL |
| RULE-004 | Any | 0.0.0.0 | pihole | UDP | 53 | pihole-FTL |
| RULE-005 | Any | 0.0.0.0 | pihole | TCP | 80 | lighttpd |
| RULE-006 | Any | 127.0.0.1 | pve | TCP | 85 | pvedaemon |
| RULE-007 | Any | 0.0.0.0 | nas | TCP | 445 | smbd |
| RULE-008 | Any | 0.0.0.0 | nas | TCP | 873 | rsync |
| RULE-009 | Any | 0.0.0.0 | pve | TCP | 8006 | pveproxy |

Each listening socket, TCP listener or bound UDP socket, becomes an inbound rule from any source (`Any`) to its address and port: the starting point of an inbound rule set. `0.0.0.0` means every address of the machine; `pvedaemon` (`RULE-006`) listens on `127.0.0.1` only and needs no firewall rule.

## Write every matrix in one run

The detailed matrix answers "which process uses which port". Other questions need other views of the same flows; each `--…-output` option adds a file, with the same filters, and the logs are read once:

```bash
PortAtlas export rules -i logs -o rules.csv --flows-output flows.csv --pairs-output pairs.csv --host-rules-output host_rules.csv --listeners-output services.csv --group-rules-output group_rules.csv
```

| Question | File | Option |
| :--- | :--- | :--- |
| Which process opens or accepts which connection? | `rules.csv` | `-o` |
| Which connections cross the network, counted once? | `flows.csv` | `--flows-output` |
| Which machine talks to which, on which ports? | `pairs.csv` | `--pairs-output` |
| Which rules does each machine carry (network security groups)? | `host_rules.csv` | `--host-rules-output` |
| Which services does each machine offer? | `services.csv` | `--listeners-output` |
| Which rules between address groups (VLANs, application groups)? | `group_rules.csv` | `--group-rules-output` |

The sections below follow the backup from `pve` to `nas` (TCP 873) through each file.

### Flow matrix: one row per connection

In `rules.csv`, the backup is two rules, one per log:

<!-- example-output: all-matrices/rules.csv -->

| RuleId | Direction | SourceHost | DestinationHost | DestinationPort | ProcessName |
| :--- | :--- | :--- | :--- | ---: | :--- |
| RULE-004 | Inbound | pve.home.arpa | nas | 873 | rsync |
| RULE-009 | Outbound | pve | nas.home.arpa | 873 | rsync |

`flows.csv` merges them into one row, `FLOW-007`, with the process of each end:

<!-- example-output: all-matrices/flows.csv complete -->

| FlowId | SourceHost | DestinationHost | Protocol | DestinationPort | SourceProcesses | DestinationProcesses | SeenFrom |
| :--- | :--- | :--- | :--- | ---: | :--- | :--- | :--- |
| FLOW-001 | laptop.home.arpa | nas | TCP | 22 | | sshd | Destination |
| FLOW-002 | nas | pihole | UDP | 53 | systemd-resolved | | Source |
| FLOW-003 | pve | pihole | UDP | 53 | systemd-resolved | | Source |
| FLOW-004 | laptop.home.arpa | pihole | TCP | 80 | | lighttpd | Destination |
| FLOW-005 | pve | localhost | TCP | 85 | pveproxy | | Source |
| FLOW-006 | laptop.home.arpa | nas | TCP | 445 | | smbd | Destination |
| FLOW-007 | pve | nas | TCP | 873 | rsync | rsync | Both |
| FLOW-008 | laptop.home.arpa | pve | TCP | 8006 | | pveproxy | Destination |
| FLOW-009 | pve | 0.debian.pool.ntp.org | UDP | 123 | chronyd | | Source |
| FLOW-010 | pihole | 198.51.100.53 | TCP | 853 | unbound | | Source |

`SeenFrom` names the end whose log recorded the flow. `Both` is expected between two audited machines. The DNS queries to `pihole` (`FLOW-002`, `FLOW-003`) are `Source` although `pihole` is audited: a UDP server socket has no remote address, so the log of `pihole` holds the DNS listener, not the clients. Between two audited machines, a TCP flow seen from one end only means that the logs do not cover the same period.

The flow matrix names an audited machine after itself at both ends (`nas`, not `nas.home.arpa`).

### Pair matrix: who talks to whom

`pairs.csv` keeps one row per source and destination, with their ports:

<!-- example-output: all-matrices/pairs.csv -->

| PairId | SourceHost | DestinationHost | FlowCount | Ports | ObservationCount | SeenFrom |
| :--- | :--- | :--- | ---: | :--- | ---: | :--- |
| PAIR-005 | pve | nas | 1 | 873/TCP | 96 | Both |
| PAIR-008 | laptop.home.arpa | nas | 2 | 22/TCP, 445/TCP | 241 | Destination |

`PAIR-008` merges the SSH session and the file shares of the laptop on the NAS: an overview before reading the rules one by one. The two directions between two machines stay on two rows, as firewall rules do.

### Host rule matrix: the rules each machine carries

Network security groups and host firewalls hold rules per machine and per direction. `host_rules.csv` gives each flow an `Outbound` rule on its source and an `Inbound` rule on its destination, for every private address, the laptop included:

<!-- example-output: all-matrices/host_rules.csv -->

| HostRuleId | Host | Audited | Direction | PeerHost | Port | LocalProcesses | Evidence |
| :--- | :--- | :--- | :--- | :--- | ---: | :--- | :--- |
| HOST-001 | laptop.home.arpa | No | Outbound | nas | 22 | | Inferred |
| HOST-003 | laptop.home.arpa | No | Outbound | nas | 445 | | Inferred |
| HOST-007 | nas | Yes | Inbound | pve | 873 | rsync | Observed |
| HOST-009 | pihole | Yes | Inbound | nas | 53 | | Inferred |
| HOST-016 | pve | Yes | Outbound | nas | 873 | rsync | Observed |

Filter the `Host` column to get the rules of one machine. `Evidence` is `Observed` when the machine's own log recorded the flow, `Inferred` when only the other end did: the laptop has no log, and `pihole` does not see its DNS clients (see above).

### Group rule matrix: rules between address groups

Address groups come from the `AddressGroups` key of the configuration. The homelab ships this `portatlas.json`:

```json
{
  "AddressGroups": {
    "dns": [ "192.168.1.2" ],
    "servers": [ "192.168.1.10", "192.168.1.20" ],
    "clients": [ "192.168.1.48/28" ]
  }
}
```

`group_rules.csv` has one row per source group, destination group, protocol and port:

<!-- example-output: all-matrices/group_rules.csv -->

| GroupRuleId | SourceGroup | DestinationGroup | Protocol | DestinationPort | RuleCount | Processes |
| :--- | :--- | :--- | :--- | ---: | ---: | :--- |
| GROUP-004 | clients | servers | TCP | 445 | 1 | smbd |
| GROUP-007 | servers | 203.0.113.10 | UDP | 123 | 1 | chronyd |
| GROUP-008 | servers | dns | UDP | 53 | 2 | systemd-resolved |
| GROUP-009 | servers | servers | TCP | 873 | 2 | rsync |

`GROUP-008` holds the DNS rules of `nas` and `pve` (`RuleCount` 2); `GROUP-009` holds the two rules of the backup. An address in no group stands for itself (`203.0.113.10`).

`--address-group` adds groups for one run, without editing the file. Grouping the two Internet services:

```bash
PortAtlas export rules -i logs -o rules.csv --group-rules-output group_rules.csv --address-group upstream=198.51.100.0/24 --address-group upstream=203.0.113.0/24
```

<!-- example-output: groups-option/group_rules.csv -->

| GroupRuleId | SourceGroup | DestinationGroup | Protocol | DestinationPort | Processes |
| :--- | :--- | :--- | :--- | ---: | :--- |
| GROUP-006 | dns | upstream | TCP | 853 | unbound |
| GROUP-009 | servers | upstream | UDP | 123 | chronyd |

## Read the result

The command prints the number of files read, flows scanned and rules written. Each row carries the process, its path and the observation dates, so that each rule can be justified ([column list](../reference/output-formats.md#firewall-rule-matrix)).

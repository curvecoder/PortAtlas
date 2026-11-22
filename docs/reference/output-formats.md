> **Language / Langue**: **English** · [Français](output-formats.fr.md)

# Output formats reference

## Files written

| File | Written by | Encoding | Replaced |
| :--- | :--- | :--- | :--- |
| CSV audit log | `audit` | UTF-8 with byte order mark | Once complete (see [Rewrite cycle](#rewrite-cycle)) |
| NDJSON audit log | `audit --ndjson` | UTF-8 without byte order mark | Once complete, as the CSV log |
| NDJSON export | `export ndjson` | UTF-8 without byte order mark | Written in place: an interrupted export leaves a partial file |
| Rule, listening socket, flow, pair, host rule and group rule matrices | `export rules` | UTF-8 with byte order mark | Once complete |
| DNS cache | `audit`, `resolve` | UTF-8 without byte order mark | Once complete; when it cannot be written, the previous file stays |
| Enriched copy | `resolve` | Byte order mark of the log it copies | Once complete |
| HTML map | `map --html`, `audit --html` | UTF-8 with byte order mark | Written in place |
| Draw.io diagram | `map --drawio`, `audit --drawio` | UTF-8 without byte order mark | Written in place |

Lines end with the line end of the operating system (CRLF on Windows, LF on Linux and macOS), except in the Draw.io diagram, which always uses LF, and in the script embedded in the HTML map, which keeps CRLF. A file replaced once complete is first written as `<file>.tmp`, renamed `<file>.pending` once complete, then moved over `<file>`. During the write, `<file>.lock` is held open without sharing; Windows deletes it afterwards, Linux and macOS leave it beside the file.

The file names used when no path is set are listed in the [command line reference](cli.md).

## CSV audit log

Written by `audit` (`-o`, `OutputCsvPath`). Fields follow RFC 4180 quoting; the separator is `CsvDelimiter` (`,` by default). One record per deduplicated flow. `audit` writes each line break of a field (a command line, for instance) as a space, so the logs it writes have one line per flow; a log written by an earlier version may hold a quoted field with line breaks, so a record can span several lines, and every command reads it whole.

A flow is identified by its protocol, its direction, its application (`TargetExecutable`, or `ExecutablePath` when there is no target, or `ProcessName` when the path is empty too) and:

| Direction | Also identified by | Ignored |
| :--- | :--- | :--- |
| `Outbound` | remote address and remote port | local (client) port |
| `Inbound` | local port and remote address | remote (client) port |
| `Listening` | local port | addresses |

Repeated connections to the same service therefore update one line instead of adding new ones.

`--columns` and `CsvColumns` choose and order the columns; without them, the 20 columns below are written in this order. Nine columns cannot be left out, because they identify a flow when the log is read back: `FirstSeenUtc`, `Protocol`, `Direction`, `LocalPort`, `RemoteIp`, `RemotePort`, `ProcessName`, `ExecutablePath` and `TargetExecutable` (see [`--columns`](cli.md)).

| Column | Category | Content |
| :--- | :--- | :--- |
| `FirstSeenUtc` | Timestamp | UTC date and time when the flow was first observed |
| `LastSeenUtc` | Timestamp | UTC date and time when the flow was last observed |
| `ObservationCount` | Metrics | Number of scans in which the flow was seen |
| `ServerName` | Server | Name of the audited machine |
| `ServerOsVersion` | Server | Operating system description of the audited machine |
| `ServerPrimaryIp` | Server | Primary IP address of the audited machine: the first IPv4 address of an active interface that has a gateway, otherwise of any active interface, otherwise `127.0.0.1`; the interfaces are read again every minute |
| `Protocol` | Network | `Tcp` or `Udp` |
| `Direction` | Network | `Listening`, `Inbound` or `Outbound` |
| `TcpState` | Network | See [Socket states](#socket-states) |
| `LocalIp` | Network | Local address of the socket |
| `LocalPort` | Network | Local port of the socket |
| `RemoteIp` | Network | Remote address; for a listener or a UDP socket, `0.0.0.0` (IPv4) or `::` (IPv6) |
| `RemotePort` | Network | Remote port; `0` for a listener or a UDP socket |
| `RemoteHostName` | Network | Name from an alias, reverse DNS or the RDAP owner |
| `ProcessId` | Process | Process identifier |
| `ProcessName` | Process | Executable name of the process |
| `ExecutablePath` | Process | Full path of the executable; empty for the processes without executable file (System Idle, System, Secure System, Registry) and, on Linux, for another user's process when the audit does not run as root |
| `CommandLine` | Process | Command line of the process |
| `TargetExecutable` | Process | Application started by a launcher (`.jar`, `.py`, `.ps1`, `.sh`, `.dll`…) |
| `UserName` | Process | Account of the process: `DOMAIN\account` on Windows (the SID when the account has no name any more), the user name on Linux (the numeric UID when it is not in `/etc/passwd`); empty when the process cannot be opened |

Dates are written in the round-trip format (`2026-03-14T08:30:00.0000000+00:00`); numbers are written without thousands separator. A socket whose process identifier is 0 or below has `SYSTEM` as `UserName`, `System Idle` (identifier 0) or `Unknown` as `ProcessName`, and no path, command line or target.

### Socket states

| Value | When |
| :--- | :--- |
| `Closed`, `Listen`, `SynSent`, `SynReceived`, `Established`, `FinWait1`, `FinWait2`, `CloseWait`, `Closing`, `LastAck`, `TimeWait`, `DeleteTCB` | TCP socket on Windows |
| `UDP` | UDP socket on Windows |
| `Listen`, `Established`, `SynSent`, `SynRecv`, `FinWait1`, `FinWait2`, `TimeWait`, `Close`, `CloseWait`, `LastAck` | TCP socket on Linux |
| `Close`, `Established` | UDP socket on Linux, unconnected or connected |
| `Unknown(<n>)` (Windows), `State(<xx>)` (Linux) | State code not known to PortAtlas |

### Rewrite cycle

`audit` keeps the flows in memory and rewrites the whole log from them, one line per flow with its latest count, last-seen date and name: when it starts, every `--rewrite-interval` minutes (`LogRewriteIntervalMinutes`, 5 by default) and when it stops. The NDJSON log (`--ndjson`) is rewritten at the same times, from the same flows. An abrupt stop (power loss, killed process) therefore loses at most the observations of the last period. The log is written with the columns of the current run: a column left out of `--columns` since the previous run disappears from the whole log.

Each rewrite writes `<log>.tmp`, renames it `<log>.pending` once complete, then replaces the log: a program reading the log gets the earlier version or the new one, never a mix of both. Windows refuses the replacement while another program holds the log open: the audit goes on, `<log>.pending` stays, and the next period tries again; the dashboard (or, with `--no-tui`, the console) shows a warning that names the log and the reason, repeated at each failed period. When the rewrite fails at stop, `audit` ends with code 1 and a message naming the log. At start, a `<log>.pending` left by an interrupted rewrite replaces the log and a `<log>.tmp` is deleted; a log that cannot be replaced at start stops `audit` with code 1 and a message naming it.

### Reading a log back

At startup, `audit` reads the whole existing CSV log, so that counts and first-seen dates continue across restarts. The log is read with the separator found on its first line, as described below, so a log written before `CsvDelimiter` changed is still resumed. The log is then rewritten from what was read, with `CsvDelimiter`, so a log that cannot be read whole stops `audit` with an error naming the columns or the line, and exit code 1:

- a log without one of the nine columns that identify a flow, listed above: its flows would match none of the observed ones;
- a line whose `FirstSeenUtc`, `Protocol`, `Direction`, `LocalPort`, `RemoteIp` or `RemotePort` is empty or unreadable, or whose `LastSeenUtc`, `ObservationCount` or `LocalIp` is unreadable: the rewrite would replace the value with a default;
- a quote never closed before the end of the log: the message names the log and the line where the quote opens;
- a CSV log that is missing or holds no flow while the NDJSON log given by `--ndjson` holds flows: the rewrite would erase them.

Correct or remove the line, or write to a new log with `--output`.

`audit` (when it resumes its log), `map`, `export rules`, `export ndjson` and `resolve` find the separator of each log from its first line, so logs written with different separators can be read together. The candidates are `,`, `;`, tab, `|` and `CsvDelimiter` (`,` for `resolve`): on a header, the one that splits out the most audit column names wins; on a line without header, the one that occurs most often outside quotes. `CsvDelimiter` (`,` for `resolve`) wins a tie and is used for an empty first line. A separator outside these four can be read only through `CsvDelimiter`, and not by `resolve`.

The first line is taken as a header when it names an audit column; otherwise the log is read by position, in the order of the table above; `resolve` copies a log without header and with fewer than 20 fields unchanged. `map` takes the first line as a header only when it names `ServerName` or `LocalPort`.

In `export rules` and `export ndjson`, a value that cannot be read takes a default instead of stopping the command: the time of reading for `FirstSeenUtc`, `FirstSeenUtc` for `LastSeenUtc`, `1` for `ObservationCount`, `Tcp`, `Outbound`, `127.0.0.1` for `LocalIp`, `0.0.0.0` for `RemoteIp`, `0` for a port or the process identifier; an empty `TcpState` becomes `Established`, and an empty `ServerName` the name of the machine that reads the log. A line with neither `ServerName` nor `LocalPort`, or with a port outside 0-65535, is left out. `map` reads only what the map needs: an unreadable `Direction` becomes `Listening` when the remote port is 0, `Outbound` otherwise.

`map` leaves out each line without a valid local and remote port and names it on the console. A quote never closed stops `export` and `resolve` with code 1 and a message naming the log and the line; `map` keeps the flows before that line and reports the rest of the log as one line left out.

## NDJSON

Written by `audit --ndjson` and by `export ndjson`. One JSON object per line, always with the 20 values of the CSV log under camel-case names, whatever `--columns` says:

`firstSeenUtc`, `lastSeenUtc`, `observationCount`, `serverName`, `serverOsVersion`, `serverPrimaryIp`, `protocol`, `direction`, `tcpState`, `localIp`, `localPort`, `remoteIp`, `remotePort`, `remoteHostName`, `processId`, `processName`, `executablePath`, `commandLine`, `targetExecutable`, `userName`.

`observationCount`, `localPort`, `remotePort` and `processId` are numbers; the other values are strings, empty when unknown, never `null`. Dates are ISO 8601 with their offset (`2026-03-14T08:30:00.1234567+00:00`). Accented characters are written as they are. PortAtlas reads each line break of a command line as a space; a line break of an older CSV log converted by `export ndjson` is written as `\n`.

`export rules`, `export ndjson` and `resolve` read NDJSON files (`.ndjson`, `.jsonl`) as well as CSV files. All three read property names without case. `export rules` and `export ndjson` skip a line that is not a valid JSON object without a message; `resolve` copies such a line unchanged, as well as a line holding two keys that differ only in case, and writes a host name under the key spelling the line already uses.

## Firewall rule matrix

Written by `export rules`. One row per direction, source address, destination address, destination port, protocol and process; process names are compared with case. By default, listeners and ephemeral destination ports (49152 and above) are left out: `--listeners-only` exports only the listeners, and `--include-ephemeral-ports` keeps the ephemeral ports (see the [command line reference](cli.md)).

Rows are sorted by direction, zone, port, destination address, source address, protocol and process: the numbering does not depend on the order of the audit logs given to `--input`. Addresses are sorted as text, so `192.168.1.10` comes before `192.168.1.2`. The separator is `CsvDelimiter`. The `ExecutablePath`, `UserName` and host names of a row come from the most recently seen log row that has a value.

A value that starts with `=`, `+`, `-`, `@` (or their full-width forms), a tab or a carriage return is written with a leading apostrophe, so that a spreadsheet shows it as text, apostrophe included, instead of running it as a formula: a process name or a host name can be chosen by a third party. The audit log keeps its values as they are, since PortAtlas reads it back.

| Column | Content |
| :--- | :--- |
| `RuleId` | `RULE-001`, `RULE-002`… in sort order |
| `Direction` | `Outbound` or `Inbound` |
| `SourceIp` | Client address; `0.0.0.0` or `::` for a listener |
| `SourceHost` | Client name: the audited server for an outbound flow, the remote name (or address, when no name is known) for an inbound flow, `Any` for a listener |
| `DestinationIp` | Server address |
| `DestinationHost` | Server name, or its address when no name is known |
| `DestinationZone` | `Internal` (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `127.0.0.0/8`, `169.254.0.0/16`, `::1`, `fc00::/7`, `fe80::/10`, `0.0.0.0`, `::`) or `Public` (every other address) |
| `Protocol` | `TCP` or `UDP` |
| `DestinationPort` | Service port |
| `ServiceName` | Name deduced from the port and process (for example `Oracle-TNS` for 1521) |
| `ProcessName` | Process owning the socket |
| `ExecutablePath` | Path of that process |
| `UserName` | Account of that process, as in the audit log |
| `ObservationCount` | Sum of the observation counts of the merged flows |
| `FirstSeenUtc` | Earliest first observation |
| `LastSeenUtc` | Latest observation |

### Listening socket matrix

Written by `export rules --listeners-output`, in the same columns as the firewall rule matrix: the rows that `--listeners-only` would write, from the same logs and filters, while the rule matrix keeps the flows.

## Flow matrix

Written by `export rules --flows-output`, from the rows of the firewall rule matrix after filtering. One row per source address, destination address, protocol and destination port: a connection recorded by the log of its source (an `Outbound` rule) and by the log of its destination (an `Inbound` rule) is one row, and so is a flow opened by several processes. A loopback or `0.0.0.0` / `::` destination names no machine: the same local flow on two servers stays on two rows.

Rows are sorted by zone, port, destination address, source address, protocol and server, as text: the numbering does not depend on the order of the logs. An address that belongs to an audited server, found as a local address in its own log, is named after that server, whichever log saw the flow; any other address takes the name given by the log of its own end, otherwise the name given by the other end, otherwise the address. A value that could start a formula gets the same leading apostrophe as in the firewall rule matrix.

| Column | Content |
| :--- | :--- |
| `FlowId` | `FLOW-001`, `FLOW-002`… in sort order |
| `SourceIp`, `SourceHost` | Client address and name |
| `DestinationIp`, `DestinationHost` | Server address and name |
| `DestinationZone` | `Internal` or `Public`, as in the rule matrix |
| `Protocol` | `TCP` or `UDP` |
| `DestinationPort` | Service port |
| `ServiceName` | Service named by the destination rules (by the source rules when only the source saw the flow); `port/protocol` when they name several services |
| `SourceProcesses` | Processes that opened the flow, sorted and separated by `, `; empty when only the destination saw it |
| `DestinationProcesses` | Processes that accepted the flow; empty when only the source saw it |
| `SeenFrom` | `Source`, `Destination` or `Both`: the end whose log recorded the flow |
| `SourceObservationCount` | Observations recorded by the source log |
| `DestinationObservationCount` | Observations recorded by the destination log |
| `FirstSeenUtc` | Earliest first observation on either end |
| `LastSeenUtc` | Latest observation on either end |

`SeenFrom` shows a gap in the logs: between two audited servers, a flow seen from one end only means that the logs do not cover the same period, or that an address translation or a second network card hides one end behind another address.

## Pair matrix

Written by `export rules --pairs-output`, from the rows of the flow matrix. One row per source address and destination address: the two directions between two servers stay on two rows, as firewall rules do. Sorted by source then destination address, as text. The host names are those of the first flow of the pair. A value that could start a formula gets the same leading apostrophe as in the firewall rule matrix.

| Column | Content |
| :--- | :--- |
| `PairId` | `PAIR-001`, `PAIR-002`… in sort order |
| `SourceIp`, `SourceHost` | Client address and name, as in the flow matrix |
| `DestinationIp`, `DestinationHost` | Server address and name, as in the flow matrix |
| `FlowCount` | Number of flow matrix rows merged into the pair |
| `Ports` | Distinct destination ports, `port/protocol`, sorted by port (`443/TCP, 873/TCP`) |
| `ObservationCount` | Sum of the observations of the merged flows, each flow counted once: the larger of its two ends |
| `FirstSeenUtc` | Earliest first observation |
| `LastSeenUtc` | Latest observation |
| `SeenFrom` | Distinct `SeenFrom` values of the merged flows, in the order `Source`, `Destination`, `Both` |

## Host rule matrix

Written by `export rules --host-rules-output`, from the rows of the flow matrix, for network security groups and security lists, which are set per resource and per direction. Each flow gives an `Outbound` rule to its source and an `Inbound` rule to its destination. Only a private address (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16`, `fc00::/7`, `fe80::/10`) carries rules: a public address shows only as the peer of a private host, and a loopback or `0.0.0.0` / `::` flow never crosses the network.

Rows are sorted by host name, host address, direction (`Inbound` first), port, protocol and peer address, as text. A value that could start a formula gets the same leading apostrophe as in the firewall rule matrix.

| Column | Content |
| :--- | :--- |
| `HostRuleId` | `HOST-001`, `HOST-002`… in sort order |
| `Host`, `HostIp` | Host that carries the rule, named as in the flow matrix |
| `Audited` | `Yes` when a log of this address was read, otherwise `No` |
| `Direction` | `Inbound` when the host accepts the connection, `Outbound` when it opens it |
| `PeerIp`, `PeerHost` | Other end of the flow |
| `PeerZone` | `Internal` or `Public`, as `DestinationZone` |
| `Protocol` | `TCP` or `UDP` |
| `Port` | Destination port of the flow: on the host for an inbound rule, on the peer for an outbound rule |
| `ServiceName` | As in the flow matrix |
| `LocalProcesses` | Processes of the host that use the flow; empty when the host's log did not record it |
| `Evidence` | `Observed` when the host's own log recorded the flow, `Inferred` when only the peer's log did |
| `ObservationCount` | Observations recorded by the host's log, or by the peer's log for an `Inferred` rule |
| `FirstSeenUtc` | Earliest first observation of the flow |
| `LastSeenUtc` | Latest observation of the flow |
| `FlowId` | Row of the flow matrix the rule comes from |

## Group rule matrix

Written by `export rules --group-rules-output`, from the rows of the firewall rule matrix (same filters), with the address groups of `AddressGroups` and `--address-group`. One row per source group, destination group, protocol and port. An address in no group stands for itself, in one form: IPv4 for an IPv4-mapped address (`::ffff:192.168.1.53` is `192.168.1.53`), without the zone of a link-local address (`fe80::1%3` is `fe80::1`); the source of a listening socket is `Any`. A flow recorded at both of its ends is two rules of the firewall rule matrix: the row counts both and adds their observations. Rows are sorted by source and destination, as text without case, then by port and protocol. A value that could start a formula gets the same leading apostrophe as in the firewall rule matrix.

| Column | Content |
| :--- | :--- |
| `GroupRuleId` | `GROUP-001`, `GROUP-002`… in sort order |
| `SourceGroup` | Group of the client address, its address when it is in no group, or `Any` |
| `DestinationGroup` | Group of the server address, or its address when it is in no group |
| `Protocol` | `TCP` or `UDP` |
| `DestinationPort` | Destination port |
| `RuleCount` | Number of firewall rule matrix rows merged into the row |
| `ObservationCount` | Sum of the observations of those rules |
| `FirstSeenUtc` | Earliest first observation |
| `LastSeenUtc` | Latest observation |
| `Processes` | Distinct process names of those rules, in ordinal order (`caddy, curl`) |

## Topology map

`map --html` and `audit --html` write a single HTML file that opens without network access. The page offers layout, sorting, routing and spacing choices, text filters applied with the `Apply Filters` button or `Enter`, pagination of servers, an SVG export, and a `CLI Command` section showing the `PortAtlas map` command that reproduces the current filters and layout choices. The copied command contains no `-i` option: add the audit logs to read.

`map --drawio` writes an uncompressed Draw.io file (`.drawio`) for Draw.io Desktop or diagrams.net. The target servers are grouped into zones: one zone per /24 subnet for IPv4 addresses, a single `Internet` zone for all public addresses (public IPv4, and IPv6 in `2000::/3` outside `2001:db8::/32`), an `Other` zone for the other IPv6 addresses, and an `Unknown` zone for a server without address. IPv4 ranges that are not public, such as the documentation ranges (`192.0.2.0/24`, `198.51.100.0/24`, `203.0.113.0/24`) and `100.64.0.0/10`, get a /24 zone. Zones continue on a new row beyond a fixed width, so the diagram stays readable on large networks. With `--drawio-subnets off` (`DrawioSubnets`), no zone is drawn and the servers are laid out as a plain grid. `--sort` (`MapSortMethod`) sets the order of the servers and, for the subnet methods, of the zones.

## DNS cache

`portatlas_dns_cache.json` (`DnsCacheFilePath`) maps each resolved address to its name and to where the name came from. It is an indented JSON object whose property names are in Pascal case (the NDJSON log uses camel case):

```json
{
  "192.168.1.20": {
    "HostName": "proxmox.home.arpa",
    "Organization": null,
    "Country": null,
    "NetworkRange": null,
    "Source": "DNS",
    "ResolvedAtUtc": "2026-03-14T08:30:00.1234567+00:00"
  }
}
```

| Property | Content |
| :--- | :--- |
| `HostName` | Name of the address; empty when none was found |
| `Organization`, `Country`, `NetworkRange` | Owner of the address, from RDAP; `null` when unknown |
| `Source` | `DNS`, `RDAP` or `None` (no name found), written by `audit` and `resolve`; `Local` (loopback), written by `resolve` only |
| `ResolvedAtUtc` | Date of the lookup |

An alias of `CustomHostAliases` is never written to the cache. A cache in the 1.x form, `{ "192.168.1.20": "proxmox.home.arpa" }`, is still read. An entry that cannot be read is dropped and its address is asked again.

`audit` reads and updates it; `resolve` merges several of them; `lookup` reads it.

## Enriched copy

`resolve` writes, beside each audit log `<name>.<ext>`, a copy `<name>_enriched.<ext>` in which `RemoteHostName` is filled from the cache and the lookups; `--in-place` rewrites the log itself instead. A CSV copy keeps the separator of the log and its byte order mark, or its absence; in an NDJSON copy, only the lines whose name changed are rewritten. See [`resolve`](cli.md) and [Audit isolated servers](../how-to/audit-isolated-servers.md).

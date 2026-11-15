> **Language / Langue**: **English** · [Français](output-formats.fr.md)

# Output formats reference

## CSV audit log

Written by `audit` (`-o`, `OutputCsvPath`). Fields follow RFC 4180 quoting; the separator is `CsvDelimiter` (`,` by default). One record per deduplicated flow. `audit` writes each line break of a field (a command line, for instance) as a space, so the logs it writes have one line per flow; a log written by an earlier version may hold a quoted field with line breaks, so a record can span several lines: `audit`, `export` and `resolve` read it whole, `map` still reads the log line by line. A quote never closed before the end of the log stops `audit`, `export` and `resolve` with code 1 and a message naming the log and the line where the quote opens. A flow is identified by its protocol, its direction, its application (`TargetExecutable`, or `ExecutablePath` when there is no target, or `ProcessName` when the path is empty too) and:

| Direction | Also identified by | Ignored |
| :--- | :--- | :--- |
| `Outbound` | remote address and remote port | local (client) port |
| `Inbound` | local port and remote address | remote (client) port |
| `Listening` | local port | addresses |

Repeated connections to the same service therefore update one line instead of adding new ones.

`--columns` and `CsvColumns` choose and order the columns; without them, the 20 columns below are written in this order.

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
| `TcpState` | Network | TCP state (`Established`, `Listen`, `TimeWait`, `CloseWait`…) |
| `LocalIp` | Network | Local address of the socket |
| `LocalPort` | Network | Local port of the socket |
| `RemoteIp` | Network | Remote address (`0.0.0.0` for a listener) |
| `RemotePort` | Network | Remote port (`0` for a listener) |
| `RemoteHostName` | Network | Name from an alias, reverse DNS or the RDAP owner |
| `ProcessId` | Process | Process identifier |
| `ProcessName` | Process | Executable name of the process |
| `ExecutablePath` | Process | Full path of the executable; empty when the process cannot be opened |
| `CommandLine` | Process | Command line of the process |
| `TargetExecutable` | Process | Application started by a launcher (`.jar`, `.py`, `.ps1`, `.sh`, `.dll`…) |
| `UserName` | Process | Account of the process: `DOMAIN\account` on Windows (the SID when the account has no name any more), the user name on Linux (the numeric UID when it is not in `/etc/passwd`); empty when the process cannot be opened |

Processes with identifier 0 or below show `SYSTEM`.

`audit` keeps the flows in memory and rewrites the whole log from them, one line per flow with its latest count, last-seen date and name: when it starts, every `--rewrite-interval` minutes (`LogRewriteIntervalMinutes`, 5 by default) and when it stops. The NDJSON log (`--ndjson`) is rewritten at the same times, from the same flows. An abrupt stop (power loss, killed process) therefore loses at most the observations of the last period. The log is written with the columns of the current run: a column left out of `--columns` since the previous run disappears from the whole log.

Each rewrite writes `<log>.tmp`, renames it `<log>.pending` once complete, then replaces the log: a program reading the log gets the earlier version or the new one, never a mix of both. Windows refuses the replacement while another program holds the log open: the audit goes on, `<log>.pending` stays, and the next period tries again. When the rewrite fails at stop, `audit` ends with code 1 and a message naming the log. At start, a `<log>.pending` left by an interrupted rewrite replaces the log and a `<log>.tmp` is deleted; a log that cannot be replaced at start stops `audit` with code 1 and a message naming it.

At startup, `audit` reads the whole existing CSV log, so that counts and first-seen dates continue across restarts. The log is then rewritten from what was read, so a log that cannot be read whole stops `audit` with an error naming the columns or the line, and exit code 1:

- a log without one of the columns that identify a flow (`Protocol`, `Direction`, `LocalPort`, `RemoteIp`, `RemotePort`, `ProcessName`, `ExecutablePath`, `TargetExecutable`) or without `FirstSeenUtc`: its flows would match none of the observed ones;
- a line whose `FirstSeenUtc`, `Protocol`, `Direction`, `LocalPort`, `RemoteIp` or `RemotePort` is empty or unreadable, or whose `LastSeenUtc`, `ObservationCount` or `LocalIp` is unreadable: the rewrite would replace the value with a default.

Correct or remove the line, or write to a new log with `--output`.

## NDJSON

Written by `audit --ndjson` and by `export ndjson`. One JSON object per line, with the same 20 values as the CSV log under camel-case names:

`firstSeenUtc`, `lastSeenUtc`, `observationCount`, `serverName`, `serverOsVersion`, `serverPrimaryIp`, `protocol`, `direction`, `tcpState`, `localIp`, `localPort`, `remoteIp`, `remotePort`, `remoteHostName`, `processId`, `processName`, `executablePath`, `commandLine`, `targetExecutable`, `userName`.

`export rules` and `resolve` read NDJSON files (`.ndjson`, `.jsonl`) as well as CSV files.

## Firewall rule matrix

Written by `export rules`. One row per direction, source address, destination address, destination port, protocol and process, sorted by direction, zone, port, destination, source, protocol and process: the numbering does not depend on the order of the audit logs given to `--input`. A value that starts with `=`, `+`, `-`, `@` (or their full-width forms), a tab or a carriage return is written with a leading apostrophe, so that a spreadsheet shows it as text, apostrophe included, instead of running it as a formula: a process name or a host name can be chosen by a third party. The audit log keeps its values as they are, since PortAtlas reads it back.

| Column | Content |
| :--- | :--- |
| `RuleId` | `RULE-001`, `RULE-002`… in sort order |
| `Direction` | `Outbound` or `Inbound` |
| `SourceIp` | Client address; `0.0.0.0` for a listener |
| `SourceHost` | Client name: the audited server for an outbound flow, the remote name for an inbound flow, `Any` for a listener |
| `DestinationIp` | Server address |
| `DestinationHost` | Server name |
| `DestinationZone` | `Internal` (loopback, private range, `0.0.0.0`, `::`) or `Public` |
| `Protocol` | `TCP` or `UDP` |
| `DestinationPort` | Service port |
| `ServiceName` | Name deduced from the port and process (for example `Oracle-TNS` for 1521) |
| `ProcessName` | Process owning the socket |
| `ExecutablePath` | Path of that process |
| `UserName` | Account of that process, as in the audit log |
| `ObservationCount` | Sum of the observation counts of the merged flows |
| `FirstSeenUtc` | Earliest first observation |
| `LastSeenUtc` | Latest observation |

## Pair matrix

Written by `export rules --pairs-output`, from the rows of the firewall rule matrix after filtering. One row per source address and destination address: the two directions between two servers stay on two rows, as firewall rules do. Sorted by source then destination address. A value that could start a formula gets the same leading apostrophe as in the firewall rule matrix.

| Column | Content |
| :--- | :--- |
| `PairId` | `PAIR-001`, `PAIR-002`… in sort order |
| `SourceIp`, `SourceHost` | Client address and name, as in the rule matrix |
| `DestinationIp`, `DestinationHost` | Server address and name, as in the rule matrix |
| `FlowCount` | Number of rule matrix rows merged into the pair |
| `Ports` | Distinct destination ports, `port/protocol`, sorted by port (`443/TCP, 873/TCP`) |
| `ObservationCount` | Sum of the observation counts of the merged rows |
| `FirstSeenUtc` | Earliest first observation |
| `LastSeenUtc` | Latest observation |

## Topology map

`map --html` and `audit --html` write a single HTML file that opens without network access. The page offers layout, sorting, routing and spacing choices, text filters applied with the `Apply Filters` button or `Enter`, pagination of servers, an SVG export, and a `CLI Command` section showing the `PortAtlas map` command that reproduces the current filters and layout choices. The copied command contains no `-i` option: add the audit logs to read.

`map --drawio` writes an uncompressed Draw.io file (`.drawio`) for Draw.io Desktop or diagrams.net. The target servers are grouped into one zone per /24 subnet (all public IPv4 addresses share a single "Internet" zone), and zones continue on a new row beyond a fixed width so the diagram stays readable on large networks. With `--drawio-subnets off` (`DrawioSubnets`), no zone is drawn and the servers are laid out as a plain grid. `--sort` (`MapSortMethod`) sets the order of the servers and, for the subnet methods, of the zones.

## DNS cache

`netflow_dns_cache.json` (`DnsCacheFilePath`) maps each resolved address to its name and to where the name came from, for example:

```json
{ "198.51.100.20": { "HostName": "app.example.com", "Source": "DNS" } }
```

`audit` reads and updates it; `resolve` merges several of them; `lookup` reads it.

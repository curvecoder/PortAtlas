> **Language / Langue**: **English** · [Français](cli.fr.md)

# Command-line reference

PortAtlas is a set of commands. Each command reads only its own options: an option given to a command that does not read it, an unknown option or a value outside the accepted list stops the program with an error and exit code 1.

```text
PortAtlas <command> [options]
```

| Command | Purpose |
| :--- | :--- |
| [`audit`](#audit) | Scan sockets continuously and record deduplicated flows |
| [`map`](#map) | Render an HTML topology map and/or a Draw.io diagram from audit logs |
| [`export rules`](#export-rules) | Consolidate audit logs into a firewall rule matrix |
| [`export ndjson`](#export-ndjson) | Convert CSV audit logs into one NDJSON file |
| [`resolve`](#resolve) | Merge DNS caches and write host names into audit logs |
| [`lookup`](#lookup) | Diagnose the name resolution of one IP address |
| [`config init`](#config-init) | Write a configuration file with the default values |

Without a command, PortAtlas prints the help and exits with code 0. `-h` or `--help` after any command prints the help of that command.

## Sources of values

A value comes from, in order of precedence:

1. the command line;
2. the configuration file given by `--config`, or `portatlas.json` in the working directory, or `portatlas.json` next to the executable;
3. the default built into the program.

The key of the configuration file matching each option is listed in the tables below and described in the [configuration reference](configuration.md).

## Global option

| Option | Value | Default | Effect |
| :--- | :--- | :--- | :--- |
| `--config` | `<path>` | `portatlas.json` | Configuration file read by every command |
| `--version` | none | | Prints the version and exits (root command only) |

## File name tokens

Output paths, and the paths of the configuration file, accept these tokens:

| Token | Replaced by |
| :--- | :--- |
| `{ServerName}` | Name of the machine |
| `{HostName}` | Name of the machine (same value as `{ServerName}`) |
| `{Date}` | UTC date, `yyyy-MM-dd` |
| `{Timestamp}` | UTC date and time, `yyyyMMdd_HHmmss` |

## Shared option families

### Inputs

Read by `map`, `export rules`, `export ndjson` and `resolve`.

| Option | Value | Default | Effect |
| :--- | :--- | :--- | :--- |
| `-i`, `--input` | `<path>`, repeatable | `OutputCsvPath` of the configuration | A file, a directory or a wildcard pattern on the file name (`audits/nas-*.csv`) |

A directory contributes its files with the extensions read by the command (`.csv` for `map` and `export ndjson`; `.csv`, `.ndjson`, `.jsonl` for `export rules` and `resolve`). Subdirectories are not read. When no file matches, the command prints `No audit log found for: …` and exits with code 1.

Among the files of a directory or of a wildcard pattern, the command reads the audit logs only: a CSV whose first line names the `ServerName` or `LocalPort` column, an NDJSON whose first record carries one of these two properties. A matrix written by `export rules`, a log without header line or an empty file is left out. When several files of a directory hold the same audit, the command keeps one: `map`, `export rules` and `export ndjson` read the copy written by `resolve` (`<name>_enriched.<ext>`) rather than the original, and `export rules` reads the CSV rather than the NDJSON of the same name; `resolve` enriches each original, CSV and NDJSON alike, and leaves out an enriched copy whose original is present. Each file left out is named on the console with its reason. A file whose own path is given to `--input` is always read, whatever it contains. The enriched copy does not receive the flows observed after `resolve` ran: run `resolve` again, or name the original, to read them.

### Flow filters

Read by `audit` (for its map), `map` and `export rules`. List values are separated by commas; a list option can be repeated and the values add up.

| Option | Value | Configuration key | Effect |
| :--- | :--- | :--- | :--- |
| `--include-ports` | ports or ranges | `MapIncludedPorts` | Keep only these service ports (`80,443,8000-8080`) |
| `--exclude-ports` | ports or ranges | `MapExcludedPorts` | Remove these service ports |
| `--include-hosts` | wildcard patterns | `MapIncludedHosts` | Keep only servers whose name matches (`nas*,*.home.arpa`) |
| `--exclude-hosts` | wildcard patterns | `MapExcludedHosts` | Remove servers whose name matches |
| `--include-ips` | addresses or CIDR | `MapIncludedIps` | Keep only these addresses (`10.0.0.0/8`) |
| `--exclude-ips` | addresses or CIDR | `MapExcludedIps` | Remove these addresses |
| `--include-processes` | names | `MapIncludedProcesses` | Keep only these process names or application targets |
| `--exclude-processes` | names | `MapExcludedProcesses` | Remove these process names or application targets |
| `--direction` | `all`, `in`, `out` | `MapDirection` | Flow direction to keep; default `all` |
| `--listeners-only` | none | `MapListenersOnly` | Keep only listening sockets |
| `--include-ephemeral-ports` | none | `MapExcludeEphemeralPorts` | Keep ports 49152-65535, removed by default |

A port is a number between 0 and 65535, a range is written in increasing order (`8000-8080`). An address filter is an address (`192.168.1.50`, an IPv4 address being written with its four decimal bytes) or a CIDR subnet; a subnet written with the address of one of its hosts (`192.168.1.10/24`) is read as the subnet of that host (`192.168.1.0/24`). Any other value given to `--include-ports`, `--exclude-ports`, `--include-ips` or `--exclude-ips`, or to their configuration keys, stops the command before it starts: the message names the option, the key and the value, and the exit code is 1.

### Map rendering

Read by `audit` and `map`. Accepted values are compared without case.

| Option | Value | Configuration key | Default |
| :--- | :--- | :--- | :--- |
| `--html` | `<path>` | `OutputHtmlMapPath` | none |
| `--drawio` | `<path>` | `OutputDrawioPath` | none |
| `--layout` | `layered`, `tree`, `horizontal-tree`, `organic`, `circular`, `grid` | `MapLayoutMode` | `layered` |
| `--sort` | `barycentric`, `tiers`, `subnet`, `degree`, `alpha`, `ip` | `MapSortMethod` | `barycentric` |
| `--routing` | `bezier`, `orthogonal`, `straight`, `bus` | `MapRoutingMode` | `bezier` |
| `--spacing` | `compact`, `balanced`, `spacious` | `MapSpacingProfile` | `balanced` |
| `--edge-labels` | `ports`, `none` | `MapEdgeLabels` | `ports` |
| `--drawio-edges` | `adaptive`, `direction`, `pair`, `none` | `DrawioEdgeMode` | `adaptive` |
| `--drawio-subnets` | `on`, `off` | `DrawioSubnets` | `on` |
| `--drawio-theme` | `dark`, `light` | `DrawioTheme` | `dark` |

`--layout`, `--sort`, `--routing`, `--spacing` and `--edge-labels` set the initial state of the HTML map; the map page can change them. `--sort` also orders the target servers, and the subnet zones, of the Draw.io diagram.

- `bus` routing: the arrows of a server share one trunk per direction, then split into one branch per target. The branches that reach a server through the same side and in the same direction end under one arrow head. Designed for the `tree` layout.
- `--edge-labels none`: unchecks *Show Ports*. The arrows lose their port labels and the flows of a server pair are merged into one arrow, with a head at each end that receives a flow. The server inspector still lists the ports.
- `--drawio-edges pair`: one edge per server pair in the Draw.io diagram, with two heads when flows run both ways.

## audit

Scans the TCP and UDP sockets of the machine at a fixed interval, attributes each socket to its process and writes one line per deduplicated flow in the CSV audit log. Runs until `Q`, `Ctrl+C` or `SIGTERM`.

```text
PortAtlas audit [options]
```

| Option | Value | Configuration key | Default | Effect |
| :--- | :--- | :--- | :--- | :--- |
| `-o`, `--output` | `<path>` | `OutputCsvPath` | `netflow_audit.csv` | CSV audit log |
| `--ndjson` | `<path>` | `OutputNdjsonPath` | none | NDJSON log written alongside the CSV log |
| `--interval` | seconds, at least 1 | `ScanIntervalSeconds` | `5` | Time between two scans |
| `--columns` | column names | `CsvColumns` | all 20 | Ordered CSV columns ([list](output-formats.md#csv-audit-log)) |
| `--rewrite-interval` | minutes, at least 1 | `LogRewriteIntervalMinutes` | `5` | Time between two rewrites of the logs, which are also written at start and stop ([details](output-formats.md#csv-audit-log)) |
| `--no-tui` | none | `EnableDashboard` | dashboard on | Run without the terminal dashboard |
| `--no-dns` | none | `ResolveRemoteHostNames` | DNS on | No name resolution at all: no reverse DNS, no RDAP, no alias |
| `--no-rdap` | none | `ResolveIpOwners` | RDAP on | No RDAP query to Internet registries; reverse DNS stays active |
| `--dns-timeout-ms` | milliseconds, at least 1 | `DnsResolutionTimeoutMs` | `3000` | Time to wait for a reverse DNS answer |
| `--exclude-remote-ips` | addresses or CIDR | `ExcludedRemoteIps` | none | Remote addresses that are not recorded |
| `--alias` | `ip=host` or `ip:host`, repeatable | `CustomHostAliases` | none | Host name used instead of reverse DNS |
| `--mask-server` | `<name>` | `MaskServerName` | none | Server name shown by the dashboard |
| `--mask-ip` | `<address>` | `MaskLocalIp` | none | Local address shown by the dashboard |
| `--obfuscate` | none | none | off | Shows `HOMELAB-01` and `192.168.1.10` unless `--mask-server` or `--mask-ip` is given |

Families read: flow filters and map rendering. When `--html` (or `OutputHtmlMapPath`) is set, the HTML map is rewritten after every scan; the `M` key writes it on demand, with the Draw.io diagram when `--drawio` (or `OutputDrawioPath`) is set. One map is written at a time: scans that end while it is written lead to a single later rewrite, and a map that cannot be written (file locked or read-only) shows a warning while the audit goes on.

The dashboard is not shown when the input or the output of the process is redirected, even without `--no-tui`.

A scan that fails (socket table that cannot be read, file held for a moment) does not stop the audit: the dashboard shows `Scan failed (n of 10 in a row): …` in the title of the flow table, or the line is printed with its time without the dashboard, and the next scan runs at the usual interval. After 10 failed scans in a row the audit stops with `The audit stopped after 10 failed scans in a row: …` and exit code 1, after writing the logs.

An invalid column name in `--columns` or `CsvColumns`, or a list without `FirstSeenUtc`, `Protocol`, `Direction`, `LocalPort`, `RemoteIp`, `RemotePort`, `ProcessName`, `ExecutablePath` and `TargetExecutable` (the log is read back at the next start, see [output formats](output-formats.md#csv-audit-log)), stops the command with `Column configuration error: …` and exit code 1.

With name resolution on, the DNS cache named by `DnsCacheFilePath` is read at start and saved during the audit. If it exists but cannot be read (truncated or invalid JSON, file held by another program or not readable by the account), the audit stops before its first scan with `The DNS cache '<path>' cannot be read: …` and exit code 1, and writes neither the logs nor the cache, so that the cache is not replaced by the names of this run alone.

## map

Builds the topology graph from CSV audit logs and writes the requested outputs.

```text
PortAtlas map [-i <path>]... [--html <path>] [--drawio <path>] [options]
```

Outputs:

- `--html` writes the HTML map, `--drawio` the Draw.io diagram; both can be given.
- Without `--html` and `--drawio`, the command writes the HTML map to `OutputHtmlMapPath` (or `netflow_topology.html` when the key is empty), plus the Draw.io diagram when `OutputDrawioPath` is set.

A log line without a valid local and remote port is left out of the map; the console names the log and the numbers of these lines (the first ten).

Families read: inputs (`.csv`), flow filters, map rendering.

## export rules

Consolidates audit logs into a matrix with one row per source, destination, port, protocol and process.

```text
PortAtlas export rules [-i <path>]... [-o <path>] [options]
```

| Option | Value | Default | Effect |
| :--- | :--- | :--- | :--- |
| `-o`, `--output` | `<path>` | `firewall_rules.csv` | Matrix file ([columns](output-formats.md#firewall-rule-matrix)) |
| `--pairs-output` | `<path>` | not written | Also writes the pair matrix, one row per source and destination ([columns](output-formats.md#pair-matrix)); same tokens as `--output` |
| `--min-observations` | number, at least 1 | `1` | Keep flows observed at least this number of times |
| `--active-within-days` | number, at least 1 | no limit | Keep flows last seen during the last N days |
| `--zone` | `all`, `internal`, `public` | `all` | Destination zone to keep |

Families read: inputs (`.csv`, `.ndjson`, `.jsonl`) and flow filters, with two differences:

- ephemeral destination ports (49152 and above) are removed unless `--include-ephemeral-ports` is given; the `MapExcludeEphemeralPorts` key is not read;
- listening sockets are exported only with `--listeners-only` (or `MapListenersOnly`), as inbound rules from any source (`0.0.0.0`, host `Any`). A socket is listening when its `Direction` is `Listening`, whatever its state: TCP listeners and bound UDP sockets.

A destination is `Internal` when it is a loopback address, an address of a private range, or `0.0.0.0` / `::`; otherwise it is `Public`.

A rule carries the server name and the remote name found on any of its log rows, the most recently seen one when several rows disagree. `--include-hosts` and `--exclude-hosts` are tested on the consolidated rule, against both of its ends, by name and by the address the rule shows: one match is enough, to keep the rule as to remove it. The `Any` source of a listener is not tested.

`--include-ips` and `--exclude-ips` follow the same rule on the two addresses the rule shows, whatever the direction of the flow: one match is enough, to keep the rule as to remove it. Excluding a remote address removes the flows it receives and the flows it opens; including the subnet of the server keeps all of its flows, excluding it removes them all. A listening socket is tested on the address it is bound to only: a listener bound to `0.0.0.0` or `::` is not in the subnet of its server.

A CSV log is read with the separator found on its own first line (`,`, `;`, tab or `|`), so logs written with different `CsvDelimiter` values can be consolidated together: the separator between the column names of the header or, in a log without header, the one that occurs most often outside the quoted fields. The matrix is written with `CsvDelimiter`.

## export ndjson

Converts CSV audit logs into a single NDJSON file, one JSON object per flow.

```text
PortAtlas export ndjson [-i <path>]... [-o <path>]
```

| Option | Value | Default | Effect |
| :--- | :--- | :--- | :--- |
| `-o`, `--output` | `<path>` | `netflow_audit.ndjson` | NDJSON file ([fields](output-formats.md#ndjson)) |

Family read: inputs (`.csv`). Input files are read sequentially and only the current file is held in memory.

## resolve

Merges DNS caches, resolves the addresses that still have no host name and writes enriched copies of the audit logs.

```text
PortAtlas resolve [-i <path>]... [--cache <path>]... [options]
```

| Option | Value | Default | Effect |
| :--- | :--- | :--- | :--- |
| `--cache` | `<path>`, repeatable | `DnsCacheFilePath` | DNS cache to merge: a file, a directory (its `.json` files) or a pattern |
| `--output-cache` | `<path>` | `DnsCacheFilePath` | Merged cache to write |
| `--in-place` | none | off | Overwrite the audit logs instead of writing `<name>_enriched.<ext>` |
| `--force-resolve` | none | off | Resolve every address again, including those that already have a name, and replace the names already in the logs. A lookup that finds nothing never erases a known name |
| `--rdap-delay-ms` | milliseconds, at least 0 | `200` | Delay between two RDAP queries |
| `--cache-only` | none | off | Send no DNS or RDAP query; use only the merged caches |
| `--dns-timeout-ms` | milliseconds, at least 1 | `DnsResolutionTimeoutMs` | Time to wait for a reverse DNS answer |

Family read: inputs (`.csv`, `.ndjson`, `.jsonl`). When two caches give different names for the same address, the most recent entry is kept and the conflict is printed. An entry without a name never conflicts with a named one: the named entry is kept.

Audit logs are optional. When `--cache` is given without `-i`, the audit logs are left alone and the caches alone are resolved: the addresses of the merged cache are the ones to resolve. Without `--force-resolve` only the entries without a name are resolved; with it, every entry is. When audit logs are given, the addresses to resolve are those of the logs. The cache named by `--output-cache` is always merged before it is written, even when `--cache` does not list it, so that running `resolve` from several machines completes the cache instead of replacing it. If that cache exists but cannot be read (truncated or invalid JSON, file held by another program or not readable by the account), the command stops before any lookup with `The DNS cache '<path>' cannot be read: …` and exit code 1, and writes nothing, so that the cache is not replaced by the names of this run alone. The other caches listed in `--cache` are only read: one that cannot be read adds no names and the run goes on.

`Ctrl+C`, and `SIGTERM` on Linux and macOS, stop the lookups: the addresses resolved before the stop are saved to the cache named by `--output-cache`, the audit logs not processed yet are left as they were, and the command ends with exit code `130`. Running it again resumes from that cache.

## lookup

Prints a six-step diagnostic of the resolution of one address: classification, local cache, operating system DNS settings, reverse DNS query, RDAP query, resulting cache entry.

```text
PortAtlas lookup <ip> [--dns-timeout-ms <milliseconds>]
```

`<ip>` must be an IPv4 or IPv6 address. The command reads `ExcludedRemoteIps`, `DnsCacheFilePath` and `DnsResolutionTimeoutMs` from the configuration. It sends the reverse DNS query with the same timeout as `audit`, so that a `SUCCESS` here means the audit names the address too, and the RDAP query for public addresses only; `ResolveIpOwners` and `--no-rdap` do not apply to it.

## config init

Writes a configuration file holding the default value of every key, with the 20 CSV columns listed.

```text
PortAtlas config init [<path>]
```

`<path>` is a file or a directory; the default is `portatlas.json` in the working directory. An existing file is overwritten.

## Option matrix

| Option | `audit` | `map` | `export rules` | `export ndjson` | `resolve` | `lookup` | `config init` |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| `--config` | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| `-i`, `--input` | | ✔ | ✔ | ✔ | ✔ | | |
| `-o`, `--output` | ✔ | | ✔ | ✔ | | | |
| Flow filters | ✔ | ✔ | ✔ | | | | |
| Map rendering, `--html`, `--drawio` | ✔ | ✔ | | | | | |
| `--ndjson`, `--interval`, `--columns`, `--rewrite-interval` | ✔ | | | | | | |
| `--no-tui`, `--no-dns`, `--no-rdap` | ✔ | | | | | | |
| `--dns-timeout-ms` | ✔ | | | | ✔ | ✔ | |
| `--exclude-remote-ips`, `--alias` | ✔ | | | | | | |
| `--mask-server`, `--mask-ip`, `--obfuscate` | ✔ | | | | | | |
| `--min-observations`, `--active-within-days`, `--zone`, `--pairs-output` | | | ✔ | | | | |
| `--cache`, `--output-cache`, `--in-place`, `--force-resolve`, `--rdap-delay-ms`, `--cache-only` | | | | | ✔ | | |

`config init` accepts `--config` because the option is global, but does not read it.

## Stopping through the standard input

When the environment variable `PORTATLAS_STOP_ON_INPUT_END` is `1` and the standard input is redirected, the end of that input stops the command as `Ctrl+C` does: `audit` writes its logs and saves its DNS cache, `resolve` saves the addresses already resolved and ends with exit code `130`, and the other commands end at once with exit code `130`. The end of the input is acted on as soon as it arrives, even when the input was empty from the start. The graphical interface sets the variable and closes the input to stop a command. Without the variable the input is not read, so a service started with an empty input keeps running.

## Exit codes

| Code | Meaning |
| :--- | :--- |
| `0` | Success, or help printed |
| `1` | Invalid command line, no audit log found, invalid CSV column name or column list without the columns of the flow key, `audit` log that cannot be read back or replaced at start or written at stop ([details](output-formats.md#csv-audit-log)), `audit` stopped by 10 failed scans in a row, a port or an address of a [flow filter](#flow-filters) that cannot be read, a `--config` path that is empty or does not exist, or a configuration file that cannot be read, is not valid (see the [configuration reference](configuration.md)) or sets `"IsolatedMode": true`, `audit` DNS cache or `resolve` output cache that cannot be read |
| `130` | `resolve` stopped by `Ctrl+C` or `SIGTERM`, after saving the addresses already resolved; a command other than `audit` [stopped through its input](#stopping-through-the-standard-input) |

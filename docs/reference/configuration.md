> **Language / Langue**: **English** · [Français](configuration.fr.md)

# Configuration file reference

PortAtlas reads a JSON file: the one given by `--config`, otherwise `portatlas.json` in the working directory, otherwise `portatlas.json` next to the executable. A missing key takes its default value. Without `--config`, a missing `portatlas.json` leaves every setting on its default value. A file given by `--config` that is found neither in the working directory nor next to the executable stops the command with an error naming the path, and exit code 1; so does an empty value given to `--config`. A file that exists stops the command with an error naming the file and the cause, and exit code 1, when it cannot be read (locked, access denied), is not valid JSON or holds only `null`, or when one of its keys is not a setting of this page (names are case-sensitive), has a value of the wrong type, is `null` or holds a `null` entry in a list or in `CustomHostAliases`: the defaults are not used in its place.

`PortAtlas config init` writes a file holding every key with its default value. Command-line options take precedence over the file ([CLI reference](cli.md#sources-of-values)).

The path keys `OutputCsvPath`, `OutputNdjsonPath`, `OutputHtmlMapPath`, `OutputDrawioPath` and `DnsCacheFilePath` accept the [file name tokens](cli.md#file-name-tokens) `{ServerName}`, `{HostName}`, `{Date}` and `{Timestamp}`. The keys read by each command are listed in the [CLI reference](cli.md).

## Audit

| Key | Type | Default | Effect | Option |
| :--- | :--- | :--- | :--- | :--- |
| `ScanIntervalSeconds` | integer | `5` | Seconds between two scans, at least 1 | `--interval` |
| `LogRewriteIntervalMinutes` | integer | `5` | Minutes between two rewrites of the audit logs, which are also written at start and stop; at least 1 | `--rewrite-interval` |
| `OutputCsvPath` | path | `portatlas_audit.csv` | CSV audit log; default input of the other commands | `-o` (`audit`) |
| `OutputNdjsonPath` | path | `""` | NDJSON log written alongside the CSV log; empty for none | `--ndjson` |
| `CsvColumns` | list of names | `[]` | Ordered CSV columns; empty for the 20 columns | `--columns` |
| `CsvDelimiter` | string | `","` | CSV field separator, first character used. Written by `audit` and in the matrices of `export rules`; read by `audit`, `map`, `export rules` and `export ndjson` for a log whose first line does not show its separator; it is also the only way for them to read a separator other than `,`, `;`, tab or `|`. `resolve` does not read it. After a change, `audit` resumes its log with the separator of the log and rewrites it with the new value. | none |
| `EnableDashboard` | boolean | `true` | Terminal dashboard | `--no-tui` |
| `MaskServerName` | string | `""` | Server name shown by the dashboard | `--mask-server` |
| `MaskLocalIp` | string | `""` | Local address shown by the dashboard | `--mask-ip` |

## Recording filters

These keys decide which sockets are recorded by `audit`. The dashboard adds entries to some of them when you exclude a cell ([dashboard reference](dashboard.md#excluding-from-the-dashboard)).

| Key | Type | Default | Effect | Option |
| :--- | :--- | :--- | :--- | :--- |
| `ExcludeLoopbackFlows` | boolean | `true` | Skip traffic that stays on the machine: sockets bound to a loopback address (listeners included), flows to a loopback address, and flows between two addresses of the machine. Listeners on other addresses are kept. | none |
| `ExcludedLocalPorts` | list of integers | `[]` | Local ports not recorded | none |
| `ExcludedRemotePorts` | list of integers | `[]` | Remote ports not recorded | none |
| `ExcludedRemoteIps` | list of addresses or CIDR | `[]` | Remote addresses not recorded | `--exclude-remote-ips` |
| `ExcludedPids` | list of integers | `[0, 4]` | Process identifiers not recorded | none |
| `ExcludedExactNames` | list of names | `["System", "Idle"]` | Process names not recorded (whole name, case ignored) | none |
| `ExcludedPrefixes` | list of strings | `[]` | Process names starting with one of these values (case ignored) | none |
| `ExcludedSuffixes` | list of strings | `[]` | Process names ending with one of these values (case ignored) | none |
| `ExcludedContains` | list of strings | `[]` | Process names containing one of these values | none |
| `ExcludeSystemAccounts` | boolean | `false` | Skip processes of system accounts (see the note below) | none |

`ExcludeSystemAccounts` skips processes whose account contains `NT AUTHORITY` or equals `SYSTEM` or `root`. Most services run under these accounts, hence the `false` default. A process whose account cannot be read (protected process, insufficient rights) has an empty account and is not skipped by this filter.

## Name resolution

| Key | Type | Default | Effect | Option |
| :--- | :--- | :--- | :--- | :--- |
| `ResolveRemoteHostNames` | boolean | `true` | Name resolution of remote addresses. `false` turns off reverse DNS, RDAP and aliases in `audit`, but only reverse DNS in `resolve`. `lookup` does not read it. | `--no-dns` |
| `DnsResolutionTimeoutMs` | integer | `3000` | Milliseconds to wait for a reverse DNS answer; used by `audit`, `resolve` and `lookup`; at least 1 | `--dns-timeout-ms` |
| `ResolveIpOwners` | boolean | `true` | RDAP query for the owner of public addresses, used as name when there is no DNS name; read by `audit` and `resolve`, not by `lookup` | `--no-rdap` |
| `RdapTimeoutMs` | integer | `4000` | Milliseconds to wait for an RDAP answer, redirect from `rdap.org` to the registry included; used by `audit`, `resolve` and `lookup`; at least 1 | `--rdap-timeout-ms` |
| `DnsCacheFilePath` | path | `portatlas_dns_cache.json` | Persistent DNS cache; default cache of `resolve` and `lookup` | `--cache`, `--output-cache` (`resolve`) |
| `DnsExcludedNetworks` | list of addresses or CIDR | `["0.0.0.0", "255.255.255.255", "169.254.0.0/16", "224.0.0.0/4", "fe80::/10", "ff00::/8"]` | Addresses never resolved by `audit`, `resolve` and `lookup` | none |
| `CustomHostAliases` | object `{ "ip": "name" }` | `{}` | Names used instead of reverse DNS by `audit`, `resolve` and `lookup`; never written to the DNS cache | `--alias` (`audit`, adds to the file) |

## Topology map and rule matrix

These keys are read by the map of `audit`, by `map`, and for the filters by `export rules`.

| Key | Type | Default | Option |
| :--- | :--- | :--- | :--- |
| `OutputHtmlMapPath` | path | `""` | `--html` |
| `OutputDrawioPath` | path | `""` | `--drawio` |
| `MapIncludedPorts` | list of ports or ranges | `[]` | `--include-ports` |
| `MapExcludedPorts` | list of ports or ranges | `[]` | `--exclude-ports` |
| `MapIncludedHosts` | list of patterns | `[]` | `--include-hosts` |
| `MapExcludedHosts` | list of patterns | `[]` | `--exclude-hosts` |
| `MapIncludedIps` | list of addresses or CIDR | `[]` | `--include-ips` |
| `MapExcludedIps` | list of addresses or CIDR | `[]` | `--exclude-ips` |
| `MapIncludedProcesses` | list of names | `[]` | `--include-processes` |
| `MapExcludedProcesses` | list of names | `[]` | `--exclude-processes` |
| `MapDirection` | `all`, `in`, `out` | `all` | `--direction` |
| `MapListenersOnly` | boolean | `false` | `--listeners-only` |
| `MapExcludeEphemeralPorts` | boolean | `true` | `--include-ephemeral-ports` (map only) |
| `MapLayoutMode` | see `--layout` | `layered` | `--layout` |
| `MapSortMethod` | see `--sort` | `barycentric` | `--sort` |
| `MapRoutingMode` | see `--routing` | `bezier` | `--routing` |
| `MapSpacingProfile` | see `--spacing` | `balanced` | `--spacing` |
| `MapEdgeLabels` | `ports`, `none` | `ports` | `--edge-labels` |
| `DrawioEdgeMode` | `adaptive`, `direction`, `pair`, `none` | `adaptive` | `--drawio-edges` |
| `DrawioSubnets` | `on`, `off` | `on` | `--drawio-subnets` |
| `DrawioTheme` | `dark`, `light` | `dark` | `--drawio-theme` |

An entry of `MapIncludedPorts` or `MapExcludedPorts` may hold several ports or ranges separated by `,` or `;` (`"80,443"`).

The topology map (not the rule matrix) also removes the ports of `ExcludedLocalPorts` and `ExcludedRemotePorts` and the addresses of `ExcludedRemoteIps`.

A value that `MapDirection`, `MapLayoutMode`, `MapSortMethod`, `MapRoutingMode`, `MapSpacingProfile`, `MapEdgeLabels`, `DrawioEdgeMode`, `DrawioSubnets` or `DrawioTheme` does not take (see the table above), compared without case, stops the command with an error naming the file, the key, the value and the accepted values, and exit code 1, as the matching option does: the default is not used in its place.

## Removed key

| Key | Removed in | Replacement |
| :--- | :--- | :--- |
| `IsolatedMode` | 2.0.0 | `"ResolveIpOwners": false`. A file with `"IsolatedMode": true` stops every command with an error; `false` prints a warning. |

> **Language / Langue**: **English** · [Français](upgrade-to-5.0.fr.md)

# Upgrade from 4.x to 5.0

PortAtlas 5.0 names the files it writes by default after the product, changes how `export rules` writes its matrices, and refuses inputs that 4.x skipped without a message. The command names and the configuration keys do not change. An install whose `portatlas.json` sets the file paths keeps its files; one that relied on the default names starts new files. Check each section below before deploying 5.0 to a service or a scheduled task.

## Before you upgrade

1. Stop the service or scheduled task that runs `audit`, so that it writes its logs and its DNS cache.
2. Copy the audit logs, the DNS cache and `portatlas.json` to a backup folder.
3. Replace the executable, then follow the sections below before starting the service again.

## Default file names start with `portatlas_`

**What changes.** A file whose path is not set is written, and read, under a new name:

| 4.x | 5.0 | Set by |
| --- | --- | --- |
| `netflow_audit.csv` | `portatlas_audit.csv` | `OutputCsvPath`, `audit -o` |
| `netflow_audit.ndjson` | `portatlas_audit.ndjson` | `export ndjson -o` |
| `netflow_topology.html` | `portatlas_topology.html` | `OutputHtmlMapPath`, `map --html` |
| `netflow_dns_cache.json` | `portatlas_dns_cache.json` | `DnsCacheFilePath`, `resolve --cache` and `--output-cache` |

`audit` does not look for the 4.x files: without a configured path, it starts a new audit log and an empty DNS cache. By default, `map`, `export` and `resolve` read the new log, and `resolve` and `lookup` the new cache. The `portatlas.json` shipped with 5.0 holds the new names. A `portatlas.json` copied from the 4.x template sets `OutputCsvPath` and `DnsCacheFilePath` to the 4.x names, so it keeps reading and writing the 4.x files.

**What to do.** For each server, choose one of the following before the first start:

- rename the files to their 5.0 names, in the folder where the audit runs;
- or keep the 4.x names by setting `OutputCsvPath`, `DnsCacheFilePath` and, for the map, `OutputHtmlMapPath` in `portatlas.json`.

Update the scripts that read these files by their default name.

**How to check.** Run `audit --no-tui`. The settings printed at startup name the audit log on the `CSV output` line and the DNS cache on the `Reverse DNS` line. If you renamed the files or kept the 4.x names, the log still holds the flows recorded by 4.x after the first rewrite.

## Firewall matrices

**What changes.**

- The rule matrix and the pair matrix of `export rules` are written in UTF-8 with a byte order mark, like the CSV audit log, so that a spreadsheet shows accented names.
- Each matrix replaces the previous file only once complete: an interrupted export leaves the previous matrix, where 4.x could leave it empty or truncated. An output path that is a folder is refused before anything is written.
- The `ExecutablePath` and `UserName` columns of a rule come from the most recently seen log row that has a value, like the host names. In 4.x they came from the first row read, so they could change with the order of the logs given to `--input`.

**What to do.** A program that reads a matrix as text must skip the byte order mark at the start of the file. Nothing else.

**How to check.** Open a matrix in a hexadecimal viewer: it starts with `EF BB BF`. Export the same logs twice, with `--input` in two orders: the two matrices are identical.

## Executable paths on Windows

**What changes.** `ExecutablePath` is read without opening the process. In 4.x it stayed empty for a service running under another account than the audit, and for protected processes, even with the audit run as administrator. It now stays empty only for the processes without an executable file: System Idle, System, Secure System and Registry. A path is also written without `..` segments, as Windows stores it.

**What to do.** Nothing to resume an audit. A flow recorded by 4.x without path is identified by its process name; the same flow, scanned by 5.0 with its path, is a new flow. The 4.x row stays in the log with its counters, and a new row starts. To start from clean counters, give `audit` a new output file.

**How to check.** Run `audit` as administrator on a server where a service runs under its own account, then open the log: the rows of that service carry its path.

## Map settings checked in the configuration file

**What changes.** An unknown value of `MapDirection`, `MapEdgeLabels`, `MapLayoutMode`, `MapSortMethod`, `MapRoutingMode`, `MapSpacingProfile`, `DrawioSubnets`, `DrawioEdgeMode` or `DrawioTheme` was replaced by the default without a message in 4.x. It now stops every command that reads the file, with exit code 1 and `<file>: "<key>" is "<value>", which the setting does not take. Accepted values: …`. Values are matched without case, and the spellings read by earlier versions are still accepted.

**What to do.** Correct the value named in the message, or remove the key to keep the default.

**How to check.** Run `map -i <log>` with the configuration file of each server: a value that is not accepted stops it with the message above.

## `--alias` pairs checked

**What changes.** A pair that `audit --alias` cannot read (`nas`, `=nas`, `192.168.1.5=`) was skipped without a message in 4.x. It is now a command-line error that names the pair, with exit code 1. A pair is split at its first `=`, so an IPv6 alias is written `fe80::10=nas.home.arpa`; the `ip:host` form is accepted for an IPv4 address only, with its four parts.

**What to do.** Check the `--alias` pairs of the services and scheduled tasks that run `audit`.

**How to check.** Start `audit` with the same arguments: it starts instead of printing `'<pair>' is not accepted by --alias: …`.

## `resolve` and `lookup` follow the name resolution keys

**What changes.** In 4.x, `resolve` ignored the name resolution keys of the configuration file. It now reads `ResolveRemoteHostNames`, `ResolveIpOwners`, `DnsExcludedNetworks` and `CustomHostAliases`: `"ResolveIpOwners": false` stops its RDAP queries, `"ResolveRemoteHostNames": false` stops its reverse DNS queries, the addresses of `DnsExcludedNetworks` and those with an alias receive no query, and an alias names its address. `lookup` now applies `CustomHostAliases` and `DnsExcludedNetworks` as the audit does, instead of `ExcludedRemoteIps`.

**What to do.** On the machine that runs `resolve` for [isolated servers](audit-isolated-servers.md), check its `portatlas.json`: a file copied from an isolated server, with `"ResolveIpOwners": false`, now prevents the RDAP queries you run `resolve` for.

**How to check.** Run `resolve` on a log that holds a public address: its RDAP owner appears in the enriched copy.

## See also

- [Command line reference](../reference/cli.md)
- [Configuration file reference](../reference/configuration.md)
- [Output formats](../reference/output-formats.md)

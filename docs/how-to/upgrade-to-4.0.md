> **Language / Langue**: **English** · [Français](upgrade-to-4.0.fr.md)

# Upgrade from 3.x to 4.0

PortAtlas 4.0 rewrites the audit log from the flows held in memory and stops a command on a file or a value it cannot read, where 3.x guessed or overwrote it. The command names and the configuration file name do not change, but some 3.x configuration files, command lines and audit logs are refused. Check each section below before deploying 4.0 to a service or a scheduled task.

## Before you upgrade

1. Stop the service or scheduled task that runs `audit`, so that it writes its logs and its DNS cache.
2. Copy the audit logs, the DNS cache and `portatlas.json` to a backup folder. The 4.0 audit rewrites the log in place at its first start.
3. Replace the executable, then run the checks of the sections below before starting the service again.

## The audit log is rewritten, one line per flow

**What changes.** In 3.x the audit appended new flows to the log and left the lines already written as they were. In 4.0 `audit` loads the whole existing log at start, then rewrites the CSV log, and the NDJSON log given by `--ndjson`, at start, every `--rewrite-interval` minutes (key `LogRewriteIntervalMinutes`, `5` by default) and at stop. Observation counts, last-seen dates and host names on disk are kept current. A line break inside a field is written as a space. A rewrite interrupted by a power cut is completed at the next start from `<log>.pending`.

**What to do.** Nothing for a log written by `audit`. A program that reads the log while the audit runs must accept that the file is replaced, not only appended to. A script that edited the log by hand must stop the audit first: the next rewrite replaces the file with the flows held in memory.

**How to check.** Start `audit`, wait for the first rewrite, and compare the `ObservationCount` and `LastSeenUtc` columns of a flow with the dashboard.

## `--reload-history` and `ReloadHistoryLimit` are removed

**What changes.** The whole log is always reloaded. `audit --reload-history` is an unknown option, and a configuration file that still holds `ReloadHistoryLimit` stops every command with exit code 1:

```text
portatlas.json: "ReloadHistoryLimit" is not a configuration key. Correct its name or remove it.
```

**What to do.** Remove `--reload-history` from service definitions, scheduled tasks and scripts, and remove the `ReloadHistoryLimit` key from `portatlas.json`.

**How to check.** Run a command that reads the configuration, for example `PortAtlas lookup 192.168.1.10 --config portatlas.json`. It must print the diagnostic and not the line above.

## An audit log is resumed only if it holds the flow key

**What changes.** `audit` stops with exit code 1 when the existing log lacks one of `FirstSeenUtc`, `Protocol`, `Direction`, `LocalPort`, `RemoteIp`, `RemotePort`, `ProcessName`, `ExecutablePath` and `TargetExecutable`, or holds a value it cannot read in one of these columns. `ProcessName` is new in this list: it identifies a flow whose target and executable path are both empty. `--columns` and `CsvColumns` without these columns are refused too. The message names the log and the missing columns or the line:

```text
The audit log 'D:\audits\homelab.csv' cannot be resumed: it has no ProcessName column, so the flows it holds would match none of the observed ones. Write to a new log with --output.
```

**What to do.** A log written with the default columns needs no change. If `--columns` or `CsvColumns` leaves out one of the columns above, add it. To keep a log that lacks them, start a new log with `--output` and keep the old one as an archive: `map` and `export` still read it.

**How to check.** Start `audit` with your usual options. It must open the dashboard, or print its first scan with `--no-tui`, without the message above.

## Flows launched by an interpreter get a new key

**What changes.** On Windows, the target of a script started by an interpreter is now read and filled in `TargetExecutable`. The flow key holds the target, so such a flow is a new flow at the first run of 4.0: it gets a new line, and its 3.x line, which has no target, stays in the log.

**What to do.** Nothing is required, and no conversion of the old lines is possible. If the duplicate lines matter, for example in a firewall rule matrix, start a new log with `--output` after the upgrade.

**How to check.** After the first scans, the lines of these flows have a `TargetExecutable` value; the 3.x lines of the same flows have none.

## A DNS cache that cannot be read is refused

**What changes.** `resolve` stops before any lookup when the cache named by `--output-cache` exists but cannot be read (truncated or invalid JSON, file held by another program or not readable by the account). `audit` does the same for the cache named by `DnsCacheFilePath`. Both end with exit code 1 and write nothing:

```text
The DNS cache 'D:\audits\dns_cache.json' cannot be read: …
```

In 3.x such a cache was read as empty, then replaced by the names of the current run. A cache listed in `--cache` that is only read is still skipped when it cannot be read.

**What to do.** Restore the cache from a backup, give the account read access, or delete the file to start from an empty cache.

**How to check.** Run `PortAtlas lookup 192.168.1.10` with the configuration of the audit: step 2 must not show `Cache access error`.

## The configuration file is checked

**What changes.** An unknown key, a `null` value or a `null` list entry stops every command with exit code 1 and names the key. Keys are case-sensitive, and a free key such as `Comment` is unknown too. A configuration file that cannot be read or is not valid JSON stops the command instead of falling back to the defaults with a warning. A `--config` path that is empty or does not exist stops the command. Without `--config`, a missing `portatlas.json` still gives the defaults.

**What to do.** Remove comment keys and keys left by older versions, correct the case of each key against the [configuration reference](../reference/configuration.md), and replace `null` with a value or remove the key. `PortAtlas config init` writes a file with every key and its default value, to compare with yours.

**How to check.** Run `PortAtlas lookup 192.168.1.10 --config portatlas.json`. A refused file prints its path and the key, then exits with code 1.

## Flow filters refuse what they cannot read

**What changes.** A port or an address of `--include-ports`, `--exclude-ports`, `--include-ips` or `--exclude-ips` (keys `MapIncludedPorts`, `MapExcludedPorts`, `MapIncludedIps`, `MapExcludedIps`) that cannot be read stops `audit`, `map` and `export rules` with exit code 1. The message names the option, the key and the value. In 3.x an address that could not be read was ignored, and a port ended `map` with a stack trace. In addition:

- An IPv4 address must have four decimal bytes: `192.168.1/24` and `010.0.0.1` are refused.
- A subnet with host bits set is read as its network: `192.168.1.10/24` is `192.168.1.0/24`.
- An IPv6 address in brackets (`[2001:db8::1]`, `[2001:db8::1]:443`) is refused. In the dashboard and DNS exclusions it is ignored.

**What to do.** Write each port as `443` or `8000-8080`, each IPv4 address with four bytes, and each IPv6 address without brackets.

**How to check.** Run `PortAtlas map -i <log> --html map.html --config portatlas.json`. It must write the map without a filter message.

## IPv6 rules no longer match IPv4 peers

**What changes.** `::/0` matches IPv6 addresses only. An IPv4-mapped rule with a prefix shorter than 96, such as `::ffff:0:0/80`, is an IPv6 rule. In 3.x both also matched IPv4 peers.

**What to do.** To filter every address, list `0.0.0.0/0` next to `::/0`. To filter IPv4 peers, write IPv4 subnets such as `192.168.1.0/24`.

**How to check.** Run `map` or `export rules` with the filter and look for the IPv4 peers you expect in the result.

## Audit logs are read as RFC 4180 CSV

**What changes.** A quoted field may span several lines, so a command line holding line breaks is one flow. A quote that is never closed stops `audit`, `export` and `resolve` with exit code 1 and a message naming the log and the line where the field starts. In 3.x each physical line was read as a flow.

**What to do.** Open the log in a text editor at the line given by the message, then close the quote or remove the line. A log cut while it was written, for example by a full disk, ends with such a field.

**How to check.** Run `PortAtlas export ndjson -i <log> --output check.ndjson`. It must end without a message about the log.

## Firewall rule identifiers are `RULE-001`, `RULE-002`…

**What changes.** `export rules` numbers its rules `RULE-001`, `RULE-002`… instead of `FLUX-001`. Rules are sorted on their whole group key, protocol and process included, so their numbers no longer depend on the order of the logs and may differ from 3.7.0 for the same logs.

**What to do.** Do not carry 3.x rule numbers over to a firewall request or a spreadsheet: export the matrix again with 4.0 and match rules on their content (protocol, addresses, port, process).

**How to check.** Export the same logs twice in a different order: the numbers are the same.

## Folders and patterns given to `--input` keep only audit logs

**What changes.** A folder or a pattern given to `--input` now provides only the files recognized as audit logs, one file per audit. `map`, `export rules` and `export ndjson` prefer the `_enriched` copy to its original, and a CSV log to the NDJSON log of the same name. `resolve` keeps both formats and skips an `_enriched` copy whose original is present. Skipped files are named on the console. A file given by its path is always read.

**What to do.** Nothing, unless a script counted on reading every copy in a folder. Counts may change for folders that held both a log and its copies: give the files by path to read them all.

**How to check.** Run the command on the folder and read the names of the skipped files on the console.

## Output formats

**What changes.**

- The NDJSON audit log is written in UTF-8 without a byte order mark. The CSV log keeps it.
- In the rule and pair matrices of `export rules`, a value starting with `=`, `+`, `-`, `@`, a tab or a carriage return is prefixed with an apostrophe, so that a spreadsheet does not run it as a formula.
- With `--listeners-only`, a `Listen` line whose direction is not `Listening` is no longer exported, and bound UDP sockets are kept.

**What to do.** A program that skipped a byte order mark at the start of the NDJSON log has nothing to change. A program that reads the matrices as text must remove the leading apostrophe of such values.

**How to check.** Open the NDJSON log in a hexadecimal viewer: it starts with `{`. Open the matrix in a spreadsheet: no cell is run as a formula.

## See also

- [Release notes of 4.0.0](../../release-notes/v4.0.0.md)
- [Command line reference](../reference/cli.md)
- [Configuration file reference](../reference/configuration.md)
- [Output formats](../reference/output-formats.md)

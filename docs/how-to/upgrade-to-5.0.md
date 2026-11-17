> **Language / Langue**: **English** · [Français](upgrade-to-5.0.fr.md)

# Upgrade from 4.x to 5.0

PortAtlas 5.0 names the files it writes by default after the product, and changes how `export rules` writes its matrices. The command names and the configuration keys do not change. An install whose `portatlas.json` sets the file paths keeps its files; one that relied on the default names starts new files. Check each section below before deploying 5.0 to a service or a scheduled task.

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

## See also

- [Command line reference](../reference/cli.md)
- [Configuration file reference](../reference/configuration.md)
- [Output formats](../reference/output-formats.md)

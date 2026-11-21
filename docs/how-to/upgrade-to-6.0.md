> **Language / Langue**: **English** · [Français](upgrade-to-6.0.fr.md)

# Upgrade from 5.x to 6.0

PortAtlas 6.0 changes what `export rules` writes: the default name of the rule matrix, the outputs it refuses, the counts and names of the pair matrix and the rules of loopback flows. It also stops reading a default audit log whose name changes with each audit start. The audit log, the DNS cache, the command names and the configuration keys do not change: `audit` reads the logs and the cache written by 5.x. Check the scripts that run `export rules`, and those that run `map`, `export` or `resolve` without `-i`, before deploying 6.0.

## Before you upgrade

1. List the scripts and scheduled tasks that run `export rules`, the programs that read its matrices, and the commands run without `-i`.
2. Replace the executable, then follow the sections below.

## The rule matrix is named `portatlas_rules.csv`

**What changes.** Without `-o`, `export rules` writes `portatlas_rules.csv` instead of `firewall_rules.csv`, like the other files PortAtlas names after the product.

**What to do.** In each script that runs `export rules` without `-o` and reads `firewall_rules.csv`, either read `portatlas_rules.csv`, or add `-o firewall_rules.csv`.

**How to check.** Run `export rules` without `-o`: the line `Firewall rule matrix with <n> rule(s) successfully generated: <path>` names `portatlas_rules.csv`.

## Outputs that name an audit log or the same file

**What changes.** The matrices are written once every log has been read. In 5.x, an output option that named one of the logs to read replaced that audit log by a matrix without a message. In 6.0, the command writes nothing and stops with exit code 1:

- when an output option names an audit log to read, given by its own path or found in a directory or a pattern: `The output '<path>' is also an audit log to read: choose another path for <option>.`
- when two output options name the same file: `<option> and <option> name the same file '<path>': choose two different paths.`

File names are compared without case.

**What to do.** Give each matrix its own path, outside the files read by `--input`. A script that writes the matrices in the folder it reads with `-i <folder>` keeps working: a matrix is not an audit log, so the folder does not offer it as an input. Each run then prints `Skipped <path>: its first line is not the header of an audit log.` for each matrix, followed by `A file whose own path is given to --input is always read.`: these lines are not errors.

**How to check.** Run the script once: it ends with exit code 0 and no `choose` message on the error output.

## Pair matrix counts and names

**What changes.** `--pairs-output` is built on the new flow matrix instead of the rule matrix:

- `FlowCount` counts flows. A flow leaves out the process, and merges the rules recorded by the logs of its two ends. A connection recorded by both ends, or a port used by several processes, gave several rule rows in 5.x and was counted several times; it is now counted once.
- `ObservationCount` counts each flow once, with the larger count of its two ends, instead of adding them.
- A `SeenFrom` column is added at the end of each row: the distinct `SeenFrom` values of the flows of the pair, separated by `, `, for example `Source, Both`.
- `SourceHost` and `DestinationHost` come from the flow matrix: the address of an audited server takes the name of that server. In 5.x they came from the first rule of the pair, so a name can change.
- A loopback pair is now one row per server, instead of one row for all servers.

**What to do.** A program that reads the pair matrix by column name has nothing to change, except to read `SeenFrom` as a list. A program that reads it by position must accept one more column at the end. A comparison with matrices written by 5.x must expect a lower `FlowCount` wherever both ends were audited or several processes share a port.

**How to check.** Open the pair matrix: the last column is `SeenFrom`.

## Loopback and unspecified destinations give one rule per server

**What changes.** A loopback flow, or a listener bound to `0.0.0.0` or `::`, found in the logs of two servers shared one rule in 5.x, named after the server with the most recent observation. It now gives one rule per server, since such an address names no machine. A routable destination still gives one rule whatever the log that saw it.

**What to do.** Expect more rows in the rule matrix and the listening socket matrix when several servers are read together. Their `RULE-` numbers shift accordingly: do not keep references to rule numbers from one export to the next.

**How to check.** Export the rules of two servers with `--listeners-only`: a service that both servers run on `0.0.0.0` has one row per server.

## A default audit log named by `{Date}` or `{Timestamp}` is no longer read

**What changes.** Without `-i`, `map`, `export rules`, `export ndjson` and `resolve` read the audit log named by `OutputCsvPath`. In 5.x, `{Date}` in that path named the log of the current UTC day, so a command run on another day found no log, and `{Timestamp}` named a log that no audit wrote. In 6.0, with either token in `OutputCsvPath`, these commands stop at once with exit code 1: `OutputCsvPath '<value>' names a new audit log at each audit start, so it names no log to read: give the logs with --input, for example --input "<pattern>".`, where `<pattern>` is the path with `*` in place of the tokens. `resolve` stops only when neither `-i` nor `--cache` is given.

**What to do.** In each script that relies on a `{Date}` path to read the log of the day, pass that log with `-i`, or pass the pattern given by the message to read every log.

**How to check.** Run the script once: it ends with exit code 0 and no `OutputCsvPath` message on the error output.

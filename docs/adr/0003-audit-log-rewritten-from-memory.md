# ADR-0003: Audit log rewritten whole from the flows held in memory

**Date**: 2026-11-14
**Status**: accepted
**Deciders**: curvecoder (owner)

## Context

`audit` deduplicates flows: a service used thousands of times is one flow with a first-seen date, a last-seen date,
an observation count and a resolved name. Until now the CSV log (and the NDJSON log) was appended to: a flow was
written once, when first seen. On disk `ObservationCount` was always 1, `LastSeenUtc` equalled `FirstSeenUtc`, and a
name resolved after the first sighting was missing. `export rules --active-within-days` dropped flows active every
day, `--min-observations` above 1 kept nothing, and counts did not continue across restarts, contrary to the
documented output format.

Keeping the log faithful to the observations requires writing a flow again when it changes. The log is read by other
programs while the audit runs (spreadsheets, `map`, `export`, backups), and the audit runs for weeks as a service,
where it can be stopped abruptly.

## Decision

The audit keeps every flow in memory (`ConnectionDeduplicationRegister`) and rewrites the whole log from it, one line
per flow (`AuditLogRewriter`):

- when the audit starts, after the existing log has been read whole;
- every `LogRewriteIntervalMinutes` minutes (`--rewrite-interval`, 5 by default);
- when the audit stops.

The NDJSON log is rewritten at the same times from the same flows.

Each rewrite goes through `AtomicFileWriter` (FileForge.Core 1.3.0): the new content is written to `<log>.tmp`,
flushed to disk, renamed `<log>.pending`, and only then replaces the log. A reader gets the earlier version or the
new one, never a mix.

- At start, `RecoverInterruptedWrite` applies a `<log>.pending` left by an interrupted rewrite and deletes a
  `<log>.tmp`; a log that cannot be read whole, or cannot be replaced, stops the audit with exit code 1 and a
  message naming the log and, when it applies, the line and the column.
- During the audit, a refused replacement (Windows, while another program holds the log open) is tolerated: the
  flows stay in memory, `<log>.pending` stays, the next period tries again.
- At stop, a failed rewrite ends the audit with exit code 1 and the message, since the observations since the last
  period are not on disk.

Line breaks inside a field of the CSV log are written as spaces, so the logs written by `audit` keep one line per
flow for programs that read them line by line (the NDJSON log escapes them, as JSON does).

The column list must keep the columns of the flow key and `FirstSeenUtc`: a log without them could not be read back,
so `audit` refuses such a list at start.

Since the rewrite writes back what was read, the reload limit (`--reload-history`, `ReloadHistoryLimit`) is
removed: a partial reload would drop the flows left out from the log. A configuration file that still holds the key
is refused like any unknown key.

## Alternatives Considered

### Alternative 1: Append a delta line at each change
- **Pros**: no rewrite; every observation is on disk at once.
- **Cons**: the log grows with every scan of every active flow; readers must aggregate the lines of a flow.
- **Why not**: the log is meant to hold one line per flow, read as it is by people and other programs.

### Alternative 2: Rewrite the log in place, without a temporary file
- **Pros**: simplest code.
- **Cons**: a stop during the write truncates the log and loses its whole history; a reader gets a mix of both
  versions.
- **Why not**: the audit runs unattended for weeks; losing the history on a power cut is not acceptable.

### Alternative 3: Rewrite at every scan
- **Pros**: nothing lost on an abrupt stop.
- **Cons**: rewriting thousands of lines every few seconds, and many more refused replacements while a reader holds
  the log.
- **Why not**: a period of a few minutes bounds the loss and keeps the disk activity low; the period is adjustable.

## Consequences

### Positive
- `ObservationCount`, `LastSeenUtc` and `RemoteHostName` on disk follow the observations, and continue across
  restarts; the filters of `export rules` work as documented.
- A reader never sees a half-written log, and an interrupted rewrite is recovered at the next start.

### Negative
- An abrupt stop loses at most the observations of the last period.
- The whole history is held in memory and rewritten at each period: memory and write time grow with the number of
  flows (lot 10 measures them).
- The NDJSON log holds the flows of the CSV log only: flows that were only in the NDJSON log are dropped at the
  first rewrite.
- A column left out of `--columns` since the previous run disappears from the whole log, not only from the new
  lines.
- Breaking change: `--reload-history` and `ReloadHistoryLimit` no longer exist.

### Risks
- A log held open for longer than a period is not updated during that time; the operator sees it only at the stop or
  in the `.pending` file.
- A log edited by hand into unreadable values stops the audit at the next start until the line is corrected.

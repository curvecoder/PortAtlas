> **Language / Langue**: **English** · [Français](dashboard.fr.md)

# Terminal dashboard reference

`PortAtlas audit` shows a full-screen dashboard in the terminal, unless `--no-tui` is given, `EnableDashboard` is `false`, or the input or output of the process is redirected.

Without the dashboard, the audit writes its messages as lines in the console. When the input is not redirected, `Q` still stops the audit and `M` still writes the maps; the other keys do nothing.

## Screen layout

| Area | Content |
| :--- | :--- |
| Header | `PortAtlas v<version>`: server name, OS, uptime; host IP, scan interval, clock; status, current view, number of sockets ignored by the filters |
| METRICS | Active sockets of the last scan, unique flows recorded, new flows of the last scan |
| NETWORK TRAFFIC BREAKDOWN | TCP sockets, followed by the listening and outbound sockets of both protocols; UDP sockets; inbound sockets |
| TOP ACTIVE PROCESSES (AUDITED), or ACTIVE PROCESSES (SNAPSHOT) in the Active Sockets view | The four processes with the most sockets, with a bar and their socket count; a process without a name counts as `Unknown` |
| Flow table | Title line, column headers, one row per flow |
| Footer | The keys available in the current state |

The traffic and process counts cover the sockets of the last scan. In the Audit Log view, the sockets excluded by the filters are left out of them.

A terminal smaller than 100 columns by 16 rows shows only `Window too small`, the current and minimum sizes, and `[Q] Quit`. The dashboard comes back when the window is enlarged.

## Views

| View | Content |
| :--- | :--- |
| Audit Log | The flows recorded by this audit, newest first, followed by the flows reloaded from the audit log at startup. Excluded flows are not shown. |
| Active Sockets | Every socket seen by the last scan, before the recording filters are applied |

Only a flow seen for the first time is added to the Audit Log view; a flow seen again keeps its place. The view keeps 100 rows in memory, or the number of reloaded flows left after the filters when it is larger. A new flow beyond that limit pushes the oldest row out of the view; the audit log on disk keeps it. `L` and `A` raise the limit, but a row already pushed out does not come back.

## Flow table

The title line names the view, `AUDIT LOG (DISCOVERIES)` or `ACTIVE SOCKETS (SNAPSHOT)`, followed by `[Page <n>/<total> | Rows <first>-<last> of <count> | Max: <limit>]`. `Max` shows `ALL` once the limit reaches 50,000 rows, and `LIVE ACTIVE` in the Active Sockets view. `ALL` is still a limit: 50,000 rows, or the number of reloaded flows when it is larger.

| Column | Content |
| :--- | :--- |
| `TIME` | Time the flow was first seen, in UTC (HH:mm:ss); in the Active Sockets view, time of the last scan. The header clock shows local time. |
| `PROTO` | `TCP` or `UDP` |
| `DIR` | `LIST` (listening), `IN` (inbound), `OUT` (outbound) |
| `LOCAL` | Local address and port |
| `REMOTE` | Remote host name and port, or remote address and port while the name is not resolved; always the address in the Active Sockets view |
| `PROCESS` | Name of the owning process |
| `TARGET / CMD` | Target executable, or the command line when there is none |

A page holds as many rows as the window height allows, at least 3.

### Messages in the title line

A message replaces the page information of the title line for at least the time below; it goes at the next redraw after that time (a scan, a key, a resolved name):

| Message | Shown for |
| :--- | :--- |
| `Scan failed (<n> of 10 in a row): …` | 15 s. `n` goes from 1 to 9: the 10th failed scan in a row stops the audit, which prints `The audit stopped after 10 failed scans in a row: …` and exits with code 1. |
| `Could not rewrite the audit log: …` | 15 s. The flows stay in memory, and the next rewrite retries. |
| `Topology map not written: …` (`M`, or the map rewritten after each scan when `OutputHtmlMapPath` is set) | 15 s |
| `No remote address to exclude …`, `No remote port to exclude …` | 15 s |
| Result of an exclusion | 4 s |
| `Reloaded <n> historical flows from CSV` | 5 s |
| View change, `L`, `A` | 3 s |

Warnings start with `⚠`, other messages with `✔`; the result of an exclusion that could not be saved also starts with `✔`. `M` shows no message when the map is written. Without the dashboard, warnings are written to the console as `[HH:mm:ss] <message>`.

## Keys

| Key | Action |
| :--- | :--- |
| `Q` | Stop the audit and exit |
| `C` | Redraw the screen |
| `V` or `F` | Switch between the two views |
| `M` | Write the HTML map (`OutputHtmlMapPath`, or `portatlas_topology.html`), and the Draw.io diagram when `OutputDrawioPath` is set |
| `↑` `↓` | Start the selection on the first row of the page, then move it; it stops at the first and last rows of the page |
| `←` `→` | Start the selection on the first row, LOCAL cell, then move it between cells; it goes from TARGET back to LOCAL |
| `N` or `Page Down` | Next page |
| `P` or `Page Up` | Previous page |
| `L` or `+` | Raise the row limit by 100, up to 50,000 |
| `A` | Raise the row limit to 50,000 |
| `Enter`, `X` or `E` | Exclude the value of the selected cell; nothing without a selection |
| `Esc` | Cancel the selection |

`Ctrl+C` and `SIGTERM` stop the audit like `Q`. Changing the view goes back to the first page; changing the page keeps the selection.

## Excluding from the dashboard

`←` and `→` move the selection through five stops: LOCAL, REMOTE, REMOTE PORT, PROCESS, TARGET. Excluding a cell applies the rule at once to the recording and to the display, removes the matching rows from the Audit Log view, and adds the value to the configuration file. Flows already recorded stay in the audit log and in the maps.

| Selected cell | Value excluded | Key updated |
| :--- | :--- | :--- |
| LOCAL | Local port | `ExcludedLocalPorts` |
| REMOTE | Remote address (an IPv4-mapped IPv6 address is written as IPv4) | `ExcludedRemoteIps` |
| REMOTE PORT | Remote port | `ExcludedRemotePorts` |
| PROCESS | Process name, and the name without its extension | `ExcludedExactNames` |
| TARGET | File name of the target executable, without its folder, and the name without its extension; the process name when the row has no target | `ExcludedExactNames` |

The REMOTE cell has two stops: `→` first selects the address, then its port, shown highlighted alone under the `[REMOTE PORT]` header. A remote address `0.0.0.0` or `::`, or a remote port 0, as on listening and UDP rows, which have no peer, cannot be excluded: the message says to select LOCAL or PROCESS instead.

The file updated is the one the audit read: the file given by `--config`, or `portatlas.json`, looked up in the working directory, then next to the executable. When the audit started without a configuration file, the first exclusion creates `portatlas.json` in the working directory.

Only the excluded values are added; the other keys and values are kept, and the file is written again with an indentation of two spaces. A value already listed, compared without case for addresses and names, is not added twice. When the file has no key for that list yet, the key is written with its built-in values followed by the new one, because a list in the file replaces the built-in list at the next start. A value the audit already excludes (from the file, the built-in values or the command line) causes no save. Each save writes every exclusion added during the run: an exclusion removed from the file by hand while the audit runs comes back at the next save.

The new version is first written beside the file, under its name followed by `.tmp` and then `.pending`, and only then replaces it: a program reading the file gets the earlier version or the new one, never a mix of both, and an interruption leaves the earlier file intact. Windows refuses the replacement while another program holds the file open; the save then counts as not saved, and the `.pending` file stays until the next save replaces it. If the file itself is missing while a `.pending` file is present, the next save starts from the `.pending` version.

When the file cannot be written (in use, read-only, access denied), the message ends with `not saved to <file>: the file is in use or cannot be written`. When the file is not a JSON object, holds a key twice, or holds the list under another type, it ends with `not saved to <file>: the file is no longer a valid configuration`. The file is left as it is in both cases, and the cause stays in the messages of the next exclusions until a save succeeds. The exclusion still applies until the audit stops, and the next exclusion that can be saved writes the earlier ones too.

## Masking for screenshots

These options change only what the dashboard displays; the audit log, the maps and the console header of an audit without dashboard keep the real values.

| Option | Configuration key | Effect |
| :--- | :--- | :--- |
| `--mask-server <name>` | `MaskServerName` | Server name shown in the header |
| `--mask-ip <address>` | `MaskLocalIp` | Host IP shown in the header, and the local address of a row when it is the primary address or `0.0.0.0` |
| `--obfuscate` | none | Shows `HOMELAB-01` and `192.168.1.10`, in place of the configuration keys too, unless `--mask-server` or `--mask-ip` gives another value |

The header cuts the server name and the host IP to 10 characters (`192.168.1…`). The other local addresses, the remote addresses and host names, the process names and the command lines are shown as they are.

> **Language / Langue**: **English** · [Français](use-the-graphical-interface.fr.md)

# Use the graphical interface

The graphical interface fills in the options of a command from a form, shows the command line it builds, and runs it with the `PortAtlas` executable. It offers `map`, `export rules`, `export ndjson`, `resolve`, `lookup` and `config init`; the `audit` command is run from a terminal (see [Getting started](../getting-started.md)). What each option does is in the [CLI reference](../reference/cli.md); the form shows the same description under each option.

## Install

On Windows, download `PortAtlas-<version>-win-x64.zip` and `PortAtlas.Gui-<version>-win-x64.zip` from the [releases page](https://github.com/curvecoder/PortAtlas/releases) and extract both into the same folder, for example `C:\Tools\PortAtlas`. Both archives hold `README.md`, `README.fr.md`, `LICENSE` and `portatlas.json`: when the second one asks, replace them, they are the same files. Start `PortAtlas.Gui.exe`.

The Linux releases do not include the interface; the [README](../../README.md#install) gives the command that builds it. Put the built `PortAtlas.Gui` next to the `PortAtlas` executable.

The interface runs the first of these files that exists:

1. the executable chosen with **Change...** or **Select PortAtlas...**;
2. `PortAtlas.exe` (Linux: `PortAtlas`) in the folder of the interface;
3. the file named by the `PORTATLAS_EXE` environment variable.

When none exists, a banner at the top of the window reads `PortAtlas.exe was not found next to the GUI or at the configured path.` (Linux: `PortAtlas was not found …`) with a **Select PortAtlas...** button. The rest of the window still works, but **Run** fails with a message that names every location searched. The **Executable** line, above the command line, shows the file in use; **Change...** picks another one, and **Automatic**, shown while a file is chosen, returns to the search above.

## The window

| Area | Content |
| :--- | :--- |
| Top bar | **Profile** list with **Load**, profile name box with **Save**, theme button. Errors about profiles and preferences are shown in this bar, next to the title. |
| **Commands** (left) | The commands the interface can run. Values typed in a form are kept when you switch to another command and back. |
| Form (centre) | The description of the command, its arguments, then its options in sections. |
| **Executable** | The executable in use, **Change...**, **Automatic**. |
| **Command line** | The command line built from the form, starting with the command name (without `PortAtlas`), updated after every change, and **Copy**, which puts it on the clipboard. |
| Run bar | **Run**, **Cancel**, the run state (`Idle`, `Running`, `Completed`, `Cancelled`, `Failed`), `Exit code: <n>` when the command has ended by itself, and a message when the run was cancelled or the executable could not be started. |
| Output (bottom) | The lines written by the command; the lines of the error output are shown in red. A new run clears them. |

## Fill in a form

The options are grouped in sections, shown in this order and only when the command has options in them: **Inputs**, **Filters**, **Map rendering**, **Outputs**, **Resolution**, then **Other options**, which holds the options of no other section, such as `--config`. Each section folds and unfolds from its header; **Other options** starts folded.

| Field | Use |
| :--- | :--- |
| Check box | An option without value, such as `--listeners-only`. |
| Text box | A value. For a path, **Browse...** opens the file dialog: an open dialog for a file that is read, a save dialog for a file that is written. |
| Text box for an integer | An integer, such as `--min-observations`. A value that is not an integer or is out of range is refused by `PortAtlas` when it runs, with its usual message and exit code. |
| List | One of the values `PortAtlas` accepts, each with its description. **Clear** goes back to no value. |
| Text box with **Add** | An option that takes several values, such as `--input`. **Add** moves the typed value to the list below; **Remove** takes a value out. For `--input` and `--cache`, **File...** and **Folder...** add a file or a folder. A value left in the box without pressing **Add** still counts. |

An option left empty adds nothing to the command line, so `PortAtlas` applies its default value or the value of the configuration file. `--config` has one value for every command: typing it in one form sets it in the others.

A relative path is relative to the working directory of the interface: its own folder when it is started from the file explorer. **Browse...**, **File...** and **Folder...** give full paths.

## Run a command

**Run** starts the command shown under **Command line**. The interface passes each value as a separate argument, without a shell, so spaces and quotes in a value need no escaping. The command behaves and ends as in a terminal, with the same messages and [exit codes](../reference/cli.md); a run state of `Completed` only means that the command has ended, and `Exit code: 0` that it succeeded.

**Cancel** stops the command as `Ctrl+C` does in a terminal: `resolve` saves the addresses already resolved before it exits. A command still running 10 seconds later is ended by force. Closing the window during a run cancels the run the same way, and the window closes once the command has stopped. A cancelled run shows `Cancelled` and no exit code.

None of these commands reads the socket tables, so none needs administrator rights, except to write into a folder the account cannot write to: they read audit logs and caches, write files and, for `resolve` and `lookup`, send DNS and RDAP queries.

## Example: draw a map from an audit log

1. In **Commands**, select `map`.
2. In **Inputs**, on the `--input` line, click **File...** and choose `portatlas_audit.csv`.
3. In **Outputs**, on the `--html` line, click **Browse...** and give `homelab.html`.
4. Check the command line, for example `map --input C:\Tools\PortAtlas\portatlas_audit.csv --html C:\Tools\PortAtlas\homelab.html`.
5. Click **Run**. The output shows `Topology map generated: …`, and the run bar `Completed` and `Exit code: 0`.
6. Open `homelab.html` in a browser.

## Save and load profiles

A profile keeps the values of every form, and the executable chosen with **Change...** when there is one, so that a set of commands can be run again later.

- **Save** writes the profile under the name typed in the **Profile name** box, or updates the selected profile when the box is empty. Replacing an existing profile asks for confirmation. A name cannot be blank, start or end with a space, or hold a character that a file name cannot hold on that system (on Windows: `\ / : * ? " < > |`).
- **Load** opens the profile selected in the list, or the one whose name is typed when none is selected. A profile without a chosen executable returns to the automatic search.
- The last profile saved or loaded opens again at the next start.

Profiles are JSON files in `Data\profiles` (Linux: `Data/profiles`), in the folder of the interface. When that folder cannot be created, as under `C:\Program Files`, they are kept in the folder of the user: `%APPDATA%\PortAtlas\profiles` on Windows, `$XDG_CONFIG_HOME/portatlas/profiles` (or `~/.config/portatlas/profiles`) on Linux. A profile that cannot be read, saved or loaded shows a one-line message in the top bar; the form keeps its values.

## Change the theme

The theme button shows `Theme: System`, `Theme: Light` or `Theme: Dark`; each click moves to the next one. The theme and the last profile are kept in `preferences.json`, one level above the profile folder (`Data`, or `%APPDATA%\PortAtlas`, `$XDG_CONFIG_HOME/portatlas`, `~/.config/portatlas`). A missing or damaged `preferences.json` gives the defaults: `System`, and no profile loaded.

For how the interface is built and why it has no option descriptions of its own, see [How the graphical interface is built](../explanation/gui-architecture.md).

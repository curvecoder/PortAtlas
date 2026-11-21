> **Language / Langue**: **English** · [Français](gui-architecture.fr.md)

# How the graphical interface is built

The graphical interface lets you fill in a form instead of typing the options of a command. This page explains how it is organized and why it has no description of the options of its own. The decision is recorded in [ADR-0002](../adr/0002-avalonia-gui-generated-from-command-tree.md). The options themselves are in the [CLI reference](../reference/cli.md). To install and use the interface, see [Use the graphical interface](../how-to/use-the-graphical-interface.md).

## One source for the options

The options of every command are defined once, in the command tree that the CLI builds for parsing and for the help text. The interface reads that tree and generates one form per command, so a new option appears in the form without any interface code. Only two kinds of information are missing from the tree, and a table keyed by option name supplies them: the section of the form an option belongs to, and whether a path option designates a file to read, a file to write or a directory. A test fails when an option has no entry in that table, or when an entry names an option that no longer exists.

The closed sets of values (for example the layouts of the map) are exposed by the CLI itself as completion values of the option, so the form lists exactly what the CLI accepts. Minimum values are not copied into the form: the CLI validates the command line, and the form shows its message and exit code unchanged.

## Commands in the form

The form offers the commands that run once and stop: `map`, `export rules`, `export ndjson`, `resolve`, `lookup` and `config init`. The `audit` command runs until it is stopped and is not part of the first version of the interface.

## View models and abstractions

Each command descriptor is turned into a `CommandFormViewModel` that groups its option view models (`FlagOptionViewModel`, `TextOptionViewModel`, `IntegerOptionViewModel`, `ChoiceOptionViewModel`, `TextListOptionViewModel`) in the order of `OptionGroup`, omitting empty groups, and updates the command line preview after every change. `MainViewModel` keeps one form instance per command so values typed for a command remain when switching to another command and back. `RunViewModel` manages the execution state (`Idle`, `Running`, `Completed`, `Cancelled`, `Failed`), streams output lines through `IUiDispatcher` and disables the run command while a run is in progress. Closing the main window during a run first stops it (`RunViewModel.StopAsync`) and closes the window once the run has ended, so no CLI process outlives the GUI. To stop a run, from the **Cancel** button or by closing the window, the runner closes the standard input of the CLI, which [stops the command as `Ctrl+C` does](../reference/cli.md#stopping-through-the-standard-input): `resolve` saves the addresses already resolved. The runner kills the CLI process tree only when it is still running 10 seconds later. `ProfilesViewModel` lists, saves and loads named profiles.

The view models live in `PortAtlas.Gui.Core` and reach the operating system and the windowing toolkit only through abstractions: `IPortAtlasRunner` and `IPortAtlasExecutableLocator` for process execution, `IFilePickerService` for file and folder dialogs, `IUiDispatcher` for marshalling output lines to the user interface thread, `IConfirmationService` for yes / no questions, and `IProfileDirectoryProvider` for locating the profile directory.

## Running a command

The form state, which holds the values typed for one command, is turned into an argument list: the command path, then the options that have a value, then the positional arguments. An option left empty adds nothing, so the CLI applies its default. A repeatable option is written once per value. A preview shows the command line with quotes for display and copy, but the program never runs that text.

The command runs as a child process of the `PortAtlas` executable, found as described in [Use the graphical interface](../how-to/use-the-graphical-interface.md#install). The process receives the argument array directly, without a shell, so no value can be interpreted as shell syntax. Standard output and standard error are read line by line while the command runs. Cancelling a run closes the standard input first, as described above, and kills the process tree only if it is still running 10 seconds later.

Running the executable gives the same behavior as the CLI, including its exit codes, and a failure of a command cannot bring the interface down.

## The PortAtlas executable

The archive of the interface does not contain the `PortAtlas` executable; [Use the graphical interface](../how-to/use-the-graphical-interface.md#install) explains how to install both and the order in which the interface looks for the executable. The line next to **Executable** shows the path found at start-up or after the last change of path (**Change...**, **Automatic**, loading a profile), but each run searches again, and a chosen path that no longer holds a file is skipped without a message. The chosen path is stored in the profile, so that different profiles can run different versions of PortAtlas.

## Profiles

A profile is a named set of form states, one per command, stored as a JSON file in `Data\profiles`, next to the executable. When that folder cannot be created (an installation under Program Files), profiles stay in the per-user folder of earlier versions: `%APPDATA%\PortAtlas\profiles` on Windows, `$XDG_CONFIG_HOME/portatlas/profiles` (or `~/.config/portatlas/profiles`) on Linux. The first time `Data\profiles` is created, the profiles and `preferences.json` of the per-user folder are copied into it and left in place. The copy only becomes `Data\profiles` once complete: if it stops half-way (a profile held open by another program), the application uses the per-user folder for that run and copies again at the next start. A file is written to a temporary name and then renamed over the previous one, so an interruption leaves the earlier profile intact. A profile file that cannot be read raises an error naming the file.

Overwriting an existing profile always asks for confirmation through `IConfirmationService`, which keeps the question out of the view model and lets the tests answer it. The colour theme and the name of the last profile used are stored in `preferences.json`, one level above the profile directory in use, so they follow the profiles when these stay in the per-user folder; a missing or damaged preferences file falls back to the defaults.

Options declared once on the root command, such as `--config`, share a single value across all commands.

## Layers

| Project | Role |
| :--- | :--- |
| `PortAtlas.Gui.Core` | Command catalog, option presentation, form state, argument list, runner, profiles, view models and UI abstractions. It has no dependency on the user interface library. |
| `PortAtlas.Gui` | Avalonia views, file picker adapter, UI thread dispatcher and profile directory provider. |
| `PortAtlas.Gui.Tests` | Unit, headless UI and architecture tests. |

The core reaches the process, the file dialogs and the user interface thread only through abstractions, so it is tested without a window.

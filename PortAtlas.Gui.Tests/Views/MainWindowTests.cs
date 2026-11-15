using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using AwesomeAssertions;
using PortAtlas.Gui.Core.Catalog;
using PortAtlas.Gui.Core.Execution;
using PortAtlas.Gui.Core.Interaction;
using PortAtlas.Gui.Core.Profiles;
using PortAtlas.Gui.Core.ViewModels;
using PortAtlas.Gui.Tests.Views;
using PortAtlas.Gui.Views;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace PortAtlas.Gui.Tests.Views;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public sealed class MainWindowTests : IDisposable
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    private readonly string _root = Directory.CreateTempSubdirectory("portatlas-window-").FullName;
    private string _directory => Path.Combine(_root, "profiles");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [AvaloniaFact]
    public void MainWindow_WhenShown_ListsEveryCatalogCommand()
    {
        // Arrange
        var catalog = CommandCatalog.Build();
        var vm = new MainViewModel(
            catalog,
            new NoOpRunner(),
            new NullFilePicker(),
            new ImmediateDispatcher(),
            new ProfileStore(new FixedDirectory(_directory)),
            new AlwaysConfirm(),
            new UserPreferencesStore(new FixedDirectory(_directory)),
            new ExecutableViewModel(new PortAtlasExecutableLocator(_root, _ => null), new NullFilePicker()));

        var window = new MainWindow { DataContext = vm };

        // Act
        window.Show();
        var listBox = window.FindControl<ListBox>("CommandsList");
        var listedKeys = listBox?.Items.OfType<CommandDescriptor>().Select(c => c.Key).ToArray() ?? [];
        window.Close();

        // Assert
        listedKeys.Should().Equal(catalog.Commands.Select(c => c.Key));
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenConfigInitIsRunEndToEnd_CreatesFileAndCompletesWithZeroExitCode()
    {
        // Arrange
        var configPath = Path.Combine(_directory, "homelab-portatlas.json");
        var locator = new PortAtlasExecutableLocator(AppContext.BaseDirectory, Environment.GetEnvironmentVariable);
        var runner = new PortAtlasProcessRunner(locator);
        var vm = new MainViewModel(
            CommandCatalog.Build(),
            runner,
            new NullFilePicker(),
            new ImmediateDispatcher(),
            new ProfileStore(new FixedDirectory(_directory)),
            new AlwaysConfirm(),
            new UserPreferencesStore(new FixedDirectory(_directory)),
            new ExecutableViewModel(locator, new NullFilePicker()));

        var window = new MainWindow { DataContext = vm };

        // Act
        window.Show();
        vm.SelectedCommand = vm.Commands.Single(c => c.Key == "config init");
        vm.CurrentForm.Arguments.Single(a => a.Name == "path").Value = configPath;
        await vm.Run.RunCommand.ExecuteAsync(null);
        window.Close();

        // Assert
        vm.Run.State.Should().Be(RunState.Completed);
        vm.Run.ExitCode.Should().Be(0);
        File.Exists(configPath).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task Close_WhenCommandIsRunning_CancelsTheRunBeforeClosing()
    {
        // Arrange
        var runner = new BlockingRunner();
        var vm = new MainViewModel(
            CommandCatalog.Build(),
            runner,
            new NullFilePicker(),
            new ImmediateDispatcher(),
            new ProfileStore(new FixedDirectory(_directory)),
            new AlwaysConfirm(),
            new UserPreferencesStore(new FixedDirectory(_directory)),
            new ExecutableViewModel(new PortAtlasExecutableLocator(_root, _ => null), new NullFilePicker()));
        var window = new MainWindow { DataContext = vm };
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        var runTask = vm.Run.RunCommand.ExecuteAsync(null);
        var runStarted = await Task.WhenAny(runner.Started, Task.Delay(WaitLimit)) == runner.Started;

        // Act
        window.Close();
        var openWhileStopping = window.IsVisible;
        var runFinished = await Task.WhenAny(runTask, Task.Delay(WaitLimit)) == runTask;
        var windowClosed = await Task.WhenAny(closed.Task, Task.Delay(WaitLimit)) == closed.Task;

        // Assert
        runStarted.Should().BeTrue();
        openWhileStopping.Should().BeTrue("the window waits for the command to stop");
        runFinished.Should().BeTrue("closing the window must stop the running command");
        runner.CancellationObserved.Should().BeTrue();
        vm.Run.State.Should().Be(RunState.Cancelled);
        windowClosed.Should().BeTrue("the window closes once the command has stopped");
    }

    [AvaloniaFact]
    public async Task Close_WhenLastRunFailedUnexpectedly_ClosesTheWindow()
    {
        // Arrange
        var vm = new MainViewModel(
            CommandCatalog.Build(),
            new FailingRunner(),
            new NullFilePicker(),
            new ImmediateDispatcher(),
            new ProfileStore(new FixedDirectory(_directory)),
            new AlwaysConfirm(),
            new UserPreferencesStore(new FixedDirectory(_directory)),
            new ExecutableViewModel(new PortAtlasExecutableLocator(_root, _ => null), new NullFilePicker()));
        var window = new MainWindow { DataContext = vm };
        var closed = new TaskCompletionSource();
        window.Closed += (_, _) => closed.TrySetResult();
        window.Show();
        var run = () => vm.Run.RunCommand.ExecuteAsync(null);
        await run.Should().ThrowAsync<InvalidOperationException>();

        // Act
        window.Close();
        var windowClosed = await Task.WhenAny(closed.Task, Task.Delay(WaitLimit)) == closed.Task;

        // Assert
        windowClosed.Should().BeTrue();
    }

    private sealed class FailingRunner : IPortAtlasRunner
    {
        public Task<RunResult> RunAsync(
            IReadOnlyList<string> arguments,
            IProgress<RunOutputLine> output,
            CancellationToken cancellationToken = default) =>
            Task.FromException<RunResult>(new InvalidOperationException("Process stream closed."));
    }

    private sealed class BlockingRunner : IPortAtlasRunner
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public bool CancellationObserved { get; private set; }

        public async Task<RunResult> RunAsync(
            IReadOnlyList<string> arguments,
            IProgress<RunOutputLine> output,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }

            return new RunResult(0);
        }
    }

    private sealed class AlwaysConfirm : IConfirmationService
    {
        public Task<bool> ConfirmAsync(string message, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class NoOpRunner : IPortAtlasRunner
    {
        public Task<RunResult> RunAsync(
            IReadOnlyList<string> arguments,
            IProgress<RunOutputLine> output,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RunResult(0));
    }

    private sealed class NullFilePicker : IFilePickerService
    {
        public Task<string?> PickAsync(PathKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    private sealed class FixedDirectory(string path) : IProfileDirectoryProvider
    {
        public string GetProfileDirectory() => path;
    }
}

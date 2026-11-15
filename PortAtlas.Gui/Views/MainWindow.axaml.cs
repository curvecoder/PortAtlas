using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using PortAtlas.Gui.Core.Profiles;
using PortAtlas.Gui.Core.ViewModels;

namespace PortAtlas.Gui.Views;

/// <summary>
/// Main window of the PortAtlas GUI application.
/// </summary>
public sealed partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private Task? _stopBeforeClose;

    /// <summary>
    /// Initializes the window.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Once the stop has finished, the Close() below must go through even if the run state was left at Running
        // (a run that failed with an unexpected exception): checking IsRunning again would cancel the close forever.
        if (_stopBeforeClose is { IsCompleted: true })
        {
            return;
        }

        // A second close request while the run is stopping waits for the pending one, which closes the window.
        if (_stopBeforeClose is not null)
        {
            e.Cancel = true;
            return;
        }

        if (_viewModel is null || !_viewModel.Run.IsRunning)
        {
            return;
        }

        // The CLI child process has no window of its own and would outlive the GUI: the window stays open until the run has stopped.
        e.Cancel = true;
        _stopBeforeClose = _viewModel.Run.StopAsync();
        try
        {
            await _stopBeforeClose.ConfigureAwait(true);
        }
        finally
        {
            Close();
        }
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || Clipboard is null)
        {
            return;
        }

        await Clipboard.SetTextAsync(_viewModel.CurrentForm.Preview).ConfigureAwait(true);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyTheme(_viewModel.Theme);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Theme) && _viewModel is not null)
        {
            ApplyTheme(_viewModel.Theme);
        }
    }

    private static void ApplyTheme(ThemeChoice theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = theme switch
        {
            ThemeChoice.Light => ThemeVariant.Light,
            ThemeChoice.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}

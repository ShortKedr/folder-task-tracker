using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TaskTracker.App.ViewModels;
using TaskTracker.Core.Storage;

namespace TaskTracker.App.Views;

public sealed partial class MainWindow : Window
{
    private const double WheelScrollStep = 96;
    private const double ScrollEasing = 0.28;

    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _smoothScrollTimer;
    private ScrollViewer? _tasksScrollViewer;
    private double _targetTaskScrollY;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(new GroupFileStore(), new UserSettingsStore())
        {
            PickFolderAsync = PickFolderAsync,
            RequestTextAsync = RequestTextAsync,
            RequestTaskEditAsync = RequestTaskEditAsync,
            ShowErrorAsync = ShowErrorAsync
        };

        DataContext = _viewModel;
        KeyDown += OnKeyDown;
        TasksListBox.AddHandler(PointerWheelChangedEvent, OnTasksPointerWheelChanged, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _smoothScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _smoothScrollTimer.Tick += SmoothScrollTick;
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        await _viewModel.InitializeAsync();
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose task data folder",
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private async Task<string?> RequestTextAsync(string title, string? initialValue)
    {
        var dialog = new TextInputWindow(title, initialValue);
        return await ShowOwnedDialog<string?>(dialog);
    }

    private async Task<TaskEditResult?> RequestTaskEditAsync(string title, TaskTracker.Core.Models.TaskItem? initialValue)
    {
        var dialog = new TaskEditorWindow(title, initialValue);
        return await ShowOwnedDialog<TaskEditResult?>(dialog);
    }

    private async void EditTaskClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: TaskItemViewModel task })
        {
            await _viewModel.EditTaskAsync(task);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
            _viewModel.RedoTaskCommand.CanExecute(null))
        {
            _viewModel.RedoTaskCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            _viewModel.UndoTaskCommand.CanExecute(null))
        {
            _viewModel.UndoTaskCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnTasksPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var scrollViewer = GetTasksScrollViewer();
        if (scrollViewer is null)
        {
            return;
        }

        var maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        _targetTaskScrollY = Math.Clamp(_targetTaskScrollY - e.Delta.Y * WheelScrollStep, 0, maxY);

        if (!_smoothScrollTimer.IsEnabled)
        {
            _smoothScrollTimer.Start();
        }

        e.Handled = true;
    }

    private void SmoothScrollTick(object? sender, EventArgs e)
    {
        var scrollViewer = GetTasksScrollViewer();
        if (scrollViewer is null)
        {
            _smoothScrollTimer.Stop();
            return;
        }

        var currentY = scrollViewer.Offset.Y;
        var nextY = currentY + (_targetTaskScrollY - currentY) * ScrollEasing;

        if (Math.Abs(_targetTaskScrollY - nextY) < 0.5)
        {
            nextY = _targetTaskScrollY;
            _smoothScrollTimer.Stop();
        }

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, nextY);
    }

    private ScrollViewer? GetTasksScrollViewer()
    {
        _tasksScrollViewer ??= TasksListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_tasksScrollViewer is not null && !_smoothScrollTimer.IsEnabled)
        {
            _targetTaskScrollY = _tasksScrollViewer.Offset.Y;
        }

        return _tasksScrollViewer;
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new MessageWindow("Folder Task Tracker", message);
        await ShowOwnedDialog<object?>(dialog);
    }

    private Task<TResult?> ShowOwnedDialog<TResult>(Window dialog)
    {
        dialog.Topmost = Topmost;
        dialog.ShowInTaskbar = false;
        return dialog.ShowDialog<TResult?>(this);
    }
}

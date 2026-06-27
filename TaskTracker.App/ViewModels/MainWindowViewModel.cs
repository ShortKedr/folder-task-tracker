using System.Diagnostics;
using System.Collections.ObjectModel;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;

namespace TaskTracker.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const int HistoryLimit = 50;

    private readonly GroupFileStore _store;
    private readonly UserSettingsStore _settings;
    private readonly List<TaskHistoryEntry> _undoHistory = new();
    private readonly List<TaskHistoryEntry> _redoHistory = new();
    private TaskGroupViewModel? _selectedGroup;
    private TaskItemViewModel? _selectedTask;
    private string? _folderPath;
    private string? _statusMessage;
    private bool _isAlwaysOnTop;
    private bool _isStatusBarVisible = true;

    public MainWindowViewModel(GroupFileStore store, UserSettingsStore settings)
    {
        _store = store;
        _settings = settings;

        CreateGroupCommand = new RelayCommand(async () => await CreateGroupAsync());
        RenameGroupCommand = new RelayCommand(async () => await RenameGroupAsync(), () => SelectedGroup is not null);
        DeleteGroupCommand = new RelayCommand(DeleteGroup, () => SelectedGroup is not null);
        AddTaskCommand = new RelayCommand(async () => await AddTaskAsync(), () => SelectedGroup is not null);
        DeleteTaskCommand = new RelayCommand(DeleteTask, () => SelectedGroup is not null && SelectedTask is not null);
        OpenFolderCommand = new RelayCommand(async () => await OpenFolderFromPickerAsync());
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder, () => !string.IsNullOrWhiteSpace(FolderPath));
        ToggleStatusBarCommand = new RelayCommand(() => IsStatusBarVisible = !IsStatusBarVisible);
        UndoTaskCommand = new RelayCommand(UndoLastTaskChange, () => _undoHistory.Count > 0);
        RedoTaskCommand = new RelayCommand(RedoLastTaskChange, () => _redoHistory.Count > 0);
    }

    public ObservableCollection<TaskGroupViewModel> Groups { get; } = new();

    public Func<Task<string?>>? PickFolderAsync { get; set; }
    public Func<string, string?, Task<string?>>? RequestTextAsync { get; set; }
    public Func<string, TaskItem?, Task<TaskEditResult?>>? RequestTaskEditAsync { get; set; }
    public Func<string, Task>? ShowErrorAsync { get; set; }

    public RelayCommand CreateGroupCommand { get; }
    public RelayCommand RenameGroupCommand { get; }
    public RelayCommand DeleteGroupCommand { get; }
    public RelayCommand AddTaskCommand { get; }
    public RelayCommand DeleteTaskCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand ToggleStatusBarCommand { get; }
    public RelayCommand UndoTaskCommand { get; }
    public RelayCommand RedoTaskCommand { get; }

    public TaskGroupViewModel? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                SelectedTask = value?.Tasks.FirstOrDefault();
                RaiseCommandStates();
            }
        }
    }

    public TaskItemViewModel? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (SetProperty(ref _selectedTask, value))
            {
                OnPropertyChanged(nameof(HasSelectedTask));
                RaiseCommandStates();
            }
        }
    }

    public string? FolderPath
    {
        get => _folderPath;
        private set
        {
            if (SetProperty(ref _folderPath, value))
            {
                OpenDataFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsAlwaysOnTop
    {
        get => _isAlwaysOnTop;
        set => SetProperty(ref _isAlwaysOnTop, value);
    }

    public bool IsStatusBarVisible
    {
        get => _isStatusBarVisible;
        set
        {
            if (SetProperty(ref _isStatusBarVisible, value))
            {
                OnPropertyChanged(nameof(StatusBarMenuText));
            }
        }
    }

    public string StatusBarMenuText => IsStatusBarVisible ? "Hide Status Bar" : "Show Status Bar";

    public bool HasSelectedTask => SelectedTask is not null;

    public async Task InitializeAsync()
    {
        var lastFolder = _settings.LoadLastFolder();
        if (!string.IsNullOrWhiteSpace(lastFolder) && Directory.Exists(lastFolder))
        {
            OpenFolder(lastFolder);
            return;
        }

        await OpenFolderFromPickerAsync();
    }

    public async Task OpenFolderFromPickerAsync()
    {
        if (PickFolderAsync is null)
        {
            return;
        }

        var path = await PickFolderAsync();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        OpenFolder(path);
        _settings.SaveLastFolder(path);
    }

    private async Task CreateGroupAsync()
    {
        var name = RequestTextAsync is null ? "New Group" : await RequestTextAsync("Create group", "New Group");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var group = _store.CreateGroup(name);
            var viewModel = CreateGroupViewModel(group);
            Groups.Add(viewModel);
            SelectedGroup = viewModel;
            StatusMessage = "Group created.";
        }
        catch (Exception ex)
        {
            await ShowError(ex.Message);
        }
    }

    private async Task RenameGroupAsync()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        var name = RequestTextAsync is null ? SelectedGroup.Name : await RequestTextAsync("Rename group", SelectedGroup.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            _store.RenameGroup(SelectedGroup.Model, name.Trim());
            SelectedGroup.RefreshName();
            StatusMessage = "Group renamed.";
        }
        catch (Exception ex)
        {
            await ShowError(ex.Message);
        }
    }

    private void DeleteGroup()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        var group = SelectedGroup;
        _store.DeleteGroup(group.Model);
        Groups.Remove(group);
        SelectedGroup = Groups.FirstOrDefault();
        StatusMessage = "Group deleted.";
    }

    private async Task AddTaskAsync()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        var result = RequestTaskEditAsync is null
            ? new TaskEditResult("New task", DateOnly.FromDateTime(DateTime.Today), TimeOnly.FromDateTime(DateTime.Now), null)
            : await RequestTaskEditAsync("Create task", null);

        if (result is null)
        {
            return;
        }

        try
        {
            var task = new TaskItem
            {
                Title = result.Title,
                Date = result.Date,
                Time = result.Time,
                Description = result.Description
            };

            TaskValidation.ValidateTask(task);
            PushUndoHistory(new TaskHistoryEntry(SelectedGroup.Model.Id, task.Id, null, TaskCloner.Clone(task)));
            SelectedTask = SelectedGroup.AddTask(task);
            StatusMessage = "Task added.";
        }
        catch (Exception ex)
        {
            await ShowError(ex.Message);
        }
    }

    public async Task EditTaskAsync(TaskItemViewModel task)
    {
        if (SelectedGroup is null)
        {
            return;
        }

        var result = RequestTaskEditAsync is null
            ? new TaskEditResult(task.Title, task.Model.Date, task.Model.Time, task.Description)
            : await RequestTaskEditAsync("Edit task", task.Model);

        if (result is null)
        {
            return;
        }

        try
        {
            var candidate = new TaskItem
            {
                Title = result.Title,
                Date = result.Date,
                Time = result.Time,
                Description = result.Description
            };
            TaskValidation.ValidateTask(candidate);

            var before = TaskCloner.Clone(task.Model);
            task.Apply(result);
            _store.SaveGroup(SelectedGroup.Model);
            PushUndoHistory(new TaskHistoryEntry(SelectedGroup.Model.Id, task.Model.Id, before, TaskCloner.Clone(task.Model)));
            SelectedTask = task;
            StatusMessage = "Task updated.";
        }
        catch (Exception ex)
        {
            await ShowError(ex.Message);
        }
    }

    private void DeleteTask()
    {
        if (SelectedGroup is null || SelectedTask is null)
        {
            return;
        }

        var task = SelectedTask;
        PushUndoHistory(new TaskHistoryEntry(SelectedGroup.Model.Id, task.Model.Id, TaskCloner.Clone(task.Model), null));
        SelectedGroup.RemoveTask(task);
        SelectedTask = SelectedGroup.Tasks.FirstOrDefault();
        StatusMessage = "Task deleted.";
    }

    private void OpenFolder(string path)
    {
        _store.OpenFolder(path);
        FolderPath = path;
        ClearTaskHistory();
        Groups.Clear();

        var result = _store.Load();
        foreach (var group in result.Groups)
        {
            Groups.Add(CreateGroupViewModel(group));
        }

        SelectedGroup = Groups.FirstOrDefault();
        StatusMessage = result.Errors.Count == 0
            ? "Folder opened."
            : $"Opened with {result.Errors.Count} skipped file(s).";

        if (result.Errors.Count > 0)
        {
            _ = ShowError("Some group files could not be loaded:\n\n" + string.Join("\n", result.Errors));
        }
    }

    private void OpenDataFolder()
    {
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            StatusMessage = "No data folder selected.";
            return;
        }

        try
        {
            ProcessStartInfo startInfo;
            if (OperatingSystem.IsWindows())
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = FolderPath,
                    UseShellExecute = true
                };
            }
            else
            {
                startInfo = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsMacOS() ? "open" : "xdg-open",
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add(FolderPath);
            }

            Process.Start(startInfo);
            StatusMessage = "Folder opened.";
        }
        catch (Exception ex)
        {
            _ = ShowError($"Could not open data folder:\n\n{ex.Message}");
        }
    }

    private TaskGroupViewModel CreateGroupViewModel(TaskGroup group)
    {
        return new TaskGroupViewModel(group, SaveGroup, RecordTaskChange);
    }

    private void RecordTaskChange(TaskGroupViewModel group, TaskItemViewModel task, TaskItem before)
    {
        PushUndoHistory(new TaskHistoryEntry(group.Model.Id, task.Model.Id, before, TaskCloner.Clone(task.Model)));
    }

    private void PushUndoHistory(TaskHistoryEntry entry)
    {
        PushBounded(_undoHistory, entry);
        _redoHistory.Clear();
        RaiseHistoryCommandStates();
    }

    private void UndoLastTaskChange()
    {
        while (_undoHistory.Count > 0)
        {
            var entry = PopLast(_undoHistory);
            if (ApplyTaskSnapshot(entry, entry.Before, isRedo: false))
            {
                PushBounded(_redoHistory, entry);
                RaiseHistoryCommandStates();
                return;
            }
        }

        RaiseHistoryCommandStates();
    }

    private void RedoLastTaskChange()
    {
        while (_redoHistory.Count > 0)
        {
            var entry = PopLast(_redoHistory);
            if (ApplyTaskSnapshot(entry, entry.After, isRedo: true))
            {
                PushBounded(_undoHistory, entry);
                RaiseHistoryCommandStates();
                return;
            }
        }

        RaiseHistoryCommandStates();
    }

    private bool ApplyTaskSnapshot(TaskHistoryEntry entry, TaskItem? snapshot, bool isRedo)
    {
        var group = Groups.FirstOrDefault(item => item.Model.Id == entry.GroupId);
        if (group is null)
        {
            return false;
        }

        var task = group.Tasks.FirstOrDefault(item => item.Model.Id == entry.TaskId);
        if (snapshot is null)
        {
            if (task is not null)
            {
                group.RemoveTask(task);
            }

            SelectedGroup = group;
            SelectedTask = group.Tasks.FirstOrDefault();
            StatusMessage = isRedo ? "Task deletion redone." : "Task creation undone.";
            return true;
        }

        if (task is null)
        {
            SelectedGroup = group;
            SelectedTask = group.RestoreTask(TaskCloner.Clone(snapshot));
            StatusMessage = isRedo ? "Task creation redone." : "Task deletion undone.";
            return true;
        }

        TaskCloner.CopyTo(snapshot, task.Model);
        task.RefreshAll();
        group.SaveTaskState();
        SelectedGroup = group;
        SelectedTask = task;
        StatusMessage = isRedo ? "Task change redone." : "Task change undone.";
        return true;
    }

    private static void PushBounded(List<TaskHistoryEntry> history, TaskHistoryEntry entry)
    {
        history.Add(entry);
        if (history.Count > HistoryLimit)
        {
            history.RemoveAt(0);
        }
    }

    private static TaskHistoryEntry PopLast(List<TaskHistoryEntry> history)
    {
        var lastIndex = history.Count - 1;
        var entry = history[lastIndex];
        history.RemoveAt(lastIndex);
        return entry;
    }

    private void ClearTaskHistory()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        RaiseHistoryCommandStates();
    }

    private void RaiseHistoryCommandStates()
    {
        UndoTaskCommand.RaiseCanExecuteChanged();
        RedoTaskCommand.RaiseCanExecuteChanged();
    }

    private void SaveGroup(TaskGroupViewModel group)
    {
        try
        {
            _store.SaveGroup(group.Model);
            group.RefreshCounts();
            StatusMessage = "Saved.";
        }
        catch (Exception ex)
        {
            _ = ShowError(ex.Message);
        }
    }

    private async Task ShowError(string message)
    {
        if (ShowErrorAsync is not null)
        {
            await ShowErrorAsync(message);
        }
    }

    private void RaiseCommandStates()
    {
        RenameGroupCommand.RaiseCanExecuteChanged();
        DeleteGroupCommand.RaiseCanExecuteChanged();
        AddTaskCommand.RaiseCanExecuteChanged();
        DeleteTaskCommand.RaiseCanExecuteChanged();
    }
}

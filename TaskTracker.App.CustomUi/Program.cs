using System.Collections.Specialized;
using System.ComponentModel;
using SkiaSharp;
using TaskTracker.App.ViewModels;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;
using TaskTracker.UiRuntime;

var window = new UiWindow(new UiWindowOptions("Folder Task Tracker - Custom UI Runtime", 980, 640, 520, 360));
window.SetRoot(new TaskTrackerShell());

if (args.Length == 2 && args[0] == "--render-smoke")
{
    window.RenderToPng(args[1], 980, 640);
    return;
}

window.Run();

internal sealed class TaskTrackerShell : StatefulWidget
{
    public TaskTrackerShell()
        : base("task-tracker-shell")
    {
    }

    public override State CreateState() => new TaskTrackerShellState();
}

internal sealed class TaskTrackerShellState : State
{
    private readonly ScrollController _taskScroll = new();
    private readonly Dictionary<string, Task> _subscriptions = new();
    private MainWindowViewModel? _vm;
    private DialogModel? _dialog;
    private bool _initialized;

    public override void InitState()
    {
        _vm = new MainWindowViewModel(new GroupFileStore(), new UserSettingsStore())
        {
            PickFolderAsync = PickFolderAsync,
            RequestTextAsync = RequestTextAsync,
            RequestTaskEditAsync = RequestTaskEditAsync,
            ShowErrorAsync = ShowErrorAsync
        };
        Watch(_vm);
        _ = InitializeAsync();
    }

    public override Widget Build(BuildContext context)
    {
        var shell = BuildShell(context);
        if (_dialog is null)
        {
            return new KeyRegion(shell, HandleShortcut);
        }

        return new KeyRegion(BuildModalOverlay(context, shell, _dialog), HandleShortcut);
    }

    private void HandleShortcut(KeyEvent key)
    {
        if (_vm is null || _dialog is not null || !key.HasControl)
        {
            return;
        }

        if ((key.Key is UiKey.Y || key.Key is UiKey.Z && key.HasShift) &&
            _vm.RedoTaskCommand.CanExecute(null))
        {
            _vm.RedoTaskCommand.Execute(null);
            WatchCollections();
            key.Use();
            SetState();
            return;
        }

        if (key.Key is UiKey.Z && _vm.UndoTaskCommand.CanExecute(null))
        {
            _vm.UndoTaskCommand.Execute(null);
            WatchCollections();
            key.Use();
            SetState();
        }
    }

    private async Task InitializeAsync()
    {
        if (_vm is null || _initialized)
        {
            return;
        }

        _initialized = true;
        await _vm.InitializeAsync();
        WatchCollections();
        SetState();
    }

    private Widget BuildShell(BuildContext context)
    {
        var theme = context.Theme;
        var vm = _vm!;

        return new Box(
            background: theme.WindowBackground,
            child: new Flex(Axis.Vertical, [
                new FlexItem(BuildMenu(theme)),
                new FlexItem(BuildToolbar(theme, context.Window)),
                new FlexItem(BuildBody(theme), Flex: 1),
                new FlexItem(BuildStatusBar(theme))
            ]));
    }

    private Widget BuildMenu(UiTheme theme)
    {
        var vm = _vm!;
        return new Box(
            background: SKColor.Parse("#F6FBFF"),
            border: SKColor.Parse("#B9DAF4"),
            borderWidth: 1,
            padding: new UiThickness(8, 6, 8, 6),
            child: new Flex(Axis.Horizontal, [
                CommandButton("_Open Folder", vm.OpenFolderCommand),
                CommandButton("_Create Group", vm.CreateGroupCommand),
                CommandButton("_Rename Group", vm.RenameGroupCommand),
                CommandButton("_Delete Group", vm.DeleteGroupCommand),
                new FlexItem(new Box(width: 16)),
                CommandButton(vm.StatusBarMenuText, vm.ToggleStatusBarCommand)
            ], spacing: 8, crossAxisAlignment: CrossAxisAlignment.Center));
    }

    private Widget BuildToolbar(UiTheme theme, UiWindow window)
    {
        var vm = _vm!;
        return new Box(
            background: SKColor.Parse("#F6FBFF"),
            border: SKColor.Parse("#B9DAF4"),
            borderWidth: 1,
            padding: new UiThickness(10, 8, 10, 8),
            child: new Flex(Axis.Horizontal, [
                CommandButton("+ Group", vm.CreateGroupCommand),
                new FlexItem(new Box(), Flex: 1),
                CommandButton("Folder", vm.OpenDataFolderCommand),
                new FlexItem(new CheckBox(vm.IsAlwaysOnTop, value =>
                {
                    vm.IsAlwaysOnTop = value;
                    window.TopMost = value;
                    SetState();
                }, "Always on top"))
            ], spacing: 8, crossAxisAlignment: CrossAxisAlignment.Center));
    }

    private Widget BuildBody(UiTheme theme)
    {
        return new Flex(Axis.Horizontal, [
            new FlexItem(BuildGroups(theme), Basis: 230),
            new FlexItem(new Box(width: 10)),
            new FlexItem(BuildTasks(theme), Flex: 1)
        ]);
    }

    private Widget BuildGroups(UiTheme theme)
    {
        var vm = _vm!;
        return new Box(
            background: SKColor.Parse("#F3FAFF"),
            border: SKColor.Parse("#B9DAF4"),
            borderWidth: 1,
            padding: new UiThickness(8),
            child: new Flex(Axis.Vertical, [
                new FlexItem(new Text("Groups", new TextStyle(14, theme.Text, Bold: true))),
                new FlexItem(new ScrollView(
                    new ListView<TaskGroupViewModel>(
                        vm.Groups,
                        group => BuildGroupRow(theme, group),
                        spacing: 4),
                    behavior: new ScrollBehavior(new SmoothScrollPhysics { WheelStep = 72 }, ScrollEdgeShaderEffect.Default)),
                    Flex: 1)
            ], spacing: 8));
    }

    private Widget BuildGroupRow(UiTheme theme, TaskGroupViewModel group)
    {
        var selected = ReferenceEquals(_vm!.SelectedGroup, group);
        return new TapRegion(
            new Box(
                background: selected ? SKColor.Parse("#BFE4FF") : SKColors.Transparent,
                border: selected ? SKColor.Parse("#8CCAF3") : SKColors.Transparent,
                borderWidth: selected ? 1 : 0,
                cornerRadius: 6,
                padding: new UiThickness(8, 6, 8, 6),
                child: new Flex(Axis.Horizontal, [
                    new FlexItem(new Text(group.Name, new TextStyle(13, theme.Text, Bold: true)), Flex: 1),
                    new FlexItem(new Text(group.OpenCount.ToString(), new TextStyle(12, theme.MutedText)))
                ], spacing: 8, crossAxisAlignment: CrossAxisAlignment.Center)),
            () =>
            {
                _vm!.SelectedGroup = group;
                WatchCollections();
                SetState();
            });
    }

    private Widget BuildTasks(UiTheme theme)
    {
        var vm = _vm!;
        var selectedGroup = vm.SelectedGroup;
        return new Box(
            background: theme.PanelBackground,
            border: SKColor.Parse("#B9DAF4"),
            borderWidth: 1,
            cornerRadius: 8,
            padding: new UiThickness(10),
            child: new Flex(Axis.Vertical, [
                new FlexItem(BuildTaskHeader(theme)),
                new FlexItem(selectedGroup is null
                    ? EmptyState(theme, "Open or create a group.")
                    : new ScrollView(
                        new ListView<TaskItemViewModel>(
                            selectedGroup.Tasks,
                            task => BuildTaskRow(theme, task),
                            spacing: 6),
                        controller: _taskScroll,
                        behavior: new ScrollBehavior(
                            new SmoothScrollPhysics { WheelStep = 96, Responsiveness = 18 },
                            new ScrollEdgeShaderEffect(edgeSize: 32, bendStrength: 0.88f))),
                    Flex: 1)
            ], spacing: 8));
    }

    private Widget BuildTaskHeader(UiTheme theme)
    {
        var vm = _vm!;
        return new Flex(Axis.Horizontal, [
            new FlexItem(new Text(vm.SelectedGroup?.Name ?? "No group", new TextStyle(16, theme.Text, Bold: true)), Flex: 1),
            CommandButton("Rename", vm.RenameGroupCommand),
            CommandButton("+ Task", vm.AddTaskCommand),
            CommandButton("Delete Task", vm.DeleteTaskCommand),
            CommandButton("Delete Group", vm.DeleteGroupCommand)
        ], spacing: 8, crossAxisAlignment: CrossAxisAlignment.Center);
    }

    private Widget BuildTaskRow(UiTheme theme, TaskItemViewModel task)
    {
        var selected = ReferenceEquals(_vm!.SelectedTask, task);
        return new TapRegion(
            new Box(
                background: selected ? SKColor.Parse("#BFE4FF") : SKColors.White.WithAlpha(150),
                border: selected ? SKColor.Parse("#8CCAF3") : SKColor.Parse("#D9EAF6"),
                borderWidth: 1,
                cornerRadius: 6,
                padding: new UiThickness(8, 6, 8, 6),
                child: new Flex(Axis.Horizontal, [
                    new FlexItem(new CheckBox(task.IsDone, value =>
                    {
                        task.IsDone = value;
                        WatchCollections();
                        SetState();
                    }), Basis: 26),
                    new FlexItem(new Flex(Axis.Vertical, [
                        new FlexItem(new Text(task.Title, new TextStyle(13, theme.Text, Bold: true))),
                        new FlexItem(new Text(task.StatusText, new TextStyle(11, theme.MutedText)))
                    ], spacing: 2), Flex: 1),
                    new FlexItem(new Text(task.DisplaySchedule, new TextStyle(12, theme.MutedText)), Basis: 128),
                    new FlexItem(new Button("Edit", () => _ = _vm!.EditTaskAsync(task)), Basis: 58)
                ], spacing: 8, crossAxisAlignment: CrossAxisAlignment.Center)),
            () =>
            {
                _vm!.SelectedTask = task;
                SetState();
            });
    }

    private Widget BuildStatusBar(UiTheme theme)
    {
        var vm = _vm!;
        return vm.IsStatusBarVisible
            ? new Box(
                background: SKColor.Parse("#F6FBFF"),
                padding: new UiThickness(10, 6, 10, 6),
                child: new Text(vm.StatusMessage ?? "", new TextStyle(12, theme.MutedText)))
            : new Box(height: 0);
    }

    private Widget EmptyState(UiTheme theme, string text)
    {
        return new Box(
            padding: new UiThickness(18),
            child: new Text(text, new TextStyle(13, theme.MutedText)));
    }

    private FlexItem CommandButton(string text, System.Windows.Input.ICommand command)
    {
        return new FlexItem(new Button(text, () =>
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
                WatchCollections();
                SetState();
            }
        }, command.CanExecute(null)));
    }

    private Widget BuildModalOverlay(BuildContext context, Widget shell, DialogModel dialog)
    {
        return new LiquidGlassModal(
            shell,
            dialog.Build(this, context.Theme),
            new LiquidGlassStyle
            {
                MaxWidth = 520,
                MaxHeight = 420,
                Margin = 24,
                Padding = new UiThickness(16),
                CornerRadius = 14,
                BlurPasses = 2
            });
    }

    private Task<string?> PickFolderAsync()
    {
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "FolderTaskTracker");
        return RequestTextAsync("Choose task data folder", fallback);
    }

    private Task<string?> RequestTextAsync(string title, string? initialValue)
    {
        var dialog = new TextDialogModel(title, initialValue ?? "");
        _dialog = dialog;
        SetState();
        return dialog.Task;
    }

    private Task<TaskEditResult?> RequestTaskEditAsync(string title, TaskItem? initialValue)
    {
        var dialog = new TaskDialogModel(title, initialValue);
        _dialog = dialog;
        SetState();
        return dialog.Task;
    }

    private Task ShowErrorAsync(string message)
    {
        var dialog = new MessageDialogModel("Folder Task Tracker", message);
        _dialog = dialog;
        SetState();
        return dialog.Task;
    }

    private void CloseDialog()
    {
        _dialog = null;
        WatchCollections();
        SetState();
    }

    private void Watch(INotifyPropertyChanged source)
    {
        source.PropertyChanged += (_, _) => SetState();
    }

    private void WatchCollections()
    {
        if (_vm is null)
        {
            return;
        }

        WatchCollection("groups", _vm.Groups);
        if (_vm.SelectedGroup is not null)
        {
            WatchCollection("tasks:" + _vm.SelectedGroup.Model.Id, _vm.SelectedGroup.Tasks);
            foreach (var task in _vm.SelectedGroup.Tasks)
            {
                Watch("task:" + task.Model.Id, task);
            }
        }
    }

    private void WatchCollection(string key, INotifyCollectionChanged collection)
    {
        if (_subscriptions.ContainsKey(key))
        {
            return;
        }

        collection.CollectionChanged += (_, _) => SetState();
        _subscriptions[key] = Task.CompletedTask;
    }

    private void Watch(string key, INotifyPropertyChanged source)
    {
        if (_subscriptions.ContainsKey(key))
        {
            return;
        }

        source.PropertyChanged += (_, _) => SetState();
        _subscriptions[key] = Task.CompletedTask;
    }

    private abstract class DialogModel
    {
        protected DialogModel(string title)
        {
            Title = title;
        }

        public string Title { get; }
        public abstract Widget Build(TaskTrackerShellState owner, UiTheme theme);
    }

    private sealed class TextDialogModel : DialogModel
    {
        private readonly TaskCompletionSource<string?> _completion = new();
        private readonly TextInputController _value;

        public TextDialogModel(string title, string initial)
            : base(title)
        {
            _value = new TextInputController(initial);
        }

        public Task<string?> Task => _completion.Task;

        public override Widget Build(TaskTrackerShellState owner, UiTheme theme)
        {
            return new Flex(Axis.Vertical, [
                new FlexItem(new Text(Title, new TextStyle(16, theme.Text, Bold: true))),
                new FlexItem(new TextInput(_value, maxLength: 260)),
                new FlexItem(new Flex(Axis.Horizontal, [
                    new FlexItem(new Box(), Flex: 1),
                    new FlexItem(new Button("Cancel", () =>
                    {
                        _completion.TrySetResult(null);
                        owner.CloseDialog();
                    })),
                    new FlexItem(new Button("OK", () =>
                    {
                        _completion.TrySetResult(_value.Text.Trim());
                        owner.CloseDialog();
                    }))
                ], spacing: 8))
            ], spacing: 12);
        }
    }

    private sealed class TaskDialogModel : DialogModel
    {
        private readonly TaskCompletionSource<TaskEditResult?> _completion = new();
        private readonly TextInputController _title;
        private readonly TextInputController _date;
        private readonly TextInputController _time;
        private readonly TextInputController _description;
        private string _error = "";

        public TaskDialogModel(string title, TaskItem? initial)
            : base(title)
        {
            var now = DateTime.Now;
            _title = new TextInputController(initial?.Title ?? "");
            _date = new TextInputController((initial?.Date ?? DateOnly.FromDateTime(now)).ToString("yyyy-MM-dd"));
            _time = new TextInputController((initial?.Time ?? TimeOnly.FromDateTime(now)).ToString("HH:mm"));
            _description = new TextInputController(initial?.Description ?? "");
        }

        public Task<TaskEditResult?> Task => _completion.Task;

        public override Widget Build(TaskTrackerShellState owner, UiTheme theme)
        {
            return new Flex(Axis.Vertical, [
                new FlexItem(new Text(Title, new TextStyle(16, theme.Text, Bold: true))),
                new FlexItem(new TextInput(_title, "Task name", maxLength: 100)),
                new FlexItem(new Flex(Axis.Horizontal, [
                    new FlexItem(new TextInput(_date, "yyyy-MM-dd", maxLength: 10), Flex: 1),
                    new FlexItem(new TextInput(_time, "HH:mm", maxLength: 5), Flex: 1)
                ], spacing: 8)),
                new FlexItem(new TextInput(_description, "Description", multiline: true, maxLength: 500), Flex: 1),
                new FlexItem(new Text(_error, new TextStyle(12, theme.Error))),
                new FlexItem(new Flex(Axis.Horizontal, [
                    new FlexItem(new Box(), Flex: 1),
                    new FlexItem(new Button("Cancel", () =>
                    {
                        _completion.TrySetResult(null);
                        owner.CloseDialog();
                    })),
                    new FlexItem(new Button("Save", () => Save(owner)))
                ], spacing: 8))
            ], spacing: 10);
        }

        private void Save(TaskTrackerShellState owner)
        {
            if (!DateOnly.TryParse(_date.Text, out var date))
            {
                _error = "Choose a date as yyyy-MM-dd.";
                owner.SetState();
                return;
            }

            if (!TimeOnly.TryParse(_time.Text, out var time))
            {
                _error = "Enter time as HH:mm.";
                owner.SetState();
                return;
            }

            var result = new TaskEditResult(
                _title.Text.Trim(),
                date,
                time,
                string.IsNullOrWhiteSpace(_description.Text) ? null : _description.Text.Trim());

            try
            {
                TaskValidation.ValidateTask(new TaskItem
                {
                    Title = result.Title,
                    Date = result.Date,
                    Time = result.Time,
                    Description = result.Description
                });
            }
            catch
            {
                _error = string.IsNullOrWhiteSpace(result.Title) ? "Enter a task name." : "Check the task fields.";
                owner.SetState();
                return;
            }

            _completion.TrySetResult(result);
            owner.CloseDialog();
        }
    }

    private sealed class MessageDialogModel : DialogModel
    {
        private readonly TaskCompletionSource _completion = new();
        private readonly string _message;

        public MessageDialogModel(string title, string message)
            : base(title)
        {
            _message = message;
        }

        public Task Task => _completion.Task;

        public override Widget Build(TaskTrackerShellState owner, UiTheme theme)
        {
            return new Flex(Axis.Vertical, [
                new FlexItem(new Text(Title, new TextStyle(16, theme.Text, Bold: true))),
                new FlexItem(new ScrollView(new Text(_message, new TextStyle(13, theme.Text))), Flex: 1),
                new FlexItem(new Flex(Axis.Horizontal, [
                    new FlexItem(new Box(), Flex: 1),
                    new FlexItem(new Button("OK", () =>
                    {
                        _completion.TrySetResult();
                        owner.CloseDialog();
                    }))
                ]))
            ], spacing: 12);
        }
    }
}

using Android.Content;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TaskTracker.App.ViewModels;
using TaskTracker.App.Android.Storage;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;
using TaskTracker.Presentation.Services;
using Button = Avalonia.Controls.Button;
using CheckBox = Avalonia.Controls.CheckBox;
using Orientation = Avalonia.Layout.Orientation;
using ToggleButton = Avalonia.Controls.Primitives.ToggleButton;

namespace TaskTracker.App.Android.Views;

public sealed class MainView : UserControl
{
    private const string CardNormalBackground = "#FAFBFC";
    private const string CardNormalBorder = "#D8DEE4";
    private const string CardSelectedBackground = "#EEF6FF";
    private const string CardSelectedBorder = "#79B8FF";
    private const string CardPressedBackground = "#EAF2F8";
    private const string CardPressedBorder = "#8DB7D9";

    private static readonly Thickness PagePadding = new(10, 8);
    private static readonly Thickness CardPadding = new(9, 7);
    private static readonly Thickness CardGap = new(0, 0, 0, 5);

    private readonly MainWindowViewModel _viewModel;
    private readonly Border _dialogOverlay;
    private readonly Border _dialogFrame;
    private readonly ContentControl _dialogHost;
    private readonly DispatcherTimer _scrollSaveTimer;
    private readonly Dictionary<TaskItemViewModel, Border> _taskRows = new();
    private readonly Dictionary<TaskGroupViewModel, Border> _groupRows = new();
    private ListBox? _taskList;
    private ScrollViewer? _taskScrollViewer;
    private TaskGroupViewModel? _scrollTrackedGroup;
    private double _pendingTaskScrollOffset;
    private bool _isRestoringTaskScroll;
    private bool _initialized;
    private Action? _dialogBackAction;

    public MainView()
    {
        var services = new AndroidPlatformServices(this);
        _viewModel = MainWindowViewModelFactory.Create(services);
        DataContext = _viewModel;

        _dialogHost = new ContentControl();
        _dialogFrame = BuildDialogFrame(_dialogHost);
        _dialogOverlay = BuildDialogOverlay(_dialogFrame);
        Content = BuildRoot();
        MainActivity.CustomBackRequested = HandleBackRequested;

        _scrollSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _scrollSaveTimer.Tick += SavePendingTaskScroll;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.SelectedGroup))
            {
                SaveTrackedTaskScroll();
                RestoreSelectedGroupScroll();
                UpdateGroupRowStyles();
                UpdateTaskRowStyles();
            }
            else if (args.PropertyName == nameof(MainWindowViewModel.SelectedTask))
            {
                UpdateTaskRowStyles();
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            SaveTrackedTaskScroll();
            MainActivity.CustomBackRequested = null;
        };

        AttachedToVisualTree += async (_, _) =>
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            try
            {
                await _viewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"Не удалось запустить приложение:\n\n{ex.Message}");
            }
        };
    }

    private Grid BuildRoot()
    {
        var root = new Grid
        {
            Background = Brushes.White
        };

        root.Children.Add(BuildShell());
        root.Children.Add(_dialogOverlay);
        return root;
    }

    private Control BuildShell()
    {
        var shell = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = PagePadding,
            RowSpacing = 8
        };

        shell.Children.Add(Row(BuildGroupPickerRow(), 0));
        shell.Children.Add(Row(BuildActionBar(), 1));
        shell.Children.Add(Row(BuildTaskPanel(), 2));
        shell.Children.Add(Row(BuildStatusLine(), 3));
        return shell;
    }

    private Control BuildGroupPickerRow()
    {
        var label = new TextBlock
        {
            Text = "Группа",
            FontSize = 12,
            Foreground = Brush("#5A6670"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var name = new TextBlock
        {
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#17212B"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        name.Bind(TextBlock.TextProperty, new Binding("SelectedGroup.Name")
        {
            FallbackValue = "Нет группы"
        });

        var counts = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush("#5A6670"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        counts.Bind(TextBlock.TextProperty, new Binding("SelectedGroup.OpenCount")
        {
            StringFormat = "{0} активных"
        });

        var textGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8
        };
        textGrid.Children.Add(Column(label, 0));
        textGrid.Children.Add(Column(name, 1));
        textGrid.Children.Add(Column(counts, 2));

        var button = new Button
        {
            Content = textGrid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 7),
            MinHeight = 38
        };
        button.Click += (_, _) => ShowGroupScreen();
        return button;
    }

    private Control BuildActionBar()
    {
        var add = CompactButton("+ Задача", _viewModel.AddTaskCommand);
        var delete = CompactButton("Удалить", _viewModel.DeleteTaskCommand);
        var undo = CompactButton("Назад", _viewModel.UndoTaskCommand);
        var redo = CompactButton("Повтор", _viewModel.RedoTaskCommand);
        var folder = CompactButton("Папка", _viewModel.OpenFolderCommand);

        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*"),
            ColumnSpacing = 6
        };
        bar.Children.Add(Column(add, 0));
        bar.Children.Add(Column(delete, 1));
        bar.Children.Add(Column(undo, 2));
        bar.Children.Add(Column(redo, 3));
        bar.Children.Add(Column(folder, 4));
        return bar;
    }

    private Control BuildTaskPanel()
    {
        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<TaskItemViewModel>((task, _) => BuildTaskRow(task))
        };
        _taskList = list;
        list.AttachedToVisualTree += (_, _) => AttachTaskScrollViewer();
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding("SelectedGroup.Tasks"));
        list.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(MainWindowViewModel.SelectedTask))
        {
            Mode = BindingMode.TwoWay
        });

        return list;
    }

    private void AttachTaskScrollViewer()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_taskList is null)
            {
                return;
            }

            var scrollViewer = _taskList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (ReferenceEquals(_taskScrollViewer, scrollViewer))
            {
                RestoreSelectedGroupScroll();
                return;
            }

            if (_taskScrollViewer is not null)
            {
                _taskScrollViewer.PropertyChanged -= OnTaskScrollViewerPropertyChanged;
            }

            _taskScrollViewer = scrollViewer;
            if (_taskScrollViewer is not null)
            {
                _taskScrollViewer.PropertyChanged += OnTaskScrollViewerPropertyChanged;
            }

            RestoreSelectedGroupScroll();
        }, DispatcherPriority.Loaded);
    }

    private void OnTaskScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (_isRestoringTaskScroll || args.Property != ScrollViewer.OffsetProperty || _taskScrollViewer is null)
        {
            return;
        }

        _scrollTrackedGroup ??= _viewModel.SelectedGroup;
        _pendingTaskScrollOffset = _taskScrollViewer.Offset.Y;
        if (!_scrollSaveTimer.IsEnabled)
        {
            _scrollSaveTimer.Start();
        }
    }

    private void SavePendingTaskScroll(object? sender, EventArgs e)
    {
        _scrollSaveTimer.Stop();
        if (_scrollTrackedGroup is not null)
        {
            _viewModel.SaveTaskScrollOffset(_scrollTrackedGroup, _pendingTaskScrollOffset);
        }
    }

    private void SaveTrackedTaskScroll()
    {
        _scrollSaveTimer.Stop();
        if (_taskScrollViewer is not null && _scrollTrackedGroup is not null)
        {
            _viewModel.SaveTaskScrollOffset(_scrollTrackedGroup, _taskScrollViewer.Offset.Y);
        }
    }

    private void RestoreSelectedGroupScroll()
    {
        var group = _viewModel.SelectedGroup;
        _scrollTrackedGroup = group;
        var offset = _viewModel.GetTaskScrollOffset(group);

        _isRestoringTaskScroll = true;
        Dispatcher.UIThread.Post(() =>
        {
            ApplyTaskScrollOffset(offset);
            Dispatcher.UIThread.Post(() =>
            {
                ApplyTaskScrollOffset(offset);
                _pendingTaskScrollOffset = offset;
                _isRestoringTaskScroll = false;
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Loaded);
    }

    private void ApplyTaskScrollOffset(double offset)
    {
        if (_taskScrollViewer is null)
        {
            return;
        }

        var maxY = Math.Max(0, _taskScrollViewer.Extent.Height - _taskScrollViewer.Viewport.Height);
        var targetY = maxY > 0 ? Math.Min(offset, maxY) : offset;
        _taskScrollViewer.Offset = new Vector(_taskScrollViewer.Offset.X, Math.Max(0, targetY));
    }

    private Control BuildStatusLine()
    {
        var status = new TextBlock
        {
            FontSize = 11,
            Foreground = Brush("#6D7780"),
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(MainWindowViewModel.StatusMessage)));

        var folder = new Button
        {
            Content = "Открыть",
            Command = _viewModel.OpenDataFolderCommand,
            FontSize = 11,
            MinHeight = 28,
            Padding = new Thickness(8, 3)
        };

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 6
        };
        row.Children.Add(Column(status, 0));
        row.Children.Add(Column(folder, 1));
        return row;
    }

    private Control BuildTaskRow(TaskItemViewModel? task)
    {
        if (task is null)
        {
            return new TextBlock();
        }

        var done = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        done.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(TaskItemViewModel.IsDone))
        {
            Mode = BindingMode.TwoWay
        });

        var title = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#17212B"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(TaskItemViewModel.Title)));

        var schedule = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush("#5A6670"),
            MaxLines = 1
        };
        schedule.Bind(TextBlock.TextProperty, new Binding(nameof(TaskItemViewModel.DisplaySchedule)));

        var text = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { title, schedule }
        };

        var edit = new Button
        {
            Content = "Изм.",
            MinWidth = 50,
            MinHeight = 32,
            Padding = new Thickness(8, 4),
            Margin = new Thickness(6, 0, 0, 0)
        };
        edit.Click += async (_, _) => await _viewModel.EditTaskAsync(task);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            MinHeight = 44
        };
        row.Children.Add(Column(done, 0));
        row.Children.Add(Column(text, 1));
        row.Children.Add(Column(edit, 2));

        var card = new Border
        {
            Background = Brush(CardNormalBackground),
            BorderBrush = Brush(CardNormalBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = CardPadding,
            Margin = CardGap,
            Child = row
        };

        _taskRows[task] = card;
        ApplyTaskRowStyle(task, card, isPressed: false);
        card.PointerPressed += (_, _) => ApplyCardStyle(card, isSelected: true, isPressed: true);
        card.PointerReleased += (_, _) =>
        {
            _viewModel.SelectedTask = task;
            ApplyTaskRowStyle(task, card, isPressed: false);
        };
        card.PointerCaptureLost += (_, _) => ApplyTaskRowStyle(task, card, isPressed: false);

        return card;
    }

    private void ShowGroupScreen()
    {
        var title = new TextBlock
        {
            Text = "Группы",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#17212B"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var close = CompactButton("Закрыть");
        close.Click += (_, _) => CloseDialog();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        header.Children.Add(Column(title, 0));
        header.Children.Add(Column(close, 1));

        var groups = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<TaskGroupViewModel>((group, _) => BuildGroupRow(group))
        };
        groups.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(MainWindowViewModel.Groups)));
        groups.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(MainWindowViewModel.SelectedGroup))
        {
            Mode = BindingMode.TwoWay
        });

        var create = CompactButton("+ Новая", _viewModel.CreateGroupCommand);
        var rename = CompactButton("Имя", _viewModel.RenameGroupCommand);
        var delete = CompactButton("Удалить", _viewModel.DeleteGroupCommand);

        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 6
        };
        actions.Children.Add(Column(create, 0));
        actions.Children.Add(Column(rename, 1));
        actions.Children.Add(Column(delete, 2));

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 10,
            MinHeight = 360
        };
        content.Children.Add(Row(header, 0));
        content.Children.Add(Row(groups, 1));
        content.Children.Add(Row(actions, 2));

        ShowDialog(content, stretch: true, backAction: CloseDialog);
    }

    private Control BuildGroupRow(TaskGroupViewModel? group)
    {
        if (group is null)
        {
            return new TextBlock();
        }

        var name = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#17212B"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        name.Bind(TextBlock.TextProperty, new Binding(nameof(TaskGroupViewModel.Name)));

        var counts = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush("#5A6670")
        };
        counts.Bind(TextBlock.TextProperty, new Binding(nameof(TaskGroupViewModel.TotalCount))
        {
            StringFormat = "{0} задач"
        });

        var open = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush("#0F6B4A"),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        open.Bind(TextBlock.TextProperty, new Binding(nameof(TaskGroupViewModel.OpenCount))
        {
            StringFormat = "{0} активных"
        });

        var meta = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8
        };
        meta.Children.Add(Column(counts, 0));
        meta.Children.Add(Column(open, 1));

        var row = new Border
        {
            Background = Brush(CardNormalBackground),
            BorderBrush = Brush(CardNormalBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = CardPadding,
            Margin = CardGap,
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { name, meta }
            }
        };

        _groupRows[group] = row;
        ApplyGroupRowStyle(group, row, isPressed: false);
        row.PointerPressed += (_, _) => ApplyCardStyle(row, isSelected: true, isPressed: true);
        row.PointerReleased += (_, args) =>
        {
            _viewModel.SelectedGroup = group;
            CloseDialog();
            args.Handled = true;
        };

        return row;
    }

    private void UpdateTaskRowStyles()
    {
        foreach (var (task, row) in _taskRows)
        {
            ApplyTaskRowStyle(task, row, isPressed: false);
        }
    }

    private void UpdateGroupRowStyles()
    {
        foreach (var (group, row) in _groupRows)
        {
            ApplyGroupRowStyle(group, row, isPressed: false);
        }
    }

    private void ApplyTaskRowStyle(TaskItemViewModel task, Border row, bool isPressed)
    {
        ApplyCardStyle(row, ReferenceEquals(_viewModel.SelectedTask, task), isPressed);
    }

    private void ApplyGroupRowStyle(TaskGroupViewModel group, Border row, bool isPressed)
    {
        ApplyCardStyle(row, ReferenceEquals(_viewModel.SelectedGroup, group), isPressed);
    }

    private static void ApplyCardStyle(Border row, bool isSelected, bool isPressed)
    {
        row.Background = Brush(isPressed ? CardPressedBackground : isSelected ? CardSelectedBackground : CardNormalBackground);
        row.BorderBrush = Brush(isPressed ? CardPressedBorder : isSelected ? CardSelectedBorder : CardNormalBorder);
        row.BorderThickness = new Thickness(isSelected || isPressed ? 1.5 : 1);
    }

    private static Border BuildDialogOverlay(Border frame)
    {
        return new Border
        {
            IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(132, 16, 22, 28)),
            Padding = new Thickness(10),
            Child = new Grid
            {
                Children = { frame }
            }
        };
    }

    private static Border BuildDialogFrame(ContentControl host)
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#C9D1D9"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 560,
            Child = host
        };
    }

    private Task<string?> RequestTextAsync(string title, string? initialValue)
    {
        var completion = new TaskCompletionSource<string?>();
        var input = new TextBox
        {
            Text = initialValue ?? "",
            PlaceholderText = title,
            MaxLength = 100
        };

        void Cancel()
        {
            CloseDialog();
            completion.TrySetResult(null);
        }

        void Confirm()
        {
            CloseDialog();
            completion.TrySetResult(input.Text?.Trim());
        }

        ShowDialog(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                DialogTitle(title),
                input,
                DialogButtons(
                    ("Отмена", Cancel),
                    ("OK", Confirm))
            }
        }, backAction: Cancel);

        input.Focus();
        input.SelectAll();
        return completion.Task;
    }

    private Task<string?> RequestFolderAsync(string defaultPath, string? externalPath)
    {
        var completion = new TaskCompletionSource<string?>();
        var pathBox = new TextBox
        {
            Text = externalPath ?? defaultPath,
            PlaceholderText = "Путь к папке",
            MaxLength = 260
        };

        var currentPath = new TextBlock
        {
            Text = _viewModel.FolderPath ?? defaultPath,
            FontSize = 12,
            Foreground = Brush("#5A6670"),
            TextWrapping = TextWrapping.Wrap
        };

        void Complete(string? path)
        {
            CloseDialog();
            completion.TrySetResult(string.IsNullOrWhiteSpace(path) ? null : path.Trim());
        }

        var presetButtons = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 6
        };
        presetButtons.Children.Add(Column(CompactButton("Внутренняя", click: () => Complete(defaultPath)), 0));
        presetButtons.Children.Add(Column(CompactButton("Внешняя", click: () => Complete(externalPath ?? defaultPath)), 1));

        ShowDialog(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                DialogTitle("Папка данных"),
                currentPath,
                presetButtons,
                pathBox,
                DialogButtons(
                    ("Отмена", () => Complete(null)),
                    ("Открыть", () => Complete(pathBox.Text)))
            }
        }, backAction: () => Complete(null));

        pathBox.Focus();
        pathBox.SelectAll();
        return completion.Task;
    }

    private Task<TaskEditResult?> RequestTaskEditAsync(string title, TaskItem? initialValue)
    {
        var completion = new TaskCompletionSource<TaskEditResult?>();
        var now = DateTime.Now;
        var titleBox = new TextBox
        {
            Text = initialValue?.Title ?? "",
            PlaceholderText = "Название",
            MaxLength = TaskValidation.MaxTaskTitleLength
        };
        var dateBox = new TextBox
        {
            Text = (initialValue?.Date ?? DateOnly.FromDateTime(now)).ToString("yyyy-MM-dd"),
            PlaceholderText = "yyyy-MM-dd",
            MaxLength = 10
        };
        var timeBox = new TextBox
        {
            Text = (initialValue?.Time ?? TimeOnly.FromDateTime(now)).ToString("HH:mm"),
            PlaceholderText = "HH:mm",
            MaxLength = 5
        };
        var descriptionBox = new TextBox
        {
            Text = initialValue?.Description ?? "",
            PlaceholderText = "Описание",
            MaxLength = TaskValidation.MaxDescriptionLength,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 76
        };
        var error = new TextBlock
        {
            Foreground = Brush("#B42318"),
            TextWrapping = TextWrapping.Wrap
        };

        void Save()
        {
            error.Text = "";
            if (!DateOnly.TryParse(dateBox.Text, out var date))
            {
                error.Text = "Дата в формате yyyy-MM-dd.";
                return;
            }

            if (!TimeOnly.TryParse(timeBox.Text, out var time))
            {
                error.Text = "Время в формате HH:mm.";
                return;
            }

            var result = new TaskEditResult(
                titleBox.Text?.Trim() ?? "",
                date,
                time,
                string.IsNullOrWhiteSpace(descriptionBox.Text) ? null : descriptionBox.Text.Trim());

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
                error.Text = string.IsNullOrWhiteSpace(result.Title) ? "Введите название задачи." : "Проверьте поля задачи.";
                return;
            }

            CloseDialog();
            completion.TrySetResult(result);
        }

        void Cancel()
        {
            CloseDialog();
            completion.TrySetResult(null);
        }

        ShowDialog(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                DialogTitle(title),
                titleBox,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    ColumnSpacing = 6,
                    Children =
                    {
                        Column(dateBox, 0),
                        Column(timeBox, 1)
                    }
                },
                descriptionBox,
                error,
                DialogButtons(
                    ("Отмена", Cancel),
                    ("Сохранить", Save))
            }
        }, backAction: Cancel);

        titleBox.Focus();
        titleBox.SelectAll();
        return completion.Task;
    }

    private Task ShowErrorAsync(string message)
    {
        var completion = new TaskCompletionSource();
        void Dismiss()
        {
            CloseDialog();
            completion.TrySetResult();
        }

        ShowDialog(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                DialogTitle("Folder Task Tracker"),
                new ScrollViewer
                {
                    MaxHeight = 260,
                    Content = new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brush("#17212B")
                    }
                },
                DialogButtons(("OK", Dismiss))
            }
        }, backAction: Dismiss);

        return completion.Task;
    }

    private void ShowDialog(Control content, bool stretch = false, Action? backAction = null)
    {
        _dialogHost.Content = content;
        _dialogBackAction = backAction;
        _dialogFrame.VerticalAlignment = stretch ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        _dialogFrame.Margin = new Thickness(0);
        _dialogOverlay.IsVisible = true;
    }

    private void CloseDialog()
    {
        _dialogOverlay.IsVisible = false;
        _dialogBackAction = null;
        _dialogHost.Content = null;
    }

    private bool HandleBackRequested()
    {
        if (!_dialogOverlay.IsVisible)
        {
            return false;
        }

        var backAction = _dialogBackAction;
        if (backAction is null)
        {
            CloseDialog();
        }
        else
        {
            backAction();
        }

        return true;
    }

    private static TextBlock DialogTitle(string title)
    {
        return new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush("#17212B")
        };
    }

    private static Control DialogButtons(params (string Text, Action Click)[] buttons)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };

        foreach (var button in buttons)
        {
            panel.Children.Add(CompactButton(button.Text, click: button.Click));
        }

        return panel;
    }

    private static Button CompactButton(string text, System.Windows.Input.ICommand? command = null, Action? click = null)
    {
        var control = new Button
        {
            Content = text,
            Command = command,
            FontSize = 13,
            MinHeight = 34,
            Padding = new Thickness(8, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        if (click is not null)
        {
            control.Click += (_, _) => click();
        }

        return control;
    }

    private static T Row<T>(T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static T Column<T>(T control, int column)
        where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static IBrush Brush(string color)
    {
        return new SolidColorBrush(Color.Parse(color));
    }

    private sealed class AndroidPlatformServices : ITaskTrackerPlatformServices
    {
        private readonly MainView _owner;
        private bool _returnedStartupFolder;

        public AndroidPlatformServices(MainView owner)
        {
            _owner = owner;
            StoragePaths = AppStoragePaths.FromBaseFolder(GetBaseFolder());
        }

        public AppStoragePaths StoragePaths { get; }
        public IGroupStorage GroupStorage { get; } = new AndroidDocumentTreeGroupStorage();

        public async Task<string?> PickFolderAsync()
        {
            if (!_returnedStartupFolder)
            {
                _returnedStartupFolder = true;
                return StoragePaths.DataFolderPath;
            }

            var pickedFolder = await MainActivity.PickFolderAsync();
            return string.IsNullOrWhiteSpace(pickedFolder) ? null : pickedFolder;
        }

        public Task<string?> RequestTextAsync(string title, string? initialValue)
        {
            return _owner.RequestTextAsync(title, initialValue);
        }

        public Task<TaskEditResult?> RequestTaskEditAsync(string title, TaskItem? initialValue)
        {
            return _owner.RequestTaskEditAsync(title, initialValue);
        }

        public Task ShowErrorAsync(string message)
        {
            return _owner.ShowErrorAsync(message);
        }

        public Task OpenDataFolderAsync(string folderPath)
        {
            try
            {
                var intent = new Intent(Intent.ActionView);
                intent.SetDataAndType(global::Android.Net.Uri.Parse(folderPath), "*/*");
                intent.AddFlags(ActivityFlags.NewTask);
                global::Android.App.Application.Context.StartActivity(Intent.CreateChooser(intent, "Открыть папку"));
                return Task.CompletedTask;
            }
            catch
            {
                return _owner.ShowErrorAsync($"Текущая папка:\n\n{folderPath}");
            }
        }

        private static string GetBaseFolder()
        {
            var filesDir = global::Android.App.Application.Context.FilesDir?.AbsolutePath;
            if (!string.IsNullOrWhiteSpace(filesDir))
            {
                return Path.Combine(filesDir, "FolderTaskTracker");
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderTaskTracker");
        }

        private static string? GetExternalDataFolder()
        {
            var context = global::Android.App.Application.Context;
            var externalDir = context.GetExternalFilesDir(null)?.AbsolutePath;
            return string.IsNullOrWhiteSpace(externalDir)
                ? null
                : Path.Combine(externalDir, "FolderTaskTracker");
        }
    }
}

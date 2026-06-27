using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;
using TaskTracker.App.ViewModels;
using TaskStatus = TaskTracker.Core.Models.TaskStatus;

var tests = new (string Name, Action Run)[]
{
    ("create load rename delete group", CreateLoadRenameDeleteGroup),
    ("create delete edit status task", CreateDeleteEditStatusTask),
    ("validates task limits", ValidatesTaskLimits),
    ("saves changed group file", SavesChangedGroupFile),
    ("builds safe file names", BuildsSafeFileNames),
    ("skips broken json files", SkipsBrokenJsonFiles),
    ("undo restores previous task version", UndoRestoresPreviousTaskVersion),
    ("redo reapplies undone task version", RedoReappliesUndoneTaskVersion),
    ("task history keeps last fifty actions", TaskHistoryKeepsLastFiftyActions),
    ("completed task sorting stays visual", CompletedTaskSortingStaysVisual)
};

var passed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {test.Name}");
        Console.Error.WriteLine(ex);
        Environment.ExitCode = 1;
        break;
    }
}

Console.WriteLine($"{passed}/{tests.Length} tests passed.");

static void CreateLoadRenameDeleteGroup()
{
    using var temp = TempFolder.Create();
    var store = new GroupFileStore();
    store.OpenFolder(temp.Path);

    var group = store.CreateGroup("Work");
    AssertEqual(1, Directory.GetFiles(temp.Path, "*.group.json").Length);

    var loaded = store.Load();
    AssertEqual(1, loaded.Groups.Count);
    AssertEqual("Work", loaded.Groups[0].Name);

    store.RenameGroup(group, "Personal");
    AssertEqual("Personal", store.Load().Groups[0].Name);
    AssertEqual(1, Directory.GetFiles(temp.Path, "*.group.json").Length);
    AssertContains("personal", Path.GetFileName(store.GetPath(group)));

    store.DeleteGroup(group);
    AssertEqual(0, Directory.GetFiles(temp.Path, "*.group.json").Length);
}

static void CreateDeleteEditStatusTask()
{
    using var temp = TempFolder.Create();
    var store = new GroupFileStore();
    store.OpenFolder(temp.Path);

    var group = store.CreateGroup("Tasks");
    var task = new TaskItem
    {
        Title = "Ship app",
        Date = new DateOnly(2026, 6, 8),
        Time = new TimeOnly(14, 30),
        Description = "Smoke test and publish.",
        Status = TaskStatus.Todo
    };

    group.Tasks.Add(task);
    store.SaveGroup(group);

    var loadedTask = store.Load().Groups[0].Tasks[0];
    AssertEqual("Ship app", loadedTask.Title);
    AssertEqual(TaskStatus.Todo, loadedTask.Status);

    var statusGroup = store.Load().Groups[0];
    statusGroup.Tasks[0].Status = TaskStatus.Done;
    store.SaveGroup(statusGroup);
    AssertEqual(TaskStatus.Done, store.Load().Groups[0].Tasks[0].Status);

    var loadedGroup = store.Load().Groups[0];
    loadedGroup.Tasks.Clear();
    store.SaveGroup(loadedGroup);
    AssertEqual(0, store.Load().Groups[0].Tasks.Count);
}

static void ValidatesTaskLimits()
{
    var valid = new TaskItem
    {
        Title = new string('a', TaskValidation.MaxTaskTitleLength),
        Description = new string('b', TaskValidation.MaxDescriptionLength)
    };
    TaskValidation.ValidateTask(valid);

    AssertThrows<ArgumentException>(() => TaskValidation.ValidateTask(new TaskItem { Title = "" }));
    AssertThrows<ArgumentException>(() => TaskValidation.ValidateTask(new TaskItem { Title = new string('a', 101) }));
    AssertThrows<ArgumentException>(() => TaskValidation.ValidateTask(new TaskItem
    {
        Title = "Valid",
        Description = new string('b', 501)
    }));
}

static void SavesChangedGroupFile()
{
    using var temp = TempFolder.Create();
    var store = new GroupFileStore();
    store.OpenFolder(temp.Path);
    var group = store.CreateGroup("State");
    var path = store.GetPath(group);
    var initial = File.GetLastWriteTimeUtc(path);

    Thread.Sleep(30);
    group.Tasks.Add(new TaskItem { Title = "Changed" });
    store.SaveGroup(group);

    var changed = File.GetLastWriteTimeUtc(path);
    AssertTrue(changed > initial, "Expected group file write time to change.");
}

static void BuildsSafeFileNames()
{
    var fileName = GroupFileNames.BuildFileName("  Work / Inbox: Now!  ", "abcdef123456");
    AssertEqual("work-inbox-now-abcdef12.group.json", fileName);

    var fallback = GroupFileNames.BuildFileName("///", "12345678");
    AssertEqual("group-12345678.group.json", fallback);
}

static void SkipsBrokenJsonFiles()
{
    using var temp = TempFolder.Create();
    File.WriteAllText(Path.Combine(temp.Path, "broken.group.json"), "{ not json");

    var store = new GroupFileStore();
    store.OpenFolder(temp.Path);
    var result = store.Load();

    AssertEqual(0, result.Groups.Count);
    AssertEqual(1, result.Errors.Count);
}

static void UndoRestoresPreviousTaskVersion()
{
    using var temp = TempFolder.Create();

    var store = new GroupFileStore();
    store.OpenFolder(temp.Path);
    var group = store.CreateGroup("History");
    group.Tasks.Add(new TaskItem { Title = "Undo me", Status = TaskStatus.Todo });
    store.SaveGroup(group);

    var viewModel = new MainWindowViewModel(new GroupFileStore(), new UserSettingsStore(Path.Combine(temp.Path, "settings.json")))
    {
        PickFolderAsync = () => Task.FromResult<string?>(temp.Path)
    };

    viewModel.InitializeAsync().GetAwaiter().GetResult();
    var task = viewModel.SelectedGroup!.Tasks[0];
    task.IsDone = true;

    AssertEqual(TaskStatus.Done, task.Model.Status);
    AssertTrue(viewModel.UndoTaskCommand.CanExecute(null), "Undo should be available after task status change.");

    viewModel.UndoTaskCommand.Execute(null);

    AssertEqual(TaskStatus.Todo, task.Model.Status);
    AssertEqual(TaskStatus.Todo, store.Load().Groups[0].Tasks[0].Status);
}

static void RedoReappliesUndoneTaskVersion()
{
    using var temp = TempFolder.Create();
    var (store, viewModel) = CreateHistoryViewModel(temp.Path);

    var task = viewModel.SelectedGroup!.Tasks[0];
    task.IsDone = true;
    viewModel.UndoTaskCommand.Execute(null);

    AssertTrue(viewModel.RedoTaskCommand.CanExecute(null), "Redo should be available after undo.");
    viewModel.RedoTaskCommand.Execute(null);

    AssertEqual(TaskStatus.Done, task.Model.Status);
    AssertEqual(TaskStatus.Done, store.Load().Groups[0].Tasks[0].Status);
}

static void TaskHistoryKeepsLastFiftyActions()
{
    using var temp = TempFolder.Create();
    var (_, viewModel) = CreateHistoryViewModel(temp.Path);

    var task = viewModel.SelectedGroup!.Tasks[0];
    for (var index = 0; index < 51; index++)
    {
        task.IsDone = !task.IsDone;
    }

    for (var index = 0; index < 50; index++)
    {
        AssertTrue(viewModel.UndoTaskCommand.CanExecute(null), "Undo should be available while buffered actions remain.");
        viewModel.UndoTaskCommand.Execute(null);
    }

    AssertTrue(!viewModel.UndoTaskCommand.CanExecute(null), "Undo buffer should contain only the last fifty task actions.");
    AssertEqual(TaskStatus.Done, task.Model.Status);
}

static void CompletedTaskSortingStaysVisual()
{
    var first = new TaskItem { Title = "First", Status = TaskStatus.Todo };
    var middle = new TaskItem { Title = "Middle", Status = TaskStatus.Done };
    var last = new TaskItem { Title = "Last", Status = TaskStatus.Todo };
    var group = new TaskGroup { Name = "Visual order" };
    group.Tasks.AddRange([first, middle, last]);

    var viewModel = new TaskGroupViewModel(group, _ => { });

    AssertTaskOrder(viewModel, "First", "Last", "Middle");
    AssertModelOrder(group, "First", "Middle", "Last");

    viewModel.Tasks.Single(task => task.Model == middle).IsDone = false;

    AssertTaskOrder(viewModel, "First", "Middle", "Last");
    AssertModelOrder(group, "First", "Middle", "Last");
}

static (GroupFileStore Store, MainWindowViewModel ViewModel) CreateHistoryViewModel(string path)
{
    var store = new GroupFileStore();
    store.OpenFolder(path);
    var group = store.CreateGroup("History");
    group.Tasks.Add(new TaskItem { Title = "Undo me", Status = TaskStatus.Todo });
    store.SaveGroup(group);

    var viewModel = new MainWindowViewModel(new GroupFileStore(), new UserSettingsStore(Path.Combine(path, "settings.json")))
    {
        PickFolderAsync = () => Task.FromResult<string?>(path)
    };

    viewModel.InitializeAsync().GetAwaiter().GetResult();
    return (store, viewModel);
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void AssertContains(string expectedPart, string actual)
{
    if (!actual.Contains(expectedPart, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected '{actual}' to contain '{expectedPart}'.");
    }
}

static void AssertTaskOrder(TaskGroupViewModel group, params string[] titles)
{
    AssertSequence(titles, group.Tasks.Select(static task => task.Title));
}

static void AssertModelOrder(TaskGroup group, params string[] titles)
{
    AssertSequence(titles, group.Tasks.Select(static task => task.Title));
}

static void AssertSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    var expectedItems = expected.ToArray();
    var actualItems = actual.ToArray();
    if (!expectedItems.SequenceEqual(actualItems))
    {
        throw new InvalidOperationException(
            $"Expected [{string.Join(", ", expectedItems)}], got [{string.Join(", ", actualItems)}].");
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class TempFolder : IDisposable
{
    private TempFolder(string path)
    {
        Path = path;
        Directory.CreateDirectory(path);
    }

    public string Path { get; }

    public static TempFolder Create()
    {
        return new TempFolder(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FolderTaskTracker.Tests", Guid.NewGuid().ToString("N")));
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, true);
        }
    }
}

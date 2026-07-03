using SkiaSharp;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;
using TaskTracker.App.ViewModels;
using TaskTracker.UiRuntime;
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
    ("completed task sorting stays visual", CompletedTaskSortingStaysVisual),
    ("runtime yoga adapter lays out flex children", RuntimeYogaAdapterLaysOutFlexChildren),
    ("runtime scroll physics converges and clamps", RuntimeScrollPhysicsConvergesAndClamps),
    ("runtime scroll edge space is reserved outside viewport", RuntimeScrollEdgeSpaceIsReservedOutsideViewport),
    ("runtime scroll edge space is not interactive content", RuntimeScrollEdgeSpaceIsNotInteractiveContent),
    ("runtime wheel bubbles from child to scroll view", RuntimeWheelBubblesFromChildToScrollView),
    ("runtime text input handles focus and typing", RuntimeTextInputHandlesFocusAndTyping),
    ("runtime text input handles keyboard shortcuts", RuntimeTextInputHandlesKeyboardShortcuts),
    ("runtime text input consumes editing shortcuts", RuntimeTextInputConsumesEditingShortcuts),
    ("runtime key region receives root shortcuts", RuntimeKeyRegionReceivesRootShortcuts),
    ("runtime used child click stops parent propagation", RuntimeUsedChildClickStopsParentPropagation),
    ("runtime button click survives rebuild between pointer events", RuntimeButtonClickSurvivesRebuildBetweenPointerEvents),
    ("runtime liquid glass modal captures backdrop input", RuntimeLiquidGlassModalCapturesBackdropInput),
    ("runtime shader effect compiles sksl", RuntimeShaderEffectCompilesSksl)
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

static void RuntimeYogaAdapterLaysOutFlexChildren()
{
    var root = new YogaLayoutNode(width: 320, height: 240);
    var fixedChild = new YogaLayoutNode(height: 40);
    var flexChild = new YogaLayoutNode(flexGrow: 1);

    root.Add(fixedChild);
    root.Add(flexChild);
    root.Calculate(320, 240);

    AssertEqual(40f, fixedChild.Layout.Height);
    AssertEqual(200f, flexChild.Layout.Height);
    AssertEqual(40f, flexChild.Layout.Y);
}

static void RuntimeScrollPhysicsConvergesAndClamps()
{
    var physics = new SmoothScrollPhysics { WheelStep = 100, Responsiveness = 20 };
    var metrics = new ScrollMetrics(Viewport: 100, Extent: 320, Offset: 0);

    var target = physics.ApplyWheel(metrics, currentTarget: 0, wheelDelta: -10);
    AssertEqual(220f, target);

    var current = 0f;
    for (var index = 0; index < 40; index++)
    {
        current = physics.Step(metrics, current, target, 1.0 / 60.0);
    }

    AssertTrue(current > 200, "Smooth scroll should converge toward the target.");
    AssertEqual(0f, physics.ApplyWheel(metrics, currentTarget: 0, wheelDelta: 10));
}

static void RuntimeScrollEdgeSpaceIsReservedOutsideViewport()
{
    using var temp = TempFolder.Create();
    var controller = new ScrollController();
    using var window = new UiWindow(new UiWindowOptions("scroll reserved edge test", 240, 100));
    window.SetRoot(new ScrollView(
        new ListView<int>(
            Enumerable.Range(0, 4),
            index => new Button($"Row {index}", () => { }),
            spacing: 4),
        controller,
        new ScrollBehavior(
            new SmoothScrollPhysics { WheelStep = 100 },
            new ScrollEdgeShaderEffect(edgeSize: 20, bendStrength: 0.4f))));

    window.RenderToPng(Path.Combine(temp.Path, "scroll-reserved-edge.png"), 240, 100);
    window.SendWheel(20, 50, -100);

    AssertEqual(72f, controller.TargetOffset);
}

static void RuntimeScrollEdgeSpaceIsNotInteractiveContent()
{
    using var temp = TempFolder.Create();
    var clicks = 0;
    using var window = new UiWindow(new UiWindowOptions("scroll edge hit test", 240, 100));
    window.SetRoot(new ScrollView(
        new ListView<int>(
            Enumerable.Range(0, 4),
            index => new Button($"Row {index}", () => clicks++),
            spacing: 4),
        behavior: new ScrollBehavior(
            new SmoothScrollPhysics(),
            new ScrollEdgeShaderEffect(edgeSize: 20, bendStrength: 0.4f))));

    window.RenderToPng(Path.Combine(temp.Path, "scroll-edge-hit-test.png"), 240, 100);
    window.SendPointerDown(20, 30);
    window.SendPointerUp(20, 30);
    window.SendPointerDown(20, 90);
    window.SendPointerUp(20, 90);

    AssertEqual(1, clicks);
}

static void RuntimeWheelBubblesFromChildToScrollView()
{
    using var temp = TempFolder.Create();
    var controller = new ScrollController();
    using var window = new UiWindow(new UiWindowOptions("scroll test", 240, 100));
    window.SetRoot(new ScrollView(
        new ListView<int>(
            Enumerable.Range(0, 12),
            index => new Button($"Row {index}", () => { }),
            spacing: 4),
        controller,
        new ScrollBehavior(new SmoothScrollPhysics { WheelStep = 40 })));

    window.RenderToPng(Path.Combine(temp.Path, "scroll-before.png"), 240, 100);
    window.SendWheel(20, 20, -1);

    AssertTrue(controller.TargetOffset > 0, "Wheel over a child button should scroll its parent scroll view.");
}

static void RuntimeTextInputHandlesFocusAndTyping()
{
    using var temp = TempFolder.Create();
    var controller = new TextInputController();
    using var window = new UiWindow(new UiWindowOptions("input test", 240, 80));
    window.SetRoot(new Box(
        padding: new UiThickness(8),
        child: new TextInput(controller)));

    window.RenderToPng(Path.Combine(temp.Path, "input.png"), 240, 80);
    window.SendPointerDown(20, 20);
    window.SendTextInput("abc");
    window.SendKeyDown(UiKey.Backspace);

    AssertEqual("ab", controller.Text);
}

static void RuntimeTextInputHandlesKeyboardShortcuts()
{
    using var temp = TempFolder.Create();
    var controller = new TextInputController();
    using var window = new UiWindow(new UiWindowOptions("input shortcuts test", 240, 80));
    window.SetRoot(new Box(
        padding: new UiThickness(8),
        child: new TextInput(controller)));

    window.RenderToPng(Path.Combine(temp.Path, "input-shortcuts.png"), 240, 80);
    window.SendPointerDown(20, 20);
    window.SendTextInput("abc");
    window.SendKeyDown(UiKey.Z, UiKeyModifiers.Control);
    AssertEqual("", controller.Text);

    window.SendKeyDown(UiKey.Y, UiKeyModifiers.Control);
    AssertEqual("abc", controller.Text);

    window.SendKeyDown(UiKey.A, UiKeyModifiers.Control);
    window.SendTextInput("x");
    AssertEqual("x", controller.Text);
}

static void RuntimeTextInputConsumesEditingShortcuts()
{
    using var temp = TempFolder.Create();
    var controller = new TextInputController();
    var rootUndoCount = 0;
    using var window = new UiWindow(new UiWindowOptions("input shortcut routing test", 240, 80));
    window.SetRoot(new KeyRegion(
        new Box(
            padding: new UiThickness(8),
            child: new TextInput(controller)),
        key =>
        {
            if (key.Key is UiKey.Z && key.HasControl)
            {
                rootUndoCount++;
            }
        }));

    window.RenderToPng(Path.Combine(temp.Path, "input-shortcut-routing.png"), 240, 80);
    window.SendPointerDown(20, 20);
    window.SendTextInput("abc");
    window.SendKeyDown(UiKey.Z, UiKeyModifiers.Control);

    AssertEqual("", controller.Text);
    AssertEqual(0, rootUndoCount);
}

static void RuntimeKeyRegionReceivesRootShortcuts()
{
    using var temp = TempFolder.Create();
    var shortcutCount = 0;
    using var window = new UiWindow(new UiWindowOptions("key region test", 240, 80));
    window.SetRoot(new KeyRegion(
        new Box(width: 240, height: 80),
        key =>
        {
            if (key.Key is UiKey.Z && key.HasControl)
            {
                shortcutCount++;
                key.Use();
            }
        }));

    window.RenderToPng(Path.Combine(temp.Path, "key-region.png"), 240, 80);
    window.SendKeyDown(UiKey.Z, UiKeyModifiers.Control);

    AssertEqual(1, shortcutCount);
}

static void RuntimeUsedChildClickStopsParentPropagation()
{
    using var temp = TempFolder.Create();
    var childClicks = 0;
    var parentClicks = 0;
    using var window = new UiWindow(new UiWindowOptions("event propagation test", 240, 80));
    window.SetRoot(new TapRegion(
        new Box(
            padding: new UiThickness(8),
            child: new Button("Child", () => childClicks++)),
        () => parentClicks++));

    window.RenderToPng(Path.Combine(temp.Path, "event-propagation.png"), 240, 80);
    window.SendPointerDown(20, 20);
    window.SendPointerUp(20, 20);

    AssertEqual(1, childClicks);
    AssertEqual(0, parentClicks);
}

static void RuntimeButtonClickSurvivesRebuildBetweenPointerEvents()
{
    using var temp = TempFolder.Create();
    var clicks = 0;
    using var window = new UiWindow(new UiWindowOptions("button test", 240, 80));
    window.SetRoot(new Box(
        padding: new UiThickness(8),
        child: new Button("Click", () => clicks++)));

    window.RenderToPng(Path.Combine(temp.Path, "button-before.png"), 240, 80);
    window.SendPointerDown(20, 20);
    window.RenderToPng(Path.Combine(temp.Path, "button-rebuilt.png"), 240, 80);
    window.SendPointerUp(20, 20);

    AssertEqual(1, clicks);
}

static void RuntimeLiquidGlassModalCapturesBackdropInput()
{
    using var temp = TempFolder.Create();
    var backdropClicks = 0;
    var dialogClicks = 0;
    using var window = new UiWindow(new UiWindowOptions("liquid glass modal test", 240, 160));
    window.SetRoot(new LiquidGlassModal(
        new TapRegion(
            new Box(background: SKColor.Parse("#DFF1FF")),
            () => backdropClicks++),
        new Button("OK", () => dialogClicks++),
        new LiquidGlassStyle
        {
            MaxWidth = 120,
            MaxHeight = 80,
            Margin = 12,
            Padding = new UiThickness(14),
            BlurPasses = 1
        }));

    window.RenderToPng(Path.Combine(temp.Path, "liquid-glass-modal.png"), 240, 160);
    window.SendPointerDown(8, 8);
    window.SendPointerUp(8, 8);
    window.SendPointerDown(120, 80);
    window.SendPointerUp(120, 80);

    AssertEqual(0, backdropClicks);
    AssertEqual(1, dialogClicks);
}

static void RuntimeShaderEffectCompilesSksl()
{
    using var effect = new RuntimeShaderEffect("""
half4 main(float2 coord) {
    return half4(coord.x * 0.0 + 1.0, 1.0, 1.0, 1.0);
}
""");

    effect.Set("unused", 1f);
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

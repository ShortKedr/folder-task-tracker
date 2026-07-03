using System.Collections.ObjectModel;
using SkiaSharp;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace TaskTracker.UiRuntime;

public readonly record struct UiPoint(float X, float Y);

public readonly record struct UiSize(float Width, float Height)
{
    public static UiSize Zero => new(0, 0);
}

public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public float Left => X;
    public float Top => Y;
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public SKRect ToSkRect() => new(X, Y, Right, Bottom);
    public bool Contains(UiPoint point) => point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
}

public readonly record struct UiThickness(float Left, float Top, float Right, float Bottom)
{
    public UiThickness(float all)
        : this(all, all, all, all)
    {
    }

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
}

public sealed record UiWindowOptions(string Title, int Width, int Height, int MinWidth = 360, int MinHeight = 240);

public enum Axis
{
    Horizontal,
    Vertical
}

public enum CrossAxisAlignment
{
    Start,
    Center,
    Stretch,
    End
}

public enum MainAxisAlignment
{
    Start,
    Center,
    End,
    SpaceBetween
}

public enum UiKey
{
    Unknown,
    Backspace,
    Delete,
    Enter,
    Escape,
    Tab,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    Z,
    A,
    C,
    V,
    X,
    Y
}

[Flags]
public enum UiKeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4
}

public enum UiEventPhase
{
    BubbleDown,
    BubbleUp
}

public abstract class UiEvent
{
    public bool IsUsed { get; private set; }
    public UiEventPhase Phase { get; internal set; }
    public RenderNode? Target { get; internal set; }
    public RenderNode? CurrentTarget { get; internal set; }

    public void Use()
    {
        IsUsed = true;
    }
}

public sealed class PointerEvent : UiEvent
{
    public PointerEvent(UiPoint position)
    {
        Position = position;
    }

    public UiPoint Position { get; }
}

public sealed class WheelEvent : UiEvent
{
    public WheelEvent(UiPoint position, float deltaX, float deltaY)
    {
        Position = position;
        DeltaX = deltaX;
        DeltaY = deltaY;
    }

    public UiPoint Position { get; }
    public float DeltaX { get; }
    public float DeltaY { get; }
}

public sealed class KeyEvent : UiEvent
{
    private readonly Action<string>? _setClipboardText;

    public KeyEvent(UiKey key, UiKeyModifiers modifiers, string? clipboardText = null, Action<string>? setClipboardText = null)
    {
        Key = key;
        Modifiers = modifiers;
        ClipboardText = clipboardText;
        _setClipboardText = setClipboardText;
    }

    public UiKey Key { get; }
    public UiKeyModifiers Modifiers { get; }
    public string? ClipboardText { get; }

    public bool HasControl => Modifiers.HasFlag(UiKeyModifiers.Control);
    public bool HasShift => Modifiers.HasFlag(UiKeyModifiers.Shift);

    public void SetClipboardText(string text)
    {
        _setClipboardText?.Invoke(text);
    }
}

public sealed class TextInputEvent : UiEvent
{
    public TextInputEvent(string text)
    {
        Text = text;
    }

    public string Text { get; }
}

public sealed class UiTheme
{
    public SKColor WindowBackground { get; init; } = SKColor.Parse("#EFF8FF");
    public SKColor PanelBackground { get; init; } = SKColor.Parse("#FBFDFF");
    public SKColor AccentSoft { get; init; } = SKColor.Parse("#DFF1FF");
    public SKColor Accent { get; init; } = SKColor.Parse("#8CCAF3");
    public SKColor Text { get; init; } = SKColor.Parse("#173B57");
    public SKColor MutedText { get; init; } = SKColor.Parse("#426276");
    public SKColor Error { get; init; } = SKColor.Parse("#B42318");
    public string FontFamily { get; init; } = "Segoe UI";
    public float FontSize { get; init; } = 13;
}

public sealed class BuildContext
{
    internal BuildContext(UiWindow window, StateRegistry stateRegistry)
    {
        Window = window;
        StateRegistry = stateRegistry;
    }

    public UiWindow Window { get; }
    public UiTheme Theme => Window.Theme;
    internal StateRegistry StateRegistry { get; }
}

public abstract class Widget
{
    protected Widget(string? key = null)
    {
        Key = key;
    }

    public string? Key { get; }
    internal abstract RenderNode CreateRenderNode(BuildContext context);
}

public abstract class StatelessWidget : Widget
{
    protected StatelessWidget(string? key = null)
        : base(key)
    {
    }

    public abstract Widget Build(BuildContext context);

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        return Build(context).CreateRenderNode(context);
    }
}

public abstract class StatefulWidget : Widget
{
    protected StatefulWidget(string? key = null)
        : base(key)
    {
    }

    public abstract State CreateState();

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        var state = context.StateRegistry.GetOrCreate(this);
        state.Attach(context.Window, this);
        return state.Build(context).CreateRenderNode(context);
    }
}

public abstract class State
{
    private UiWindow? _window;

    public void SetState(Action? mutate = null)
    {
        mutate?.Invoke();
        _window?.RequestBuild();
    }

    public virtual void InitState()
    {
    }

    public abstract Widget Build(BuildContext context);

    internal void Attach(UiWindow window, StatefulWidget widget)
    {
        if (_window is null)
        {
            _window = window;
            InitState();
        }
    }
}

internal sealed class StateRegistry
{
    private readonly Dictionary<string, State> _states = new();

    public State GetOrCreate(StatefulWidget widget)
    {
        var key = widget.Key ?? widget.GetType().FullName ?? widget.GetType().Name;
        if (_states.TryGetValue(key, out var state))
        {
            return state;
        }

        state = widget.CreateState();
        _states[key] = state;
        return state;
    }
}

public abstract class RenderNode
{
    private readonly List<RenderNode> _children = new();
    private RenderNode? _parent;

    public IReadOnlyList<RenderNode> Children => _children;
    internal RenderNode? Parent => _parent;
    public UiRect Bounds { get; private set; }
    public bool ClipToBounds { get; init; }
    public bool IsFocusable { get; init; }
    public string? DebugName { get; init; }
    public Action<PointerEvent>? BubbleDownPointerDown { get; init; }
    public Action<PointerEvent>? BubbleDownPointerUp { get; init; }
    public Action<PointerEvent>? BubbleDownPointerMove { get; init; }
    public Action<WheelEvent>? BubbleDownWheel { get; init; }
    public Action<KeyEvent>? BubbleDownKeyDown { get; init; }
    public Action<TextInputEvent>? BubbleDownTextInput { get; init; }
    public Action<PointerEvent>? PointerDown { get; init; }
    public Action<PointerEvent>? PointerUp { get; init; }
    public Action<PointerEvent>? PointerMove { get; init; }
    public Action<WheelEvent>? Wheel { get; init; }
    public Action<KeyEvent>? KeyDown { get; init; }
    public Action<TextInputEvent>? TextInput { get; init; }
    public Func<double, bool>? Tick { get; init; }

    public void Add(RenderNode child)
    {
        child._parent = this;
        _children.Add(child);
    }

    public virtual UiSize Measure(SKCanvas canvas, UiSize available)
    {
        var width = 0f;
        var height = 0f;
        foreach (var child in _children)
        {
            var childSize = child.Measure(canvas, available);
            width = Math.Max(width, childSize.Width);
            height = Math.Max(height, childSize.Height);
        }

        return new UiSize(width, height);
    }

    public virtual void Arrange(UiRect bounds)
    {
        Bounds = bounds;
        foreach (var child in _children)
        {
            child.Arrange(bounds);
        }
    }

    public void PaintTree(SKCanvas canvas)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var restore = 0;
        if (ClipToBounds)
        {
            restore = canvas.Save();
            canvas.ClipRect(Bounds.ToSkRect(), SKClipOperation.Intersect, antialias: true);
        }

        Paint(canvas);

        if (restore != 0)
        {
            canvas.RestoreToCount(restore);
        }
    }

    public virtual void Paint(SKCanvas canvas)
    {
        foreach (var child in _children)
        {
            child.PaintTree(canvas);
        }
    }

    public virtual RenderNode? HitTest(UiPoint point)
    {
        if (!Bounds.Contains(point))
        {
            return null;
        }

        for (var index = _children.Count - 1; index >= 0; index--)
        {
            var hit = _children[index].HitTest(point);
            if (hit is not null)
            {
                return hit;
            }
        }

        return HasEventHandler || IsFocusable ? this : null;
    }

    private bool HasEventHandler =>
        BubbleDownPointerDown is not null ||
        BubbleDownPointerUp is not null ||
        BubbleDownPointerMove is not null ||
        BubbleDownWheel is not null ||
        BubbleDownKeyDown is not null ||
        BubbleDownTextInput is not null ||
        PointerDown is not null ||
        PointerUp is not null ||
        PointerMove is not null ||
        Wheel is not null ||
        KeyDown is not null ||
        TextInput is not null;

    public bool TickTree(double deltaSeconds)
    {
        var changed = Tick?.Invoke(deltaSeconds) ?? false;
        foreach (var child in _children)
        {
            changed |= child.TickTree(deltaSeconds);
        }

        return changed;
    }
}

public sealed class UiWindow : IDisposable
{
    private readonly StateRegistry _stateRegistry = new();
    private IWindow? _silkWindow;
    private IInputContext? _inputContext;
    private GL? _gl;
    private GRContext? _grContext;
    private RenderNode? _rootNode;
    private Widget? _rootWidget;
    private RenderNode? _capturedPointerNode;
    private bool _needsBuild = true;
    private bool _topMost;

    public UiWindow(UiWindowOptions options)
    {
        Options = options;
    }

    public UiWindowOptions Options { get; }
    public UiTheme Theme { get; set; } = new();
    public RenderNode? FocusedNode { get; private set; }
    public bool TopMost
    {
        get => _topMost;
        set
        {
            _topMost = value;
            if (_silkWindow is not null)
            {
                _silkWindow.TopMost = value;
            }
        }
    }

    public void SetRoot(Widget root)
    {
        _rootWidget = root;
        RequestBuild();
    }

    public void Invalidate()
    {
        RequestBuild();
    }

    internal void RequestBuild()
    {
        _needsBuild = true;
        RequestPaint();
    }

    private void RequestPaint()
    {
        _silkWindow?.ContinueEvents();
    }

    public void Run()
    {
        var refreshRate = DetectDisplayRefreshRate();
        var options = WindowOptions.Default;
        options.Title = Options.Title;
        options.Size = new Vector2D<int>(Options.Width, Options.Height);
        options.TopMost = _topMost;
        options.IsEventDriven = false;
        options.FramesPerSecond = refreshRate;
        options.UpdatesPerSecond = refreshRate;
        options.VSync = true;
        options.PreferredDepthBufferBits = 0;
        options.PreferredStencilBufferBits = 8;
        _silkWindow = Window.Create(options);
        _silkWindow.Load += SetupRenderer;
        _silkWindow.Load += SetupInput;
        _silkWindow.Render += RenderFrame;
        _silkWindow.Update += UpdateFrame;
        _silkWindow.Run();
    }

    public static double DetectDisplayRefreshRate()
    {
        try
        {
            var refreshRate = Silk.NET.Windowing.Monitor.GetMainMonitor(null!).VideoMode.RefreshRate ?? 0;
            return refreshRate > 0 ? refreshRate : 60;
        }
        catch
        {
            return 60;
        }
    }

    public void RenderToPng(string path, int width, int height)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        RenderToCanvas(surface.Canvas, width, height, forceBuild: true);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 92);
        using var stream = File.Open(path, FileMode.Create, FileAccess.Write);
        data.SaveTo(stream);
    }

    public void SendPointerDown(float x, float y)
    {
        var point = new UiPoint(x, y);
        var node = _rootNode?.HitTest(point);
        _capturedPointerNode = node;
        if (node is null)
        {
            return;
        }

        if (node.IsFocusable)
        {
            FocusedNode = node;
        }

        DispatchEvent(node, new PointerEvent(point), static item => item.BubbleDownPointerDown, static item => item.PointerDown);
        RequestPaint();
    }

    public void SendPointerUp(float x, float y)
    {
        var point = new UiPoint(x, y);
        var node = _capturedPointerNode ?? _rootNode?.HitTest(point);
        _capturedPointerNode = null;
        if (node is not null)
        {
            DispatchEvent(node, new PointerEvent(point), static item => item.BubbleDownPointerUp, static item => item.PointerUp);
        }

        RequestPaint();
    }

    public void SendWheel(float x, float y, float deltaY)
    {
        var point = new UiPoint(x, y);
        var node = _rootNode?.HitTest(point);
        if (node is not null)
        {
            DispatchEvent(node, new WheelEvent(point, 0, deltaY), static item => item.BubbleDownWheel, static item => item.Wheel);
        }

        RequestPaint();
    }

    public void SendKeyDown(UiKey key, UiKeyModifiers modifiers = UiKeyModifiers.None)
    {
        SendKeyDown(key, modifiers, clipboardText: null, setClipboardText: null);
    }

    private void SendKeyDown(UiKey key, UiKeyModifiers modifiers, string? clipboardText, Action<string>? setClipboardText)
    {
        var uiEvent = new KeyEvent(key, modifiers, clipboardText, setClipboardText);
        if (FocusedNode is not null)
        {
            DispatchEvent(FocusedNode, uiEvent, static item => item.BubbleDownKeyDown, static item => item.KeyDown);
        }
        else if (_rootNode is not null)
        {
            DispatchEvent(_rootNode, uiEvent, static item => item.BubbleDownKeyDown, static item => item.KeyDown);
        }

        RequestPaint();
    }

    public void SendTextInput(string text)
    {
        if (FocusedNode is not null)
        {
            DispatchEvent(FocusedNode, new TextInputEvent(text), static item => item.BubbleDownTextInput, static item => item.TextInput);
        }

        RequestPaint();
    }

    public void Dispose()
    {
        _grContext?.Dispose();
        _inputContext?.Dispose();
        _silkWindow?.Dispose();
    }

    private void SetupRenderer()
    {
        if (_silkWindow is null)
        {
            return;
        }

        _gl = GL.GetApi(_silkWindow);
        _grContext = GRContext.CreateGl();
    }

    private void SetupInput()
    {
        if (_silkWindow is null)
        {
            return;
        }

        _inputContext = _silkWindow.CreateInput();
        foreach (var mouse in _inputContext.Mice)
        {
            mouse.MouseDown += (_, _) => SendPointerDown(mouse.Position.X, mouse.Position.Y);
            mouse.MouseUp += (_, _) => SendPointerUp(mouse.Position.X, mouse.Position.Y);
            mouse.Scroll += (_, wheel) => SendWheel(mouse.Position.X, mouse.Position.Y, wheel.Y);
        }

        foreach (var keyboard in _inputContext.Keyboards)
        {
            keyboard.BeginInput();
            keyboard.KeyDown += (_, key, _) => SendKeyDown(
                MapKey(key),
                ReadModifiers(keyboard),
                keyboard.ClipboardText,
                text => keyboard.ClipboardText = text);
            keyboard.KeyChar += (_, value) => SendTextInput(value.ToString());
        }
    }

    private static UiKey MapKey(Key key)
    {
        return key switch
        {
            Key.Backspace => UiKey.Backspace,
            Key.Delete => UiKey.Delete,
            Key.Enter => UiKey.Enter,
            Key.Escape => UiKey.Escape,
            Key.Tab => UiKey.Tab,
            Key.Left => UiKey.Left,
            Key.Right => UiKey.Right,
            Key.Up => UiKey.Up,
            Key.Down => UiKey.Down,
            Key.Home => UiKey.Home,
            Key.End => UiKey.End,
            Key.Z => UiKey.Z,
            Key.A => UiKey.A,
            Key.C => UiKey.C,
            Key.V => UiKey.V,
            Key.X => UiKey.X,
            Key.Y => UiKey.Y,
            _ => UiKey.Unknown
        };
    }

    private static UiKeyModifiers ReadModifiers(IKeyboard keyboard)
    {
        var result = UiKeyModifiers.None;
        if (keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight))
        {
            result |= UiKeyModifiers.Shift;
        }

        if (keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight))
        {
            result |= UiKeyModifiers.Control;
        }

        if (keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight))
        {
            result |= UiKeyModifiers.Alt;
        }

        return result;
    }

    private static void DispatchEvent<TEvent>(
        RenderNode target,
        TEvent uiEvent,
        Func<RenderNode, Action<TEvent>?> bubbleDown,
        Func<RenderNode, Action<TEvent>?> bubbleUp)
        where TEvent : UiEvent
    {
        var route = BuildRoute(target);
        uiEvent.Target = target;

        foreach (var node in route)
        {
            uiEvent.CurrentTarget = node;
            uiEvent.Phase = UiEventPhase.BubbleDown;
            bubbleDown(node)?.Invoke(uiEvent);
            if (uiEvent.IsUsed)
            {
                return;
            }
        }

        for (var index = route.Count - 1; index >= 0; index--)
        {
            var node = route[index];
            uiEvent.CurrentTarget = node;
            uiEvent.Phase = UiEventPhase.BubbleUp;
            bubbleUp(node)?.Invoke(uiEvent);
            if (uiEvent.IsUsed)
            {
                return;
            }
        }
    }

    private static List<RenderNode> BuildRoute(RenderNode target)
    {
        var route = new List<RenderNode>();
        for (var node = target; node is not null; node = node.Parent)
        {
            route.Add(node);
        }

        route.Reverse();
        return route;
    }

    private void UpdateFrame(double deltaSeconds)
    {
        if (_rootNode?.TickTree(deltaSeconds) == true)
        {
            RequestPaint();
        }
    }

    private void RenderFrame(double deltaSeconds)
    {
        if (_silkWindow is null)
        {
            return;
        }

        var width = Math.Max(1, _silkWindow.Size.X);
        var height = Math.Max(1, _silkWindow.Size.Y);
        using var surface = CreateGpuSurface(width, height) ??
                            SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        RenderToCanvas(surface.Canvas, width, height, forceBuild: _needsBuild);
        surface.Canvas.Flush();
        _grContext?.Flush();
    }

    private SKSurface? CreateGpuSurface(int width, int height)
    {
        if (_gl is null || _grContext is null)
        {
            return null;
        }

        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.GetInteger(GLEnum.FramebufferBinding, out var framebuffer);

        var framebufferInfo = new GRGlFramebufferInfo((uint)framebuffer, (uint)GLEnum.Rgba8);
        using var renderTarget = new GRBackendRenderTarget(width, height, 0, 8, framebufferInfo);
        return SKSurface.Create(_grContext, renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);
    }

    private void RenderToCanvas(SKCanvas canvas, int width, int height, bool forceBuild)
    {
        canvas.Clear(Theme.WindowBackground);
        if (_rootWidget is null)
        {
            return;
        }

        if (forceBuild || _rootNode is null)
        {
            _needsBuild = false;
            _rootNode = _rootWidget.CreateRenderNode(new BuildContext(this, _stateRegistry));
        }

        _rootNode.Measure(canvas, new UiSize(width, height));
        _rootNode.Arrange(new UiRect(0, 0, width, height));
        _rootNode.PaintTree(canvas);
    }
}

public sealed record TextStyle(float Size, SKColor Color, bool Bold = false);

public sealed class Text : Widget
{
    public Text(string value, TextStyle? style = null, string? key = null)
        : base(key)
    {
        Value = value;
        Style = style;
    }

    public string Value { get; }
    public TextStyle? Style { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        return new TextRenderNode(Value, Style ?? new TextStyle(context.Theme.FontSize, context.Theme.Text));
    }
}

internal sealed class TextRenderNode : RenderNode
{
    private readonly string _text;
    private readonly TextStyle _style;

    public TextRenderNode(string text, TextStyle style)
    {
        _text = text;
        _style = style;
    }

    public override UiSize Measure(SKCanvas canvas, UiSize available)
    {
        using var paint = CreatePaint();
        using var font = CreateFont();
        var width = Math.Min(available.Width, Math.Max(1, font.MeasureText(_text, paint)));
        return new UiSize(width, _style.Size * 1.35f);
    }

    public override void Paint(SKCanvas canvas)
    {
        using var paint = CreatePaint();
        using var font = CreateFont();
        canvas.DrawText(_text, Bounds.X, Bounds.Y + _style.Size, SKTextAlign.Left, font, paint);
    }

    private SKPaint CreatePaint()
    {
        return new SKPaint
        {
            Color = _style.Color,
            IsAntialias = true
        };
    }

    private SKFont CreateFont()
    {
        return new SKFont
        {
            Size = _style.Size,
            Typeface = SKTypeface.FromFamilyName("Segoe UI", _style.Bold ? SKFontStyle.Bold : SKFontStyle.Normal)
        };
    }
}

public sealed class Box : Widget
{
    public Box(
        Widget? child = null,
        SKColor? background = null,
        SKColor? border = null,
        float borderWidth = 0,
        float cornerRadius = 0,
        UiThickness? padding = null,
        float? width = null,
        float? height = null,
        bool expand = false,
        string? key = null)
        : base(key)
    {
        Child = child;
        Background = background;
        Border = border;
        BorderWidth = borderWidth;
        CornerRadius = cornerRadius;
        Padding = padding ?? new UiThickness(0);
        Width = width;
        Height = height;
        Expand = expand;
    }

    public Widget? Child { get; }
    public SKColor? Background { get; }
    public SKColor? Border { get; }
    public float BorderWidth { get; }
    public float CornerRadius { get; }
    public UiThickness Padding { get; }
    public float? Width { get; }
    public float? Height { get; }
    public bool Expand { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        var node = new BoxRenderNode(Background, Border, BorderWidth, CornerRadius, Padding, Width, Height, Expand);
        if (Child is not null)
        {
            node.Add(Child.CreateRenderNode(context));
        }

        return node;
    }
}

public sealed class LiquidGlassStyle
{
    public static LiquidGlassStyle Dialog { get; } = new();

    public float MaxWidth { get; init; } = 520;
    public float MaxHeight { get; init; } = 420;
    public float Margin { get; init; } = 24;
    public float CornerRadius { get; init; } = 14;
    public UiThickness Padding { get; init; } = new(16);
    public int BlurPasses { get; init; } = 2;
    public SKColor OverlayTint { get; init; } = SKColors.Black.WithAlpha(58);
    public SKColor GlassTint { get; init; } = SKColor.Parse("#F8FCFF").WithAlpha(188);
    public SKColor GlassStroke { get; init; } = SKColors.White.WithAlpha(178);
    public SKColor EdgeStroke { get; init; } = SKColor.Parse("#8CCAF3").WithAlpha(150);
}

public sealed class LiquidGlassModal : Widget
{
    public LiquidGlassModal(Widget backdrop, Widget content, LiquidGlassStyle? style = null, string? key = null)
        : base(key)
    {
        Backdrop = backdrop;
        Content = content;
        Style = style ?? LiquidGlassStyle.Dialog;
    }

    public Widget Backdrop { get; }
    public Widget Content { get; }
    public LiquidGlassStyle Style { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        var node = new LiquidGlassModalRenderNode(Style);
        node.Add(Backdrop.CreateRenderNode(context));
        node.Add(Content.CreateRenderNode(context));
        return node;
    }
}

internal sealed class LiquidGlassModalRenderNode : RenderNode
{
    private readonly LiquidGlassStyle _style;
    private UiRect _panelBounds;
    private UiRect _contentBounds;

    public LiquidGlassModalRenderNode(LiquidGlassStyle style)
    {
        _style = style;
        PointerDown = pointer => pointer.Use();
        PointerUp = pointer => pointer.Use();
        Wheel = wheel => wheel.Use();
    }

    public override UiSize Measure(SKCanvas canvas, UiSize available)
    {
        Children[0].Measure(canvas, available);
        _panelBounds = ResolvePanelBounds(new UiRect(0, 0, available.Width, available.Height));
        var contentAvailable = new UiSize(
            Math.Max(0, _panelBounds.Width - _style.Padding.Horizontal),
            Math.Max(0, _panelBounds.Height - _style.Padding.Vertical));
        Children[1].Measure(canvas, contentAvailable);
        return available;
    }

    public override void Arrange(UiRect bounds)
    {
        base.Arrange(bounds);
        Children[0].Arrange(bounds);
        _panelBounds = ResolvePanelBounds(bounds);
        _contentBounds = new UiRect(
            _panelBounds.X + _style.Padding.Left,
            _panelBounds.Y + _style.Padding.Top,
            Math.Max(0, _panelBounds.Width - _style.Padding.Horizontal),
            Math.Max(0, _panelBounds.Height - _style.Padding.Vertical));
        Children[1].Arrange(_contentBounds);
    }

    public override void Paint(SKCanvas canvas)
    {
        var width = Math.Max(1, (int)MathF.Ceiling(Bounds.Width));
        var height = Math.Max(1, (int)MathF.Ceiling(Bounds.Height));
        using var backdropSurface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (backdropSurface is null)
        {
            Children[0].PaintTree(canvas);
            PaintGlassPanel(canvas, null);
            return;
        }

        backdropSurface.Canvas.Clear(SKColors.Transparent);
        backdropSurface.Canvas.Translate(-Bounds.X, -Bounds.Y);
        Children[0].PaintTree(backdropSurface.Canvas);
        backdropSurface.Canvas.Flush();

        using var backdrop = backdropSurface.Snapshot();
        using var backdropPaint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(backdrop, Bounds.ToSkRect(), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), backdropPaint);

        if (_style.OverlayTint.Alpha > 0)
        {
            using var overlayPaint = new SKPaint { Color = _style.OverlayTint, IsAntialias = true };
            canvas.DrawRect(Bounds.ToSkRect(), overlayPaint);
        }

        PaintGlassPanel(canvas, backdrop);
    }

    public override RenderNode? HitTest(UiPoint point)
    {
        if (!Bounds.Contains(point))
        {
            return null;
        }

        var contentHit = Children[1].HitTest(point);
        return contentHit ?? this;
    }

    private UiRect ResolvePanelBounds(UiRect bounds)
    {
        var margin = Math.Max(0, _style.Margin);
        var width = Math.Max(1, Math.Min(_style.MaxWidth, Math.Max(1, bounds.Width - margin * 2)));
        var height = Math.Max(1, Math.Min(_style.MaxHeight, Math.Max(1, bounds.Height - margin * 2)));
        return new UiRect(
            bounds.X + (bounds.Width - width) / 2f,
            bounds.Y + (bounds.Height - height) / 2f,
            width,
            height);
    }

    private void PaintGlassPanel(SKCanvas canvas, SKImage? backdrop)
    {
        var panel = _panelBounds.ToSkRect();
        var save = canvas.Save();
        using var panelRoundRect = new SKRoundRect(panel, _style.CornerRadius, _style.CornerRadius);
        canvas.ClipRoundRect(panelRoundRect, SKClipOperation.Intersect, antialias: true);

        if (backdrop is not null)
        {
            DrawDualKawaseBlur(canvas, backdrop, panel, _style.BlurPasses);
        }

        using var tintPaint = new SKPaint { Color = _style.GlassTint, IsAntialias = true };
        canvas.DrawRect(panel, tintPaint);

        using var shinePaint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(panel.Left, panel.Top),
                new SKPoint(panel.Right, panel.Bottom),
                new[] { SKColors.White.WithAlpha(116), SKColors.White.WithAlpha(18), SKColor.Parse("#DFF1FF").WithAlpha(54) },
                new[] { 0f, 0.48f, 1f },
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(panel, shinePaint);
        Children[1].PaintTree(canvas);
        canvas.RestoreToCount(save);

        using var strokePaint = new SKPaint
        {
            Color = _style.GlassStroke,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.2f
        };
        canvas.DrawRoundRect(panel, _style.CornerRadius, _style.CornerRadius, strokePaint);

        var innerPanel = panel;
        innerPanel.Inflate(-1.2f, -1.2f);
        using var edgePaint = new SKPaint
        {
            Color = _style.EdgeStroke,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };
        canvas.DrawRoundRect(innerPanel, Math.Max(0, _style.CornerRadius - 1.2f), Math.Max(0, _style.CornerRadius - 1.2f), edgePaint);
    }

    private static void DrawDualKawaseBlur(SKCanvas canvas, SKImage source, SKRect sourceRect, int passes)
    {
        var width = Math.Max(1, (int)MathF.Ceiling(sourceRect.Width));
        var height = Math.Max(1, (int)MathF.Ceiling(sourceRect.Height));
        var passCount = Math.Clamp(passes, 1, 3);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

        using var firstSurface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (firstSurface is null)
        {
            return;
        }

        firstSurface.Canvas.Clear(SKColors.Transparent);
        firstSurface.Canvas.DrawImage(source, sourceRect, new SKRect(0, 0, width, height), sampling);
        firstSurface.Canvas.Flush();

        using var firstImage = firstSurface.Snapshot();
        SKImage current = firstImage;
        for (var pass = 0; pass < passCount; pass++)
        {
            var nextWidth = Math.Max(1, width >> (pass + 1));
            var nextHeight = Math.Max(1, height >> (pass + 1));
            var next = ResampleKawase(current, nextWidth, nextHeight, sampling, pass + 0.65f);
            if (!ReferenceEquals(next, current) && !ReferenceEquals(current, firstImage))
            {
                current.Dispose();
            }

            current = next;
        }

        for (var index = passCount - 1; index >= 0; index--)
        {
            var targetWidth = Math.Max(1, width >> index);
            var targetHeight = Math.Max(1, height >> index);
            var next = ResampleKawase(current, targetWidth, targetHeight, sampling, index + 0.85f);
            if (!ReferenceEquals(next, current) && !ReferenceEquals(current, firstImage))
            {
                current.Dispose();
            }

            current = next;
        }

        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(current, new SKRect(0, 0, current.Width, current.Height), sourceRect, sampling, paint);
        if (!ReferenceEquals(current, firstImage))
        {
            current.Dispose();
        }
    }

    private static SKImage ResampleKawase(SKImage image, int width, int height, SKSamplingOptions sampling, float offset)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
        {
            return image;
        }

        surface.Canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(64) };
        var destination = new SKRect(0, 0, width, height);
        var taps = new[]
        {
            new SKRect(-offset, -offset, image.Width - offset, image.Height - offset),
            new SKRect(offset, -offset, image.Width + offset, image.Height - offset),
            new SKRect(-offset, offset, image.Width - offset, image.Height + offset),
            new SKRect(offset, offset, image.Width + offset, image.Height + offset)
        };

        foreach (var tap in taps)
        {
            surface.Canvas.DrawImage(image, tap, destination, sampling, paint);
        }

        surface.Canvas.Flush();
        return surface.Snapshot();
    }
}

internal sealed class BoxRenderNode : RenderNode
{
    private readonly SKColor? _background;
    private readonly SKColor? _border;
    private readonly float _borderWidth;
    private readonly float _cornerRadius;
    private readonly UiThickness _padding;
    private readonly float? _width;
    private readonly float? _height;

    public BoxRenderNode(SKColor? background, SKColor? border, float borderWidth, float cornerRadius, UiThickness padding, float? width, float? height, bool expand)
    {
        _background = background;
        _border = border;
        _borderWidth = borderWidth;
        _cornerRadius = cornerRadius;
        _padding = padding;
        _width = width;
        _height = height;
        Expand = expand;
        ClipToBounds = cornerRadius > 0;
    }

    public bool Expand { get; }

    public override UiSize Measure(SKCanvas canvas, UiSize available)
    {
        var inner = new UiSize(
            Math.Max(0, (_width ?? available.Width) - _padding.Horizontal),
            Math.Max(0, (_height ?? available.Height) - _padding.Vertical));
        var childSize = Children.Count == 0 ? UiSize.Zero : Children[0].Measure(canvas, inner);
        return new UiSize(_width ?? childSize.Width + _padding.Horizontal, _height ?? childSize.Height + _padding.Vertical);
    }

    public override void Arrange(UiRect bounds)
    {
        base.Arrange(bounds);
        if (Children.Count > 0)
        {
            Children[0].Arrange(new UiRect(
                bounds.X + _padding.Left,
                bounds.Y + _padding.Top,
                Math.Max(0, bounds.Width - _padding.Horizontal),
                Math.Max(0, bounds.Height - _padding.Vertical)));
        }
    }

    public override void Paint(SKCanvas canvas)
    {
        var rect = Bounds.ToSkRect();
        if (_background is not null)
        {
            using var paint = new SKPaint { Color = _background.Value, IsAntialias = true };
            canvas.DrawRoundRect(rect, _cornerRadius, _cornerRadius, paint);
        }

        base.Paint(canvas);

        if (_border is not null && _borderWidth > 0)
        {
            using var paint = new SKPaint
            {
                Color = _border.Value,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = _borderWidth
            };
            canvas.DrawRoundRect(rect, _cornerRadius, _cornerRadius, paint);
        }
    }
}

public sealed record FlexItem(Widget Child, float Flex = 0, float? Basis = null);

public sealed class Flex : Widget
{
    public Flex(Axis direction, IEnumerable<FlexItem> children, float spacing = 0, CrossAxisAlignment crossAxisAlignment = CrossAxisAlignment.Stretch, string? key = null)
        : base(key)
    {
        Direction = direction;
        Items = children.ToArray();
        Spacing = spacing;
        CrossAxisAlignment = crossAxisAlignment;
    }

    public Axis Direction { get; }
    public IReadOnlyList<FlexItem> Items { get; }
    public float Spacing { get; }
    public CrossAxisAlignment CrossAxisAlignment { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        var node = new FlexRenderNode(Direction, Items, Spacing, CrossAxisAlignment);
        foreach (var item in Items)
        {
            node.Add(item.Child.CreateRenderNode(context));
        }

        return node;
    }
}

internal sealed class FlexRenderNode : RenderNode
{
    private readonly IReadOnlyList<FlexItem> _items;
    private readonly Axis _direction;
    private readonly float _spacing;
    private readonly CrossAxisAlignment _crossAxisAlignment;
    private UiSize[] _measured = Array.Empty<UiSize>();

    public FlexRenderNode(Axis direction, IReadOnlyList<FlexItem> items, float spacing, CrossAxisAlignment crossAxisAlignment)
    {
        _direction = direction;
        _items = items;
        _spacing = spacing;
        _crossAxisAlignment = crossAxisAlignment;
    }

    public override UiSize Measure(SKCanvas canvas, UiSize available)
    {
        _measured = new UiSize[Children.Count];
        var usedMain = Math.Max(0, Children.Count - 1) * _spacing;
        var maxCross = 0f;
        var totalFlex = _items.Sum(static item => item.Flex);
        for (var i = 0; i < Children.Count; i++)
        {
            if (_items[i].Flex > 0)
            {
                continue;
            }

            var childAvailable = _direction == Axis.Horizontal
                ? new UiSize(_items[i].Basis ?? available.Width, available.Height)
                : new UiSize(available.Width, _items[i].Basis ?? available.Height);
            _measured[i] = Children[i].Measure(canvas, childAvailable);
            usedMain += Main(_measured[i]);
            maxCross = Math.Max(maxCross, Cross(_measured[i]));
        }

        var remaining = Math.Max(0, Main(available) - usedMain);
        for (var i = 0; i < Children.Count; i++)
        {
            if (_items[i].Flex <= 0)
            {
                continue;
            }

            var main = totalFlex <= 0 ? 0 : remaining * (_items[i].Flex / totalFlex);
            var childAvailable = _direction == Axis.Horizontal
                ? new UiSize(main, available.Height)
                : new UiSize(available.Width, main);
            _measured[i] = Children[i].Measure(canvas, childAvailable);
            usedMain += main;
            maxCross = Math.Max(maxCross, Cross(_measured[i]));
        }

        return _direction == Axis.Horizontal
            ? new UiSize(Math.Min(available.Width, usedMain), Math.Min(available.Height, maxCross))
            : new UiSize(Math.Min(available.Width, maxCross), Math.Min(available.Height, usedMain));
    }

    public override void Arrange(UiRect bounds)
    {
        base.Arrange(bounds);
        var cursor = _direction == Axis.Horizontal ? bounds.X : bounds.Y;
        for (var i = 0; i < Children.Count; i++)
        {
            var itemMain = _items[i].Flex > 0
                ? Math.Max(0, (Main(new UiSize(bounds.Width, bounds.Height)) - FixedMain()) * (_items[i].Flex / Math.Max(1, _items.Sum(static item => item.Flex))))
                : Main(_measured[i]);
            var itemCross = _crossAxisAlignment == CrossAxisAlignment.Stretch
                ? Cross(new UiSize(bounds.Width, bounds.Height))
                : Cross(_measured[i]);

            var rect = _direction == Axis.Horizontal
                ? new UiRect(cursor, bounds.Y, itemMain, itemCross)
                : new UiRect(bounds.X, cursor, itemCross, itemMain);
            Children[i].Arrange(rect);
            cursor += itemMain + _spacing;
        }
    }

    private float FixedMain()
    {
        var value = Math.Max(0, Children.Count - 1) * _spacing;
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Flex <= 0)
            {
                value += Main(_measured[i]);
            }
        }

        return value;
    }

    private float Main(UiSize size) => _direction == Axis.Horizontal ? size.Width : size.Height;
    private float Cross(UiSize size) => _direction == Axis.Horizontal ? size.Height : size.Width;
}

public sealed class Button : Widget
{
    public Button(string text, Action onClick, bool enabled = true, string? key = null)
        : base(key)
    {
        Text = text;
        OnClick = onClick;
        Enabled = enabled;
    }

    public string Text { get; }
    public Action OnClick { get; }
    public bool Enabled { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        return new ButtonRenderNode(Text, OnClick, Enabled, context.Theme);
    }
}

internal sealed class ButtonRenderNode : RenderNode
{
    private readonly string _text;
    private readonly Action _onClick;
    private readonly bool _enabled;
    private readonly UiTheme _theme;
    private bool _pressed;

    public ButtonRenderNode(string text, Action onClick, bool enabled, UiTheme theme)
    {
        _text = text;
        _onClick = onClick;
        _enabled = enabled;
        _theme = theme;
        PointerDown = _ =>
        {
            if (_enabled)
            {
                _pressed = true;
            }

            _.Use();
        };
        PointerUp = _ =>
        {
            if (_enabled && _pressed && Bounds.Contains(_.Position))
            {
                _onClick();
            }

            _pressed = false;
            _.Use();
        };
    }

    public override UiSize Measure(SKCanvas canvas, UiSize available)
    {
        using var paint = new SKPaint { IsAntialias = true };
        using var font = new SKFont { Size = _theme.FontSize, Typeface = SKTypeface.FromFamilyName(_theme.FontFamily) };
        return new UiSize(Math.Min(available.Width, font.MeasureText(_text, paint) + 22), 30);
    }

    public override void Paint(SKCanvas canvas)
    {
        var bg = _pressed ? SKColor.Parse("#B7DCFA") : _theme.AccentSoft;
        if (!_enabled)
        {
            bg = bg.WithAlpha(120);
        }

        using var fill = new SKPaint { Color = bg, IsAntialias = true };
        using var stroke = new SKPaint { Color = _theme.Accent, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRoundRect(Bounds.ToSkRect(), 6, 6, fill);
        canvas.DrawRoundRect(Bounds.ToSkRect(), 6, 6, stroke);
        using var text = new SKPaint { Color = _theme.Text.WithAlpha(_enabled ? (byte)255 : (byte)130), IsAntialias = true };
        using var font = new SKFont { Size = _theme.FontSize, Typeface = SKTypeface.FromFamilyName(_theme.FontFamily) };
        var tw = font.MeasureText(_text, text);
        canvas.DrawText(_text, Bounds.X + (Bounds.Width - tw) / 2, Bounds.Y + 19, SKTextAlign.Left, font, text);
    }
}

public sealed class CheckBox : Widget
{
    public CheckBox(bool isChecked, Action<bool> changed, string? label = null, string? key = null)
        : base(key)
    {
        IsChecked = isChecked;
        Changed = changed;
        Label = label;
    }

    public bool IsChecked { get; }
    public Action<bool> Changed { get; }
    public string? Label { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        return new CheckBoxRenderNode(IsChecked, Changed, Label, context.Theme);
    }
}

internal sealed class CheckBoxRenderNode : RenderNode
{
    private readonly bool _isChecked;
    private readonly Action<bool> _changed;
    private readonly string? _label;
    private readonly UiTheme _theme;

    public CheckBoxRenderNode(bool isChecked, Action<bool> changed, string? label, UiTheme theme)
    {
        _isChecked = isChecked;
        _changed = changed;
        _label = label;
        _theme = theme;
        PointerDown = _ => _.Use();
        PointerUp = _ =>
        {
            _changed(!_isChecked);
            _.Use();
        };
        IsFocusable = true;
        KeyDown = key =>
        {
            if (key.Key is UiKey.Enter)
            {
                _changed(!_isChecked);
                key.Use();
            }
        };
    }

    public override UiSize Measure(SKCanvas canvas, UiSize available)
    {
        return new UiSize(string.IsNullOrEmpty(_label) ? 22 : 120, 24);
    }

    public override void Paint(SKCanvas canvas)
    {
        var box = new SKRect(Bounds.X, Bounds.Y + 3, Bounds.X + 18, Bounds.Y + 21);
        using var fill = new SKPaint { Color = _isChecked ? _theme.Accent : SKColors.White, IsAntialias = true };
        using var stroke = new SKPaint { Color = _theme.Accent, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRoundRect(box, 4, 4, fill);
        canvas.DrawRoundRect(box, 4, 4, stroke);
        if (_isChecked)
        {
            using var check = new SKPaint { Color = SKColors.White, StrokeWidth = 2, IsAntialias = true, Style = SKPaintStyle.Stroke };
            canvas.DrawLine(box.Left + 4, box.MidY, box.Left + 8, box.Bottom - 5, check);
            canvas.DrawLine(box.Left + 8, box.Bottom - 5, box.Right - 4, box.Top + 5, check);
        }

        if (!string.IsNullOrEmpty(_label))
        {
            using var text = new SKPaint { Color = _theme.Text, IsAntialias = true };
            using var font = new SKFont { Size = _theme.FontSize, Typeface = SKTypeface.FromFamilyName(_theme.FontFamily) };
            canvas.DrawText(_label, Bounds.X + 26, Bounds.Y + 17, SKTextAlign.Left, font, text);
        }
    }
}

public sealed class TextInputController
{
    private string _text;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public TextInputController(string text = "")
    {
        _text = text;
        CaretIndex = text.Length;
        SelectionStart = text.Length;
        SelectionEnd = text.Length;
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            CaretIndex = Math.Clamp(CaretIndex, 0, _text.Length);
            SelectionStart = Math.Clamp(SelectionStart, 0, _text.Length);
            SelectionEnd = Math.Clamp(SelectionEnd, 0, _text.Length);
        }
    }

    public int CaretIndex { get; set; }
    public int SelectionStart { get; private set; }
    public int SelectionEnd { get; private set; }
    public bool HasSelection => SelectionStart != SelectionEnd;
    public int SelectionMin => Math.Min(SelectionStart, SelectionEnd);
    public int SelectionMax => Math.Max(SelectionStart, SelectionEnd);
    public string SelectedText => HasSelection ? _text[SelectionMin..SelectionMax] : "";

    public void MoveCaret(int caretIndex, bool select = false)
    {
        CaretIndex = Math.Clamp(caretIndex, 0, _text.Length);
        if (select)
        {
            SelectionEnd = CaretIndex;
        }
        else
        {
            SelectionStart = CaretIndex;
            SelectionEnd = CaretIndex;
        }
    }

    public void SelectAll()
    {
        SelectionStart = 0;
        SelectionEnd = _text.Length;
        CaretIndex = _text.Length;
    }

    public void ReplaceSelection(string value, int maxLength)
    {
        var start = SelectionMin;
        var end = SelectionMax;
        var next = _text[..start] + value + _text[end..];
        if (next.Length > maxLength || next == _text)
        {
            return;
        }

        PushUndo();
        _text = next;
        _redo.Clear();
        MoveCaret(start + value.Length);
    }

    public void Backspace()
    {
        if (HasSelection)
        {
            ReplaceSelection("", int.MaxValue);
            return;
        }

        if (CaretIndex == 0)
        {
            return;
        }

        PushUndo();
        _text = _text.Remove(CaretIndex - 1, 1);
        _redo.Clear();
        MoveCaret(CaretIndex - 1);
    }

    public void Delete()
    {
        if (HasSelection)
        {
            ReplaceSelection("", int.MaxValue);
            return;
        }

        if (CaretIndex >= _text.Length)
        {
            return;
        }

        PushUndo();
        _text = _text.Remove(CaretIndex, 1);
        _redo.Clear();
        MoveCaret(CaretIndex);
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Push(_text);
        _text = _undo.Pop();
        MoveCaret(_text.Length);
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Push(_text);
        _text = _redo.Pop();
        MoveCaret(_text.Length);
        return true;
    }

    private void PushUndo()
    {
        if (_undo.Count == 0 || _undo.Peek() != _text)
        {
            _undo.Push(_text);
        }
    }
}

public sealed class TextInput : Widget
{
    public TextInput(TextInputController controller, string placeholder = "", bool multiline = false, int maxLength = int.MaxValue, string? key = null)
        : base(key)
    {
        Controller = controller;
        Placeholder = placeholder;
        Multiline = multiline;
        MaxLength = maxLength;
    }

    public TextInputController Controller { get; }
    public string Placeholder { get; }
    public bool Multiline { get; }
    public int MaxLength { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        return new TextInputRenderNode(Controller, Placeholder, Multiline, MaxLength, context.Theme);
    }
}

internal sealed class TextInputRenderNode : RenderNode
{
    private readonly TextInputController _controller;
    private readonly string _placeholder;
    private readonly bool _multiline;
    private readonly int _maxLength;
    private readonly UiTheme _theme;

    public TextInputRenderNode(TextInputController controller, string placeholder, bool multiline, int maxLength, UiTheme theme)
    {
        _controller = controller;
        _placeholder = placeholder;
        _multiline = multiline;
        _maxLength = maxLength;
        _theme = theme;
        IsFocusable = true;
        PointerDown = _ => _.Use();
        TextInput = input =>
        {
            if (string.IsNullOrEmpty(input.Text) || input.Text.Any(char.IsControl))
            {
                return;
            }

            _controller.ReplaceSelection(input.Text, _maxLength);
            input.Use();
        };
        KeyDown = key =>
        {
            if (key.HasControl)
            {
                switch (key.Key)
                {
                    case UiKey.A:
                        _controller.SelectAll();
                        key.Use();
                        return;
                    case UiKey.C:
                        if (_controller.HasSelection)
                        {
                            key.SetClipboardText(_controller.SelectedText);
                        }

                        key.Use();
                        return;
                    case UiKey.X:
                        if (_controller.HasSelection)
                        {
                            key.SetClipboardText(_controller.SelectedText);
                            _controller.ReplaceSelection("", _maxLength);
                        }

                        key.Use();
                        return;
                    case UiKey.V:
                        if (!string.IsNullOrEmpty(key.ClipboardText))
                        {
                            _controller.ReplaceSelection(key.ClipboardText, _maxLength);
                        }

                        key.Use();
                        return;
                    case UiKey.Z:
                        if (key.HasShift)
                        {
                            _controller.Redo();
                        }
                        else
                        {
                            _controller.Undo();
                        }

                        key.Use();
                        return;
                    case UiKey.Y:
                        _controller.Redo();
                        key.Use();
                        return;
                }
            }

            switch (key.Key)
            {
                case UiKey.Backspace:
                    _controller.Backspace();
                    break;
                case UiKey.Delete:
                    _controller.Delete();
                    break;
                case UiKey.Left:
                    _controller.MoveCaret(_controller.CaretIndex - 1, key.HasShift);
                    break;
                case UiKey.Right:
                    _controller.MoveCaret(_controller.CaretIndex + 1, key.HasShift);
                    break;
                case UiKey.Home:
                    _controller.MoveCaret(0, key.HasShift);
                    break;
                case UiKey.End:
                    _controller.MoveCaret(_controller.Text.Length, key.HasShift);
                    break;
                case UiKey.Enter when _multiline:
                    _controller.ReplaceSelection("\n", _maxLength);
                    break;
                default:
                    return;
            }

            key.Use();
        };
    }

    public override UiSize Measure(SKCanvas canvas, UiSize available)
    {
        return new UiSize(available.Width, _multiline ? Math.Min(120, available.Height) : 32);
    }

    public override void Paint(SKCanvas canvas)
    {
        using var fill = new SKPaint { Color = SKColor.Parse("#F8FCFF"), IsAntialias = true };
        using var stroke = new SKPaint { Color = _theme.Accent, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRoundRect(Bounds.ToSkRect(), 6, 6, fill);
        canvas.DrawRoundRect(Bounds.ToSkRect(), 6, 6, stroke);
        var value = string.IsNullOrEmpty(_controller.Text) ? _placeholder : _controller.Text;
        var color = string.IsNullOrEmpty(_controller.Text) ? _theme.MutedText.WithAlpha(150) : _theme.Text;
        using var text = new SKPaint { Color = color, IsAntialias = true };
        using var font = new SKFont { Size = _theme.FontSize, Typeface = SKTypeface.FromFamilyName(_theme.FontFamily) };
        if (_controller.HasSelection && !string.IsNullOrEmpty(_controller.Text))
        {
            var beforeSelection = _controller.Text[.._controller.SelectionMin];
            var selected = _controller.SelectedText;
            var selectionX = Bounds.X + 8 + font.MeasureText(beforeSelection, text);
            var selectionWidth = Math.Max(1, font.MeasureText(selected, text));
            using var selectionPaint = new SKPaint { Color = _theme.Accent.WithAlpha(90), IsAntialias = true };
            canvas.DrawRect(new SKRect(selectionX, Bounds.Y + 6, selectionX + selectionWidth, Bounds.Y + 26), selectionPaint);
        }

        canvas.DrawText(value, Bounds.X + 8, Bounds.Y + 21, SKTextAlign.Left, font, text);
        var caretText = _controller.Text[..Math.Clamp(_controller.CaretIndex, 0, _controller.Text.Length)];
        var caretX = Bounds.X + 8 + font.MeasureText(caretText, text);
        using var caret = new SKPaint { Color = _theme.Text, StrokeWidth = 1 };
        canvas.DrawLine(caretX, Bounds.Y + 7, caretX, Bounds.Y + 25, caret);
    }
}

public sealed class ScrollController
{
    public float Offset { get; set; }
    public float TargetOffset { get; set; }
}

public sealed record ScrollMetrics(float Viewport, float Extent, float Offset)
{
    public float MaxOffset => Math.Max(0, Extent - Viewport);
}

public abstract class ScrollPhysics
{
    public abstract float ApplyWheel(ScrollMetrics metrics, float currentTarget, float wheelDelta);
    public abstract float Step(ScrollMetrics metrics, float current, float target, double deltaSeconds);
}

public sealed class SmoothScrollPhysics : ScrollPhysics
{
    public float WheelStep { get; init; } = 96;
    public float Responsiveness { get; init; } = 16;

    public override float ApplyWheel(ScrollMetrics metrics, float currentTarget, float wheelDelta)
    {
        return Math.Clamp(currentTarget - wheelDelta * WheelStep, 0, metrics.MaxOffset);
    }

    public override float Step(ScrollMetrics metrics, float current, float target, double deltaSeconds)
    {
        var factor = 1f - MathF.Exp(-Responsiveness * (float)deltaSeconds);
        var next = current + (target - current) * factor;
        return Math.Abs(target - next) < 0.35f ? target : Math.Clamp(next, 0, metrics.MaxOffset);
    }
}

public sealed record ScrollBehavior(ScrollPhysics Physics, IShaderEffect? EdgeEffect = null)
{
    public static ScrollBehavior Default { get; } = new(new SmoothScrollPhysics(), ScrollEdgeShaderEffect.Default);
}

public sealed class ScrollView : Widget
{
    public ScrollView(Widget child, ScrollController? controller = null, ScrollBehavior? behavior = null, string? key = null)
        : base(key)
    {
        Child = child;
        Controller = controller ?? new ScrollController();
        Behavior = behavior ?? ScrollBehavior.Default;
    }

    public Widget Child { get; }
    public ScrollController Controller { get; }
    public ScrollBehavior Behavior { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        var node = new ScrollViewRenderNode(Controller, Behavior);
        node.Add(Child.CreateRenderNode(context));
        return node;
    }
}

internal sealed class ScrollViewRenderNode : RenderNode
{
    private readonly ScrollController _controller;
    private readonly ScrollBehavior _behavior;
    private float _extent;
    private float _viewport;
    private UiRect _contentViewport;

    public ScrollViewRenderNode(ScrollController controller, ScrollBehavior behavior)
    {
        _controller = controller;
        _behavior = behavior;
        ClipToBounds = true;
        Wheel = wheel =>
        {
            var metrics = new ScrollMetrics(_viewport, _extent, _controller.Offset);
            _controller.TargetOffset = _behavior.Physics.ApplyWheel(metrics, _controller.TargetOffset, wheel.DeltaY);
            wheel.Use();
        };
        Tick = delta =>
        {
            var metrics = new ScrollMetrics(_viewport, _extent, _controller.Offset);
            var next = _behavior.Physics.Step(metrics, _controller.Offset, _controller.TargetOffset, delta);
            if (Math.Abs(next - _controller.Offset) < 0.01f)
            {
                return false;
            }

            _controller.Offset = next;
            return true;
        };
    }

    public override UiSize Measure(SKCanvas canvas, UiSize available)
    {
        var reservedSpace = _behavior.EdgeEffect?.ReservedSpace ?? new UiThickness(0);
        _viewport = Math.Max(1, available.Height - reservedSpace.Vertical);
        var contentSize = Children[0].Measure(canvas, new UiSize(available.Width, 100_000));
        _extent = contentSize.Height;
        _controller.Offset = Math.Clamp(_controller.Offset, 0, Math.Max(0, _extent - _viewport));
        _controller.TargetOffset = Math.Clamp(_controller.TargetOffset, 0, Math.Max(0, _extent - _viewport));
        return available;
    }

    public override void Arrange(UiRect bounds)
    {
        base.Arrange(bounds);
        var reservedSpace = _behavior.EdgeEffect?.ReservedSpace ?? new UiThickness(0);
        _contentViewport = new UiRect(
            bounds.X + reservedSpace.Left,
            bounds.Y + reservedSpace.Top,
            Math.Max(0, bounds.Width - reservedSpace.Horizontal),
            Math.Max(0, bounds.Height - reservedSpace.Vertical));
        Children[0].Arrange(new UiRect(
            bounds.X + reservedSpace.Left,
            bounds.Y + reservedSpace.Top - _controller.Offset,
            Math.Max(0, bounds.Width - reservedSpace.Horizontal),
            _extent));
    }

    public override RenderNode? HitTest(UiPoint point)
    {
        if (!Bounds.Contains(point))
        {
            return null;
        }

        if (_contentViewport.Contains(point))
        {
            for (var index = Children.Count - 1; index >= 0; index--)
            {
                var hit = Children[index].HitTest(point);
                if (hit is not null)
                {
                    return hit;
                }
            }
        }

        return Wheel is not null || IsFocusable ? this : null;
    }

    public override void Paint(SKCanvas canvas)
    {
        if (_behavior.EdgeEffect is null)
        {
            base.Paint(canvas);
            return;
        }

        _behavior.EdgeEffect.Paint(canvas, Bounds.ToSkRect(), base.Paint);
    }
}

public interface IShaderEffect
{
    UiThickness ReservedSpace { get; }
    void Paint(SKCanvas canvas, SKRect bounds, Action<SKCanvas> paintContent);
}

public sealed class RuntimeShaderEffect : IShaderEffect, IDisposable
{
    private readonly SKRuntimeEffect _effect;
    private readonly Dictionary<string, object> _uniforms = new();

    public RuntimeShaderEffect(string sksl)
    {
        Sksl = sksl;
        var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);
        if (effect is null)
        {
            throw new InvalidOperationException(errors);
        }

        _effect = effect;
    }

    public string Sksl { get; }
    public UiThickness ReservedSpace => new(0);

    public RuntimeShaderEffect Set(string name, float value)
    {
        _uniforms[name] = value;
        return this;
    }

    public RuntimeShaderEffect Set(string name, SKSize value)
    {
        _uniforms[name] = value;
        return this;
    }

    public void Paint(SKCanvas canvas, SKRect bounds, Action<SKCanvas> paintContent)
    {
        using var uniforms = new SKRuntimeEffectUniforms(_effect);
        foreach (var item in _uniforms)
        {
            switch (item.Value)
            {
                case float value:
                    uniforms[item.Key] = value;
                    break;
                case SKSize value:
                    uniforms[item.Key] = value;
                    break;
                case SKPoint value:
                    uniforms[item.Key] = value;
                    break;
                case int value:
                    uniforms[item.Key] = value;
                    break;
                case SKColor value:
                    uniforms[item.Key] = value;
                    break;
            }
        }

        using var shader = _effect.ToShader(uniforms);
        using var paint = new SKPaint { Shader = shader, IsAntialias = true };
        paintContent(canvas);
        canvas.DrawRect(bounds, paint);
    }

    public void Dispose()
    {
        _effect.Dispose();
    }
}

public sealed class ScrollEdgeShaderEffect : IShaderEffect
{
    public static ScrollEdgeShaderEffect Default { get; } = new(edgeSize: 26, bendStrength: 0.62f);

    public ScrollEdgeShaderEffect(float edgeSize, float bendStrength)
    {
        EdgeSize = edgeSize;
        BendStrength = bendStrength;
    }

    public float EdgeSize { get; }
    public float BendStrength { get; }
    public UiThickness ReservedSpace => new(0, EdgeSize, 0, EdgeSize);

    public void Paint(SKCanvas canvas, SKRect bounds, Action<SKCanvas> paintContent)
    {
        var width = Math.Max(1, (int)MathF.Ceiling(bounds.Width));
        var height = Math.Max(1, (int)MathF.Ceiling(bounds.Height));
        var edge = Math.Clamp(EdgeSize, 0, MathF.Floor(height / 2f) - 1);
        if (edge <= 1)
        {
            paintContent(canvas);
            return;
        }

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
        {
            paintContent(canvas);
            return;
        }

        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Translate(-bounds.Left, -bounds.Top);
        paintContent(surface.Canvas);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        var save = canvas.Save();
        canvas.ClipRect(bounds, SKClipOperation.Intersect, antialias: true);

        using var contentPaint = new SKPaint
        {
            IsAntialias = true
        };
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);

        var blurRadius = Math.Clamp(edge * 0.24f, 3f, 9f);
        var softOverlap = Math.Clamp(edge * 0.42f, 6f, 16f);

        DrawBlurredBentEdges(canvas, image, bounds, width, height, edge, blurRadius, sampling);
        DrawMiddle(canvas, image, bounds, width, height, edge, softOverlap, sampling, contentPaint);

        canvas.RestoreToCount(save);
    }

    private static void DrawMiddle(SKCanvas canvas, SKImage image, SKRect bounds, int width, int height, float edge, float softOverlap, SKSamplingOptions sampling, SKPaint paint)
    {
        var safeHeight = Math.Max(0, height - edge * 2);
        if (safeHeight <= 0)
        {
            return;
        }

        var source = new SKRect(0, edge, width, edge + safeHeight);
        var destination = new SKRect(bounds.Left, bounds.Top + edge, bounds.Left + width, bounds.Top + edge + safeHeight);
        var layer = canvas.SaveLayer(destination, null);
        canvas.DrawImage(image, source, destination, sampling, paint);

        using var maskShader = SKShader.CreateLinearGradient(
            new SKPoint(destination.Left, destination.Top),
            new SKPoint(destination.Left, destination.Bottom),
            new[]
            {
                SKColors.Transparent,
                SKColors.White,
                SKColors.White,
                SKColors.Transparent
            },
            new[]
            {
                0f,
                Math.Clamp(softOverlap / Math.Max(1f, destination.Height), 0f, 0.48f),
                Math.Clamp(1f - softOverlap / Math.Max(1f, destination.Height), 0.52f, 1f),
                1f
            },
            SKShaderTileMode.Clamp);
        using var maskPaint = new SKPaint
        {
            BlendMode = SKBlendMode.DstIn,
            Shader = maskShader
        };
        canvas.DrawRect(destination, maskPaint);
        canvas.RestoreToCount(layer);
    }

    private void DrawBlurredBentEdges(SKCanvas canvas, SKImage image, SKRect bounds, int width, int height, float edge, float blurRadius, SKSamplingOptions sampling)
    {
        using var edgeSurface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (edgeSurface is null)
        {
            DrawBentEdge(canvas, image, bounds, width, height, edge, sampling, isTop: true);
            DrawBentEdge(canvas, image, bounds, width, height, edge, sampling, isTop: false);
            return;
        }

        edgeSurface.Canvas.Clear(SKColors.Transparent);
        var localBounds = new SKRect(0, 0, width, height);
        DrawBentEdge(edgeSurface.Canvas, image, localBounds, width, height, edge, sampling, isTop: true);
        DrawBentEdge(edgeSurface.Canvas, image, localBounds, width, height, edge, sampling, isTop: false);
        edgeSurface.Canvas.Flush();

        using var edgeImage = edgeSurface.Snapshot();
        using var blur = SKImageFilter.CreateBlur(0f, blurRadius, SKShaderTileMode.Clamp);
        using var blurPaint = new SKPaint
        {
            IsAntialias = true,
            ImageFilter = blur
        };
        canvas.DrawImage(edgeImage, bounds.Left, bounds.Top, sampling, blurPaint);
    }

    private void DrawBentEdge(SKCanvas canvas, SKImage image, SKRect bounds, int width, int height, float edge, SKSamplingOptions sampling, bool isTop)
    {
        const float sliceHeight = 2f;
        using var paint = new SKPaint
        {
            IsAntialias = true
        };

        for (var y = 0f; y < edge; y += sliceHeight)
        {
            var slice = Math.Min(sliceHeight, edge - y);
            var t = y / Math.Max(1, edge);
            var fade = isTop ? SmoothStep(t) : 1f - SmoothStep(t);
            var outer = isTop ? 1f - t : t;
            var curl = outer * outer;
            var opacity = MathF.Pow(fade, 1.35f) * 0.18f;
            var squeeze = EdgeSize * BendStrength * 0.72f * curl;
            var wave = MathF.Sin((t * MathF.PI) + (isTop ? 0f : MathF.PI * 0.22f)) * EdgeSize * BendStrength * 0.13f * curl;

            var destinationY = isTop
                ? bounds.Top + y
                : bounds.Top + height - edge + y;
            var sourceY = isTop
                ? y
                : height - edge + y;

            paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(opacity * 255f, 0, 255));
            var source = new SKRect(0, sourceY, width, sourceY + slice);
            var destination = new SKRect(
                bounds.Left + squeeze + wave,
                destinationY,
                bounds.Left + width - squeeze + wave,
                destinationY + slice);

            canvas.DrawImage(image, source, destination, sampling, paint);
        }
    }

    private static float SmoothStep(float value)
    {
        var t = Math.Clamp(value, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}

public sealed class ListView<T> : Widget
{
    public ListView(IEnumerable<T> items, Func<T, Widget> itemBuilder, float spacing = 4, string? key = null)
        : base(key)
    {
        Items = items.ToArray();
        ItemBuilder = itemBuilder;
        Spacing = spacing;
    }

    public IReadOnlyList<T> Items { get; }
    public Func<T, Widget> ItemBuilder { get; }
    public float Spacing { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        return new Flex(Axis.Vertical, Items.Select(item => new FlexItem(ItemBuilder(item))), Spacing).CreateRenderNode(context);
    }
}

public sealed class Stack : Widget
{
    public Stack(IEnumerable<Widget> children, string? key = null)
        : base(key)
    {
        Children = children.ToArray();
    }

    public IReadOnlyList<Widget> Children { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        var node = new StackRenderNode();
        foreach (var child in Children)
        {
            node.Add(child.CreateRenderNode(context));
        }

        return node;
    }
}

internal sealed class StackRenderNode : RenderNode
{
    public override UiSize Measure(SKCanvas canvas, UiSize available)
    {
        foreach (var child in Children)
        {
            child.Measure(canvas, available);
        }

        return available;
    }

    public override void Arrange(UiRect bounds)
    {
        base.Arrange(bounds);
        foreach (var child in Children)
        {
            child.Arrange(bounds);
        }
    }
}

public sealed class KeyRegion : Widget
{
    public KeyRegion(Widget child, Action<KeyEvent> keyDown, string? key = null)
        : base(key)
    {
        Child = child;
        KeyDown = keyDown;
    }

    public Widget Child { get; }
    public Action<KeyEvent> KeyDown { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        var node = new KeyRegionRenderNode(KeyDown);
        node.Add(Child.CreateRenderNode(context));
        return node;
    }
}

internal sealed class KeyRegionRenderNode : RenderNode
{
    public KeyRegionRenderNode(Action<KeyEvent> keyDown)
    {
        KeyDown = keyDown;
    }

    public override UiSize Measure(SKCanvas canvas, UiSize available)
    {
        return Children.Count == 0 ? UiSize.Zero : Children[0].Measure(canvas, available);
    }

    public override void Arrange(UiRect bounds)
    {
        base.Arrange(bounds);
        if (Children.Count > 0)
        {
            Children[0].Arrange(bounds);
        }
    }
}

public sealed class TapRegion : Widget
{
    public TapRegion(Widget child, Action onTap, bool focusable = false, string? key = null)
        : base(key)
    {
        Child = child;
        OnTap = onTap;
        Focusable = focusable;
    }

    public Widget Child { get; }
    public Action OnTap { get; }
    public bool Focusable { get; }

    internal override RenderNode CreateRenderNode(BuildContext context)
    {
        var node = new TapRenderNode(OnTap, Focusable);
        node.Add(Child.CreateRenderNode(context));
        return node;
    }
}

internal sealed class TapRenderNode : RenderNode
{
    private bool _pressed;

    public TapRenderNode(Action onTap, bool focusable)
    {
        IsFocusable = focusable;
        PointerDown = _ =>
        {
            _pressed = true;
            _.Use();
        };
        PointerUp = _ =>
        {
            if (_pressed && Bounds.Contains(_.Position))
            {
                onTap();
            }

            _pressed = false;
            _.Use();
        };
        KeyDown = key =>
        {
            if (focusable && key.Key is UiKey.Enter)
            {
                onTap();
                key.Use();
            }
        };
    }

    public override UiSize Measure(SKCanvas canvas, UiSize available)
    {
        return Children.Count == 0 ? UiSize.Zero : Children[0].Measure(canvas, available);
    }

    public override void Arrange(UiRect bounds)
    {
        base.Arrange(bounds);
        if (Children.Count > 0)
        {
            Children[0].Arrange(bounds);
        }
    }
}

public sealed class YogaLayoutNode
{
    private readonly List<YogaLayoutNode> _children = new();

    public YogaLayoutNode(float width = float.NaN, float height = float.NaN, float flexGrow = 0)
    {
        Width = width;
        Height = height;
        FlexGrow = flexGrow;
    }

    public float Width { get; set; }
    public float Height { get; set; }
    public float FlexGrow { get; set; }
    public UiRect Layout { get; private set; }
    public IReadOnlyList<YogaLayoutNode> Children => new ReadOnlyCollection<YogaLayoutNode>(_children);

    public void Add(YogaLayoutNode child) => _children.Add(child);

    public void Calculate(float availableWidth, float availableHeight)
    {
        // Adapter boundary for YogaSharp. The C# package is intentionally kept
        // isolated here; this managed fallback keeps tests deterministic while
        // the native binding can be swapped underneath this API.
        Layout = new UiRect(0, 0, float.IsNaN(Width) ? availableWidth : Width, float.IsNaN(Height) ? availableHeight : Height);
        var fixedHeight = _children.Where(static child => child.FlexGrow <= 0).Sum(child => float.IsNaN(child.Height) ? 0 : child.Height);
        var flex = _children.Sum(static child => child.FlexGrow);
        var y = 0f;
        foreach (var child in _children)
        {
            var childHeight = child.FlexGrow > 0 && flex > 0
                ? Math.Max(0, Layout.Height - fixedHeight) * (child.FlexGrow / flex)
                : float.IsNaN(child.Height) ? 0 : child.Height;
            child.Layout = new UiRect(0, y, float.IsNaN(child.Width) ? Layout.Width : child.Width, childHeight);
            y += childHeight;
        }
    }
}

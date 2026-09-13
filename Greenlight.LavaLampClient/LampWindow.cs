using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Greenlight.LavaLampClient;

/// <summary>
/// The desk the lamp stands on: a transparent, always-on-top, click-through window over the
/// whole work area, with the lamp drawn on it.
/// </summary>
/// <remarks>
/// A window over the whole desktop rather than one shrink-wrapped around the lamp, because the
/// lamp can be dragged anywhere and resized as it goes — a tight window would have to be moved,
/// resized and re-layered on every frame of every drag, the glow would be clipped at its own
/// edges, and the save and cancel buttons sitting outside the box would have nowhere to be
/// drawn.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class LampWindow : Window
{
    private readonly LampConfig _config;
    private readonly LavaCanvas _canvas;
    private readonly DispatcherTimer _frames;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private TimeSpan _lastFrame;
    private TimeSpan _lastHousekeeping;
    private AreaBounds _area;

    private bool _editing;
    private LampGrip _grip;
    private ScenePoint _grabOffset;

    public LampWindow(LampConfig config, LampScene scene)
    {
        _config = config;
        Scene = scene;

        Title = "Greenlight lava lamp";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // Never take the caret out of somebody's editor. Paired with WS_EX_NOACTIVATE, which is
        // what makes a click landing here harmless in the first place.
        ShowActivated = false;

        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _canvas = new LavaCanvas(scene, config) { Opacity = config.Opacity };
        Content = _canvas;

        // 60fps. A desk toy that stutters is worse than no desk toy, and the whole frame is a few
        // dozen filled paths.
        _frames = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _frames.Tick += OnFrame;
    }

    public LampScene Scene { get; }

    /// <summary>Raised when edit mode ends and the arrangement should be kept: the tick, or Enter.</summary>
    public event EventHandler? EditSaved;

    /// <summary>Raised when edit mode ends and the arrangement should be thrown away: the cross, or Escape.</summary>
    public event EventHandler? EditCancelled;

    /// <summary>
    /// Whether the lamp can be picked up. Off, the overlay is furniture; on, it is a window
    /// covering the desktop, and nothing underneath it is clickable — so it is a mode somebody
    /// enters on purpose and leaves quickly, which is exactly why it has its own two buttons
    /// rather than only a line in the tray menu.
    /// </summary>
    public bool IsEditing
    {
        get => _editing;
        set
        {
            if (_editing == value) return;

            _editing = value;
            _canvas.IsEditing = value;

            if (value)
            {
                // The lamp is held in view for the duration, whatever Greenlight says, and warmed
                // up rather than cold. There is nothing to grab on a lamp that has faded out, and
                // nobody wants to wait six seconds for the wax to come up before they can see
                // what they are placing.
                Scene.ForceVisible = true;
                Scene.SnapVisible();
                Scene.BeginEdit();
            }
            else
            {
                Scene.ForceVisible = false;
                _grip = LampGrip.None;
                _canvas.Held = LampGrip.None;
                Cursor = Cursor.Default;
            }

            ClickThroughNative.SetInteractive(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, value);

            // Focus only so the keyboard shortcuts work. A window that never activates cannot be
            // sent a key, and Escape is the way most people will try to leave this.
            if (value) Activate();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Both of these need a real window handle, so neither can run before it is shown.
        ClickThroughNative.Apply(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        LayOutDesktop();

        _lastFrame = _clock.Elapsed;
        _frames.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _frames.Stop();
        base.OnClosed(e);
    }

    /// <summary>
    /// Take up a setting changed from the tray while the lamp is out: how solid it is, how much
    /// glow it throws, how fast the wax runs, and how much of the screen it has.
    /// </summary>
    /// <remarks>
    /// The config object is shared with the tray, so there is nothing to pass — this is the
    /// overlay being told to go and look again.
    /// </remarks>
    public void ApplyConfig()
    {
        _canvas.Opacity = _config.Opacity;
        Scene.ShowWhenOff = _config.ShowWhenOff;
        Scene.Liveliness = _config.Liveliness;
        LayOutDesktop();
    }

    /// <summary>
    /// Put the window over the desktop and tell the scene how big it is. Re-run when the screen
    /// changes size or the taskbar moves.
    /// </summary>
    public void LayOutDesktop()
    {
        var area = DesktopArea.Find(_config.Area);
        if (area.IsEmpty) return;

        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;

        // Position is physical, Width and Height are logical. Mixing those up puts the overlay at
        // a plausible-looking but wrong size on every scaled display, which is most of them.
        Position = new PixelPoint(area.X, area.Y);
        Width = area.Width / scaling;
        Height = area.Height / scaling;

        _area = area;
        Scene.Resize(Width, Height);
    }

    // ── edit mode ─────────────────────────────────────────────────────────────
    // Handled on the window rather than the canvas: the canvas is not hit-testable, because the
    // rest of the time this window is something the mouse is supposed to pass through.

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEditing) return;

        var point = At(e);
        var grip = Scene.HitTest(point);

        switch (grip)
        {
            case LampGrip.Save:
                EditSaved?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;

            case LampGrip.Cancel:
                EditCancelled?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;

            case LampGrip.None:
                // A click on bare desktop keeps what has been done. It is the same answer the
                // tick gives, deliberately: somebody who has dragged the lamp where they want it
                // and clicked away has said what they meant, and throwing the drag away there
                // would be a trap.
                EditSaved?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
        }

        _grip = grip;
        _canvas.Held = grip;

        // The lamp is carried by the point it was grabbed at rather than snapping its middle to
        // the cursor: a widget that jumps out from under the mouse on mouse-down is the
        // difference between dragging something and flinging it.
        _grabOffset = grip == LampGrip.Body ? point - Scene.Measure().Centre : default;

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!IsEditing) return;

        var point = At(e);

        if (_grip == LampGrip.None)
        {
            // Nothing held: the cursor is the only thing saying what is grabbable, since there
            // are no real controls on a window with nothing in it.
            Cursor = new Cursor(CursorFor(Scene.HitTest(point)));
            return;
        }

        if (_grip == LampGrip.Body) Scene.MoveTo(point - _grabOffset);
        else Scene.ResizeTo(_grip, point);

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_grip == LampGrip.None) return;

        _grip = LampGrip.None;
        _canvas.Held = LampGrip.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsEditing) return;

        var point = At(e);
        if (Scene.HitTest(point) == LampGrip.None) return;

        // The wheel resizes about the middle rather than about a corner, which is what somebody
        // scrolling over the lamp itself means by it.
        var box = Scene.Measure().Box;
        var centre = box.Centre;
        var height = Math.Clamp(
            box.Height * (1 + Math.Sign(e.Delta.Y) * 0.08), LampScene.MinimumHeight, Scene.MaximumHeight);

        Scene.Placement.Size = Scene.Height <= 0 ? Scene.Placement.Size : height / Scene.Height;
        Scene.MoveTo(centre);

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEditing) return;

        switch (e.Key)
        {
            case Key.Escape:
                EditCancelled?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.Enter:
                EditSaved?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            // Nudging with the arrow keys, because a drag is a blunt instrument for the last four
            // pixels and the box is clamped to the desktop anyway.
            case Key.Left or Key.Right or Key.Up or Key.Down:
            {
                var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
                var centre = Scene.Measure().Centre;

                Scene.MoveTo(centre + e.Key switch
                {
                    Key.Left => new ScenePoint(-step, 0),
                    Key.Right => new ScenePoint(step, 0),
                    Key.Up => new ScenePoint(0, -step),
                    _ => new ScenePoint(0, step),
                });

                e.Handled = true;
                break;
            }
        }
    }

    private static StandardCursorType CursorFor(LampGrip grip) => grip switch
    {
        LampGrip.Body => StandardCursorType.SizeAll,
        LampGrip.ResizeTopLeft or LampGrip.ResizeBottomRight => StandardCursorType.TopLeftCorner,
        LampGrip.ResizeTopRight or LampGrip.ResizeBottomLeft => StandardCursorType.TopRightCorner,
        LampGrip.Save or LampGrip.Cancel => StandardCursorType.Hand,
        _ => StandardCursorType.Arrow,
    };

    private ScenePoint At(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return new ScenePoint(p.X, p.Y);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var elapsed = now - _lastFrame;
        _lastFrame = now;

        // Once a second: re-claim the top of the z-order, and notice if the screen or the taskbar
        // has changed shape. Cheap, and much less code than listening for every way Windows has
        // of mentioning either.
        if (now - _lastHousekeeping >= TimeSpan.FromSeconds(1))
        {
            _lastHousekeeping = now;

            ClickThroughNative.KeepOnTop(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);

            if (DesktopArea.Find(_config.Area) != _area) LayOutDesktop();
        }

        Scene.Advance(elapsed);
        _canvas.InvalidateVisual();
    }
}

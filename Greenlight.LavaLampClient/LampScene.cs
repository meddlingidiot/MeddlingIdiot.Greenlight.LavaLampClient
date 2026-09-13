namespace Greenlight.LavaLampClient;

/// <summary>What the lamp is doing, which is Greenlight's aggregate colour by another name.</summary>
public enum LampState
{
    /// <summary>
    /// No Greenlight attached, or it has nothing to say. The lamp is switched off rather than
    /// taken away — dark glass with the wax pooled cold in the bottom. A lit lamp on a
    /// four-minute-old snapshot would be the toy lying, and a blank desktop looks like it
    /// crashed.
    /// </summary>
    Off,

    /// <summary>Everything passing. Green wax, rising gently.</summary>
    Green,

    /// <summary>A pull request wants you. Greenlight's yellow.</summary>
    Amber,

    /// <summary>A pipeline is broken. The lamp goes over to a rolling boil.</summary>
    Red,
}

/// <summary>What the mouse is over, in edit mode.</summary>
public enum LampGrip
{
    /// <summary>Nothing. A click here means "I am finished".</summary>
    None,

    /// <summary>The lamp itself — drag to carry it around the desktop.</summary>
    Body,

    ResizeTopLeft,
    ResizeTopRight,
    ResizeBottomLeft,
    ResizeBottomRight,

    /// <summary>The tick. Keep where it has been dragged to.</summary>
    Save,

    /// <summary>The cross. Put it back where it started.</summary>
    Cancel,
}

/// <summary>A point in the overlay's logical pixels.</summary>
public readonly record struct ScenePoint(double X, double Y)
{
    public static ScenePoint operator +(ScenePoint a, ScenePoint b) => new(a.X + b.X, a.Y + b.Y);

    public static ScenePoint operator -(ScenePoint a, ScenePoint b) => new(a.X - b.X, a.Y - b.Y);

    public static ScenePoint operator *(ScenePoint a, double k) => new(a.X * k, a.Y * k);

    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>A rectangle in the overlay's logical pixels.</summary>
public readonly record struct SceneRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public ScenePoint Centre => new(X + Width / 2, Y + Height / 2);

    public ScenePoint TopLeft => new(X, Y);

    public ScenePoint TopRight => new(Right, Y);

    public ScenePoint BottomLeft => new(X, Bottom);

    public ScenePoint BottomRight => new(Right, Bottom);

    public bool Contains(ScenePoint p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    /// <summary>A square of side <paramref name="side"/> centred on a point.</summary>
    public static SceneRect Square(ScenePoint centre, double side) =>
        new(centre.X - side / 2, centre.Y - side / 2, side, side);

    /// <summary>A rectangle of a given size, centred on a point.</summary>
    public static SceneRect FromCentre(ScenePoint centre, double width, double height) =>
        new(centre.X - width / 2, centre.Y - height / 2, width, height);
}

/// <summary>
/// One tapered section of the lamp — the cap, the bottle or the base — as the two half-widths
/// at its top and its bottom.
/// </summary>
/// <remarks>
/// The whole lamp is three of these stacked up, which is what lets the wax ask a plain question
/// — "how much room is there at this height" — and get the same answer the glass is drawn to.
/// Two sets of that arithmetic would be two chances for a blob to sit half outside its bottle.
/// </remarks>
public readonly record struct LampTaper(double CentreX, double Top, double Bottom, double HalfTop, double HalfBottom)
{
    public double Height => Bottom - Top;

    /// <summary>Half the width of this section at a given height, clamped to its own ends.</summary>
    public double HalfWidthAt(double y)
    {
        if (Height <= 0) return HalfBottom;

        var t = Math.Clamp((y - Top) / Height, 0, 1);
        return HalfTop + (HalfBottom - HalfTop) * t;
    }

    public ScenePoint Left(double y) => new(CentreX - HalfWidthAt(y), y);

    public ScenePoint Right(double y) => new(CentreX + HalfWidthAt(y), y);
}

/// <summary>Where the lamp sits and how big it is.</summary>
/// <remarks>
/// A mutable class rather than a record, because edit mode drags it in place while the tray
/// menu and the file are both looking at the same instance — the same arrangement
/// <see cref="LampConfig"/> relies on when it writes a finished edit straight back to disk.
/// </remarks>
public sealed class LampPlacement
{
    /// <summary>
    /// Where the centre of the lamp sits, as a fraction of the overlay: 0 is the left or top
    /// edge, 1 the right or the bottom.
    /// </summary>
    /// <remarks>
    /// A fraction rather than a pixel so the lamp survives the screen it was placed on — a
    /// laptop undocked from a 4K monitor would otherwise find it jammed in the corner.
    /// </remarks>
    public double AnchorX { get; set; } = 0.93;

    public double AnchorY { get; set; } = 0.62;

    /// <summary>How tall the lamp is, as a fraction of the overlay's height.</summary>
    /// <remarks>
    /// A height rather than the side of a square: a lava lamp is a tall thing, and its width
    /// follows from <see cref="LampScene.Aspect"/> rather than being anybody's to set.
    /// </remarks>
    public double Size { get; set; } = 0.34;

    public LampPlacement Copy() => new() { AnchorX = AnchorX, AnchorY = AnchorY, Size = Size };

    public void CopyFrom(LampPlacement other)
    {
        AnchorX = other.AnchorX;
        AnchorY = other.AnchorY;
        Size = other.Size;
    }
}

/// <summary>
/// Every measurement of the lamp at a given overlay size: the box it occupies, the three tapers
/// it is built from, and the edit-mode furniture.
/// </summary>
/// <remarks>
/// Separate from the drawing, the wax and the placement on purpose. The canvas needs it to draw,
/// the wax needs it to know where the walls of the glass are, and edit mode needs exactly the
/// same numbers to work out what the mouse is over — three copies of this arithmetic would be
/// three chances for the button you can press to sit somewhere other than the one you can see.
/// </remarks>
public readonly record struct LampLayout(
    SceneRect Box,
    ScenePoint Centre,
    LampTaper Cap,
    LampTaper Glass,
    LampTaper Stand,
    double HandleSize,
    SceneRect SaveButton,
    SceneRect CancelButton)
{
    public SceneRect Handle(LampGrip grip) => grip switch
    {
        LampGrip.ResizeTopLeft => SceneRect.Square(Box.TopLeft, HandleSize),
        LampGrip.ResizeTopRight => SceneRect.Square(Box.TopRight, HandleSize),
        LampGrip.ResizeBottomLeft => SceneRect.Square(Box.BottomLeft, HandleSize),
        LampGrip.ResizeBottomRight => SceneRect.Square(Box.BottomRight, HandleSize),
        _ => default,
    };

    /// <summary>The four corners, in the order the canvas draws them.</summary>
    public static readonly LampGrip[] Corners =
    [
        LampGrip.ResizeTopLeft,
        LampGrip.ResizeTopRight,
        LampGrip.ResizeBottomRight,
        LampGrip.ResizeBottomLeft,
    ];
}

/// <summary>
/// The lamp itself: where it stands, what colour it is currently showing, how hot it has got,
/// where every blob of wax is, and what the mouse is over while it is being moved. Deliberately
/// free of Avalonia — it is all arithmetic, so it can be tested without a window, which is the
/// only way the wax staying inside its own bottle was ever going to be checkable.
/// </summary>
public sealed class LampScene
{
    /// <summary>How wide the lamp is against its height. A lava lamp is a tall thing.</summary>
    public const double Aspect = 0.46;

    /// <summary>Shortest the lamp may be dragged, in logical pixels. Below this there is nothing to grab.</summary>
    public const double MinimumHeight = 90;

    /// <summary>
    /// How many blobs of wax there are. Enough that the bottle is never empty, few enough that
    /// they are still separate things rising past each other rather than one orange soup.
    /// </summary>
    private const int BlobCount = 7;

    /// <summary>Seconds for the lamp to fade out, or back in, across a change of state.</summary>
    private const double FadeSeconds = 0.35;

    /// <summary>Seconds the lamp sits blank between one state going out and the next coming in.</summary>
    /// <remarks>
    /// The whole point of the beat. Wax that changed colour in place would read as a rendering
    /// bug; a lamp that goes out and comes back a different colour reads as something having
    /// happened, which is what the toy is trying to say.
    /// </remarks>
    private const double ChangeoverPause = 0.12;

    /// <summary>Seconds from a cold lamp to wax running the full height of the glass.</summary>
    /// <remarks>
    /// The one bit of physics worth keeping. A real lamp takes the better part of an hour, which
    /// nobody is going to sit through — but a lamp whose wax was already climbing the instant it
    /// was switched on would not read as a lava lamp at all.
    /// </remarks>
    private const double WarmUpSeconds = 6.0;

    /// <summary>Seconds for the wax to settle back to the bottom once the lamp goes out.</summary>
    private const double CoolDownSeconds = 3.0;

    /// <summary>Seconds for a broken pipeline to take the lamp from a simmer to a rolling boil.</summary>
    private const double BoilSeconds = 2.4;

    /// <summary>Seconds for the boil to die down again once the pipeline is fixed.</summary>
    private const double SettleSeconds = 1.6;

    /// <summary>Seconds for one full breath of the build pulse — down and back up again.</summary>
    /// <remarks>
    /// Faster than a resting breath on purpose, and the one place this toy is allowed to catch
    /// the eye: "a build is running" is the state a person is most likely to be waiting on.
    /// </remarks>
    private const double PulseSeconds = 1.8;

    private readonly Random _random;
    private readonly LavaBlob[] _blobs;

    private LampState _state = LampState.Off;
    private double _pulsePhase;
    private double _pause;
    private LampPlacement? _beforeEdit;

    public LampScene(LampPlacement placement, int? randomSeed = null)
    {
        Placement = placement;
        _random = randomSeed is null ? new Random() : new Random(randomSeed.Value);
        _blobs = LavaBlob.Fill(_random, BlobCount);
    }

    public LampPlacement Placement { get; }

    /// <summary>The wax, in the order the canvas draws it.</summary>
    public IReadOnlyList<LavaBlob> Blobs => _blobs;

    public double Width { get; private set; } = 1920;

    public double Height { get; private set; } = 1080;

    /// <summary>
    /// What Greenlight last said. Setting it starts a changeover: what is on screen fades out,
    /// the state is swapped while the lamp is blank, and the new one fades in.
    /// </summary>
    public LampState State
    {
        get => _state;
        set
        {
            if (value == _state) return;
            _state = value;

            // Nothing on screen to put away, so there is nothing to wait for and no blank beat
            // worth showing — this is the first snapshot after a cold start.
            if (Fade <= 0)
            {
                Shown = value;
                _pause = 0;
            }
            else
            {
                _pause = ChangeoverPause;
            }
        }
    }

    /// <summary>
    /// The state the lamp is currently wearing. It lags <see cref="State"/> for as long as the
    /// changeover takes.
    /// </summary>
    /// <remarks>
    /// This is what keeps a boiling lamp red on its way out. Drawing from <see cref="State"/>
    /// instead would turn the wax green while it was still churning, which reads as the status
    /// having changed a third of a second before anything moved.
    /// </remarks>
    public LampState Shown { get; private set; } = LampState.Off;

    /// <summary>Whether the lamp is mid-swap: fading out, or sitting blank before it fades in.</summary>
    public bool IsChangingOver => Shown != _state;

    /// <summary>A build is running. Everything drawn breathes — Greenlight's own rule.</summary>
    public bool IsBuilding { get; set; }

    /// <summary>
    /// Show the lamp regardless of what Greenlight says. Edit mode turns this on: a lamp you
    /// cannot see is a lamp you cannot drag, and dragging it is the whole point.
    /// </summary>
    public bool ForceVisible { get; set; }

    /// <summary>
    /// Whether a dark, switched-off lamp is drawn when there is no Greenlight to ask.
    /// </summary>
    /// <remarks>
    /// On by default, and the same judgement the cars make by parking rather than vanishing: a
    /// blank desktop looks like the app crashed, where an unlit lamp looks like what it is.
    /// </remarks>
    public bool ShowWhenOff { get; set; } = true;

    /// <summary>
    /// How fast the wax runs, as a multiple of its ordinary pace. The one setting here that is
    /// pure taste: some people want it barely moving, some want it churning.
    /// </summary>
    public double Liveliness { get; set; } = 1.0;

    /// <summary>Whether the lamp is being moved and resized.</summary>
    public bool IsEditing => _beforeEdit is not null;

    /// <summary>
    /// How far in the lamp is, 0 to 1. Animated rather than switched, because a lamp that simply
    /// appeared in a different colour reads as a drawing bug and not as news.
    /// </summary>
    public double Fade { get; private set; }

    /// <summary>
    /// How hot the lamp is, 0 to 1: 0 is a cold bottle with the wax pooled in the bottom, 1 is
    /// wax running the full height of the glass. Climbs while the bulb is on and falls when it
    /// goes out.
    /// </summary>
    public double Warmth { get; private set; }

    /// <summary>
    /// How hard it is boiling, 0 to 1. Red drives this up; everything else lets it die down.
    /// </summary>
    /// <remarks>
    /// Eased both ways rather than switched, unlike the colour. The colour is the news; the boil
    /// is what the lamp then goes on doing for as long as the pipeline is broken, and a lamp
    /// that stopped dead the instant the fix landed would be claiming more than Greenlight said.
    /// </remarks>
    public double Boil { get; private set; }

    /// <summary>
    /// A small waver on the glow, well under a pixel of meaning. A perfectly steady glow looks
    /// printed on.
    /// </summary>
    public double Flicker { get; private set; } = 1.0;

    /// <summary>
    /// Where in the breath we are: 1 at the top, 0 at the bottom, and a flat 1 when nothing is
    /// building.
    /// </summary>
    public double Pulse => IsBuilding ? 0.5 * (1 + Math.Cos(_pulsePhase * 2 * Math.PI)) : 1;

    /// <summary>How bright the wax is this frame: 1 at rest, easing down and back up while a build runs.</summary>
    /// <remarks>
    /// The depth is deliberately spent mostly on the glow rather than on the wax. The eye reads a
    /// halo swelling far more readily than it reads a colour dimming — and "dimming" is
    /// uncomfortably close to "going out", which already means something else here.
    /// </remarks>
    public double Brightness => 0.78 + 0.22 * Pulse;

    /// <summary>How far the glow reaches this frame, as a multiple of its resting reach.</summary>
    public double Halo => (IsBuilding ? 0.82 + 0.45 * Pulse : 1.0) * Flicker;

    /// <summary>
    /// Whether the bulb is on. Off is a lamp switched off at the wall, not a lamp taken away —
    /// which is the whole difference between "Greenlight is not running" and "nothing is wrong".
    /// </summary>
    public bool IsLit => Shown != LampState.Off && !IsChangingOver;

    public void Resize(double width, double height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
    }

    /// <summary>Move the scene on by one frame.</summary>
    public void Advance(TimeSpan elapsed)
    {
        // A frame that arrives after the machine has been asleep, or after a breakpoint, is
        // worth clamping: a two-minute step would run the whole warm-up inside one frame, which
        // looks like a glitch rather than like time passing.
        var dt = Math.Clamp(elapsed.TotalSeconds, 0, 0.25);
        if (dt <= 0) return;

        _pulsePhase = (_pulsePhase + dt / PulseSeconds) % 1.0;

        if (IsChangingOver)
        {
            // Out, wait, then swap. Fading back in is the ordinary path below, on the frame
            // after the state has changed.
            if (Fade > 0) Fade = Math.Max(0, Fade - dt / FadeSeconds);
            else if (_pause > 0) _pause = Math.Max(0, _pause - dt);
            else Shown = _state;
        }
        else if (ForceVisible || ShowWhenOff || Shown != LampState.Off)
        {
            Fade = Math.Min(1, Fade + dt / FadeSeconds);
        }
        else
        {
            Fade = Math.Max(0, Fade - dt / FadeSeconds);
        }

        // The bulb, and then the wax's answer to it. Both directions are a ramp: wax that
        // dropped to the floor the instant Greenlight went away would read as the lamp having
        // been knocked over rather than switched off.
        Warmth = IsLit
            ? Math.Min(1, Warmth + dt / WarmUpSeconds)
            : Math.Max(0, Warmth - dt / CoolDownSeconds);

        Boil = Shown == LampState.Red && !IsChangingOver
            ? Math.Min(1, Boil + dt / BoilSeconds)
            : Math.Max(0, Boil - dt / SettleSeconds);

        // Well below a simmer even at full warmth. The wax has to be visibly slower when
        // everything is passing than when something is broken, or the boil says nothing.
        var pace = Liveliness * (0.18 + 0.82 * Warmth) * (1 + 1.5 * Boil) * (IsBuilding ? 1.3 : 1.0);
        foreach (var blob in _blobs) blob.Advance(dt, pace);

        Flicker = Fade > 0 ? 1 + (_random.NextDouble() - 0.5) * 0.04 : 1;
    }

    /// <summary>Bring the lamp straight in, warmed up and running. For entering edit mode.</summary>
    public void SnapVisible()
    {
        Shown = _state;
        _pause = 0;
        Fade = 1;

        // Warm, so there is a lamp to look at while it is being placed rather than a cold bottle
        // — and boiling if that is what it was, because entering edit mode is not news.
        if (Shown != LampState.Off) Warmth = 1;
        if (Shown == LampState.Red) Boil = 1;
    }

    // ── layout ────────────────────────────────────────────────────────────────

    /// <summary>Every measurement of the lamp at the current overlay size.</summary>
    /// <remarks>
    /// Three tapers make the lamp: a cap flaring down into the neck, the bottle itself running
    /// from that narrow neck out to a wide foot, and the base flaring again to meet the desk.
    /// All of them are fractions of the box, so it is the same lamp at every size.
    /// </remarks>
    public LampLayout Measure()
    {
        var height = LampHeight();
        var width = height * Aspect;
        var centre = new ScenePoint(Placement.AnchorX * Width, Placement.AnchorY * Height);
        var box = SceneRect.FromCentre(centre, width, height);

        var x = box.Centre.X;
        var capBottom = box.Y + height * 0.075;
        var glassBottom = box.Y + height * 0.715;

        var cap = new LampTaper(x, box.Y, capBottom, width * 0.105, width * 0.150);
        var glass = new LampTaper(x, capBottom, glassBottom, width * 0.150, width * 0.395);
        var stand = new LampTaper(x, glassBottom, box.Bottom, width * 0.395, width * 0.500);

        var handle = Math.Clamp(width * 0.30, 10, 22);

        // The buttons live above the box, unless there is no room above — at the top of the
        // screen they go underneath rather than off the edge, where they could not be pressed.
        var button = Math.Clamp(width * 0.55, 22, 40);
        var gap = button * 0.35;
        var buttonY = box.Y - gap - button < 0 ? box.Bottom + gap : box.Y - gap - button;

        var save = new SceneRect(box.Right - button, buttonY, button, button);
        var cancel = new SceneRect(box.Right - button * 2 - gap, buttonY, button, button);

        return new LampLayout(box, box.Centre, cap, glass, stand, handle, save, cancel);
    }

    /// <summary>Where one blob of wax goes this frame, inside the glass this layout describes.</summary>
    /// <remarks>
    /// On the scene rather than on the blob because warmth belongs to the lamp, not to the wax:
    /// every blob in the bottle is answering the same bulb.
    /// </remarks>
    public SceneBlob Place(LavaBlob blob, LampLayout layout) => blob.PlaceIn(layout.Glass, Warmth);

    /// <summary>The lamp's height in logical pixels, clamped to something draggable and something sane.</summary>
    public double LampHeight() => Math.Clamp(Height * Placement.Size, MinimumHeight, MaximumHeight);

    /// <summary>The tallest the lamp may be, and never shorter than the minimum.</summary>
    /// <remarks>
    /// Bounded by the overlay's width as well as its height, because the lamp has a width too. A
    /// bottle taller than the screen is the obvious case; on a very short wide overlay the two
    /// clamps would otherwise cross over and Math.Clamp would throw.
    /// </remarks>
    public double MaximumHeight => Math.Max(MinimumHeight, Math.Min(Height * 0.95, Width * 0.95 / Aspect));

    /// <summary>
    /// What is under the mouse. Edit mode uses it for the cursor and for the drag, and both have
    /// to agree with what the canvas drew.
    /// </summary>
    /// <remarks>
    /// Buttons first, then corners, then the body. The save button sits close to the box's own
    /// corner handle at small sizes, and a tick that resized the lamp instead of keeping it
    /// would be the single most annoying bug this thing could have.
    /// </remarks>
    public LampGrip HitTest(ScenePoint point)
    {
        var layout = Measure();

        if (layout.SaveButton.Contains(point)) return LampGrip.Save;
        if (layout.CancelButton.Contains(point)) return LampGrip.Cancel;

        foreach (var corner in LampLayout.Corners)
            if (layout.Handle(corner).Contains(point))
                return corner;

        return layout.Box.Contains(point) ? LampGrip.Body : LampGrip.None;
    }

    /// <summary>Carry the lamp somewhere else. <paramref name="centre"/> is where its middle lands.</summary>
    public void MoveTo(ScenePoint centre)
    {
        // Clamped by the box rather than by its centre: an anchor clamped to 0–1 would still let
        // half the lamp hang off the edge of the desktop, and half a lamp is half a thing to
        // grab hold of when you want it back.
        var height = LampHeight();

        Placement.AnchorX = Fraction(centre.X, height * Aspect / 2, Width);
        Placement.AnchorY = Fraction(centre.Y, height / 2, Height);
    }

    /// <summary>
    /// Drag a corner. The opposite corner stays where it is, and the lamp keeps its proportions —
    /// a bottle stretched to whatever shape the mouse was making would stop being a lava lamp
    /// somewhere around the second pixel.
    /// </summary>
    public void ResizeTo(LampGrip corner, ScenePoint point)
    {
        if (corner is not (LampGrip.ResizeTopLeft or LampGrip.ResizeTopRight
            or LampGrip.ResizeBottomLeft or LampGrip.ResizeBottomRight)) return;

        var box = Measure().Box;

        var anchored = corner switch
        {
            LampGrip.ResizeTopLeft => box.BottomRight,
            LampGrip.ResizeTopRight => box.BottomLeft,
            LampGrip.ResizeBottomLeft => box.TopRight,
            _ => box.TopLeft,
        };

        // Whichever of the two reaches asks for the taller lamp, so the box follows the mouse
        // rather than stalling when it moves along only one axis.
        var height = Math.Max(Math.Abs(point.Y - anchored.Y), Math.Abs(point.X - anchored.X) / Aspect);
        height = Math.Clamp(height, MinimumHeight, MaximumHeight);

        var signX = corner is LampGrip.ResizeTopRight or LampGrip.ResizeBottomRight ? 1 : -1;
        var signY = corner is LampGrip.ResizeBottomLeft or LampGrip.ResizeBottomRight ? 1 : -1;

        // Size before the move, so MoveTo clamps the new centre against the new half-height
        // rather than the old one — resizing towards an edge would otherwise push the box off it
        // and then refuse to bring it back.
        Placement.Size = Height <= 0 ? Placement.Size : height / Height;
        MoveTo(new ScenePoint(anchored.X + signX * height * Aspect / 2, anchored.Y + signY * height / 2));
    }

    // ── edit mode ─────────────────────────────────────────────────────────────

    /// <summary>Remember where the lamp was, so the cross has something to put it back to.</summary>
    /// <remarks>
    /// A second call while already editing is ignored rather than re-snapshotting. The tray can
    /// ask for edit mode while it is already on, and taking a fresh snapshot there would quietly
    /// turn the cross into a second tick.
    /// </remarks>
    public void BeginEdit() => _beforeEdit ??= Placement.Copy();

    /// <summary>Keep it where it has been dragged to.</summary>
    public void CommitEdit() => _beforeEdit = null;

    /// <summary>Put it back where it was when edit mode started.</summary>
    public void CancelEdit()
    {
        if (_beforeEdit is null) return;

        Placement.CopyFrom(_beforeEdit);
        _beforeEdit = null;
    }

    /// <summary>
    /// Where a coordinate sits in the overlay as a fraction, with the box kept fully on screen.
    /// </summary>
    private static double Fraction(double value, double half, double extent)
    {
        if (extent <= 0 || !double.IsFinite(value)) return 0.5;

        // A box taller than the overlay has no legal position at all, so it is centred rather
        // than clamped — Math.Clamp with a low above its high throws, and a 4K lamp dropped onto
        // a netbook is an ordinary way to arrive here.
        if (half * 2 >= extent) return 0.5;

        return Math.Clamp(value, half, extent - half) / extent;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Greenlight.LavaLampClient;

/// <summary>
/// Draws the lamp. Everything comes off the scene's numbers each frame — there are no controls,
/// because a window nobody can click has no use for layout or hit testing, and edit mode does
/// its own.
/// </summary>
/// <remarks>
/// The glow and the wax are both stacks of translucent ellipses rather than gradient brushes. It
/// is a few dozen filled ellipses a frame either way, it survives a wallpaper of any colour, it
/// does not depend on which of the gradient properties the current Avalonia calls what — and for
/// the wax it is the whole trick: soft-edged blobs drawn over each other run together where they
/// touch, which is the one thing a lava lamp has to do.
/// </remarks>
public sealed class LavaCanvas : Control
{
    /// <summary>
    /// How many rings the glow is built from.
    /// </summary>
    /// <remarks>
    /// Rather more than it looks like it needs. Each ring is a hard-edged ellipse, so a handful
    /// of them at a workable alpha reads as a target painted round the lamp rather than as light
    /// — the fix is many rings, each almost invisible on its own.
    /// </remarks>
    private const int GlowRings = 22;

    /// <summary>How many shells each blob of wax is built from.</summary>
    /// <remarks>
    /// Four is the fewest that still reads as wax rather than as a disc: an outer shell faint
    /// enough to feather into its neighbours, two middles, and a core bright enough to look lit
    /// from inside.
    /// </remarks>
    private const int BlobShells = 4;

    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.FromArgb(180, 13, 14, 17));
    private static readonly IPen PanelPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1);

    // The lamp itself: brushed metal for the cap and the base, and the dark fluid the wax sits in.
    private static readonly Color MetalLight = Color.FromRgb(176, 181, 190);
    private static readonly Color MetalMid = Color.FromRgb(116, 122, 132);
    private static readonly Color MetalDark = Color.FromRgb(58, 62, 70);
    private static readonly Color GlassEdge = Color.FromArgb(150, 226, 236, 248);

    private static readonly IBrush EditFill = new SolidColorBrush(Color.FromArgb(30, 120, 200, 255));
    private static readonly IPen EditPen = new Pen(
        new SolidColorBrush(Color.FromArgb(210, 150, 210, 255)), 1.5, new DashStyle([4, 3], 0));

    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromArgb(235, 245, 250, 255));
    private static readonly IPen HandlePen = new Pen(new SolidColorBrush(Color.FromArgb(220, 40, 60, 90)), 1);

    private static readonly IBrush SaveBrush = new SolidColorBrush(Color.FromArgb(240, 32, 160, 78));
    private static readonly IBrush CancelBrush = new SolidColorBrush(Color.FromArgb(240, 190, 48, 40));
    private static readonly IBrush ButtonHeld = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
    private static readonly IPen GlyphPen = new Pen(Brushes.White, 2.4)
    {
        LineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    };

    private readonly LampConfig _config;
    private readonly Dictionary<string, Color> _colours = [];

    public LavaCanvas(LampScene scene, LampConfig config)
    {
        Scene = scene;
        _config = config;
        IsHitTestVisible = false;
    }

    public LampScene Scene { get; }

    /// <summary>Whether the move-and-resize chrome is drawn.</summary>
    public bool IsEditing { get; set; }

    /// <summary>What is currently being dragged, so it can be shown as pressed.</summary>
    public LampGrip Held { get; set; }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Height <= 1 || Bounds.Width <= 1) return;

        var layout = Scene.Measure();

        // Faded all the way out and not being edited: nothing to draw, and nothing to spend a
        // frame on either.
        if (Scene.Fade > 0.001)
        {
            using var fade = context.PushOpacity(Ease(Scene.Fade));

            var look = _config.LookFor(Scene.Shown);
            var wax = Colour(look.Wax, fallback: Colors.Gray);
            var glow = Colour(look.Glow, fallback: wax);

            if (_config.ShowPanel) DrawPanel(context, layout);

            // A lamp that is off throws no light. An unlit bottle with a halo round it would be
            // claiming something.
            if (Scene.Shown != LampState.Off) DrawGlow(context, layout, glow);

            DrawStand(context, layout, glow);
            DrawBottle(context, layout, wax, glow);
            DrawCap(context, layout);
        }

        if (IsEditing) DrawEditChrome(context, layout);
    }

    // ── the furniture ─────────────────────────────────────────────────────────

    /// <summary>The dark panel the lamp stands against, so the wallpaper has no vote.</summary>
    private static void DrawPanel(DrawingContext context, LampLayout layout)
    {
        var box = layout.Box;
        var pad = box.Width * 0.18;
        var rect = new Rect(box.X - pad, box.Y - pad * 0.6, box.Width + pad * 2, box.Height + pad * 1.2);

        context.DrawRectangle(PanelBrush, PanelPen, new RoundedRect(rect, box.Width * 0.22));
    }

    /// <summary>
    /// The light coming off the bottle: concentric shells, each fainter and wider than the last.
    /// </summary>
    /// <remarks>
    /// Most of the build pulse is spent here rather than on the wax's own alpha. The eye reads a
    /// glow swelling and settling far more readily than it reads a colour dimming — and
    /// "dimming" is uncomfortably close to "going out", which already means something else here.
    /// </remarks>
    private void DrawGlow(DrawingContext context, LampLayout layout, Color colour)
    {
        if (_config.Glow <= 0) return;

        var glass = layout.Glass;
        var centre = new Point(glass.CentreX, (glass.Top + glass.Bottom) / 2);

        // Taller than it is wide, like the thing throwing it. A round halo round a tall bottle
        // reads as a sticker somebody put behind the lamp.
        var baseX = glass.HalfBottom * 1.15;
        var baseY = glass.Height * 0.55;
        var reach = 1 + 0.75 * _config.Glow * Scene.Halo;

        // Cold wax at the bottom of a lamp that has only just come on is not throwing much light
        // yet, and the glow climbing with the warmth is most of what sells the warm-up.
        var strength = (0.35 + 0.65 * Scene.Warmth) * Scene.Brightness;

        for (var i = GlowRings; i >= 1; i--)
        {
            var t = (double)i / GlowRings;
            var k = 1 + (reach - 1) * t;

            // Squared falloff, and an alpha low enough that no single shell has a visible edge —
            // what is seen is the two dozen of them piling up towards the bottle.
            var alpha = 0.075 * Math.Pow(1 - t, 2) * strength;
            if (alpha < 0.002) continue;

            context.DrawEllipse(new SolidColorBrush(colour, alpha), null, centre, baseX * k, baseY * k);
        }
    }

    /// <summary>The base: a flared metal stand, with the bottle's light caught on the top of it.</summary>
    private void DrawStand(DrawingContext context, LampLayout layout, Color glow)
    {
        var stand = layout.Stand;
        var metal = Taper(stand, bulge: -0.12);

        context.DrawGeometry(new SolidColorBrush(MetalMid), null, metal);

        // A lighter strip down the left and a dark one down the right, which is the whole of
        // "brushed metal" at this size. A flat grey trapezoid reads as a cardboard cutout.
        context.DrawGeometry(
            new SolidColorBrush(MetalLight, 0.55), null,
            Strip(stand, -0.62, -0.18));
        context.DrawGeometry(
            new SolidColorBrush(MetalDark, 0.65), null,
            Strip(stand, 0.34, 0.92));

        // The lamp lighting its own base. It is the join that makes the bottle look like it is
        // actually standing in the thing rather than in front of it — and it is clipped to the
        // metal, because light pooled on the desk beside the base reads as a stray ellipse
        // somebody forgot to delete.
        using (context.PushGeometryClip(metal))
        {
            context.DrawEllipse(
                new SolidColorBrush(glow, 0.34 * Scene.Warmth * Scene.Brightness), null,
                new Point(stand.CentreX, stand.Top),
                stand.HalfTop * 1.05, Math.Max(1, stand.Height * 0.30));
        }

        context.DrawLine(
            new Pen(new SolidColorBrush(MetalLight, 0.5), Math.Max(1, stand.Height * 0.04)),
            new Point(stand.CentreX - stand.HalfBottom, stand.Bottom),
            new Point(stand.CentreX + stand.HalfBottom, stand.Bottom));
    }

    /// <summary>The cap: the same metal, the other way up.</summary>
    private static void DrawCap(DrawingContext context, LampLayout layout)
    {
        var cap = layout.Cap;
        context.DrawGeometry(new SolidColorBrush(MetalMid), null, Taper(cap, bulge: -0.10));
        context.DrawGeometry(new SolidColorBrush(MetalLight, 0.55), null, Strip(cap, -0.62, -0.18));
        context.DrawGeometry(new SolidColorBrush(MetalDark, 0.65), null, Strip(cap, 0.34, 0.92));
    }

    // ── the bottle ────────────────────────────────────────────────────────────

    /// <summary>
    /// The glass, the fluid in it, and the wax: everything that actually says what the build is
    /// doing.
    /// </summary>
    /// <remarks>
    /// The wax is clipped to the bottle rather than trusted to stay inside it. The scene does
    /// keep it in — that is what <see cref="LavaBlob.PlaceIn"/> is for, and it is tested — but a
    /// blob is drawn as a soft shell wider than the radius it was placed at, and feathered edges
    /// spilling a pixel through the side of the glass is exactly the sort of thing that makes a
    /// desk toy look cheap.
    /// </remarks>
    private void DrawBottle(DrawingContext context, LampLayout layout, Color wax, Color glow)
    {
        var glass = layout.Glass;
        var bottle = Taper(glass, bulge: -0.05);

        // The fluid: the glow colour taken down almost to black. A clear bottle would leave the
        // wax floating in mid-air, and a bottle the colour of the wax would hide it.
        var fluid = Mix(glow, Color.FromRgb(8, 9, 12), 0.74);
        context.DrawGeometry(new SolidColorBrush(fluid, 0.88), null, bottle);

        using (context.PushGeometryClip(bottle))
        {
            DrawBulb(context, glass, glow);

            foreach (var blob in Scene.Blobs)
                DrawBlob(context, Scene.Place(blob, layout), wax);

            DrawGlassHighlight(context, glass);
        }

        context.DrawGeometry(null, new Pen(new SolidColorBrush(GlassEdge), Math.Max(1, glass.HalfBottom * 0.05)),
            bottle);
    }

    /// <summary>The bulb under the bottle, which is what the wax is answering.</summary>
    /// <remarks>
    /// It brightens with the warmth and again with the boil, so a broken pipeline is hotter at
    /// the bottom as well as faster — most of what reads as "boiling" is happening down here
    /// rather than in the speed of any one blob.
    /// </remarks>
    private void DrawBulb(DrawingContext context, LampTaper glass, Color glow)
    {
        var heat = (0.30 + 0.70 * Scene.Warmth) * (1 + 0.8 * Scene.Boil) * Scene.Brightness;
        var centre = new Point(glass.CentreX, glass.Bottom);

        for (var i = 3; i >= 1; i--)
        {
            var t = i / 3.0;
            context.DrawEllipse(
                new SolidColorBrush(Lighten(glow, 0.35 * (1 - t)), 0.34 * heat * (1 - t * 0.55)), null,
                centre, glass.HalfBottom * (0.55 + 0.85 * t), glass.Height * (0.06 + 0.16 * t));
        }
    }

    /// <summary>
    /// One blob of wax: a few shells from a soft edge in to a lit core.
    /// </summary>
    /// <remarks>
    /// Drawn without any blending mode of its own. Two blobs passing each other pile their outer
    /// shells up in the overlap, which is what makes them appear to merge and part again — the
    /// cheap version of a metaball, and at seven blobs it is indistinguishable from the dear one.
    /// </remarks>
    private void DrawBlob(DrawingContext context, SceneBlob blob, Color wax)
    {
        if (blob.RadiusX <= 0.5 || blob.RadiusY <= 0.5) return;

        var centre = new Point(blob.Centre.X, blob.Centre.Y);

        // Dimmer while the lamp is still cold, so wax lying on the floor of a lamp that has just
        // come on looks like wax rather than like a stripe of paint.
        var brightness = Scene.Brightness * (0.62 + 0.38 * Scene.Warmth);

        for (var i = 0; i < BlobShells; i++)
        {
            var t = (double)i / (BlobShells - 1);
            var k = 1 - t * 0.62;

            var colour = Lighten(Fade(wax, brightness), t * 0.45);
            var alpha = 0.30 + t * 0.50;

            context.DrawEllipse(new SolidColorBrush(colour, alpha), null, centre, blob.RadiusX * k, blob.RadiusY * k);
        }

        // The highlight off the shoulder of the blob, up and to the left, where the glass is.
        context.DrawEllipse(
            new SolidColorBrush(Colors.White, 0.16), null,
            new Point(centre.X - blob.RadiusX * 0.30, centre.Y - blob.RadiusY * 0.38),
            blob.RadiusX * 0.26, blob.RadiusY * 0.20);
    }

    /// <summary>The window reflected in the glass: one soft stripe down the left of the bottle.</summary>
    private static void DrawGlassHighlight(DrawingContext context, LampTaper glass)
    {
        var geometry = new StreamGeometry();
        var inset = 0.42;
        var width = glass.HalfBottom * 0.16;

        using (var ctx = geometry.Open())
        {
            var top = glass.Top + glass.Height * 0.06;
            var bottom = glass.Bottom - glass.Height * 0.10;

            ctx.BeginFigure(new Point(glass.CentreX - glass.HalfWidthAt(top) * inset, top), true);
            ctx.LineTo(new Point(glass.CentreX - glass.HalfWidthAt(top) * inset + width * 0.6, top));
            ctx.LineTo(new Point(glass.CentreX - glass.HalfWidthAt(bottom) * inset + width, bottom));
            ctx.LineTo(new Point(glass.CentreX - glass.HalfWidthAt(bottom) * inset, bottom));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(new SolidColorBrush(Colors.White, 0.10), null, geometry);
    }

    // ── edit mode ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The frame, the corner handles and the two buttons. Drawn outside the faded content on
    /// purpose: the chrome has to stay solid even when the lamp underneath it is mid-change, or
    /// the tick would blink while somebody was aiming at it.
    /// </summary>
    private void DrawEditChrome(DrawingContext context, LampLayout layout)
    {
        var box = layout.Box;
        context.DrawRectangle(EditFill, EditPen, new Rect(box.X, box.Y, box.Width, box.Height));

        foreach (var corner in LampLayout.Corners)
        {
            var handle = layout.Handle(corner);
            var fill = Held == corner ? SaveBrush : HandleBrush;
            context.DrawRectangle(
                fill, HandlePen,
                new RoundedRect(new Rect(handle.X, handle.Y, handle.Width, handle.Height), 2));
        }

        DrawButton(context, layout.SaveButton, SaveBrush, tick: true);
        DrawButton(context, layout.CancelButton, CancelBrush, tick: false);
    }

    private void DrawButton(DrawingContext context, SceneRect rect, IBrush fill, bool tick)
    {
        var centre = new Point(rect.Centre.X, rect.Centre.Y);
        var radius = rect.Width / 2;

        context.DrawEllipse(fill, HandlePen, centre, radius, radius);

        if (Held == (tick ? LampGrip.Save : LampGrip.Cancel))
            context.DrawEllipse(ButtonHeld, null, centre, radius, radius);

        var r = radius * 0.48;
        var pen = new Pen(Brushes.White, Math.Max(1.6, radius * 0.20))
        {
            LineCap = GlyphPen.LineCap,
            LineJoin = GlyphPen.LineJoin,
        };

        if (tick)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(centre.X - r, centre.Y + r * 0.05), false);
                ctx.LineTo(new Point(centre.X - r * 0.25, centre.Y + r * 0.72));
                ctx.LineTo(new Point(centre.X + r, centre.Y - r * 0.66));
                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }
        else
        {
            context.DrawLine(pen, new Point(centre.X - r, centre.Y - r), new Point(centre.X + r, centre.Y + r));
            context.DrawLine(pen, new Point(centre.X + r, centre.Y - r), new Point(centre.X - r, centre.Y + r));
        }
    }

    // ── the shapes ────────────────────────────────────────────────────────────

    /// <summary>
    /// One section of the lamp as a closed shape, with its sides curved by
    /// <paramref name="bulge"/> — negative for the waisted curve a bottle and a lamp base both
    /// have, zero for a plain trapezoid.
    /// </summary>
    /// <remarks>
    /// Straight sides are what makes a drawn lamp look like a traffic cone. The curve is small
    /// enough that nobody would name it and the difference is the whole silhouette.
    /// </remarks>
    private static Geometry Taper(LampTaper taper, double bulge)
    {
        var geometry = new StreamGeometry();
        var pull = (taper.HalfTop + taper.HalfBottom) * bulge;

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(taper.CentreX - taper.HalfTop, taper.Top), true);

            ctx.CubicBezierTo(
                new Point(taper.CentreX - taper.HalfTop + pull, taper.Top + taper.Height * 0.35),
                new Point(taper.CentreX - taper.HalfBottom + pull, taper.Top + taper.Height * 0.65),
                new Point(taper.CentreX - taper.HalfBottom, taper.Bottom));

            ctx.LineTo(new Point(taper.CentreX + taper.HalfBottom, taper.Bottom));

            ctx.CubicBezierTo(
                new Point(taper.CentreX + taper.HalfBottom - pull, taper.Top + taper.Height * 0.65),
                new Point(taper.CentreX + taper.HalfTop - pull, taper.Top + taper.Height * 0.35),
                new Point(taper.CentreX + taper.HalfTop, taper.Top));

            ctx.EndFigure(true);
        }

        return geometry;
    }

    /// <summary>
    /// A vertical band down a tapered section, between two fractions of its half-width. The
    /// light and dark sides of the metal.
    /// </summary>
    private static Geometry Strip(LampTaper taper, double from, double to)
    {
        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(taper.CentreX + taper.HalfTop * from, taper.Top), true);
            ctx.LineTo(new Point(taper.CentreX + taper.HalfTop * to, taper.Top));
            ctx.LineTo(new Point(taper.CentreX + taper.HalfBottom * to, taper.Bottom));
            ctx.LineTo(new Point(taper.CentreX + taper.HalfBottom * from, taper.Bottom));
            ctx.EndFigure(true);
        }

        return geometry;
    }

    // ── colour ────────────────────────────────────────────────────────────────

    /// <summary>Ease the fade, so a state change does not start and stop with a jolt.</summary>
    private static double Ease(double t)
    {
        var clamped = Math.Clamp(t, 0, 1);
        return clamped * clamped * (3 - 2 * clamped);
    }

    private static Color Mix(Color a, Color b, double t)
    {
        var k = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * k),
            (byte)(a.R + (b.R - a.R) * k),
            (byte)(a.G + (b.G - a.G) * k),
            (byte)(a.B + (b.B - a.B) * k));
    }

    /// <summary>Darken a colour towards nothing, for the breath. Alpha is left alone.</summary>
    private static Color Fade(Color colour, double brightness)
    {
        var k = Math.Clamp(brightness, 0, 1);
        return Color.FromArgb(colour.A, (byte)(colour.R * k), (byte)(colour.G * k), (byte)(colour.B * k));
    }

    /// <summary>Take a colour towards white, for the lit core of a blob and the bulb under it.</summary>
    private static Color Lighten(Color colour, double towards) =>
        Mix(colour, Colors.White, Math.Clamp(towards, 0, 1));

    /// <summary>
    /// A colour out of the config, parsed once and remembered.
    /// </summary>
    /// <remarks>
    /// Cached because this is asked several times a frame at 60fps, and because a hand-edited
    /// file is entitled to contain <c>"fluorescent"</c> — which must cost one failed parse and
    /// then nothing, rather than one per frame forever.
    /// </remarks>
    private Color Colour(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        if (_colours.TryGetValue(hex, out var cached)) return cached;

        var parsed = Color.TryParse(hex, out var colour) ? colour : fallback;
        _colours[hex] = parsed;
        return parsed;
    }
}

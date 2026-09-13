namespace Greenlight.LavaLampClient;

/// <summary>One blob of wax as it should be drawn this frame: where it is, and how it is squashed.</summary>
public readonly record struct SceneBlob(ScenePoint Centre, double RadiusX, double RadiusY)
{
    public double Left => Centre.X - RadiusX;

    public double Right => Centre.X + RadiusX;

    public double Top => Centre.Y - RadiusY;

    public double Bottom => Centre.Y + RadiusY;
}

/// <summary>
/// One blob of wax: how big it is, which part of the bottle it favours, and how far through its
/// own rise and fall it has got.
/// </summary>
/// <remarks>
/// <para>
/// Not a physics simulation, and deliberately not. Buoyancy against a temperature gradient is a
/// weekend that ends in wax stuck to the ceiling; what a lava lamp actually reads as is several
/// blobs, each on its own unhurried cycle, drifting past each other at slightly different speeds.
/// That is one number per blob, and it never goes wrong.
/// </para>
/// <para>
/// The cycle is a smoothstep of a triangle wave, which is what gives the two pauses that make it
/// read as wax rather than as a lift: a long flattened rest on the floor, a climb, a moment
/// gathered under the neck, and back down.
/// </para>
/// </remarks>
public sealed class LavaBlob
{
    private LavaBlob(double phase, double speed, double radius, double lane, double sway, double swayPhase,
        double swaySpeed)
    {
        Phase = phase;
        Speed = speed;
        Radius = radius;
        Lane = lane;
        Sway = sway;
        SwayPhase = swayPhase;
        SwaySpeed = swaySpeed;
    }

    /// <summary>How far through its own rise and fall, 0 to 1. 0 is resting on the floor.</summary>
    public double Phase { get; private set; }

    /// <summary>Cycles a second at full pace. Every blob gets its own, or they move as one lump.</summary>
    public double Speed { get; }

    /// <summary>How big it is, as a fraction of the widest half-width of the glass.</summary>
    public double Radius { get; }

    /// <summary>Which side of the bottle it favours, -1 to 1.</summary>
    public double Lane { get; }

    /// <summary>How far it wanders across the bottle as it goes, as a fraction of the room it has.</summary>
    public double Sway { get; }

    /// <summary>How far through that wander it is.</summary>
    public double SwayPhase { get; private set; }

    /// <summary>Cycles a second of the wander. Slower than the rise, so the drift is never a wobble.</summary>
    public double SwaySpeed { get; }

    /// <summary>
    /// How far up it has got, 0 to 1, eased at both ends. Multiplied by the lamp's warmth by
    /// whoever is placing it — a cold lamp keeps all of its wax on the floor.
    /// </summary>
    public double Lift
    {
        get
        {
            var triangle = Phase < 0.5 ? Phase * 2 : (1 - Phase) * 2;
            return triangle * triangle * (3 - 2 * triangle);
        }
    }

    /// <summary>A bottle's worth of wax, no two blobs alike.</summary>
    /// <remarks>
    /// Sorted biggest first so the canvas can draw them back to front: a big slow blob behind
    /// two small quick ones reads as depth, and the other order reads as a mistake.
    /// </remarks>
    public static LavaBlob[] Fill(Random random, int count)
    {
        var blobs = new LavaBlob[Math.Max(0, count)];

        for (var i = 0; i < blobs.Length; i++)
            blobs[i] = new LavaBlob(
                phase: random.NextDouble(),

                // Between about twelve and twenty-five seconds for a round trip at full pace.
                // Faster than that and it is a snow globe.
                speed: 0.040 + random.NextDouble() * 0.045,

                // Well under half the width of the bottle at its widest. Bigger than this and
                // every blob is pinned to the middle of the glass — there is no room left either
                // side of it to drift into, and seven of them queue up in one column like beads
                // on a string.
                radius: 0.20 + random.NextDouble() * 0.28,
                lane: random.NextDouble() * 2 - 1,
                sway: 0.25 + random.NextDouble() * 0.55,
                swayPhase: random.NextDouble(),
                swaySpeed: 0.05 + random.NextDouble() * 0.06);

        Array.Sort(blobs, (a, b) => b.Radius.CompareTo(a.Radius));
        return blobs;
    }

    /// <summary>Move this blob on by one frame. <paramref name="pace"/> is the lamp's heat and taste.</summary>
    public void Advance(double seconds, double pace)
    {
        Phase = Wrap(Phase + seconds * Speed * pace);

        // The drift does not slow down as much as the rise: wax that has settled on the floor of
        // a cooling lamp still shifts about a little, and a bottle that froze solid would read as
        // the app having hung.
        SwayPhase = Wrap(SwayPhase + seconds * SwaySpeed * (0.4 + 0.6 * pace));
    }

    /// <summary>
    /// Where this blob goes inside the glass, and how squashed it is, at a given warmth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything here is clamped against the walls the canvas is about to draw rather than
    /// merely aimed at the middle of them. The bottle tapers, so a blob that was comfortably
    /// inside it near the foot is wider than the neck it is climbing towards — and wax sticking
    /// out through the side of the glass is the one fault that would make the whole toy look
    /// broken.
    /// </para>
    /// <para>
    /// The squashing is the other half of that. Wax lying on the floor of a lamp is a flattened
    /// pool, not a ball resting on a point, and it spreads out as it flattens.
    /// </para>
    /// </remarks>
    public SceneBlob PlaceIn(LampTaper glass, double warmth)
    {
        var radius = Radius * glass.HalfBottom;
        if (radius <= 0 || glass.Height <= 0) return new SceneBlob(new ScenePoint(glass.CentreX, glass.Bottom), 0, 0);

        var travel = Lift * Math.Clamp(warmth, 0, 1);

        // Flattened on the floor and rounding up as it leaves — and spreading sideways as it
        // flattens, so the blob keeps roughly the amount of wax it started with.
        var risen = Math.Min(1, travel * 2.4);
        var radiusY = radius * (0.52 + 0.48 * risen);
        var radiusX = radius * (1.30 - 0.30 * risen);

        var floor = glass.Bottom - radiusY;
        var ceiling = Highest(glass, radiusX) + radiusY;
        if (ceiling > floor) ceiling = floor;

        var y = floor + (ceiling - floor) * travel;

        // The room actually left at this height, after the thickness of the glass. A blob wider
        // than the bottle at its own height is simply centred.
        var wall = glass.HalfBottom * 0.10;
        var room = Math.Max(0, glass.HalfWidthAt(y) - wall - radiusX);

        var drift = Math.Sin(SwayPhase * 2 * Math.PI) * Sway;
        var offset = Math.Clamp(Lane * 0.75 + drift * 0.45, -1, 1) * room;

        return new SceneBlob(new ScenePoint(glass.CentreX + offset, y), radiusX, radiusY);
    }

    /// <summary>
    /// The height at which the bottle has narrowed to meet a blob this wide — the highest its
    /// top may reach.
    /// </summary>
    /// <remarks>
    /// The taper is a straight line, so this is one division rather than a search. A big blob
    /// stops short of the neck, which is what real wax does and what stops the toy looking like
    /// it is drawing outside its own glass.
    /// </remarks>
    private static double Highest(LampTaper glass, double radiusX)
    {
        var wall = glass.HalfBottom * 0.10;
        var needed = radiusX + wall;
        if (glass.HalfTop >= needed) return glass.Top;

        var spread = glass.HalfBottom - glass.HalfTop;

        // A bottle with no taper at all, narrower than the blob: there is no height that fits, so
        // the blob stays on the floor rather than being placed somewhere arbitrary.
        if (spread <= 0) return glass.Bottom;

        var t = Math.Clamp((needed - glass.HalfTop) / spread, 0, 1);
        return glass.Top + t * glass.Height;
    }

    private static double Wrap(double value)
    {
        var wrapped = value % 1.0;
        return wrapped < 0 ? wrapped + 1 : wrapped;
    }
}

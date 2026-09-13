using System.Text.Json;
using System.Text.Json.Serialization;

namespace Greenlight.LavaLampClient;

/// <summary>
/// One state's colours: the wax, and the light the lamp throws.
/// </summary>
/// <remarks>
/// Two colours rather than one, because the glow is not simply the wax at a lower alpha — a hot
/// core reads as lit where a faded copy of the same green reads as a printing error.
/// </remarks>
public sealed class LampLook
{
    /// <summary>The wax. Any hex Avalonia can parse — <c>#3DFF6E</c>, <c>#CC3DFF6E</c>.</summary>
    public string Wax { get; set; } = "#3DFF6E";

    /// <summary>The light coming off the bottle, and what it spills onto the wall behind it.</summary>
    public string Glow { get; set; } = "#19C24B";
}

/// <summary>
/// The lamp, read from a JSON file the user can edit. Written out with the defaults the first
/// time it is missing, so "where do I change the green" has an answer that does not involve
/// rebuilding anything.
/// </summary>
/// <remarks>
/// Kept in AppData rather than beside the executable: the executable lives under <c>bin</c>,
/// which a rebuild is entitled to delete, and losing somebody's arrangement to a rebuild would
/// be its own small betrayal.
/// </remarks>
public sealed class LampConfig
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Greenlight.LavaLamp", "lavalamp.json");

    /// <summary>Where the lamp stands and how tall it is. Edit mode writes straight into this.</summary>
    public LampPlacement Lamp { get; set; } = new();

    /// <summary>
    /// No Greenlight to ask: the lamp is switched off. Cold grey wax, and no light at all.
    /// </summary>
    public LampLook WhenOff { get; set; } = new()
    {
        Wax = "#6A7079",
        Glow = "#2C3138",
    };

    /// <summary>Everything passing: green wax, rising gently.</summary>
    public LampLook WhenGreen { get; set; } = new()
    {
        Wax = "#4BFF86",
        Glow = "#14B546",
    };

    /// <summary>A pull request waiting on you: amber wax.</summary>
    public LampLook WhenAmber { get; set; } = new()
    {
        Wax = "#FFCE42",
        Glow = "#E08C0C",
    };

    /// <summary>A broken pipeline: red wax, and the lamp goes over to a rolling boil.</summary>
    public LampLook WhenRed { get; set; } = new()
    {
        Wax = "#FF4E3C",
        Glow = "#C11A0C",
    };

    /// <summary>
    /// How much of the screen the lamp may stand on. The work area by default, so a lamp dragged
    /// to the bottom edge does not end up over the Start button.
    /// </summary>
    public AreaChoice Area { get; set; } = AreaChoice.WorkArea;

    /// <summary>Overall opacity, for when the lamp is livelier than you want it to be.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// How far the glow reaches, as a multiple of the ordinary reach. Low is a sticker of a lamp;
    /// high is a lamp lighting up the wallpaper around it.
    /// </summary>
    public double Glow { get; set; } = 1.0;

    /// <summary>
    /// How fast the wax runs, as a multiple of its ordinary pace.
    /// </summary>
    /// <remarks>
    /// The only setting in the file that means nothing at all about the build. It is here because
    /// a lava lamp is a thing people have opinions about, and the pace that reads as calm to one
    /// person reads as stalled to the next.
    /// </remarks>
    public double Liveliness { get; set; } = 1.0;

    /// <summary>
    /// Draw the dark panel the lamp stands against.
    /// </summary>
    /// <remarks>
    /// On by default, and not decoration: a glowing bottle laid straight over somebody's
    /// photograph of a forest is unreadable, and the panel is what stops the wallpaper deciding
    /// whether the build is passing.
    /// </remarks>
    public bool ShowPanel { get; set; } = true;

    /// <summary>Draw an unlit lamp when Greenlight is away, rather than nothing at all.</summary>
    public bool ShowWhenOff { get; set; } = true;

    /// <summary>The colours for a given state. What the canvas asks, every frame.</summary>
    public LampLook LookFor(LampState state) => state switch
    {
        LampState.Green => WhenGreen,
        LampState.Amber => WhenAmber,
        LampState.Red => WhenRed,
        _ => WhenOff,
    };

    /// <summary>
    /// Load the file, writing the defaults out first if it is not there. A file that cannot be
    /// read or parsed falls back to the defaults rather than refusing to start: this is a desk
    /// toy, and a stray comma should not cost you the whole thing.
    /// </summary>
    public static LampConfig Load(string? path = null)
    {
        var file = path ?? DefaultPath;

        try
        {
            if (!File.Exists(file))
            {
                var fresh = new LampConfig();
                fresh.Save(file);
                return fresh;
            }

            var loaded = JsonSerializer.Deserialize<LampConfig>(File.ReadAllText(file), Json);
            if (loaded is null) return new LampConfig();

            // A file written before one of these existed deserializes it as null, and so does a
            // hand edit that deleted a block. Neither should be a crash on the next frame.
            var defaults = new LampConfig();
            loaded.Lamp ??= defaults.Lamp;
            loaded.WhenOff ??= defaults.WhenOff;
            loaded.WhenGreen ??= defaults.WhenGreen;
            loaded.WhenAmber ??= defaults.WhenAmber;
            loaded.WhenRed ??= defaults.WhenRed;

            loaded.Opacity = Math.Clamp(loaded.Opacity, 0.1, 1.0);
            loaded.Glow = Math.Clamp(loaded.Glow, 0.0, 2.5);

            // Not down to zero: a lava lamp with the wax stopped dead is a bottle, and somebody
            // who typed a 0 in here meant "slow", not "broken".
            loaded.Liveliness = Math.Clamp(loaded.Liveliness, 0.1, 4.0);

            // Clamped on the way in, not only on the way out. The file is hand-editable, and an
            // anchor of 12 or a size of -3 should give you a lamp you can find and drag back
            // rather than one that is somewhere off the side of the desktop.
            loaded.Lamp.AnchorX = Math.Clamp(loaded.Lamp.AnchorX, 0, 1);
            loaded.Lamp.AnchorY = Math.Clamp(loaded.Lamp.AnchorY, 0, 1);
            loaded.Lamp.Size = Math.Clamp(loaded.Lamp.Size, 0.05, 0.95);

            return loaded;
        }
        catch
        {
            return new LampConfig();
        }
    }

    public void Save(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // A toy that cannot write its config still runs perfectly well on the defaults.
        }
    }

    /// <summary>Take on everything from a freshly-read file, in place.</summary>
    /// <remarks>
    /// Copied into this instance rather than swapping it for the new one: the tray is holding
    /// this object, and it is the tray's menu that has to keep agreeing with the file.
    /// </remarks>
    public void CopyFrom(LampConfig other)
    {
        Lamp = other.Lamp;
        WhenOff = other.WhenOff;
        WhenGreen = other.WhenGreen;
        WhenAmber = other.WhenAmber;
        WhenRed = other.WhenRed;
        Area = other.Area;
        Opacity = other.Opacity;
        Glow = other.Glow;
        Liveliness = other.Liveliness;
        ShowPanel = other.ShowPanel;
        ShowWhenOff = other.ShowWhenOff;
    }
}

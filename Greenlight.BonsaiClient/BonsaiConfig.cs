using System.Text.Json;
using System.Text.Json.Serialization;

namespace Greenlight.BonsaiClient;

/// <summary>The colours the tree is drawn in. Any hex Avalonia can parse — <c>#4E9A3A</c>, <c>#CC4E9A3A</c>.</summary>
public sealed class BonsaiColours
{
    /// <summary>A healthy leaf. Each leaf varies a little either side of it.</summary>
    public string Leaf { get; set; } = "#4C9F38";

    /// <summary>A leaf on a tree that has been broken for a while.</summary>
    public string LeafSick { get; set; } = "#C8B23E";

    /// <summary>A leaf on a tree that has been broken all day.</summary>
    public string LeafDead { get; set; } = "#8A5A2B";

    /// <summary>What a leaf flushes to the moment a build breaks, on its way down.</summary>
    public string LeafBroken { get; set; } = "#E8392A";

    /// <summary>The bark.</summary>
    public string Wood { get; set; } = "#5E3D27";

    /// <summary>The glazed pot.</summary>
    public string Pot { get; set; } = "#36596E";

    /// <summary>The caterpillar a waiting pull request puts on the tree. Amber, for Greenlight's yellow.</summary>
    public string Caterpillar { get; set; } = "#F2B230";

    /// <summary>The butterfly it leaves as.</summary>
    public string Butterfly { get; set; } = "#FF8A2A";
}

/// <summary>
/// The tree's settings, read from a JSON file the user can edit and written out with the
/// defaults the first time it is missing. The tree's growth is not in here — that lives in
/// <c>tree.json</c> beside it, where a hand edit cannot cost anybody their tree.
/// </summary>
public sealed class BonsaiConfig
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Greenlight.Bonsai", "bonsai.json");

    /// <summary>Where the tree stands and how tall it is. Edit mode writes straight into this.</summary>
    public TreePlacement Tree { get; set; } = new();

    public BonsaiColours Colours { get; set; } = new();

    /// <summary>
    /// Hours a broken build takes to bring a healthy tree to its worst. A working day by default:
    /// break it at nine and leave it, and by five the tree is brown and mostly bare.
    /// </summary>
    public double DecayHours { get; set; } = 8;

    /// <summary>Hours a fixed tree takes to get its colour back.</summary>
    public double RecoveryHours { get; set; } = 2;

    public AreaChoice Area { get; set; } = AreaChoice.WorkArea;

    public double Opacity { get; set; } = 1.0;

    /// <summary>How much it moves in the breeze, as a multiple of the ordinary. The only setting that means nothing about the build.</summary>
    public double Liveliness { get; set; } = 1.0;

    /// <summary>The dark panel behind the tree, so the wallpaper does not decide whether the leaves look healthy.</summary>
    public bool ShowPanel { get; set; } = true;

    /// <summary>Draw a dormant, greyed tree when Greenlight is away, rather than nothing.</summary>
    public bool ShowWhenOff { get; set; } = true;

    public static BonsaiConfig Load(string? path = null)
    {
        var file = path ?? DefaultPath;

        try
        {
            if (!File.Exists(file))
            {
                var fresh = new BonsaiConfig();
                fresh.Save(file);
                return fresh;
            }

            var loaded = JsonSerializer.Deserialize<BonsaiConfig>(File.ReadAllText(file), Json);
            if (loaded is null) return new BonsaiConfig();

            loaded.Tree ??= new TreePlacement();
            loaded.Colours ??= new BonsaiColours();

            loaded.Opacity = Math.Clamp(loaded.Opacity, 0.1, 1.0);
            loaded.Liveliness = Math.Clamp(loaded.Liveliness, 0.0, 4.0);
            loaded.DecayHours = Math.Clamp(loaded.DecayHours, 0.05, 24 * 14);
            loaded.RecoveryHours = Math.Clamp(loaded.RecoveryHours, 0.05, 24 * 14);

            loaded.Tree.AnchorX = Math.Clamp(loaded.Tree.AnchorX, 0, 1);
            loaded.Tree.AnchorY = Math.Clamp(loaded.Tree.AnchorY, 0, 1);
            loaded.Tree.Size = Math.Clamp(loaded.Tree.Size, 0.05, 0.95);

            return loaded;
        }
        catch
        {
            return new BonsaiConfig();
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

    /// <summary>Take on everything from a freshly-read file, in place — the tray holds this instance.</summary>
    public void CopyFrom(BonsaiConfig other)
    {
        Tree = other.Tree;
        Colours = other.Colours;
        DecayHours = other.DecayHours;
        RecoveryHours = other.RecoveryHours;
        Area = other.Area;
        Opacity = other.Opacity;
        Liveliness = other.Liveliness;
        ShowPanel = other.ShowPanel;
        ShowWhenOff = other.ShowWhenOff;
    }
}

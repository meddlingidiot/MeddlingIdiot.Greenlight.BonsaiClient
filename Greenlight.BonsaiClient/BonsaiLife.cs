using System.Text.Json;
using Greenlight.Sdk.Protocol;

namespace Greenlight.BonsaiClient;

/// <summary>
/// Everything the tree has to remember between runs. Small on purpose: the shape comes back from
/// <see cref="Seed"/>, and which leaves are on it follows from <see cref="Leaves"/>, because
/// <see cref="BonsaiTree.Slots"/> fills from the front.
/// </summary>
/// <remarks>
/// Kept apart from the config file. The config is the user's to edit; this is the tree's own
/// diary, and a hand edit that broke the JSON should cost somebody their colours, not their tree.
/// </remarks>
public sealed class BonsaiLife
{
    /// <summary>Which tree this is. Picked once, at planting, and never again.</summary>
    public int Seed { get; set; }

    /// <summary>When it was planted, for the tray to brag about.</summary>
    public DateTimeOffset PlantedAt { get; set; }

    /// <summary>Every passing build the tree has ever seen. Drives the branches.</summary>
    public int Lifetime { get; set; }

    /// <summary>
    /// Leaves on the tree, which can briefly run ahead of what the branches have room for — a
    /// passing build while a branch is still pushing out is banked, not lost.
    /// </summary>
    public int Leaves { get; set; }

    /// <summary>1 is a healthy tree, 0 is a tree that has been ignored for a working day.</summary>
    public double Health { get; set; } = 1.0;

    /// <summary>
    /// Whether the last colour seen was red. Remembered so that a restart in the middle of a
    /// broken build does not shake a second load of leaves off for the same breakage.
    /// </summary>
    public bool WasRed { get; set; }

    /// <summary>
    /// The builds already counted, newest last. Capped: Greenlight only reports builds inside its
    /// retention window, so nothing older than that can come round again.
    /// </summary>
    public List<string> Counted { get; set; } = [];

    public static BonsaiLife Plant(int? seed = null, DateTimeOffset? at = null) => new()
    {
        Seed = seed ?? Random.Shared.Next(),
        PlantedAt = at ?? DateTimeOffset.Now,
    };
}

/// <summary>What one snapshot did to the tree.</summary>
/// <param name="Grown">Leaves added for builds that passed since the last look.</param>
/// <param name="Broken">Leaves shaken off because the colour has just gone red. Zero otherwise.</param>
public readonly record struct Observation(int Grown, int Broken);

/// <summary>
/// Looks after the tree: counts passing builds into leaves, knocks leaves off when something
/// breaks, and wilts or heals it with the passing of time. Free of Avalonia and of the clock, so
/// a month in the life of a tree is a unit test.
/// </summary>
public sealed class Gardener
{
    /// <summary>How many build ids are remembered. Far beyond any retention window Greenlight has.</summary>
    public const int MemoryLength = 2000;

    /// <summary>Share of the leaves that turn red and drop the moment a build breaks.</summary>
    public const double BreakShare = 0.15;

    /// <summary>The fewest leaves a break shakes off, if there are that many, so even a sapling notices.</summary>
    public const int BreakMinimum = 3;

    /// <summary>The most a break shakes off at once. Sixty leaves raining down is a screensaver.</summary>
    public const int BreakMaximum = 40;

    /// <summary>How much of the tree is left after <c>DecayHours</c> of red, before the burst is counted.</summary>
    /// <remarks>
    /// A tenth rather than none: the slow shed runs down exponentially, so the tree thins quickly
    /// at first and holds its last leaves for a long time — a broken build over a weekend leaves
    /// a sorry-looking tree, not a stick.
    /// </remarks>
    private const double LeftAfterDecay = 0.10;

    /// <summary>A passing build while nothing is broken is a little water: this much health back.</summary>
    public const double PassTonic = 0.04;

    private double _shedOwed;

    public Gardener(BonsaiLife life, BonsaiTree tree)
    {
        Life = life;
        Tree = tree;
    }

    public BonsaiLife Life { get; }

    public BonsaiTree Tree { get; }

    /// <summary>Hours of red that take a healthy tree to none.</summary>
    public double DecayHours { get; set; } = 8;

    /// <summary>Hours of not-red that take a dying tree back to full health.</summary>
    public double RecoveryHours { get; set; } = 2;

    /// <summary>Set by anything that changed <see cref="Life"/>. The app saves and clears it.</summary>
    public bool IsDirty { get; set; }

    /// <summary>Leaves the tree is actually showing — the ones the branches have grown room for.</summary>
    public int VisibleLeaves => Math.Min(Life.Leaves, Tree.Capacity(Life.Lifetime));

    /// <summary>Take in a snapshot: grow a leaf per newly passed build, and notice the colour going red.</summary>
    public Observation Observe(GreenlightSnapshot snapshot)
    {
        var grown = 0;

        foreach (var build in snapshot.Builds)
        {
            if (build.Status != GreenlightBuildStatus.Completed || build.Result != GreenlightBuildResult.Succeeded)
                continue;

            var key = Key(build);
            if (Life.Counted.Contains(key)) continue;

            Life.Counted.Add(key);
            grown++;
        }

        if (Life.Counted.Count > MemoryLength) Life.Counted.RemoveRange(0, Life.Counted.Count - MemoryLength);

        if (grown > 0)
        {
            Life.Lifetime += grown;
            Life.Leaves = Math.Min(Tree.Slots.Count, Life.Leaves + grown);

            // A passing build on a tree that is not currently broken waters it a little, so a
            // tree that has just recovered from a bad afternoon finishes healing faster under a
            // run of green builds than it would sitting idle.
            if (snapshot.Status != GreenlightStatus.Red)
                Life.Health = Math.Min(1, Life.Health + PassTonic * grown);

            IsDirty = true;
        }

        var broken = 0;

        // Unknown says nothing either way, so it neither breaks the tree nor clears the memory of
        // it having been broken.
        if (snapshot.Status == GreenlightStatus.Red && !Life.WasRed)
        {
            broken = Shed(BreakCount(VisibleLeaves));
            Life.WasRed = true;
            IsDirty = true;
        }
        else if (snapshot.Status is (GreenlightStatus.Green or GreenlightStatus.Yellow) && Life.WasRed)
        {
            Life.WasRed = false;
            _shedOwed = 0;
            IsDirty = true;
        }

        return new Observation(grown, broken);
    }

    /// <summary>
    /// Let time pass. Red wilts the tree and slowly sheds it; anything else lets it heal. Returns
    /// how many leaves dropped while it did. Nothing happens while Greenlight is away — a tree
    /// nobody can see the state of should neither die nor recover on a guess.
    /// </summary>
    public int Tend(TimeSpan elapsed, GreenlightStatus status)
    {
        var hours = Math.Max(0, elapsed.TotalHours);
        if (hours <= 0 || status == GreenlightStatus.Unknown) return 0;

        if (status == GreenlightStatus.Red)
        {
            var before = Life.Health;
            Life.Health = Math.Max(0, Life.Health - hours / Math.Max(0.01, DecayHours));

            // Exponential: the same share of whatever is left, every hour, so the fall-rate tracks
            // how much is still on the tree. Fractions of a leaf are carried until they add up.
            var rate = -Math.Log(LeftAfterDecay) / Math.Max(0.01, DecayHours);
            _shedOwed += VisibleLeaves * (1 - Math.Exp(-rate * hours));

            var shed = (int)Math.Floor(_shedOwed);
            _shedOwed -= shed;
            shed = Shed(shed);

            if (shed > 0 || Math.Abs(before - Life.Health) > 0) IsDirty = true;
            return shed;
        }

        if (Life.Health < 1)
        {
            Life.Health = Math.Min(1, Life.Health + hours / Math.Max(0.01, RecoveryHours));
            IsDirty = true;
        }

        return 0;
    }

    /// <summary>How many leaves a break shakes off a tree carrying <paramref name="leaves"/>.</summary>
    public static int BreakCount(int leaves) =>
        Math.Min(leaves, Math.Clamp((int)Math.Round(leaves * BreakShare), BreakMinimum, BreakMaximum));

    /// <summary>
    /// Take leaves off the tree. Banked leaves the branches have no room for yet go first — they
    /// were never on show — so a shed is always visible leaves falling.
    /// </summary>
    private int Shed(int count)
    {
        Life.Leaves = VisibleLeaves;
        var shed = Math.Clamp(count, 0, Life.Leaves);
        Life.Leaves -= shed;
        return shed;
    }

    /// <summary>
    /// What makes one build itself and not another. The id alone is not enough: Azure DevOps
    /// and GitHub number their builds independently, and both happily reach 4031.
    /// </summary>
    public static string Key(GreenlightBuild build) => $"{build.Provider}:{build.Project}:{build.Id}";

    // ── on disk ───────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Greenlight.Bonsai", "tree.json");

    /// <summary>
    /// Read the tree back, or plant a new one if there is none. A file that will not parse is
    /// put aside rather than overwritten: somebody's tree is worth a second look by hand.
    /// </summary>
    public static BonsaiLife Load(string? path = null)
    {
        var file = path ?? DefaultPath;

        try
        {
            if (File.Exists(file))
            {
                var life = JsonSerializer.Deserialize<BonsaiLife>(File.ReadAllText(file), Json);
                if (life is not null)
                {
                    life.Counted ??= [];
                    life.Health = double.IsFinite(life.Health) ? Math.Clamp(life.Health, 0, 1) : 1;
                    life.Lifetime = Math.Max(0, life.Lifetime);
                    life.Leaves = Math.Max(0, life.Leaves);
                    return life;
                }
            }
        }
        catch
        {
            try { File.Move(file, file + ".unreadable", overwrite: true); }
            catch { /* nothing more we can do; a fresh tree it is */ }
        }

        var fresh = BonsaiLife.Plant();
        Save(fresh, file);
        return fresh;
    }

    public static void Save(BonsaiLife life, string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            // Written beside and swapped in, so a crash halfway through a write cannot cost
            // somebody a tree they have been growing since spring.
            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(life, Json));
            File.Move(temp, file, overwrite: true);
        }
        catch
        {
            // The tree is still in memory; the next save will try again.
        }
    }
}

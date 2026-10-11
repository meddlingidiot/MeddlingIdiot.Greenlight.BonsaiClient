namespace Greenlight.BonsaiClient;

/// <summary>Why a leaf is coming off.</summary>
public enum LeafFate
{
    /// <summary>A build broke. The leaf flushes red and lets go.</summary>
    Broken,

    /// <summary>The build has stayed broken. The leaf has browned and gives up.</summary>
    Wilted,
}

/// <summary>A leaf still on the branch but on its way: changing colour, then letting go.</summary>
public sealed class TurningLeaf(int slot, LeafFate fate, double delay)
{
    /// <summary>Seconds a leaf spends changing colour before it drops.</summary>
    public const double TurnSeconds = 0.7;

    public int Slot { get; } = slot;

    public LeafFate Fate { get; } = fate;

    /// <summary>Seconds until it starts to turn — staggered, so a break is a cascade, not a blink.</summary>
    public double Delay { get; set; } = delay;

    /// <summary>0 still its old colour, 1 fully turned and about to drop.</summary>
    public double Turn { get; set; }
}

/// <summary>A leaf in the air, or lying where it landed.</summary>
public sealed class FallingLeaf
{
    /// <summary>Seconds a landed leaf lies there before it starts to fade.</summary>
    public const double RestSeconds = 8;

    /// <summary>Seconds a landed leaf takes to fade away.</summary>
    public const double FadeSeconds = 2;

    public required TreePoint Position { get; set; }

    public required LeafFate Fate { get; init; }

    public double Shade { get; init; }

    public double Angle { get; set; }

    public double Spin { get; set; }

    public double VelocityX { get; set; }

    public double VelocityY { get; set; }

    /// <summary>Phase of the side-to-side flutter a falling leaf does instead of dropping straight.</summary>
    public double Flutter { get; set; }

    /// <summary>Where it will come to rest: the soil inside the pot, or the desk beside it.</summary>
    public double Ground { get; init; }

    public bool Landed { get; set; }

    /// <summary>Seconds since it landed.</summary>
    public double Resting { get; set; }

    /// <summary>1 fully there, 0 gone.</summary>
    public double Opacity => Landed ? Math.Clamp(1 - (Resting - RestSeconds) / FadeSeconds, 0, 1) : 1;

    public bool IsGone => Landed && Resting >= RestSeconds + FadeSeconds;
}

/// <summary>What a caterpillar is doing.</summary>
public enum CaterpillarActivity
{
    Crawling,

    /// <summary>At the end of its branch, eating. Head down, chewing.</summary>
    Munching,
}

/// <summary>
/// One pull request waiting on you, made flesh. It inches up and down a branch, eats at the end
/// of it, and gets fatter the longer the review waits.
/// </summary>
public sealed class Caterpillar(string key, int limb)
{
    /// <summary>Body segments, head first.</summary>
    public const int Segments = 7;

    /// <summary>Seconds for one inch: bunch up, then stretch out.</summary>
    public const double InchSeconds = 1.3;

    /// <summary>Seconds of munching at the end of a branch before it turns round.</summary>
    public const double MunchSeconds = 4.5;

    /// <summary>The pull request it stands for.</summary>
    public string Key { get; } = key;

    /// <summary>The branch it is on — an index into <see cref="BonsaiTree.Branches"/>, or -1 for the trunk.</summary>
    public int Limb { get; } = limb;

    /// <summary>How far along its limb the head is, 0 at the trunk end and 1 at the tip.</summary>
    public double Along { get; set; } = 0.2;

    /// <summary>+1 heading for the tip, -1 heading back.</summary>
    public int Direction { get; set; } = 1;

    /// <summary>0–1 through the current inch.</summary>
    public double Inch { get; set; }

    public CaterpillarActivity Activity { get; set; }

    public double Munch { get; set; }

    /// <summary>How fat: 1 for a fresh pull request, up to <see cref="MaximumSize"/> for one ignored for days.</summary>
    public double Size { get; set; } = 1;

    /// <summary>The fattest a caterpillar gets. Big enough to be a reproach, small enough to stay on a branch.</summary>
    public const double MaximumSize = 1.8;

    /// <summary>0 when it has just arrived, 1 once it has faded in.</summary>
    public double Appear { get; set; }

    /// <summary>
    /// How hard it is bunched up this instant: 0 stretched out, 1 a full arch. Rises through the
    /// first half of an inch as the tail catches up, falls through the second as the head reaches.
    /// </summary>
    public double Hump => Math.Sin(Math.Clamp(Inch, 0, 1) * Math.PI);

    /// <summary>How fat a caterpillar is for a review that has waited this long.</summary>
    public static double SizeFor(TimeSpan waiting) =>
        1 + (MaximumSize - 1) * Math.Clamp(waiting.TotalHours / 72, 0, 1);
}

/// <summary>A reviewed pull request's caterpillar, leaving.</summary>
public sealed class Butterfly
{
    /// <summary>Seconds from taking off to gone.</summary>
    public const double LifeSeconds = 7;

    public required TreePoint Position { get; set; }

    public double Size { get; init; } = 1;

    /// <summary>Which way it is drifting, left or right.</summary>
    public int Heading { get; init; } = 1;

    /// <summary>Wing beat phase.</summary>
    public double Flap { get; set; }

    public double Age { get; set; }

    /// <summary>The wings open and close; 1 fully spread, near 0 edge-on.</summary>
    public double WingOpen => 0.15 + 0.85 * Math.Abs(Math.Cos(Flap * Math.PI * 2));

    public double Opacity => Math.Clamp((LifeSeconds - Age) / 1.5, 0, 1) * Math.Clamp(Age / 0.4, 0, 1);

    public bool IsGone => Age >= LifeSeconds;
}

/// <summary>A pull request waiting on you, as the scene needs it: who it is, and how long it has waited.</summary>
public readonly record struct WaitingReview(string Key, TimeSpan Waiting);

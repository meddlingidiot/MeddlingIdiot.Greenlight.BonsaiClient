namespace Greenlight.BonsaiClient;

/// <summary>What the tree is doing, which is Greenlight's aggregate colour by another name.</summary>
public enum TreeMood
{
    /// <summary>
    /// No Greenlight attached. The tree goes dormant: greyed, still, and neither wilting nor
    /// healing — a tree that died because Greenlight was not running would be lying about the
    /// build.
    /// </summary>
    Off,

    /// <summary>Everything passing.</summary>
    Green,

    /// <summary>A pull request wants you. The caterpillars say which.</summary>
    Amber,

    /// <summary>A pipeline is broken. The tree wilts until it is fixed.</summary>
    Red,
}

/// <summary>What the mouse is over, in edit mode.</summary>
public enum TreeGrip
{
    /// <summary>Nothing. A click here means "I am finished".</summary>
    None,

    /// <summary>The tree itself — drag to carry it around the desktop.</summary>
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

    public static SceneRect Square(ScenePoint centre, double side) =>
        new(centre.X - side / 2, centre.Y - side / 2, side, side);

    public static SceneRect FromCentre(ScenePoint centre, double width, double height) =>
        new(centre.X - width / 2, centre.Y - height / 2, width, height);
}

/// <summary>Where the tree sits and how big it is.</summary>
/// <remarks>
/// A mutable class rather than a record, because edit mode drags it in place while the tray menu
/// and the file are both looking at the same instance.
/// </remarks>
public sealed class TreePlacement
{
    /// <summary>
    /// Where the centre of the tree's box sits, as a fraction of the overlay — a fraction rather
    /// than a pixel so the tree survives being undocked from a bigger monitor.
    /// </summary>
    public double AnchorX { get; set; } = 0.88;

    public double AnchorY { get; set; } = 0.74;

    /// <summary>How tall the tree's box is, as a fraction of the overlay's height.</summary>
    public double Size { get; set; } = 0.36;

    public TreePlacement Copy() => new() { AnchorX = AnchorX, AnchorY = AnchorY, Size = Size };

    public void CopyFrom(TreePlacement other)
    {
        AnchorX = other.AnchorX;
        AnchorY = other.AnchorY;
        Size = other.Size;
    }
}

/// <summary>
/// Every measurement of the tree at a given overlay size: the box, where the soil is, how big a
/// tree unit is, and the edit-mode furniture. One copy of the arithmetic, shared by the drawing
/// and the hit test, so the button you press is the button you see.
/// </summary>
/// <param name="Origin">The middle of the soil, in pixels — tree unit (0, 0).</param>
/// <param name="Unit">Pixels per tree unit.</param>
public readonly record struct TreeLayout(
    SceneRect Box,
    ScenePoint Origin,
    double Unit,
    double HandleSize,
    SceneRect SaveButton,
    SceneRect CancelButton)
{
    /// <summary>A tree point on the screen.</summary>
    public ScenePoint ToScreen(TreePoint p) => new(Origin.X + p.X * Unit, Origin.Y + p.Y * Unit);

    public SceneRect Handle(TreeGrip grip) => grip switch
    {
        TreeGrip.ResizeTopLeft => SceneRect.Square(Box.TopLeft, HandleSize),
        TreeGrip.ResizeTopRight => SceneRect.Square(Box.TopRight, HandleSize),
        TreeGrip.ResizeBottomLeft => SceneRect.Square(Box.BottomLeft, HandleSize),
        TreeGrip.ResizeBottomRight => SceneRect.Square(Box.BottomRight, HandleSize),
        _ => default,
    };

    public static readonly TreeGrip[] Corners =
    [
        TreeGrip.ResizeTopLeft,
        TreeGrip.ResizeTopRight,
        TreeGrip.ResizeBottomRight,
        TreeGrip.ResizeBottomLeft,
    ];
}

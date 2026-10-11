namespace Greenlight.BonsaiClient;

/// <summary>
/// A point in tree units: the soil in the middle of the pot is the origin, one unit is the
/// height of a full-grown tree, and up is negative — the same way round as the screen, so the
/// canvas never has to remember to flip anything.
/// </summary>
public readonly record struct TreePoint(double X, double Y)
{
    public static TreePoint operator +(TreePoint a, TreePoint b) => new(a.X + b.X, a.Y + b.Y);

    public static TreePoint operator -(TreePoint a, TreePoint b) => new(a.X - b.X, a.Y - b.Y);

    public static TreePoint operator *(TreePoint a, double k) => new(a.X * k, a.Y * k);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public static TreePoint Lerp(TreePoint a, TreePoint b, double t) => a + (b - a) * t;
}

/// <summary>A run of wood: the trunk, a branch, or a twig inside a pad.</summary>
/// <param name="Points">From the end nearest the soil outwards.</param>
/// <param name="BaseWidth">Thickness at the first point, in tree units.</param>
/// <param name="TipWidth">Thickness at the last point.</param>
/// <param name="UnlockAt">The lifetime count of passing builds at which this starts to grow.</param>
/// <param name="Pad">The foliage pad it carries, or -1 for the trunk.</param>
public sealed record TreeLimb(
    IReadOnlyList<TreePoint> Points,
    double BaseWidth,
    double TipWidth,
    int UnlockAt,
    int Pad)
{
    /// <summary>Total length along the points.</summary>
    public double Length { get; } = Measure(Points);

    /// <summary>
    /// The point a fraction of the way along, by distance rather than by point index — a
    /// caterpillar walking by index would sprint down the long segments and crawl the short ones.
    /// </summary>
    public TreePoint At(double fraction)
    {
        if (Points.Count == 0) return default;
        if (Points.Count == 1 || Length <= 0) return Points[0];

        var target = Math.Clamp(fraction, 0, 1) * Length;
        for (var i = 1; i < Points.Count; i++)
        {
            var step = (Points[i] - Points[i - 1]).Length;
            if (target <= step || i == Points.Count - 1)
                return TreePoint.Lerp(Points[i - 1], Points[i], step <= 0 ? 0 : Math.Clamp(target / step, 0, 1));
            target -= step;
        }

        return Points[^1];
    }

    /// <summary>Thickness a fraction of the way along.</summary>
    public double WidthAt(double fraction) => BaseWidth + (TipWidth - BaseWidth) * Math.Clamp(fraction, 0, 1);

    private static double Measure(IReadOnlyList<TreePoint> points)
    {
        var total = 0.0;
        for (var i = 1; i < points.Count; i++) total += (points[i] - points[i - 1]).Length;
        return total;
    }
}

/// <summary>
/// A cloud of foliage at the end of a branch — the flat, layered pads that make a bonsai read as
/// a bonsai rather than as a small tree.
/// </summary>
public sealed record TreePad(TreePoint Centre, double RadiusX, double RadiusY, int UnlockAt, int Limb);

/// <summary>Somewhere a leaf can hang.</summary>
/// <param name="Position">Where, before any sway or droop.</param>
/// <param name="Angle">Which way the leaf points, in radians, 0 being to the right.</param>
/// <param name="Pad">The pad it belongs to, which is what it droops with.</param>
/// <param name="ReadyAt">The lifetime count from which a leaf may grow here.</param>
/// <param name="Shade">0–1, a fixed per-leaf variation so a pad is not one flat green.</param>
public sealed record LeafSlot(TreePoint Position, double Angle, int Pad, int ReadyAt, double Shade);

/// <summary>
/// The shape of one tree: its trunk, its branches, its pads and every place a leaf can grow, in
/// the order leaves arrive. Generated once from a seed, so the same tree comes back after every
/// restart — leaf 40 is always the same leaf.
/// </summary>
/// <remarks>
/// <para>
/// The ordering is the trick that lets the saved state be two integers. Leaves fill
/// <see cref="Slots"/> from the front and fall from the back, so "how many leaves" is the whole
/// of "which leaves", and nothing about the drawing needs persisting.
/// </para>
/// <para>
/// Growth is unlocked by the lifetime count of passing builds, and paced by the leaves: a new
/// branch only pushes out once the pads already on the tree are most of the way full. So the
/// wood never runs ahead of the foliage — a tree that has seen ten builds is a sapling with one
/// small pad, and one that has seen a few hundred is a full informal-upright bonsai.
/// </para>
/// </remarks>
public sealed class BonsaiTree
{
    /// <summary>How many branches a full-grown tree has, the apex included.</summary>
    public const int BranchCount = 12;

    /// <summary>
    /// How full the pads already out must be before the next branch starts. Less than all of them,
    /// so the newest pad is still filling in as the next branch pushes out and the tree is never
    /// just waiting.
    /// </summary>
    private const double FillBeforeNext = 0.6;

    /// <summary>How many passing builds a branch takes to reach its full length.</summary>
    public const int GrowSpan = 4;

    /// <summary>
    /// How many builds after its branch unlocks a pad's first leaves can appear — once the trunk
    /// has reached it and the branch is most of the way out.
    /// </summary>
    private const int LeafDelay = 3;

    /// <summary>
    /// How far either side of the pot's middle any leaf may reach, in tree units. The scene's box
    /// is square and a tree unit is 0.76 of its height, so the edge of the box is 0.66 out; this
    /// leaves a leaf's length to spare.
    /// </summary>
    public const double MaximumSpread = 0.60;

    /// <summary>Where the full-grown trunk ends.</summary>
    public const double TrunkHeight = 0.84;

    private BonsaiTree(int seed, TreeLimb trunk, double[] trunkUnlockHeights, List<TreeLimb> branches,
        List<TreePad> pads, List<TreeLimb> twigs, List<LeafSlot> slots)
    {
        MatureAt = branches.Max(b => b.UnlockAt) + GrowSpan;
        Seed = seed;
        Trunk = trunk;
        _trunkHeights = trunkUnlockHeights;
        Branches = branches;
        Pads = pads;
        Twigs = twigs;
        Slots = slots;
    }

    private readonly double[] _trunkHeights;

    public int Seed { get; }

    /// <summary>The whole full-grown trunk. <see cref="TrunkReach"/> says how much of it is there yet.</summary>
    public TreeLimb Trunk { get; }

    /// <summary>One per pad, in the order they unlock.</summary>
    public IReadOnlyList<TreeLimb> Branches { get; }

    public IReadOnlyList<TreePad> Pads { get; }

    /// <summary>The fine wood inside each pad, drawn under the leaves.</summary>
    public IReadOnlyList<TreeLimb> Twigs { get; }

    /// <summary>Every leaf position, in the order leaves grow.</summary>
    public IReadOnlyList<LeafSlot> Slots { get; }

    /// <summary>The lifetime at which the last branch has finished growing.</summary>
    public int MatureAt { get; }

    /// <summary>How grown-up the tree is, 0 for a sapling and 1 once every branch is out.</summary>
    public double Maturity(double lifetime) => Math.Clamp(lifetime / MatureAt, 0, 1);

    /// <summary>
    /// How far a branch's growth has got, 0 before it starts and 1 once it is fully out. The first
    /// half is the trunk climbing to meet it, the second the branch itself pushing out.
    /// </summary>
    /// <remarks>
    /// A double because the scene feeds it a lifetime that eases from one build to the next, so a
    /// new build grows the wood over a few seconds instead of snapping it into place.
    /// </remarks>
    public static double Growth(int unlockAt, double lifetime) =>
        Math.Clamp((lifetime - unlockAt) / GrowSpan + 0.25, 0, 1);

    /// <summary>How much of a branch's length is out, 0 to 1.</summary>
    public static double Extent(TreeLimb limb, double lifetime) =>
        Math.Clamp(Growth(limb.UnlockAt, lifetime) * 2 - 1, 0, 1);

    /// <summary>How much of the trunk is there, 0 to 1 of its full height.</summary>
    /// <remarks>
    /// Always a little past the highest branch that has started, and climbing to meet each new
    /// one before it pushes out, so no branch ever sprouts from thin air above the trunk.
    /// </remarks>
    public double TrunkReach(double lifetime)
    {
        var reach = SaplingReach;
        for (var i = 0; i < Branches.Count; i++)
        {
            var climb = Math.Clamp(Growth(Branches[i].UnlockAt, lifetime) * 2, 0, 1);
            if (climb <= 0) continue;

            var from = i == 0 ? SaplingReach : _trunkHeights[i - 1];
            reach = Math.Max(reach, from + (_trunkHeights[i] - from) * climb);
        }

        return Math.Clamp(reach, 0, 1);
    }

    /// <summary>How tall a tree that has never seen a passing build is, as a fraction of the trunk.</summary>
    public const double SaplingReach = 0.22;

    /// <summary>
    /// How many leaves the tree has room for at a given lifetime: the run of slots, from the front,
    /// whose pads have grown far enough to carry them.
    /// </summary>
    public int Capacity(int lifetime)
    {
        var count = 0;
        while (count < Slots.Count && Slots[count].ReadyAt <= lifetime) count++;
        return count;
    }

    /// <summary>Grow a tree from a seed. The same seed is always the same tree.</summary>
    public static BonsaiTree Grow(int seed)
    {
        var random = new Random(seed);

        // ── trunk: an S-curve with a lean, the informal upright every bonsai book starts with ──
        var lean = (random.NextDouble() - 0.5) * 0.16;
        var wave = 0.07 + random.NextDouble() * 0.05;
        var phase = random.NextDouble() * Math.PI;
        const int trunkNodes = 14;

        var trunkPoints = new List<TreePoint>(trunkNodes);
        for (var i = 0; i < trunkNodes; i++)
        {
            var t = i / (double)(trunkNodes - 1);
            var x = wave * Math.Sin(t * Math.PI * 1.6 + phase) * (1 - 0.45 * t) + lean * t
                    - wave * Math.Sin(phase); // starts at the middle of the pot whatever the phase
            trunkPoints.Add(new TreePoint(x, -t * TrunkHeight));
        }

        var trunk = new TreeLimb(trunkPoints, 0.085, 0.016, 0, -1);


        // ── branches: alternating sides, lowest first, the last one the apex ─────────────────
        var shapes = new List<(List<TreePoint> Points, double Width, TreePoint Tip, double RadiusX, double RadiusY)>();
        var heights = new double[BranchCount];

        var side = random.Next(2) == 0 ? -1 : 1;
        const int sideBranches = BranchCount - 1;

        for (var b = 0; b < BranchCount; b++)
        {
            var isApex = b == BranchCount - 1;

            // Spread up the trunk with some jitter, lower branches first. The real thing grows
            // from the top, but a tree that sprouted its lowest branch last would need its lower
            // trunk bare for months, and a bare-stick sapling says "dead" rather than "young".
            var along = isApex ? 1.0 : 0.30 + 0.60 * b / (sideBranches - 1) + (random.NextDouble() - 0.5) * 0.09;
            along = Math.Clamp(along, 0.25, isApex ? 1 : 0.95);
            heights[b] = Math.Min(1, along + 0.05);

            var start = trunk.At(along);
            var width = trunk.WidthAt(along) * (0.36 + random.NextDouble() * 0.16);

            TreePoint tip;
            List<TreePoint> points;
            double radiusX;

            if (isApex)
            {
                tip = start + new TreePoint(0.01, -0.04);
                points = [start, tip];
                radiusX = 0.12;
            }
            else
            {
                // Lower branches reach further, but not uniformly — an even ladder of branches
                // reads as a fishbone. Each dips and lifts by its own amount: the wiring a bonsai
                // grower would have given it.
                var taper = 1 - 0.5 * b / (sideBranches - 1);
                var reach = (0.30 + random.NextDouble() * 0.16) * taper + 0.08;
                var rise = -0.03 + random.NextDouble() * 0.19;
                radiusX = 0.15 - 0.05 * b / (sideBranches - 1) + random.NextDouble() * 0.025;

                // Kept short enough that the pad on the end stays inside the tree's square box —
                // a trunk that leans one way and a long branch the same way would otherwise
                // hang a pad off the edge of the panel.
                reach = Math.Min(reach, MaximumSpread - radiusX - side * start.X);
                tip = start + new TreePoint(side * reach, -rise);

                var dip = 0.01 + random.NextDouble() * 0.035;
                var mid1 = TreePoint.Lerp(start, tip, 0.33) + new TreePoint(0, dip);
                var mid2 = TreePoint.Lerp(start, tip, 0.68) + new TreePoint(0, dip * 0.4);
                points = [start, mid1, mid2, tip];

                // Mostly alternating, sometimes not — perfectly alternating reads as a diagram.
                if (random.NextDouble() < 0.78) side = -side;
            }

            var radiusY = radiusX * (0.40 + random.NextDouble() * 0.08);
            shapes.Add((points, width, tip, radiusX, radiusY));
        }

        // ── leaves: scattered through each pad, denser on top, never piled on each other ──────
        var placedByPad = new List<List<(TreePoint Point, double Angle, double Shade)>>();
        foreach (var shape in shapes)
        {
            var centre = PadCentre(shape.Tip, shape.RadiusY);
            var wanted = (int)Math.Round(30 * (shape.RadiusX * shape.RadiusY) / (0.13 * 0.057));
            wanted = Math.Clamp(wanted, 12, 46);

            var spacing = Math.Sqrt(Math.PI * shape.RadiusX * shape.RadiusY / wanted) * 0.62;
            var placed = new List<(TreePoint Point, double Angle, double Shade)>();

            for (var attempt = 0; placed.Count < wanted && attempt < wanted * 40; attempt++)
            {
                // Uniform in the ellipse, with the bottom half squashed: a pad is a dome.
                var a = random.NextDouble() * Math.PI * 2;
                var r = Math.Sqrt(random.NextDouble());
                var dy = Math.Sin(a) * r;
                if (dy > 0) dy *= 0.55;

                var candidate = centre + new TreePoint(Math.Cos(a) * r * shape.RadiusX, dy * shape.RadiusY);
                if (placed.Any(q => (q.Point - candidate).Length < spacing)) continue;

                var outward = Math.Atan2(candidate.Y - centre.Y, candidate.X - centre.X);
                placed.Add((candidate, outward + (random.NextDouble() - 0.5) * 1.2, random.NextDouble()));
            }

            placedByPad.Add(placed);
        }

        // ── the schedule: each branch waits for the pads before it to be mostly full ─────────
        var unlocks = new int[BranchCount];
        var leavesSoFar = 0;
        for (var b = 0; b < BranchCount; b++)
        {
            unlocks[b] = (int)Math.Round(leavesSoFar * FillBeforeNext);
            leavesSoFar += placedByPad[b].Count;
        }

        var branches = new List<TreeLimb>();
        var pads = new List<TreePad>();
        var twigs = new List<TreeLimb>();
        var slots = new List<LeafSlot>();

        for (var b = 0; b < BranchCount; b++)
        {
            var shape = shapes[b];
            var centre = PadCentre(shape.Tip, shape.RadiusY);

            branches.Add(new TreeLimb(shape.Points, shape.Width, 0.008, unlocks[b], b));
            pads.Add(new TreePad(centre, shape.RadiusX, shape.RadiusY, unlocks[b], b));

            // A few twigs fanning out inside the pad, from the branch tip towards its edges.
            var twigCount = 3 + random.Next(2);
            for (var t = 0; t < twigCount; t++)
            {
                var angle = Math.PI * (0.1 + 0.8 * t / Math.Max(1, twigCount - 1));
                var end = centre + new TreePoint(-Math.Cos(angle) * shape.RadiusX * 0.8, -Math.Sin(angle) * shape.RadiusY * 0.5);
                twigs.Add(new TreeLimb([shape.Tip, TreePoint.Lerp(shape.Tip, end, 0.5) + new TreePoint(0, 0.006), end],
                    0.007, 0.003, unlocks[b], b));
            }

            var readyAt = unlocks[b] + LeafDelay;
            foreach (var (point, angle, shade) in placedByPad[b])
                slots.Add(new LeafSlot(point, angle, b, readyAt, shade));
        }

        return new BonsaiTree(seed, trunk, heights, branches, pads, twigs, Order(slots, random));
    }

    /// <summary>
    /// The order leaves arrive in, found by growing the tree once at a leaf a build: each build,
    /// one leaf goes to a random free slot on any pad that is ready for it.
    /// </summary>
    /// <remarks>
    /// Played out rather than sorted by a formula, because what matters is how the tree looks at
    /// a leaf a build — and a formula that ordered slots by when their pad opened spread the
    /// leaves so thinly that a new branch sat bare for weeks while older pads filled. Picking
    /// from every free slot gives a newly opened pad its fair share straight away.
    /// </remarks>
    private static List<LeafSlot> Order(List<LeafSlot> slots, Random random)
    {
        var remaining = new List<LeafSlot>(slots);
        var ordered = new List<LeafSlot>(slots.Count);

        for (var lifetime = 1; remaining.Count > 0; lifetime++)
        {
            var ready = remaining.Where(s => s.ReadyAt <= lifetime).ToList();

            // Every open pad is full and the next is still growing: this build grows wood only.
            if (ready.Count == 0) continue;

            var pick = ready[random.Next(ready.Count)];
            remaining.Remove(pick);
            ordered.Add(pick);
        }

        return ordered;
    }

    /// <summary>A pad sits a little above the branch tip it hangs from, so the tip is inside it.</summary>
    private static TreePoint PadCentre(TreePoint tip, double radiusY) => tip + new TreePoint(0, -radiusY * 0.35);
}

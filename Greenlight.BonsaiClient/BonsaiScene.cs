namespace Greenlight.BonsaiClient;

/// <summary>
/// The tree on the desk: how much of it is grown, which leaves are sprouting, turning or
/// falling, where the caterpillars are, how it sways, and what the mouse is over while it is
/// being moved. Deliberately free of Avalonia — it is all arithmetic, so it can be tested
/// without a window.
/// </summary>
/// <remarks>
/// The scene follows the <see cref="Gardener"/> rather than being told about every leaf. Each
/// frame it compares the leaves on show with the leaves the tree should have and closes the gap
/// one leaf at a time — sprouting from the front of <see cref="BonsaiTree.Slots"/>, dropping
/// from the back — so a burst of forty passing builds is a growth spurt rather than a pop, and a
/// break is a cascade rather than a blink.
/// </remarks>
public sealed class BonsaiScene
{
    /// <summary>The box is square: the tree is about as wide as it is tall, pot included.</summary>
    public const double Aspect = 1.0;

    /// <summary>Shortest the tree may be dragged, in logical pixels.</summary>
    public const double MinimumHeight = 110;

    /// <summary>Half the width of the pot at its rim, in tree units.</summary>
    public const double PotHalfWidth = 0.36;

    /// <summary>Half the width of the pot at its foot.</summary>
    public const double PotFootHalfWidth = 0.30;

    /// <summary>Rim to foot, in tree units.</summary>
    public const double PotHeight = 0.15;

    /// <summary>The little feet the pot stands on.</summary>
    public const double FeetHeight = 0.025;

    /// <summary>Where the desk is: the bottom of the feet, where fallen leaves outside the pot land.</summary>
    public const double TableY = PotHeight + FeetHeight;

    /// <summary>Where the soil is, a little below the rim — where fallen leaves inside the pot land.</summary>
    public const double SoilY = 0.012;

    /// <summary>The most caterpillars on the tree at once. Past this it is an infestation, not a reminder.</summary>
    public const int MaximumCaterpillars = 4;

    /// <summary>Seconds between one leaf sprouting and the next.</summary>
    private const double SproutInterval = 0.09;

    /// <summary>Seconds a leaf takes to unfurl.</summary>
    private const double SproutSeconds = 0.55;

    /// <summary>Most leaves in the air or on the ground at once. Anything over is simply not drawn falling.</summary>
    private const int MaximumFalling = 160;

    /// <summary>Seconds for a fade in or out of the whole tree.</summary>
    private const double FadeSeconds = 0.6;

    /// <summary>Seconds for one breath of the build pulse.</summary>
    private const double PulseSeconds = 1.8;

    /// <summary>How far a caterpillar's body segments sit apart, in tree units, at size 1.</summary>
    public const double SegmentGap = 0.017;

    /// <summary>How much of its length a caterpillar gathers up into an arch.</summary>
    private const double Bunch = 0.45;

    private readonly Random _random;
    private readonly double[] _growth;
    private readonly List<TurningLeaf> _turning = [];
    private readonly List<FallingLeaf> _falling = [];
    private readonly List<Caterpillar> _caterpillars = [];
    private readonly List<Butterfly> _butterflies = [];

    private int _redOwed;
    private double _sproutTimer;
    private double _swayPhase;
    private double _pulsePhase;
    private double _clock;
    private TreePlacement? _beforeEdit;

    public BonsaiScene(TreePlacement placement, Gardener gardener, int? randomSeed = null)
    {
        Placement = placement;
        Gardener = gardener;
        _random = randomSeed is null ? new Random() : new Random(randomSeed.Value);

        // The tree arrives as it was left: grown, leafed and as healthy as it was. Sprouting a
        // month of leaves one by one every time the app starts would be a show nobody asked for.
        _growth = new double[Tree.Slots.Count];
        ShownLeaves = gardener.VisibleLeaves;
        for (var i = 0; i < ShownLeaves; i++) _growth[i] = 1;

        Lifetime = gardener.Life.Lifetime;
        Health = gardener.Life.Health;
    }

    public TreePlacement Placement { get; }

    public Gardener Gardener { get; }

    public BonsaiTree Tree => Gardener.Tree;

    public double Width { get; private set; } = 1920;

    public double Height { get; private set; } = 1080;

    /// <summary>What Greenlight last said.</summary>
    public TreeMood Mood { get; set; } = TreeMood.Off;

    /// <summary>A build is running: the tree sways in a stronger breeze, and the next leaf shows as a glowing bud.</summary>
    public bool IsBuilding { get; set; }

    /// <summary>Show the tree regardless. Edit mode turns this on.</summary>
    public bool ForceVisible { get; set; }

    /// <summary>Draw the dormant tree when Greenlight is away, rather than nothing.</summary>
    public bool ShowWhenOff { get; set; } = true;

    /// <summary>How much the tree moves in the breeze, as a multiple of the ordinary.</summary>
    public double Liveliness { get; set; } = 1.0;

    public bool IsEditing => _beforeEdit is not null;

    /// <summary>0–1, how far in the whole tree is.</summary>
    public double Fade { get; private set; }

    /// <summary>
    /// The lifetime the drawing is showing, easing towards the real one so new wood grows over a
    /// few seconds rather than appearing.
    /// </summary>
    public double Lifetime { get; private set; }

    /// <summary>The health the drawing is showing, easing towards the real one.</summary>
    public double Health { get; private set; }

    /// <summary>How many leaves are on the branches, counting from the front of the slots.</summary>
    public int ShownLeaves { get; private set; }

    public IReadOnlyList<TurningLeaf> Turning => _turning;

    public IReadOnlyList<FallingLeaf> Falling => _falling;

    public IReadOnlyList<Caterpillar> Caterpillars => _caterpillars;

    public IReadOnlyList<Butterfly> Butterflies => _butterflies;

    /// <summary>How thick the wood is drawn, from a sapling's whip to a mature trunk.</summary>
    public double WoodScale => 0.45 + 0.55 * Tree.Maturity(Lifetime);

    /// <summary>1 at the top of a breath, 0 at the bottom; a flat 1 when nothing is building.</summary>
    public double Pulse => IsBuilding ? 0.5 * (1 + Math.Cos(_pulsePhase * 2 * Math.PI)) : 1;

    /// <summary>Seconds since the scene started, for anything that wants to wobble.</summary>
    public double Clock => _clock;

    /// <summary>How far a leaf has unfurled, 0 to 1.</summary>
    public double Growth(int slot) => slot >= 0 && slot < _growth.Length ? _growth[slot] : 0;

    /// <summary>
    /// Where the next leaf will come, while a build runs — the bud that opens if it passes. Null
    /// when nothing is building or the tree has no room left.
    /// </summary>
    public int? BudSlot => IsBuilding && Mood != TreeMood.Off && ShownLeaves < Tree.Slots.Count
        ? ShownLeaves
        : null;

    public void Resize(double width, double height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
    }

    /// <summary>
    /// A build has just broken and the gardener has taken <paramref name="leaves"/> off the tree:
    /// the next that many to come off are red ones.
    /// </summary>
    public void MarkBroken(int leaves) => _redOwed += Math.Max(0, leaves);

    // ── the breeze, and the droop ─────────────────────────────────────────────

    /// <summary>
    /// Where a point of the tree is this frame: swayed by the breeze, and sagging as the tree's
    /// health goes. Everything on the tree — wood, leaves, caterpillars — goes through here, so
    /// nothing slides off the branch it is sitting on.
    /// </summary>
    public TreePoint Displace(TreePoint p)
    {
        var height = Math.Max(0, -p.Y);

        // Sag grows with the distance out from the trunk, the way a branch with no water in it
        // hangs from where it joins.
        var reach = Math.Clamp(Math.Abs(p.X) / 0.5, 0, 1.4);
        var sag = (1 - Health) * 0.07 * Math.Pow(reach, 1.3);

        var sway = SwayAmplitude * Math.Sin(_swayPhase * Math.PI * 2 + p.Y * 2.2) * Math.Pow(height, 1.4);
        return new TreePoint(p.X + sway, p.Y + sag);
    }

    /// <summary>Which way a leaf points this frame: towards the ground, the sicker the tree is.</summary>
    public double LeafAngle(LeafSlot slot)
    {
        var droop = (1 - Health) * 0.75;

        // Rotate towards straight down (π/2 in screen terms) by the shortest way round.
        var target = Math.PI / 2;
        var delta = Math.IEEERemainder(target - slot.Angle, Math.PI * 2);
        return slot.Angle + delta * droop;
    }

    private double SwayAmplitude => Mood == TreeMood.Off
        ? 0
        : (IsBuilding ? 0.016 : 0.005) * Liveliness;

    // ── a frame ───────────────────────────────────────────────────────────────

    public void Advance(TimeSpan elapsed)
    {
        // A frame after a sleep or a breakpoint is clamped: two minutes in one step would empty
        // the sky of falling leaves without anyone seeing them fall.
        var dt = Math.Clamp(elapsed.TotalSeconds, 0, 0.25);
        if (dt <= 0) return;

        _clock += dt;
        _pulsePhase = (_pulsePhase + dt / PulseSeconds) % 1.0;

        // A dormant tree is still, but it finishes the swing it was in rather than freezing
        // mid-lean — the phase keeps going, the amplitude is what drops to nothing.
        _swayPhase = (_swayPhase + dt / (IsBuilding ? 2.2 : 5.5)) % 1.0;

        var visible = ForceVisible || ShowWhenOff || Mood != TreeMood.Off;
        Fade = visible ? Math.Min(1, Fade + dt / FadeSeconds) : Math.Max(0, Fade - dt / FadeSeconds);

        // The wood grows over a couple of seconds a build; health follows in its own time.
        var lifeGap = Gardener.Life.Lifetime - Lifetime;
        Lifetime = Math.Abs(lifeGap) < 0.001
            ? Gardener.Life.Lifetime
            : Lifetime + Math.Sign(lifeGap) * Math.Min(Math.Abs(lifeGap), dt * Math.Max(0.6, Math.Abs(lifeGap) * 0.8));

        var healthGap = Gardener.Life.Health - Health;
        Health += Math.Sign(healthGap) * Math.Min(Math.Abs(healthGap), dt * 0.25);

        FollowLeaves(dt);
        AdvanceTurning(dt);
        AdvanceFalling(dt);
        AdvanceCaterpillars(dt);
        AdvanceButterflies(dt);
    }

    /// <summary>Close the gap between the leaves on show and the leaves the tree has.</summary>
    private void FollowLeaves(double dt)
    {
        var target = Gardener.VisibleLeaves;

        if (target < ShownLeaves)
        {
            // Off the back, newest first. Red ones while a break is still owed its leaves; the
            // rest have simply wilted.
            var cascade = 0;
            for (var slot = ShownLeaves - 1; slot >= target; slot--)
            {
                var fate = _redOwed > 0 ? LeafFate.Broken : LeafFate.Wilted;
                if (_redOwed > 0) _redOwed--;

                var delay = fate == LeafFate.Broken
                    ? cascade * 0.045 + _random.NextDouble() * 0.15
                    : cascade * 0.35 + _random.NextDouble() * 0.3;

                _turning.Add(new TurningLeaf(slot, fate, delay));
                _growth[slot] = 0;
                cascade++;
            }

            ShownLeaves = target;
            _sproutTimer = 0;
        }

        // Nothing owed any more: whatever red was promised and has not come off — because the
        // tree had fewer leaves than the break counted — is forgotten rather than turning the
        // next wilted leaf red hours later.
        if (_redOwed > 0 && _turning.Count == 0 && target >= ShownLeaves) _redOwed = 0;

        if (target > ShownLeaves)
        {
            _sproutTimer -= dt;
            while (_sproutTimer <= 0 && target > ShownLeaves)
            {
                _growth[ShownLeaves] = Math.Max(_growth[ShownLeaves], 0.01);
                ShownLeaves++;
                _sproutTimer += SproutInterval;
            }
        }

        for (var i = 0; i < ShownLeaves; i++)
            if (_growth[i] < 1) _growth[i] = Math.Min(1, _growth[i] + dt / SproutSeconds);
    }

    private void AdvanceTurning(double dt)
    {
        for (var i = _turning.Count - 1; i >= 0; i--)
        {
            var leaf = _turning[i];

            if (leaf.Delay > 0)
            {
                leaf.Delay -= dt;
                continue;
            }

            leaf.Turn += dt / TurningLeaf.TurnSeconds;
            if (leaf.Turn < 1) continue;

            _turning.RemoveAt(i);
            Drop(leaf);
        }
    }

    private void Drop(TurningLeaf leaf)
    {
        if (_falling.Count >= MaximumFalling) return;

        var slot = Tree.Slots[leaf.Slot];
        var start = Displace(slot.Position);

        // Inside the pot's rim it lands on the soil; outside, on the desk.
        var ground = Math.Abs(start.X) < PotHalfWidth * 0.92 ? SoilY : TableY;

        _falling.Add(new FallingLeaf
        {
            Position = start,
            Fate = leaf.Fate,
            Shade = slot.Shade,
            Angle = LeafAngle(slot),
            Spin = (_random.NextDouble() - 0.5) * 4,
            VelocityX = (_random.NextDouble() - 0.5) * 0.04,
            VelocityY = 0.02,
            Flutter = _random.NextDouble(),
            Ground = Math.Max(ground, start.Y),
        });
    }

    private void AdvanceFalling(double dt)
    {
        for (var i = _falling.Count - 1; i >= 0; i--)
        {
            var leaf = _falling[i];

            if (leaf.Landed)
            {
                leaf.Resting += dt;
                if (leaf.IsGone) _falling.RemoveAt(i);
                continue;
            }

            // A leaf does not drop, it see-saws: a slow terminal speed, and a sideways swing
            // that tips it as it goes.
            leaf.VelocityY = Math.Min(0.16, leaf.VelocityY + 0.35 * dt);
            leaf.Flutter += dt * 0.9;

            var swing = Math.Sin(leaf.Flutter * Math.PI * 2);
            var x = leaf.Position.X + (leaf.VelocityX + swing * 0.07) * dt;
            var y = leaf.Position.Y + leaf.VelocityY * dt * (0.75 + 0.25 * Math.Abs(swing));

            leaf.Angle += (leaf.Spin + swing * 1.5) * dt;

            if (y >= leaf.Ground)
            {
                y = leaf.Ground;
                leaf.Landed = true;

                // Lying flat-ish, whichever way up it came down.
                leaf.Angle = Math.Round(leaf.Angle / Math.PI) * Math.PI + (_random.NextDouble() - 0.5) * 0.5;
            }

            leaf.Position = new TreePoint(x, y);
        }
    }

    // ── caterpillars ──────────────────────────────────────────────────────────

    /// <summary>
    /// The pull requests waiting on you. Each one new since last time gets a caterpillar; each
    /// one gone turns its caterpillar into a butterfly — unless <paramref name="quietly"/>, for
    /// when Greenlight has gone away and nobody actually reviewed anything.
    /// </summary>
    public void SetWaiting(IReadOnlyList<WaitingReview> waiting, bool quietly = false)
    {
        var wanted = waiting
            .OrderByDescending(w => w.Waiting)
            .Take(MaximumCaterpillars)
            .ToDictionary(w => w.Key, w => w.Waiting);

        for (var i = _caterpillars.Count - 1; i >= 0; i--)
        {
            var caterpillar = _caterpillars[i];
            if (wanted.ContainsKey(caterpillar.Key)) continue;

            _caterpillars.RemoveAt(i);
            if (!quietly && caterpillar.Appear > 0.3) Metamorphose(caterpillar);
        }

        foreach (var (key, age) in wanted)
        {
            var existing = _caterpillars.FirstOrDefault(c => c.Key == key);
            if (existing is null)
            {
                existing = new Caterpillar(key, ChooseLimb(key));
                existing.Along = existing.Limb < 0 ? 0.2 : 0.1;
                existing.Inch = _random.NextDouble();
                _caterpillars.Add(existing);
            }

            existing.Size = Caterpillar.SizeFor(age);
        }
    }

    /// <summary>
    /// A branch for a new caterpillar: one that is fully out, picked by the pull request so the
    /// same one goes back to the same branch after a restart, and not one already taken if there
    /// is a free one. A tree with no finished branch yet gets it on the trunk.
    /// </summary>
    private int ChooseLimb(string key)
    {
        var grown = Enumerable.Range(0, Tree.Branches.Count - 1) // not the apex: it has no length to crawl
            .Where(b => BonsaiTree.Extent(Tree.Branches[b], Lifetime) >= 1)
            .ToList();

        if (grown.Count == 0) return -1;

        var free = grown.Where(b => _caterpillars.All(c => c.Limb != b)).ToList();
        var pool = free.Count > 0 ? free : grown;

        return pool[(int)(StableHash(key) % (uint)pool.Count)];
    }

    /// <summary>The limb a caterpillar is crawling along.</summary>
    public TreeLimb LimbOf(Caterpillar caterpillar) =>
        caterpillar.Limb >= 0 && caterpillar.Limb < Tree.Branches.Count ? Tree.Branches[caterpillar.Limb] : Tree.Trunk;

    /// <summary>How far along its limb a caterpillar may go: not into the trunk, not off the end, not above the trunk's top.</summary>
    public (double Min, double Max) RangeOf(Caterpillar caterpillar) =>
        caterpillar.Limb < 0
            ? (0.12, Math.Max(0.2, Tree.TrunkReach(Lifetime) * 0.9))
            : (0.08, 0.95);

    private void AdvanceCaterpillars(double dt)
    {
        foreach (var c in _caterpillars)
        {
            c.Appear = Math.Min(1, c.Appear + dt / 0.8);

            var limb = LimbOf(c);
            var length = Math.Max(0.01, limb.Length);
            var (min, max) = RangeOf(c);
            var body = (Caterpillar.Segments - 1) * SegmentGap * c.Size;

            if (c.Activity == CaterpillarActivity.Munching)
            {
                c.Munch += dt;
                if (c.Munch < Caterpillar.MunchSeconds) continue;

                c.Activity = CaterpillarActivity.Crawling;
                c.Munch = 0;
                TurnRound(c, body / length);
                continue;
            }

            var humpBefore = c.Hump;
            var inchBefore = c.Inch;
            c.Inch = (c.Inch + dt / Caterpillar.InchSeconds) % 1.0;

            // The head only moves in the second half of an inch — the half where the arch lets
            // go and the front stretches out. The first half is the tail catching up. Moving the
            // head by exactly the arch it gives up is what keeps the tail planted.
            if (inchBefore >= 0.5 && c.Inch >= 0.5)
            {
                var stride = Bunch * body * (humpBefore - c.Hump);
                c.Along += c.Direction * stride / length;
            }

            if (c.Direction > 0 && c.Along >= max)
            {
                c.Along = max;

                // At the end of a branch, where the leaves are, it stops for a meal. On the
                // trunk there is nothing to eat at the top, so it just turns round.
                if (c.Limb >= 0) c.Activity = CaterpillarActivity.Munching;
                else TurnRound(c, body / length);
            }
            else if (c.Direction < 0 && c.Along <= min)
            {
                c.Along = min;
                TurnRound(c, body / length);
            }
        }
    }

    /// <summary>
    /// Turn round on the spot: the tail becomes the head. Swapping ends rather than flipping the
    /// body over the head is what stops it jumping a body-length when it changes its mind.
    /// </summary>
    private static void TurnRound(Caterpillar c, double bodyFraction)
    {
        c.Along -= c.Direction * bodyFraction;
        c.Direction = -c.Direction;
    }

    /// <summary>
    /// The caterpillar's body, head first: where each segment is, in tree units before the breeze,
    /// and how big it is.
    /// </summary>
    public IReadOnlyList<(TreePoint Centre, double Radius)> BodyOf(Caterpillar c)
    {
        var limb = LimbOf(c);
        var length = Math.Max(0.01, limb.Length);
        var gap = SegmentGap * c.Size * (1 - Bunch * c.Hump);
        var wood = WoodScale;

        var body = new List<(TreePoint, double)>(Caterpillar.Segments);
        for (var i = 0; i < Caterpillar.Segments; i++)
        {
            var along = c.Along - c.Direction * i * gap / length;
            var on = limb.At(along);

            // Up off the wood: the normal to the branch that points skywards.
            var ahead = limb.At(along + 0.01) - limb.At(along - 0.01);
            var normal = new TreePoint(ahead.Y, -ahead.X);
            var normLength = normal.Length;
            normal = normLength <= 0 ? new TreePoint(0, -1) : normal * (1 / normLength);
            if (normal.Y > 0) normal = normal * -1;

            var radius = 0.0095 * c.Size * (i == 0 ? 1.12 : 1 - 0.28 * i / Caterpillar.Segments);
            var arch = c.Hump * Math.Sin(Math.PI * i / (Caterpillar.Segments - 1)) * 0.022 * c.Size;

            // Chewing: the head dips to the leaves and comes back up, a few times a second.
            var chew = i == 0 && c.Activity == CaterpillarActivity.Munching
                ? -Math.Abs(Math.Sin(c.Munch * Math.PI * 3)) * 0.008 * c.Size
                : 0;

            var lift = limb.WidthAt(along) * wood / 2 + radius * 0.85 + arch + chew;
            body.Add((on + normal * lift, radius));
        }

        return body;
    }

    private void Metamorphose(Caterpillar c)
    {
        var head = BodyOf(c)[0].Centre;
        _butterflies.Add(new Butterfly
        {
            Position = Displace(head),
            Size = 0.8 + 0.25 * c.Size,
            Heading = head.X >= 0 ? 1 : -1,
            Flap = _random.NextDouble(),
        });
    }

    private void AdvanceButterflies(double dt)
    {
        for (var i = _butterflies.Count - 1; i >= 0; i--)
        {
            var b = _butterflies[i];
            b.Age += dt;
            b.Flap = (b.Flap + dt * 3.4) % 1.0;

            // Up and away in a loose zigzag, the way a butterfly never quite flies straight.
            var wobble = Math.Sin(b.Age * 2.3) * 0.05;
            b.Position += new TreePoint((b.Heading * 0.09 + wobble) * dt, -(0.11 + 0.04 * Math.Sin(b.Age * 5)) * dt);

            if (b.IsGone) _butterflies.RemoveAt(i);
        }
    }

    /// <summary>Snap straight in, for edit mode.</summary>
    public void SnapVisible() => Fade = 1;

    // ── layout ────────────────────────────────────────────────────────────────

    /// <summary>Every measurement of the tree at the current overlay size.</summary>
    /// <remarks>
    /// The soil sits four-fifths of the way down the box and a tree unit is a little less than
    /// that, so a full-grown crown just clears the top and the pot's feet just reach the bottom.
    /// </remarks>
    public TreeLayout Measure()
    {
        var height = TreeHeight();
        var width = height * Aspect;
        var centre = new ScenePoint(Placement.AnchorX * Width, Placement.AnchorY * Height);
        var box = SceneRect.FromCentre(centre, width, height);

        var unit = height * 0.76;
        var origin = new ScenePoint(box.Centre.X, box.Y + height * 0.80);

        var handle = Math.Clamp(width * 0.10, 10, 22);
        var button = Math.Clamp(width * 0.16, 22, 40);
        var gap = button * 0.35;
        var buttonY = box.Y - gap - button < 0 ? box.Bottom + gap : box.Y - gap - button;

        var save = new SceneRect(box.Right - button, buttonY, button, button);
        var cancel = new SceneRect(box.Right - button * 2 - gap, buttonY, button, button);

        return new TreeLayout(box, origin, unit, handle, save, cancel);
    }

    public double TreeHeight() => Math.Clamp(Height * Placement.Size, MinimumHeight, MaximumHeight);

    public double MaximumHeight => Math.Max(MinimumHeight, Math.Min(Height * 0.95, Width * 0.95 / Aspect));

    public TreeGrip HitTest(ScenePoint point)
    {
        var layout = Measure();

        if (layout.SaveButton.Contains(point)) return TreeGrip.Save;
        if (layout.CancelButton.Contains(point)) return TreeGrip.Cancel;

        foreach (var corner in TreeLayout.Corners)
            if (layout.Handle(corner).Contains(point))
                return corner;

        return layout.Box.Contains(point) ? TreeGrip.Body : TreeGrip.None;
    }

    public void MoveTo(ScenePoint centre)
    {
        var height = TreeHeight();
        Placement.AnchorX = Fraction(centre.X, height * Aspect / 2, Width);
        Placement.AnchorY = Fraction(centre.Y, height / 2, Height);
    }

    public void ResizeTo(TreeGrip corner, ScenePoint point)
    {
        if (corner is not (TreeGrip.ResizeTopLeft or TreeGrip.ResizeTopRight
            or TreeGrip.ResizeBottomLeft or TreeGrip.ResizeBottomRight)) return;

        var box = Measure().Box;

        var anchored = corner switch
        {
            TreeGrip.ResizeTopLeft => box.BottomRight,
            TreeGrip.ResizeTopRight => box.BottomLeft,
            TreeGrip.ResizeBottomLeft => box.TopRight,
            _ => box.TopLeft,
        };

        var height = Math.Max(Math.Abs(point.Y - anchored.Y), Math.Abs(point.X - anchored.X) / Aspect);
        height = Math.Clamp(height, MinimumHeight, MaximumHeight);

        var signX = corner is TreeGrip.ResizeTopRight or TreeGrip.ResizeBottomRight ? 1 : -1;
        var signY = corner is TreeGrip.ResizeBottomLeft or TreeGrip.ResizeBottomRight ? 1 : -1;

        Placement.Size = Height <= 0 ? Placement.Size : height / Height;
        MoveTo(new ScenePoint(anchored.X + signX * height * Aspect / 2, anchored.Y + signY * height / 2));
    }

    // ── edit mode ─────────────────────────────────────────────────────────────

    public void BeginEdit() => _beforeEdit ??= Placement.Copy();

    public void CommitEdit() => _beforeEdit = null;

    public void CancelEdit()
    {
        if (_beforeEdit is null) return;

        Placement.CopyFrom(_beforeEdit);
        _beforeEdit = null;
    }

    private static double Fraction(double value, double half, double extent)
    {
        if (extent <= 0 || !double.IsFinite(value)) return 0.5;
        if (half * 2 >= extent) return 0.5;

        return Math.Clamp(value, half, extent - half) / extent;
    }

    /// <summary>
    /// FNV-1a. <see cref="string.GetHashCode()"/> is randomised per process, which would send a
    /// pull request's caterpillar to a different branch after every restart.
    /// </summary>
    private static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash;
    }
}

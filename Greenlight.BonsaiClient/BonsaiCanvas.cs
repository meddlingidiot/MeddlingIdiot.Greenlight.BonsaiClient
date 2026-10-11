using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Greenlight.BonsaiClient;

/// <summary>
/// Draws the tree. Everything comes off the scene's numbers each frame — no controls, no
/// images, every shape is code — so the tree can be any size without a single blurry pixel.
/// </summary>
/// <remarks>
/// Wood is drawn as one filled outline per limb rather than a run of thick line segments: line
/// caps overlapping at every joint show up as beads along a translucent branch, and a tapering
/// outline is also the only way to get the flared root a bonsai is judged on.
/// </remarks>
public sealed class BonsaiCanvas : Control
{
    /// <summary>How many points a limb's outline is built from. Enough that a curve is a curve.</summary>
    private const int LimbSamples = 18;

    /// <summary>A leaf's length and width in tree units, full grown.</summary>
    private const double LeafLength = 0.036;

    private const double LeafWidth = 0.017;

    /// <summary>How many shades of green a pad is drawn in. Quantised so a frame needs a handful of brushes, not hundreds.</summary>
    private const int ShadeBins = 6;

    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.FromArgb(170, 13, 14, 17));
    private static readonly IPen PanelPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);

    private static readonly Color Soil = Color.FromRgb(52, 37, 27);
    private static readonly Color Moss = Color.FromRgb(104, 140, 58);
    private static readonly Color Dormant = Color.FromRgb(118, 122, 128);
    private static readonly Color Bud = Color.FromRgb(190, 255, 130);

    private static readonly IBrush EditFill = new SolidColorBrush(Color.FromArgb(30, 120, 200, 255));
    private static readonly IPen EditPen = new Pen(
        new SolidColorBrush(Color.FromArgb(210, 150, 210, 255)), 1.5, new DashStyle([4, 3], 0));

    private static readonly IBrush HandleBrush = new SolidColorBrush(Color.FromArgb(235, 245, 250, 255));
    private static readonly IPen HandlePen = new Pen(new SolidColorBrush(Color.FromArgb(220, 40, 60, 90)), 1);
    private static readonly IBrush SaveBrush = new SolidColorBrush(Color.FromArgb(240, 32, 160, 78));
    private static readonly IBrush CancelBrush = new SolidColorBrush(Color.FromArgb(240, 190, 48, 40));
    private static readonly IBrush ButtonHeld = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));

    /// <summary>
    /// One leaf, pointing right from its stalk at the origin, one unit long and one unit wide.
    /// Every leaf is this shape scaled, turned and moved.
    /// </summary>
    private static readonly Geometry LeafShape = BuildLeaf();

    private readonly BonsaiConfig _config;
    private readonly Dictionary<string, Color> _colours = [];

    public BonsaiCanvas(BonsaiScene scene, BonsaiConfig config)
    {
        Scene = scene;
        _config = config;
        IsHitTestVisible = false;
    }

    public BonsaiScene Scene { get; }

    public bool IsEditing { get; set; }

    public TreeGrip Held { get; set; }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Height <= 1 || Bounds.Width <= 1) return;

        var layout = Scene.Measure();

        if (Scene.Fade > 0.001)
        {
            using var fade = context.PushOpacity(Ease(Scene.Fade));

            if (_config.ShowPanel) DrawPanel(context, layout);

            var palette = Palette();

            DrawPot(context, layout, palette);
            DrawWood(context, layout, palette);
            DrawLeaves(context, layout, palette);
            DrawTurning(context, layout, palette);
            DrawCaterpillars(context, layout, palette);
            DrawFalling(context, layout, palette);
            DrawButterflies(context, layout, palette);
        }

        if (IsEditing) DrawEditChrome(context, layout);
    }

    // ── colours ───────────────────────────────────────────────────────────────

    /// <summary>This frame's colours, with the dormant greying already applied.</summary>
    private readonly record struct Paints(
        Color Leaf, Color Broken, Color Dead, Color Wood, Color Pot, Color Caterpillar, Color Butterfly, double Grey);

    private Paints Palette()
    {
        var c = _config.Colours;
        var grey = Scene.Mood == TreeMood.Off ? 0.65 : 0.0;

        var healthy = Colour(c.Leaf, Color.FromRgb(76, 159, 56));
        var sick = Colour(c.LeafSick, Color.FromRgb(200, 178, 62));
        var dead = Colour(c.LeafDead, Color.FromRgb(138, 90, 43));

        // Green to yellow over the first half of the decline, yellow to brown over the second —
        // a tree should look worried well before it looks dead.
        var h = Scene.Health;
        var leaf = h >= 0.5 ? Mix(sick, healthy, (h - 0.5) * 2) : Mix(dead, sick, h * 2);

        return new Paints(
            Grey(leaf, grey),
            Colour(c.LeafBroken, Color.FromRgb(232, 57, 42)),
            dead,
            Grey(Colour(c.Wood, Color.FromRgb(94, 61, 39)), grey * 0.6),
            Grey(Colour(c.Pot, Color.FromRgb(54, 89, 110)), grey * 0.5),
            Colour(c.Caterpillar, Color.FromRgb(242, 178, 48)),
            Colour(c.Butterfly, Color.FromRgb(255, 138, 42)),
            grey);
    }

    private static Color Grey(Color colour, double amount) => Mix(colour, Dormant, amount);

    // ── the furniture ─────────────────────────────────────────────────────────

    private static void DrawPanel(DrawingContext context, TreeLayout layout)
    {
        var box = layout.Box;
        var pad = box.Width * 0.06;
        var rect = new Rect(box.X - pad, box.Y - pad, box.Width + pad * 2, box.Height + pad * 2);
        context.DrawRectangle(PanelBrush, PanelPen, new RoundedRect(rect, box.Width * 0.08));
    }

    /// <summary>A glazed rectangular pot on two feet, with soil and a little moss.</summary>
    private void DrawPot(DrawingContext context, TreeLayout layout, Paints paints)
    {
        var u = layout.Unit;
        Point P(double x, double y) => Pt(layout.ToScreen(new TreePoint(x, y)));

        var pot = paints.Pot;
        var dark = Mix(pot, Colors.Black, 0.35);
        var light = Mix(pot, Colors.White, 0.22);

        // Feet first, so the body sits on them.
        var feet = new SolidColorBrush(dark);
        foreach (var side in new[] { -1, 1 })
        {
            var x = side * BonsaiScene.PotFootHalfWidth * 0.72;
            var tl = P(x - 0.045, BonsaiScene.PotHeight - 0.004);
            context.DrawRectangle(feet, null, new RoundedRect(new Rect(tl, new Size(0.09 * u, BonsaiScene.FeetHeight * u)), 0.006 * u));
        }

        var body = new StreamGeometry();
        using (var ctx = body.Open())
        {
            ctx.BeginFigure(P(-BonsaiScene.PotHalfWidth, 0.0), true);
            ctx.LineTo(P(BonsaiScene.PotHalfWidth, 0.0));
            ctx.QuadraticBezierTo(P(BonsaiScene.PotHalfWidth - 0.01, BonsaiScene.PotHeight * 0.7),
                P(BonsaiScene.PotFootHalfWidth, BonsaiScene.PotHeight));
            ctx.LineTo(P(-BonsaiScene.PotFootHalfWidth, BonsaiScene.PotHeight));
            ctx.QuadraticBezierTo(P(-BonsaiScene.PotHalfWidth + 0.01, BonsaiScene.PotHeight * 0.7),
                P(-BonsaiScene.PotHalfWidth, 0.0));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(new SolidColorBrush(pot), new Pen(new SolidColorBrush(dark), Math.Max(1, 0.004 * u)), body);

        // A glaze highlight down the left, which is what makes it read as fired clay and not paper.
        var shine = new SolidColorBrush(Color.FromArgb(70, light.R, light.G, light.B));
        context.DrawRectangle(shine, null,
            new RoundedRect(new Rect(P(-BonsaiScene.PotHalfWidth + 0.035, 0.03), new Size(0.035 * u, BonsaiScene.PotHeight * 0.62 * u)), 0.015 * u));

        // The rim, a shade proud of the body.
        context.DrawRectangle(new SolidColorBrush(light), null,
            new RoundedRect(new Rect(P(-BonsaiScene.PotHalfWidth - 0.012, -0.016), new Size((BonsaiScene.PotHalfWidth + 0.012) * 2 * u, 0.026 * u)), 0.008 * u));

        // Soil, and a few cushions of moss that brown with the tree.
        var soilRect = new Rect(P(-BonsaiScene.PotHalfWidth + 0.012, -0.01), new Size((BonsaiScene.PotHalfWidth - 0.012) * 2 * u, 0.022 * u));
        context.DrawRectangle(new SolidColorBrush(Grey(Soil, paints.Grey)), null, new RoundedRect(soilRect, 0.006 * u));

        var moss = new SolidColorBrush(Grey(Mix(paints.Dead, Moss, Scene.Health), paints.Grey));
        foreach (var (x, r) in new[] { (-0.25, 0.030), (-0.17, 0.022), (0.12, 0.026), (0.21, 0.034), (0.29, 0.018) })
            context.DrawEllipse(moss, null, P(x, -0.006), r * u, r * 0.38 * u);
    }

    // ── wood ──────────────────────────────────────────────────────────────────

    private void DrawWood(DrawingContext context, TreeLayout layout, Paints paints)
    {
        var bark = new SolidColorBrush(paints.Wood);
        var shine = new SolidColorBrush(Color.FromArgb(55, 255, 230, 200));
        var lifetime = Scene.Lifetime;
        var scale = Scene.WoodScale;

        DrawLimb(context, layout, Scene.Tree.Trunk, Scene.Tree.TrunkReach(lifetime), scale, bark, shine, flare: true);

        foreach (var branch in Scene.Tree.Branches)
        {
            var extent = BonsaiTree.Extent(branch, lifetime);
            if (extent > 0.01) DrawLimb(context, layout, branch, extent, scale, bark, shine, flare: false);
        }

        // Twigs only once their branch is out: they are the inside of a pad, and a pad with no
        // branch under it yet is not there.
        foreach (var twig in Scene.Tree.Twigs)
        {
            var extent = Math.Clamp((BonsaiTree.Extent(Scene.Tree.Branches[twig.Pad], lifetime) - 0.6) / 0.4, 0, 1);
            if (extent > 0.01) DrawLimb(context, layout, twig, extent, 1.0, bark, null, flare: false);
        }
    }

    /// <summary>
    /// One limb as a tapering outline, from its base to <paramref name="extent"/> of the way
    /// along. Every point goes through the scene's breeze and droop first.
    /// </summary>
    private void DrawLimb(DrawingContext context, TreeLayout layout, TreeLimb limb, double extent, double scale,
        IBrush fill, IBrush? shine, bool flare)
    {
        var left = new Point[LimbSamples + 1];
        var right = new Point[LimbSamples + 1];
        var centre = new TreePoint[LimbSamples + 1];
        var widths = new double[LimbSamples + 1];

        for (var i = 0; i <= LimbSamples; i++)
        {
            var t = extent * i / LimbSamples;
            centre[i] = Scene.Displace(limb.At(t));

            // A growing limb tapers to its current tip, not to where its tip will be one day.
            var along = extent <= 0 ? 0 : t / extent;
            var width = (limb.BaseWidth + (limb.TipWidth - limb.BaseWidth) * along) * scale;

            // The nebari: the flare where trunk meets soil, which a bonsai is judged on before
            // anything else.
            if (flare && t < 0.09) width *= 1 + 0.9 * Math.Pow(1 - t / 0.09, 2);

            widths[i] = width / 2;
        }

        for (var i = 0; i <= LimbSamples; i++)
        {
            var ahead = centre[Math.Min(LimbSamples, i + 1)] - centre[Math.Max(0, i - 1)];
            var length = ahead.Length;
            var normal = length <= 0 ? new TreePoint(1, 0) : new TreePoint(-ahead.Y / length, ahead.X / length);

            left[i] = Pt(layout.ToScreen(centre[i] + normal * widths[i]));
            right[i] = Pt(layout.ToScreen(centre[i] - normal * widths[i]));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(left[0], true);
            for (var i = 1; i <= LimbSamples; i++) ctx.LineTo(left[i]);
            for (var i = LimbSamples; i >= 0; i--) ctx.LineTo(right[i]);
            ctx.EndFigure(true);
        }

        context.DrawGeometry(fill, null, geometry);

        // Round off the tip rather than leaving it cut square.
        var tip = Pt(layout.ToScreen(centre[LimbSamples]));
        var tipRadius = widths[LimbSamples] * layout.Unit;
        context.DrawEllipse(fill, null, tip, tipRadius, tipRadius);

        if (shine is null || widths[0] * layout.Unit < 3) return;

        // A thin lit edge down one side, so the wood has a round to it.
        var lit = new StreamGeometry();
        using (var ctx = lit.Open())
        {
            ctx.BeginFigure(left[0], true);
            for (var i = 1; i <= LimbSamples; i++) ctx.LineTo(left[i]);
            for (var i = LimbSamples; i >= 0; i--) ctx.LineTo(Lerp(left[i], right[i], 0.3));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(shine, null, lit);
    }

    // ── leaves ────────────────────────────────────────────────────────────────

    private void DrawLeaves(DrawingContext context, TreeLayout layout, Paints paints)
    {
        var tree = Scene.Tree;
        var shown = Scene.ShownLeaves;

        // A dark underside to each pad, as full as the pad is — the shadow the foliage throws on
        // itself, and what turns a scatter of leaves into a cloud.
        var perPad = new int[tree.Pads.Count];
        var totalPad = new int[tree.Pads.Count];
        for (var i = 0; i < tree.Slots.Count; i++)
        {
            totalPad[tree.Slots[i].Pad]++;
            if (i < shown) perPad[tree.Slots[i].Pad]++;
        }

        var under = Mix(paints.Leaf, Colors.Black, 0.55);
        for (var p = 0; p < tree.Pads.Count; p++)
        {
            if (perPad[p] == 0 || totalPad[p] == 0) continue;

            var pad = tree.Pads[p];
            var fullness = (double)perPad[p] / totalPad[p];
            var brush = new SolidColorBrush(Color.FromArgb((byte)(120 * fullness), under.R, under.G, under.B));
            var centre = Pt(layout.ToScreen(Scene.Displace(pad.Centre + new TreePoint(0, pad.RadiusY * 0.25))));
            context.DrawEllipse(brush, null, centre, pad.RadiusX * 0.9 * layout.Unit, pad.RadiusY * 0.75 * layout.Unit);
        }

        var brushes = ShadeBrushes(paints.Leaf);

        for (var i = 0; i < shown; i++)
        {
            var slot = tree.Slots[i];
            var growth = Scene.Growth(i);
            if (growth <= 0) continue;

            var bin = Math.Clamp((int)(slot.Shade * ShadeBins), 0, ShadeBins - 1);
            DrawLeaf(context, layout, Scene.Displace(slot.Position), Scene.LeafAngle(slot), Ease(growth), brushes[bin]);
        }

        // The bud that opens if the running build passes.
        if (Scene.BudSlot is { } budSlot)
        {
            var at = Pt(layout.ToScreen(Scene.Displace(tree.Slots[budSlot].Position)));
            var pulse = Scene.Pulse;
            var r = (0.006 + 0.004 * pulse) * layout.Unit;

            var glow = Color.FromArgb((byte)(60 + 80 * pulse), Bud.R, Bud.G, Bud.B);
            context.DrawEllipse(new SolidColorBrush(glow), null, at, r * 2.4, r * 2.4);
            context.DrawEllipse(new SolidColorBrush(Bud), null, at, r, r);
        }
    }

    private void DrawTurning(DrawingContext context, TreeLayout layout, Paints paints)
    {
        foreach (var leaf in Scene.Turning)
        {
            var slot = Scene.Tree.Slots[leaf.Slot];
            var target = leaf.Fate == LeafFate.Broken ? paints.Broken : paints.Dead;
            var colour = Mix(Shade(paints.Leaf, slot.Shade), target, Ease(leaf.Turn));

            // A broken leaf trembles as it flushes, which is the one cue that it is about to go.
            var shake = leaf.Fate == LeafFate.Broken && leaf.Delay <= 0
                ? Math.Sin(Scene.Clock * 60 + leaf.Slot) * 0.12 * leaf.Turn
                : 0;

            DrawLeaf(context, layout, Scene.Displace(slot.Position), Scene.LeafAngle(slot) + shake, 1, new SolidColorBrush(colour));
        }
    }

    private void DrawFalling(DrawingContext context, TreeLayout layout, Paints paints)
    {
        foreach (var leaf in Scene.Falling)
        {
            var colour = leaf.Fate == LeafFate.Broken ? paints.Broken : Shade(paints.Dead, leaf.Shade);

            // Fallen red leaves brown where they lie: the alarm was the falling, not the lying.
            if (leaf.Landed) colour = Mix(colour, paints.Dead, Math.Clamp(leaf.Resting / FallingLeaf.RestSeconds, 0, 1) * 0.7);

            var alpha = (byte)(255 * leaf.Opacity);
            DrawLeaf(context, layout, leaf.Position, leaf.Angle, 1,
                new SolidColorBrush(Color.FromArgb(alpha, colour.R, colour.G, colour.B)));
        }
    }

    private static void DrawLeaf(DrawingContext context, TreeLayout layout, TreePoint at, double angle, double size, IBrush brush)
    {
        var p = layout.ToScreen(at);
        var length = LeafLength * layout.Unit * size;
        var width = LeafWidth * layout.Unit * size;

        // Stalk at the point, blade pointing along the angle.
        var matrix = Matrix.CreateScale(length, width)
                     * Matrix.CreateRotation(angle)
                     * Matrix.CreateTranslation(p.X, p.Y);

        using (context.PushTransform(matrix))
            context.DrawGeometry(brush, null, LeafShape);
    }

    private IBrush[] ShadeBrushes(Color leaf)
    {
        var brushes = new IBrush[ShadeBins];
        for (var b = 0; b < ShadeBins; b++) brushes[b] = new SolidColorBrush(Shade(leaf, (b + 0.5) / ShadeBins));
        return brushes;
    }

    /// <summary>A leaf's own variation: a little darker or a little more yellow-green than the tree's colour.</summary>
    private static Color Shade(Color colour, double shade) =>
        shade < 0.5
            ? Mix(colour, Colors.Black, (0.5 - shade) * 0.45)
            : Mix(colour, Color.FromRgb(214, 236, 120), (shade - 0.5) * 0.45);

    // ── visitors ──────────────────────────────────────────────────────────────

    private void DrawCaterpillars(DrawingContext context, TreeLayout layout, Paints paints)
    {
        foreach (var caterpillar in Scene.Caterpillars)
        {
            using var appear = context.PushOpacity(caterpillar.Appear);

            var body = Scene.BodyOf(caterpillar);
            var skin = paints.Caterpillar;
            var dark = Mix(skin, Colors.Black, 0.45);
            var outline = new Pen(new SolidColorBrush(dark), Math.Max(0.8, 0.0018 * layout.Unit));
            var fill = new SolidColorBrush(skin);
            var spot = new SolidColorBrush(Mix(skin, Colors.Black, 0.6));

            // Tail first, so each segment overlaps the one behind it.
            for (var i = body.Count - 1; i >= 0; i--)
            {
                var (centre, radius) = body[i];
                var at = Pt(layout.ToScreen(Scene.Displace(centre)));
                var r = radius * layout.Unit;

                context.DrawEllipse(i == 0 ? new SolidColorBrush(Mix(skin, Colors.Black, 0.25)) : fill, outline, at, r, r);
                if (i > 0) context.DrawEllipse(spot, null, new Point(at.X, at.Y - r * 0.35), r * 0.28, r * 0.28);
            }

            // The face: two eyes looking where it is going, and antennae.
            var (headCentre, headRadius) = body[0];
            var head = Pt(layout.ToScreen(Scene.Displace(headCentre)));
            var hr = headRadius * layout.Unit;
            var facing = caterpillar.Direction * (Scene.LimbOf(caterpillar).At(1).X >= Scene.LimbOf(caterpillar).At(0).X ? 1 : -1);

            var eye = new Point(head.X + facing * hr * 0.4, head.Y - hr * 0.2);
            context.DrawEllipse(Brushes.White, null, eye, hr * 0.32, hr * 0.32);
            context.DrawEllipse(Brushes.Black, null, new Point(eye.X + facing * hr * 0.1, eye.Y), hr * 0.16, hr * 0.16);

            var feeler = new Pen(new SolidColorBrush(dark), Math.Max(0.8, hr * 0.14), lineCap: PenLineCap.Round);
            var bob = Math.Sin(Scene.Clock * 5 + caterpillar.Key.Length) * hr * 0.25;
            context.DrawLine(feeler, new Point(head.X - facing * hr * 0.1, head.Y - hr * 0.8),
                new Point(head.X + facing * hr * 0.3, head.Y - hr * 1.7 + bob));
            context.DrawLine(feeler, new Point(head.X + facing * hr * 0.3, head.Y - hr * 0.8),
                new Point(head.X + facing * hr * 0.9, head.Y - hr * 1.6 - bob));
        }
    }

    private void DrawButterflies(DrawingContext context, TreeLayout layout, Paints paints)
    {
        foreach (var b in Scene.Butterflies)
        {
            using var fade = context.PushOpacity(b.Opacity);

            var at = Pt(layout.ToScreen(b.Position));
            var span = 0.042 * b.Size * layout.Unit;
            var open = b.WingOpen;

            var wing = new SolidColorBrush(paints.Butterfly);
            var edge = new Pen(new SolidColorBrush(Mix(paints.Butterfly, Colors.Black, 0.55)), Math.Max(0.8, span * 0.06));
            var lower = new SolidColorBrush(Mix(paints.Butterfly, Colors.White, 0.25));

            foreach (var side in new[] { -1, 1 })
            {
                // Seen from the front, a closing wing just gets narrower.
                var w = span * open;
                context.DrawEllipse(wing, edge, new Point(at.X + side * w * 0.55, at.Y - span * 0.25), w * 0.6, span * 0.5);
                context.DrawEllipse(lower, edge, new Point(at.X + side * w * 0.42, at.Y + span * 0.35), w * 0.42, span * 0.32);
            }

            var body = new Pen(new SolidColorBrush(Color.FromRgb(40, 30, 20)), Math.Max(1, span * 0.16), lineCap: PenLineCap.Round);
            context.DrawLine(body, new Point(at.X, at.Y - span * 0.55), new Point(at.X, at.Y + span * 0.6));
        }
    }

    // ── edit mode ─────────────────────────────────────────────────────────────

    private void DrawEditChrome(DrawingContext context, TreeLayout layout)
    {
        var box = layout.Box;
        context.DrawRectangle(EditFill, EditPen, new Rect(box.X, box.Y, box.Width, box.Height));

        foreach (var corner in TreeLayout.Corners)
        {
            var handle = layout.Handle(corner);
            var fill = Held == corner ? SaveBrush : HandleBrush;
            context.DrawRectangle(fill, HandlePen,
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
        if (Held == (tick ? TreeGrip.Save : TreeGrip.Cancel))
            context.DrawEllipse(ButtonHeld, null, centre, radius, radius);

        var r = radius * 0.48;
        var pen = new Pen(Brushes.White, Math.Max(1.6, radius * 0.20))
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
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

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Geometry BuildLeaf()
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, 0), true);
            ctx.CubicBezierTo(new Point(0.25, -0.62), new Point(0.75, -0.5), new Point(1, 0));
            ctx.CubicBezierTo(new Point(0.75, 0.5), new Point(0.25, 0.62), new Point(0, 0));
            ctx.EndFigure(true);
        }

        return geometry;
    }

    private static Point Pt(ScenePoint p) => new(p.X, p.Y);

    private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

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

    private Color Colour(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        if (_colours.TryGetValue(hex, out var cached)) return cached;

        var parsed = Color.TryParse(hex, out var colour) ? colour : fallback;
        _colours[hex] = parsed;
        return parsed;
    }
}

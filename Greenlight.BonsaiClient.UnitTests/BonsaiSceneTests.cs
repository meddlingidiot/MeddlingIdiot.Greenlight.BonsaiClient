using Greenlight.Sdk.Protocol;
using static Greenlight.BonsaiClient.UnitTests.Snapshots;

namespace Greenlight.BonsaiClient.UnitTests;

public class BonsaiSceneTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private static (BonsaiScene Scene, Gardener Gardener) Planted(int passes = 0, int seed = 21)
    {
        var gardener = new Gardener(BonsaiLife.Plant(seed), BonsaiTree.Grow(seed));
        if (passes > 0) gardener.Observe(Passes(1, passes));

        var scene = new BonsaiScene(new TreePlacement(), gardener, randomSeed: 3) { Mood = TreeMood.Green };
        scene.Resize(1920, 1080);
        return (scene, gardener);
    }

    private static void Run(BonsaiScene scene, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame.TotalSeconds) scene.Advance(Frame);
    }

    [Fact]
    public void A_tree_comes_back_exactly_as_it_was_left()
    {
        var (scene, gardener) = Planted(passes: 300);

        Assert.Equal(gardener.VisibleLeaves, scene.ShownLeaves);
        Assert.Equal(1, scene.Growth(0));
        Assert.Equal(1, scene.Growth(scene.ShownLeaves - 1));
    }

    [Fact]
    public void New_leaves_sprout_one_after_another_rather_than_all_at_once()
    {
        var (scene, gardener) = Planted(passes: 300);
        var before = scene.ShownLeaves;

        gardener.Observe(Passes(1000, 20));
        scene.Advance(Frame);

        Assert.InRange(scene.ShownLeaves, before + 1, before + 2);

        Run(scene, 5);
        Assert.Equal(gardener.VisibleLeaves, scene.ShownLeaves);
        Assert.Equal(1, scene.Growth(scene.ShownLeaves - 1));
    }

    [Fact]
    public void A_break_turns_leaves_red_and_drops_them()
    {
        var (scene, gardener) = Planted(passes: 600);
        var before = scene.ShownLeaves;

        var observed = gardener.Observe(With(GreenlightStatus.Red));
        scene.MarkBroken(observed.Broken);
        scene.Mood = TreeMood.Red;
        scene.Advance(Frame);

        Assert.Equal(before - observed.Broken, scene.ShownLeaves);
        Assert.Equal(observed.Broken, scene.Turning.Count);
        Assert.All(scene.Turning, leaf => Assert.Equal(LeafFate.Broken, leaf.Fate));

        Run(scene, 4);
        Assert.Empty(scene.Turning);
        Assert.Equal(observed.Broken, scene.Falling.Count);
        Assert.All(scene.Falling, leaf => Assert.Equal(LeafFate.Broken, leaf.Fate));
    }

    [Fact]
    public void Leaves_lost_to_a_long_breakage_wilt_brown_rather_than_flush_red()
    {
        var (scene, gardener) = Planted(passes: 600);

        gardener.Tend(TimeSpan.FromHours(1), GreenlightStatus.Red);
        scene.Advance(Frame);

        Assert.NotEmpty(scene.Turning);
        Assert.All(scene.Turning, leaf => Assert.Equal(LeafFate.Wilted, leaf.Fate));
    }

    [Fact]
    public void Fallen_leaves_land_and_eventually_go()
    {
        var (scene, gardener) = Planted(passes: 600);
        scene.MarkBroken(gardener.Observe(With(GreenlightStatus.Red)).Broken);

        Run(scene, 15);
        Assert.All(scene.Falling, leaf =>
        {
            Assert.True(leaf.Landed);
            Assert.True(leaf.Position.Y is BonsaiScene.SoilY or BonsaiScene.TableY || leaf.Position.Y >= BonsaiScene.SoilY);
        });

        Run(scene, FallingLeaf.RestSeconds + FallingLeaf.FadeSeconds);
        Assert.Empty(scene.Falling);
    }

    [Fact]
    public void A_waiting_pull_request_puts_a_caterpillar_on_the_tree()
    {
        var (scene, _) = Planted(passes: 300);

        scene.SetWaiting([new WaitingReview("gh:repo:12", TimeSpan.FromHours(1))]);

        var caterpillar = Assert.Single(scene.Caterpillars);
        Assert.True(caterpillar.Limb >= 0, "a grown tree should put it on a branch, not the trunk");
    }

    [Fact]
    public void A_sapling_puts_its_caterpillar_on_the_trunk()
    {
        var (scene, _) = Planted(passes: 0);

        scene.SetWaiting([new WaitingReview("gh:repo:12", TimeSpan.Zero)]);
        Assert.Equal(-1, Assert.Single(scene.Caterpillars).Limb);
    }

    [Fact]
    public void The_longer_a_review_waits_the_fatter_the_caterpillar()
    {
        Assert.Equal(1, Caterpillar.SizeFor(TimeSpan.Zero));
        Assert.True(Caterpillar.SizeFor(TimeSpan.FromDays(1)) > Caterpillar.SizeFor(TimeSpan.FromHours(1)));
        Assert.Equal(Caterpillar.MaximumSize, Caterpillar.SizeFor(TimeSpan.FromDays(30)));
    }

    [Fact]
    public void A_reviewed_pull_request_flies_off_as_a_butterfly()
    {
        var (scene, _) = Planted(passes: 300);
        scene.SetWaiting([new WaitingReview("gh:repo:12", TimeSpan.FromHours(1))]);
        Run(scene, 1);

        scene.SetWaiting([]);

        Assert.Empty(scene.Caterpillars);
        Assert.Single(scene.Butterflies);

        Run(scene, Butterfly.LifeSeconds + 0.5);
        Assert.Empty(scene.Butterflies);
    }

    [Fact]
    public void Greenlight_going_away_takes_the_caterpillars_without_a_butterfly()
    {
        var (scene, _) = Planted(passes: 300);
        scene.SetWaiting([new WaitingReview("gh:repo:12", TimeSpan.FromHours(1))]);
        Run(scene, 1);

        scene.SetWaiting([], quietly: true);

        Assert.Empty(scene.Caterpillars);
        Assert.Empty(scene.Butterflies);
    }

    [Fact]
    public void There_are_never_more_caterpillars_than_the_cap_and_the_oldest_reviews_win()
    {
        var (scene, _) = Planted(passes: 300);

        var waiting = Enumerable.Range(1, 10)
            .Select(i => new WaitingReview($"pr:{i}", TimeSpan.FromHours(i)))
            .ToList();
        scene.SetWaiting(waiting);

        Assert.Equal(BonsaiScene.MaximumCaterpillars, scene.Caterpillars.Count);
        Assert.Contains(scene.Caterpillars, c => c.Key == "pr:10");
        Assert.DoesNotContain(scene.Caterpillars, c => c.Key == "pr:1");
    }

    [Fact]
    public void Caterpillars_spread_out_over_the_branches()
    {
        var (scene, _) = Planted(passes: 300);

        scene.SetWaiting(Enumerable.Range(1, 4).Select(i => new WaitingReview($"pr:{i}", TimeSpan.Zero)).ToList());

        Assert.Equal(4, scene.Caterpillars.Select(c => c.Limb).Distinct().Count());
    }

    [Fact]
    public void A_caterpillar_crawls_to_the_end_of_its_branch_eats_and_comes_back_without_ever_leaving_it()
    {
        var (scene, _) = Planted(passes: 300);
        scene.SetWaiting([new WaitingReview("gh:repo:12", TimeSpan.FromDays(2))]);
        var caterpillar = scene.Caterpillars[0];
        var (min, max) = scene.RangeOf(caterpillar);

        var munched = false;
        var turnedBack = false;

        for (var t = 0.0; t < 240; t += Frame.TotalSeconds)
        {
            scene.Advance(Frame);

            munched |= caterpillar.Activity == CaterpillarActivity.Munching;
            turnedBack |= munched && caterpillar.Direction < 0;

            // The head may go one body-length past an end on the frame it turns round, since the
            // tail becomes the head — but never off the branch.
            Assert.InRange(caterpillar.Along, min - 0.35, max + 0.01);

            foreach (var (centre, radius) in scene.BodyOf(caterpillar))
            {
                Assert.True(double.IsFinite(centre.X) && double.IsFinite(centre.Y));
                Assert.True(radius > 0);
            }
        }

        Assert.True(munched, "it never reached the end of its branch");
        Assert.True(turnedBack, "it never turned round after eating");
    }

    [Fact]
    public void A_building_tree_shows_the_bud_of_its_next_leaf()
    {
        var (scene, _) = Planted(passes: 30);

        Assert.Null(scene.BudSlot);

        scene.IsBuilding = true;
        Assert.Equal(scene.ShownLeaves, scene.BudSlot);

        scene.Mood = TreeMood.Off;
        Assert.Null(scene.BudSlot);
    }

    [Fact]
    public void A_dormant_tree_does_not_sway()
    {
        var (scene, _) = Planted(passes: 300);
        scene.Mood = TreeMood.Off;
        var tip = new TreePoint(0.4, -0.8);

        var first = scene.Displace(tip);
        Run(scene, 1.3);

        Assert.Equal(first, scene.Displace(tip));
    }

    [Fact]
    public void A_sick_tree_droops()
    {
        var (scene, gardener) = Planted(passes: 300);
        var tip = new TreePoint(0.45, -0.5);
        scene.Mood = TreeMood.Off; // no breeze to confuse the measurement
        var healthy = scene.Displace(tip);

        gardener.Life.Health = 0;
        Run(scene, 6);

        Assert.True(scene.Displace(tip).Y > healthy.Y + 0.03);
    }

    [Fact]
    public void A_month_of_random_weather_never_puts_anything_off_the_map()
    {
        var (scene, gardener) = Planted(passes: 0, seed: 99);
        var random = new Random(4);
        var buildId = 1;
        GreenlightStatus[] colours = [GreenlightStatus.Green, GreenlightStatus.Yellow, GreenlightStatus.Red, GreenlightStatus.Unknown];

        for (var step = 0; step < 3000; step++)
        {
            var status = colours[random.Next(colours.Length)];
            var passes = random.Next(3);
            var observed = gardener.Observe(Passes(buildId, passes, status));
            buildId += passes;

            scene.MarkBroken(observed.Broken);
            scene.Mood = status switch
            {
                GreenlightStatus.Green => TreeMood.Green,
                GreenlightStatus.Yellow => TreeMood.Amber,
                GreenlightStatus.Red => TreeMood.Red,
                _ => TreeMood.Off,
            };
            scene.IsBuilding = random.Next(4) == 0;
            gardener.Tend(TimeSpan.FromMinutes(random.Next(30)), status);

            if (random.Next(10) == 0)
                scene.SetWaiting(Enumerable.Range(0, random.Next(6))
                    .Select(i => new WaitingReview($"pr:{random.Next(8)}:{i}", TimeSpan.FromHours(random.Next(100))))
                    .DistinctBy(w => w.Key)
                    .ToList());

            for (var f = 0; f < 6; f++) scene.Advance(Frame);

            Assert.InRange(scene.ShownLeaves, 0, gardener.Tree.Slots.Count);
            Assert.InRange(scene.Health, 0, 1);
            Assert.InRange(gardener.Life.Health, 0, 1);
            Assert.True(scene.Falling.Count <= 160);

            foreach (var leaf in scene.Falling)
                Assert.True(double.IsFinite(leaf.Position.X) && double.IsFinite(leaf.Position.Y));

            foreach (var caterpillar in scene.Caterpillars)
            foreach (var (centre, _) in scene.BodyOf(caterpillar))
                Assert.True(double.IsFinite(centre.X) && double.IsFinite(centre.Y));
        }
    }

    // ── placing it ────────────────────────────────────────────────────────────

    [Fact]
    public void The_tree_cannot_be_dragged_off_the_desktop()
    {
        var (scene, _) = Planted();

        scene.MoveTo(new ScenePoint(-5000, 99999));
        var box = scene.Measure().Box;

        Assert.True(box.X >= -0.001);
        Assert.True(box.Bottom <= scene.Height + 0.001);
    }

    [Fact]
    public void Cancelling_an_edit_puts_it_back()
    {
        var (scene, _) = Planted();
        var before = scene.Placement.Copy();

        scene.BeginEdit();
        scene.MoveTo(new ScenePoint(100, 100));
        scene.CancelEdit();

        Assert.Equal(before.AnchorX, scene.Placement.AnchorX);
        Assert.Equal(before.AnchorY, scene.Placement.AnchorY);
    }

    [Fact]
    public void The_buttons_win_over_the_corner_they_sit_beside()
    {
        var (scene, _) = Planted();
        var layout = scene.Measure();

        Assert.Equal(TreeGrip.Save, scene.HitTest(layout.SaveButton.Centre));
        Assert.Equal(TreeGrip.Cancel, scene.HitTest(layout.CancelButton.Centre));
        Assert.Equal(TreeGrip.Body, scene.HitTest(layout.Box.Centre));
        Assert.Equal(TreeGrip.None, scene.HitTest(new ScenePoint(1, 1)));
    }

    [Fact]
    public void The_whole_grown_tree_and_its_pot_fit_in_the_box()
    {
        var (scene, _) = Planted(passes: 1000);
        var layout = scene.Measure();

        foreach (var slot in scene.Tree.Slots)
            Assert.True(layout.Box.Contains(layout.ToScreen(slot.Position)), $"leaf at {slot.Position} is outside the box");

        Assert.True(layout.ToScreen(new TreePoint(0, BonsaiScene.TableY)).Y <= layout.Box.Bottom);
    }
}

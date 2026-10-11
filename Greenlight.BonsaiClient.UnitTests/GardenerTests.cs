using Greenlight.Sdk;
using Greenlight.Sdk.Protocol;
using static Greenlight.BonsaiClient.UnitTests.Snapshots;

namespace Greenlight.BonsaiClient.UnitTests;

public class GardenerTests
{
    private static Gardener NewGardener(int seed = 11) =>
        new(BonsaiLife.Plant(seed), BonsaiTree.Grow(seed));

    /// <summary>A tree that has seen enough builds to be fully grown and fully leafed.</summary>
    private static Gardener Grown(int seed = 11)
    {
        var gardener = NewGardener(seed);
        gardener.Observe(Passes(1, 1000));
        Assert.Equal(gardener.Tree.Slots.Count, gardener.VisibleLeaves);
        return gardener;
    }

    [Fact]
    public void Each_passing_build_is_counted_once()
    {
        var gardener = NewGardener();

        Assert.Equal(3, gardener.Observe(Passes(1, 3)).Grown);
        Assert.Equal(0, gardener.Observe(Passes(1, 3)).Grown);
        Assert.Equal(1, gardener.Observe(Passes(1, 4)).Grown);

        Assert.Equal(4, gardener.Life.Lifetime);
        Assert.Equal(4, gardener.Life.Leaves);
    }

    [Fact]
    public void Failed_cancelled_and_running_builds_grow_nothing()
    {
        var gardener = NewGardener();

        var observed = gardener.Observe(With(GreenlightStatus.Green,
            Failed(1),
            Running(2),
            Build(3, GreenlightBuildStatus.Completed, GreenlightBuildResult.Cancelled),
            Build(4, GreenlightBuildStatus.Completed, GreenlightBuildResult.PartiallySucceeded)));

        Assert.Equal(0, observed.Grown);
        Assert.Equal(0, gardener.Life.Lifetime);
    }

    [Fact]
    public void A_running_build_is_counted_once_it_passes()
    {
        var gardener = NewGardener();

        gardener.Observe(With(GreenlightStatus.Green, Running(7)));
        Assert.Equal(1, gardener.Observe(With(GreenlightStatus.Green, Passed(7))).Grown);
    }

    [Fact]
    public void The_same_number_from_two_providers_is_two_builds()
    {
        var gardener = NewGardener();

        var observed = gardener.Observe(With(GreenlightStatus.Green,
            Passed(4031, GreenlightProvider.GitHub),
            Passed(4031, GreenlightProvider.AzureDevOps)));

        Assert.Equal(2, observed.Grown);
    }

    [Fact]
    public void Leaves_beyond_what_the_branches_can_hold_are_banked_not_shown()
    {
        var gardener = NewGardener();
        gardener.Observe(Passes(1, 2));

        // Two builds in, no pad has grown far enough to carry a leaf yet.
        Assert.Equal(2, gardener.Life.Leaves);
        Assert.Equal(0, gardener.VisibleLeaves);
    }

    [Fact]
    public void Going_red_shakes_off_a_share_of_the_leaves()
    {
        var gardener = Grown();
        var before = gardener.VisibleLeaves;

        var observed = gardener.Observe(With(GreenlightStatus.Red, Failed(5000)));

        Assert.Equal(Gardener.BreakCount(before), observed.Broken);
        Assert.Equal(before - observed.Broken, gardener.VisibleLeaves);
        Assert.True(gardener.Life.WasRed);
    }

    [Fact]
    public void Staying_red_does_not_shake_off_more_with_every_snapshot()
    {
        var gardener = Grown();
        gardener.Observe(With(GreenlightStatus.Red, Failed(5000)));

        Assert.Equal(0, gardener.Observe(With(GreenlightStatus.Red, Failed(5000))).Broken);
    }

    [Fact]
    public void A_restart_in_the_middle_of_a_breakage_does_not_break_it_again()
    {
        var gardener = Grown();
        gardener.Observe(With(GreenlightStatus.Red, Failed(5000)));

        var reopened = new Gardener(gardener.Life, gardener.Tree);
        Assert.Equal(0, reopened.Observe(With(GreenlightStatus.Red, Failed(5000))).Broken);
    }

    [Fact]
    public void Unknown_neither_breaks_nor_forgets_a_breakage()
    {
        var gardener = Grown();
        gardener.Observe(With(GreenlightStatus.Red));
        gardener.Observe(With(GreenlightStatus.Unknown));

        Assert.True(gardener.Life.WasRed);
        Assert.Equal(0, gardener.Observe(With(GreenlightStatus.Red)).Broken);
    }

    [Fact]
    public void Fixing_it_and_breaking_it_again_is_a_second_breakage()
    {
        var gardener = Grown();
        gardener.Observe(With(GreenlightStatus.Red));
        gardener.Observe(With(GreenlightStatus.Green));

        Assert.True(gardener.Observe(With(GreenlightStatus.Red)).Broken > 0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(10, 3)]
    [InlineData(100, 15)]
    [InlineData(1000, 40)]
    public void A_break_takes_a_share_with_a_floor_and_a_ceiling(int leaves, int expected)
    {
        Assert.Equal(expected, Gardener.BreakCount(leaves));
    }

    [Fact]
    public void A_working_day_of_red_takes_a_healthy_tree_to_nothing()
    {
        var gardener = Grown();
        gardener.Observe(With(GreenlightStatus.Red));
        var afterBreak = gardener.VisibleLeaves;

        for (var minute = 0; minute < 8 * 60; minute++)
            gardener.Tend(TimeSpan.FromMinutes(1), GreenlightStatus.Red);

        Assert.Equal(0, gardener.Life.Health, 3);

        // The slow shed leaves about a tenth of what the break left behind.
        Assert.InRange(gardener.VisibleLeaves, afterBreak * 0.07, afterBreak * 0.13);
    }

    [Fact]
    public void The_shed_is_the_same_however_finely_time_is_sliced()
    {
        var coarse = Grown();
        var fine = Grown();

        coarse.Tend(TimeSpan.FromHours(2), GreenlightStatus.Red);
        for (var i = 0; i < 2 * 60 * 12; i++) fine.Tend(TimeSpan.FromSeconds(5), GreenlightStatus.Red);

        Assert.InRange(fine.VisibleLeaves - coarse.VisibleLeaves, -2, 2);
    }

    [Fact]
    public void Fixing_it_lets_the_tree_heal_but_does_not_give_its_leaves_back()
    {
        var gardener = Grown();
        gardener.Tend(TimeSpan.FromHours(4), GreenlightStatus.Red);
        var bare = gardener.VisibleLeaves;

        gardener.Tend(TimeSpan.FromHours(1), GreenlightStatus.Green);
        Assert.Equal(1.0, gardener.Life.Health, 3);
        Assert.Equal(bare, gardener.VisibleLeaves);
    }

    [Fact]
    public void Nothing_happens_to_a_tree_while_Greenlight_is_away()
    {
        var gardener = Grown();
        gardener.Life.Health = 0.5;

        Assert.Equal(0, gardener.Tend(TimeSpan.FromHours(10), GreenlightStatus.Unknown));
        Assert.Equal(0.5, gardener.Life.Health);
    }

    [Fact]
    public void A_passing_build_on_a_healthy_day_waters_the_tree()
    {
        var gardener = NewGardener();
        gardener.Life.Health = 0.5;

        gardener.Observe(Passes(1, 5));
        Assert.Equal(0.5 + 5 * Gardener.PassTonic, gardener.Life.Health, 6);
    }

    [Fact]
    public void A_passing_build_while_red_still_grows_a_leaf_but_does_not_heal()
    {
        var gardener = NewGardener();
        gardener.Life.Health = 0.5;

        var observed = gardener.Observe(Passes(1, 5, GreenlightStatus.Red));

        Assert.Equal(5, observed.Grown);
        Assert.Equal(0.5, gardener.Life.Health);
    }

    [Fact]
    public void The_memory_of_counted_builds_is_capped()
    {
        var gardener = NewGardener();
        gardener.Observe(Passes(1, Gardener.MemoryLength + 50));

        Assert.Equal(Gardener.MemoryLength, gardener.Life.Counted.Count);
        Assert.Equal(Gardener.MemoryLength + 50, gardener.Life.Lifetime);
    }

    [Fact]
    public void A_tree_survives_being_saved_and_read_back()
    {
        var file = Path.Combine(Path.GetTempPath(), $"bonsai-{Guid.NewGuid():N}", "tree.json");
        try
        {
            var gardener = Grown(31);
            gardener.Observe(With(GreenlightStatus.Red));
            Gardener.Save(gardener.Life, file);

            var back = Gardener.Load(file);

            Assert.Equal(31, back.Seed);
            Assert.Equal(gardener.Life.Lifetime, back.Lifetime);
            Assert.Equal(gardener.Life.Leaves, back.Leaves);
            Assert.True(back.WasRed);
            Assert.Equal(gardener.Life.Counted, back.Counted);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }

    [Fact]
    public void An_unreadable_tree_is_put_aside_not_overwritten()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"bonsai-{Guid.NewGuid():N}");
        var file = Path.Combine(folder, "tree.json");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(file, "{ this is not json");

            var fresh = Gardener.Load(file);

            Assert.Equal(0, fresh.Lifetime);
            Assert.Equal("{ this is not json", File.ReadAllText(file + ".unreadable"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

namespace Greenlight.BonsaiClient.UnitTests;

public class BonsaiTreeTests
{
    [Fact]
    public void The_same_seed_grows_the_same_tree()
    {
        var a = BonsaiTree.Grow(1234);
        var b = BonsaiTree.Grow(1234);

        Assert.Equal(a.Slots.Count, b.Slots.Count);
        Assert.Equal(a.Slots.Select(s => s.Position), b.Slots.Select(s => s.Position));
        Assert.Equal(a.Trunk.Points, b.Trunk.Points);
    }

    [Fact]
    public void Different_seeds_grow_different_trees()
    {
        Assert.NotEqual(BonsaiTree.Grow(1).Trunk.Points, BonsaiTree.Grow(2).Trunk.Points);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(77)]
    [InlineData(4031)]
    [InlineData(int.MaxValue)]
    public void Every_leaf_hangs_inside_the_box_above_the_soil(int seed)
    {
        var tree = BonsaiTree.Grow(seed);

        Assert.InRange(tree.Slots.Count, 200, 500);
        foreach (var slot in tree.Slots)
        {
            Assert.InRange(slot.Position.X, -0.64, 0.64);
            Assert.InRange(slot.Position.Y, -1.04, -0.05);
        }
    }

    [Fact]
    public void A_tree_that_has_seen_nothing_has_no_room_for_leaves()
    {
        Assert.Equal(0, BonsaiTree.Grow(5).Capacity(0));
    }

    [Fact]
    public void Room_for_leaves_only_ever_grows_and_eventually_fits_every_slot()
    {
        var tree = BonsaiTree.Grow(5);
        var previous = 0;

        for (var lifetime = 0; lifetime < 400; lifetime++)
        {
            var capacity = tree.Capacity(lifetime);
            Assert.True(capacity >= previous, $"capacity fell at lifetime {lifetime}");
            previous = capacity;
        }

        Assert.Equal(tree.Slots.Count, previous);
    }

    [Fact]
    public void A_young_tree_has_a_little_room_after_its_first_few_builds()
    {
        var tree = BonsaiTree.Grow(9);
        Assert.InRange(tree.Capacity(5), 1, 40);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(123)]
    public void A_new_branch_waits_for_the_pads_before_it_to_fill(int seed)
    {
        var tree = BonsaiTree.Grow(seed);

        for (var b = 1; b < tree.Branches.Count; b++)
        {
            var earlier = tree.Slots.Count(s => s.Pad < b);
            Assert.InRange(tree.Branches[b].UnlockAt, earlier * 0.5, earlier * 0.7);
        }
    }

    [Fact]
    public void The_wood_never_runs_far_ahead_of_the_leaves()
    {
        // At a leaf a build, no more than two branches should ever be out and bare.
        var tree = BonsaiTree.Grow(17);

        for (var lifetime = 0; lifetime < tree.MatureAt; lifetime++)
        {
            var leaves = Math.Min(lifetime, tree.Capacity(lifetime));
            var leafedPads = tree.Slots.Take(leaves).Select(s => s.Pad).Distinct().Count();
            var grownBranches = tree.Branches.Count(b => BonsaiTree.Extent(b, lifetime) > 0);

            Assert.True(grownBranches - leafedPads <= 2, $"{grownBranches} branches but {leafedPads} with leaves at {lifetime}");
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(42)]
    public void No_branch_ever_sprouts_above_the_top_of_the_trunk(int seed)
    {
        var tree = BonsaiTree.Grow(seed);

        for (var lifetime = 0.0; lifetime < tree.MatureAt + 5; lifetime += 0.25)
        {
            var top = -tree.TrunkReach(lifetime) * BonsaiTree.TrunkHeight;
            foreach (var branch in tree.Branches.Where(b => BonsaiTree.Extent(b, lifetime) > 0))
                Assert.True(branch.Points[0].Y >= top - 0.01,
                    $"branch {branch.Pad} at {branch.Points[0].Y:F3} is above the trunk's {top:F3} at lifetime {lifetime}");
        }
    }

    [Fact]
    public void The_trunk_starts_as_a_sapling_and_climbs_to_full_height()
    {
        var tree = BonsaiTree.Grow(8);

        Assert.Equal(BonsaiTree.SaplingReach, tree.TrunkReach(-10), 3);
        Assert.Equal(1.0, tree.TrunkReach(tree.MatureAt + 10), 3);
    }

    [Fact]
    public void A_limb_is_walked_by_distance_not_by_point()
    {
        var limb = new TreeLimb([new TreePoint(0, 0), new TreePoint(1, 0), new TreePoint(1, 3)], 0.1, 0.1, 0, 0);

        Assert.Equal(4, limb.Length, 6);
        Assert.Equal(new TreePoint(1, 1), limb.At(0.5));
        Assert.Equal(new TreePoint(0, 0), limb.At(-1));
        Assert.Equal(new TreePoint(1, 3), limb.At(2));
    }
}

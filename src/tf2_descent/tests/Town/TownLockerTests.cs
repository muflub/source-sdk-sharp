using SourceSharp.Tests;
using SourceSharp.Abi;

using TF2.Descent.Server.Town;

using Xunit;

namespace TF2.Descent.Tests.Town;

/// <summary>
/// The town's resupply locker (the user, 2026-11-15: "the medical locker in town should work"): a
/// hero who touches the stash stall's locker is resupplied, its doors open and then close, and he
/// waits TF2's three seconds before the next; nowhere but the town, and each level starts afresh.
/// </summary>
public sealed class TownLockerTests
{
    private sealed class Resupplies : ITownResupply
    {
        public readonly List<int> Given = [];
        public bool Living = true;

        public bool Resupply(int player)
        {
            if (!Living)
                return false;
            Given.Add(player);
            return true;
        }
    }

    private sealed class Rig
    {
        public readonly TownRig Town;
        public readonly Resupplies Resupply = new();
        public readonly TownLocker Locker;

        public Rig(bool place = true)
        {
            Town = new TownRig(place);
            Locker = new TownLocker(Town.Service, Town.World, Resupply, Town.Owner.Gear.Hero.Heroes, Town.Owner.Gear.Log);
        }

        public (int Entity, Vector At) Placed =>
            Town.Service.Placed.Where(p => p.Stall == TownStalls.IndexOf(TownLocker.Stall)).Select(p => (p.Entity, p.At)).Single();

        /// <summary>The hero stands against the locker's street side (it is solid: 48 from its middle).</summary>
        public void StandAgainst() => Town.World.Feet = Placed.At + new Vector(0f, 50f, 0f);

        public void At(double seconds)
        {
            Town.World.Clock = seconds;
            Locker.Check();
        }
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_hero_standing_against_the_locker_in_town_is_resupplied()
    {
        var rig = new Rig();
        rig.StandAgainst();

        rig.At(1.0);

        Assert.Equal([TownRig.Index], rig.Resupply.Given);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_hero_a_step_away_from_the_locker_is_not_resupplied()
    {
        var rig = new Rig();
        rig.Town.World.Feet = rig.Placed.At + new Vector(0f, 48f + 24f + TownLocker.TouchSkin + 8f, 0f);

        rig.At(1.0);

        Assert.Empty(rig.Resupply.Given);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_hero_on_the_floor_above_the_locker_does_not_touch_it()
    {
        var rig = new Rig();
        rig.Town.World.Feet = rig.Placed.At + new Vector(0f, 50f, 96f + TownLocker.TouchSkin + 40f);

        rig.At(1.0);

        Assert.Empty(rig.Resupply.Given);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Standing_on_waits_three_seconds_for_the_next_resupply()
    {
        var rig = new Rig();
        rig.StandAgainst();

        rig.At(1.0);
        rig.At(2.0);
        rig.At(3.9);
        Assert.Single(rig.Resupply.Given);

        rig.At(4.0);
        Assert.Equal(2, rig.Resupply.Given.Count);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_doors_open_on_a_resupply_and_close_two_seconds_later()
    {
        var rig = new Rig();
        rig.StandAgainst();

        rig.At(1.0);
        Assert.Equal([(rig.Placed.Entity, TownLocker.OpenSequence)], rig.Town.World.Animations);

        rig.At(2.9);
        Assert.Single(rig.Town.World.Animations);

        rig.At(3.0);
        Assert.Equal((rig.Placed.Entity, TownLocker.CloseSequence), rig.Town.World.Animations[^1]);
        Assert.Equal(2, rig.Town.World.Animations.Count);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_hero_with_no_living_body_is_not_put_on_the_cooldown()
    {
        var rig = new Rig();
        rig.StandAgainst();
        rig.Resupply.Living = false;
        rig.At(1.0);
        Assert.Null(rig.Locker.NextFor(TownRig.Index));
        Assert.Empty(rig.Town.World.Animations);

        rig.Resupply.Living = true;
        rig.At(1.1);

        Assert.Equal([TownRig.Index], rig.Resupply.Given);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Off_the_town_the_locker_resupplies_nobody()
    {
        var rig = new Rig(place: false);
        rig.Town.Service.Place("tf2_descent-1-s1");
        rig.Town.World.Feet = new Vector(-150f, -190f, 0f);

        rig.At(1.0);

        Assert.Empty(rig.Resupply.Given);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_cooldown_stamped_on_the_last_level_clock_is_gone_on_the_next()
    {
        var rig = new Rig();
        rig.StandAgainst();
        rig.At(1.0);

        NextLevel(rig);
        rig.At(2.0);

        Assert.Equal(2, rig.Resupply.Given.Count);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_close_due_on_the_last_level_is_not_played_on_the_next()
    {
        var rig = new Rig();
        rig.StandAgainst();
        rig.At(1.0);

        NextLevel(rig);
        rig.Town.World.Feet = null;
        rig.At(3.5);

        Assert.Equal([(rig.Placed.Entity, TownLocker.OpenSequence)], rig.Town.World.Animations);
    }

    private static void NextLevel(Rig rig)
    {
        rig.Locker.OnLevelShutdown();
        rig.Town.Service.OnLevelShutdown();
        rig.Town.World.Clock = 0;
        rig.Town.Service.Place(TownRig.Town);
    }
}

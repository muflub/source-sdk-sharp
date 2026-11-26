using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// What a horde brain's view carries for the families (plan_descent_logic 3.6, HO6;
/// <see cref="HordeView"/>): its pack-mates holding bodies with their family and life, its
/// own most life, its gun's look and its line of fire, all in fields the core's recording
/// writes; a robot of another pack is not in it; the hunt does not chase a pack-mate.
/// </summary>
public sealed class HordeViewTests
{
    private static PerceivedView ViewOf(BrainRig rig, int body)
    {
        Assert.True(rig.Brains.Session.Snapshots.TryGet(new Tick((int)rig.Now), out var snap));
        return snap.ViewFor(body) ?? throw new InvalidOperationException($"no view for body {body}");
    }

    private static ActorFacts[] Mates(PerceivedView view) => view.Sighted.ToArray().Where(f => HordeView.IsMate(f)).ToArray();

    [Fact]
    public void A_view_carries_a_pack_mate_with_its_family_and_life()
    {
        var rig = new BrainRig();
        var soldier = rig.Robot(Centre(0), "soldier", seed: 1);
        var medic = rig.Robot(Centre(0) + At(80, 0), "medic", seed: 2, joinLastPack: true);
        int sb = rig.Lend(soldier), mb = rig.Lend(medic);
        rig.Roster.Hurt(soldier.Index, rig.Roster.Stats(soldier.Index).Life * 0.25);
        rig.Tick(2);

        var mate = Assert.Single(Mates(ViewOf(rig, mb)));

        Assert.Equal(rig.Brains.BrainAt(sb), mate.Id);
        Assert.Equal(HordeWeapons.RocketLauncher, HordeView.MateFamily(mate));
        Assert.Equal(0.75f, HordeView.MateLife(mate), 3);
        Assert.Equal(rig.Poses.Position(sb), mate.Origin);
    }

    [Fact]
    public void A_robot_of_another_pack_is_not_in_the_view()
    {
        var rig = new BrainRig();
        var a = rig.Robot(Centre(0), "soldier", seed: 1);
        var b = rig.Robot(Centre(0) + At(80, 0), "medic", seed: 2);
        rig.Lend(a);
        int mb = rig.Lend(b);
        rig.Tick(2);

        Assert.Empty(Mates(ViewOf(rig, mb)));
    }

    [Fact]
    public void A_view_holds_the_nearest_seven_pack_mates_nearest_first()
    {
        var rig = new BrainRig();
        var me = rig.Robot(Centre(0), "medic", seed: 100);
        int mine = rig.Lend(me);
        var far = new List<int>();
        for (int k = 1; k <= 9; k++)
            far.Add(rig.Lend(rig.Robot(Centre(0) + At(20 * k, 0), "soldier", seed: (ulong)k, joinLastPack: true)));
        rig.Slide = false;
        rig.Tick(2);

        var mates = Mates(ViewOf(rig, mine));

        Assert.Equal(HordeView.PackMates, mates.Length);
        Assert.Equal(far.Take(7).Select(b => rig.Brains.BrainAt(b)), mates.Select(m => m.Id));
    }

    [Fact]
    public void A_view_carries_the_robots_most_life_beside_its_life()
    {
        var rig = new BrainRig();
        var id = rig.Robot(Centre(0), "soldier");
        int body = rig.Lend(id);
        rig.Roster.Hurt(id.Index, 50.5);
        rig.Tick(2);

        var self = ViewOf(rig, body).Self;

        Assert.Equal((int)Math.Ceiling(rig.Roster.Stats(id.Index).Life), self.Armour);
        Assert.Equal((int)Math.Ceiling(rig.Roster.Life(id.Index)), self.Health);
        Assert.Equal((float)self.Health / self.Armour, HordeView.Life(self), 5);
    }

    /// <summary>Found live (2026-11-22): a x1.75 giant sniper aimed from a scale-1 eye and never settled, so a view carries the robot's scale.</summary>
    [Fact]
    public void A_view_carries_the_robots_scale()
    {
        var rig = new BrainRig();
        int body = rig.Lend(rig.Robot(Centre(0), "giant_sniper", scale: 1.75));
        rig.Tick(2);

        Assert.Equal(1.75f, HordeView.Scale(ViewOf(rig, body).Self), 3);
    }

    [Fact]
    public void A_view_carries_the_guns_look()
    {
        var rig = new BrainRig();
        int body = rig.Lend(rig.Robot(Centre(0)));
        rig.Aim.Look = _ => new AiAngles(-12.5f, 33f, 0f);
        rig.Tick(2);

        Assert.Equal(new AiAngles(-12.5f, 33f, 0f), ViewOf(rig, body).Self.Eye);
    }

    [Fact]
    public void The_line_of_fire_is_traced_on_the_ticks_the_brain_decides_and_not_otherwise()
    {
        var rig = new BrainRig();
        int body = rig.Lend(rig.Robot(Centre(0)));
        rig.Aim.Line = (_, _) => 333.7f;
        var seen = new List<(bool Decides, int Line)>();
        for (int t = 0; t < 2 * DecideSchedule.Period; t++)
        {
            rig.Tick();
            seen.Add((DecideSchedule.IsDecideTick(new Tick((int)rig.Now), body), ViewOf(rig, body).Self.Reserve));
        }

        Assert.Contains(seen, s => s.Decides);
        Assert.All(seen, s => Assert.Equal(s.Decides ? 333 : HordeView.NotTraced, s.Line));
        Assert.Equal(seen.Count(s => s.Decides), rig.Aim.Traced[body]);
    }

    [Fact]
    public void A_mates_family_and_life_round_trip_through_the_packed_field()
    {
        var f = new ActorFacts(new ActorId(3, 1), default, default, HordeView.Mate(HordeWeapons.SyringeGun, 0.4567), ActorActivity.None, 1f, Team: HordeBrains.RobotTeam);

        Assert.Equal(HordeWeapons.SyringeGun, HordeView.MateFamily(f));
        Assert.Equal(0.457f, HordeView.MateLife(f), 3);
        Assert.True(HordeView.IsMate(f));
        Assert.False(HordeView.IsHero(f));
    }

    [Fact]
    public void A_heros_facing_round_trips_through_the_packed_field()
    {
        foreach (float yaw in new[] { 0f, 90f, 179.9f, -90f, 359.95f, 725f })
        {
            var f = new ActorFacts(new ActorId(50, 1), default, default, HordeView.Hero(yaw), ActorActivity.None, 1f, Team: HordeBrains.HeroTeam);
            float want = ((yaw % 360f) + 360f) % 360f;
            float got = HordeView.HeroYaw(f);
            Assert.True(MathF.Abs(got - want) < 0.051f || MathF.Abs(got - want) > 359.9f, $"yaw {yaw}: {got}");
        }
    }

    [Fact]
    public void A_view_carries_the_facing_of_a_hero_it_sees()
    {
        var rig = new BrainRig();
        var soldier = rig.Robot(Centre(0), "soldier", seed: 1);
        int b = rig.Lend(soldier, yaw: 0f);
        rig.Hero(1, Centre(0) + At(300, 0), yaw: 135f);
        rig.Tick(150);

        var hero = Assert.Single(ViewOf(rig, b).Sighted.ToArray(), f => HordeView.IsHero(f));

        Assert.Equal(135f, HordeView.HeroYaw(hero), 1);
    }

    [Fact]
    public void Each_attack_row_has_its_family_number_and_name()
    {
        foreach (var t in TheRules.Content.Robots.Rows)
        {
            int n = HordeWeapons.Of(t.Attack);
            Assert.True(n > HordeWeapons.None && n < HordeWeapons.Count, t.Id);
            Assert.Equal(t.Attack, HordeWeapons.NameOf(n));
        }

        Assert.Equal(HordeWeapons.Knife, HordeWeapons.Of(TheRules.Robots.Template("spy").Attack));
        Assert.Equal(HordeWeapons.SentryBuster, HordeWeapons.Of(TheRules.Robots.Template("sentry_buster").Attack));
    }

    [Fact]
    public void The_hunt_does_not_chase_a_pack_mate()
    {
        var rig = new BrainRig();
        var a = rig.Robot(Centre(0), "soldier", seed: 1);
        var b = rig.Robot(Centre(0) + At(400, 0), "soldier", seed: 2, joinLastPack: true);
        int ab = rig.Lend(a);
        rig.Lend(b);
        rig.Tick(60);

        Assert.Contains(Mates(ViewOf(rig, ab)), m => m.Id == rig.Brains.BrainAt(rig.Roster.Body(b.Index)));
        Assert.Equal(HuntDecider.Node.Hold, rig.NodeOf(ab));
    }
}

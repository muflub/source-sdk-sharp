using System.Collections.Immutable;
using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// A boss's adds come out of its arena's open mouths (the ARNA contract of 2026-11-16): a leg-1
/// mouth is open from the start, the phase that is a mouth's leg opens it, an add stands on an
/// open mouth facing the mouth's way, and an arena whose layout has no mouths keeps its markers.
/// </summary>
public sealed class ArenaMouthTests
{
    private static readonly AiVector Leg1A = Centre(BossRig.ArenaRoom) + At(-300, -300, 0), Leg1B = Centre(BossRig.ArenaRoom) + At(300, -300, 0),
                                     Leg2 = Centre(BossRig.ArenaRoom) + At(300, 300, 0), Leg3 = Centre(BossRig.ArenaRoom) + At(-300, 300, 0);

    private static HordeArenaLayout Layout() => new(ImmutableArray<TankTrackPoint>.Empty,
        [new AddMouth(Leg1A, 45f, 1), new AddMouth(Leg2, 225f, 2), new AddMouth(Leg1B, 135f, 1), new AddMouth(Leg3, 315f, 3)]);

    private static List<int> NewRows(BossRig rig, int before) => [.. Enumerable.Range(before, rig.Roster.Count - before)];

    [Fact]
    public void Adds_at_the_start_come_out_of_the_leg_1_mouths_in_file_order()
    {
        var rig = new BossRig(layout: Layout());
        rig.Lend();
        int before = rig.Roster.Count;

        rig.Adds.Summon(rig.Controller, rig.Row, "demoman", 2, 9, rig.Now, legs: 1);

        Assert.Equal([Leg1A, Leg1B], NewRows(rig, before).Select(r => rig.Roster.Post(r)));
    }

    [Fact]
    public void An_add_faces_the_way_its_mouth_faces()
    {
        var rig = new BossRig(layout: Layout());
        rig.Lend();
        int before = rig.Roster.Count;

        rig.Adds.Summon(rig.Controller, rig.Row, "demoman", 2, 9, rig.Now, legs: 1);

        Assert.Equal([45f, 135f], NewRows(rig, before).Select(r => rig.Roster.PostYaw(r)));
    }

    [Fact]
    public void A_mouth_of_a_later_leg_is_closed_until_its_phase()
    {
        var rig = new BossRig(layout: Layout());
        rig.Lend();
        int before = rig.Roster.Count;

        rig.Adds.Summon(rig.Controller, rig.Row, "demoman", 4, 9, rig.Now, legs: 1);

        Assert.DoesNotContain(NewRows(rig, before), r => rig.Roster.Post(r).DistanceTo(Leg2) < 1f || rig.Roster.Post(r).DistanceTo(Leg3) < 1f);
    }

    [Fact]
    public void A_phase_that_opens_a_leg_adds_its_mouths_to_the_open_ones()
    {
        var rig = new BossRig(layout: Layout());
        rig.Lend();
        int before = rig.Roster.Count;

        rig.Adds.Summon(rig.Controller, rig.Row, "demoman", 3, 9, rig.Now, legs: 2);

        Assert.Equal([Leg1A, Leg2, Leg1B], NewRows(rig, before).Select(r => rig.Roster.Post(r)));
    }

    [Fact]
    public void A_phase_s_summons_come_out_of_the_mouths_its_phase_opens()
    {
        // The only mouth is leg 3's: the third phase's (0-based 2) adds come out of it.
        var rig = new BossRig("major_crits", new HordeArenaLayout(ImmutableArray<TankTrackPoint>.Empty, [new AddMouth(Leg3, 315f, 3)]));
        var body = rig.Lend();
        int before = rig.Roster.Count;

        rig.Ask(body, HordeAbilities.BossSummon, 2);

        var added = Assert.Single(NewRows(rig, before));
        Assert.Equal(Leg3, rig.Roster.Post(added));
    }

    [Fact]
    public void A_layout_with_no_mouths_keeps_the_arena_s_markers()
    {
        var plain = new BossRig();
        var shaped = new BossRig(layout: new HordeArenaLayout(ImmutableArray<TankTrackPoint>.Empty, ImmutableArray<AddMouth>.Empty));
        plain.Lend();
        shaped.Lend();
        int a = plain.Roster.Count, b = shaped.Roster.Count;

        plain.Adds.Summon(plain.Controller, plain.Row, "demoman", 3, 9, plain.Now, legs: 1);
        shaped.Adds.Summon(shaped.Controller, shaped.Row, "demoman", 3, 9, shaped.Now, legs: 1);

        Assert.Equal(NewRows(plain, a).Select(r => plain.Roster.Post(r)), NewRows(shaped, b).Select(r => shaped.Roster.Post(r)));
    }
}

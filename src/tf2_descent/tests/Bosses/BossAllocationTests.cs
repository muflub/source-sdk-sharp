using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The horde's steady tick allocates nothing with a boss embodied (plan_descent_logic 3.10's
/// structure gate, held for HO13's tickers): the encounter's look at its arena, its bar and the
/// verbs' windup sweep, a thousand ticks with a hero in the arena and nothing changing.
/// </summary>
public sealed class BossAllocationTests
{
    [Fact]
    public void A_thousand_steady_boss_fight_ticks_allocate_nothing()
    {
        var rig = new BossRig();
        rig.Lend();
        rig.Hero(Centre(BossRig.ArenaRoom));
        rig.Tick(600);   // the intro, the first bar, the jit
        long barsBefore = rig.Encounter.BarsSent;

        // A steady allocation shows in every window; a one-off (a re-jit on a loaded box) in one: the quietest is the cost.
        long quietest = long.MaxValue;
        for (int window = 0; window < 3 && quietest != 0; window++)
        {
            long a = GC.GetAllocatedBytesForCurrentThread();
            rig.Tick(1000);
            quietest = Math.Min(quietest, GC.GetAllocatedBytesForCurrentThread() - a);
        }

        Assert.True(rig.Encounter.Engaged && rig.Encounter.HeroesInArena == 1, "the control: the fight ran with its hero in the arena");
        Assert.Equal(barsBefore, rig.Encounter.BarsSent);   // steady: nothing changed to send
        Assert.Equal(0, quietest);
    }
}

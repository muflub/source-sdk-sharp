using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde.Senses;
using TF2.Descent.Server.Horde.Session;
using TF2.Descent.Tests.Horde;

using Xunit;

using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The horde hears a hero go down and get up (WO6; B.3's <c>HeroDowned</c>): the downed state's
/// listener hands it to the session, which tells every brain with a body.
/// </summary>
public sealed class DownedHordeTests
{
    private static (ServerHordeRig Rig, HordeSession Session) Make()
    {
        HordeSession? session = null;
        var clock = new RigClock(null);
        var rig = new ServerHordeRig(null, brains: r =>
        {
            session = new HordeSession(new RecordingMotion(), new CountingActions(), r.SightWorld, r.Heroes, new RigRules(), clock,
                                       new NoLandings(), new HordeWorkers(), DeclaredConVars.Holding<HordeAiSyncConVar>("1"),
                                       DeclaredConVars.Holding<HordeAiThreadsConVar>("1"), DeclaredConVars.Fresh<HordeAiRecordConVar>(),
                                       new RigMod(Path.GetTempPath()), r.Log, new RigAttackWorld(), r.Nav(clock: clock), r.Motion);
            return session;
        });
        clock.Rig = rig;
        return (rig, session!);
    }

    private static (ServerHordeRig Rig, HordeSession Session) WithAHeroSeen()
    {
        var (rig, session) = Make();
        rig.Lent("soldier");
        rig.Heroes.Put(rig.Level, Centre(0) + At(0, 220));
        rig.Tick(30);
        Assert.True(session.Brains!.HeroSlots.ActorOf(1).IsValid);
        return (rig, session);
    }

    [Fact]
    public void A_hero_going_down_is_an_event_for_every_brain()
    {
        var (_, session) = WithAHeroSeen();
        long before = session.Brains!.Session.Counters.EventsIn;

        new DownedHordeTap(session).HeroDowned(1, down: true, new Vector(0, 0, 0));

        Assert.Equal(1, session.HeroDowns);
        Assert.Equal(before + 1, session.Brains.Session.Counters.EventsIn);
    }

    [Fact]
    public void A_hero_getting_up_is_told_as_up()
    {
        var (_, session) = WithAHeroSeen();

        new DownedHordeTap(session).HeroDowned(1, down: false, default);

        Assert.Equal((0L, 1L), (session.HeroDowns, session.HeroUps));
    }

    [Fact]
    public void A_seat_with_no_hero_slot_is_told_to_nobody()
    {
        var (_, session) = WithAHeroSeen();
        long before = session.Brains!.Session.Counters.EventsIn;

        session.HeroDowned(9, down: true, default);

        Assert.Equal((0L, before), (session.HeroDowns, session.Brains.Session.Counters.EventsIn));
    }
}

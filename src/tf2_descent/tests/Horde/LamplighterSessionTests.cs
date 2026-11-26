using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde.Session;

using Xunit;

using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The server's horde hands each hero's Lamplighter to the brains on his pose
/// (<c>IHeroVisibility.Beacon</c>, the user's ruling L26): the same robot, the hero at the same
/// spot in the dark with his lamp lit, is not noticed while the beacon is off and is once it is on.
/// </summary>
public sealed class LamplighterSessionTests
{
    private const float Distance = 240f;

    private sealed class Rig
    {
        public Rig()
        {
            HordeSession? session = null;
            Clock = new RigClock(null);
            Horde = new ServerHordeRig(null, brains: r =>
            {
                session = new HordeSession(new RecordingMotion(), new CountingActions(), r.SightWorld, r.Heroes, new RigRules(), Clock,
                                           new NoLandings(), new HordeWorkers(), DeclaredConVars.Holding<HordeAiSyncConVar>("1"),
                                           DeclaredConVars.Holding<HordeAiThreadsConVar>("1"), DeclaredConVars.Fresh<HordeAiRecordConVar>(),
                                           new RigMod(Path.GetTempPath()), r.Log, new RigAttackWorld(), r.Nav(clock: Clock), r.Motion,
                                           new RespawnGraces(), Visibility);
                return session;
            });
            Clock.Rig = Horde;
            Session = session!;
            var (_, body) = Horde.Lent("soldier");
            Body = body.Index;
            Horde.Heroes.Put(Horde.Level, Centre(0) + At(0, Distance));
            Horde.Tick();

            // The dark: his scaled range falls far short of him; his lit lamp's plain reach short of him too.
            float range = Session.Brains!.SenseOf(Body).Range;
            Visibility.Scale = 100.0 / range;
            Visibility.Reach = Distance / ((1 + TheRules.Content.Horde.Senses.Sight.BeaconReachScale) / 2);
        }

        public ServerHordeRig Horde { get; }
        public HordeSession Session { get; }
        public RigClock Clock { get; }
        public RigVisibility Visibility { get; } = new();
        public int Body { get; }
        public bool Sees => Session.Brains!.Vision.Holds(Body, 0);
    }

    [Fact]
    public void Off_the_robot_does_not_notice_the_lit_hero_in_the_dark()
    {
        var rig = new Rig();
        rig.Horde.Tick(120);

        Assert.True(rig.Visibility.Reach * TheRules.Content.Horde.Senses.Sight.LampReachScale < Distance);
        Assert.False(rig.Sees);
    }

    [Fact]
    public void On_the_same_robot_notices_him_at_the_same_spot()
    {
        var rig = new Rig();
        rig.Horde.Tick(120);
        bool before = rig.Sees;

        rig.Visibility.Beacons = true;
        rig.Horde.Tick(120);

        Assert.Equal((false, true), (before, rig.Sees));
    }
}

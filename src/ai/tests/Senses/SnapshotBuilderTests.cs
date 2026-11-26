using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;
using SourceSharp.Ai.Server.Senses;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>
/// The snapshot builder (plan_hl2mp_ai §4.3): a bot's view holds its own HUD
/// and exactly the actors it sees this tick, as a client draws them; an
/// opponent behind a wall is never in the <see cref="PerceivedView"/>.
/// </summary>
public sealed class SnapshotBuilderTests
{
    private sealed class Hud(int health) : IBotHud
    {
        public SelfState Read(EntityHandle bot) => new(default, default, default, health, 0, 3, 17, 90, SelfFlags.Alive);
    }

    private static readonly VisionParams Eyes = new(6000f, 90f, 0.3f, 0.5f);

    private static (PerceivedView View, ActorId Seen, ActorId Hidden) Build(bool selfReloading = false, float eye = 64f, bool ducked = false, bool teams = false,
                                                                            float seenYaw = 0f)
    {
        var w = new SenseWorld();
        SenseWorld.Player bot = w.Add(1, new AiVector(0, 0, 0));
        SenseWorld.Player seen = w.Add(2, new AiVector(500, 0, 0));
        seen.Weapon = "weapon_shotgun";
        seen.EyeHeight = eye;
        seen.Ducked = ducked;
        seen.Angles = new AiVector(10f, seenYaw, 0f);
        SenseWorld.Player hidden = w.Add(3, new AiVector(500, 600, 0));
        w.Occluder(new AiVector(250, 100, -10), new AiVector(266, 2000, 300), SenseWorld.Solid);
        var pass = new VisionPass(w.Engine(), w.Entities(), w.Traces());
        var id = new ActorId(1, 1);
        pass.Add(id);
        var sensed = new SensedBot(id, bot.Handle, Eyes);
        var events = new List<AiEvent>();
        for (int t = 1; t <= 60; t++)
            pass.Run(new Tick(t), 0.015f, [sensed], w.Living(sensed), events);

        var rendered = new FakeTeams();
        if (teams)
            rendered.Put(bot.Handle, 2).Put(seen.Handle, 3);
        var builder = new SnapshotBuilder(w.Engine(), WeaponCatalog.Stock(), new Hud(77), w.Stance(), rendered, TestGames.Hl2mp(), new ThingSight(w.Engine(), w.Entities(), w.Traces(), TestGames.Hl2mp()), new ShownActors(TestGames.Hl2mp()));
        var ring = new SnapshotRing(2, VisionPass.Slots, 8);
        AiSnapshot snap = ring.BeginWrite(new Tick(60));
        builder.Build(snap, [sensed], pass, slot => slot == 2 || (selfReloading && slot == 1) ? ActorActivity.Reloading : ActorActivity.None);
        return (snap.ViewFor(1)!, ActorDirectory.HumanActor(2, seen.Handle), ActorDirectory.HumanActor(3, hidden.Handle));
    }

    [Fact]
    public void TheViewHoldsTheSeenOpponentAsAClientDrawsIt()
    {
        (PerceivedView view, ActorId seen, _) = Build();
        Assert.True(view.TryActor(seen, out ActorFacts f));
        Assert.Equal(new AiVector(500, 0, 0), f.Origin);
        Assert.Equal(WeaponCatalog.Stock().IndexOf("weapon_shotgun"), f.WeaponSeen);
        Assert.Equal(ActorActivity.Reloading, f.Activity);
        Assert.Equal(1f, f.VisibleFraction);
    }

    /// <summary>Where a sighted player looks is in the view, as every client draws his head and aim (P7): facing the bot, yaw 180; facing away, 0.</summary>
    [Fact]
    public void TheViewHoldsWhereTheSeenOpponentLooks()
    {
        (PerceivedView toward, ActorId seen, _) = Build(seenYaw: 180f);
        (PerceivedView away, ActorId same, _) = Build(seenYaw: 0f);
        Assert.True(toward.TryActor(seen, out ActorFacts a));
        Assert.True(away.TryActor(same, out ActorFacts b));
        Assert.Equal((new AiAngles(10f, 180f, 0f), new AiAngles(10f, 0f, 0f)), (a.Facing, b.Facing));
    }

    /// <summary>A crouched body is one a client can see: the flag its animation turns on says so (§4.3).</summary>
    [Fact]
    public void AnOpponentCrouchedIsSeenCrouched()
    {
        (PerceivedView standing, ActorId seen, _) = Build();
        Assert.True(standing.TryActor(seen, out ActorFacts up));
        Assert.Equal(ActorActivity.None, up.Activity & ActorActivity.Crouched);

        (PerceivedView ducked, ActorId same, _) = Build(eye: 28f, ducked: true);
        Assert.True(ducked.TryActor(same, out ActorFacts down));
        Assert.Equal(ActorActivity.Crouched, down.Activity & ActorActivity.Crouched);
    }

    /// <summary>
    /// The eye starts down at a duck's first tick; other clients see nothing until the duck
    /// has finished. A bot must not know before they could (ruling 2026-11-03).
    /// </summary>
    [Fact]
    public void AnEyeOnItsWayDownIsNotYetSeenCrouched()
    {
        (PerceivedView going, ActorId seen, _) = Build(eye: 40f, ducked: false);
        Assert.True(going.TryActor(seen, out ActorFacts f));
        Assert.Equal(ActorActivity.None, f.Activity & ActorActivity.Crouched);
    }

    [Fact]
    public void AnOpponentBehindAWallIsNeverInTheView()
    {
        (PerceivedView view, _, ActorId hidden) = Build();
        Assert.False(view.TryActor(hidden, out _));
        Assert.Equal(1, view.SightedCount);
    }

    [Fact]
    public void TheBotsOwnStateIsItsHud()
    {
        (PerceivedView view, _, _) = Build();
        Assert.Equal(77, view.Self.Health);
        Assert.Equal(17, view.Self.Clip);
    }

    /// <summary>The bot's own reload, as its own view model shows it, is in its HUD's flags (live, the own-reload branch never fired without it).</summary>
    [Fact]
    public void TheBotsOwnReloadIsInItsHud()
    {
        Assert.True((Build(selfReloading: true).View.Self.Flags & SelfFlags.Reloading) != 0);
        Assert.True((Build().View.Self.Flags & SelfFlags.Reloading) == 0);
    }

    /// <summary>A team mode's view carries the bot's team and each sighted actor's as a client renders them (plan_bots_teams_tf2 B.2).</summary>
    [Fact]
    public void ATeamModesViewCarriesEachTeam()
    {
        (PerceivedView view, ActorId seen, _) = Build(teams: true);
        Assert.Equal(2, view.Self.Team);
        Assert.True(view.TryActor(seen, out ActorFacts f));
        Assert.Equal(3, f.Team);
    }

    /// <summary>The control: with no teams rendered every team reads 0, so nobody is a friend.</summary>
    [Fact]
    public void ADeathmatchViewCarriesNoTeam()
    {
        (PerceivedView view, ActorId seen, _) = Build();
        Assert.True(view.TryActor(seen, out ActorFacts f));
        Assert.Equal((0, 0), (view.Self.Team, f.Team));
    }
}

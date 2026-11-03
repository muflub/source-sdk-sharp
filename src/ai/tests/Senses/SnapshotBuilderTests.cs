using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Shared;
using Xunit;

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

    private static (PerceivedView View, ActorId Seen, ActorId Hidden) Build()
    {
        var w = new SenseWorld();
        SenseWorld.Player bot = w.Add(1, new AiVector(0, 0, 0));
        SenseWorld.Player seen = w.Add(2, new AiVector(500, 0, 0));
        seen.Weapon = "weapon_shotgun";
        SenseWorld.Player hidden = w.Add(3, new AiVector(500, 600, 0));
        w.Occluder(new AiVector(250, 100, -10), new AiVector(266, 2000, 300), SenseWorld.Solid);
        var pass = new VisionPass(w.Engine(), w.Entities(), w.Traces());
        var id = new ActorId(1, 1);
        pass.Add(id);
        var sensed = new SensedBot(id, bot.Handle, Eyes);
        var events = new List<AiEvent>();
        for (int t = 1; t <= 60; t++)
            pass.Run(new Tick(t), 0.015f, [sensed], w.Living(sensed), events);

        var builder = new SnapshotBuilder(w.Engine(), WeaponCatalog.Stock(), new Hud(77));
        var ring = new SnapshotRing(2, VisionPass.Slots, 8);
        AiSnapshot snap = ring.BeginWrite(new Tick(60));
        builder.Build(snap, [sensed], pass, slot => slot == 2 ? ActorActivity.Reloading : ActorActivity.None);
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
}

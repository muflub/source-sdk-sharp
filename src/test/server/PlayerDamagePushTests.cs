using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// CBasePlayer::OnTakeDamage_Alive's shove (player.cpp:1622-1638): a walking player is
/// pushed away from the inflictor, and it was not: no hit moved a player.
/// </summary>
public class PlayerDamagePushTests
{
    private static readonly EntityHandle Victim = new(0x0001_0003u);
    private static readonly EntityHandle Attacker = new(0x0002_0001u);
    private static readonly EntityHandle Grenade = new(0x0003_0001u);

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class PushRule(bool pushes) : IPlayerDamagePushRule
    {
        public bool PushesFromDamage(EntityHandle player) => pushes;
    }

    /// <summary>A walking player of 32 x 32 x <paramref name="height"/> whose centre is 36 u up, an attacker level with it 100 u along +x, the grenade below.</summary>
    private static (SsPlayer Player, RecordingEngine Engine, ServerPlayerMove Record, DamageInflictors Inflictors) Rig(
        float height = 72f, IHooks<IPlayerDamagePushRule>? rules = null, SolidFlags attackerSolid = 0)
    {
        var engine = Fake();
        engine.Returns("Entities.GetHealth", 100).Returns("Entities.GetIndex", 3)
              .Returns("Entities.IsValid", true).Returns("Entities.GetOrigin", Vector.Zero)
              .Returns("Entities.GetBounds", (new Vector(-16f, -16f, 0f), new Vector(16f, 16f, height)))
              .ReturnsEachCall("Entities.GetSolidFlags", () => attackerSolid)
              .ReturnsEachCall("Entities.GetWorldSpaceCenter", () =>
              {
                  var of = (EntityHandle)engine.Last("Entities.GetWorldSpaceCenter")!.Value.Args[0]!;
                  return of == Victim ? new Vector(0f, 0f, 36f)
                       : of == Grenade ? new Vector(0f, 0f, -100f)
                       : new Vector(100f, 0f, 46f);
              })
              .Ignores("Entities.ApplyVelocityImpulse");
        var moves = new ServerPlayerMoves();
        var inflictors = new DamageInflictors();
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), InfiniteAux(), PrintDamage(), messages: engine, moves: moves,
                                  inflictors: inflictors, pushRules: rules) { Handle = Victim };
        player.BindAreas(Areas(engine));
        player.State.BindLocal();
        return (player, engine, moves.For(Victim), inflictors);
    }

    private static Vector Pushed(RecordingEngine engine)
    {
        var call = Assert.Single(engine.AllOf("Entities.ApplyVelocityImpulse"));
        Assert.Equal(Victim, call.Args[0]);
        Assert.Equal(false, call.Args[2]);
        return (Vector)call.Args[1]!;
    }

    /// <summary>DamageForce on a standing hull: five u/s a point, away from what did it.</summary>
    [Fact]
    public void AHitPushesThePlayerAwayFiveUnitsASecondAPoint()
    {
        var (player, engine, _, _) = Rig();

        player.OnDamaged(Attacker, 20f, DamageType.Bullet);

        Assert.Equal(new Vector(-100f, 0f, 0f), Pushed(engine));
    }

    /// <summary>The ratio is the standing hull's over this one's: a crouched player goes twice as far.</summary>
    [Fact]
    public void ACrouchedPlayerGoesTwiceAsFar()
    {
        var (player, engine, _, _) = Rig(height: 36f);

        player.OnDamaged(Attacker, 20f, DamageType.Bullet);

        Assert.Equal(new Vector(-200f, 0f, 0f), Pushed(engine));
    }

    [Fact]
    public void TheForceStopsAtAThousand()
    {
        var (player, engine, _, _) = Rig();

        player.OnDamaged(Attacker, 500f, DamageType.Bullet);

        Assert.Equal(new Vector(-1000f, 0f, 0f), Pushed(engine));
    }

    /// <summary>The inflictor's scope, a blast's, over the attacker; straight up from below stops at 250.</summary>
    [Fact]
    public void AGrenadeUnderThePlayerLiftsItFromTheGrenadeNoMoreThan250()
    {
        var (player, engine, _, inflictors) = Rig();

        using (inflictors.Push(Grenade, new Vector(0f, 0f, -100f)))
            player.OnDamaged(Attacker, 150f, DamageType.Blast);

        Assert.Equal(new Vector(0f, 0f, 250f), Pushed(engine));
    }

    [Fact]
    public void ATriggerAttackerPushesNobody()
    {
        var (player, engine, _, _) = Rig(attackerSolid: SolidFlags.Trigger);

        player.OnDamaged(Attacker, 20f, DamageType.Bullet);

        Assert.False(engine.Called("Entities.ApplyVelocityImpulse"), engine.Describe());
    }

    [Fact]
    public void APlayerThatIsNotWalkingIsNotPushed()
    {
        var (player, engine, record, _) = Rig();
        record.Move.MoveType = (int)MoveType.NoClip;

        player.OnDamaged(Attacker, 20f, DamageType.Bullet);

        Assert.False(engine.Called("Entities.ApplyVelocityImpulse"), engine.Describe());
    }

    /// <summary>A mod's rule refuses it (TF2's player has its own), and a rule allowing it changes nothing.</summary>
    [Fact]
    public void AModsRuleCanRefuseThePush()
    {
        var (refused, refusedEngine, _, _) = Rig(rules: new ListHooks<IPlayerDamagePushRule>(new PushRule(true), new PushRule(false)));
        refused.OnDamaged(Attacker, 20f, DamageType.Bullet);
        Assert.False(refusedEngine.Called("Entities.ApplyVelocityImpulse"), refusedEngine.Describe());

        var (allowed, allowedEngine, _, _) = Rig(rules: new ListHooks<IPlayerDamagePushRule>(new PushRule(true)));
        allowed.OnDamaged(Attacker, 20f, DamageType.Bullet);
        Assert.True(allowedEngine.Called("Entities.ApplyVelocityImpulse"));
    }

    /// <summary>m_takedamage: what a hurting brush and a door's dmg ask before they hurt.</summary>
    [Fact]
    public void ALivingPlayerTakesDamage()
        => Assert.True(Rig().Player.TakesDamage);

    [Fact]
    public void AHitGodModeRefusesPushesNobody()
    {
        var (player, engine, _, _) = Rig();
        player.GodMode = true;

        player.OnDamaged(Attacker, 20f, DamageType.Bullet);

        Assert.False(engine.Called("Entities.ApplyVelocityImpulse"), engine.Describe());
    }
}

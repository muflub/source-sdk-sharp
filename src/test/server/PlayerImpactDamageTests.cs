using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Physics stock parity C10 (8e): a player hurt by a moving prop. Stock's
/// <c>CBaseCombatCharacter::VPhysicsShadowCollision</c> takes
/// <c>CalculatePhysicsImpactDamage</c> with the player table
/// (<c>gDefaultPlayerImpactDamageTable</c>), <c>m_impactEnergyScale</c> 1 and
/// no static damage, from a moving, moveable VPHYSICS entity that is not what
/// the player stands on, and delivers it with that entity as the attacker.
/// </summary>
public class PlayerImpactDamageTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private const uint Crate = 41;

    /// <summary>The ground word's "standing on nothing" (ground_handle_wire.h).</summary>
    private const int GroundNone = (1 << 21) - 1;

    // A 30 kg crate at 600 in/s into a still player (shadow mass 85), losing
    // 500 in/s: 500^2 x 30 = 7.5e6, over the player's 85 = 88,235 -> the
    // player table's 250^2 step, 10.
    private static PhysicsEvent CrateIntoPlayer() => new()
    {
        KindRaw = (uint)PhysicsEventKind.PostCollision,
        Entity0 = Crate, Entity1 = Player.Value,
        Mass0 = 30f, Mass1 = 85f,
        MoveType0 = (int)MoveType.VPhysics,
        MoveType1 = 0,
        Flags = PhysicsEvent.FlagMoveable0 | PhysicsEvent.FlagMoveable1 | PhysicsEvent.FlagShadow1
              | PhysicsEvent.FlagCollision | PhysicsEvent.FlagShadowCollision,
        PreVelocity0X = 600f,
        PostVelocity0X = 100f,
    };

    private static (SsPlayer Player, RecordingEngine Engine, PhysicsDamageQueue Impacts) World(int groundIndex = -1)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsPlayer).Assembly);
        var services = Services(out var engine);
        registry.Services = services;
        engine.Returns("Entities.GetIndex", (int)Crate);
        engine.Returns("Entities.IsPlayer", false);
        engine.Returns("Entities.GetSolidFlags", (SolidFlags)0);

        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");
        var player = (SsPlayer)registry.Get(Player)!;

        player.State.BindLocal();
        player.State.GroundEntity = groundIndex < 0 ? GroundNone : groundIndex;
        return (player, engine, services.Require<PhysicsDamageQueue>());
    }

    [Fact]
    public void A_crate_thrown_into_the_player_hurts_them_by_the_player_table()
    {
        var (player, engine, impacts) = World();
        var ev = CrateIntoPlayer();

        player.OnVPhysicsShadowCollision(in ev, 1);
        impacts.Deliver();

        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.Equal(Player, hit.Args[0]);
        Assert.Equal(new EntityHandle(Crate), hit.Args[1]);
        Assert.Equal(10f, hit.Args[2]);
        Assert.Equal(DamageType.Crush, hit.Args[3]);
    }

    [Fact]
    public void The_player_on_side_zero_reads_its_own_side()
    {
        var (player, engine, impacts) = World();
        var ev = CrateIntoPlayer();

        // The same impact with the sides swapped.
        (ev.Entity0, ev.Entity1) = (ev.Entity1, ev.Entity0);
        (ev.Mass0, ev.Mass1) = (ev.Mass1, ev.Mass0);
        (ev.MoveType0, ev.MoveType1) = (ev.MoveType1, ev.MoveType0);
        (ev.PreVelocity0X, ev.PreVelocity1X) = (ev.PreVelocity1X, ev.PreVelocity0X);
        (ev.PostVelocity0X, ev.PostVelocity1X) = (ev.PostVelocity1X, ev.PostVelocity0X);
        ev.Flags = PhysicsEvent.FlagMoveable0 | PhysicsEvent.FlagMoveable1 | PhysicsEvent.FlagShadow0
                 | PhysicsEvent.FlagCollision | PhysicsEvent.FlagShadowCollision;

        player.OnVPhysicsShadowCollision(in ev, 0);
        impacts.Deliver();

        Assert.Equal(10f, Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage").Args[2]);
    }

    [Fact]
    public void The_world_does_not_hurt_the_player()
    {
        var (player, engine, impacts) = World();
        var ev = CrateIntoPlayer();
        ev.Entity0 = PhysicsEvent.NoEntity;

        player.OnVPhysicsShadowCollision(in ev, 1);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    [Fact]
    public void Only_a_vphysics_mover_hurts_the_player()
    {
        var (player, engine, impacts) = World();
        var ev = CrateIntoPlayer();
        ev.MoveType0 = (int)MoveType.Push;

        player.OnVPhysicsShadowCollision(in ev, 1);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    [Fact]
    public void A_motion_disabled_prop_does_not_hurt_the_player()
    {
        var (player, engine, impacts) = World();
        var ev = CrateIntoPlayer();
        ev.Flags &= ~PhysicsEvent.FlagMoveable0;

        player.OnVPhysicsShadowCollision(in ev, 1);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    [Fact]
    public void What_the_player_stands_on_does_not_hurt_them()
    {
        var (player, engine, impacts) = World(groundIndex: (int)Crate);
        var ev = CrateIntoPlayer();

        player.OnVPhysicsShadowCollision(in ev, 1);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    [Fact]
    public void A_slow_crate_does_not_hurt_the_player()
    {
        // 100 in/s, losing 60: 3,600 x 30 / 85 = 1,270, under the first step.
        var (player, engine, impacts) = World();
        var ev = CrateIntoPlayer();
        ev.PreVelocity0X = 100f;
        ev.PostVelocity0X = 40f;

        player.OnVPhysicsShadowCollision(in ev, 1);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }
}

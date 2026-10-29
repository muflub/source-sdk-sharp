using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Physics stock parity B5: a player crushed between the world and a heavy
/// pusher. Stock's <c>CBasePlayer::VPhysicsUpdate</c> scales
/// <c>m_impactEnergyScale</c> by 1/8 and calls
/// <c>ApplyStressDamage( pPhysics, true )</c>: received stress x scale / mass
/// over <c>phys_stressbodyweights</c> (5), with a non-static pusher, a large
/// object (bRequireLargeObject) and the hull not penetrating, is 200
/// DMG_CRUSH from the world (basecombatcharacter.cpp:3042-3091).
/// </summary>
public class PlayerStressDamageTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private const uint Crushing = PhysicsEvent.StressNonStatic | PhysicsEvent.StressLargeObject;

    // The shadow's 85 kg: 5 body weights at 1/8 scale is 3400 received.
    private static PhysicsEvent Stress(float received, uint flags = Crushing, float mass = 85f) => new()
    {
        KindRaw = (uint)PhysicsEventKind.Stress,
        Entity0 = Player.Value,
        Entity1 = PhysicsEvent.NoEntity,
        Speed = received,
        Energy = mass,
        Flags = flags,
    };

    private static (SsPlayer Player, RecordingEngine Engine, PhysicsDamageQueue Impacts) World()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsPlayer).Assembly);
        var services = Services(out var engine);
        registry.Services = services;
        engine.Returns("Entities.IsPlayer", true);
        engine.Returns("Players.IsAlive", true);
        engine.Returns("Entities.GetSolidFlags", (SolidFlags)0);

        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");
        var player = (SsPlayer)registry.Get(Player)!;
        player.State.BindLocal();
        return (player, engine, services.Require<PhysicsDamageQueue>());
    }

    [Fact]
    public void A_large_pusher_squeezing_the_player_crushes_them_from_the_world()
    {
        var (player, engine, impacts) = World();
        var ev = Stress(3500f);

        player.OnVPhysicsStress(in ev);
        impacts.Deliver();

        var hit = Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage");
        Assert.Equal(Player, hit.Args[0]);
        Assert.Equal(EntityHandle.None, hit.Args[1]);
        Assert.Equal(200f, hit.Args[2]);
        Assert.Equal(DamageType.Crush, hit.Args[3]);
        Assert.Equal(1, impacts.StressCrushes);
    }

    [Fact]
    public void Five_body_weights_exactly_is_not_a_crush()
    {
        var (player, engine, impacts) = World();
        var ev = Stress(3400f);

        player.OnVPhysicsStress(in ev);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    [Fact]
    public void The_player_feels_an_eighth_of_the_stress()
    {
        // 500 / 85 is 5.9 body weights unscaled, 0.7 at stock's 1/8.
        var (player, engine, impacts) = World();
        var ev = Stress(500f);

        player.OnVPhysicsStress(in ev);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    [Fact]
    public void The_stress_is_per_kilogram_of_the_hull()
    {
        // 1750 x 1/8 over 42.5 kg is 5.15 body weights.
        var (player, engine, impacts) = World();
        var ev = Stress(1750f, mass: 42.5f);

        player.OnVPhysicsStress(in ev);
        impacts.Deliver();

        Assert.Single(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    [Fact]
    public void Static_stress_is_not_a_crush()
    {
        var (player, engine, impacts) = World();
        var ev = Stress(9000f, PhysicsEvent.StressLargeObject);

        player.OnVPhysicsStress(in ev);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    [Fact]
    public void The_player_is_crushed_only_by_a_large_object()
    {
        var (player, engine, impacts) = World();
        var ev = Stress(9000f, PhysicsEvent.StressNonStatic);

        player.OnVPhysicsStress(in ev);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }

    [Fact]
    public void A_penetrating_hull_is_stuck_not_crushed()
    {
        var (player, engine, impacts) = World();
        var ev = Stress(9000f, Crushing | PhysicsEvent.StressPenetrating);

        player.OnVPhysicsStress(in ev);
        impacts.Deliver();

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Damage.TakeDamage");
    }
}

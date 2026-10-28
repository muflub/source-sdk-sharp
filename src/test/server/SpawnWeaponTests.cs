using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// CHL2MP_Player::GiveDefaultItems' tail: the player spawns holding what their
/// cl_defaultweapon names when they own it, and the gravity gun otherwise.
/// Found against stock (2026-10-25): this mod left the player holding whatever
/// the grant handed them last.
/// </summary>
public class SpawnWeaponTests
{
    private static EntityHandle Player => new(0x0001_0001);

    private static Arsenal Kitted()
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", 1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", Vector.Zero)
            .Returns("Entities.GetEyeAngles", Vector.Zero)
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", 10f);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out var entityEngine);
        entityEngine.Strict = false;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);
        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups());
        foreach (string w in GameRules.Loadout)
            Assert.True(arsenal.GiveWeapon(Player, w), $"the fixture's player has {w}");
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;
        return arsenal;
    }

    private static string Spawned(string? clDefaultWeapon)
    {
        var arsenal = Kitted();
        // Not the gravity gun in the hands to begin with, whatever the grant left there.
        arsenal.Switch(Player, EntityNames.WeaponPistol);
        Assert.Equal(EntityNames.WeaponPistol, arsenal.Holdings(Player).Single(h => h.Active).ClassName);
        var entities = Fake().Returns("Entities.GetIndex", 1);
        int asked = 0;
        new DefaultSpawnWeapon(arsenal, entities, index => { asked = index; return clDefaultWeapon; }).Choose(Player);
        Assert.Equal(1, asked);
        return arsenal.Holdings(Player).Single(h => h.Active).ClassName;
    }

    [Fact]
    public void TheWeaponTheClientNamesIsHeld()
        => Assert.Equal(EntityNames.WeaponSmg1, Spawned(EntityNames.WeaponSmg1));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(EntityNames.WeaponRpg)]
    public void ElseTheGravityGun(string? clDefaultWeapon)
        => Assert.Equal(EntityNames.WeaponPhyscannon, Spawned(clDefaultWeapon));
}

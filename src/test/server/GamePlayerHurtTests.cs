using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// game_player_hurt as stock's CGamePlayerHurt::Use (maprules.cpp, commented
/// out in the SDK; tools/stock-ab compiles it): a player activator takes
/// <c>dmg</c> as DMG_GENERIC from the entity itself, or that much health when
/// it is negative, and a fire-once entity (spawnflag 1) removes itself.
/// </summary>
public class GamePlayerHurtTests
{
    private static readonly EntityHandle Hurt = new(0x0004_0501u);
    private static readonly EntityHandle Player = new(0x0004_0502u);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Spawned(int spawnflags, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(GamePlayerHurt).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("Entities.IsValid", true)
              .Returns("Entities.IsPlayer", true)
              .Returns("Players.GetTeam", 2)
              .Returns("Entities.GetHealth", 50)
              .Returns("Entities.GetSpawnFlags", spawnflags);

        foreach (var (key, value) in keys)
            registry.KeyValue(Hurt, EntityNames.GamePlayerHurt, key, value);
        registry.Spawn(Hurt, EntityNames.GamePlayerHurt);
        registry.Input(Hurt, "Use", string.Empty, 0f, Player);
        return (registry, engine);
    }

    [Fact]
    public void ThePlayerIsHurtGenericallyByTheEntity()
    {
        var (_, engine) = Spawned(0, ("dmg", "5"));

        var call = engine.Last("Damage.Apply");
        Assert.True(call is not null, "nobody was hurt");
        Assert.Equal(Player, (EntityHandle)call!.Value.Args[0]!);
        Assert.Equal(Hurt, (EntityHandle)call.Value.Args[1]!);
        Assert.Equal(5f, (float)call.Value.Args[2]!);
        Assert.Equal(DamageType.Generic, (DamageType)call.Value.Args[3]!);
    }

    [Fact]
    public void ItsDamageIsNothingUntilTheMapSaysSo()
    {
        var (_, engine) = Spawned(0);

        Assert.Equal(0f, (float)engine.Last("Damage.Apply")!.Value.Args[2]!);
    }

    [Fact]
    public void FireOnceRemovesItAfterUse()
    {
        var (registry, _) = Spawned(1, ("dmg", "5"));

        Assert.Equal(1, registry.Directory.PendingRemovalCount);
    }

    [Fact]
    public void WithoutFireOnceItStays()
    {
        var (registry, _) = Spawned(0, ("dmg", "5"));

        Assert.Equal(0, registry.Directory.PendingRemovalCount);
    }
}

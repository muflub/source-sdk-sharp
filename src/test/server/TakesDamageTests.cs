using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// m_takedamage's family defaults (ScriptedEntity.TakesDamage): a prop takes
/// damage, a brush only when it has health, glass and a physbox always, and a
/// point entity never -- what a crossbow bolt asks to tell a body from a wall.
/// </summary>
public class TakesDamageTests
{
    private static readonly EntityHandle Ent = new(0x0004_0c01u);

    private static ScriptedEntity Spawned(string className, int health)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ScriptedEntity).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("Entities.GetHealth", health);
        registry.Spawn(Ent, className);
        return registry.Get(Ent)!;
    }

    [Fact]
    public void APropTakesDamage() => Assert.True(Spawned(EntityNames.PropPhysics, 0).TakesDamage);

    [Fact]
    public void ADoorWithNoHealthTakesNone() => Assert.False(Spawned(EntityNames.FuncDoor, 0).TakesDamage);

    [Fact]
    public void ADoorGivenHealthTakesDamage() => Assert.True(Spawned(EntityNames.FuncDoor, 10).TakesDamage);

    [Fact]
    public void APhysboxAlwaysTakesDamage() => Assert.True(Spawned(EntityNames.FuncPhysbox, 0).TakesDamage);

    [Fact]
    public void APointEntityTakesNone() => Assert.False(Spawned(EntityNames.InfoTarget, 0).TakesDamage);
}

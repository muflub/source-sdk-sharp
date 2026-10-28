using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The carried weapon and the view model are not solid, as stock's
/// CBaseEntity::FollowEntity leaves what it attaches (CBaseCombatWeapon::Equip,
/// CBasePlayer::CreateViewModel). Found beside stock (2026-10-27): they took
/// the flag from the Effect kind's default, which stock's point entities do
/// not have.
/// </summary>
public class CarriedWeaponSolidityTests
{
    private static readonly EntityHandle Entity = new(0x0004_0341u);

    [Theory]
    [InlineData(EntityNames.SsWeapon)]
    [InlineData(EntityNames.PredictedViewModel)]
    public void It_spawns_not_solid(string className)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;

        registry.Spawn(Entity, className);

        Assert.Contains(engine.Calls, c => c.Member == "EntityPhysics.SetNotSolid"
                                           && (EntityHandle)c.Args[0]! == Entity && (bool)c.Args[1]!);
    }
}

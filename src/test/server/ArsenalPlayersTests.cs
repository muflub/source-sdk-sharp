using SourceSharp.Abi;

using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The decorated players' active-weapon entity (<see cref="ArsenalPlayers"/>):
/// the answer is the carried <c>ss_weapon</c> the Arsenal created — the
/// entity every weapon-state read resolves through, the damage layer's jarate
/// (<c>TFDamageLayer</c>'s <c>SsWeapon { State: { } }</c>), the piercing
/// round's owner and the flame's ignition among them. The engine binding
/// itself answers None (<c>EngineBinding</c>, plan_server_native_v2 T2's
/// "the active weapon is the Arsenal's to name"), which is the before.
/// </summary>
public class ArsenalPlayersTests
{
    [Fact]
    public void TheDecoratedAnswerIsTheCarriedWeaponOnceOneIsGiven()
    {
        var (arsenal, engine, _) = WeaponSwitchTests.World(out var registry);
        engine.Returns("Players.GetActiveWeaponEntity", EntityHandle.None);
        var players = new ArsenalPlayers(engine, arsenal);

        // No weapon yet: none, the binding's own answer.
        Assert.Equal(EntityHandle.None, players.GetActiveWeaponEntity(WeaponSwitchTests.Player));

        Assert.True(arsenal.GiveWeapon(WeaponSwitchTests.Player, "weapon_crowbar"),
                    "the fixture's weapon is given");

        var carried = players.GetActiveWeaponEntity(WeaponSwitchTests.Player);
        Assert.NotEqual(EntityHandle.None, carried);

        // The damage layer's predicate, word for word: the carried entity is
        // an ss_weapon, and its state is bound — the blob the machine's shot
        // mutated, whose charge the hit-time read counts.
        Assert.True(registry.Get(carried) is SsWeapon { State: { } },
                    "the carried entity is an ss_weapon whose state is bound");
    }
}

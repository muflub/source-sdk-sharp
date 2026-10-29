using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The C# collision rules are stock's: the matrix the predicate builds is the
/// one stock's <c>CGameRules::ShouldCollide</c> answers over all four hundred
/// pairs, which native once held as its default table and which
/// <c>PhysicsSolverRulesTests</c> holds for the solver's copy.
/// </summary>
public class CollisionRulesTests
{
    /// <summary>
    /// Stock's answers, one row per group: bit b of row a is "a collides with b".
    /// The table shell_gamerules.cpp held until T6 part 3 retired it with the
    /// slot that overwrote it (nothing native asked it).
    /// </summary>
    private static readonly uint[] StockRows =
    [
        0x000FABFFu, // COLLISION_GROUP_NONE
        0x00020001u, // COLLISION_GROUP_DEBRIS
        0x00000001u, // COLLISION_GROUP_DEBRIS_TRIGGER
        0x000EAAC1u, // COLLISION_GROUP_INTERACTIVE_DEBRIS
        0x000EABE1u, // COLLISION_GROUP_INTERACTIVE
        0x000C23F1u, // COLLISION_GROUP_PLAYER
        0x000EABB9u, // COLLISION_GROUP_BREAKABLE_GLASS
        0x000EB3F9u, // COLLISION_GROUP_VEHICLE
        0x000CABF1u, // COLLISION_GROUP_PLAYER_MOVEMENT
        0x000EE3F9u, // COLLISION_GROUP_NPC
        0x00000000u, // COLLISION_GROUP_IN_VEHICLE
        0x000E8959u, // COLLISION_GROUP_WEAPON
        0x00000080u, // COLLISION_GROUP_VEHICLE_CLIP
        0x000E83F9u, // COLLISION_GROUP_PROJECTILE
        0x000E8200u, // COLLISION_GROUP_DOOR_BLOCKER
        0x000EEBD9u, // COLLISION_GROUP_PASSABLE_DOOR
        0x00000001u, // COLLISION_GROUP_DISSOLVING
        0x000EEADBu, // COLLISION_GROUP_PUSHAWAY
        0x000EEBF9u, // COLLISION_GROUP_NPC_ACTOR
        0x000EEBF9u, // COLLISION_GROUP_NPC_SCRIPTED
    ];

    [Fact]
    public void TheBuiltMatrixIsStocksTable()
    {
        var native = StockRows;
        var built = CollisionRules.BuildMatrix();

        Assert.True(native.Length == built.Length,
            "stock holds " + native.Length + " row(s) and C# builds " + built.Length
            + ". One of the two has a different idea of how many collision groups "
            + "there are, and the matrix is indexed by that number.");

        for (int i = 0; i < native.Length; i++)
        {
            Assert.True(native[i] == built[i],
                "row " + i + " (" + (EngineCollisionGroup)i + "): stock has 0x"
                + native[i].ToString("X8") + ", C# builds 0x" + built[i].ToString("X8")
                + ", so the C# rules have diverged from stock's.");
        }
    }

    /// <summary>The row count is the group count: LAST_SHARED_COLLISION_GROUP.</summary>
    [Fact]
    public void TheGroupCountIsTheEnums()
    {
        Assert.Equal((int)EngineCollisionGroup.Count, CollisionRules.GroupCount);
        Assert.Equal(CollisionRules.GroupCount, StockRows.Length);
        Assert.Equal(CollisionRules.GroupCount, CollisionRules.BuildMatrix().Length);
    }

    /// <summary>
    /// The relation is symmetric because the predicate sorts first. The native
    /// lookup does NOT sort, so this is what makes argument order irrelevant
    /// there.
    /// </summary>
    [Fact]
    public void TheRelationIsSymmetric()
    {
        for (int a = 0; a < CollisionRules.GroupCount; a++)
        {
            for (int b = a + 1; b < CollisionRules.GroupCount; b++)
            {
                Assert.True(
                    CollisionRules.ShouldCollide((EngineCollisionGroup)a, (EngineCollisionGroup)b)
                    == CollisionRules.ShouldCollide((EngineCollisionGroup)b, (EngineCollisionGroup)a),
                    (EngineCollisionGroup)a + " and " + (EngineCollisionGroup)b
                    + " answer differently depending on argument order. The native "
                    + "lookup does not sort its arguments, so it would give two "
                    + "answers for one pair.");
            }
        }
    }

    // ------------------------------------------------------------------
    // A few rules named in English, so a reader can check the table means
    // what the comments claim without decoding hex.
    // ------------------------------------------------------------------

    [Fact]
    public void APlayerPassesThroughPushawayPropsButDebrisDoesNot()
    {
        Assert.False(CollisionRules.ShouldCollide(EngineCollisionGroup.Player,
                                                  EngineCollisionGroup.PushAway));
        Assert.False(CollisionRules.ShouldCollide(EngineCollisionGroup.PlayerMovement,
                                                  EngineCollisionGroup.PushAway));

        // The exception, and it has to be tested BEFORE the blanket debris rule
        // or it would never be reached -- which is why order is load-bearing.
        Assert.True(CollisionRules.ShouldCollide(EngineCollisionGroup.Debris,
                                                 EngineCollisionGroup.PushAway));
    }

    [Fact]
    public void AnythingInAVehicleCollidesWithNothing()
    {
        for (int b = 0; b < CollisionRules.GroupCount; b++)
        {
            Assert.False(CollisionRules.ShouldCollide(EngineCollisionGroup.InVehicle,
                                                      (EngineCollisionGroup)b),
                "InVehicle collides with " + (EngineCollisionGroup)b);
        }
    }

    [Fact]
    public void AVehicleClipStopsVehiclesAndNothingElse()
    {
        Assert.True(CollisionRules.ShouldCollide(EngineCollisionGroup.VehicleClip,
                                                 EngineCollisionGroup.Vehicle));

        foreach (var other in new[] { EngineCollisionGroup.Player, EngineCollisionGroup.Npc,
                                      EngineCollisionGroup.None, EngineCollisionGroup.Projectile })
        {
            Assert.False(CollisionRules.ShouldCollide(EngineCollisionGroup.VehicleClip, other),
                "a vehicle clip stops " + other + ", so a player would walk into an "
                + "invisible wall meant only for cars.");
        }
    }

    [Fact]
    public void ADoorBlockerStopsEverythingExceptNpcs()
    {
        Assert.True(CollisionRules.ShouldCollide(EngineCollisionGroup.DoorBlocker,
                                                 EngineCollisionGroup.Npc));
        Assert.False(CollisionRules.ShouldCollide(EngineCollisionGroup.DoorBlocker,
                                                  EngineCollisionGroup.Player));
    }
}

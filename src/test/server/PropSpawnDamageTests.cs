using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// CBreakableProp::Spawn's damage rule (props.cpp). A prop with no pieces of
/// its own breaks into its prop data's generic chunks (breakable_model,
/// breakable_count). One with health but neither pieces nor an interaction
/// that breaks or burns it is DAMAGE_EVENTS_ONLY at no health. Found
/// 2026-10-28: here every prop with health broke, a stone or pottery model
/// with no gibs vanishing where stock's stands.
/// </summary>
public class PropSpawnDamageTests
{
    private static readonly EntityHandle Prop = new(0x0006_0101u);

    private static PropPhysics Spawned(string text, int pieces = 0)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelKeyValueText", text)
              .Returns("Precache.BreakPieceCount", pieces)
              .Returns("Entities.GetHealth", 50);

        registry.KeyValue(Prop, EntityNames.PropPhysics, "model", "models/props_junk/rock001a.mdl");
        registry.Spawn(Prop, EntityNames.PropPhysics);
        var prop = (PropPhysics)registry.Get(Prop)!;
        prop.OnPhysicsCreated();
        return prop;
    }

    private static int Hit(PropPhysics prop) => prop.OnDamaged(EntityHandle.None, 10f, DamageType.Club);

    [Fact]
    public void Health_with_no_pieces_and_no_interactions_takes_no_damage()
        => Assert.Equal(0, Hit(Spawned("mdlkeyvalue { prop_data { health 50 } }")));

    [Fact]
    public void The_same_model_with_pieces_takes_it()
        => Assert.Equal(10, Hit(Spawned("mdlkeyvalue { prop_data { health 50 } }", pieces: 3)));

    /// <summary>An interaction that burns it is enough without pieces.</summary>
    [Fact]
    public void A_flammable_one_with_no_pieces_takes_it()
        => Assert.Equal(10, Hit(Spawned("mdlkeyvalue { prop_data { health 50 } fire_interactions { flammable yes } }")));

    /// <summary>Wooden.Medium's four WoodChunks stand in for pieces the model does not have.</summary>
    [Fact]
    public void Generic_chunks_count_as_its_pieces()
    {
        var prop = Spawned("mdlkeyvalue { prop_data { health 50 breakable_model WoodChunks breakable_count 4 } }");

        Assert.Equal(4, prop.BreakPieceCount);
        Assert.Equal(10, Hit(prop));
    }

    /// <summary>Wooden.Tiny names its chunks but a count of none.</summary>
    [Fact]
    public void A_chunk_group_with_a_count_of_none_is_no_pieces()
    {
        var prop = Spawned("mdlkeyvalue { prop_data { health 50 breakable_model WoodChunks breakable_count 0 } }");

        Assert.Equal(0, prop.BreakPieceCount);
        Assert.Equal(0, Hit(prop));
    }
}

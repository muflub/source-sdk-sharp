using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// GetBreakableDamage (props.cpp) and the prop_data damage modifiers it reads
/// (CPropData::ParsePropFromKV's dmg.bullets, dmg.club, dmg.explosive). Found
/// 2026-10-28: no prop applied its modifiers, so a wooden crate (Wooden.Base:
/// bullets 0.75, club 2.0, explosive 1.5) took a crowbar's blow at face value.
/// </summary>
public class BreakableDamageTests
{
    private static readonly PropDamageModifiers Wood = new(0.75f, 2f, 1.5f);

    [Theory]
    [InlineData(DamageType.Bullet, 7.5f)]
    [InlineData(DamageType.Bullet | DamageType.Buckshot, 15f)]
    [InlineData(DamageType.Club, 20f)]
    [InlineData(DamageType.Blast, 15f)]
    [InlineData(DamageType.Crush, 10f)]
    [InlineData(DamageType.Slash | DamageType.Crush, 100f)]
    [InlineData(DamageType.Generic, 10f)]
    public void Ten_damage_by_type_to_wood(DamageType type, float taken)
        => Assert.Equal(taken, BreakableDamage.Scale(10f, type, Wood), 3);

    private const string File = @"
""PropData""
{
    ""Wooden.Base""  { ""dmg.bullets"" ""0.75"" ""dmg.club"" ""2.0"" ""dmg.explosive"" ""1.5"" }
    ""Wooden.Small"" { ""base"" ""Wooden.Base"" ""health"" ""30"" }
}
";

    private static PropDamageModifiers Read(string? text)
    {
        var engine = Fake();
        engine.Returns("Files.ReadGameFile", File);
        var props = new PropData(new RecordingLog(), engine, engine, engine);
        props.Load();
        return props.ModelDamageModifiers(text);
    }

    [Fact]
    public void A_models_modifiers_come_through_its_base_and_it_may_set_its_own()
    {
        Assert.Equal(Wood, Read("mdlkeyvalue { prop_data { base Wooden.Small } }"));
        Assert.Equal(new PropDamageModifiers(0.75f, 3f, 1.5f), Read("mdlkeyvalue { prop_data { base Wooden.Small dmg.club 3 } }"));
    }

    [Fact]
    public void A_model_with_no_prop_data_takes_damage_as_it_comes()
    {
        Assert.Equal(new PropDamageModifiers(1f, 1f, 1f), Read(null));
        Assert.Equal(new PropDamageModifiers(1f, 1f, 1f), Read("mdlkeyvalue { fire_interactions { flammable yes } }"));
    }

    /// <summary>CBreakableProp::OnTakeDamage scales the blow before it takes it.</summary>
    [Fact]
    public void A_crate_takes_a_crowbar_twice_over()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelKeyValueText", "mdlkeyvalue { prop_data { health 40 dmg.club 2 dmg.bullets 0.5 } }")
              .Returns("Entities.GetHealth", 40);
        var crate = new EntityHandle(51);
        registry.Spawn(crate, EntityNames.PropPhysics);
        registry.Get(crate)!.OnPhysicsCreated();

        Assert.Equal(20, registry.Damaged(crate, EntityHandle.None, 10f, (int)DamageType.Club));
        Assert.Equal(5, registry.Damaged(crate, EntityHandle.None, 10f, (int)DamageType.Bullet));
    }
}

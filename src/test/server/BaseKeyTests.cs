using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The keys CBaseEntity and CBaseAnimating take for every class that does not
/// take them itself: disableshadows (EF_NOSHADOW, any entity), and for a model
/// entity skin, body / SetBodyGroup and modelscale. Found 2026-10-27:
/// dm_lockdown keys skin 1 or 2 on 19 props and disableshadows on 23, and ours
/// drew every one with skin 0 and a shadow.
/// </summary>
public class BaseKeyTests
{
    private static readonly EntityHandle Ent = new(0x0007_0101u);

    private sealed class Skins : IServerSkins
    {
        public readonly List<(EntityHandle, int)> Skin = new(), Body = new();
        public void SetSkin(EntityHandle entity, int skin) => Skin.Add((entity, skin));
        public void SetBody(EntityHandle entity, int body) => Body.Add((entity, body));
    }

    private sealed class Bits : IEntityEffectBits
    {
        public readonly List<(EntityHandle, EntityEffect, bool)> Set = new();
        public int Read(EntityHandle entity) => 0;
        void IEntityEffectBits.Set(EntityHandle entity, EntityEffect bits, bool on) => Set.Add((entity, bits, on));
    }

    private static (RecordingEngine Engine, Skins Skins, Bits Bits) Keyed(string className, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        var skins = new Skins();
        var bits = new Bits();
        var services = Services(out var engine);
        services.AddInstance<IServerSkins>(skins);
        services.AddInstance<IEntityEffectBits>(bits);
        registry.Services = services;
        engine.Strict = false;

        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, className, key, value);
        return (engine, skins, bits);
    }

    [Fact]
    public void A_props_skin_key_is_its_skin()
        => Assert.Equal(new[] { (Ent, 2) }, Keyed(EntityNames.PropPhysics, ("skin", "2")).Skins.Skin);

    [Theory]
    [InlineData("body")]
    [InlineData("SetBodyGroup")]
    public void A_props_body_key_is_its_body(string key)
        => Assert.Equal(new[] { (Ent, 3) }, Keyed(EntityNames.PropPhysics, (key, "3")).Skins.Body);

    [Fact]
    public void A_props_modelscale_key_is_its_scale()
    {
        var (engine, _, _) = Keyed(EntityNames.PropPhysics, ("modelscale", "1.5"));

        Assert.Equal(1.5f, engine.Last("EntityRender.SetModelScale")!.Value.ArgAs<float>(1));
    }

    [Fact]
    public void Disableshadows_marks_any_entity_EF_NOSHADOW()
    {
        Assert.Equal(new[] { (Ent, EntityEffect.NoShadow, true) },
                     Keyed(EntityNames.TriggerMultiple, ("disableshadows", "1")).Bits.Set);
        Assert.Empty(Keyed(EntityNames.PropPhysics, ("disableshadows", "0")).Bits.Set);
    }

    /// <summary>CBaseAnimating's keys are its own: a brush is no animating entity.</summary>
    [Fact]
    public void A_brush_takes_no_skin()
        => Assert.Empty(Keyed(EntityNames.TriggerMultiple, ("skin", "2")).Skins.Skin);

    /// <summary>
    /// The class's last word on its draw, after its keys and its spawn: the
    /// last EntityRender.SetVisible made for it. cp_granary keys "rendermode"
    /// "10" on two func_illusionary light-fill slabs (lights/white_fill, under
    /// the silo platforms either side of the middle), which ours drew white.
    /// </summary>
    private static bool DrawnAfterSpawn(string className, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("Precache.PrecacheModel", 3).Returns("EntityRender.GetModelKeyValueText", null);

        registry.KeyValue(Ent, className, "model", "*82");
        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, className, key, value);
        registry.Spawn(Ent, className);

        var last = engine.Last("EntityRender.SetVisible");
        Assert.True(last is not null, $"{className} never said whether it is drawn");
        return (bool)last!.Value.Args[1]!;
    }

    /// <summary>
    /// kRenderNone (RenderMode_t 10, const.h) draws nothing: stock's
    /// C_BaseEntity::ShouldDraw (c_baseentity.cpp:1447) refuses it whatever the
    /// entity's effects say, and CBaseEntity keys "rendermode" for every class.
    /// </summary>
    [Theory]
    [InlineData(EntityNames.FuncIllusionary)]
    [InlineData(EntityNames.FuncBrush)]
    [InlineData(EntityNames.FuncWall)]
    public void A_brush_keyed_render_mode_none_is_not_drawn(string className)
        => Assert.False(DrawnAfterSpawn(className, ("rendermode", "10")));

    /// <summary>The retention half: any other mode leaves the brush drawn.</summary>
    [Theory]
    [InlineData(EntityNames.FuncIllusionary, "0")]
    [InlineData(EntityNames.FuncIllusionary, "9")]
    [InlineData(EntityNames.FuncBrush, "0")]
    [InlineData(EntityNames.FuncWall, "5")]
    public void A_brush_keyed_another_render_mode_is_drawn(string className, string mode)
        => Assert.True(DrawnAfterSpawn(className, ("rendermode", mode)));

    [Fact]
    public void A_brush_with_no_render_mode_key_is_drawn()
        => Assert.True(DrawnAfterSpawn(EntityNames.FuncIllusionary));

    /// <summary>
    /// Keyed after its spawn (an AddOutput, a SetKeyValue), the mode reaches the
    /// draw at once and back again, as stock's client reads m_nRenderMode each frame.
    /// </summary>
    [Fact]
    public void A_render_mode_keyed_after_spawn_hides_and_shows_the_brush()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropPhysics).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("Precache.PrecacheModel", 3).Returns("EntityRender.GetModelKeyValueText", null);
        registry.KeyValue(Ent, EntityNames.FuncIllusionary, "model", "*82");
        registry.Spawn(Ent, EntityNames.FuncIllusionary);
        Assert.True((bool)engine.Last("EntityRender.SetVisible")!.Value.Args[1]!);

        registry.KeyValue(Ent, EntityNames.FuncIllusionary, "rendermode", "10");
        Assert.False((bool)engine.Last("EntityRender.SetVisible")!.Value.Args[1]!);

        registry.KeyValue(Ent, EntityNames.FuncIllusionary, "rendermode", "0");
        Assert.True((bool)engine.Last("EntityRender.SetVisible")!.Value.Args[1]!);
    }
}

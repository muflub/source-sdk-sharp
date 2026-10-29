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
}

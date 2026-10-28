using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// CBaseAnimating's inputs on every model entity (baseanimating.cpp): skin,
/// SetBodyGroup, SetModelScale (at once or over time), SetModel, and the
/// ignites with OnIgnite. Found in the contract diff (2026-10-28): none of the
/// 43 classes stock derives from CBaseAnimating answered any of them, because
/// ours had no base for them to share.
/// </summary>
public class AnimatingTests
{
    private static readonly EntityHandle Can = new(0x000A_0101u);
    private static readonly EntityHandle FlameHandle = new(0x000A_0102u);

    private sealed class Rig
    {
        public required ScriptedEntityRegistry Registry;
        public required RecordingEngine Engine;
        public required RecordingServerSkins Skins;
        public readonly ThinkScheduler Thinks = new();
        public float Now = 10f;

        public AnimatingEntity Entity => (AnimatingEntity)Registry.Get(Can)!;

        public void Input(string name, string value = "", float number = 0f)
            => Registry.Input(Can, name, value, number, EntityHandle.None);

        public float[] Scales => Engine.AllOf("EntityRender.SetModelScale").Select(c => c.ArgAs<float>(1)).ToArray();

        public SsEntityFlame Flame => (SsEntityFlame)Registry.Get(FlameHandle)!;
    }

    private static Rig Made(string className = EntityNames.ItemSodacan)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(AnimatingEntity).Assembly);
        var services = Services(out var engine);
        var skins = new RecordingServerSkins();
        services.AddInstance<IServerSkins>(skins);
        services.AddInstance(new Flames(new RecordingLog(), registry, engine));
        services.AddInstance<IEntityRoster<EnvFire>>(new EntityRoster<EnvFire>());
        registry.Services = services;
        engine.Strict = false;

        var rig = new Rig { Registry = registry, Engine = engine, Skins = skins };
        engine.ReturnsEachCall("Clock.Now", () => rig.Now)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.Create", FlameHandle)
              .Returns("Precache.PrecacheModel", 3);

        registry.KeyValue(Can, className, "origin", "0 0 0");
        rig.Entity.BindThinks(rig.Thinks);

        // The flame Flames makes: the registry's own instance under the handle
        // the engine's Create answers.
        registry.KeyValue(FlameHandle, EntityNames.EntityFlame, "origin", "0 0 0");
        rig.Flame.BindThinks(rig.Thinks);
        return rig;
    }

    [Fact]
    public void The_skin_input_is_its_skin_and_SetBodyGroup_its_body()
    {
        var rig = Made();

        rig.Input("skin", "2", 2f);
        rig.Input("SetBodyGroup", "3", 3f);

        Assert.Equal(new[] { (Can, 2) }, rig.Skins.Set);
        Assert.Equal(new[] { (Can, 3) }, rig.Skins.Bodies);
    }

    [Fact]
    public void SetModelScale_with_no_duration_is_at_once()
    {
        var rig = Made();

        rig.Input("SetModelScale", "2 0 0");

        Assert.Equal(new[] { 2f }, rig.Scales);
        Assert.False(rig.Thinks.IsScheduled(Can, "UpdateModelScaleThink"));
    }

    /// <summary>UpdateModelScale: a lerp from the scale it had, each think, to the goal at the finish.</summary>
    [Fact]
    public void SetModelScale_over_a_second_lerps_and_lands_on_the_goal()
    {
        var rig = Made();

        rig.Input("SetModelScale", "3 1 0");
        Assert.Empty(rig.Scales);

        rig.Now = 10.5f;
        rig.Entity.RunThink("UpdateModelScaleThink");
        Assert.Equal(2f, rig.Scales[^1], 3);
        Assert.Equal(10.5f, rig.Thinks.NextThinkTime(Can, "UpdateModelScaleThink"), 3);

        rig.Now = 11.25f;
        rig.Entity.RunThink("UpdateModelScaleThink");
        Assert.Equal(3f, rig.Scales[^1], 3);
        Assert.Equal(3f, rig.Entity.ModelScale);
    }

    /// <summary>The base's think is taken before a class's own OnThink(context), which an item overrides.</summary>
    [Fact]
    public void A_class_with_context_thinks_of_its_own_still_scales()
    {
        var rig = Made(EntityNames.ItemBattery);
        rig.Input("SetModelScale", "2 1 0");

        rig.Now = 12f;
        rig.Entity.RunThink("UpdateModelScaleThink");

        Assert.Equal(2f, rig.Scales[^1], 3);
    }

    [Fact]
    public void SetModel_sets_a_model_that_precaches_and_ignores_one_that_does_not()
    {
        var rig = Made();

        rig.Input("SetModel", "models/props_junk/garbage_can.mdl");
        Assert.Equal("models/props_junk/garbage_can.mdl", rig.Engine.Last("EntityRender.SetModel")!.Value.ArgAs<string>(1));

        rig.Engine.Returns("Precache.PrecacheModel", -1);
        rig.Input("SetModel", "models/missing.mdl");
        Assert.Single(rig.Engine.AllOf("EntityRender.SetModel"));

        rig.Input("SetModel", "");
        Assert.Single(rig.Engine.AllOf("EntityRender.SetModel"));
    }

    [Fact]
    public void Ignite_sets_it_alight_for_thirty_seconds_and_fires_OnIgnite_once()
    {
        var rig = Made();

        rig.Input("Ignite");
        Assert.Equal(Can, rig.Flame.Attached);
        Assert.Equal(40f, rig.Flame.ExpiresAt, 3);
        Assert.True(rig.Entity.IsOnFire);
        Assert.Equal(1, rig.Entity.OnIgnite.FireCount);

        rig.Now = 20f;
        rig.Input("Ignite");
        Assert.Equal(40f, rig.Flame.ExpiresAt, 3);
        Assert.Equal(1, rig.Entity.OnIgnite.FireCount);
    }

    [Fact]
    public void IgniteLifetime_sets_alight_what_is_not_burning_and_sets_how_long()
    {
        var rig = Made();

        rig.Input("IgniteLifetime", "5", 5f);

        Assert.Equal(Can, rig.Flame.Attached);
        Assert.Equal(15f, rig.Flame.ExpiresAt, 3);
    }

    [Fact]
    public void The_hitbox_fire_inputs_set_the_flames_values()
    {
        var rig = Made();
        Assert.Equal((10, 1f), (rig.Flame.NumHitboxFires, rig.Flame.HitboxFireScale));

        rig.Input("IgniteNumHitboxFires", "4", 4f);
        rig.Input("IgniteHitboxFireScale", "0.5", 0.5f);

        Assert.Equal((4, 0.5f), (rig.Flame.NumHitboxFires, rig.Flame.HitboxFireScale));
        Assert.Equal(Can, rig.Flame.Attached);
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>A body that dissolves: CHL2MP_Player::Event_Killed's DMG_DISSOLVE arm, and EntityDissolve.cpp's times.</summary>
public class RagdollDissolveTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);
    private static readonly EntityHandle Body = new(0x0005_0020u);

    private static (Corpses Corpses, SsPlayer Player, SsRagdoll Body, ScriptedEntityRegistry Registry) Rig()
    {
        var services = Services(out var engine);
        engine.Strict = false;
        engine.Returns("EntityRender.GetModelName", "models/combine_soldier.mdl")
              .Returns("Entities.GetClassName", EntityNames.Player)
              .Returns("Entities.GetOrigin", new Vector(1f, 2f, 3f))
              .Returns("Entities.GetAngles", default(Vector))
              .Returns("Entities.GetVelocity", default(Vector))
              .Returns("Entities.Create", Body)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetIndex", 1)
              .Returns("Clock.Now", 10f);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsRagdoll).Assembly);
        registry.Services = services;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "victim");
        registry.Spawn(Body, EntityNames.Hl2mpRagdoll);
        var corpses = new Corpses(new RecordingLog(), engine, engine, registry, engine);
        return (corpses, Assert.IsType<SsPlayer>(registry.Get(Player)), Assert.IsType<SsRagdoll>(registry.Get(Body)), registry);
    }

    [Fact]
    public void A_dissolving_blow_makes_a_dissolving_body()
    {
        var (corpses, player, body, _) = Rig();
        player.OnDamaged(EntityHandle.None, 1f, DamageType.Crush | DamageType.Dissolve);
        Assert.Equal(DamageType.Crush | DamageType.Dissolve, player.LastHitType);

        corpses.Make(Player);

        Assert.True(body.Dissolving);
        Assert.True(body.State.DissolveStart > 0f);
    }

    [Fact]
    public void Any_other_blow_leaves_a_body_as_it_is()
    {
        var (corpses, player, body, _) = Rig();
        player.OnDamaged(EntityHandle.None, 1f, DamageType.Bullet);

        corpses.Make(Player);

        Assert.False(body.Dissolving);
    }

    /// <summary>The blow is forgotten at the death, or a later suicide would dissolve too.</summary>
    [Fact]
    public void A_death_forgets_the_blow()
    {
        var (_, player, _, _) = Rig();
        player.OnDamaged(EntityHandle.None, 1f, DamageType.Dissolve);
        player.OnKilled(EntityHandle.None);
        Assert.Equal(default, player.LastHitType);
    }

    [Fact]
    public void A_dissolving_body_is_gone_when_the_dissolve_is_done()
    {
        var (_, _, body, registry) = Rig();
        body.Dissolve();
        Assert.Equal(0, registry.Removal.FreeNow());

        body.OnThink();

        Assert.True(body.Gone);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(1.95f, 1f)]
    public void It_goes_to_black_over_the_first_second(float elapsed, float fade)
        => Assert.Equal(fade, RagdollDissolve.FadeIn(elapsed), 3);

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1.9f, 1f)]
    [InlineData(1.95f, 0.5f)]
    [InlineData(2f, 0f)]
    [InlineData(5f, 0f)]
    public void It_vanishes_in_the_last_tenth_of_a_second(float elapsed, float alpha)
        => Assert.Equal(alpha, RagdollDissolve.ModelFadeOut(elapsed), 3);
}

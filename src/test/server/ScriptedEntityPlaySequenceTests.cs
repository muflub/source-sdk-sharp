using System.Linq;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>A fixture that exposes the base's PlaySequence, which is protected.</summary>
[ScriptedEntity("play_sequence_fixture")]
public sealed class PlaySequenceFixture : ScriptedEntity
{
    public bool Play(string name) => PlaySequence(name);
}

/// <summary>
/// WHERE A SCRIPTED ENTITY RESOLVES AN ANIMATION NAME.
///
/// <para>
/// <c>ScriptedEntity.PlaySequence</c> is what a map's animation name reaches:
/// <c>PropDynamic</c> calls it with whatever the mapper wrote. It used to
/// resolve that name through the <c>EntityLookupSequence</c> ABI slot — and
/// lane 2-leafmath set that slot to <c>NULL</c> on both arms, which the
/// wrapper's own null guard turned into a permanent -1. So <c>PlaySequence</c>
/// returned false for every prop on the map, at every call, with nothing said
/// anywhere.
/// </para>
///
/// <para>
/// These were source reads while the base called the static <c>Engine</c>,
/// which a test could not watch. Since plan_abi_hotreload D8 the base asks the
/// render area its registry bound (whose real binding resolves the name through
/// the cached <c>ModelDescriptor</c>), so the conversation is observable and
/// the facts are behavioural.
/// </para>
/// </summary>
public class ScriptedEntityPlaySequenceTests
{
    private static readonly EntityHandle Handle = new(0x0001_0042u);

    private static (PlaySequenceFixture Entity, RecordingEngine Engine) Spawned(int lookup)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PlaySequenceFixture).Assembly);
        registry.Services = TestServices.Services(out var engine);
        engine.Returns("EntityRender.LookupSequence", lookup);

        registry.Spawn(Handle, "play_sequence_fixture");
        return ((PlaySequenceFixture)registry.Get(Handle)!, engine);
    }

    [Fact]
    public void PlaySequenceResolvesTheNameThroughTheBoundRenderArea()
    {
        var (entity, engine) = Spawned(lookup: 3);

        Assert.True(entity.Play("idle"), "a name the model has plays");

        EngineCall ask = Assert.Single(engine.AllOf("EntityRender.LookupSequence"));
        Assert.Equal(Handle, ask.ArgAs<EntityHandle>(0));
        Assert.Equal("idle", ask.ArgAs<string>(1));
    }

    /// <summary>
    /// And that it PLAYS what it resolved: a body that resolved correctly and
    /// stopped setting the sequence would satisfy the fact above and animate
    /// nothing.
    /// </summary>
    [Fact]
    public void PlaySequenceSetsTheSequenceItResolved()
    {
        var (entity, engine) = Spawned(lookup: 3);

        entity.Play("idle");

        EngineCall set = Assert.Single(engine.AllOf("EntityRender.SetSequence"));
        Assert.Equal(Handle, set.ArgAs<EntityHandle>(0));
        Assert.Equal(3, set.ArgAs<int>(1));
    }

    [Fact]
    public void ANameTheModelLacksPlaysNothingAndSaysSo()
    {
        var (entity, engine) = Spawned(lookup: -1);

        Assert.False(entity.Play("no_such_sequence"), "a missing name answers false");
        Assert.False(engine.Called("EntityRender.SetSequence"), "and sets nothing");
    }

    /// <summary>
    /// TWO REGISTRIES, TWO ENGINES (plan_abi_hotreload D8): each entity's
    /// calls reach the engine its own registry was given, never a shared or
    /// active-side one.
    /// </summary>
    [Fact]
    public void TwoRegistriesEntitiesCallTheirOwnEngines()
    {
        var (first, firstEngine) = Spawned(lookup: 4);
        var (second, secondEngine) = Spawned(lookup: 9);

        first.Play("a");
        second.Play("b");

        Assert.Equal(new[] { 4 }, firstEngine.AllOf("EntityRender.SetSequence").Select(c => c.ArgAs<int>(1)));
        Assert.Equal(new[] { 9 }, secondEngine.AllOf("EntityRender.SetSequence").Select(c => c.ArgAs<int>(1)));
    }

    /// <summary>
    /// An entity no registry built has no engine at all: the call names the
    /// missing area rather than reaching whichever side happens to be active.
    /// </summary>
    [Fact]
    public void AnUnboundEntityRefusesRatherThanReachingAStaticEngine()
    {
        var entity = new PlaySequenceFixture();

        var refusal = Assert.Throws<System.InvalidOperationException>(() => entity.Play("idle"));
        Assert.Contains(nameof(IEngineEntityRender), refusal.Message);
    }
}

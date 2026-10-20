using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ai_sound, the write half of the world's noise record.
///
/// <para>
/// What is worth pinning is not that it inserts — that is one ABI call — but
/// WHAT each of its two inputs decides to insert. They differ in exactly two
/// ways and both are easy to lose: the obsolete input takes its volume from the
/// input's parameter rather than from the keyvalue, and it does not OR the
/// context bits onto the type. A port that quietly made them the same would
/// change every legacy map that uses the old one, and nothing would say so.
/// </para>
/// </summary>
public class AiSoundTests
{

    /// <summary>
    /// That the registry the SERVER builds has this classname in it.
    ///
    /// <para>
    /// Looks like a tautology and is not. Registration walks the assembly for
    /// <c>[ScriptedEntity]</c>, and every way it can go wrong — an attribute on
    /// an abstract type, a class that does not derive from
    /// <see cref="ScriptedEntity"/>, a var set the generator could not describe
    /// — is a WARNING and a skip rather than an error. The class compiles, the
    /// mod starts, and the map's entity is silently not there.
    /// </para>
    ///
    /// <para>
    /// The anchor is deliberately the assembly rather than the type: that is
    /// what <c>ServerModule</c> passes, so this asks the question the game asks.
    /// </para>
    /// </summary>
    [Fact]
    public void TheClassIsDeclared()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(AiSound).Assembly);
        registry.Services = Services();

        Assert.True(registry.IsRegistered(EntityNames.AiSound), "the server's registry declares ai_sound");

        // Its reader, for the same reason: the pair is the point, and a
        // microphone that stopped registering would make every ai_sound check
        // below pass while the feature did nothing.
        Assert.True(registry.IsRegistered(EntityNames.EnvMicrophone), "and it still declares env_microphone, which reads what it writes");
    }

    private static AiSound Spawn(uint handle, params (string Key, string Value)[] keys)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(AiSound).Assembly);
        registry.Services = Services();

        var entity = new EntityHandle(handle);

        foreach (var (key, value) in keys)
            registry.KeyValue(entity, EntityNames.AiSound, key, value);

        registry.Spawn(entity, EntityNames.AiSound);
        return (AiSound)registry.Get(entity)!;
    }

    [Fact]
    public void TheKeysArrive()
    {
        // Every key stock declares, under the spelling stock declares it with.
        var sound = Spawn(0x00A1_0001u,
            ("soundtype", "1"),
            ("soundcontext", "4"),
            ("volume", "512"),
            ("duration", "1.5"),
            ("locationproxy", "somewhere"));

        Assert.True(sound.Modern().Volume == 512, "ai_sound takes its volume from the map");
        Assert.True(Math.Abs(sound.Modern().Duration - 1.5f) < 0.0001f, "ai_sound takes its duration from the map");

        // The default is stock's CONSTRUCTOR, not zero, and it matters: a
        // duration of zero is a noise gone before anything samples the list.
        var bare = Spawn(0x00A1_0002u, ("soundtype", "1"));

        Assert.True(Math.Abs(bare.Legacy(0f).Duration - 0.3f) < 0.0001f, "an ai_sound with no duration key keeps stock's 0.3");

        Assert.True(bare.Modern().Volume == 0, "an ai_sound with no volume key is silent rather than loud");
    }

    [Fact]
    public void TheTwoInputsDiffer()
    {
        var sound = Spawn(0x00A1_0003u,
            ("soundtype", "1"),        // SOUND_COMBAT
            ("soundcontext", "4"),     // some qualifier; the number is the point, not the name
            ("volume", "512"));

        // EmitAISound: the keyed volume, and the context ORed on.
        Assert.True((int)sound.Modern().Type == (1 | 4), "EmitAISound ORs the context onto the type");

        Assert.True(sound.Modern().Volume == 512, "EmitAISound uses the volume the map set");

        // InsertSound: the parameter's volume, and NO context. Stock's own
        // comment above it says "*** OBSOLETE **** Here for legacy support
        // only!", and legacy support is the whole reason it stays different.
        Assert.True((int)sound.Legacy(100f).Type == 1, "InsertSound leaves the context bits off");

        Assert.True(sound.Legacy(100f).Volume == 100, "InsertSound takes the volume from the input, not the keyvalue");

        Assert.True(Math.Abs(sound.Legacy(100f).Duration - sound.Modern().Duration) < 0.0001f, "InsertSound still uses the keyed duration");
    }

    [Fact]
    public void TheContextBitIsNotANeighbour()
    {
        // SOUND_CONTEXT_EXPLOSION is 0x02000000 — twenty-odd places above the
        // plain kinds. A map writes the number, and it has to survive the trip
        // through an int keyvalue and a cast to a [Flags] enum that does not
        // name every member. This is the same case MicrophoneTests pins from the
        // reading side; the two halves must agree or a microphone will not hear
        // what an ai_sound entered.
        var sound = Spawn(0x00A1_0004u,
            ("soundtype", "1"),
            ("soundcontext", "33554432"));

        Assert.True((int)sound.Modern().Type == (1 | 0x02000000), "a context bit high above the kinds survives the keyvalue");

        Assert.True(((int)sound.Modern().Type & (int)SoundType.ContextExplosion) != 0, "and it is the same bit env_microphone asks for");
    }
}

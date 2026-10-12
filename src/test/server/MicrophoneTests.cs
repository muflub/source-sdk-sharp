using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// env_microphone's meter: the sound mask a map's spawnflags build, whether a
/// recorded noise reaches the sample point, and how the reading settles.
///
/// <para>
/// The hearing test is the one worth pinning, because its units read backwards
/// from every other volume in the game: a CSoundEnt volume is roughly how far a
/// noise CARRIES, so the microphone divides a distance by it rather than
/// multiplying, and the answer is how much reach was left over.
/// </para>
/// </summary>
public class MicrophoneTests
{

    private static ScriptedEntityRegistry NewRegistry()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(EnvMicrophone).Assembly);
        registry.Services = Services();
        return registry;
    }

    private static EnvMicrophone Spawn(EntityHandle handle,
                                       params (string Key, string Value)[] keys)
    {
        var registry = NewRegistry();

        foreach (var (key, value) in keys)
            registry.KeyValue(handle, EntityNames.EnvMicrophone, key, value);

        registry.Spawn(handle, EntityNames.EnvMicrophone);
        return (EnvMicrophone)registry.Get(handle)!;
    }

    /// <summary>Reaches the mask the spawnflags built, without exposing it.</summary>
    private static SoundType MaskOf(EnvMicrophone microphone)
    {
        var field = typeof(EnvMicrophone).GetField("_mask",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        return (SoundType)field!.GetValue(microphone)!;
    }

    [Fact]
    public void SpawnflagsBuildTheMask()
    {
        // The mask cannot be a shifted copy of the spawnflags and the two tables
        // are kept apart for exactly this: the explosion flag is bit 5 and the
        // CSoundEnt bit it names is a CONTEXT twenty places further up.
        var microphone = Spawn(new EntityHandle(0x0011_0001u), ("SoundMask", "0"));

        Assert.True(MaskOf(microphone) == SoundType.None, "a microphone with no flags hears nothing at all");

        // The SoundMask key is ORed with the flags rather than replaced by them,
        // which is stock, and it is how a map asks for a kind with no flag.
        //
        // Checked as a raw bit on purpose: 8 is SOUND_DANGER, which SoundType
        // does not name because nothing here reacts to it. A mask the map writes
        // has to reach the query intact whether or not the enum has a word for
        // it, so this is the case that would break if the cast ever started
        // filtering to known members.
        var direct = Spawn(new EntityHandle(0x0011_0003u), ("SoundMask", "8"));

        Assert.True(((int)MaskOf(direct) & 8) != 0, "a SoundMask the map wrote survives the spawnflags pass");
    }

    /// <summary>Drives the private hearing test with a fabricated recorded noise.</summary>
    private static bool Hears(EnvMicrophone microphone, Vector where, int volume,
                              Vector at, out float heard)
    {
        var method = typeof(EnvMicrophone).GetMethod("Hears",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var sound = new HeardSound
        {
            Origin = where,
            Volume = volume,
            Type = SoundType.Combat,
            Owner = EntityHandle.None,
        };

        object[] args = [sound, at, 0f];
        bool result = (bool)method!.Invoke(microphone, args)!;

        heard = (float)args[2]!;
        return result;
    }

    [Fact]
    public void HearingIsDistanceAgainstReach()
    {
        var microphone = Spawn(new EntityHandle(0x0011_0010u), ("Sensitivity", "1"));

        var at = new Vector(0f, 0f, 0f);

        // A volume of 100 means the noise carries about 100 units, so at 50 the
        // microphone has half the reach left.
        Assert.True(Hears(microphone, new Vector(50f, 0f, 0f), 100, at, out float half)
            && Math.Abs(half - 0.5f) < 0.001f, "a noise at half its reach reads about a half");

        Assert.True(!Hears(microphone, new Vector(150f, 0f, 0f), 100, at, out _), "a noise beyond its reach is not heard");

        // Guarded before the division, which is the only way a noise recorded
        // with zero volume is ever heard at all.
        Assert.True(Hears(microphone, at, 0, at, out float onTop) && onTop == 1f, "a noise exactly on the sample point reads full");

        var sensitive = Spawn(new EntityHandle(0x0011_0011u), ("Sensitivity", "4"));

        Assert.True(Hears(sensitive, new Vector(200f, 0f, 0f), 100, at, out float far)
            && Math.Abs(far - 0.5f) < 0.001f, "sensitivity multiplies the reach rather than the volume");

        // Zero would be a divide by zero, and one is the identity — which is
        // what a map that left the key alone means by it.
        var deaf = Spawn(new EntityHandle(0x0011_0012u), ("Sensitivity", "0"));

        Assert.True(Hears(deaf, new Vector(50f, 0f, 0f), 100, at, out _), "a sensitivity of zero is read as one");

        var limited = Spawn(new EntityHandle(0x0011_0013u),
                            ("Sensitivity", "10"), ("MaxRange", "100"));

        Assert.True(!Hears(limited, new Vector(150f, 0f, 0f), 1000, at, out _), "MaxRange cuts a noise off however loud it was");
    }

    [Fact]
    public void TheReadingSettlesAndStops()
    {
        // No smoothing: the reading is whatever was just heard.
        Assert.True(EnvMicrophone.Smooth(0f, 1f, 0f) == 1f, "with no smoothing the reading arrives at once");

        // Smoothed, it approaches over several samples rather than jumping.
        float level = 0f;

        for (int i = 0; i < 3; i++)
            level = EnvMicrophone.Smooth(level, 1f, 0.9f);

        Assert.True(level > 0f && level < 0.5f, $"smoothing approaches rather than arriving (reached {level:0.000})");

        // And the epsilon is what stops it approaching forever: without it the
        // exponential creeps toward zero and the output fires on every sample
        // long after the room has gone quiet.
        float quiet = 0.001f;
        float settled = EnvMicrophone.Smooth(quiet, 0f, 0.9f);

        Assert.True(settled == 0f, "a reading inside the epsilon snaps to the target");
        Assert.True(EnvMicrophone.Smooth(0f, 0f, 0.9f) == 0f, "and then stops changing");
    }
}

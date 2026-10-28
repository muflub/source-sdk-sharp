using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's sound members over the managed SoundEmitter
/// (plan_server_native_v2 T4a), against what shell_sound.cpp's five slots,
/// shell_soundenvelope.cpp's CSoundPatch and the standalone player's move
/// helper did natively. The engine is SoundEmitterTests' recorder; the
/// entities are RecordingEngine's.
/// </summary>
public sealed class ServerSoundsTests
{
    private readonly SoundEmitterTests.FakeEngine _engine = new();
    private readonly SoundEmitterTests.Listeners _listeners = new();
    private readonly SoundEmitterTests.Captions _captions = new();
    private readonly RecordingEngine _entities = new();
    private readonly List<int> _created = [];
    private readonly List<string> _warnings = [];

    private static readonly EntityHandle Door = new(64u | (1u << 16));

    public ServerSoundsTests()
    {
        _engine.Scripts["Doors.Move1"] = new SoundEmitterTests.Script
        {
            Index = 7, Channel = 4, Volume = 0.8f, SoundLevel = 80, Pitch = 95, Wave = "doors/move1.wav",
            Waves = ["doors/move1.wav"],
        };
        _entities.Returns("Entities.IsValid", true)
                 .Returns("Entities.GetIndex", 64)
                 .Returns("Entities.GetWorldSpaceCenter", new Vector(10, 20, 30))
                 .Returns("Entities.GetOrigin", new Vector(1, 2, 3));
        _engine.PasBits[0] = 0b110;   // players 2 and 3
        _listeners.Ears[2] = new Vector(10, 20, 30);
        _listeners.Ears[3] = new Vector(10, 20, 30);
    }

    private ServerSounds Sounds()
    {
        var emitter = new SoundEmitter(_engine, new SoundRecipients(_engine, _listeners), _captions,
                                       new RecordingEngine(), _ => null, () => false, _ => { }, _ => { });
        return new ServerSounds(emitter, _entities, _created.Add, _warnings.Add);
    }

    //-------------------------------------------------------------------------
    // EntityEmitSound / EntityStopSound
    //-------------------------------------------------------------------------

    [Fact]
    public void AnEntitysSoundComesFromItsIndexToThePasOfItsCentre()
    {
        Sounds().EntityEmitSound(Door, "Doors.Move1");

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal((64, "2,3", "doors/move1.wav", (Vector?)null), (emit.EntIndex, emit.Recipients, emit.Sample, emit.Origin));
        Assert.Contains(_entities.Calls, c => c.Member == "Entities.GetWorldSpaceCenter");
    }

    /// <summary>
    /// CBreakable's way (DamageSound, Die): GetParametersForSound, then the
    /// script's wave on its channel and level at the caller's volume and pitch,
    /// to the PAS of the centre at ATTN_NORM.
    /// </summary>
    [Fact]
    public void AnEntitysSoundAtAVolumeAndPitchIsTheScriptsWaveWithThoseInstead()
    {
        Sounds().EntityEmitSoundAs(Door, "Doors.Move1", 0.5f, 110);

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal((64, "doors/move1.wav", 4, 0.5f, 80, 110, 0),
                     (emit.EntIndex, emit.Sample, emit.Channel, emit.Volume, emit.SoundLevel, emit.Pitch, emit.Flags));
    }

    [Fact]
    public void A_sound_at_a_volume_with_no_script_plays_nothing()
    {
        Sounds().EntityEmitSoundAs(Door, "No.Such.Script", 0.5f, 110);

        Assert.Empty(_engine.Emits);
    }

    [Fact]
    public void TheScriptsLevelDecidesHowFarTheEntitysSoundCarries()
    {
        // SNDLVL 80 -> attenuation 20/30: 3000 units; player 3 is 3100 away.
        _listeners.Ears[3] = new Vector(10 + 3100, 20, 30);

        Sounds().EntityEmitSound(Door, "Doors.Move1");

        Assert.Equal("2", Assert.Single(_engine.Emits).Recipients);
    }

    [Fact]
    public void AnEntityTheClientCannotIndexSoundsFromTheWorldAtItsOrigin()
    {
        _entities.Returns("Entities.GetIndex", -1);

        Sounds().EntityEmitSound(Door, "Doors.Move1");

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal((0, (Vector?)new Vector(1, 2, 3)), (emit.EntIndex, emit.Origin));
    }

    [Fact]
    public void AHandleThatResolvesToNothingPlaysNothingAndIsSaidOnce()
    {
        _entities.Returns("Entities.IsValid", false);
        var sounds = Sounds();

        sounds.EntityEmitSound(Door, "Doors.Move1");
        sounds.EntityEmitSound(Door, "Doors.Move1");

        Assert.Empty(_engine.Emits);
        Assert.Contains("resolves to nothing", Assert.Single(_warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void AStopIsOnTheIndexTheSoundStartedOn()
    {
        Sounds().EntityStopSound(Door, "Doors.Move1");

        Assert.Equal((64, 4, "doors/move1.wav"), Assert.Single(_engine.Stops));
    }

    [Fact]
    public void AStopForAHandleThatResolvesToNothingIsTheWorlds()
    {
        _entities.Returns("Entities.IsValid", false);

        Sounds().EntityStopSound(Door, "Doors.Move1");

        Assert.Equal(0, Assert.Single(_engine.Stops).EntIndex);
    }

    //-------------------------------------------------------------------------
    // From a point
    //-------------------------------------------------------------------------

    [Fact]
    public void ASoundAtAPositionComesFromTheWorldThere()
    {
        Sounds().EmitSoundAtPosition("Doors.Move1", new Vector(10, 20, 30));

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal((0, (Vector?)new Vector(10, 20, 30), "2,3"), (emit.EntIndex, emit.Origin, emit.Recipients));
    }

    [Fact]
    public void AWorldScriptPlaysItsWaveAtItsLevelAndPitchOnTheChannelScaled()
    {
        Assert.True(Sounds().EmitWorldSoundScript("Doors.Move1", new Vector(10, 20, 30), 6, 0.5f));

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal((0, 6, "doors/move1.wav", 0.4f, 80, 95), (emit.EntIndex, emit.Channel, emit.Sample, emit.Volume, emit.SoundLevel, emit.Pitch));
    }

    [Fact]
    public void AWorldScriptTheTableDoesNotHaveIsFalse()
        => Assert.False(Sounds().EmitWorldSoundScript("No.Such", default, 0, 1f));

    [Fact]
    public void AnAmbientIsUtilEmitAmbientSound()
    {
        Sounds().EmitAmbientSound(9, new Vector(1, 1, 1), "Doors.Move1", 1f, 75, 0, 100);

        Assert.Equal((9, "doors/move1.wav"), (Assert.Single(_engine.Ambients).EntIndex, _engine.Ambients[0].Sample));
    }

    //-------------------------------------------------------------------------
    // The move helper
    //-------------------------------------------------------------------------

    [Fact]
    public void AMoveSoundWithParametersGoesToThePlainPasFromThePlayer()
    {
        _listeners.Ears[3] = new Vector(99999, 0, 0);   // the plain PAS does not crop by distance

        Sounds().MoveSound(1, new Vector(5, 5, 5), 4, "player/pl_fallpain1.wav", 0.7f, 70, 0, 100);

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal((1, "2,3", 4, "player/pl_fallpain1.wav", 0.7f, 70, (Vector?)new Vector(5, 5, 5)),
                     (emit.EntIndex, emit.Recipients, emit.Channel, emit.Sample, emit.Volume, emit.SoundLevel, emit.Origin));
    }

    [Fact]
    public void AMoveScriptGoesFromThePlayerWithNoOrigin()
    {
        Sounds().MoveSoundScript(1, new Vector(5, 5, 5), "Doors.Move1");

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal((1, "doors/move1.wav", (Vector?)null), (emit.EntIndex, emit.Sample, emit.Origin));
    }

    //-------------------------------------------------------------------------
    // Patches
    //-------------------------------------------------------------------------

    [Fact]
    public void APatchIsHandedOutFromOneAndToldToTheController()
    {
        var sounds = Sounds();

        int a = sounds.SoundPatchCreate(Door, 1, "Doors.Move1", 75);
        int b = sounds.SoundPatchCreate(Door, 1, "Doors.Move1", 75);

        Assert.Equal((1, 2), (a, b));
        Assert.Equal([1, 2], _created);
    }

    [Fact]
    public void APatchOnAnEntityThatResolvesToNothingIsNone()
    {
        _entities.Returns("Entities.IsValid", false);

        Assert.Equal(0, Sounds().SoundPatchCreate(Door, 1, "Doors.Move1", 75));
        Assert.Empty(_created);
    }

    [Fact]
    public void APatchPlaysItsScriptsWaveAtTheEnvelopeTimesTheScriptsVolume()
    {
        var sounds = Sounds();
        int patch = sounds.SoundPatchCreate(Door, 1, "Doors.Move1", 60);

        sounds.SoundPatchEmit(patch, 0.5f, 110f, SoundEmitter.SndChangeVol, 0f, false);

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal((64, 4, "doors/move1.wav", 0.4f, 80, 110, SoundEmitter.SndChangeVol),
                     (emit.EntIndex, emit.Channel, emit.Sample, emit.Volume, emit.SoundLevel, emit.Pitch, emit.Flags));
    }

    [Fact]
    public void AWavePatchPlaysAsNamedAtTheCallersLevelAndChannel()
    {
        var sounds = Sounds();
        int patch = sounds.SoundPatchCreate(Door, 3, "ambient/loop.WAV", 60);

        sounds.SoundPatchEmit(patch, 0.5f, 100f, 0, 0f, false);

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal((3, "ambient/loop.WAV", 0.5f, 60), (emit.Channel, emit.Sample, emit.Volume, emit.SoundLevel));
    }

    [Fact]
    public void APatchsStartCarriesItsTimeAndItsScriptsCaption()
    {
        _captions.Wanted = true;
        var sounds = Sounds();
        int patch = sounds.SoundPatchCreate(Door, 1, "Doors.Move1", 60);

        sounds.SoundPatchEmit(patch, 1f, 100f, 0, 12.5f, true);

        Assert.Equal(12.5f, Assert.Single(_engine.Emits).SoundTime);
        var sent = Assert.Single(_captions.Sent);
        Assert.Equal(("Doors.Move1", 2.0f, true), (sent.Token, sent.Duration, sent.WarnIfMissing));
    }

    [Fact]
    public void APatchThatNeverStartedIsNotStopped()
    {
        var sounds = Sounds();
        int patch = sounds.SoundPatchCreate(Door, 1, "Doors.Move1", 60);

        sounds.SoundPatchEmit(patch, 1f, 100f, 0, 0f, false);
        sounds.SoundPatchStop(patch);

        Assert.Empty(_engine.Stops);
    }

    [Fact]
    public void AStartedPatchIsStoppedOnceOnItsWaveAndChannel()
    {
        var sounds = Sounds();
        int patch = sounds.SoundPatchCreate(Door, 1, "Doors.Move1", 60);

        sounds.SoundPatchEmit(patch, 1f, 100f, 0, 0f, true);
        sounds.SoundPatchStop(patch);
        sounds.SoundPatchStop(patch);

        Assert.Equal((64, 4, "doors/move1.wav"), Assert.Single(_engine.Stops));
    }

    [Fact]
    public void APatchsStateIsItsEntitysIndexAndWhetherAnyoneHears()
    {
        var sounds = Sounds();
        int patch = sounds.SoundPatchCreate(Door, 1, "Doors.Move1", 60);

        sounds.SoundPatchState(patch, out int index, out bool active);

        Assert.Equal((64, true), (index, active));
    }

    [Fact]
    public void APatchWhoseEntityHasGoneAnswersMinusOneAndIsNotEmitted()
    {
        var sounds = Sounds();
        int patch = sounds.SoundPatchCreate(Door, 1, "Doors.Move1", 60);
        _entities.Returns("Entities.IsValid", false);

        sounds.SoundPatchState(patch, out int index, out _);
        sounds.SoundPatchEmit(patch, 1f, 100f, 0, 0f, false);

        Assert.Equal(-1, index);
        Assert.Empty(_engine.Emits);
    }

    [Fact]
    public void APatchFromTheWorldRidesNothingAndStaysSilent()
    {
        _entities.Returns("Entities.GetIndex", -1);
        var sounds = Sounds();
        int patch = sounds.SoundPatchCreate(Door, 1, "Doors.Move1", 60);

        sounds.SoundPatchState(patch, out int index, out _);

        Assert.Equal(-1, index);
    }

    [Fact]
    public void APatchNobodyHearsIsKeptButNotEmitted()
    {
        _engine.PasBits[0] = 0;
        var sounds = Sounds();
        int patch = sounds.SoundPatchCreate(Door, 1, "Doors.Move1", 60);

        sounds.SoundPatchEmit(patch, 1f, 100f, 0, 0f, false);
        sounds.SoundPatchState(patch, out int index, out bool active);

        Assert.Empty(_engine.Emits);
        Assert.Equal((64, false), (index, active));
    }

    [Fact]
    public void AFreedPatchIsForgotten()
    {
        var sounds = Sounds();
        int patch = sounds.SoundPatchCreate(Door, 1, "Doors.Move1", 60);

        sounds.SoundPatchFree(patch);
        sounds.SoundPatchState(patch, out int index, out bool active);

        Assert.Equal((0, -1, false), (sounds.Patches.Count, index, active));
    }
}

using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client prediction's move world against stock's client move helper
/// (movehelper_client.cpp's CMoveHelperClient), lane c6-input, C6 cluster 9.
/// </summary>
public class ClientPredictionMoveWorldTests
{
    private static readonly EntityHandle Host = new(0x0001_0001u);

    /// <summary>The engine half: records the effects that reach it.</summary>
    private sealed class Engine : IMoveWorld
    {
        public readonly List<string> Seen = new();
        public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs, uint mask, int collisionGroup)
        {
            Seen.Add("Trace");
            return new MoveTrace { Fraction = 0.5f };
        }
        public int PointContents(in Vector point, int slot) { Seen.Add("PointContents"); return 32; }
        public MoveSurface Surface(int surfaceProps) { Seen.Add("Surface"); return new MoveSurface { Valid = true }; }
        public void ResetTouchList() => Seen.Add("ResetTouchList");
        public void AddToTouched(in MoveTrace trace, in Vector velocity) => Seen.Add("AddToTouched");
        public void StartSound(in Vector origin, string sound) => Seen.Add("StartSound");
        public void PlayerSetAnimation(int activity) => Seen.Add("PlayerSetAnimation");
        public bool PlayerFallingDamage(float damage) { Seen.Add("PlayerFallingDamage"); return false; }
        public void PlayStepSound(in Vector origin, int surfaceProps, float volume, bool force) => Seen.Add("PlayStepSound");
        public void UpdateStepSound(in Vector origin, in Vector velocity, int surfaceProps) => Seen.Add("UpdateStepSound");
        public void Splash() => Seen.Add("Splash");
        public void RumbleEffect(byte index, byte data, byte flags) => Seen.Add("RumbleEffect");
        public float EngineTime() { Seen.Add("EngineTime"); return 7f; }
        public void ResetDuckLatched() => Seen.Add("ResetDuckLatched");
        public void SetPlayerSurface(int material) => Seen.Add("SetPlayerSurface");
    }

    private static (ClientPredictionMoveWorld World, Engine Engine, RecordingEngine Sound) Rig(bool firstRun)
    {
        var engine = new Engine();
        var sound = new RecordingEngine();
        return (new ClientPredictionMoveWorld(engine, () => Host, () => firstRun, sound), engine, sound);
    }

    [Fact]
    public void TheFirstRunsSwimSoundIsPlayedFromTheHostAtTheOrigin()
    {
        var (w, engine, sound) = Rig(firstRun: true);
        w.StartSound(new Vector(1, 2, 3), "Player.Swim");
        var call = Assert.Single(sound.Calls, c => c.Member == "Sound.ClientEmitSound");
        Assert.Equal(Host, call.ArgAs<EntityHandle>(0));
        Assert.Equal("Player.Swim", call.ArgAs<string>(1));
        Assert.Equal(new Vector(1, 2, 3), call.ArgAs<Vector?>(2));
        Assert.DoesNotContain("StartSound", engine.Seen);
    }

    [Fact]
    public void ARepredictedRunPlaysNoSound()
    {
        var (w, engine, sound) = Rig(firstRun: false);
        w.StartSound(new Vector(1, 2, 3), "Player.Swim");
        Assert.DoesNotContain(sound.Calls, c => c.Member == "Sound.ClientEmitSound");
        Assert.DoesNotContain("StartSound", engine.Seen);
    }

    [Fact]
    public void FallingDamageIsTheServersAndTheAnswerIsAlive()
    {
        var (w, engine, _) = Rig(firstRun: true);
        Assert.True(w.PlayerFallingDamage(50f));
        Assert.Empty(engine.Seen);
    }

    [Fact]
    public void TheAnimationIsTheServersAndNothingCrosses()
    {
        var (w, engine, _) = Rig(firstRun: true);
        w.PlayerSetAnimation(3);
        Assert.Empty(engine.Seen);
    }

    [Fact]
    public void TheTouchListHasNoReaderOnThisClientAndNothingCrosses()
    {
        var (w, engine, _) = Rig(firstRun: true);
        w.ResetTouchList();
        w.AddToTouched(new MoveTrace(), Vector.Zero);
        Assert.Empty(engine.Seen);
    }

    [Fact]
    public void TheQuestionsAndTheOtherEffectsGoToTheEngine()
    {
        var (w, engine, _) = Rig(firstRun: true);
        Assert.Equal(0.5f, w.Trace(Vector.Zero, Vector.Zero, Vector.Zero, Vector.Zero, 0, 0).Fraction);
        Assert.Equal(32, w.PointContents(Vector.Zero, 0));
        Assert.True(w.Surface(1).Valid);
        w.PlayStepSound(Vector.Zero, 0, 1f, false);
        w.UpdateStepSound(Vector.Zero, Vector.Zero, 0);
        w.Splash();
        w.RumbleEffect(0, 0, 0);
        Assert.Equal(7f, w.EngineTime());
        w.ResetDuckLatched();
        w.SetPlayerSurface(0);
        Assert.Equal(new[] { "Trace", "PointContents", "Surface", "PlayStepSound", "UpdateStepSound", "Splash",
                             "RumbleEffect", "EngineTime", "ResetDuckLatched", "SetPlayerSurface" }, engine.Seen);
    }
}

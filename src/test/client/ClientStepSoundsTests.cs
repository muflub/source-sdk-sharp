using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Sounds;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The client's footsteps (the CLIENT_DLL arms of <c>CBasePlayer::UpdateStepSound</c>,
/// <c>CHL2MP_Player::PlayStepSound</c> and <c>C_BasePlayer::Simulate</c>): the
/// server sends a step only to the PAS less the PVS, so without these a player
/// heard no step of its own and none of anyone it could see.
/// </summary>
public class ClientStepSoundsTests
{
    private const int Local = 1, Other = 2;

    /// <summary>A citizen's model, so the step's prefix is NPC_Citizen.</summary>
    private const string Model = "models/humans/group03/male_01.mdl";

    private const int FlOnGround = 1 << 0, FlDucking = 1 << 1, MoveTypeWalk = 2, MaterialConcrete = 'C';

    private sealed class Sink : IClientStepSoundSink
    {
        public readonly List<(int Index, string Script, Vector Origin, float Volume)> Steps = [];
        public void EmitStep(int entIndex, string script, in Vector origin, float volume) => Steps.Add((entIndex, script, origin, volume));
    }

    private static ClientStepSounds Steps(Sink sink, bool footsteps = true, int maxClients = 8)
        => new(sink, () => footsteps, () => maxClients, _ => Model);

    // ---- the other players: C_BasePlayer::Simulate's step ------------------

    private sealed class Players(params ClientStandaloneEntity[] all) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => all;
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => all.FirstOrDefault(p => p.Index == entityIndex);
    }

    private sealed class Concrete : IFwdPhysicsSurfaceProps
    {
        public int ParseSurfaceData(string fileName, string text) => 0;
        public int SurfacePropCount() => 0;
        public string? PropName(int index) => null;
        public int SurfaceIndex(string name) => -1;
        public int GameMaterial(int surfaceProps) => MaterialConcrete;
        public MoveSurface Surface(int surfaceProps) => new() { Valid = true, Material = MaterialConcrete };
        public string? ScrapeSound(int surfaceProps, bool smooth) => null;
        public string? BulletImpactSound(int surfaceProps) => null;
    }

    private static ClientSsPlayer Player(RecordingEngine engine, int index, float speed)
    {
        var p = new ClientSsPlayer(engine) { Handle = new EntityHandle((uint)(0x0001_0000 | index)) };
        p.BindIndex(index);
        p.State.BindLocal();
        p.State.Alive = true;
        p.State.Flags = FlOnGround;
        p.State.MoveType = MoveTypeWalk;
        p.State.Velocity = new Vector(speed, 0f, 0f);
        return p;
    }

    private static (OtherPlayerSteps Steps, Sink Sink, ClientSsPlayer Local, ClientSsPlayer Other) OtherRig(float speed, bool ground = true)
    {
        var engine = Fake().Returns("Hud.LocalPlayerIndex", Local)
                           .Returns("Entities.GetOrigin", new Vector(10f, 20f, 0f))
                           .Returns("WorldQuery.ClientTraceLine", new ClientTraceResult(ground, 0.02f, Vector.Zero, new Vector(0, 0, 1), 0, 0, ground ? 3 : -1, 0));
        ClientSsPlayer local = Player(engine, Local, speed), other = Player(engine, Other, speed);
        var sink = new Sink();
        return (new OtherPlayerSteps(new Players(local, other), engine, engine, engine, new Concrete(), Steps(sink)), sink, local, other);
    }

    /// <summary>Three seconds at 60 frames a second.</summary>
    private static void Run(OtherPlayerSteps steps, int frames = 180)
    {
        for (int i = 0; i < frames; i++)
            steps.Frame(1f / 60f);
    }

    [Fact]
    public void AnotherPlayerRunningOnConcreteStepsEvery300MsFromAlternateFeet()
    {
        var (steps, sink, _, _) = OtherRig(speed: 250f);
        Run(steps);

        // 3 s at 300 ms a step: the first at once, then every 18 frames.
        Assert.InRange(sink.Steps.Count, 10, 11);
        Assert.All(sink.Steps, s => Assert.Equal(Other, s.Index));
        Assert.All(sink.Steps, s => Assert.Equal(0.5f, s.Volume));
        Assert.Equal("NPC_Citizen.RunFootstepLeft", sink.Steps[0].Script);
        Assert.Equal("NPC_Citizen.RunFootstepRight", sink.Steps[1].Script);
        Assert.Equal(new Vector(10f, 20f, 0f), sink.Steps[0].Origin);
    }

    [Fact]
    public void TheLocalPlayerIsNotSteppedFromItsNetworkedMovement()
    {
        var (steps, sink, _, _) = OtherRig(speed: 250f);
        Run(steps);
        Assert.DoesNotContain(sink.Steps, s => s.Index == Local);
    }

    [Fact]
    public void AWalkerStepsEvery400MsAtTheWalkingVolume()
    {
        var (steps, sink, _, _) = OtherRig(speed: 150f);
        Run(steps);
        Assert.InRange(sink.Steps.Count, 7, 8);
        Assert.All(sink.Steps, s => Assert.Equal(0.2f, s.Volume));
    }

    [Fact]
    public void AStandingOrDuckingPlayerOrOneWithNoGroundBelowMakesNoStep()
    {
        var (still, stillSink, _, _) = OtherRig(speed: 50f);
        Run(still);
        Assert.Empty(stillSink.Steps);

        var (ducked, duckedSink, _, other) = OtherRig(speed: 250f);
        other.State.Flags = FlOnGround | FlDucking;
        Run(ducked);
        Assert.Empty(duckedSink.Steps);

        var (air, airSink, _, _) = OtherRig(speed: 250f, ground: false);
        Run(air);
        Assert.Empty(airSink.Steps);
    }

    [Fact]
    public void FootstepsOffInMultiplayerMakeNoStep()
    {
        var sink = new Sink();
        ClientStepSounds steps = Steps(sink, footsteps: false);
        for (int i = 0; i < 180; i++)
            steps.UpdateOther(Other, Vector.Zero, new Vector(250f, 0, 0), 1f / 60f, FlOnGround, MoveTypeWalk, 0, MaterialConcrete);
        Assert.Empty(sink.Steps);
    }

    // ---- the local player: its predicted movement ---------------------------

    private sealed class NoSounds : IClientEntitySounds
    {
        public void EmitFrom(EntityHandle entity, string soundName, Vector? origin = null) { }
        public void StopFrom(EntityHandle entity, string soundName) { }
    }

    private sealed class Ground : IMoveWorld
    {
        public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs, uint mask, int collisionGroup) => default;
        public int PointContents(in Vector point, int slot) => 0;
        public void ResetPointContentsCache(bool optimizations) { }
        public MoveSurface Surface(int surfaceProps) => new() { Valid = true, Material = MaterialConcrete };
        public void ResetTouchList() { }
        public void AddToTouched(in MoveTrace trace, in Vector velocity) { }
        public void StartSound(in Vector origin, string sound) { }
        public void PlayerSetAnimation(int activity) { }
        public bool PlayerFallingDamage(float damage) => true;
        public void PlayStepSound(in Vector origin, int surfaceProps, float volume, bool force) { }
        public void UpdateStepSound(in Vector origin, in Vector velocity, int surfaceProps) { }
        public void Splash() { }
        public void RumbleEffect(byte index, byte data, byte flags) { }
        public float EngineTime() => 0f;
        public void ResetDuckLatched() { }
        public void SetPlayerSurface(int material) { }
    }

    private static MoveState Running() => new()
    {
        EntIndex = Local, FrameTime = 0.015f, Flags = FlOnGround, MoveType = MoveTypeWalk, Velocity = new Vector(250f, 0, 0),
    };

    [Fact]
    public void TheLocalPlayersPredictedStepIsPlayedOnTheCommandsFirstRun()
    {
        var sink = new Sink();
        MoveState move = Running();
        var world = new ClientPredictionMoveWorld(new Ground(), () => new EntityHandle(0x0001_0001u), () => true, new NoSounds(), Steps(sink), () => move);

        world.UpdateStepSound(new Vector(1, 2, 3), move.Velocity, 5);

        var step = Assert.Single(sink.Steps);
        Assert.Equal((Local, "NPC_Citizen.RunFootstepLeft", new Vector(1, 2, 3), 0.5f), step);
        Assert.Equal(StepSoundRules.RunStepMs, move.StepSoundTime);
    }

    [Fact]
    public void ARepredictedCommandPlaysNoStep()
    {
        var sink = new Sink();
        MoveState move = Running();
        var world = new ClientPredictionMoveWorld(new Ground(), () => new EntityHandle(0x0001_0001u), () => false, new NoSounds(), Steps(sink), () => move);

        world.UpdateStepSound(Vector.Zero, move.Velocity, 5);
        world.PlayStepSound(Vector.Zero, 5, 1f, true);

        Assert.Empty(sink.Steps);
    }

    [Fact]
    public void TheLocalPlayersStepsComeEvery300MsOfCommands()
    {
        var sink = new Sink();
        MoveState move = Running();
        var world = new ClientPredictionMoveWorld(new Ground(), () => new EntityHandle(0x0001_0001u), () => true, new NoSounds(), Steps(sink), () => move);

        // 200 commands of 15 ms: 3 s.
        for (int i = 0; i < 200; i++)
            world.UpdateStepSound(Vector.Zero, move.Velocity, 5);

        Assert.InRange(sink.Steps.Count, 10, 11);
    }

    /// <summary>The client module builds the other players' steps and asks them once a frame.</summary>
    [Fact]
    public void TheClientStepsTheOtherPlayersEveryFrame()
    {
        string code = File.ReadAllText(RepoLayout.Repo("src", "client", "managed", "ClientModule.cs"));
        Assert.Matches(@"_otherSteps\s*=\s*_services\.Create<Sounds\.OtherPlayerSteps>\(\)\s*;", code);
        Assert.Matches(@"_otherSteps\?\.Frame\(frameTime\)\s*;", code);
    }
}

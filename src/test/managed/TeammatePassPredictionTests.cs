extern alias client;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

using C = client::SourceSharp.Abi;
using CShared = client::SourceSharp.Shared;
using CClient = client::SourceSharp.Client;
using CStandalone = client::SourceSharp.Client.Standalone;

namespace SourceSharp.Tests;

/// <summary>
/// Plan A.7 / F04: the client's prediction and the server move a player the
/// same way through a teammate and into an enemy. The server's movement runs
/// over a world whose players are hit as its trace filter decides
/// (<see cref="TraceFilterRules"/> asking <see cref="TeammatesPassRule"/>); the
/// client's runs over the prediction's player clip asking
/// <c>ClientTeammatesPassRule</c> from the published answer and the player
/// resource. The same commands from the same start must end in the same place.
/// </summary>
public class TeammatePassPredictionTests
{
    private const int Commands = 40;
    private const int Combine = 2;
    private const int Rebels = 3;
    private const float OtherX = 96f;
    private const int StudioModel = 12;

    private static readonly EntityHandle MoverHandle = new(0x0001_0001u);
    private static readonly EntityHandle OtherHandle = new(0x0002_0002u);

    private static readonly Vector HullMin = new(-16f, -16f, 0f);
    private static readonly Vector HullMax = new(16f, 16f, 72f);

    /// <summary>
    /// A flat floor at z = 0 under an open sky: a hull swept down stops on it
    /// with DIST_EPSILON's gap, as the engine's brush trace does.
    /// </summary>
    private static (float Fraction, bool StartSolid) Floor(float startZ, float endZ, float minsZ)
    {
        float bottom0 = startZ + minsZ;
        float bottom1 = endZ + minsZ;
        if (bottom1 >= 0f)
            return (1f, false);
        if (bottom0 < 0f)
            return (0f, true);
        float f = MathF.Max(0f, bottom0 - 0.03125f) / (bottom0 - bottom1);
        return (f, false);
    }

    private static MoveTrace ServerFloor(in Vector start, in Vector end, in Vector mins)
    {
        (float f, bool solid) = Floor(start.Z, end.Z, mins.Z);
        if (f >= 1f)
            return MoveTrace.Clear(end);
        return new MoveTrace
        {
            Fraction = f, EndPos = start + (end - start) * f, PlaneNormal = new Vector(0f, 0f, 1f), StartSolid = solid,
            AllSolid = solid, Contents = 1, HasEntity = true, HasGround = true, HitWorld = true, SurfaceProps = 3,
        };
    }

    //---------------------------------------------------------------------
    // The server: the floor, and the other player wherever the filter hits it.
    //---------------------------------------------------------------------

    private sealed class Candidates : ITraceCandidates
    {
        public bool TryRead(EntityHandle entity, out TraceCandidate candidate)
        {
            candidate = new TraceCandidate((int)EngineCollisionGroup.Player, SolidType.BoundingBox, StudioModel, EntityHandle.None);
            return entity == MoverHandle || entity == OtherHandle;
        }

        public bool IsWorld(EntityHandle entity) => false;
        public bool IsBrushModel(int modelIndex) => false;
    }

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Flow : IRoundFlow
    {
        public bool RoundOver => false;
        public void Think(float now) { }
        public void ResetRound() { }
        public void OnPlayerSpawned(EntityHandle player, float now) { }
        public void OnPlayerKilled(PlayerDeathArgs death) { }
    }

    private sealed class PassingMode : IGameMode
    {
        public string Id => "passtest";
        public IRoundFlow Rounds { get; } = new Flow();
        public bool TeammatesPass => true;
    }

    private sealed class Selector(string id) : IGameModeSelector
    {
        public string? PickMode() => id;
    }

    private sealed class ServerWorld(TraceFilterRules rules, Vector other) : IMoveWorld
    {
        public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs, uint mask, int collisionGroup)
        {
            MoveTrace best = ServerFloor(start, end, mins);
            if (best.Fraction == 0f)
                return best;

            TraceFilterQuery query = rules.Resolve(MoverHandle, collisionGroup);
            if (!rules.ShouldHitEntity(query, OtherHandle.Value, (int)mask))
                return best;

            // The engine's box clip, the same arithmetic the client's clip ports.
            if (!CClient.PredictionPlayerClip.SweepBox(start, end, mins, maxs, new Vector(other.X - 16f, other.Y - 16f, other.Z),
                                                      new Vector(other.X + 16f, other.Y + 16f, other.Z + 72f), out MoveTrace hit))
                return best;

            if (hit.AllSolid || hit.StartSolid || hit.Fraction < best.Fraction)
            {
                hit.StartSolid = best.StartSolid || hit.StartSolid;
                hit.HasEntity = true;
                hit.HasGround = true;
                hit.HitEntity = OtherHandle;
                best = hit;
            }

            return best;
        }

        public int PointContents(in Vector point, int slot) => 0;
        public void ResetPointContentsCache(bool optimizations) { }
        public MoveSurface Surface(int surfaceProps) => new() { Valid = true, Friction = 0.8f, MaxSpeedFactor = 1f, JumpFactor = 1f };
        public void ResetTouchList() { }
        public void AddToTouched(in MoveTrace trace, in Vector velocity) { }
        public void StartSound(in Vector origin, string sound) { }
        public void PlayerSetAnimation(int activity) { }
        public bool PlayerFallingDamage(float damage) => true;
        public void PlayStepSound(in Vector origin, int surfaceProps, float volume, bool force) { }
        public void UpdateStepSound(in Vector origin, in Vector velocity, int surfaceProps) { }
        public void Splash() { }
        public void RumbleEffect(byte index, byte data, byte flags) { }
        public float EngineTime() => 100f;
        public void ResetDuckLatched() { }
        public void SetPlayerSurface(int material) { }
    }

    //---------------------------------------------------------------------
    // The client: the floor, through the prediction's player clip.
    //---------------------------------------------------------------------

    private sealed class ClientWorld(CClient.PredictionPlayerClip clip, EntityHandle mover) : C.IMoveWorld
    {
        public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs, uint mask, int collisionGroup)
            => clip.Clip(ServerFloor(start, end, mins), mover, start, end, mins, maxs, mask, collisionGroup);

        public int PointContents(in Vector point, int slot) => 0;
        public void ResetPointContentsCache(bool optimizations) { }
        public MoveSurface Surface(int surfaceProps) => new() { Valid = true, Friction = 0.8f, MaxSpeedFactor = 1f, JumpFactor = 1f };
        public void ResetTouchList() { }
        public void AddToTouched(in MoveTrace trace, in Vector velocity) { }
        public void StartSound(in Vector origin, string sound) { }
        public void PlayerSetAnimation(int activity) { }
        public bool PlayerFallingDamage(float damage) => true;
        public void PlayStepSound(in Vector origin, int surfaceProps, float volume, bool force) { }
        public void UpdateStepSound(in Vector origin, in Vector velocity, int surfaceProps) { }
        public void Splash() { }
        public void RumbleEffect(byte index, byte data, byte flags) { }
        public float EngineTime() => 100f;
        public void ResetDuckLatched() { }
        public void SetPlayerSurface(int material) { }
    }

    private sealed class ClientPlayers(params CStandalone.ClientStandaloneEntity[] all) : CClient.IClientStandalones
    {
        public IReadOnlyList<CStandalone.ClientStandaloneEntity> Live() => all;

        public CStandalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => all.FirstOrDefault(e => e.Handle.EntryIndex == entityIndex);
    }

    //---------------------------------------------------------------------
    // The run.
    //---------------------------------------------------------------------

    /// <summary>The server's final x after the walk, with the other player on <paramref name="otherTeam"/>.</summary>
    private static float ServerWalk(int otherTeam)
    {
        var convars = new ConVarRegistry(new NamedConVars(new Dictionary<string, string>()));
        ServiceContainer services = TestServices.Services();
        var mode = new PassingMode();
        services.AddInstance(mode.GetType(), mode);
        services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(new Selector(mode.Id)));
        RecordingEngine engine = TestServices.Fake();
        engine.Returns("Host.ActiveSide", Side.Server);
        engine.WhenCalled("Players.GetTeam",
                          call => engine.Returns("Players.GetTeam", (EntityHandle)call.Args[0]! == MoverHandle ? Combine : otherTeam));
        var modes = new GameModes(new RecordingLog(), engine, services, convars.Declare<SvModeSwitchWarmupConVar>(),
                                  convars.Declare<MpWaitingForPlayersTimeConVar>());
        var rule = new TeammatesPassRule(modes, convars.Declare<MpTeammatesCollideConVar>(),
                                         convars.Declare<MpTeammatesPassConVar>(), engine);
        var rules = TraceFilterRules.Over(new Candidates(), CollisionRules.BuildMatrix(), new ListHooks<IEntityCollideRule>(rule));

        var loop = new GameMovement(new ServerWorld(rules, new Vector(OtherX, 0f, 0f)), MoveConstantDefaults.Stock);
        MoveState s = Start();
        for (int i = 0; i < Commands; i++)
        {
            s.CommandNumber = i + 1;
            Assert.True(loop.ProcessMovement(s));
        }

        return s.Origin.X;
    }

    /// <summary>The client's predicted final x after the same walk.</summary>
    private static float ClientWalk(int otherTeam)
    {
        C.RecordingEngine engine = ClientDoubles.Fake().Returns("EntityRender.IsDormant", false);
        var mover = new CStandalone.ClientSsPlayer(engine) { Handle = new EntityHandle(MoverHandle.Value) };
        mover.State.BindLocal();
        var other = new CStandalone.ClientSsPlayer(engine) { Handle = new EntityHandle(OtherHandle.Value) };
        other.State.BindLocal();
        other.State.Origin = new Vector(OtherX, 0f, 0f);
        other.State.Alive = true;
        var resource = new CStandalone.ClientPlayerResource(engine) { Handle = new EntityHandle(0x0003_0040u) };
        resource.State.BindLocal();
        resource.State.SetRow(1, 0, 0, 100, 1, Combine, true, true, true);
        resource.State.SetRow(2, 0, 0, 100, 2, otherTeam, true, true, true);

        // What the server published: its rule's answer under the passing mode.
        var published = DeclaredClientConVars.Holding<CClient.MpTeammatesPassConVar>("1");
        var standalones = new ClientPlayers(mover, other, resource);
        var clip = new CClient.PredictionPlayerClip(standalones, engine,
            new ClientHooks(new CClient.ClientTeammatesPassRule(published, standalones)), () => 2);

        C.MoveConstants constants = client::SourceSharp.Abi.MoveConstantDefaults.Stock;
        var loop = new C.GameMovement(new ClientWorld(clip, mover.Handle), constants);
        C.MoveState s = ClientStart();
        for (int i = 0; i < Commands; i++)
        {
            s.CommandNumber = i + 1;
            clip.BeginCommand(constants);
            Assert.True(loop.ProcessMovement(s));
        }

        return s.Origin.X;
    }

    private sealed class ClientHooks(params CShared.IEntityCollideRule[] all) : C.IHooks<CShared.IEntityCollideRule>
    {
        public IReadOnlyList<CShared.IEntityCollideRule> All => all;
        public void Each(string what, Action<CShared.IEntityCollideRule> call) { foreach (var r in all) call(r); }
    }

    private static MoveState Start() => new()
    {
        FrameTime = 0.015f, MoveType = GameMovement.MoveTypeWalk, Flags = 1, Origin = Vector.Zero, MaxSpeed = 320f,
        ClientMaxSpeed = 320f, StepSize = 18f, ViewOffset = new Vector(0f, 0f, 64f), CurrentGravity = 600f,
        SurfaceFriction = 1f, Health = 100, IsAlive = true, LaggedMovementValue = 1f, ModelScale = 1f, EntIndex = 1,
        AllowAutoMovement = true, SurfaceDataValid = true, SurfaceProps = 3, IsServer = true, HasGround = true,
        ForwardMove = 320f,
    };

    private static C.MoveState ClientStart() => new()
    {
        FrameTime = 0.015f, MoveType = C.GameMovement.MoveTypeWalk, Flags = 1, Origin = Vector.Zero, MaxSpeed = 320f,
        ClientMaxSpeed = 320f, StepSize = 18f, ViewOffset = new Vector(0f, 0f, 64f), CurrentGravity = 600f,
        SurfaceFriction = 1f, Health = 100, IsAlive = true, LaggedMovementValue = 1f, ModelScale = 1f, EntIndex = 1,
        AllowAutoMovement = true, SurfaceDataValid = true, SurfaceProps = 3, IsServer = false, HasGround = true,
        ForwardMove = 320f,
    };

    [Fact]
    public void PredictedAndServerPositionsAgreeThroughATeammate()
    {
        float server = ServerWalk(Combine);
        float client = ClientWalk(Combine);

        // The stimulus: the walk went through the teammate's box (it starts at 80).
        Assert.True(server > OtherX + 32f, $"the server walked through the teammate (x {server})");
        Assert.Equal(server, client);
    }

    [Fact]
    public void PredictedAndServerPositionsAgreeAgainstAnEnemy()
    {
        float server = ServerWalk(Rebels);
        float client = ClientWalk(Rebels);

        // The stimulus: the enemy stopped the walk short of its box.
        Assert.True(server < OtherX - 32f + 1f && server > OtherX - 48f, $"the server stopped at the enemy (x {server})");
        Assert.Equal(server, client);
    }
}

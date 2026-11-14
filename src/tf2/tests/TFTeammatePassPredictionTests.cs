extern alias client;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Movement;
using TF2.Tests.Rules;

using Xunit;

using C = client::SourceSharp.Abi;
using CClient = client::SourceSharp.Client;
using CShared = client::SourceSharp.Shared;
using CStandalone = client::SourceSharp.Client.Standalone;
using CTF = client::TF2.Shared;

namespace TF2.Tests;

/// <summary>
/// TF2's teammate pass on both sides (tf2 F05 under the prediction's player
/// clip, plan A.7's TF2 half): the server's movement over a world whose
/// players are hit as its trace filter decides (<see cref="TFTeamCollide"/>
/// over the roster), the client's over the prediction's player clip asking
/// <c>TFClientTeamCollide</c> over the player resource's team, each with its
/// own compilation of the mask arm (<see cref="TFMovementRules.PlayerSolidMask"/>).
/// The same commands from the same start end in the same place, through a
/// teammate and against an enemy: a client clip that blocked a teammate the
/// server passes would mispredict at every one.
/// </summary>
public class TFTeammatePassPredictionTests
{
    private const int Commands = 40;
    private const float OtherX = 96f;
    private const int StudioModel = 12;
    private const int PlayerMovement = (int)EngineCollisionGroup.PlayerMovement;

    /// <summary>A team by player index, the server compilation's.</summary>
    private sealed class Teams(TFTeam mover, TFTeam other) : TF2.Shared.Rules.ITFPlayerTeam
    {
        public TFTeam Of(int playerIndex) => playerIndex switch
        {
            1 => mover,
            2 => other,
            _ => TFTeam.Unassigned,
        };
    }

    /// <summary>The same, the client compilation's.</summary>
    private sealed class ClientTeams(CTF.TFTeam mover, CTF.TFTeam other) : CTF.Rules.ITFPlayerTeam
    {
        public CTF.TFTeam Of(int playerIndex) => playerIndex switch
        {
            1 => mover,
            2 => other,
            _ => CTF.TFTeam.Unassigned,
        };
    }

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class ClientHooks(params CShared.IEntityCollideRule[] all) : C.IHooks<CShared.IEntityCollideRule>
    {
        public IReadOnlyList<CShared.IEntityCollideRule> All => all;
        public void Each(string what, Action<CShared.IEntityCollideRule> call) { foreach (var r in all) call(r); }
    }

    /// <summary>A flat floor at z = 0: a hull swept down stops on it with DIST_EPSILON's gap.</summary>
    private static MoveTrace Floor(in Vector start, in Vector end, in Vector mins)
    {
        float bottom0 = start.Z + mins.Z;
        float bottom1 = end.Z + mins.Z;
        if (bottom1 >= 0f)
            return MoveTrace.Clear(end);
        bool solid = bottom0 < 0f;
        float f = solid ? 0f : MathF.Max(0f, bottom0 - 0.03125f) / (bottom0 - bottom1);
        return new MoveTrace
        {
            Fraction = f, EndPos = start + (end - start) * f, PlaneNormal = new Vector(0f, 0f, 1f), StartSolid = solid,
            AllSolid = solid, Contents = 1, HasEntity = true, HasGround = true, HitWorld = true, SurfaceProps = 3,
        };
    }

    private abstract class WorldBase : IMoveWorld
    {
        public abstract MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs, uint mask, int collisionGroup);
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

    private sealed class Candidates(EntityHandle mover, EntityHandle other) : ITraceCandidates
    {
        public bool TryRead(EntityHandle entity, out TraceCandidate candidate)
        {
            candidate = new TraceCandidate((int)EngineCollisionGroup.Player, SolidType.BoundingBox, StudioModel, EntityHandle.None);
            return entity == mover || entity == other;
        }

        public bool IsWorld(EntityHandle entity) => false;
        public bool IsBrushModel(int modelIndex) => false;
    }

    /// <summary>The server: the floor, and the other player wherever the trace filter hits it.</summary>
    private sealed class ServerWorld(TraceFilterRules rules, EntityHandle mover, EntityHandle other, MoveConstants hull)
        : WorldBase
    {
        public override MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs, uint mask, int collisionGroup)
        {
            MoveTrace best = Floor(start, end, mins);
            if (best.Fraction == 0f)
                return best;

            TraceFilterQuery query = rules.Resolve(mover, collisionGroup);
            if (!rules.ShouldHitEntity(query, other.Value, (int)mask))
                return best;

            // The engine's box clip, the same arithmetic the client's clip ports.
            Vector at = new(OtherX, 0f, 0f);
            if (!CClient.PredictionPlayerClip.SweepBox(start, end, mins, maxs, at + hull.HullMin, at + hull.HullMax,
                                                       out MoveTrace hit))
                return best;

            if (hit.AllSolid || hit.StartSolid || hit.Fraction < best.Fraction)
            {
                hit.StartSolid = best.StartSolid || hit.StartSolid;
                hit.HasEntity = true;
                hit.HasGround = true;
                hit.HitEntity = other;
                best = hit;
            }

            return best;
        }
    }

    /// <summary>The client: the floor, through the prediction's player clip.</summary>
    private sealed class ClientWorld(CClient.PredictionPlayerClip clip, EntityHandle mover) : WorldBase, C.IMoveWorld
    {
        public override MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs, uint mask, int collisionGroup)
            => clip.Clip(Floor(start, end, mins), mover, start, end, mins, maxs, mask, collisionGroup);
    }

    private sealed class ClientPlayers(params CStandalone.ClientStandaloneEntity[] all) : CClient.IClientStandalones
    {
        public IReadOnlyList<CStandalone.ClientStandaloneEntity> Live() => all;

        public CStandalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => all.FirstOrDefault(e => e.Handle.EntryIndex == entityIndex);
    }

    private static MoveState Start(bool server) => new()
    {
        FrameTime = 0.015f, MoveType = GameMovement.MoveTypeWalk, Flags = 1, Origin = Vector.Zero, MaxSpeed = 320f,
        ClientMaxSpeed = 320f, StepSize = 18f, ViewOffset = new Vector(0f, 0f, 64f), CurrentGravity = 600f,
        SurfaceFriction = 1f, Health = 100, IsAlive = true, LaggedMovementValue = 1f, ModelScale = 1f, EntIndex = 1,
        AllowAutoMovement = true, SurfaceDataValid = true, SurfaceProps = 3, IsServer = server, HasGround = true,
        ForwardMove = 320f,
    };

    /// <summary>The server's final x after the walk, the mover red and the other player on <paramref name="otherTeam"/>.</summary>
    private static float ServerWalk(TFTeam otherTeam)
    {
        var convars = TFMovementFixtures.ConVars();
        convars.RegisterTypes([typeof(TFAvoidTeammatesConVar)]);
        var roster = new FakeRoster(new FakeClock());
        EntityHandle mover = roster.Add(TFTeam.Red);
        EntityHandle other = roster.Add(otherTeam);
        Assert.Equal((1, 2), (mover.EntryIndex, other.EntryIndex));

        var rule = new TFTeamCollide(convars.Declared<TFAvoidTeammatesConVar>(), roster);
        var filter = TraceFilterRules.Over(new Candidates(mover, other), CollisionRules.BuildMatrix(),
                                           new ListHooks<IEntityCollideRule>(rule));

        var players = new TFMovementFixtures.Players();
        players.Add(1, TFClass.Soldier);
        TFMovementRules arms = TFMovementFixtures.Rules(players, convars, teams: new Teams(TFTeam.Red, otherTeam));

        MoveConstants constants = MoveConstantDefaults.Stock;
        var loop = new GameMovement(new ServerWorld(filter, mover, other, constants), constants)
        {
            Rules = MovementRules.Stock with { SolidMask = arms.PlayerSolidMask },
        };
        MoveState s = Start(server: true);
        for (int i = 0; i < Commands; i++)
        {
            s.CommandNumber = i + 1;
            Assert.True(loop.ProcessMovement(s));
        }

        return s.Origin.X;
    }

    /// <summary>The client's predicted final x after the same walk.</summary>
    private static float ClientWalk(CTF.TFTeam otherTeam)
    {
        var engine = new C.RecordingEngine().Returns("EntityRender.IsDormant", false);
        var mover = new CStandalone.ClientSsPlayer(engine) { Handle = new EntityHandle(0x0001_0001u) };
        mover.State.BindLocal();
        var other = new CStandalone.ClientSsPlayer(engine) { Handle = new EntityHandle(0x0001_0002u) };
        other.State.BindLocal();
        other.State.Origin = new Vector(OtherX, 0f, 0f);
        other.State.Alive = true;

        var convars = TFMovementFixtures.ClientConVars();
        convars.RegisterTypes([typeof(CTF.TFAvoidTeammatesConVar)]);
        var teams = new ClientTeams(CTF.TFTeam.Red, otherTeam);
        var clip = new CClient.PredictionPlayerClip(new ClientPlayers(mover, other), engine,
            new ClientHooks(new client::TF2.Client.Player.TFClientTeamCollide(
                convars.Declared<CTF.TFAvoidTeammatesConVar>(), teams)), () => 2);

        var players = new TFMovementFixtures.ClientPlayers();
        players.Add(1, TFClass.Soldier);
        var arms = TFMovementFixtures.ClientServices(players, convars, teams).Create<CTF.Movement.TFMovementRules>();

        C.MoveConstants constants = C.MoveConstantDefaults.Stock;
        var loop = new C.GameMovement(new ClientWorld(clip, mover.Handle), constants)
        {
            Rules = C.MovementRules.Stock with { SolidMask = arms.PlayerSolidMask },
        };
        C.MoveState s = ClientStart();
        for (int i = 0; i < Commands; i++)
        {
            s.CommandNumber = i + 1;
            clip.BeginCommand(constants);
            Assert.True(loop.ProcessMovement(s));
        }

        return s.Origin.X;
    }

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
        float server = ServerWalk(TFTeam.Red);
        float client = ClientWalk(CTF.TFTeam.Red);

        // The stimulus: the walk went through the teammate's box (it starts at 80).
        Assert.True(server > OtherX + 32f, $"the server walked through the teammate (x {server})");
        Assert.Equal(server, client);
    }

    [Fact]
    public void PredictedAndServerPositionsAgreeAgainstAnEnemy()
    {
        float server = ServerWalk(TFTeam.Blue);
        float client = ClientWalk(CTF.TFTeam.Blue);

        // The stimulus: the enemy stopped the walk short of its box.
        Assert.True(server < OtherX - 32f + 1f && server > OtherX - 48f, $"the server stopped at the enemy (x {server})");
        Assert.Equal(server, client);
    }
}

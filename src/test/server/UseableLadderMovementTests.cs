using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// CHL2GameMovement's useable ladders (hl_gamemovement.cpp), in the managed
/// loop: mounting by +USE and by walking in, the forced slide on and off, the
/// climb along the ladder's axis, and the ways off.
///
/// <para>
/// Every expected number is worked from the C++ by hand and written as a
/// literal (the derivation beside it). One ladder throughout, unless a fact
/// says otherwise: from (0,32,0) to (0,32,128), so its axis is straight up and
/// a player facing +Y (yaw 90) faces it from the -Y side. Frame time is 1/16
/// so a 200 u/s climb is 12.5 units a command.
/// </para>
/// </summary>
public sealed class UseableLadderMovementTests
{
    private const int InJump = 1 << 1;
    private const int InForward = 1 << 3;
    private const int InUse = 1 << 5;

    private const int LadderIndex = 7;

    private static readonly Vector Bottom = new(0f, 32f, 0f);
    private static readonly Vector Top = new(0f, 32f, 128f);

    /// <summary>The ladders a fact climbs, and what the loop told them.</summary>
    private sealed class Ladders : ILadderWorld
    {
        public List<UseableLadder> All { get; } = new();
        public Dictionary<int, List<Vector>> DismountsOf { get; } = new();
        public List<string> Told { get; } = new();
        public LadderConVars ConVars { get; set; } = new(AutoDismount: true, AutoMountDot: 0.4f, UseOnly: false);

        public int Count => All.Count;
        public UseableLadder At(int index) => All[index];

        public bool TryGet(int entityIndex, out UseableLadder ladder)
        {
            foreach (UseableLadder l in All)
            {
                if (l.Index == entityIndex)
                {
                    ladder = l;
                    return true;
                }
            }

            ladder = default;
            return false;
        }

        public IReadOnlyList<Vector> Dismounts(int ladderIndex)
            => DismountsOf.TryGetValue(ladderIndex, out var list) ? list : new List<Vector>();

        public void PlayerGotOn(int ladderIndex, int playerIndex) => Told.Add($"on {ladderIndex} by {playerIndex}");
        public void PlayerGotOff(int ladderIndex, int playerIndex) => Told.Add($"off {ladderIndex} by {playerIndex}");

        public static Ladders One(bool enabled = true, bool fake = false)
        {
            var world = new Ladders();
            world.All.Add(new UseableLadder(LadderIndex, Top, Bottom, enabled, fake));
            return world;
        }
    }

    /// <summary>A world whose traces go the whole way unless a fact says otherwise.</summary>
    private sealed class OpenWorld : IMoveWorld
    {
        public Func<Vector, Vector, Vector, Vector, uint, int, MoveTrace> OnTrace { get; set; }
            = static (_, end, _, _, _, _) => MoveTrace.Clear(end);

        public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs,
                               uint mask, int collisionGroup)
            => OnTrace(start, end, mins, maxs, mask, collisionGroup);

        public int PointContents(in Vector point, int slot) => 0;
        public void ResetPointContentsCache(bool optimizations) { }

        public MoveSurface Surface(int surfaceProps) => new()
        {
            Valid = true, Friction = 0.8f, Material = 'C', Climbable = 0,
            MaxSpeedFactor = 1f, JumpFactor = 1f,
        };

        public void ResetTouchList() { }
        public void AddToTouched(in MoveTrace trace, in Vector velocity) { }
        public void StartSound(in Vector origin, string sound) { }
        public void PlayerSetAnimation(int activity) { }
        public bool PlayerFallingDamage(float damage) => true;
        public void PlayStepSound(in Vector o, int p, float v, bool f) { }
        public void UpdateStepSound(in Vector o, in Vector v, int p) { }
        public void Splash() { }
        public void RumbleEffect(byte i, byte d, byte f) { }
        public float EngineTime() => 100f;
        public void ResetDuckLatched() { }
        public void SetPlayerSurface(int material) { }
    }

    /// <summary>A standing player at the origin facing +Y, the ladder 32 units ahead.</summary>
    private static MoveState Standing() => new()
    {
        CommandNumber = 1,
        FrameTime = 0.0625f,
        MoveType = GameMovement.MoveTypeWalk,
        Flags = GameMovement.FlOnGround,
        HasGround = true,
        Origin = Vector.Zero,
        ViewAngles = new Vector(0f, 90f, 0f),
        MaxSpeed = 320f,
        ClientMaxSpeed = 320f,
        StepSize = 18f,
        ViewOffset = new Vector(0f, 0f, 64f),
        CurrentGravity = 600f,
        SurfaceFriction = 1f,
        Health = 100,
        IsAlive = true,
        LaggedMovementValue = 1f,
        ModelScale = 1f,
        EntIndex = 1,
        AllowAutoMovement = true,
        SurfaceDataValid = true,
        SurfaceProps = 3,
        IsServer = true,
    };

    /// <summary>A player already on the ladder at height <paramref name="z"/>, at rest.</summary>
    private static MoveState OnTheLadder(float z)
    {
        MoveState s = Standing();
        s.MoveType = GameMovement.MoveTypeLadder;
        s.Flags = 0;
        s.HasGround = false;
        s.Origin = new Vector(0f, 32f, z);
        s.Ladder = LadderIndex;
        return s;
    }

    private static GameMovement Loop(Ladders ladders, OpenWorld? world = null)
        => new(world ?? new OpenWorld(), MoveConstantDefaults.Stock) { Ladders = ladders };

    //-------------------------------------------------------------------------
    // Mounting.
    //-------------------------------------------------------------------------

    /// <summary>
    /// +USE facing a ladder within 64 starts the forced mount (LadderMove ->
    /// LookingAtLadder -> CheckLadderAutoMount -> StartForcedMove). The goal is
    /// Findladder's closest point, (0,32,0); the transit is 32 / MaxSpeed 320 =
    /// 0.1 s from curtime; the move type is NONE for the slide; the ladder word
    /// is set and the ladder is told once.
    /// </summary>
    [Fact]
    public void UseFacingALadderStartsTheForcedMount()
    {
        var ladders = Ladders.One();
        MoveState s = Standing();
        s.Buttons = InUse;

        Loop(ladders).ProcessMovement(s);

        Assert.True(s.LadderForceMove);
        Assert.True(s.LadderForceMount);
        Assert.Equal(Bottom, s.LadderGoalPosition);
        Assert.Equal(Vector.Zero, s.LadderStartPosition);
        Assert.Equal(0f, s.LadderStartTime);
        Assert.Equal(0.1f, s.LadderArrivalTime);
        Assert.Equal(GameMovement.MoveTypeNone, s.MoveType);
        Assert.Equal(LadderIndex, s.Ladder);
        Assert.Equal(LadderIndex, s.LadderForceLadder);
        Assert.Equal(new[] { "on 7 by 1" }, ladders.Told);
    }

    /// <summary>
    /// Base CGameMovement's brush ladder is not HL2MP's: OnLadder answers from
    /// the player's ladder, so a trace into CONTENTS_LADDER while walking at it
    /// leaves the player walking.
    /// </summary>
    [Fact]
    public void ABrushLadderIsNotClimbable()
    {
        var world = new OpenWorld
        {
            OnTrace = static (start, end, _, _, _, _) => end.Y > start.Y
                ? new MoveTrace
                  {
                      Fraction = 0.5f, EndPos = start, PlaneNormal = new Vector(0f, -1f, 0f),
                      Contents = GameMovement.ContentsLadder, HasEntity = true, HitWorld = true,
                  }
                : MoveTrace.Clear(end),
        };
        MoveState s = Standing();
        s.ForwardMove = 320f;
        s.Buttons = InForward;

        Loop(new Ladders(), world).ProcessMovement(s);

        Assert.Equal(GameMovement.MoveTypeWalk, s.MoveType);
        Assert.Equal(MoveState.NoLadder, s.Ladder);
    }

    /// <summary>
    /// Walking at a ladder mounts it without +USE: CheckLadderAutoMountCone.
    /// From (0,8,0) the closest point is 24 away (under 32), dead ahead (0
    /// degrees, under 15), not strafing.
    /// </summary>
    [Fact]
    public void WalkingIntoALadderMountsIt()
    {
        var ladders = Ladders.One();
        MoveState s = Standing();
        s.Origin = new Vector(0f, 8f, 0f);
        s.ForwardMove = 320f;
        s.Buttons = InForward;

        Loop(ladders).ProcessMovement(s);

        Assert.True(s.LadderForceMount);
        Assert.Equal(Bottom, s.LadderGoalPosition);
        Assert.Equal(new[] { "on 7 by 1" }, ladders.Told);
    }

    /// <summary>
    /// Tracker 21776: strafing at it does not (|sidemove| 1 or more), and the
    /// end-point check is out of reach too (24^2 = 576 over 16^2).
    /// </summary>
    [Fact]
    public void StrafingIntoALadderDoesNotMountIt()
    {
        var ladders = Ladders.One();
        MoveState s = Standing();
        s.Origin = new Vector(0f, 8f, 0f);
        s.ForwardMove = 320f;
        s.SideMove = 50f;
        s.Buttons = InForward;

        Loop(ladders).ProcessMovement(s);

        Assert.False(s.LadderForceMove);
        Assert.Empty(ladders.Told);
    }

    /// <summary>sv_ladder_useonly 1: walking in no longer mounts.</summary>
    [Fact]
    public void UseOnlyRefusesTheWalkInMount()
    {
        var ladders = Ladders.One();
        ladders.ConVars = ladders.ConVars with { UseOnly = true };
        MoveState s = Standing();
        s.Origin = new Vector(0f, 8f, 0f);
        s.ForwardMove = 320f;
        s.Buttons = InForward;

        Loop(ladders).ProcessMovement(s);

        Assert.False(s.LadderForceMove);
    }

    /// <summary>A disabled ladder is skipped by Findladder.</summary>
    [Fact]
    public void ADisabledLadderIsNotFound()
    {
        var ladders = Ladders.One(enabled: false);
        MoveState s = Standing();
        s.Buttons = InUse;

        Loop(ladders).ProcessMovement(s);

        Assert.False(s.LadderForceMove);
        Assert.Empty(ladders.Told);
    }

    /// <summary>
    /// SF_LADDER_DONTGETON: the ladder hears PlayerGotOn and the player is not
    /// moved onto it (StartForcedMove returns after the notification).
    /// </summary>
    [Fact]
    public void AFakeLadderIsToldButNotMounted()
    {
        var ladders = Ladders.One(fake: true);
        MoveState s = Standing();
        s.Buttons = InUse;

        Loop(ladders).ProcessMovement(s);

        Assert.False(s.LadderForceMove);
        Assert.Equal(MoveState.NoLadder, s.Ladder);
        Assert.Equal(new[] { "on 7 by 1" }, ladders.Told);
    }

    /// <summary>
    /// The server's reserved spot: a goal the hull does not fit at, nor one
    /// unit above, starts no forced move.
    /// </summary>
    [Fact]
    public void TheServerRefusesABlockedGoal()
    {
        var ladders = Ladders.One();
        var world = new OpenWorld
        {
            OnTrace = static (start, end, _, _, _, _) => start.Equals(end)
                ? new MoveTrace { Fraction = 0f, EndPos = start, StartSolid = true, HasEntity = true }
                : MoveTrace.Clear(end),
        };
        MoveState s = Standing();
        s.Buttons = InUse;

        Loop(ladders, world).ProcessMovement(s);

        Assert.False(s.LadderForceMove);
    }

    /// <summary>The client has no reservation and does not ask (#if !CLIENT_DLL).</summary>
    [Fact]
    public void TheClientDoesNotAskWhetherTheGoalIsBlocked()
    {
        var ladders = Ladders.One();
        var world = new OpenWorld
        {
            OnTrace = static (start, end, _, _, _, _) => start.Equals(end)
                ? new MoveTrace { Fraction = 0f, EndPos = start, StartSolid = true, HasEntity = true }
                : MoveTrace.Clear(end),
        };
        MoveState s = Standing();
        s.Buttons = InUse;
        s.IsServer = false;

        Loop(ladders, world).ProcessMovement(s);

        Assert.True(s.LadderForceMove);
    }

    //-------------------------------------------------------------------------
    // The slide.
    //-------------------------------------------------------------------------

    private static MoveState Sliding(float curTime)
    {
        MoveState s = Standing();
        s.MoveType = GameMovement.MoveTypeNone;
        s.Ladder = LadderIndex;
        s.LadderForceMove = true;
        s.LadderForceMount = true;
        s.LadderForceLadder = LadderIndex;
        s.LadderStartPosition = Vector.Zero;
        s.LadderGoalPosition = Bottom;
        s.LadderStartTime = 0f;
        s.LadderArrivalTime = 0.1f;
        s.CurTime = curTime;
        return s;
    }

    /// <summary>
    /// Half way through the transit the origin is half way: frac = 0.05 / 0.1
    /// = 0.5 exactly (0.05f is 0.1f halved), so start + 0.5 * (0,32,0).
    /// </summary>
    [Fact]
    public void TheForcedMoveIsLinearInTime()
    {
        MoveState s = Sliding(0.05f);

        Loop(Ladders.One()).ProcessMovement(s);

        Assert.Equal(new Vector(0f, 16f, 0f), s.Origin);
        Assert.True(s.LadderForceMove);
        Assert.Equal(GameMovement.MoveTypeNone, s.MoveType);
    }

    /// <summary>
    /// Past the arrival time the slide ends at the goal, on the ladder, at rest.
    /// </summary>
    [Fact]
    public void AForcedMountEndsOnTheLadder()
    {
        MoveState s = Sliding(0.2f);
        s.Velocity = new Vector(5f, 5f, 5f);

        Loop(Ladders.One()).ProcessMovement(s);

        Assert.Equal(Bottom, s.Origin);
        Assert.False(s.LadderForceMove);
        Assert.Equal(GameMovement.MoveTypeLadder, s.MoveType);
        Assert.Equal(LadderIndex, s.Ladder);
        Assert.Equal(Vector.Zero, s.Velocity);
    }

    //-------------------------------------------------------------------------
    // The climb.
    //-------------------------------------------------------------------------

    /// <summary>
    /// +FORWARD looking up climbs the ladder's axis at MAX_CLIMB_SPEED: the
    /// velocity is 200 * ladderUp, and TryPlayerMove carries the player 200 /
    /// 16 = 12.5 up the open world.
    /// </summary>
    [Fact]
    public void ForwardLookingUpClimbsAtTwoHundred()
    {
        MoveState s = OnTheLadder(64f);
        s.ViewAngles = new Vector(-45f, 90f, 0f);
        s.ForwardMove = 320f;
        s.Buttons = InForward;

        Loop(Ladders.One()).ProcessMovement(s);

        Assert.Equal(GameMovement.MoveTypeLadder, s.MoveType);
        Assert.Equal(0f, s.Velocity.X);
        Assert.Equal(0f, s.Velocity.Y);
        Assert.Equal(200f, s.Velocity.Z, 3);
        Assert.Equal(76.5f, s.Origin.Z, 3);
    }

    /// <summary>
    /// Looking down while pressing forward climbs DOWN: velocity.z is under 0
    /// and -ladderUp . velocity = 0.707 is over the 0.3 changeover, so the
    /// factor is -1.
    /// </summary>
    [Fact]
    public void ForwardLookingDownClimbsDown()
    {
        MoveState s = OnTheLadder(64f);
        s.ViewAngles = new Vector(45f, 90f, 0f);
        s.ForwardMove = 320f;
        s.Buttons = InForward;

        Loop(Ladders.One()).ProcessMovement(s);

        Assert.Equal(-200f, s.Velocity.Z, 3);
        Assert.Equal(51.5f, s.Origin.Z, 3);
    }

    /// <summary>
    /// On the ladder with no key held the player hangs still, and the ladder
    /// zeroes the player's gravity (SetGravity( 0.0f )).
    /// </summary>
    [Fact]
    public void NoKeysHangsStillWithGravityZeroed()
    {
        MoveState s = OnTheLadder(64f);
        s.Velocity = new Vector(1f, 2f, 3f);
        s.EntityGravity = 0.5f;

        Loop(Ladders.One()).ProcessMovement(s);

        Assert.Equal(Vector.Zero, s.Velocity);
        Assert.Equal(new Vector(0f, 32f, 64f), s.Origin);
        Assert.Equal(0f, s.EntityGravity);
    }

    /// <summary>
    /// The ladder holds a player who would climb past its end without +USE:
    /// from 127, 12.5 up is 139.5, newt = -1472/16384 is under -1/128 and
    /// below oldt, and the move is undone.
    /// </summary>
    [Fact]
    public void ClimbingPastTheTopIsUndone()
    {
        MoveState s = OnTheLadder(127f);
        s.ViewAngles = new Vector(-45f, 90f, 0f);
        s.ForwardMove = 320f;
        s.Buttons = InForward;

        Loop(Ladders.One()).ProcessMovement(s);

        Assert.Equal(127f, s.Origin.Z);
        Assert.Equal(GameMovement.MoveTypeLadder, s.MoveType);
    }

    //-------------------------------------------------------------------------
    // Getting off.
    //-------------------------------------------------------------------------

    /// <summary>
    /// +JUMP lets go and pushes off along the view at 200, plus 50 up when not
    /// looking down; the ladder hears the player get off.
    /// </summary>
    [Fact]
    public void JumpPushesOffAlongTheView()
    {
        var ladders = Ladders.One();
        MoveState s = OnTheLadder(64f);
        s.Buttons = InJump;

        Loop(ladders).ProcessMovement(s);

        Assert.Equal(GameMovement.MoveTypeWalk, s.MoveType);
        Assert.Equal(MoveState.NoLadder, s.Ladder);
        Assert.Equal(200f, s.Velocity.Y, 3);
        Assert.InRange(s.Velocity.Z, 0f, 50f);   // 50 up, less the walk move's half gravity
        Assert.Equal(new[] { "off 7 by 1" }, ladders.Told);
    }

    /// <summary>
    /// +USE mid-span with no dismount point near lets go with
    /// USE_DISMOUNT_SPEED along the view and 50 up: forward is (~0,1,0), so
    /// (~0,100,50).
    /// </summary>
    [Fact]
    public void UseMidSpanLetsGoWithAPush()
    {
        var ladders = Ladders.One();
        MoveState s = OnTheLadder(64f);
        s.Buttons = InUse;

        Loop(ladders).ProcessMovement(s);

        Assert.Equal(GameMovement.MoveTypeWalk, s.MoveType);
        Assert.Equal(MoveState.NoLadder, s.Ladder);
        Assert.Equal(0f, s.Velocity.X, 3);
        Assert.Equal(100f, s.Velocity.Y);
        Assert.Equal(50f, s.Velocity.Z);
        Assert.Equal(new[] { "off 7 by 1" }, ladders.Told);
    }

    /// <summary>
    /// +USE near the top with a dismount point ahead slides the player onto it:
    /// at (0,32,120) the top is 8 away (inside 16), the point (0,64,128) is
    /// 1088 squared away (inside 100^2) and dead ahead, so the goal is that
    /// point one unit up, the transit sqrt(32^2 + 9^2) / 320 = 0.1038798 s.
    /// </summary>
    [Fact]
    public void UseNearTheTopDismountsOntoThePoint()
    {
        var ladders = Ladders.One();
        ladders.DismountsOf[LadderIndex] = new List<Vector> { new(0f, 64f, 128f) };
        MoveState s = OnTheLadder(120f);
        s.Buttons = InUse;

        Loop(ladders).ProcessMovement(s);

        Assert.True(s.LadderForceMove);
        Assert.False(s.LadderForceMount);
        Assert.Equal(new Vector(0f, 64f, 129f), s.LadderGoalPosition);
        Assert.Equal(0.1038798f, s.LadderArrivalTime, 6);
        Assert.Equal(GameMovement.MoveTypeNone, s.MoveType);
        Assert.Equal(MoveState.NoLadder, s.Ladder);
        Assert.Equal(new[] { "off 7 by 1" }, ladders.Told);
    }

    /// <summary>
    /// Of two dismount points the one most dead ahead wins, not the nearest:
    /// the sorted list is walked whole and the best dot is kept.
    /// </summary>
    [Fact]
    public void TheDismountMostAheadWins()
    {
        var ladders = Ladders.One();
        ladders.DismountsOf[LadderIndex] = new List<Vector> { new(20f, 50f, 128f), new(0f, 70f, 128f) };
        MoveState s = OnTheLadder(120f);
        s.Buttons = InUse;

        Loop(ladders).ProcessMovement(s);

        Assert.Equal(new Vector(0f, 70f, 129f), s.LadderGoalPosition);
    }

    /// <summary>
    /// sv_autoladderdismount: climbing to within a tick's climb (200 * 0.015
    /// = 3) of the top while pressing a key across the ladder takes the
    /// dismount without +USE. From 118, 12.5 up is 130.5, 2.5 from the top.
    /// </summary>
    [Fact]
    public void ReachingTheTopDismountsByItself()
    {
        var ladders = Ladders.One();
        ladders.DismountsOf[LadderIndex] = new List<Vector> { new(0f, 64f, 128f) };
        MoveState s = OnTheLadder(118f);
        s.ForwardMove = 320f;
        s.Buttons = InForward;

        Loop(ladders).ProcessMovement(s);

        Assert.True(s.LadderForceMove);
        Assert.Equal(new Vector(0f, 64f, 129f), s.LadderGoalPosition);
    }

    /// <summary>With sv_autoladderdismount 0 the same climb is held at the top instead.</summary>
    [Fact]
    public void WithoutAutoDismountTheTopHolds()
    {
        var ladders = Ladders.One();
        ladders.ConVars = ladders.ConVars with { AutoDismount = false };
        ladders.DismountsOf[LadderIndex] = new List<Vector> { new(0f, 64f, 128f) };
        MoveState s = OnTheLadder(118f);
        s.ForwardMove = 320f;
        s.Buttons = InForward;

        Loop(ladders).ProcessMovement(s);

        Assert.False(s.LadderForceMove);
        Assert.Equal(118f, s.Origin.Z);
    }

    /// <summary>
    /// A push (base velocity over 1 squared) knocks the player off: ExitLadder,
    /// which fires no output.
    /// </summary>
    [Fact]
    public void BaseVelocityKnocksThePlayerOff()
    {
        var ladders = Ladders.One();
        MoveState s = OnTheLadder(64f);
        s.BaseVelocity = new Vector(2f, 0f, 0f);

        Loop(ladders).ProcessMovement(s);

        Assert.NotEqual(GameMovement.MoveTypeLadder, s.MoveType);
        Assert.Equal(MoveState.NoLadder, s.Ladder);
        Assert.Empty(ladders.Told);
    }

    //-------------------------------------------------------------------------
    // CHL2GameMovement's one other override that changes a number here.
    //-------------------------------------------------------------------------

    /// <summary>
    /// CHL2GameMovement::CanAccelerate answers true always (it calls the base
    /// and discards the answer), so in HL2MP a player whose deadflag is set
    /// still accelerates on the ground: 10 * (1/16) * 320 = 200 along the view.
    /// </summary>
    [Fact]
    public void HL2MPLetsADeadPlayerAccelerate()
    {
        MoveState s = Standing();
        s.DeadFlag = true;
        s.ForwardMove = 320f;
        s.Buttons = InForward;

        Loop(new Ladders()).ProcessMovement(s);

        Assert.True(s.Velocity.Y > 100f, $"a dead HL2MP player accelerates ({s.Velocity.Y})");
    }

    /// <summary>Base CGameMovement's CanAccelerate refuses a dead player, and a loop with no ladders is base.</summary>
    [Fact]
    public void BaseMovementHoldsADeadPlayerStill()
    {
        MoveState s = Standing();
        s.DeadFlag = true;
        s.ForwardMove = 320f;
        s.Buttons = InForward;

        new GameMovement(new OpenWorld(), MoveConstantDefaults.Stock).ProcessMovement(s);

        Assert.Equal(0f, s.Velocity.Y);
    }

    /// <summary>Noclip lets go of the ladder, and the ladder hears it.</summary>
    [Fact]
    public void NoclipLetsGo()
    {
        var ladders = Ladders.One();
        MoveState s = OnTheLadder(64f);
        s.MoveType = GameMovement.MoveTypeNoClip;

        Loop(ladders).ProcessMovement(s);

        Assert.Equal(MoveState.NoLadder, s.Ladder);
        Assert.Equal(new[] { "off 7 by 1" }, ladders.Told);
    }
}

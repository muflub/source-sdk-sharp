using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's command stages in managed (plan_server_native_v2 T4g), over an
/// unattached side: every engine call answers its refusal, so what is measured
/// is the managed half alone -- the record, the shared words, the ground
/// write-back, the tick base and the publish. The rules are
/// ServerCommandRulesTests'; the side reached is ServerCommandSideTests'.
/// </summary>
public sealed unsafe class ServerPlayerCommandsTests : IDisposable
{
    private static readonly EntityHandle Player = new(0x0003_0002u);
    private static readonly EntityHandle Lift = new(0x0004_0040u);
    private static readonly EntityHandle Crate = new(0x0005_0041u);

    private sealed class Env : IEngineEnvironment
    {
        public CGlobalVarsRaw* Raw;
        public nint GetInterface(string name) => 0;
        public Globals Globals => new(Raw);
    }

    private sealed class Published : IPlayerMovePublisher
    {
        public readonly List<(EntityHandle Player, int TickBase)> Calls = new();
        public void Publish(EntityHandle player, ServerPlayerMove move) => Calls.Add((player, move.TickBase));
    }

    private sealed class NoSounds : IMoveSoundSink
    {
        public void MoveSound(int entIndex, Vector origin, int channel, string sample, float volume, int soundLevel,
                              int flags, int pitch) { }

        public readonly List<string> Scripts = [];

        public void MoveSoundScript(int entIndex, Vector origin, string soundName) => Scripts.Add(soundName);
    }

    private readonly NoSounds _sounds = new();

    private readonly CGlobalVarsRaw* _globals = (CGlobalVarsRaw*)NativeMemory.AllocZeroed((nuint)sizeof(CGlobalVarsRaw));
    private readonly ServerModuleEngine _engine = new(TestSides.Create(Side.Server));
    private readonly ServerPlayerMoves _moves = new();
    private readonly ServerMoveData _data = new();
    private readonly Published _published = new();
    private readonly ServerPlayerCommands _commands;

    public ServerPlayerCommandsTests()
    {
        _globals->interval_per_tick = 0.015f;
        _globals->frametime = 0.015f;
        _globals->maxClients = 2;
        _commands = ServerPlayerCommands.For(_engine, EngineBound.Trace(_engine.Api), EngineBound.SurfaceProps(_engine.Api), _moves, _data, _sounds, null,
                                             TestSides.Movement(_engine), Frame, _published,
                                             new Env { Raw = _globals });
    }

    public void Dispose() => NativeMemory.Free(_globals);

    private static MoveFrameConstants Frame()
    {
        MovementParams p = Movement.Current;
        var c = new MoveConstants
        {
            MaxVelocity = 3500f, MaxSpeed = 320f, Friction = 4f, StopSpeed = 100f, Accelerate = 10f,
            AirAccelerate = 10f, AirSpeedCap = 30f, DuckSpeedScale = 0.33333333f, Optimizations = true,
            MaxClients = 2, TickInterval = 0.015f, View = p.View, HullMin = p.HullMin, HullMax = p.HullMax,
            DuckHullMin = p.DuckHullMin, DuckHullMax = p.DuckHullMax, DuckView = p.DuckView,
            ObsHullMin = p.ObsHullMin, ObsHullMax = p.ObsHullMax, DeadViewHeight = p.DeadViewHeight,
        };
        return new MoveFrameConstants(c, 21f, 0f, 1f, 600f);
    }

    private int Move(in UserCmd cmd) => _commands.Stage(Player, 0, 1, in cmd);

    private sealed class Impacts : IPlayerCommandImpacts
    {
        public readonly List<(EntityHandle Player, bool Finished)> Calls = new();
        public Func<bool>? Finished;
        public void ProcessImpacts(EntityHandle player) => Calls.Add((player, Finished?.Invoke() ?? false));
    }

    [Fact]
    public void EachMoveProcessesTheImpactsOnceAfterTheLoopRan()
    {
        // Stock's RunCommand: ProcessMovement, FinishMove, ProcessImpacts --
        // once per command, after the move (the loop's count says it ran).
        var impacts = new Impacts();
        impacts.Finished = () => _commands.Ran == 1;
        _commands.Impacts = impacts;
        var cmd = new UserCmd { CommandNumber = 1 };

        Move(in cmd);

        Assert.Equal([(Player, true)], impacts.Calls);
    }

    [Fact]
    public void AMoveRunsTheLoop()
    {
        var cmd = new UserCmd { CommandNumber = 1 };

        Assert.Equal(1, Move(in cmd));

        Assert.Equal(1, _commands.Ran);
    }

    [Fact]
    public void TheOldButtonsAreTheCommandsAfterTheMove()
    {
        // FinishMove stores the command's buttons, not the loop's: a jump the
        // loop latched into its old buttons is the command's next time.
        var cmd = new UserCmd { CommandNumber = 1, Buttons = 0x22 };

        Move(in cmd);

        Assert.Equal(0x22, _moves.For(Player).Move.OldButtons);
    }

    [Fact]
    public void ThePreviouslyPredictedOriginIsWhereTheMoveLeftThem()
    {
        ServerPlayerMove m = _moves.For(Player);
        m.Move.Origin = new Vector(5f, 6f, 7f);
        var cmd = new UserCmd { CommandNumber = 1 };

        Move(in cmd);

        Assert.True(m.PreviouslyPredictedOrigin.Equals(m.Move.Origin),
                    $"previously predicted {m.PreviouslyPredictedOrigin}, origin {m.Move.Origin}");
    }

    [Fact]
    public void TheSharedMoveWordsGoInAndComeBackOut()
    {
        // The global CMoveData's outputs and the mover's latches outlive a
        // command and a player: what one command leaves, the next reads.
        // The step height comes in (nothing here steps) and the wish velocity
        // goes out (PlayerMove zeroes it).
        _data.OutStepHeight = 3.5f;
        _data.OutWishVel = new Vector(1f, 1f, 1f);
        var cmd = new UserCmd { CommandNumber = 1 };

        Move(in cmd);

        MoveState s = _moves.For(Player).Move;
        Assert.True(s.OutStepHeight == 3.5f && _data.OutStepHeight == 3.5f
                    && s.OutWishVel.Equals(Vector.Zero) && _data.OutWishVel.Equals(Vector.Zero),
                    $"step height {s.OutStepHeight}/{_data.OutStepHeight}, wish {s.OutWishVel}/{_data.OutWishVel}");
    }

    [Fact]
    public void TheTickBaseStepsAfterTheCommandAndIsPublished()
    {
        ServerPlayerMove m = _moves.For(Player);
        m.TickBase = 41;
        var cmd = new UserCmd { CommandNumber = 1 };

        Assert.Equal(1, _commands.Stage(Player, 0, 2, in cmd));

        Assert.True(m.TickBase == 42 && _published.Calls.Count == 1 && _published.Calls[0] == (Player, 42),
                    $"tick base {m.TickBase}, published {string.Join(",", _published.Calls)}");
    }

    [Fact]
    public void ANoClockFrameDoesNotStepTheTickBase()
    {
        _globals->frametime = 0f;
        ServerPlayerMove m = _moves.For(Player);
        m.TickBase = 41;
        var cmd = new UserCmd { CommandNumber = 1 };

        _commands.Stage(Player, 0, 2, in cmd);

        Assert.Equal(41, m.TickBase);
    }

    [Fact]
    public void TheBatchPlacesTheTickBaseAsSinglePlayerWhenNeverPlaced()
    {
        _globals->tickcount = 1000;
        _globals->simTicksThisFrame = 1;

        _commands.AdjustTickBase(Player, 3);

        Assert.Equal(1000 - 3 + 1, _moves.For(Player).TickBase);
    }

    [Fact]
    public void APrepareClearsTheLastCommandsTouchesFirst()
    {
        // Stock's PlayerRunCommand clears m_touchedPhysObject as its first
        // line; the touch list is cleared before anything else is asked, so
        // even a prepare that finds no player leaves none behind.
        ServerPlayerMove m = _moves.For(Player);
        m.RecordTouch(Crate);
        var cmd = new UserCmd { CommandNumber = 1 };

        _commands.Stage(Player, 0, 0, in cmd);

        Assert.True(m.Touched == 0 && m.TouchedGround == 0, $"touched {m.Touched}");
    }

    [Fact]
    public void APrepareWithNoPlayerRefuses()
    {
        var cmd = new UserCmd { CommandNumber = 1 };
        Assert.Equal(0, _commands.Stage(Player, 0, 0, in cmd));
    }

    //-------------------------------------------------------------------------
    // The ground write-back (movement_ground_writeback.h's rule, MvSetGroundEntity).
    //-------------------------------------------------------------------------

    private static (ServerPlayerMove M, MoveState S) Grounded(EntityHandle ground, bool loopFound)
    {
        var m = new ServerPlayerMove { Ground = ground };
        m.Move.HasGround = loopFound;
        m.Move.Flags = ground != EntityHandle.None ? GameMovement.FlOnGround : 0;
        return (m, m.Move);
    }

    [Fact]
    public void NoGroundFoundClearsAHeldGround()
    {
        var (m, s) = Grounded(Lift, loopFound: false);

        ServerPlayerCommands.WriteBackGround(m, s, held: true, lastGround: Lift);

        Assert.True(m.Ground == EntityHandle.None && (s.Flags & GameMovement.FlOnGround) == 0, $"{m.Ground}");
    }

    [Fact]
    public void GroundFoundAgainIsLeft()
    {
        var (m, s) = Grounded(Lift, loopFound: true);

        ServerPlayerCommands.WriteBackGround(m, s, held: true, lastGround: Lift);

        Assert.True(m.Ground == Lift && (s.Flags & GameMovement.FlOnGround) != 0, $"{m.Ground}");
    }

    [Fact]
    public void OtherGroundFoundIsSet()
    {
        // The lift the player was carried up onto, from the crate below it.
        var (m, s) = Grounded(Crate, loopFound: true);

        ServerPlayerCommands.WriteBackGround(m, s, held: true, lastGround: Lift);

        Assert.True(m.Ground == Lift && (s.Flags & GameMovement.FlOnGround) != 0, $"{m.Ground}");
    }

    [Fact]
    public void GroundFoundWithNoneHeldIsSet()
    {
        var (m, s) = Grounded(EntityHandle.None, loopFound: true);

        ServerPlayerCommands.WriteBackGround(m, s, held: false, lastGround: Crate);

        Assert.True(m.Ground == Crate && (s.Flags & GameMovement.FlOnGround) != 0, $"{m.Ground}");
    }

    [Fact]
    public void GroundFoundWithNoTraceEverNamingOneIsSetToNothing()
    {
        // MvSetGroundEntity( NULL ): the flag goes with the handle.
        var (m, s) = Grounded(Crate, loopFound: true);
        s.Flags = GameMovement.FlOnGround;

        ServerPlayerCommands.WriteBackGround(m, s, held: true, lastGround: EntityHandle.None);

        Assert.True(m.Ground == EntityHandle.None && (s.Flags & GameMovement.FlOnGround) == 0, $"{m.Ground}");
    }

    [Fact]
    public void NoGroundFoundAndNoneHeldIsLeft()
    {
        // Left, flags and all: the loop's own FL_ONGROUND is not touched.
        var (m, s) = Grounded(EntityHandle.None, loopFound: false);
        s.Flags = GameMovement.FlOnGround | 1 << 3;

        ServerPlayerCommands.WriteBackGround(m, s, held: false, lastGround: Crate);

        Assert.True(m.Ground == EntityHandle.None && s.Flags == (GameMovement.FlOnGround | 1 << 3), $"{m.Ground} {s.Flags}");
    }

    [Fact]
    public void GroundFoundWithNoneHeldAndNoneEverNamedIsLeft()
    {
        // The pointer compare NULL == NULL: nothing to set, so nothing changes.
        var (m, s) = Grounded(EntityHandle.None, loopFound: true);
        s.Flags = GameMovement.FlOnGround;

        ServerPlayerCommands.WriteBackGround(m, s, held: false, lastGround: EntityHandle.None);

        Assert.True(m.Ground == EntityHandle.None && s.Flags == GameMovement.FlOnGround, $"{m.Ground} {s.Flags}");
    }

    [Fact]
    public void AHeldGroundThatNoLongerResolvesIsSetAgain()
    {
        // The handle the record holds names nothing now (MvGetGroundEntity()
        // NULL), so it is not "the one found" even when the handles agree.
        var (m, s) = Grounded(Crate, loopFound: true);
        s.Flags = 0;

        ServerPlayerCommands.WriteBackGround(m, s, held: false, lastGround: Crate);

        Assert.True(m.Ground == Crate && (s.Flags & GameMovement.FlOnGround) != 0, $"{m.Ground} {s.Flags}");
    }

    //-------------------------------------------------------------------------
    // The record.
    //-------------------------------------------------------------------------

    [Fact]
    public void AFreshRecordHasTheNativeConstructorsValues()
    {
        MoveState s = new ServerPlayerMove().Move;

        Assert.True(s.StepSize == 18f && s.AllowAutoMovement && s.ViewOffset.Equals(new Vector(0f, 0f, 64f))
                    && s.EntityGravity == 1f && s.SurfaceFriction == 1f && s.MoveType == GameMovement.MoveTypeWalk
                    && s.Flags == 0 && s.LaggedMovementValue == 1f && s.IsServer,
                    "the constructor's step height, auto movement, eye, gravity, friction and walk");
    }

    [Fact]
    public void ASpawnIsAWalkingClientWithTheHullsEye()
    {
        var m = new ServerPlayerMove();
        m.Move.MoveType = GameMovement.MoveTypeNoClip;

        m.Spawn(new Vector(0f, 0f, 70f));

        Assert.True((m.Move.Flags & ServerCommandRules.FlClient) != 0 && m.Move.MoveType == GameMovement.MoveTypeWalk
                    && m.Move.ViewOffset.Equals(new Vector(0f, 0f, 70f)), "FL_CLIENT, MOVETYPE_WALK, the view");
    }

    [Fact]
    public void ATouchOfTheHeldGroundIsMarked()
    {
        var m = new ServerPlayerMove { Ground = Lift };

        m.RecordTouch(Crate);
        m.RecordTouch(Lift);

        Assert.True(m.Touched == 2 && m.TouchedGround == 0b10 && m.TouchedEntities[1] == Lift.Value,
                    $"touched {m.Touched}, ground bits {m.TouchedGround}");
    }

    [Fact]
    public void TouchesPastTheListAreCountedNotListed()
    {
        var m = new ServerPlayerMove();
        for (int i = 0; i < PlayerShadowFacts.TouchMax + 3; i++)
            m.RecordTouch(new EntityHandle((uint)(0x0001_0100 + i)));

        Assert.True(m.Touched == PlayerShadowFacts.TouchMax + 3, $"{m.Touched}");
    }

    [Fact]
    public void APushAtANonPlayerIsRefusedAndSaidOnce()
    {
        // shell_worldquery.cpp's CSaidOncePerId, carried over: a push at
        // something with no base-velocity channel says so once per entity, not
        // once per tick of the belt or the push field (plan_server_native_v2 T4g).
        var route = ServerPlayerMovement.For(_engine, _moves, _commands);

        bool landed = route.AddBaseVelocity(Crate, new Vector(0f, 0f, 100f));

        Assert.True(!landed && !route.FirstRefusal(Crate) && route.FirstRefusal(Lift),
                    "the refused push was not recorded once for its entity alone");
    }

    /// <summary>SuitPower_SetCharge on the route: the player's record holds the charge.</summary>
    [Fact]
    public void TheRouteSetsTheRecordsSuitPower()
    {
        var route = ServerPlayerMovement.For(_engine, _moves, _commands);

        route.SetSuitPower(Player, 100f);

        Assert.Equal(100f, _moves.For(Player).Move.SuitPower);
    }

    /// <summary>
    /// CHL2_Player::Spawn's SuitPower_SetCharge( 100 ): every spawn charges the
    /// suit full through the player movement the module holds.
    /// </summary>
    [Fact]
    public void ASpawnChargesTheSuitFull()
    {
        string code = File.ReadAllText(RepoLayout.Repo("src", "server", "managed", "ServerModule.cs"));
        int at = code.IndexOf("public void OnPlayerSpawned(EntityHandle player)", StringComparison.Ordinal);
        Assert.True(at >= 0, "the module handles a spawn");
        string body = code[at..code.IndexOf("\n    }\n", at, StringComparison.Ordinal)];

        Assert.Matches(@"_services\.Require<IEnginePlayerMovement>\(\)\.SetSuitPower\(\s*player\s*,\s*100f\s*\)\s*;", body);
    }

    [Fact]
    public void ARefusalIsSaidOncePerEntity()
    {
        var route = ServerPlayerMovement.For(_engine, _moves, _commands);

        Assert.Equal((true, false, true),
                     (route.FirstRefusal(Crate), route.FirstRefusal(Crate), route.FirstRefusal(Lift)));
    }

    /// <summary>
    /// The server's entities area with <see cref="Player"/> a player and
    /// nothing else: the default area answers no for everything, so a route
    /// over it never reaches a record.
    /// </summary>
    private sealed class OnePlayer : DefaultEntitiesArea, IEntitiesServerArea
    {
        private readonly DefaultEntitiesServerArea _rest = new();

        public int EntityIsPlayer(uint a0) => a0 == Player.Value ? 1 : 0;
        public void EntityGetClassName(uint a0, byte* a1, int a2) => _rest.EntityGetClassName(a0, a1, a2);
        public void EntityGetName(uint a0, byte* a1, int a2) => _rest.EntityGetName(a0, a1, a2);
        public int EntityGetState(uint a0, int* a1) => _rest.EntityGetState(a0, a1);
        public uint FindEntityByClassname(uint a0, byte* a1) => _rest.FindEntityByClassname(a0, a1);
        public int EntitiesInSphere(float* a0, float a1, uint* a2, int a3) => _rest.EntitiesInSphere(a0, a1, a2, a3);
        public int EntitySetKeyValue(uint a0, byte* a1, byte* a2) => _rest.EntitySetKeyValue(a0, a1, a2);
        public void EntityGetSpawnOrigin(uint a0, float* a1) => _rest.EntityGetSpawnOrigin(a0, a1);
        public void EntityGetSpawnAngles(uint a0, float* a1) => _rest.EntityGetSpawnAngles(a0, a1);
        public int EntityGetSpawnFlags(uint a0) => _rest.EntityGetSpawnFlags(a0);
        public void EntitySetSpawnFlags(uint a0, int a1) => _rest.EntitySetSpawnFlags(a0, a1);
        public int EntityGetHealth(uint a0) => _rest.EntityGetHealth(a0);
        public void EntitySetHealth(uint a0, int a1) => _rest.EntitySetHealth(a0, a1);
        public void EntitySetVisible(uint a0, int a1) => _rest.EntitySetVisible(a0, a1);
        public void EntitySetOwner(uint a0, uint a1) => _rest.EntitySetOwner(a0, a1);
        public void EntitySetClassName(uint a0, byte* a1) => _rest.EntitySetClassName(a0, a1);
        public int EntitiesStandalonePlayerSkyboxArea(int a0, int* a1) => _rest.EntitiesStandalonePlayerSkyboxArea(a0, a1);
    }

    private ServerPlayerMovement PlayerRoute() => new(_engine.Api, _engine.Log, new OnePlayer(), _moves, _commands);

    /// <summary>A player standing on <see cref="Lift"/>, moving at <paramref name="velocity"/>.</summary>
    private ServerPlayerMove Standing(Vector velocity)
    {
        ServerPlayerMove m = _moves.For(Player);
        m.Ground = Lift;
        m.Move.Flags |= GameMovement.FlOnGround;
        m.Move.Velocity = velocity;
        return m;
    }

    [Fact]
    public void AnImpulseAddsToAPlayersRecordedVelocity()
    {
        // Stock's ApplyAbsVelocityImpulse on a MOVETYPE_WALK entity adds to
        // its abs velocity; a player's is its record's (T4g). The shadow, where
        // the impulse used to go, is overwritten by the next move.
        ServerPlayerMove m = Standing(new Vector(10f, 0f, 0f));

        bool taken = PlayerRoute().ApplyVelocityImpulse(Player, new Vector(0f, 5f, 200f), clearGround: false);

        Assert.True(taken && m.Move.Velocity.Equals(new Vector(10f, 5f, 200f)),
                    $"taken {taken}, velocity {m.Move.Velocity}");
    }

    [Fact]
    public void AnImpulseThatClearsTheGroundLetsGoOfIt()
    {
        // SetGroundEntity( NULL ): the ground and FL_ONGROUND go. (No unstick:
        // that is AddBaseVelocity's, and CTriggerPush's push-once arm has none.)
        ServerPlayerMove m = Standing(Vector.Zero);

        PlayerRoute().ApplyVelocityImpulse(Player, new Vector(0f, 0f, 200f), clearGround: true);

        Assert.True(m.Ground == EntityHandle.None && (m.Move.Flags & GameMovement.FlOnGround) == 0,
                    $"ground {m.Ground}, flags {m.Move.Flags}");
    }

    [Fact]
    public void AnImpulseThatKeepsTheGroundKeepsIt()
    {
        ServerPlayerMove m = Standing(Vector.Zero);

        PlayerRoute().ApplyVelocityImpulse(Player, new Vector(0f, 0f, 200f), clearGround: false);

        Assert.True(m.Ground == Lift && (m.Move.Flags & GameMovement.FlOnGround) != 0,
                    $"ground {m.Ground}, flags {m.Move.Flags}");
    }

    [Fact]
    public void AnImpulseAtANonPlayerIsNotTheRoutes()
        => Assert.False(PlayerRoute().ApplyVelocityImpulse(Crate, new Vector(0f, 0f, 200f), clearGround: true));

    [Fact]
    public void AnAbsurdImpulseIsDiscarded()
    {
        // CheckEntityVelocity's -1: past a hundred times k_flMaxEntitySpeed
        // the impulse is dropped, not clamped.
        ServerPlayerMove m = Standing(new Vector(1f, 2f, 3f));

        bool taken = PlayerRoute().ApplyVelocityImpulse(Player, new Vector(0f, 0f, EntityPhysicsMotion.MaxEntitySpeed * 200f),
                                                        clearGround: false);

        Assert.True(taken && m.Move.Velocity.Equals(new Vector(1f, 2f, 3f)), $"taken {taken}, velocity {m.Move.Velocity}");
    }

    [Fact]
    public void AFastImpulseIsClampedToTheMaximum()
    {
        ServerPlayerMove m = Standing(Vector.Zero);

        PlayerRoute().ApplyVelocityImpulse(Player, new Vector(0f, 0f, EntityPhysicsMotion.MaxEntitySpeed * 2f),
                                           clearGround: false);

        Assert.True(MathF.Abs(m.Move.Velocity.Z - EntityPhysicsMotion.MaxEntitySpeed) < 0.01f, $"{m.Move.Velocity}");
    }

    [Fact]
    public void ARecordIsSeededTheFirstTimeOnly()
    {
        int seeded = 0;
        _moves.Seed = (_, _) => { seeded++; return true; };

        _moves.For(Player);
        _moves.For(Player);

        Assert.Equal(1, seeded);
    }

    private sealed class CountedLadders : ILadderWorld
    {
        public int Asked;
        public int Count { get { Asked++; return 0; } }
        public UseableLadder At(int index) => throw new ArgumentOutOfRangeException(nameof(index));
        public bool TryGet(int entityIndex, out UseableLadder ladder) { ladder = default; return false; }
        public IReadOnlyList<Vector> Dismounts(int ladderIndex) => Array.Empty<Vector>();
        public LadderConVars ConVars => new(true, 0.4f, false);
        public void PlayerGotOn(int ladderIndex, int playerIndex) { }
        public void PlayerGotOff(int ladderIndex, int playerIndex) { }
    }

    /// <summary>
    /// The ladders the module hands the command loop are what its movement
    /// climbs: a walking command looks for a ladder (Findladder) in them.
    /// </summary>
    [Fact]
    public void TheLoopLooksForLaddersInTheOnesItIsHanded()
    {
        var ladders = new CountedLadders();
        _commands.Ladders = ladders;
        _moves.For(Player).Move.MoveType = GameMovement.MoveTypeWalk;
        var cmd = new UserCmd { CommandNumber = 1 };

        Assert.Equal(1, Move(in cmd));

        Assert.True(ladders.Asked > 0, "the command's movement asked the ladders it was handed");
    }

    private sealed class SprintingPlayer : IHl2PlayerMove
    {
        public float WalkSpeed => 150f;
        public float NormSpeed => 190f;
        public float SprintSpeed => 320f;
        public bool InfiniteAuxPower => false;
        public bool CanSprint(bool ducked, bool ducking, int waterLevel, bool sprintDisabled) => true;
    }

    /// <summary>
    /// The HL2 player the module hands the command loop is the one it runs,
    /// with the suit on: a +speed command with power sprints.
    /// </summary>
    [Fact]
    public void TheLoopRunsTheHl2PlayerItIsHanded()
    {
        _commands.Hl2Player = new SprintingPlayer();
        MoveState s = _moves.For(Player).Move;
        s.MoveType = GameMovement.MoveTypeWalk;
        s.SuitPower = 100f;
        s.Buttons = GameMovement.InSpeed;   // stage 0's copy of the command's
        var cmd = new UserCmd { CommandNumber = 1, Buttons = GameMovement.InSpeed };

        Assert.Equal(1, Move(in cmd));

        Assert.True(s.SuitEquipped, "the loop put the suit on");
        Assert.True(s.NewSprinting, "the command sprinted");
        // CMoveHelperServer::StartSound: the sound the loop named, not the swim's.
        Assert.Equal(["HL2Player.SprintStart"], _sounds.Scripts);
    }

    [Fact]
    public void ALoopHandedNoHl2PlayerRunsNoSuit()
    {
        MoveState s = _moves.For(Player).Move;
        s.MoveType = GameMovement.MoveTypeWalk;
        s.SuitPower = 100f;
        s.Buttons = GameMovement.InSpeed;
        var cmd = new UserCmd { CommandNumber = 1, Buttons = GameMovement.InSpeed };

        Assert.Equal(1, Move(in cmd));

        Assert.False(s.SuitEquipped || s.NewSprinting, "no suit, no sprint");
    }
}

/// <summary>
/// The route's rules, which were native headers (standalone_basevelocity.h,
/// view_effect_reach.h, with their gtests' cases), and the publish.
/// </summary>
public sealed class ServerPlayerMovementRulesTests
{
    [Fact]
    public void AnUnconsumedBaseVelocityAccumulates()
        => Assert.True(ServerPlayerMovement.BaseVelocityPush(ServerCommandRules.FlBaseVelocity, new Vector(1f, 2f, 3f),
                                                             new Vector(10f, 0f, 0f)).Equals(new Vector(11f, 2f, 3f)));

    [Fact]
    public void AConsumedBaseVelocityIsReplaced()
        => Assert.True(ServerPlayerMovement.BaseVelocityPush(0, new Vector(1f, 2f, 3f),
                                                             new Vector(10f, 0f, 0f)).Equals(new Vector(10f, 0f, 0f)));

    [Theory]
    [InlineData(300f, -100f, 200f, true)]
    [InlineData(100f, -300f, -200f, false)]
    public void TheSumDecidesTheRelease(float unconsumedZ, float pushZ, float sumZ, bool releases)
    {
        // standalone_basevelocity_test's TheSumDecidesTheRelease: a downward
        // push on a larger upward one still lifts, and the other way not.
        Vector sum = ServerPlayerMovement.BaseVelocityPush(ServerCommandRules.FlBaseVelocity,
                                                           new Vector(0f, 0f, unconsumedZ), new Vector(0f, 0f, pushZ));

        Assert.Equal((sumZ, releases), (sum.Z, ServerPlayerMovement.ReleasesGround(sum.Z, onGround: true)));
    }

    [Theory]
    [InlineData(5f, true, true)]
    [InlineData(5f, false, false)]
    [InlineData(0f, true, false)]
    [InlineData(-5f, true, false)]
    public void OnlyAnUpwardPushReleasesAGroundedPlayer(float z, bool onGround, bool releases)
        => Assert.Equal(releases, ServerPlayerMovement.ReleasesGround(z, onGround));

    [Theory]
    [InlineData(500f, 0f, 1f)]
    [InlineData(500f, -1f, 1f)]
    [InlineData(0f, 100f, 1f)]
    [InlineData(25f, 100f, 0.75f)]
    [InlineData(100f, 100f, 0f)]
    [InlineData(101f, 100f, -1f)]
    public void APunchFallsOffInsideItsRadius(float distance, float radius, float scale)
        => Assert.Equal(scale, ServerPlayerMovement.PunchScale(distance, radius));

    [Fact]
    public void OutOfRangeIsTheOnlyNegativeStrength()
        => Assert.True(!ServerPlayerMovement.InRange(ServerPlayerMovement.OutOfRange)
                       && ServerPlayerMovement.InRange(0f) && ServerPlayerMovement.InRange(1f));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void APunchReachesTheGroundedOrAnAirPunchesAnyone(bool onGround, bool inAir, bool reaches)
        => Assert.Equal(reaches, ServerPlayerMovement.PunchReachesPlayer(onGround, inAir));

    [Theory]
    [InlineData(0x43, 0x43)]
    [InlineData(0xC3, -61)]
    [InlineData(0x100, null)]
    [InlineData(0, null)]
    public void TheSurfaceChangeIsTheSignedMaterialAndNothingInTheAir(int material, int? reaches)
        => Assert.Equal(reaches, ServerMoveWorld.SurfaceChange(material, new EntityHandle(0x0001_0002u)));

    [Fact]
    public void ASurfaceChangeForNoPlayerReachesNobody()
        => Assert.Null(ServerMoveWorld.SurfaceChange(0x43, EntityHandle.None));

    [Fact]
    public void TheGroundWireIsTheEntryIndexOrNone()
        => Assert.True(PlayerMovePublisher.GroundWire(EntityHandle.None) == PlayerMovePublisher.GroundWireNone
                       && PlayerMovePublisher.GroundWire(new EntityHandle(0x0007_0123u)) == 0x123
                       && PlayerMovePublisher.GroundWire(new EntityHandle(0x0007_0900u)) == PlayerMovePublisher.GroundWireNone,
                       "none for no ground or an index past the wire's eleven bits, the entry index otherwise");

    private static ServerPlayerMove Moved()
    {
        var m = new ServerPlayerMove { TickBase = 77, Ground = new EntityHandle(0x0002_0009u) };
        MoveState s = m.Move;
        s.Flags = 0x101;
        s.FallVelocity = 12.5f;
        s.DuckTime = 1f;
        s.DuckJumpTime = 2f;
        s.JumpTime = 3f;
        s.WaterJumpTime = 4f;
        s.SurfaceFriction = 0.5f;
        s.OldButtons = 0x22;
        s.Ducked = true;
        s.Ducking = true;
        s.InDuckJump = true;
        s.WaterLevel = 2;
        s.WaterType = 1;
        s.MoveType = GameMovement.MoveTypeLadder;
        s.Velocity = new Vector(1f, 2f, 3f);
        s.BaseVelocity = new Vector(4f, 5f, 6f);
        s.ViewOffset = new Vector(0f, 0f, 28f);
        s.PunchAngle = new Vector(1f, 0f, 0f);
        s.PunchAngleVel = new Vector(0f, 2f, 0f);
        s.LadderNormal = new Vector(0f, 1f, 0f);
        s.WaterJumpVel = new Vector(0f, 0f, 9f);
        s.Ladder = 40;
        s.LadderForceMove = true;
        s.LadderForceMount = true;
        s.LadderStartTime = 5f;
        s.LadderArrivalTime = 5.25f;
        s.LadderGoalPosition = new Vector(1f, 2f, 3f);
        s.LadderStartPosition = new Vector(4f, 5f, 6f);
        s.LadderForceLadder = 41;
        s.SuitPower = 62.5f;
        s.SuitPowerLoad = 25f;
        s.SuitDevices = GameMovement.SuitDeviceSprint;
        s.SuitDevicesOffTime = 7.25f;
        s.NewSprinting = true;
        return m;
    }

    /// <summary>
    /// HL2's ladder words (m_hLadder and LadderMove_t) are networked, as stock
    /// sends them in the HL2 local data, so the client's replay starts from them.
    /// </summary>
    [Fact]
    public void ThePublishWritesTheLadderWords()
    {
        var state = new PlayerNetState();
        state.BindLocal();

        PlayerMovePublisher.Write(state, Moved());

        Assert.Equal(40, state.Ladder);
        Assert.True(state.LadderForceMove);
        Assert.True(state.LadderForceMount);
        Assert.Equal(5f, state.LadderStartTime);
        Assert.Equal(5.25f, state.LadderArrivalTime);
        Assert.Equal(new Vector(1f, 2f, 3f), state.LadderGoalPosition);
        Assert.Equal(new Vector(4f, 5f, 6f), state.LadderStartPosition);
        Assert.Equal(41, state.LadderForceLadder);
    }

    /// <summary>
    /// HL2's suit words (m_HL2Local's power, load, devices and quiet time,
    /// and m_bNewSprinting) are networked, so the client's replay starts from them.
    /// </summary>
    [Fact]
    public void ThePublishWritesTheSuitWords()
    {
        var state = new PlayerNetState();
        state.BindLocal();

        PlayerMovePublisher.Write(state, Moved());

        Assert.Equal((62.5f, 25f, GameMovement.SuitDeviceSprint, 7.25f, true),
                     (state.SuitPower, state.SuitPowerLoad, state.SuitDevices, state.SuitDevicesOffTime, state.NewSprinting));
    }

    [Fact]
    public void ThePublishWritesEveryMovementWord()
    {
        var state = new PlayerNetState();
        state.BindLocal();

        PlayerMovePublisher.Write(state, Moved());

        Assert.True(state.TickBase == 77 && state.GroundEntity == 9 && state.Flags == 0x101u && state.MaxSpeed == 320f
                    && state.FallVelocity == 12.5f && state.DuckTime == 1f && state.DuckJumpTime == 2f
                    && state.JumpTime == 3f && state.StepSize == 18f && state.WaterJumpTime == 4f
                    && state.SurfaceFriction == 0.5f && state.OldForwardMove == 0f && state.OldButtons == 0x22
                    && state.Ducked && state.Ducking && state.InDuckJump && state.AllowAutoMovement
                    && state.WaterLevel == 2 && state.WaterType == 1 && state.MoveType == GameMovement.MoveTypeLadder
                    && state.Velocity.Equals(new Vector(1f, 2f, 3f)) && state.BaseVelocity.Equals(new Vector(4f, 5f, 6f))
                    && state.ViewOffset.Equals(new Vector(0f, 0f, 28f))
                    && state.PunchAngle.AsVector().Equals(new Vector(1f, 0f, 0f))
                    && state.PunchAngleVel.AsVector().Equals(new Vector(0f, 2f, 0f))
                    && state.LadderNormal.Equals(new Vector(0f, 1f, 0f))
                    && state.WaterJumpVel.Equals(new Vector(0f, 0f, 9f)),
                    "SourceSharpPlayerSync_Write's twenty-seven movement words");
    }

    [Fact]
    public void ThePublishLeavesTheEngineSidesWordsAlone()
    {
        var state = new PlayerNetState();
        state.BindLocal();
        state.Health = 55;
        state.Alive = true;
        state.Origin = new Vector(9f, 9f, 9f);

        PlayerMovePublisher.Write(state, Moved());

        Assert.True(state.Health == 55 && state.Alive && state.Origin.Equals(new Vector(9f, 9f, 9f)),
                    "health, alive and origin are native's (PlayerSyncEngineSide)");
    }

    [Fact]
    public void APublishedStateSeedsTheSameRecordBack()
    {
        var state = new PlayerNetState();
        state.BindLocal();
        ServerPlayerMove published = Moved();
        PlayerMovePublisher.Write(state, published);

        var seeded = new ServerPlayerMove();
        Assert.True(PlayerMovePublisher.Seed(state, seeded), "a published state seeds");

        // The wire carries the ground's index, not its serial, so the seed
        // leaves the ground for the next command's traces to name.
        Assert.Equal(EntityHandle.None, seeded.Ground);
        seeded.Ground = published.Ground;

        var again = new PlayerNetState();
        again.BindLocal();
        PlayerMovePublisher.Write(again, seeded);
        Assert.Equal(state.Snapshot(), again.Snapshot());
    }

    [Fact]
    public void AStateNeverPublishedSeedsNothing()
    {
        var state = new PlayerNetState();
        state.BindLocal();

        Assert.False(PlayerMovePublisher.Seed(state, new ServerPlayerMove()));
    }
}

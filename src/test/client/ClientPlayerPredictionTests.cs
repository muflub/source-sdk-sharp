using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// THE CLIENT'S REPLAY (plan_entity.md step 6f), without a game under it.
///
/// <para>
/// What is held still here is the FRAME: which commands run, what opens and
/// closes around each of them, what the ring keeps, what the compare says, and
/// what native is told to draw. The arithmetic inside the loop is step 3e's and
/// is checked bit-for-bit against a recording of the C++ it replaces
/// (MovementTests, Fixtures/movement-processmovement-stock.tsv) — repeating any
/// of it here would be a second, weaker copy of a stronger test.
/// </para>
///
/// <para>
/// The replay engine is a DOUBLE, and that is the point of it being an
/// interface: <c>BeginCommand</c> and <c>EndCommand</c> are a bracket that arms
/// step 3e's four trace slots, and "the bracket is balanced" is a property no
/// live gate can see and every one of these facts can.
/// </para>
/// </summary>
public class ClientPlayerPredictionTests
{
    static ClientPlayerPredictionTests()
    {
        StandaloneNetStates.Discover(typeof(PlayerNetState).Assembly, new RecordingLog());
    }

    private const int LocalPlayer = 1;
    private const uint LocalHandle = 0x0100_0001u;

    //-------------------------------------------------------------------------
    // The doubles.
    //-------------------------------------------------------------------------

    private sealed class Standalones : IClientStandalones
    {
        public readonly List<ClientStandaloneEntity> Entities = new();
        public IReadOnlyList<ClientStandaloneEntity> Live() => Entities;
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    /// <summary>
    /// A replay engine that answers from a script and remembers what it was
    /// asked, so the bracket can be checked for balance.
    /// </summary>
    private sealed class Replay : IClientReplayEngine
    {
        /// <summary>Command numbers the input ring holds. Anything else is "not there".</summary>
        public readonly HashSet<int> Ring = new();

        public readonly List<string> Calls = new();

        /// <summary>Command numbers BeginCommand was handed, in order.</summary>
        public readonly List<int> Seeded = new();

        /// <summary>What EndCommand answers as the last standable ground.</summary>
        public int GroundIndex = -1;

        /// <summary>Whether BeginCommand opens at all. False is a refusal.</summary>
        public bool Opens = true;

        public int Opened, Closed, Ran, Published;
        public bool LastDriving;
        public int LastHealth = int.MinValue;
        public Vector LastOrigin, LastViewOffset;

        public float TickInterval = 0.015f;

        public float ForwardMove;
        public int Buttons;

        public bool TryGetUserCmd(int commandNumber, out UserCmd cmd)
        {
            cmd = default;

            if (!Ring.Contains(commandNumber))
                return false;

            cmd.CommandNumber = commandNumber;
            cmd.TickCount = commandNumber;
            cmd.ForwardMove = ForwardMove;
            cmd.Buttons = Buttons;
            return true;
        }

        public bool BeginCommand(EntityHandle player, int commandNumber,
                                 out MoveFrameConstants constants)
        {
            Calls.Add($"begin:{commandNumber}");
            Seeded.Add(commandNumber);

            constants = new MoveFrameConstants(
                new MoveConstants
                {
                    TickInterval = TickInterval,
                    MaxSpeed = 320f,
                    Friction = 4f,
                    StopSpeed = 75f,
                    Accelerate = 10f,
                    AirAccelerate = 10f,
                    MaxVelocity = 3500f,
                    Optimizations = true,
                    MaxClients = 2,
                },
                JumpHeight: 21f, EngineTime: 1f, ModelScale: 1f, CurrentGravity: 600f);

            if (!Opens)
                return false;

            Opened++;
            return true;
        }

        public int EndCommand(bool ran)
        {
            Calls.Add($"end:{(ran ? 1 : 0)}");
            Closed++;

            if (ran)
                Ran++;

            return GroundIndex;
        }

        public void PublishView(EntityHandle player, in Vector origin, in Vector viewOffset,
                                in QAngle punch, in QAngle punchVel, bool driving, int health)
        {
            Calls.Add($"publish:{(driving ? 1 : 0)}");
            Published++;
            LastDriving = driving;
            LastOrigin = origin;
            LastViewOffset = viewOffset;
            LastHealth = health;
        }
    }

    //-------------------------------------------------------------------------
    // The fixture.
    //-------------------------------------------------------------------------

    private sealed class Bench
    {
        public RecordingEngine Engine = null!;
        public Standalones Standalones = null!;
        public Replay Replay = null!;
        public ClientSsPlayer Player = null!;
        public ClientPlayerPrediction Prediction = null!;
        public MovementRunner Runner = null!;
        public RecordingLog Log = null!;

        /// <summary>Marks a packet as having decoded into the player's state.</summary>
        public void Packet() => Player.OnDataChanged(default, DataUpdateType.DataTableChanged);
    }

    private static Bench Build(params int[] ringCommands) => Build(null, null, new ListHooks(), ringCommands);

    private static Bench Build(ILadderWorld? ladders, params int[] ringCommands)
        => Build(ladders, null, new ListHooks(), ringCommands);

    private static Bench Build(ILadderWorld? ladders, IHl2PlayerMove? hl2Player, params int[] ringCommands)
        => Build(ladders, hl2Player, new ListHooks(), ringCommands);

    private static Bench Build(IHooks<IClientPredictedState> predicted, params int[] ringCommands)
        => Build(null, null, predicted, ringCommands);

    private static Bench Build(ILadderWorld? ladders, IHl2PlayerMove? hl2Player,
                               IHooks<IClientPredictedState> predicted, params int[] ringCommands)
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var player = new ClientSsPlayer(engine) { Handle = new EntityHandle(LocalHandle) };
        player.State.BindLocal();

        // The animation policy is 6d's and needs a model; these facts are about
        // the replay. Off leaves the decode counting, which is what the compare
        // is gated on.
        player.Enabled = false;

        // A player standing on the world with a plausible eye height, which is
        // what a decoded player looks like on the frame after it spawns.
        player.State.Origin = new Vector(64f, 128f, 16f);
        player.State.ViewOffset = new Vector(0f, 0f, 64f);
        player.State.MaxSpeed = 320f;
        player.State.StepSize = 18f;
        player.State.Alive = true;
        player.State.MoveType = 2;   // MOVETYPE_WALK
        player.State.GroundEntity = ClientPlayerPrediction.GroundWireNone;

        var standalones = new Standalones();
        standalones.Entities.Add(player);

        var replay = new Replay();
        var runner = TestSides.Movement(new ClientModuleEngine(TestSides.Create(Side.Client)));

        foreach (int command in ringCommands)
            replay.Ring.Add(command);

        var log = new RecordingLog();

        return new Bench
        {
            Log = log,
            Engine = engine,
            Standalones = standalones,
            Replay = replay,
            Player = player,
            Runner = runner,
            Prediction = new ClientPlayerPrediction(log, standalones, engine, replay,
                                                    runner,
                                                    null, new PredictionErrorRing(),
                                                    NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()),
                                                    predicted,
                                                    new Playback(),
                                                    new SmoothClock(1f),
                                                    ConVarOf(new ClSmoothConVar(), "1"),
                                                    ConVarOf(new ClSmoothTimeConVar(), "0.1"),
                                                    ladders: ladders, hl2Player: hl2Player),
        };
    }

    private sealed class Hl2Player : IHl2PlayerMove
    {
        public float WalkSpeed => 150f;
        public float NormSpeed => 190f;
        public float SprintSpeed => 320f;
        public bool InfiniteAuxPower => false;
        public bool CanSprint(bool ducked, bool ducking, int waterLevel, bool sprintDisabled) => true;
    }

    /// <summary>
    /// The replay runs HL2MP's player from the suit words the server's state
    /// carries (m_HL2Local, predicted as stock predicts it): +speed with power
    /// starts the sprint device and saves it; with no HL2 player, base
    /// CGameMovement, it saves no sprint.
    /// </summary>
    [Fact]
    public void TheReplaySprintsOnTheSuitPowerTheServerSent()
    {
        PlayerNetState Saved(IHl2PlayerMove? hl2, float power)
        {
            var bench = Build(null, hl2, 11);
            bench.Player.State.SuitPower = power;
            bench.Replay.Buttons = 1 << 17;   // IN_SPEED

            bench.Prediction.Update(0, validFrame: true, incomingAcknowledged: 10,
                                    outgoingCommand: 11, worldUpdateArrived: false);

            Assert.True(bench.Prediction.LastReplayed == 1, "the command was replayed");
            var saved = new PlayerNetState();
            saved.BindLocal();
            saved.Decode(bench.Prediction.LastSavedBytes.ToArray());
            return saved;
        }

        PlayerNetState sprinting = Saved(new Hl2Player(), 50f);
        Assert.True(sprinting.NewSprinting, "a +speed with power sprints");
        Assert.Equal((GameMovement.SuitDeviceSprint, 25f), (sprinting.SuitDevices, sprinting.SuitPowerLoad));

        Assert.False(Saved(new Hl2Player(), 5f).NewSprinting, "too little power to start one");
        Assert.False(Saved(null, 50f).NewSprinting, "no HL2 player, no sprint");
    }

    /// <summary>One ladder, entity 7, from (0,32,0) to (0,32,128).</summary>
    private sealed class OneLadder : ILadderWorld
    {
        public int Count => 1;
        public UseableLadder At(int index) => Ladder;
        public bool TryGet(int entityIndex, out UseableLadder ladder)
        {
            ladder = Ladder;
            return entityIndex == Ladder.Index;
        }
        public IReadOnlyList<Vector> Dismounts(int ladderIndex) => Array.Empty<Vector>();
        public LadderConVars ConVars => new(true, 0.4f, false);
        public void PlayerGotOn(int ladderIndex, int playerIndex) { }
        public void PlayerGotOff(int ladderIndex, int playerIndex) { }

        private static readonly UseableLadder Ladder =
            new(7, new Vector(0f, 32f, 128f), new Vector(0f, 32f, 0f), Enabled: true, Fake: false);
    }

    /// <summary>
    /// The replay climbs the ladder the server's word names (HL2's m_hLadder,
    /// predicted as stock predicts it): a player decoded on ladder 7 with no
    /// key held hangs on it, and the saved state still names ladder 7 in
    /// MOVETYPE_LADDER. A replay that dropped the word, or ran base
    /// CGameMovement, would let go and save MOVETYPE_WALK.
    /// </summary>
    [Fact]
    public void TheReplayHangsOnTheLadderTheServerNamed()
    {
        var bench = Build(new OneLadder(), 11);
        bench.Player.State.MoveType = 9;   // MOVETYPE_LADDER
        bench.Player.State.Origin = new Vector(0f, 32f, 64f);
        bench.Player.State.Ladder = 7;

        bench.Prediction.Update(0, validFrame: true, incomingAcknowledged: 10,
                                outgoingCommand: 11, worldUpdateArrived: false);

        Assert.True(bench.Prediction.LastReplayed == 1, "the command was replayed");

        var saved = new PlayerNetState();
        saved.BindLocal();
        saved.Decode(bench.Prediction.LastSavedBytes.ToArray());

        Assert.Equal(9, saved.MoveType);
        Assert.Equal(7, saved.Ladder);
        Assert.Equal(new Vector(0f, 32f, 64f), saved.Origin);
    }

    //=========================================================================
    // 1. Which commands run.
    //=========================================================================

    [Fact]
    public void EveryUnacknowledgedCommandInTheRingIsReplayed()
    {
        var bench = Build(11, 12, 13);

        bench.Prediction.Update(0, validFrame: true, incomingAcknowledged: 10,
                                outgoingCommand: 13, worldUpdateArrived: false);

        Assert.Equal(3, bench.Prediction.LastReplayed);
    }

    [Fact]
    public void AWalkingCommandDoesNotPersistOldForwardMove()
    {
        // Stock never writes m_Local.m_flOldForwardMove back from the move
        // (CPlayerMove::FinishMove and CPrediction::FinishMove store only the
        // old buttons) and neither does this server, so it stays at what the
        // server sent. Persisting the move's value made every walking command
        // a misprediction: predicted 320, server 0.
        var bench = Build(11);
        bench.Replay.ForwardMove = 320f;

        bench.Prediction.Update(0, validFrame: true, incomingAcknowledged: 10,
                                outgoingCommand: 11, worldUpdateArrived: false);

        Assert.True(bench.Prediction.LastReplayed == 1, "the walking command was replayed");

        var saved = new PlayerNetState();
        saved.BindLocal();
        saved.Decode(bench.Prediction.LastSavedBytes.ToArray());

        Assert.True(saved.OldForwardMove == 0f,
            $"the predicted OldForwardMove stays the server's 0, not {saved.OldForwardMove}");
    }

    /// <summary>
    /// The loop's cap (mv->m_flMaxSpeed) is not the player's networked
    /// m_flMaxspeed: stock's FinishMove never stores it back
    /// (prediction.cpp:720 and player_command.cpp:212 are commented out), and
    /// neither does this server, whose record keeps what game code set. A
    /// movement rule that raises the cap (TF's class speed, 400 for a scout)
    /// changes the move and not the saved MaxSpeed; saving the cap made every
    /// compare a MaxSpeed misprediction (predicted 400, server 320).
    /// </summary>
    [Fact]
    public void ARuleRaisedCapIsNotSavedAsTheNetworkedMaxSpeed()
    {
        var bench = Build(11);
        bench.Replay.ForwardMove = 450f;
        int asked = 0;
        bench.Runner.ReplayRules = MovementRules.Stock with
        {
            MaxSpeed = (_, _, _, _) =>
            {
                asked++;
                return 400f;
            },
        };

        bench.Prediction.Update(0, validFrame: true, incomingAcknowledged: 10,
                                outgoingCommand: 11, worldUpdateArrived: false);

        Assert.True(asked > 0, "the rule was asked for the cap");

        var saved = new PlayerNetState();
        saved.BindLocal();
        saved.Decode(bench.Prediction.LastSavedBytes.ToArray());

        Assert.True(saved.MaxSpeed == 320f,
            $"the predicted MaxSpeed stays the server's 320, not {saved.MaxSpeed}");
    }

    /// <summary>
    /// HL2MP's speeds are the move's client max speed (CHL2GameMovement's
    /// CheckParameters), and the cap they leave is not saved either: a replay
    /// with the HL2 player at its normal speed (190) still saves the server's
    /// 320.
    /// </summary>
    [Fact]
    public void TheHl2SpeedCapIsNotSavedAsTheNetworkedMaxSpeed()
    {
        var bench = Build(null, new Hl2Player(), 11);
        bench.Replay.ForwardMove = 450f;

        bench.Prediction.Update(0, validFrame: true, incomingAcknowledged: 10,
                                outgoingCommand: 11, worldUpdateArrived: false);

        Assert.True(bench.Prediction.LastReplayed == 1, "the walking command was replayed");

        var saved = new PlayerNetState();
        saved.BindLocal();
        saved.Decode(bench.Prediction.LastSavedBytes.ToArray());

        Assert.True(saved.MaxSpeed == 320f,
            $"the predicted MaxSpeed stays the server's 320, not {saved.MaxSpeed}");
    }

    /// <summary>
    /// The live symptom: the server sends its record's MaxSpeed (320) beside
    /// the moved state, and a replay whose rule raised the cap to 400 holds
    /// against it instead of standing as a MaxSpeed misprediction.
    /// </summary>
    [Fact]
    public void AReplayUnderARaisedCapHoldsAgainstTheServersMaxSpeed()
    {
        var bench = Build(11);
        bench.Replay.ForwardMove = 450f;
        bench.Runner.ReplayRules = MovementRules.Stock with { MaxSpeed = (_, _, _, _) => 400f };

        bench.Prediction.Update(0, true, 10, 11, false);

        // The server's word: the move the replay made, with the MaxSpeed its
        // record holds (ServerPlayerMove.MaxSpeed, which no command writes).
        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.MaxSpeed = 320f;
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.True(bench.Prediction.Mispredicts == 0,
                    $"no misprediction, not {bench.Prediction.Mispredicts} on {bench.Prediction.LastMissFields}");
        Assert.Equal(1, bench.Prediction.Holds);
    }

    /// <summary>
    /// The spawn stamp's adoption: the acked slot was predicted under the old
    /// stamp, the arrival carries the bumped one and a new origin — the server
    /// spawned or respawned between the command and its acknowledgement, with
    /// no command to mirror. The compare adopts rather than judges, and the
    /// meter counts the adoption apart (stock re-seeds the whole predicted
    /// state at spawn — InitPredictable, c_baseentity.cpp:4578-4583).
    /// </summary>
    [Fact]
    public void ACompareStraddlingASpawnAdoptsTheArrivalInsteadOfJudgingIt()
    {
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        Assert.True(bench.Prediction.LastReplayed == 1, "the command was replayed");

        // The server's word: the join's placement under the bumped stamp.
        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.Origin = new Vector(96f, 128f, 16f);
        bench.Player.State.SpawnEpoch = bench.Player.State.SpawnEpoch + 1;
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.True(bench.Prediction.Mispredicts == 0,
                    $"the straddling compare is adopted, not judged ({bench.Prediction.Mispredicts} " +
                    $"on {bench.Prediction.LastMissFields})");
        Assert.Equal(1, bench.Prediction.Rebuilt);
        Assert.Equal(0, bench.Prediction.Holds);
    }

    /// <summary>
    /// m_iAmmo is not error-checked (HL2MP's FTYPEDESC_NOERRORCHECK): a pool
    /// the server drew inside the command — a TF reload's — arrives as the
    /// word and is no misprediction. The control is the next fact's: an
    /// unexempted field that moved still stands judged.
    /// </summary>
    [Fact]
    public void AnAmmoPoolTheServerMovedIsNoMisprediction()
    {
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        Assert.True(bench.Prediction.LastReplayed == 1, "the command was replayed");

        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.Ammo[12] = (short)(bench.Player.State.Ammo[12] - 4);
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.True(bench.Prediction.Mispredicts == 0,
                    $"the pool is the arrival's word ({bench.Prediction.Mispredicts} on {bench.Prediction.LastMissFields})");
    }

    /// <summary>
    /// The arm the adoption must not swallow: the same origin difference with
    /// an unchanged stamp — a move the client really did predict wrong — still
    /// stands judged red. Without this arm the first fact cannot fail.
    /// </summary>
    [Fact]
    public void AnEqualStampedOriginDifferenceStillStandsJudged()
    {
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        Assert.True(bench.Prediction.LastReplayed == 1, "the command was replayed");

        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.Origin = new Vector(96f, 128f, 16f);
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.Equal(1, bench.Prediction.Mispredicts);
        Assert.Equal(0, bench.Prediction.Rebuilt);
    }

    /// <summary>
    /// The connect-time tick-base handshake: the server rebases the player's clock
    /// once when it first runs the queued commands, with no command to carry it
    /// (CBasePlayer::AdjustPlayerTimeBase, player.cpp:3152-3172), and the client's
    /// clock is received-and-predicted (RecvPropInt m_nTickBase, c_baseplayer.cpp:236;
    /// DEFINE_PRED_FIELD, :403). The acked slot was predicted before the seed, so
    /// its clock stands away by the re-base, and the first full state carries the
    /// spawn's initial words with it (SuitPower, WaterType, Velocity — the one line
    /// every clean boot logged right after connect). Stock absorbs this silently
    /// because the replay's basis IS the decoded snapshot — RestoreOriginalEntityState
    /// before every replay (prediction.cpp:1781-1784) — so the first arrival seeds,
    /// not judges. The stamp plays no part: the clock moved with no spawn behind it.
    /// </summary>
    [Fact]
    public void TheFirstAckAfterConnectSeedsInsteadOfBeingJudged()
    {
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        Assert.True(bench.Prediction.LastReplayed == 1, "the command was replayed");

        // The server's first ack: its clock rebased onto its own tick count, the
        // predicted slot's standing at the pre-seed count, epoch untouched.
        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.TickBase = 99;   // srv after the re-base; pred stands at 1
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.True(bench.Prediction.Mispredicts == 0,
                    $"the seeding arrival is adopted, not judged ({bench.Prediction.Mispredicts} " +
                    $"on {bench.Prediction.LastMissFields})");
        Assert.Equal(1, bench.Prediction.Seeded);
        Assert.Equal(0, bench.Prediction.Rebuilt);
        Assert.DoesNotContain("MISPREDICTION", bench.Log.Text);
    }

    /// <summary>
    /// The arm the seeding must not swallow: once the first arrival has seeded the
    /// basis, a later acknowledgement that really disagrees still stands judged
    /// red — without this arm the seeding fact cannot fail.
    /// </summary>
    [Fact]
    public void ACompareAfterTheSeedStillJudgesTheNextAck()
    {
        var bench = Build(11, 12);
        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);

        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.TickBase = 99;
        bench.Packet();
        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(1, bench.Prediction.Seeded);
        Assert.Equal(0, bench.Prediction.Mispredicts);

        // Mid-game drift, after the seed: an origin the replay did not move to.
        bench.Player.State.Origin = new Vector(96f, 128f, 16f);
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 12, worldUpdateArrived: true);

        Assert.Equal(1, bench.Prediction.Mispredicts);
        Assert.Contains("MISPREDICTION", bench.Log.Text);
    }

    [Fact]
    public void AnObserverIsReplayedInItsNetworkedObserverMode()
    {
        // The server runs FullObserverMove in the player's networked
        // m_iObserverMode: ROAMING flies, IN_EYE / CHASE follow the target,
        // anything else holds still. A replay that left the mode at NONE would
        // hold a roaming spectator still against a server that flew it. The
        // target arm counts every command that reaches it, which is the
        // reading here (the fake engine's spec_speed is 0, so a roaming
        // position cannot tell the arms apart).
        var bench = Build(11);
        bench.Player.State.MoveType = 10;   // MOVETYPE_OBSERVER
        bench.Player.State.ObserverMode = (int)ObserverMode.InEye;

        bench.Prediction.Update(0, validFrame: true, incomingAcknowledged: 10,
                                outgoingCommand: 11, worldUpdateArrived: false);

        Assert.True(bench.Prediction.LastReplayed == 1, "the observer command was replayed");
        Assert.True(bench.Runner.ClientLoop(default, 0f, 0f).ObserverTargetAsks == 1,
            "the replayed command reached FullObserverMove's in-eye arm");
    }

    [Fact]
    public void TheAcknowledgedCommandIsNotReplayedAgain()
    {
        var bench = Build(10, 11);

        bench.Prediction.Update(0, true, incomingAcknowledged: 10, outgoingCommand: 11,
                                worldUpdateArrived: false);

        // Command 10 is the one the server has already run; replaying it would
        // integrate the same tick twice.
        Assert.Equal(new[] { 11 }, bench.Replay.Seeded);
    }

    [Fact]
    public void TheReplayStopsAtTheFirstCommandTheInputRingDoesNotHold()
    {
        // 12 is missing: the ring lost it, which happens after a stall. Stock's
        // own loop breaks on the same NULL rather than skipping the hole.
        var bench = Build(11, 13);

        bench.Prediction.Update(0, true, 10, 13, false);

        Assert.Equal(1, bench.Prediction.LastReplayed);
        Assert.Equal(new[] { 11 }, bench.Replay.Seeded);
    }

    [Fact]
    public void PredictionOffReplaysNothing()
    {
        var bench = Build(11, 12);
        bench.Prediction.Enabled = false;

        bench.Prediction.Update(0, true, 10, 12, false);

        Assert.Equal(0, bench.Prediction.LastReplayed);
        Assert.Equal(0, bench.Replay.Opened);
    }

    [Fact]
    public void AnInvalidFrameReplaysNothing()
    {
        // The engine's own gate, which stock also honours: a frame it will not
        // vouch for is a frame whose command numbers cannot be trusted.
        var bench = Build(11, 12);

        bench.Prediction.Update(0, validFrame: false, incomingAcknowledged: 10,
                                outgoingCommand: 12, worldUpdateArrived: false);

        Assert.Equal(0, bench.Prediction.LastReplayed);
        Assert.Equal(0, bench.Replay.Opened);
    }

    [Fact]
    public void NoLocalPlayerReplaysNothingAndPublishesNothing()
    {
        var bench = Build(11);
        bench.Standalones.Entities.Clear();

        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.Equal(0, bench.Prediction.LastReplayed);
        Assert.Equal(0, bench.Replay.Published);
    }

    //=========================================================================
    // 2. The bracket. Nothing else can see this.
    //=========================================================================

    [Fact]
    public void EveryCommandOpensAndClosesTheBracketExactlyOnce()
    {
        var bench = Build(11, 12, 13);

        bench.Prediction.Update(0, true, 10, 13, false);

        Assert.Equal(3, bench.Replay.Opened);
        Assert.Equal(3, bench.Replay.Closed);
    }

    [Fact]
    public void TheBracketIsBalancedInOrderAndNeverNested()
    {
        var bench = Build(11, 12);

        bench.Prediction.Update(0, true, 10, 12, false);

        // A begin is always followed by its own end before the next begin. A
        // nested pair would leave step 3e's trace slots armed for the wrong
        // player, and every trace would still answer a plausible fraction.
        Assert.Equal(new[] { "begin:11", "end:1", "begin:12", "end:1", "publish:1" },
                     bench.Replay.Calls);
    }

    [Fact]
    public void ABracketThatWouldNotOpenIsStillClosed()
    {
        // The refusal path: Begin answered false, so the loop must not run —
        // and End must run anyway, because outside a bracket the trace slots
        // have to be back to refusing rather than left armed.
        var bench = Build(11);
        bench.Replay.Opens = false;

        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.Equal(0, bench.Replay.Opened);
        Assert.Equal(0, bench.Replay.Ran);
        Assert.Equal(0, bench.Prediction.LastReplayed);
    }

    [Fact]
    public void TheCommandNumberReachesTheBracketSoTheSharedRandomStreamIsSeeded()
    {
        // The bracket seeds SharedRandomInt/Float/Vector/Angle from the command
        // it is handed. A bracket opened with the wrong number — or with a
        // constant — disagrees with the server about every random decision,
        // silently and only under prediction.
        var bench = Build(41, 42, 43);

        bench.Prediction.Update(0, true, 40, 43, false);

        Assert.Equal(new[] { 41, 42, 43 }, bench.Replay.Seeded);
    }

    //=========================================================================
    // 3. What the frame publishes.
    //=========================================================================

    [Fact]
    public void AFrameWithNothingReplayedStillPublishesTheDecodedView()
    {
        // The view has to follow the player with cs_cl_player_predict 0. A
        // frame that published only when it had replayed something would leave
        // the eye wherever the last replayed frame put it.
        var bench = Build();
        bench.Prediction.Enabled = false;

        bench.Prediction.Update(0, true, 10, 10, false);

        Assert.Equal(1, bench.Replay.Published);
        Assert.False(bench.Replay.LastDriving);
        Assert.Equal(64f, bench.Replay.LastOrigin.X);
        Assert.Equal(64f, bench.Replay.LastViewOffset.Z);
    }

    [Fact]
    public void TheHudsHealthIsPublishedWithTheViewAndIsTheDecodedOne()
    {
        // It used to be read out of the blob at a #define'd offset that claimed
        // to be pinned from the C# side and was not — the HUD's first paint
        // printed 1544355841. One declaration and one writer is the fix, and
        // the number is the DECODED one either way: damage is the server's
        // decision, so the replay has nothing better to say about it than the
        // packet does.
        var bench = Build(11);
        bench.Player.State.Health = 73;

        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.Equal(73, bench.Replay.LastHealth);
    }

    [Fact]
    public void AFrameThatReplayedSomethingPublishesAsDriving()
    {
        var bench = Build(11);

        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.True(bench.Replay.LastDriving);
        Assert.True(bench.Prediction.Driving);
    }

    /// <summary>
    /// NOTHING LEFT UNACKNOWLEDGED IS STILL A PREDICTED FRAME, at the server's
    /// word. On a LAN the acknowledgement catches up with the last command
    /// every few frames; the frame used to fall back to interpolation, whose
    /// rings for the predicted player had not been fed while prediction drove,
    /// and the eye jumped back to wherever the player stood before its last
    /// teleport for one frame in ~6 (live gate render-plan after beam-ring and
    /// proxy-animated, 2026-10-11: (-3800.09, 2500, -0.23) against the stand's
    /// (-4116, 4600, 1.92)). Stock's prediction keeps the predicted player at
    /// its restored networked state when no command runs.
    /// </summary>
    [Fact]
    public void NothingLeftUnacknowledgedDrivesAtTheDecodedOrigin()
    {
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, false);
        Assert.True(bench.Prediction.Driving);

        bench.Prediction.Update(0, true, 11, 11, false);

        Assert.True(bench.Prediction.Driving);
        Assert.True(bench.Replay.LastDriving);
        Assert.Equal(0, bench.Prediction.LastReplayed);
        Assert.Equal(bench.Player.State.Origin.X, bench.Replay.LastOrigin.X);
        Assert.Equal(bench.Player.State.Origin.Y, bench.Replay.LastOrigin.Y);
        Assert.Equal(bench.Player.State.Origin.Z, bench.Replay.LastOrigin.Z);
    }

    [Fact]
    public void ADrivingPublishNamesTheDrivenEntity()
    {
        // What the interpolation pass leaves out (InterpolatedSet, lane M1-A):
        // the entity the shell is told to draw at the replay's origin.
        var bench = Build(11);

        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.Equal(LocalHandle, bench.Prediction.DrivenHandle.Value);
    }

    [Fact]
    public void APublishThatDoesNotDriveNamesNoEntity()
    {
        var bench = Build(13);

        bench.Prediction.Update(0, true, 10, 13, false);

        Assert.Equal(EntityHandle.None, bench.Prediction.DrivenHandle);
    }

    [Fact]
    public void ACommandTheReplayCouldNotRunStopsDriving()
    {
        // The other way a replay runs nothing: a command IS unacknowledged and
        // the input ring does not hold it. Then nothing computed the origin, and
        // the frame must not claim it did.
        var bench = Build(13);

        bench.Prediction.Update(0, true, 10, 13, false);

        Assert.Equal(0, bench.Prediction.LastReplayed);
        Assert.False(bench.Prediction.Driving);
        Assert.False(bench.Replay.LastDriving);
    }

    //=========================================================================
    // 4. The ground word — the one field that cannot round-trip as a value.
    //=========================================================================

    [Fact]
    public void NoGroundUnderTheLoopClearsTheGroundWord()
    {
        // With no trace API behind the double the loop finds nothing to stand
        // on, which is the arm this fact is about: HasGround false must write
        // the "nothing" word and not leave the basis's entity in place.
        var bench = Build(11);
        bench.Player.State.GroundEntity = 7;
        bench.Replay.GroundIndex = 7;

        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.Equal(ClientPlayerPrediction.GroundWireNone, bench.Prediction.GroundWord);
    }

    [Fact]
    public void TheGroundWordIsTheIndexAndCarriesNoSerial()
    {
        // ground_handle_wire.h's measurement: the two sides do not share a
        // serial counter, so a serial in the word makes the SAME ground compare
        // unequal on every packet. The word is the entry index alone.
        Assert.Equal(0x7FF, ClientPlayerPrediction.GroundWireIndexMask);
        Assert.Equal((1 << 21) - 1, ClientPlayerPrediction.GroundWireNone);
    }

    [Fact]
    public void AHandlesEntryIndexIsNotTheGroundWordsWidth()
    {
        // Two masks that look interchangeable and are not. ENT_ENTRY_MASK
        // (const.h:83) is the low SIXTEEN bits of what CBaseHandle::ToInt
        // packs; the ground word's is ELEVEN, because it imitates a networked
        // ehandle rather than a handle. Using the narrow one to read a handle
        // works for every player index this mod can have and truncates silently
        // above 2047 — held apart here so that collapsing them is an edit that
        // fails rather than one that reads fine.
        Assert.NotEqual((uint)ClientPlayerPrediction.GroundWireIndexMask,
                        ClientPlayerPrediction.HandleEntryMask);
        Assert.Equal(0xFFFFu, ClientPlayerPrediction.HandleEntryMask);
    }

    //=========================================================================
    // 5. The compare and the meter.
    //=========================================================================

    [Fact]
    public void NothingIsComparedOnAFrameWithNoPacket()
    {
        var bench = Build(11);

        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);

        Assert.Equal(0, bench.Prediction.Compares);
    }

    [Fact]
    public void NothingIsComparedWhenThePacketDidNotCarryThisPlayer()
    {
        // A world update can arrive that this entity was not in. The decoded
        // state then still holds the PREVIOUS acknowledgement's word, and
        // judging the new one against it would be judging two different
        // commands against each other.
        var bench = Build(11);

        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: true);

        Assert.Equal(0, bench.Prediction.Compares);
    }

    [Fact]
    public void APacketThatCarriedThePlayerIsCompared()
    {
        var bench = Build(11);
        bench.Packet();

        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: true);

        Assert.Equal(1, bench.Prediction.Compares);
    }

    [Fact]
    public void OnePacketIsJudgedOnce()
    {
        var bench = Build(11, 12);
        bench.Packet();

        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: true);
        bench.Prediction.Update(0, true, 10, 12, worldUpdateArrived: true);

        Assert.Equal(1, bench.Prediction.Compares);
    }

    [Fact]
    public void AnAcknowledgementWithNoSavedSlotIsNoBasisAndNotAMiss()
    {
        // Nothing has been replayed, so the ring holds nothing for command 10.
        // "I cannot answer" and "the prediction was wrong" are different facts
        // and counting the first as the second is a meter that lies at every
        // level load.
        var bench = Build();
        bench.Packet();

        bench.Prediction.Update(0, true, 10, 10, worldUpdateArrived: true);

        Assert.Equal(1, bench.Prediction.NoBasis);
        Assert.Equal(0, bench.Prediction.Mispredicts);
        Assert.Equal(0, bench.Prediction.Holds);
    }

    [Fact]
    public void APredictionThatHeldIsAHoldAndNotAMiss()
    {
        var bench = Build(11);

        // Replay command 11 and then acknowledge it with the state unchanged:
        // the loop wrote the working copy, the ring saved it, and the decoded
        // state is what the replay started from.
        bench.Prediction.Update(0, true, 10, 11, false);

        // Adopt what the replay produced as the server's word, which is what a
        // server running the same command would have sent.
        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.Equal(1, bench.Prediction.Holds);
        Assert.Equal(0, bench.Prediction.Mispredicts);
    }

    [Fact]
    public void ADifferenceOnADeclaredFieldStandsAndIsNamed()
    {
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, false);

        // The server says they are somewhere else entirely — well past Origin's
        // 0.0625 tolerance, which is stock's coordTolerance.
        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.Origin = new Vector(999f, 0f, 0f);
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.Equal(1, bench.Prediction.Mispredicts);
        Assert.Contains("Origin", bench.Prediction.LastMissFields);
    }

    [Fact]
    public void ADifferenceInsideItsToleranceIsForgivenAndCountedApart()
    {
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, false);

        byte[] saved = bench.Prediction.LastSavedBytes.ToArray();
        bench.Player.State.Decode(saved);

        // Half of stock's coordTolerance: two runs of the same arithmetic, not
        // a player standing somewhere else.
        Vector was = bench.Player.State.Origin;
        bench.Player.State.Origin = new Vector(was.X + 0.03f, was.Y, was.Z);
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.Equal(0, bench.Prediction.Mispredicts);
        Assert.Equal(1, bench.Prediction.Holds);
        Assert.Equal(1, bench.Prediction.Tolerated);
    }

    [Fact]
    public void ThePokeMakesTheNextCompareGoRed()
    {
        // The provocation that proves the meter is alive. Without it a quiet
        // meter and a broken meter read the same from outside.
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, false);

        // Offset 0 of the PREDICTED STATE, which is what the console command's
        // default means. A poke into the engine block would be erased by the
        // compare, which is a provocation the meter is guaranteed to ignore.
        Assert.True(bench.Prediction.Poke(0));

        bench.Prediction.Update(0, true, 10, 11, false);
        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.Equal(1, bench.Prediction.Mispredicts);
    }

    [Fact]
    public void APokePastTheRingIsRefusedRatherThanIgnored()
    {
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.False(bench.Prediction.Poke(bench.Prediction.RingBytes));
        Assert.False(bench.Prediction.Poke(-1));
    }

    [Fact]
    public void TheEngineBlockIsNotCompared()
    {
        // The first 368 bytes of a player's 524-byte image are the ENGINE
        // BLOCK: the shell's networked pose, handle, model and render state,
        // written by the engine's own decoder every update. Nothing predicts
        // any of it.
        //
        // THIS IS WHAT THE TREE CALLED "PADDING". Step 3e measured 86-91
        // mispredictions per window in both arms at bytes no declared field
        // claimed — offsets 24-27 and 108-111, both inside this block — and
        // concluded the raw count could not separate the arms. Including the
        // block makes every compare differ, which is what the meter reported
        // the first time step 6f ran it live: 377 compares, 377
        // "mispredictions", zero holds.
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, false);

        byte[] saved = bench.Prediction.LastSavedBytes.ToArray();
        bench.Player.State.Decode(saved);

        // The engine moves the block under the compare, exactly as a packet
        // does. If it were compared, this would stand as a misprediction.
        byte[] moved = bench.Player.State.Snapshot();
        moved[24] ^= 0xFF;
        moved[108] ^= 0xFF;
        moved[264] ^= 0xFF;
        bench.Player.State.Decode(moved);
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.Equal(0, bench.Prediction.Mispredicts);
        Assert.Equal(1, bench.Prediction.Holds);
    }

    [Fact]
    public void APokeIsMeasuredFromTheFirstDeclaredFieldAndNotFromTheImage()
    {
        // Offset 0 of the IMAGE is inside the engine block, which the compare
        // takes from the arrival — so a poke there would be erased before the
        // judge ever saw it, and the provocation that proves the meter can go
        // red would be one the meter is guaranteed to ignore.
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, false);

        int block = bench.Prediction.DeclaredStartOffset;
        int declared = bench.Prediction.RingBytes - block;

        Assert.True(block > 0, "a player's image carries an engine block in front of its state");

        // The exact boundary, which is what pins the base the offset is measured
        // from: the last byte of the DECLARED state is reachable and the one
        // past it is not. Measured from the image instead, both would be
        // reachable — the block is 368 bytes of room to be wrong in.
        Assert.True(bench.Prediction.Poke(declared - 1),
            "the last byte of the predicted state is pokeable");
        Assert.False(bench.Prediction.Poke(declared),
            "one past it is off the end of the image, not into the block");
        Assert.True(bench.Prediction.Poke(0),
            "zero is the first DECLARED field, which is what the console default means");
    }

    //=========================================================================
    // 6. The decoded state is the server's word and stays that way.
    //=========================================================================

    [Fact]
    public void TheReplayNeverWritesTheDecodedState()
    {
        // ClientArsenal's rule, and the reason the compare means anything: a
        // prediction scribbled over networked memory is compared against
        // something this side wrote. The delta only carries what changed, so a
        // scribbled word the server never re-sends would corrupt the client's
        // view permanently.
        var bench = Build(11, 12, 13);

        Vector before = bench.Player.State.Origin;

        bench.Prediction.Update(0, true, 10, 13, false);

        Assert.Equal(before.X, bench.Player.State.Origin.X);
        Assert.Equal(before.Y, bench.Player.State.Origin.Y);
        Assert.Equal(before.Z, bench.Player.State.Origin.Z);
    }

    [Fact]
    public void TheBasisIsRestoredSoTwoRERUNSOfTheSameCommandsAgree()
    {
        // What makes the replay stateless: a frame with no new packet
        // re-predicts the same commands from the same start and lands on the
        // same answer. Without the basis reset the second frame would start
        // from the first frame's result and the player would accelerate away.
        //
        // THE FIRST FRAME IS DELIBERATELY NOT ONE OF THE TWO COMPARED, and that
        // is stock's rule rather than a weakening of this one. A command's FIRST
        // run is licensed to do things a re-run must not — CUserCmd's
        // hasbeenpredicted is exactly that licence — and gamemovement.cpp:5215's
        // jimd HACK arm is one: `firstTimePredicted && !IsDead()` restores an
        // eye left at the ducked height, which only ever fires on a first run.
        // So the property is about RE-RUNS, and a test that compared run one
        // against run two would be asserting that the licence is never used.
        var bench = Build(11, 12);

        bench.Prediction.Update(0, true, 10, 12, false);   // the first run
        bench.Prediction.Update(0, true, 10, 12, false);
        byte[] first = bench.Prediction.LastSavedBytes.ToArray();

        bench.Prediction.Update(0, true, 10, 12, false);
        byte[] second = bench.Prediction.LastSavedBytes.ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void ALevelLoadForgetsEverySlotButKeepsTheTally()
    {
        // The ring is keyed by COMMAND NUMBER and command numbers start again
        // from the beginning on every map, so a slot kept across a load is
        // judged against a different world's acknowledgement. The COUNTERS are
        // kept: a meter that forgot its tally at every map change could not
        // answer "has this build ever mispredicted".
        var bench = Build(11);
        bench.Prediction.Update(0, true, 10, 11, false);
        bench.Packet();
        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        int comparesBefore = bench.Prediction.Compares;
        Assert.True(comparesBefore > 0);

        bench.Prediction.LevelInit();

        Assert.Equal(comparesBefore, bench.Prediction.Compares);
        Assert.False(bench.Prediction.Driving);
        Assert.Equal(0, bench.Prediction.LastReplayed);

        // And the slot is gone: the same acknowledgement now has no basis.
        bench.Packet();
        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.Equal(1, bench.Prediction.NoBasis);
    }

    [Fact]
    public void ACommandIsAFirstRunExactlyOnce()
    {
        // The other half of the same rule, held where it can fail: the licence
        // above is spent by the first run and never comes back. It is tracked
        // here rather than asked of the engine's ring because command numbers
        // only increase and this side is the only thing that runs them.
        var bench = Build(11, 12);

        bench.Prediction.Update(0, true, 10, 12, false);
        byte[] firstRun = bench.Prediction.LastSavedBytes.ToArray();

        bench.Prediction.Update(0, true, 10, 12, false);
        byte[] reRun = bench.Prediction.LastSavedBytes.ToArray();

        Assert.NotEqual(firstRun, reRun);
    }

    //=========================================================================
    // A mod's own predicted state (IClientPredictedState).
    //=========================================================================

    /// <summary>A participant that writes what it was asked into a call log.</summary>
    private class Participant(List<string> calls, string name, bool throws = false)
        : IClientPredictedState
    {
        /// <summary>Where the calls go; swapped for the replay's own log to read the order.</summary>
        public List<string> Calls { get; set; } = calls;

        public void ResetBasis(EntityHandle player, int acknowledged)
        {
            Calls.Add($"{name}.reset:{acknowledged}:{player.Value:x}");
            if (throws)
                throw new InvalidOperationException("the participant fails");
        }

        public void CommandRan(EntityHandle player, int commandNumber, bool firstTime)
        {
            Calls.Add($"{name}.ran:{commandNumber}:{(firstTime ? 1 : 0)}");
            if (throws)
                throw new InvalidOperationException("the participant fails");
        }
    }

    private sealed class ListHooks(params IClientPredictedState[] all) : IHooks<IClientPredictedState>
    {
        public IReadOnlyList<IClientPredictedState> All => all;
        public void Each(string what, Action<IClientPredictedState> call) => throw new NotSupportedException();
    }

    /// <summary>
    /// The no-owner arm: with no participant registered the frame is the
    /// core replay alone (the bracket, the ring save, the publish), which the
    /// participant facts below each extend by exactly its own calls.
    /// </summary>
    [Fact]
    public void WithNoPredictedStatesTheFrameIsTheCoreReplayAlone()
    {
        var bench = Build(new ListHooks(), 11);

        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.Equal(new[] { "begin:11", "end:1", "publish:1" }, bench.Replay.Calls);
        Assert.Equal(1, bench.Prediction.Saves);
        Assert.DoesNotContain(bench.Log.Lines, l => l.Warning);
    }

    [Fact]
    public void AModsPredictedStateIsResetOnceBeforeTheReplayAndSavedInsideEachCommandsBracket()
    {
        var participant = new Participant(new List<string>(), "tf");
        var bench = Build(new ListHooks(participant), 11, 12);

        // The participant and the bracket share one log, so the order is the reading.
        participant.Calls = bench.Replay.Calls;

        bench.Prediction.Update(0, true, 10, 12, false);

        Assert.Equal(new[]
        {
            "tf.reset:10:1000001",
            "begin:11", "tf.ran:11:1", "end:1",
            "begin:12", "tf.ran:12:1", "end:1",
            "publish:1",
        }, bench.Replay.Calls);
    }

    [Fact]
    public void AReRunCommandReachesTheParticipantAsNotItsFirstRun()
    {
        var calls = new List<string>();
        var bench = Build(new ListHooks(new Participant(calls, "tf")), 11);

        bench.Prediction.Update(0, true, 10, 11, false);
        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.Equal(new[] { "tf.reset:10:1000001", "tf.ran:11:1", "tf.reset:10:1000001", "tf.ran:11:0" }, calls);
    }

    [Fact]
    public void WithPredictionOffTheBasisIsStillResetAndNoCommandIsSaved()
    {
        // The working copy must be the server's word on a frame that does not
        // replay, or a reader of it (a movement rule) sees a stale prediction.
        var calls = new List<string>();
        var bench = Build(new ListHooks(new Participant(calls, "tf")), 11);
        bench.Prediction.Enabled = false;

        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.Equal(new[] { "tf.reset:10:1000001" }, calls);
    }

    [Fact]
    public void AThrowingParticipantIsLoggedAndTheNextOneAndTheReplayStillRun()
    {
        var calls = new List<string>();
        var bench = Build(new ListHooks(new Participant(calls, "bad", throws: true),
                                        new Participant(calls, "good")), 11);

        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.Equal(new[] { "bad.reset:10:1000001", "good.reset:10:1000001", "bad.ran:11:1", "good.ran:11:1" }, calls);
        Assert.Equal(1, bench.Prediction.LastReplayed);

        // Each throw is named, once per call that threw, as a warning.
        var warnings = bench.Log.Lines.Where(l => l.Warning).Select(l => l.Text).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.Contains("IClientPredictedState.ResetBasis on ", warnings[0], StringComparison.Ordinal);
        Assert.Contains("IClientPredictedState.CommandRan on ", warnings[1], StringComparison.Ordinal);
        Assert.All(warnings, w => Assert.Contains("the participant fails", w, StringComparison.Ordinal));
    }

    private sealed class LateParticipant(List<string> calls) : Participant(calls, "late"), IOrderedHook
    {
        public int HookOrder => 20;
    }

    private sealed class EarlyParticipant(List<string> calls) : Participant(calls, "early"), IOrderedHook
    {
        public int HookOrder => 10;
    }

    /// <summary>
    /// The chain as the game builds it: the core's Hooks over the side's
    /// container, which orders the participants by HookOrder and not by the
    /// order they were registered in (late is registered first).
    /// </summary>
    [Fact]
    public void ParticipantsAreAskedInTheirHookOrder()
    {
        var calls = new List<string>();
        var container = new ServiceContainer();
        container.AddInstance<ILog>(new RecordingLog());
        container.AddFactory(typeof(LateParticipant), _ => new LateParticipant(calls));
        container.AddFactory(typeof(EarlyParticipant), _ => new EarlyParticipant(calls));
        var bench = Build(new Hooks<IClientPredictedState>(container, new RecordingLog()), 11);

        bench.Prediction.Update(0, true, 10, 11, false);

        Assert.Equal(new[] { "early.reset:10:1000001", "late.reset:10:1000001", "early.ran:11:1", "late.ran:11:1" },
                     calls);
    }

    /// <summary>
    /// The shape a mod's carrier lives on: a movement rule, asked during a
    /// replayed command, writes the participant's working copy, the
    /// participant saves it at CommandRan, and the next frame's reset puts it
    /// back so the same commands save the same values again (stateless
    /// replay). The rule writes through the replay's own rules
    /// (MovementRunner.ReplayRules, which the client module sets from its
    /// resolved IMovementRules).
    /// </summary>
    [Fact]
    public void AMovementRulesWriteDuringACommandIsWhatTheParticipantSavesForIt()
    {
        var carrier = new CountingCarrier();
        var bench = Build(new ListHooks(carrier), 11, 12);
        bench.Runner.ReplayRules = MovementRules.Stock with
        {
            MaxSpeed = (playerIndex, maxSpeed, _, _) =>
            {
                carrier.RuleWrote(playerIndex);
                return maxSpeed;
            },
        };

        bench.Prediction.Update(0, true, 10, 12, false);
        bench.Prediction.Update(0, true, 10, 12, false);

        Assert.Equal(new[] { "11=1", "12=2", "11=1", "12=2" }, carrier.Saved);
        Assert.Equal(new[] { LocalPlayer }, carrier.Writers.Distinct());
    }

    /// <summary>
    /// The game's composition registers the chain, over the side's container,
    /// before it builds the replay that takes it. Held on the source, as
    /// ClientLevelHookTests holds the level hooks: ClientModule.Initialize
    /// cannot run without an engine.
    /// </summary>
    [Fact]
    public void TheClientRegistersThePredictedStateChainBeforeItBuildsTheReplay()
    {
        string module = File.ReadAllText(RepoLayout.Sdk("src/sourcesharp/managed/SourceSharp.Client/ClientModule.cs"));
        int registered = module.IndexOf(
            "_services.AddFactory<IHooks<IClientPredictedState>>(locator => new Hooks<IClientPredictedState>(locator, log));",
            StringComparison.Ordinal);
        int built = module.IndexOf("_prediction = _services.Create<ClientPlayerPrediction>();", StringComparison.Ordinal);

        Assert.True(built >= 0, "the client builds its replay through the container");
        Assert.True(registered >= 0, "the client registers the predicted-state chain");
        Assert.True(registered < built, "the chain is registered before the replay that takes it is built");
    }

    /// <summary>A participant whose working copy is one counter the rule bumps.</summary>
    private sealed class CountingCarrier : IClientPredictedState
    {
        private int _working;
        public readonly List<string> Saved = new();
        public readonly List<int> Writers = new();

        public void RuleWrote(int playerIndex)
        {
            Writers.Add(playerIndex);
            _working++;
        }

        public void ResetBasis(EntityHandle player, int acknowledged) => _working = 0;

        public void CommandRan(EntityHandle player, int commandNumber, bool firstTime)
            => Saved.Add($"{commandNumber}={_working}");
    }

    //=========================================================================
    // 2. The view smoothing after a corrected origin (stock's CheckError /
    //    NotePredictionError / GetPredictionErrorSmoothingVector chain,
    //    prediction.cpp:169-197 and c_baseplayer.cpp:2641-2738).
    //=========================================================================

    /// <summary>
    /// The bench the smoothing facts need: stock's clock is scripted, so a
    /// published frame's fraction of <c>cl_smoothtime</c> is a number the fact
    /// chose rather than one it hopes the wall clock left it. Same standing
    /// player the regular bench decodes.
    /// </summary>
    private static Bench BuildSmooth(IGameClock? clock = null,
                                     IPlaybackState? playback = null,
                                     ClSmoothConVar? smooth = null,
                                     ClSmoothTimeConVar? smoothTime = null,
                                     params int[] ringCommands)
    {
        clock ??= new SmoothClock(1f);
        playback ??= new Playback();
        smooth ??= ConVarOf(new ClSmoothConVar(), "1");
        smoothTime ??= ConVarOf(new ClSmoothTimeConVar(), "0.1");

        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var player = new ClientSsPlayer(engine) { Handle = new EntityHandle(LocalHandle) };
        player.State.BindLocal();
        player.Enabled = false;

        player.State.Origin = new Vector(64f, 128f, 16f);
        player.State.ViewOffset = new Vector(0f, 0f, 64f);
        player.State.MaxSpeed = 320f;
        player.State.StepSize = 18f;
        player.State.Alive = true;
        player.State.MoveType = 2;   // MOVETYPE_WALK
        player.State.GroundEntity = ClientPlayerPrediction.GroundWireNone;

        var standalones = new Standalones();
        standalones.Entities.Add(player);

        var replay = new Replay();
        var runner = TestSides.Movement(new ClientModuleEngine(TestSides.Create(Side.Client)));

        foreach (int command in ringCommands)
            replay.Ring.Add(command);

        var log = new RecordingLog();

        return new Bench
        {
            Log = log,
            Engine = engine,
            Standalones = standalones,
            Replay = replay,
            Player = player,
            Runner = runner,
            Prediction = new ClientPlayerPrediction(log, standalones, engine, replay,
                                                    runner, null, new PredictionErrorRing(),
                                                    NetStateCatalog.Of(TestServices.ClientGame,
                                                                       new RecordingLog()),
                                                    new ListHooks(),
                                                    clock: clock, playback: playback,
                                                    smooth: smooth, smoothTime: smoothTime),
        };
    }

    /// <summary>
    /// The rocket-jump case the lane measured live: the ack's arrival carries an
    /// origin the replay did not reach, so the eye is eased from the predicted
    /// place toward it rather than snapping. Stock: the offset rides the eye from
    /// the full delta at the correction to nothing a smoothing time later
    /// (c_baseplayer.cpp:2724-2734).
    /// </summary>
    [Fact]
    public void TheEyeIsEasedToTheCorrectedOriginAndTheEntityDoesNotMove()
    {
        var clock = new SmoothClock(1f);
        var bench = BuildSmooth(clock, ringCommands: [11]);

        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        Assert.True(bench.Prediction.LastReplayed == 1, "the command was replayed");

        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.Origin = new Vector(96f, 128f, 16f);
        bench.Packet();

        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);

        Assert.Equal(1, bench.Prediction.Mispredicts);

        // The eye is at the predicted place (64), not at the corrected one: the
        // entity origin published is the decoded word and the whole difference
        // rides the eye offset. Predicted minus Server — the sign the smoothing
        // decays along.
        Assert.Equal(new Vector(96f, 128f, 16f), bench.Replay.LastOrigin);
        Assert.Equal(-32f, bench.Replay.LastViewOffset.X);
    }

    /// <summary>
    /// The ease is linear and it ends: half a smoothing time left halves the
    /// offset, and past it the eye is the decoded origin with nothing owed.
    /// </summary>
    [Fact]
    public void TheEaseDecaysLinearlyAndEndsAtTheSmoothingTime()
    {
        var clock = new SmoothClock(1f);
        var bench = BuildSmooth(clock,
                                smoothTime: ConVarOf(new ClSmoothTimeConVar(), "0.25"),
                                ringCommands: [11]);

        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.Origin = new Vector(96f, 128f, 16f);
        bench.Packet();
        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(-32f, bench.Replay.LastViewOffset.X);

        clock.Now = 1.125f;   // half the 0.25 smoothing window still left
        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(-16f, bench.Replay.LastViewOffset.X);

        clock.Now = 1.375f;   // past the window: nothing owed, the eye is the arrival's
        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(0f, bench.Replay.LastViewOffset.X);
        Assert.Equal(new Vector(96f, 128f, 16f), bench.Replay.LastOrigin);
    }

    /// <summary>
    /// The window is stock's: a difference at or under <c>MIN_PREDICTION_EPSILON</c>
    /// is noise the eye is not eased for, and one past <c>MAX_PREDICTION_ERROR</c>
    /// reads as a teleport and clears instead of easing (prediction.cpp:173-180).
    /// </summary>
    [Fact]
    public void TooSmallAndTooBigADifferenceDoNotEaseTheEye()
    {
        var clock = new SmoothClock(1f);
        var small = BuildSmooth(clock, ringCommands: [11]);
        small.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        small.Player.State.Decode(small.Prediction.LastSavedBytes.ToArray());
        small.Player.State.Origin = new Vector(64.4f, 128f, 16f);   // 0.4, under the epsilon
        small.Packet();
        small.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(new Vector(64.4f, 128f, 16f), small.Replay.LastOrigin);
        Assert.Equal(0f, small.Replay.LastViewOffset.X);

        var big = BuildSmooth(clock, ringCommands: [11]);
        big.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        big.Player.State.Decode(big.Prediction.LastSavedBytes.ToArray());
        big.Player.State.Origin = new Vector(200f, 128f, 16f);      // 136, past the cap
        big.Packet();
        big.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(new Vector(200f, 128f, 16f), big.Replay.LastOrigin);
        Assert.Equal(0f, big.Replay.LastViewOffset.X);
    }

    /// <summary>
    /// The second error within the window sums onto what the eye still owes, at
    /// its half-decayed amount — stock sums all errors within the smoothing time
    /// onto the current remainder and restamps the clock
    /// (c_baseplayer.cpp:2648-2656). Two corrections, half a window apart, leave
    /// the second note at the new delta plus a half-decayed first one.
    /// </summary>
    [Fact]
    public void TwoErrorsWithinTheWindowSumOntoTheRemainder()
    {
        var clock = new SmoothClock(1f);
        var bench = BuildSmooth(clock,
                                smoothTime: ConVarOf(new ClSmoothTimeConVar(), "0.25"),
                                ringCommands: [11, 12]);

        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.Origin = new Vector(96f, 128f, 16f);
        bench.Packet();
        bench.Prediction.Update(0, true, 11, 12, worldUpdateArrived: true);
        Assert.Equal(-32f, bench.Replay.LastViewOffset.X);

        // The rebase replays command 12 from the corrected basis, so the next
        // slot's predicted origin is the corrected 96; the arrival lands twelve
        // further out, half a smoothing time after the first note.
        clock.Now = 1.125f;
        bench.Player.State.Origin = new Vector(128f, 128f, 16f);
        bench.Packet();
        bench.Prediction.Update(0, true, 12, 12, worldUpdateArrived: true);

        // The new delta (predicted 96 minus arrival 128) over the half-remained
        // first note: −32 + (−32 * 0.5) = −48, at the fresh note's full amount.
        Assert.Equal(new Vector(128f, 128f, 16f), bench.Replay.LastOrigin);
        Assert.Equal(-48f, bench.Replay.LastViewOffset.X);
    }

    /// <summary>
    /// <c>cl_smooth</c> off closes the ease; on, it holds. Stock reads the ConVar
    /// on every publish (c_baseplayer.cpp:2718), so the console decides.
    /// </summary>
    [Fact]
    public void ClSmoothOffClosesTheEaseAndOnHoldsIt()
    {
        var clock = new SmoothClock(1f);

        var off = new ClSmoothConVar();
        off.NotifyChanged("0", "1");
        var offBench = BuildSmooth(clock, smooth: off, ringCommands: [11]);
        offBench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        offBench.Player.State.Decode(offBench.Prediction.LastSavedBytes.ToArray());
        offBench.Player.State.Origin = new Vector(96f, 128f, 16f);
        offBench.Packet();
        offBench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(0f, offBench.Replay.LastViewOffset.X);

        var on = new ClSmoothConVar();
        on.NotifyChanged("1", "0");
        var onBench = BuildSmooth(clock, smooth: on, ringCommands: [11]);
        onBench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        onBench.Player.State.Decode(onBench.Prediction.LastSavedBytes.ToArray());
        onBench.Player.State.Origin = new Vector(96f, 128f, 16f);
        onBench.Packet();
        onBench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(-32f, onBench.Replay.LastViewOffset.X);
    }

    /// <summary>
    /// <c>cl_smoothtime</c> is the window: a quarter-second convar still owes half
    /// its offset a quarter-second in, where the declared default of 0.1 has
    /// finished. Stock's bounds (0.01..2.0) are held at the declaration.
    /// </summary>
    [Fact]
    public void ClSmoothtimeIsTheWindowTheEaseDecaysOver()
    {
        var clock = new SmoothClock(1f);
        var time = new ClSmoothTimeConVar();
        time.NotifyChanged("0.5", "0.1");

        var bench = BuildSmooth(clock, smoothTime: time, ringCommands: [11]);
        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.Origin = new Vector(96f, 128f, 16f);
        bench.Packet();
        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(-32f, bench.Replay.LastViewOffset.X);

        clock.Now = 1.25f;   // halfway through the half-second window
        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(-16f, bench.Replay.LastViewOffset.X);
    }

    /// <summary>
    /// Demo playback and a paused game close the ease, both of the checks stock's
    /// <c>GetPredictionErrorSmoothingVector</c> opens with
    /// (c_baseplayer.cpp:2718).
    /// </summary>
    [Fact]
    public void DemoAndPauseCloseTheEase()
    {
        var clock = new SmoothClock(1f);

        var paused = BuildSmooth(clock, playback: new Playback(paused: true), ringCommands: [11]);
        paused.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        paused.Player.State.Decode(paused.Prediction.LastSavedBytes.ToArray());
        paused.Player.State.Origin = new Vector(96f, 128f, 16f);
        paused.Packet();
        paused.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(0f, paused.Replay.LastViewOffset.X);

        var demo = BuildSmooth(clock, playback: new Playback(demo: true), ringCommands: [11]);
        demo.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        demo.Player.State.Decode(demo.Prediction.LastSavedBytes.ToArray());
        demo.Player.State.Origin = new Vector(96f, 128f, 16f);
        demo.Packet();
        demo.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(0f, demo.Replay.LastViewOffset.X);
    }

    /// <summary>A dead player notes nothing (stock's <c>IsAlive</c> arm, c_baseplayer.cpp:2644).</summary>
    [Fact]
    public void ADeadPlayerIsNotSmoothed()
    {
        var clock = new SmoothClock(1f);
        var bench = BuildSmooth(clock, ringCommands: [11]);
        bench.Prediction.Update(0, true, 10, 11, worldUpdateArrived: false);
        bench.Player.State.Decode(bench.Prediction.LastSavedBytes.ToArray());
        bench.Player.State.Origin = new Vector(96f, 128f, 16f);
        bench.Player.State.Alive = false;
        bench.Packet();
        bench.Prediction.Update(0, true, 11, 11, worldUpdateArrived: true);
        Assert.Equal(0f, bench.Replay.LastViewOffset.X);
    }

    /// <summary>The engine facts the smoothing gates on.</summary>
    private sealed class Playback(bool paused = false, bool demo = false) : IPlaybackState
    {
        public bool IsPaused => paused;
        public bool IsPlayingDemo => demo;
        public bool IsPlayingTimeDemo => false;
    }

    /// <summary>
    /// The scripted clock the smoothing facts pace on: float seconds exactly
    /// as the seam answers, so a scripted fraction is the number the fact chose.
    /// </summary>
    private sealed class SmoothClock(float now) : IGameClock
    {
        public float Now { get; set; } = now;
    }

    /// <summary>
    /// Binds a fresh convar instance to a value the way the registry's engine
    /// callback does, so a test-held convar reads that value rather than the
    /// zero an unbound instance defaults to.
    /// </summary>
    private static T ConVarOf<T>(T conVar, string value)
        where T : ConVarBase
    {
        conVar.NotifyChanged(value, "");
        return conVar;
    }
}

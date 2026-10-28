//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client;

//=============================================================================
// THE CLIENT'S PREDICTION, IN MANAGED CODE (plan_entity.md §3.9, step 6f).
//
// WHAT THIS REPLACES. C_StandalonePlayer -- a client entity subclass carrying
// sixty members of native movement state, a CPlayerState, a CPlayerLocalData,
// an IMovementPlayer vtable, a native prediction ring and the replay loop that
// drove all of it -- and the tracked-class half of shell_prediction.cpp beside
// it. What is left native is the IPrediction vtable, one crossing, and three
// asks (the input ring, the movement bracket, the published view).
//
// WHY IT IS THE PAYOFF OF §7. Step 3e put the WHOLE of ProcessMovement in C#
// and proved it bit-exact against 34 recorded commands of the native loop. Its
// sentence is: "One Movement on both sides removes the class of bug". Until
// this step the client reached that one copy through a native CGameMovement
// holding a native player object -- so the arithmetic was shared and the STATE
// it ran on was not. Here the state is the one C# declaration too
// (PlayerNetState), which is also the wire format and also the prediction
// schema. A field cannot be predicted differently from how it is sent, because
// there is one of it.
//
// THE SHAPE PER FRAME, stated once because every method below serves it:
//
//   packet:  the engine decodes -> ClientSsPlayer.State IS the server's word
//   Update:  ONE crossing in
//            compare ring[ack] against that state    (the misprediction check)
//            basis: the working MoveState <- the decoded state
//            for cmd in ack+1..outgoing:
//                UserCmd <- the engine's input ring
//                bracket open (constants in, trace slots armed)
//                GameMovement.ProcessMovement        (3e's loop, managed)
//                bracket closed (ground index out)
//                the weapon machine's phase 1        (managed, no crossing)
//                ring.SaveCommand(cmd, the state as bytes)
//            publish the view -> native draws it
//
// DELIBERATELY NOT WRITTEN INTO THE NATIVE BLOB, which is ClientArsenal's rule
// and holds here for the same reason. Stock can scribble predictions over
// networked memory because PreEntityPacketReceived restores the server's word
// before every packet decodes; without that restore a scribbled word the server
// never re-sends corrupts the client's view permanently, since the delta only
// carries what changed. The working copy keeps the prediction on this side of
// that hazard and leaves the decoded state the server's word by construction --
// which is also what makes the compare meaningful: it is comparing against
// something this side did not write.
//=============================================================================

/// <summary>
/// The local player's replay, its ring, and its meter.
/// </summary>
public sealed class ClientPlayerPrediction
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    /// <summary>
    /// <c>INVALID_NETWORKED_EHANDLE_VALUE</c> — the "standing on nothing" word.
    /// <c>game/shared/ground_handle_wire.h</c> is the declaration; this is its
    /// managed twin and <c>GroundWireTests</c> pins the two together.
    /// </summary>
    public const int GroundWireNone = (1 << 21) - 1;

    /// <summary>
    /// The ground WORD's entry-index mask, <c>MAX_EDICT_BITS</c> wide
    /// (<c>GROUNDWIRE_INDEX_MASK</c>). Eleven bits, because that is what the
    /// networked ehandle word this field imitates carries.
    /// </summary>
    public const int GroundWireIndexMask = (1 << 11) - 1;

    /// <summary>
    /// A HANDLE's entry index, which is a DIFFERENT WIDTH and is not the mask
    /// above. <c>ENT_ENTRY_MASK</c> (const.h:83) is sixteen bits, the low half
    /// of what <c>CBaseHandle::ToInt</c> packs; the ground word is eleven,
    /// because it imitates a networked ehandle and not a handle. Using the
    /// narrower one here would work for every player index this mod can have
    /// and truncate silently for anything above 2047, which is the shape of
    /// wrong answer this file has no other way to notice.
    /// </summary>
    public const uint HandleEntryMask = (1u << 16) - 1;

    private readonly IClientStandalones _standalones;
    private readonly IClientReplayEngine _engine;

    // The client composition's movement runner (plan_abi_hotreload D8): the
    // same loop the native crossing runs, over this side's engine.
    private readonly MovementRunner _movement;
    private readonly IEngineHud _screen;
    private readonly ClientArsenal? _arsenal;

    /// <summary>Where the HUD's debug line reads the view from; null in tests.</summary>
    private readonly ViewReadout? _view;

    /// <summary>
    /// The working copy the replay moves, and the ONLY thing the ring saves.
    /// Never the decoded state — see the file header.
    /// </summary>
    private readonly PlayerNetState _working = new();

    private readonly PredictionRing _ring = new();
    private byte[] _scratch = Array.Empty<byte>();

    /// <summary>
    /// The loop's state, PERSISTENT across commands and across frames, exactly
    /// as the native replay's members were.
    ///
    /// <para>
    /// The networked half of it is overwritten from the decoded state every
    /// frame (<see cref="ApplyBasis"/>); the rest — the sound clocks, the
    /// surface, the stuck counter, the ladder latch, the angles — is scratch
    /// the loop carries between commands and nothing networks. Rebuilding the
    /// whole object per frame would silently reset those, which is a class of
    /// drift with no symptom until somebody stands on a ladder.
    /// </para>
    /// </summary>
    private readonly MoveState _move = new()
    {
        IsServer = false,
        EntityGravity = 1f,
        MoveType = MoveTypeWalk,
    };

    /// <summary><c>MOVETYPE_WALK</c>. The state a player starts in.</summary>
    private const int MoveTypeWalk = 2;

    /// <summary>
    /// Where the LAST command's movement left them, so the next one can tell
    /// whether anything other than the loop has moved them since — which on this
    /// side is the once-a-frame basis reset.
    ///
    /// <para>
    /// Stock's <c>m_vecPreviouslyPredictedOrigin</c>, compared against the
    /// network origin at prediction.cpp:616 to raise
    /// <c>m_bGameCodeMovedPlayer</c>, which is what makes PlayerMove
    /// re-CategorizePosition instead of trusting the ground state it was handed
    /// (gamemovement.cpp:4766).
    /// </para>
    /// </summary>
    private Vector _previouslyPredictedOrigin;

    /// <summary>
    /// The highest command this side has ever run. <c>firstTime</c> is
    /// "the command has not been run before", which is what stock's
    /// <c>CUserCmd::hasbeenpredicted</c> means — kept here rather than asked of
    /// the engine's ring because command numbers only ever increase and this
    /// side is the only thing that runs them.
    /// </summary>
    private int _highestCommandRun = -1;

    // The meter.
    private int _saves, _holds, _tolerated, _mispredicts, _noBasis, _compares;
    private int _lastMissCommand = -1;
    private string _lastMissFields = "";
    private int _pokeOffset = -1;
    /// <summary>
    /// The player's decode count at the last compare.
    ///
    /// <para>
    /// ZERO, NOT -1, and the difference is a compare that must not happen: a
    /// packet can arrive that this entity was not in, and before the first
    /// decode the state is a zeroed blob rather than the server's word. Starting
    /// at -1 made the first world update judge an acknowledgement against
    /// nothing and report it as a no-basis — a meter counting an event that did
    /// not occur.
    /// </para>
    /// </summary>
    private int _lastComparedDecode;

    /// <summary>
    /// WHERE THE PREDICTED STATE STARTS INSIDE THE IMAGE, and the reason this
    /// number has to exist at all.
    ///
    /// <para>
    /// A player's networked image is 524 bytes and only the last 156 of them
    /// are <see cref="PlayerNetState"/>. The first 368 are the ENGINE BLOCK —
    /// the shell's networked pose, its handle, its model and render state,
    /// written by the engine's own decoder every update. Nothing predicts any
    /// of it, and it changes on every packet by design.
    /// </para>
    ///
    /// <para>
    /// <b>This is what the tree has been calling "padding" since 9c1.</b> Step
    /// 3e measured 86-91 mispredictions per window in BOTH arms, every one of
    /// them at a byte no declared field claimed, and concluded the raw count
    /// could not separate the arms — the offsets it named, 24-27 and 108-111,
    /// are inside this block. They were never padding: they were the engine
    /// writing the entity's pose, being compared as though the client had
    /// predicted it. A compare that includes them can never hold, which is
    /// exactly what the meter reported the first time this step ran it: 377
    /// compares, 377 "mispredictions", zero holds.
    /// </para>
    ///
    /// <para>
    /// So the compare below takes the block FROM THE ARRIVAL on both sides.
    /// Excluded by construction rather than forgiven by a tolerance: a
    /// tolerance says "this difference is small enough", and the honest
    /// statement is "this side never claimed to predict this".
    /// </para>
    /// </summary>
    private int _declaredStart = -1;

    // This composition's prediction ring and declared states (statics to zero):
    // the ring was the process-wide PredictionErrors.Ring.
    private readonly PredictionErrorRing _errors;
    private readonly NetStateCatalog _catalog;

    public ClientPlayerPrediction(ILog log, IClientStandalones standalones, IEngineHud screen,
                                  IClientReplayEngine engine, MovementRunner movement,
                                  ClientArsenal? arsenal, PredictionErrorRing errors,
                                  NetStateCatalog catalog, ViewReadout? view = null)
    {

        _log = log;
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _standalones = standalones;
        _screen = screen;
        _engine = engine;
        _movement = movement;
        _arsenal = arsenal;
        _view = view;

        _working.BindLocal();
    }

    /// <summary>Whether the replay runs at all — <c>cs_cl_player_predict</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How many commands the last frame replayed. The liveness reading.</summary>
    public int LastReplayed { get; private set; }

    /// <summary>Whether the last frame's replay is what native is drawing.</summary>
    public bool Driving { get; private set; }

    public int Saves => _saves;
    public int Holds => _holds;
    public int Tolerated => _tolerated;
    public int Mispredicts => _mispredicts;
    public int NoBasis => _noBasis;

    /// <summary>Compares actually made — the number a silent meter fails to raise.</summary>
    public int Compares => _compares;

    /// <summary>The fields of the last standing misprediction, or empty.</summary>
    public string LastMissFields => _lastMissFields;

    /// <summary>How wide one ring slot is. 0 before the first frame sizes it.</summary>
    public int RingBytes => _ring.BlobBytes;

    /// <summary>
    /// Where the predicted state starts inside a ring slot — the engine block's
    /// width. -1 before the first frame resolves it. See the field.
    /// </summary>
    public int DeclaredStartOffset => _declaredStart;

    /// <summary>
    /// The working copy's ground word — what the next ring save will carry for
    /// "what am I standing on".
    /// </summary>
    public int GroundWord => _working.GroundEntity;

    /// <summary>
    /// The bytes the last replayed command saved into the ring.
    ///
    /// <para>
    /// EXPOSED FOR THE FACTS, and it is the working copy's snapshot rather than
    /// a copy kept beside it: a second copy would be a second answer, and the
    /// property of the replay these facts hold still is precisely that the
    /// saved bytes and the working copy are the same bytes.
    /// </para>
    /// </summary>
    public ReadOnlySpan<byte> LastSavedBytes => _scratch;

    //-------------------------------------------------------------------------
    // The frame.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <c>IPrediction::Update</c>, forwarded whole. See the file header for the
    /// shape.
    /// </summary>
    /// <param name="validFrame">The engine vouching for the frame. Stock's own
    /// gate on running a replay at all.</param>
    /// <param name="worldUpdateArrived">A packet landed since the last call.
    /// Compares are gated on it AND on the player's own decode count: the first
    /// says the blobs hold the server's word, the second says the player's does
    /// — a packet can arrive that did not carry this entity.</param>
    public void Update(int startFrame, bool validFrame, int incomingAcknowledged,
                       int outgoingCommand, bool worldUpdateArrived)
    {
        LastReplayed = 0;

        ClientSsPlayer? player = FindLocalPlayer();

        if (player is null)
        {
            Driving = false;
            return;
        }

        int bytes = player.State.Snapshot().Length;

        if (bytes <= 0)
        {
            Driving = false;
            return;
        }

        if (_ring.BlobBytes != bytes)
        {
            _ring.Init(bytes);
            _scratch = new byte[bytes];
        }

        if (_declaredStart < 0)
            _declaredStart = PredictionJudge.DeclaredStart(typeof(PlayerNetState), bytes);

        // A fresh decode means the decoded state holds the server's word for
        // this acknowledgement, and the slot has something to be judged against.
        if (worldUpdateArrived && player.Decodes != _lastComparedDecode)
        {
            _lastComparedDecode = player.Decodes;
            Compare(player, incomingAcknowledged);
        }

        // THE BASIS, every frame, whether or not a packet arrived. That is what
        // makes the replay stateless: a frame with no new packet re-predicts the
        // same commands from the same start and lands on the same answer.
        ApplyBasis(player.State);

        bool predicting = Enabled && validFrame;
        bool ran = predicting && Replay(player, incomingAcknowledged, outgoingCommand);

        // NOTHING LEFT UNACKNOWLEDGED is still a predicted frame: the origin
        // is the basis, the server's latest word. Falling back to interpolation
        // here drew the predicted player from rings nobody fed while prediction
        // drove -- one frame in ~6 on a LAN looked from the spot before the last
        // teleport (NothingLeftUnacknowledgedDrivesAtTheDecodedOrigin).
        bool atBasis = predicting && !ran && incomingAcknowledged >= outgoingCommand;

        Driving = ran || atBasis;
        PublishView(player, Driving, fromReplay: ran);
    }

    /// <summary>
    /// Runs every command the server has not acknowledged. Answers whether any
    /// ran, which is what decides whether native draws the replay's origin or
    /// the interpolated one.
    /// </summary>
    private bool Replay(ClientSsPlayer player, int acknowledged, int outgoing)
    {
        // Phase 2 — the weapon machine's basis reset, once a frame, exactly as
        // the working copy above was reset. A DIRECT CALL and not a crossing:
        // before this step native drove the replay and had to cross to reach
        // the arsenal; now both are managed and the frame is one call graph.
        UserCmd basisReset = EmptyCommand(acknowledged);
        _arsenal?.OnPlayerCommand(player.Handle, in basisReset, 2, false);

        int ran = 0;

        for (int number = acknowledged + 1; number <= outgoing; number++)
        {
            if (!_engine.TryGetUserCmd(number, out UserCmd cmd))
                break;

            bool firstTime = number > _highestCommandRun;

            // A COMMAND THAT DID NOT RUN IS NOT A REPLAYED COMMAND, and the
            // distinction decides what native draws. RunOne answers false when
            // the bracket refused to open or the loop declined the move type;
            // counting either as replayed would set Driving, and native would
            // draw a predicted origin the replay never computed — stale by one
            // frame, and by more the longer the refusal lasts.
            if (!RunOne(player, in cmd, firstTime))
                break;

            if (firstTime)
                _highestCommandRun = number;

            if (_working.CopyTo(_scratch) == _scratch.Length && _ring.SaveCommand(number, _scratch))
            {
                _saves++;

                // A poke in flight damages every slot as it is saved, so the
                // next acknowledgement has a provably wrong basis — persistent
                // until a compare witnesses it, which is 9c1's one-shot-poke
                // lesson kept.
                if (_pokeOffset >= 0)
                    _ring.CorruptCommand(number, _pokeOffset);
            }

            if (++ran >= PredictionRing.Slots)
                break;
        }

        LastReplayed = ran;
        return ran > 0;
    }

    /// <summary>
    /// One replayed command: the loop, bracketed, and the state it produced back
    /// into the working copy.
    ///
    /// <para>
    /// The client flavour keeps the two differences the stock pair keeps.
    /// <c>OldAngles</c> is the PREVIOUS command's angles rather than the view
    /// angle (prediction.cpp:624), and the finished angles are never written
    /// back to the view — the view is this side's own authority every frame.
    /// </para>
    /// </summary>
    /// <returns>Whether the loop actually ran this command.</returns>
    /// <summary>The player the command being predicted moves (the move helper's host).</summary>
    public EntityHandle PredictingHost { get; private set; }

    /// <summary>Whether the command being predicted runs for the first time (g_pMoveData-&gt;m_bFirstRunOfFunctions).</summary>
    public bool FirstRunOfFunctions => _move.FirstRunOfFunctions;

    private bool RunOne(ClientSsPlayer player, in UserCmd cmd, bool firstTime)
    {
        PredictingHost = player.Handle;
        if (!_engine.BeginCommand(player.Handle, cmd.CommandNumber,
                                  out MoveFrameConstants constants))
        {
            return false;
        }

        bool ran = false;

        try
        {
            float tick = constants.Constants.TickInterval;

            _move.CommandNumber = cmd.CommandNumber;
            _view?.PublishAngles(cmd.Pitch, cmd.Yaw);
            _move.FrameTime = tick;
            _move.CurTime = _working.TickBase * tick;
            _move.FirstRunOfFunctions = firstTime;

            // Base velocity becomes real velocity — the server's
            // CheckMovingGround, and it must be here too or the replay diverges
            // the moment anything pushes the player.
            if ((_working.Flags & FlBaseVelocity) == 0)
            {
                _move.Velocity += (1f + (tick * 0.5f)) * _move.BaseVelocity;
                _move.BaseVelocity = Vector.Zero;
            }

            _move.Flags &= ~(int)FlBaseVelocity;

            _move.ForwardMove = cmd.ForwardMove;
            _move.SideMove = cmd.SideMove;
            _move.UpMove = cmd.UpMove;
            _move.Buttons = cmd.Buttons;
            _move.ImpulseCommand = cmd.Impulse;
            _move.ViewAngles = new Vector(cmd.Pitch, cmd.Yaw, cmd.Roll);

            // The previous command's angles, still sitting in the persistent
            // state — prediction.cpp:624's shape.
            _move.OldAngles = _move.Angles;
            _move.Angles = _move.ViewAngles;

            // "Did something other than the movement loop move me since the
            // last command?" — stock's CPrediction::SetupMove,
            // prediction.cpp:616. True on the FIRST replayed command after a
            // packet that moved the player and false for the rest of the frame,
            // which is exactly when PlayerMove must re-CategorizePosition rather
            // than trust the ground state it was handed
            // (gamemovement.cpp:4766).
            _move.GameCodeMovedPlayer = !Same(_previouslyPredictedOrigin, _move.Origin);

            _move.ClientMaxSpeed = _move.MaxSpeed;
            _move.LaggedMovementValue = 1f;
            _move.ModelScale = constants.ModelScale > 0f ? constants.ModelScale : 1f;
            _move.CurrentGravity = constants.CurrentGravity;
            _move.EntIndex = (int)(player.Handle.Value & HandleEntryMask);

            GameMovement loop = _movement.ClientLoop(constants.Constants,
                                                     constants.JumpHeight,
                                                     constants.EngineTime);

            ran = loop.ProcessMovement(_move);
            _movement.NoteClientCommand(ran);

            // Phase 1 — the weapon machine, over the same command the movement
            // just ran, exactly as the server runs it. A direct call: both
            // halves are managed now.
            //
            // INSIDE THE BRACKET, and that is what keeps the shared random
            // stream honest: the bracket holds this command's seed, and the
            // weapon machine is a SharedRandom* caller. Run after the bracket
            // closed it would run at seed -1 and disagree with the server about
            // every random decision — silently, and only under prediction.
            if (ran)
                _arsenal?.OnPlayerCommand(player.Handle, in cmd, 1, firstTime, _move.CurTime);
        }
        finally
        {
            int groundIndex = _engine.EndCommand(ran);

            if (ran)
                Finish(groundIndex);
        }

        return ran;
    }

    /// <summary>
    /// What one ran command leaves behind: the working copy, the ground word and
    /// the clock.
    /// </summary>
    private void Finish(int groundIndex)
    {
        // THE GROUND, and the one field that cannot round-trip as a value: the
        // loop carries "is there ground" and native remembers WHICH entity the
        // last standable trace found. The rule is
        // MovementGround_WriteBack's, spelled out: no ground clears the word,
        // ground with a named entity sets it, and ground with none named leaves
        // what was there — which is the LEAVE arm, and is what happens when no
        // trace this command found standable ground.
        if (!_move.HasGround)
            _working.GroundEntity = GroundWireNone;
        else if (groundIndex >= 0)
            _working.GroundEntity = groundIndex & GroundWireIndexMask;

        WriteBack(_working);

        // prediction.cpp:711's other half: the two origins agree from here until
        // something outside the loop moves the player.
        _previouslyPredictedOrigin = _move.Origin;

        _working.TickBase++;
    }

    //-------------------------------------------------------------------------
    // The basis and the write-back: PlayerNetState <-> MoveState.
    //
    // The managed twin of standalone_player_sync.cpp, and it is a smaller thing
    // than that file because there are no offsets to resolve: both sides are
    // C# objects and the compiler does the joining. What the native version had
    // to ask the managed side for by NAME, once per publish generation, is a
    // field access here.
    //-------------------------------------------------------------------------

    /// <summary><c>FL_BASEVELOCITY</c> (const.h, the multiplayer block this build compiles; 1 &lt;&lt; 5 there is FL_INRAIN).</summary>
    private const uint FlBaseVelocity = 1u << 24;

    /// <summary>
    /// The server's last word, into the loop's state. Every networked field,
    /// and nothing else: the scratch the loop carries between commands is not
    /// the server's to set.
    /// </summary>
    private void ApplyBasis(PlayerNetState arrived)
    {
        CopyState(arrived, _working);

        _move.MoveType = _working.MoveType;
        _move.ObserverMode = _working.ObserverMode;
        _move.Flags = (int)_working.Flags;
        _move.WaterLevel = _working.WaterLevel;
        _move.WaterType = _working.WaterType;
        _move.DeadFlag = !_working.Alive;
        _move.IsAlive = _working.Alive;

        _move.Origin = _working.Origin;
        _move.Velocity = _working.Velocity;
        _move.BaseVelocity = _working.BaseVelocity;
        _move.ViewOffset = _working.ViewOffset;
        _move.PunchAngle = _working.PunchAngle.AsVector();
        _move.PunchAngleVel = _working.PunchAngleVel.AsVector();
        _move.LadderNormal = _working.LadderNormal;
        _move.WaterJumpVel = _working.WaterJumpVel;

        _move.MaxSpeed = _working.MaxSpeed;
        _move.FallVelocity = _working.FallVelocity;
        _move.DuckTime = _working.DuckTime;
        _move.DuckJumpTime = _working.DuckJumpTime;
        _move.JumpTime = _working.JumpTime;
        _move.StepSize = _working.StepSize;
        _move.WaterJumpTime = _working.WaterJumpTime;
        _move.SurfaceFriction = _working.SurfaceFriction;
        _move.OldForwardMove = _working.OldForwardMove;
        _move.OldButtons = _working.OldButtons;
        _move.Health = _working.Health;

        _move.Ducked = _working.Ducked;
        _move.Ducking = _working.Ducking;
        _move.InDuckJump = _working.InDuckJump;
        _move.AllowAutoMovement = _working.AllowAutoMovement;

        // WHAT THEY WERE STANDING ON AT THE ACKNOWLEDGED COMMAND, and the basis
        // is wrong without it. FL_ONGROUND arrives in the flags word above; the
        // loop does not read that bit — every branch that matters asks whether
        // there is ground. Before the word was carried, the answer survived from
        // the last command of the previous frame's replay while the flags came
        // from the acknowledged one, so at every jump the replay stood with
        // FL_ONGROUND set and no ground, gamemovement.cpp:2475 refused the jump
        // the server had taken, and the next compare read ~150 u/s of Velocity
        // and a Flags word apart. ground_handle_wire.h carries the whole story.
        _move.HasGround = _working.GroundEntity != GroundWireNone;
    }

    /// <summary>
    /// What the loop produced, back into the working copy — which is the thing
    /// the ring saves and the thing the next compare is judged on.
    /// </summary>
    private void WriteBack(PlayerNetState into)
    {
        into.MoveType = (byte)_move.MoveType;
        into.Flags = (uint)_move.Flags;
        into.WaterLevel = (byte)_move.WaterLevel;
        into.WaterType = (byte)_move.WaterType;

        into.Origin = _move.Origin;
        into.Velocity = _move.Velocity;
        into.BaseVelocity = _move.BaseVelocity;
        into.ViewOffset = _move.ViewOffset;
        into.PunchAngle = QAngle.FromVector(_move.PunchAngle);
        into.PunchAngleVel = QAngle.FromVector(_move.PunchAngleVel);
        into.LadderNormal = _move.LadderNormal;
        into.WaterJumpVel = _move.WaterJumpVel;

        into.MaxSpeed = _move.MaxSpeed;
        into.FallVelocity = _move.FallVelocity;
        into.DuckTime = _move.DuckTime;
        into.DuckJumpTime = _move.DuckJumpTime;
        into.JumpTime = _move.JumpTime;
        into.StepSize = _move.StepSize;
        into.WaterJumpTime = _move.WaterJumpTime;
        into.SurfaceFriction = _move.SurfaceFriction;
        // OldForwardMove is NOT written back: stock's FinishMove stores only
        // the old buttons, and so does this server, so the networked value is
        // the move's input and never its output. Persisting it here made every
        // walking command a misprediction (predicted 320, server 0).
        into.OldButtons = _move.OldButtons;

        into.Ducked = _move.Ducked;
        into.Ducking = _move.Ducking;
        into.InDuckJump = _move.InDuckJump;
        into.AllowAutoMovement = _move.AllowAutoMovement;
    }

    private static void CopyState(NetState from, NetState to)
    {
        byte[] bytes = from.Snapshot();

        if (bytes.Length > 0)
            to.Decode(bytes);
    }

    /// <summary>
    /// Two origins, bit for bit. Not an epsilon: this decides whether the loop
    /// re-derives the ground state, and an epsilon here would be a window a real
    /// move could hide in.
    /// </summary>
    private static bool Same(in Vector a, in Vector b)
        => a.X.Equals(b.X) && a.Y.Equals(b.Y) && a.Z.Equals(b.Z);

    //-------------------------------------------------------------------------
    // The compare, and the meter.
    //-------------------------------------------------------------------------

    private void Compare(ClientSsPlayer player, int acknowledged)
    {
        _compares++;

        ReadOnlySpan<byte> ringed = _ring.CommandBytes(acknowledged);

        if (ringed.IsEmpty)
        {
            _noBasis++;
            return;
        }

        byte[] arrived = player.State.Snapshot();

        if (arrived.Length != ringed.Length)
        {
            _noBasis++;
            return;
        }

        ClearWitnessedPoke(acknowledged);

        uint typeId = NetSchema.TypeIdOf(EntityNames.SsPlayer);

        // THE ENGINE BLOCK COMES FROM THE ARRIVAL ON BOTH SIDES. See
        // _declaredStart: nothing predicts it, so comparing it would report the
        // engine's own writes as this side's mistakes — and did, on every
        // compare. The offsets stay ABSOLUTE, which is what lets the judge name
        // a field: a slice would renumber every one of them.
        byte[] predicted = PredictionJudge.OverlayDeclared(ringed, arrived, _declaredStart);

        if (predicted.AsSpan().SequenceEqual(arrived))
        {
            _holds++;
            return;
        }

        // THE VISUALISER'S CAPTURE, before the verdict and not after, so a
        // difference every tolerance forgave is still recorded. A tolerated
        // difference that is GROWING is the early warning for one that will not
        // be, and a capture that only fired on the standing case could not show
        // it.
        _errors.Observe(typeId, acknowledged, predicted, arrived, DateTime.UtcNow);

        int standing = PredictionJudge.Judge(_catalog, typeId, predicted, arrived,
                                             out string fields, out _);

        if (standing == 0)
        {
            _holds++;
            _tolerated++;
            return;
        }

        _mispredicts++;
        _lastMissCommand = acknowledged;
        _lastMissFields = fields.Length > 0 ? fields : "(unnamed)";

        // REPORTED A FEW TIMES AND THEN COUNTED SILENTLY, for ClientArsenal's
        // reason: a misprediction that repeats does so twenty times a second and
        // drowns the console for anyone looking at anything else. Nothing is
        // lost — the tally keeps counting and the report reads the last one.
        if (_mispredicts <= QuietAfter)
        {
            _log.Msg($"[SourceSharp] player prediction: MISPREDICTION at command {acknowledged} — " +
                    $"{standing} byte(s) stand: {_lastMissFields}" +
                    (_mispredicts == QuietAfter
                        ? " (quiet from here; the tally keeps counting)"
                        : "") + "\n");
        }
    }

    /// <summary>How many mispredictions are reported before the log goes quiet.</summary>
    public const int QuietAfter = 3;

    private void ClearWitnessedPoke(int acknowledged)
    {
        if (_pokeOffset < 0)
            return;

        _log.Msg($"[SourceSharp] player prediction: poke at offset {_pokeOffset} was seen by " +
                "the compare — poke cleared\n");
        _pokeOffset = -1;
    }

    /// <summary>
    /// Arms the poke: every future ring save is damaged at this offset until a
    /// compare witnesses it.
    ///
    /// <para>
    /// PERSISTENT AND NOT ONE-SHOT, which is 9c1's measured lesson: the newest
    /// slot is re-saved clean on every frame while its command is still the
    /// outgoing one, so the first live poke landed, was overwritten within a
    /// frame, and no compare ever saw it.
    /// </para>
    /// </summary>
    /// <param name="offset">
    /// AN OFFSET INTO THE PREDICTED STATE, not into the image. Zero is the
    /// first declared field, because the first 368 bytes of the image are the
    /// engine block and a poke landing there would be erased by the compare —
    /// a provocation the meter is guaranteed to ignore, which is a red path
    /// that cannot go red.
    /// </param>
    public bool Poke(int offset)
    {
        if (_ring.BlobBytes == 0 || _declaredStart < 0)
            return false;

        int at = _declaredStart + offset;

        if (offset < 0 || at >= _ring.BlobBytes)
            return false;

        _pokeOffset = at;
        return true;
    }

    /// <summary>
    /// EVERYTHING THROWN AWAY, for a new map.
    ///
    /// <para>
    /// The ring is keyed by command number and command numbers start again from
    /// the beginning on every map, so a slot kept across a level load would be
    /// compared against a different world's acknowledgement and reported as a
    /// misprediction — the same hazard <c>Interpolation.Clear</c> exists for,
    /// reached by the same route. The loop's scratch goes with it: a ladder
    /// latch or a water level carried into a map that has neither is state
    /// nothing will ever clear.
    /// </para>
    ///
    /// <para>
    /// The COUNTERS are kept deliberately. They are what a session is measured
    /// with, and a meter that forgot its tally at every map change could not
    /// answer "has this build ever mispredicted".
    /// </para>
    /// </summary>
    public void LevelInit()
    {
        _ring.Reset();
        _working.Decode(new byte[_working.Snapshot().Length]);

        _highestCommandRun = -1;
        _lastComparedDecode = 0;
        _previouslyPredictedOrigin = default;

        LastReplayed = 0;
        Driving = false;
        DrivenHandle = EntityHandle.None;
    }

    public string Report()
    {
        string line =
            $"[SourceSharp] player prediction: {(FindLocalPlayer() is not null ? "local player present" : "no local player")}, " +
            $"{_ring.BlobBytes} byte(s) ringed — {_saves} save(s), {_holds} hold(s) " +
            $"({_tolerated} tolerated), {_mispredicts} misprediction(s), {_noBasis} no-basis, " +
            $"{_compares} compare(s), {LastReplayed} replayed last frame, " +
            $"driving {(Driving ? "yes" : "no")}";

        if (_lastMissCommand >= 0)
            line += $"\n  last mismatch: command {_lastMissCommand}: {_lastMissFields}";

        return line;
    }

    //-------------------------------------------------------------------------
    // Bookkeeping.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The viewer's own player, or null.
    ///
    /// <para>
    /// Re-resolved per frame rather than held: the entity can arrive, die or be
    /// replaced between any two frames, and a stale reference is the shape of
    /// bug that survives a whole session.
    /// </para>
    ///
    /// <para>
    /// THROUGH THE DIRECTORY'S CLASSNAME INDEX rather than over every live
    /// entity — <c>plan_entity.md</c> §3.5's find-by-class, lane
    /// <c>entlist-client</c>. This used to walk the whole live list once per
    /// frame, and on dm_lockdown the whole live list is 480 entities where the
    /// players are four. The type test is KEPT behind the index, so a classname
    /// that somehow named something else answers null rather than a bad cast.
    /// </para>
    /// </summary>
    private ClientSsPlayer? FindLocalPlayer()
    {
        int local = _screen.LocalPlayerIndex;

        if (local <= 0)
            return null;

        foreach (var entity in _standalones.OfClass(EntityNames.SsPlayer))
        {
            if (entity is ClientSsPlayer player &&
                (int)(player.Handle.Value & HandleEntryMask) == local)
            {
                return player;
            }
        }

        return null;
    }

    /// <summary>
    /// The command the weapon machine's basis reset carries: a command number
    /// and nothing else. Phase 2 reads no input, and handing it a real command
    /// would be handing it one it must not run.
    /// </summary>
    private static UserCmd EmptyCommand(int commandNumber)
    {
        UserCmd cmd = default;
        cmd.CommandNumber = commandNumber;
        return cmd;
    }

    /// <summary>
    /// The entity the replay last said it drives (<see cref="EntityHandle.None"/>
    /// when the last published view did not drive): what the interpolation pass
    /// leaves out (<see cref="InterpolatedSet"/>), set with the same publish
    /// that tells the shell to draw the replay's origin.
    /// </summary>
    public EntityHandle DrivenHandle { get; private set; }

    private void PublishView(ClientSsPlayer player, bool driving, bool fromReplay)
    {
        DrivenHandle = driving ? player.Handle : EntityHandle.None;

        // The DECODED punch and eye when nothing was replayed, the replay's when
        // something was. Both are the right answer for their case, and
        // publishing every frame is what keeps the view following a player whose
        // prediction is switched off. `driving` says whether native draws this
        // origin (a replay, or the basis with nothing left to replay) rather
        // than the interpolated one.
        Vector origin = fromReplay ? _move.Origin : player.State.Origin;
        Vector viewOffset = fromReplay ? _move.ViewOffset : player.State.ViewOffset;
        QAngle punch = fromReplay ? QAngle.FromVector(_move.PunchAngle) : player.State.PunchAngle;
        QAngle punchVel = fromReplay
            ? QAngle.FromVector(_move.PunchAngleVel)
            : player.State.PunchAngleVel;

        _view?.PublishEye(origin + viewOffset);

        // THE HEALTH IS THE DECODED ONE EITHER WAY, and deliberately: it is
        // the server's word about a number the client cannot predict — damage
        // is the server's decision — so the replay has nothing better to say
        // about it than the packet does.
        _engine.PublishView(player.Handle, in origin, in viewOffset, in punch, in punchVel,
                            driving, player.State.Health);
    }
}

/// <summary>
/// <c>cs_cl_player_predict_report</c> — what the player's prediction ring has
/// seen. The console door onto the meter, and the twin of
/// <c>cs_cl_weapon_predict_report</c>.
/// </summary>
[ConCommand("cs_cl_player_predict_report",
            Help = "What the standalone player's prediction ring has seen")]
public sealed class PlayerPredictReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ClientPlayerPrediction _prediction;

    public PlayerPredictReportCommand(ILog log, ClientPlayerPrediction prediction)        

    {

        _log = log;

        _prediction = prediction;

    }

    public void Execute(string args) => _log.Msg(_prediction.Report() + "\n");
}

/// <summary>
/// <c>cs_cl_player_predict_poke</c> — the provocation that proves the meter can
/// go red. Corrupts the replayed slots at one offset until a compare sees it.
/// </summary>
[ConCommand("cs_cl_player_predict_poke",
            Help = "cs_cl_player_predict_poke [offset] — corrupt the player's replayed slots " +
                   "at one offset until a compare witnesses it")]
public sealed class PlayerPredictPokeCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ClientPlayerPrediction _prediction;

    public PlayerPredictPokeCommand(ILog log, ClientPlayerPrediction prediction)        

    {

        _log = log;

        _prediction = prediction;

    }

    public void Execute(string args)
    {
        int offset = int.TryParse(args.Trim(), out int parsed) ? parsed : 0;

        _log.Msg(_prediction.Poke(offset)
            ? $"[SourceSharp] player prediction: poking offset {offset} until a compare sees it\n"
            : "[SourceSharp] player prediction: nothing ringed at that offset\n");
    }
}

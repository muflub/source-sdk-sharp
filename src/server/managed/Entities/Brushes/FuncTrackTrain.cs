//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A brush that runs a <see cref="PathTrack"/>, turning as it goes, in either
/// direction, at a speed something else decides.
///
/// The difference from <see cref="FuncTrain"/> is not that it is bigger. A
/// func_train is a sequence of ERRANDS — go there, wait, go there — and the map
/// wires the sequence. A track train has no sequence at all: it has a direction
/// and a speed, and every question a map asks it is about one of those two. That
/// is why it has ten inputs and none of them is "go to the next one", why its
/// nodes carry a speed and no wait, and why its track is linked both ways.
///
/// <para>
/// Driven leg by leg between nodes rather than a velocity re-aimed every tick,
/// which is what stock does. The track is a chain of straight segments either
/// way, so the PATH is the same one; what a leg cannot express is anything that
/// changes DURING it, and stock has three such things — acceleration, the
/// velocity blends, and banking into a corner. Each is written down at the key
/// that asks for it.
/// </para>
///
/// <para>
/// Turning is the one continuous thing that survived, because there is a
/// duration to spend it over: the leg starts, the facing is aimed at wherever
/// the front of the train will be, and the turn is given the time the train
/// takes to travel its own wheelbase. A long train therefore begins rounding a
/// corner before its middle reaches it, which is what the wheelbase is for.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncTracktrain, ScriptedEntityKind.Brush)]
public sealed class FuncTrackTrain : TrainBrush
{
    public FuncTrackTrain(IGameClock clock, IEngineDamage damage,
                          IEnginePrecache precache, IEngineSound sound)
        : base(clock, damage, precache, sound)
    {
    }

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>Fired the first time the train is asked to move.</summary>
    public readonly Output OnStart = new();

    /// <summary>Fired on reaching every node, stock's OnNextPoint.</summary>
    public readonly Output OnNextPoint = new();

    /// <summary>What a map may set on this entity. See <see cref="FuncTrackTrainVars"/>.</summary>
    [ScriptVars] public FuncTrackTrainVars FuncTrackTrainVars = new();

    private PathTrack? _node;
    private PathTrack? _target;

    /// <summary>Signed: the sign is the direction, so it and <see cref="_dir"/> agree.</summary>
    private float _speed;

    private float _oldSpeed;
    private int _dir = 1;
    private float _forwardModifier = 1f;
    private bool _started;
    private bool _soundPlaying;

    /// <summary>
    /// Stock's SF_TRACKTRAIN_NOCONTROL, held here because a node can SET it and a
    /// managed entity cannot write its own spawnflags.
    ///
    /// It has one consequence left. The flag means "the player is not driving",
    /// and stock uses it to decide whether a node's speed may override the
    /// train's — a train under a player's hand must not be re-throttled by the
    /// track it is running over. Nothing here is ever under a player's hand (see
    /// docs/divergences.md), so the flag now only chooses whether node speeds are
    /// read, and it is kept because that is the choice a map made.
    /// </summary>
    private bool _noControl;

    //-------------------------------------------------------------------------
    // The volume a player has to be standing in to be at the controls, in the
    // TRAIN's own frame.
    //
    // Local rather than world, and that is the whole trick: stored once in the
    // train's frame it needs no updating as the train runs, because every test
    // re-derives the world position from wherever the train is now. A
    // func_traincontrols therefore hands this over at level load and is never
    // read again — see FuncTrainControls.
    //-------------------------------------------------------------------------
    private Vector _controlMins;

    /// <summary>
    /// The roof of the driving volume, raised above the train's footprint by a
    /// player's height. Measured ONCE, in <see cref="OnSpawn"/>, which a hot
    /// reload does not run again -- so it is <c>[SaveField]</c>. Uncarried, the
    /// rebuilt instance held <c>Vector.Zero</c> and nobody could steer the
    /// train after a <c>cs_sv_reload</c> unless a func_traincontrols happened to
    /// hand its box over again (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private Vector _controlMaxs;

    /// <summary>
    /// func_tracktrain's own spawnflags, stock's SF_TRACKTRAIN_*.
    ///
    /// <para>
    /// One domain: everything a mapper can tick on a track train, read off one
    /// word. Worth the type mostly for the low four, which collide numerically with
    /// the flags of every other brush that runs on tracks -- 8 is "passable" here
    /// and on func_train, but 8 is "start at the bottom" on func_trackchange, and
    /// all three are brushes a mapper places along the same track.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000_0000_0000,

        NoPitch = 0b0000_0000_0000_0001,
        NoControl = 0b0000_0000_0000_0010,
        ForwardOnly = 0b0000_0000_0000_0100,
        Passable = 0b0000_0000_0000_1000,
        FixedOrientation = 0b0000_0000_0001_0000,
        UseMaxSpeedForPitch = 0b0000_0001_0000_0000,  // 0x100
        UnblockableByPlayer = 0b0000_0010_0000_0000,  // 0x200
    }

    /// <summary>The node the train last reached. Null until it has found its first.</summary>
    public PathTrack? CurrentNode => _node;

    /// <summary>The node it is travelling to, or null when it is not travelling.</summary>
    public PathTrack? TargetNode => _target;

    /// <summary>Signed, so its sign is the direction of travel.</summary>
    public float CurrentSpeed => _speed;

    public float MaxSpeed => FuncTrackTrainVars.MaxSpeed;

    /// <summary>
    /// How long the train is, as far as anything else is concerned. Read by a
    /// track changer, which uses it as the distance within which a train counts as
    /// parked on the changer rather than merely near it.
    /// </summary>
    internal float WheelBase => FuncTrackTrainVars.WheelBase;

    public bool IsDirForward => _dir == 1;

    //-------------------------------------------------------------------------
    // A track train has no wait anywhere, so the base's noise2 is stock's
    // StopSound and the arrival hook it belongs to is SoundStop.
    //-------------------------------------------------------------------------
    protected override (string Moving, string Arriving) SoundNames => (FuncTrackTrainVars.SoundMove, FuncTrackTrainVars.SoundStop);

    /// <summary>
    /// Zero, and it matters. A func_train with no speed key cannot be told one,
    /// so the base fills in a hundred; here zero is how a map says "sit still
    /// until something starts me", which is what most track trains do.
    /// </summary>
    protected override float DefaultSpeed => 0f;

    /// <summary>Also zero: stock's track train crushes for nothing unless the map says.</summary>
    protected override float DefaultBlockDamage => 0f;

    public override void OnSpawn()
    {
        // Read before the base sees it, because the base's job is the moving
        // loop and this is the speed the train sets off at.
        float keyedSpeed = Vars.Speed;

        base.OnSpawn();

        _speed = keyedSpeed;

        // Stock's ladder: the map's max, or the speed it starts at, or a hundred.
        if (FuncTrackTrainVars.MaxSpeed == 0f)
            FuncTrackTrainVars.MaxSpeed = keyedSpeed != 0f ? keyedSpeed : 100f;

        if (FuncTrackTrainVars.PitchMin == 0)
            FuncTrackTrainVars.PitchMin = 60;

        if (FuncTrackTrainVars.PitchMax == 0)
            FuncTrackTrainVars.PitchMax = 200;

        _noControl = Flags.HasFlag(SpawnFlag.NoControl);

        // A train the player walks through. Stock's own comment on the flag says
        // it is for water.
        if (Flags.HasFlag(SpawnFlag.Passable))
            SetSolidity(BrushSolidity.None);

        if (FuncTrackTrainVars.SoundStart.Length > 0)
            Precache.PrecacheSound(FuncTrackTrainVars.SoundStart);

        if (FuncTrackTrainVars.SoundPing.Length > 0)
            Precache.PrecacheSound(FuncTrackTrainVars.SoundPing);

        // The default control volume: the train's own footprint, and a player's
        // height above it. Stock's, and it is what a train with no
        // func_traincontrols gets — standing anywhere on the roof counts.
        (_controlMins, _controlMaxs) = Entities.GetBounds(Handle);
        _controlMaxs = new Vector(_controlMaxs.X, _controlMaxs.Y, _controlMaxs.Z + 72f);

        if (FuncTrackTrainVars.FirstNodeName.Length == 0)
            Log.Warning($"[SourceSharp] func_tracktrain '{Name}' has no target, so it has no track\n");

        // Deferred, and the only window there is: a train names a node and
        // managed entities spawn together in no order worth relying on. A node's
        // own link think is armed for a tenth of a second, so by now the chain
        // exists — and Find joins it anyway, for a train made from the console
        // after the nodes were.
        SetNextThink(0.2f);
    }

    /// <summary>The deferred start, which is the only reason this think exists.</summary>
    protected override void OnIdleThink() => Find();

    /// <summary>A leg ended. Stock's arrival at a node, which is most of the class.</summary>
    protected override void OnLegDone() => Arrive();

    //-------------------------------------------------------------------------
    // Finding the track.
    //-------------------------------------------------------------------------

    /// <summary>
    /// What stock does in its own deferred Find: land on the first node, face
    /// along the track, announce the arrival, and set off if the map gave it a
    /// speed.
    ///
    /// Only ever once, because a train that found its track twice would jump back
    /// to the start of it.
    /// </summary>
    private void Find()
    {
        if (_started)
            return;

        var first = FindByName<PathTrack>(FuncTrackTrainVars.FirstNodeName);

        if (first is null)
        {
            _started = true;

            // Stock says "must be on a path of path_track" and asserts, because
            // it has already blind-cast whatever it found. Here the lookup is
            // typed, so a name that resolved to something else is the same case
            // as a name that resolved to nothing.
            Log.Warning($"[SourceSharp] func_tracktrain '{Name}': " +
                           $"no path_track named '{FuncTrackTrainVars.FirstNodeName}'\n");
            return;
        }

        StartOn(first);
    }

    /// <summary>
    /// The same, with the node already in hand — which is how a test reaches
    /// everything below, the lookup above being the only part that needs a map.
    /// </summary>
    internal void StartOn(PathTrack first)
    {
        if (_started)
            return;

        _started = true;

        // Before anything walks it. A node links itself from its own think, but a
        // train made from the console after the track was placed gets here first.
        first.LinkChain();

        _node = first;

        Origin = PositionOf(first);

        if (!Flags.HasFlag(SpawnFlag.FixedOrientation))
            SetAngles(FacingAt(first));

        ArriveAtNode(first);

        if (_speed != 0f)
            Next();
    }

    //-------------------------------------------------------------------------
    // The legs.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Sets off for the next node in the current direction, or ends at a dead
    /// end. Stock's Next, which it calls every tick and this calls once a leg.
    /// </summary>
    private void Next()
    {
        if (_speed == 0f)
        {
            SoundStop();
            return;
        }

        if (_node is null)
        {
            // Stock's "lost path", which happens when a track change takes the
            // train's node away from under it.
            SoundStop();
            _speed = 0f;
            return;
        }

        PathTrack? ahead = PathTrack.ValidPath(_node.NextKey(_dir));

        if (ahead is null)
        {
            DeadEnd();
            return;
        }

        SoundStart();

        _target = ahead;

        float speed = MathF.Abs(_speed);
        Vector destination = PositionOf(ahead);
        float legSeconds = (destination - Origin).Length / speed;

        TravelTo(destination, speed);
        AimAlongLeg(legSeconds, speed);
    }

    /// <summary>
    /// Arriving at the node the leg was aimed at.
    ///
    /// The teleport check reads the node AFTER the one just reached and does not
    /// take the direction into account, which is stock's asymmetry rather than an
    /// oversight here: the flag marks a place the track jumps FROM, and stock
    /// only ever wired it forward.
    /// </summary>
    private void Arrive()
    {
        PathTrack? reached = _target;

        _target = null;

        if (reached is null)
        {
            Next();
            return;
        }

        _node = reached;

        ArriveAtNode(reached);

        OnNextPoint.Fire();

        PathTrack? teleport = reached.GetNext();

        if (teleport is not null && teleport.Teleport)
            TeleportToNode(teleport);

        Next();
    }

    /// <summary>
    /// What reaching a node means to the train, as opposed to where it goes next.
    ///
    /// Three things, and the order is stock's: whoever the mapper wired to the
    /// node hears about it, the node may take the train out of the player's hands
    /// permanently, and only THEN may the node's own speed override the train's.
    /// </summary>
    private void ArriveAtNode(PathTrack node)
    {
        node.Pass(Handle);

        if (node.DisablesTrain)
            _noControl = true;

        // See the note on _noControl. Zero is a node with nothing to say about
        // speed, not a node asking for a stop.
        if (_noControl && node.NodeSpeed != 0f)
            RecordSpeed(node.NodeSpeed);
    }

    /// <summary>
    /// The end of the track.
    ///
    /// Stock walks to the far end of the chain here, because a velocity-driven
    /// train can stop past the last node and has to find it again. This one stops
    /// exactly on it, so the node it is holding IS the end — but the node's
    /// OnPass still fires a second time, which is what stock does and what a map
    /// counting passes was built against.
    /// </summary>
    private void DeadEnd()
    {
        Halt();
        SoundStop();

        _oldSpeed = _speed;
        _speed = 0f;
        _target = null;

        _node?.Pass(Handle);
    }

    /// <summary>Where the train's origin goes for it to be riding on a node.</summary>
    private Vector PositionOf(PathTrack node) => node.Position + new Vector(0f, 0f, FuncTrackTrainVars.Height);

    /// <summary>
    /// Jumps to a node rather than travelling to it, and tells the node so.
    ///
    /// The node becomes the one the train is on, which stock's own teleport does
    /// not do — it moves the brush and leaves the node it was walking from
    /// behind, so the next look-ahead measures from one place and starts from
    /// another. Carrying the node across is the reading that leaves the train
    /// somewhere it can continue from.
    /// </summary>
    private void TeleportToNode(PathTrack node)
    {
        Halt();

        _node = node;
        _target = null;

        Origin = PositionOf(node);

        if (!Flags.HasFlag(SpawnFlag.FixedOrientation))
            SetAngles(FacingAt(node));

        node.Teleported(Handle);
    }

    //-------------------------------------------------------------------------
    // Which way it is pointing.
    //-------------------------------------------------------------------------

    private TrainOrientation OrientationType
        => Flags.HasFlag(SpawnFlag.FixedOrientation) ? TrainOrientation.Fixed
                                                   : (TrainOrientation)FuncTrackTrainVars.OrientationType;

    /// <summary>How far ahead the front of the train is. Stock's stand-in is a hundred.</summary>
    private float LookDistance => FuncTrackTrainVars.WheelBase > 0f ? FuncTrackTrainVars.WheelBase : 100f;

    /// <summary>
    /// The facing the train should have while sitting on a node, which is where
    /// it will be pointing once its front wheels are past.
    /// </summary>
    private Vector FacingAt(PathTrack node)
    {
        var ahead = node.LookAheadFrom(node.Position, _dir * LookDistance, respectDisabled: false);

        Vector face = ahead.Origin - node.Position;

        // A train going backward keeps pointing the way it came: the look-ahead
        // walked backward along the track, so the direction has to be flipped to
        // stay the train's nose rather than its tail.
        if (_dir < 0)
            face = face * -1f;

        // Nowhere to point, so stay as we are. Stock's guard, and it covers both
        // the dead end and a purely vertical segment.
        if (face.X == 0f && face.Y == 0f)
            return Entities.GetAngles(Handle);

        Vector angles = Angles.FromVector(face);

        // A node that says so hands back the mapper's own facing instead of the
        // one the geometry implies. Gated on ManualSpeedChanges in stock, which
        // is how the newer behaviour was kept away from old maps.
        if (FuncTrackTrainVars.ManualSpeedChanges && ahead.NextNext?.OrientationType == TrackOrientation.FacePathAngles)
            angles = ahead.NextNext.GetOrientation(IsDirForward);

        return WithoutPitchChange(angles);
    }

    /// <summary>
    /// Starts the turn for the leg that has just begun.
    ///
    /// The turn is given the time the train takes to cover its own wheelbase
    /// rather than the whole leg, which is what makes a corner read as a corner:
    /// spread over a long straight leg the train would be pointing slightly wrong
    /// for all of it. The two blend orientation types are the exception — those
    /// ARE meant to run the length of the segment.
    /// </summary>
    private void AimAlongLeg(float legSeconds, float speed)
    {
        if (OrientationType == TrainOrientation.Fixed || _target is null)
            return;

        Vector wanted;
        float seconds;

        if (OrientationType == TrainOrientation.AtPathTracks)
        {
            wanted = FacingAt(_target);
            seconds = MathF.Min(legSeconds, LookDistance / speed);
        }
        else
        {
            wanted = WithoutPitchChange(_target.GetOrientation(IsDirForward));
            seconds = legSeconds;
        }

        Vector current = Entities.GetAngles(Handle);

        float largest = MathF.Max(MathF.Abs(AngleDelta(wanted.X, current.X)),
                        MathF.Max(MathF.Abs(AngleDelta(wanted.Y, current.Y)),
                                  MathF.Abs(AngleDelta(wanted.Z, current.Z))));

        // Under a tenth of a degree is stock's "close enough", and it is there to
        // stop a train that is already straight from being given a rotation at
        // all — which at these durations would be a divide by nearly zero.
        if (largest < 0.1f || seconds <= 0f)
            return;

        RotateTo(wanted, largest / seconds);
    }

    /// <summary>Stock's NOPITCH: the pitch the train has is the pitch it keeps.</summary>
    private Vector WithoutPitchChange(Vector angles)
    {
        if (!Flags.HasFlag(SpawnFlag.NoPitch))
            return angles;

        return new Vector(Entities.GetAngles(Handle).X, angles.Y, angles.Z);
    }

    /// <summary>The shortest way round from one angle to another, signed.</summary>
    private static float AngleDelta(float to, float from)
    {
        float delta = (to - from) % 360f;

        if (delta > 180f)
            delta -= 360f;
        else if (delta < -180f)
            delta += 360f;

        return delta;
    }

    //-------------------------------------------------------------------------
    // Speed and direction.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Sets the speed, and does whatever the change implies: starting, carrying
    /// on at a new speed, or stopping.
    ///
    /// The magnitude is all the caller gets to say — the SIGN comes from the
    /// train's direction, which is what makes SetSpeed and Reverse independent.
    /// </summary>
    internal void SetSpeed(float speed)
    {
        float before = _speed;

        RecordSpeed(speed);

        if (_speed == before)
            return;

        if (_speed == 0f)
        {
            Stop();
            return;
        }

        if (before == 0f)
            Start();
        else
            Relaunch();
    }

    /// <summary>
    /// The speed, without acting on it.
    ///
    /// Arriving at a node is the one place stock's SetSpeed is called by a caller
    /// that is about to set the next leg going anyway, and letting it launch one
    /// as well would run two legs out of the same node.
    /// </summary>
    private void RecordSpeed(float speed)
    {
        // The modifier only slows the forward direction, which is what stock's
        // own comment says it is for: a payload cart pushed uphill.
        if (_forwardModifier < 1f && _dir > 0)
            speed *= _forwardModifier;

        _speed = MathF.Abs(speed) * _dir;
    }

    private void Start()
    {
        OnStart.Fire();
        Next();
    }

    private void Stop()
    {
        _oldSpeed = _speed;
        _speed = 0f;
        _target = null;

        Halt();
        SoundStop();
    }

    /// <summary>
    /// Re-runs the leg in flight at the speed it now has.
    ///
    /// This is where the leg model shows: stock changes the velocity and the
    /// clock takes care of itself, and here the clock has to be recomputed from
    /// where the train has actually got to. Which is also why it is correct after
    /// a reversal — the leg it re-runs is the new one.
    /// </summary>
    private void Relaunch()
    {
        if (_target is null)
        {
            Next();
            return;
        }

        float speed = MathF.Abs(_speed);
        Vector destination = PositionOf(_target);
        float legSeconds = (destination - Origin).Length / speed;

        TravelTo(destination, speed);
        AimAlongLeg(legSeconds, speed);
    }

    /// <summary>
    /// Turns the train round, which swaps which node is behind it and which is
    /// ahead.
    ///
    /// Stock steps its single node pointer one place the other way and calls that
    /// the reversal, because it only remembers the node BEHIND. Two are held
    /// here, so the reversal is the swap it was approximating — and the train
    /// goes back to the node it came from rather than to that node's neighbour.
    /// </summary>
    internal void SetDirForward(bool forward)
    {
        int wanted = forward ? 1 : -1;

        if (_dir == wanted)
            return;

        _dir = wanted;
        _speed = MathF.Abs(_speed) * _dir;

        if (_target is not null)
            (_node, _target) = (_target, _node);
    }

    /// <summary>
    /// Puts the train on a track, at whichever of its nodes is nearest.
    ///
    /// Stock's SetTrack, and its only caller is a track change handing the train
    /// over to the branch it has just swung into place.
    /// </summary>
    internal void SetTrack(PathTrack track) => _node = track.Nearest(Origin);

    /// <summary>
    /// Puts the train in the state a node's disable-train flag would have.
    ///
    /// Within the assembly and for the tests only: the flag is read from the
    /// engine, and there is none in a test, so the branch that reads node speeds
    /// would otherwise be unreachable and every case would pass for the same wrong
    /// reason.
    /// </summary>
    internal void TakeOutOfPlayersHands() => _noControl = true;

    /// <summary>Within the assembly so a track change can take the train's node away, as stock does.</summary>
    internal void LosePath()
    {
        _node = null;
        _target = null;
    }

    //-------------------------------------------------------------------------
    // The controls.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Takes a control box's volume, expressed in this train's frame.
    ///
    /// A managed method call rather than the pointer hand-off stock does, and the
    /// arithmetic is the part that changed: stock reads the box's PARENT-RELATIVE
    /// origin, which is already the train-local offset because a mapper parents
    /// the box to the train. There is no parenting on this hierarchy, so the same
    /// offset is derived instead — the box's map position against the train's,
    /// rotated into the train's frame. For the axis-aligned pair a mapper
    /// actually builds, the two are the same number.
    /// </summary>
    internal void SetControls(FuncTrainControls controls)
    {
        Vector offset = ToLocal(SpawnAngles, controls.MapOrigin - SpawnOrigin);

        var (mins, maxs) = controls.Box;

        _controlMins = mins + offset;
        _controlMaxs = maxs + offset;
    }

    /// <summary>
    /// Whether something is standing at the controls.
    ///
    /// <para>
    /// Correct, and unreachable. Stock's two callers are
    /// <c>CBasePlayer::PlayerUse</c> and <c>CBasePlayer::HandleFuncTrain</c>, and
    /// both reach the train through <c>GetGroundEntity()</c> — which can never be
    /// a parallel-hierarchy brush, because the movement code resolves the ground
    /// through a cast that answers nothing for one. So no player is ever offered
    /// a managed train to take the controls of, and nothing calls this. It is
    /// here because the volume is the half that CAN be got right, and the half
    /// that cannot is not this class's to fix. See docs/divergences.md.
    /// </para>
    /// </summary>
    public bool OnControls(EntityHandle tester)
    {
        if (_noControl)
            return false;

        Vector local = ToLocal(Entities.GetAngles(Handle),
                               Entities.GetOrigin(tester) - Origin);

        return local.X >= _controlMins.X && local.X <= _controlMaxs.X
            && local.Y >= _controlMins.Y && local.Y <= _controlMaxs.Y
            && local.Z >= _controlMins.Z && local.Z <= _controlMaxs.Z;
    }

    /// <summary>
    /// A world offset in the frame of something facing <paramref name="angles"/>.
    ///
    /// The Y component is against the LEFT and not the right, which looks like a
    /// sign error and is not: Source's rotation matrix has forward, left and up
    /// for its columns, so transposing it dots against those three. Using the
    /// right would mirror the box, and a control volume drawn on one side of a
    /// cab would answer for the other.
    /// </summary>
    internal static Vector ToLocal(Vector angles, Vector worldOffset)
    {
        var basis = Angles.Vectors(angles);

        return new Vector(worldOffset.Dot(basis.Forward),
                          -worldOffset.Dot(basis.Right),
                          worldOffset.Dot(basis.Up));
    }

    //-------------------------------------------------------------------------
    // The inputs.
    //-------------------------------------------------------------------------
    [ScriptedEvent("Stop")] private void OnStopInput() => Stop();

    [ScriptedEvent("Resume")]
    private void OnResume()
    {
        _speed = _oldSpeed;
        Start();
    }

    [ScriptedEvent("Reverse")]
    private void OnReverse()
    {
        SetDirForward(!IsDirForward);
        Relaunch();
    }

    [ScriptedEvent("StartForward")]
    private void OnStartForward()
    {
        SetDirForward(true);
        SetSpeed(FuncTrackTrainVars.MaxSpeed);
    }

    [ScriptedEvent("StartBackward")]
    private void OnStartBackward()
    {
        SetDirForward(false);
        SetSpeed(FuncTrackTrainVars.MaxSpeed);
    }

    [ScriptedEvent("Toggle")]
    private void OnToggle() => SetSpeed(_speed == 0f ? FuncTrackTrainVars.MaxSpeed : 0f);

    /// <summary>A fraction of the maximum, which is what the FGD says this takes.</summary>
    [ScriptedEvent("SetSpeed")]
    private void OnSetSpeed(float value) => SetSpeed(FuncTrackTrainVars.MaxSpeed * Math.Clamp(value, 0f, 1f));

    /// <summary>Units per second, clamped to the maximum.</summary>
    [ScriptedEvent("SetSpeedReal")]
    private void OnSetSpeedReal(float value) => SetSpeed(Math.Clamp(value, 0f, FuncTrackTrainVars.MaxSpeed));

    /// <summary>A fraction from -1 to 1, where the sign is the direction.</summary>
    [ScriptedEvent("SetSpeedDir")]
    private void OnSetSpeedDir(float value)
    {
        SetDirForward(value >= 0f);
        SetSpeed(FuncTrackTrainVars.MaxSpeed * Math.Clamp(MathF.Abs(value), 0f, 1f));
    }

    /// <summary>
    /// The same, and stock accelerates to it rather than jumping. There is nothing
    /// to accelerate over here, so it jumps. See docs/divergences.md.
    /// </summary>
    [ScriptedEvent("SetSpeedDirAccel")]
    private void OnSetSpeedDirAccel(float value) => OnSetSpeedDir(value);

    [ScriptedEvent("SetSpeedForwardModifier")]
    private void OnSetSpeedForwardModifier(float value)
    {
        _forwardModifier = Math.Clamp(MathF.Abs(value), 0f, 1f);
        SetSpeed(MathF.Abs(_speed));
    }

    [ScriptedEvent("TeleportToPathTrack")]
    private void OnTeleportToPathTrack(string value)
    {
        var node = FindByName<PathTrack>(value);

        if (node is null)
        {
            Log.Warning($"[SourceSharp] func_tracktrain '{Name}': " +
                           $"no path_track named '{value}' to teleport to\n");
            return;
        }

        TeleportToNode(node);
    }

    /// <summary>
    /// Stock's player-at-the-controls arithmetic, reachable only as an I/O input
    /// here.
    ///
    /// The value is a nudge of -1, 0 or 1 and what it does is move the throttle a
    /// quarter of the way: the current speed is rounded DOWN to a quarter of
    /// maximum and the nudge added, so repeated presses walk the train up through
    /// four notches. A track change fires it with zero, which is how it asks a
    /// stopped train to take up the speed it was just given.
    /// </summary>
    [ScriptedEvent("Use")]
    internal void OnUse(float value)
    {
        if (FuncTrackTrainVars.MaxSpeed == 0f)
            return;

        float notch = (int)(_speed * 4f) / (int)FuncTrackTrainVars.MaxSpeed * 0.25f + 0.25f * value;

        notch = Math.Clamp(notch, -0.25f, 1f);

        // A forward-only train cannot be reversed by the throttle, only stopped.
        if (Flags.HasFlag(SpawnFlag.ForwardOnly) && notch < 0f)
            notch = 0f;

        SetDirForward(notch >= 0f);
        SetSpeed(FuncTrackTrainVars.MaxSpeed * MathF.Abs(notch));
    }

    //-------------------------------------------------------------------------
    // The sounds.
    //
    // One loop for the whole journey rather than one per leg, which is what the
    // two overrides below are for: the base restarts its loop at every leg
    // because a func_train's legs are journeys, and a track train's legs are the
    // gaps between sleepers.
    //-------------------------------------------------------------------------
    protected override void PlayMoveSound()
    {
        if (_soundPlaying)
            return;

        _soundPlaying = true;

        if (FuncTrackTrainVars.SoundStart.Length > 0)
            Sound.EntityEmitSound(Handle, FuncTrackTrainVars.SoundStart);

        base.PlayMoveSound();
    }

    /// <summary>
    /// Nothing, deliberately. The base calls this at the end of every leg; see
    /// the note above, and <see cref="SoundStop"/> for where the loop does end.
    /// </summary>
    protected override void StopMoveSound() { }

    private void SoundStart() => PlayMoveSound();

    /// <summary>The loop ends and the stop sound plays, which is stock's SoundStop.</summary>
    private void SoundStop()
    {
        if (!_soundPlaying)
            return;

        _soundPlaying = false;

        base.StopMoveSound();
        PlayArriveSound();
    }

    /// <summary>
    /// <c>FL_UNBLOCKABLE_BY_PLAYER</c>, which stock's <c>Spawn</c> adds for the
    /// "Unblockable by Player" spawnflag: the pusher puts a player in the way
    /// where the push says instead of reporting them as a blocker.
    /// </summary>
    public override bool UnblockableByPlayer => Flags.HasFlag(SpawnFlag.UnblockableByPlayer);

    //-------------------------------------------------------------------------
    // Something in the way.
    //
    // A track train shoves before it crushes, and the shove is different
    // depending on where the blocker is: something riding on the train is lifted,
    // which is how stock un-wedges a player who has sunk into the roof, and
    // anything else is pushed away from the middle. Both are stock's, and so is
    // the absence of the base's half-second throttle — a blocked track train
    // damages on every frame it is blocked.
    //-------------------------------------------------------------------------
    public override void OnBlocked(EntityHandle other)
    {
        Vector away = Entities.GetOrigin(other) - Origin;

        if (away.X == 0f && away.Y == 0f && away.Z == 0f)
            away = new Vector(0f, 0f, 1f);

        Entities.SetVelocity(other, away.Normalized * Vars.BlockDamage);

        if (Vars.BlockDamage <= 0f)
            return;

        Damage.TakeDamage(other, Handle, Vars.BlockDamage, DamageType.Crush);
    }
}

/// <summary>How a train gets from one node's facing to the next.</summary>
public enum TrainOrientation
{
    /// <summary>Never turns. A lift on a vertical track.</summary>
    Fixed = 0,

    /// <summary>Faces where its front wheels are going. The default, and what a train looks like.</summary>
    AtPathTracks = 1,

    /// <summary>Turns from one node's facing to the next at a constant rate.</summary>
    LinearBlend = 2,

    /// <summary>The same, and stock eases in and out of it. See docs/divergences.md.</summary>
    EaseInEaseOut = 3,
}

/// <summary>
/// The map keys <see cref="FuncTrackTrain"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncTrackTrainVars
{
    /// <summary>The name of the node to start on.</summary>
    [VarName("target")]
    public string FirstNodeName { get; set; } = string.Empty;

    /// <summary>
    /// How far apart the wheels are, which is how far ahead the train looks to
    /// decide which way it is pointing. Zero means a hundred units, stock's
    /// stand-in for a train that never said.
    /// </summary>
    [VarName("wheels")]
    public float WheelBase { get; set; }

    /// <summary>How far above the track the train rides.</summary>
    public float Height { get; set; }

    /// <summary>The fastest it will go, and the speed every scaled input is a fraction of.</summary>
    [VarName("startspeed")]
    public float MaxSpeed { get; set; }

    /// <summary>
    /// How far it rolls into a corner, in degrees. Declared so a map's value
    /// survives being read back, and used by nothing: a bank is a roll driven by
    /// how fast the train is YAWING, so it only exists while the yaw is being
    /// integrated tick by tick, and a leg has one facing from end to end. See
    /// docs/divergences.md.
    /// </summary>
    public float Bank { get; set; }

    /// <summary>
    /// Which of stock's four ways of getting from one node's facing to the next.
    /// Fixed is honoured, and so is facing along the track; the two blends both
    /// become a constant-rate turn across the leg, because there is nothing
    /// mid-leg to ease.
    /// </summary>
    public int OrientationType { get; set; } = (int)TrainOrientation.AtPathTracks;

    /// <summary>
    /// Read back and unused, all three. Stock's velocity blends interpolate the
    /// speed between two nodes' values across the segment, and its manual accel
    /// approaches a desired speed a frame at a time; a leg runs at one speed from
    /// end to end, so the blend collapses onto the node behind and the accel onto
    /// its target. See docs/divergences.md.
    /// </summary>
    public int VelocityType { get; set; }

    public bool ManualSpeedChanges { get; set; }

    [VarName("ManualAccelSpeed")]
    public float AccelSpeed { get; set; }

    [VarName("ManualDecelSpeed")]
    public float DecelSpeed { get; set; }

    //-------------------------------------------------------------------------
    // The four sounds, which are NOT noise1/noise2 — a track train is not a
    // platform train and has its own key names. The loop and the stop one go to
    // the base through SoundNames; the other two are played here.
    //-------------------------------------------------------------------------
    [VarName("MoveSound")]
    public string SoundMove { get; set; } = string.Empty;

    [VarName("StopSound")]
    public string SoundStop { get; set; } = string.Empty;

    [VarName("StartSound")]
    public string SoundStart { get; set; } = string.Empty;

    /// <summary>
    /// The tick a moving train makes, at an interval that shortens with speed.
    /// Precached so a map's sound is not missing if this ever gets a clock, and
    /// never played: the interval is a second timer and the one this class has is
    /// spent on the leg. See docs/divergences.md.
    /// </summary>
    [VarName("MovePingSound")]
    public string SoundPing { get; set; } = string.Empty;

    [VarName("MoveSoundMinTime")]
    public float PingMinTime { get; set; }

    [VarName("MoveSoundMaxTime")]
    public float PingMaxTime { get; set; }

    /// <summary>
    /// What the moving loop should be pitched at, slowest and fastest. Read back
    /// and unused: the managed sound call plays a name at the entity's own
    /// attenuation and carries no pitch. See docs/divergences.md.
    /// </summary>
    [VarName("MoveSoundMinPitch")]
    public int PitchMin { get; set; }

    [VarName("MoveSoundMaxPitch")]
    public int PitchMax { get; set; }
}

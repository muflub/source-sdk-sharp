//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Server;

/// <summary>
/// Stock's <c>ILagCompensationManager</c> (<c>game/server/ilagcompensationmanager.h</c>):
/// the other players moved back to where the shooter saw them for the length
/// of an attack, and put back after it.
/// </summary>
public interface ILagCompensation
{
    /// <summary>
    /// <c>StartLagCompensation( player, player-&gt;GetCurrentCommand() )</c>:
    /// the command <paramref name="player"/> is running now. A player running no
    /// command moves nobody.
    /// </summary>
    void Start(EntityHandle player);

    /// <summary><c>StartLagCompensation( player, cmd )</c>.</summary>
    void Start(EntityHandle player, in UserCmd cmd);

    /// <summary><c>FinishLagCompensation( player )</c>: every player moved is put back.</summary>
    void Finish(EntityHandle player);

    /// <summary><c>IsCurrentlyDoingLagCompensation()</c>.</summary>
    bool IsCurrentlyDoingLagCompensation { get; }

    /// <summary>
    /// <c>CHL2MP_Player::NoteWeaponFired</c>: the command the player is
    /// running is the one they last fired in, which HL2MP's
    /// <c>WantsLagCompensationOnEntity</c> reads. Nothing outside a command.
    /// </summary>
    void NoteWeaponFired(EntityHandle player);
}

/// <summary>
/// One player the shooter's command could move back, as
/// <c>WantsLagCompensationOnEntity( pPlayer, pCmd, pEntityTransmitBits )</c>
/// is asked it.
/// </summary>
/// <param name="Shooter">The player whose command is being compensated.</param>
/// <param name="Target">The player who would be moved back.</param>
/// <param name="Command">The shooter's command.</param>
/// <param name="ShooterOrigin">The shooter's <c>GetAbsOrigin()</c>.</param>
/// <param name="TargetOrigin">The target's <c>GetAbsOrigin()</c>.</param>
/// <param name="TargetMaxSpeed">The target's <c>MaxSpeed()</c>.</param>
/// <param name="MaxUnlag"><c>sv_maxunlag</c>.</param>
public readonly record struct LagCompensationTarget(EntityHandle Shooter, EntityHandle Target, UserCmd Command,
                                                    Vector ShooterOrigin, Vector TargetOrigin,
                                                    float TargetMaxSpeed, float MaxUnlag)
{
    /// <summary>
    /// The target is not a player but an entity that opted in
    /// (<see cref="ScriptedEntity.LagCompensated"/>); its
    /// <see cref="TargetMaxSpeed"/> is the one it declares.
    /// </summary>
    public bool OptedIn { get; init; }
}

/// <summary>
/// A mod's <c>WantsLagCompensationOnEntity</c> override (TF2's
/// <c>CTFPlayer</c>): <see cref="Decision.UseStockRule"/> leaves the core's,
/// which is <c>CHL2MP_Player</c>'s. The first hook that decides wins.
/// </summary>
public interface ILagCompensationTargets
{
    Decision WantsLagCompensationOnEntity(in LagCompensationTarget target);
}

/// <summary>The rules the stock overrides share.</summary>
public static class LagCompensationRules
{
    /// <summary>cos 45: the cone in front of the shooter.</summary>
    public const float ConeCosine = 0.707107f;

    /// <summary>
    /// The reach half every stock override ends with
    /// (<c>CBasePlayer::WantsLagCompensationOnEntity</c>, player.cpp:760-782):
    /// a target close enough to have run past us within <c>sv_maxunlag</c>
    /// (1.5 times its top speed, to avoid dead zones) is compensated; one
    /// further away only inside the 45-degree cone the command looks along,
    /// unless <paramref name="coneCheck"/> is off (TF's melee on a teammate).
    /// </summary>
    public static bool InReach(in LagCompensationTarget target, bool coneCheck = true)
    {
        float maxDistance = 1.5f * target.TargetMaxSpeed * target.MaxUnlag;

        if ((target.TargetOrigin - target.ShooterOrigin).Length < maxDistance)
            return true;

        if (!coneCheck)
            return true;

        Vector forward = Angles.Forward(new Vector(target.Command.Pitch, target.Command.Yaw, target.Command.Roll));
        Vector diff = Normalize(target.TargetOrigin - target.ShooterOrigin);

        return forward.Dot(diff) >= ConeCosine;
    }

    /// <summary>
    /// <c>CHL2MP_Player::WantsLagCompensationOnEntity</c> (hl2mp_player.cpp:683-714):
    /// nobody is moved for a command that is not attacking from a player who
    /// has not fired in the last five commands; otherwise the reach.
    /// </summary>
    public static bool Hl2mpWants(in LagCompensationTarget target, int lastWeaponFireCommand)
    {
        if ((target.Command.Buttons & (int)InputButtons.Attack) == 0
            && target.Command.CommandNumber - lastWeaponFireCommand > 5)
            return false;

        return InReach(in target);
    }

    /// <summary><c>VectorNormalize</c>: a zero vector stays zero.</summary>
    private static Vector Normalize(Vector v)
    {
        float length = v.Length;
        return length > 0f ? v * (1f / length) : Vector.Zero;
    }
}

/// <summary>The lag compensation's convars, as it reads them.</summary>
public interface ILagCompensationSettings
{
    /// <summary><c>sv_unlag</c>.</summary>
    bool Unlag { get; }

    /// <summary><c>sv_maxunlag</c>.</summary>
    float MaxUnlag { get; }

    /// <summary><c>sv_lagflushbonecache</c>.</summary>
    bool FlushBoneCache { get; }

    /// <summary><c>sv_lagcompensation_teleport_dist</c>.</summary>
    float TeleportDistance { get; }
}

/// <summary>
/// What the lag compensation reads and moves: the players by slot, the facts
/// its gates ask, and each player's place in the engine's collision and its
/// skeleton.
/// </summary>
public interface ILagCompensationWorld
{
    /// <summary><c>gpGlobals-&gt;curtime</c>.</summary>
    float CurTime { get; }

    /// <summary><c>gpGlobals-&gt;tickcount</c>.</summary>
    int TickCount { get; }

    /// <summary><c>TICK_INTERVAL</c>.</summary>
    float TickInterval { get; }

    /// <summary><c>gpGlobals-&gt;maxClients</c>.</summary>
    int MaxClients { get; }

    /// <summary><c>UTIL_PlayerByIndex( index )</c>, or <see cref="EntityHandle.None"/>.</summary>
    EntityHandle PlayerByIndex(int index);

    /// <summary><c>IsAlive()</c>.</summary>
    bool IsAlive(EntityHandle player);

    /// <summary><c>IsBot()</c>.</summary>
    bool IsBot(EntityHandle player);

    /// <summary><c>IsObserver()</c>.</summary>
    bool IsObserver(EntityHandle player);

    /// <summary><c>m_bLagCompensation</c>: the client predicts and asked for lag compensation.</summary>
    bool WantsLagCompensation(EntityHandle player);

    /// <summary><c>m_fLerpTime</c>: the client's view interpolation, in seconds.</summary>
    float LerpTime(EntityHandle player);

    /// <summary><c>GetPlayerNetInfo( index )-&gt;GetLatency( FLOW_OUTGOING )</c>; null with no channel.</summary>
    float? Latency(EntityHandle player);

    /// <summary><c>MaxSpeed()</c>.</summary>
    float MaxSpeed(EntityHandle player);

    /// <summary><c>GetSimulationTime()</c>.</summary>
    float GetSimulationTime(EntityHandle player);

    /// <summary><c>SetSimulationTime( time )</c>.</summary>
    void SetSimulationTime(EntityHandle player, float time);

    /// <summary>The local origin, local angles and the pre-scaled collision box; false for none.</summary>
    bool TryGetPlacement(EntityHandle player, out EntityPlacement placement);

    /// <summary><c>SetLocalOrigin</c> and <c>SetLocalAngles</c>: no trigger is touched.</summary>
    void Relink(EntityHandle player, Vector origin, QAngle angles);

    /// <summary><c>SetSize( mins, maxs )</c>.</summary>
    void SetSize(EntityHandle player, Vector mins, Vector maxs);

    /// <summary><c>UTIL_SetOrigin( player, origin, true )</c>: the triggers are touched.</summary>
    void SetOrigin(EntityHandle player, Vector origin);

    /// <summary>
    /// <c>UTIL_TraceEntity( player, start, end, MASK_PLAYERSOLID, player,
    /// COLLISION_GROUP_PLAYER_MOVEMENT )</c>: the player's box swept.
    /// </summary>
    EntityHullTrace TraceEntity(EntityHandle player, Vector start, Vector end, Vector mins, Vector maxs);

    /// <summary>The skeleton's animation; false for no skeleton.</summary>
    bool CaptureAnimation(EntityHandle player, SkeletonAnimation into);

    /// <summary><c>SetSequence</c>, <c>SetCycle</c>, <c>SetPoseParameter</c> for the rewind.</summary>
    void PoseAnimation(EntityHandle player, int sequence, float cycle, ReadOnlySpan<float> poseParameters);

    /// <summary>What <see cref="CaptureAnimation"/> read, put back.</summary>
    void RestoreAnimation(EntityHandle player, SkeletonAnimation saved);

    /// <summary><c>InvalidateBoneCache()</c>.</summary>
    void InvalidateBoneCache(EntityHandle player);
}

/// <summary>
/// The command each player is running: stock's <c>CBasePlayer::m_pCurrentCommand</c>,
/// which <c>GetCurrentCommand()</c> answers and the lag compensation reads.
/// </summary>
public sealed class PlayerCurrentCommands
{
    private readonly Dictionary<uint, UserCmd> _running = new();

    /// <summary><c>SetCurrentCommand( cmd )</c>, for the scope's length.</summary>
    public Scope Begin(EntityHandle player, in UserCmd cmd)
    {
        _running[player.Value] = cmd;
        return new Scope(this, player);
    }

    /// <summary><c>GetCurrentCommand()</c>: false when the player is running none.</summary>
    public bool TryGet(EntityHandle player, out UserCmd cmd) => _running.TryGetValue(player.Value, out cmd);

    /// <summary>The command's end: <c>SetCurrentCommand( NULL )</c>.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly PlayerCurrentCommands? _owner;
        private readonly EntityHandle _player;

        internal Scope(PlayerCurrentCommands owner, EntityHandle player)
        {
            _owner = owner;
            _player = player;
        }

        public void Dispose() => _owner?._running.Remove(_player.Value);
    }
}

/// <summary>
/// One player at one simulation time: stock's <c>LagRecord</c>
/// (player_lagcompensation.cpp:69-127), without the overlay layers this
/// server's skeletons do not carry.
/// </summary>
public sealed class LagRecord
{
    /// <summary><c>m_fFlags</c>: <see cref="LagCompensation.Alive"/> and the changed bits.</summary>
    public int Flags;

    public Vector Origin;
    public QAngle Angles;
    public Vector MinsPreScaled;
    public Vector MaxsPreScaled;

    /// <summary><c>m_flSimulationTime</c>; -1 for a record never written.</summary>
    public float SimulationTime = -1f;

    /// <summary>Whether the skeleton answered; a record without one rewinds no animation.</summary>
    public bool HasAnimation;

    /// <summary><c>m_masterSequence</c>, <c>m_masterCycle</c> and <c>m_flPoseParameters</c>, with the held cycle.</summary>
    public readonly SkeletonAnimation Animation = new();

    /// <summary>A record as the constructor (or <c>Q_memset</c>) leaves it.</summary>
    public void Clear()
    {
        Flags = 0;
        Origin = default;
        Angles = default;
        MinsPreScaled = default;
        MaxsPreScaled = default;
        SimulationTime = -1f;
        HasAnimation = false;
        Animation.Sequence = 0;
        Animation.Cycle = 0f;
        Animation.HeldCycle = null;
        Array.Clear(Animation.PoseParameters);
    }
}

/// <summary>
/// <b>CLagCompensationManager</b> (<c>game/server/player_lagcompensation.cpp</c>):
/// each frame, after the entities have thought, every player's place is
/// recorded against their simulation time; while a shooter's attack runs,
/// every other player the shooter could have been aiming at is moved back to
/// where the shooter's client drew them, and put back after.
///
/// <para>
/// The target time is stock's (<c>StartLagCompensation</c>, :379-412): the
/// command's <c>tick_count</c> less the client's interpolation ticks, unless
/// it disagrees by more than 0.2 s with what the measured latency plus the
/// interpolation say (clamped to <c>sv_maxunlag</c>), when the latency's
/// answer is used. Each player is walked back through their records to the
/// first at or before that time and interpolated against the one after it
/// (<c>BacktrackPlayer</c>, :440-765); a death or a teleport in the way loses
/// the track and leaves them where they are.
/// </para>
///
/// <para>
/// The restore (<c>FinishLagCompensation</c>, :768-873) puts back only what
/// the attack did not change itself: a size or angles still as the rewind left
/// them, and an origin moved by the attack by its own delta, unless that
/// moved it further than the teleport distance.
/// </para>
///
/// <para>
/// The animation rewound is what this server's skeletons have: the base
/// sequence, its cycle and the pose parameters. They carry no overlay layers,
/// so none are recorded (docs/divergences.md).
/// </para>
///
/// <para>
/// ENTITIES THAT OPTED IN (<see cref="ScriptedEntity.LagCompensated"/>, through
/// <see cref="ILagCompensatedEntities"/>; stock has no such arm) are recorded,
/// moved back and put back exactly as a player is, with three differences
/// that follow from their not being players: a record's time is the frame's
/// (an entity is simulated every frame, as stock's NPCs are, and runs no
/// commands to stamp one), an entity opted in is alive, and its simulation
/// time is neither read nor restored. A history starts again when the entity
/// stops answering yes, or when its <see cref="ScriptedEntity.LagHistorySerial"/>
/// moves on; a rewind asks the entity's serial at the shot, so a history
/// restarted since the frame's record is not used.
/// </para>
/// </summary>
public sealed class LagCompensation : ILagCompensation, IFrameLifecycle, IGameLifecycle
{
    /// <summary><c>LC_ALIVE</c>.</summary>
    public const int Alive = 1 << 0;

    /// <summary><c>LC_ORIGIN_CHANGED</c>.</summary>
    public const int OriginChanged = 1 << 8;

    /// <summary><c>LC_ANGLES_CHANGED</c>.</summary>
    public const int AnglesChanged = 1 << 9;

    /// <summary><c>LC_SIZE_CHANGED</c>.</summary>
    public const int SizeChanged = 1 << 10;

    /// <summary><c>LC_ANIMATION_CHANGED</c>.</summary>
    public const int AnimationChanged = 1 << 11;

    /// <summary><c>LAG_COMPENSATION_EPS_SQR</c>.</summary>
    private const float EpsilonSquared = 0.1f * 0.1f;

    /// <summary><c>g_flFractionScale</c>: how far along a blocked restore goes.</summary>
    private const float FractionScale = 0.95f;

    /// <summary>The difference between the command's time and the latency's beyond which the latency wins.</summary>
    private const float MaxCommandLatencyDelta = 0.2f;

    private readonly ILagCompensationWorld _world;
    private readonly ILagCompensationSettings _settings;
    private readonly PlayerCurrentCommands _commands;
    private readonly IHooks<ILagCompensationTargets>? _targets;
    private readonly ILagCompensatedEntities? _entities;
    private readonly ILog _log;

    // m_PlayerTrack: each slot's records, oldest first (stock's tail) and
    // newest last (stock's head).
    private readonly Dictionary<int, List<LagRecord>> _tracks = new();

    // Records dropped from a track, kept for the next frame's writes.
    private readonly Stack<LagRecord> _spare = new();

    // m_RestorePlayer, m_RestoreData and m_ChangeData, by slot.
    private readonly HashSet<int> _restorePlayer = new();
    private readonly Dictionary<int, LagRecord> _restoreData = new();
    private readonly Dictionary<int, LagRecord> _changeData = new();

    // CHL2MP_Player::m_iLastWeaponFireUsercmd, by player.
    private readonly Dictionary<uint, int> _lastWeaponFire = new();

    // The opted-in entities' tracks, by handle; the frame's census of them;
    // the tracks the census no longer names; tracks kept for reuse; and the
    // tracks the session moved (m_RestorePlayer's half for entities).
    private readonly Dictionary<uint, EntityTrack> _entityTracks = new();
    private readonly List<LagCompensatedEntity> _census = new();
    private readonly List<uint> _goneEntities = new();
    private readonly Stack<EntityTrack> _spareTracks = new();
    private readonly List<EntityTrack> _restoreEntities = new();

    private bool _needToRestore;
    private EntityHandle _currentPlayer;
    private float _teleportDistanceSqr = 64f * 64f;

    public LagCompensation(ILagCompensationWorld world, ILagCompensationSettings settings,
                           PlayerCurrentCommands commands, ILog log,
                           IHooks<ILagCompensationTargets>? targets = null,
                           ILagCompensatedEntities? entities = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _targets = targets;
        _entities = entities;
    }

    /// <summary>
    /// One opted-in entity's history (a player's <c>m_PlayerTrack</c> slot)
    /// with its restore and change records, the serial its records were taken
    /// under and the top speed it declared.
    /// </summary>
    private sealed class EntityTrack
    {
        public EntityHandle Handle;
        public int Serial;
        public float MaxSpeed;
        public bool Seen;
        public readonly List<LagRecord> Records = new();
        public readonly LagRecord Restore = new();
        public readonly LagRecord Change = new();
    }

    int IFrameLifecycle.FrameOrder => (int)FrameStage.LagCompensation;

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.LagCompensation;

    /// <inheritdoc/>
    public bool IsCurrentlyDoingLagCompensation { get; private set; }

    /// <summary>A slot's records, oldest first; empty for a slot with none.</summary>
    public IReadOnlyList<LagRecord> History(int playerIndex)
        => _tracks.TryGetValue(playerIndex, out List<LagRecord>? track) ? track : Array.Empty<LagRecord>();

    /// <summary>An opted-in entity's records, oldest first; empty for an entity with none.</summary>
    public IReadOnlyList<LagRecord> EntityHistory(EntityHandle entity)
        => _entityTracks.TryGetValue(entity.Value, out EntityTrack? track) ? track.Records : Array.Empty<LagRecord>();

    /// <summary>The entities with a track, into <paramref name="into"/> (cleared first).</summary>
    public void CopyTrackedEntities(List<EntityHandle> into)
    {
        into.Clear();
        foreach (EntityTrack track in _entityTracks.Values)
            into.Add(track.Handle);
    }

    /// <summary><c>Shutdown</c>.</summary>
    void IGameLifecycle.OnShutdown() => ClearHistory();

    /// <summary><c>LevelShutdownPostEntity</c>.</summary>
    void IGameLifecycle.OnLevelShutdown()
    {
        ClearHistory();
        _lastWeaponFire.Clear();
    }

    /// <summary><c>FrameUpdatePostEntityThink</c>.</summary>
    void IFrameLifecycle.OnThink(float frameTime) => RecordFrame();

    /// <inheritdoc/>
    public void NoteWeaponFired(EntityHandle player)
    {
        if (_commands.TryGet(player, out UserCmd cmd))
            _lastWeaponFire[player.Value] = cmd.CommandNumber;
    }

    private void ClearHistory()
    {
        foreach (List<LagRecord> track in _tracks.Values)
            Purge(track, track.Count);

        foreach (EntityTrack track in _entityTracks.Values)
        {
            Purge(track.Records, track.Records.Count);
            _spareTracks.Push(track);
        }

        _entityTracks.Clear();
    }

    private void Purge(List<LagRecord> track, int count)
    {
        for (int i = 0; i < count; i++)
            _spare.Push(track[i]);

        track.RemoveRange(0, count);
    }

    //-------------------------------------------------------------------------
    // FrameUpdatePostEntityThink (:245-342)
    //-------------------------------------------------------------------------

    /// <summary>
    /// The frame's record: every player whose simulation time moved on gets a
    /// new head record, and records older than <c>sv_maxunlag</c> go.
    /// </summary>
    public void RecordFrame()
    {
        if (_world.MaxClients <= 1 || !_settings.Unlag)
        {
            ClearHistory();
            return;
        }

        _teleportDistanceSqr = _settings.TeleportDistance * _settings.TeleportDistance;

        // Stock truncates the dead time to an int (`int flDeadtime`), so a
        // record is kept up to a second longer than sv_maxunlag says.
        int deadTime = (int)(_world.CurTime - _settings.MaxUnlag);

        for (int i = 1; i <= _world.MaxClients; i++)
        {
            EntityHandle player = _world.PlayerByIndex(i);

            if (player == EntityHandle.None)
            {
                if (_tracks.TryGetValue(i, out List<LagRecord>? gone))
                    Purge(gone, gone.Count);

                continue;
            }

            List<LagRecord> track = Track(i);

            // Remove the tail records that are too old.
            int old = 0;
            while (old < track.Count && track[old].SimulationTime < deadTime)
                old++;

            Purge(track, old);

            float simulationTime = _world.GetSimulationTime(player);

            // No new entry for the same or an older time.
            if (track.Count > 0 && track[^1].SimulationTime >= simulationTime)
                continue;

            if (!_world.TryGetPlacement(player, out EntityPlacement placement))
                continue;

            LagRecord record = _spare.Count > 0 ? _spare.Pop() : new LagRecord();
            record.Clear();

            if (_world.IsAlive(player))
                record.Flags |= Alive;

            record.SimulationTime = simulationTime;
            record.Angles = placement.Angles;
            record.Origin = placement.Origin;
            record.MinsPreScaled = placement.Mins;
            record.MaxsPreScaled = placement.Maxs;
            record.HasAnimation = _world.CaptureAnimation(player, record.Animation);

            track.Add(record);
        }

        RecordEntities(deadTime);

        // Clear the current player.
        _currentPlayer = EntityHandle.None;
    }

    /// <summary>
    /// The frame's record for each opted-in entity, as a player's is taken: its
    /// old records go, and a record at the frame's time joins the head. A
    /// track whose serial moved on starts empty; the track of an entity no
    /// longer opted in goes.
    /// </summary>
    private void RecordEntities(int deadTime)
    {
        if (_entities is null)
            return;

        _entities.Collect(_census);

        foreach (EntityTrack track in _entityTracks.Values)
            track.Seen = false;

        float now = _world.CurTime;

        foreach (LagCompensatedEntity entity in _census)
        {
            EntityTrack track = EntityTrackOf(entity);
            track.Seen = true;
            track.MaxSpeed = entity.MaxSpeed;

            // The history starts here: whatever was recorded is someone else's.
            if (track.Serial != entity.HistorySerial)
            {
                Purge(track.Records, track.Records.Count);
                track.Serial = entity.HistorySerial;
            }

            List<LagRecord> records = track.Records;

            int old = 0;
            while (old < records.Count && records[old].SimulationTime < deadTime)
                old++;

            Purge(records, old);

            if (records.Count > 0 && records[^1].SimulationTime >= now)
                continue;

            if (!_world.TryGetPlacement(entity.Handle, out EntityPlacement placement))
                continue;

            LagRecord record = _spare.Count > 0 ? _spare.Pop() : new LagRecord();
            record.Clear();
            record.Flags = Alive;
            record.SimulationTime = now;
            record.Angles = placement.Angles;
            record.Origin = placement.Origin;
            record.MinsPreScaled = placement.Mins;
            record.MaxsPreScaled = placement.Maxs;
            record.HasAnimation = _world.CaptureAnimation(entity.Handle, record.Animation);

            records.Add(record);
        }

        foreach (KeyValuePair<uint, EntityTrack> pair in _entityTracks)
        {
            if (!pair.Value.Seen)
                _goneEntities.Add(pair.Key);
        }

        foreach (uint key in _goneEntities)
        {
            EntityTrack gone = _entityTracks[key];
            Purge(gone.Records, gone.Records.Count);
            _entityTracks.Remove(key);
            _spareTracks.Push(gone);
        }

        _goneEntities.Clear();
    }

    private EntityTrack EntityTrackOf(in LagCompensatedEntity entity)
    {
        if (!_entityTracks.TryGetValue(entity.Handle.Value, out EntityTrack? track))
        {
            track = _spareTracks.Count > 0 ? _spareTracks.Pop() : new EntityTrack();
            track.Handle = entity.Handle;
            track.Serial = entity.HistorySerial;
            _entityTracks[entity.Handle.Value] = track;
        }

        return track;
    }

    private List<LagRecord> Track(int index)
    {
        if (!_tracks.TryGetValue(index, out List<LagRecord>? track))
            _tracks[index] = track = new List<LagRecord>();

        return track;
    }

    //-------------------------------------------------------------------------
    // StartLagCompensation (:345-438)
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Start(EntityHandle player)
    {
        if (_commands.TryGet(player, out UserCmd cmd))
        {
            Start(player, in cmd);
            return;
        }

        // No command running: nothing to compensate, but the session is open
        // so its Finish pairs with it.
        OpenSession(player);
    }

    /// <inheritdoc/>
    public void Start(EntityHandle player, in UserCmd cmd)
    {
        if (!OpenSession(player))
            return;

        if (!_world.WantsLagCompensation(player)   // player not wanting lag compensation
            || _world.MaxClients <= 1              // no lag compensation in single player
            || !_settings.Unlag                    // disabled by server admin
            || _world.IsBot(player)                // not for bots
            || _world.IsObserver(player))          // not for spectators
            return;

        foreach (LagRecord data in _restoreData.Values)
            data.Clear();

        foreach (LagRecord data in _changeData.Values)
            data.Clear();

        IsCurrentlyDoingLagCompensation = true;

        float targetTime = TargetTime(player, in cmd);

        _ = _world.TryGetPlacement(player, out EntityPlacement shooter);

        for (int i = 1; i <= _world.MaxClients; i++)
        {
            EntityHandle other = _world.PlayerByIndex(i);

            if (other == EntityHandle.None)
                continue;

            // Don't lag compensate yourself.
            if (other == player)
                continue;

            if (!WantsLagCompensationOnEntity(player, other, in cmd, shooter.Origin))
                continue;

            // Move the other player back in time.
            BacktrackPlayer(i, other, targetTime);
        }

        StartEntities(player, in cmd, shooter.Origin, targetTime);
    }

    /// <summary>
    /// The opted-in entities moved back as the players are: each asked
    /// <see cref="ILagCompensationTargets"/> the same question, and walked
    /// back through its track. An entity no longer opted in, or whose history
    /// restarted since its records were taken, is left where it is.
    /// </summary>
    private void StartEntities(EntityHandle shooter, in UserCmd cmd, Vector shooterOrigin, float targetTime)
    {
        if (_entities is null)
            return;

        foreach (EntityTrack track in _entityTracks.Values)
        {
            if (track.Records.Count <= 0 || track.Handle == shooter)
                continue;

            if (!_entities.TryGet(track.Handle, out LagCompensatedEntity now) || now.HistorySerial != track.Serial)
                continue;

            if (!_world.TryGetPlacement(track.Handle, out EntityPlacement place))
                continue;

            var asked = new LagCompensationTarget(shooter, track.Handle, cmd, shooterOrigin, place.Origin,
                                                  now.MaxSpeed, _settings.MaxUnlag) { OptedIn = true };

            if (!WantsLagCompensationOn(in asked))
                continue;

            track.Restore.Clear();
            track.Change.Clear();

            if (Backtrack(track.Handle, track.Records, track.Restore, track.Change, targetTime, isPlayer: false))
            {
                _restoreEntities.Add(track);
                _needToRestore = true;
            }
        }
    }

    /// <summary>
    /// The session's own sentinel (:347-362): a second start before a finish is
    /// a code bug, warned and refused; otherwise nobody is marked for a
    /// restore yet and the shooter is the current player.
    /// </summary>
    private bool OpenSession(EntityHandle player)
    {
        if (_currentPlayer != EntityHandle.None)
        {
            _log.Warning("Trying to start a new lag compensation session while one is already active!\n");
            return false;
        }

        _restorePlayer.Clear();
        _restoreEntities.Clear();
        _needToRestore = false;
        _currentPlayer = player;
        return true;
    }

    /// <summary>
    /// The time the shooter's client drew the world at (:379-412): the
    /// command's tick less the interpolation, or, when that disagrees with
    /// the latency's estimate by more than 0.2 s, the latency's.
    /// </summary>
    public float TargetTime(EntityHandle player, in UserCmd cmd)
    {
        float interval = _world.TickInterval;

        // The time there is to correct by: the network latency...
        float correct = _world.Latency(player) ?? 0f;

        // ...and the view interpolation (C_BaseEntity::GetInterpolationAmount).
        int lerpTicks = TimeToTicks(_world.LerpTime(player), interval);
        correct += lerpTicks * interval;

        // In [0, sv_maxunlag].
        correct = Math.Clamp(correct, 0f, _settings.MaxUnlag);

        // The tick the player sent, corrected.
        int targetTick = cmd.TickCount - lerpTicks;

        // Too far from the latency's estimate (over 200 ms): the latency's.
        float deltaTime = correct - (_world.TickCount - targetTick) * interval;
        if (MathF.Abs(deltaTime) > MaxCommandLatencyDelta)
            targetTick = _world.TickCount - TimeToTicks(correct, interval);

        return targetTick * interval;
    }

    /// <summary><c>TIME_TO_TICKS</c>.</summary>
    private static int TimeToTicks(float time, float interval)
        => interval > 0f ? (int)(0.5f + time / interval) : 0;

    private bool WantsLagCompensationOnEntity(EntityHandle shooter, EntityHandle target, in UserCmd cmd,
                                              Vector shooterOrigin)
    {
        _ = _world.TryGetPlacement(target, out EntityPlacement targetPlace);

        var asked = new LagCompensationTarget(shooter, target, cmd, shooterOrigin, targetPlace.Origin,
                                              _world.MaxSpeed(target), _settings.MaxUnlag);

        return WantsLagCompensationOn(in asked);
    }

    /// <summary>The mods' hooks in order, the first that decides winning; else HL2MP's rule.</summary>
    private bool WantsLagCompensationOn(in LagCompensationTarget asked)
    {
        if (_targets is not null)
        {
            IReadOnlyList<ILagCompensationTargets> rules = _targets.All;
            for (int i = 0; i < rules.Count; i++)
            {
                Decision decided = rules[i].WantsLagCompensationOnEntity(in asked);
                if (decided != Decision.UseStockRule)
                    return decided == Decision.Yes;
            }
        }

        return LagCompensationRules.Hl2mpWants(in asked,
                                               _lastWeaponFire.TryGetValue(asked.Shooter.Value, out int fired) ? fired : 0);
    }

    //-------------------------------------------------------------------------
    // BacktrackPlayer (:440-765)
    //-------------------------------------------------------------------------

    private void BacktrackPlayer(int index, EntityHandle player, float targetTime)
    {
        // Get the track history of this player; at least one entry.
        if (!_tracks.TryGetValue(index, out List<LagRecord>? track) || track.Count <= 0)
            return;

        if (!Backtrack(player, track, Data(_restoreData, index), Data(_changeData, index), targetTime, isPlayer: true))
            return;

        _restorePlayer.Add(index);   // remember that we changed this player
        _needToRestore = true;       // we changed at least one player
    }

    /// <summary>
    /// One player's or opted-in entity's walk back through <paramref name="track"/>
    /// to <paramref name="targetTime"/>, and the move there; answers whether it
    /// was moved (and so must be restored). Only a player's simulation time is
    /// remembered for the restore.
    /// </summary>
    private bool Backtrack(EntityHandle player, List<LagRecord> track, LagRecord restore, LagRecord change,
                           float targetTime, bool isPlayer)
    {
        if (track.Count <= 0 || !_world.TryGetPlacement(player, out EntityPlacement now))
            return false;

        LagRecord? prevRecord = null;
        LagRecord? record = null;
        Vector prevOrg = now.Origin;

        // Walk the context, newest first, looking for any invalidating event.
        for (int curr = track.Count - 1; curr >= 0; curr--)
        {
            // Remember the last record, and take the next.
            prevRecord = record;
            record = track[curr];

            // The player must be alive: lost track.
            if ((record.Flags & Alive) == 0)
                return false;

            // Lost track: too much difference.
            Vector delta = record.Origin - prevOrg;
            if (delta.X * delta.X + delta.Y * delta.Y > _teleportDistanceSqr)
                return false;

            // A context at or before the target time: stop.
            if (record.SimulationTime <= targetTime)
                break;

            prevOrg = record.Origin;
        }

        if (record is null)
            return false;

        Vector org;
        QAngle ang;
        Vector minsPreScaled;
        Vector maxsPreScaled;
        float frac = 0f;

        if (prevRecord is not null
            && record.SimulationTime < targetTime
            && record.SimulationTime < prevRecord.SimulationTime)
        {
            // Not the exact time, but a valid previous record: interpolate
            // between the two.
            frac = (targetTime - record.SimulationTime) / (prevRecord.SimulationTime - record.SimulationTime);

            ang = Lerp(frac, record.Angles, prevRecord.Angles);
            org = Lerp(frac, record.Origin, prevRecord.Origin);
            minsPreScaled = Lerp(frac, record.MinsPreScaled, prevRecord.MinsPreScaled);
            maxsPreScaled = Lerp(frac, record.MaxsPreScaled, prevRecord.MaxsPreScaled);
        }
        else
        {
            // The exact record, or none to interpolate with: the best there is.
            org = record.Origin;
            ang = record.Angles;
            minsPreScaled = record.MinsPreScaled;
            maxsPreScaled = record.MaxsPreScaled;
        }

        // See if this represents a change for the player.
        int flags = 0;

        // Always remember the pristine simulation time in case we need to
        // restore it (a player's: an entity's is not ours to set).
        if (isPlayer)
            restore.SimulationTime = _world.GetSimulationTime(player);

        Vector angdiff = new(now.Angles.Pitch - ang.Pitch, now.Angles.Yaw - ang.Yaw, now.Angles.Roll - ang.Roll);
        Vector orgdiff = now.Origin - org;

        QAngle placedAngles = now.Angles;
        if (angdiff.LengthSquared > EpsilonSquared)
        {
            flags |= AnglesChanged;
            restore.Angles = now.Angles;
            placedAngles = ang;
            change.Angles = ang;
        }

        // Absolute equality here.
        if (!minsPreScaled.Equals(now.Mins) || !maxsPreScaled.Equals(now.Maxs))
        {
            flags |= SizeChanged;
            restore.MinsPreScaled = now.Mins;
            restore.MaxsPreScaled = now.Maxs;
            _world.SetSize(player, minsPreScaled, maxsPreScaled);
            change.MinsPreScaled = minsPreScaled;
            change.MaxsPreScaled = maxsPreScaled;
        }

        // The origin last, since it relinks the player; the angles with it.
        Vector placedOrigin = now.Origin;
        if (orgdiff.LengthSquared > EpsilonSquared)
        {
            flags |= OriginChanged;
            restore.Origin = now.Origin;
            placedOrigin = org;
            change.Origin = org;
        }

        if ((flags & (AnglesChanged | OriginChanged)) != 0)
            _world.Relink(player, placedOrigin, placedAngles);

        // Stock always takes the animation: "you breathe even on the server".
        flags |= AnimationChanged;
        restore.HasAnimation = _world.CaptureAnimation(player, restore.Animation);

        if (restore.HasAnimation && record.HasAnimation)
            RewindAnimation(player, record, prevRecord, frac);

        if (_settings.FlushBoneCache)
            _world.InvalidateBoneCache(player);

        restore.Flags = flags;       // we need to restore these flags
        change.Flags = flags;        // we have changed these flags
        return true;
    }

    /// <summary>
    /// The master sequence, its cycle and the pose parameters (:648-692):
    /// interpolated between the two records when they play the same sequence,
    /// a cycle that wrapped lerped through 1; the pose parameters the older
    /// record's, never lerped.
    /// </summary>
    private void RewindAnimation(EntityHandle player, LagRecord record, LagRecord? prevRecord, float frac)
    {
        SkeletonAnimation at = record.Animation;
        float cycle = at.Cycle;

        if (frac > 0f && prevRecord is { HasAnimation: true }
            && prevRecord.Animation.Sequence == at.Sequence)
        {
            SkeletonAnimation next = prevRecord.Animation;

            if (at.Cycle > next.Cycle)
            {
                // The older record is further into the cycle than the newer:
                // it wrapped from 1 back to 0, so lerp .9 to 1.1, not .9 to .1.
                float newCycle = Lerp(frac, at.Cycle, next.Cycle + 1f);
                cycle = newCycle < 1f ? newCycle : newCycle - 1f;
            }
            else
            {
                cycle = Lerp(frac, at.Cycle, next.Cycle);
            }
        }

        _world.PoseAnimation(player, at.Sequence, cycle, at.PoseParameters);
    }

    private static LagRecord Data(Dictionary<int, LagRecord> table, int index)
    {
        if (!table.TryGetValue(index, out LagRecord? data))
            table[index] = data = new LagRecord();

        return data;
    }

    //-------------------------------------------------------------------------
    // FinishLagCompensation (:768-873)
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Finish(EntityHandle player)
    {
        _currentPlayer = EntityHandle.None;

        if (!_needToRestore)
        {
            IsCurrentlyDoingLagCompensation = false;
            return;   // no player was changed at all
        }

        for (int i = 1; i <= _world.MaxClients; i++)
        {
            // A player the lag compensation did not change.
            if (!_restorePlayer.Contains(i))
                continue;

            EntityHandle other = _world.PlayerByIndex(i);
            if (other == EntityHandle.None || !_world.TryGetPlacement(other, out EntityPlacement now))
                continue;

            Restore(other, now, Data(_restoreData, i), Data(_changeData, i), isPlayer: true);
        }

        foreach (EntityTrack track in _restoreEntities)
        {
            if (_world.TryGetPlacement(track.Handle, out EntityPlacement now))
                Restore(track.Handle, now, track.Restore, track.Change, isPlayer: false);
        }

        IsCurrentlyDoingLagCompensation = false;
    }

    /// <summary>
    /// One player or opted-in entity put back (:791-870): only what the attack
    /// did not change itself. Only a player's simulation time is restored.
    /// </summary>
    private void Restore(EntityHandle other, EntityPlacement now, LagRecord restore, LagRecord change, bool isPlayer)
    {
        bool restoreSimulationTime = false;

        if ((restore.Flags & SizeChanged) != 0)
        {
            restoreSimulationTime = true;

            // Restored only if the attack made no change of its own.
            if (now.Mins.Equals(change.MinsPreScaled) && now.Maxs.Equals(change.MaxsPreScaled))
            {
                _world.SetSize(other, restore.MinsPreScaled, restore.MaxsPreScaled);
                now = now with { Mins = restore.MinsPreScaled, Maxs = restore.MaxsPreScaled };
            }
        }

        if ((restore.Flags & AnglesChanged) != 0)
        {
            restoreSimulationTime = true;

            if (now.Angles.Equals(change.Angles))
            {
                _world.Relink(other, now.Origin, restore.Angles);
                now = now with { Angles = restore.Angles };
            }
        }

        if ((restore.Flags & OriginChanged) != 0)
        {
            restoreSimulationTime = true;

            // Something reasonable with the change the attack made...
            Vector delta = now.Origin - change.Origin;

            // ...unless it moved them really far: then they stay in the new spot.
            if (delta.X * delta.X + delta.Y * delta.Y < _teleportDistanceSqr)
                RestorePlayerTo(other, now, restore.Origin + delta);
        }

        if ((restore.Flags & AnimationChanged) != 0)
        {
            restoreSimulationTime = true;

            if (restore.HasAnimation)
                _world.RestoreAnimation(other, restore.Animation);

            // The bone cache is not keyed on the pose parameters here, so
            // the rewound bones go with the rewound pose (docs/divergences.md).
            if (_settings.FlushBoneCache)
                _world.InvalidateBoneCache(other);
        }

        if (restoreSimulationTime && isPlayer)
            _world.SetSimulationTime(other, restore.SimulationTime);
    }

    /// <summary>
    /// <c>RestorePlayerTo</c> (:138-176): back to <paramref name="wanted"/> if the
    /// player fits there; otherwise as far towards it from where they are as
    /// they fit, or nowhere when even that is blocked.
    /// </summary>
    private void RestorePlayerTo(EntityHandle player, in EntityPlacement now, Vector wanted)
    {
        EntityHullTrace tr = _world.TraceEntity(player, wanted, wanted, now.Mins, now.Maxs);

        if (!tr.StartSolid && !tr.AllSolid)
        {
            // The player can go back whence he came.
            _world.SetOrigin(player, tr.EndPos);
            return;
        }

        tr = _world.TraceEntity(player, now.Origin, wanted, now.Mins, now.Maxs);

        // Stuck back wherever the compensation put them.
        if (tr.StartSolid || tr.AllSolid)
            return;

        // A valid place, but not all the way back.
        _world.SetOrigin(player, Lerp(tr.Fraction * FractionScale, now.Origin, wanted));
    }

    private static float Lerp(float frac, float a, float b) => a + (b - a) * frac;

    private static Vector Lerp(float frac, Vector a, Vector b) => a + (b - a) * frac;

    /// <summary>
    /// <c>Lerp&lt;QAngle&gt;</c> (mathlib.h:661-682): equal angles as they
    /// are; otherwise each through its quaternion, slerped, and back.
    /// </summary>
    private static QAngle Lerp(float frac, QAngle a, QAngle b)
    {
        if (a.Equals(b))
            return a;

        var q = BoneSetupMath.QuaternionSlerp(BoneSetupMath.AngleQuaternion(new BoneAngle(a.Pitch, a.Yaw, a.Roll)),
                                              BoneSetupMath.AngleQuaternion(new BoneAngle(b.Pitch, b.Yaw, b.Roll)),
                                              frac);

        // QuaternionAngles: the matrix's angles, a RadianEuler's (roll, pitch, yaw).
        RadianEuler e = BoneSetupMath.QuaternionAngles(q);
        const float RadToDeg = 180f / MathF.PI;
        return new QAngle(e.Y * RadToDeg, e.Z * RadToDeg, e.X * RadToDeg);
    }
}

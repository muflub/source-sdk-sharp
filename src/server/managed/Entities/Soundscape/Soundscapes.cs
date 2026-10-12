//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Shared.Soundscapes;

namespace SourceSharp.Server;

/// <summary>
/// Which soundscape each player is hearing.
///
/// <para>
/// This is the half of the soundscape system that is a DECISION, and it is the
/// reason the family could move at all. What a soundscape is — its loops, its
/// random waves, the other soundscapes it nests — is parsed out of
/// <c>scripts/soundscapes_*.txt</c> by both sides of the wire and indexed by a
/// number; that is data, and it stays native. Which of them reaches you is
/// arithmetic over positions, radii and lines of sight, and all of it is here.
/// </para>
///
/// <para>
/// The rule, which is stock's and worth stating because it is not obvious from
/// the code it came out of: <b>the nearest soundscape with a clear line to your
/// ears wins, and the one you already have keeps playing while it still
/// qualifies.</b> The second half is what stops a soundscape flickering between
/// two overlapping volumes as you walk the line between them.
/// </para>
///
/// <para>
/// Stock reaches that answer by walking every soundscape visible from the
/// player's visleaf cluster in whatever order a radix sort left them, tracing
/// to each candidate that could beat the incumbent. This walks them in
/// ASCENDING DISTANCE and stops at the first that traces clear, which reaches
/// the identical answer — nearest visible wins — with fewer traces and no
/// dependence on the order. See docs/divergences.md.
/// </para>
/// </summary>
public sealed class Soundscapes : ISoundscapes, IGameLifecycle, IFrameLifecycle
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    // Soundscape identity tokens restart with the map, which is safe: the
    // client's own soundscape state restarts with it too.
    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.Soundscapes;

    void IGameLifecycle.OnLevelShutdown() => Reset();

    // THE INDEX IS C#'S, plan_server_novalve.md P2.4. It was the native
    // registry in shell_soundscape.cpp (stock's CSoundscapeSystem), built at
    // LevelInitPreEntity by vstdlib's KeyValues parse; that parse is what
    // freed engine-allocated strings with server.so's allocator. The shared
    // SoundscapeLibrary is stock's traversal and was held to the native
    // registry's table, index for index, on dm_lockdown (255 soundscapes)
    // before the native one was deleted.
    void IGameLifecycle.OnLevelInitPreEntity(string mapName) => LoadLibrary(mapName);

    // Ahead of the arsenal, which is the order these two ran in when the frame
    // was a hand-written list in ServerModule.Frame.
    int IFrameLifecycle.FrameOrder => (int)FrameStage.Soundscapes;

    void IFrameLifecycle.OnThink(float frameTime) => Frame();

    /// <summary>
    /// How many positioned sounds one soundscape can name. The engine's
    /// NUM_AUDIO_LOCAL_SOUNDS, and the width of the bitfield that says which
    /// are live.
    /// </summary>
    public const int LocalSoundCount = 8;

    /// <summary>
    /// The trace budget per tick, from stock. Tuned for Team Fortress, where a
    /// good many maps give every soundscape an unlimited radius and so need
    /// this many traces for a single player.
    /// </summary>
    private const int MaxTracesPerTick = 20;

    private readonly IEngineEntities _entities;
    private readonly IUserMessageSender _messages;
    private readonly IEnginePlayers _playerApi;
    private readonly IEngineWorldQuery _world;

    /// <summary>
    /// A SERVICE takes its engine by constructor, unlike an entity, which takes
    /// it by property. There is no hierarchy here to cascade a parameter
    /// through, and being unable to construct the soundscape system without an
    /// engine is the right outcome — it does nothing else.
    /// </summary>
    private readonly IEngineFiles _files;

    private SoundscapeLibrary? _library;

    public Soundscapes(ILog log, IEngineEntities entities, IUserMessageSender messages,
                       IEnginePlayers players, IEngineWorldQuery world,
                       IEngineFiles files)
    {

        _log = log;
        _entities = entities;
        _messages = messages;
        _playerApi = players;
        _world = world;
        _files = files;
    }

    /// <summary>The level's soundscape library; empty before the first level.</summary>
    public SoundscapeLibrary? Library => _library;

    /// <summary>
    /// Builds the level's library: the manifest's files, then the map's own
    /// scripts/soundscapes_&lt;map&gt;.txt when the manifest did not name it.
    /// </summary>
    public void LoadLibrary(string mapName)
    {
        _library = SoundscapeLibrary.Load(_files.ReadGameFile, mapName,
                                          warning => _log.Warning($"[SourceSharp] {warning}\n"));

        // The marker the native registry was held to (tools/novalve-snapshot).
        if (System.Environment.GetEnvironmentVariable("SOURCESHARP_NOVALVE_SNAPSHOT") != null)
        {
            foreach (var soundscape in _library.All)
                _log.Msg($"[novalve-snapshot:soundscape] {soundscape.Index} {soundscape.Name}\n");
            _log.Msg($"[novalve-snapshot:soundscape-count] {_library.Count}\n");
        }
    }

    public int IndexOf(string name) => _library?.IndexOf(name) ?? -1;

    private readonly List<EnvSoundscape> _all = new();

    /// <summary>
    /// Where the round-robin over players resumes. Stock load-balances the same
    /// way, and for the same reason: one player's full check can cost twenty
    /// traces, so a full server must not do all of them in one tick.
    /// </summary>
    private int _activeIndex;

    /// <summary>
    /// What each player is hearing, by player slot. Kept here rather than read
    /// back from the engine because this is the only writer — the native
    /// soundscape system's own entity list is empty once these classes own the
    /// classnames, so nothing else can change it.
    /// </summary>
    private readonly Dictionary<uint, PlayerState> _players = new();

    private sealed class PlayerState
    {
        /// <summary>The automatic soundscape's id, or 0 for none.</summary>
        public int CurrentId;

        /// <summary>
        /// The trigger volumes this player is standing in, most recent first.
        /// A stack rather than a single value because volumes overlap, and
        /// leaving one should put you back in the one you were already in.
        /// </summary>
        public readonly List<EnvSoundscape> Triggered = new();
    }

    /// <summary>Every registered soundscape, in spawn order. For tests and debug-api.</summary>
    public IReadOnlyList<EnvSoundscape> All => _all;

    /// <summary>
    /// Adds a soundscape and gives it its identity token.
    ///
    /// <para>
    /// One-based, because zero is what the client reads as "no soundscape at
    /// all". Never reused within a level: the client compares this number with
    /// the last it saw to decide whether anything changed, so handing a new
    /// entity a dead one's id would make a genuine change look like none.
    /// </para>
    /// </summary>
    public int Register(EnvSoundscape soundscape)
    {
        _all.Add(soundscape);
        return _all.Count;
    }

    /// <summary>
    /// Forgets everything. Called at level shutdown — ids restart with the map,
    /// which is safe because the client's own soundscape state does too.
    /// </summary>
    public void Reset()
    {
        _all.Clear();
        _players.Clear();
        _activeIndex = 0;
    }

    /// <summary>
    /// Whether a soundscape at <paramref name="range"/> is close enough to be
    /// heard at all.
    ///
    /// <para>
    /// A radius of -1 means unlimited, which a great many maps use, and it is
    /// the reason the trace budget exists. A radius of zero means never, which
    /// is what an <c>env_soundscape_triggerable</c> has: it is reached by
    /// standing in a volume, not by being near it.
    /// </para>
    /// </summary>
    public static bool WithinRadius(float radius, float range)
        => radius == -1f || radius > range;

    //-------------------------------------------------------------------------
    // Trigger volumes.
    //
    // These override the automatic choice outright rather than competing with
    // it: standing in a trigger_soundscape means hearing what it names.
    //-------------------------------------------------------------------------

    /// <summary>A player entered a volume naming this soundscape.</summary>
    public void EnterTrigger(EntityHandle player, EnvSoundscape soundscape)
    {
        var state = StateFor(player);

        // Removed first in case we are already in the list, which happens when
        // a player noclips out and back in without an end touch. Stock does the
        // same and says the same thing about why.
        state.Triggered.Remove(soundscape);
        state.Triggered.Insert(0, soundscape);

        Apply(player, soundscape);
    }

    /// <summary>
    /// A player left a volume. Whatever they were standing in before takes over
    /// again; if there is nothing, they hear nothing.
    /// </summary>
    public void LeaveTrigger(EntityHandle player, EnvSoundscape soundscape)
    {
        var state = StateFor(player);
        state.Triggered.Remove(soundscape);

        if (state.Triggered.Count > 0)
        {
            Apply(player, state.Triggered[0]);
            return;
        }

        // Silence rather than falling back to the automatic choice, which is
        // stock: leaving a soundscape trigger sets entIndex to 0 and the next
        // automatic pass may or may not put something there.
        state.CurrentId = 0;
        Push(player, -1, 0, 0, ReadOnlySpan<Vector>.Empty);
    }

    /// <summary>How many trigger volumes a player is inside. For tests.</summary>
    public int TriggerDepth(EntityHandle player)
        => _players.TryGetValue(player.Value, out var state) ? state.Triggered.Count : 0;

    /// <summary>The soundscape a player is hearing, by id. For tests and debug-api.</summary>
    public int CurrentId(EntityHandle player)
        => _players.TryGetValue(player.Value, out var state) ? state.CurrentId : 0;

    /// <summary>A player left the game.</summary>
    public void Forget(EntityHandle player) => _players.Remove(player.Value);

    //-------------------------------------------------------------------------
    // The automatic pass.
    //-------------------------------------------------------------------------

    /// <summary>
    /// One tick's worth of choosing, spread across players.
    ///
    /// <para>
    /// The budget is stock's: at most twenty traces and at most half the
    /// server's slots per tick, and always at least one player's full share —
    /// which is what makes a single-player game update every tick and a full
    /// one degrade rather than stall.
    /// </para>
    /// </summary>
    public void Frame()
    {
        if (_all.Count == 0)
            return;

        int maxPlayers = Math.Max(1, EngineLimits.MaxPlayerSlots / 2);
        int traceCount = 0;
        int playerCount = 0;

        for (int i = 0; i < EngineLimits.MaxPlayerSlots
                        && traceCount <= MaxTracesPerTick
                        && playerCount <= maxPlayers; i++)
        {
            _activeIndex = (_activeIndex + 1) % EngineLimits.MaxPlayerSlots;

            var player = _playerApi.GetHandle(_activeIndex + 1);

            if (!_entities.IsValid(player))
                continue;

            traceCount += UpdateForPlayer(player);
            playerCount++;
        }
    }

    /// <summary>
    /// Chooses for one player and returns how many traces it cost.
    /// </summary>
    private int UpdateForPlayer(EntityHandle player)
    {
        var state = StateFor(player);

        // A trigger volume wins outright and costs nothing to honour: it was
        // already applied when the player walked in.
        if (state.Triggered.Count > 0)
            return 0;

        Vector ears = _entities.GetEyePosition(player);
        int traces = 0;

        // The incumbent first. It keeps playing while it still qualifies, which
        // is what stops a soundscape flickering between two overlapping volumes
        // as a player walks the line between them.
        var current = Find(state.CurrentId);
        float currentDistance = 0f;
        bool haveCurrent = false;

        if (current is not null && current.Enabled)
        {
            float range = (ears - current.Ears).Length;

            if (WithinRadius(current.Radius, range))
            {
                traces++;

                if (Reaches(current.Ears, ears, player))
                {
                    haveCurrent = true;
                    currentDistance = range;
                }
            }
        }

        // Everything else, nearest first. Ascending order is what lets the walk
        // stop at the first clear line: nothing further away can beat it, so
        // the answer is the same one stock reaches by tracing to every
        // candidate that could.
        Span<int> order = stackalloc int[0];
        var candidates = new List<(float Range, EnvSoundscape Soundscape)>();

        foreach (var soundscape in _all)
        {
            if (soundscape == current || !soundscape.Enabled || soundscape.IsTriggerable)
                continue;

            float range = (ears - soundscape.Ears).Length;

            if (!WithinRadius(soundscape.Radius, range))
                continue;

            if (haveCurrent && range >= currentDistance)
                continue;

            candidates.Add((range, soundscape));
        }

        candidates.Sort(static (a, b) => a.Range.CompareTo(b.Range));

        foreach (var (_, soundscape) in candidates)
        {
            if (traces >= MaxTracesPerTick)
                break;

            traces++;

            if (!Reaches(soundscape.Ears, ears, player))
                continue;

            Apply(player, soundscape);
            return traces;
        }

        // Nothing new won. If the incumbent no longer qualifies the player
        // keeps hearing it anyway, which is stock: a soundscape is only ever
        // replaced by another, never switched off by walking out of range.
        return traces;
    }

    /// <summary>
    /// Whether sound gets from one place to the other.
    ///
    /// <para>
    /// Brushes AND water, which is the mask stock uses and is not the same as
    /// what a bullet would hit: a soundscape on the far side of a pool does not
    /// reach a swimmer, and props do not block sound at all.
    /// </para>
    /// </summary>
    private bool Reaches(Vector from, Vector to, EntityHandle ignore)
        => !_world.TraceLine(from, to, TraceMask.BrushesAndWater, ignore).Hit;

    /// <summary>Tells the player, and remembers that we did.</summary>
    private void Apply(EntityHandle player, EnvSoundscape soundscape)
    {
        var state = StateFor(player);
        state.CurrentId = soundscape.SoundscapeId;

        Span<Vector> positions = stackalloc Vector[LocalSoundCount];
        int bits = soundscape.ResolveLocalSounds(positions);

        Push(player, soundscape.SoundscapeIndex, soundscape.SoundscapeId, bits, positions);

        soundscape.OnPlay.Fire();
    }

    /// <summary>
    /// <b>Tells ONE player what they are hearing</b> — plan_entity.md step
    /// 8e-sound, and this method is the whole of what that step changed on this
    /// side.
    ///
    /// <para>
    /// <b>WHAT IT REPLACED, and why the replacement is a message rather than a
    /// slot.</b> This used to call <c>IEngineSound.PlayerSetSoundscape</c>,
    /// whose native body was EMPTY and had been since step 13c: stock networked
    /// <c>audioparams_t</c> through <c>CBasePlayer</c>'s local-data table, that
    /// class retired, and the standalone player carries no equivalent. So the
    /// decisions this class makes — all of them correct, and measured to change
    /// on entry — reached nobody. Step 7e read it from the other end: 255
    /// soundscapes parsed on <c>dm_lockdown</c>, a player standing inside one,
    /// and the client's current soundscape <c>-1 [none]</c>.
    /// </para>
    ///
    /// <para>
    /// The alternative was to put <c>audioparams_t</c> back on the player's
    /// declared networked state, which is what stock did. It costs eight
    /// <c>Vector</c>s and three ints on EVERY player snapshot for a value that
    /// changes a few times a map, and it edits <c>PlayerNetState</c>. A user
    /// message costs those bytes when the answer changes and nothing the rest of
    /// the time.
    /// </para>
    ///
    /// <para>
    /// <b>Zero is EVERYBODY on the send call</b>, one integer away from the
    /// "no such player" answer, so the index is checked here rather than passed
    /// down — a soundscape meant for one player arriving on every screen is the
    /// kind of failure nobody reports as a bug.
    /// </para>
    /// </summary>
    private void Push(EntityHandle player, int soundscapeIndex, int entIndex,
                      int localBits, ReadOnlySpan<Vector> localPositions)
    {
        int index = _entities.GetIndex(player);

        if (index <= 0)
            return;

        // Reliable: a soundscape push is edge-triggered. Dropping one leaves the
        // player hearing the previous room until they walk into a third, which
        // is exactly the symptom this step exists to remove.
        _messages.Send(SoundscapeMessage.PayloadOf(soundscapeIndex, entIndex, localBits, localPositions),
                       index, reliable: true);
    }

    private EnvSoundscape? Find(int id)
        => id > 0 && id <= _all.Count ? _all[id - 1] : null;

    private PlayerState StateFor(EntityHandle player)
    {
        if (!_players.TryGetValue(player.Value, out var state))
        {
            state = new PlayerState();
            _players[player.Value] = state;
        }

        return state;
    }
}

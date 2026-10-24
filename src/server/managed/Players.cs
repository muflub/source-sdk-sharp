//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// What the server remembers about one player.
///
/// The plan called this the thing Phase 4 needed first, and it was right: half
/// a dozen separate dictionaries keyed by handle had grown up around the code,
/// each holding one field and each with its own idea of when to forget a
/// player. One record with one lifetime is the difference between "spawn
/// protection expires" and "spawn protection expires, and the frag count and
/// the respawn deadline agree about who this is".
/// </summary>
public sealed class PlayerRecord
{
    /// <summary>Server time they last spawned. Spawn protection counts from here.</summary>
    public float SpawnedAt { get; set; }

    /// <summary>Server time they last died, or -1 if they are alive.</summary>
    public float DiedAt { get; set; } = -1f;

    /// <summary>Whether the death cycle has already put them into spectating.</summary>
    public bool Watching { get; set; }

    public int Kills { get; set; }
    public int Deaths { get; set; }
}

/// <summary>
/// The players, and the cycle of dying and coming back.
///
/// That cycle is a game rule end to end, and this owns it: how long the wait
/// is, what you watch while you wait, and what ends the wait. The engine
/// supplies three things it cannot do itself — whether a player is alive, what
/// they are watching, and putting them back in the world.
/// </summary>
public sealed class Players : IPlayerDirectory, IGameLifecycle, IFrameLifecycle
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    // Handles do not survive a map change, so keeping player records would
    // leave entries for players who no longer exist and whose handles may since
    // have been reused by something else entirely.
    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.Players;

    void IGameLifecycle.OnLevelInit(string mapName) => Reset();

    private readonly Dictionary<uint, PlayerRecord> _records = new();
    private readonly IEngineEntities _entities;
    private readonly IEnginePlayers _players;

    /// <summary>
    /// A SERVICE takes its engine by constructor, unlike an entity, which takes
    /// it by property. There is no hierarchy here to cascade a parameter
    /// through, and being unable to construct this without an engine is the
    /// right outcome.
    /// </summary>
    public Players(ILog log, IEngineEntities entities, IEnginePlayers players,
                   IGameClock clock, IPlayerButtons buttons, IHooks<IRespawnGate> gates,
                   IEntityDirectory? directory = null, Observers? observers = null)
    {
        _directory = directory;
        _observers = observers;
        _log = log;
        _entities = entities;
        _players = players;
        _clock = clock;
        _buttons = buttons;
        _gates = gates;
    }

    /// <summary>
    /// The mode proxy, for the active mode's <see cref="IRespawnPolicy"/>
    /// (tf2 stage 0.7 part C), wired by <see cref="AttachModes"/>.
    /// </summary>
    private GameModes? _modes;

    /// <summary>
    /// The wiring the module does once, before the first frame: the proxy the
    /// death cycle asks for its respawn policy. An ATTACH and not a
    /// constructor parameter because the proxy cannot be constructed this
    /// early — it takes the ConVarRegistry, which the module registers only
    /// later — and `Create` resolves constructor arguments eagerly, so asking
    /// for it by parameter would make the live game pass null. Same reason the
    /// proxy is attached to its own machine (<c>GameModes.Attach</c>). Null
    /// stays the no-owner arm: a test that builds the roster without ever
    /// attaching gets this class's own answers, exactly as before modes.
    /// </summary>
    internal void AttachModes(GameModes modes) => _modes = modes;

    /// <summary>The mods' vetoes on respawning. See <see cref="MayRespawn"/>.</summary>
    private readonly IHooks<IRespawnGate> _gates;

    /// <summary>
    /// Whether every <see cref="IRespawnGate"/> lets <paramref name="player"/>
    /// back in. A gate that throws is logged and does not veto: a broken mod
    /// must not trap a player dead.
    /// </summary>
    private bool MayRespawn(EntityHandle player)
    {
        int entIndex = _entities.GetIndex(player);

        foreach (var gate in _gates.All)
        {
            try
            {
                if (!gate.MayRespawn(player, entIndex))
                    return false;
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp/Players] respawn gate {gate.GetType().FullName} threw: {e.Message}\n");
            }
        }

        return true;
    }

    /// <summary>
    /// The active mode's respawn policy, or null: no proxy, no picked mode, or
    /// a mode that says "the core's own answer" by leaving the concern out.
    /// The two halves are asked together or not at all — a mode answers both or
    /// neither, because a long wait with no release and a short wait with a
    /// silent gate are the two ways a mode can accidentally trap a player, and
    /// the interface makes the pair one decision (<see cref="IRespawnPolicy"/>).
    /// </summary>
    private IRespawnPolicy? Policy => _modes?.Active?.Respawn;

    /// <summary>
    /// Seconds this player waits from their death: the mode's number when it
    /// owns the concern, this class's <see cref="RespawnDelay"/> otherwise. A
    /// policy that throws is named and treated as no policy — the wait the
    /// player would have waited anyway is what a broken mod must leave behind,
    /// not a player held dead by it (the rule the gate walk above applies to a
    /// throwing veto, for the same reason).
    /// </summary>
    private float DelayFor(EntityHandle player)
    {
        if (Policy is not { } policy)
            return RespawnDelay;

        try
        {
            return Math.Max(0f, policy.Delay(player));
        }
        catch (Exception e)
        {
            _log.Warning($"[SourceSharp/Players] mode respawn policy {policy.GetType().FullName} " +
                         $"threw on Delay: {e.Message}\n");
            return RespawnDelay;
        }
    }

    /// <summary>
    /// Whether the mode lets this player out right now — a wave's release, or
    /// the plain yes. Null policy and a throw are both yes: the core's own
    /// answer is that a wait ends when it ends, and the mode that owns the
    /// hold is the one that has to say so on purpose.
    /// </summary>
    private bool WaveAllows(EntityHandle player, float now)
    {
        if (Policy is not { } policy)
            return true;

        try
        {
            return policy.WaveRelease(player, now);
        }
        catch (Exception e)
        {
            _log.Warning($"[SourceSharp/Players] mode respawn policy {policy.GetType().FullName} " +
                         $"threw on WaveRelease: {e.Message}\n");
            return true;
        }
    }

    private readonly IGameClock _clock;
    private readonly IEntityDirectory? _directory;
    private readonly Observers? _observers;
    private readonly IPlayerButtons _buttons;

    //-------------------------------------------------------------------------
    // The death cycle, every frame.
    //
    // After the rules and before the pickups -- the order these three lines had
    // when ServerModule.OnGameRulesThink was a hand-written list.
    //
    // It has to see a player the tick they die, which is why it is a frame
    // participant rather than something driven off a timer, and why "how long
    // have you been dead" is measured against the same clock the rules use.
    //-------------------------------------------------------------------------
    int IFrameLifecycle.FrameOrder => (int)FrameStage.Players;

    void IFrameLifecycle.OnThink(float frameTime) => Think(_clock.Now, WantsToRespawn);

    /// <summary>
    /// Whether a dead player is asking to respawn.
    ///
    /// <para>
    /// Any of attack, jump or reload, which is more forgiving than stock's
    /// attack-only: a player who has been watching the room they died in for
    /// three seconds is trying to get back in, and quibbling about which key
    /// they pressed helps nobody.
    /// </para>
    ///
    /// <para>
    /// Here rather than on the module, which is where it used to be. It is the
    /// death cycle's own rule and nothing else asks it, so it belongs with the
    /// death cycle -- and it is the last thing that made Think need an argument
    /// the module had to supply.
    /// </para>
    /// </summary>
    private bool WantsToRespawn(EntityHandle player)
    {
        int buttons = _buttons.Held(player);

        const int attack = 1 << 0, jump = 1 << 1, reload = 1 << 13;

        return (buttons & (attack | jump | reload)) != 0;
    }

    /// <summary>
    /// How long a dead player watches before they may come back.
    ///
    /// Zero means the moment they ask. The wait is a rule about pacing, which
    /// is why it is here and not a property of the player entity.
    /// </summary>
    public float RespawnDelay { get; set; } = 3f;

    /// <summary>
    /// What a dead player watches when no managed observer is wired (tests);
    /// with one, stock HL2MP's death cam (mode NONE) until respawn.
    /// </summary>
    public ObserverMode DeathView { get; set; } = ObserverMode.Roaming;

    /// <summary>
    /// Whether a dead player leaves their weapon behind.
    ///
    /// A rule about how a deathmatch feels rather than a property of the player:
    /// it is what makes killing somebody worth walking to.
    /// </summary>
    public bool DropWeaponOnDeath { get; set; } = true;

    /// <summary>How hard the dropped weapon is thrown, along the victim's aim.</summary>
    public float DropSpeed { get; set; } = 250f;

    /// <summary>The record for a player, created on first use.</summary>
    public PlayerRecord this[EntityHandle player]
    {
        get
        {
            if (!_records.TryGetValue(player.Value, out var record))
                _records[player.Value] = record = new PlayerRecord();

            return record;
        }
    }

    /// <summary>Whether anything is known about a player, without inventing it.</summary>
    public bool Knows(EntityHandle player) => _records.ContainsKey(player.Value);

    public IEnumerable<(EntityHandle Player, PlayerRecord Record)> All
        => _records.Select(pair => (new EntityHandle(pair.Key), pair.Value));

    public void OnSpawned(EntityHandle player, float now)
    {
        // A player the server has not seen before spectates in DeathView
        // (m_iObserverLastMode) until they choose a mode (spec_mode).
        if (!Knows(player) && _directory?.Get(player) is SsPlayer fresh)
            fresh.Observer.LastMode = DeathView;

        var record = this[player];

        record.SpawnedAt = now;
        record.DiedAt = -1f;
        record.Watching = false;
    }

    /// <summary>
    /// Records a death and scores it, as stock's
    /// <c>CMultiplayRules::PlayerKilled</c> does under HL2MP's rules (no
    /// teamplay, <c>UseSuicidePenalty</c>): a death against the victim; a kill
    /// by another PLAYER is a frag to them (<c>IPointsForKill</c>, 1); a kill
    /// by yourself, the world or anything that is not a player costs the victim
    /// a frag. <see cref="PlayerRecord.Kills"/> is stock's frag count, which is
    /// what the scoreboard shows, so it can go below zero.
    /// </summary>
    public void OnKilled(EntityHandle player, EntityHandle attacker, float now)
    {
        var record = this[player];

        record.DiedAt = now;
        record.Deaths++;

        // GetDeathScorer: the attacker if it is a player. Asked of the engine,
        // and only for a real handle: None and the world are nobody, and a
        // prop or a trigger -- which now arrive as attackers -- score nothing.
        bool scorer = attacker != EntityHandle.None && attacker != player
                   && _entities.IsPlayer(attacker);

        if (scorer)
            this[attacker].Kills++;
        else
            record.Kills--;

        if (DropWeaponOnDeath)
            Drop(player);

        // The rest of the inventory dies with them — stock's RemoveAllItems on
        // death, which is what makes the respawn loadout a fresh start rather
        // than a top-up. Routed through the engine interface so the hierarchy
        // decorator (Phase 10) answers for hierarchy players.
        _players.RemoveAllItems(player, stripSuit: false);
    }

    /// <summary>
    /// Leaves the victim's weapon where they fell, thrown along their aim.
    ///
    /// Thrown rather than dropped straight down so it does not land inside the
    /// body and become unreachable, which is what a weapon dropped at zero
    /// velocity tends to do.
    /// </summary>
    private void Drop(EntityHandle player)
    {
        var aim = Angles.Vectors(_entities.GetEyeAngles(player));

        _players.DropActiveWeapon(player, aim.Forward * DropSpeed + aim.Up * (DropSpeed * 0.4f));
    }

    /// <summary>
    /// Runs the death cycle for every player, once a frame.
    ///
    /// Two transitions, and they are deliberately separate. A player who has
    /// just died falls into the death cam. A player who has waited long enough and is
    /// asking to come back does. Merging them would respawn people who were
    /// still looking at the room they died in.
    /// </summary>
    public void Think(float now, Func<EntityHandle, bool> wantsToRespawn)
    {
        foreach (var (player, record) in _records.Select(p => (new EntityHandle(p.Key), p.Value)).ToArray())
        {
            if (!_entities.IsValid(player))
                continue;

            if (_players.IsAlive(player))
            {
                record.DiedAt = -1f;
                record.Watching = false;
                continue;
            }

            if (record.DiedAt < 0f)
                continue;

            var watcher = _directory?.Get(player) as SsPlayer;

            if (!record.Watching)
            {
                record.Watching = true;

                // Stock HL2MP: the dead body falls not solid and the mode
                // stays NONE (the client's death cam) until respawn;
                // CHL2MP_Player::StartObserverMode refuses a death timeout.
                if (_observers is not null && watcher is not null)
                    _observers.Killed(watcher, record.DiedAt);
                else
                    _players.SetObserverMode(player, DeathView);

                _log.Msg($"[SourceSharp/Players] {player} dead, may return in " +
                           $"{Math.Max(0f, DelayFor(player) - (now - record.DiedAt)):F1}s\n");
                continue;
            }

            if (_observers is not null && watcher is not null)
            {
                _observers.DeathThink(watcher, now);

                // CHL2MP_Player::PlayerDeathThink: an observer does not come back on a button.
                if (!Observers.MayRespawn(watcher))
                    continue;
            }

            // The wait and the release, both the mode's when it owns the
            // concern (tf2 stage 0.7 part C): a long delay plus a false
            // release keeps the player dead until the mode says otherwise —
            // the pair is why the policy answers them together
            // (IRespawnPolicy). No policy is this class's own answer: the
            // deathmatch wait and the plain yes.
            if (now - record.DiedAt < DelayFor(player))
                continue;

            if (!WaveAllows(player, now))
                continue;

            if (!wantsToRespawn(player))
                continue;

            if (!MayRespawn(player))
                continue;

            _log.Msg($"[SourceSharp/Players] {player} respawning after " +
                       $"{now - record.DiedAt:F1}s dead\n");

            if (_observers is not null && watcher is not null)
                _observers.Stop(watcher);

            _players.Respawn(player);

            record.DiedAt = -1f;
            record.Watching = false;
        }
    }

    /// <summary>Forgets a player. Called when they leave, so a rejoin starts clean.</summary>
    public void Forget(EntityHandle player) => _records.Remove(player.Value);

    /// <summary>
    /// Forgets everyone, at level change.
    ///
    /// Handles do not survive a map change, so keeping the records would leave
    /// this holding entries for players who no longer exist and whose handles
    /// may since have been reused by something else entirely.
    /// </summary>
    public void Reset() => _records.Clear();

    /// <summary>Seconds a player has left to wait, or zero. For the HUD and for tests.</summary>
    public float WaitRemaining(EntityHandle player, float now)
    {
        var record = this[player];

        if (record.DiedAt < 0f)
            return 0f;

        return Math.Max(0f, DelayFor(player) - (now - record.DiedAt));
    }
}

/// <summary>
/// <c>sourcesharp_player_shadow</c> — whether a player is a body in the physics
/// solver, and therefore whether walking into a crate moves it.
///
/// <para>
/// <b>WHY IT IS DECLARED HERE.</b> It was <c>ConVar sourcesharp_player_shadow</c>
/// in <c>shell_standalone_player.cpp</c>, one of the shim's seventy-eight native
/// convar declarations, and it is not engine plumbing: it decides what the game
/// does. plan_entity.md §6 has no ConVar row and the shim-audit's standing rule
/// is that no convar's default or semantics is declared natively.
/// </para>
///
/// <para>
/// It registers with the engine like every other managed convar, so
/// <c>sourcesharp_player_shadow 0</c> still works from the console, from
/// <c>autoexec.cfg</c> and from the command line — which is what
/// <c>tools/3f-shadow-ab</c> and the live gate's control run stand on, and both
/// are unchanged by the move.
/// </para>
///
/// <para>
/// The server's composition reads the DECLARATION live and not a copy of its
/// value, so a value bound before any change notification — a
/// <c>+sourcesharp_player_shadow 0</c> on the command line — is still the one
/// the shadow step reads. It is that composition's own convar, handed to
/// <c>PlayerCommandsBinding</c>, not a static (plan_abi_hotreload D7).
/// </para>
/// </summary>
[ConVar("sourcesharp_player_shadow", "1",
        Help = "SourceSharp: give the player a VPhysics shadow, so walking into a prop "
             + "moves it (plan_entity.md step 3f). 0 is the build before that step.")]
public sealed class PlayerShadowConVar : BoolConVar
{
}

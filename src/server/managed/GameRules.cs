//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using System;
using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

// Gameplay policy that CMultiplayRules would otherwise answer in C++.
//
// The base class stays as mechanism — it still runs the respawn machinery, the
// damage pipeline and the spawn sequence — but the decisions are made here. Each
// one can decline, and the shell then applies Source's own rule, so a gap in
// managed policy degrades to stock behaviour rather than to none.

/// <summary>Seconds a dead player waits. Negative uses the stock rule.</summary>
[ConVar("cs_sv_respawn_delay", "-1", Help = "Respawn delay in seconds; negative uses the stock rule")]
public sealed class RespawnDelayConVar : FloatConVar { }

/// <summary>Kills needed to end the round. Zero or less disables the limit.</summary>
[ConVar("cs_sv_fraglimit", "0", Help = "Kills required to win the round; 0 disables")]
public sealed class FragLimitConVar : IntConVar { }

/// <summary>Seconds of invulnerability after spawning. Zero disables it.</summary>
[ConVar("cs_sv_spawn_protect", "0", Help = "Seconds of damage immunity after spawning")]
public sealed class SpawnProtectConVar : FloatConVar { }

/// <summary>
/// Ends the round on demand.
///
/// Exists to exercise the intermission path without needing kills — the frag
/// limit that normally triggers it cannot be reached without interactive play,
/// and EndRound is the one new call here that reaches deep into the base rules.
/// </summary>
[ConCommand("cs_sv_end_round", Help = "End the current round and go to intermission")]
public sealed class EndRoundCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEngineHost _host;

    public EndRoundCommand(ILog log, IEngineHost host, Intermission? intermission = null)
    {

        _log = log;
        _host = host;
        _intermission = intermission;
    }

    private readonly Intermission? _intermission;

    public void Execute(string args)
    {
        _log.Msg("[SourceSharp/Rules] ending round on request\n");
        if (_intermission is not null)
            _intermission.Begin(_host.CurrentTime);
        else
            _host.EndRound();
    }
}

/// <summary>
/// The rules themselves.
///
/// Kept out of ServerModule because these are policy, not lifecycle — and
/// because keeping them together makes it obvious which decisions the mod has
/// actually taken over and which still fall through to Source.
/// </summary>
public sealed class GameRules : IGameRules, IGameLifecycle, IFrameLifecycle
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    //-------------------------------------------------------------------------
    // Lifecycle. LifecycleStage.Rules -- FIRST, and that is stock's order rather than a
    // preference: ServerModule.LevelInit reset the round before it reset
    // anything else, and per-round state that other participants read while
    // clearing their own has to be cleared before they look at it.
    //
    // The numbers are spaced by ten so a participant can be put BETWEEN two
    // without renumbering the rest, which is the whole reason they are numbers
    // and not an enum.
    //-------------------------------------------------------------------------
    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.Rules;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        ResetRound();
        _levelStartedAt = _host.CurrentTime;
        _intermission?.LevelInit(mapName, _levelStartedAt);
    }

    //-------------------------------------------------------------------------
    // The win check, every frame, after the world has settled.
    //
    // FIRST of the three that used to be ServerModule.OnGameRulesThink, and
    // after FrameStage.Arsenal, which used to be the tail of ServerModule.Frame --
    // which together is the exact order the two native hooks ran in.
    //
    // WHERE THIS RUNS IS THE POINT OF THE MOVE. It was reached from
    // CSourceSharpGameRules::Think, a CGameRules virtual driven by
    // CGameRules::FrameUpdatePostEntityThink -- so the managed think ran
    // because the rules object was a game system. It now runs from
    // CSourceSharpSystem's own FrameUpdatePostEntityThink, at the same point in
    // the same frame, and owes the rules class nothing.
    //-------------------------------------------------------------------------
    int IFrameLifecycle.FrameOrder => (int)FrameStage.Rules;

    void IFrameLifecycle.OnThink(float frameTime) => Think();

    private readonly RespawnDelayConVar? _respawnDelay;
    // Declared in ServerModule alongside the relationship rule, and reused here:
    // "are they teammates" and "may teammates hurt each other" are two halves of
    // one setting, so they must not drift apart into two ConVars.
    private readonly FriendlyFireConVar? _friendlyFire;
    private readonly SpawnProtectConVar? _spawnProtect;
    private readonly FragLimitConVar? _fragLimit;

    /// <summary>Set once the round has been ended, so it is ended only once.</summary>
    private bool _roundOver;

    /// <summary>Server time each player last spawned, for spawn protection.</summary>
    private readonly Dictionary<uint, float> _spawnedAt = new();

    /// <summary>
    /// A SERVICE takes its engine by constructor, unlike an entity, which takes
    /// it by property. There is no hierarchy here to cascade a parameter
    /// through, and being unable to construct this without an engine is the
    /// right outcome.
    /// </summary>
    private readonly IEngineHost _host;
    private readonly IEnginePlayers _players;
    private readonly IEngineEntities _entities;
    private readonly IScoreboard _scoreboard;
    private readonly Intermission? _intermission;
    private readonly MpFragLimitConVar? _mpFragLimit;
    private readonly MpTimeLimitConVar? _mpTimeLimit;
    private float _levelStartedAt;

    public GameRules(ILog log, ConVarRegistry convars, IEngineHost host, IEnginePlayers players,
                     IEngineEntities entities, IScoreboard scoreboard,
                     IGameEvent<PlayerDeathArgs> deaths, Intermission? intermission = null,
                     ConVarRegistry? mpConVars = null)
    {
        _intermission = intermission;
        _mpFragLimit = (mpConVars ?? convars).Get<MpFragLimitConVar>();
        _mpTimeLimit = (mpConVars ?? convars).Get<MpTimeLimitConVar>();

        _log = log;
        _host = host;
        _players = players;
        _entities = entities;
        _scoreboard = scoreboard;

        _respawnDelay = convars.Get<RespawnDelayConVar>();
        _friendlyFire = convars.Get<FriendlyFireConVar>();
        _spawnProtect = convars.Get<SpawnProtectConVar>();
        _fragLimit    = convars.Get<FragLimitConVar>();

        // This module's death event, not a process-wide one. It used to be a
        // static event, so the subscription was made once and routed through a
        // static "current rules" pointer, lest an old rules object keep scoring
        // after a reload. An event per container has no old subscribers: a
        // reload builds a new module, a new event and new rules together.
        deaths.Fired += ScoreDeath;
    }

    /// <summary>
    /// What every player gets on spawn — weapons and the ammo to use them.
    ///
    /// On the game rules, not the player: which loadout you get is the mode's
    /// decision, and HL2MP putting it in CHL2MP_Player::GiveDefaultItems is why
    /// changing it there means editing the player class.
    ///
    /// The weapon entity classes do not exist yet, so giving them warns and does
    /// nothing; the ammo does apply. Registering the intent here means only the
    /// entity side is left to build.
    /// </summary>
    public static readonly ImmutableArray<string> Loadout =
    [
        "weapon_crowbar",
        "weapon_pistol",
        "weapon_smg1",
    ];

    /// <summary>Ammo handed out alongside the loadout.</summary>
    public static readonly ImmutableArray<(string Ammo, int Count)> StartingAmmo =
    [
        ("Pistol", 100),
        ("SMG1",    45),
    ];


    //-------------------------------------------------------------------------

    /// <summary>
    /// Whether an attack lands. Answers two rules; anything else defers.
    /// </summary>
    public Decision CanTakeDamage(EntityHandle player, EntityHandle attacker,
                                  float damage, int damageType, float now)
    {
        // Spawn protection. Checked before friendly fire because it should hold
        // regardless of who is shooting — including the world.
        float protect = _spawnProtect?.Value ?? 0f;
        if (protect > 0f && _spawnedAt.TryGetValue(player.Value, out float spawnedAt))
        {
            if (now - spawnedAt < protect)
                return Decision.No;
        }

        // Self-damage is never friendly fire — falling, and your own grenade,
        // should still hurt whatever the team rules say.
        if (attacker.Value == player.Value)
            return Decision.UseStockRule;

        // Friendly fire. Only answered when it is OFF and the two are actually
        // on the same team; anything else defers, so this rule can never make
        // damage land that the stock rules would have blocked.
        if (_friendlyFire?.Value == false && _entities.IsValid(attacker))
        {
            int victimTeam   = _players.GetTeam(player);
            int attackerTeam = _players.GetTeam(attacker);

            // Both must be real players on a real team. A -1 means the attacker
            // was not a player at all (a trigger, the world), which is not
            // friendly fire and must not be blocked.
            if (victimTeam >= 0 && victimTeam == attackerTeam)
                return Decision.No;
        }

        return Decision.UseStockRule;
    }

    /// <summary>Whether a dead player may come back. Deferred for now.</summary>
    public Decision CanRespawn(EntityHandle player) => Decision.UseStockRule;

    /// <summary>Seconds until respawn, or negative for the stock rule.</summary>
    public float GetRespawnDelay(EntityHandle player) => _respawnDelay?.Value ?? -1f;

    /// <summary>
    /// Hands out the loadout and starts the spawn-protection clock.
    /// </summary>
    public void OnPlayerSpawned(EntityHandle player, float now)
    {
        _spawnedAt[player.Value] = now;

        int given = 0;
        foreach (string weapon in Loadout)
        {
            if (_players.GiveItem(player, weapon))
                given++;
        }

        // Ammo before weapons would be equally valid; after is chosen so a
        // weapon that sets its own default clip does not overwrite the grant.
        int ammoGiven = 0;
        foreach (var (ammo, count) in StartingAmmo)
            ammoGiven += _players.GiveAmmo(player, count, ammo);

        _log.Msg($"[SourceSharp/Rules] {player} spawned: gave {given}/{Loadout.Length} item(s), " +
                   $"{ammoGiven} ammo" +
                   $"{((_spawnProtect?.Value ?? 0f) > 0f ? $", protected for {_spawnProtect!.Value:F1}s" : "")}\n");
    }

    /// <summary>Forgets a player's spawn time. Called when they leave.</summary>
    public void Forget(EntityHandle player) => _spawnedAt.Remove(player.Value);

    /// <summary>Test seam: whether a player is currently spawn-protected.</summary>
    public bool IsProtected(EntityHandle player, float now)
    {
        float protect = _spawnProtect?.Value ?? 0f;
        return protect > 0f
            && _spawnedAt.TryGetValue(player.Value, out float at)
            && now - at < protect;
    }

    //-------------------------------------------------------------------------
    // Win conditions.
    //-------------------------------------------------------------------------

    //-------------------------------------------------------------------------
    // Scoring.
    //
    // What a death is WORTH is a game rule and lives here; the running total is
    // Scoreboard's, in Shared. The arithmetic deliberately matches what
    // CMultiplayRules::PlayerKilled does in C++, and that is not conservatism:
    // that function still runs on every death, and it is what writes m_iFrags
    // and m_iDeaths on the player — which is what CPlayerResource networks to
    // every client. So this does NOT call _players.AddScore; a managed
    // award on top of the stock one would double every point.
    //
    // What it does own is the number the frag limit is read against, which
    // until now was a different one arrived at by different arithmetic: raw
    // credited kills, with no suicide penalty and no intermission rule, so a
    // round could end on a total no player could see.
    //-------------------------------------------------------------------------

    /// <summary>
    /// What one death does to the board.
    ///
    /// Suicides and world deaths are treated alike, because the engine reports
    /// them alike and stock scores them alike: neither credits a killer, both
    /// cost the victim a point, and both count as a death. That a suicide still
    /// counts as a death is the part worth stating — it is what stops dying on
    /// purpose being cheaper than being killed.
    /// </summary>
    public void ScoreDeath(PlayerDeathArgs death)
    {
        // Server only. The client sees a networked copy of the same event, and
        // scoring it there as well would double every kill.
        if (_host.ActiveSide != Side.Server)
            return;

        // Nothing scores once the round is over, which is what stock HL2MP's
        // PlayerKilled does with IsIntermission: kills traded over the
        // scoreboard screen must not decide a round that has already ended.
        if (_roundOver)
            return;

        _scoreboard.Add(death.UserId, 0, 1, death.IsCredited ? 0 : -1);

        if (death.IsCredited)
            _scoreboard.Add(death.AttackerUserId, 1, 0, 1);

        // Resolved for the log, and because a rule wanting to act on either
        // player — as the round-end below does — has only a userid otherwise.
        _log.Msg($"[SourceSharp/Score] {Describe(death.UserId)} killed by " +
                   $"{(death.IsCredited ? Describe(death.AttackerUserId) : "the world")} " +
                   $"with '{death.Weapon}'{(death.Headshot ? " (headshot)" : "")} — " +
                   $"now {_scoreboard.ScoreFor(death.UserId)} point(s), " +
                   $"{_scoreboard.DeathsFor(death.UserId)} death(s)\n");
    }

    /// <summary>
    /// A player under both of the engine's names for them.
    ///
    /// Events carry the userid and entity calls take the handle, and until
    /// the engine's player area existed nothing could put the two together — so a
    /// rule reading a death could name neither player it described.
    /// </summary>
    private string Describe(int userId)
    {
        var player = _players.FindByUserId(userId);

        return player == EntityHandle.None ? $"userid={userId}" : $"userid={userId} ({player})";
    }

    /// <summary>
    /// Checks the frag limit. Runs every server frame, so it does as little as
    /// possible: an early return when the limit is off or the round is already
    /// over, and no allocation on the common path.
    /// </summary>
    public void Think()
    {
        // CHL2MPRules::Think's game-over arm: wait out the intermission, then
        // change the level, and nothing else.
        if (_intermission is { GameOver: true } over)
        {
            over.Think(_host.CurrentTime);
            return;
        }

        if (_roundOver)
            return;

        // GetMapRemainingTime() < 0: mp_timelimit minutes since the level began.
        float minutes = _mpTimeLimit?.Value ?? 0f;
        if (minutes > 0f && _host.CurrentTime >= _levelStartedAt + minutes * 60f)
        {
            _roundOver = true;
            _log.Msg($"[SourceSharp/Rules] time limit reached ({minutes} min) — ending round\n");
            EndRound();
            return;
        }

        // This mod's cs_sv_fraglimit, else stock's mp_fraglimit.
        int limit = _fragLimit?.Value ?? 0;
        if (limit <= 0)
            limit = _mpFragLimit?.Value ?? 0;
        if (limit <= 0)
            return;

        // Score rather than kills: it is the total the engine keeps on the
        // player and networks, so the round ends on the number the players are
        // looking at rather than on a private one.
        var (userId, score) = _scoreboard.Best();

        if (userId == 0 || score < limit)
            return;

        // Latched before ending, not after: EndRound goes through the
        // gamerules and can call back into this Think on the same frame, and
        // ending the round twice would skip the map change.
        _roundOver = true;

        _log.Msg($"[SourceSharp/Rules] frag limit reached: {Describe(userId)} " +
                   $"on {score} of {limit} — ending round\n");

        EndRound();
    }

    /// <summary>
    /// EndMultiplayerGame: the intermission (stock's GoToIntermission), or,
    /// for rules built without one, the native round end.
    /// </summary>
    public void EndRound()
    {
        _roundOver = true;

        if (_intermission is not null)
            _intermission.Begin(_host.CurrentTime);
        else
            _host.EndRound();
    }

    /// <summary>
    /// Clears per-round state. Called at level init, since the same GameRules
    /// instance outlives a map change and a latched _roundOver would otherwise
    /// stop the next round ever ending.
    /// </summary>
    public void ResetRound()
    {
        _roundOver = false;
        _spawnedAt.Clear();
        _scoreboard.Reset();
    }

    /// <summary>Test seam: whether the round has been ended.</summary>
    public bool RoundOver => _roundOver;
}

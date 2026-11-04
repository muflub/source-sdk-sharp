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
public sealed class EndRoundCommand
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

    public void Execute()
    {
        _log.Msg("[SourceSharp/Rules] ending round on request\n");
        _intermission?.Begin(_host.CurrentTime);
    }
}

/// <summary>
/// The rules themselves.
///
/// Kept out of ServerModule because these are policy, not lifecycle — and
/// because keeping them together makes it obvious which decisions the mod has
/// actually taken over and which still fall through to Source.
/// </summary>
public sealed class GameRules : IGameRules, IGameLifecycle, IFrameLifecycle, IRoundFlow
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

    private readonly RespawnDelayConVar _respawnDelay;
    // Declared in ServerModule alongside the relationship rule, and reused here:
    // "are they teammates" and "may teammates hurt each other" are two halves of
    // one setting, so they must not drift apart into two ConVars.
    private readonly FriendlyFireConVar _friendlyFire;
    private readonly SpawnProtectConVar _spawnProtect;
    private readonly FragLimitConVar _fragLimit;

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
    private readonly MpFragLimitConVar _mpFragLimit;
    private readonly MpTimeLimitConVar _mpTimeLimit;
    private readonly MpRestartGameConVar _mpRestartGame;
    private float _levelStartedAt;
    private readonly GameModes? _modes;
    private readonly IHooks<IDamageRule>? _damageRules;
    private readonly IHooks<IDamageModifier>? _damageModifiers;
    private readonly DamageHitGroups? _hits;
    private readonly IHooks<ILoadoutProvider>? _loadoutProviders;
    private readonly IHooks<IPlayerSpawnHook>? _spawnHooks;
    private readonly IHooks<IKitGate>? _kitGates;

    /// <summary>What a spawning player ends up holding; null leaves the grant's own choice.</summary>
    private readonly ISpawnWeapon? _spawnWeapon;
    // The restart (stock's m_flRestartGameTime / m_tmNextPeriodicThink). When
    // the armed restart fires; zero is none. The periodic clock is stock's
    // once-a-second pass over mp_restartgame.
    private float _restartGameTime;
    private float _nextPeriodicThink;
    private readonly IPlayerDirectory? _roster;
    private readonly IUserMessageSender? _messages;
    private readonly IEngineGameEvents? _events;

    /// <summary>
    /// The mode the proxy is answering for, or null — before any proxy,
    /// before the first pick, and on the unknown-id arm. Null is not an
    /// error to handle here: every question this class asks a mode then
    /// answers with its own machine, which is the game exactly as it was
    /// before modes existed (and what every no-owner fact measures).
    /// </summary>
    private IGameMode? Mode => _modes?.Active;

    public GameRules(ILog log, GameRulesConVars convars, IEngineHost host, IEnginePlayers players,
                     IEngineEntities entities, IScoreboard scoreboard,
                     IGameEvent<PlayerDeathArgs> deaths, Intermission? intermission = null,
                     GameModes? modes = null,
                     IHooks<IDamageRule>? damageRules = null,
                     IHooks<ILoadoutProvider>? loadout = null,
                     ISpawnWeapon? spawnWeapon = null,
                     DamageHitGroups? hits = null,
                     IHooks<IDamageModifier>? damageModifiers = null,
                     IHooks<IPlayerSpawnHook>? spawnHooks = null,
                     IHooks<IKitGate>? kitGates = null,
                     IPlayerDirectory? roster = null, IUserMessageSender? messages = null,
                     IEngineGameEvents? events = null)
    {
        _spawnWeapon = spawnWeapon;
        // The mods' arms around the kit (tf2 stage 2.3). Null is the game
        // before them: the kit alone.
        _spawnHooks = spawnHooks;
        // The mods' veto on that kit (tf2 spawnfix). Null is the game before
        // it: the kit always goes out.
        _kitGates = kitGates;
        // The open shot's crit flag (DamageHitGroups), read into the chain's
        // DamageContext. Null is a game with no weapon arms: nothing is critical.
        _hits = hits;
        // The mode proxy (tf2 stage 0.7). OPTIONAL and null means the game
        // before it: every question below answers with this class's own
        // machine, which is what every no-owner fact measures. The module
        // wires the real one; a test that builds the rules without it is not
        // testing a lesser game, it is testing the no-owner arm.
        _modes = modes;
        _damageRules = damageRules;
        _damageModifiers = damageModifiers;
        _loadoutProviders = loadout;
        _intermission = intermission;
        _mpFragLimit = convars.MpFragLimit;
        _mpTimeLimit = convars.MpTimeLimit;
        _mpRestartGame = convars.MpRestartGame;

        _log = log;
        _host = host;
        _players = players;
        _entities = entities;
        _scoreboard = scoreboard;
        _roster = roster;
        _messages = messages;
        _events = events;

        _respawnDelay = convars.RespawnDelay;
        _friendlyFire = convars.FriendlyFire;
        _spawnProtect = convars.SpawnProtect;
        _fragLimit    = convars.FragLimit;

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
    /// <para>
    /// Stock's kit (CHL2MP_Player::GiveDefaultItems): the melee weapon, the
    /// pistol, the SMG1, a frag and the gravity gun. Stock's melee follows the
    /// player model -- a stunstick for a combine or police model, a crowbar for
    /// a citizen -- and every player here wears SsPlayer's citizen
    /// (models/humans/...), so the crowbar.
    /// </para>
    /// </summary>
    public static readonly ImmutableArray<string> Loadout =
    [
        "weapon_crowbar",
        "weapon_pistol",
        "weapon_smg1",
        "weapon_frag",
        "weapon_physcannon",
    ];

    /// <summary>
    /// Ammo handed out alongside the loadout: GiveDefaultItems' GiveAmmo calls,
    /// each held to its type's carry limit (the pistol's 255 is 150).
    /// </summary>
    public static readonly ImmutableArray<(string Ammo, int Count)> StartingAmmo =
    [
        ("Pistol",   255),
        ("SMG1",      45),
        ("Grenade",    1),
        ("Buckshot",   6),
        ("357",        6),
    ];

    /// <summary>
    /// The kit one spawn hands out: each column is a provider's when the
    /// last provider that named anything on it named it, the core's static
    /// array otherwise (the rules of <see cref="ILoadoutProvider"/>).
    /// Separate from the grant so a fact can see WHICH kit was chosen, not
    /// just count engine calls.
    /// </summary>
    private (IReadOnlyList<string> Weapons, IReadOnlyList<(string, int)> Ammo) Kit(EntityHandle player)
    {
        IReadOnlyList<string> weapons = Loadout;
        IReadOnlyList<(string, int)> ammo = StartingAmmo;

        if (_loadoutProviders is { } providers)
        {
            // One pass, keeping the LAST provider that answered each column.
            // A provider is asked for both columns inside one try: a provider
            // that throws after answering one has not finished answering, so
            // neither pointer moves and the kit before it stands (the throw
            // is named on the log — the rule IDamageRule's dispatch applies
            // to its thrower, for the same reason).
            List<string>? bestWeapons = null;
            List<(string, int)>? bestAmmo = null;
            foreach (var provider in providers.All)
            {
                try
                {
                    List<string> w = [.. provider.Weapons(player)];
                    List<(string, int)> a = [.. provider.Ammo(player)];
                    if (w.Count > 0) bestWeapons = w;
                    if (a.Count > 0) bestAmmo = a;
                }
                catch (Exception e)
                {
                    _log.Warning($"[SourceSharp/Rules] loadout provider {provider.GetType().FullName} threw: {e.Message}\n");
                }
            }

            // De-duped by name, last entry for a name winning — the plan's
            // "last answer wins per slot" over one provider's list.
            if (bestWeapons is not null)
                weapons = Dedupe(bestWeapons, w => w);
            if (bestAmmo is not null)
                ammo = Dedupe(bestAmmo, a => a.Item1);
        }

        return (weapons, ammo);
    }

    /// <summary>
    /// Keeps one entry per key — the LAST one with that key — preserving the
    /// order of first mention, so a kit reads the same every spawn.
    /// </summary>
    private static List<T> Dedupe<T>(List<T> entries, Func<T, string> key)
    {
        var last = new Dictionary<string, T>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (T e in entries)
        {
            string k = key(e);
            if (!last.ContainsKey(k))
                order.Add(k);
            last[k] = e;
        }
        return [.. order.Select(k => last[k])];
    }


    //-------------------------------------------------------------------------

    /// <summary>
    /// Whether an attack lands. Answers two rules of its own; anything it
    /// defers goes to the mods' <see cref="IDamageRule"/> chain before the
    /// stock rule gets it. <paramref name="hitGroup"/> rides into the chain's
    /// <see cref="DamageContext"/> (tf2 stage 0.8); no core rule reads it.
    /// </summary>
    public Decision CanTakeDamage(EntityHandle player, EntityHandle attacker,
                                  float damage, int damageType, float now,
                                  HitGroup hitGroup = HitGroup.Generic)
    {
        // Spawn protection. Checked before friendly fire because it should hold
        // regardless of who is shooting — including the world.
        float protect = _spawnProtect.Value;
        if (protect > 0f && _spawnedAt.TryGetValue(player.Value, out float spawnedAt))
        {
            if (now - spawnedAt < protect)
                return Decision.No;
        }

        // Self-damage is never friendly fire — falling, and your own grenade,
        // should still hurt whatever the team rules say.
        if (attacker.Value == player.Value)
            return AskDamageRules(player, attacker, damage, damageType, hitGroup);

        // Friendly fire. Only answered when it is OFF and the two are actually
        // teammates; anything else defers, so this rule can never make damage
        // land that the stock rules would have blocked.
        if (!_friendlyFire.Value && _entities.IsValid(attacker)
            && Teammates(_players.GetTeam(player), _players.GetTeam(attacker)))
            return Decision.No;

        return AskDamageRules(player, attacker, damage, damageType, hitGroup);
    }

    /// <summary>
    /// stock's <c>FIRST_GAME_TEAM</c> (shareddefs.h: <c>LAST_SHARED_TEAM + 1</c>,
    /// after <c>TEAM_UNASSIGNED</c> 0 and <c>TEAM_SPECTATOR</c> 1).
    /// </summary>
    public const int FirstGameTeam = 2;

    /// <summary>
    /// Whether two team numbers make the players on them teammates, the
    /// relationship <c>CTeamplayRules::FPlayerCanTakeDamage</c> refuses friendly
    /// fire on (<c>PlayerRelationship == GR_TEAMMATE</c>).
    ///
    /// <para>
    /// <b>ONLY A GAME TEAM.</b> Unassigned (0) and Spectator (1) are the shared
    /// teams, not sides: stock's HL2MP deathmatch answers <c>GR_NOTTEAMMATE</c>
    /// for everyone (<c>CHL2MPRules::PlayerRelationship</c> without teamplay),
    /// and this build's deathmatch players are all on Unassigned. Counting
    /// team 0 as a side made any two of them teammates, so turning friendly
    /// fire off would refuse every hit in the game. -1 (not a player: a
    /// trigger, the world) is never a teammate.
    /// </para>
    /// </summary>
    public static bool Teammates(int team, int otherTeam)
        => team >= FirstGameTeam && team == otherTeam;

    /// <summary>
    /// The mods' half of the damage question, reached ONLY where this class
    /// deferred (the two directions are argued on <see cref="IDamageRule"/>):
    /// first actual answer wins, a rule that throws is named and treated as a
    /// declination — a broken immunity zone must not kill the player it was
    /// guarding, and must not let the rest of the chain off the hook.
    /// </summary>
    private Decision AskDamageRules(EntityHandle victim, EntityHandle attacker, float damage, int damageType,
                                    HitGroup hitGroup)
    {
        if (_damageRules is not { } rules)
            return Decision.UseStockRule;

        var hit = new DamageContext(victim, attacker, damage, damageType, hitGroup,
                                    Critical: _hits?.Critical ?? false);

        foreach (var rule in rules.All)
        {
            try
            {
                if (rule.Decide(in hit) is { } d && d != Decision.UseStockRule)
                    return d;
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp/Rules] damage rule {rule.GetType().FullName} threw: {e.Message}\n");
            }
        }

        return Decision.UseStockRule;
    }

    /// <summary>
    /// The mods' <see cref="IDamageModifier"/> fold over an allowed hit (tf2
    /// stage 2.7): the running damage through each in order, a throwing
    /// modifier named and skipped. No modifiers is the hit as dealt.
    /// </summary>
    public float ModifyDamage(EntityHandle player, EntityHandle attacker, float damage, int damageType,
                              HitGroup hitGroup = HitGroup.Generic)
    {
        if (_damageModifiers is not { } modifiers)
            return damage;

        var hit = new DamageContext(player, attacker, damage, damageType, hitGroup,
                                    Critical: _hits?.Critical ?? false);

        float running = damage;
        foreach (var modifier in modifiers.All)
        {
            try
            {
                running = modifier.Modify(in hit, running);
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp/Rules] damage modifier {modifier.GetType().FullName} threw: {e.Message}\n");
            }
        }

        return running;
    }

    /// <summary>Whether a dead player may come back. Deferred for now.</summary>
    public Decision CanRespawn(EntityHandle player) => Decision.UseStockRule;

    /// <summary>Seconds until respawn, or negative for the stock rule.</summary>
    public float GetRespawnDelay(EntityHandle player) => _respawnDelay.Value;

    /// <summary>
    /// Hands out the loadout — the mods' when a provider owns a column, the
    /// core's HL2 kit otherwise — and starts the spawn-protection clock.
    /// </summary>
    public void OnPlayerSpawned(EntityHandle player, float now)
    {
        _spawnedAt[player.Value] = now;

        // Before the kit: a mod that sets the player's class here is asked
        // about it by its own loadout provider below — and by the gates under
        // it, which judge the class the hooks just took.
        EachSpawnHook(nameof(IPlayerSpawnHook.OnSpawning), h => h.OnSpawning(player, now));

        // The kit gates' veto (tf2 spawnfix): a class game's unassigned player
        // watches with nothing, as stock's does — GiveDefaultItems is simply
        // not called, so the providers are not asked and the default-weapon
        // choice is not made either. The hooks around it run either way.
        bool giveKit = GateAllowsKit(player);
        IReadOnlyList<string> weapons = [];
        int given = 0;
        int ammoGiven = 0;

        if (giveKit)
        {
            var (w, ammo) = Kit(player);
            weapons = w;

            foreach (string weapon in weapons)
            {
                if (_players.GiveItem(player, weapon))
                    given++;
            }

            // Ammo before weapons would be equally valid; after is chosen so a
            // weapon that sets its own default clip does not overwrite the grant.
            foreach (var (name, count) in ammo)
                ammoGiven += _players.GiveAmmo(player, count, name);

            // GiveDefaultItems' last word: the weapon the player asked to spawn with.
            _spawnWeapon?.Choose(player);
        }

        // After the kit: the health, the model, whatever the weapons decide.
        EachSpawnHook(nameof(IPlayerSpawnHook.OnSpawned), h => h.OnSpawned(player, now));

        // The mode's spawn arm, after the core's own bookkeeping: today's
        // deathmatch adds nothing here (NullMode's machine IS this class and
        // its OnPlayerSpawned is the no-op this call becomes), and a mode
        // that readies a player per class owns it. A mode that throws is
        // named and the spawn stands — the player has their weapons and the
        // broken mod's half is in the log.
        if (Mode is { } mode && !ReferenceEquals(mode.Rounds, this))
        {
            try
            {
                mode.Rounds.OnPlayerSpawned(player, now);
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp/Rules] mode '{mode.Id}' spawn arm threw: {e}\n");
            }
        }

        if (giveKit)
            _log.Msg($"[SourceSharp/Rules] {player} spawned: gave {given}/{weapons.Count} item(s), " +
                       $"{ammoGiven} ammo" +
                       $"{(_spawnProtect.Value > 0f ? $", protected for {_spawnProtect.Value:F1}s" : "")}\n");
        else
            _log.Msg($"[SourceSharp/Rules] {player} spawned: no kit, a gate refused it\n");
    }

    /// <summary>
    /// The kit gates' answer: any gate's <c>false</c> refuses the grant, and a
    /// gate that throws is named and does not veto — a broken mod leaves the
    /// kit standing, as it leaves the spawn (<see cref="IKitGate"/>).
    /// </summary>
    private bool GateAllowsKit(EntityHandle player)
    {
        if (_kitGates is null)
            return true;

        foreach (var gate in _kitGates.All)
        {
            try
            {
                if (!gate.ShouldGiveKit(player))
                    return false;
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp/Rules] kit gate {gate.GetType().FullName}.ShouldGiveKit threw: {e}\n");
            }
        }

        return true;
    }

    /// <summary>
    /// One spawn arm of every hook. A hook that throws is named and the next
    /// one is still asked: the spawn the core made stands either way, as a
    /// thrown mode spawn arm leaves it.
    /// </summary>
    private void EachSpawnHook(string what, Action<IPlayerSpawnHook> call)
    {
        if (_spawnHooks is null)
            return;

        foreach (var hook in _spawnHooks.All)
        {
            try
            {
                call(hook);
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp/Rules] spawn hook {hook.GetType().FullName}.{what} threw: {e}\n");
            }
        }
    }

    /// <summary>Forgets a player's spawn time. Called when they leave.</summary>
    public void Forget(EntityHandle player) => _spawnedAt.Remove(player.Value);

    /// <summary>Test seam: whether a player is currently spawn-protected.</summary>
    public bool IsProtected(EntityHandle player, float now)
    {
        float protect = _spawnProtect.Value;
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
        // "The round" is whichever machine owns it: the core's own flag, or
        // the active mode's RoundOver (IRoundFlow: a round "ends once, and
        // scores nothing more") — nothing below, the core's board included,
        // runs for a round its owner has ended.
        if (_roundOver || ModeRoundOver())
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

        // THE MODE'S DEATH ARMS, after the core's own writes (plan 0.7): the
        // round machine's view of the kill, and the extra score writes the
        // mode keeps beside the scoreboard. Both after, so a mode reads the
        // board the same way a frag-limit check does. A mode that throws is
        // named and the core's score stands: a broken team-score line must
        // not uncredit a kill the engine already counted.
        if (Mode is { } mode)
        {
            if (!ReferenceEquals(mode.Rounds, this))
            {
                try
                {
                    mode.Rounds.OnPlayerKilled(death);
                }
                catch (Exception e)
                {
                    _log.Warning($"[SourceSharp/Rules] mode '{mode.Id}' death arm threw: {e}\n");
                }
            }

            if (mode.Scoring is { } scoring)
            {
                try
                {
                    scoring.OnDeath(death);
                }
                catch (Exception e)
                {
                    _log.Warning($"[SourceSharp/Rules] mode '{mode.Id}' scoring threw: {e}\n");
                }
            }
        }
    }

    /// <summary>
    /// Whether the active mode's own machine has ended its round. False when
    /// no mode owns the round (NullMode's machine is this class, whose flag
    /// the caller already read). A machine that throws is named and treated
    /// as running: a broken mod must not stop the core's scoreboard.
    /// </summary>
    private bool ModeRoundOver()
    {
        if (Mode is not { } mode || ReferenceEquals(mode.Rounds, this))
            return false;

        try
        {
            return mode.Rounds.RoundOver;
        }
        catch (Exception e)
        {
            _log.Warning($"[SourceSharp/Rules] mode '{mode.Id}' RoundOver threw: {e}\n");
            return false;
        }
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
    /// The frame's win check: the core's intermission arm, the mode proxy's
    /// gates, then the frag/time-limit machine — this class's own, or the
    /// active mode's. Runs every server frame, so it does as little as
    /// possible: an early return when the limit is off or the round is
    /// already over, and no allocation on the common path.
    /// </summary>
    public void Think()
    {
        // CHL2MPRules::Think's game-over arm: wait out the intermission, then
        // change the level, and nothing else. STAYS CORE whatever the mode
        // (plan 0.7): the intermission is the core's own screen, and a mode
        // that never ends its round must not strand the level-change clock.
        if (_intermission is { GameOver: true } over)
        {
            over.Think(_host.CurrentTime);
            return;
        }

        // THE MODE PROXY (tf2 stage 0.7). Two gates before the machine:
        //
        // A switch's warmup — the round has not started, and the win check of
        // a round that has not started must not run: a frag traded while the
        // players wait out the warmup would otherwise end it.
        if (_modes is { IsWarmingUp: true })
            return;

        // The active mode's machine, when one owns the round. No proxy, no
        // picked mode, or NullMode (whose Rounds IS this class) is the arm
        // below — today's machine, unchanged, which is what every no-owner
        // fact measures. A machine that throws is named and skipped: a broken
        // mod's Think does not make the core's own checks run as well, which
        // would be two answers to one question.
        if (Mode is { } mode && !ReferenceEquals(mode.Rounds, this))
        {
            try
            {
                mode.Rounds.Think(_host.CurrentTime);
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp/Rules] mode '{mode.Id}' round think threw: {e}\n");
            }
            return;
        }

        ThinkStock();

        // Stock's Think returns the frame the round ended (its GoToIntermission
        // arms return), and its restart walk sits behind that return: a round
        // being ended is not the frame a restart arms or fires on.
        if (_roundOver)
            return;

        RestartThink();
    }
    // IRoundFlow, and the one member that needs spelling out. Asked AS the
    // machine — which is what NullMode.Rounds being this object means — the
    // delegation above has already happened, so the frame pass goes straight
    // to the stock check: asking Think again would ask the mode, and the mode
    // is the one asking. ResetRound and RoundOver are the members below,
    // matched by shape; the two arms today's deathmatch does not use are the
    // explicit no-ops beside them.
    void IRoundFlow.Think(float now) => ThinkStock();

    void IRoundFlow.OnPlayerSpawned(EntityHandle player, float now) { }

    void IRoundFlow.OnPlayerKilled(PlayerDeathArgs death) { }

    /// <summary>
    /// The core's own win check — the machine behind
    /// <see cref="IRoundFlow.Think"/> when the active mode is
    /// <see cref="NullMode"/>, and what a mode that reuses the deathmatch
    /// checks calls from its own Think. Public for that second caller: the
    /// delegation in <see cref="Think"/> is one-way, and a wrapper that came
    /// back through it would ask itself the question it was asked.
    /// </summary>
    public void ThinkStock()
    {

        if (_roundOver)
            return;

        // GetMapRemainingTime() < 0: mp_timelimit minutes since the level began.
        float minutes = _mpTimeLimit.Value;
        if (minutes > 0f && _host.CurrentTime >= _levelStartedAt + minutes * 60f)
        {
            _roundOver = true;
            _log.Msg($"[SourceSharp/Rules] time limit reached ({minutes} min) — ending round\n");
            EndRound();
            return;
        }

        // This mod's cs_sv_fraglimit, else stock's mp_fraglimit.
        int limit = _fragLimit.Value;
        if (limit <= 0)
            limit = _mpFragLimit.Value;
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

    //-------------------------------------------------------------------------
    // The restart (mp_restartgame). Stock's CheckRestartGame and RestartGame,
    // reached only on the core's own arm: the mode dispatch in Think returns
    // before here when a mode owns the round, and a mode reads
    // mp_restartgame through its own machine (TF2's does).
    //-------------------------------------------------------------------------

    /// <summary>
    /// Stock's CHL2MPRules::Think frame pass over the restart (the periodic
    /// 1 s think and the fire check, hl2mp_gamerules.cpp:350-360): the convar
    /// is read a pass at a time, not per frame; an armed restart fires the
    /// frame its time arrives.
    ///
    /// <para>
    /// Stock reads curtime for both halves of the pass, every frame. Here the
    /// clock is read only when there is something to consult — the convar
    /// set, or a restart armed — so a game with neither pays no engine read
    /// per frame (the convar's value is cached; the crossing is the costly
    /// read).
    /// </para>
    /// </summary>
    private void RestartThink()
    {
        bool armed = _restartGameTime > 0f;
        if (!armed && _mpRestartGame.Value <= 0)
            return;

        float now = _host.CurrentTime;

        if (!armed && now > _nextPeriodicThink)
        {
            CheckRestartGame(now);
            _nextPeriodicThink = now + 1f;
        }

        if (_restartGameTime > 0f && _restartGameTime <= now)
            RestartGame();
    }

    /// <summary>
    /// Stock's CheckRestartGame (hl2mp_gamerules.cpp:1179): mp_restartgame N
    /// arms a restart N seconds out — capped at stock's 60 — tells the
    /// players, and puts the convar back to 0 so the next pass does not
    /// re-arm. Stock's mp_readyrestart half is not ported (the divergence
    /// row). Stock also sets m_bCompleteReset here; it is written only and
    /// never read in stock HL2MP, so it is not carried.
    /// </summary>
    private void CheckRestartGame(float now)
    {
        MpRestartGameConVar restart = _mpRestartGame;
        int delay = restart.Value;

        if (delay <= 0)
            return;

        if (delay > 60)
            delay = 60;

        AnnounceRestart(delay);

        _restartGameTime = now + delay;
        restart.SetValue(0);
    }

    /// <summary>
    /// Stock's two UTIL_ClientPrintAll lines: one to the centre print, one to
    /// the console, the count and its unit into the text's %s1/%s2.
    /// </summary>
    private void AnnounceRestart(int seconds)
    {
        if (_messages is null)
            return;

        // Stock's HUD_PRINTCENTER / HUD_PRINTCONSOLE (shareddefs.h:127-130).
        const byte printCenter = 4;
        const byte printConsole = 2;
        const string text = "Game will restart in %s1 %s2";

        string count = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string unit = seconds == 1 ? "SECOND" : "SECONDS";

        _messages.Send(new TextMsg { Destination = printCenter, Text = text, Params = [count, unit] },
                       player: 0, reliable: true);
        _messages.Send(new TextMsg { Destination = printConsole, Text = text, Params = [count, unit] },
                       player: 0, reliable: true);
    }

    /// <summary>
    /// Stock's CHL2MPRules::RestartGame (hl2mp_gamerules.cpp:1000): the map
    /// comes back as it loaded, every player is holstered, stripped and
    /// respawned, the counts go back to zero, and round_start fires.
    /// </summary>
    public void RestartGame()
    {
        float now = _host.CurrentTime;

        // Stock bounds-checks mp_timelimit before anything else.
        if (_mpTimeLimit.Value < 0f)
            _mpTimeLimit.SetValue(0f);

        // m_flGameStartTime: the round's clock restarts with the restart.
        _levelStartedAt = now;

        // The map: the mode proxy's CleanUpMap walk with the null mode, which
        // is the core's own HL2MPMapCleanupFilter — exactly what GameModes
        // cleans with. Without a proxy there is nothing to ask, and said, not
        // silent.
        if (_modes is { } modes)
        {
            modes.CleanUpMap(null);
        }
        else
        {
            _log.Warning("[SourceSharp/Rules] restart with no mode proxy: the map is not reset\n");
        }

        // Now respawn all the players, stock's order per player (its
        // CBasePlayer walk): the weapon in their hands away, everything else
        // stripped, and back into the world.
        if (_roster is { } roster)
        {
            foreach (var (player, _) in roster.All)
            {
                _players.HolsterActiveWeapon(player);
                _players.RemoveAllItems(player, true);
                _players.Respawn(player);
            }
        }
        else
        {
            _log.Warning("[SourceSharp/Rules] restart with no player directory: nobody is respawned\n");
        }

        // The counts. Stock zeroes each player's frags and deaths
        // (CHL2MP_Player::Reset) and both teams' CTeam scores; the core's
        // tally is the scoreboard alone (NullMode has no IScoring, so the
        // teams' totals are the players'), and ResetRound is what zeroes it —
        // clearing the round flag and the spawn-protection stamps with it,
        // which is the fresh round the respawn begins.
        ResetRound();

        _restartGameTime = 0f;

        // Stock also clears m_flIntermissionEndTime here. There is nothing to
        // clear: this only runs from Think's walk, which returns before it
        // once the round is over, so no intermission can be running here.

        FireRoundStart();
    }

    /// <summary>Stock's round_start: fraglimit 0, HLTV priority 6, objective DEATHMATCH.</summary>
    private void FireRoundStart()
    {
        if (_events is null)
            return;

        IntPtr e = _events.EventCreate("round_start");
        if (e == IntPtr.Zero)
            return;

        _events.EventSetInt(e, "fraglimit", 0);
        _events.EventSetInt(e, "priority", 6); // stock's HLTV event priority, not transmitted
        _events.EventSetString(e, "objective", "DEATHMATCH");
        _events.EventFire(e);
    }

    /// <summary>
    /// EndMultiplayerGame: the intermission (stock's GoToIntermission). Rules
    /// built without one only mark the round over: the native rules'
    /// EndMultiplayerGame they used to be handed was empty, and its shim slot
    /// retired at plan_server_native_v2 T4e.
    /// </summary>
    public void EndRound()
    {
        _roundOver = true;
        _intermission?.Begin(_host.CurrentTime);
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

    /// <summary>Test seam: whether an mp_restartgame restart is armed.</summary>
    public bool RestartPending => _restartGameTime > 0f;
}

/// <summary>The convars the rules read, built by the container.</summary>
public sealed class GameRulesConVars(RespawnDelayConVar respawnDelay, FriendlyFireConVar friendlyFire,
                                     SpawnProtectConVar spawnProtect, FragLimitConVar fragLimit,
                                     MpFragLimitConVar mpFragLimit, MpTimeLimitConVar mpTimeLimit,
                                     MpRestartGameConVar mpRestartGame)
{
    public readonly RespawnDelayConVar RespawnDelay = respawnDelay;
    public readonly FriendlyFireConVar FriendlyFire = friendlyFire;
    public readonly SpawnProtectConVar SpawnProtect = spawnProtect;
    public readonly FragLimitConVar FragLimit = fragLimit;
    public readonly MpFragLimitConVar MpFragLimit = mpFragLimit;
    public readonly MpTimeLimitConVar MpTimeLimit = mpTimeLimit;
    public readonly MpRestartGameConVar MpRestartGame = mpRestartGame;
}

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Buildings;
using TF2.Server.Entities;
using TF2.Server.Entities.Items;
using TF2.Server.Entities.Rooms;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Rules;

namespace TF2.Server.Rules;

// The round machine's seams over the core's services: the only code in
// Rules/ that names a core concrete type. Each is thin, and each has its
// own facts (TeamRoundAdapterTests).

/// <summary>
/// The round convars, read live. Each declaration is found once, on first
/// read (after every assembly's convars have registered); a missing one is
/// a startup bug and throws naming it.
/// </summary>
[SideService(Side.Server, As = [typeof(ITeamRoundSettings)])]
public sealed class ConVarTeamRoundSettings(ConVarRegistry convars) : ITeamRoundSettings
{
    private readonly Dictionary<Type, ConVarBase> _found = new();

    private T Get<T>() where T : ConVarBase
    {
        if (_found.TryGetValue(typeof(T), out var known))
            return (T)known;

        var found = convars.Get<T>()
            ?? throw new InvalidOperationException($"the round rules read {typeof(T).Name}, which is not registered");
        _found[typeof(T)] = found;
        return found;
    }

    public float RespawnWaveTime => Get<MpRespawnWaveTimeConVar>().Value;
    public bool DisableRespawnTimes => Get<MpDisableRespawnTimesConVar>().Value;
    public int BonusRoundTime => Get<MpBonusRoundTimeConVar>().Value;
    public float EnableRoundWaitTime => Get<MpEnableRoundWaitTimeConVar>().Value;
    public bool StalemateEnable => Get<MpStalemateEnableConVar>().Value;
    public int StalemateTimeLimit => Get<MpStalemateTimeLimitConVar>().Value;
    public float WaitingForPlayersTime => Get<MpWaitingForPlayersTimeConVar>().Value;

    public bool WaitingForPlayersCancel
    {
        get => Get<MpWaitingForPlayersCancelConVar>().Value;
        set => Get<MpWaitingForPlayersCancelConVar>().SetValue(value);
    }

    public bool WaitingForPlayersRestart
    {
        get => Get<MpWaitingForPlayersRestartConVar>().Value;
        set => Get<MpWaitingForPlayersRestartConVar>().SetValue(value);
    }

    public int TimeLimitMinutes
    {
        get => (int)Get<MpTimeLimitConVar>().Value;
        set => Get<MpTimeLimitConVar>().SetValue(value);
    }

    /// <summary>Stock's MPTimeLimitCallback, through the core convar's subscription.</summary>
    public IDisposable OnTimeLimitChanged(Action<int, int> handler)
        => Get<MpTimeLimitConVar>().Subscribe((oldValue, newValue) => handler((int)oldValue, (int)newValue));

    /// <summary>Stock's WinlimitChanged, through the core convar's subscription.</summary>
    public IDisposable OnWinLimitChanged(Action<int, int> handler)
        => Get<MpWinLimitConVar>().Subscribe((oldValue, newValue) => handler(oldValue, newValue));
    public int WinLimit => Get<MpWinLimitConVar>().Value;
    public int WinDifference => Get<MpWinDifferenceConVar>().Value;
    public int WinDifferenceMin => Get<MpWinDifferenceMinConVar>().Value;
    public int MaxRounds => Get<MpMaxRoundsConVar>().Value;
    public bool ScrambleAuto => Get<MpScrambleTeamsAutoConVar>().Value;
    public int ScrambleAutoWinDifference => Get<MpScrambleTeamsAutoWinDifferenceConVar>().Value;
    public string NextLevel => Get<NextLevelConVar>().Value ?? string.Empty;

    public int RestartRound
    {
        get => Get<MpRestartRoundConVar>().Value;
        set => Get<MpRestartRoundConVar>().SetValue(value);
    }

    public int RestartGame
    {
        get => Get<MpRestartGameConVar>().Value;
        set => Get<MpRestartGameConVar>().SetValue(value);
    }

    public bool RestartGameImmediate
    {
        get => Get<MpRestartGameImmediateConVar>().Value;
        set => Get<MpRestartGameImmediateConVar>().SetValue(value);
    }

    public bool MatchEndAtTimeLimit => Get<MpMatchEndAtTimeLimitConVar>().Value;
    public int TeamsUnbalanceLimit => Get<MpTeamsUnbalanceLimitConVar>().Value;

    public bool SpectatorsRestricted => Get<MpSpectatorsRestrictedConVar>().Value;
    public int AutoTeamBalance => Get<MpAutoTeamBalanceConVar>().Value;
    public bool ForceAutoTeam => Get<MpForceAutoTeamConVar>().Value;
    public bool Tournament => Get<MpTournamentConVar>().Value;

    public bool ReadyRestart
    {
        get => Get<MpClanReadyRestartConVar>().Value;
        set => Get<MpClanReadyRestartConVar>().SetValue(value);
    }

    public string ReadySignal => Get<MpClanReadySignalConVar>().Value ?? string.Empty;
}

/// <summary>
/// The players, over the core's team roster, death records, observers and
/// engine player area. <see cref="ForceRespawn"/> is the core death cycle's
/// own respawn arm (<c>Players.Think</c>: stop observing, respawn, clear the
/// record) taken without its button and its wait.
/// </summary>
[SideService(Side.Server, As = [typeof(ITeamRoundRoster)])]
public sealed class CoreTeamRoundRoster(ITeams teams, IEnginePlayers players, IEngineEntities entities,
                                        IPlayerDirectory records, IEntityDirectory directory, Observers observers,
                                        PlayerNames? names = null, ITeamRoundReadiness? readiness = null)
    : ITeamRoundRoster
{
    public IReadOnlyList<EntityHandle> Players(TFTeam team) => teams.Players((int)team);

    public TFTeam TeamOf(EntityHandle player)
    {
        int team = teams.Team(player);
        return team is >= 0 and < (int)TFTeam.Count ? (TFTeam)team : TFTeam.Unassigned;
    }

    public bool IsAlive(EntityHandle player) => players.IsAlive(player);

    public float DeathTime(EntityHandle player) => records.Knows(player) ? records[player].DiedAt : -1f;

    public bool IsReadyToPlay(EntityHandle player)
        => TeamOf(player) is TFTeam.Red or TFTeam.Blue && (readiness?.HasChosenClass(player) ?? true);

    public void ForceRespawn(EntityHandle player)
    {
        var watcher = directory.Get(player) as SsPlayer;
        if (watcher is not null)
            observers.Stop(watcher);

        players.Respawn(player);

        // The respawn's words land outside any command; the stamp lets the
        // client adopt the compare that straddles them (PlayerNetState.
        // SpawnEpoch).
        if (watcher is not null)
            watcher.State.SpawnEpoch++;

        var record = records[player];
        record.DiedAt = -1f;
        record.Watching = false;
    }

    public bool ChangeTeam(EntityHandle player, TFTeam team)
    {
        teams.Join(player, (int)team);
        return TeamOf(player) == team;
    }

    public string Name(EntityHandle player) => names?.NameOf(entities.GetIndex(player)) ?? string.Empty;

    public int EntIndex(EntityHandle player) => entities.GetIndex(player);
}

/// <summary>The announcements, as stock's game events and <c>TextMsg</c> prints to everyone.</summary>
[SideService(Side.Server, As = [typeof(IRoundAnnouncer)])]
public sealed class GameEventRoundAnnouncer(IEngineGameEvents events, IUserMessageSender messages) : IRoundAnnouncer
{
    public void RoundStart(bool fullReset)
        => Fire(TFMessages.EventRoundStart, e => events.EventSetBool(e, "full_reset", fullReset));

    public void RoundActive() => Fire(TFMessages.EventRoundActive, _ => { });

    public void RoundWin(TFTeam team, WinReason reason, bool fullRound, float roundTime, bool wasSuddenDeath)
        => Fire(TFMessages.EventRoundWin, e =>
        {
            events.EventSetInt(e, "team", (int)team);
            events.EventSetInt(e, "winreason", (int)reason);
            events.EventSetBool(e, "full_round", fullRound);
            // A float key: the area has no float setter, and the event's
            // KeyValues reads a float back from its text.
            events.EventSetString(e, "round_time", roundTime.ToString("R", CultureInfo.InvariantCulture));
            events.EventSetBool(e, "was_sudden_death", wasSuddenDeath);
        });

    public void RoundStalemate(StalemateReason reason)
        => Fire(TFMessages.EventRoundStalemate, e => events.EventSetInt(e, "reason", (int)reason));

    public void RestartRound() => Fire(TFMessages.EventRestartRound, _ => { });

    public void RestartSeconds(int seconds, string? format)
    {
        Fire(TFMessages.EventRoundRestartSeconds, e => events.EventSetInt(e, "seconds", seconds));

        if (format is not null)
        {
            string text = seconds.ToString(CultureInfo.InvariantCulture);
            PrintAll(TFMessages.HudPrintCenter, format, text);
            PrintAll(TFMessages.HudPrintConsole, format, text);
        }
    }

    public void GameOver(string reason)
        => Fire(TFMessages.EventGameOver, e => events.EventSetString(e, "reason", reason));

    public void BalanceWarning(int seconds)
        => PrintAll(TFMessages.HudPrintTalk, TFMessages.AutoTeamBalanceIn, seconds.ToString(CultureInfo.InvariantCulture));

    public void TeamBalanced(int entIndex, TFTeam team, string name)
    {
        Fire(TFMessages.EventTeamBalancedPlayer, e =>
        {
            events.EventSetInt(e, "player", entIndex);
            events.EventSetInt(e, "team", (int)team);
        });

        PrintAll(TFMessages.HudPrintTalk, TFMessages.PlayerWasTeamBalanced, name);
    }

    // ---- the songs: CTeamplayRoundBasedRules::BroadcastSound is the
    // teamplay_broadcast_audio event (tf_gamerules.cpp:22356-22370), team
    // TEAM_UNASSIGNED meaning everyone. The Halloween/MvM arms of the TF
    // overrides (tf_gamerules.cpp:4639-4684) are not ported, so the base
    // names stand.

    public void WinSong(TFTeam team)
    {
        if (team == TFTeam.Unassigned)
        {
            StalemateSong();
            return;
        }

        Fire(TFMessages.EventBroadcastAudio, e =>
        {
            events.EventSetInt(e, "team", (int)TFTeam.Unassigned);
            events.EventSetString(e, "sound", $"Game.TeamWin{(int)team}");
        });

        // The winner its win song, the loser stock's base lose song
        // (teamplayroundbased_gamerules.cpp:3324-3338).
        foreach (var team_ in new[] { TFTeam.Red, TFTeam.Blue })
        {
            string song = team_ == team ? "Game.YourTeamWon" : "Game.YourTeamLost";
            Fire(TFMessages.EventBroadcastAudio, e =>
            {
                events.EventSetInt(e, "team", (int)team_);
                events.EventSetString(e, "sound", song);
            });
        }
    }

    public void SuddenDeathSong() => SongFor("Game.SuddenDeath");

    public void Broadcast(TFTeam team, string sound)
        => Fire(TFMessages.EventBroadcastAudio, e =>
        {
            events.EventSetInt(e, "team", (int)team);
            events.EventSetString(e, "sound", sound);
        });

    private void StalemateSong() => SongFor("Game.Stalemate");

    private void SongFor(string song)
    {
        Fire(TFMessages.EventBroadcastAudio, e =>
        {
            events.EventSetInt(e, "team", (int)TFTeam.Unassigned);
            events.EventSetString(e, "sound", song);
        });

        foreach (var team in new[] { TFTeam.Red, TFTeam.Blue })
        {
            Fire(TFMessages.EventBroadcastAudio, e =>
            {
                events.EventSetInt(e, "team", (int)team);
                events.EventSetString(e, "sound", song);
            });
        }
    }

    public void StartRoundVoice()
    {
        // LAST_SHARED_TEAM + 1 .. GetNumberOfTeams() - 1 (teamplayroundbased_gamerules.cpp:3305-3308).
        foreach (var team in new[] { TFTeam.Red, TFTeam.Blue })
        {
            Fire(TFMessages.EventBroadcastAudio, e =>
            {
                events.EventSetInt(e, "team", (int)team);
                events.EventSetString(e, "sound", $"Game.TeamRoundStart{(int)team}");
            });
        }
    }
    public void MapTimeRemaining(int seconds)
        => Fire(TFMessages.EventMapTimeRemaining, e => events.EventSetInt(e, "seconds", seconds));

    public void UpdateTimer() => Fire(TFMessages.EventUpdateTimer, _ => { });

    public void WinlimitChanged() => Fire(TFMessages.EventWinlimitChanged, _ => { });

    public void ClanReadyRules(string signal)
    {
        PrintAll(TFMessages.HudPrintConsole, TFMessages.ClanReadyRules, signal);
        PrintAll(TFMessages.HudPrintTalk, TFMessages.ClanReadyRules, signal);
    }

    public void ReadyRestart() => Fire(TFMessages.EventReadyRestart, _ => { });

    public void TeamReady(TFTeam team)
        => Fire(TFMessages.EventTeamReady, e => events.EventSetInt(e, "team", (int)team));

    public void TeamsSwitched()
        => PrintAll(TFMessages.HudPrintTalk, TFMessages.TeamsSwitched);

    /// <summary>HUD_ALERT_SCRAMBLE_TEAMS, the first HUD_ALERT_* (tf_shareddefs.h:1781-1787).</summary>
    private const int HudAlertScrambleTeams = 0;

    public void ScrambleTeamsAlert()
        => Fire(TFMessages.EventTeamplayAlert, e => events.EventSetInt(e, "alert_type", HudAlertScrambleTeams));

    public void ScorestatsAccumulatedReset() => Fire(TFMessages.EventScorestatsAccumulatedReset, _ => { });

    private void Fire(string name, Action<IntPtr> fill)
    {
        IntPtr e = events.EventCreate(name);
        if (e == IntPtr.Zero)
            return;

        fill(e);
        events.EventFire(e);
    }

    private void PrintAll(byte destination, string text, params string[] args)
        => messages.Send(new TextMsg { Destination = destination, Text = text, Params = args }, player: 0, reliable: true);
}

/// <summary>
/// The map's round reset (stock's <c>CleanUpMap</c>, through the core's
/// <see cref="IMapCleanup"/> with TF's preserve list) and round inputs over the
/// core's entity walk: the inputs go through the registry's input dispatch, as stock's
/// <c>AcceptInput( "RoundSpawn" )</c> did, so an entity takes them as
/// <c>[ScriptedEvent]</c> inputs and a map's own I/O may send them too.
///
/// <para>
/// The control points' rules are resolved when the round asks, not taken by
/// constructor: they take <see cref="ITeamRoundState"/>, which is the round
/// machine this world is built for, and asking for them at construction is a
/// cycle (ControlPointRules -> ITeamRoundState -> TeamRoundFlow -> IRoundWorld)
/// that refuses the mod's whole load.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(IRoundWorld)])]
public sealed class CoreRoundWorld(IMapCleanup cleanup, EntityDirectory live, IEntityDirectory entities,
                                   IEngineEntities engine, IRoundTimers timers, IServiceLocator services,
                                   ILog log) : IRoundWorld
{
    private readonly TFMapCleanupFilter _filter = new();

    // The kits the stalemate disabled (stock's m_hDisabledHealthKits): handles,
    // so a kit removed mid-stalemate is simply not resolved any more.
    private readonly List<EntityHandle> _disabledKits = new();

    /// <summary>
    /// <c>CTFGameRules::CleanUpMap</c>: every player's conditions go first
    /// ("to prevent some dependency bugs"), then the teamplay base's
    /// <c>CleanUpMap</c> with TF's preserve list. Returns the map entities it
    /// created again. (The rest of TF's override -- the HLTV camera list, the
    /// toy and soccer-ball latches, the upgrade station's item reset -- has no
    /// subject in this port.)
    /// </summary>
    public int ResetMap()
    {
        if (services.Resolve<ITFPlayerConditions>() is { } conditions)
        {
            foreach (var player in live.Snapshot().OfType<SsPlayer>())
                conditions.RemoveAll(player.Handle);
        }

        return cleanup.CleanUpMap(_filter).Respawn.Created;
    }

    public int RoundSpawn() => Each("RoundSpawn");

    public int RoundActivate() => Each("RoundActivate");

    public void SetupControlPointSpawns()
    {
        if (services.Resolve<IControlPointRules>() is { } controlPoints)
            controlPoints.SetupControlPointSpawns();
        else
            log.Warning("[TF2/Rounds] no IControlPointRules is registered: the control points' spawns are not set up\n");
    }

    /// <summary>
    /// The projectiles by classname (this build's four, as
    /// <see cref="EntityForceRespawn"/>'s wipe takes them) and the buildings
    /// through the buildings lane's <see cref="TFBaseObject.DestroyObject"/> —
    /// stock's <c>SetSolid(SOLID_NONE)</c> + <c>UTIL_Remove</c> with the
    /// <c>object_removed</c> event fired first (docs/divergences.md: nothing
    /// here consumes that event, and every building is built, none map-placed).
    /// </summary>
    public void RemoveProjectilesAndBuildings()
    {
        foreach (string className in EntityForceRespawn.ProjectileClasses)
            foreach (EntityHandle projectile in engine.FindAllByClassname(className).ToList())
                engine.Remove(projectile);

        if (services.Resolve<ITFBuildings>() is not { } buildings)
        {
            log.Warning("[TF2/Rounds] no ITFBuildings is registered: the stalemate leaves the buildings standing\n");
            return;
        }

        foreach (TFBaseObject building in buildings.OwnedBy().ToList())
            building.DestroyObject();
    }

    public void DisableWorldHealthKits()
    {
        _disabledKits.Clear();
        foreach (string className in new[] { HealthKit.MapClass, HealthKitSmall.SmallClass, HealthKitMedium.MediumClass })
        {
            foreach (ScriptedEntity entity in entities.OfClass(className))
            {
                if (entity is not TFPowerup kit || kit.IsDisabled)
                    continue;

                // SetDisabled( true ) (tf_gamerules.cpp:5509): the kit hides.
                kit.SetDisabled(true);
                _disabledKits.Add(kit.Handle);
            }
        }
    }

    public void RestoreWorldHealthKits()
    {
        foreach (EntityHandle handle in _disabledKits)
        {
            // Stock's `if ( m_hDisabledHealthKits[i] )`: a kit that went away
            // under the stalemate is not brought back.
            if (entities.Get(handle) is TFPowerup kit)
                kit.SetDisabled(false);
        }

        _disabledKits.Clear();
    }

    // ---- The HUD timers stock's rules spawn (teamplayroundbased_gamerules.cpp) --
    //
    // The waiting timer (m_hWaitingForPlayersTimer, :1007-1023), the stalemate
    // one (m_hStalemateTimer, :2032-2042) and the time-limit one
    // (m_hTimeLimitTimer, :2782-2794) are team_round_timer entities the rules
    // create themselves, as handles: a map reset takes them, and the next ask
    // makes a new one (stock's CHandle, invalid after UTIL_Remove).

    private const string WaitingTimerName = "zz_teamplay_waiting_timer";
    private const string TimeLimitTimerName = "zz_teamplay_timelimit_timer";

    private EntityHandle _waitingTimer = EntityHandle.None;
    private EntityHandle _stalemateTimer = EntityHandle.None;
    private EntityHandle _timeLimitTimer = EntityHandle.None;

    public bool EnsureWaitingTimer(float seconds)
    {
        if (entities.Get(_waitingTimer) is TeamRoundTimer)
            return false;

        _waitingTimer = SpawnTimer();
        if (_waitingTimer == EntityHandle.None)
            return false;

        // Stock's create arm (:1007-1010) hides the HUD's timers BEFORE this
        // one shows: a hide after the show takes the waiting timer down with
        // it, and the HUD never sees the clock (live, ctf_2fort).
        timers.HideActive();

        // SetName (the targetname) and the show_in_hud + SetTime + Resume + Enable run (:1015-1020).
        live.SetName(_waitingTimer, WaitingTimerName);
        Show(_waitingTimer, seconds);
        return true;
    }

    public void RetuneWaitingTimer(float seconds)
    {
        // Stock :933-938: a SetTime on the timer it already has.
        if (entities.Get(_waitingTimer) is TeamRoundTimer)
            Input(_waitingTimer, "SetTime", seconds);
    }

    public void RemoveWaitingTimer() => Remove(ref _waitingTimer);

    public void SpawnStalemateTimer(int seconds)
    {
        if (entities.Get(_stalemateTimer) is not TeamRoundTimer)
        {
            _stalemateTimer = SpawnTimer();
            if (_stalemateTimer == EntityHandle.None)
                return;
        }

        Show(_stalemateTimer, seconds);
    }

    public void RemoveStalemateTimer() => Remove(ref _stalemateTimer);

    public void SpawnTimeLimitTimer(int seconds)
    {
        if (entities.Get(_timeLimitTimer) is not TeamRoundTimer)
        {
            _timeLimitTimer = SpawnTimer();
            if (_timeLimitTimer == EntityHandle.None)
                return;

            live.SetName(_timeLimitTimer, TimeLimitTimerName);
        }

        // The reuse path re-shows too (the SetTime/Resume/Enable run on it, :2789-2794).
        Show(_timeLimitTimer, seconds);
    }

    /// <summary>Stock's `if ( m_hTimeLimitTimer ) UTIL_Remove( ... )` (:2717-2721), the handle then invalid.</summary>
    public void RemoveTimeLimitTimer() => Remove(ref _timeLimitTimer);

    public bool MapHasActiveTimer()
    {
        foreach (ScriptedEntity entity in entities.OfClass(RoundTimers.ClassName))
        {
            if (entity is not TeamRoundTimer timer || !timer.ShowInHud)
                continue;

            // Not the time-limit one (the Q_stricmp, :2759).
            if (!string.Equals(live.NameOf(entity.Handle), TimeLimitTimerName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Stock's <c>CBaseEntity::Create("team_round_timer")</c>; the managed half binds at the native spawn.</summary>
    private EntityHandle SpawnTimer()
    {
        var created = engine.Create(RoundTimers.ClassName);
        if (created == EntityHandle.None)
        {
            log.Warning("[TF2/Rounds] the engine did not create a team_round_timer\n");
            return EntityHandle.None;
        }

        engine.Spawn(created);
        return created;
    }

    /// <summary>Stock's show_in_hud KeyValue and SetTime/Resume/Enable inputs (:1016-1020, :2037-2042, :2790-2794).</summary>
    private void Show(EntityHandle timer, float seconds)
    {
        Input(timer, "ShowInHUD", 1f);
        Input(timer, "SetTime", seconds);
        Input(timer, "Resume", 0f);
        Input(timer, "Enable", 0f);
    }

    private void Input(EntityHandle timer, string name, float number)
    {
        if (entities is not ScriptedEntityRegistry registry)
            return;

        try
        {
            registry.Input(timer, name, string.Empty, number, EntityHandle.None);
        }
        catch (Exception e)
        {
            log.Warning($"[TF2/Rounds] {name} on {timer} threw: {e}\n");
        }
    }

    /// <summary>Stock's `if ( m_h...Timer ) UTIL_Remove( ... )` (:866-869, :2016-2020, :2059-2062), the handle then invalid.</summary>
    private void Remove(ref EntityHandle timer)
    {
        if (entities.Get(timer) is not null)
            engine.Remove(timer);

        timer = EntityHandle.None;
    }

    public bool SetWinnerAndForceCaps(TFTeam team)
    {
        if (entities is not ScriptedEntityRegistry registry)
            return false;

        bool handled = false;
        foreach (var master in entities.OfClass(TeamControlPointMaster.MapClass))
        {
            try
            {
                if (registry.Input(master.Handle, "SetWinnerAndForceCaps", string.Empty, (int)team, EntityHandle.None))
                    handled = true;
            }
            catch (Exception e)
            {
                log.Warning($"[TF2/Rounds] SetWinnerAndForceCaps on {master.Handle} threw: {e}\n");
            }
        }

        return handled;
    }

    private int Each(string input)
    {
        if (entities is not ScriptedEntityRegistry registry)
            return 0;

        int took = 0;
        foreach (var entity in live.Snapshot())
        {
            try
            {
                if (registry.Input(entity.Handle, input, string.Empty, 0f, EntityHandle.None))
                    took++;
            }
            catch (Exception e)
            {
                log.Warning($"[TF2/Rounds] {input} on {entity.GetType().FullName} ({entity.Handle}) threw: {e}\n");
            }
        }

        return took;
    }
}

/// <summary>The core's intermission: the scoreboard, then the next map.</summary>
[SideService(Side.Server, As = [typeof(IGameEnd)])]
public sealed class CoreGameEnd(Intermission intermission, IGameClock clock) : IGameEnd
{
    public void GoToIntermission() => intermission.Begin(clock.Now);
}

/// <summary>The active mode's team-round half, through the core's mode proxy.</summary>
[SideService(Side.Server, As = [typeof(IActiveTeamRoundMode)])]
public sealed class ActiveTeamRoundMode(GameModes modes) : IActiveTeamRoundMode
{
    public ITeamRoundMode? Current => modes.Active as ITeamRoundMode;
}

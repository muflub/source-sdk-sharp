using SourceSharp.Abi;

namespace SourceSharp.Shared;

// Game event classes, declared in shared code so both sides see the same set —
// the server the authoritative events, the client the networked copies.
//
// These replace what CEventLog did in C++. That base class existed to listen for
// a fixed set of events and write them to the log; subscribing to the same
// events from managed code covers the requirement without inheriting the rest of
// its behaviour.
//
// Each class is an INSTANCE held by the side's service container
// (GameEventRegistry.AddEventsTo) and does two things: decode the raw event
// once into typed data, and raise a C# event that other code subscribes to by
// taking IGameEvent<TArgs> through its constructor. Subscribers therefore never
// touch GameEventReader, which is a ref struct precisely because it must not
// outlive the callback. They were static classes with static events; an
// instance per container means a subscription reaches one module's events and
// no other's, and a test raising one reaches only what it built.
//
// NOTE these fire on BOTH sides. A subscriber that should act only once — award
// a point, write a stat — has to check IEngineHost.ActiveSide.
//
// Unlike user messages, order carries no meaning: the engine dispatches by name,
// so classes can be added, removed or reordered freely.

/// <summary>Someone connected. Fires before they are in the game.</summary>
public readonly record struct PlayerConnectArgs(
    string Name, int UserId, bool IsBot, string Address);

[GameEvent("player_connect")]
public sealed class PlayerConnectEvent : IGameEvent<PlayerConnectArgs>
{
    /// <summary>Raised when a player connects.</summary>
    public event Action<PlayerConnectArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerConnectArgs args) => Fired?.Invoke(args);

    /// <summary>Called by the registry. Decodes, then raises.</summary>
    public void Dispatch(GameEventReader reader)
    {
        Raise(new PlayerConnectArgs(
            reader.GetString("name"),
            reader.GetInt("userid"),
            reader.GetInt("bot") != 0,
            reader.GetString("address")));
    }
}

/// <summary>Someone left, with the reason the engine recorded.</summary>
public readonly record struct PlayerDisconnectArgs(
    string Name, int UserId, string Reason);

[GameEvent("player_disconnect")]
public sealed class PlayerDisconnectEvent : IGameEvent<PlayerDisconnectArgs>
{
    public event Action<PlayerDisconnectArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerDisconnectArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
    {
        Raise(new PlayerDisconnectArgs(
            reader.GetString("name"),
            reader.GetInt("userid"),
            reader.GetString("reason")));
    }
}

/// <summary>
/// A death. The event a real mod hangs scoring off. The last three fields are
/// the mod-specific keys of stock's player_death (customkill, damagebits,
/// crit_type), which the stock TF death notices read for their icons
/// (tf_hud_deathnotice.cpp AddDeathNotice); a game that sends none of them
/// decodes zeros.
/// </summary>
public readonly record struct PlayerDeathArgs(
    int UserId, int AttackerUserId, string Weapon, bool Headshot, int DeathFlags = 0,
    int Custom = 0, int Bits = 0, int CritType = 0)
{
    /// <summary>
    /// True when the kill should credit the attacker. The engine reports a
    /// suicide as attacker == victim and a world death as attacker == 0, and
    /// neither should score — so this is worth answering once here rather than
    /// in every subscriber.
    /// </summary>
    public bool IsCredited => AttackerUserId != 0 && AttackerUserId != UserId;
}

[GameEvent("player_death")]
public sealed class PlayerDeathEvent : IGameEvent<PlayerDeathArgs>
{
    public event Action<PlayerDeathArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerDeathArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
    {
        Raise(new PlayerDeathArgs(
            reader.GetInt("userid"),
            reader.GetInt("attacker"),
            reader.GetString("weapon"),
            reader.GetBool("headshot"),
            reader.GetInt("death_flags"),
            reader.GetInt("customkill"),
            reader.GetInt("damagebits"),
            reader.GetInt("crit_type")));
    }
}

/// <summary>
/// A player was hurt. Stock fires it from <c>CBasePlayer::OnTakeDamage</c>
/// with the post-armour answer (<c>tf_player.cpp</c>: damageamount is
/// <c>iPrevHealth - m_iHealth</c>), which is what the combat text keys off —
/// the attacker's HUD reads the hits they dealt, the victim's the hits they
/// took. The TF-specific keys (custom, weaponid, bonuseffect and the crit
/// ladders) are stock's fields; a game that sends none of them decodes zeros.
/// </summary>
public readonly record struct PlayerHurtArgs(
    int UserId, int Health, int AttackerUserId, int DamageAmount = 0,
    bool Crit = false, int Custom = 0, int WeaponId = 0, int BonusEffect = 0,
    bool MiniCrit = false, bool AllSeeCrit = false, bool ShowDisguisedCrit = false);

[GameEvent("player_hurt")]
public sealed class PlayerHurtEvent : IGameEvent<PlayerHurtArgs>
{
    public event Action<PlayerHurtArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerHurtArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
    {
        Raise(new PlayerHurtArgs(
            reader.GetInt("userid"),
            reader.GetInt("health"),
            reader.GetInt("attacker"),
            reader.GetInt("damageamount"),
            reader.GetBool("crit"),
            reader.GetInt("custom"),
            reader.GetInt("weaponid"),
            reader.GetInt("bonuseffect"),
            reader.GetBool("minicrit"),
            reader.GetBool("allseecrit"),
            reader.GetBool("showdisguisedcrit")));
    }
}

/// <summary>
/// A player walked into (or out of) a control point's capture area. Stock
/// fires them with the player's entindex and the cap index
/// (resource/modevents.res); the HUD's teardrop progress bar attaches to
/// the icon of the point the local player's events name
/// (hud_controlpointicons.cpp, CHudControlPointIcons::FireGameEvent).
/// </summary>
public readonly record struct ControlPointTouchArgs(int Player, int Area);

[GameEvent("controlpoint_starttouch")]
public sealed class ControlPointStartTouchEvent : IGameEvent<ControlPointTouchArgs>
{
    public event Action<ControlPointTouchArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(ControlPointTouchArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new ControlPointTouchArgs(reader.GetInt("player"), reader.GetInt("area")));
}

[GameEvent("controlpoint_endtouch")]
public sealed class ControlPointEndTouchEvent : IGameEvent<ControlPointTouchArgs>
{
    public event Action<ControlPointTouchArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(ControlPointTouchArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new ControlPointTouchArgs(reader.GetInt("player"), reader.GetInt("area")));
}

/// <summary>A player finished spawning in and is playable.</summary>
public readonly record struct PlayerActivateArgs(int UserId);

[GameEvent("player_activate")]
public sealed class PlayerActivateEvent : IGameEvent<PlayerActivateArgs>
{
    public event Action<PlayerActivateArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerActivateArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new PlayerActivateArgs(reader.GetInt("userid")));
}

/// <summary>
/// A team change, including the initial assignment: whether the game chose
/// the team, whether it is to go unannounced, and the player's name.
/// </summary>
public readonly record struct PlayerTeamArgs(
    int UserId, int Team, int OldTeam, bool Disconnect, bool AutoTeam = false, bool Silent = false, string Name = "");

[GameEvent("player_team")]
public sealed class PlayerTeamEvent : IGameEvent<PlayerTeamArgs>
{
    public event Action<PlayerTeamArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerTeamArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
    {
        Raise(new PlayerTeamArgs(
            reader.GetInt("userid"),
            reader.GetInt("team"),
            reader.GetInt("oldteam"),
            reader.GetBool("disconnect"),
            reader.GetBool("autoteam"),
            reader.GetBool("silent"),
            reader.GetString("name")));
    }
}

/// <summary>Someone connected, as a client hears it (no address).</summary>
public readonly record struct PlayerConnectClientArgs(string Name, int Index, int UserId, string NetworkId, bool IsBot);

[GameEvent("player_connect_client")]
public sealed class PlayerConnectClientEvent : IGameEvent<PlayerConnectClientArgs>
{
    public event Action<PlayerConnectClientArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerConnectClientArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new PlayerConnectClientArgs(reader.GetString("name"), reader.GetInt("index"), reader.GetInt("userid"),
                                             reader.GetString("networkid"), reader.GetInt("bot") != 0));
}

/// <summary>A player's name changed.</summary>
public readonly record struct PlayerChangeNameArgs(int UserId, string OldName, string NewName);

[GameEvent("player_changename")]
public sealed class PlayerChangeNameEvent : IGameEvent<PlayerChangeNameArgs>
{
    public event Action<PlayerChangeNameArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(PlayerChangeNameArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new PlayerChangeNameArgs(reader.GetInt("userid"), reader.GetString("oldname"), reader.GetString("newname")));
}

/// <summary>A server convar marked FCVAR_NOTIFY changed.</summary>
public readonly record struct ServerCvarArgs(string Name, string Value);

[GameEvent("server_cvar")]
public sealed class ServerCvarEvent : IGameEvent<ServerCvarArgs>
{
    public event Action<ServerCvarArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(ServerCvarArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new ServerCvarArgs(reader.GetString("cvarname"), reader.GetString("cvarvalue")));
}

/// <summary>A new map, as the client mode fires it client-side at level init (stock ClientModeShared::LevelInit).</summary>
public readonly record struct GameNewMapArgs(string MapName);

[GameEvent("game_newmap")]
public sealed class GameNewMapEvent : IGameEvent<GameNewMapArgs>
{
    public event Action<GameNewMapArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(GameNewMapArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader) => Raise(new GameNewMapArgs(reader.GetString("mapname")));
}

/// <summary>The engine's GameUI became visible: stock's engine fires this client-side event
/// (<c>OnGameUIActivated</c>, shell_clientdll_gameclientexports.cpp) when Escape activates the
/// GameUI, and only through it does the client learn of the show.</summary>
public readonly record struct GameUiActivatedArgs;

[GameEvent("gameui_activated")]
public sealed class GameUiActivatedEvent : IGameEvent<GameUiActivatedArgs>
{
    public event Action<GameUiActivatedArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(GameUiActivatedArgs args) => Fired?.Invoke(args);

    /// <inheritdoc/>
    public void Dispatch(GameEventReader reader) => Raise(new GameUiActivatedArgs());   // the engine sends no fields
}

/// <summary>The engine's GameUI hid again (<c>OnGameUIHidden</c>): whatever hid it — the
/// menu's Resume running <c>gameui_hide</c>, the engine's own arm — the menu agrees through it.</summary>
public readonly record struct GameUiHiddenArgs;

[GameEvent("gameui_hidden")]
public sealed class GameUiHiddenEvent : IGameEvent<GameUiHiddenArgs>
{
    public event Action<GameUiHiddenArgs>? Fired;

    /// <inheritdoc/>
    public void Raise(GameUiHiddenArgs args) => Fired?.Invoke(args);

    /// <inheritdoc/>
    public void Dispatch(GameEventReader reader) => Raise(new GameUiHiddenArgs());      // the engine sends no fields
}

/// <summary>The win panel's event (modevents.res), stock's
/// <c>CTFWinPanel</c> shows on (tf_hud_winpanel.cpp:127-665) and the
/// server's <c>SendWinPanelInfo</c> fills. The defaults are the engine's
/// own: a field the sender never set reads zero.</summary>
public readonly record struct TeamplayWinPanelArgs(
    int PanelStyle = 0, int WinningTeam = 0, int WinReason = 0, string Cappers = "", int FlagCapLimit = 0,
    int BlueScore = 0, int RedScore = 0, int BlueScorePrev = 0, int RedScorePrev = 0,
    bool RoundComplete = false, int RoundsRemaining = 0,
    int Player1 = 0, int Player1Points = 0, int Player2 = 0, int Player2Points = 0,
    int Player3 = 0, int Player3Points = 0,
    int KillstreakPlayer = 0, int KillstreakCount = 0, bool GameOver = false);

[GameEvent("teamplay_win_panel")]
public sealed class TeamplayWinPanelEvent : IGameEvent<TeamplayWinPanelArgs>
{
    public event Action<TeamplayWinPanelArgs>? Fired;

    public void Raise(TeamplayWinPanelArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new TeamplayWinPanelArgs(
            reader.GetInt("panel_style"),
            reader.GetInt("winning_team"),
            reader.GetInt("winreason"),
            reader.GetString("cappers"),
            reader.GetInt("flagcaplimit"),
            reader.GetInt("blue_score"),
            reader.GetInt("red_score"),
            reader.GetInt("blue_score_prev"),
            reader.GetInt("red_score_prev"),
            reader.GetBool("round_complete"),
            reader.GetInt("rounds_remaining"),
            reader.GetInt("player_1"), reader.GetInt("player_1_points"),
            reader.GetInt("player_2"), reader.GetInt("player_2_points"),
            reader.GetInt("player_3"), reader.GetInt("player_3_points"),
            reader.GetInt("killstreak_player_1"), reader.GetInt("killstreak_player_1_count"),
            reader.GetBool("game_over")));
}

/// <summary>A round's start (modevents.res): the hide arm of the win panel
/// (tf_hud_winpanel.cpp:119-126).</summary>
public readonly record struct TeamplayRoundStartArgs(bool FullReset);

[GameEvent("teamplay_round_start")]
public sealed class TeamplayRoundStartEvent : IGameEvent<TeamplayRoundStartArgs>
{
    public event Action<TeamplayRoundStartArgs>? Fired;

    public void Raise(TeamplayRoundStartArgs args) => Fired?.Invoke(args);

    public void Dispatch(GameEventReader reader)
        => Raise(new TeamplayRoundStartArgs(reader.GetBool("full_reset")));
}

//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace TF2.Server.Player;

/// <summary>
/// The <c>joinclass &lt;class&gt;</c> client command:
/// <c>CTFPlayer::HandleCommand_JoinClass</c> (tf_player.cpp:6739-7060) with
/// the arms a non-arena, non-MvM, non-competitive game reaches.
///
/// <list type="bullet">
/// <item>Refused at game over, on no team, on the spectators.</item>
/// <item>A name is matched against each class's script name, case-insensitive;
/// Civilian and anything else is stock's warning. <c>random</c>/<c>auto</c>
/// draw a class other than the current one that the class limit allows.</item>
/// <item>The class limit (<c>CTFGameRules::CanPlayerChooseClass</c>,
/// tf_gamerules.cpp:17651-17707): <c>mp_highlander</c>'s one, else
/// <c>tf_classlimit</c>; teammates of the class, the player not counted.
/// A refusal reopens the team's class panel (<c>ShowViewPortPanel</c>,
/// :6866-6870, :6891-6893); a choice that passes sends no panel.</item>
/// <item>The chosen class is the DESIRED class (<see cref="TFPlayerClass"/>)
/// and fires <c>player_changeclass</c>; it becomes the class at the next spawn
/// (<see cref="TFPlayerSpawns"/>). The player respawns at once when they may:
/// allowed an instant spawn (no class yet: <c>ShouldGainInstantSpawn</c>),
/// dead past their team's next respawn wave, or alive inside their team's
/// respawn room (<see cref="TF2.Server.Entities.Rooms.ITFRespawnRooms"/>, the rooms' <c>PointInRespawnRoom</c>). Otherwise
/// <c>#game_respawn_as</c> (alive) or <c>#game_spawn_as</c> (dead) tells them
/// so. A loser during the winners' round (<c>GR_STATE_TEAM_WIN</c>) never
/// respawns from here.</item>
/// <item>In a stalemate an alive player outside the
/// <c>tf_stalematechangeclasstime</c> window is refused
/// (<c>#game_stalemate_cant_change_class</c>), and inside it the class
/// change respawns the player — alive or dead, the window overriding the
/// respawn rules (tf_player.cpp:6785-6795, :6995-7002).</item>
/// </list>
///
/// <para>
/// "Alive" is stock's <c>IsAlive()</c>: a classless player here is the
/// engine's living player in observer mode, where stock's is dead, so a
/// watcher counts as dead.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFJoinClassCommand(TFPlayerCarriers carriers, IEnginePlayers players, ITeamRoundRoster roster,
                                       TF2.Server.Entities.Rooms.ITFRespawnRooms rooms, TFClassTable classes, IUniformRandom random,
                                       IEngineGameEvents events, IUserMessageSender messages, IGameClock clock, ILog log,
                                       ITeamRoundState? round = null, ITeamRespawnWaves? waves = null,
                                       ITFClassLimitSettings? limits = null, ITFStalemateSettings? stalemate = null)
    : IClientCommandHook, IOrderedHook
{
    private readonly ITFStalemateSettings? _stalemate = stalemate;
    public const string Verb = "joinclass";

    /// <summary>The stock event a class choice fires.</summary>
    public const string ChangeClassEvent = "player_changeclass";

    /// <summary>A stage of its own in TF2's thousand (see <see cref="TF2.Server.Entities.ObjectiveResourceSpawner.Order"/>).</summary>
    public const int Order = 1_000_000 - 1_000 + 70;

    /// <summary><c>NO_CLASS_LIMIT</c>.</summary>
    public const int NoClassLimit = -1;

    public int HookOrder => Order;

    /// <summary>What a <c>joinclass</c> did, for the facts and the log.</summary>
    public enum Outcome
    {
        /// <summary>A gate before the name refused it (game over, no team, no carrier, a stalemate's living player).</summary>
        Refused,

        /// <summary>No such class.</summary>
        Invalid,

        /// <summary>The class limit refused it; the team's class panel is reopened (stock's <c>ShowViewPortPanel</c>).</summary>
        Limited,

        /// <summary>The class was already the desired or the current one, and nothing respawned.</summary>
        Unchanged,

        /// <summary>The player respawned as the class.</summary>
        Respawned,

        /// <summary>The class is desired and waits for the next spawn.</summary>
        AtNextSpawn,
    }

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!string.Equals(command, Verb, StringComparison.OrdinalIgnoreCase))
            return false;

        if (args.Length >= 1)
            JoinClass(player, args[0]);
        return true;
    }

    public Outcome JoinClass(EntityHandle player, string name)
    {
        RoundState state = round?.State ?? RoundState.RoundRunning;
        if (state == RoundState.GameOver)
            return Outcome.Refused;

        // "can only join a class after you join a valid team".
        int team = players.GetTeam(player);
        if (team <= (int)TFTeam.Spectator)
            return Outcome.Refused;

        int index = player.EntryIndex;
        if (carriers.Part<TFPlayerClass>(index) is not { } desired)
        {
            log.Warning($"[TF2/Player] {player} has no {TFEntityNames.PlayerData}; joinclass {name} ignored\n");
            return Outcome.Refused;
        }

        // "In case we don't get the class menu message before the spawn timer
        // comes up, fake that we've closed the menu." (tf_player.cpp:6781-6783)
        desired.MenuOpen = false;

        bool alive = IsAlive(player);
        bool stalemate = state == RoundState.Stalemate;

        // CanChangeClassInStalemate (tf_gamerules.cpp:16565-16568): the window
        // is tf_stalematechangeclasstime seconds from the stalemate's start.
        // An alive player outside it is refused with the window's length
        // (tf_player.cpp:6785-6795).
        bool inChangeWindow = stalemate && !round!.InWaitingForPlayers
                              && clock.Now < round.StalemateStartTime + (_stalemate?.ChangeClassTime ?? 20f);
        if (stalemate && alive && !inChangeWindow)
        {
            messages.Send(new TextMsg
            {
                Destination = TFMessages.HudPrintTalk,
                Text = TFMessages.StalemateCantChangeClass,
                Params = [((int)(_stalemate?.ChangeClassTime ?? 20f)).ToString(System.Globalization.CultureInfo.InvariantCulture)],
            }, player: player.EntryIndex, reliable: true);
            return Outcome.Refused;
        }

        bool shouldNotRespawn = state == RoundState.TeamWin && (int)(round?.WinningTeam ?? TFTeam.Unassigned) != team;
        if (shouldNotRespawn)
            desired.AllowInstantSpawn = false; // tf_player.cpp:6838-6842
        TFClass current = carriers.ClassOf(player);

        TFClass chosen;
        if (IsRandom(name))
        {
            chosen = ChooseRandom(player, team, current);
            if (chosen == TFClass.Undefined)
            {
                // "We failed to find a random class. Bring up the class menu
                // again" (:6891-6893) — unless the current class stands and
                // the player simply keeps it (:6888-6889, a silent return).
                if (!CanPlayerChooseClass(player, team, current))
                    ShowClassMenu(player, team);
                return Outcome.Limited;
            }
        }
        else
        {
            chosen = Named(name);
            if (chosen == TFClass.Undefined)
            {
                log.Warning($"HandleCommand_JoinClass( {name} ) - invalid class name.\n");
                return Outcome.Invalid;
            }

            if (!CanPlayerChooseClass(player, team, chosen))
            {
                ShowClassMenu(player, team); // :6866-6870
                return Outcome.Limited;
            }
        }

        // A player with no class yet spawns the moment they pick one (the
        // dead-never-spawned arm of stock's gates), and so one carrying a
        // grant (m_bAllowInstantSpawn, tf_player.cpp:7032 — game_forcerespawn's).
        bool allowInstantSpawn = current == TFClass.Undefined || desired.AllowInstantSpawn;

        // "joining the same class?": only a dead player allowed an instant spawn comes back.
        if (chosen == desired.Desired)
            return SameClass(player, allowInstantSpawn && !alive && !shouldNotRespawn);

        desired.Desired = chosen;
        FireChangeClass(player, chosen);

        if (chosen == current)
            return SameClass(player, allowInstantSpawn && !alive && !shouldNotRespawn);

        bool inRespawnRoom = alive && !stalemate && rooms.InRespawnRoom(player, sameTeamOnly: true);
        bool deadInstantSpawn = !alive && !stalemate;
        if (deadInstantSpawn && roster.DeathTime(player) > 0f && waves is not null)
        {
            // "In death mode, don't allow class changes to force respawns ahead of respawn waves."
            deadInstantSpawn = clock.Now > waves.NextRespawnWave((TFTeam)team, player);
        }

        // The stalemate overrides the respawn rules: only the class-change
        // window spawns (tf_player.cpp:6995-7002), where the dead-instant and
        // respawn-room arms are forced off.
        if (!shouldNotRespawn && (allowInstantSpawn || deadInstantSpawn || inRespawnRoom || inChangeWindow))
        {
            log.Msg($"[TF2/Player] player {index} joins as {TFClassNames.NonLocalized(chosen)} now\n");
            roster.ForceRespawn(player);
            return Outcome.Respawned;
        }

        log.Msg($"[TF2/Player] player {index} will be {TFClassNames.NonLocalized(chosen)} at the next spawn\n");
        messages.Send(new TextMsg
        {
            Destination = TFMessages.HudPrintTalk,
            Text = alive ? "#game_respawn_as" : "#game_spawn_as",
            Params = [classes[chosen].LocalizeName],
        }, player: index, reliable: true);
        return Outcome.AtNextSpawn;
    }

    private Outcome SameClass(EntityHandle player, bool respawn)
    {
        if (!respawn)
            return Outcome.Unchanged;

        roster.ForceRespawn(player);
        return Outcome.Respawned;
    }

    /// <summary><c>IsAlive()</c>: alive, and not the observer a classless player is here.</summary>
    private bool IsAlive(EntityHandle player)
        => players.IsAlive(player) && players.GetObserverMode(player) == ObserverMode.None;

    private static bool IsRandom(string name)
        => string.Equals(name, "random", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "auto", StringComparison.OrdinalIgnoreCase);

    /// <summary>The class a script name names (Scout..Engineer, stock's loop), else <see cref="TFClass.Undefined"/>.</summary>
    public static TFClass Named(string name)
    {
        for (var c = TFClass.Scout; c < TFClass.Civilian; c++)
        {
            if (string.Equals(name, TFClassNames.Raw(c), StringComparison.OrdinalIgnoreCase))
                return c;
        }

        return TFClass.Undefined;
    }

    /// <summary>
    /// <c>random</c>/<c>auto</c>: one of Scout..Engineer other than the
    /// current class that the limit allows; none left is
    /// <see cref="TFClass.Undefined"/> (stock reopens the class menu).
    /// </summary>
    private TFClass ChooseRandom(EntityHandle player, int team, TFClass current)
    {
        Span<TFClass> choices = stackalloc TFClass[(int)TFClass.Civilian - 1];
        int count = 0;
        for (var c = TFClass.Scout; c < TFClass.Civilian; c++)
        {
            if (c != current && CanPlayerChooseClass(player, team, c))
                choices[count++] = c;
        }

        return count == 0 ? TFClass.Undefined : choices[random.RandomInt(0, count - 1)];
    }

    /// <summary><c>CTFGameRules::GetClassLimit</c> outside tournament mode.</summary>
    public int ClassLimit()
    {
        if (limits?.Highlander == true)
            return 1;
        int limit = limits?.ClassLimit ?? 0;
        return limit != 0 ? limit : NoClassLimit;
    }

    /// <summary><c>CTFGameRules::CanPlayerChooseClass</c>.</summary>
    public bool CanPlayerChooseClass(EntityHandle player, int team, TFClass cls)
    {
        int limit = ClassLimit();
        if (limit == NoClassLimit || team is not ((int)TFTeam.Red or (int)TFTeam.Blue))
            return true;

        int count = 0;
        foreach (var teammate in roster.Players((TFTeam)team))
        {
            if (teammate != player && carriers.ClassOf(teammate) == cls)
                count++;
        }

        return count < limit;
    }

    /// <summary>Stock's <c>ShowViewPortPanel(GetTeamNumber() == TF_TEAM_RED ?
    /// PANEL_CLASS_RED : PANEL_CLASS_BLUE)</c> (:6868, :6892): the team's
    /// class panel, to that player alone — the same show
    /// <see cref="TFWelcomeMenuCommands"/> and <see cref="TFJoinTeamCommand"/> send.</summary>
    private void ShowClassMenu(EntityHandle player, int team)
        => messages.Send(new VguiMenu
        {
            Name = team == (int)TFTeam.Red
                ? TFJoinTeamCommand.ClassRedPanel
                : TFJoinTeamCommand.ClassBluePanel,
            Show = true,
            Keys = [],
            Values = [],
        }, player: player.EntryIndex, reliable: true);

    private void FireChangeClass(EntityHandle player, TFClass cls)
    {
        IntPtr e = events.EventCreate(ChangeClassEvent);
        if (e == IntPtr.Zero)
            return;

        events.EventSetInt(e, "userid", players.GetUserId(player));
        events.EventSetInt(e, "class", (int)cls);
        events.EventFire(e);
    }
}

/// <summary>The class-limit convars as <see cref="TFJoinClassCommand"/> reads them.</summary>
public interface ITFClassLimitSettings
{
    /// <summary><c>mp_highlander</c>: one of each class.</summary>
    bool Highlander { get; }

    /// <summary><c>tf_classlimit</c>: 0 for none.</summary>
    int ClassLimit { get; }
}

/// <summary>The class-limit convars, read when a <c>joinclass</c> asks.</summary>
[SideService(Side.Server, As = [typeof(ITFClassLimitSettings)])]
public sealed class TFClassLimitConVars(TFClassLimitConVar limit, MpHighlanderConVar highlander) : ITFClassLimitSettings
{
    public bool Highlander => highlander.Value;

    public int ClassLimit => limit.Value;
}

//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>How long a kill feed line stays, seconds; 0 turns the feed off (stock: hud_deathnotice_time 6).</summary>
[ConVar("hud_deathnotice_time", "6", Help = "How long a death notice stays on the screen, seconds (0 = none)")]
public sealed class HudDeathNoticeTimeConVar : FloatConVar
{
}

/// <summary>Who a user id is, for the kill feed: the player resource's slot, name and team.</summary>
public interface IKillFeedPlayers
{
    /// <summary>The player with user id <paramref name="userId"/>; false for 0 (the world) or someone gone.</summary>
    bool TryFind(int userId, out string name, out int team, out bool local);
}

/// <summary>
/// A death the feed stores, handed to a mod's own decode before the core's
/// queue takes it (stock's <c>CTFHudDeathNotice::OnGameEvent</c>: the subclass
/// reads its custom-kill keys, then <c>AddDeathNotice</c> stores). Called for
/// every <c>player_death</c>, even one the core's <c>hud_deathnotice_time</c>
/// would not show; the listener decides for itself. Implemented by a
/// <c>[SideService]</c> mod class; the feed's <see cref="IHooks{IKillFeedListener}"/>
/// collects them.
/// </summary>
public interface IKillFeedListener
{
    /// <summary>One <c>player_death</c>, as the core read it.</summary>
    void OnDeath(PlayerDeathArgs death);
}

/// <summary>
/// What killed a player when no player did: a mod's monster, robot or trap,
/// which <c>player_death</c> cannot name (its <c>attacker</c> is a user id, 0
/// for anything that is not a player). The kill feeds ask the chain for a
/// death whose attacker is no player and is not the victim, before they call
/// it a suicide; the first member that answers names the killer, and the
/// line is drawn as a kill by that name. With no member answering, the death
/// reads as stock's: a suicide. Implemented by a <c>[SideService]</c> mod
/// class; order among several is <see cref="IOrderedHook"/>'s.
/// </summary>
public interface IKillFeedKillerNames
{
    /// <summary>The killer of <paramref name="death"/>, when no player killed: its name and team; false when this member does not know.</summary>
    bool TryName(in PlayerDeathArgs death, out string name, out int team);
}

/// <summary>
/// The first answer of the <see cref="IKillFeedKillerNames"/> chain, for a
/// death no player is credited with: what the core feed and a mod's feed ask
/// alike, so they agree. A member that throws is skipped.
/// </summary>
public static class KillFeedKillers
{
    /// <summary>
    /// The named killer of a death whose attacker is no player (<paramref name="hasKiller"/> false)
    /// and not the victim; false for every other death, and when no member answers.
    /// </summary>
    public static bool TryName(IHooks<IKillFeedKillerNames>? names, in PlayerDeathArgs death, bool hasKiller,
                               out string name, out int team)
    {
        (name, team) = ("", 0);
        if (hasKiller || names is null || death.AttackerUserId == death.UserId)
            return false;
        foreach (var member in names.All)
        {
            try
            {
                if (member.TryName(in death, out name, out team) && name.Length > 0)
                    return true;
            }
            catch (Exception)
            {
                // A broken member names nobody; the next is asked.
            }
        }
        (name, team) = ("", 0);
        return false;
    }
}

/// <summary>
/// THE KILL FEED'S QUEUE (stock CHudDeathNotice, hl2mp/hud_deathnotice.cpp):
/// each player_death becomes a line, the oldest dropped past
/// <see cref="MaxLines"/> (stock MaxDeathNotices 4), each kept
/// hud_deathnotice_time seconds (stock 6) and none at 0. A suicide is
/// <c>!killer || killer == victim</c>; its line draws no killer and the skull.
/// The weapon's icon is the UI icon set's art for it (<see cref="KillIcons"/>). Each death is also written to the
/// console as stock does.
/// </summary>
public sealed class KillFeed : IKillFeedSource
{
    /// <summary>Stock's MaxDeathNotices (CPanelAnimationVar, default "4").</summary>
    public const int MaxLines = 4;

    private readonly IGameClock _clock;
    private readonly ILog _log;
    private readonly IKillFeedPlayers _players;
    private readonly HudDeathNoticeTimeConVar? _time;
    private readonly IHooks<IKillFeedListener>? _listeners;
    private readonly IHooks<IKillFeedKillerNames>? _killerNames;
    private readonly KillFeedLine[] _lines = new KillFeedLine[MaxLines];
    private readonly float[] _expires = new float[MaxLines];
    private int _count;

    public KillFeed(IGameClock clock, ILog log, IKillFeedPlayers players, HudDeathNoticeTimeConVar? time = null,
                    IHooks<IKillFeedListener>? listeners = null, IHooks<IKillFeedKillerNames>? killerNames = null)
        => (_clock, _log, _players, _time, _listeners, _killerNames) = (clock, log, players, time, listeners, killerNames);

    /// <summary>The lines as of now: reading the count retires the expired ones first.</summary>
    public int LineCount
    {
        get
        {
            Refresh();
            return _count;
        }
    }

    public KillFeedLine Line(int i) => _lines[i];

    public void Subscribe(PlayerDeathEvent deaths) => deaths.Fired += Add;

    /// <summary>One player_death (stock FireGameEvent). A mod's listeners
    /// decode first (stock's subclass does, before AddDeathNotice).</summary>
    public void Add(PlayerDeathArgs death)
    {
        _listeners?.Each(nameof(IKillFeedListener.OnDeath), h => h.OnDeath(death));

        float time = _time?.Value ?? 6f;
        if (time == 0f)
            return;

        bool hasKiller = _players.TryFind(death.AttackerUserId, out string killer, out int killerTeam, out bool killerLocal);
        bool hasVictim = _players.TryFind(death.UserId, out string victim, out int victimTeam, out bool victimLocal);
        if (!hasKiller)
            (killer, killerTeam, killerLocal) = ("", 0, false);
        if (!hasVictim)
            (victim, victimTeam, victimLocal) = ("", 0, false);

        // A killer that is no player, named by a mod (IKillFeedKillerNames):
        // the line is a kill by that name, not stock's suicide.
        if (KillFeedKillers.TryName(_killerNames, in death, hasKiller, out string named, out int namedTeam))
            (killer, killerTeam, hasKiller) = (named, namedTeam, true);

        // Stock: iSuicide = ( !killer || killer == victim ), on entity indices.
        bool suicide = !hasKiller || death.AttackerUserId == death.UserId;
        string weapon = death.Weapon ?? "";
        string icon = suicide ? KillIcons.Skull : KillIcons.For(weapon);

        // Stock removes the oldest when the queue is full.
        if (_count >= MaxLines)
            RemoveAt(0);

        _lines[_count] = new KillFeedLine(suicide ? "" : killer, killerTeam, victim, victimTeam,
                                          weapon.Replace('_', ' ').ToUpperInvariant(), icon, suicide,
                                          death.Headshot, killerLocal || victimLocal);
        _expires[_count] = _clock.Now + time;
        _seen = Math.Max(_seen, _clock.Now);
        _count++;

        // Stock's console line. Its "died." arm compares "death_<weapon>" with
        // "d_worldspawn" and so never matches: every suicide says "suicided.".
        if (suicide)
            _log.Msg($"{victim} suicided.\n");
        else if (weapon.Length > 0)
            _log.Msg($"{killer} killed {victim} with {weapon}.\n");
        else
            _log.Msg($"{killer} killed {victim}");
    }

    /// <summary>
    /// Drops the lines whose time is up (stock RetireExpiredDeathNotices), and
    /// all of them when the clock went back (a new level; stock purges at VidInit).
    /// </summary>
    public void Refresh()
    {
        float now = _clock.Now;
        bool newLevel = now < _seen;
        _seen = now;
        for (int i = _count - 1; i >= 0; i--)
            if (newLevel || _expires[i] < now)
                RemoveAt(i);
    }

    private float _seen;

    private void RemoveAt(int i)
    {
        for (int j = i; j < _count - 1; j++)
        {
            _lines[j] = _lines[j + 1];
            _expires[j] = _expires[j + 1];
        }
        _count--;
        _lines[_count] = default;
    }
}

/// <summary>
/// The kill feed's players over the player resource (user id to slot and team)
/// and the engine's player info (the name), as the scoreboard reads them.
/// </summary>
public sealed class KillFeedPlayers(IClientStandalones standalones, IEngineHud screen, IPlayerNames names)
    : IKillFeedPlayers
{
    public bool TryFind(int userId, out string name, out int team, out bool local)
    {
        (name, team, local) = ("", 0, false);
        if (userId <= 0)
            return false;
        var resource = standalones.OfClass(EntityNames.PlayerManager).OfType<Standalone.ClientPlayerResource>().FirstOrDefault();
        if (resource is null)
            return false;
        var state = resource.State;
        for (int slot = 1; slot < PlayerResourceNetState.Slots; slot++)
        {
            if (!state.HasFlag(slot, PlayerResourceNetState.FlagConnected) || state.UserId[slot] != userId)
                continue;
            name = names.TryGet(slot, out var info) && info.Name.Length > 0 ? info.Name : $"#{slot}";
            team = state.Team[slot];
            local = slot == screen.LocalPlayerIndex;
            return true;
        }
        return false;
    }
}

/// <summary>
/// Which icon of the UI's icon set (src/client/ui/icons) a kill is drawn with,
/// from the weapon name player_death carries (the server's renames of stock's
/// DeathNotice: weapon_/npc_/func_ dropped, physics, combine_ball,
/// smg1_grenade, slam). A world death is the skull, as stock's (no
/// death_world icon); a name the set has no art for is "generic" (user ruling
/// 2026-10-05: stock fell back to the skull).
/// </summary>
public static class KillIcons
{
    public const string Skull = "skull", Generic = "generic";

    public static string For(string weapon) => weapon switch
    {
        "crowbar" or "stunstick" or "pistol" or "357" or "smg1" or "ar2" or "shotgun" or "crossbow" or "rpg" or "slam"
            or "physcannon" or "physics" or "combine_ball" => weapon,
        // This mod's own projectiles reach the event under their own class names
        // (DeathNotice renames only weapon_/npc_/func_ and stock's few), and stock's.
        "frag" or "grenade_frag" or EntityNames.NpcGrenadeFrag => "frag",
        "crossbow_bolt" or EntityNames.CrossbowBolt => "crossbow",
        "rpg_missile" or EntityNames.RpgMissile => "rpg",
        EntityNames.PropCombineBall => "combine_ball",
        EntityNames.SsBarrel => "physics",
        "smg1_grenade" or "env_explosion" or "explosion" => "explosion",
        "world" or "worldspawn" or "" => Skull,
        _ => Generic,
    };
}

/// <summary>
/// A player_death raised on this client alone, as the engine's would be
/// dispatched: what proves the kill feed before the server fires the event.
/// A cheat: it puts a death on the screen that did not happen.
/// </summary>
[ConCommand("cs_cl_death", Flags = ConVarFlags.Cheat,
            Help = "cs_cl_death <victim userid|me> <attacker userid|me> [weapon] [headshot 1|0] [death_flags]: raise player_death on this client")]
public sealed class ClientDeathCommand(PlayerDeathEvent deaths, ILog log, IClientStandalones standalones, IEngineHud screen)
{
    // "me": the local player's user id, from the player resource.
    private bool TryUser(string word, out int user)
    {
        if (word != "me")
            return int.TryParse(word, out user);
        user = 0;
        var resource = standalones.OfClass(EntityNames.PlayerManager).OfType<Standalone.ClientPlayerResource>().FirstOrDefault();
        int local = screen.LocalPlayerIndex;
        if (resource is null || local <= 0 || local >= PlayerResourceNetState.Slots)
            return false;
        user = resource.State.UserId[local];
        return user > 0;
    }

    public void Execute(string victim, string attacker, string weapon = "", bool headshot = false, int deathFlags = 0)
    {
        // A userid, or "me": the local player's. Refused like any other word that is neither.
        if (!TryUser(victim, out int victimUser) || !TryUser(attacker, out int attackerUser))
        {
            log.Msg("usage: cs_cl_death <victim userid|me> <attacker userid|me> [weapon] [headshot 1|0] [death_flags]\n");
            return;
        }
        deaths.Raise(new PlayerDeathArgs(victimUser, attackerUser, weapon, headshot, deathFlags));
    }
}

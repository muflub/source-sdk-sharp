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
/// A mod's say in the round end, as a hook: <see cref="Intermission.Begin"/>
/// asks every gate first, and any <c>false</c> ignores that round end entirely
/// -- no scoreboard, no frozen players, no level change. With no gates the
/// round ends as stock's does (intermission, then changelevel). A gate that
/// throws is logged and does not veto, as <see cref="IRespawnGate"/>.
/// </summary>
public interface IRoundEndGate
{
    bool MayEndRound();
}

/// <summary>
/// Told when the round ends and the intermission starts (the end-of-map vote),
/// and asked, while it runs, whether the level change must wait: a hook that
/// holds it (a vote on the next map still open) delays the change until it lets
/// go, at most <see cref="Intermission.MostHeldSeconds"/> past the intermission.
/// </summary>
public interface IIntermissionHook
{
    /// <summary>The round ended on <paramref name="currentMap"/>; the level would change to <paramref name="nextMap"/> now.</summary>
    void OnIntermission(string currentMap, string nextMap, float now);

    /// <summary>Whether the level must not change yet.</summary>
    bool HoldsLevel(float now);
}

/// <summary>
/// The end of a round, as stock's HL2MP: <c>CHL2MPRules::GoToIntermission</c>
/// shows every player the scoreboard (VGUIMenu <c>scores</c>) and freezes them,
/// and <c>mp_chattime</c> later <c>Think</c> changes the level once
/// (<c>CMultiplayRules::ChangeLevel</c>): to <c>nextlevel</c> if it is set,
/// else the map cycle's next map (<c>mapcyclefile</c>, looked for under cfg/
/// then the mod root, <c>//</c> comments and blank lines skipped, walked in
/// order and wrapped), else the current map.
///
/// <para>
/// The level change is the console's <c>changelevel</c>, the same path the
/// descent mod's LevelHop takes (IServerConsole), which is stock's
/// <c>engine-&gt;ChangeLevel( map, NULL )</c>. Native code makes no decision:
/// the empty <c>EndMultiplayerGame</c> it used to be handed is not called.
/// </para>
/// </summary>
public sealed class Intermission(ILog log, MpChatTimeConVar chatTime, NextLevelConVar nextLevel,
                                 MapCycleFileConVar mapCycle, IEntityDirectory directory, IEngineEntities entities,
                                 IEnginePlayerMovement players, IUserMessageSender messages, IServerConsole console,
                                 IEngineFiles files, IHooks<IRoundEndGate>? gates = null, IHooks<IIntermissionHook>? hooks = null)
{
    /// <summary>The scoreboard's viewport panel name (PANEL_SCOREBOARD).</summary>
    public const string ScoresPanel = "scores";

    /// <summary>The longest a hook may hold the level past the intermission's own end.</summary>
    public const float MostHeldSeconds = 60f;

    private readonly IHooks<IRoundEndGate>? _gates = gates;
    private readonly IHooks<IIntermissionHook>? _hooks = hooks;
    private bool _heldSaid;

    private string _mapName = string.Empty;
    private float _endsAt;
    private bool _changed;
    private bool _ignoredSaid;

    // Stock's m_nMapCycleindex: a static, so it carries across levels.
    private int _cycleIndex;

    /// <summary>g_fGameOver: the round is over and the intermission running.</summary>
    public bool GameOver { get; private set; }

    /// <summary>Levels changed, since the module started.</summary>
    public int Changes { get; private set; }

    /// <summary>
    /// A new level: no round is over (stock's constructor and RestartGame clear it),
    /// and no next level is set (the constructor's <c>nextlevel.SetValue( "" )</c>),
    /// so a level a vote chose is played once and the cycle goes on after it.
    /// </summary>
    public void LevelInit(string mapName, float now)
    {
        _mapName = mapName;
        GameOver = false;
        _changed = false;
        _ignoredSaid = false;
        _heldSaid = false;
        _endsAt = now;
        if (nextLevel.Value.Length > 0)
            nextLevel.SetValue("");
    }

    /// <summary>GoToIntermission: once.</summary>
    public void Begin(float now)
    {
        if (GameOver)
            return;

        if (!MayEndRound())
        {
            if (!_ignoredSaid)
                log.Msg("[SourceSharp/Rules] round end ignored: a mod's round-end gate keeps this level\n");
            _ignoredSaid = true;
            return;
        }

        GameOver = true;
        _endsAt = now + (int)chatTime.Value;   // mp_chattime.GetInt()

        foreach (var entity in directory.OfClass(EntityNames.Player))
        {
            if (entity is not SsPlayer player)
                continue;

            messages.Send(new VguiMenu { Name = ScoresPanel, Show = true, Keys = [], Values = [] },
                          entities.GetIndex(player.Handle), reliable: true);
            players.SetFrozen(player.Handle, true);
        }

        log.Msg($"[SourceSharp/Rules] intermission: scores shown, the level changes in {_endsAt - now:0} s\n");
        _hooks?.Each(nameof(IIntermissionHook.OnIntermission), hook => hook.OnIntermission(_mapName, PeekNextMap(), now));
    }

    /// <summary>Whether a hook holds the level; a hook that throws holds nothing.</summary>
    private bool Held(float now)
    {
        if (_hooks is null)
            return false;
        foreach (var hook in _hooks.All)
        {
            try
            {
                if (hook.HoldsLevel(now))
                    return true;
            }
            catch (Exception e)
            {
                log.Warning($"[SourceSharp/Rules] intermission hook {hook.GetType().FullName} threw: {e.Message}\n");
            }
        }
        return false;
    }

    /// <summary>Whether every gate lets the round end. A gate that throws is logged and does not veto.</summary>
    private bool MayEndRound()
    {
        if (_gates is null)
            return true;

        foreach (var gate in _gates.All)
        {
            try
            {
                if (!gate.MayEndRound())
                    return false;
            }
            catch (Exception e)
            {
                log.Warning($"[SourceSharp/Rules] round-end gate {gate.GetType().FullName} threw: {e.Message}\n");
            }
        }

        return true;
    }

    /// <summary>Think's game-over arm: change the level once the intermission is over.</summary>
    public void Think(float now)
    {
        if (!GameOver || _changed || _endsAt >= now)
            return;

        if (now < _endsAt + MostHeldSeconds && Held(now))
        {
            if (!_heldSaid)
                log.Msg("[SourceSharp/Rules] intermission: the level change waits for the vote on the next map\n");
            _heldSaid = true;
            return;
        }

        _changed = true;
        string next = NextMap();
        log.Msg($"CHANGE LEVEL: {next}\n");
        Changes++;
        console.Execute($"changelevel {next}");
    }

    /// <summary>ChangeLevel's choice: nextlevel, else the cycle's next (advancing it), else this map.</summary>
    public string NextMap()
    {
        string map = PeekNextMap();
        // Stock's ChangeLevel advances the cycle only when nextlevel did not choose.
        if (nextLevel.Value.Length == 0 && Cycle() is { Count: > 0 } cycle && ++_cycleIndex >= cycle.Count)
            _cycleIndex = 0;
        return map;
    }

    /// <summary>
    /// The map the level would change to now, the cycle left where it is
    /// (stock TF2's <c>nextmap</c>: <c>nextlevel</c>, else <c>GetNextLevelName</c>).
    /// </summary>
    public string PeekNextMap()
    {
        string set = nextLevel.Value;
        if (set.Length > 0)
            return set;

        var cycle = Cycle();
        if (cycle.Count == 0)
            return _mapName;

        if (_cycleIndex >= cycle.Count)
            _cycleIndex = 0;

        return cycle[_cycleIndex];
    }

    /// <summary>The level playing now.</summary>
    public string CurrentMap => _mapName;

    private List<string> Cycle() => ReadCycle(files, mapCycle.Value);

    /// <summary>The maps <paramref name="name"/> lists, the map cycle file's own fallback included.</summary>
    public static List<string> ReadCycle(IEngineFiles files, string? name)
    {
        name ??= string.Empty;
        if (name.Length == 0 || name.Contains("..", StringComparison.Ordinal))
            return [];

        string? text = files.ReadGameFile("cfg/" + name) ?? files.ReadGameFile(name);
        if (text is null && string.Equals(name, "mapcycle.txt", StringComparison.OrdinalIgnoreCase))
            text = files.ReadGameFile("cfg/mapcycle_default.txt");

        return text is null ? [] : ParseCycle(text);
    }

    /// <summary>RawLoadMapCycleFileIntoVector: lines, spaces and CRs stripped, comments and blanks dropped.</summary>
    public static List<string> ParseCycle(string text)
        => text.Split('\n')
               .Select(l => l.Replace(" ", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal))
               .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal))
               .ToList();
}

/// <summary>
/// <c>nextmap</c>: the map after this one, a voted one included (stock TF2's
/// client command, tf_gamerules.cpp: <c>nextlevel</c>, else the cycle's next),
/// in the asking player's chat and in the server's log.
/// </summary>
[ConCommand("nextmap", Help = "Print the map the level changes to next")]
public sealed class NextMapCommand(ILog log, Intermission intermission, IPlayerNotices notices, IUserMessageSender messages)
{
    /// <summary>The chat line's localised text; its one parameter is the map.</summary>
    public const string Token = "#SS_UI_NextMap";

    public void Execute()
    {
        string next = intermission.PeekNextMap();
        log.Msg($"[SourceSharp/Rules] nextmap: {next}\n");
        if (notices.CommandIssuer() is > 0 and int slot)
            messages.Send(new TextMsg { Destination = PlayerNotices.PrintTalk, Text = Token, Params = [next] }, slot, reliable: true);
    }
}

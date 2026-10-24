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
public sealed class Intermission(ILog log, ConVarRegistry convars, IEntityDirectory directory, IEngineEntities entities,
                                 IEnginePlayerMovement players, IUserMessageSender messages, IServerConsole console,
                                 IEngineFiles files, IHooks<IRoundEndGate> gates)
{
    /// <summary>The scoreboard's viewport panel name (PANEL_SCOREBOARD).</summary>
    public const string ScoresPanel = "scores";

    private readonly MpChatTimeConVar? _chatTime = convars.Get<MpChatTimeConVar>();
    private readonly NextLevelConVar? _nextLevel = convars.Get<NextLevelConVar>();
    private readonly MapCycleFileConVar? _mapCycle = convars.Get<MapCycleFileConVar>();

    private readonly IHooks<IRoundEndGate> _gates = gates;

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

    /// <summary>A new level: no round is over (stock's constructor and RestartGame clear it).</summary>
    public void LevelInit(string mapName, float now)
    {
        _mapName = mapName;
        GameOver = false;
        _changed = false;
        _ignoredSaid = false;
        _endsAt = now;
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
        _endsAt = now + (int)(_chatTime?.Value ?? 10f);   // mp_chattime.GetInt()

        foreach (var entity in directory.OfClass(EntityNames.SsPlayer))
        {
            if (entity is not SsPlayer player)
                continue;

            messages.Send(new VguiMenu { Name = ScoresPanel, Show = true, Keys = [], Values = [] },
                          entities.GetIndex(player.Handle), reliable: true);
            players.SetFrozen(player.Handle, true);
        }

        log.Msg($"[SourceSharp/Rules] intermission: scores shown, the level changes in {_endsAt - now:0} s\n");
    }

    /// <summary>Whether every gate lets the round end. A gate that throws is logged and does not veto.</summary>
    private bool MayEndRound()
    {
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

        _changed = true;
        string next = NextMap();
        log.Msg($"CHANGE LEVEL: {next}\n");
        Changes++;
        console.Execute($"changelevel {next}");
    }

    /// <summary>ChangeLevel's choice: nextlevel, else the cycle's next (advancing it), else this map.</summary>
    public string NextMap()
    {
        string set = _nextLevel?.Value ?? string.Empty;
        if (set.Length > 0)
            return set;

        var cycle = Cycle();
        if (cycle.Count == 0)
            return _mapName;

        if (_cycleIndex >= cycle.Count)
            _cycleIndex = 0;

        string map = cycle[_cycleIndex];
        if (++_cycleIndex >= cycle.Count)
            _cycleIndex = 0;

        return map;
    }

    private List<string> Cycle()
    {
        string name = _mapCycle?.Value ?? string.Empty;
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

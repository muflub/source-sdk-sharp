using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Client.Setup;

/// <summary>
/// The create-server screen's state and what Start sends (plan_hl2mp_ai
/// §3.5): a map checked on the game's paths, a player count, preset bots at
/// a difficulty stop, the custom bots, and the console lines that start the
/// listen server with them. The commands are stock's create-server dialog's
/// (disconnect, a wait, maxplayers, map) with the roster set before the
/// map, so the server reads it at the level's start; the server judges every
/// bot again.
/// </summary>
[SideService(Side.Client, As = [typeof(ICreateServerSource), typeof(ICreateServerActions)])]
public sealed class CreateServerModel(IFwdEngineClient engine, IFwdBaseFileSystem files, CustomBots customs, BotEditorModel editor)
    : ICreateServerSource, ICreateServerActions
{
    public const int PlayersMin = 2;

    public const int PlayersMax = 16;

    /// <summary>
    /// The difficulty slider's stops, Easy, Medium, Hard: the presets
    /// <c>easy</c>, <c>normal</c>, <c>hard</c> (§10). Expert spends the whole
    /// pool and is reachable only as a custom bot, so it is not a stop.
    /// </summary>
    public static string StopPreset(int stop) => stop switch
    {
        0 => "easy",
        1 => "normal",
        2 => "hard",
        _ => throw new ArgumentOutOfRangeException(nameof(stop), stop, "the slider has three stops"),
    };

    public const int Stops = 3;

    private string _map = "dm_lockdown";
    private string? _checkedMap;
    private bool _mapFound;

    public string MapName => _map;

    /// <summary><c>maps/&lt;name&gt;.bsp</c> on the GAME paths, asked once per name typed.</summary>
    public bool MapFound
    {
        get
        {
            if (!ReferenceEquals(_checkedMap, _map))
            {
                _checkedMap = _map;
                _mapFound = MapNameValid(_map) && files.FileExists($"maps/{_map}.bsp", "GAME");
            }

            return _mapFound;
        }
    }

    public int MaxPlayers { get; private set; } = 8;

    public int MaxPlayersMin => PlayersMin;

    public int MaxPlayersMax => PlayersMax;

    private int _botCount = 3;

    /// <summary>The preset bots, never more than the slots the custom bots leave (a custom bot added in the editor takes one).</summary>
    public int BotCount => Math.Min(_botCount, BotCountMax);

    /// <summary>Every slot but the player's own, less the custom bots.</summary>
    public int BotCountMax => Math.Max(0, MaxPlayers - 1 - customs.Count);

    public int Difficulty { get; private set; } = 1;

    public int DifficultyStops => Stops;

    public string DifficultyToken(int stop) => stop switch
    {
        0 => "#Ai_Bots_Easy",
        1 => "#Ai_Bots_Medium",
        _ => "#Ai_Bots_Hard",
    };

    public int DifficultySpent(int stop)
    {
        SkillPresets.TryGet(StopPreset(stop), out SkillProfile? preset);
        return preset!.Spent;
    }

    public int CustomCount => customs.Count;

    public CustomBotRow Custom(int i)
    {
        CustomBot bot = customs[i];
        var (first, second) = SkillTokens.Style(bot.Profile);
        return new CustomBotRow(bot.Profile.Name, bot.Profile.Spent, StandardSkills.Pool, first, second);
    }

    public string StartRefusalToken
        => !MapNameValid(_map) ? "#Ai_Start_BadMap"
         : !MapFound ? "#Ai_Start_NoMap"
         : BotCount + customs.Count > MaxPlayers - 1 ? "#Ai_Start_TooMany"
         : "";

    /// <summary>A map name that can ride a console line: letters, digits, _ and -.</summary>
    public static bool MapNameValid(string name)
    {
        if (name.Length is 0 or > 64)
            return false;
        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
                return false;
        }

        return true;
    }

    public void SetMapName(string name) => _map = name.Trim();

    public void SetMaxPlayers(int count)
    {
        MaxPlayers = Math.Clamp(count, PlayersMin, PlayersMax);
        _botCount = Math.Min(_botCount, BotCountMax);
    }

    public void SetBotCount(int count) => _botCount = Math.Clamp(count, 0, BotCountMax);

    public void SetDifficulty(int stop) => Difficulty = Math.Clamp(stop, 0, Stops - 1);

    /// <summary>The screen the editor is.</summary>
    public const string EditorScreen = "boteditor";

    /// <summary>A new custom bot: the editor opens on the Medium preset, unnamed (the server draws its name unless it is saved).</summary>
    public void AddCustom()
    {
        if (MaxPlayers - 1 - customs.Count <= 0)
            return;
        editor.Begin(-1);
        engine.SubmitConsoleLine($"cs_ui_open {EditorScreen}");
    }

    public void EditCustom(int i)
    {
        if ((uint)i >= (uint)customs.Count)
            return;
        editor.Begin(i);
        engine.SubmitConsoleLine($"cs_ui_open {EditorScreen}");
    }

    public void RemoveCustom(int i)
    {
        if ((uint)i < (uint)customs.Count)
            customs.RemoveAt(i);
    }

    /// <summary>The roster Start sets: the preset bots, then each custom bot.</summary>
    public string RosterSpecText()
    {
        var entries = new List<RosterEntry>();
        for (int i = 0; i < BotCount; i++)
            entries.Add(new RosterEntry(null, StopPreset(Difficulty)));
        foreach (CustomBot bot in customs.All)
            entries.Add(bot.Entry);
        return RosterSpec.Format(entries);
    }

    /// <summary>The console lines Start sends, in order; none while Start is refused.</summary>
    public IReadOnlyList<string> StartCommands()
        => StartRefusalToken.Length > 0
            ? []
            : ["disconnect", $"wait {ShutdownWaitFrames}", $"maxplayers {MaxPlayers}", $"ai_bot_roster \"{RosterSpecText()}\"", $"map {_map}"];

    /// <summary>
    /// Frames between <c>disconnect</c> and <c>maxplayers</c>: the listen
    /// server shuts down a few frames after its own player leaves, and
    /// <c>maxplayers</c> is refused while it runs. Stock's dialog waits two;
    /// with bots on the server two were too few (seen live: "Cannot change
    /// maxplayers while the server is running" before the bots were dropped).
    /// Half a second at the local fps_max.
    /// </summary>
    public const int ShutdownWaitFrames = 30;

    /// <summary>
    /// Sends <see cref="StartCommands"/> as ONE console text, its lines split
    /// by newlines as stock's dialog sends them: a <c>wait</c> delays only the
    /// commands added in the same text (the command buffer's tick is per
    /// add), so sent line by line the waits delayed nothing and
    /// <c>maxplayers</c> ran before the old server was down ("Cannot change
    /// maxplayers while the server is running", seen live).
    /// </summary>
    public void Start()
    {
        IReadOnlyList<string> lines = StartCommands();
        if (lines.Count > 0)
            engine.SubmitConsoleLine(string.Join("\n", lines));
    }
}

using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Client.Setup;

/// <summary><c>ai_bot_reroll_on_map</c>: the server declares it; the page reads what is archived.</summary>
[EngineConVar(CreateServerModel.RerollConVar)]
public sealed class AiBotRerollOnMapConVar : BoolConVar { }

/// <summary>
/// The bots page's state and what Start runs for it (plan_hl2mp_ai §3.5):
/// preset bots at a difficulty stop and the custom bots, within the player
/// count the core's create-server window hands it, and the roster line. The
/// window runs the line after the server's size and before the map, so the
/// server reads the roster at the level's start; the server judges every
/// bot again.
/// </summary>
[SideService(Side.Client, As = [typeof(ICreateServerSource), typeof(ICreateServerActions)])]
public sealed class CreateServerModel(IFwdEngineClient engine, AiBotRerollOnMapConVar reroll, CustomBots customs, BotEditorModel editor)
    : ICreateServerSource, ICreateServerActions
{
    public const int PlayersMin = 2;

    /// <summary>The engine's player slots; the window's own slider bounds the count below this.</summary>
    public const int PlayersMax = 64;

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

    public int MaxPlayers { get; private set; } = 8;

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

    public string StartRefusalToken(int maxPlayers)
        => BotCount + customs.Count > Math.Clamp(maxPlayers, PlayersMin, PlayersMax) - 1 ? "#Ai_Start_TooMany" : "";

    /// <summary>The server's archived cvar, until the page's box has said otherwise.</summary>
    public bool NewBotsEachMap
    {
        get => _newBotsEachMap ??= reroll.Value;
        private set => _newBotsEachMap = value;
    }

    private bool? _newBotsEachMap;

    public const string RerollConVar = "ai_bot_reroll_on_map";

    public void SetNewBotsEachMap(bool on) => NewBotsEachMap = on;

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

    /// <summary>The roster line, which the window runs after <c>maxplayers</c> and before the map; none while refused.</summary>
    public void BeforeMap(List<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (StartRefusalToken(MaxPlayers).Length == 0)
        {
            // The server's roster is this page's now (§3.5): the last server's wanted bots go first, even with no bot here.
            lines.Add("ai_bot_kick all");
            lines.Add($"ai_bot_roster \"{RosterSpecText()}\"");
            lines.Add($"{RerollConVar} {(NewBotsEachMap ? 1 : 0)}");
        }
    }
}

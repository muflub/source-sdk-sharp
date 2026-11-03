namespace SourceSharp.Ai.Hl2mp.Ui;

// THE DATA THE BOT-SETUP SCREENS READ, AND WHAT THEY ASK FOR
// (docs/plans/plan_hl2mp_ai.md §3.5, Phase 5c). The same contract as
// src/client/interface/Ui/UiSources.cs: a screen never sees the engine, the
// AI core or the disk; index-and-count shapes, so reading them each frame
// allocates nothing; one interface per concern. The answers live in
// SourceSharp.Ai.Hl2mp.Client (the profile store, the pool, the commands);
// the samples answer them with fixed data.

/// <summary>One custom bot on the create-server list: its name ("" when the server is to draw one), its spend, and its style.</summary>
public readonly record struct CustomBotRow(
    string Name,              // ui.Data; "" for a bot the server names by its trait
    int Spent,                // points spent of the pool
    int Pool,                 // the pool (60)
    string StyleFirstToken,   // the style name's two skills, highest first (#Ai_Skill_*)
    string StyleSecondToken);

/// <summary>
/// The bots page's state (the core's create-server window holds the map and
/// the player count, and hands the page the count): the preset bots and the
/// custom ones.
/// </summary>
public interface ICreateServerSource
{
    /// <summary>The player count the page last had from the window.</summary>
    int MaxPlayers { get; }

    /// <summary>Bots at the difficulty slider's preset.</summary>
    int BotCount { get; }

    /// <summary>The most preset bots the player count leaves room for, beside the custom ones.</summary>
    int BotCountMax { get; }

    /// <summary>The difficulty slider's stop, 0..<see cref="DifficultyStops"/>-1.</summary>
    int Difficulty { get; }

    /// <summary>The slider's stops: Easy, Medium, Hard. Expert is a custom bot only.</summary>
    int DifficultyStops { get; }

    /// <summary>A stop's name (#Ai_Bots_Easy ...).</summary>
    string DifficultyToken(int stop);

    /// <summary>The points a stop's preset spends.</summary>
    int DifficultySpent(int stop);

    int CustomCount { get; }

    CustomBotRow Custom(int i);

    /// <summary>Whether every bot is drawn anew on each new level (<c>ai_bot_reroll_on_map</c>).</summary>
    bool NewBotsEachMap { get; }

    /// <summary>Why Start is refused with <paramref name="maxPlayers"/> slots (#Ai_Start_*), or "" when it is allowed.</summary>
    string StartRefusalToken(int maxPlayers);
}

/// <summary>What the bots page asks for.</summary>
public interface ICreateServerActions
{
    /// <summary>The window's player count, which bounds the bots.</summary>
    void SetMaxPlayers(int count);

    void SetBotCount(int count);

    void SetDifficulty(int stop);

    /// <summary>A new custom bot: opens the editor on it.</summary>
    void AddCustom();

    /// <summary>Opens the editor on custom bot <paramref name="i"/>.</summary>
    void EditCustom(int i);

    void RemoveCustom(int i);

    void SetNewBotsEachMap(bool on);

    /// <summary>The console lines Start runs for the bots, after the server's size and before the map (the roster).</summary>
    void BeforeMap(List<string> lines);
}

/// <summary>One skill row of the editor.</summary>
public readonly record struct BotSkillRow(
    string NameToken,         // #Ai_Skill_Accuracy ...
    int Level,                // 0..Cap
    bool CanRaise,            // below the cap and a point left
    bool CanLower,            // above 0
    BotParameter[] Parameters); // what the level makes of the bot's parameters, from the curves

/// <summary>
/// One parameter a skill's level sets, for the editor's row: its name as the
/// web inspector's, in PascalCase (<c>AimErrorDeg</c>, <c>ReactionMs</c>),
/// which the screen turns into the <c>#Ai_Param_&lt;Key&gt;</c> label with its
/// unit, and its value.
/// </summary>
public readonly record struct BotParameter(string Key, float Value);

/// <summary>One saved revision of the profile being edited.</summary>
public readonly record struct BotRevisionRow(
    int Number,
    string At,                // ui.Data: when, already formatted
    string Note,              // ui.Data: "first", "restored #3"
    int Spent);

/// <summary>A profile the editor can load: a built-in preset or one saved on disk.</summary>
public readonly record struct BotProfileChoice(string Name, int Spent, bool BuiltIn);

/// <summary>The editor's side panel.</summary>
public enum BotEditorPanel : byte
{
    None,
    Revisions,
    Load,
    SaveAs,
}

/// <summary>The bot editor's state: the profile being edited, its history, what can be loaded.</summary>
public interface IBotEditorSource
{
    /// <summary>The profile's name (ui.Data), "" while it has none.</summary>
    string ProfileName { get; }

    int Pool { get; }

    /// <summary>The most points one skill takes.</summary>
    int Cap { get; }

    int Spent { get; }

    int PointsLeft { get; }

    int SkillCount { get; }

    BotSkillRow Skill(int i);

    /// <summary>The style name's two skills, highest first (#Ai_Skill_*).</summary>
    string StyleFirstToken { get; }

    string StyleSecondToken { get; }

    bool CanUndo { get; }

    bool CanRedo { get; }

    /// <summary>Changed since it was last saved or loaded.</summary>
    bool Unsaved { get; }

    /// <summary>Whether Save writes now: the profile has a name of its own and fits the pool.</summary>
    bool CanSave { get; }

    BotEditorPanel Panel { get; }

    int RevisionCount { get; }

    /// <summary>Revision <paramref name="i"/>, newest first.</summary>
    BotRevisionRow Revision(int i);

    int ChoiceCount { get; }

    /// <summary>Choice <paramref name="i"/>: the built-in presets first, then the saved profiles by name.</summary>
    BotProfileChoice Choice(int i);

    /// <summary>The name typed into Save as.</summary>
    string NameDraft { get; }

    /// <summary>Why the draft cannot be a profile's name (#Ai_Name_*), or "".</summary>
    string NameRefusalToken { get; }

    /// <summary>The last save or load's message (#Ai_Status_*), or "".</summary>
    string StatusToken { get; }

    /// <summary>What the status names (ui.Data): a profile, or the store's refusal.</summary>
    string StatusData { get; }
}

/// <summary>What the bot editor asks for (the shape of the Descent sheet's actions).</summary>
public interface IBotEditorActions
{
    /// <summary>Moves skill <paramref name="skill"/> by <paramref name="delta"/> points, if the result fits the pool.</summary>
    void Stage(int skill, int delta);

    void Undo();

    void Redo();

    /// <summary>Writes the profile under its own name, appending a revision.</summary>
    void Save();

    /// <summary>Writes the profile under the name drafted in Save as.</summary>
    void SaveAs();

    void SetNameDraft(string name);

    void ShowPanel(BotEditorPanel panel);

    /// <summary>Loads choice <paramref name="choice"/> as the starting point (an undoable edit).</summary>
    void Load(int choice);

    /// <summary>Makes revision <paramref name="number"/> current and records "restored #n".</summary>
    void Restore(int number);

    /// <summary>Back to the create-server list, with this bot on it.</summary>
    void Done();
}

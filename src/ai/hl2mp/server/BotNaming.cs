using SourceSharp.Abi;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>A bot's skill as the scoreboard shows it beside its name (user ruling 2026-11-01, plan_hl2mp_ai §3.5.1).</summary>
public static class BotSkillLabel
{
    /// <summary>"(Easy)", "(Medium)" (the slider's name for <c>normal</c>), "(Hard)", "(Expert)", or "(Custom N)" for N points spent.</summary>
    public static string For(SkillProfile skills)
    {
        ArgumentNullException.ThrowIfNull(skills);
        if (SkillPresets.TryGet(skills.Name, out SkillProfile? preset) && preset!.Spent == skills.Spent)
        {
            return skills.Name switch
            {
                "easy" => "(Easy)",
                "normal" => "(Medium)",
                "hard" => "(Hard)",
                _ => "(Expert)",
            };
        }

        return $"(Custom {skills.Spent})";
    }
}

/// <summary>The pools a bot's name is drawn from, or why there are none.</summary>
public interface IBotNameSource
{
    BotNamePools? Pools { get; }

    string Refusal { get; }
}

/// <summary>
/// <see cref="IBotNameSource"/> over the game's files: <see cref="Path"/>,
/// read once through the engine's file seam when a name is first wanted.
/// </summary>
[SideService(Side.Server, As = [typeof(IBotNameSource)])]
public sealed class GameFileBotNames(IEngineFiles files) : IBotNameSource
{
    public const string Path = "scripts/ai/hl2mp_bot_names.json";

    private BotNamePools? _pools;
    private string? _refusal;

    public BotNamePools? Pools
    {
        get
        {
            if (_pools is null && _refusal is null)
                Load();
            return _pools;
        }
    }

    public string Refusal => _refusal ?? "";

    private void Load()
    {
        string? json = files.ReadGameFile(Path);
        if (json is null)
        {
            _refusal = $"no bot names: expected {Path}";
            return;
        }

        try
        {
            _pools = BotNamePools.Parse(json);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException)
        {
            _refusal = $"{Path} cannot be read: {e.Message}";
        }
    }
}

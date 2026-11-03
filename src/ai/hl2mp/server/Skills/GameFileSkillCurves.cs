using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Skills;

/// <summary>The skill curves the brains read (plan_hl2mp_ai §10), or why there are none.</summary>
public interface ISkillCurveSource
{
    SkillCurves? Curves { get; }

    string Refusal { get; }
}

/// <summary>
/// <see cref="ISkillCurveSource"/> over the game's files: <see cref="Path"/>,
/// read once through the engine's file seam on the main thread and handed to
/// the core as data.
/// </summary>
[SideService(Side.Server, As = [typeof(ISkillCurveSource)])]
public sealed class GameFileSkillCurves(IEngineFiles files) : ISkillCurveSource
{
    public const string Path = "scripts/ai/hl2mp_skills.json";

    private SkillCurves? _curves;
    private string? _refusal;

    public SkillCurves? Curves
    {
        get
        {
            if (_curves is null && _refusal is null)
                Load();
            return _curves;
        }
    }

    public string Refusal => _refusal ?? "";

    private void Load()
    {
        string? json = files.ReadGameFile(Path);
        if (json is null)
        {
            _refusal = $"no skill curves: expected {Path}";
            return;
        }

        try
        {
            _curves = SkillCurves.Parse(json);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            _refusal = $"{Path} cannot be read: {e.Message}";
        }
    }
}

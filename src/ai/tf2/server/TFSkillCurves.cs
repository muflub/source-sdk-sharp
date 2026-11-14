using SourceSharp.Abi;
using SourceSharp.Ai.Server.Skills;

namespace SourceSharp.Ai.TF2;

/// <summary>
/// TF2's <see cref="ISkillCurveSource"/> over the tf2 mod's files: <see cref="Path"/>, read once
/// through the engine's file seam on the main thread. Without curves a brain has no decision
/// parameters and so no decider: the session gives a TF2 bot its tree only through these. The
/// file holds one set of curves for every class until P7's per-class sections (plan C.5).
/// </summary>
[SideService(Side.Server, As = [typeof(ISkillCurveSource)])]
public sealed class TFSkillCurves(IEngineFiles files) : ISkillCurveSource
{
    public const string Path = "scripts/ai/tf2_skills.json";

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

    public SkillCurves? Probe(string parameter, int level, float value)
        => Curves is { } now ? _curves = now.With(parameter, level, value) : null;

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

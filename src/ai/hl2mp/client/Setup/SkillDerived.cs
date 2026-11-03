using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Client.Setup;

/// <summary>The skill curves on the client, read from the game's file through the engine's file seam, or none.</summary>
[SideService(Side.Client)]
public sealed class ClientSkillCurves(IEngineFiles files)
{
    public const string Path = "scripts/ai/hl2mp_skills.json";

    private SkillCurves? _curves;
    private bool _read;

    /// <summary>The curves, or null when the file is missing or wrong (the editor then shows no parameters).</summary>
    public SkillCurves? Curves
    {
        get
        {
            if (_read)
                return _curves;
            _read = true;
            try
            {
                _curves = files.ReadGameFile(Path) is { } json ? SkillCurves.Parse(json) : null;
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
            {
                _curves = null;
            }

            return _curves;
        }
    }
}

/// <summary>
/// What a skill's level makes of a bot's parameters, for the editor's rows
/// (plan_hl2mp_ai §3.5): the numbers the web inspector shows
/// (<see cref="BotInspect.Skills"/>), each parameter under the skill that
/// drives it: the curve's own skill, or, for the vision, memory and motion
/// numbers, Alertness and Tenacity.
/// </summary>
public static class SkillDerived
{
    /// <summary>The skill key a non-curve parameter of the inspector belongs to, or null (the overall turn speed belongs to none).</summary>
    public static string? SkillOf(string parameter) => parameter switch
    {
        "recognize_s" or "sight_range" or "fov_deg" => "alertness",
        "memory_half_life_s" or "habit_half_life_s" => "tenacity",
        _ => null,
    };

    /// <summary>
    /// Each skill's parameters, in the profile's order, by PascalCase key
    /// (<see cref="Key"/>) in key order: the editor's rows label them from the
    /// language file; the web inspector keeps the raw names.
    /// </summary>
    public static BotParameter[][] Parameters(SkillProfile profile, SkillCurves? curves)
    {
        ArgumentNullException.ThrowIfNull(profile);
        InspectSkills inspect = BotInspect.Skills(profile, StandardSkills.Pool, StandardSkills.Cap, curves?.For(profile));
        var rows = new BotParameter[profile.Skills.Length][];
        for (int i = 0; i < rows.Length; i++)
        {
            string skill = profile.Skills[i].Key;
            rows[i] = [.. inspect.Derived
                .Where(p => (SkillOf(p.Key) ?? CurveSkill(curves, p.Key)) == skill)
                .Select(p => new BotParameter(Key(p.Key), p.Value))
                .OrderBy(p => p.Key, StringComparer.Ordinal)];
        }

        return rows;
    }

    /// <summary>An inspector name in PascalCase: <c>fov_deg</c> is <c>FovDeg</c>, <c>AimErrorDeg</c> stays.</summary>
    public static string Key(string parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var key = new System.Text.StringBuilder(parameter.Length);
        bool upper = true;
        foreach (char c in parameter)
        {
            if (c == '_')
            {
                upper = true;
                continue;
            }

            key.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return key.ToString();
    }

    private static string? CurveSkill(SkillCurves? curves, string parameter)
    {
        if (curves is null)
            return null;
        string name = char.ToLowerInvariant(parameter[0]) + parameter[1..];
        foreach (SkillCurves.Curve c in curves.All)
        {
            if (string.Equals(c.Parameter, name, StringComparison.Ordinal))
                return c.Skill;
        }

        return null;
    }
}

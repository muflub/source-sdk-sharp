using System.Collections.Immutable;
using System.Text.Json;

namespace SourceSharp.Ai;

/// <summary>
/// The skill curves (plan_hl2mp_ai §10): each parameter the brain reads is a
/// value per level 0..10 of one skill, read from the game's
/// <c>scripts/ai/hl2mp_skills.json</c> (handed in as text by the binding) and
/// declared as rising or falling with its level. A profile's
/// <see cref="DecisionParams"/> are each parameter at the profile's level of
/// its skill.
/// </summary>
public sealed class SkillCurves
{
    public const int Format = 1;

    /// <summary>The parameters the brain needs; a file without one of them is refused by name.</summary>
    public static readonly ImmutableArray<string> Required =
    [
        "aimErrorDeg", "leadQuality", "glanceRate", "preferredRangeScale", "chaseSeconds", "coverWeight", "reloadBelowPct",
        "searchSeconds", "ambushWeight", "routeAltWeight", "fireRangeGate", "pushTrust", "pushMarginSeconds", "strafeWeight",
        "strafeMeanSeconds", "retreatHealthPct", "rankBonus", "pathCapMultiplier", "easyTargetDiscount",
        "reactionMs", "trackingLagMs", "flickOvershootPct",
    ];

    /// <summary>One parameter's curve.</summary>
    public sealed record Curve(string Parameter, string Skill, bool Rises, ImmutableArray<float> Levels)
    {
        public float At(int level) => Levels[Math.Clamp(level, 0, Levels.Length - 1)];
    }

    private readonly Dictionary<string, Curve> _curves;

    private SkillCurves(Dictionary<string, Curve> curves, string json)
    {
        _curves = curves;
        Json = json;
    }

    /// <summary>The text these were read from, for a recording's world (§2.6).</summary>
    public string Json { get; }

    public IReadOnlyCollection<Curve> All => _curves.Values;

    public Curve this[string parameter] => _curves[parameter];

    /// <summary>
    /// Reads the file's text; a wrong format, an unknown skill, a curve that
    /// is not 11 levels, a missing parameter or a curve that does not move
    /// the way it says is refused by name (<see cref="InvalidDataException"/>).
    /// </summary>
    public static SkillCurves Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("format", out JsonElement f) || f.GetInt32() != Format)
            throw new InvalidDataException($"skill curves: format {Format} expected");
        var curves = new Dictionary<string, Curve>(StringComparer.Ordinal);
        foreach (JsonProperty p in root.GetProperty("parameters").EnumerateObject())
        {
            string skill = p.Value.GetProperty("skill").GetString() ?? "";
            if (!StandardSkills.Ten.Any(s => s.Key == skill))
                throw new InvalidDataException($"skill curves: '{p.Name}' names no skill '{skill}'");
            string direction = p.Value.GetProperty("direction").GetString() ?? "";
            if (direction is not ("up" or "down"))
                throw new InvalidDataException($"skill curves: '{p.Name}' goes '{direction}', not up or down");
            ImmutableArray<float> levels = [.. p.Value.GetProperty("levels").EnumerateArray().Select(v => v.GetSingle())];
            if (levels.Length != StandardSkills.Cap + 1)
                throw new InvalidDataException($"skill curves: '{p.Name}' has {levels.Length} levels, not {StandardSkills.Cap + 1}");
            var curve = new Curve(p.Name, skill, direction == "up", levels);
            if (!Moves(curve))
                throw new InvalidDataException($"skill curves: '{p.Name}' does not go {direction} with {skill}");
            curves[p.Name] = curve;
        }

        foreach (string r in Required)
        {
            if (!curves.ContainsKey(r))
                throw new InvalidDataException($"skill curves: no '{r}'");
        }

        return new SkillCurves(curves, json);
    }

    /// <summary>Whether a curve never goes against its direction and ends apart from where it starts (a knob that moves).</summary>
    public static bool Moves(Curve c)
    {
        for (int i = 1; i < c.Levels.Length; i++)
        {
            if (c.Rises ? c.Levels[i] < c.Levels[i - 1] : c.Levels[i] > c.Levels[i - 1])
                return false;
        }

        return c.Rises ? c.Levels[^1] > c.Levels[0] : c.Levels[^1] < c.Levels[0];
    }

    /// <summary>
    /// These curves with <paramref name="parameter"/> at <paramref name="value"/>
    /// at <paramref name="level"/> (the aim range's probes, <c>ai_curve</c>);
    /// the levels either side give way as far as the curve's direction needs,
    /// so what comes back still parses.
    /// </summary>
    public SkillCurves With(string parameter, int level, float value)
    {
        Curve c = _curves[parameter];
        if (level < 0 || level >= c.Levels.Length)
            throw new ArgumentOutOfRangeException(nameof(level), level, $"levels run 0..{c.Levels.Length - 1}");
        System.Text.Json.Nodes.JsonNode root = System.Text.Json.Nodes.JsonNode.Parse(Json)!;
        System.Text.Json.Nodes.JsonArray levels = root["parameters"]![parameter]!["levels"]!.AsArray();
        for (int i = 0; i < levels.Count; i++)
        {
            float v = c.Levels[i];
            if (i == level)
                v = value;
            else if (c.Rises == (i > level))
                v = Math.Max(v, value);
            else
                v = Math.Min(v, value);
            levels[i] = v;
        }

        return Parse(root.ToJsonString());
    }

    /// <summary>
    /// The presets' hit rates as ratios of Medium's (§10, ruling 2026-11-02):
    /// <c>hitRateRatios</c> in the file, by preset. Medium's is 1 by definition
    /// and the rest rise with the preset, or the file is refused by name.
    /// </summary>
    public static ImmutableArray<(string Preset, double Ratio)> HitRateRatios(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("hitRateRatios", out JsonElement r))
            throw new InvalidDataException("skill curves: no 'hitRateRatios'");
        (string Preset, double Ratio)[] list = [.. new[] { "easy", "normal", "hard", "expert" }.Select(p =>
            r.TryGetProperty(p, out JsonElement v) ? (p, v.GetDouble()) : throw new InvalidDataException($"skill curves: hitRateRatios has no '{p}'"))];
        if (list[1].Ratio != 1.0)
            throw new InvalidDataException("skill curves: hitRateRatios' normal must be 1: the others are relative to it");
        for (int i = 1; i < list.Length; i++)
        {
            if (list[i].Ratio <= list[i - 1].Ratio)
                throw new InvalidDataException($"skill curves: hitRateRatios' {list[i].Preset} is not above {list[i - 1].Preset}");
        }

        return [.. list];
    }

    /// <summary>The parameters at a profile's levels.</summary>
    public DecisionParams For(SkillProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        float V(string p)
        {
            Curve c = _curves[p];
            int i = profile.IndexOf(c.Skill);
            return c.At(i < 0 ? 0 : profile.Levels[i]);
        }

        return new DecisionParams(V("aimErrorDeg"), V("leadQuality"), V("glanceRate"), V("preferredRangeScale"), V("chaseSeconds"),
                                  V("coverWeight"), V("reloadBelowPct"), V("searchSeconds"), V("ambushWeight"), V("routeAltWeight"),
                                  V("fireRangeGate"), V("pushTrust"), V("pushMarginSeconds"), V("strafeWeight"), V("strafeMeanSeconds"),
                                  V("retreatHealthPct"), V("rankBonus"), V("pathCapMultiplier"), V("easyTargetDiscount"),
                                  V("reactionMs"), V("trackingLagMs"), V("flickOvershootPct"));
    }
}

/// <summary>What a bot's skills make of its decisions (§10), each from its curve at the bot's level.</summary>
public readonly record struct DecisionParams(
    float AimErrorDeg,
    float LeadQuality,
    float GlanceRate,
    float PreferredRangeScale,
    float ChaseSeconds,
    float CoverWeight,
    float ReloadBelowPct,
    float SearchSeconds,
    float AmbushWeight,
    float RouteAltWeight,
    float FireRangeGate,
    float PushTrust,
    float PushMarginSeconds,
    float StrafeWeight,
    float StrafeMeanSeconds,
    float RetreatHealthPct,
    float RankBonus,
    float PathCapMultiplier,
    float EasyTargetDiscount,
    float ReactionMs,
    float TrackingLagMs,
    float FlickOvershootPct);

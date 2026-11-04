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
        "reactionMs", "trackingLagMs", "flickOvershootPct", "grudgeWeight",
        "prefireChance", "prefireThrift", "prefireBurstSeconds", "giveUpHealth", "crouchShare", "recoilControl", "burstSeconds", "recoilSettleDeg", "altFireChance", "aimRise", "fragChance", "fragKnack", "fragAimDeg", "fragCareless",
    ];

    /// <summary>
    /// The style level of a skill at its own mean. Spending points on favourites puts
    /// the rest about half a spread under the mean, so a bot's other skills read about
    /// 3.25, where Medium's levels 3 and 4 are: a favourite stands out against a bot
    /// that otherwise plays as Medium does, at any difficulty.
    /// </summary>
    public const float StyleMid = 4.5f;

    /// <summary>How many style levels one of the bot's own spreads above its mean is worth.</summary>
    public const float StyleGain = 2.5f;

    /// <summary>
    /// How strong a style a skill makes (§10, ruling 2026-11-02: "Can fix bot
    /// strongest trait to show at lower difficulty"): how far it sits above the bot's
    /// own mean, in the bot's own spread (the standard deviation of its levels, at
    /// least 1), not how high it is, so a favourite shows at any difficulty. Easy's
    /// favourite is 2.7 levels up and Hard's 4.3 (§10's spread), and each stands about
    /// two of its own spreads clear of its mean.
    /// </summary>
    public static float StyleLevel(int level, float mean, float spread) => Math.Clamp(StyleMid + StyleGain * (level - mean) / Math.Max(1f, spread), 0f, StandardSkills.Cap);

    /// <summary>A profile's levels' mean and standard deviation.</summary>
    public static (float Mean, float Spread) OwnSpread(SkillProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int n = profile.Levels.Length;
        if (n == 0)
            return (0f, 0f);
        float mean = (float)profile.Levels.Sum() / n, sum = 0f;
        foreach (int l in profile.Levels)
            sum += (l - mean) * (l - mean);
        return (mean, MathF.Sqrt(sum / n));
    }

    /// <summary>One parameter's curve.</summary>
    public sealed record Curve(string Parameter, string Skill, bool Rises, ImmutableArray<float> Levels, bool Style = false)
    {
        public float At(int level) => Levels[Math.Clamp(level, 0, Levels.Length - 1)];

        /// <summary>The curve between its levels.</summary>
        public float At(float level)
        {
            float l = Math.Clamp(level, 0f, Levels.Length - 1);
            int below = (int)MathF.Floor(l), above = Math.Min(below + 1, Levels.Length - 1);
            return Levels[below] + (Levels[above] - Levels[below]) * (l - below);
        }
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
            string reads = p.Value.TryGetProperty("reads", out JsonElement r) ? r.GetString() ?? "" : "level";
            if (reads is not ("level" or "style"))
                throw new InvalidDataException($"skill curves: '{p.Name}' reads '{reads}', not level or style");
            var curve = new Curve(p.Name, skill, direction == "up", levels, reads == "style");
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

    /// <summary>
    /// How far a hot streak lifts a level (§5.5, ruling 2026-11-02: "some times bad bots become
    /// great for a very short stint"), and the level it stops at: the most at the bottom,
    /// next to nothing near the top, and never to Expert's (Accuracy 8, Alertness 7).
    /// </summary>
    public const float HotLift = 3f, HotCap = 6.5f;

    /// <summary>How far a cold spell drops a level: the most at the top, next to nothing at the bottom.</summary>
    public const float ColdDrop = 1.2f;

    /// <summary>A level on a hot streak: Easy's Accuracy 2 reads about 3.9, near Medium's 4; one at the cap or over it stays.</summary>
    public static float HotLevel(float level)
    {
        float gap = 1f - level / StandardSkills.Cap;
        return MathF.Max(level, MathF.Min(level + HotLift * gap * gap, HotCap));
    }

    /// <summary>A level on a cold spell.</summary>
    public static float ColdLevel(float level)
    {
        float top = level / StandardSkills.Cap;
        return MathF.Max(0f, level - ColdDrop * top * top);
    }

    /// <summary>What a streak changes, at a shift of the bot's own levels: only these (§5.5). A turn's limits stay a person's.</summary>
    private StreakParams Streak(SkillProfile profile, Func<float, float> shift)
    {
        float Level(string parameter, out Curve curve)
        {
            curve = _curves[parameter];
            int i = profile.IndexOf(curve.Skill);
            return shift(i < 0 ? 0 : profile.Levels[i]);
        }

        float error = Level("aimErrorDeg", out Curve errors), lead = Level("leadQuality", out Curve leads), reaction = Level("reactionMs", out Curve reactions);
        return new StreakParams(errors.At(error), leads.At(lead), reactions.At(reaction), VisionCurves.RecognizeAt(reaction));
    }

    /// <summary>From this spend (Hard's) a grenade is never thrown at what is right in front of it, whatever one skill says (§5.4).</summary>
    public const int CarefulFromPoints = 50;

    /// <summary>The parameters at a profile's levels.</summary>
    public DecisionParams For(SkillProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        (float mean, float spread) = OwnSpread(profile);
        float V(string p)
        {
            Curve c = _curves[p];
            int i = profile.IndexOf(c.Skill);
            int level = i < 0 ? 0 : profile.Levels[i];
            return c.Style ? c.At(StyleLevel(level, mean, spread)) : c.At(level);
        }

        return new DecisionParams(V("aimErrorDeg"), V("leadQuality"), V("glanceRate"), V("preferredRangeScale"), V("chaseSeconds"),
                                  V("coverWeight"), V("reloadBelowPct"), V("searchSeconds"), V("ambushWeight"), V("routeAltWeight"),
                                  V("fireRangeGate"), V("pushTrust"), V("pushMarginSeconds"), V("strafeWeight"), V("strafeMeanSeconds"),
                                  V("retreatHealthPct"), V("rankBonus"), V("pathCapMultiplier"), V("easyTargetDiscount"),
                                  V("reactionMs"), V("trackingLagMs"), V("flickOvershootPct"), V("grudgeWeight"),
                                  V("prefireChance"), V("prefireThrift"), V("prefireBurstSeconds"), V("giveUpHealth"), V("crouchShare"), V("recoilControl"), V("burstSeconds"), V("recoilSettleDeg"),
                                  Streak(profile, HotLevel), Streak(profile, ColdLevel), VisionCurves.For(profile).RecognizeSeconds, V("altFireChance"), V("aimRise"),
                                  V("fragChance"), V("fragKnack"), V("fragAimDeg"), profile.Spent >= CarefulFromPoints ? 0f : V("fragCareless"));
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
    float FlickOvershootPct,
    float GrudgeWeight = 0f,
    float PrefireChance = 0f,
    float PrefireThrift = 1f,
    float PrefireBurstSeconds = 0f,
    float GiveUpHealth = 0f,
    float CrouchShare = 0f,
    float RecoilControl = 0f,
    float BurstSeconds = 0f,
    float RecoilSettleDeg = 0f,
    StreakParams Hot = default,
    StreakParams Cold = default,
    float RecognizeSeconds = 0f,
    float AltFireChance = 0f,
    float AimRise = 0f,
    float FragChance = 0f,
    float FragKnack = 0f,
    float FragAimDeg = 0f,
    float FragCareless = 0f);

/// <summary>What a streak changes while it lasts (§5.5): the aim's error and lead, the reaction, the recognition.</summary>
public readonly record struct StreakParams(float AimErrorDeg, float LeadQuality, float ReactionMs, float RecognizeSeconds);

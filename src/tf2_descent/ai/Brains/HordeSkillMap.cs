using System.Collections.Immutable;
using SourceSharp.Ai;

namespace TF2.Descent.Ai.Brains;

/// <summary>
/// Descent's temperament (plan_tf2_rpg B.11: ten skills, each 0 to 10, in
/// robot_rules.json's order) as the AI core's eleven skill levels
/// (<see cref="StandardSkills.Eleven"/>), which is what an <see cref="AiSession"/>
/// brain is added with (plan_descent_logic 3.6: a brain holds without a word
/// unless it has ten or eleven levels and its session has curves).
///
/// <list type="bullet">
/// <item>Accuracy, Alertness, Aggression, Cover, Tenacity, Cunning, Discipline,
/// Agility and Nerve: the same skill, by name.</item>
/// <item>Teamwork: <c>10 - Soloness</c> (B.11's Soloness is glued-to-the-pack at 0,
/// a lone flanker at 10; the core's Teamwork is the reverse).</item>
/// <item>Headhunter: 0. Descent has no such skill; it drives only the core's
/// rankBonus, pathCapMultiplier and easyTargetDiscount, which no horde decider reads.</item>
/// </list>
///
/// The curves the levels go through are <see cref="CurvesResource"/>, embedded
/// in this assembly (each parameter reads the level, never the core's style).
/// </summary>
public sealed class HordeSkillMap
{
    /// <summary>The embedded skill curves' resource name.</summary>
    public const string CurvesResource = "TF2.Descent.Ai.Brains.descent_ai_skills.json";

    /// <summary>The source of a core skill that Descent does not have (Headhunter): level 0.</summary>
    public const int None = -1;

    /// <summary>The source of Teamwork: <c>Cap - Soloness</c>.</summary>
    public const int InverseSoloness = -2;

    private readonly int[] _source;   // by core skill (Eleven's order): Descent's index, None or InverseSoloness
    private readonly int _soloness;

    public HordeSkillMap(int[] source, int soloness)
    {
        _source = source;
        _soloness = soloness;
    }

    /// <summary>The map for Descent's skills in <paramref name="descentSkills"/>' order (robot_rules.json's ids). Throws naming a core skill Descent lacks a source for.</summary>
    public static HordeSkillMap For(IReadOnlyList<string> descentSkills)
    {
        ArgumentNullException.ThrowIfNull(descentSkills);
        int Index(string key)
        {
            for (int i = 0; i < descentSkills.Count; i++)
                if (descentSkills[i] == key)
                    return i;
            return None;
        }

        int soloness = Index("soloness");
        if (soloness == None)
            throw new ArgumentException("Descent's skills have no soloness, which Teamwork is the inverse of", nameof(descentSkills));

        var eleven = StandardSkills.Eleven;
        var source = new int[eleven.Length];
        for (int k = 0; k < eleven.Length; k++)
        {
            string key = eleven[k].Key;
            source[k] = key == StandardSkills.Teamwork.Key ? InverseSoloness
                : key == "headhunter" ? None
                : Index(key) is var i && i != None ? i
                : throw new ArgumentException($"Descent's skills have no '{key}'", nameof(descentSkills));
        }

        return new HordeSkillMap(source, soloness);
    }

    /// <summary>How many levels a brain is added with: the core's eleven.</summary>
    public int Count => _source.Length;

    /// <summary>Where core skill <paramref name="coreIndex"/> comes from: a Descent index, <see cref="None"/> or <see cref="InverseSoloness"/>.</summary>
    public int SourceOf(int coreIndex) => _source[coreIndex];

    /// <summary>Writes the eleven levels of a temperament's <paramref name="levels"/> (Descent's order) into <paramref name="eleven"/>.</summary>
    public void Fill(ImmutableArray<int> levels, Span<byte> eleven)
    {
        if (eleven.Length != _source.Length)
            throw new ArgumentException($"{_source.Length} levels are written, not {eleven.Length}", nameof(eleven));
        for (int k = 0; k < _source.Length; k++)
        {
            int s = _source[k];
            int level = s == None ? 0
                : s == InverseSoloness ? StandardSkills.Cap - levels[_soloness]
                : levels[s];
            eleven[k] = (byte)Math.Clamp(level, 0, StandardSkills.Cap);
        }
    }

    /// <summary>The levels as a profile over the eleven, for reading the curves on the main thread.</summary>
    public SkillProfile Profile(ImmutableArray<int> levels)
    {
        Span<byte> eleven = stackalloc byte[_source.Length];
        Fill(levels, eleven);
        var b = ImmutableArray.CreateBuilder<int>(eleven.Length);
        foreach (byte l in eleven)
            b.Add(l);
        return new SkillProfile("descent", StandardSkills.Eleven, b.MoveToImmutable());
    }

    /// <summary>The curves text embedded in this assembly.</summary>
    public static string CurvesJson()
    {
        using Stream s = typeof(HordeSkillMap).Assembly.GetManifestResourceStream(CurvesResource)
                         ?? throw new InvalidOperationException($"{CurvesResource} is not embedded in {typeof(HordeSkillMap).Assembly.GetName().Name}");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>The embedded curves, parsed (refused by name when one is missing or does not move as it says).</summary>
    public static SkillCurves Curves() => SkillCurves.Parse(CurvesJson());

    /// <summary>A Descent parameter the core does not know (the senses' scales), at a profile's level of its skill.</summary>
    public static float Extra(SkillCurves curves, SkillProfile profile, string parameter)
    {
        ArgumentNullException.ThrowIfNull(curves);
        ArgumentNullException.ThrowIfNull(profile);
        var c = curves[parameter];
        int i = profile.IndexOf(c.Skill);
        return c.At(i < 0 ? 0 : profile.Levels[i]);
    }
}

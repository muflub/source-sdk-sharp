using System.Globalization;
using System.Text;

namespace SourceSharp.Ai.Hl2mp.Trees;

/// <summary>Who holds a place among one difficulty's drawn bots, as shares: the ones named for it, the rest, and everyone.</summary>
public readonly record struct StyleShares(int Campers, double CampersHold, double CampersOverHunt, double RestHold, double RestOverHunt, double EveryoneHold, double EveryoneOverHunt);

/// <summary>
/// What §10's spread and style make of a difficulty's bots (plan_hl2mp_ai §10,
/// §12.3, <c>ai-replay style</c>): over seeded draws of a preset, the share that would
/// hold a place with nothing to hunt, and over a hunt, with those named for camping
/// (their name's skill is a high Cover or Cunning or a low Aggression) apart.
/// </summary>
public static class Hl2mpStyleSurvey
{
    /// <summary>The draws a survey reads, and the seed they come from (§10's table's).</summary>
    public const int Draws = 500;

    public const ulong Seed = 0x5EED0001;

    public static bool HoldsWithNothingToHunt(in DecisionParams p) => Hl2mpStyle.Camp(in p) >= Hl2mpStyle.CampFrom;

    /// <summary>Over a hunt with nothing to add to it, by the commitment's margin too.</summary>
    public static bool HoldsOverAHunt(in DecisionParams p) => Hl2mpStyle.Planned(Hl2mpStyle.Camp(in p)) > Hl2mpStyle.Hunt(in p) + 0.1f;

    public static bool NamedForCamping(SkillProfile p, ulong seed)
    {
        BotTrait t = BotNamer.Trait(p, seed);
        string skill = StandardSkills.Ten[t.Skill].Key;
        return (t.High && skill is "cover" or "cunning") || (!t.High && skill == "aggression");
    }

    public static StyleShares Of(SkillCurves curves, SkillProfile preset)
    {
        ArgumentNullException.ThrowIfNull(curves);
        ArgumentNullException.ThrowIfNull(preset);
        int campers = 0, holdC = 0, overC = 0, holdR = 0, overR = 0;
        for (int i = 0; i < Draws; i++)
        {
            ulong seed = AiRandom.SeedFor(Seed, i);
            SkillProfile p = SkillSpread.Draw(preset, seed);
            DecisionParams d = curves.For(p);
            bool hold = HoldsWithNothingToHunt(in d), over = HoldsOverAHunt(in d);
            if (NamedForCamping(p, seed))
            {
                campers++;
                holdC += hold ? 1 : 0;
                overC += over ? 1 : 0;
            }
            else
            {
                holdR += hold ? 1 : 0;
                overR += over ? 1 : 0;
            }
        }

        int rest = Draws - campers;
        return new StyleShares(campers, holdC / (double)Math.Max(1, campers), overC / (double)Math.Max(1, campers), holdR / (double)Math.Max(1, rest), overR / (double)Math.Max(1, rest),
                               (holdC + holdR) / (double)Draws, (overC + overR) / (double)Draws);
    }

    /// <summary>What a name's skill does: among one difficulty's bots, those named by it high and low against everyone, on the curve its phrases are about.</summary>
    public static string Names(SkillCurves curves, string preset, SkillProfile mean)
    {
        ArgumentNullException.ThrowIfNull(curves);
        (string Skill, string Curve, Func<DecisionParams, float> Of)[] promises =
        [
            ("accuracy", "aimErrorDeg", p => p.AimErrorDeg),
            ("alertness", "glanceRate", p => p.GlanceRate),
            ("aggression", "chaseSeconds", p => p.ChaseSeconds),
            ("cover", "coverWeight", p => p.CoverWeight),
            ("tenacity", "giveUpHealth", p => p.GiveUpHealth),
            ("cunning", "routeAltWeight", p => p.RouteAltWeight),
            ("discipline", "prefireThrift", p => p.PrefireThrift),
            ("agility", "strafeWeight", p => p.StrafeWeight),
            ("nerve", "retreatHealthPct", p => p.RetreatHealthPct),
            ("headhunter", "rankBonus", p => p.RankBonus),
        ];
        var bots = new (DecisionParams P, BotTrait T)[Draws];
        for (int i = 0; i < Draws; i++)
        {
            ulong seed = AiRandom.SeedFor(Seed, i);
            SkillProfile p = SkillSpread.Draw(mean, seed);
            bots[i] = (curves.For(p), BotNamer.Trait(p, seed));
        }

        var sb = new StringBuilder();
        for (int s = 0; s < promises.Length; s++)
        {
            (string skill, string curve, Func<DecisionParams, float> of) = promises[s];
            int index = Array.FindIndex(StandardSkills.Ten.ToArray(), k => k.Key == skill);
            float[] high = [.. bots.Where(b => b.T.Skill == index && b.T.High).Select(b => of(b.P))], low = [.. bots.Where(b => b.T.Skill == index && !b.T.High).Select(b => of(b.P))];
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{preset,-7} {skill,-11} {curve,-17} everyone {bots.Average(b => of(b.P)),6:F2}; named high {high.Length,3}: {(high.Length > 0 ? high.Average() : float.NaN),6:F2}; named low {low.Length,3}: {(low.Length > 0 ? low.Average() : float.NaN),6:F2}"));
        }

        return sb.ToString();
    }
}

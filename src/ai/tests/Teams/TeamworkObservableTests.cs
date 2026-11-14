using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// Teamwork's observable (plan_bots_teams_tf2 B.8, "teamwork is monotonic"), simulated inline as
/// the ten skills' are: a bot that walks where its brain says, a teammate in sight 1,000 u away,
/// nobody else; the mean distance between them over a minute falls as Teamwork rises.
/// </summary>
public sealed class TeamworkObservableTests
{
    private static readonly ActorId Mate = Duel.Other;

    private static readonly AiVector MateAt = new(1000f, 0f, 0f);

    /// <summary>One decide's walk at the run speed: six ticks of 15 ms.</summary>
    private const float StepUnits = 190f * 6 * 0.015f;

    private static double MeanDistance(int teamwork)
    {
        SkillPresets.TryGet("normal", out SkillProfile? p);
        var d = new Duel(new SkillProfile("tw", StandardSkills.Eleven, [.. p!.Levels, teamwork]));
        AiVector at = AiVector.Zero;
        d.StepTeams(d.Self() with { Team = 2 }, [(Mate, MateAt, 2)], [AiEvent.ActorTeam(Duel.Bot, new Tick(d.Tick + 6), Mate, 2, 0), .. d.SeeAt(Mate, MateAt)]);
        double sum = 0;
        const int Decides = 660;
        for (int k = 0; k < Decides; k++)
        {
            IntentCommand i = d.StepTeams(d.Self() with { Origin = at, Team = 2 }, [(Mate, MateAt, 2)]);
            if (i.Move.CornerCount > 0)
            {
                AiVector to = new(i.Move.Corner0.X - at.X, i.Move.Corner0.Y - at.Y, 0f);
                float len = to.Length;
                if (len > 1f)
                    at += to * (Math.Min(StepUnits, len) / len);
            }

            sum += at.DistanceTo(MateAt);
        }

        return sum / Decides;
    }

    [Fact]
    public void MoreTeamworkKeepsCloserToATeammate()
    {
        double low = MeanDistance(2), mid = MeanDistance(6), high = MeanDistance(10);
        Assert.True(low > mid && mid > high, $"mean distance to the teammate at Teamwork 2, 6, 10: {low:F0}, {mid:F0}, {high:F0}");
    }
}

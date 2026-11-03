using System.Text.Json;
using Xunit;

namespace SourceSharp.Ai.Tests.Scenario;

/// <summary>
/// ss_ai_routes' scenario (plan_hl2mp_ai §6.6, §8.2 "Headhunter passes a low
/// scorer to hunt the leader"), from its committed recording: the leader
/// takes two frags off the low scorer, a Headhunter 10 bot sees both, both
/// go out of its sight (the low scorer nearer it, the leader farther), and
/// its brain, let act, hunts the leader's believed place.
/// </summary>
public sealed class SsAiRoutesScenarioTests
{
    private static readonly Lazy<ScenarioRecording> Recording = new(() => ScenarioRecording.Load("ss_ai_routes"));

    private static int Slot(string who) => Recording.Value.Summary.GetProperty(who).GetInt32();

    private static JsonElement[] Thinks(string bot, string scene)
    {
        JsonElement s = Recording.Value.Summary.GetProperty("scenes").EnumerateArray().Single(e => e.GetProperty("name").GetString() == scene);
        int from = s.GetProperty("from").GetInt32(), to = s.GetProperty("to").GetInt32();
        return [.. Recording.Value.Summary.GetProperty("thinks").EnumerateArray()
            .Where(t => t.GetProperty("bot").GetString() == bot && t.GetProperty("tick").GetInt32() >= from && t.GetProperty("tick").GetInt32() <= to)];
    }

    /// <summary>The control: the scoreboard the Headhunter was shown ranks the leader first and the low scorer below it.</summary>
    [Fact]
    public void TheHeadhuntersScoreboardRanksTheLeaderFirst()
    {
        ScenarioRecording r = Recording.Value;
        AiEvent leader = r.Events.Last(e => e.Kind == AiEventKind.ScoreboardRow && e.Bot.Slot == Slot("hunter") && e.Other.Slot == Slot("leader"));
        AiEvent low = r.Events.Last(e => e.Kind == AiEventKind.ScoreboardRow && e.Bot.Slot == Slot("hunter") && e.Other.Slot == Slot("low"));
        Assert.Equal(1, (int)leader.Amount);
        Assert.True(low.Amount > leader.Amount, $"the low scorer ranks {low.Amount}");
    }

    [Fact]
    public void WithNobodyInSightTheHeadhunterHuntsTheFartherLeader()
    {
        JsonElement first = Assert.Single(Thinks("Hunter", "hunt").Take(1));
        Assert.Equal(("Hunt", Slot("leader")), (first.GetProperty("branch").GetString(), first.GetProperty("target").GetInt32()));
        float[] at = [.. first.GetProperty("at").EnumerateArray().Select(v => v.GetSingle())];
        float[] goal = [.. first.GetProperty("goal").EnumerateArray().Select(v => v.GetSingle())];
        Assert.True(goal[0] > at[0] + 400f, $"the goal ({goal[0]} {goal[1]}) is east, toward the leader, from ({at[0]} {at[1]})");
        // The low scorer was nearer: hidden at (64 96), the leader at (1444 100).
        Assert.True(Dist(at, 64f, 96f) < Dist(at, 1444f, 100f));
    }

    private static float Dist(float[] a, float x, float y) => MathF.Sqrt((a[0] - x) * (a[0] - x) + (a[1] - y) * (a[1] - y));
}

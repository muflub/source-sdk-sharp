using System.Text.Json;
using SourceSharp.Ai.Tests.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Scenario;

/// <summary>
/// ss_ai_duel's scenario (plan_hl2mp_ai §6.6, §8.2 "bot hears an SMG1 run
/// dry and pushes"), replayed from its committed recording: a Discipline 10
/// bot, its brain deciding and nobody acting, watches a target 150 u away
/// empty its SMG1's clip facing away until the gun reloads itself. Its
/// decisions push only in the reload's window, which the recipient truth
/// (the reload's sound) dates, and never while the clip still has rounds.
/// </summary>
public sealed class SsAiDuelScenarioTests
{
    private static readonly Lazy<ScenarioRecording> Recording = new(() => ScenarioRecording.Load("ss_ai_duel"));

    // The leaf: its sequence is called "push" too, and comes first.
    private static readonly int PushNode = Array.LastIndexOf([.. new Duel(Duel.Preset("hard")).Decider.NodeNames], "push");

    private const int ReloadTicks = 100;   // the SMG1's 1.5 s reload at 66 ticks a second

    [Fact]
    public void ThePushFallsInTheTargetsReloadWindowOnly()
    {
        ScenarioRecording r = Recording.Value;
        int pusher = r.Summary.GetProperty("pusher").GetInt32();
        int[] reloads = [.. r.Summary.GetProperty("reloads").EnumerateArray().Select(e => e.GetInt32())];
        Assert.True(PushNode >= 0, "the tree has a push leaf");
        Assert.NotEmpty(reloads);

        int[] pushes = [.. r.Intents.Where(i => i.Bot.Slot == pusher && i.Reason.NodeId == PushNode).Select(i => i.FromTick.Value)];
        Assert.True(pushes.Length >= 3, $"{pushes.Length} push decides");
        string[] outside = [.. pushes.Where(t => !reloads.Any(r0 => t >= r0 - 33 && t <= r0 + ReloadTicks)).Select(t => t.ToString())];
        Assert.True(outside.Length == 0, $"pushes outside the reload window (reload at {string.Join(",", reloads)}): {string.Join(",", outside)}");
    }

    [Fact]
    public void APushGoesForTheTargetAndCarriesTheAmmoTerm()
    {
        ScenarioRecording r = Recording.Value;
        int pusher = r.Summary.GetProperty("pusher").GetInt32();
        IntentCommand push = r.Intents.First(i => i.Bot.Slot == pusher && i.Reason.NodeId == PushNode);
        Assert.Equal(MoveKind.Follow, push.Move.Kind);
        Assert.InRange(push.Move.Corner0.X, 250f, 350f);
        Assert.True(push.Reason.AmmoTerm >= 0.6f, $"P(empty) + P(reloading) {push.Reason.AmmoTerm} against Discipline 10's trust of 0.6");
    }

    /// <summary>The control: while the clip still had rounds (the first three seconds of fire) the bot never pushed.</summary>
    [Fact]
    public void NoPushWhileTheClipHadRounds()
    {
        ScenarioRecording r = Recording.Value;
        int pusher = r.Summary.GetProperty("pusher").GetInt32();
        JsonElement fires = r.Summary.GetProperty("scenes").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "target fires");
        int from = fires.GetProperty("from").GetInt32();
        Assert.DoesNotContain(r.Intents, i => i.Bot.Slot == pusher && i.Reason.NodeId == PushNode && i.FromTick.Value < from + 200);
        Assert.Contains(r.Intents, i => i.Bot.Slot == pusher && i.FromTick.Value < from + 200 && i.FromTick.Value > from);
    }
}

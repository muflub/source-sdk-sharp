using System.Text.Json;
using Xunit;

namespace SourceSharp.Ai.Tests.Scenario;

/// <summary>
/// ss_ai_sight's scenario (plan_hl2mp_ai §6.6), replayed from its committed
/// recording: a watcher at a post, a target placed past each occluder in
/// turn (a wall, the window, another player, the closed door, the stacked
/// crates). Over each scene's last second the watcher's view holds the
/// target exactly where the scene says it is seen; any deviation is red.
/// </summary>
public sealed class SsAiSightScenarioTests
{
    private static readonly Lazy<ScenarioRecording> Recording = new(() => ScenarioRecording.Load("ss_ai_sight"));

    /// <summary>The scene's answer: how many of its last 66 views held the target, and the mean visible share then.</summary>
    private static (int Views, int Seen, float Fraction) Scene(JsonElement scene)
    {
        ScenarioRecording r = Recording.Value;
        int to = scene.GetProperty("to").GetInt32(), watcher = scene.GetProperty("watcher").GetInt32(), target = scene.GetProperty("target").GetInt32();
        int views = 0, seen = 0;
        float fraction = 0f;
        for (int t = to - 66; t <= to; t++)
        {
            if (!r.Views.TryGetValue((t, watcher), out ActorFacts[]? sighted))
                continue;
            views++;
            foreach (ActorFacts f in sighted.Where(f => f.Id.Slot == target))
            {
                seen++;
                fraction += f.VisibleFraction;
            }
        }

        return (views, seen, seen == 0 ? 0f : fraction / seen);
    }

    [Fact]
    public void EveryOccluderScenePassesAsTheMapSays()
    {
        var wrong = new List<string>();
        int scenes = 0;
        foreach (JsonElement s in Recording.Value.Summary.GetProperty("scenes").EnumerateArray())
        {
            scenes++;
            (int views, int seen, float fraction) = Scene(s);
            string name = s.GetProperty("name").GetString()!, expect = s.GetProperty("expect").GetString()!;
            if (views < 50)
                wrong.Add($"{name}: only {views} views recorded");
            else if (expect == "seen" && seen < views * 9 / 10)
                wrong.Add($"{name}: seen in {seen} of {views} views (expected seen)");
            else if (expect == "hidden" && seen > 0)
                wrong.Add($"{name}: seen in {seen} of {views} views at {fraction:F2} (expected hidden)");
        }

        Assert.True(scenes >= 7, $"{scenes} scenes");
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    [Fact]
    public void TheWindowShowsTheTargetInPartTheOpenFloorWhole()
    {
        JsonElement[] s = [.. Recording.Value.Summary.GetProperty("scenes").EnumerateArray()];
        float open = Scene(s.First(x => x.GetProperty("name").GetString() == "open")).Fraction;
        float window = Scene(s.First(x => x.GetProperty("name").GetString() == "window")).Fraction;
        Assert.Equal(1f, open, 2);
        Assert.InRange(window, 0.05f, 1f);
    }

    [Fact]
    public void ARecordingOfAnotherMapSourceIsRefused()
    {
        string json = File.ReadAllText(ScenarioRecording.Path("ss_ai_sight.json"));
        Assert.Contains("\"source_sha256\"", json);
        Assert.Throws<InvalidDataException>(() =>
        {
            string vmf = SourceSharp.Tests.RepoLayout.Repo("game", "mod_test", "maps", "src", "ai", "ss_ai_sight.vmf");
            string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([.. File.ReadAllBytes(vmf), (byte)' '])).ToLowerInvariant();
            if (sha != JsonDocument.Parse(json).RootElement.GetProperty("source_sha256").GetString())
                throw new InvalidDataException("a changed map source is not this recording's");
        });
    }
}

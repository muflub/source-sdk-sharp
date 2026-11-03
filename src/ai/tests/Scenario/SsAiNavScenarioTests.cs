using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace SourceSharp.Ai.Tests.Scenario;

/// <summary>
/// ss_ai_nav's scenario (plan_hl2mp_ai §6.6, Phase 5 step 6), from its
/// committed recording: one bot sent to every pickup in turn by ai_bot_goto,
/// across the step's drop, the ledges' jumps, the crawlspace, the door and
/// the ladder: it arrives at each, and climbs the ladder to the crossbow.
/// </summary>
public sealed partial class SsAiNavScenarioTests
{
    private static readonly Lazy<ScenarioRecording> Recording = new(() => ScenarioRecording.Load("ss_ai_nav"));

    [Fact]
    public void TheBotArrivesAtEveryPickupInTurn()
    {
        JsonElement[] scenes = [.. Recording.Value.Summary.GetProperty("scenes").EnumerateArray()];
        Assert.Equal(["weapon_shotgun", "weapon_smg1", "weapon_357", "item_battery", "weapon_crossbow"], scenes.Select(s => s.GetProperty("name").GetString()));
        string[] missed = [.. scenes.Where(s => !s.GetProperty("arrived").GetBoolean()).Select(s => s.GetProperty("name").GetString()!)];
        Assert.True(missed.Length == 0, "not arrived: " + string.Join(", ", missed));
    }

    [Fact]
    public void TheBotClimbsTheLadderToTheCrossbow()
    {
        string[] ladder = [.. Recording.Value.Summary.GetProperty("ladder").EnumerateArray().Select(e => e.GetString()!)];
        Assert.Contains(ladder, l => l.Contains("is on a ladder", StringComparison.Ordinal));
        Match off = ladder.Select(l => Climbed().Match(l)).Last(m => m.Success);
        Assert.True(float.Parse(off.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) >= 140f, off.Value);
    }

    [GeneratedRegex(@"got off the ladder at \(.*\), (-?\d+) u from where it got on")]
    private static partial Regex Climbed();
}

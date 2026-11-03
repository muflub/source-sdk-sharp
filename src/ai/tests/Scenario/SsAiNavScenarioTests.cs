using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace SourceSharp.Ai.Tests.Scenario;

/// <summary>
/// ss_ai_nav's scenario (plan_hl2mp_ai §6.6, Phase 5 step 6), from its
/// committed recording: one bot sent to every pickup in turn by ai_bot_goto,
/// across the step's drop, the ledges' jumps, the crawlspace, the door, the
/// ladder and the hand rail: it arrives at each, climbs the ladder to the
/// crossbow in one go, and crouch-jumps the rail to the ammo past it.
/// </summary>
public sealed partial class SsAiNavScenarioTests
{
    private static readonly Lazy<ScenarioRecording> Recording = new(() => ScenarioRecording.Load("ss_ai_nav"));

    [Fact]
    public void TheBotArrivesAtEveryPickupInTurn()
    {
        JsonElement[] scenes = [.. Recording.Value.Summary.GetProperty("scenes").EnumerateArray()];
        Assert.Equal(["weapon_shotgun", "weapon_smg1", "weapon_357", "item_battery", "weapon_crossbow", "item_ammo_crossbow"], scenes.Select(s => s.GetProperty("name").GetString()));
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

    /// <summary>
    /// One climb is one on and one off, at the top: live, the stall's +use let
    /// go mid-span and the bot flickered on and off the way up.
    /// </summary>
    [Fact]
    public void TheClimbIsOneOnAndOneOff()
    {
        string[] ladder = [.. Recording.Value.Summary.GetProperty("ladder").EnumerateArray().Select(e => e.GetString()!)];
        Assert.Equal(1, ladder.Count(l => l.Contains("is on a ladder", StringComparison.Ordinal)));
        Assert.Equal(1, ladder.Count(l => l.Contains("got off the ladder", StringComparison.Ordinal)));
    }

    /// <summary>The rail on the platform (top 40 u over it) is taken with a crouch-jump from the platform: a plain jump does not clear it.</summary>
    [Fact]
    public void TheBotCrouchJumpsTheRailToTheAmmoAndNothingHoldsIt()
    {
        JsonElement summary = Recording.Value.Summary;
        string[] jumps = [.. summary.GetProperty("jumps").EnumerateArray().Select(e => e.GetString()!)];
        Assert.Contains(jumps, l => l.Contains("crouch-jumps at", StringComparison.Ordinal) && l.Contains(" 176)", StringComparison.Ordinal));
        Assert.DoesNotContain(jumps, l => l.Contains(") jumps at", StringComparison.Ordinal) && l.Contains(" 176)", StringComparison.Ordinal));
        Assert.Empty(summary.GetProperty("stuck").EnumerateArray());
    }

    [GeneratedRegex(@"got off the ladder at \(.*\), (-?\d+) u from where it got on")]
    private static partial Regex Climbed();
}

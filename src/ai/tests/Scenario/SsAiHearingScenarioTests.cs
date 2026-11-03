using System.Text.Json;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Scenario;

/// <summary>
/// ss_ai_hearing's scenario (plan_hl2mp_ai §6.6), replayed from its committed
/// recording: a listener holds still while a target fires and reloads an
/// SMG1, a pistol and a 357 near it and an SMG1 in the far room. Every sound
/// the server emitted is in the summary with its recipient truth (logged by
/// the tap at emit time); the listener's heard events equal that truth, sound
/// for sound, and a 357's reload is never heard.
/// </summary>
public sealed class SsAiHearingScenarioTests
{
    private static readonly Lazy<ScenarioRecording> Recording = new(() => ScenarioRecording.Load("ss_ai_hearing"));

    private static readonly WeaponCatalog Weapons = WeaponCatalog.Stock();

    private static int Listener => Recording.Value.Summary.GetProperty("listener").GetInt32();

    [Fact]
    public void TheListenerHearsExactlyWhatTheRecipientTruthSays()
    {
        ScenarioRecording r = Recording.Value;
        // From the first scripted scene on: the bots' joining (their spawn's
        // claw sounds) is not the scenario.
        int first = r.Summary.GetProperty("scenes")[0].GetProperty("from").GetInt32();
        var expected = new Dictionary<(int Tick, int Kind, int Weapon), int>();
        int truths = 0;
        foreach (JsonElement t in r.Summary.GetProperty("truth").EnumerateArray())
        {
            if (t.GetProperty("tick").GetInt32() < first)
                continue;
            truths++;
            bool hear = t.GetProperty("in").GetBoolean() && t.GetProperty("owner").GetInt32() != Listener
                        && t.GetProperty("dist").GetDouble() <= t.GetProperty("reach").GetDouble() * t.GetProperty("share").GetDouble();
            if (!hear)
                continue;
            var kind = Enum.Parse<SoundKind>(t.GetProperty("kind").GetString()!);
            var key = (t.GetProperty("tick").GetInt32(), (int)kind, t.GetProperty("weapon").GetInt32());
            expected[key] = expected.GetValueOrDefault(key) + 1;
        }

        var heard = new Dictionary<(int, int, int), int>();
        foreach (AiEvent e in r.Events.Where(e => e.Kind == AiEventKind.SoundHeard && e.Bot.Slot == Listener && e.Tick.Value >= first))
        {
            var key = (e.Tick.Value, e.Code, e.Code2);
            heard[key] = heard.GetValueOrDefault(key) + 1;
        }

        Assert.True(truths > 30 && expected.Count > 20, $"the control: {truths} sounds, {expected.Count} to hear");
        string[] diff = [.. expected.Keys.Union(heard.Keys).Where(k => expected.GetValueOrDefault(k) != heard.GetValueOrDefault(k))
                                     .Select(k => $"tick {k.Item1} kind {(SoundKind)k.Item2} weapon {k.Item3}: truth {expected.GetValueOrDefault(k)}, heard {heard.GetValueOrDefault(k)}")];
        Assert.True(diff.Length == 0, string.Join("; ", diff));
    }

    [Fact]
    public void TheMagnumsReloadIsNeverHeardItsShotsAre()
    {
        ScenarioRecording r = Recording.Value;
        int magnum = Weapons.IndexOf("weapon_357");
        AiEvent[] heard = [.. r.Events.Where(e => e.Kind == AiEventKind.SoundHeard && e.Bot.Slot == Listener && e.Code2 == magnum)];
        Assert.Contains(heard, e => (SoundKind)e.Code == SoundKind.Gunfire);
        Assert.DoesNotContain(heard, e => (SoundKind)e.Code == SoundKind.Reload);
        JsonElement scene = r.Summary.GetProperty("scenes").EnumerateArray().Single(s => s.GetProperty("name").GetString() == "near 357 reload");
        Assert.True(scene.GetProperty("to").GetInt32() - scene.GetProperty("from").GetInt32() > 200, "the reload had time to happen");
    }

    [Fact]
    public void TheSmg1AndPistolReloadsAreHeardWithTheirWeapons()
    {
        ScenarioRecording r = Recording.Value;
        AiEvent[] reloads = [.. r.Events.Where(e => e.Kind == AiEventKind.SoundHeard && e.Bot.Slot == Listener && (SoundKind)e.Code == SoundKind.Reload)];
        Assert.Contains(reloads, e => e.Code2 == Weapons.IndexOf("weapon_smg1"));
        Assert.Contains(reloads, e => e.Code2 == Weapons.IndexOf("weapon_pistol"));
    }

    [Fact]
    public void TheListenerNeverHearsItsOwnSounds()
        => Assert.DoesNotContain(Recording.Value.Events, e => e.Kind == AiEventKind.SoundHeard && e.Bot.Slot == Listener && e.Other.Slot == Listener);
}

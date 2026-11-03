using System.Text.Json;
using SourceSharp.Ai.Hl2mp.Senses;
using Xunit;

namespace SourceSharp.Ai.Tests.Inspect;

/// <summary>
/// The bot inspector's record (plan_hl2mp_ai §7.2): the memory part carries
/// what a scripted brain step put in memory (the opponent, its ammo estimate
/// whose bar is exactly the range, its belief), the skills part the
/// allocation and what it drives; both as the routes' JSON.
/// </summary>
public sealed class BotInspectTests
{
    private static readonly ActorId Bot = new(3, 1), Them = new(2, 0x4000_0001);
    private static readonly WeaponFacts Smg1 = new(3, "weapon_smg1", 45, 0.075f, 1.5f, true, false, 0f);

    private static (Brain Brain, SkillProfile Skills) Stepped()
    {
        (NavGraph g, int[,] ids) = Nav.Grids.Parse("........", "........", "........");
        var world = new MemoryWorld(new MapKnowledge("t", [], [], new WeaponKnowledge([Smg1])), g, 0.015f, id => id.ToString());
        SkillProfile skills = SkillPresets.TryGet("hard", out SkillProfile? p) ? p! : throw new InvalidOperationException();
        var memory = new BotMemory(Bot, 8, MemoryParams.For(skills), world, new HabitStore());
        var brain = new Brain(Bot, 1, memory);
        var view = new PerceivedView(4);
        view.Reset(Bot, new Tick(1), new SelfState(new AiVector(16, 16, 0), default, default, 100, 0, 3, 45, 90, SelfFlags.Alive));
        view.AddSighted(new ActorFacts(Them, new AiVector(200, 40, 0), default, 3, ActorActivity.None, 1f));
        brain.Step(new Tick(1), view, [AiEvent.SightGained(Bot, new Tick(1), Them, new AiVector(200, 40, 0), default, 1f)]);
        var view2 = new PerceivedView(4);
        view2.Reset(Bot, new Tick(30), view.Self);
        AiEvent[] heard = [.. Enumerable.Range(0, 17).Select(i =>
            AiEvent.SoundHeard(Bot, new Tick(2), SoundKind.Gunfire, new AiVector(200, 40, 0), 1f, Them, 3) with { Tick = new Tick(2 + i) })];
        brain.Step(new Tick(30), view2, [AiEvent.SightLost(Bot, new Tick(2), Them, new AiVector(200, 40, 0), new AiVector(100, 0, 0)), .. heard]);
        return (brain, skills);
    }

    [Fact]
    public void TheMemoryPartCarriesTheOpponentAndItsAmmoBarIsTheRange()
    {
        (Brain brain, SkillProfile skills) = Stepped();
        BotInspectRecord r = BotInspect.Build(brain, skills, new Tick(30));
        InspectActor a = Assert.Single(r.Memory!.Actors);
        Assert.Equal((2, false, 3), (a.Slot, a.InSight, a.WeaponSeen));
        Assert.Equal(28, a.Ammo.Max);
        Assert.Equal((a.Ammo.Max - a.Ammo.Min) / 45f, a.Ammo.BarWidth, 4);
        Assert.NotNull(a.Belief);
        Assert.True(a.Belief!.Cells.Length > 0 && Math.Abs(a.Belief.Mass.Sum() - 1f) < 1e-3f, "the belief's mass is a distribution");
    }

    [Fact]
    public void TheSkillsPartIsTheAllocationAndWhatItDrives()
    {
        (Brain brain, SkillProfile skills) = Stepped();
        InspectSkills s = BotInspect.Build(brain, skills, new Tick(30)).Skills;
        Assert.Equal(("hard", 50, 60), (s.Profile, s.Spent, s.Pool));
        Assert.Equal(skills.Levels, s.Skills.Select(k => k.Level));
        Assert.Equal(VisionCurves.For(skills).RecognizeSeconds, s.Derived["recognize_s"]);
    }

    [Fact]
    public void TheRoutesJsonSpellsTheRecord()
    {
        (Brain brain, SkillProfile skills) = Stepped();
        string json = AiJson.Write(BotInspect.Build(brain, skills, new Tick(30)));
        using JsonDocument d = JsonDocument.Parse(json);
        JsonElement ammo = d.RootElement.GetProperty("memory").GetProperty("actors")[0].GetProperty("ammo");
        Assert.Equal(28, ammo.GetProperty("max").GetInt32());
        Assert.Equal(JsonValueKind.Number, ammo.GetProperty("barWidth").ValueKind);
        Assert.Equal(3, d.RootElement.GetProperty("memory").GetProperty("actors")[0].GetProperty("lastSeen").GetArrayLength());
    }
}

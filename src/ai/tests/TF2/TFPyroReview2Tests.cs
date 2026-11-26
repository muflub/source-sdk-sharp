using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.TF2.TFPyroTests;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Pyro's blast after the second code review of 2026-11-22: an Über shoved for any reason is not also backed away from;
/// the dodge is refused only for a projectile the blast will be ready for.
/// </summary>
public sealed class TFPyroReview2Tests
{
    private static AiVector Eye => Mid + new AiVector(0f, 0f, 68f);

    private static long Counter(TFField f, string name) => f.Decider.ClassCounters().Where(c => c.Key == name).Sum(c => c.Value);

    /// <summary>
    /// An Übered Heavy 200 u in front, standing on the Pyro's team's point (so the shove's reason is the point, not the Über),
    /// is shoved and not backed away from (review 2, T5: the back-off was refused only for the Über's own reason).
    /// </summary>
    [Fact]
    public void AnUberOnOurPointIsShovedNotBackedAwayFrom()
    {
        TFField f = Field(Sure);
        var point = new ObjectiveKnowledge([new Objective(40, ObjectiveKind.Capture, TFField.Red, (1 << TFField.Red) | (1 << TFField.Blue), ObjectiveState.Open,
                                                          Mid + new AiVector(100f, 0f, 0f))]);
        ActorFacts heavy = Drawn(Enemy, Mid + new AiVector(200f, 0f, 0f), TFField.Blue, conditions: ActorConditions.Invulnerable);
        IntentCommand i = f.StepDrawn(Pyro(Mid), point, heavy);
        Assert.Equal((TFPyroClass.Airblast, 1L, 0), (Ability(f, in i), Counter(f, "pyro.cap_shoves"), f.Decider.InvulnerableBackOffs));
    }

    private static ThingFacts Pipe(int id, AiVector at) => new(id, ThingKind.Projectile, (int)TFThingType.Pipe, at, new AiVector(-900f, 0f, 0f), TFField.Blue, 0);

    /// <summary>
    /// A pipe reflected, then a second pipe on top of the bot a decide later, inside the 0.75 s refire: the blast cannot be
    /// ready for it, so the bot steps out of its way instead of standing to reflect it (review 2, T6).
    /// </summary>
    [Fact]
    public void ADodgeIsNotGivenUpForAPipeTheBlastCannotBeReadyFor()
    {
        TFField f = Field(p => Sure(p) with { DodgeChance = 1f, DodgeLateMs = 0f });
        IntentCommand first = f.StepSeeing(Pyro(Mid), TFField.Ctf(), [Pipe(901, Eye + new AiVector(100f, 0f, -40f))]);
        Assert.Equal(TFPyroClass.Airblast, Ability(f, in first));
        int before = f.Decider.Dodges;
        f.StepSeeing(Pyro(Mid), TFField.Ctf(), [Pipe(902, Eye + new AiVector(90f, 0f, -40f))]);
        Assert.True(f.Decider.Dodges > before, "the second pipe was not dodged");
    }
}

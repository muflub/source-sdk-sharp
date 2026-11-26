using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The shared ammo errand (round 2's S5, TFDecider.Ammo.cs): a class asks for it through its play's terms (a floor and a
/// utility), off by default. The Heavy's own ammo facts (TFHeavyTests) run through it unchanged.
/// </summary>
public sealed class TFAmmoErrandTests
{
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly AiVector PackAt = new(1600f, 256f, 0f);

    private static readonly int StepsPerSecond = (int)MathF.Round(1f / (DecideSchedule.Period * 0.015f));

    private static ItemSpawn[] Packs() =>
    [
        new ItemSpawn(0, "item_healthkit_medium", ItemKind.HealthMedium, new AiVector(512f, 64f, 0f), 10f),
        new ItemSpawn(1, "item_ammopack_medium", ItemKind.AmmoMedium, PackAt, 10f),
    ];

    /// <summary>A play of the general kind under the Soldier's class whose ammo terms are a fact's.</summary>
    private sealed class ErrandPlay(TFDecider d, TFAmmoErrand terms) : TFDecider.ClassPlay(d, TFClass.Soldier)
    {
        public override TFAmmoErrand Ammo => terms;
    }

    /// <summary>The verdicts of <paramref name="seconds"/> of quiet decides of a Soldier with no clip and <paramref name="reserve"/> rounds held, and the trips counted.</summary>
    private static (List<string> Verdicts, int Trips, TFField Field) Empty(Func<TFDecider, TFClass, TFDecider.ClassPlay?>? plays, float seconds = 4f, int reserve = 0)
    {
        var f = new TFField(packs: Packs(), plays: plays);
        var verdicts = new List<string>();
        for (int i = 0; i < (int)(seconds * StepsPerSecond); i++)
        {
            f.Step(TFField.Self(Mid, clip: 0, reserve: reserve), TFField.Ctf());
            verdicts.Add(f.Branch + "/" + f.Decider.Doing.Verdict);
        }

        return (verdicts, f.Decider.AmmoTrips, f);
    }

    private static readonly string OnErrand = TFDecider.ClassBranch + "/" + TFDecider.AmmoVerdict;

    /// <summary>
    /// A class with the errand off never takes it: an empty Soldier in a lull 450 u from an ammo pack, by the shipped Soldier's
    /// play and by a play whose terms are <see cref="TFAmmoErrand.Off"/>. The control: the same play with a floor of 10 and the
    /// Heavy's utility goes for the pack.
    /// </summary>
    [Fact]
    public void AClassWithTheErrandOffNeverTakesIt()
    {
        (List<string> shipped, int shippedTrips, _) = Empty(null);
        (List<string> off, int offTrips, _) = Empty((d, c) => c == TFClass.Soldier ? new ErrandPlay(d, TFAmmoErrand.Off) : null);
        (List<string> on, int onTrips, TFField onField) = Empty((d, c) => c == TFClass.Soldier ? new ErrandPlay(d, new TFAmmoErrand(10, 0.95f)) : null);

        Assert.DoesNotContain(OnErrand, shipped);
        Assert.DoesNotContain(OnErrand, off);
        Assert.Equal((0, 0), (shippedTrips, offTrips));
        Assert.Contains(OnErrand, on);
        Assert.Equal((1, PackAt), (onTrips, onField.Decider.Doing.Goal));
    }

    /// <summary>
    /// A pool at the floor is not under it: the errand's floor is a strict bound, as the Heavy's was (<c>&lt; AmmoLow</c>). Ten
    /// rounds held against a floor of 10 stay off the errand; against a floor of 11 they go.
    /// </summary>
    [Fact]
    public void APoolAtTheFloorStaysOffTheErrand()
    {
        List<string> Verdicts(int floor) => Empty((d, c) => c == TFClass.Soldier ? new ErrandPlay(d, new TFAmmoErrand(floor, 0.95f)) : null, reserve: 10).Verdicts;
        Assert.DoesNotContain(OnErrand, Verdicts(10));
        Assert.Contains(OnErrand, Verdicts(11));
    }

    /// <summary>
    /// A friendly dispenser is an ammo place for <see cref="TFDecider.DispenserMemorySeconds"/> after the bot last saw it: a Heavy
    /// that saw one 400 u off with its gun full, and ran low 50 s later, goes to it; 65 s later it does not (no pack in reach).
    /// </summary>
    [Fact]
    public void ASeenDispenserOlderThanSixtySecondsIsNotAPlace()
    {
        AiVector dispenser = Mid + new AiVector(-400f, 0f, 0f);
        ItemSpawn[] noAmmo = [new ItemSpawn(0, "item_healthkit_medium", ItemKind.HealthMedium, new AiVector(512f, 64f, 0f), 10f)];
        (string Branch, AiVector Goal) LowAfter(float seconds)
        {
            TFField f = TFHeavyTests.Field(packs: noAmmo);
            var seen = new ThingFacts(950, ThingKind.Building, (int)TFThingType.Dispenser, dispenser, default, TFField.Red);
            f.StepSeeing(TFHeavyTests.Heavy(Mid), TFField.Ctf(), [seen]);
            for (int i = 0; i < (int)(seconds * StepsPerSecond); i++)
                f.Step(TFHeavyTests.Heavy(Mid), TFField.Ctf());
            for (int i = 0; i < 3 * StepsPerSecond; i++)
                f.Step(TFHeavyTests.Heavy(Mid, reserve: 30), TFField.Ctf());
            return (f.Branch, f.Decider.Doing.Goal);
        }

        Assert.Equal((TFDecider.ClassBranch, dispenser), LowAfter(50f));
        Assert.NotEqual(TFDecider.ClassBranch, LowAfter(65f).Branch);
    }
}

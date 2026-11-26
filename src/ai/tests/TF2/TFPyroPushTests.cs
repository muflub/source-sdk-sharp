using System.Collections.Immutable;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.TF2.TFPyroTests;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The user's request of 2026-11-22 for the Pyro's blast: push enemies off objectives, push an Übered Medic away from his
/// patient, push enemies over a drop, and reflect a projectile before it is on top of the bot (predicting where it will be
/// at the press).
/// </summary>
public sealed class TFPyroPushTests
{
    private static readonly ActorId Medic = new(6, 0x4000_0001);
    private const int PointId = 40, RedFlagId = 41;

    private static AiVector Eye => Mid + new AiVector(0f, 0f, 68f);

    private static long Counter(TFField f, string name) => f.Decider.ClassCounters().Where(c => c.Key == name).Sum(c => c.Value);

    private static ObjectiveKnowledge Point(AiVector at, int owner = TFField.Red, ObjectiveState state = ObjectiveState.Open)
        => new([new Objective(PointId, ObjectiveKind.Capture, owner, (1 << TFField.Red) | (1 << TFField.Blue), state, at)]);

    /// <summary>Runs <paramref name="decides"/> decides of a sure Pyro at <see cref="Mid"/> with <paramref name="sighted"/> and returns its counter <paramref name="counter"/>.</summary>
    private static long Run(string counter, ObjectiveKnowledge? objectives, int decides = 30, NavGraph? floor = null, params ActorFacts[] sighted)
    {
        var f = new TFField(seed: SeedOf(TFPyroClass.Stock), weapons: Weapons, tune: Sure, floor: floor) { Hud = Kit() };
        for (int n = 0; n < decides; n++)
            f.StepDrawn(Pyro(Mid), objectives, sighted);
        return Counter(f, counter);
    }

    /// <summary>
    /// A Heavy 200 u in front, standing on the Pyro's team's point (its middle 100 u in front, so the push takes him off it),
    /// is shoved off it; the same Heavy with no point there is no objective shove.
    /// </summary>
    [Fact]
    public void AnEnemyOnOurPointIsShovedOffIt()
    {
        ActorFacts heavy = Drawn(Enemy, Mid + new AiVector(200f, 0f, 0f), TFField.Blue);
        Assert.True(Run("pyro.cap_shoves", Point(Mid + new AiVector(100f, 0f, 0f)), sighted: [heavy]) > 0);
        Assert.Equal(0, Run("pyro.cap_shoves", null, sighted: [heavy]));
    }

    /// <summary>A push from where the Pyro stands that would send the capper toward the point's middle is not an objective shove (it waits to be aligned).</summary>
    [Fact]
    public void ACapperIsNotShovedOntoThePointsMiddle()
    {
        ActorFacts heavy = Drawn(Enemy, Mid + new AiVector(150f, 0f, 0f), TFField.Blue);
        Assert.Equal(0, Run("pyro.cap_shoves", Point(Mid + new AiVector(250f, 0f, 0f)), sighted: [heavy]));
    }

    /// <summary>The Pyro's team's flag carried by <see cref="Enemy"/>, and Blue's capture zone at <paramref name="zone"/>.</summary>
    private static ObjectiveKnowledge Carried(AiVector zone)
        => new([new Objective(RedFlagId, ObjectiveKind.Carry, TFField.Red, 1 << TFField.Blue, ObjectiveState.Carried, zone, CarrierSlot: Enemy.Slot),
                new Objective(RedFlagId + 1, ObjectiveKind.Deliver, TFField.Blue, (1 << TFField.Red) | (1 << TFField.Blue), ObjectiveState.Open, zone)]);

    /// <summary>The enemy carrying the Pyro's team's flag, 200 u in front with his capture zone behind the Pyro, is shoved back from it.</summary>
    [Fact]
    public void TheCarrierOfOurFlagIsShovedBack()
        => Assert.True(Run("pyro.cap_shoves", Carried(Mid + new AiVector(-2000f, 0f, 0f)), sighted: [Drawn(Enemy, Mid + new AiVector(200f, 0f, 0f), TFField.Blue)]) > 0);

    /// <summary>
    /// The chase from behind (review 2, T1): the carrier 200 u ahead running for his zone further ahead is never shoved
    /// (a push would send him on his way and hold the flame off a second): he is flamed.
    /// </summary>
    [Fact]
    public void ACarrierChasedFromBehindIsFlamedNotShoved()
    {
        var f = new TFField(seed: SeedOf(TFPyroClass.Stock), weapons: Weapons, tune: Sure) { Hud = Kit() };
        ActorFacts carrier = Drawn(Enemy, Mid + new AiVector(200f, 0f, 0f), TFField.Blue, velocity: new AiVector(300f, 0f, 0f));
        var actions = new List<string>();
        for (int n = 0; n < 40; n++)
        {
            IntentCommand i = f.StepDrawn(Pyro(Mid), Carried(Mid + new AiVector(2500f, 0f, 0f)), carrier);
            actions.Add(i.Action.Kind == ActionKind.Fire ? "fire" : Ability(f, in i));
        }

        Assert.DoesNotContain(TFPyroClass.Airblast, actions);
        Assert.Contains("fire", actions);
    }

    /// <summary>
    /// An Übered Medic 200 u in front with his Übered patient 100 u in front (between them) is pushed away from him: the split,
    /// and the view on the Medic, not the Über shove of the patient. An Übered Heavy alone is an Über shove.
    /// </summary>
    [Fact]
    public void AnUberedMedicIsPushedAwayFromHisPatient()
    {
        var f = new TFField(seed: SeedOf(TFPyroClass.Stock), weapons: Weapons, tune: Sure) { Hud = Kit() };
        ActorFacts medic = Drawn(Medic, Mid + new AiVector(200f, 30f, 0f), TFField.Blue, TFClass.Medic, ActorConditions.Invulnerable);
        ActorFacts patient = Drawn(Enemy, Mid + new AiVector(100f, -30f, 0f), TFField.Blue, TFClass.HeavyWeapons, ActorConditions.Invulnerable);
        IntentCommand i = f.StepDrawn(Pyro(Mid), TFField.Ctf(), patient, medic);
        Assert.Equal((TFPyroClass.Airblast, Medic), (Ability(f, in i), i.Look.Actor));
        Assert.Equal((1L, 0L), (Counter(f, "pyro.uber_splits"), Counter(f, "pyro.uber_shoves")));

        Assert.Equal(0, Run("pyro.uber_splits", TFField.Ctf(), 5, null, patient));
        Assert.True(Run("pyro.uber_shoves", TFField.Ctf(), 5, null, patient) > 0);
    }

    /// <summary>
    /// A graph of strips across the field (y 0-512), each <c>(minX, maxX, floorZ)</c>; strips that touch end to end with floors
    /// a step apart are joined by Walk edges both ways, as the derivation joins walkable neighbours.
    /// </summary>
    private static NavGraph Strips(params (float MinX, float MaxX, float Z)[] strips)
    {
        var b = new NavGraphBuilder();
        var ids = strips.Select(t => b.AddCell(new NavCell(t.MinX, 0f, t.MaxX, 512f, t.Z, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f))).ToArray();
        for (int i = 0; i < strips.Length; i++)
        {
            for (int j = i + 1; j < strips.Length; j++)
            {
                if ((strips[i].MaxX == strips[j].MinX || strips[j].MaxX == strips[i].MinX) && MathF.Abs(strips[i].Z - strips[j].Z) <= 18f)
                    b.AddBoth(ids[i], ids[j], NavEdgeType.Walk);
            }
        }

        return b.Build();
    }

    private static readonly AiVector PyroAt = new(2050f, 256f, 0f), HeavyAt = new(2170f, 256f, 0f);

    /// <summary>Ten decides of a sure Pyro 120 u behind a Heavy on <paramref name="floor"/> (and <paramref name="zones"/>); its env and fall shove counts.</summary>
    private static (long Env, long Fall, long Uber) Over(NavGraph floor, TeamZone[]? zones = null, ActorConditions conditions = 0)
    {
        var f = new TFField(seed: SeedOf(TFPyroClass.Stock), weapons: Weapons, tune: Sure, floor: floor, zones: zones) { Hud = Kit() };
        ActorFacts heavy = Drawn(Enemy, HeavyAt, TFField.Blue, conditions: conditions);
        for (int n = 0; n < 10; n++)
            f.StepDrawn(Pyro(PyroAt), TFField.Ctf(), heavy);
        return (Counter(f, "pyro.env_shoves"), Counter(f, "pyro.fall_shoves"), Counter(f, "pyro.uber_shoves"));
    }

    /// <summary>A floor ending 70 u behind the Heavy, with another 600 u below starting 16 u past the edge (a ledge): a fall-damage shove, not a lethal one.</summary>
    [Fact]
    public void AnEnemyByALedgeIsPushedOverItForTheFall()
    {
        (long env, long fall, _) = Over(Strips((0f, 2240f, 0f), (2256f, 4000f, -600f)));
        Assert.Equal((0L, true), (env, fall > 0));
    }

    /// <summary>The drop's bound (270 u, past the safe fall): 260 u below is no fall shove, 280 u is.</summary>
    [Fact]
    public void AFallShoveNeedsADropPastTheSafeFall()
    {
        Assert.Equal(0L, Over(Strips((0f, 2240f, 0f), (2256f, 4000f, -260f))).Fall);
        Assert.True(Over(Strips((0f, 2240f, 0f), (2256f, 4000f, -280f))).Fall > 0);
    }

    /// <summary>The push's travel bound (300 u): an edge 290 u behind the Heavy is pushed over, one 330 u behind is not.</summary>
    [Fact]
    public void AnEdgeOutOfThePushsTravelIsNoShove()
    {
        Assert.True(Over(Strips((0f, 2460f, 0f), (2476f, 4000f, -600f))).Fall > 0);
        Assert.Equal(0L, Over(Strips((0f, 2500f, 0f), (2516f, 4000f, -600f))).Fall);
    }

    /// <summary>A wall 70 u behind the Heavy with a room 300 u lower behind it (its floor starting 48 u past the near floor's edge) is no ledge (review 2, T3 input A).</summary>
    [Fact]
    public void AWallWithARoomBehindItIsNoLedge()
    {
        (long env, long fall, _) = Over(Strips((0f, 2240f, 0f), (2288f, 4000f, -300f)));
        Assert.Equal((0L, 0L), (env, fall));
    }

    /// <summary>A stair up behind the Heavy, over a storey 300 u below, is walked up, not read through: no shove (review 2, T3 input B).</summary>
    [Fact]
    public void AStairOverALowerStoreyIsNoLedge()
    {
        (long env, long fall, _) = Over(Strips((0f, 2240f, 0f), (2240f, 2320f, 16f), (2320f, 2400f, 32f), (2400f, 2600f, 48f), (2240f, 2600f, -300f)));
        Assert.Equal((0L, 0L), (env, fall));
    }

    private static readonly TeamZone[] Pit = [new TeamZone(0, ZoneKind.Hurt, new AiVector(2240f, 0f, -900f), new AiVector(2700f, 512f, -500f))];

    /// <summary>A floor ending 70 u behind the Heavy over a death pit (a hurt zone below, no floor): the lethal shove.</summary>
    [Fact]
    public void AnEnemyByADeathPitIsPushedIntoIt()
        => Assert.True(Over(Strips((0f, 2240f, 0f)), Pit).Env > 0);

    /// <summary>A pit kills through an Über (the game lets a trigger_hurt's damage through), so an Übered Heavy by one is pushed in; by a mere drop he gets the Über shove.</summary>
    [Fact]
    public void AnUberByAPitIsPushedInButByADropIsShovedAsAnUber()
    {
        Assert.True(Over(Strips((0f, 2240f, 0f)), Pit, ActorConditions.Invulnerable).Env > 0);
        (long env, long fall, long uber) = Over(Strips((0f, 2240f, 0f), (2256f, 4000f, -600f)), null, ActorConditions.Invulnerable);
        Assert.Equal((0L, 0L, true), (env, fall, uber > 0));
    }

    private static ThingFacts Rocket(AiVector at, AiVector velocity) => new(950, ThingKind.Projectile, (int)TFThingType.Rocket, at, velocity, TFField.Blue, 0);

    /// <summary>
    /// A Direct Hit rocket (1,980 u/s) fired at the Pyro from 800 u: decides 0.09 s apart see it at 800, 622, 444, 266 and
    /// 88 u. The blast is pressed while it is still over 200 u out (it will be inside the sphere at the press), not at 88 u,
    /// where it would already be on the bot.
    /// </summary>
    [Fact]
    public void ARocketFiredFrom800UnitsIsReflectedOnTime()
    {
        float? pressedAt = null;
        TFField f = Field(Sure);
        float speed = 1980f, step = speed * DecideSchedule.Period * 0.015f;
        for (int n = 0; n < 5 && pressedAt is null; n++)
        {
            float x = 800f - step * n;
            IntentCommand i = f.StepSeeing(Pyro(Mid), TFField.Ctf(), [Rocket(Eye + new AiVector(x, 0f, 0f), new AiVector(-speed, 0f, 0f))]);
            if (Ability(f, in i) == TFPyroClass.Airblast)
                pressedAt = x;
        }

        Assert.NotNull(pressedAt);
        Assert.True(pressedAt > 200f, $"pressed at {pressedAt} u");
    }

    /// <summary>A rocket flying past 300 u to the bot's side is not turned to nor blasted.</summary>
    [Fact]
    public void ARocketAimedElsewhereIsNotChased()
    {
        TFField f = Field(Sure);
        for (int n = 0; n < 5; n++)
        {
            IntentCommand i = f.StepSeeing(Pyro(Mid), TFField.Ctf(), [Rocket(Eye + new AiVector(500f - 99f * n, 300f, 0f), new AiVector(-1100f, 0f, 0f))]);
            Assert.NotEqual(LookKind.Point, i.Look.Kind);
            Assert.NotEqual(TFPyroClass.Airblast, Ability(f, in i));
        }
    }

    /// <summary>A bot whose reflect chance is 0 (stock's Easy) predicts nothing: the same Direct Hit rocket is never blasted.</summary>
    [Fact]
    public void TheReflectChanceStillGatesThePrediction()
    {
        TFField f = Field(Never);
        float speed = 1980f, step = speed * DecideSchedule.Period * 0.015f;
        for (int n = 0; n < 5; n++)
        {
            IntentCommand i = f.StepSeeing(Pyro(Mid), TFField.Ctf(), [Rocket(Eye + new AiVector(800f - step * n, 0f, 0f), new AiVector(-speed, 0f, 0f))]);
            Assert.NotEqual(TFPyroClass.Airblast, Ability(f, in i));
        }
    }
}

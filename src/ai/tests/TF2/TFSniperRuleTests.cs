using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

using static SourceSharp.Ai.Tests.TF2.TFSniperFixture;

/// <summary>
/// The Sniper's rules the hand-back listed with no fact (the code review's F2): the overheal guess at its call, the facing Sniper, the error floor, the target hold, the Pyro in the splash, the branch weights.
/// </summary>
public sealed class TFSniperRuleTests
{
    /// <summary>
    /// A Medic within 450 u of a scoped Soldier makes the charge a kill needs 100 (300 overhealed): at 80 the Soldier alone is fired at,
    /// with a Medic by him he is not.
    /// </summary>
    [Fact]
    public void AMedicByTheTargetRaisesTheChargeItNeeds()
    {
        ActorFacts soldier = TFSniperPlayTests.Drawn(A, Mid + new AiVector(1500f, 0f, 0f), TFClass.Soldier);
        ActorFacts medic = TFSniperPlayTests.Drawn(Medic, Mid + new AiVector(1650f, 100f, 0f), TFClass.Medic);
        // Scoped on the Soldier first (uncharged: no shot), so he is the target held when the Medic comes into sight.
        IntentCommand[] Arm(params ActorFacts[] seen)
        {
            TFField f = Field();
            Scoped(f, 0f, 4, soldier);
            return Scoped(f, 80f, 10, seen);
        }

        Assert.Contains(Arm(soldier), i => i.Action.Kind == ActionKind.Fire && i.Look.Actor == A);
        IntentCommand[] withMedic = Arm(soldier, medic);
        Assert.All(withMedic, i => Assert.Equal(A, i.Look.Actor));
        Assert.DoesNotContain(withMedic, i => i.Action.Kind == ActionKind.Fire);
    }

    /// <summary>
    /// An enemy Sniper with a Medic by him (63 a kill) facing the bot is fired at uncharged; the control, the same Sniper facing away,
    /// is not (stock's immediate threat, tf_bot_sniper_attack.cpp:199-209).
    /// </summary>
    [Fact]
    public void AnEnemySniperFacingTheBotIsFiredAtUncharged()
    {
        AiVector at = Mid + new AiVector(1500f, 0f, 0f);
        ActorFacts medic = TFSniperPlayTests.Drawn(Medic, at + new AiVector(150f, 150f, 0f), TFClass.Medic);
        ActorFacts facing = TFSniperPlayTests.Drawn(B, at, TFClass.Sniper, facing: Facing(at, Mid));
        ActorFacts away = TFSniperPlayTests.Drawn(B, at, TFClass.Sniper, facing: Facing(Mid, at));
        Assert.Contains(Scoped(Field(), 0f, 12, facing, medic), i => i.Action.Kind == ActionKind.Fire && i.Look.Actor == B);
        Assert.DoesNotContain(Scoped(Field(), 0f, 12, away, medic), i => i.Action.Kind == ActionKind.Fire && i.Look.Actor == B);
    }

    /// <summary>
    /// The aim's error is never under stock's 0.01 rad: a bot whose skill gives 0.05 degrees aims, on average over a second scoped
    /// at a still Soldier, more than 0.2 degrees off the head.
    /// </summary>
    [Fact]
    public void TheAimErrorHasStocksFloor()
    {
        TFField f = Field(tune: p => p with { AimErrorDeg = 0.05f });
        AiVector at = Mid + new AiVector(1500f, 0f, 0f);
        IntentCommand[] decides = Scoped(f, 0f, 14, TFSniperPlayTests.Drawn(A, at, TFClass.Soldier));
        AiVector head = at + new AiVector(0f, 0f, TFKnowledge.AimHeight(AimPoint.Head, TFClass.Soldier).Standing);
        float mean = decides.Skip(2).Where(i => i.Look.Kind == LookKind.Actor)
                            .Average(i => MathF.Atan2((i.Look.Point - head).Length, 1500f) * 180f / MathF.PI);
        Assert.True(mean > 0.2f, $"mean {mean:F3} degrees off the head");
    }

    /// <summary>
    /// Scoped on a Soldier, an enemy Sniper (a better target) coming into sight is not swapped to inside 3 s; after it, he is.
    /// The control: unscoped, the Sniper is taken at once.
    /// </summary>
    [Fact]
    public void ScopedTheTargetIsHeldThreeSeconds()
    {
        ActorFacts soldier = TFSniperPlayTests.Drawn(A, Mid + new AiVector(1500f, 100f, 0f), TFClass.Soldier);
        ActorFacts sniper = TFSniperPlayTests.Drawn(B, Mid + new AiVector(1500f, -100f, 0f), TFClass.Sniper, facing: Facing(Mid, Mid + new AiVector(1500f, -900f, 0f)));
        TFField f = Field();
        Scoped(f, 0f, 3, soldier);
        IntentCommand[] early = Scoped(f, 0f, 10, soldier, sniper);
        IntentCommand[] late = Scoped(f, 0f, 40, soldier, sniper);
        Assert.All(early, i => Assert.NotEqual(B, i.Look.Actor));
        Assert.Contains(late, i => i.Look.Actor == B);
    }

    /// <summary>A Soldier with a Pyro facing the bot beside him (in the splash) draws no jar: the airblast would send it back. The control: facing away, it does.</summary>
    [Fact]
    public void APyroFacingTheBotInTheSplashDrawsNoJar()
    {
        AiVector at = Mid + new AiVector(800f, 0f, 0f);
        ActorFacts soldier = TFSniperPlayTests.Drawn(A, at, TFClass.Soldier);
        ActorFacts facing = TFSniperPlayTests.Drawn(Pyro, at + new AiVector(80f, 0f, 0f), TFClass.Pyro, facing: Facing(at, Mid));
        ActorFacts away = TFSniperPlayTests.Drawn(Pyro, at + new AiVector(80f, 0f, 0f), TFClass.Pyro, facing: Facing(Mid, at));
        static bool Jar(TFField f, params ActorFacts[] seen)
        {
            f.Hud = TFSniperPlayTests.Kit(jar: true);
            IntentCommand i = f.StepDrawn(TFSniperPlayTests.Me(Mid), TFField.Ctf(), seen);
            return i.Action.Kind == ActionKind.SelectWeapon && i.Action.WeaponSlot == EngineBotSelf.ByKind + TFSniperPlayTests.JarKind;
        }

        Assert.True(Jar(Field(TFSniperClass.Jarate), soldier, away));
        Assert.False(Jar(Field(TFSniperClass.Jarate), soldier, facing));
    }

    /// <summary>A Sniper with a scoped rifle takes its spot over the fetch of CTF; one with a bow (no rifle in its kit) does not.</summary>
    [Fact]
    public void ARifleSniperPrefersItsSpotAndABowSniperDoesNot()
    {
        TFField rifle = Field();
        Walk(rifle, 2, _ => TFField.Ctf(), Start);
        var bowKit = new TFHud([new TFKitItem(TFLoadoutSlot.Primary, 56, "tf_weapon_compound_bow", TFSniperPlayTests.Rifle, [])], [], []);
        TFField bow = Field();
        Walk(bow, 2, _ => TFField.Ctf(), Start, hud: bowKit);
        Assert.Equal((TFDecider.ClassBranch, false), (rifle.Branch, bow.Branch == TFDecider.ClassBranch));
    }
}

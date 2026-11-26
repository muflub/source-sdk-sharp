using System.Collections.Immutable;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Round 2's S4 in the general fight's pick (TFDecider.Fight.cs): an enemy in sight that every hit mini-crits (Jarated,
/// marked for death) is worth a little more than one as near, and a blast-airborne one to a bot holding a weapon that
/// mini-crits the airborne (the Direct Hit, the Reserve Shooter); the bonus is small, so a far one never beats a near one.
/// </summary>
public sealed class TFConditionTargetTests
{
    private static readonly ActorId Low = new(5, 1), High = new(6, 1);

    private static readonly AiVector Bot = new(1000f, 256f, 0f);

    private static ActorFacts Enemy(ActorId id, AiVector at, ActorConditions conditions = ActorConditions.None)
        => new(id, at, default, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, Class: (int)TFClass.Soldier, Conditions: conditions,
               NameSlot: id.Slot);

    /// <summary>Two enemies 905 u off, the later-offered one carrying <paramref name="conditions"/>: the decide's target.</summary>
    private static (ActorId Target, TFDecider Decider) Pick(ActorConditions conditions, TFHud? hud = null, int weapon = TFField.Launcher)
    {
        var f = new TFField { Hud = hud };
        f.StepDrawn(TFField.Self(Bot, weapon: weapon), TFField.Ctf(),
                    Enemy(Low, new AiVector(1900f, 156f, 0f)), Enemy(High, new AiVector(1900f, 356f, 0f), conditions));
        return (f.Decider.Doing.Target, f.Decider);
    }

    /// <summary>The control: with nothing drawn on either, the pick keeps the first of two as near.</summary>
    [Fact]
    public void WithNoConditionTheFirstOfTwoAsNearIsPicked()
        => Assert.Equal(Low, Pick(ActorConditions.None).Target);

    [Fact]
    public void TheMarkedEnemyIsPickedOverAnEqualUnmarkedOne()
        => Assert.Equal(High, Pick(ActorConditions.MarkedForDeath).Target);

    [Fact]
    public void TheJaratedEnemyIsPickedOverAnEqualDryOne()
        => Assert.Equal(High, Pick(ActorConditions.Jarated | ActorConditions.Wet).Target);

    /// <summary>Milk heals the hitter and mini-crits nothing (tf_gamerules.cpp:6126-6129): no bonus.</summary>
    [Fact]
    public void AMilkedEnemyEarnsNoBonus()
        => Assert.Equal(Low, Pick(ActorConditions.Milked | ActorConditions.Wet).Target);

    /// <summary>The counters see a pick the bonus made: the next decide counts the last one's target and that the pick with no bonus went elsewhere.</summary>
    [Fact]
    public void ThePickTheBonusMadeIsCountedAsASwing()
    {
        var f = new TFField();
        ActorFacts low = Enemy(Low, new AiVector(1900f, 156f, 0f)), high = Enemy(High, new AiVector(1900f, 356f, 0f), ActorConditions.Jarated);
        f.StepDrawn(TFField.Self(Bot), TFField.Ctf(), low, high);
        f.StepDrawn(TFField.Self(Bot), TFField.Ctf(), low, high);
        Assert.Equal((1, 1, 1, 1), (f.Decider.MiniCritSeenDecides, f.Decider.JaratedSeenDecides, f.Decider.ConditionTargetDecides, f.Decider.ConditionSwings));
        Assert.Equal(0, f.Decider.AirborneTargetDecides);
    }

    /// <summary>The view's reads are counted whatever the pick made of them: an enemy drawn marked, wet, or thrown by a blast.</summary>
    [Fact]
    public void EachConditionSeenIsCounted()
    {
        var f = new TFField();
        ActorFacts low = Enemy(Low, new AiVector(1900f, 156f, 0f), ActorConditions.MarkedForDeath | ActorConditions.BlastAirborne),
                   high = Enemy(High, new AiVector(1900f, 356f, 0f), ActorConditions.Wet);
        f.StepDrawn(TFField.Self(Bot), TFField.Ctf(), low, high);
        f.StepDrawn(TFField.Self(Bot), TFField.Ctf(), low with { Conditions = ActorConditions.None }, high with { Conditions = ActorConditions.None });
        f.StepDrawn(TFField.Self(Bot), TFField.Ctf(), low, high);
        Assert.Equal((1, 1, 1, 0), (f.Decider.MarkedSeenDecides, f.Decider.WetSeenDecides, f.Decider.AirborneSeenDecides, f.Decider.JaratedSeenDecides));
    }

    /// <summary>A marked enemy 1,700 u off does not beat an unmarked one 800 u off: the bonus is a tie-breaker, not a threat.</summary>
    [Fact]
    public void AFarMarkedEnemyDoesNotBeatANearThreat()
    {
        var f = new TFField();
        f.StepDrawn(TFField.Self(Bot), TFField.Ctf(),
                    Enemy(Low, new AiVector(2700f, 256f, 0f), ActorConditions.MarkedForDeath | ActorConditions.Jarated),
                    Enemy(High, new AiVector(1800f, 256f, 0f)));
        Assert.Equal(High, f.Decider.Doing.Target);
    }

    private static TFHud Kit(bool miniCritAirborne)
        => new([new TFKitItem(TFLoadoutSlot.Primary, 127, "tf_weapon_rocketlauncher_directhit", TFField.Launcher,
                              miniCritAirborne ? [new TFOwnKitAttribute(TFDecider.MiniCritAirborne, 1f)] : [])],
               ImmutableArray<TFMeter>.Empty, ImmutableArray<TFMateHealth>.Empty);

    /// <summary>A launcher in hand that mini-crits the airborne (the Direct Hit's <c>mini_crit_airborne</c> 1) raises an enemy a blast threw.</summary>
    [Fact]
    public void AWeaponThatMiniCritsTheAirborneRaisesAnEnemyABlastThrew()
    {
        Assert.Equal(High, Pick(ActorConditions.BlastAirborne, Kit(miniCritAirborne: true)).Target);
    }

    /// <summary>The same launcher without <c>mini_crit_airborne</c> earns an airborne enemy nothing.</summary>
    [Fact]
    public void ALauncherWithoutTheAttributeEarnsAnAirborneEnemyNothing()
        => Assert.Equal(Low, Pick(ActorConditions.BlastAirborne, Kit(miniCritAirborne: false)).Target);

    /// <summary>The attribute's launcher carried but not in hand (the shotgun out) earns an airborne enemy nothing.</summary>
    [Fact]
    public void TheAttributesWeaponNotInHandEarnsAnAirborneEnemyNothing()
        => Assert.Equal(Low, Pick(ActorConditions.BlastAirborne, Kit(miniCritAirborne: true), weapon: TFField.Shotgun).Target);

    /// <summary>With no kit read (a replay, a fact with no HUD) an airborne enemy earns nothing.</summary>
    [Fact]
    public void WithNoKitAnAirborneEnemyEarnsNothing() => Assert.Equal(Low, Pick(ActorConditions.BlastAirborne).Target);
}

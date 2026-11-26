using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.TF2.TFDemomanTests;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Demoknight and the hybrid knight (plan_bots_teams_tf2 P7; .scratch/p7-research/demoman.md §4.3 "Demoknight", A2, A5,
/// the review's D3): the charge on a full meter at a target along the eye in its band, the swing at the charge's reach, never
/// at a Pyro, the Tide Turner's shorter band, the knight's targets, the heads' caution, the hybrid knight's sword for a charge.
/// </summary>
public sealed class TFDemomanKnightTests
{
    private static readonly ActorId Other = new(6, 0x4000_0001);

    /// <summary>A knight's kit: Ali Baba's Wee Booties, a shield (the Targe, or the Tide Turner), the Eyelander; the HUD's meter and heads.</summary>
    private static TFHud KnightKit(float meter, int heads = 0, bool tideTurner = false)
        => new([Item(TFLoadoutSlot.Primary, 405, "tf_wearable", -1, new TFOwnKitAttribute("add_maxhealth", 25f)),
                tideTurner ? Item(TFLoadoutSlot.Secondary, 1099, "tf_wearable_demoshield", -1, new TFOwnKitAttribute("lose_demo_charge_on_damage_when_charging", 1f))
                           : Item(TFLoadoutSlot.Secondary, 131, "tf_wearable_demoshield", -1),
                Item(TFLoadoutSlot.Melee, 132, "tf_weapon_sword", Sword, new TFOwnKitAttribute("is_a_sword", 72f))],
               [new TFMeter(TFDemomanMeters.Charge, meter), new TFMeter(TFDemomanMeters.Heads, heads)], []);

    /// <summary>The hybrid knight's: the pipes, the Targe, the Eyelander.</summary>
    private static TFHud HybridKnightKit(float meter)
        => new([Item(TFLoadoutSlot.Primary, 19, "tf_weapon_grenadelauncher", Pipes), Item(TFLoadoutSlot.Secondary, 131, "tf_wearable_demoshield", -1),
                Item(TFLoadoutSlot.Melee, 132, "tf_weapon_sword", Sword, new TFOwnKitAttribute("is_a_sword", 72f))],
               [new TFMeter(TFDemomanMeters.Charge, meter)], []);

    private static bool Charges(TFField f, in IntentCommand i)
        => i.Action.Kind == ActionKind.Ability && f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow row)
           && row.Name == TFDemomanClass.Charge;

    private static ActorFacts Enemy(AiVector at, TFClass cls = TFClass.Soldier, ActorId? id = null)
        => new(id ?? TFDemomanTests.Enemy, at, AiVector.Zero, Pipes, ActorActivity.None, 1f, TFField.Blue, (int)cls);

    /// <summary>The knight holding his sword, looking along +x (yaw <paramref name="yaw"/>).</summary>
    private static SelfState Knight(float yaw = 0f, int health = 175) => Self(Mid, Sword, 0, 0, health) with { Eye = new AiAngles(0f, yaw, 0f) };

    /// <summary>Whether a charge is sent within a few decides at a target <paramref name="distance"/> along +x.</summary>
    private static bool ChargesAt(TFHud hud, float distance, float yaw = 0f, TFClass cls = TFClass.Soldier)
    {
        var f = new TFField(weapons: Weapons) { Hud = hud };
        for (int n = 0; n < 4; n++)
        {
            IntentCommand i = f.StepDrawn(Knight(yaw), null, Enemy(Mid + new AiVector(distance, 0f, 0f), cls));
            if (Charges(f, in i))
                return true;
        }

        return false;
    }

    /// <summary>A full meter and a target 900 u along the eye: the knight charges.</summary>
    [Fact]
    public void AFullMeterChargesATargetAlongTheEye() => Assert.True(ChargesAt(KnightKit(100f), 900f));

    /// <summary>A meter not full: no charge (the game starts one only at 100).</summary>
    [Fact]
    public void AMeterNotFullDoesNotCharge() => Assert.False(ChargesAt(KnightKit(60f), 900f));

    /// <summary>A target 40° off the eye: no charge (the charge steers little).</summary>
    [Fact]
    public void ATargetOffTheEyeIsNotCharged() => Assert.False(ChargesAt(KnightKit(100f), 900f, yaw: 40f));

    /// <summary>A Pyro is never charged: an airblast stops a charge.</summary>
    [Fact]
    public void APyroIsNeverCharged() => Assert.False(ChargesAt(KnightKit(100f), 900f, cls: TFClass.Pyro));

    /// <summary>A target 300 u off, inside the band, is walked to, not charged (the meter would not drain to a crit).</summary>
    [Fact]
    public void ATargetInsideTheBandIsNotCharged() => Assert.False(ChargesAt(KnightKit(100f), 300f));

    /// <summary>The Tide Turner never crits: its band ends at the mini-crit's, so 900 u is not charged and 550 u is.</summary>
    [Fact]
    public void TheTideTurnersBandIsTheMiniCrits()
        => Assert.Equal((false, true), (ChargesAt(KnightKit(100f, tideTurner: true), 900f), ChargesAt(KnightKit(100f, tideTurner: true), 550f)));

    /// <summary>In the charge, the swing goes when the target comes within the charge's reach (250 u: 128 and 0.2 s of the run).</summary>
    [Fact]
    public void TheChargesSwingGoesAtItsReach()
    {
        var f = new TFField(weapons: Weapons) { Hud = KnightKit(100f) };
        IntentCommand charge = f.StepDrawn(Knight(), null, Enemy(Mid + new AiVector(900f, 0f, 0f)));
        Assert.True(Charges(f, in charge));
        f.Hud = KnightKit(70f);
        IntentCommand swing = f.StepDrawn(Knight(), null, Enemy(Mid + new AiVector(250f, 0f, 0f)));
        Assert.Equal(ActionKind.Fire, swing.Action.Kind);
    }

    /// <summary>A sword with stickies and no shield never charges (the second trigger detonates).</summary>
    [Fact]
    public void ASwordWithoutAShieldNeverCharges()
    {
        var hud = new TFHud([Item(TFLoadoutSlot.Primary, 19, "tf_weapon_grenadelauncher", Pipes), Item(TFLoadoutSlot.Secondary, 20, "tf_weapon_pipebomblauncher", Stickies),
                             Item(TFLoadoutSlot.Melee, 132, "tf_weapon_sword", Sword)], [new TFMeter(TFDemomanMeters.Charge, 100f)], []);
        Assert.False(ChargesAt(hud, 900f));
    }

    /// <summary>Of a Soldier and a Sniper in sight at the same distance, the knight goes for the Sniper.</summary>
    [Fact]
    public void AKnightGoesForTheSniper()
    {
        var f = new TFField(weapons: Weapons) { Hud = KnightKit(40f) };
        IntentCommand i = f.StepDrawn(Knight(), null, Enemy(Mid + new AiVector(600f, 100f, 0f)), Enemy(Mid + new AiVector(600f, -100f, 0f), TFClass.Sniper, Other));
        Assert.Equal(Other, f.Decider.Doing.Target);
    }

    /// <summary>With three heads the knight goes to heal at under half his health (60 of the field's 125), which a knight with none does not.</summary>
    [Fact]
    public void ThreeHeadsHealSooner()
    {
        string Branch(int heads)
        {
            var f = new TFField(weapons: Weapons) { Hud = KnightKit(40f, heads) };
            for (int n = 0; n < 3; n++)
                f.Step(Knight(health: 60), TFField.Ctf());
            return f.Branch;
        }

        Assert.Equal((TFDecider.Survive, false), (Branch(3), Branch(0) == TFDecider.Survive));
    }

    /// <summary>The hybrid knight with the pipes in hand and a full meter takes out the sword for a target in the charge's band.</summary>
    [Fact]
    public void TheHybridKnightTakesOutTheSwordToCharge()
    {
        var f = new TFField(weapons: Weapons) { Hud = HybridKnightKit(100f) };
        IntentCommand i = f.StepDrawn(Self(Mid, Pipes, 4), null, Enemy(Mid + new AiVector(700f, 0f, 0f)));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Sword), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>The hybrid knight with the meter not full keeps the pipes at range.</summary>
    [Fact]
    public void TheHybridKnightKeepsThePipesWithTheMeterLow()
    {
        var f = new TFField(weapons: Weapons) { Hud = HybridKnightKit(30f) };
        IntentCommand i = f.StepDrawn(Self(Mid, Pipes, 4), null, Enemy(Mid + new AiVector(700f, 0f, 0f)));
        Assert.NotEqual(ActionKind.SelectWeapon, i.Action.Kind);
    }
}

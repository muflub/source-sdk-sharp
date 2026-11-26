using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.TF2.TFPyroTests;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Pyro's archetypes past the stock one (research pyro.md §4.3's table): each plays differently from Stock on the same
/// stimulus. A bot plays the archetype its seed draws, so each fact picks a seed that draws the one it asks about.
/// </summary>
public sealed class TFPyroArchetypeTests
{
    private const int Flare = 53, Axtinguisher = 54;

    private static readonly WeaponKnowledge ComboWeapons = new([
        .. TFPyroTests.Weapons.All,
        new WeaponFacts(Flare, "tf_weapon_flaregun", 1, 2f, 2f, true, true, 2000f, ProjectileGravity: 120f),
        new WeaponFacts(Axtinguisher, "tf_weapon_fireaxe_axt", 0, 0.8f, 0f, false, false, 0f),
    ]);

    private static TFHud ComboKit()
        => new([Item(TFLoadoutSlot.Primary, 215, TFPyroClass.FlamethrowerClass, Flamer), Item(TFLoadoutSlot.Secondary, 39, "tf_weapon_flaregun", Flare),
                Item(TFLoadoutSlot.Melee, 38, "tf_weapon_fireaxe", Axtinguisher)], [], []);

    private static TFField Playing(string archetype, TFHud kit, Func<DecisionParams, DecisionParams>? tune = null, WeaponKnowledge? weapons = null)
        => new(seed: SeedOf(archetype), weapons: weapons ?? ComboWeapons, tune: tune ?? Never) { Hud = kit };

    private static ActorFacts Facing(ActorId id, AiVector at, float yaw, ActorConditions conditions = 0)
        => new(id, at, default, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.HeavyWeapons, conditions, Facing: new AiAngles(0f, yaw, 0f));

    /// <summary>A burning Heavy 600 u off: the Combo takes out its flare gun (crits on a burning target); Stock keeps its flamethrower (its secondary only past 750 u).</summary>
    [Fact]
    public void TheComboFlaresABurningTargetAtMidRange()
    {
        ActorFacts heavy = Drawn(Enemy, Mid + new AiVector(600f, 0f, 0f), TFField.Blue, conditions: ActorConditions.Burning);
        IntentCommand combo = Playing(TFPyroClass.Combo, ComboKit()).StepDrawn(Pyro(Mid), TFField.Ctf(), heavy);
        IntentCommand stock = Playing(TFPyroClass.Stock, ComboKit()).StepDrawn(Pyro(Mid), TFField.Ctf(), heavy);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Flare), (combo.Action.Kind, combo.Action.WeaponSlot));
        Assert.NotEqual(ActionKind.SelectWeapon, stock.Action.Kind);
    }

    /// <summary>A burning Heavy 90 u off has the Combo take out its Axtinguisher; one not burning keeps the Degreaser on him.</summary>
    [Fact]
    public void TheComboAxesABurningTargetInReach()
    {
        IntentCommand At(ActorConditions conditions)
            => Playing(TFPyroClass.Combo, ComboKit()).StepDrawn(Pyro(Mid), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(90f, 0f, 0f), TFField.Blue, conditions: conditions));

        IntentCommand burning = At(ActorConditions.Burning), cold = At(0);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Axtinguisher), (burning.Action.Kind, burning.Action.WeaponSlot));
        Assert.Equal(ActionKind.Fire, cold.Action.Kind);
    }

    /// <summary>The Combo holds its range on a burning target 400 u off (it flares him); Stock closes on the same target.</summary>
    [Fact]
    public void TheComboHoldsRangeOnABurningTarget()
    {
        ActorFacts heavy = Drawn(Enemy, Mid + new AiVector(400f, 0f, 0f), TFField.Blue, conditions: ActorConditions.Burning);
        IntentCommand combo = Playing(TFPyroClass.Combo, ComboKit()).StepDrawn(Pyro(Mid, weapon: Flare), TFField.Ctf(), heavy);
        IntentCommand stock = Playing(TFPyroClass.Stock, Kit()).StepDrawn(Pyro(Mid), TFField.Ctf(), heavy);
        Assert.True(combo.Move.Direction.X < 0.5f, $"the Combo moved {combo.Move.Direction}");
        Assert.True(stock.Move.Direction.X > 0.9f, $"Stock moved {stock.Move.Direction}");
    }

    /// <summary>A Heavy 200 u off facing the Backburner is not flamed; the same Heavy facing away is (the back crit).</summary>
    [Fact]
    public void TheBackburnerFlamesOnlyABack()
    {
        ActionKind At(float yaw)
            => Playing(TFPyroClass.Backburner, Kit(Item(TFLoadoutSlot.Primary, 40, TFPyroClass.FlamethrowerClass, Flamer)))
               .StepDrawn(Pyro(Mid), TFField.Ctf(), Facing(Enemy, Mid + new AiVector(200f, 0f, 0f), yaw)).Action.Kind;

        Assert.Equal((ActionKind.None, ActionKind.Fire), (At(180f), At(0f)));
    }

    /// <summary>A Heavy 300 u off facing the Backburner is gone round: its move is mostly across his line. Stock closes straight in.</summary>
    [Fact]
    public void TheBackburnerGoesRoundAFacingTarget()
    {
        ActorFacts heavy = Facing(Enemy, Mid + new AiVector(300f, 0f, 0f), 180f);
        IntentCommand back = Playing(TFPyroClass.Backburner, Kit(Item(TFLoadoutSlot.Primary, 40, TFPyroClass.FlamethrowerClass, Flamer))).StepDrawn(Pyro(Mid), TFField.Ctf(), heavy);
        IntentCommand stock = Playing(TFPyroClass.Stock, Kit()).StepDrawn(Pyro(Mid), TFField.Ctf(), heavy);
        Assert.True(MathF.Abs(back.Move.Direction.Y) > MathF.Abs(back.Move.Direction.X), $"the Backburner moved {back.Move.Direction}");
        Assert.True(stock.Move.Direction.X > 0.9f, $"Stock moved {stock.Move.Direction}");
    }

    /// <summary>
    /// Over 60 decides with a Heavy 200 u in front, Stock shoves him now and then ("pushy" half of each 3 s); the Backburner,
    /// whose blast costs 50 of its 200, never does.
    /// </summary>
    [Fact]
    public void TheBackburnerShovesNoOneButAnUber()
    {
        int Shoves(string archetype, TFHud kit)
        {
            TFField f = Playing(archetype, kit, Sure, TFPyroTests.Weapons);
            return Enumerable.Range(0, 60).Count(_ =>
            {
                IntentCommand i = f.StepDrawn(Pyro(Mid), TFField.Ctf(), Facing(Enemy, Mid + new AiVector(200f, 0f, 0f), 0f));
                return Ability(f, in i) == TFPyroClass.Airblast;
            });
        }

        Assert.True(Shoves(TFPyroClass.Stock, Kit()) > 0);
        Assert.Equal(0, Shoves(TFPyroClass.Backburner, Kit(Item(TFLoadoutSlot.Primary, 40, TFPyroClass.FlamethrowerClass, Flamer,
                                                                  new TFOwnKitAttribute("mult_airblast_cost", 2.5f)))));
    }

    private static readonly ActorId Medic = new(4, 0x4000_0001);

    /// <summary>A Guard with a Medic in sight 700 u off and no enemy about goes to him (its own place); Stock does not.</summary>
    [Fact]
    public void TheGuardGoesBackToItsCharge()
    {
        ActorFacts medic = Drawn(Medic, Mid + new AiVector(-700f, 0f, 0f), TFField.Red, TFClass.Medic);
        TFField guard = new(seed: SeedOf(TFPyroClass.Guard), weapons: TFPyroTests.Weapons, tune: Never, mates: [Medic.Slot]) { Hud = Kit() };
        TFField stock = new(seed: SeedOf(TFPyroClass.Stock), weapons: TFPyroTests.Weapons, tune: Never, mates: [Medic.Slot]) { Hud = Kit() };
        guard.StepDrawn(Pyro(Mid), TFField.Ctf(), medic);
        stock.StepDrawn(Pyro(Mid), TFField.Ctf(), medic);
        Assert.Equal(TFDecider.ClassBranch, guard.Branch);
        Assert.NotEqual(TFDecider.ClassBranch, stock.Branch);
    }

    /// <summary>
    /// The Guard keeps its tank for reflects (review C6, 2026-11-21: shoving every enemy at each refire emptied it in about
    /// 8 s): over 200 decides with a Heavy 200 u in front and no charge in sight it never shoves him, where Stock's "pushy"
    /// draw does; with its Medic 100 u from the Heavy it does, while half its tank is left.
    /// </summary>
    [Fact]
    public void TheGuardShovesOnlyWhatThreatensItsCharge()
    {
        int Shoves(string archetype, bool medic, int reserve)
        {
            TFField f = new(seed: SeedOf(archetype), weapons: TFPyroTests.Weapons, tune: Sure, mates: [Medic.Slot]) { Hud = Kit() };
            ActorFacts heavy = Facing(Enemy, Mid + new AiVector(200f, 0f, 0f), 180f);
            ActorFacts charge = Drawn(Medic, Mid + new AiVector(100f, 60f, 0f), TFField.Red, TFClass.Medic);
            return Enumerable.Range(0, 200).Count(_ =>
            {
                IntentCommand i = medic ? f.StepDrawn(Pyro(Mid, reserve: reserve), TFField.Ctf(), heavy, charge) : f.StepDrawn(Pyro(Mid, reserve: reserve), TFField.Ctf(), heavy);
                return Ability(f, in i) == TFPyroClass.Airblast;
            });
        }

        Assert.Equal(0, Shoves(TFPyroClass.Guard, medic: false, reserve: 200));
        Assert.True(Shoves(TFPyroClass.Stock, medic: false, reserve: 200) > 0);
        Assert.True(Shoves(TFPyroClass.Guard, medic: true, reserve: 200) > 0);
        Assert.Equal(0, Shoves(TFPyroClass.Guard, medic: true, reserve: 90));
    }

    private static TFHud PhlogKit(float rage, float draining = 0f)
        => new([Item(TFLoadoutSlot.Primary, 594, TFPyroClass.FlamethrowerClass, Flamer, new TFOwnKitAttribute("airblast_disabled", 1f)),
                Item(TFLoadoutSlot.Secondary, 12, "tf_weapon_shotgun", PyroShotgun), Item(TFLoadoutSlot.Melee, 2, "tf_weapon_fireaxe", Axe)],
               [new TFMeter(TFPyroMeters.Rage, rage), new TFMeter(TFPyroMeters.RageDraining, draining)], []);

    /// <summary>The Phlogistinator with a full meter and nobody in sight taunts (its Mmmph row); half full, it does not.</summary>
    [Fact]
    public void AFullMeterIsTauntedIntoCrits()
    {
        string With(float rage)
        {
            TFField f = Playing(TFPyroClass.Phlog, PhlogKit(rage), weapons: TFPyroTests.Weapons);
            IntentCommand i = f.StepDrawn(Pyro(Mid), TFField.Ctf());
            return Ability(f, in i);
        }

        Assert.Equal((TFPyroClass.Mmmph, ""), (With(1f), With(0.5f)));
    }

    /// <summary>The Phlogistinator does not taunt with an enemy in sight 400 u off (the taunt waits for a clear 600 u).</summary>
    [Fact]
    public void NoTauntWithAnEnemyNear()
    {
        TFField f = Playing(TFPyroClass.Phlog, PhlogKit(1f), weapons: TFPyroTests.Weapons);
        IntentCommand i = f.StepDrawn(Pyro(Mid), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(400f, 0f, 0f), TFField.Blue));
        Assert.NotEqual(TFPyroClass.Mmmph, Ability(f, in i));
    }

    /// <summary>Each archetype carries its research loadout: the Combo 215 / 39 / 38, the Backburner 40, the Phlogistinator 594.</summary>
    [Fact]
    public void TheArchetypesCarryTheirLoadouts()
    {
        TFArchetype Row(string name) => TFPyroClass.Archetypes().Rows.Single(a => a.Name == name);
        Assert.Equal((215, 39, 38), (Row(TFPyroClass.Combo).Prefer(TFLoadoutSlot.Primary)[0], Row(TFPyroClass.Combo).Prefer(TFLoadoutSlot.Secondary)[0],
                                     Row(TFPyroClass.Combo).Prefer(TFLoadoutSlot.Melee)[0]));
        Assert.Equal((40, 594), (Row(TFPyroClass.Backburner).Prefer(TFLoadoutSlot.Primary)[0], Row(TFPyroClass.Phlog).Prefer(TFLoadoutSlot.Primary)[0]));
    }
}

using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Pyro's play (plan_bots_teams_tf2 P7, research pyro.md §4.3, stock tf_bot's Pyro): the flame only inside its reach,
/// closing and circling and never backing off, the weapon by range and by whom it fights, and the compression blast:
/// reflect, shove, extinguish, never without the flamethrower's reserve or on a flamethrower that has none.
/// </summary>
public sealed class TFPyroTests
{
    internal static readonly ActorId Enemy = new(5, 0x4000_0001), Mate = new(4, 0x4000_0001);
    internal static readonly AiVector Mid = new(2048f, 256f, 0f);

    internal const int Flamer = 50, PyroShotgun = 51, Axe = 52;

    internal static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Flamer, TFPyroClass.FlamethrowerClass, 0, 0.04f, 0f, false, false, 0f),
        new WeaponFacts(PyroShotgun, "tf_weapon_shotgun_pyro", 6, 0.625f, 3f, true, true, 0f),
        new WeaponFacts(Axe, "tf_weapon_fireaxe", 0, 0.8f, 0f, false, false, 0f),
    ]);

    internal static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params TFOwnKitAttribute[] attributes)
        => new(slot, defindex, itemClass, kind, [.. attributes]);

    /// <summary>The stock Pyro's kit; <paramref name="flame"/> stands in for the primary (a Backburner, a Phlogistinator).</summary>
    internal static TFHud Kit(TFKitItem? flame = null)
        => new([flame ?? Item(TFLoadoutSlot.Primary, 21, TFPyroClass.FlamethrowerClass, Flamer), Item(TFLoadoutSlot.Secondary, 12, "tf_weapon_shotgun", PyroShotgun),
                Item(TFLoadoutSlot.Melee, 2, "tf_weapon_fireaxe", Axe)], [], []);

    /// <summary>The bot, a red Pyro at <paramref name="at"/> holding <paramref name="weapon"/>, its view along +x.</summary>
    internal static SelfState Pyro(AiVector at, int weapon = Flamer, int reserve = 200, int health = 175)
        => TFField.Self(at, health: health, clip: -1, reserve: reserve, weapon: weapon, playerClass: TFClass.Pyro);

    /// <summary>A Pyro bot on the field playing the Stock archetype (its seed draws it).</summary>
    internal static TFField Field(Func<DecisionParams, DecisionParams>? tune = null, TFHud? kit = null, int[]? mates = null)
        => new(seed: SeedOf(TFPyroClass.Stock), weapons: Weapons, tune: tune, mates: mates) { Hud = kit ?? Kit() };

    /// <summary>The first seed whose draw of the Pyro's archetypes is <paramref name="name"/>.</summary>
    internal static ulong SeedOf(string name)
    {
        TFArchetypeTable table = TFPyroClass.Archetypes();
        for (ulong seed = 1; seed < 10_000; seed++)
        {
            if (table.Draw(seed)?.Name == name)
                return seed;
        }

        throw new InvalidOperationException($"no seed draws {name}");
    }

    internal static ActorFacts Drawn(ActorId id, AiVector at, int team, TFClass playerClass = TFClass.HeavyWeapons, ActorConditions conditions = 0, AiVector velocity = default)
        => new(id, at, velocity, TFField.Launcher, ActorActivity.None, 1f, team, (int)playerClass, conditions);

    /// <summary>The ability row's name an intent's one-shot names, "" for none.</summary>
    internal static string Ability(TFField f, in IntentCommand i)
        => i.Action.Kind == ActionKind.Ability && f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow row) ? row.Name : "";

    /// <summary>A Pyro that never reflects or shoves (stock's Easy): its fire and hands alone.</summary>
    internal static DecisionParams Never(DecisionParams p) => p with { ReflectChance = 0f };

    /// <summary>A sure, instant reflex: every second skilled, no reaction time.</summary>
    internal static DecisionParams Sure(DecisionParams p) => p with { ReflectChance = 1f, ReactionMs = 0f };

    /// <summary>A Pyro with a Heavy 600 u off closes and holds its fire; at 200 u it fires (stock's 250 u reach; the general play fires to 2,000 u).</summary>
    [Fact]
    public void TheFlameIsFiredOnlyInsideItsReach()
    {
        ActionKind At(float distance)
        {
            TFField f = Field(Never);
            return f.StepDrawn(Pyro(Mid), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(distance, 0f, 0f), TFField.Blue)).Action.Kind;
        }

        Assert.Equal((ActionKind.None, ActionKind.Fire), (At(600f), At(200f)));
    }

    /// <summary>
    /// A Pyro with its target 60 u off does not back away (stock's Pyro never retreats in a fight): its move has no part away
    /// from the target. The general play backs off anything nearer than 320 u.
    /// </summary>
    [Fact]
    public void APyroNeverBacksOffItsTarget()
    {
        TFField f = Field();
        AiVector enemy = Mid + new AiVector(60f, 0f, 0f);
        for (int n = 0; n < 20; n++)
        {
            IntentCommand i = f.StepDrawn(Pyro(Mid), TFField.Ctf(), Drawn(Enemy, enemy, TFField.Blue));
            Assert.True(i.Move.Kind != MoveKind.Strafe || i.Move.Direction.X >= 0f, $"decide {n}: moved {i.Move.Direction}");
        }
    }

    /// <summary>A Pyro 400 u from its target closes on it: its move heads at the target (the general play holds 320-900 u).</summary>
    [Fact]
    public void APyroClosesOnAFarTarget()
    {
        TFField f = Field();
        IntentCommand i = f.StepDrawn(Pyro(Mid), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(400f, 0f, 0f), TFField.Blue));
        Assert.Equal(MoveKind.Strafe, i.Move.Kind);
        Assert.True(i.Move.Direction.X > 0.9f, $"moved {i.Move.Direction}");
    }

    /// <summary>A Heavy 900 u off has the Pyro take out its secondary (stock: past 750 u); a Soldier at 900 u keeps the flamethrower out, for his rockets.</summary>
    [Fact]
    public void PastItsReachThePyroTakesItsSecondaryButNotAgainstASoldier()
    {
        IntentCommand At(TFClass them)
        {
            TFField f = Field();
            return f.StepDrawn(Pyro(Mid), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(900f, 0f, 0f), TFField.Blue, them));
        }

        IntentCommand heavy = At(TFClass.HeavyWeapons), soldier = At(TFClass.Soldier);
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + PyroShotgun), (heavy.Action.Kind, heavy.Action.WeaponSlot));
        Assert.NotEqual(ActionKind.SelectWeapon, soldier.Action.Kind);
    }

    /// <summary>An enemy Pyro at 200 u, whom afterburn does not hurt, is not flamed: the hands take out the shotgun.</summary>
    [Fact]
    public void AnEnemyPyroIsShotNotFlamed()
    {
        TFField f = Field(Never);
        IntentCommand i = f.StepDrawn(Pyro(Mid), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(200f, 0f, 0f), TFField.Blue, TFClass.Pyro));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + PyroShotgun), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>
    /// A Pyro holding its shotgun with a Heavy 200 u off takes the flamethrower out, though its trigger is busy every decide
    /// (live, 2026-11-21: the choice in the hands after the fire never ran while the shotgun fired, so a Pyro that took it
    /// out at range kept it to the end of the fight).
    /// </summary>
    [Fact]
    public void ANearTargetHasTheShotgunPutAwayForTheFlame()
    {
        TFField f = Field(Never);
        IntentCommand i = f.StepDrawn(Pyro(Mid, weapon: PyroShotgun, reserve: 32), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(200f, 0f, 0f), TFField.Blue));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + Flamer), (i.Action.Kind, i.Action.WeaponSlot));
    }

    /// <summary>A Pyro whose flamethrower is empty takes out its shotgun at 200 u.</summary>
    [Fact]
    public void AnEmptyFlamethrowerIsSwappedForTheShotgun()
    {
        TFField f = Field(Never);
        IntentCommand i = f.StepDrawn(Pyro(Mid, reserve: 0), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(200f, 0f, 0f), TFField.Blue));
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + PyroShotgun), (i.Action.Kind, i.Action.WeaponSlot));
    }

    private static ThingFacts Rocket(AiVector at, AiVector velocity, int id = 900)
        => new(id, ThingKind.Projectile, (int)TFThingType.Rocket, at, velocity, TFField.Blue, 9);

    /// <summary>The bot's eye: a Pyro's over its feet.</summary>
    private static AiVector Eye => Mid + new AiVector(0f, 0f, 68f);

    /// <summary>
    /// An enemy rocket 100 u in front, heading at the bot, is reflected by a sure Pyro (the blast's row); by one that never
    /// reflects (dodge chance 0, stock's Easy) it is not.
    /// </summary>
    [Fact]
    public void ARocketHeadingAtThePyroIsReflectedBySkill()
    {
        string With(float chance)
        {
            TFField f = Field(p => Sure(p) with { ReflectChance = chance });
            IntentCommand i = f.StepSeeing(Pyro(Mid), TFField.Ctf(), [Rocket(Eye + new AiVector(100f, 0f, 0f), new AiVector(-1100f, 0f, 0f))]);
            return Ability(f, in i);
        }

        Assert.Equal((TFPyroClass.Airblast, ""), (With(1f), With(0f)));
    }

    /// <summary>An enemy flare 100 u in front is reflected too (the game sends flares back since the Pyro lane's TFFlameEffects arm).</summary>
    [Fact]
    public void AnEnemyFlareIsReflected()
    {
        TFField f = Field(Sure);
        var flare = new ThingFacts(901, ThingKind.Projectile, (int)TFThingType.Flare, Eye + new AiVector(100f, 0f, 0f), new AiVector(-2000f, 0f, 0f), TFField.Blue, 9);
        IntentCommand i = f.StepSeeing(Pyro(Mid), TFField.Ctf(), [flare]);
        Assert.Equal(TFPyroClass.Airblast, Ability(f, in i));
    }

    /// <summary>A rocket in the blast's reach that passes across the bot's front (stock's deflect tolerance) is not reflected.</summary>
    [Fact]
    public void ARocketPassingWideIsNotReflected()
    {
        TFField f = Field(Sure);
        IntentCommand i = f.StepSeeing(Pyro(Mid), TFField.Ctf(), [Rocket(Eye + new AiVector(100f, 0f, 0f), new AiVector(0f, 1100f, 0f))]);
        Assert.Equal("", Ability(f, in i));
    }

    /// <summary>With the flamethrower's reserve under the blast's cost (20), nothing is reflected.</summary>
    [Fact]
    public void NoBlastUnderItsCost()
    {
        TFField f = Field(Sure);
        IntentCommand i = f.StepSeeing(Pyro(Mid, reserve: 19), TFField.Ctf(), [Rocket(Eye + new AiVector(100f, 0f, 0f), new AiVector(-1100f, 0f, 0f))]);
        Assert.Equal("", Ability(f, in i));
    }

    /// <summary>A Phlogistinator (airblast_disabled) never blasts, a reflectable rocket in front or not.</summary>
    [Fact]
    public void APhlogistinatorNeverBlasts()
    {
        TFField f = Field(Sure, Kit(Item(TFLoadoutSlot.Primary, 594, TFPyroClass.FlamethrowerClass, Flamer, new TFOwnKitAttribute("airblast_disabled", 1f))));
        IntentCommand i = f.StepSeeing(Pyro(Mid), TFField.Ctf(), [Rocket(Eye + new AiVector(100f, 0f, 0f), new AiVector(-1100f, 0f, 0f))]);
        Assert.Equal("", Ability(f, in i));
    }

    /// <summary>An Übered enemy 200 u in front is shoved, and the Pyro does not back away from him as the general play does.</summary>
    [Fact]
    public void AnUberIsShovedNotBackedAwayFrom()
    {
        TFField f = Field(Sure);
        IntentCommand i = f.StepDrawn(Pyro(Mid), TFField.Ctf(), Drawn(Enemy, Mid + new AiVector(200f, 0f, 0f), TFField.Blue, conditions: ActorConditions.Invulnerable));
        Assert.Equal(TFPyroClass.Airblast, Ability(f, in i));
        Assert.Equal(0, f.Decider.InvulnerableBackOffs);
    }

    /// <summary>A burning teammate 120 u in front, with no enemy about, is put out by the blast.</summary>
    [Fact]
    public void ABurningTeammateIsPutOut()
    {
        TFField f = Field(Sure, mates: [Mate.Slot]);
        IntentCommand i = f.StepDrawn(Pyro(Mid), TFField.Ctf(), Drawn(Mate, Mid + new AiVector(120f, 0f, 0f), TFField.Red, TFClass.Soldier, ActorConditions.Burning));
        Assert.Equal(TFPyroClass.Airblast, Ability(f, in i));
    }

    /// <summary>A teammate who is not burning, in the same place, is not blasted.</summary>
    [Fact]
    public void ATeammateNotBurningIsNotBlasted()
    {
        TFField f = Field(Sure, mates: [Mate.Slot]);
        IntentCommand i = f.StepDrawn(Pyro(Mid), TFField.Ctf(), Drawn(Mate, Mid + new AiVector(120f, 0f, 0f), TFField.Red, TFClass.Soldier));
        Assert.Equal("", Ability(f, in i));
    }

    /// <summary>The Pyro's stock archetype carries the Flame Thrower, the Pyro's Shotgun and the Fire Axe.</summary>
    [Fact]
    public void TheStockArchetypeIsTheStockLoadout()
    {
        TFArchetype stock = TFPyroClass.Archetypes().Rows.Single(a => a.Name == TFPyroClass.Stock);
        Assert.Equal((21, 12, 2), (stock.Prefer(TFLoadoutSlot.Primary)[0], stock.Prefer(TFLoadoutSlot.Secondary)[0], stock.Prefer(TFLoadoutSlot.Melee)[0]));
    }
}

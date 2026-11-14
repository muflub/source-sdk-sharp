using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Player;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Medigun;

/// <summary>
/// The Amputator's healing taunt (<see cref="TFTauntHeal"/>): the taunt's
/// start and the attribute gate, the one-second pulse's reach, ring and
/// 25-a-second heal, the team, disguise, sightline and blocked-hand gates,
/// and the ends — the taunt's, the death's and the walk-out's.
/// </summary>
public sealed class TFTauntHealTests
{
    private const int Medic = 5;
    private const int Patient = 6;

    /// <summary>The rig's default team, the medic's; the enemies wear the other.</summary>
    private const int MedicTeam = 0;
    private const int Blue = 3;

    private static EntityHandle H(int index) => MedigunRig.HandleFor(index);

    /// <summary>A carrier state with storage of its own (the net states bind
    /// to nothing here): the class when one is named.</summary>
    private static TFPlayerNetState NewState(int playerClass = 0)
    {
        var state = new TFPlayerNetState();
        state.BindLocal();
        if (playerClass != 0)
            state.PlayerClass = playerClass;
        return state;
    }

    private static AttributeContainer Item(string attribute, float value)
    {
        var item = new AttributeContainer();
        item.Set(AttributeValue.OfFloat(new AttributeDefinition(304, attribute, attribute,
            AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral, false, false,
            AttributeValueType.Default, MergedKeys.Block("x")), value));
        return item;
    }

    /// <summary>The aura's world, nothing ticked: the taunting medic, a
    /// patient carrier 300 units out, the bonesaw's say-so.</summary>
    private static MedigunRig RigWithAura()
    {
        var rig = new MedigunRig();
        rig.Data.States[Medic] = NewState((int)TFClass.Medic);
        rig.Data.States[Medic].SetConditions(TFCondMask.Of(TFCond.Taunting));
        rig.Data.States[Patient] = NewState();
        rig.Owners.Held[Medic] = "tf_weapon_bonesaw";
        rig.Owners.Items[Medic] = Item(TFTauntHeal.EnablesAoeHeal, 1f);
        rig.Origins[Medic] = Vector.Zero;
        rig.Origins[Patient] = new Vector(300f, 0f, 0f);
        return rig;
    }

    private static TFTauntHeal HealerOf(MedigunRig rig)
        => new(rig.Data, rig.Conditions, rig.Engine, rig.Engine, rig.Engine,
               rig.Owners, rig.Hooks, rig.Healing);

    /// <summary>The aura's first pulse, taken.</summary>
    private static (MedigunRig Rig, TFTauntHeal Heal) HealingAura(float now = 10f)
    {
        MedigunRig rig = RigWithAura();
        TFTauntHeal heal = HealerOf(rig);
        heal.Attach(H(Medic));
        heal.Tick(now);
        return (rig, heal);
    }

    [Fact]
    public void The_taunting_amputator_heals_the_teammate_beside_it()
    {
        var (rig, _) = HealingAura();

        Assert.Equal(1, rig.Healing.NumHealers(Patient));
        Assert.Contains(rig.Conditions.Calls, c => c.StartsWith("add 6 Radiusheal d1.2"));
    }

    [Fact]
    public void The_aura_heals_at_25_a_second()
    {
        var (rig, _) = HealingAura();
        rig.Give(Patient, 100, 150);
        rig.NoteDamage(Patient, time: 10f);

        rig.Healing.TickPatient(H(Patient), now: 10f, frametime: 1f);   // the pulse's entry, a second of it

        Assert.Contains("take 6 25 True", rig.Resupply.Takes);
    }

    [Fact]
    public void Without_the_attribute_the_taunt_heals_nothing()
    {
        var rig = RigWithAura();
        rig.Owners.Held.Clear();                            // no held weapon: no say-so
        var heal = HealerOf(rig);
        heal.Attach(H(Medic));

        heal.Tick(10f);

        Assert.Equal(0, rig.Healing.NumHealers(Patient));
        Assert.DoesNotContain(rig.Conditions.Calls, c => c.Contains("Radiusheal"));
    }

    [Fact]
    public void The_reach_is_450()
    {
        var rig = RigWithAura();
        const int inner = 7;
        const int outer = 8;
        rig.Data.States[inner] = NewState();
        rig.Data.States[outer] = NewState();
        rig.Origins[inner] = new Vector(449f, 0f, 0f);
        rig.Origins[outer] = new Vector(451f, 0f, 0f);
        var heal = HealerOf(rig);
        heal.Attach(H(Medic));

        heal.Tick(10f);

        Assert.Equal(1, rig.Healing.NumHealers(inner));     // :14333, inside
        Assert.Equal(0, rig.Healing.NumHealers(outer));     // and outside it
    }

    [Fact]
    public void The_pulse_repeats_each_second_and_heals_once()
    {
        var (rig, heal) = HealingAura();

        heal.Tick(10.5f);                                   // between pulses: nothing new
        Assert.Single(rig.Conditions.Calls, c => c.Contains("add 6 Radiusheal"));

        heal.Tick(11f);                                     // :14362 — the next rescan
        Assert.Equal(2, rig.Conditions.Calls.Count(c => c.Contains("add 6 Radiusheal")));
        Assert.Equal(1, rig.Healing.NumHealers(Patient));   // :14346, already healing them
    }

    [Fact]
    public void The_taunts_end_stops_the_heal()
    {
        var (rig, heal) = HealingAura();
        Assert.Equal(1, rig.Healing.NumHealers(Patient));

        rig.Data.States[Medic].SetConditions(default);
        heal.Tick(11f);                                     // OnRemoveTaunting's Heal_Radius(false)

        Assert.Equal(0, rig.Healing.NumHealers(Patient));
        Assert.Contains("remove 6 HealthBuff", rig.Conditions.Calls);
    }

    [Fact]
    public void The_medics_death_ends_the_heal()
    {
        var (rig, heal) = HealingAura();

        rig.Dead.Add(Medic);
        heal.Tick(11f);                                     // the validity gate's dead arm (:14294)

        Assert.Equal(0, rig.Healing.NumHealers(Patient));
    }

    [Fact]
    public void The_patient_who_walks_out_is_released()
    {
        var (rig, heal) = HealingAura();

        rig.Origins[Patient] = new Vector(1000f, 0f, 0f);
        heal.Tick(11f);                                     // :14352-14359

        Assert.Equal(0, rig.Healing.NumHealers(Patient));
    }

    [Fact]
    public void A_wall_between_the_centres_hides_a_patient()
    {
        var rig = RigWithAura();
        rig.QueueBlocked(new Vector(150f, 0f, 0f), H(Medic));   // the sightline ask, blocked
        var heal = HealerOf(rig);
        heal.Attach(H(Medic));

        heal.Tick(10f);                                     // :14339-14340

        Assert.Equal(0, rig.Healing.NumHealers(Patient));
    }

    [Fact]
    public void A_visible_enemy_is_nobody_to_the_aura()
    {
        var rig = RigWithAura();
        rig.Teams[Patient] = 2;
        var heal = HealerOf(rig);
        heal.Attach(H(Medic));

        heal.Tick(10f);                                     // :14313-14316, not a teammate

        Assert.Equal(0, rig.Healing.NumHealers(Patient));
    }

    [Fact]
    public void A_cloaked_enemy_is_reached_and_so_is_a_disguise_onto_the_medic_s_team()
    {
        var rig = RigWithAura();
        const int cloaked = 7;
        const int disguised = 8;
        rig.Teams[cloaked] = Blue;
        rig.Teams[disguised] = Blue;
        rig.Data.States[cloaked] = NewState();
        rig.Data.States[cloaked].SetConditions(TFCondMask.Of(TFCond.Stealthed));
        rig.Data.States[disguised] = NewState();
        rig.Data.States[disguised].DisguiseTeam = MedicTeam;
        rig.Data.States[disguised].SetConditions(TFCondMask.Of(TFCond.Disguised));
        var heal = HealerOf(rig);
        heal.Attach(H(Medic));

        heal.Tick(10f);                                     // :14315's stealth pass, :14318's disguise match

        Assert.Equal(1, rig.Healing.NumHealers(Patient));   // the teammate, on the default team
        Assert.Equal(1, rig.Healing.NumHealers(cloaked));
        Assert.Equal(1, rig.Healing.NumHealers(disguised));
    }

    [Fact]
    public void A_disguise_onto_the_other_team_is_missed()
    {
        var rig = RigWithAura();
        rig.Teams[Patient] = Blue;
        rig.Data.States[Patient].DisguiseTeam = Blue;       // showing their own colours, not the medic's
        rig.Data.States[Patient].SetConditions(TFCondMask.Of(TFCond.Disguised));
        var heal = HealerOf(rig);
        heal.Attach(H(Medic));

        heal.Tick(10f);                                     // :14318, the wrong disguise

        Assert.Equal(0, rig.Healing.NumHealers(Patient));
    }

    [Fact]
    public void A_hand_that_blocks_healing_is_skipped()
    {
        var rig = RigWithAura();
        rig.Owners.Held[Patient] = "tf_weapon_bonesaw";
        rig.Owners.Items[Patient] = Item(TFTauntHeal.BlocksHealing, 1f);
        var heal = HealerOf(rig);
        heal.Attach(H(Medic));

        heal.Tick(10f);                                     // :14322-14330

        Assert.Equal(0, rig.Healing.NumHealers(Patient));
    }

    [Fact]
    public void The_medic_heals_not_himself()
    {
        var (rig, _) = HealingAura();

        Assert.Equal(1, rig.Healing.NumHealers(Patient));
        Assert.Equal(0, rig.Healing.NumHealers(Medic));     // :14310, no proc on the caster
    }
}

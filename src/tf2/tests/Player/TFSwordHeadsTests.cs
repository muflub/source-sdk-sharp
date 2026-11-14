using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Movement;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The demoman's heads (<see cref="TFSwordHeads"/>): the sword kill takes the
/// victim's pile and one more, the heads pay health and speed while the
/// count lives, and the count dies with its holder.
/// </summary>
public sealed class TFSwordHeadsTests
{
    private static readonly EntityHandle Killer = PlayerRig.Player;

    /// <summary>The dead teammate, the rig's second carrier (PlayerRig.Join's own shape).</summary>
    private const int VictimIndex = 4;
    private static readonly EntityHandle Victim = new(0x0002_0000u | VictimIndex);

    /// <summary>The two players, the killer's sword arm and the hook over them.</summary>
    private sealed class Rig
    {
        public readonly PlayerRig Player = new();
        public readonly TFPlayerConditions Conditions;
        public readonly TFPlayerResupply Resupply;
        public readonly TFSwordHeads Heads;
        public readonly TFDamageRecords Records = new();
        public readonly TF2.Tests.Weapons.FakeOwners Owners = new();
        public readonly AttributeHooks Hooks = new();

        public Rig()
        {
            Player.As(TFClass.Demoman);
            Player.Join(VictimIndex);
            Conditions = new TFPlayerConditions(Player.Carriers, Player.Engine, new TF2.Tests.Rules.ListHooks<ITFConditionHook>(), Player.Engine);
            Resupply = new TFPlayerResupply(Player.Carriers, Player.Classes, Player.Engine, Player.Engine,
                                            Conditions, Player.Loadout, Player.Engine, Player.Items, Hooks,
                                            scripts: new TFWeaponScripts(Player.Log, TF2.Tests.Weapons.WeaponFixtures.ShippedWithClasses()),
                                            catalog: Player.Catalog,
                                            arsenal: new Arsenal(Player.Log, Player.Engine, Player.Engine, Player.Engine, Player.Engine,
                                                                 Player.Engine, Player.Engine, Player.Engine, Player.Engine,
                                                                 new ScriptedEntityRegistry(TestEngines.Server()), Player.Engine,
                                                                 new DamageHitGroups()),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: Player.Engine, equipHooks: new TF2.Tests.Rules.ListHooks<ITFWeaponEquipHook>());
            Heads = new TFSwordHeads(Player.Carriers, Owners, Hooks, Records, Resupply,
                                     new FixedHealing(), Player.Engine);
        }

        public TFPlayerNetState KillerState => Player.Carriers.Get(PlayerRig.Index)!;

        public TFPlayerNetState VictimState => Player.Carriers.Get(VictimIndex)!;
    }

    /// <summary>A death the killer's sword dealt, as the damage layer's record holds it.</summary>
    private static void SwordKill(Rig rig) =>
        rig.Records.Note(VictimIndex, new TFHitRecord(Killer, 65f, DamageType.Generic, TFCritType.None,
                                                      TFDamageCustom.Decapitation, TFWeaponId.Sword,
                                                      "tf_weapon_sword", 10f), damager: false);

    private static PlayerDeath TheDeath => new(Victim, VictimIndex, Killer, default, EntityHandle.None);

    /// <summary>The killer's sword, decapitating, their class the demoman's.</summary>
    private static void Arms(Rig rig)
    {
        rig.Owners.Classes[PlayerRig.Index] = TFClass.Demoman;
        rig.Owners.ByName[PlayerRig.Index] = new() { ["tf_weapon_sword"] =
            Arm("decapitate_type", 1f, AttributeDescriptionFormat.Additive, 6011) };
        rig.Player.Items.Set(PlayerRig.Index, new Dictionary<string, IAttributeProvider> { ["tf_weapon_sword"] =
            Arm("decapitate_type", 1f, AttributeDescriptionFormat.Additive, 6011) });   // the resupply's own table
    }

    private static IAttributeProvider Arm(string name, float value, AttributeDescriptionFormat format, int defindex)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(defindex, $"attr {defindex}", name, format,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    [Fact]
    public void AKillTakesAHeadAndHeadsRaiseMaxHealthAndSpeed()
    {
        var rig = new Rig();
        Arms(rig);
        rig.VictimState.Decapitations = 2;
        var movement = TFMovementFixtures.Rules(new CarrierData(rig.Player.Carriers),
                                                owners: rig.Owners, hooks: rig.Hooks);
        float plain = movement.MaxSpeedScale(PlayerRig.Index, 175f, ducking: false, groundMaterial: 0);

        SwordKill(rig);
        rig.Heads.OnPlayerDied(TheDeath);

        Assert.Equal(3, rig.KillerState.Decapitations);         // their own none, the victim's two, one more
        Assert.Equal(TFMovementFixtures.Server()[TFClass.Demoman].MaxHealth + 3 * TFSwordHook.HealthPerHead,
                     rig.Resupply.MaxHealth(Killer));           // the demoman's max, +15 a head
        Assert.Equal(115, rig.Player.Engine.AllOf("Entities.SetHealth").Last().ArgAs<int>(1));
        Assert.Equal(plain * (1f + 3 * TFSwordHook.SpeedPerHead),
                     movement.MaxSpeedScale(PlayerRig.Index, 175f, ducking: false, groundMaterial: 0), 4);
        Assert.True(rig.Conditions.InCond(Killer, TFCond.DemoBuff));
    }

    [Fact]
    public void HeadsResetOnDeath()
    {
        var rig = new Rig();
        rig.KillerState.Decapitations = 1;
        rig.VictimState.Decapitations = 2;

        rig.Heads.OnPlayerDied(TheDeath);                       // no sword record: the plain death

        Assert.Equal(0, rig.VictimState.Decapitations);
        Assert.Equal(1, rig.KillerState.Decapitations);         // the count is the holder's own
    }

    /// <summary>The heads' buffed ceiling, what the grant's guard reads.</summary>
    private sealed class FixedHealing : TF2.Server.Medigun.ITFPlayerHealing
    {
        public int NumHealers(int patientIndex) => 0;
        public IEnumerable<int> HealerIndexes(int patientIndex) => [];

        public void Heal(EntityHandle patient, EntityHandle healer, float healRate, float now,
                         float overhealBonus = TF2.Server.Medigun.ITFPlayerHealing.StandardOverhealBonus,
                         float decayMult = TF2.Server.Medigun.ITFPlayerHealing.StandardOverhealDecayMult,
                         bool dispenserHeal = false, int scorer = -1) { }
        public float StopHealing(EntityHandle patient, EntityHandle healer, float now) => 0f;
        public void RecalculateChargeEffects(EntityHandle player, float now) { }
        public void NoteReleasing(int playerIndex, bool releasing, int chargeType = 0) { }
        public int MaxBuffedHealth(EntityHandle patient, bool ignoreHealthOverMax = false) => 500;
        public bool CanReceiveMedigunChargeEffect(EntityHandle player) => false;
        public void RemoveChargeEffect(EntityHandle player) { }
        public void TestAndExpireChargeEffect(EntityHandle player, float now) { }
        public void TickPatient(EntityHandle patient, float now, float frametime) { }
        public void Extinguished(EntityHandle patient) { }
    }

    /// <summary>The carrier's states as the movement rules' data.</summary>
    private sealed class CarrierData(TFPlayerCarriers carriers) : ITFPlayerData
    {
        public TFPlayerNetState? Predicted(int playerIndex) => carriers.Get(playerIndex);

        public TFPlayerNetState? Networked(int playerIndex) => carriers.Get(playerIndex);
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;
using TF2.Server.Player;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The kill that restores the killer (<see cref="TFRestoreOnKill"/>): the
/// killing weapon's <c>restore_health_on_kill</c> percentage of the unhindered
/// maximum, one health over, capped at half as much again the maximum.
/// </summary>
public sealed class TFRestoreOnKillTests
{
    private const int VictimIndex = 4;
    private static readonly EntityHandle Victim = new(0x0002_0000u | VictimIndex);

    private sealed class Rig
    {
        public readonly PlayerRig Player = new();
        public readonly TFPlayerConditions Conditions;
        public readonly TFPlayerResupply Resupply;
        public readonly TFRestoreOnKill Restore;
        public readonly TFDamageRecords Records = new();
        public readonly TF2.Tests.Weapons.FakeOwners Owners = new();
        public readonly AttributeHooks Hooks = new();

        public Rig()
        {
            Player.As(TFClass.Demoman);
            Player.Join(VictimIndex);
            Conditions = new TFPlayerConditions(Player.Carriers, Player.Engine);
            Resupply = new TFPlayerResupply(Player.Carriers, Player.Classes, Player.Engine, Player.Engine,
                                            Conditions, Player.Loadout, Player.Engine, Owners, Hooks);
            Restore = new TFRestoreOnKill(Owners, Hooks, Records, Resupply, Player.Engine);
        }

        /// <summary>A kill the katana dealt, its item restoring.</summary>
        public void KatanaKill(float restore = 35f)
        {
            Owners.ByName[PlayerRig.Index] = new()
            {
                [TFWeaponTypes.Katana] = Arm(TFRestoreOnKill.RestoreAttrib, restore),
            };
            Records.Note(VictimIndex, new TFHitRecord(PlayerRig.Player, 65f, DamageType.Generic, TFCritType.None,
                                                      TFDamageCustom.Decapitation, TFWeaponId.Sword,
                                                      TFWeaponTypes.Katana, 10f), damager: false);
        }

        /// <summary>A kill whose record names a weapon the killer holds no item of.</summary>
        public void UnattributedKill()
        {
            Records.Note(VictimIndex, new TFHitRecord(PlayerRig.Player, 65f, DamageType.Generic, TFCritType.None,
                                                      TFDamageCustom.None, TFWeaponId.Bat,
                                                      "tf_weapon_bat", 10f), damager: false);
        }

        /// <summary>The killer's health, as the engine would answer it.</summary>
        public int Health { set => Player.Engine.Returns("Entities.GetHealth", value); }

        /// <summary>The health the arm set, its last word; null before any.</summary>
        public int? SetHealth
        {
            get
            {
                var calls = Player.Engine.AllOf("Entities.SetHealth").ToList();
                return calls.Count > 0 ? calls[^1].ArgAs<int>(1) : null;
            }
        }

        public int SetHealthCalls => Player.Engine.AllOf("Entities.SetHealth").Count();
    }

    /// <summary>An item wearing one attribute, the sword facts' Arm.</summary>
    private static IAttributeProvider Arm(string name, float value)
    {
        var container = new AttributeContainer(null, AttributeProviderType.Generic);
        container.Set(AttributeValue.OfFloat(
            new AttributeDefinition(515, $"attr {name}", name, AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), value));
        return container;
    }

    private static PlayerDeath TheDeath => new(Victim, VictimIndex, PlayerRig.Player, default, EntityHandle.None);

    [Fact]
    public void TheKillPaysThePercentageOfTheMaxOneOver()
    {
        var rig = new Rig();
        int max = rig.Resupply.MaxHealth(PlayerRig.Player);
        rig.Health = 30;
        rig.KatanaKill();
        rig.Restore.OnPlayerDied(TheDeath);

        // 35% of the maximum, the fudged health over, on top of the 30.
        Assert.Equal(30 + (int)(35f / 100f * max) + 1, rig.SetHealth);
    }

    [Fact]
    public void TheCapHoldsTheRestoreAtHalfAsMuchAgainTheMax()
    {
        var rig = new Rig();
        int cap = (int)(rig.Resupply.MaxHealth(PlayerRig.Player) * TFRestoreOnKill.BuffedMaxScale);
        rig.Health = cap - 10;                         // the full grant would pass the cap
        rig.KatanaKill();
        rig.Restore.OnPlayerDied(TheDeath);

        Assert.Equal(cap, rig.SetHealth);
    }

    [Fact]
    public void AKillWithoutTheAttributeRestoresNothing()
    {
        var rig = new Rig();
        int before = rig.SetHealthCalls;
        rig.UnattributedKill();
        rig.Restore.OnPlayerDied(TheDeath);

        Assert.Equal(before, rig.SetHealthCalls);
    }

    [Fact]
    public void TheKillersOwnBlowIsTheOneThatPays()
    {
        var rig = new Rig();
        int before = rig.SetHealthCalls;
        rig.KatanaKill();
        // Another's blow was the last: the killer's item never answers it.
        rig.Records.Note(VictimIndex, new TFHitRecord(new EntityHandle(0x0001_0009u), 65f, DamageType.Generic,
                                                      TFCritType.None, TFDamageCustom.Decapitation,
                                                      TFWeaponId.Sword, TFWeaponTypes.Katana, 10f), damager: false);
        rig.Restore.OnPlayerDied(TheDeath);

        Assert.Equal(before, rig.SetHealthCalls);
    }
}

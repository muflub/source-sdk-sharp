using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The kill that refills the charge (<see cref="TFChargeMeterRefills"/>, stock
/// tf_player.cpp:11258-11275): the demoman's melee or bash kill of an enemy
/// pays <c>kill_refills_meter</c> onto the meter, the meter capped at full.
/// </summary>
public sealed class TFChargeRefillsTests
{
    private static readonly EntityHandle Killer = PlayerRig.Player;

    private const int VictimIndex = 4;
    private static readonly EntityHandle Victim = new(0x0002_0000u | VictimIndex);

    private sealed class Rig
    {
        public readonly PlayerRig Player = new();
        public readonly FakeOwners Owners = new();
        public readonly AttributeHooks Hooks = new();
        public readonly TFDamageRecords Records = new();
        public readonly TFChargeMeterRefills Refills;

        public Rig()
        {
            Player.As(TFClass.Demoman);
            Player.Join(VictimIndex);
            // The enemy pair: the killer blue, the victim red — the stub reads
            // the handle the standing call carries (CombatRig's shape).
            Player.Engine.ReturnsEachCall("Players.GetTeam", () =>
                Player.Engine.Calls[^1].Arg(0) is EntityHandle h && h == Victim
                    ? (int)TFTeam.Red : (int)TFTeam.Blue);
            Refills = new TFChargeMeterRefills(Player.Carriers, Owners, Hooks, Records, Player.Engine);
        }

        public TFPlayerNetState KillerState => Player.Carriers.Get(PlayerRig.Index)!;

        /// <summary>A killing blow by the sword, the melee bit aboard.</summary>
        public void SwordKill() =>
            Records.Note(VictimIndex, new TFHitRecord(Killer, 65f, DamageType.BlastSurface, TFCritType.None,
                                                      TFDamageCustom.Decapitation, TFWeaponId.Sword,
                                                      "tf_weapon_sword", 10f), damager: true);
    }

    private static PlayerDeath TheDeath => new(Victim, VictimIndex, Killer, default, EntityHandle.None);

    private static void Arms(Rig rig, float refill = 0.5f)
    {
        rig.Owners.Classes[PlayerRig.Index] = TFClass.Demoman;
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(
            new AttributeDefinition(6012, "attr 6012", "kill_refills_meter", AttributeDescriptionFormat.Additive,
                                    AttributeEffectType.Neutral, false, false,
                                    AttributeValueType.Default, MergedKeys.Block("x")), refill));
        rig.Owners.Managers[PlayerRig.Index] = manager;
    }

    [Fact]
    public void AProperKillRefillsTheMeter()
    {
        var rig = new Rig();
        Arms(rig);
        rig.KillerState.ChargeMeter = 40f;

        rig.SwordKill();
        rig.Refills.OnPlayerDied(TheDeath);

        Assert.Equal(90f, rig.KillerState.ChargeMeter);           // 40 + half the meter
    }

    [Fact]
    public void TheFullMeterStaysFull()
    {
        var rig = new Rig();
        Arms(rig, 1f);
        rig.KillerState.ChargeMeter = 100f;

        rig.SwordKill();
        rig.Refills.OnPlayerDied(TheDeath);

        Assert.Equal(100f, rig.KillerState.ChargeMeter);
    }

    [Fact]
    public void NoAttributeRefillsNothing()
    {
        var rig = new Rig();
        rig.Owners.Classes[PlayerRig.Index] = TFClass.Demoman;
        rig.KillerState.ChargeMeter = 40f;

        rig.SwordKill();
        rig.Refills.OnPlayerDied(TheDeath);

        Assert.Equal(40f, rig.KillerState.ChargeMeter);
    }

    [Fact]
    public void ATeammatesDeathRefillsNothing()
    {
        var rig = new Rig();
        Arms(rig);
        rig.KillerState.ChargeMeter = 40f;
        // The same team: every ask answers blue.
        rig.Player.Engine.Returns("Players.GetTeam", (int)TFTeam.Blue);

        rig.SwordKill();
        rig.Refills.OnPlayerDied(TheDeath);

        Assert.Equal(40f, rig.KillerState.ChargeMeter);
    }

    [Fact]
    public void ABashKillRefillsToo()
    {
        var rig = new Rig();
        Arms(rig, 1f);
        rig.KillerState.ChargeMeter = 10f;

        // The bash's own record: the charge impact, no melee bit.
        rig.Records.Note(VictimIndex, new TFHitRecord(Killer, 60f, DamageType.Club, TFCritType.None,
                                                      TFDamageCustom.ChargeImpact, TFWeaponId.None,
                                                      "tf_wearable_demoshield", 10f), damager: true);
        rig.Refills.OnPlayerDied(TheDeath);

        Assert.Equal(100f, rig.KillerState.ChargeMeter);          // 10 + the whole meter, capped
    }

    [Fact]
    public void ARangedKillRefillsNothing()
    {
        var rig = new Rig();
        Arms(rig);
        rig.KillerState.ChargeMeter = 40f;

        // The launcher's hit: blast, no melee bit, no custom.
        rig.Records.Note(VictimIndex, new TFHitRecord(Killer, 90f, DamageType.Blast, TFCritType.None,
                                                      TFDamageCustom.None, TFWeaponId.Grenadelauncher,
                                                      "tf_weapon_grenadelauncher", 10f), damager: true);
        rig.Refills.OnPlayerDied(TheDeath);

        Assert.Equal(40f, rig.KillerState.ChargeMeter);
    }
}

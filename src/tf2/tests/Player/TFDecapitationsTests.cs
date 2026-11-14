using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <see cref="TFDecapitations"/>: the Bazaar Bargain's banked head
/// (CTFSniperRifleDecap::OnPlayerKill, tf_weapon_sniperrifle.cpp:1722-1733).
/// </summary>
public sealed class TFDecapitationsTests
{
    private static readonly EntityHandle Killer = PlayerRig.Player;

    /// <summary>The dead teammate, the rig's second carrier (PlayerRig.Join's own shape).</summary>
    private const int VictimIndex = 4;
    private static readonly EntityHandle Victim = new(0x0002_0000u | VictimIndex);

    private sealed class Rig
    {
        public readonly PlayerRig Player = new();
        public readonly TFDamageRecords Records = new();
        public readonly TFDecapitations Heads;

        public Rig()
        {
            Player.Join();
            Player.Join(VictimIndex);
            Heads = new TFDecapitations(Player.Carriers, Records);
        }

        public TFPlayerNetState KillerState => Player.Carriers.Get(PlayerRig.Index)!;
    }

    /// <summary>The killing blow, as the damage layer's record holds it.</summary>
    private static void Killed(Rig rig, TFDamageCustom custom, TFWeaponId weapon) =>
        rig.Records.Note(VictimIndex, new TFHitRecord(Killer, 65f, DamageType.Generic, TFCritType.None,
                                                      custom, weapon, "tf_weapon_sniperrifle_decap", 10f), damager: false);

    private static PlayerDeath TheDeath => new(Victim, VictimIndex, Killer, default, EntityHandle.None);

    [Fact]
    public void AHeadshotKillByTheDecapRifleBanksAHead()
    {
        var rig = new Rig();

        Killed(rig, TFDamageCustom.Headshot, TFWeaponId.SniperrifleDecap);
        rig.Heads.OnPlayerDied(TheDeath);

        Assert.Equal(1, rig.KillerState.Decapitations);
    }

    [Fact]
    public void ABodyshotOrAnotherRifleBanksNothing()
    {
        var rig = new Rig();

        Killed(rig, TFDamageCustom.None, TFWeaponId.SniperrifleDecap);
        rig.Heads.OnPlayerDied(TheDeath);
        Assert.Equal(0, rig.KillerState.Decapitations);

        Killed(rig, TFDamageCustom.Headshot, TFWeaponId.Sniperrifle);
        rig.Heads.OnPlayerDied(TheDeath);
        Assert.Equal(0, rig.KillerState.Decapitations);
    }

    [Fact]
    public void TheVictimsOwnBankSurvivesTheHook()
    {
        // Stock zeroes m_iDecapitations only at the shared's init; the death
        // walks that clear it are other hooks' (TFSwordHeads's), none here.
        var rig = new Rig();
        rig.KillerState.Decapitations = 3;

        Killed(rig, TFDamageCustom.Headshot, TFWeaponId.SniperrifleDecap);
        rig.Heads.OnPlayerDied(TheDeath);

        Assert.Equal(4, rig.KillerState.Decapitations);
    }
}

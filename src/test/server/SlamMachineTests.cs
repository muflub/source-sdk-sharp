/*
 * weapon_slam's machine: stock HL2MP's CWeapon_SLAM (weapon_slam.cpp), run
 * through WeaponLogic over locally bound states and arms that answer from
 * v_slam.mdl's own sequence table (dumped from hl2mp_pak_dir.vpk: index,
 * activity, duration in seconds).
 */

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

public class SlamMachineTests
{
    internal static readonly (string Activity, float Duration)[] VSlam =
    [
        ("ACT_SLAM_THROW_IDLE", 3.333f), ("ACT_SLAM_THROW_ND_IDLE", 3.333f),
        ("ACT_SLAM_THROW_THROW", 0.467f), ("ACT_SLAM_THROW_THROW2", 0.8f),
        ("ACT_SLAM_THROW_THROW_ND", 0.467f), ("ACT_SLAM_THROW_THROW_ND2", 0.8f),
        ("ACT_SLAM_THROW_DRAW", 1.0f), ("ACT_SLAM_THROW_ND_DRAW", 1.0f),
        ("ACT_SLAM_THROW_TO_TRIPMINE_ND", 1.333f), ("ACT_SLAM_THROW_DETONATE", 1.333f),
        ("ACT_SLAM_THROW_TO_STICKWALL", 1.333f), ("ACT_SLAM_THROW_TO_STICKWALL_ND", 1.333f),
        ("ACT_SLAM_DETONATOR_DRAW", 1.0f), ("ACT_SLAM_DETONATOR_IDLE", 3.333f),
        ("ACT_SLAM_DETONATOR_DETONATE", 0.667f), ("ACT_SLAM_DETONATOR_THROW_DRAW", 1.0f),
        ("ACT_SLAM_DETONATOR_HOLSTER", 0.4f), ("ACT_SLAM_TRIPMINE_IDLE", 3.333f),
        ("ACT_SLAM_TRIPMINE_DRAW", 0.833f), ("ACT_SLAM_TRIPMINE_TO_THROW_ND", 1.667f),
        ("ACT_SLAM_TRIPMINE_ATTACH", 0.233f), ("ACT_SLAM_TRIPMINE_ATTACH2", 0.6f),
        ("ACT_SLAM_STICKWALL_IDLE", 1.333f), ("ACT_SLAM_STICKWALL_ND_IDLE", 1.333f),
        ("ACT_SLAM_STICKWALL_ATTACH", 0.167f), ("ACT_SLAM_STICKWALL_ATTACH2", 0.6f),
        ("ACT_SLAM_STICKWALL_TO_THROW", 1.0f), ("ACT_SLAM_STICKWALL_TO_THROW_ND", 1.0f),
    ];

    private sealed class SlamArms : IWeaponArms
    {
        public readonly List<string> Sounds = new();
        public WallProbe Ahead = WallProbe.Nothing;
        public bool Out;
        public int Throws, Attaches, Mines, Detonations;

        public void Sound(string soundScript) { if (soundScript.Length > 0) Sounds.Add(soundScript); }
        public void FireBullets(in WeaponType type, int pellets, float spread) { }
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }
        public int LookupSequence(WeaponAnim anim) => -1;
        public float AnimDuration(WeaponAnim anim) => 0f;

        public int LookupActivity(string activity) => Array.FindIndex(VSlam, s => s.Activity == activity);
        public float SequenceDuration(int sequence) => sequence >= 0 && sequence < VSlam.Length ? VSlam[sequence].Duration : 0f;
        public WallProbe ProbeWall(float reach) => Ahead;
        public void ThrowCharge(in WeaponType type) => Throws++;
        public void AttachCharge(in WeaponType type) => Attaches++;
        public void AttachTripmine(in WeaponType type) => Mines++;
        public void DetonateCharges() => Detonations++;
        public bool ChargesOut() => Out;
    }

    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const float Tick = 0.015f;

    private sealed class Rig
    {
        public readonly SlamArms Arms = new();
        public readonly WeaponLogic Machine;
        public readonly WeaponNetState W = new();
        public readonly ViewModelNetState Vm = new();
        public float Now = 10f;

        public Rig(int charges = 3)
        {
            Machine = new WeaponLogic(Arms);
            W.BindLocal();
            Vm.BindLocal();
            W.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponSlam);
            W.Clip = -1;
            W.SecondaryReserve = charges;
            W.Special = SlamBits.Initial;
        }

        public void Deploy() => Assert.True(Machine.Deploy(W, Vm, Now), "the SLAM picks its own draw");

        /// <summary>Commands at a fixed tick for <paramref name="seconds"/>.</summary>
        public void Hold(int buttons, float seconds)
        {
            float until = Now + seconds;
            for (; Now < until; Now += Tick)
                Machine.RunCommand(W, Vm, buttons, Now, effectsLive: true);
        }

        public string Activity => SlamBits.Activity(W.Special);
        public SlamMode Mode => SlamBits.Mode(W.Special);
        public bool Has(int flag) => SlamBits.Has(W.Special, flag);
        public string Sequence => Vm.Sequence >= 0 && Vm.Sequence < VSlam.Length ? VSlam[Vm.Sequence].Activity : "";
    }

    // =====================================================================
    // The row.
    // =====================================================================

    [Fact]
    public void TheSlamIsAppendedAfterTheStunstickWithItsOwnMachine()
    {
        var stock = WeaponCatalog.Stock();
        var row = stock.For(EntityNames.WeaponSlam)!.Value;

        Assert.Equal(10, stock.IndexOf(EntityNames.WeaponSlam));
        Assert.Equal(WeaponKind.Slam, row.Kind);
        Assert.Equal("slam", row.SecondaryAmmoName);
        Assert.Equal(3, row.DefaultSecondaryAmmo);
        Assert.Equal("models/weapons/v_slam.mdl", row.ViewModel);
        Assert.Equal("models/weapons/w_slam.mdl", row.WorldModel);
        Assert.False(row.IsMelee, "no primary ammo, but not a swing either");
    }

    [Fact]
    public void TheSlamsPoolIsStocksSlamAmmo()
    {
        var slam = AmmoCatalog.Stock().For("slam")!.Value;

        Assert.Equal(DamageType.Burn, slam.DamageType);
        Assert.Equal(5, slam.MaxCarry);
    }

    [Fact]
    public void TheStateWordRoundTripsEveryFlagAndTheActivity()
    {
        int bits = SlamBits.WithMode(0, SlamMode.SatchelAttach);
        bits = SlamBits.With(bits, SlamBits.DetonatorArmed, true);
        bits = SlamBits.WithActivity(bits, SlamActivities.StickwallToThrow);

        Assert.Equal(SlamMode.SatchelAttach, SlamBits.Mode(bits));
        Assert.True(SlamBits.Has(bits, SlamBits.DetonatorArmed));
        Assert.False(SlamBits.Has(bits, SlamBits.NeedReload));
        Assert.Equal(SlamActivities.StickwallToThrow, SlamBits.Activity(bits));
        Assert.Equal(SlamActivities.All.Length, SlamActivities.All.Distinct().Count());
    }

    // =====================================================================
    // Deploy.
    // =====================================================================

    [Fact]
    public void DrawnInTheOpenItIsReadyToThrowWithNoDetonator()
    {
        var rig = new Rig();
        rig.Deploy();

        Assert.Equal(SlamActivities.ThrowNdDraw, rig.Sequence);
        Assert.Equal(SlamMode.SatchelThrow, rig.Mode);
        Assert.False(rig.Has(SlamBits.NeedReload), "Deploy clears the reload the constructor owed");
        Assert.False(rig.Has(SlamBits.DetonatorArmed));
        Assert.Equal(rig.Now + 1.0f, rig.W.NextPrimaryAttack, 3);
    }

    [Fact]
    public void DrawnFacingAWallItIsReadyToPlantATripmine()
    {
        var rig = new Rig();
        rig.Arms.Ahead = WallProbe.Surface;
        rig.Deploy();

        Assert.Equal(SlamActivities.TripmineDraw, rig.Sequence);
        Assert.Equal(SlamMode.TripmineReady, rig.Mode);
    }

    [Fact]
    public void DrawnWithAChargeOutAndNoneLeftItHoldsOnlyTheDetonator()
    {
        var rig = new Rig(charges: 0);
        rig.Arms.Out = true;
        rig.Deploy();

        Assert.Equal(SlamActivities.DetonatorDraw, rig.Sequence);
        Assert.True(rig.Has(SlamBits.DetonatorArmed));
        Assert.True(rig.Has(SlamBits.NeedReload));
    }

    // =====================================================================
    // The throw.
    // =====================================================================

    [Fact]
    public void ThePrimaryStartsTheThrowAndArmsTheDetonatorButThrowsNothingYet()
    {
        var rig = new Rig();
        rig.Deploy();
        rig.Hold(0, 1.1f);

        rig.Hold(Attack, Tick);

        Assert.Equal(SlamActivities.ThrowThrowNd, rig.Sequence);
        Assert.True(rig.Has(SlamBits.DetonatorArmed));
        Assert.True(rig.Has(SlamBits.ThrowSatchel));
        Assert.True(rig.Has(SlamBits.NeedReload));
        Assert.Equal(0, rig.Arms.Throws);
        Assert.Equal(3, rig.W.SecondaryReserve);
    }

    [Fact]
    public void TheSatchelLeavesWhenTheThrowAnimationHasRun()
    {
        var rig = new Rig();
        rig.Deploy();
        rig.Hold(0, 1.1f);
        rig.Hold(Attack, Tick);

        rig.Hold(0, 0.5f);

        Assert.Equal(1, rig.Arms.Throws);
        Assert.Equal(2, rig.W.SecondaryReserve);
        Assert.Equal(new[] { SlamMachine.ThrowSound }, rig.Arms.Sounds);
        Assert.Equal(SlamActivities.ThrowThrowNd2, rig.Sequence);
    }

    [Fact]
    public void AHeldTriggerThrowsOneSatchelPerCycleAndNotOnePerCommand()
    {
        var rig = new Rig();
        rig.Deploy();
        rig.Hold(0, 1.1f);

        // Throw, follow-through and redraw: 0.467 + 0.8 + 1.0, and the idle that clears the reload.
        rig.Hold(Attack, 2.0f);

        Assert.Equal(1, rig.Arms.Throws);
        Assert.Equal(2, rig.W.SecondaryReserve);
    }

    [Fact]
    public void AfterTheCycleItIdlesArmedOnTheThrowIdle()
    {
        var rig = new Rig();
        rig.Deploy();
        rig.Hold(0, 1.1f);
        rig.Hold(Attack, Tick);

        rig.Hold(0, 2.4f);

        Assert.Equal(SlamActivities.ThrowIdle, rig.Activity);
        Assert.False(rig.Has(SlamBits.NeedReload));
        Assert.True(rig.Has(SlamBits.DetonatorArmed));
    }

    // =====================================================================
    // The detonator.
    // =====================================================================

    [Fact]
    public void TheSecondaryDetonatesFromTheThrowIdle()
    {
        var rig = new Rig();
        rig.Deploy();
        rig.Hold(0, 1.1f);
        rig.Hold(Attack, Tick);
        rig.Hold(0, 2.4f);
        rig.Arms.Sounds.Clear();

        rig.Hold(Attack2, Tick);

        Assert.Equal(1, rig.Arms.Detonations);
        Assert.Equal(new[] { SlamMachine.DetonateSound }, rig.Arms.Sounds);
        Assert.False(rig.Has(SlamBits.DetonatorArmed));
        Assert.Equal(SlamActivities.ThrowDetonate, rig.Sequence);
    }

    [Fact]
    public void TheSecondaryDoesNothingBeforeTheIdleIsReached()
    {
        var rig = new Rig();
        rig.Deploy();
        rig.Hold(0, 1.1f);
        rig.Hold(Attack, Tick);
        rig.Hold(0, 1.0f);

        rig.Hold(Attack2, Tick);

        // StartSatchelDetonate's guard: only from the detonator's or the throw's idle.
        Assert.Equal(0, rig.Arms.Detonations);
        Assert.True(rig.Has(SlamBits.DetonatorArmed));
    }

    [Fact]
    public void TheSecondaryWithNothingArmedDoesNothing()
    {
        var rig = new Rig();
        rig.Deploy();
        rig.Hold(0, 1.1f);
        rig.Arms.Sounds.Clear();

        rig.Hold(Attack2, 0.5f);

        Assert.Equal(0, rig.Arms.Detonations);
        Assert.Empty(rig.Arms.Sounds);
        Assert.Equal(3, rig.W.SecondaryReserve);
    }

    // =====================================================================
    // The tripmine.
    // =====================================================================

    [Fact]
    public void AWallComingIntoReachSwitchesToTripmineMode()
    {
        var rig = new Rig();
        rig.Deploy();
        rig.Hold(0, 1.1f);

        rig.Arms.Ahead = WallProbe.Surface;
        rig.Hold(0, Tick);

        Assert.Equal(SlamMode.TripmineReady, rig.Mode);
        Assert.Equal(SlamActivities.ThrowToTripmineNd, rig.Sequence);
        Assert.True(rig.W.SpecialTime > rig.Now, "the switch holds until its animation has run");
    }

    [Fact]
    public void APlayerInReachIsNotAWall()
    {
        var rig = new Rig();
        rig.Deploy();
        rig.Hold(0, 1.1f);

        rig.Arms.Ahead = WallProbe.Character;
        rig.Hold(0, 0.5f);

        Assert.Equal(SlamMode.SatchelThrow, rig.Mode);
    }

    [Fact]
    public void ThePrimaryAtAWallPlantsATripmineWhenTheAttachHasRun()
    {
        var rig = new Rig();
        rig.Arms.Ahead = WallProbe.Surface;
        rig.Deploy();
        rig.Hold(0, 1.0f);

        rig.Hold(Attack, Tick);
        Assert.Equal(SlamActivities.TripmineAttach, rig.Sequence);
        Assert.Equal(0, rig.Arms.Mines);

        rig.Hold(0, 0.3f);

        Assert.Equal(1, rig.Arms.Mines);
        Assert.Equal(2, rig.W.SecondaryReserve);
        Assert.Equal(SlamActivities.TripmineAttach2, rig.Sequence);
        Assert.Equal(0, rig.Arms.Throws);
    }

    // =====================================================================
    // Running out, and the replay.
    // =====================================================================

    [Fact]
    public void WithNoChargesAndNothingOutTheSlamAsksToGo()
    {
        var rig = new Rig(charges: 1);
        rig.Deploy();
        rig.Hold(0, 1.1f);
        rig.Hold(Attack, Tick);
        rig.Hold(0, 2.4f);
        rig.Hold(Attack2, Tick);

        Assert.Equal(0, rig.W.SecondaryReserve);
        rig.Hold(0, 1.5f);

        Assert.True(rig.Has(SlamBits.Spent), "Weapon_Drop and UTIL_Remove: nothing left to throw or set off");
    }

    [Fact]
    public void AnArmedSlamWithNoChargesIsKeptForItsDetonator()
    {
        var rig = new Rig(charges: 1);
        rig.Deploy();
        rig.Hold(0, 1.1f);
        rig.Hold(Attack, Tick);
        rig.Hold(0, 2.4f);

        Assert.Equal(0, rig.W.SecondaryReserve);
        Assert.False(rig.Has(SlamBits.Spent));
        Assert.Equal(SlamActivities.DetonatorIdle, rig.Activity);
    }

    [Fact]
    public void TheReplaySpendsTheSameChargeAndMakesNothing()
    {
        var live = new Rig();
        var replay = new Rig();
        foreach (var rig in new[] { live, replay })
        {
            rig.Deploy();
            rig.Hold(0, 1.1f);
        }

        live.Machine.RunCommand(live.W, live.Vm, Attack, live.Now, effectsLive: true);
        replay.Machine.RunCommand(replay.W, replay.Vm, Attack, replay.Now, effectsLive: false);
        for (int i = 1; i < 40; i++)
        {
            live.Machine.RunCommand(live.W, live.Vm, 0, live.Now + i * Tick, effectsLive: true);
            replay.Machine.RunCommand(replay.W, replay.Vm, 0, replay.Now + i * Tick, effectsLive: false);
        }

        Assert.Equal(1, live.Arms.Throws);
        Assert.Equal(0, replay.Arms.Throws);
        Assert.Empty(replay.Arms.Sounds);
        Assert.Equal(live.W.Special, replay.W.Special);
        Assert.Equal(live.W.SecondaryReserve, replay.W.SecondaryReserve);
        Assert.Equal(live.Vm.Sequence, replay.Vm.Sequence);
    }

    [Fact]
    public void AnActivityTheModelLacksChangesNothing()
    {
        // v_slam carries no ACT_SLAM_DETONATOR_STICKWALL_DRAW: drawn armed at a
        // wall, the SLAM keeps the sequence it had, as SetIdealActivity does.
        var rig = new Rig();
        rig.Arms.Ahead = WallProbe.Surface;
        rig.Arms.Out = true;
        rig.Vm.Sequence = 17;

        rig.Deploy();

        Assert.Equal(17, rig.Vm.Sequence);
        Assert.Equal(SlamMode.TripmineReady, rig.Mode);
    }
}

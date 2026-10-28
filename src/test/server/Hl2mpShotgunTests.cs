using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The HL2MP shotgun's own machine (weapon_shotgun.cpp, hl2mp, ItemPostFrame
/// and the helpers it calls): every shot owes a pump (SPECIAL1, ACT_SHOTGUN_PUMP)
/// before the next, both barrels share the primary's clock and the clip, and a
/// reload loads one shell per ACT_VM_RELOAD and can be cut short by firing.
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons, 2026-10-25): this
/// mod never pumped (no Weapon_Shotgun.Special1, server or client).
/// </para>
/// </summary>
public class Hl2mpShotgunTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const int ReloadKey = (int)InputButtons.Reload;

    private sealed class Arms : IWeaponArms
    {
        public readonly List<string> Sounds = [];
        public readonly List<int> Volleys = [];
        public readonly List<WeaponAnim> Anims = [];

        public void Sound(string soundScript) { if (soundScript.Length > 0) Sounds.Add(soundScript); }
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) => Volleys.Add(pellets);
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }

        public int LookupSequence(WeaponAnim anim)
        {
            Anims.Add(anim);
            return (int)anim + 1;
        }

        // Every sequence a third of a second long.
        public float AnimDuration(WeaponAnim anim) => 1f / 3f;
    }

    private static (WeaponLogic Machine, Arms Arms, WeaponNetState W, ViewModelNetState Vm) Shotgun(int clip, int reserve)
    {
        var arms = new Arms();
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf("weapon_shotgun");
        w.Clip = clip;
        w.Reserve = reserve;
        return (new WeaponLogic(arms), arms, w, vm);
    }

    private static void Run(WeaponLogic machine, WeaponNetState w, ViewModelNetState vm, int buttons, float from, float to)
    {
        for (float t = from; t < to; t += 0.015f)
            machine.RunCommand(w, vm, buttons, t, effectsLive: true);
    }

    [Fact]
    public void AShotIsFollowedByAPump()
    {
        var (machine, arms, w, vm) = Shotgun(clip: 6, reserve: 10);

        machine.RunCommand(w, vm, Attack, 10f, effectsLive: true);
        Run(machine, w, vm, 0, 10.015f, 10.5f);

        Assert.Equal(new[] { "Weapon_Shotgun.Single", "Weapon_Shotgun.Special1" }, arms.Sounds);
        Assert.Contains(WeaponAnim.Pump, arms.Anims);
        Assert.Equal(new[] { 7 }, arms.Volleys);
        Assert.Equal(5, w.Clip);
    }

    [Fact]
    public void HeldTheTriggerWaitsForThePump()
    {
        var (machine, arms, w, vm) = Shotgun(clip: 6, reserve: 10);

        // fire (1/3 s), pump (1/3 s), fire again: two shots in under a second.
        Run(machine, w, vm, Attack, 10f, 10.66f);
        Assert.Single(arms.Volleys);

        Run(machine, w, vm, Attack, 10.66f, 10.70f);
        Assert.Equal(2, arms.Volleys.Count);
    }

    [Fact]
    public void BothBarrelsAreTwelvePelletsFromTheClipThenAPump()
    {
        var (machine, arms, w, vm) = Shotgun(clip: 6, reserve: 10);

        machine.RunCommand(w, vm, Attack2, 10f, effectsLive: true);
        Run(machine, w, vm, 0, 10.015f, 10.5f);

        Assert.Equal(new[] { 12 }, arms.Volleys);
        Assert.Equal(4, w.Clip);
        Assert.Equal(new[] { "Weapon_Shotgun.Double", "Weapon_Shotgun.Special1" }, arms.Sounds);
        Assert.Contains(WeaponAnim.Secondary, arms.Anims);
    }

    [Fact]
    public void OneShellLeftMakesTheDoubleASingle()
    {
        var (machine, arms, w, vm) = Shotgun(clip: 1, reserve: 10);

        machine.RunCommand(w, vm, Attack2, 10f, effectsLive: true);

        Assert.Equal(new[] { 7 }, arms.Volleys);
        Assert.Equal(0, w.Clip);
    }

    [Fact]
    public void AReloadLoadsOneShellAtATime()
    {
        var (machine, arms, w, vm) = Shotgun(clip: 3, reserve: 10);

        machine.RunCommand(w, vm, ReloadKey, 10f, effectsLive: true);
        Assert.True(w.InReload);
        Assert.Equal(3, w.Clip);
        Assert.Contains(WeaponAnim.ReloadStart, arms.Anims);

        Run(machine, w, vm, 0, 10.015f, 10.40f);
        Assert.Equal(4, w.Clip);
        Assert.Equal(9, w.Reserve);

        Run(machine, w, vm, 0, 10.40f, 12f);
        Assert.Equal(6, w.Clip);
        Assert.Equal(7, w.Reserve);
        Assert.False(w.InReload);
        Assert.Equal(3, arms.Sounds.Count(s => s == "Weapon_Shotgun.Reload"));
        Assert.Contains(WeaponAnim.ReloadFinish, arms.Anims);
    }

    [Fact]
    public void FiringMidReloadStopsTheReloadAndFires()
    {
        var (machine, arms, w, vm) = Shotgun(clip: 3, reserve: 10);

        machine.RunCommand(w, vm, ReloadKey, 10f, effectsLive: true);
        Run(machine, w, vm, 0, 10.015f, 10.40f);
        Run(machine, w, vm, Attack, 10.40f, 10.80f);

        Assert.False(w.InReload);
        Assert.Single(arms.Volleys);
    }

    [Fact]
    public void EmptyWithNothingToLoadItClicks()
    {
        var (machine, arms, w, vm) = Shotgun(clip: 0, reserve: 0);

        machine.RunCommand(w, vm, Attack, 10f, effectsLive: true);

        Assert.Equal(new[] { "Weapon_Shotgun.Empty" }, arms.Sounds);
        Assert.Contains(WeaponAnim.DryFire, arms.Anims);
        Assert.Empty(arms.Volleys);
    }

    [Fact]
    public void AReplayLandsOnTheSameState()
    {
        var (live, _, w1, vm1) = Shotgun(clip: 6, reserve: 10);
        var (replay, _, w2, vm2) = Shotgun(clip: 6, reserve: 10);

        for (float t = 10f; t < 11.5f; t += 0.015f)
        {
            int buttons = t < 10.2f ? Attack : t < 10.9f ? 0 : ReloadKey;
            live.RunCommand(w1, vm1, buttons, t, effectsLive: true);
            replay.RunCommand(w2, vm2, buttons, t, effectsLive: false);
        }

        Assert.Equal(w1.Clip, w2.Clip);
        Assert.Equal(w1.Reserve, w2.Reserve);
        Assert.Equal(w1.NextPrimaryAttack, w2.NextPrimaryAttack);
        Assert.Equal(w1.NeedPump, w2.NeedPump);
        Assert.Equal(w1.InReload, w2.InReload);
        Assert.Equal(vm1.Sequence, vm2.Sequence);
    }
}

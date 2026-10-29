using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The HL2MP grenade's own machine (weapon_frag.cpp, hl2mp, ItemPostFrame and
/// Operator_HandleAnimEvent): a trigger held pulls the pin and holds it, the
/// release once the pullback has played throws, the grenade leaves (and one
/// is spent) at the throw sequence's event, and the next is drawn when the
/// throw has played.
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons, 2026-10-25): this
/// mod threw the moment the trigger went down, and its secondary threw
/// without spending a grenade.
/// </para>
/// </summary>
public class Hl2mpFragTests
{
    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const int Duck = (int)InputButtons.Duck;

    private sealed class Arms : IWeaponArms
    {
        public readonly List<string> Sounds = [];
        public readonly List<WeaponAnim> Anims = [];
        public readonly List<(GrenadeThrow Kind, float Timer)> Throws = [];

        public void Sound(string soundScript) { if (soundScript.Length > 0) Sounds.Add(soundScript); }
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) { }
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown)
            => throw new InvalidOperationException("the frag throws through ThrowGrenade");
        public void ThrowGrenade(in WeaponType type, GrenadeThrow kind, float timer) => Throws.Add((kind, timer));

        public int LookupSequence(WeaponAnim anim)
        {
            Anims.Add(anim);
            return (int)anim + 1;
        }

        // Every sequence a second long: the events fall at their cycles.
        public float AnimDuration(WeaponAnim anim) => 1f;
    }

    private static (WeaponLogic Machine, Arms Arms, WeaponNetState W, ViewModelNetState Vm) Frag(int grenades)
    {
        var arms = new Arms();
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf("weapon_frag");
        w.Clip = -1;
        w.Reserve = grenades;
        return (new WeaponLogic(arms), arms, w, vm);
    }

    private static void Run(WeaponLogic machine, WeaponNetState w, ViewModelNetState vm, int buttons, float from, float to)
    {
        for (float t = from; t < to; t += 0.015f)
            machine.RunCommand(w, vm, buttons, t, effectsLive: true);
    }

    [Fact]
    public void HeldThePinIsPulledAndNothingIsThrown()
    {
        var (machine, arms, w, vm) = Frag(grenades: 3);

        Run(machine, w, vm, Attack, 10f, 12f);

        Assert.Equal(WeaponAnim.PullbackHigh, arms.Anims.First());
        Assert.Empty(arms.Throws);
        Assert.Equal(3, w.Reserve);
    }

    [Fact]
    public void ReleasedItThrowsOverhandAtTheThrowsEvent()
    {
        var (machine, arms, w, vm) = Frag(grenades: 3);

        Run(machine, w, vm, Attack, 10f, 11f);       // the pullback's event at 10.8
        Run(machine, w, vm, 0, 11f, 11.05f);         // released: THROW, its event at 0.091
        Assert.Contains(WeaponAnim.Throw, arms.Anims);
        Assert.Empty(arms.Throws);

        Run(machine, w, vm, 0, 11.05f, 11.2f);
        Assert.Equal(new[] { (GrenadeThrow.Throw, 2.5f) }, arms.Throws);
        Assert.Equal(2, w.Reserve);
    }

    [Fact]
    public void ReleasedEarlyTheThrowWaitsForThePullback()
    {
        var (machine, arms, w, vm) = Frag(grenades: 3);

        Run(machine, w, vm, Attack, 10f, 10.2f);
        Run(machine, w, vm, 0, 10.2f, 10.75f);
        Assert.DoesNotContain(WeaponAnim.Throw, arms.Anims);

        Run(machine, w, vm, 0, 10.75f, 11f);
        Assert.Contains(WeaponAnim.Throw, arms.Anims);
    }

    [Fact]
    public void TheSecondaryLobsAndSpendsAGrenade()
    {
        var (machine, arms, w, vm) = Frag(grenades: 3);

        Run(machine, w, vm, Attack2, 10f, 11f);
        Run(machine, w, vm, 0, 11f, 11.3f);

        Assert.Equal(WeaponAnim.PullbackLow, arms.Anims.First());
        Assert.Contains(WeaponAnim.Haulback, arms.Anims);
        Assert.Equal(new[] { (GrenadeThrow.Lob, 2.5f) }, arms.Throws);
        Assert.Equal(2, w.Reserve);
    }

    /// <summary>
    /// Only the roll sounds. The script's single and double shots are
    /// common/null.wav, which stock's WeaponSound resolves to no script and
    /// so never plays (tools/stock-ab --weapons: this mod played it).
    /// </summary>
    [Fact]
    public void TheOverhandThrowAndTheLobAreSilent()
    {
        var (machine, arms, w, vm) = Frag(grenades: 3);

        Run(machine, w, vm, Attack, 10f, 11f);
        Run(machine, w, vm, 0, 11f, 12.5f);
        Run(machine, w, vm, Attack2, 12.5f, 13.5f);
        Run(machine, w, vm, 0, 13.5f, 14f);

        Assert.Equal(2, arms.Throws.Count);
        Assert.Empty(arms.Sounds);
    }

    [Fact]
    public void CrouchedTheSecondaryRollsWithTheRollSound()
    {
        var (machine, arms, w, vm) = Frag(grenades: 3);

        Run(machine, w, vm, Attack2 | Duck, 10f, 11f);
        Run(machine, w, vm, Duck, 11f, 11.3f);

        Assert.Contains(WeaponAnim.Secondary, arms.Anims);
        Assert.Equal(new[] { (GrenadeThrow.Roll, 2.5f) }, arms.Throws);
        Assert.Contains("WeaponFrag.Roll", arms.Sounds);
    }

    [Fact]
    public void TheNextIsDrawnWhenTheThrowHasPlayed()
    {
        var (machine, arms, w, vm) = Frag(grenades: 3);

        Run(machine, w, vm, Attack, 10f, 11f);
        Run(machine, w, vm, 0, 11f, 11.9f);
        Assert.True(w.Redraw);
        Assert.DoesNotContain(WeaponAnim.Draw, arms.Anims);

        Run(machine, w, vm, 0, 11.9f, 12.1f);        // the throw ends at 12.0
        Assert.False(w.Redraw);
        Assert.Contains(WeaponAnim.Draw, arms.Anims);
    }

    [Fact]
    public void TheLastGrenadeIsNotRedrawn()
    {
        var (machine, arms, w, vm) = Frag(grenades: 1);

        Run(machine, w, vm, Attack, 10f, 11f);
        Run(machine, w, vm, 0, 11f, 13f);

        Assert.Equal(0, w.Reserve);
        Assert.DoesNotContain(WeaponAnim.Draw, arms.Anims);
    }

    [Fact]
    public void WithNoGrenadesTheTriggersDoNothing()
    {
        var (machine, arms, w, vm) = Frag(grenades: 0);

        Run(machine, w, vm, Attack, 10f, 11f);
        Run(machine, w, vm, Attack2, 11f, 12f);

        Assert.DoesNotContain(WeaponAnim.PullbackHigh, arms.Anims);
        Assert.DoesNotContain(WeaponAnim.PullbackLow, arms.Anims);
    }

    [Fact]
    public void AReplayLandsOnTheSameState()
    {
        var (live, _, w1, vm1) = Frag(grenades: 3);
        var (replay, _, w2, vm2) = Frag(grenades: 3);

        for (float t = 10f; t < 13f; t += 0.015f)
        {
            int buttons = t < 11f ? Attack : 0;
            live.RunCommand(w1, vm1, buttons, t, effectsLive: true);
            replay.RunCommand(w2, vm2, buttons, t, effectsLive: false);
        }

        Assert.Equal(w1.Reserve, w2.Reserve);
        Assert.Equal(w1.Redraw, w2.Redraw);
        Assert.Equal(w1.AnimEvent, w2.AnimEvent);
        Assert.Equal(w1.NextPrimaryAttack, w2.NextPrimaryAttack);
        Assert.Equal(vm1.Sequence, vm2.Sequence);
    }
}

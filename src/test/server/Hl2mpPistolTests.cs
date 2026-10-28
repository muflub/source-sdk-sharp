using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The HL2MP pistol's own firing rules (weapon_pistol.cpp, hl2mp): held, it
/// refires every GetFireRate() (0.5 s); released and clicked again, it refires
/// as soon as PISTOL_FASTEST_REFIRE_TIME (0.1 s) after the last shot.
///
/// <para>
/// Found side by side with stock (tools/stock-ab --weapons, 2026-10-25): held
/// for the same press, stock fired one round and this mod three (it refired
/// every 0.2 s however the trigger was worked).
/// </para>
/// </summary>
public class Hl2mpPistolTests
{
    private const int Attack = (int)InputButtons.Attack;

    private sealed class Arms : IWeaponArms
    {
        public int Shots;
        public readonly List<float> Spreads = [];
        public void Sound(string soundScript) { }
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees)
        {
            Shots++;
            Spreads.Add(spreadDegrees);
        }
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }
        public int LookupSequence(WeaponAnim anim) => (int)anim + 1;
        public float AnimDuration(WeaponAnim anim) => 0.3f;
    }

    private static (WeaponLogic Machine, Arms Arms, WeaponNetState W, ViewModelNetState Vm) Pistol()
    {
        var arms = new Arms();
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf("weapon_pistol");
        w.Clip = 18;
        w.Reserve = 100;
        return (new WeaponLogic(arms), arms, w, vm);
    }

    [Fact]
    public void HeldItRefiresEveryHalfSecond()
    {
        var (machine, arms, w, vm) = Pistol();

        for (float t = 10f; t < 10.45f; t += 0.015f)
            machine.RunCommand(w, vm, Attack, t, effectsLive: true);

        Assert.Equal(1, arms.Shots);

        for (float t = 10.45f; t < 10.55f; t += 0.015f)
            machine.RunCommand(w, vm, Attack, t, effectsLive: true);

        Assert.Equal(2, arms.Shots);
    }

    [Fact]
    public void ClickedItRefiresAfterATenthOfASecond()
    {
        var (machine, arms, w, vm) = Pistol();

        machine.RunCommand(w, vm, Attack, 10f, effectsLive: true);
        machine.RunCommand(w, vm, 0, 10.05f, effectsLive: true);
        machine.RunCommand(w, vm, 0, 10.12f, effectsLive: true);
        machine.RunCommand(w, vm, Attack, 10.13f, effectsLive: true);

        Assert.Equal(2, arms.Shots);
    }

    [Fact]
    public void ClickedTooSoonItDoesNotRefire()
    {
        var (machine, arms, w, vm) = Pistol();

        machine.RunCommand(w, vm, Attack, 10f, effectsLive: true);
        machine.RunCommand(w, vm, 0, 10.03f, effectsLive: true);
        machine.RunCommand(w, vm, Attack, 10.06f, effectsLive: true);

        Assert.Equal(1, arms.Shots);
    }

    [Fact]
    public void AReplayLandsOnTheSameClock()
    {
        var (live, _, w1, vm1) = Pistol();
        var (replay, _, w2, vm2) = Pistol();

        foreach (var (buttons, t) in new[] { (Attack, 10f), (0, 10.05f), (0, 10.12f), (Attack, 10.13f) })
        {
            live.RunCommand(w1, vm1, buttons, t, effectsLive: true);
            replay.RunCommand(w2, vm2, buttons, t, effectsLive: false);
        }

        Assert.Equal(w1.NextPrimaryAttack, w2.NextPrimaryAttack);
        Assert.Equal(w1.SoonestPrimaryAttack, w2.SoonestPrimaryAttack);
        Assert.Equal(w1.Clip, w2.Clip);
    }

    // CWeaponPistol's accuracy penalty: 0.2 s a shot, which lerps GetBulletSpread
    // from VECTOR_CONE_1DEGREES to VECTOR_CONE_6DEGREES over 1.5 s, and drains
    // while the trigger is up. Found against stock (2026-10-25): this mod fired
    // every pistol round in one fixed two-degree cone.

    /// <summary>The cone's half-angle for a cone vector's sine.</summary>
    private static float HalfAngle(float sine) => MathF.Asin(sine) * 180f / MathF.PI;

    /// <summary>Clicks <paramref name="shots"/> rounds a tenth and a bit apart, from t = 10.</summary>
    private static float Click(WeaponLogic machine, WeaponNetState w, ViewModelNetState vm, int shots)
    {
        float t = 10f;
        for (int i = 0; i < shots; i++)
        {
            machine.RunCommand(w, vm, Attack, t, effectsLive: true);
            machine.RunCommand(w, vm, 0, t + 0.06f, effectsLive: true);
            machine.RunCommand(w, vm, 0, t + 0.11f, effectsLive: true);   // past the click's soonest refire
            t += 0.12f;
        }
        return t;
    }

    [Fact]
    public void TheFirstShotIsInTheOneDegreeCone()
    {
        var (machine, arms, w, vm) = Pistol();

        Click(machine, w, vm, 1);

        Assert.Equal(HalfAngle(0.00873f), arms.Spreads[0], 3);
    }

    [Fact]
    public void RapidShotsWidenTheConeToSixDegreesAndNoFurther()
    {
        var (machine, arms, w, vm) = Pistol();

        Click(machine, w, vm, 12);

        Assert.Equal(12, arms.Shots);
        Assert.True(arms.Spreads[1] > arms.Spreads[0], "the second click was no wider than the first");
        Assert.Equal(HalfAngle(0.05234f), arms.Spreads[^1], 3);
    }

    [Fact]
    public void ThePenaltyDrainsWhileTheTriggerIsUp()
    {
        var (machine, _, w, vm) = Pistol();
        float t = Click(machine, w, vm, 5);
        float owed = w.AccuracyPenalty;

        for (float u = t; u < t + 0.5f; u += 0.015f)
            machine.RunCommand(w, vm, 0, u, effectsLive: true);

        Assert.InRange(w.AccuracyPenalty, owed - 0.52f, owed - 0.46f);
    }

    [Fact]
    public void AReloadClearsThePenalty()
    {
        var (machine, _, w, vm) = Pistol();
        float t = Click(machine, w, vm, 5);
        Assert.True(w.AccuracyPenalty > 0f);

        machine.RunCommand(w, vm, (int)InputButtons.Reload, t + 0.6f, effectsLive: true);   // past the last shot's wait
        Assert.True(w.InReload, "the reload did not start");

        Assert.Equal(0f, w.AccuracyPenalty);
    }
}

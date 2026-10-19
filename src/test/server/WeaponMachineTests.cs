using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Phase 10: the weapon machine runs against the DECLARED states — the same
/// bytes the wire sends and the prediction ring copies — and the declarations
/// carry the [Predicted] schema stock kept in basecombatweapon_shared.cpp and
/// baseviewmodel_shared.cpp.
/// </summary>
public class WeaponMachineTests
{
    // This class's own catalog of the shared declarations (statics to zero).
    private readonly NetStateCatalog _catalog = new(TestServices.ServerGame, new RecordingLog());

    static WeaponMachineTests()
    {
        // The by-type-id map DescribeOffset reads is filled by discovery — the
        // same walk both modules run at startup. Once, before any test asks.
        StandaloneNetStates.Discover(typeof(WeaponNetState).Assembly, new RecordingLog());
    }

    //-------------------------------------------------------------------------
    // The declarations: where stock's 13 + 11 went.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheWeaponDeclaresItselfPredictable()
    {
        StandaloneNetStates.Discover(typeof(WeaponNetState).Assembly, new RecordingLog());

        // Every field of the weapon's state is machine state, so every field
        // is predicted — the 9c5 gate answers yes for it, which is what makes
        // the client replay it at all.
        Assert.Equal(10, _catalog.PredictedFieldCount(NetSchema.TypeIdOf(EntityNames.SsWeapon)));
        Assert.Equal(4, _catalog.PredictedFieldCount(NetSchema.TypeIdOf(EntityNames.SsViewModel)));
    }

    [Fact]
    public void TheTimersCarryStocksMillisecondTolerance()
    {
        // predictioncopy.h's TD_MSECTOLERANCE: a timer that differs by less
        // than a millisecond is two runs of the same arithmetic, not an error.
        var state = StandaloneNetStates.Of(typeof(WeaponNetState));

        foreach (string timer in new[] { "NextPrimaryAttack", "NextSecondaryAttack", "TimeWeaponIdle" })
        {
            int offset = state[timer].Offset;
            Assert.Equal(1, _catalog.DescribeOffset(
                NetSchema.TypeIdOf(EntityNames.SsWeapon), offset,
                out string name, out _, out _, out _, out float tolerance));
            Assert.Equal(timer, name);
            Assert.Equal(0.001f, tolerance);
        }
    }

    [Fact]
    public void ThePlaybackRateCarriesStocksTolerance()
    {
        var state = StandaloneNetStates.Of(typeof(ViewModelNetState));

        Assert.Equal(1, _catalog.DescribeOffset(
            NetSchema.TypeIdOf(EntityNames.SsViewModel), state["PlaybackRate"].Offset,
            out string name, out _, out _, out _, out float tolerance));
        Assert.Equal("PlaybackRate", name);
        Assert.Equal(0.125f, tolerance); // baseviewmodel_shared.cpp's own number
    }

    //-------------------------------------------------------------------------
    // The table: one row is one weapon, and the row carries what the scripts
    // used to.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryRowIsCompleteAndEveryPickupNameIsARow()
    {
        foreach (var type in WeaponTypes.All)
        {
            Assert.True(type.ViewModel.Length > 0, $"{type.Name} names a viewmodel");
            Assert.True(type.WorldModel.Length > 0, $"{type.Name} names a world model");

            if (type.AmmoName.Length > 0)
                Assert.True(AmmoCatalog.Stock().For(type.AmmoName) is not null,
                    $"{type.Name}'s ammo '{type.AmmoName}' has a row in AmmoTypes");

            if (type.SecondaryAmmoName.Length > 0)
                Assert.True(AmmoCatalog.Stock().For(type.SecondaryAmmoName) is not null,
                    $"{type.Name}'s secondary ammo '{type.SecondaryAmmoName}' has a row");

            if (type.IsMelee)
                Assert.True(type.MeleeRange > 0 && type.MeleeDamage > 0,
                    $"{type.Name} is melee and must say its reach and damage");
        }

        // The pickup class claims exactly the table's names: a row without an
        // attribute is a weapon no map can place, an attribute without a row
        // is a pickup that gives nothing.
        var claimed = typeof(WeaponPickup)
            .GetCustomAttributes<ScriptedEntityAttribute>()
            .Select(a => a.ClassName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var rows = WeaponTypes.All.Select(w => w.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(rows, claimed);
    }

    [Fact]
    public void TheTableOrderIsTheWireFormat()
    {
        // WeaponTypeId crosses the wire as an index into this table, so the
        // first nine rows are pinned in order — new weapons APPEND.
        string[] pinned =
        {
            "weapon_crowbar", "weapon_pistol", "weapon_357", "weapon_smg1",
            "weapon_ar2", "weapon_shotgun", "weapon_crossbow", "weapon_rpg",
            "weapon_frag",
        };

        for (int i = 0; i < pinned.Length; i++)
            Assert.Equal(pinned[i], WeaponTypes.All[i].Name);

        Assert.Equal(1, WeaponCatalog.Stock().IndexOf("WEAPON_PISTOL")); // case-blind, like a classname
        Assert.Null(WeaponCatalog.Stock().ByIndex(-1));
        Assert.Null(WeaponCatalog.Stock().ByIndex(WeaponTypes.All.Length));
    }

    //-------------------------------------------------------------------------
    // The machine, over locally-bound states and recording arms.
    //-------------------------------------------------------------------------

    private sealed class RecordingArms : IWeaponArms
    {
        public readonly List<string> Sounds = new();
        public int BulletsFired;
        public int PelletsFired;
        public int Swings;
        public readonly List<string> Launches = new();
        public float ReloadDuration = 1.5f;

        public void Sound(string soundScript) { if (soundScript.Length > 0) Sounds.Add(soundScript); }
        public void FireBullets(in WeaponType type, int pellets, float spread) { BulletsFired++; PelletsFired += pellets; }
        public void MeleeSwing(in WeaponType type) => Swings++;
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) => Launches.Add(projectile);
        public int LookupSequence(WeaponAnim anim) => (int)anim + 1;
        public float AnimDuration(WeaponAnim anim) => ReloadDuration;
    }

    private static (WeaponNetState w, ViewModelNetState vm) States(string weapon, int clip, int reserve, int secondary = 0)
    {
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();

        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf(weapon);
        w.Clip = clip;
        w.Reserve = reserve;
        w.SecondaryReserve = secondary;
        return (w, vm);
    }

    private const int Attack = (int)InputButtons.Attack;
    private const int Attack2 = (int)InputButtons.Attack2;
    private const int ReloadKey = (int)InputButtons.Reload;

    [Fact]
    public void OneTriggerPullSpendsOneRoundAndAdvancesTheClock()
    {
        var arms = new RecordingArms();
        var machine = new WeaponLogic(arms);
        var (w, vm) = States("weapon_pistol", clip: 18, reserve: 30);

        machine.RunCommand(w, vm, Attack, now: 10f, effectsLive: true);

        Assert.Equal(17, w.Clip);
        Assert.Equal(10.20f, w.NextPrimaryAttack, precision: 3); // the pistol's interval
        Assert.Equal(1, arms.BulletsFired);
        Assert.Single(arms.Sounds);
        Assert.Equal("Weapon_Pistol.Single", arms.Sounds[0]);
        Assert.True(vm.Sequence > 0, "the fire animation was sent");
    }

    [Fact]
    public void AReplayComputesTheSameStateWithoutTheEffects()
    {
        // The bucket-2 rule in person: effectsLive=false must change NOTHING
        // about the state, or the client's replay diverges from the server's
        // run of the same command and every shot reports a misprediction.
        var live = new RecordingArms();
        var (w1, vm1) = States("weapon_smg1", clip: 45, reserve: 100);
        new WeaponLogic(live).RunCommand(w1, vm1, Attack, 5f, effectsLive: true);

        var silent = new RecordingArms();
        var (w2, vm2) = States("weapon_smg1", clip: 45, reserve: 100);
        new WeaponLogic(silent).RunCommand(w2, vm2, Attack, 5f, effectsLive: false);

        Assert.Equal(w1.Clip, w2.Clip);
        Assert.Equal(w1.NextPrimaryAttack, w2.NextPrimaryAttack);
        Assert.Equal(vm1.Sequence, vm2.Sequence);
        Assert.Equal(vm1.AnimationParity, vm2.AnimationParity);

        Assert.Equal(1, live.BulletsFired);
        Assert.Equal(0, silent.BulletsFired);
        Assert.Empty(silent.Sounds);
    }

    [Fact]
    public void AnEmptyClipReloadsAndTheReloadFillsFromTheReserve()
    {
        var arms = new RecordingArms { ReloadDuration = 2f };
        var machine = new WeaponLogic(arms);
        var (w, vm) = States("weapon_pistol", clip: 0, reserve: 11);

        machine.RunCommand(w, vm, Attack, now: 10f, effectsLive: true);

        Assert.True(w.InReload, "an empty clip starts a reload");
        Assert.Equal(12f, w.NextPrimaryAttack, precision: 3); // now + the animation's length

        // Mid-reload, nothing happens.
        machine.RunCommand(w, vm, Attack, now: 11f, effectsLive: true);
        Assert.True(w.InReload);
        Assert.Equal(0, w.Clip);

        // Done: the last eleven rounds load as eleven, not as nothing.
        machine.RunCommand(w, vm, 0, now: 12.1f, effectsLive: true);
        Assert.False(w.InReload);
        Assert.Equal(11, w.Clip);
        Assert.Equal(0, w.Reserve);
    }

    [Fact]
    public void TheCrowbarSwingsAndNeverClicksEmpty()
    {
        var arms = new RecordingArms();
        var machine = new WeaponLogic(arms);
        var (w, vm) = States("weapon_crowbar", clip: -1, reserve: 0);

        machine.RunCommand(w, vm, Attack, now: 3f, effectsLive: true);

        Assert.Equal(1, arms.Swings);
        Assert.Equal(0, arms.BulletsFired);
        Assert.DoesNotContain(arms.Sounds, s => s.Contains("Empty"));
    }

    [Fact]
    public void TheDoubleBlastSpendsTwoShellsFromTheClipAndRefusesWithOne()
    {
        var arms = new RecordingArms();
        var machine = new WeaponLogic(arms);
        var (w, vm) = States("weapon_shotgun", clip: 6, reserve: 0);

        machine.RunCommand(w, vm, Attack2, now: 5f, effectsLive: true);
        Assert.Equal(4, w.Clip);
        Assert.Equal(12, arms.PelletsFired); // both barrels

        // One shell left: no half blast, no shell lost.
        w.Clip = 1;
        machine.RunCommand(w, vm, Attack2, now: 10f, effectsLive: true);
        Assert.Equal(1, w.Clip);
        Assert.Contains(arms.Sounds, s => s.Contains("Empty"));
    }

    [Fact]
    public void TheLeadingEdgeResetsTheClockSoALullOwesNoBurst()
    {
        var arms = new RecordingArms();
        var machine = new WeaponLogic(arms);
        var (w, vm) = States("weapon_smg1", clip: 45, reserve: 0);

        // Fire once at t=1, release, then press again after a long lull. The
        // catch-up loop must owe ONE shot, not one per interval since t=1.
        machine.RunCommand(w, vm, Attack, now: 1f, effectsLive: true);
        int afterFirst = w.Clip;
        machine.RunCommand(w, vm, 0, now: 1.1f, effectsLive: true);

        machine.RunCommand(w, vm, Attack, now: 60f, effectsLive: true);

        Assert.Equal(afterFirst - 1, w.Clip);
    }

    [Fact]
    public void ThePoolFedWeaponsLaunchTheirProjectiles()
    {
        var arms = new RecordingArms();
        var machine = new WeaponLogic(arms);

        var (rpg, vm) = States("weapon_rpg", clip: -1, reserve: 2);
        machine.RunCommand(rpg, vm, Attack, now: 5f, effectsLive: true);
        Assert.Equal(new[] { "ss_rocket" }, arms.Launches);
        Assert.Equal(1, rpg.Reserve);

        var (frag, vm2) = States("weapon_frag", clip: -1, reserve: 1);
        machine.RunCommand(frag, vm2, Attack, now: 5f, effectsLive: true);
        Assert.Contains("ss_grenade", arms.Launches);
        Assert.Equal(0, frag.Reserve);

        // Out of grenades: an empty click, not a throw.
        machine.RunCommand(frag, vm2, Attack, now: 10f, effectsLive: true);
        Assert.Equal(2, arms.Launches.Count);
    }

    [Fact]
    public void TheSecondaryPoolPaysForTheAltFireOrRefusesWholly()
    {
        var arms = new RecordingArms();
        var machine = new WeaponLogic(arms);
        var (w, vm) = States("weapon_smg1", clip: 45, reserve: 0, secondary: 1);

        machine.RunCommand(w, vm, Attack2, now: 5f, effectsLive: true);
        Assert.Contains("ss_grenade", arms.Launches);
        Assert.Equal(0, w.SecondaryReserve);
        Assert.Equal(45, w.Clip); // the launcher never touches the clip

        machine.RunCommand(w, vm, Attack2, now: 10f, effectsLive: true);
        Assert.Single(arms.Launches);
        Assert.Contains(arms.Sounds, s => s.Contains("Empty"));
    }
}

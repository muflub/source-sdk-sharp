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
    private readonly NetStateCatalog _catalog = NetStateCatalog.Of(TestServices.ServerGame, new RecordingLog());

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
        // the client replay it at all. The machine's and the HL2MP behaviours',
        // the SLAM's two, eleven of the physcannon's (PhysCannonBehaviour), and
        // the pistol's accuracy penalty with its clock, the TF owner's long-term
        // random-crit rate (stock's m_flObservedCritChance,
        // tf_weaponbase.h:761), the Huntsman's arrow-alight bit the flame sets
        // (stock's m_bArrowAlight, tf_weapon_compound_bow.cpp:36), and a
        // behaviour's own seven
        // (WeaponNetState.Behaviour*: a mod's weapon state, the TF weapons' —
        // six the deploy clears, and the kept clock the TF revolver's aim
        // rides, whose stock member the constructor alone zeroes), and the
        // deploy stamp that lets the client tell a server's (re)hand-out from
        // a mispredicted switch (WeaponNetState.DeployEpoch), and when the trigger
        // went down, what a machine gun's kick grows on (WeaponNetState.FireStartedAt),
        // and when a charging alt-fire goes off (WeaponNetState.ChargeFiresAt),
        // and the Huntsman's lit arrow (WeaponNetState.ArrowAlight),
        // and the last deploy's stamp the switch-from arm asks
        // (WeaponNetState.LastDeployTime).
        Assert.Equal(51, _catalog.PredictedFieldCount(NetSchema.TypeIdOf(EntityNames.SsWeapon)));
        Assert.Equal(5, _catalog.PredictedFieldCount(NetSchema.TypeIdOf(EntityNames.PredictedViewModel)));
    }

    [Fact]
    public void TheTimersCarryStocksMillisecondTolerance()
    {
        // predictioncopy.h's TD_MSECTOLERANCE: a timer that differs by less
        // than a millisecond is two runs of the same arithmetic, not an error.
        var state = StandaloneNetStates.Of(typeof(WeaponNetState));

        foreach (string timer in new[] { "NextPrimaryAttack", "NextSecondaryAttack", "TimeWeaponIdle", "BehaviourTimeA", "BehaviourTimeB", "BehaviourTimeC" })
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
            NetSchema.TypeIdOf(EntityNames.PredictedViewModel), state["PlaybackRate"].Offset,
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

            // The physcannon spends nothing and throws nothing, which is the
            // row's definition of melee; it never swings, because its
            // behaviour runs the whole command.
            if (type.IsMelee && type.Behaviour != PhysCannonBehaviour.Key)
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

    /// <summary>
    /// A row's behaviour answering the looping-anim seam as stock's minigun
    /// does (<c>CTFMinigun::SendWeaponAnim</c>, the activity comparison): the
    /// anim being sent and the one already playing are both the fire anim.
    /// </summary>
    private sealed class LoopingBehaviour : IWeaponBehaviour
    {
        public bool AnimContinuesOnResend(in WeaponAttackContext attack, WeaponAnim anim) =>
            anim == WeaponAnim.Primary &&
            attack.ViewModel.Sequence == attack.Arms.LookupSequence(anim);
    }

    /// <summary>The looping answer claimed for the SMG's key, over the core's.</summary>
    private sealed class LoopSource : IWeaponBehaviourSource
    {
        public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
        {
            yield return new(Hl2mpWeaponBehaviours.Smg1, new LoopingBehaviour());
        }
    }

    [Fact]
    public void OneTriggerPullSpendsOneRoundAndAdvancesTheClock()
    {
        var arms = new RecordingArms();
        var machine = new WeaponLogic(arms);
        var (w, vm) = States("weapon_pistol", clip: 18, reserve: 30);

        machine.RunCommand(w, vm, Attack, now: 10f, effectsLive: true);

        Assert.Equal(17, w.Clip);
        Assert.Equal(10.5f, w.NextPrimaryAttack, precision: 3); // the pistol's GetFireRate()
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

    /// <summary>
    /// The looping-anim seam, a per-word <see cref="IWeaponBehaviour"/>
    /// answer at the machine's own send: where the row's behaviour answers
    /// as <c>CTFMinigun::SendWeaponAnim</c> does
    /// (<c>tf_weapon_minigun.cpp:961-967</c> — comparing the ACTIVITIES, the
    /// fire anim being sent and the one playing: "If we're already playing
    /// the fire anim, let it continue. It loops."), a re-send of that anim
    /// keeps it running. The restart frame is the pose that hangs the merged
    /// attachment out of view for the whole burst; the hands alone until the
    /// trigger comes off.
    /// </summary>
    [Fact]
    public void AHeldTriggerLetsTheFireAnimLoopRatherThanRestartItEveryShot()
    {
        var arms = new RecordingArms();
        var machine = new WeaponLogic(arms, behaviours: new WeaponBehaviours(() => new[] { new LoopSource() }));
        var (w, vm) = States("weapon_smg1", clip: 45, reserve: 100);

        machine.RunCommand(w, vm, Attack, now: 10f, effectsLive: true);
        Assert.True(vm.Sequence > 0, "the first shot sends the fire anim");
        int firstShotParity = vm.AnimationParity;

        machine.RunCommand(w, vm, Attack, now: 10.2f, effectsLive: true);

        Assert.True(arms.BulletsFired >= 2, "the burst reaches the subject");
        Assert.Equal(firstShotParity, vm.AnimationParity);   // the loop keeps running
    }

    /// <summary>
    /// Stock's base, the other half of the seam: every weapon without a loop
    /// answer — the pistol among them — goes through
    /// <c>CBaseCombatWeapon::SendWeaponAnim</c>, which restarts the anim on
    /// every send. The second shot of a held burst re-sends the fire anim
    /// with a fresh parity bump, not a continued loop.
    /// </summary>
    [Fact]
    public void APistolStillRestartsItsFireAnimOnEveryShotOfAHeldBurst()
    {
        var arms = new RecordingArms();
        var machine = new WeaponLogic(arms);   // stock behaviours: the base answer restarts
        var (w, vm) = States("weapon_pistol", clip: 18, reserve: 30);

        machine.RunCommand(w, vm, Attack, now: 10f, effectsLive: true);
        Assert.True(vm.Sequence > 0, "the first shot sends the fire anim");
        int firstShotParity = vm.AnimationParity;

        machine.RunCommand(w, vm, Attack, now: 10.5f, effectsLive: true);   // the pistol's whole interval

        Assert.Equal(2, arms.BulletsFired);
        Assert.NotEqual(firstShotParity, vm.AnimationParity);   // stock's base restart
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
    public void TheDoubleBlastSpendsTwoShellsFromTheClipAndIsASingleWithOne()
    {
        var arms = new RecordingArms();
        var machine = new WeaponLogic(arms);
        var (w, vm) = States("weapon_shotgun", clip: 6, reserve: 0);

        machine.RunCommand(w, vm, Attack2, now: 5f, effectsLive: true);
        Assert.Equal(4, w.Clip);
        Assert.Equal(12, arms.PelletsFired); // both barrels

        // The pump the double owes, then one shell left: CWeaponShotgun fires
        // a single instead ("If only one shell is left, do a single shot").
        machine.RunCommand(w, vm, 0, now: 10f, effectsLive: true);
        w.Clip = 1;
        machine.RunCommand(w, vm, Attack2, now: 20f, effectsLive: true);
        Assert.Equal(0, w.Clip);
        Assert.Equal(12 + 7, arms.PelletsFired);
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
    public void APoolFedWeaponLaunchesItsProjectile()
    {
        // The frag and the RPG are pool-fed too, and launch through their own
        // machines (Hl2mpFragTests, Hl2mpRpgTests); the generic path is a
        // mod's row with no behaviour.
        var arms = new RecordingArms();
        var row = new WeaponType("weapon_test_launcher", Interval: 1f)
        {
            Projectile = "rpg_missile",
            LaunchSpeed = 1500f,
            AmmoName = "RPG_Round",
        };
        var machine = new WeaponLogic(arms, new WeaponCatalog(() => [new Source(row)]));

        var (w, vm) = States("weapon_rpg", clip: -1, reserve: 2);
        w.WeaponTypeId = new WeaponCatalog(() => [new Source(row)]).IndexOf("weapon_test_launcher");
        machine.RunCommand(w, vm, Attack, now: 5f, effectsLive: true);
        Assert.Equal(new[] { "rpg_missile" }, arms.Launches);
        Assert.Equal(1, w.Reserve);
    }

    private sealed class Source(WeaponType row) : IWeaponTypeSource
    {
        public IEnumerable<WeaponType> Rows() => [row];
    }

    [Fact]
    public void TheSecondaryPoolPaysForTheAltFireOrRefusesWholly()
    {
        var arms = new RecordingArms();
        var machine = new WeaponLogic(arms);
        var (w, vm) = States("weapon_smg1", clip: 45, reserve: 0, secondary: 1);

        machine.RunCommand(w, vm, Attack2, now: 5f, effectsLive: true);
        Assert.Contains("grenade_ar2", arms.Launches);
        Assert.Equal(0, w.SecondaryReserve);
        Assert.Equal(45, w.Clip); // the launcher never touches the clip

        machine.RunCommand(w, vm, Attack2, now: 10f, effectsLive: true);
        Assert.Single(arms.Launches);
        Assert.Contains(arms.Sounds, s => s.Contains("Empty"));
    }

    /// <summary>
    /// A melee weapon with an unregistered ammo-name literal in its script row
    /// is a weapon that USES NO AMMO at all — stock compares the name through
    /// <c>GetAmmoDef()->Index</c> (basecombatweapon_shared.cpp:263): a name the
    /// ammo system does not know leaves the index at -1, and UsesPrimaryAmmo is
    /// index &gt;= 0 (:1306-1321), so HasAnyAmmo early-trues (:1239-1247) and
    /// ReloadOrSwitchWeapons never swaps a fist out. The fists' script says
    /// lowercase <c>none</c>, which ParseAmmo keeps as a literal (stock's strcmp
    /// parity), so the machine must read it as "no ammo needed", not as "an ammo
    /// type named none". Before the fix the managed gate was the name's LENGTH,
    /// the fists reported empty, and firing them dropped the player back to the
    /// shotgun after the swing's clock ran out.
    /// </summary>
    [Fact]
    public void AFistWithAnUnregisteredAmmoLiteralKeepsItselfAfterFiring()
    {
        var arms = new SwitchCountingArms();
        var row = new WeaponType("tf_weapon_fists", Interval: 0.8f)
        {
            AmmoName = "none", // lowercase literal: not an AmmoTypes row
            MeleeRange = 48f,
            MeleeDamage = 65f,
        };
        var machine = new WeaponLogic(arms, new WeaponCatalog(() => [new Source(row)]));

        var (w, vm) = States("weapon_crowbar", clip: -1, reserve: 0);
        w.WeaponTypeId = new WeaponCatalog(() => [new Source(row)]).IndexOf("tf_weapon_fists");

        // Fire once, let the click's interval pass, then an empty command: the
        // idle tail runs ReloadOrSwitchWeapons. The fists still hold.
        machine.RunCommand(w, vm, Attack, now: 1f, effectsLive: true);
        machine.RunCommand(w, vm, 0, now: 3f, effectsLive: true);

        Assert.Equal(0, arms.SwitchCount);
    }

    /// <summary>
    /// The other arm, so the first cannot pass by always answering yes: a row
    /// whose ammo name IS registered and whose pools are empty still takes the
    /// switch — stock's empty weapon hands itself to the next best one.
    /// </summary>
    [Fact]
    public void ARegisteredEmptyPoolStillHandsToTheNextBestWeapon()
    {
        var arms = new SwitchCountingArms();
        var row = new WeaponType("wep.poolfed", Interval: 0.5f)
        {
            AmmoName = "Buckshot", // a stock AmmoTypes row
        };
        var machine = new WeaponLogic(arms, new WeaponCatalog(() => [new Source(row)]));

        var (w, vm) = States("weapon_crowbar", clip: -1, reserve: 0);
        w.WeaponTypeId = new WeaponCatalog(() => [new Source(row)]).IndexOf("wep.poolfed");

        machine.RunCommand(w, vm, Attack, now: 1f, effectsLive: true);
        machine.RunCommand(w, vm, 0, now: 3f, effectsLive: true);

        Assert.Equal(1, arms.SwitchCount);
    }

    /// <summary>The recording arms, plus a count of the exhausted-switch.</summary>
    private sealed class SwitchCountingArms : IWeaponArms
    {
        public int SwitchCount;

        public void Sound(string soundScript) { }
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) { }
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }
        public int LookupSequence(WeaponAnim anim) => (int)anim + 1;
        public float AnimDuration(WeaponAnim anim) => 1.5f;
        public bool SwitchToNextBest()
        {
            SwitchCount++;
            return true;
        }
    }

    /// <summary>
    /// CWeaponCrowbar's GetDamageForActivity 25 and CROWBAR_REFIRE 0.4, CWeaponStunStick's 40 and
    /// STUNSTICK_REFIRE 0.8. A melee row's damage has no weapon script to come from, so the row
    /// is the number: the crowbar's was 10 every 0.5, a third of stock's rate.
    /// </summary>
    [Theory]
    [InlineData("weapon_crowbar", 25f, 0.4f)]
    [InlineData("weapon_stunstick", 40f, 0.8f)]
    public void A_melee_rows_damage_and_refire_are_stocks(string name, float damage, float interval)
    {
        WeaponType row = Assert.Single(WeaponTypes.All, w => w.Name == name);

        Assert.Equal(damage, row.MeleeDamage);
        Assert.Equal(interval, row.Interval);
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// CBaseCombatWeapon::ReloadOrSwitchWeapons and CMultiplayRules::
/// GetNextBestWeapon: a weapon with nothing left to fire, nothing held and its
/// wait over is put away for the next best weapon by script weight -- the
/// first other of the same weight with any ammo, else the heaviest with any.
///
/// <para>
/// Found side by side with stock (docs/divergences.md, the empty RPG row,
/// 2026-10-25): this mod kept an empty weapon in the hands.
/// </para>
/// </summary>
public class ExhaustedSwitchTests
{
    private sealed class Arms(bool switches) : IWeaponArms
    {
        public int Asked;

        public void Sound(string soundScript) { }
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees) { }
        public void MeleeSwing(in WeaponType type) { }
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown) { }
        public int LookupSequence(WeaponAnim anim) => (int)anim + 1;
        public float AnimDuration(WeaponAnim anim) => 0.5f;

        public bool SwitchToNextBest()
        {
            Asked++;
            return switches;
        }
    }

    private static (Arms Arms, WeaponNetState W) Idle(string weapon, int clip, int reserve, bool effectsLive = true,
                                                      int buttons = 0)
    {
        var arms = new Arms(switches: true);
        var w = new WeaponNetState();
        w.BindLocal();
        var vm = new ViewModelNetState();
        vm.BindLocal();
        w.WeaponTypeId = WeaponCatalog.Stock().IndexOf(weapon);
        w.Clip = clip;
        w.Reserve = reserve;

        new WeaponLogic(arms).RunCommand(w, vm, buttons, 10f, effectsLive);
        return (arms, w);
    }

    [Fact]
    public void AnEmptyPistolAsksForTheNextBest()
        => Assert.Equal(1, Idle(EntityNames.WeaponPistol, clip: 0, reserve: 0).Arms.Asked);

    /// <summary>ReloadOrSwitchWeapons' m_flNextPrimaryAttack = curtime + 0.3 (the pistol's own tail rewrites it, as stock's does).</summary>
    [Fact]
    public void AnEmpty357AsksAndWaitsThreeTenthsMore()
    {
        var (arms, w) = Idle(EntityNames.Weapon357, clip: 0, reserve: 0);

        Assert.Equal(1, arms.Asked);
        Assert.Equal(10f + WeaponLogic.ExhaustedSwitchDelay, w.NextPrimaryAttack, 3);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 5)]
    public void APistolWithAnyAmmoStays(int clip, int reserve)
        => Assert.Equal(0, Idle(EntityNames.WeaponPistol, clip, reserve).Arms.Asked);

    [Fact]
    public void AWeaponThatUsesNoAmmoAlwaysHasSome()
        => Assert.Equal(0, Idle(EntityNames.WeaponCrowbar, clip: -1, reserve: 0).Arms.Asked);

    [Fact]
    public void AHeldTriggerKeepsTheEmptyGun()
        => Assert.Equal(0, Idle(EntityNames.WeaponPistol, 0, 0, buttons: (int)InputButtons.Attack).Arms.Asked);

    [Fact]
    public void TheReplayNeverSwitches()
        => Assert.Equal(0, Idle(EntityNames.WeaponPistol, 0, 0, effectsLive: false).Arms.Asked);

    [Theory]
    [InlineData(EntityNames.WeaponRpg)]
    [InlineData(EntityNames.WeaponFrag)]
    [InlineData(EntityNames.WeaponCrossbow)]
    [InlineData(EntityNames.WeaponShotgun)]
    public void TheWeaponsWithTheirOwnMachineAskToo(string weapon)
    {
        var (arms, _) = Idle(weapon, clip: weapon == EntityNames.WeaponRpg || weapon == EntityNames.WeaponFrag ? -1 : 0, reserve: 0);
        Assert.True(arms.Asked > 0, $"an empty {weapon} did not ask to be put away");
    }

    // =====================================================================
    // The arsenal's pick, over the weapons' scripts.
    // =====================================================================

    private static EntityHandle Player => new(0x0001_0001);

    private static readonly string[] TheSmgSet =
        [EntityNames.WeaponCrowbar, EntityNames.WeaponPistol, EntityNames.WeaponSmg1];

    /// <summary>The pistol's row answering a refusal, over the core behaviours the rest of the rig keeps.</summary>
    private static WeaponBehaviours DryBehaviours()
        => new(() => new IWeaponBehaviourSource[] { new DrySource() });

    private static string Script(int weight, int flags = 0, bool autoTo = true, bool autoFrom = true)
        => "WeaponData\n{\n\"weight\" \"" + weight + "\"\n\"item_flags\" \"" + flags + "\"\n"
         + "\"autoswitchto\" \"" + (autoTo ? 1 : 0) + "\"\n\"autoswitchfrom\" \"" + (autoFrom ? 1 : 0) + "\"\n}\n";

    /// <summary>A behaviour whose rows never come back into the hands: the stand-in CanDeploy refusal.</summary>
    private sealed class DryBehaviour : IWeaponBehaviour
    {
        public bool CanDeploy(in WeaponAttackContext attack) => false;
    }

    private sealed class DrySource : IWeaponBehaviourSource
    {
        public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
        {
            yield return KeyValuePair.Create<string, IWeaponBehaviour>(Hl2mpWeaponBehaviours.Pistol, new DryBehaviour());
        }
    }

    private sealed record Rig(Arsenal Arsenal, RecordingEngine Engine, ScriptedEntityRegistry Registry);

    private static Rig Armed(int smgFlags, WeaponBehaviours? behaviours = null, int pistolWeight = 2,
                             int crowbarWeight = 0, bool smgAutoFrom = true, bool crowbarAutoTo = true,
                             params string[] weapons)
    {
        var files = new Dictionary<string, string>
        {
            ["scripts/weapon_crowbar.txt"] = Script(crowbarWeight, autoTo: crowbarAutoTo),
            ["scripts/weapon_pistol.txt"] = Script(pistolWeight),
            ["scripts/weapon_smg1.txt"] = Script(3, smgFlags, autoFrom: smgAutoFrom),
            ["scripts/weapon_357.txt"] = Script(7),
        };
        var scripts = new WeaponScriptDatabase(new RecordingLog(), p => files.GetValueOrDefault(p));

        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.Player)
            .Returns("Entities.GetOrigin", Vector.Zero)
            .Returns("Entities.GetEyeAngles", Vector.Zero)
            .Returns("Players.GetShootOrigin", new Vector(0f, 0f, 64f))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", 10f);
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out var entityEngine);
        entityEngine.Strict = false;
        registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_player");

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);
        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups(), behaviours: behaviours,
                                  scripts: scripts);
        foreach (string w in weapons)
            Assert.True(arsenal.GiveWeapon(Player, w), $"the fixture's player has {w}");
        engine.Returns("Entities.IsValid", true);
        engine.Strict = false;
        return new Rig(arsenal, engine, registry);
    }

    /// <summary>Empties the SMG1 in the hands, runs an idle command long after its draw, and answers what is held.</summary>
    private static string? EmptyTheSmgAndIdle(Rig rig)
    {
        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, EntityNames.WeaponSmg1));
        var carry = rig.Arsenal.CarryFor(Player);
        carry.Ammo["SMG1"] = 0;
        carry.Ammo["SMG1_Grenade"] = 0;
        ((SsWeapon)rig.Registry.Get(rig.Arsenal.CarriedPair(Player).Weapon)!).State.Clip = 0;

        rig.Engine.Returns("Clock.Now", 30f);
        var cmd = new UserCmd();
        rig.Arsenal.OnPlayerCommand(Player, in cmd, firstTime: true);

        return rig.Arsenal.Holdings(Player).Single(h => h.Active).ClassName;
    }

    [Fact]
    public void TheHeaviestOtherWeaponWithAmmoIsTaken()
    {
        var rig = Armed(0, weapons: TheSmgSet);

        Assert.Equal(EntityNames.WeaponPistol, EmptyTheSmgAndIdle(rig));
    }

    [Fact]
    public void AWeaponOutOfAmmoIsNoChoice()
    {
        var rig = Armed(0, weapons: TheSmgSet);
        rig.Arsenal.CarryFor(Player).Ammo["357"] = 0;
        foreach (var owned in rig.Arsenal.CarryFor(Player).Weapons)
            owned.Clip = 0;

        Assert.Equal(EntityNames.WeaponCrowbar, EmptyTheSmgAndIdle(rig));
    }

    [Fact]
    public void AWeaponThatWillNotBeSwitchedFromEmptyStays()
    {
        var rig = Armed(smgFlags: (int)WeaponItemFlags.NoAutoSwitchEmpty, weapons: TheSmgSet);

        Assert.Equal(EntityNames.WeaponSmg1, EmptyTheSmgAndIdle(rig));
    }

    [Fact]
    public void AWeaponTheScriptBarsTheSwitchFromStays()
    {
        // CMultiplayRules::GetNextBestWeapon's first gate
        // (shared/multiplay_gamerules.cpp:524): the held weapon's
        // AllowsAutoSwitchFrom — the script's autoswitchfrom. The physcannon's
        // row is the shipped 0.
        var rig = Armed(smgFlags: 0, smgAutoFrom: false, weapons: TheSmgSet);

        Assert.Equal(EntityNames.WeaponSmg1, EmptyTheSmgAndIdle(rig));
    }

    [Fact]
    public void ACandidateTheScriptBarsTheSwitchToIsSkipped()
    {
        // The per-candidate gate (shared/multiplay_gamerules.cpp:542): the
        // crowbar row's autoswitchto 0 leaves the pistol the heaviest left.
        var rig = Armed(smgFlags: 0, crowbarWeight: 7, crowbarAutoTo: false, weapons: TheSmgSet);

        Assert.Equal(EntityNames.WeaponPistol, EmptyTheSmgAndIdle(rig));
    }

    [Fact]
    public void ASameWeightCandidateThatCannotDeployIsPassedOver()
    {
        // The same-weight handover's Weapon_CanSwitchTo
        // (shared/multiplay_gamerules.cpp:550): a candidate whose behaviour
        // refuses the deploy is passed over for the best behind it.
        var rig = Armed(smgFlags: 0, behaviours: DryBehaviours(), pistolWeight: 3, weapons: TheSmgSet);

        Assert.Equal(EntityNames.WeaponCrowbar, EmptyTheSmgAndIdle(rig));
    }
}

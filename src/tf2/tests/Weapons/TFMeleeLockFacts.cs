using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// The Buffalo Steak's melee lock at the switch: <c>CTFWeaponBaseMelee::CanHolster</c>
/// (<c>tf_weaponbase_melee.cpp:86-94</c>) answers no while the owner is in
/// <c>TF_COND_CANNOT_SWITCH_FROM_MELEE</c>, so <c>Weapon_Switch</c> keeps the
/// melee in the hands. The heavy's fists and shotgun ride the real TF
/// behaviour chain and the core arsenal's switch.
/// </summary>
public sealed class TFMeleeLockFacts
{
    private static readonly EntityHandle Player = new(0x0001_0000u | TFWeaponWorld.Owner);

    private const string Fists = "tf_weapon_fists";
    private const string Shotgun = "tf_weapon_shotgun_hwg";

    /// <summary>Under the lock the fists refuse the switch, by name and by slot alike.</summary>
    [Fact]
    public void UnderTheLockTheMeleeRefusesTheSwitch()
    {
        var rig = new SwitchRig();
        rig.HoldFists();

        rig.Weapons.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.CannotSwitchFromMelee, 16f);

        Assert.Equal(SwitchOutcome.CannotHolster, rig.Arsenal.Switch(Player, Shotgun));
        Assert.Equal(SwitchOutcome.CannotHolster, rig.Arsenal.SwitchSlot(Player, rig.SlotOf(Shotgun)));
        Assert.Equal(Fists, rig.Arsenal.ActiveWeaponName(Player));
    }

    /// <summary>The control: the same walk without the condition switches.</summary>
    [Fact]
    public void WithoutTheLockTheMeleeSwitchGoes()
    {
        var rig = new SwitchRig();
        rig.HoldFists();
        rig.Weapons.Owners.Of(TFWeaponWorld.Owner);                // a condition bank, empty

        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, Shotgun));
        Assert.Equal(Shotgun, rig.Arsenal.ActiveWeaponName(Player));
    }

    /// <summary>The lock lifted (the steak's sixteen seconds over): the fists let go again.</summary>
    [Fact]
    public void TheLockRemovedFreesTheSwitch()
    {
        var rig = new SwitchRig();
        rig.HoldFists();
        var conditions = rig.Weapons.Owners.Of(TFWeaponWorld.Owner);
        conditions.Add(TFCond.CannotSwitchFromMelee, 16f);
        Assert.Equal(SwitchOutcome.CannotHolster, rig.Arsenal.Switch(Player, Shotgun));

        conditions.Remove(TFCond.CannotSwitchFromMelee);

        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, Shotgun));
        Assert.Equal(Shotgun, rig.Arsenal.ActiveWeaponName(Player));
    }

    /// <summary>
    /// The lock is the melee base's alone: a shotgun in the hands under the
    /// condition answers the base's true, so the switch to the fists goes.
    /// </summary>
    [Fact]
    public void TheLockHoldsOnlyAMelee()
    {
        var rig = new SwitchRig();
        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, Shotgun));
        rig.Weapons.Owners.Of(TFWeaponWorld.Owner).Add(TFCond.CannotSwitchFromMelee, 16f);

        Assert.Equal(SwitchOutcome.Switched, rig.Arsenal.Switch(Player, Fists));
        Assert.Equal(Fists, rig.Arsenal.ActiveWeaponName(Player));
    }

    // ---------------------------------------------------------------------

    /// <summary>One heavy's carry over the real TF behaviour chain and the arsenal whose switch asks it.</summary>
    private sealed class SwitchRig
    {
        public readonly RecordingEngine Engine;
        public readonly ScriptedEntityRegistry Registry;
        public readonly Arsenal Arsenal;
        public readonly TFWeaponWorld Weapons = new();

        private readonly EntityHandle _weapon = new(0x0003_0010u);
        private readonly EntityHandle _viewModel = new(0x0003_0011u);

        public SwitchRig()
        {
            var created = new Queue<EntityHandle>([_weapon, _viewModel]);
            Engine = new RecordingEngine()
                .Returns("Entities.IsValid", true)
                .Returns("Entities.GetIndex", TFWeaponWorld.Owner)
                .Returns("Entities.GetClassName", EntityNames.Player)
                .Returns("Entities.GetOrigin", default(Vector))
                .Returns("Entities.GetAngles", default(Vector))
                .Returns("Players.IsAlive", true)
                .Returns("Entities.GetEyeAngles", default(Vector))
                .Returns("EntityRender.LookupSequence", -1)
                .Returns("EntityRender.LookupSequenceByActivity", -1)
                .Returns("Clock.Now", 100f)
                .ReturnsEachCall("Entities.Create", () => created.Dequeue());

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(SsWeapon).Assembly);
            Registry.Services = TestServices.Services(out _);
            Registry.KeyValue(Player, EntityNames.Player, "targetname", "fixture_heavy");
            Registry.Spawn(_weapon, EntityNames.SsWeapon);
            Registry.Spawn(_viewModel, EntityNames.PredictedViewModel);

            Weapons.Owners.Classes[TFWeaponWorld.Owner] = TFClass.HeavyWeapons;
            Arsenal = new Arsenal(new RecordingLog(), Engine, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: Weapons.Catalog, ammo: Weapons.Ammo, behaviours: Weapons.Behaviours);

            Assert.True(Arsenal.GiveWeapon(Player, Fists), "the rig's heavy carries the fists");
            Assert.True(Arsenal.GiveWeapon(Player, Shotgun), "the rig's heavy carries the shotgun");
        }

        /// <summary>The fists drawn by a real switch, from the shotgun.</summary>
        public void HoldFists()
        {
            Assert.Equal(SwitchOutcome.Switched, Arsenal.Switch(Player, Shotgun));
            Assert.Equal(SwitchOutcome.Switched, Arsenal.Switch(Player, Fists));
            Assert.Equal(Fists, Arsenal.ActiveWeaponName(Player));
        }

        /// <summary>A weapon's carry slot, as the switch's own walk names it.</summary>
        public int SlotOf(string weapon)
        {
            int slot = Arsenal.Holdings(Player).ToList().FindIndex(h => h.ClassName == weapon);
            Assert.True(slot >= 0, "the carry holds " + weapon);
            return slot;
        }
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server;
using TF2.Shared;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The Widowmaker's pool, on the carrier: the item's
/// <c>mod_use_metal_ammo_type</c> is the spawn arm that points the weapon's
/// primary at <c>TF_AMMO_METAL</c> (<c>basecombatweapon_shared.cpp:269-276</c>),
/// so the reserve the machine spends is the engineer's metal, not the row's
/// own (<see cref="global::TF2.Tests.Weapons.TFWidowmakerTests"/> holds the
/// press).
/// </summary>
public sealed class TFWidowmakerPoolTests
{
    private const string Shotgun = "tf_weapon_shotgun_primary";

    [Fact]
    public void TheWidowmakerFeedsFromTheMetalTheCarrierHolds()
    {
        var world = new PoolWorld(withItem: true);

        world.Arsenal.GiveAmmo(PlayerRig.Player, 100, TFAmmoNames.Name(TFAmmo.Metal));
        world.Arsenal.GiveAmmo(PlayerRig.Player, 24, TFAmmoNames.Name(TFAmmo.Primary));
        Assert.True(world.Arsenal.GiveWeapon(PlayerRig.Player, Shotgun), "the arsenal gives the shotgun");

        // The clip none and the metal the reserve: the override moved the
        // primary, the spawn's default clip into the pool it names
        // (Weapon_Equip's GiveAmmo(GetDefaultClip1(), m_iPrimaryAmmoType)).
        // The state's clip is the minus one a clipless row carries
        // (iClip1 -1 in the weapon data); the fund is the metal plus the
        // equip's own default round, the clipless arm of the give.
        Assert.Equal(-1, world.Weapon.State.Clip);
        Assert.Equal(101, world.Weapon.State.Reserve);
    }

    [Fact]
    public void ThePlainShotgunFeedsFromTheRow()
    {
        var world = new PoolWorld(withItem: false);

        world.Arsenal.GiveAmmo(PlayerRig.Player, 100, TFAmmoNames.Name(TFAmmo.Metal));
        world.Arsenal.GiveAmmo(PlayerRig.Player, 24, TFAmmoNames.Name(TFAmmo.Primary));
        Assert.True(world.Arsenal.GiveWeapon(PlayerRig.Player, Shotgun), "the arsenal gives the shotgun");

        // No item, the script's own pool: the clip six and the odd rounds
        // past it in TF_AMMO_PRIMARY, the metal untouched.
        Assert.Equal(6, world.Weapon.State.Clip);
        Assert.Equal(24, world.Weapon.State.Reserve);
    }

    /// <summary>The carrier rig joined to the core arsenal, the behaviours the
    /// weapon world's — the pool ask is theirs to answer.</summary>
    private sealed class PoolWorld
    {
        public readonly PlayerRig Rig = new PlayerRig().As(TFClass.Engineer);
        public readonly TFWeaponWorld Weapons = new();
        public readonly RecordingEngine Engine;
        public readonly ScriptedEntityRegistry Registry;
        public readonly Arsenal Arsenal;

        private readonly EntityHandle _weapon = new(0x0500u);
        private readonly EntityHandle _viewModel = new(0x0501u);

        public PoolWorld(bool withItem)
        {
            if (withItem)
                Weapons.Owners.ByName[PlayerRig.Index] = new()
                {
                    [Shotgun] = TFWidowmakerTests.Widowed(),
                };

            var created = new Queue<EntityHandle>([_weapon, _viewModel]);
            Engine = new RecordingEngine()
                .Returns("Entities.IsValid", true)
                .Returns("Entities.GetIndex", PlayerRig.Index)
                .Returns("Entities.GetClassName", EntityNames.Player)
                .Returns("Entities.GetOrigin", default(Vector))
                .Returns("Entities.GetAngles", default(Vector))
                .Returns("Players.IsAlive", true)
                .Returns("Entities.GetEyeAngles", default(Vector))
                .Returns("EntityRender.LookupSequence", -1)
                .Returns("EntityRender.LookupSequenceByActivity", -1)
                .Returns("Clock.Now", 10f)
                .ReturnsEachCall("Entities.Create", () => created.Dequeue());

            Registry = new ScriptedEntityRegistry(TestEngines.Server());
            Registry.RegisterAll(typeof(SsWeapon).Assembly);
            Registry.Services = TestServices.Services(out _);
            Registry.KeyValue(PlayerRig.Player, EntityNames.Player, "targetname", "fixture_player");
            Registry.Spawn(_weapon, EntityNames.SsWeapon);
            Registry.Spawn(_viewModel, EntityNames.PredictedViewModel);

            var log = new RecordingLog();
            Weapons.Owners.Classes[PlayerRig.Index] = TFClass.Engineer;
            Arsenal = new Arsenal(log, Engine, Engine, Engine, Engine, Engine, Engine, Engine, Engine,
                                  Registry, Engine, new DamageHitGroups(),
                                  weapons: Weapons.Catalog, ammo: new AmmoCatalog(() => [new TFAmmoTypes()], log),
                                  behaviours: Weapons.Behaviours,
                                  carryLimits: new AmmoCarry(new ListHooks<IAmmoCarryRule>(
                                      new TFAmmoCarryRule(Rig.Classes, Weapons.Owners)), log));
        }

        public SsWeapon Weapon => (SsWeapon)Registry.Get(_weapon)!;
    }

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Buildings;
using TF2.Server.Player;
using TF2.Server.Weapons;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Conditions;
using RevengePurse = TF2.Shared.Player.TFRevengeCrits;
using RevengeCritsService = TF2.Server.Player.TFRevengeCrits;
using TF2.Shared.Econ;

using TF2.Tests.Buildings;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The Frontier Justice's revenge window (<see cref="RevengeCritsService"/>): the
/// grant clamps at 35 and wears its boost while a revenge weapon is the
/// hands, the shot spends one and the last leaves no boost, a window granted
/// under another weapon lights at the Justice's own next shot, the sentry's
/// death pays its kills twice and its assists, and the count dies with its
/// holder.
/// </summary>
public sealed class TFRevengeCritsTests
{
    private static readonly EntityHandle Engineer = PlayerRig.Player;

    /// <summary>The enemy the sentry's death is laid on; the grant never reads it.</summary>
    private static readonly EntityHandle Killer = new(0x0001_0009u);

    private sealed class Rig
    {
        public readonly PlayerRig Player = new();
        public readonly FakeOwners Owners = new();
        public readonly AttributeHooks Hooks = new();
        public readonly RevengeCritsService Revenge;
        public readonly TFRevengeHook Hook;

        public Rig()
        {
            Player.As(TFClass.Engineer);
            Revenge = new RevengeCritsService(Player.Carriers, Owners, Hooks);
            Hook = new TFRevengeHook(Revenge);
        }

        public TFPlayerNetState EngineerState => Player.Carriers.Get(PlayerRig.Index)!;

        public bool Boosted => Player.Carriers.ConditionsOf(PlayerRig.Index)!.Has(TFCond.Critboosted);

        /// <summary>TheJustice in the hands, its item carrying the sentry-death pay.</summary>
        public void HoldsJustice()
        {
            Owners.Held[PlayerRig.Index] = RevengeCritsService.ShotgunScript;
            Owners.ByName[PlayerRig.Index] = new()
            {
                [RevengeCritsService.ShotgunScript] = PayingItem(),
            };
        }

        /// <summary>Any weapon whose item carries no revenge attribute: the plain pistol's.</summary>
        public void HoldsPlainGun() => Owners.Held[PlayerRig.Index] = "tf_weapon_pistol";

        /// <summary>The Justice's fired shot, as the behaviour's context carries it.</summary>
        private TFWeaponHookContext TheShot => new(default, WeaponFixtures.ParseExtra(RevengeCritsService.ShotgunScript),
                                                   PlayerRig.Index);

        /// <summary>The shot's spend, the hook's after-fire (tf_weapon_shotgun.cpp:143-145).</summary>
        public void Spend() { var shot = TheShot; Hook.AfterFire(in shot, default); }

        /// <summary>The shot's arm, the hook's before-fire; true, never a veto.</summary>
        public bool Arm() { var shot = TheShot; return Hook.BeforeFire(in shot, default); }
    }

    private static int _defindex = 4141;

    private static IAttributeProvider PayingItem()
    {
        var item = new AttributeContainer(null, AttributeProviderType.Generic);
        item.Set(AttributeValue.OfFloat(
            new AttributeDefinition(_defindex++, "attr " + _defindex, "sentry_killed_revenge",
                                    AttributeDescriptionFormat.Additive, AttributeEffectType.Neutral,
                                    false, false, AttributeValueType.Default, MergedKeys.Block("x")), 1f));
        return item;
    }

    [Fact]
    public void TheGrantWearsTheBoostAndClampsAtTheCap()
    {
        var rig = new Rig();
        rig.HoldsJustice();

        rig.Revenge.Grant(PlayerRig.Index, 40);                    // SetRevengeCrits(0 + 40), the clamp :14474

        Assert.Equal(RevengePurse.MaxCrits, rig.EngineerState.RevengeCrits);
        Assert.True(rig.Boosted);
    }

    [Fact]
    public void TheShotSpendsOneAndTheLastLeavesNoBoost()
    {
        var rig = new Rig();
        rig.HoldsJustice();
        rig.Revenge.Grant(PlayerRig.Index, 2);

        rig.Spend();                                               // PrimaryAttack's tail, tf_weapon_shotgun.cpp:143-145

        Assert.Equal(1, rig.EngineerState.RevengeCrits);
        Assert.True(rig.Boosted);

        rig.Spend();

        Assert.Equal(0, rig.EngineerState.RevengeCrits);
        Assert.False(rig.Boosted);                                 // the setter's else, :14483-14485
    }

    [Fact]
    public void AWindowGrantedUnderAnotherWeaponLightsAtTheJusticesOwnShot()
    {
        var rig = new Rig();
        rig.HoldsPlainGun();

        rig.Revenge.Grant(PlayerRig.Index, 3);                     // the wrench in the hands: no arm, :14477

        Assert.Equal(3, rig.EngineerState.RevengeCrits);
        Assert.False(rig.Boosted);

        rig.HoldsJustice();
        Assert.True(rig.Arm());                                    // the deploy arm's work, asked where the shot is

        Assert.True(rig.Boosted);
        Assert.Equal(3, rig.EngineerState.RevengeCrits);           // the arm moves no count
    }

    [Fact]
    public void APlainGunKeepsTheBoostUnlit()
    {
        var rig = new Rig();
        rig.HoldsPlainGun();

        rig.Revenge.Grant(PlayerRig.Index, 2);

        Assert.Equal(2, rig.EngineerState.RevengeCrits);
        Assert.False(rig.Boosted);
    }

    [Fact]
    public void TheCountDiesWithItsHolder()
    {
        var rig = new Rig();
        rig.HoldsJustice();
        rig.Revenge.Grant(PlayerRig.Index, 2);

        rig.Revenge.OnPlayerDied(new(PlayerRig.Player, PlayerRig.Index, Killer, default, EntityHandle.None));

        Assert.Equal(0, rig.EngineerState.RevengeCrits);
    }

    /// <summary>
    /// The real press: the sentry's death (the shell's kill call at zero
    /// health, <see cref="BuildingsRig.Hit"/>) tells the owner's loaded
    /// Justice, whose pile pays the kills twice and the assists
    /// (tf_obj_sentrygun.cpp:2055-2064).
    /// </summary>
    [Fact]
    public void TheSentrysDeathPaysTwiceItsKillsAndItsAssists()
    {
        var rig = new BuildingsRig();
        var owners = new FakeOwners();
        var revenge = new RevengeCritsService(rig.Carriers, owners, new AttributeHooks());
        rig.Services.AddInstance<ITFWeaponOwners>(owners);
        rig.Services.AddInstance(revenge);

        var builder = rig.AddBuilder(TFTeam.Red);
        ACarrier(rig, builder);
        owners.Held[builder.EntryIndex] = RevengeCritsService.ShotgunScript;
        owners.ByName[builder.EntryIndex] = new() { [RevengeCritsService.ShotgunScript] = PayingItem() };

        var sentry = rig.Spawn<TFSentryGun>(TFSentryGun.MapClass);
        sentry.StartPlacement(builder);
        rig.Clock.Now = 100;
        sentry.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        sentry.OnThink();
        sentry.IncrementKills();
        sentry.IncrementKills();
        sentry.IncrementAssists();

        BuildingsRig.Hit(sentry, Killer, 999f, DamageType.Generic);

        var state = rig.Carriers.Get(builder.EntryIndex);
        Assert.Equal(5, state!.RevengeCrits);                      // 2 * 2 + 1
        Assert.True(rig.Carriers.ConditionsOf(builder.EntryIndex)!.Has(TFCond.Critboosted));
    }

    /// <summary>
    /// The carrier attach, as <see cref="TFBuildingPickupTests"/> lifts it: the
    /// make answers a fixed handle and the spawn binds through the registry.
    /// </summary>
    private static void ACarrier(BuildingsRig rig, EntityHandle engineer)
    {
        var carrierHandle = new EntityHandle(0x0002_0007u);
        rig.Engine.Returns("Entities.Create", carrierHandle)
                  .ReturnsEachCall("Entities.GetIndex", () => engineer.EntryIndex);
        rig.Engine.WhenCalled("Entities.Spawn",
                              c => rig.Registry.Spawn(c.ArgAs<EntityHandle>(0), TFEntityNames.PlayerData));
        new TFPlayerCarrier(rig.Engine, rig.Registry, rig.Carriers, rig.Log).Attach(engineer);
    }
}

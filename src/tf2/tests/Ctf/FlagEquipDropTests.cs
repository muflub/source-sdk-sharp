using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Tests;

using TF2.Server.Econ;
using TF2.Server.Entities.Ctf;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Player;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Ctf;

/// <summary>
/// The reactive arm of <c>CTFPlayer::Weapon_Equip</c> (tf_player.cpp:20588-
/// 20599): after the kit's gives, a carrier whose held weapon now forbids
/// carrying lets go of the flag, and one carrying on a weapon that does not
/// keeps it.
/// </summary>
public sealed class FlagEquipDropTests
{
    /// <summary>A blue carrier holding the red flag, as the other CTF rigs stand one.</summary>
    private static (CtfRig Rig, ItemTeamFlag Red, EntityHandle Carrier) Carried()
    {
        var rig = new CtfRig();
        var red = rig.Flag(TFTeam.Red);
        var carrier = rig.Players.Add(3, TFTeam.Blue);
        rig.Players[carrier].Center = new Vector(300f, 400f, 50f);
        rig.Registry.Touch(red.Handle, carrier);
        Assert.True(red.IsStolen);
        return (rig, red, carrier);
    }

    private static FlagEquipDrop Hook(CtfRig rig) => new(rig.Rules, rig.Players);

    [Fact]
    public void The_carrier_who_equips_the_rocket_jumper_drops_the_flag()
    {
        // The jumper's cannot_pick_up_intelligence rides the player's manager:
        // the manager's answer is the gate's refusal (EngineCtfPlayersTests).
        var (rig, red, carrier) = Carried();
        rig.Players[carrier].AllowedToPickUp = false;

        Hook(rig).OnEquipped(carrier);

        Assert.True(red.IsDropped);
        Assert.Null(rig.Rules.CarriedBy(carrier));
        Assert.Contains(rig.Events.Fired, e => e.StartsWith("flag Dropped player=3 team=Red", StringComparison.Ordinal));
    }

    [Fact]
    public void The_carrier_equipping_a_plain_launcher_keeps_the_flag()
    {
        // The same hook on the same carrier with no refusing attribute on the
        // manager: the gate allows, and the flag stands stolen.
        var (rig, red, carrier) = Carried();
        int events = rig.Events.Fired.Count;

        Hook(rig).OnEquipped(carrier);

        Assert.True(red.IsStolen);
        Assert.NotNull(rig.Rules.CarriedBy(carrier));
        Assert.Equal(events, rig.Events.Fired.Count);
    }

    [Fact]
    public void A_refusal_on_a_player_holding_nothing_touches_no_flag()
    {
        var (rig, red, _) = Carried();
        var other = rig.Players.Add(4, TFTeam.Blue);
        rig.Players[other].AllowedToPickUp = false;
        int events = rig.Events.Fired.Count;

        Hook(rig).OnEquipped(other);

        Assert.True(red.IsStolen);
        Assert.Equal(events, rig.Events.Fired.Count);
    }

    /// <summary>
    /// The kit's give is an equip pass: <see cref="TFPlayerResupply.Regenerate"/>
    /// → GiveKit ends it by asking the equip chain once, with the player who
    /// equipped — the locker's regen and the spawn's fresh kit walk the same
    /// pass. A second regen asks again: the ask rides every pass, not only the
    /// first.
    /// </summary>
    [Fact]
    public void GiveKit_asks_the_equip_hook_once_per_pass()
    {
        var asked = new Recorder();
        var rig = LockerRig(asked);

        rig.Resupply.Regenerate(PlayerRig.Player);

        Assert.Single(asked.Asked);
        Assert.Equal(PlayerRig.Player, asked.Asked[0]);
        Assert.True(rig.Rig.Engine.CountOf("Players.GiveItem") > 0, "the kit actually gave: the pass is not empty");

        rig.Resupply.Regenerate(PlayerRig.Player);

        Assert.Equal(2, asked.Asked.Count);
    }

    /// <summary>
    /// The locker visit, end to end: the kit's gives end the equip pass by
    /// asking the chain, and the chain's real hook answers them — the held
    /// jumper's <c>cannot_pick_up_intelligence</c> gate (the manager's refusal,
    /// <see cref="FakeCtfPlayers.Player.AllowedToPickUp"/> false) lets the
    /// carrier go of the flag the kit's equip just reached.
    /// </summary>
    [Fact]
    public void The_locker_who_equips_the_rocket_jumper_drops_the_carried_flag()
    {
        var ctf = new CtfRig();
        var red = ctf.Flag(TFTeam.Red);
        var carrier = ctf.Players.Add(3, TFTeam.Blue); // the same handle PlayerRig.Player carries
        Assert.Equal(PlayerRig.Player, carrier);
        ctf.Registry.Touch(red.Handle, carrier);
        Assert.True(red.IsStolen);
        ctf.Players[carrier].AllowedToPickUp = false; // the jumper's cannot_pick_up_intelligence

        var (_, resupply) = LockerRig(new FlagEquipDrop(ctf.Rules, ctf.Players));
        resupply.Regenerate(PlayerRig.Player);

        Assert.True(red.IsDropped);
        Assert.Null(ctf.Rules.CarriedBy(carrier));
    }

    /// <summary>
    /// The same locker pass with the plain launcher's gate (the manager allows):
    /// the equip pass asks, the hook finds no refusal, and the flag stays stolen.
    /// </summary>
    [Fact]
    public void The_locker_with_a_plain_launcher_keeps_the_flag()
    {
        var ctf = new CtfRig();
        var red = ctf.Flag(TFTeam.Red);
        var carrier = ctf.Players.Add(3, TFTeam.Blue);
        Assert.Equal(PlayerRig.Player, carrier);
        ctf.Registry.Touch(red.Handle, carrier);
        Assert.True(red.IsStolen);
        int events = ctf.Events.Fired.Count;

        var (_, resupply) = LockerRig(new FlagEquipDrop(ctf.Rules, ctf.Players));
        resupply.Regenerate(PlayerRig.Player);

        Assert.True(red.IsStolen);
        Assert.NotNull(ctf.Rules.CarriedBy(carrier));
        Assert.Equal(events, ctf.Events.Fired.Count);
    }

    /// <summary>The locker's world: a soldier's resupply whose equip chain records every ask.</summary>
    private static (PlayerRig Rig, TFPlayerResupply Resupply) LockerRig(ITFWeaponEquipHook hook)
    {
        var rig = new PlayerRig().As(TFClass.Soldier);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions,
                                            rig.Loadout, rig.Engine, rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog, arsenal: LockerlessArsenal(rig),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine,
                                            equipHooks: new ListHooks<ITFWeaponEquipHook>(hook));
        return (rig, resupply);
    }

    /// <summary>An arsenal an empty rig walks: the kit's gives land on the engine's answer, nowhere deeper.</summary>
    private static Arsenal LockerlessArsenal(PlayerRig rig) =>
        new(rig.Log, rig.Engine, rig.Engine, rig.Engine, rig.Engine, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
            new ScriptedEntityRegistry(TestEngines.Server()), rig.Engine, new DamageHitGroups());

    /// <summary>The equip chain of one hook, in the shape every TF test's <c>ListHooks</c> has.</summary>
    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>The equip hook, as the facts' record: every ask's player.</summary>
    private sealed class Recorder : ITFWeaponEquipHook
    {
        public readonly List<EntityHandle> Asked = [];

        public void OnEquipped(EntityHandle player) => Asked.Add(player);
    }
}

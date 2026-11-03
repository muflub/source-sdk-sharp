using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Weapons;

/// <summary>
/// <c>cl_autoreload</c> (stock <c>CTFWeaponBase::ItemPostFrame</c>'s pressed
/// reload, tf_weapon.cpp... rather tf_weaponbase.cpp:2451-2457): the idle
/// top-up presses while the trigger is elsewhere and the clip can take
/// rounds, a whole-clip load reloads by its own arm, and the server reads the
/// wish as the client told it.
/// </summary>
public sealed class TFAutoReloadTests
{
    private const int AttackButton = (int)InputButtons.Attack;

    private sealed class Wish(int player, bool yes) : ITFAutoReload
    {
        public bool Wants(int ownerIndex) => yes && ownerIndex == player;
    }

    private sealed class WholeLoad : ITFWeaponHook
    {
        public int HookOrder => 0;

        public bool FiresFullClip(in TFWeaponHookContext weapon) => true;
    }

    private sealed class Said(string value) : IConnectedClients
    {
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public float? AvgLatency(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => value;
        public int UserId(int entIndex) => -1;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    private static TFWeaponWorld World(bool wish = true, ITFWeaponHook? hook = null) =>
        hook is null
            ? new TFWeaponWorld(null, null, new Wish(TFWeaponWorld.Owner, wish))
            : new TFWeaponWorld(null, null, new Wish(TFWeaponWorld.Owner, wish), hook);

    private static IWeaponBehaviour Behaviour(TFWeaponWorld world, string weapon) => world.Behaviour(weapon);

    private static WeaponAttackContext Attack(TFWeaponWorld world, string weapon, int clip, int reserve)
    {
        var (w, vm) = world.Armed(weapon, clip, reserve);
        return world.Attack(w, vm, now: 10f);
    }

    [Fact]
    public void The_wish_presses_reload_while_the_player_idles()
    {
        var on = World();
        var (w, vm) = on.Armed("tf_weapon_pistol", 5, 32);
        on.Run(w, vm, 0, now: 10f);
        Assert.True(w.InReload);

        var off = World(wish: false);
        (w, vm) = off.Armed("tf_weapon_pistol", 5, 32);
        off.Run(w, vm, 0, now: 10f);
        Assert.False(w.InReload);
    }

    [Fact]
    public void A_held_trigger_waits()
    {
        var world = World();
        var attack = Attack(world, "tf_weapon_scattergun", 3, 32);

        Assert.True(Behaviour(world, "tf_weapon_scattergun").AutoReload(in attack, 0));
        Assert.False(Behaviour(world, "tf_weapon_scattergun").AutoReload(in attack, AttackButton));
    }

    [Fact]
    public void A_full_clip_or_a_dry_bag_asks_nothing()
    {
        var world = World();
        var behaviour = Behaviour(world, "tf_weapon_scattergun");

        var full = Attack(world, "tf_weapon_scattergun", 6, 32);
        Assert.False(behaviour.AutoReload(in full, 0));

        var dry = Attack(world, "tf_weapon_scattergun", 3, 0);
        Assert.False(behaviour.AutoReload(in dry, 0));
    }

    [Fact]
    public void A_whole_clip_load_reloads_by_its_own_arm()
    {
        var world = World(hook: new WholeLoad());
        var attack = Attack(world, "tf_weapon_scattergun", 3, 32);

        Assert.False(Behaviour(world, "tf_weapon_scattergun").AutoReload(in attack, 0));
    }

    [Fact]
    public void After_a_shot_the_reload_waits_for_the_next_attack_time()
    {
        var world = World();
        var (w, vm) = world.Armed("tf_weapon_scattergun", 3, 32);

        // One shot with the trigger pressed at now=10: the clip drops one round.
        world.Run(w, vm, AttackButton, now: 10f);
        Assert.Equal(2, w.Clip);
        Assert.Equal(10f + 0.625f, w.NextPrimaryAttack, 4);   // the script's TimeFireDelay

        // Idle while the shot still cools: the wish must not start the reload.
        world.Run(w, vm, 0, now: 10.55f);
        Assert.False(w.InReload);

        // Once the next-attack time is reached, the reload starts by itself.
        world.Run(w, vm, 0, now: 10.63f);
        Assert.True(w.InReload);
    }

    [Fact]
    public void The_server_reads_the_wish_as_the_client_told_it()
    {
        Assert.True(new TFServerAutoReload(new Said("1")).Wants(TFWeaponWorld.Owner));
        Assert.True(new TFServerAutoReload(new Said("2")).Wants(TFWeaponWorld.Owner));
        Assert.False(new TFServerAutoReload(new Said("0")).Wants(TFWeaponWorld.Owner));
        Assert.False(new TFServerAutoReload(new Said("")).Wants(TFWeaponWorld.Owner));
        Assert.False(new TFServerAutoReload(new Said("1")).Wants(0));
        Assert.False(new TFServerAutoReload(null).Wants(TFWeaponWorld.Owner));
    }
}

/*
 * What death does to the Arsenal — the axis the live bug lived on.
 *
 * The bug, as observed: death drops the active weapon as a pickup AT the
 * corpse, and the corpse's own touch took it straight back within the same
 * frame ("deployed weapon_smg1" printed under "ss_player died"). The dead
 * player kept an active weapon, the HUD kept painting it, and the respawn
 * inherited it on top of the loadout. A roaming dead observer also hoovered
 * a health kit at -150 health.
 *
 * The rule these tests hold still: A DEAD PLAYER COLLECTS NOTHING — refused
 * at the Arsenal (the inventory authority), at the map-item touch, and at
 * the pickup service. Spawn clears deadflag before the loadout hooks run
 * (shell_standalone_player.cpp), so a fresh spawn is alive by the time
 * GiveItem is called and the loadout is never refused.
 */

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class WeaponDeathTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private static (Arsenal arsenal, RecordingEngine engine) DeadWorld()
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.SsPlayer)
            .Returns("Players.IsAlive", false);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);

        // THE PLAYER IS SPAWNED INTO THE REGISTRY, and this fixture used to
        // stub only the classname.
        //
        // plan-audit D19 measured Arsenal.IsStandalonePlayer asking the shell
        // for a classname 6.000 times a server frame and made it a registry
        // lookup. On a live server the two are the same fact -- SsPlayer IS the
        // [ScriptedEntity] bound to "ss_player" -- but a fixture that answers
        // the classname and holds no instance is a player the ARSENAL can see
        // and the REGISTRY cannot, which is a state no running game is ever in.
        // The stand-in had drifted from the thing it stands in for; this is the
        // agreement asserted rather than assumed.
        //
        // KeyValue and not Spawn, because Spawn is not what makes the instance
        // exist -- the registry's own comment says so: "Created on demand
        // rather than at spawn because of the order Source works in: an entity
        // is constructed, then handed every keyvalue in the map, and only then
        // spawned." A fixture stopping after the keyvalues is a state every
        // running server passes through; running OnSpawn here would put an
        // EntityRender.SetVisible into engine.Calls, which two facts in this
        // file read.
        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");

        var arsenal = new Arsenal(new RecordingLog(), engine, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups());

        return (arsenal, engine);
    }

    [Fact]
    public void ADeadPlayersHandsTakeNoWeapon()
    {
        var (arsenal, _) = DeadWorld();

        Assert.True(!arsenal.GiveWeapon(Player, "weapon_smg1"),
            "a dead player is given nothing — the corpse must not take back its own dropped weapon");
        Assert.True(arsenal.Holdings(Player).Count == 0,
            "and nothing landed in the inventory behind the refusal");
    }

    [Fact]
    public void ADeadPlayersPoolsTakeNoAmmo()
    {
        var (arsenal, _) = DeadWorld();

        Assert.True(arsenal.GiveAmmo(Player, 45, "SMG1") == 0,
            "a dead player's ammo pools take nothing");
        Assert.True(arsenal.Pools(Player).Count == 0,
            "and no pool was created behind the refusal");
    }

    //-------------------------------------------------------------------------
    // Death's other half: nothing SHOWN in the hands. The carried pair used to
    // vanish on death by accident (PVS dropped them); the dormancy fix marked
    // the viewmodel transmit-always — right for a live player — which is
    // exactly what kept a dead player's viewmodel on screen. So a strip must
    // say it out loud: hands emptied, viewmodel disowned, both hidden.
    //-------------------------------------------------------------------------

    private static (Arsenal arsenal, RecordingEngine engine,
                    WeaponSwitchTests.SpawningEntities world, ScriptedEntityRegistry registry)
        LiveWorld()
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Entities.GetClassName", EntityNames.SsPlayer)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", 10f);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsWeapon).Assembly);
        registry.Services = Services(out _);

        // THE PLAYER IS SPAWNED INTO THE REGISTRY, and this fixture used to
        // stub only the classname.
        //
        // plan-audit D19 measured Arsenal.IsStandalonePlayer asking the shell
        // for a classname 6.000 times a server frame and made it a registry
        // lookup. On a live server the two are the same fact -- SsPlayer IS the
        // [ScriptedEntity] bound to "ss_player" -- but a fixture that answers
        // the classname and holds no instance is a player the ARSENAL can see
        // and the REGISTRY cannot, which is a state no running game is ever in.
        // The stand-in had drifted from the thing it stands in for; this is the
        // agreement asserted rather than assumed.
        //
        // KeyValue and not Spawn, because Spawn is not what makes the instance
        // exist -- the registry's own comment says so: "Created on demand
        // rather than at spawn because of the order Source works in: an entity
        // is constructed, then handed every keyvalue in the map, and only then
        // spawned." A fixture stopping after the keyvalues is a state every
        // running server passes through; running OnSpawn here would put an
        // EntityRender.SetVisible into engine.Calls, which two facts in this
        // file read.
        registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");

        var world = new WeaponSwitchTests.SpawningEntities(engine, registry);

        var arsenal = new Arsenal(new RecordingLog(), world, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine, new DamageHitGroups());

        return (arsenal, engine, world, registry);
    }

    private static bool Hid(RecordingEngine engine, EntityHandle entity)
        => engine.Calls.Any(c => c.Member == "EntityRender.SetVisible"
                              && c.ArgAs<EntityHandle>(0) == entity
                              && !c.ArgAs<bool>(1));

    [Fact]
    public void StrippingEmptiesAndHidesTheCarriedPair()
    {
        var (arsenal, engine, world, registry) = LiveWorld();

        Assert.True(arsenal.GiveWeapon(Player, "weapon_smg1"), "a living player is armed");

        var weaponHandle = world.WeaponHandles[^1];
        var viewModelHandle = world.ViewModelHandles[^1];
        var weapon = (SsWeapon)registry.Get(weaponHandle)!;
        var viewModel = (SsViewModel)registry.Get(viewModelHandle)!;

        Assert.True(weapon.State.WeaponTypeId >= 0, "the deploy armed the weapon entity");

        arsenal.Strip(Player);

        Assert.True(arsenal.Holdings(Player).Count == 0, "stripping empties the inventory");
        Assert.True(weapon.State.WeaponTypeId == -1, "and the hands");
        Assert.True(viewModel.State.OwnerIndex == 0,
            "the viewmodel is DISOWNED — owner 0 is the signal the client's presenter " +
            "reads to take it out of the viewmodel pass, and it transmits always so " +
            "nothing else will hide it");
        Assert.True(Hid(engine, weaponHandle), "the carried weapon is hidden where it stands");
        Assert.True(Hid(engine, viewModelHandle), "and so is the viewmodel");
    }

    [Fact]
    public void ARedeployAfterAStripDressesAndOwnsThePairAgain()
    {
        var (arsenal, engine, world, registry) = LiveWorld();

        Assert.True(arsenal.GiveWeapon(Player, "weapon_smg1"), "armed once");
        arsenal.Strip(Player);

        // The respawn moment: alive again (the double already says so), and
        // the loadout gives. The pair is re-dressed and re-owned.
        Assert.True(arsenal.GiveWeapon(Player, "weapon_pistol"), "re-armed after the strip");

        var weaponHandle = world.WeaponHandles[^1];
        var viewModelHandle = world.ViewModelHandles[^1];
        var weapon = (SsWeapon)registry.Get(weaponHandle)!;
        var viewModel = (SsViewModel)registry.Get(viewModelHandle)!;

        Assert.True(weapon.State.WeaponTypeId == WeaponCatalog.Stock().IndexOf("weapon_pistol"),
            "the redeploy armed the weapon entity with the new loadout");
        Assert.True(viewModel.State.OwnerIndex != 0, "and the viewmodel is owned again");

        bool dressed = engine.Calls.Any(c => c.Member == "EntityRender.SetModel"
                                          && c.ArgAs<EntityHandle>(0) == viewModelHandle
                                          && c.ArgAs<string>(1).Contains("v_pistol"));
        Assert.True(dressed, "the viewmodel wears the new weapon's v_ model");
    }

    // The LAST visibility call an entity received, or null when it got none.
    private static bool? LastVisibility(RecordingEngine engine, EntityHandle entity)
    {
        var calls = engine.Calls.Where(c => c.Member == "EntityRender.SetVisible"
                                         && c.ArgAs<EntityHandle>(0) == entity).ToList();
        return calls.Count == 0 ? null : calls[^1].ArgAs<bool>(1);
    }

    [Fact]
    public void ARedeployAfterAStripShowsThePairAgain()
    {
        // SetVisible(false) sets EF_NODRAW and SetModel does not clear it, so
        // dressing alone left a respawned player with no viewmodel and no
        // world weapon. The deploy has to say "visible" itself.
        var (arsenal, engine, world, _) = LiveWorld();

        Assert.True(arsenal.GiveWeapon(Player, "weapon_smg1"), "armed once");
        arsenal.Strip(Player);

        Assert.True(LastVisibility(engine, world.ViewModelHandles[^1]) == false,
            "the strip hid the viewmodel (the state the redeploy must undo)");

        Assert.True(arsenal.GiveWeapon(Player, "weapon_pistol"), "re-armed after the strip");

        // Whichever pair the redeploy dressed -- live it is the same, hidden
        // pair (EnsureEntities reuses a valid handle); this fixture's handles
        // do not answer IsValid, so it makes a fresh one. Either way the last
        // word on its visibility has to be "shown".
        Assert.True(LastVisibility(engine, world.WeaponHandles[^1]) == true,
            "the redeploy shows the carried weapon");
        Assert.True(LastVisibility(engine, world.ViewModelHandles[^1]) == true,
            "and the viewmodel");
    }

    [Fact]
    public void ADeadPlayerCollectsNoHealthPickup()
    {
        var engine = Mock().Ignores("Entities.IsValid").Returns("Entities.GetIndex", -1)
            .Returns("Players.IsAlive", false);

        var pickups = new Pickups(new RecordingLog(), engine, engine, engine, engine);

        var pickup = new EntityHandle(0x0009_0001u);
        pickups.TrackForTest(pickup, Pickups.Health);

        Assert.True(!pickups.OnTouched(pickup, Player, now: 1f),
            "a dead observer drifting over a health kit collects nothing");
        Assert.True(pickups.LiveCount == 1,
            "and the kit stays in the world for whoever spawns there");
    }
    /// <summary>
    /// A DROPPED WEAPON LANDS ON THE FLOOR rather than hanging in the air where
    /// its owner died.
    ///
    /// <para>
    /// The bug this pins was visible in a game for as long as dropping existed:
    /// a crowbar standing in mid-air thirty-two units above a corpse. The drop
    /// point was the player's origin plus a lift and nothing else, and a
    /// WeaponPickup is an Item — no physics object, no falling movetype — so
    /// wherever it is put is where it stays forever.
    /// </para>
    /// </summary>
    [Fact]
    public void ADroppedWeaponIsPutOnTheFloorAndNotLeftInTheAir()
    {
        var (arsenal, engine, world, _) = LiveWorld();

        // The player stands at z=64 (LiveWorld's origin) and the floor is at
        // z=8 — a drop from a raised walkway, which is the case that tells a
        // trace apart from a constant offset.
        var floor = new Vector(0f, 0f, 8f);

        engine.Returns("WorldQuery.TraceLine",
                       new TraceResult(true, 0.5f, floor, new Vector(0f, 0f, 1f),
                                       EntityHandle.None));

        Assert.True(arsenal.GiveWeapon(Player, "weapon_smg1"), "a living player is armed");

        arsenal.DropActive(Player, Vector.Zero);

        var placed = engine.Calls.Last(c => c.Member == "Entities.SetOrigin");

        Assert.True(placed.ArgAs<Vector>(1).Z < 64f,
            $"the weapon is put BELOW the player who dropped it ({placed.ArgAs<Vector>(1)}) — "
          + "above them is where it used to hang, and an Item has no way to fall");

        Assert.True(Math.Abs(placed.ArgAs<Vector>(1).Z - floor.Z) < 8f,
            $"and lands on the floor the trace found ({placed.ArgAs<Vector>(1).Z}, floor at "
          + $"{floor.Z}) — a small clearance so its bounds do not start inside the ground");

        var trace = engine.Calls.Last(c => c.Member == "WorldQuery.TraceLine");

        Assert.True(trace.ArgAs<Vector>(0).Z > trace.ArgAs<Vector>(1).Z,
            "the trace looks DOWNWARD from above the player, so a weapon dropped on a crate "
          + "lands on the crate rather than inside it");
    }

    /// <summary>
    /// WITH NOTHING UNDERNEATH, it stays where it was thrown. There is no floor
    /// to put it on, and inventing one would drop it through the world.
    /// </summary>
    [Fact]
    public void AWeaponDroppedOverAPitStaysWhereItWasThrown()
    {
        var (arsenal, engine, world, _) = LiveWorld();

        engine.Returns("WorldQuery.TraceLine",
                       new TraceResult(false, 1f, Vector.Zero, Vector.Zero, EntityHandle.None));

        Assert.True(arsenal.GiveWeapon(Player, "weapon_smg1"), "a living player is armed");

        arsenal.DropActive(Player, Vector.Zero);

        var placed = engine.Calls.Last(c => c.Member == "Entities.SetOrigin");

        Assert.True(placed.ArgAs<Vector>(1).Z > 64f,
            $"a miss leaves it above the player ({placed.ArgAs<Vector>(1)}) rather than at the "
          + "trace's end, which for a miss is the far end of a 4096-unit line straight down");
    }
}

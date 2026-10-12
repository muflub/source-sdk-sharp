/*
 * The Arsenal across a hot reload and a departure.
 *
 * The live bug (daily gate, 2026-10-05): the Arsenal kept every player's carry
 * -- owned weapons and clips, ammo pools, the active and last weapon, and the
 * handles of the carried ss_weapon/ss_viewmodel pair -- in a dictionary no
 * reload carried. After cs_sv_reload every player owned nothing in managed
 * code; touching a weapon pickup they already owned then "took" it as new and
 * built a second ss_weapon and ss_viewmodel beside the first two, which the
 * hot-reload step's census read as 2 entities gained. And a departing player's
 * pair stayed in the world with its owner gone, where stock's
 * CMultiplayRules::ClientDisconnected removes all their items and their view
 * models.
 */

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class ArsenalReloadTests
{
    private static readonly EntityHandle Player = new(0x0001_0001u);

    private sealed record World(Arsenal Arsenal, RecordingEngine Engine,
                                WeaponSwitchTests.SpawningEntities Entities, ScriptedEntityRegistry Registry);

    private static World Live(RecordingEngine? sharedEngine = null, ScriptedEntityRegistry? sharedRegistry = null,
                              WeaponSwitchTests.SpawningEntities? sharedEntities = null)
    {
        var engine = sharedEngine ?? Mock().Returns("Entities.IsValid", true).Returns("Entities.GetIndex", 1)
            .Returns("Entities.GetClassName", EntityNames.SsPlayer)
            .Returns("Entities.GetOrigin", new Vector(0, 0, 64))
            .Returns("Players.IsAlive", true)
            .Returns("Clock.Now", 10f).Ignores("EntityRender.LookupSequenceByActivity");

        var registry = sharedRegistry;
        if (registry is null)
        {
            registry = new ScriptedEntityRegistry();
            registry.RegisterAll(typeof(SsWeapon).Assembly);
            registry.Services = Services(out _);
            registry.KeyValue(Player, EntityNames.SsPlayer, "targetname", "fixture_player");
        }

        var entities = sharedEntities ?? new WeaponSwitchTests.SpawningEntities(engine, registry);
        var arsenal = new Arsenal(new RecordingLog(), entities, engine, engine, engine, engine, engine,
                                  engine, engine, registry, engine);
        return new World(arsenal, engine, entities, registry);
    }

    private static ISaveCustom Saver(Arsenal arsenal) => Assert.IsAssignableFrom<ISaveCustom>(arsenal);

    /// <summary>The unload and the load: the old build's OnSave, a fresh Arsenal's OnRestore.</summary>
    private static World Reloaded(World before)
    {
        var record = new ReloadRecord("SourceSharp.Server.Arsenal");
        Saver(before.Arsenal).OnSave(new SaveRecordAccess(record, record.Owner, new RecordingLog()));

        var after = Live(before.Engine, before.Registry, before.Entities);
        Saver(after.Arsenal).OnRestore(
            new SaveRecordAccess(record, record.Owner, new RecordingLog(),
                                 new SavedHandles(before.Engine, before.Entities)));
        return after;
    }

    private static World Armed()
    {
        var w = Live();
        Assert.True(w.Arsenal.GiveWeapon(Player, "weapon_crowbar"), "armed with a crowbar");
        Assert.True(w.Arsenal.GiveWeapon(Player, "weapon_smg1"), "and an smg");
        Assert.True(w.Arsenal.GiveWeapon(Player, "weapon_pistol"), "and a pistol");
        Assert.True(w.Arsenal.GiveAmmo(Player, 45, "SMG1") > 0, "with smg ammunition");
        return w;
    }

    [Fact]
    public void ACarryRoundTripsUnchangedThroughSaveAndRestore()
    {
        var before = Armed();
        Assert.True(before.Arsenal.Switch(Player, "weapon_smg1") == SwitchOutcome.Switched, "the smg in the hands");
        var holdings = before.Arsenal.Holdings(Player).ToArray();
        var pools = before.Arsenal.Pools(Player).OrderBy(p => p.Key).ToArray();
        var pair = before.Arsenal.CarriedPair(Player);

        var after = Reloaded(before);

        Assert.True(after.Arsenal.Holdings(Player).SequenceEqual(holdings),
            $"the owned weapons, their clips and the active one come back: {string.Join(", ", after.Arsenal.Holdings(Player))} " +
            $"(was {string.Join(", ", holdings)})");
        Assert.True(after.Arsenal.Pools(Player).OrderBy(p => p.Key).SequenceEqual(pools), "and the ammo pools");
        Assert.True(after.Arsenal.CarriedPair(Player) == pair && pair.Weapon != EntityHandle.None,
            $"and the carried ss_weapon/ss_viewmodel handles ({after.Arsenal.CarriedPair(Player)}, was {pair})");
        Assert.True(after.Arsenal.SwitchLast(Player) == SwitchOutcome.Switched,
            "and the last weapon, so lastinv still has somewhere to go");
    }

    [Fact]
    public void AfterARestoreTouchingAnOwnedWeaponBuildsNoNewWeaponEntity()
    {
        var before = Armed();
        int weapons = before.Entities.WeaponHandles.Count, viewModels = before.Entities.ViewModelHandles.Count;

        var after = Reloaded(before);
        after.Arsenal.GiveWeapon(Player, "weapon_crowbar");

        Assert.True(after.Entities.WeaponHandles.Count == weapons && after.Entities.ViewModelHandles.Count == viewModels,
            $"a crowbar the player already owns is a duplicate after the reload too: {after.Entities.WeaponHandles.Count - weapons} " +
            $"new ss_weapon(s), {after.Entities.ViewModelHandles.Count - viewModels} new ss_viewmodel(s)");
    }

    [Fact]
    public void ADepartingPlayersCarriedPairIsRemoved()
    {
        var w = Armed();
        var pair = w.Arsenal.CarriedPair(Player);

        Assert.IsAssignableFrom<IClientLifecycle>(w.Arsenal).OnClientDisconnected(Player.EntryIndex);

        bool Removed(EntityHandle h) => w.Engine.Calls.Any(c => c.Member == "Entities.Remove" && c.ArgAs<EntityHandle>(0) == h);
        Assert.True(Removed(pair.Weapon) && Removed(pair.ViewModel),
            "stock's CMultiplayRules::ClientDisconnected: RemoveAllItems and DestroyViewModels -- the pair goes");
        Assert.True(w.Arsenal.Holdings(Player).Count == 0, "and the carry with it");
    }
}

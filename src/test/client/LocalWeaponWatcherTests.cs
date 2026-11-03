using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ILocalWeaponWatcher"/>: the arsenal adopts the local player's
/// weapon pair every frame, and a side service hears the moments the local
/// player's weapon entity is (re)adopted — the client's half of stock's
/// CBaseCombatWeapon constructor (basecombatweapon_shared.cpp:103-106), which
/// refills a weapon's crit bucket as the respawn's kit hands out the entity.
/// </summary>
public sealed class LocalWeaponWatcherTests
{
    private const int LocalPlayer = 1;
    private const int WeaponEntry = 0x41;

    private sealed class Pair : IClientStandalones
    {
        public readonly ClientEntityDirectory Directory = new();

        public IReadOnlyList<ClientStandaloneEntity> Live()
            => throw new InvalidOperationException("the arsenal finds its pair by class");

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);

        public IEnumerable<ClientStandaloneEntity> OfClass(string? className)
            => Directory.OfClass(className);

        public IEnumerable<ClientStandaloneEntity> OfName(string? name) => Directory.OfName(name);
    }

    private sealed class Watcher : ILocalWeaponWatcher
    {
        public readonly List<int> Spawned = [];

        public void LocalWeaponSpawned(int weaponIndex) => Spawned.Add(weaponIndex);
    }

    private static ClientSsWeapon LocalWeapon(RecordingEngine engine)
    {
        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0000u | WeaponEntry) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = LocalPlayer;
        weapon.State.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);
        weapon.State.Clip = 12;
        return weapon;
    }

    private static ClientSsViewModel LocalViewModel(RecordingEngine engine)
    {
        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x0100_0000u | 0x42) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = LocalPlayer;
        return viewModel;
    }

    [Fact]
    public void The_arsenal_tells_a_watcher_when_it_adopts_the_local_players_weapon()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        engine.Returns("Clock.Now", 10f);
        engine.Returns("EntityRender.LookupSequenceByActivity", 0);

        var pair = new Pair();
        var weapon = LocalWeapon(engine);
        pair.Directory.Bind(weapon.Handle, -1, weapon);
        var viewModel = LocalViewModel(engine);
        pair.Directory.Bind(viewModel.Handle, -1, viewModel);

        var watcher = new Watcher();
        var arsenal = new ClientArsenal(new RecordingLog(), pair, engine, engine, engine,
                                        NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()),
                                        localWeapons: watcher);

        arsenal.Frame();

        Assert.Equal(new[] { WeaponEntry }, watcher.Spawned);   // the first frame reports once

        arsenal.Frame();
        arsenal.Frame();
        Assert.Single(watcher.Spawned);                          // the same entity is not a fresh spawn

        // The respawn: the kit's remove, then a fresh entity at the recycled
        // index. The adoption of the new object is the fresh spawn.
        pair.Directory.Unbind(weapon.Handle);
        arsenal.Frame();
        Assert.Single(watcher.Spawned);                          // a lost pair is not a spawn

        var fresh = LocalWeapon(engine);
        pair.Directory.Bind(fresh.Handle, -1, fresh);
        arsenal.Frame();

        Assert.Equal(new[] { WeaponEntry, WeaponEntry }, watcher.Spawned);   // recycled index adopted again
    }

    [Fact]
    public void A_watchers_never_hears_another_players_weapon()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        engine.Returns("Clock.Now", 10f);
        engine.Returns("EntityRender.LookupSequenceByActivity", 0);

        var pair = new Pair();
        var other = LocalWeapon(engine);
        other.State.OwnerIndex = LocalPlayer + 1;
        pair.Directory.Bind(other.Handle, -1, other);
        var viewModel = LocalViewModel(engine);
        pair.Directory.Bind(viewModel.Handle, -1, viewModel);

        var watcher = new Watcher();
        var arsenal = new ClientArsenal(new RecordingLog(), pair, engine, engine, engine,
                                        NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()),
                                        localWeapons: watcher);

        arsenal.Frame();

        Assert.Empty(watcher.Spawned);
    }
}

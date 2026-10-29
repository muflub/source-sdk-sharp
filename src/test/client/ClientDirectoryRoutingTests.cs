/*
 * WHAT THE CLIENT'S PER-FRAME LOOKUPS ASK — plan_entity.md §3.5, lane
 * entlist-client.
 *
 * §3.5 puts find-by-class in the DIRECTORY, and the point of putting it there
 * is that callers use it. Three sites in this client re-resolve a named entity
 * EVERY FRAME, and all three used to do it by walking every live entity and
 * comparing types at their own call site: the prediction's local player, the
 * fog's local player, and the arsenal's weapon-and-viewmodel pair. On
 * dm_lockdown that is 480 entities walked three times a frame to find at most
 * six.
 *
 * THE DOUBLE HERE ANSWERS OfClass AND THROWS FROM Live(). That is the whole
 * mechanism of these facts, and it is deliberately brutal: a routing that
 * "usually" goes through the index is not a routing, because the walk is the
 * thing being removed and a fallback to it is invisible in every other test.
 * A site that walks fails with an exception naming itself.
 */

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class ClientDirectoryRoutingTests
{
    private const int LocalPlayer = 1;

    /// <summary>
    /// A view that can answer find-by-class and CANNOT be walked.
    ///
    /// <para>
    /// It holds a real <see cref="ClientEntityDirectory"/> rather than a list,
    /// so what these facts exercise is the production index and not a stand-in
    /// written to look like one — the same rule <c>client_entity_counts.h</c>
    /// was extracted under.
    /// </para>
    /// </summary>
    private sealed class IndexOnly : IClientStandalones
    {
        public readonly ClientEntityDirectory Directory = new();

        public int Walks { get; private set; }

        public void Add(EntityHandle handle, ClientStandaloneEntity entity)
            => Directory.Bind(handle, -1, entity);

        public IReadOnlyList<ClientStandaloneEntity> Live()
        {
            Walks++;
            throw new InvalidOperationException(
                "this lookup walked every live entity instead of asking the " +
                "directory for the class it wanted (plan_entity.md §3.5)");
        }

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);

        public IEnumerable<ClientStandaloneEntity> OfClass(string? className)
            => Directory.OfClass(className);

        public IEnumerable<ClientStandaloneEntity> OfName(string? name)
            => Directory.OfName(name);
    }

    /// <summary>The handle a player at entry <paramref name="entry"/> holds.</summary>
    private static EntityHandle PlayerHandle(int entry) => new((uint)entry | (1u << 16));

    private static ClientSsPlayer Player(RecordingEngine engine, int entry)
    {
        var player = new ClientSsPlayer(engine) { Handle = PlayerHandle(entry) };
        player.State.BindLocal();
        return player;
    }

    private static ClientSsWeapon Weapon(RecordingEngine engine, int owner)
    {
        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0041u) };

        weapon.State.BindLocal();
        weapon.State.OwnerIndex = owner;
        weapon.State.WeaponTypeId = WeaponCatalog.Stock().IndexOf(EntityNames.WeaponPistol);
        weapon.State.Clip = 12;
        weapon.State.Reserve = 30;

        return weapon;
    }

    //-------------------------------------------------------------------------

    [Fact]
    public void TheArsenalFindsTheCarriedWeaponThroughFindByClass()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var standalones = new IndexOnly();
        standalones.Add(new EntityHandle(0x0100_0041u), Weapon(engine, LocalPlayer));

        var arsenal = new ClientArsenal(new RecordingLog(), standalones, engine, engine, engine,
                                        NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()));

        Assert.True(arsenal.TryGetActive(out string name, out _, out _));
        Assert.Equal(EntityNames.WeaponPistol, name);
        Assert.Equal(0, standalones.Walks);
    }

    [Fact]
    public void TheArsenalStillTellsTheOwnersApartThroughTheIndex()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var standalones = new IndexOnly();
        standalones.Add(new EntityHandle(0x0100_0041u), Weapon(engine, owner: 2));

        var arsenal = new ClientArsenal(new RecordingLog(), standalones, engine, engine, engine,
                                        NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()));

        // THE FILTER THE INDEX DOES NOT DO. A classname lookup answers every
        // weapon on the map; whose it is is still the owner compare, and a
        // routing that dropped it would arm the HUD off somebody else's gun.
        Assert.False(arsenal.TryGetActive(out _, out _, out _));
        Assert.Equal(0, standalones.Walks);
    }

    [Fact]
    public void TheFogFindsTheLocalPlayerThroughFindByClass()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var standalones = new IndexOnly();
        standalones.Add(PlayerHandle(LocalPlayer), Player(engine, LocalPlayer));

        var fog = new ClientWorldFog(standalones, engine, engine);

        fog.Pass();

        Assert.True(fog.SawViewer, "the fog found the local player through the class index");
        Assert.Equal(0, standalones.Walks);
    }

    [Fact]
    public void FindByClassAnswersOnlyTheClassAsked()
    {
        // The property the three routings rest on, asserted once against the
        // real directory: a lookup for players does not hand back the weapon
        // standing next to them.
        var engine = Fake();
        var standalones = new IndexOnly();

        standalones.Add(PlayerHandle(LocalPlayer), Player(engine, LocalPlayer));
        standalones.Add(new EntityHandle(0x0100_0041u), Weapon(engine, LocalPlayer));

        Assert.Single(standalones.OfClass(EntityNames.Player));
        Assert.Single(standalones.OfClass(EntityNames.SsWeapon));
        Assert.Equal(0, standalones.Walks);
    }

    [Fact]
    public void AViewWithNoIndexStillAnswersFindByClassOverItsList()
    {
        // The contract's DEFAULT arm, which is what every test double and every
        // list-shaped view gets. It has to answer the same set, or a caller
        // cannot be written against the contract at all.
        var engine = Fake();
        // THROUGH THE INTERFACE, which is not a formality: a default interface
        // member is reachable only through the interface type, so this is the
        // only way to call the arm under test -- and it is exactly how every
        // production caller reaches it.
        IClientStandalones listOnly = new ListOnly();

        ((ListOnly)listOnly).Entities.Add(Player(engine, LocalPlayer));
        ((ListOnly)listOnly).Entities.Add(Weapon(engine, LocalPlayer));

        Assert.Single(listOnly.OfClass(EntityNames.Player));
        Assert.Empty(listOnly.OfClass(null));
        Assert.Empty(listOnly.OfClass(string.Empty));
    }

    [Fact]
    public void AViewWithNoDirectoryAnswersNoNamesRatherThanGuessing()
    {
        var engine = Fake();
        IClientStandalones listOnly = new ListOnly();

        ((ListOnly)listOnly).Entities.Add(Player(engine, LocalPlayer));

        // A targetname is held by the directory alone, so a list-shaped view
        // has nothing to walk. Empty is the honest answer; a walk that matched
        // on classname instead would answer a different question.
        Assert.Empty(listOnly.OfName(EntityNames.Player));
        Assert.Empty(listOnly.OfName("anything"));
    }

    private sealed class ListOnly : IClientStandalones
    {
        public readonly List<ClientStandaloneEntity> Entities = new();

        public IReadOnlyList<ClientStandaloneEntity> Live() => Entities;

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }
}

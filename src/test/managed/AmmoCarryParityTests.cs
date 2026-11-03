extern alias client;

using SourceSharp.Abi;
using SourceSharp.Shared;

using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;

using C = client::SourceSharp.Shared;
using CAbi = client::SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// TF2 STAGE 1 CORE: BOTH SIDES ANSWER ONE PLAYER'S CARRY LIMIT ALIKE.
///
/// <para>
/// A mod's <c>IAmmoCarryRule</c> compiles into both of its side assemblies, as
/// the core's shared source does; each side folds the ammo row's limit through
/// its own copy, so the client's HUD shows the limit the server clamps to. Here
/// one mod text is compiled against each side and registered in each side's
/// container, as <c>[SideService]</c> registers it.
/// </para>
/// </summary>
public class AmmoCarryParityTests
{
    private const string ModSource = """
        using SourceSharp.Abi;
        using SourceSharp.Shared;

        namespace AmmoParity;

        // Player 1 is a heavy: twice the row's limit of everything.
        public sealed class ClassLimits : IAmmoCarryRule
        {
            public int MaxCarry(int playerIndex, in AmmoType ammo, int maxCarry)
                => playerIndex == 1 ? maxCarry * 2 : maxCarry;
        }
        """;

    private static AmmoCarry ServerLimits()
    {
        var log = new RecordingLog();
        var container = new ServiceContainer();
        var rule = EmittedWeaponSources.Instantiate<IAmmoCarryRule>(
            "AmmoParity", "AmmoParity.ClassLimits", ModSource, typeof(AmmoCarry).Assembly);
        container.AddInstance(rule.GetType(), rule);
        return new AmmoCarry(new Hooks<IAmmoCarryRule>(container, log), log);
    }

    private static C.AmmoCarry ClientLimits()
    {
        var log = new RecordingLog();
        var container = new CAbi.ServiceContainer();
        var rule = EmittedWeaponSources.Instantiate<C.IAmmoCarryRule>(
            "AmmoParity", "AmmoParity.ClassLimits", ModSource, typeof(C.AmmoCarry).Assembly);
        container.AddInstance(rule.GetType(), rule);
        return new C.AmmoCarry(new CAbi.Hooks<C.IAmmoCarryRule>(container, log), log);
    }

    [Fact]
    public void BothSidesFoldTheModsRuleToOneLimitPerPlayer()
    {
        var server = ServerLimits();
        var client = ClientLimits();

        foreach (var ammo in AmmoCatalog.Stock().All)
        {
            var clientAmmo = C.AmmoCatalog.Stock().For(ammo.Name)!.Value;

            Assert.Equal(ammo.MaxCarry * 2, server.MaxFor(1, in ammo));
            Assert.Equal(ammo.MaxCarry, server.MaxFor(2, in ammo));
            Assert.Equal((server.MaxFor(1, in ammo), server.MaxFor(2, in ammo)),
                         (client.MaxFor(1, in clientAmmo), client.MaxFor(2, in clientAmmo)));
        }
    }

    private sealed class Standalones : IClientStandalones
    {
        public readonly ClientEntityDirectory Directory = new();

        public IReadOnlyList<ClientStandaloneEntity> Live()
            => throw new InvalidOperationException("the arsenal finds its pair by class");

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);
        public IEnumerable<ClientStandaloneEntity> OfClass(string? className) => Directory.OfClass(className);
        public IEnumerable<ClientStandaloneEntity> OfName(string? name) => Directory.OfName(name);
    }

    /// <summary>The HUD's alt-ammo limit for a local player at <paramref name="localPlayer"/> holding the SMG.</summary>
    private static int HudSecondaryMax(int localPlayer, C.AmmoCarry? limits)
    {
        var engine = ClientDoubles.Fake();
        engine.Returns("Hud.LocalPlayerIndex", localPlayer);

        var weapons = C.WeaponCatalog.Stock();
        var weapon = new ClientSsWeapon(engine) { Handle = new EntityHandle(0x0100_0500u) };
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = localPlayer;
        weapon.State.WeaponTypeId = weapons.IndexOf("weapon_smg1");
        weapon.State.SecondaryReserve = 1;

        var viewModel = new ClientSsViewModel(engine, engine) { Handle = new EntityHandle(0x0100_0501u) };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = localPlayer;

        var standalones = new Standalones();
        standalones.Directory.Bind(weapon.Handle, 0x500, weapon);
        standalones.Directory.Bind(viewModel.Handle, 0x501, viewModel);

        var arsenal = new ClientArsenal(new RecordingLog(), standalones, engine, engine, engine,
                                        CAbi.NetStateCatalog.Of(TestServices.ClientGame, new RecordingLog()),
                                        weapons: weapons, carryLimits: limits);

        Assert.True(arsenal.TryGetActive(out _, out _, out _), "the HUD found the local weapon");
        Assert.True(arsenal.TryGetSecondary(out int current, out int max), "the SMG has an alt-fire pool");
        Assert.Equal(1, current);
        return max;
    }

    [Fact]
    public void TheClientsHudShowsTheLocalPlayersOwnLimit()
    {
        int row = C.AmmoCatalog.Stock().For("SMG1_Grenade")!.Value.MaxCarry;

        Assert.Equal(row, HudSecondaryMax(localPlayer: 1, limits: null));
        Assert.Equal(row * 2, HudSecondaryMax(localPlayer: 1, ClientLimits()));
        Assert.Equal(row, HudSecondaryMax(localPlayer: 2, ClientLimits()));
    }
}

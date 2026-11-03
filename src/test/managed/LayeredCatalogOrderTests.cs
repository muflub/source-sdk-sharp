extern alias client;

using SourceSharp.Abi;
using SourceSharp.Shared;

using C = client::SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A mod layered on another takes the same wire indices on both sides
/// (<see cref="HookOrder"/>): the catalogue sources order by the assembly's
/// side-less LAYER, so <c>Lyr</c>'s rows come before <c>Lyr.Descent</c>'s on
/// the server and on the client alike.
///
/// <para>
/// WHAT FAILED BEFORE: the sources sorted by raw assembly name, and the side
/// suffix decided it. <c>Lyr.Descent.Server</c> sorts before
/// <c>Lyr.Server</c>, but <c>Lyr.Client</c> before <c>Lyr.Descent.Client</c>,
/// so the server numbered the descent row first and the client the base row,
/// and the networked weapon and ammo indices of both rows disagreed. The
/// assemblies are emitted under those names (<see cref="EmittedWeaponSources"/>),
/// one copy per side, as a mod's shared source compiles into both of its side
/// assemblies; sources declared in this assembly would all share one name and
/// could not show the sort at all.
/// </para>
/// </summary>
public class LayeredCatalogOrderTests
{
    private const string BaseRow = "wep_layer_base";
    private const string DescentRow = "wep_layer_descent";
    private const string BaseAmmo = "LayerBaseAmmo";
    private const string DescentAmmo = "LayerDescentAmmo";
    private const string SharedKey = "lyr.shared";

    private const string ModTemplate = """
        using System.Collections.Generic;
        using SourceSharp.Abi;
        using SourceSharp.Shared;

        namespace {NS};

        public sealed class Behaviour : IWeaponBehaviour { }

        public sealed class Mod : IWeaponTypeSource, IAmmoTypeSource, IWeaponBehaviourSource
        {
            public IEnumerable<WeaponType> Rows() => new[] { new WeaponType("{ROW}", Interval: 0.5f) };

            IEnumerable<AmmoType> IAmmoTypeSource.Rows() => new[] { new AmmoType("{AMMO}", DamageType.Bullet, MaxCarry: 5) };

            public IEnumerable<KeyValuePair<string, IWeaponBehaviour>> Behaviours()
                => new[] { KeyValuePair.Create<string, IWeaponBehaviour>("{KEY}", new Behaviour()) };
        }
        """;

    private static T Emit<T>(string assembly, string row, string ammo, System.Reflection.Assembly side) where T : class
        => EmittedWeaponSources.Instantiate<T>(
            assembly, $"{assembly}.Mod",
            ModTemplate.Replace("{NS}", assembly).Replace("{ROW}", row).Replace("{AMMO}", ammo).Replace("{KEY}", SharedKey),
            side);

    /// <summary>Each side's two layers, registered base first on both, as a scan would hand them in.</summary>
    private static (object[] Server, object[] Client) Layers()
    {
        var server = typeof(WeaponType).Assembly;
        var client = typeof(C.WeaponType).Assembly;

        return ([Emit<IWeaponTypeSource>("Lyr.Server", BaseRow, BaseAmmo, server),
                 Emit<IWeaponTypeSource>("Lyr.Descent.Server", DescentRow, DescentAmmo, server)],
                [Emit<C.IWeaponTypeSource>("Lyr.Client", BaseRow, BaseAmmo, client),
                 Emit<C.IWeaponTypeSource>("Lyr.Descent.Client", DescentRow, DescentAmmo, client)]);
    }

    [Fact]
    public void BothSidesNumberALayeredModsWeaponRowsAlikeAndTheBaseLayerFirst()
    {
        var (s, c) = Layers();
        var log = new RecordingLog();
        var server = new WeaponCatalog(() => s.Cast<IWeaponTypeSource>().ToArray(), log);
        var client = new C.WeaponCatalog(() => c.Cast<C.IWeaponTypeSource>().ToArray(), log);

        Assert.Equal(WeaponTypes.All.Length, server.IndexOf(BaseRow));
        Assert.Equal(WeaponTypes.All.Length + 1, server.IndexOf(DescentRow));
        Assert.Equal(server.IndexOf(BaseRow), client.IndexOf(BaseRow));
        Assert.Equal(server.IndexOf(DescentRow), client.IndexOf(DescentRow));
    }

    [Fact]
    public void BothSidesNumberALayeredModsAmmoRowsAlikeAndTheBaseLayerFirst()
    {
        var (s, c) = Layers();
        var log = new RecordingLog();
        var server = new AmmoCatalog(() => s.Cast<IAmmoTypeSource>().ToArray(), log);
        var client = new C.AmmoCatalog(() => c.Cast<C.IAmmoTypeSource>().ToArray(), log);

        Assert.True(server.WireIndex(BaseAmmo) < server.WireIndex(DescentAmmo), "the base layer's ammo first");
        Assert.Equal(server.WireIndex(BaseAmmo), client.WireIndex(BaseAmmo));
        Assert.Equal(server.WireIndex(DescentAmmo), client.WireIndex(DescentAmmo));
    }

    [Fact]
    public void BothSidesGiveAKeyBothLayersOfferToTheBaseLayer()
    {
        var (s, c) = Layers();
        var log = new RecordingLog();
        var server = new WeaponBehaviours(() => s.Cast<IWeaponBehaviourSource>().ToArray(), log);
        var client = new C.WeaponBehaviours(() => c.Cast<C.IWeaponBehaviourSource>().ToArray(), log);

        Assert.Equal("Lyr.Server", server.For(SharedKey)!.GetType().Assembly.GetName().Name);
        Assert.Equal("Lyr.Client", client.For(SharedKey)!.GetType().Assembly.GetName().Name);
    }

    [Theory]
    [InlineData("TF2.Server", "TF2")]
    [InlineData("TF2.Client", "TF2")]
    [InlineData("TF2.Descent.Server", "TF2.Descent")]
    [InlineData("SourceSharp.Server.Interop", "SourceSharp.Interop")]
    [InlineData("TF2.Descent.Rules", "TF2.Descent.Rules")]
    [InlineData("Serverless.Clientele", "Serverless.Clientele")]
    public void ALayerIsTheNameWithoutItsSideSegments(string assembly, string layer)
        => Assert.Equal(layer, HookOrder.Layer(assembly));
}

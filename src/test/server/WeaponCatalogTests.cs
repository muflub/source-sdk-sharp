/*
 * PHASE 0.4: THE CATALOGUE OWNS THE WIRE FORMAT.
 *
 * The carried weapon networks which weapon it is as its index in the side's
 * weapon table (WeaponNetState.WeaponTypeId), and an ammo type reaches the
 * client's native table as its index in the ammo table (WireIndex). The list
 * each side builds, core rows then every mod source's rows, is the protocol:
 *
 *   - the core rows keep the indices they have always had;
 *   - a mod's rows append after them;
 *   - the order mod rows land in depends on the assembly set alone, not on the
 *     order the container registered the sources, so two sides that loaded
 *     the same mods in another order still agree about every index;
 *   - a duplicate or nameless row is refused with a warning.
 *
 * The order facts use EMITTED sources (EmittedWeaponSources): sources declared
 * in this assembly all share one assembly name, and the sort could be deleted
 * with every fact here staying green.
 */

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

public class WeaponCatalogTests
{
    // The core table's two ends, by name: the first and last rows the wire
    // has always carried (WeaponTypes.All).
    private const int CoreCount = 11;
    private const string FirstCore = "weapon_crowbar";
    private const string LastCore = "weapon_slam";

    private sealed class RowsSource(params WeaponType[] rows) : IWeaponTypeSource
    {
        public IEnumerable<WeaponType> Rows() => rows;
    }

    private sealed class AmmoRowsSource(params AmmoType[] rows) : IAmmoTypeSource
    {
        public IEnumerable<AmmoType> Rows() => rows;
    }

    private static WeaponType Row(string name) => new(name, Interval: 0.5f);

    private static WeaponCatalog With(ILog log, params IWeaponTypeSource[] sources) => new(() => sources, log);

    private const string WeaponSourceTemplate = """
        using SourceSharp.Shared;

        namespace {ASSEMBLY};

        public sealed class Source : IWeaponTypeSource
        {
            public System.Collections.Generic.IEnumerable<WeaponType> Rows()
                => new[] { new WeaponType("{ROW}", Interval: 0.5f) };
        }
        """;

    private const string AmmoSourceTemplate = """
        using SourceSharp.Abi;
        using SourceSharp.Shared;

        namespace {ASSEMBLY};

        public sealed class Source : IAmmoTypeSource
        {
            public System.Collections.Generic.IEnumerable<AmmoType> Rows()
                => new[] { new AmmoType("{ROW}", DamageType.Bullet, MaxCarry: 5) };
        }
        """;

    /// <summary>A weapon source compiled into an assembly of its own, named <paramref name="assembly"/>.</summary>
    private static IWeaponTypeSource EmittedWeapons(string assembly, string row)
        => EmittedWeaponSources.Instantiate<IWeaponTypeSource>(
            assembly, $"{assembly}.Source",
            WeaponSourceTemplate.Replace("{ASSEMBLY}", assembly).Replace("{ROW}", row),
            typeof(WeaponType).Assembly);

    private static IAmmoTypeSource EmittedAmmo(string assembly, string row)
        => EmittedWeaponSources.Instantiate<IAmmoTypeSource>(
            assembly, $"{assembly}.Source",
            AmmoSourceTemplate.Replace("{ASSEMBLY}", assembly).Replace("{ROW}", row),
            typeof(WeaponType).Assembly);

    //-------------------------------------------------------------------------
    // The core rows keep their indices.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheStockCatalogueIsTheCoreTableInOrder()
    {
        var stock = WeaponCatalog.Stock();

        Assert.Equal(CoreCount, stock.Count);
        Assert.Equal(0, stock.IndexOf(FirstCore));
        Assert.Equal(CoreCount - 1, stock.IndexOf(LastCore));

        for (int i = 0; i < WeaponTypes.All.Length; i++)
            Assert.True(stock.IndexOf(WeaponTypes.All[i].Name) == i, $"{WeaponTypes.All[i].Name} is row {i}");
    }

    [Fact]
    public void ASourcesRowsAppendAfterTheCoreWithoutRenumberingIt()
    {
        var catalog = With(new RecordingLog(), new RowsSource(Row("wep_mod_a"), Row("wep_mod_b")));

        Assert.Equal(0, catalog.IndexOf(FirstCore));
        Assert.Equal(CoreCount - 1, catalog.IndexOf(LastCore));
        Assert.Equal(CoreCount, catalog.IndexOf("wep_mod_a"));
        Assert.Equal(CoreCount + 1, catalog.IndexOf("wep_mod_b"));
        Assert.Equal(CoreCount + 2, catalog.Count);
        Assert.Equal("wep_mod_b", catalog.ByIndex(CoreCount + 1)?.Name);
        Assert.Null(catalog.ByIndex(CoreCount + 2));
    }

    //-------------------------------------------------------------------------
    // The order rule: the assembly set, not the registration order.
    //-------------------------------------------------------------------------

    [Fact]
    public void SourcesInReversedRegistrationOrderYieldTheSameIndices()
    {
        var alpha = EmittedWeapons("WepOrder.Alpha", "wep_order_alpha");
        var zulu = EmittedWeapons("WepOrder.Zulu", "wep_order_zulu");

        var forward = With(new RecordingLog(), alpha, zulu);
        var reversed = With(new RecordingLog(), zulu, alpha);

        Assert.Equal(
            forward.All.Select(r => r.Name).ToArray(),
            reversed.All.Select(r => r.Name).ToArray());

        // Sorted by assembly name, so Alpha's row is first in both: the
        // registration order that put Zulu first did not reach the table.
        Assert.Equal(CoreCount, reversed.IndexOf("wep_order_alpha"));
        Assert.Equal(CoreCount + 1, reversed.IndexOf("wep_order_zulu"));
    }

    [Fact]
    public void TheSidesCompositionOrdersContainerRegisteredSourcesByAssembly()
    {
        // The catalogue the modules build: sources resolved from the container,
        // each registered under its own type as [SideService] registers it, in
        // the order that fights the sort.
        var mike = EmittedWeapons("WepCompose.Mike", "wep_compose_mike");
        var alpha = EmittedWeapons("WepCompose.Alpha", "wep_compose_alpha");

        var container = new ServiceContainer();
        container.AddInstance(mike.GetType(), mike);
        container.AddInstance(alpha.GetType(), alpha);

        var catalog = new WeaponCatalog(() => container.ResolveAll<IWeaponTypeSource>(), new RecordingLog());

        Assert.Equal(CoreCount, catalog.IndexOf("wep_compose_alpha"));
        Assert.Equal(CoreCount + 1, catalog.IndexOf("wep_compose_mike"));
    }

    [Fact]
    public void AmmoSourcesInReversedRegistrationOrderYieldTheSameWireIndices()
    {
        var alpha = EmittedAmmo("WepAmmoOrder.Alpha", "WepAmmoAlpha");
        var zulu = EmittedAmmo("WepAmmoOrder.Zulu", "WepAmmoZulu");

        var reversed = new AmmoCatalog(() => [zulu, alpha], new RecordingLog());
        int coreAmmo = AmmoTypes.All.Length;

        Assert.Equal(coreAmmo + 1, reversed.WireIndex("WepAmmoAlpha"));
        Assert.Equal(coreAmmo + 2, reversed.WireIndex("WepAmmoZulu"));
    }

    //-------------------------------------------------------------------------
    // Refusals: out loud, and the first owner keeps the index.
    //-------------------------------------------------------------------------

    [Fact]
    public void ADuplicateRowIsRefusedWithAWarningAndTheFirstOwnerKeepsItsIndex()
    {
        var log = new RecordingLog();
        var catalog = With(log,
            new RowsSource(Row("weapon_pistol"), Row("wep_dup_first")),
            new RowsSource(Row("WEP_DUP_FIRST"), Row("wep_dup_second")));

        Assert.Equal(1, catalog.IndexOf("weapon_pistol"));
        Assert.Equal(CoreCount, catalog.IndexOf("wep_dup_first"));
        Assert.Equal(CoreCount + 1, catalog.IndexOf("wep_dup_second"));
        Assert.Equal(CoreCount + 2, catalog.Count);

        var warnings = log.Lines.Where(l => l.Warning).Select(l => l.Text).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.Contains("'weapon_pistol'", warnings[0]);
        Assert.Contains("'WEP_DUP_FIRST'", warnings[1]);
    }

    [Fact]
    public void ANamelessRowIsRefusedWithAWarning()
    {
        var log = new RecordingLog();
        var catalog = With(log, new RowsSource(Row(""), Row("wep_after_blank")));

        Assert.Equal(CoreCount, catalog.IndexOf("wep_after_blank"));
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("no name"));
    }

    //-------------------------------------------------------------------------
    // The ammo table: the same rules, and stock's one-based native index.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnAmmoSourcesRowsAppendAndCountOnFromTheCoresWireIndex()
    {
        var catalog = new AmmoCatalog(
            () => [new AmmoRowsSource(new AmmoType("WepModAmmo", DamageType.Bullet, MaxCarry: 5))],
            new RecordingLog());

        // CAmmoDef counts from one: Pistol is 1, XBowBolt 10, slam (the core's last) 11.
        Assert.Equal(1, catalog.WireIndex("Pistol"));
        Assert.Equal(10, catalog.WireIndex("XBowBolt"));
        Assert.Equal(11, catalog.WireIndex("slam"));
        Assert.Equal(12, catalog.WireIndex("WepModAmmo"));
        Assert.Equal(12, catalog.Count);
        Assert.Equal(-1, catalog.WireIndex("NoSuchAmmo"));
    }

    [Fact]
    public void WithNoAmmoSourcesTheAmmoCatalogIsTheCoreTableInOrder()
    {
        // The no-owner arm: a side with no IAmmoTypeSource registered builds
        // exactly the core's ammo table, the wire indices stock's CAmmoDef
        // has always numbered (Pistol 1 first, slam 11 last).
        var log = new RecordingLog();
        var empty = new AmmoCatalog(() => [], log);
        var stock = AmmoCatalog.Stock();

        Assert.Equal(11, empty.Count);
        Assert.Equal("Pistol", empty.All[0].Name);
        Assert.Equal("slam", empty.All[^1].Name);
        Assert.Equal(1, empty.WireIndex("Pistol"));
        Assert.Equal(10, empty.WireIndex("XBowBolt"));
        Assert.Equal(AmmoTypes.All.Select(a => a.Name).ToArray(), empty.All.Select(a => a.Name).ToArray());
        Assert.Equal(stock.All.ToArray(), empty.All.ToArray());
        for (int i = 0; i < empty.Count; i++)
            Assert.True(empty.WireIndex(empty.All[i].Name) == i + 1, $"{empty.All[i].Name} is wire index {i + 1}");
        Assert.Empty(log.Lines);
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// Copies the player's attribute walk onto the carrier for every client
/// (<see cref="TFAttributeWire"/>; user ruling L11, 2026-11-11): the manager a
/// kit built (<see cref="Econ.TFLoadoutProvider.Weapons"/>), its own list (the
/// runtime attributes, <see cref="TFPlayerAttributes"/>), its providers in
/// order (the kit's items, the worn items, the standing sources'
/// containers) and the kit's items that do not provide.
///
/// <para>
/// Rewritten only when the walk changed: each tick compares the manager and
/// the version (<see cref="AttributeContainer.Version"/>) of it and of every
/// container on the walk with what the last write saw, and no value is
/// compared. A rewrite moves <see cref="TFPlayerNetState.AttributeSerial"/>
/// when the lists say something or said something before; a walk with no
/// values of its own leaves them empty and quiet.
/// </para>
///
/// <para>
/// Past the wire's caps the write keeps what fits, in order; what it left out
/// is counted (<see cref="Truncations"/>) and said on the log, because the
/// client then predicts without it.
/// </para>
/// </summary>
public sealed class TFAttributeListMirror(TFPlayerCarriers carriers, TFPlayerItems items, WeaponCatalog catalog,
                                          ILog log)
    : IPlayerComponent
{
    private readonly TFPlayerCarriers _carriers = carriers;
    private readonly TFPlayerItems _items = items;
    private readonly WeaponCatalog _catalog = catalog;
    private readonly ILog _log = log;

    // What the last write saw: the containers on the walk and their versions.
    private readonly List<(object Container, int Version)> _seen = [];
    private readonly List<(object Container, int Version)> _now = [];
    private readonly Dictionary<IAttributeProvider, int> _rows = new(ReferenceEqualityComparer.Instance);

    private int _index = -1;
    private bool _written;

    /// <summary>After the runtime list's expiry (45), before the ammo pools (50).</summary>
    public int Order => 46;

    /// <summary>The rewrites that could not carry the whole walk.</summary>
    public int Truncations { get; private set; }

    public void Attach(EntityHandle player)
    {
        _index = player.EntryIndex;
        _seen.Clear();
        _written = false;
    }

    public void Tick(float now)
    {
        if (_index < 0 || _carriers.Get(_index) is not { } state)
            return;

        var manager = _items.ManagerOf(_index);
        Snapshot(manager, _now);
        if (_written && Same(_seen, _now))
            return;

        _seen.Clear();
        _seen.AddRange(_now);
        _written = true;

        int before = state.AttributeProviderCount;
        _rows.Clear();
        foreach (var item in _items.Kit(_index))
        {
            if (Row(item) is int row)
                _rows[item] = row;
        }

        var dropped = TFAttributeWire.Write(state, manager, _rows);
        if (state.AttributeProviderCount != 0 || before != 0)
            state.AttributeSerial++;

        if (dropped.Any)
        {
            Truncations++;
            _log.Warning($"[TF2/Attributes] player {_index}: {dropped.Providers} provider(s) and {dropped.Values} " +
                         $"value(s) past the wire's {TFAttributeWire.MaxProviders} and {TFAttributeWire.MaxValues} " +
                         "were not sent; clients walk without them\n");
        }
    }

    public void Detach()
    {
        _index = -1;
        _seen.Clear();
        _written = false;
    }

    /// <summary>The weapon row <paramref name="item"/> stands behind in the player's kit.</summary>
    private int? Row(IAttributeProvider item)
    {
        foreach (var type in _catalog.All)
        {
            if (ReferenceEquals(_items.Of(_index, type.Name), item))
                return _catalog.IndexOf(type.Name);
        }
        return null;
    }

    /// <summary>The manager, its providers and the kit's items, each with its version, in walk order.</summary>
    private void Snapshot(IAttributeProvider? manager, List<(object, int)> into)
    {
        into.Clear();
        if (manager is null)
            return;

        into.Add((manager, VersionOf(manager)));
        var providers = manager.Providers;
        for (int i = 0; i < providers.Count; i++)
        {
            if (providers[i] is { } provider)
                into.Add((provider, VersionOf(provider)));
        }
        foreach (var item in _items.Kit(_index))
            into.Add((item, VersionOf(item)));
    }

    private static int VersionOf(IAttributeProvider provider) => provider is AttributeContainer c ? c.Version : 0;

    private static bool Same(List<(object Container, int Version)> a, List<(object Container, int Version)> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i].Container, b[i].Container) || a[i].Version != b[i].Version)
                return false;
        }
        return true;
    }
}

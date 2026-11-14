using TF2.Shared.Player;

namespace TF2.Shared.Econ;

/// <summary>
/// The player's attribute walk on the wire (<see cref="TFPlayerNetState.AttributeProviders"/>
/// and the three arrays beside it): the server writes the walk its manager
/// makes, every client reads it back into a manager that walks alike.
///
/// <para>
/// <b>ORDER.</b> Two providers compose in the order they are met
/// (<see cref="AttributeHooks"/>: an additive then a percentage is not a
/// percentage then an additive), so the run is the walk's own order: the
/// manager's own list, then its providers as <c>m_Providers</c> holds them,
/// then the kit's items that do not provide (their own values still answer a
/// walk on the item itself). The client rebuilds the same list in the same
/// order.
/// </para>
///
/// <para>
/// <b>WHAT CROSSES.</b> Each provider's OWN values (an item instance's rolled
/// attributes, the runtime list, a standing source's): the item definition
/// index rides beside them, and each side looks the item's static attributes
/// up in its own parsed schema, as the weapon rows' definition indexes do. A
/// value is its 32 bits (<see cref="AttributeValue.Bits"/>); a value with text
/// (the string and wide types) is no hook's float and does not cross. A
/// provider's own providers do not cross either: none of the server's do.
/// </para>
///
/// <para>
/// <b>EMPTY.</b> While no provider holds a value of its own, nothing is
/// written (<see cref="TFPlayerNetState.AttributeProviderCount"/> 0) and the
/// client builds its manager from the weapon rows as it always has.
/// </para>
///
/// <para>
/// <b>THE CAPS</b> are <see cref="MaxProviders"/> providers and
/// <see cref="MaxValues"/> values. Past either, the walk is written up to the
/// cap, in order, and <see cref="Write"/> answers what it left out, which the
/// server's mirror counts and logs: the client then predicts without it.
/// </para>
/// </summary>
public static class TFAttributeWire
{
    public const int MaxProviders = TFPlayerNetState.AttributeProviderSlots;
    public const int MaxValues = TFPlayerNetState.AttributeValueSlots;

    /// <summary>The item definition index plus one; zero, no item (20 bits, stock's <c>m_iItemDefinitionIndex</c> width).</summary>
    private const int ItemMask = (1 << 20) - 1;

    private const int RowShift = 20;

    /// <summary>The weapon-catalogue row plus one; zero, not one of the kit's weapons.</summary>
    private const int RowMask = 0xFF;

    private const int WeaponTypeBit = 1 << 28;
    private const int ProvidesBit = 1 << 29;
    private const int OwnListBit = 1 << 30;

    /// <summary>What a write left out: providers past the cap, and values past it (theirs included).</summary>
    public readonly record struct Dropped(int Providers, int Values)
    {
        public bool Any => Providers > 0 || Values > 0;
    }

    /// <summary>
    /// Writes <paramref name="manager"/>'s walk onto <paramref name="state"/>.
    /// <paramref name="kitRows"/> names each of the kit's items by the weapon
    /// row it is (an item there that provides keeps its place among the
    /// providers; one that does not is written after them). Answers what did
    /// not fit. Moves no serial: the caller decides whether this is news.
    /// </summary>
    public static Dropped Write(TFPlayerNetState state, IAttributeProvider? manager,
                                IReadOnlyDictionary<IAttributeProvider, int> kitRows)
    {
        int providers = 0;
        int values = 0;
        int droppedProviders = 0;
        int droppedValues = 0;
        bool any = false;

        // A walk with no value of its own writes nothing: the arrays keep
        // what they held and only the count says empty, so a kit change with
        // nothing to tell sends nothing.
        if (manager is not null && HoldsValues(manager, kitRows))
        {
            Add(state, OwnListBit, manager.Attributes, ref providers, ref values, ref droppedProviders,
                ref droppedValues, ref any);

            var list = manager.Providers;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not { } provider)
                    continue;
                Add(state, Word(provider, kitRows, provides: true), provider.Attributes, ref providers, ref values,
                    ref droppedProviders, ref droppedValues, ref any);
            }

            // The kit's items that stand out of the providers (provide_on_active),
            // in row order so the run does not depend on a dictionary's.
            foreach (var (item, _) in kitRows.OrderBy(k => k.Value))
            {
                if (Provides(manager, item))
                    continue;
                Add(state, Word(item, kitRows, provides: false), item.Attributes, ref providers, ref values,
                    ref droppedProviders, ref droppedValues, ref any);
            }
        }

        state.AttributeProviderCount = any ? providers : 0;
        return new Dropped(droppedProviders, droppedValues);
    }

    /// <summary>
    /// Reads the walk <see cref="Write"/> wrote back into a manager: the
    /// player's own list on it, then each provider rebuilt over its item's
    /// definition from <paramref name="schema"/> with its own values, providing
    /// where it provided. Each of the kit's items lands in
    /// <paramref name="rows"/> under its weapon row, owned by the manager as
    /// the server's kit owns it. Null while the lists are empty. A value whose
    /// definition the schema does not hold is passed by.
    /// </summary>
    public static AttributeContainer? Read(TFPlayerNetState state, ItemSchema schema,
                                           IDictionary<int, AttributeContainer> rows)
    {
        int count = Math.Min(state.AttributeProviderCount, MaxProviders);
        if (count <= 0)
            return null;

        var manager = new AttributeContainer();
        int v = 0;
        for (int p = 0; p < count; p++)
        {
            int word = state.AttributeProviders[p];
            AttributeContainer target;
            if ((word & OwnListBit) != 0)
            {
                target = manager;
            }
            else
            {
                int defindex = (word & ItemMask) - 1;
                target = new AttributeContainer(defindex >= 0 ? schema.Item(defindex) : null,
                                                (word & WeaponTypeBit) != 0
                                                    ? AttributeProviderType.Weapon
                                                    : AttributeProviderType.Generic);
                int row = ((word >> RowShift) & RowMask) - 1;
                if (row >= 0)
                {
                    rows[row] = target;
                    target.Owner = manager;
                }
                if ((word & ProvidesBit) != 0)
                    target.ProvideTo(manager);
            }

            int n = state.AttributeProviderValues[p];
            for (int i = 0; i < n && v < MaxValues; i++, v++)
            {
                if (schema.Attribute(state.AttributeDefindex[v]) is { } definition)
                    target.Set(new AttributeValue(definition, state.AttributeBits[v]));
            }
        }

        return manager;
    }

    private static int Word(IAttributeProvider provider, IReadOnlyDictionary<IAttributeProvider, int> kitRows,
                            bool provides)
    {
        int word = 0;
        if (provider is AttributeContainer { Item: { } item } && item.Defindex >= 0 && item.Defindex < ItemMask)
            word |= item.Defindex + 1;
        if (kitRows.TryGetValue(provider, out int row) && row >= 0 && row < RowMask)
            word |= (row + 1) << RowShift;
        if (provider.ProviderType == AttributeProviderType.Weapon)
            word |= WeaponTypeBit;
        if (provides)
            word |= ProvidesBit;
        return word;
    }

    /// <summary>Whether anything on the walk holds a value of its own that could cross.</summary>
    private static bool HoldsValues(IAttributeProvider manager, IReadOnlyDictionary<IAttributeProvider, int> kitRows)
    {
        if (HoldsValue(manager.Attributes))
            return true;
        var list = manager.Providers;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is { } provider && HoldsValue(provider.Attributes))
                return true;
        }
        foreach (var item in kitRows.Keys)
        {
            if (HoldsValue(item.Attributes))
                return true;
        }
        return false;
    }

    private static bool HoldsValue(ReadOnlySpan<AttributeValue> own)
    {
        foreach (ref readonly var value in own)
        {
            if (value.Text is null)
                return true;
        }
        return false;
    }

    private static bool Provides(IAttributeProvider manager, IAttributeProvider item)
    {
        var list = manager.Providers;
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
                return true;
        }
        return false;
    }

    /// <summary>One provider and its own values, as far as the caps let it.</summary>
    private static void Add(TFPlayerNetState state, int word, ReadOnlySpan<AttributeValue> own,
                            ref int providers, ref int values, ref int droppedProviders, ref int droppedValues,
                            ref bool any)
    {
        int crossing = 0;
        foreach (ref readonly var value in own)
        {
            if (value.Text is null)
                crossing++;
        }

        if (providers >= MaxProviders)
        {
            droppedProviders++;
            droppedValues += crossing;
            return;
        }

        int slot = providers++;
        int written = 0;
        foreach (ref readonly var value in own)
        {
            if (value.Text is not null)
                continue;
            if (values >= MaxValues || value.Definition.Defindex is < 0 or > ushort.MaxValue)
            {
                droppedValues++;
                continue;
            }
            state.AttributeDefindex[values] = (ushort)value.Definition.Defindex;
            state.AttributeBits[values] = value.Bits;
            values++;
            written++;
        }

        state.AttributeProviders[slot] = word;
        state.AttributeProviderValues[slot] = (byte)written;
        any |= crossing > 0;
    }
}

using System.Reflection;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// One hash per protocol list, over that list's KEYS: string table names,
/// temp-entity classnames, networked-state classnames (plan_side_isolation
/// §2.1).
///
/// <para>
/// Each list is in key order, so two sides holding the same set of keys hold
/// the same sequence, and comparing the sets compares the lists. What this
/// catches is the one thing key order cannot: a declaration only one side
/// compiles. Once the shared projects fold into each side, which assembly a
/// declaration came from says nothing about whether the other side has it,
/// so the comparison happens at join instead (the layout check carries these
/// three hashes) and names the list that differs.
/// </para>
/// </summary>
public sealed class ProtocolManifest
{
    public ProtocolManifest(uint stringTables, uint tempEnts, uint netStates)
    {
        StringTables = stringTables;
        TempEnts     = tempEnts;
        NetStates    = netStates;
    }

    public uint StringTables { get; }
    public uint TempEnts     { get; }
    public uint NetStates    { get; }

    /// <summary>
    /// The manifest of everything <paramref name="anchor"/>'s scan declares:
    /// the same scan, and the same key of each declaration, the registries use.
    /// </summary>
    public static ProtocolManifest Of(Assembly anchor, ILog log)
    {
        var tables = new List<string>();
        var tempEnts = new List<string>();
        var netStates = new List<string>();

        foreach (var type in LoadedAssemblies.Types(anchor, log))
        {
            if (type.GetCustomAttribute<StringTableAttribute>() is { } table)
                tables.Add(table.Name);

            if (type.GetCustomAttribute<TempEntityAttribute>() is not null)
                tempEnts.Add(TempEntityAttribute.ClassNameOf(type));

            if (type.GetCustomAttribute<StandaloneNetStateAttribute>() is not null)
                netStates.Add(StandaloneNetStates.Of(type).ClassName);
        }

        return new ProtocolManifest(Hash(tables), Hash(tempEnts), Hash(netStates));
    }

    /// <summary>
    /// FNV-1a over the keys in ordinal order, each followed by a zero byte so
    /// that ["ab", "c"] and ["a", "bc"] differ. Order-insensitive by
    /// construction: it is the SET of keys being compared.
    /// </summary>
    public static uint Hash(IEnumerable<string> keys)
    {
        const uint offsetBasis = 2166136261u;
        const uint prime       = 16777619u;

        uint hash = offsetBasis;

        foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            foreach (byte b in Encoding.UTF8.GetBytes(key))
            {
                hash ^= b;
                hash *= prime;
            }

            hash *= prime; // the zero separator: hash ^= 0 is a no-op
        }

        return hash;
    }

    /// <summary>The lists whose hash differs from the other side's, by name. Empty when all agree.</summary>
    public IReadOnlyList<string> Differences(uint stringTables, uint tempEnts, uint netStates)
    {
        var differ = new List<string>();

        if (stringTables != StringTables) differ.Add("string tables");
        if (tempEnts     != TempEnts)     differ.Add("temp entities");
        if (netStates    != NetStates)    differ.Add("networked states");

        return differ;
    }
}

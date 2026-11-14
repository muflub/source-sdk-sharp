using System.Reflection;

namespace TF2.Descent.Rules.Content;

/// <summary>Where a load reads its tables from: one JSON document per table name.</summary>
public interface IContentSource
{
    /// <summary>The bytes of <paramref name="table"/>, or false when the source has no such table.</summary>
    bool TryRead(string table, out byte[] bytes);
}

/// <summary>
/// The tables shipped inside this assembly (plan_descent_logic E3): the game and the
/// host load the same dll, so they read the same bytes. Nothing on disk is read.
/// </summary>
public sealed class EmbeddedContentSource : IContentSource
{
    /// <summary>The manifest name prefix of a table; the csproj gives each Content/*.json this logical name.</summary>
    public const string ResourcePrefix = "TF2.Descent.Rules.Content.";

    private readonly Assembly _assembly;

    public EmbeddedContentSource() : this(typeof(EmbeddedContentSource).Assembly) { }

    public EmbeddedContentSource(Assembly assembly) => _assembly = assembly;

    public bool TryRead(string table, out byte[] bytes)
    {
        using var stream = _assembly.GetManifestResourceStream(ResourcePrefix + table + ".json");
        if (stream is null)
        {
            bytes = [];
            return false;
        }
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        bytes = copy.ToArray();
        return true;
    }
}

/// <summary>Tables held in memory: a fixture's mutated copy of the shipped content, or a tool's.</summary>
public sealed class MemoryContentSource : IContentSource
{
    private readonly Dictionary<string, byte[]> _tables = new(StringComparer.Ordinal);

    public MemoryContentSource() { }

    /// <summary>A copy of every table <paramref name="from"/> has among <paramref name="tables"/>.</summary>
    public MemoryContentSource(IContentSource from, IEnumerable<string> tables)
    {
        foreach (var t in tables)
            if (from.TryRead(t, out var bytes))
                _tables[t] = bytes;
    }

    public IReadOnlyCollection<string> Tables => _tables.Keys;

    public MemoryContentSource Set(string table, byte[] bytes)
    {
        _tables[table] = bytes;
        return this;
    }

    public MemoryContentSource Remove(string table)
    {
        _tables.Remove(table);
        return this;
    }

    public bool TryRead(string table, out byte[] bytes)
    {
        if (_tables.TryGetValue(table, out var found))
        {
            bytes = found;
            return true;
        }
        bytes = [];
        return false;
    }
}

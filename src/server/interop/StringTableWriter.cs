namespace SourceSharp.Abi;

/// <summary>
/// The server's <see cref="IStringTableWriter"/>: a string table add, over the
/// server's own id-addressed string table area.
///
/// <para>
/// P1 run 3, the integrator's ruling (a): the add is a server-only slot, so it
/// is the server's code. The shared <c>StringTableHandle</c> holds one of these
/// on the server and none on the client, where the add is refused before the
/// boundary exactly as before.
/// </para>
/// </summary>
public sealed class StringTableWriter : IStringTableWriter
{
    private readonly IEngineStringTables _tables;

    internal StringTableWriter(IEngineStringTables tables)
        => _tables = tables ?? throw new ArgumentNullException(nameof(tables));

    /// <inheritdoc/>
    public int AddString(int tableId, string value) => _tables.AddString(tableId, value);

    /// <summary>Registers the server's writer over its engine area. Before the string table registry.</summary>
    public static void AddTo(ServiceContainer services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddInstance<IStringTableWriter>(new StringTableWriter(services.Require<IEngineStringTables>()));
    }
}

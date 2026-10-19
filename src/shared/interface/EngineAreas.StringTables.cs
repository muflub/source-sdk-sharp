namespace SourceSharp.Abi;

/// <summary>
/// The six id-addressed operations the ABI actually has.
///
/// <para>
/// <b>INTERNAL, and that is the change.</b> It is still an engine area — the
/// naming rule enrols it and <c>EngineStringTables</c> binds it — but nothing
/// outside this assembly can name it, so no mod code can be handed a
/// <c>TABLEID</c> to pass to whichever table holds that position today. The
/// public surface is <see cref="StringTableAttribute"/> and
/// <see cref="IEngineStringTable"/>, and neither of them has an id in it.
/// </para>
/// </summary>
internal interface IEngineStringTables
{
    /// <summary>
    /// Creates a table and answers its id, or the id of the table that already
    /// has that name — which is what a <c>cs_sv_reload</c> produces and is not
    /// an error. <c>-1</c> on refusal, including a create attempted outside
    /// <c>CreateNetworkStringTables</c>.
    /// </summary>
    int Create(string name, int maxEntries);

    /// <summary>The id of an existing table, or <c>-1</c>. Valid at any time.</summary>
    int Find(string name);

    int AddString(int tableId, string value);
    int FindStringIndex(int tableId, string value);
    string? GetString(int tableId, int stringIndex);
    int GetNumStrings(int tableId);
}

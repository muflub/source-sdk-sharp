namespace SourceSharp.Abi;

/// <summary>
/// The user-message id table (plan_side_isolation §2.2): the server writes the
/// key of engine id <i>i</i> at index <i>i</i> in <c>CreateNetworkStringTables</c>
/// (<see cref="UserMessageRegistry.WriteTable"/>), and each client maps an
/// arriving id to its key through the replicated copy
/// (<see cref="UserMessageReceiver"/>).
///
/// <para>
/// 256 entries: the engine's message id is one byte, and a string table's size
/// must be a power of two (the engine refuses any other). It costs one of the
/// engine's 32 tables: at the P2 gate SourceSharpManaged was table id 18, so
/// this one, created after it in name order, is id 19.
/// </para>
/// </summary>
[StringTable(UserMessageTables.Name, UserMessageTables.MaxEntries)]
public interface UserMessageTable : IEngineStringTable
{
}

/// <summary>The SsUserMessages table's name and size, for the declaration and its readers.</summary>
public static class UserMessageTables
{
    /// <summary>The table's name, the one both sides find it by.</summary>
    public const string Name = "SsUserMessages";

    /// <summary>Its size: the one-byte id's range, a power of two.</summary>
    public const int MaxEntries = 256;
}

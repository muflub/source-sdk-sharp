using System.Text;

namespace SourceSharp.Abi;

/// <summary>The client's string tables, read: an entry's user data as text.</summary>
public interface IClientStringTables
{
    /// <summary>
    /// The user data of <paramref name="entry"/> in table <paramref name="table"/>,
    /// as UTF-8 text up to its first NUL; null when there is no such table or
    /// entry, or it has no data.
    /// </summary>
    string? UserDataText(string table, string entry);
}

/// <summary>
/// The client's INetworkStringTableContainer (VEngineClientStringTable001)
/// through its vtable: FindTable, then the table's FindStringIndex and
/// GetStringUserData, which is what stock's CTextWindow::ShowIndex reads for
/// the message of the day (the InfoPanel table's "motd").
/// </summary>
public sealed unsafe class ClientStringTables : IClientStringTables
{
    public const string InterfaceVersion = "VEngineClientStringTable001";

    // The engine's INVALID_STRING_INDEX, (unsigned short)~0.
    private const int InvalidStringIndex = 0xFFFF;

    private readonly void* _self;
    private readonly NetworkStringTableContainerFwd* _fwd;

    // The forwarders of the INetworkStringTable objects FindTable answers.
    private readonly NetworkStringTableFwd* _tables;

    private ClientStringTables(void* self, NetworkStringTableContainerFwd* table, NetworkStringTableFwd* tables)
    {
        _self = self;
        _fwd = table;
        _tables = tables;
    }

    /// <summary>The client's container through <paramref name="engine"/>'s factory, or null with the reason.</summary>
    public static IClientStringTables? Acquire(ModuleEngine engine, out string why)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return Forwarders.TryAcquire<NetworkStringTableContainerFwd>(
                   engine.Interfaces, SourceSharpFactoryType.AppSystem, InterfaceVersion,
                   out void* self, out NetworkStringTableContainerFwd* table, out why)
               && Forwarders.TryAcquire(engine.Interfaces, out NetworkStringTableFwd* tables, out why)
            ? new ClientStringTables(self, table, tables)
            : null;
    }

    /// <inheritdoc/>
    public string? UserDataText(string table, string entry)
    {
        byte[] name = Vtable.Utf8(table);
        void* found;
        fixed (byte* p = name)
            found = _fwd->FindTable(_self, p);
        if (found == null)
            return null;

        // The table FindTable returns is called through its own forwarders.
        NetworkStringTableFwd* vt = _tables;
        byte[] key = Vtable.Utf8(entry);
        int index;
        fixed (byte* p = key)
            index = vt->FindStringIndex(found, p);
        if (index == InvalidStringIndex || index < 0)
            return null;

        int length = 0;
        byte* data = (byte*)vt->GetStringUserData(found, index, &length);
        if (data == null || length <= 0)
            return null;
        int end = 0;
        while (end < length && data[end] != 0)
            end++;
        return end == 0 ? null : Encoding.UTF8.GetString(data, end);
    }
}

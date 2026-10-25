using System;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// The id-addressed string tables over the engine's container and its tables'
/// forwarders (plan_server_native_v2 T3). This was <c>CShellStringTables</c>
/// (shell_stringtables.h) behind six shim slots, on both sides; the rules are
/// its, unchanged: a create answers the existing table of that name, a
/// refusal is <c>-1</c> and says why, a miss is <c>-1</c> and never
/// <c>INVALID_STRING_INDEX</c>, and an entry too long for the caller's buffer
/// is refused rather than truncated.
///
/// <para>
/// Creating and adding are the server's: the client's slots never had them,
/// and on the client both answer <c>-1</c> without asking the engine.
/// </para>
/// </summary>
internal sealed unsafe class ForwardedStringTables
{
    /// <summary>INVALID_STRING_TABLE.</summary>
    private const int InvalidTable = -1;

    /// <summary>INVALID_STRING_INDEX, <c>(unsigned short)~0</c>.</summary>
    private const int InvalidIndex = 0xFFFF;

    /// <summary>The buffer the slot copied an entry into; longer ones were refused.</summary>
    internal const int MaxEntryBytes = 260;

    private readonly ILog _log;
    private readonly bool _isServer;
    private readonly void* _self;
    private readonly NetworkStringTableContainerFwd* _fwd;
    private readonly NetworkStringTableFwd* _table;

    private ForwardedStringTables(ILog log, bool isServer, void* self,
                                  NetworkStringTableContainerFwd* fwd, NetworkStringTableFwd* table)
    {
        _log = log;
        _isServer = isServer;
        _self = self;
        _fwd = fwd;
        _table = table;
    }

    /// <summary>
    /// The <paramref name="side"/>'s container, acquired through its
    /// <paramref name="interfaces"/>. One that cannot be acquired is said once,
    /// to <paramref name="log"/>, and every call on it then refuses as a
    /// missing container did.
    /// </summary>
    internal static ForwardedStringTables Acquire(NativeInterfaceFactory interfaces, Side side, ILog log)
    {
        ArgumentNullException.ThrowIfNull(interfaces);
        ArgumentNullException.ThrowIfNull(log);
        bool isServer = side == Side.Server;
        string version = isServer ? "VEngineServerStringTable001" : "VEngineClientStringTable001";

        if (Forwarders.TryAcquire<NetworkStringTableContainerFwd>(
                interfaces, SourceSharpFactoryType.AppSystem, version,
                out void* self, out NetworkStringTableContainerFwd* fwd, out string why)
            && Forwarders.TryAcquire(interfaces, out NetworkStringTableFwd* table, out why))
        {
            return new ForwardedStringTables(log, isServer, self, fwd, table);
        }

        log.Warning($"[SourceSharp] string tables: no {version} ({why}); every table refuses\n");
        return new ForwardedStringTables(log, isServer, null, null, null);
    }

    private bool HasContainer => _self != null;

    public int Create(string name, int maxEntries)
    {
        if (!_isServer)
            return InvalidTable;

        if (!HasContainer || string.IsNullOrEmpty(name))
        {
            _log.Warning("[SourceSharp] string table create: no container or no name; refused\n");
            return InvalidTable;
        }

        void* existing = FindTable(name);
        if (existing != null)
        {
            _log.Warning($"[SourceSharp] string table '{name}' already exists; answering the existing table rather than creating a second. "
                       + "If two pieces of mod code claim this name, one of them is reading the other's strings.\n");
            return _table->GetTableId(existing);
        }

        if (maxEntries <= 0)
        {
            _log.Warning($"[SourceSharp] string table '{name}': maxEntries must be positive; refused\n");
            return InvalidTable;
        }

        void* created;
        fixed (byte* p = Vtable.Utf8(name))
            created = _fwd->CreateStringTable(_self, p, maxEntries, 0, 0);

        if (created == null)
        {
            _log.Warning($"[SourceSharp] string table '{name}': the engine refused to create it. Tables may only be created from "
                       + "IServerModule's CreateNetworkStringTables, which is the one moment the engine allows it.\n");
            return InvalidTable;
        }

        return _table->GetTableId(created);
    }

    public int Find(string name)
    {
        if (!HasContainer || string.IsNullOrEmpty(name))
            return InvalidTable;

        void* table = FindTable(name);
        return table != null ? _table->GetTableId(table) : InvalidTable;
    }

    public int AddString(int tableId, string value)
    {
        if (!_isServer)
            return -1;

        void* table = Resolve(tableId, "add a string to");
        if (table == null)
            return -1;

        if (value is null)
        {
            _log.Warning("[SourceSharp] string table add: null string; refused\n");
            return -1;
        }

        int index;
        fixed (byte* p = Vtable.Utf8(value))
            index = _table->AddString(table, 1, p, -1, null);

        if (index == InvalidIndex)
        {
            _log.Warning($"[SourceSharp] string table '{NameOf(table)}' is full; the string was not added\n");
            return -1;
        }

        return index;
    }

    public int FindStringIndex(int tableId, string value)
    {
        void* table = Resolve(tableId, "look a string up in");
        if (table == null || value is null)
            return -1;

        int index;
        fixed (byte* p = Vtable.Utf8(value))
            index = _table->FindStringIndex(table, p);

        return index == InvalidIndex ? -1 : index;
    }

    public string? GetString(int tableId, int stringIndex)
    {
        void* table = Resolve(tableId, "read a string from");
        if (table == null)
            return null;

        if (stringIndex < 0 || stringIndex >= _table->GetNumStrings(table))
            return null;

        byte* value = _table->GetString(table, stringIndex);
        if (value == null)
            return null;

        int length = 0;
        while (value[length] != 0)
            length++;

        if (length + 1 > MaxEntryBytes)
        {
            _log.Warning($"[SourceSharp] string table '{NameOf(table)}': an entry does not fit the caller's buffer and was NOT truncated\n");
            return null;
        }

        return Marshal.PtrToStringUTF8((nint)value, length);
    }

    public int GetNumStrings(int tableId)
    {
        void* table = Resolve(tableId, "count the strings in");
        return table == null ? -1 : _table->GetNumStrings(table);
    }

    private void* FindTable(string name)
    {
        fixed (byte* p = Vtable.Utf8(name))
            return _fwd->FindTable(_self, p);
    }

    private void* Resolve(int tableId, string what)
    {
        if (!HasContainer)
        {
            _log.Warning("[SourceSharp] string table: no container; refused\n");
            return null;
        }

        if (tableId == InvalidTable)
            return null;

        void* table = _fwd->GetTable(_self, tableId);
        if (table == null)
        {
            _log.Warning($"[SourceSharp] string table: cannot {what} a table id the engine does not know. A id kept across a level change or a "
                       + "managed reload is stale -- ask for it again by name.\n");
        }

        return table;
    }

    private string NameOf(void* table) => Vtable.TextOf(_table->GetTableName(table), 64) ?? string.Empty;
}

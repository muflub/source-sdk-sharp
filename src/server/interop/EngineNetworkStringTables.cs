using System;

namespace SourceSharp.Abi;

/// <summary>
/// The engine's string tables, by name: <c>INetworkStringTableContainer</c>
/// (<c>VEngineServerStringTable001</c>) called through its vtable.
///
/// <para>
/// NOT NAMED <c>IEngine*</c>, deliberately: EngineServices.IsArea treats every
/// <c>IEngine*</c> interface in this assembly as an area and builds
/// <c>Engine&lt;rest&gt;</c> parameterless for it. These are engine objects a
/// composition root acquires, not areas.
/// </para>
/// </summary>
public interface INetworkStringTableDirectory
{
    /// <summary>
    /// The table the engine holds under <paramref name="name"/>, or null when it
    /// holds none (the engine creates its tables in the one window it allows).
    /// </summary>
    INetworkStringTableRef? Find(string name);
}

/// <summary>One engine string table, called through its own vtable.</summary>
public interface INetworkStringTableRef
{
    /// <summary>INVALID_STRING_INDEX: <c>(unsigned short)~0</c>, the engine's "no such string".</summary>
    public const int InvalidStringIndex = 0xFFFF;

    /// <summary>The name the engine gave the table.</summary>
    string Name { get; }

    /// <summary>
    /// Adds <paramref name="value"/> with <paramref name="userData"/> as its
    /// user data (bytes, sent as they are), or updates the user data of the
    /// string already there. Answers the string's index, or
    /// <see cref="InvalidStringIndex"/> when the table is full.
    /// </summary>
    int AddString(string value, ReadOnlySpan<byte> userData);

    /// <summary>The index of <paramref name="value"/>, or <see cref="InvalidStringIndex"/>.</summary>
    int FindStringIndex(string value);
}

/// <summary>
/// <see cref="INetworkStringTableDirectory"/> over the engine's container.
/// </summary>
public sealed unsafe class EngineStringTableDirectory : INetworkStringTableDirectory
{
    /// <summary>The server's factory name for the container.</summary>
    public const string ServerInterfaceVersion = "VEngineServerStringTable001";

    private readonly void* _self;
    private readonly NetworkStringTableContainerFwd* _fwd;

    // The forwarders of the INetworkStringTable objects FindTable answers.
    private readonly NetworkStringTableFwd* _tables;
    private readonly bool _isServer;

    private EngineStringTableDirectory(void* self, NetworkStringTableContainerFwd* table,
                                       NetworkStringTableFwd* tables, bool isServer)
    {
        _self = self;
        _fwd = table;
        _tables = tables;
        _isServer = isServer;
    }

    /// <summary>
    /// The server's container, through <paramref name="engine"/>'s own side
    /// (plan_abi_hotreload D8), or null with the reason.
    /// </summary>
    public static INetworkStringTableDirectory? AcquireServer(ModuleEngine engine, out string why)
        => Forwarders.TryAcquire<NetworkStringTableContainerFwd>(
               (engine ?? throw new ArgumentNullException(nameof(engine))).Interfaces,
               SourceSharpFactoryType.AppSystem, ServerInterfaceVersion, out void* self, out NetworkStringTableContainerFwd* table, out why)
           && Forwarders.TryAcquire(engine.Interfaces, out NetworkStringTableFwd* tables, out why)
            ? new EngineStringTableDirectory(self, table, tables, isServer: true)
            : null;

    /// <inheritdoc/>
    public INetworkStringTableRef? Find(string name)
    {
        byte[] bytes = Vtable.Utf8(name);
        void* found;

        fixed (byte* p = bytes)
            found = _fwd->FindTable(_self, p);

        return found == null ? null : new EngineNetworkStringTable(found, _tables, _isServer);
    }
}

/// <summary>
/// One engine string table, reached through <c>FindTable</c>.
///
/// <para>
/// Called through <see cref="NetworkStringTableFwd"/>, the forwarders the
/// directory acquired with its container: the table object is handed to them,
/// and its vtable is the compiler's business, not this class's.
/// </para>
/// </summary>
internal sealed unsafe class EngineNetworkStringTable : INetworkStringTableRef
{
    private const int MaxTableName = 64;

    private readonly void* _self;
    private readonly NetworkStringTableFwd* _fwd;
    private readonly bool _isServer;

    internal EngineNetworkStringTable(void* self, NetworkStringTableFwd* fwd, bool isServer)
    {
        _self = self;
        _fwd = fwd;
        _isServer = isServer;
    }

    /// <inheritdoc/>
    public string Name => Vtable.TextOf(_fwd->GetTableName(_self), MaxTableName) ?? string.Empty;

    /// <inheritdoc/>
    public int AddString(string value, ReadOnlySpan<byte> userData)
    {
        byte[] text = Vtable.Utf8(value);

        fixed (byte* p = text)
        fixed (byte* data = userData)
            return _fwd->AddString(_self, (byte)(_isServer ? 1 : 0), p, userData.Length,
                                    userData.Length == 0 ? null : data);
    }

    /// <inheritdoc/>
    public int FindStringIndex(string value)
    {
        byte[] text = Vtable.Utf8(value);

        fixed (byte* p = text)
            return _fwd->FindStringIndex(_self, p);
    }
}

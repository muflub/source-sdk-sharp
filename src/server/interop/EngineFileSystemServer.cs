namespace SourceSharp.Abi;

/// <summary>
/// <c>IFileSystem</c>'s own methods, which its second base
/// <c>IBaseFileSystem</c> (<see cref="IFwdBaseFileSystem"/>) does not
/// declare: what the managed SoundEmitterSystem's -makereslists precache log
/// needs to make its directory (plan_server_native_v2 T4a, the precache path).
/// Server.Interop's: the log is the server's alone.
/// </summary>
public interface IFwdFileSystem
{
    /// <summary><c>CreateDirHierarchy( path, pathId )</c>: the directory and every parent it lacks.</summary>
    void CreateDirHierarchy(string path, string pathId);
}

/// <summary><see cref="IFwdFileSystem"/> over the engine's object, through its forwarders.</summary>
public sealed unsafe class EngineFileSystemServer : IFwdFileSystem
{
    /// <summary><c>FILESYSTEM_INTERFACE_VERSION</c> (filesystem.h): the whole object, not its IBaseFileSystem half.</summary>
    public const string InterfaceVersion = "VFileSystem022";

    private readonly void* _self;
    private readonly FileSystemFwd* _fwd;

    internal EngineFileSystemServer(void* self, FileSystemFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>The engine's filesystem, from the FILESYSTEM factory, or null with the reason.</summary>
    public static IFwdFileSystem? Acquire(ModuleEngine engine, out string why)
        => Acquire((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, out why);

    /// <summary>The same, through one side's own interface factory.</summary>
    internal static IFwdFileSystem? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire<FileSystemFwd>(factory, SourceSharpFactoryType.FileSystem,
               InterfaceVersion, out void* self, out FileSystemFwd* table, out why)
            ? new EngineFileSystemServer(self, table)
            : null;

    /// <inheritdoc/>
    public void CreateDirHierarchy(string path, string pathId)
    {
        fixed (byte* p = Vtable.Utf8(path))
        fixed (byte* i = Vtable.Utf8(pathId))
            _fwd->CreateDirHierarchy(_self, p, i);
    }
}

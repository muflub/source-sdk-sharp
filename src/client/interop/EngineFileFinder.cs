namespace SourceSharp.Abi;

/// <summary>
/// The engine filesystem's wildcard search (IFileSystem's FindFirstEx, FindNext,
/// FindIsDirectory and FindClose), as the client's options list files: the
/// player models and spray logos (plan_gameui_managed.md G2) and the maps.
/// </summary>
public interface IFwdFileFinder
{
    /// <summary>
    /// The names a wildcard finds on a search path (no directory part, as the
    /// engine answers them; null searches every path): files, or directories
    /// when <paramref name="directories"/>; never <c>.</c> or <c>..</c>.
    /// </summary>
    List<string> Find(string wildcard, string? pathId, bool directories = false);
}

/// <summary><see cref="IFwdFileFinder"/> over the client's IFileSystem, through its forwarders.</summary>
public sealed unsafe class EngineFileFinder : IFwdFileFinder
{
    /// <summary><c>FILESYSTEM_INTERFACE_VERSION</c> (filesystem.h).</summary>
    public const string InterfaceVersion = "VFileSystem022";

    private const int MaxName = 260;

    private readonly void* _self;
    private readonly FileSystemFwd* _fwd;

    private EngineFileFinder(void* self, FileSystemFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>The engine's filesystem, from the FILESYSTEM factory, or null with the reason.</summary>
    internal static IFwdFileFinder? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire<FileSystemFwd>(factory, SourceSharpFactoryType.FileSystem,
               InterfaceVersion, out void* self, out FileSystemFwd* table, out why)
            ? new EngineFileFinder(self, table)
            : null;

    /// <inheritdoc/>
    public List<string> Find(string wildcard, string? pathId, bool directories = false)
    {
        var found = new List<string>();
        int handle = 0;
        fixed (byte* w = Vtable.Utf8(wildcard))
        fixed (byte* p = pathId is null ? null : Vtable.Utf8(pathId))
        {
            byte* name = _fwd->FindFirstEx(_self, w, p, &handle);
            while (name != null)
            {
                string text = Vtable.TextOf(name, MaxName) ?? "";
                if (text is not ("." or "..") && (_fwd->FindIsDirectory(_self, handle) != 0) == directories)
                    found.Add(text);
                name = _fwd->FindNext(_self, handle);
            }
        }
        _fwd->FindClose(_self, handle);
        return found;
    }
}

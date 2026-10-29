namespace SourceSharp.Abi;

//=============================================================================
// IBaseFileSystem, AND THE `this` ADJUSTMENT THAT IS NO LONGER ANYBODY'S
// PROBLEM.
//
// IBaseFileSystem is a SECOND base of CFileSystem_Stdio, so its subobject does
// not sit at the top of the object and a method inherited from it dispatches
// through a secondary vtable at a non-zero offset. An earlier revision of
// docs/vtable-binding.md called that a refusal -- "the compiler is not here to
// compute that offset, so a multiple-inheritance interface is a guess". Then it
// stopped being a refusal and became four agreeing derivations of the number 8,
// re-confirmed on every acquire.
//
// IT IS NOW NEITHER. Native asks for "VBaseFileSystem011" -- the interface's own
// factory name -- and the module hands back the subobject pointer with the
// adjustment already applied, by name rather than by arithmetic. Where native
// does need the upcast it writes `static_cast<IBaseFileSystem *>( filesystem )`
// and the compiler emits the same adjustment it would emit at any other call
// site.
//
// So there is no offset in this file, no confirmation of one, and nothing that
// could be off by eight. That is the shape of the whole refactor in one
// interface: the safest way to be right about a number is for no number to be
// there.
//
//-----------------------------------------------------------------------------
// AND THE OVERLOAD TRAP IS STILL REAL. `IBaseFileSystem` declares Size TWICE --
// `Size( FileHandle_t )` at slot 6 and `Size( const char *, const char * )` at
// slot 7 -- so the generated forwarder struct has `Size` and `Size_2`, IN THAT
// ORDER, and the one this file wants is the SECOND. Calling `Size` would hand a
// filename to a method expecting a file handle. The field's trailing comment in
// SharedForwarders.g.cs carries the parameter list that tells them apart, and
// tools/vtable-layout --self-test has an arm on the ordering.
//=============================================================================

/// <summary>
/// Game files: their size, time and existence by name, and their bytes, read
/// through a file handle this class opens and closes within one call (the
/// user's 2026-10-19 ruling exposed the handles; none outlives a call).
/// </summary>
public interface IFwdBaseFileSystem
{
    /// <summary>
    /// The whole of a file on a search path, or null when it is not there:
    /// Open("rb"), Size, Read, Close. A short read answers the bytes read, which
    /// is a real answer. What the FilesReadGameFile / FilesReadOnPath slots did,
    /// without the slot.
    /// </summary>
    byte[]? ReadAll(string fileName, string pathId);

    /// <summary>
    /// The size in bytes of a file on a search path, or 0 when it is not there.
    ///
    /// <para>
    /// 0 for missing is the filesystem's own answer and is deliberately not
    /// folded into -1 here: <c>shell_files.cpp</c> already records that this
    /// method cannot tell an empty file from an absent one, which is why the
    /// ABI slot goes through Open/Size/Close instead.
    /// </para>
    /// </summary>
    uint FileSize(string fileName, string pathId);

    /// <summary>
    /// Whether a file is on a search path at all.
    ///
    /// <para>
    /// <b>It is here because <see cref="FileSize"/> cannot answer the question
    /// alone</b> — plan_native_api.md Phase 3. Size answers 0 both for a file
    /// that is empty and for one that is not there, and the whole reason
    /// shell_files.cpp went through Open/Size/Close was to tell those apart.
    /// The pair does it without a handle: exists-then-size, two reads, no
    /// <c>FileHandle_t</c> for managed code to own across a GC.
    /// </para>
    /// </summary>
    bool FileExists(string fileName, string? pathId);

    /// <summary>
    /// <c>Open( name, options, pathId )</c>: a <c>FileHandle_t</c>, or 0
    /// (<c>FILESYSTEM_INVALID_HANDLE</c>) when the file could not be opened.
    /// The caller owns the handle and closes it (<see cref="Close"/>): the
    /// user's 2026-10-19 ruling exposed the handles to managed code.
    /// plan_server_native_v2 T4a's precache log is the one that outlives a
    /// call, opened at a level's start and closed at its end.
    /// </summary>
    nint Open(string fileName, string options, string pathId);

    /// <summary><c>Write( bytes, size, handle )</c>: the bytes written.</summary>
    int Write(ReadOnlySpan<byte> bytes, nint handle);

    /// <summary><c>Close( handle )</c>.</summary>
    void Close(nint handle);

    // NOTHING ELSE THAT WRITES. Seek, Tell, Flush, Precache, IsFileWritable,
    // SetFileWritable, ReadFile, WriteFile and UnzipFile are typed in the
    // struct and none of them is here: SetFileWritable chmods a file while
    // WriteFile creates one. The absence IS the decision; there is no table
    // recording it.
}

/// <summary>
/// <see cref="IFwdBaseFileSystem"/> over the engine's own object, called
/// through its vtable.
/// </summary>
public sealed unsafe class EngineBaseFileSystem : IFwdBaseFileSystem
{
    /// <summary>
    /// <c>IBaseFileSystem</c>'s OWN factory name, which is the whole of the
    /// multiple-inheritance solve: the module answers it with the subobject
    /// pointer, so nothing here adds eight to anything.
    /// </summary>
    public const string InterfaceVersion = "VBaseFileSystem011";

    private readonly void* _self;
    private readonly BaseFileSystemFwd* _fwd;

    private EngineBaseFileSystem(void* self, BaseFileSystemFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>
    /// The engine's base filesystem, or null with the reason.
    ///
    /// <para>
    /// THE ONLY ONE OF THE FIVE THAT NAMES A FACTORY KIND. The other four come
    /// from the engine's appSystem factory; this one comes from the FILESYSTEM
    /// role, which is a different factory on the server and the same one on the
    /// client — a difference native resolves and no caller has to know.
    /// </para>
    ///
    /// <para>
    /// Through the side's own interface factory
    /// (plan_abi_hotreload D8), never the process-wide one that followed the
    /// thread's active side.
    /// </para>
    /// </summary>
    internal static IFwdBaseFileSystem? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire<BaseFileSystemFwd>(
               factory, SourceSharpFactoryType.FileSystem,
               InterfaceVersion, out void* self, out BaseFileSystemFwd* table, out why)
            ? new EngineBaseFileSystem(self, table)
            : null;

    /// <inheritdoc/>
    public bool FileExists(string fileName, string? pathId)
    {
        byte[] name = Vtable.Utf8(fileName);

        // A null path id is every search path (stock's default argument): the
        // engine is handed NULL, not an empty name.
        byte[]? path = pathId is null ? null : Vtable.Utf8(pathId);

        fixed (byte* n = name)
        fixed (byte* p = path)
            return _fwd->FileExists(_self, n, p) != 0;
    }

    /// <inheritdoc/>
    public uint FileSize(string fileName, string pathId)
    {
        byte[] name = Vtable.Utf8(fileName);
        byte[] path = Vtable.Utf8(pathId);

        // Size_2 AND NOT Size. The first overload takes a FileHandle_t.
        fixed (byte* n = name)
        fixed (byte* p = path)
            return _fwd->Size_2(_self, n, p);
    }

    /// <inheritdoc/>
    public byte[]? ReadAll(string fileName, string pathId)
    {
        void* handle;
        fixed (byte* n = Vtable.Utf8(fileName))
        fixed (byte* m = Vtable.Utf8("rb"))
        fixed (byte* p = Vtable.Utf8(pathId))
            handle = _fwd->Open(_self, n, m, p);

        if (handle == null)
            return null;

        try
        {
            uint size = _fwd->Size(_self, handle);
            var bytes = new byte[size];
            if (size == 0)
                return bytes;

            int read;
            fixed (byte* b = bytes)
                read = _fwd->Read(_self, b, (int)size, handle);

            return read < 0 ? null : read == bytes.Length ? bytes : bytes[..read];
        }
        finally
        {
            _fwd->Close(_self, handle);
        }
    }

    /// <inheritdoc/>
    public nint Open(string fileName, string options, string pathId)
    {
        fixed (byte* n = Vtable.Utf8(fileName))
        fixed (byte* m = Vtable.Utf8(options))
        fixed (byte* p = Vtable.Utf8(pathId))
            return (nint)_fwd->Open(_self, n, m, p);
    }

    /// <inheritdoc/>
    public int Write(ReadOnlySpan<byte> bytes, nint handle)
    {
        if (handle == 0)
            return 0;

        fixed (byte* b = bytes)
            return _fwd->Write(_self, b, bytes.Length, (void*)handle);
    }

    /// <inheritdoc/>
    public void Close(nint handle)
    {
        if (handle != 0)
            _fwd->Close(_self, (void*)handle);
    }
}

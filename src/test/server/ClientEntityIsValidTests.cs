using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client answers "is this handle alive" from its own entity list.
///
/// <para>
/// It used not to. EntityIsValid lived in the server-only IShimEntitiesServer
/// area, so on the client <c>Has_EntityIsValid</c> was false and every ask
/// answered false. The one client caller is a restore: <c>SavedHandles.Resolve</c>
/// re-checks every handle a <c>cs_cl_reload</c> carries across, so every
/// restored <see cref="EntityHandle"/> field came back <see cref="EntityHandle.None"/>.
/// SideAuditTests' transitive walk found it (lane side-audit, 2026-10-02).
/// </para>
///
/// <para>
/// <b>THE SLOT SUPPRESSES THE GC TRANSITION, so no managed fake can serve it</b>
/// (calling an <c>[UnmanagedCallersOnly]</c> method that way kills the test
/// host). Each fact serves its own entities area through a
/// <see cref="FakeShimRoot"/> with a real native function in the slot instead:
/// libc's <c>getpagesize</c>, which answers non-zero ("alive"), or
/// <c>sched_yield</c>, which answers 0 ("gone"). Both ignore their arguments,
/// so the answer can only have come from the slot the fact filled, and the
/// pair is the control: the same handle, kept or dropped by the slot's answer
/// alone. The table is laid out as the server's area, a superset of the shared
/// base with EntityIsValid at the same index. Nothing here is static.
/// </para>
/// </summary>
public unsafe class ClientEntityIsValidTests : IDisposable
{
    private static readonly EntityHandle Saved = new(0x0012_8005u);

    private readonly FakeShimRoot _root = new();
    private readonly List<nint> _blocks = new();

    public void Dispose()
    {
        _root.Dispose();
        foreach (nint block in _blocks)
            NativeMemory.Free((void*)block);
    }

    [Fact]
    public void AServerSideStillAsksItsSlot()
    {
        // The retention half: the server's route must survive the move.
        Serve("SourceSharpEntitiesServer001", Alive);
        var alive = Attached(Side.Server);
        Assert.True(alive.EntityIsValid(Saved), "the server answers alive when its slot does");

        using var other = new FakeShimRoot();
        var gone = new EngineSide(Side.Server);
        gone.Initialize();
        var (goneSelf, goneSlots) = Area(Gone);
        other.Area("SourceSharpEntitiesServer001", (void*)goneSelf, (void*)goneSlots);
        Assert.True(gone.AttachShims(other.Bind(), out string why), why);
        Assert.False(gone.EntityIsValid(Saved), "and gone when its slot does");
    }

    //-------------------------------------------------------------------------

    // TWO NATIVE FUNCTIONS THAT IGNORE THEIR ARGUMENTS, one answering non-zero
    // and one 0, borrowed from the C library as the slot's implementation. Not
    // [UnmanagedCallersOnly] methods: the slot is [SuppressGCTransition], and a
    // call made that way may not enter managed code.
    //
    // Linux: libc's getpagesize (the page size) and sched_yield (0). Windows:
    // the ucrt's _getmaxstdio (512 unless a process raises it) and
    // _get_printf_count_output (0 unless a process enables %n).
    private static readonly string Library = OperatingSystem.IsWindows() ? "ucrtbase.dll" : "libc.so.6";

    /// <summary>Ignores its arguments and answers non-zero.</summary>
    private static readonly string Alive = OperatingSystem.IsWindows() ? "_getmaxstdio" : "getpagesize";

    /// <summary>Ignores its arguments and answers 0.</summary>
    private static readonly string Gone = OperatingSystem.IsWindows() ? "_get_printf_count_output" : "sched_yield";

    /// <summary>Serves an entities area for <paramref name="version"/> whose EntityIsValid slot is <paramref name="answer"/>.</summary>
    private void Serve(string version, string answer)
    {
        var (self, slots) = Area(answer);
        _root.Area(version, (void*)self, (void*)slots);
    }

    /// <summary>An object, and a forwarder slot array whose EntityIsValid slot is <paramref name="answer"/>.</summary>
    private (nint Self, nint Slots) Area(string answer)
    {
        Type vtbl = typeof(ShimEntitiesServerFwd);
        int words = Marshal.SizeOf(vtbl) / sizeof(nint);
        var table = (nint*)Alloc(words * sizeof(nint));
        var self = (nint*)Alloc(sizeof(nint));

        int slot = (int)Marshal.OffsetOf(vtbl, nameof(ShimEntitiesServerFwd.EntityIsValid)) / sizeof(nint);
        nint libc = NativeLibrary.Load(Library);
        table[slot] = NativeLibrary.GetExport(libc, answer);
        return ((nint)self, (nint)table);
    }

    private EngineSide Attached(Side side)
    {
        var api = new EngineSide(side);
        api.Initialize();

        string why;
        bool attached = side == Side.Server
            ? api.AttachShims(_root.Bind(), out why)
            : api.AttachClientShims(_root.Bind(), out why);

        Assert.True(attached, why);
        return api;
    }

    private void* Alloc(int bytes)
    {
        void* p = NativeMemory.AllocZeroed((nuint)bytes);
        _blocks.Add((nint)p);
        return p;
    }
}

using System.Runtime.InteropServices;
using System.Text;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A game script is readable from BOTH sides (lane c2-sims, plan_client_novalve
/// C2). FilesGameFileSize and FilesReadGameFile were IShimEngineServer's, so on
/// the client <c>Has_FilesReadGameFile</c> was false and every read answered
/// null; the client's decal table and soundscapes are managed now and begin with
/// one. Each fact serves its own IShimEngine object through a
/// <see cref="FakeShimRoot"/>: word 0 the vtable, word 1 the bytes it serves.
/// </summary>
public unsafe class GameFileSideTests : IDisposable
{
    private const string Version = "SourceSharpEngine001";

    private readonly FakeShimRoot _root = new();
    private readonly List<nint> _blocks = new();

    public void Dispose()
    {
        _root.Dispose();
        foreach (nint block in _blocks)
            NativeMemory.Free((void*)block);
    }

    [Fact]
    public void AServerSideStillReadsAGameFileThroughItsShimRoot()
    {
        // The retention half: the server's route must survive the move.
        Serve("server text");
        var engine = Attached(Side.Server);

        Assert.True(SideBindings.Of<IEngineFiles>(engine).ReadGameFile("scripts/propdata.txt") == "server text",
            "the server side reads the served script through IShimEngine");
    }

    [Fact]
    public void ASideWithNoShimRootReadsNothing()
    {
        var engine = EngineSide.Create(Side.Client);
        engine.Initialize();

        Assert.True(SideBindings.Of<IEngineFiles>(engine).ReadGameFile("scripts/decals_subrect.txt") is null,
            "a side that cannot serve the slot answers null, not an empty script");
    }

    //-------------------------------------------------------------------------

    private void Serve(string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        int words = Marshal.SizeOf<ShimEngineFwd>() / sizeof(nint);
        var table = (nint*)Alloc(words * sizeof(nint));
        var content = (byte*)Alloc(utf8.Length + 4);
        *(int*)content = utf8.Length;
        utf8.CopyTo(new Span<byte>(content + 4, utf8.Length));
        var self = (nint*)Alloc(2 * sizeof(nint));

        table[(int)Marshal.OffsetOf<ShimEngineFwd>(nameof(ShimEngineFwd.FilesGameFileSize)) / sizeof(nint)]
            = (nint)(delegate* unmanaged<void*, byte*, int>)&Size;
        table[(int)Marshal.OffsetOf<ShimEngineFwd>(nameof(ShimEngineFwd.FilesReadGameFile)) / sizeof(nint)]
            = (nint)(delegate* unmanaged<void*, byte*, byte*, int, int>)&Read;
        self[1] = (nint)content;

        _root.Area(Version, self, table);
    }

    private EngineSide Attached(Side side)
    {
        var api = EngineSide.Create(side);
        api.Initialize();

        string why;
        bool attached = api.AttachOwnShims(_root.Bind(), out why);

        Assert.True(attached, why);
        return api;
    }

    private void* Alloc(int bytes)
    {
        void* p = NativeMemory.AllocZeroed((nuint)bytes);
        _blocks.Add((nint)p);
        return p;
    }

    [UnmanagedCallersOnly]
    private static int Size(void* self, byte* path)
        => *(int*)((nint*)self)[1];

    [UnmanagedCallersOnly]
    private static int Read(void* self, byte* path, byte* pOut, int nOutSize)
    {
        var content = (byte*)((nint*)self)[1];
        int n = *(int*)content;
        if (n > nOutSize)
            return -1;
        new ReadOnlySpan<byte>(content + 4, n).CopyTo(new Span<byte>(pOut, nOutSize));
        return n;
    }
}

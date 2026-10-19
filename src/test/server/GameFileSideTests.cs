using System.Runtime.InteropServices;
using System.Text;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A game script is readable from BOTH sides (lane c2-sims, plan_client_novalve
/// C2), and since the user's 2026-10-19 ruling exposed file handles to managed
/// it is read through VBaseFileSystem011's forwarders: Open("rb"), Size, Read,
/// Close in one call (EngineBaseFileSystem.ReadAll), where the FilesGameFileSize /
/// FilesReadGameFile / FilesSizeOnPath / FilesReadOnPath shim slots used to be.
/// Each fact serves one file through a <see cref="FakeShimRoot"/>'s forwarders.
/// </summary>
public unsafe class GameFileSideTests : IDisposable
{
    private static byte[] s_Content = [];
    private static string? s_Served;
    private static string? s_PathId, s_Mode;
    private static int s_Opens, s_Closes;
    private static readonly nint Handle = 0x4440;

    private readonly FakeShimRoot _root = new();
    private readonly nint* _self = (nint*)NativeMemory.AllocZeroed(1, (nuint)sizeof(nint));

    public GameFileSideTests()
    {
        s_Opens = s_Closes = 0;
        s_Served = null;
        s_PathId = s_Mode = null;
    }

    public void Dispose()
    {
        _root.Dispose();
        NativeMemory.Free(_self);
    }

    [UnmanagedCallersOnly]
    private static void* Open(void* self, byte* name, byte* mode, byte* pathId)
    {
        s_Opens++;
        s_Mode = Marshal.PtrToStringUTF8((nint)mode);
        s_PathId = Marshal.PtrToStringUTF8((nint)pathId);
        return Marshal.PtrToStringUTF8((nint)name) == s_Served ? (void*)Handle : null;
    }

    [UnmanagedCallersOnly]
    private static uint Size(void* self, void* handle) => (uint)s_Content.Length;

    [UnmanagedCallersOnly]
    private static int Read(void* self, void* output, int size, void* handle)
    {
        int n = Math.Min(size, s_Content.Length);
        s_Content.AsSpan(0, n).CopyTo(new Span<byte>(output, n));
        return n;
    }

    [UnmanagedCallersOnly]
    private static void Close(void* self, void* handle) => s_Closes++;

    [Fact]
    public void AClientSideReadsAGameFileThroughItsFileSystem()
    {
        Serve("scripts/decals_subrect.txt", "\"decals\" { \"a\" \"1\" }");

        Assert.Equal("\"decals\" { \"a\" \"1\" }", Files(Side.Client).ReadGameFile("scripts/decals_subrect.txt"));
        Assert.Equal(("rb", "GAME"), (s_Mode, s_PathId));
    }

    [Fact]
    public void AServerSideStillReadsAGameFileThroughItsFileSystem()
    {
        // The retention half: the server's route must survive the move.
        Serve("scripts/propdata.txt", "server text");

        Assert.Equal("server text", Files(Side.Server).ReadGameFile("scripts/propdata.txt"));
    }

    [Fact]
    public void AFileOnANamedPathIsReadOnThatPath()
    {
        Serve("maps/x_particles.txt", "bsp text");

        Assert.Equal("bsp text"u8.ToArray(), Files(Side.Client).ReadFileOnPath("maps/x_particles.txt", "BSP"));
        Assert.Equal("BSP", s_PathId);
    }

    [Fact]
    public void AMissingFileIsNullAndOpensNothingToClose()
    {
        Serve("scripts/other.txt", "x");

        Assert.Null(Files(Side.Server).ReadGameFile("scripts/missing.txt"));
        Assert.Equal((1, 0), (s_Opens, s_Closes));
    }

    [Fact]
    public void EveryHandleOpenedIsClosedWithinTheCall()
    {
        Serve("scripts/a.txt", "abc");
        var files = Files(Side.Server);

        files.ReadGameFile("scripts/a.txt");
        files.ReadGameFileBytes("scripts/a.txt");

        Assert.Equal((2, 2), (s_Opens, s_Closes));
    }

    [Fact]
    public void AnEmptyFileIsEmptyAndNotMissing()
    {
        Serve("scripts/empty.txt", "");

        Assert.Equal(string.Empty, Files(Side.Client).ReadGameFile("scripts/empty.txt"));
    }

    //-------------------------------------------------------------------------

    private void Serve(string name, string text)
    {
        s_Served = name;
        s_Content = Encoding.UTF8.GetBytes(text);
        _root.Interface(EngineBaseFileSystem.InterfaceVersion, _self).Forwarders(out BaseFileSystemFwd* fwd);
        fwd->Open = &Open;
        fwd->Size = &Size;
        fwd->Read = &Read;
        fwd->Close = &Close;
    }

    private IEngineFiles Files(Side side)
    {
        var api = new EngineSide(side);
        api.Initialize();

        string why;
        bool attached = api.AttachOwnShims(_root.Bind(), out why);

        Assert.True(attached, why);
        return SideBindings.Of<IEngineFiles>(api);
    }
}

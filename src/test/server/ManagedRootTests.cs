using System.Runtime.InteropServices;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;
using SourceSharp.Shared.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables D4: the managed root native acquires managed-built
/// interfaces from, called through its own generated table as native calls it.
/// </summary>
public unsafe class ManagedRootTests
{
    // A stand-in managed-built object: Release and Validate slots, never called here.
    private static ManagedVtable Stand(ManagedVtables vtables, string name)
        => vtables.Create<object>(name, new object(), [(nint)1, (nint)2], [0u, 0u]);

    private static nint AcquireThroughTheTable(ManagedVtable root, string name)
    {
        var self = (nint*)root.Pointer;
        var acquire = (delegate* unmanaged<void*, byte*, void*>)((nint*)self[0])[2];   // after Release, Validate
        byte[] utf8 = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = utf8)
            return (nint)acquire(self, p);
    }

    private static int ValidateThroughTheTable(ManagedVtable root, uint[] hashes, int count)
    {
        var self = (nint*)root.Pointer;
        var validate = (delegate* unmanaged<void*, void*, int, int>)((nint*)self[0])[1];
        fixed (uint* p = hashes)
            return validate(self, p, count);
    }

    // The root's layout hashes as the generated layout carries them: native
    // compiles the same numbers into kIManagedRootSlotHashes.
    private static uint[] RootHashes()
        => typeof(ManagedRootTable).GetFields()
               .Select(f => ((SlotHashAttribute)f.GetCustomAttributes(typeof(SlotHashAttribute), false).Single()).Hash)
               .ToArray();

    [Fact]
    public void TheRootIsReleaseValidateAcquireLikeItsNativeDeclaration()
    {
        using var vtables = new ManagedVtables();
        Assert.Equal(3, new ManagedRoot(new ManagedBindings(), vtables).Bind().Entries);
    }

    [Fact]
    public void ANamedInterfaceComesBack()
    {
        using var vtables = new ManagedVtables();
        var bindings = new ManagedBindings();
        bindings.Add("SourceSharpProbe001", v => Stand(v, "SourceSharpProbe001"));
        var root = new ManagedRoot(bindings, vtables).Bind();

        nint acquired = AcquireThroughTheTable(root, "SourceSharpProbe001");

        Assert.NotEqual(0, acquired);
        Assert.Equal((nint)bindings.Acquire("SourceSharpProbe001", vtables)!.Pointer, acquired);
    }

    [Fact]
    public void AnUnknownNameIsNull()
    {
        using var vtables = new ManagedVtables();
        var bindings = new ManagedBindings();
        bindings.Add("SourceSharpProbe001", v => Stand(v, "SourceSharpProbe001"));

        Assert.Equal(0, AcquireThroughTheTable(new ManagedRoot(bindings, vtables).Bind(), "SourceSharpOther001"));
    }

    [Fact]
    public void TheRootValidatesAgainstItsOwnLayoutHashes()
    {
        using var vtables = new ManagedVtables();
        uint[] hashes = RootHashes();
        Assert.Equal(3, hashes.Length);

        Assert.Equal(-1, ValidateThroughTheTable(new ManagedRoot(new ManagedBindings(), vtables).Bind(), hashes, hashes.Length));
    }

    [Fact]
    public void TheRootNamesTheFirstSlotWhoseHashDiffers()
    {
        using var vtables = new ManagedVtables();
        uint[] hashes = RootHashes();
        hashes[2] ^= 1;

        Assert.Equal(2, ValidateThroughTheTable(new ManagedRoot(new ManagedBindings(), vtables).Bind(), hashes, hashes.Length));
    }

    [Fact]
    public void TheRootIsRegisteredUnderItsNativeName()
    {
        using var vtables = new ManagedVtables();
        new ManagedRoot(new ManagedBindings(), vtables).Bind();

        Assert.Equal(RootHashes(), vtables.HashesOf("IManagedRoot"));
    }

    [Fact]
    public void AnInterfaceIsBuiltOnceAndHandedOutAgain()
    {
        using var vtables = new ManagedVtables();
        var bindings = new ManagedBindings();
        int built = 0;
        bindings.Add("SourceSharpProbe001", v => { built++; return Stand(v, "SourceSharpProbe001"); });

        var first = bindings.Acquire("SourceSharpProbe001", vtables);
        var second = bindings.Acquire("SourceSharpProbe001", vtables);

        Assert.Same(first, second);
        Assert.Equal(1, built);
    }

    [Fact]
    public void TheNetCatchesWhatNativeLeftAndSkipsWhatItReleased()
    {
        using var vtables = new ManagedVtables();
        var bindings = new ManagedBindings();
        bindings.Add("A", v => Stand(v, "A"));
        bindings.Add("B", v => Stand(v, "B"));
        var a = bindings.Acquire("A", vtables)!;
        var b = bindings.Acquire("B", vtables)!;
        a.Release();   // as if native released it

        Assert.Equal(1, vtables.FreeRemaining());
        Assert.True(a.Released && b.Released);
        Assert.Equal(0, vtables.Live);
    }
}

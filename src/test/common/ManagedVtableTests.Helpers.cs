using System.Runtime;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;
using SourceSharp.Shared.Interop;

namespace SourceSharp.Tests;

/// <summary>The members of <see cref="ManagedVtableTests"/> other test files use, linked into every test project that needs them (plan_side_isolation P6): the facts stay in one project.</summary>
public unsafe partial class ManagedVtableTests
{
    /// <summary>The root's Acquire slot, called as native calls it (for other facts).</summary>
    internal static nint AcquireForTest(ManagedVtable root, string name) => Acquire(root, name);

    private static nint Acquire(ManagedVtable root, string name)
    {
        var self = (nint*)root.Pointer;
        var acquire = (delegate* unmanaged<void*, byte*, void*>)((nint*)self[0])[2];   // after Release, Validate
        byte[] utf8 = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = utf8)
            return (nint)acquire(self, p);
    }
}

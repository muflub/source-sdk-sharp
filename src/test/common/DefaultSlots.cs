using System.Reflection;
using System.Runtime.InteropServices;
using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// What a fake root's unserved slot answers: its default, as the generated
/// default areas do (plan_shim_areas_split phase 11 review). Product code no
/// longer checks whether an engine is attached before it calls a slot, so a
/// fact that attaches a fake root and serves three slots must not crash the
/// test host on the fourth.
///
/// <para>
/// TWO NATIVE STUBS, NOT MANAGED ONES. Many slots are called
/// <c>unmanaged[SuppressGCTransition]</c>, which must never reach managed code,
/// so the stubs are a few bytes of x64 in executable memory: one returns zero
/// (in RAX and XMM0, so an integer, a pointer and a float all read zero), the
/// other -1 (the invalid entity handle, "no index"), for the slots the
/// generated defaults answer "nothing" for (<see cref="ShimAreaNothingSlots"/>).
/// They ignore their arguments and write through none of the pointers they are
/// handed, which the callers allocate zeroed. Elsewhere than x64 the slots stay
/// zeroed, as they were: those suites do not run there.
/// </para>
/// </summary>
internal static unsafe class DefaultSlots
{
    // xor eax, eax; pxor xmm0, xmm0; ret
    private static readonly byte[] ZeroCode = [0x31, 0xC0, 0x66, 0x0F, 0xEF, 0xC0, 0xC3];

    // mov eax, 0xFFFFFFFF; pxor xmm0, xmm0; ret
    private static readonly byte[] MinusOneCode = [0xB8, 0xFF, 0xFF, 0xFF, 0xFF, 0x66, 0x0F, 0xEF, 0xC0, 0xC3];

    // Allocated once for the process and never freed: two pages shared by
    // every fake root, read-only to them.
    private static readonly Lazy<(nint Zero, nint MinusOne)> Stubs = new(Make);

    /// <summary>Points every slot of <paramref name="fwd"/>'s table at its default stub.</summary>
    public static void Fill(Type fwd, nint* slots)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            return;

        var (zero, minusOne) = Stubs.Value;
        foreach (FieldInfo field in fwd.GetFields(BindingFlags.Instance | BindingFlags.Public))
            slots[(int)Marshal.OffsetOf(fwd, field.Name) / sizeof(nint)] =
                ShimAreaNothingSlots.Contains(field.Name) ? minusOne : zero;
    }

    private static (nint, nint) Make()
    {
        nint page = Executable(64);
        Marshal.Copy(ZeroCode, 0, page, ZeroCode.Length);
        Marshal.Copy(MinusOneCode, 0, page + 32, MinusOneCode.Length);
        return (page, page + 32);
    }

    private static nint Executable(nuint size)
    {
        if (OperatingSystem.IsWindows())
        {
            const uint MemCommitReserve = 0x3000, PageExecuteReadWrite = 0x40;
            nint p = VirtualAlloc(0, size, MemCommitReserve, PageExecuteReadWrite);
            return p != 0 ? p : throw new InvalidOperationException("VirtualAlloc refused an executable page");
        }

        const int ProtRwx = 0x7, MapPrivateAnonymous = 0x02 | (0x20);
        nint q = mmap(0, size, ProtRwx, OperatingSystem.IsMacOS() ? 0x02 | 0x1000 : MapPrivateAnonymous, -1, 0);
        return q != -1 ? q : throw new InvalidOperationException("mmap refused an executable page");
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern nint VirtualAlloc(nint address, nuint size, uint type, uint protect);

    [DllImport("libc", SetLastError = true)]
    private static extern nint mmap(nint address, nuint length, int prot, int flags, int fd, nint offset);
}

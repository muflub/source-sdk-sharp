using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SourceSharp.Abi;
using SourceSharp.Generators;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engineapi_vtables Phase D2: ManagedVtableGenerator, driven over source text.
/// Every rule is an ERROR, so its bad cases live in strings. Each comes as a PAIR
/// with <see cref="Good"/>, the declaration that must pass clean.
/// </summary>
public class ManagedVtableGeneratorTests
{
    private const string Layout = """
        [ImplementedByManaged("IProbe")]
        public unsafe struct ProbeVtbl
        {
            [SlotHash(0x00000010)]
            public delegate* unmanaged<void*, void> Release;
            [SlotHash(0x00000011)]
            public delegate* unmanaged<void*, void*, int, int> Validate;
            [SlotHash(0x00000012)]
            public delegate* unmanaged<void*, int, int> Add;
            [SlotHash(0x00000013)]
            public delegate* unmanaged<void*, byte*, int, void> Name;
        }
        """;

    private const string Contract = """
        [ManagedVtable(typeof(ProbeVtbl))]
        public unsafe interface IProbe
        {
            int Add(int a);
            void Name(byte* pOut, int nOutSize);
        }
        """;

    private const string Good = Layout + Contract;

    // A class the round trip builds the table around, and a caller that calls
    // through the table exactly as native does: object -> vtbl -> slot(self, ...).
    private const string Implementation = """
        public sealed unsafe class Probe : IProbe
        {
            public int Base = 40;
            public int Add(int a) => Base + a;
            public void Name(byte* pOut, int nOutSize) => throw new InvalidOperationException("thrown on purpose");
        }

        public static unsafe class NativeCaller
        {
            public static SourceSharp.Abi.ManagedVtable Build(IProbe target) => IProbe__ManagedVtable.Create(new SourceSharp.Abi.ManagedVtables(), target);

            private static nint Slot(nint self, int i) => ((nint*)((nint*)self)[0])[i];

            public static void Release(nint self)
                => ((delegate* unmanaged<void*, void>)Slot(self, 0))((void*)self);

            public static int Validate(nint self, uint[] hashes, int count)
            {
                fixed (uint* p = hashes)
                    return ((delegate* unmanaged<void*, void*, int, int>)Slot(self, 1))((void*)self, p, count);
            }

            public static int Add(nint self, int a)
                => ((delegate* unmanaged<void*, int, int>)Slot(self, 2))((void*)self, a);

            public static void Name(nint self)
            {
                byte b = 0;
                ((delegate* unmanaged<void*, byte*, int, void>)Slot(self, 3))((void*)self, &b, 1);
            }
        }
        """;

    // ---- what it writes ----------------------------------------------------

    [Fact]
    public void AGoodDeclarationIsAcceptedClean()
        => Assert.Empty(Diagnostics(Good));

    [Fact]
    public void ItWritesOneThunkPerSlotAndTheCountNativeChecks()
    {
        string written = Generated(Good);

        Assert.Contains("internal static unsafe class IProbe__ManagedVtable", written);
        Assert.Contains("public const int Entries = 4;", written);
        Assert.Equal(4, written.Split("[global::System.Runtime.InteropServices.UnmanagedCallersOnly]").Length - 1);
        Assert.Contains("ReleaseBlock<global::IProbe>(self)", written);
    }

    [Fact]
    public void EveryThunkCatchesRatherThanUnwindingIntoNative()
    {
        string written = Generated(Good);

        Assert.Equal(4, written.Split("catch (global::System.Exception e)").Length - 1);
        // L6: every thunk raises the depth first and lowers it in a finally.
        // The depth is the block's own registry's, handed from Enter to Leave
        // rather than read back from a block a Release has freed.
        Assert.Equal(4, written.Split("global::SourceSharp.Abi.ManagedVtables? owner = global::SourceSharp.Abi.ManagedVtable.Enter(self);").Length - 1);
        Assert.Equal(4, written.Split("finally\n        {\n            global::SourceSharp.Abi.ManagedVtable.Leave(owner);").Length - 1);
        Assert.Contains("Fail(owner?.Log, \"IProbe.Add\", e);", written);
    }

    /// <summary>The round trip: a call through the built table lands on the instance.</summary>
    [Fact]
    public unsafe void ACallThroughTheTableLandsOnTheInstance()
    {
        var (build, caller, probe) = RoundTrip();
        var vtable = (ManagedVtable)build.Invoke(null, [probe])!;

        Assert.Equal(4, vtable.Entries);
        Assert.Equal(42, (int)caller.GetMethod("Add")!.Invoke(null, [(nint)vtable.Pointer, 2])!);
    }

    [Fact]
    public unsafe void AnExceptionInTheContractDoesNotEscapeTheThunk()
    {
        var (build, caller, probe) = RoundTrip();
        var vtable = (ManagedVtable)build.Invoke(null, [probe])!;

        // Would terminate the process if it unwound through the unmanaged frame.
        caller.GetMethod("Name")!.Invoke(null, [(nint)vtable.Pointer]);

        Assert.False(vtable.Released);
    }

    [Fact]
    public unsafe void ReleaseThroughTheTableFreesTheObject()
    {
        var (build, caller, probe) = RoundTrip();
        var vtable = (ManagedVtable)build.Invoke(null, [probe])!;
        Assert.False(vtable.Released);   // the control

        caller.GetMethod("Release")!.Invoke(null, [(nint)vtable.Pointer]);

        // plan_managed_vtable_reload.md L3: the registry freed the block; native
        // nulled its holder first, so nothing calls through it again.
        Assert.True(vtable.Released);
    }

    [Fact]
    public void TheBlockIsRegisteredUnderTheLayoutsNativeName()
    {
        string written = Generated(Good);

        Assert.Contains("public const string Native = \"IProbe\";", written);
        Assert.Contains("vtables.Create<global::IProbe>(Native, target,", written);
        Assert.Contains("SlotHashes, ReservedWords, instance);", written);
        Assert.Contains("public const int ReservedWords = 0;", written);
    }

    [Fact]
    public void TheBuildCarriesEachContractsHashesForTheReloadCheck()
        => Assert.Contains(
            "[assembly: global::SourceSharp.Abi.ManagedVtableHashes(\"IProbe\", new uint[] { 0x00000010u, 0x00000011u, 0x00000012u, 0x00000013u })]",
            Generated(Good));

    // plan_managed_vtable_reload.md L1: Validate answers from the layout's
    // [SlotHash] values, so a native caller compiled from another layout is
    // refused at the slot that differs.
    [Fact]
    public unsafe void ValidateMatchesTheLayoutsOwnHashes()
    {
        var (build, caller, probe) = RoundTrip();
        var vtable = (ManagedVtable)build.Invoke(null, [probe])!;

        Assert.Equal(-1, (int)caller.GetMethod("Validate")!.Invoke(null, [(nint)vtable.Pointer, new uint[] { 0x10, 0x11, 0x12, 0x13 }, 4])!);
    }

    [Fact]
    public unsafe void ValidateNamesTheFirstSlotThatDiffers()
    {
        var (build, caller, probe) = RoundTrip();
        var vtable = (ManagedVtable)build.Invoke(null, [probe])!;

        Assert.Equal(2, (int)caller.GetMethod("Validate")!.Invoke(null, [(nint)vtable.Pointer, new uint[] { 0x10, 0x11, 0x99, 0x13 }, 4])!);
    }

    [Fact]
    public unsafe void ValidateRefusesACallerWithAnotherSlotCount()
    {
        var (build, caller, probe) = RoundTrip();
        var vtable = (ManagedVtable)build.Invoke(null, [probe])!;

        Assert.Equal(3, (int)caller.GetMethod("Validate")!.Invoke(null, [(nint)vtable.Pointer, new uint[] { 0x10, 0x11, 0x12 }, 3])!);
        Assert.Equal(4, (int)caller.GetMethod("Validate")!.Invoke(null, [(nint)vtable.Pointer, new uint[] { 0x10, 0x11, 0x12, 0x13, 0x14 }, 5])!);
    }

    // ---- what it refuses, each against the Good control -------------------

    [Fact]
    public void ALayoutWithoutImplementedByManagedIsRefused()
        => Assert.Contains("SSVT001", Diagnostics(Good.Replace("[ImplementedByManaged(\"IProbe\")]", "")));

    [Fact]
    public void ASlotWithNoMethodIsRefused()
        => Assert.Contains("SSVT002", Diagnostics(Good.Replace("    void Name(byte* pOut, int nOutSize);\n", "")));

    [Fact]
    public void AMethodWithNoSlotIsRefused()
        => Assert.Contains("SSVT003", Diagnostics(Good.Replace("    int Add(int a);", "    int Add(int a);\n    int Extra();")));

    [Fact]
    public void AMethodWhoseTypesDifferFromItsSlotIsRefused()
        => Assert.Contains("SSVT004", Diagnostics(Good.Replace("    int Add(int a);", "    int Add(long a);")));

    [Fact]
    public void AMethodWhoseReturnDiffersFromItsSlotIsRefused()
        => Assert.Contains("SSVT004", Diagnostics(Good.Replace("    int Add(int a);", "    byte Add(int a);")));

    [Fact]
    public void ALayoutThatDoesNotStartWithReleaseIsRefused()
        => Assert.Contains("SSVT005", Diagnostics(Good.Replace(
            "    [SlotHash(0x00000010)]\n    public delegate* unmanaged<void*, void> Release;\n", "")));

    [Fact]
    public void ALayoutWithoutValidateAtSlotOneIsRefused()
        => Assert.Contains("SSVT005", Diagnostics(Good.Replace(
            "    [SlotHash(0x00000011)]\n    public delegate* unmanaged<void*, void*, int, int> Validate;\n", "")));

    [Fact]
    public void AValidateOfAnotherShapeIsRefused()
        => Assert.Contains("SSVT005", Diagnostics(Good.Replace(
            "unmanaged<void*, void*, int, int> Validate;", "unmanaged<void*, void*, long, int> Validate;")));

    [Fact]
    public void AContractThatDeclaresReleaseIsRefused()
        => Assert.Contains("SSVT005", Diagnostics(Good.Replace("    int Add(int a);", "    int Add(int a);\n    void Release();")));

    [Fact]
    public void AContractThatDeclaresValidateIsRefused()
        => Assert.Contains("SSVT005", Diagnostics(Good.Replace("    int Add(int a);", "    int Add(int a);\n    int Validate(void* p, int n);")));

    [Fact]
    public void ASlotWithoutAHashIsRefused()
        => Assert.Contains("SSVT008", Diagnostics(Good.Replace("    [SlotHash(0x00000012)]\n", "")));

    [Fact]
    public void AnOverloadedMethodIsRefused()
        => Assert.Contains("SSVT006", Diagnostics(Good.Replace("    int Add(int a);", "    int Add(int a);\n    int Add(int a, int b);")));

    [Fact]
    public void ANestedContractIsRefused()
        => Assert.Contains("SSVT007", Diagnostics(Layout + "public static class Outer {\n" + Contract + "}\n"));

    // ---- Valve's layout (plan_physics_managed B0b) -------------------------
    //
    // A callback vphysics calls keeps Valve's slots: no Release, no Validate.

    private const string ValveLayout = """
        [ImplementedByManaged("IWakeProbe", ValveLayout = true)]
        public unsafe struct WakeProbeVtbl
        {
            [SlotHash(0x00000020)]
            public delegate* unmanaged<void*, void*, void> ObjectWake;
            [SlotHash(0x00000021)]
            public delegate* unmanaged<void*, void*, int> ObjectSleep;
        }
        """;

    private const string ValveContract = """
        [ManagedVtable(typeof(WakeProbeVtbl))]
        public unsafe interface IWakeProbe
        {
            void ObjectWake(void* pObject);
            int ObjectSleep(void* pObject);
        }
        """;

    private const string ValveImplementation = """
        public sealed unsafe class WakeProbe : IWakeProbe
        {
            public nint Woken;
            public void ObjectWake(void* pObject) => Woken = (nint)pObject;
            public int ObjectSleep(void* pObject) => (int)(nint)pObject + 1;
        }

        public static unsafe class ValveCaller
        {
            public static SourceSharp.Abi.ManagedVtable Build(IWakeProbe target) => IWakeProbe__ManagedVtable.Create(new SourceSharp.Abi.ManagedVtables(), target);

            private static nint Slot(nint self, int i) => ((nint*)((nint*)self)[0])[i];

            public static void Wake(nint self, nint obj)
                => ((delegate* unmanaged<void*, void*, void>)Slot(self, 0))((void*)self, (void*)obj);

            public static int Sleep(nint self, nint obj)
                => ((delegate* unmanaged<void*, void*, int>)Slot(self, 1))((void*)self, (void*)obj);
        }
        """;

    [Fact]
    public void AValveLayoutWithNoReleaseOrValidateIsAcceptedClean()
        => Assert.Empty(Diagnostics(ValveLayout + ValveContract));

    [Fact]
    public void TheSameLayoutWithoutValveLayoutIsRefused()
        => Assert.Contains("SSVT005", Diagnostics(
            (ValveLayout + ValveContract).Replace(", ValveLayout = true", "")));

    [Fact]
    public void AValveLayoutWritesOnlyItsOwnSlots()
    {
        string written = Generated(ValveLayout + ValveContract);

        Assert.Contains("public const int Entries = 2;", written);
        Assert.Equal(2, written.Split("[global::System.Runtime.InteropServices.UnmanagedCallersOnly]").Length - 1);
        Assert.DoesNotContain("ReleaseBlock", written);
        Assert.DoesNotContain("ValidateSlots", written);
    }

    [Fact]
    public unsafe void ACallThroughAValveTableLandsOnTheInstanceAtSlotZero()
    {
        var (compilation, _) = Drive(ValveLayout + ValveContract + ValveImplementation);
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        Type caller = assembly.GetType("ValveCaller")!;
        object probe = Activator.CreateInstance(assembly.GetType("WakeProbe")!)!;
        var vtable = (ManagedVtable)caller.GetMethod("Build")!.Invoke(null, [probe])!;

        Assert.Equal(2, vtable.Entries);
        caller.GetMethod("Wake")!.Invoke(null, [(nint)vtable.Pointer, (nint)0x1234]);
        Assert.Equal((nint)0x1234, (nint)probe.GetType().GetField("Woken")!.GetValue(probe)!);
        Assert.Equal(8, (int)caller.GetMethod("Sleep")!.Invoke(null, [(nint)vtable.Pointer, (nint)7])!);

        // No Release slot: managed frees the block itself, once.
        Assert.True(vtable.Release());
        Assert.True(vtable.Released);
    }

    // ---- Every thunk at its field's index ------------------------------------
    //
    // tools/vtable-layout emits no table MSVC orders differently, so a slot's
    // index is its field position on both ABIs.

    [Fact]
    public void ALayoutWritesEachThunkAtItsFieldIndex()
    {
        string written = Generated(ValveLayout + ValveContract);
        string[] lines = written.Split('\n').Select(l => l.Trim()).ToArray();

        Assert.Single(lines, l => l.StartsWith("slots[0] = ", StringComparison.Ordinal) && l.EndsWith("&ObjectWake;", StringComparison.Ordinal));
        Assert.Single(lines, l => l.StartsWith("slots[1] = ", StringComparison.Ordinal) && l.EndsWith("&ObjectSleep;", StringComparison.Ordinal));
        Assert.DoesNotContain("IsWindows", written);
    }

    // A Valve class with data after its vptr (CCallbackBase): the layout's
    // ReservedWords reaches the block, and a call through the table still lands.
    [Fact]
    public void AReservedLayoutCarriesItsCountAndANamedInstanceCreate()
    {
        string written = Generated((ValveLayout + ValveContract).Replace("ValveLayout = true)", "ValveLayout = true, ReservedWords = 1)"));

        Assert.Contains("public const int ReservedWords = 1;", written);
        Assert.Contains("target, string? instance)", written);
    }

    [Fact]
    public unsafe void ACallThroughAReservedValveTableLandsOnTheInstance()
    {
        var (compilation, _) = Drive((ValveLayout + ValveContract + ValveImplementation)
            .Replace("ValveLayout = true)", "ValveLayout = true, ReservedWords = 1)"));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        Type caller = assembly.GetType("ValveCaller")!;
        object probe = Activator.CreateInstance(assembly.GetType("WakeProbe")!)!;
        var vtable = (ManagedVtable)caller.GetMethod("Build")!.Invoke(null, [probe])!;

        Assert.Equal(1, vtable.ReservedWords);
        Assert.Equal((nint)((nint*)vtable.Pointer + 1 + 1 + 3), ((nint*)vtable.Pointer)[0]);
        caller.GetMethod("Wake")!.Invoke(null, [(nint)vtable.Pointer, (nint)0x4321]);
        Assert.Equal((nint)0x4321, (nint)probe.GetType().GetField("Woken")!.GetValue(probe)!);
        Assert.True(vtable.Release());
    }

    // ---- the driver --------------------------------------------------------

    private static List<string> Diagnostics(string source)
        => Drive(source).result.Diagnostics.Select(d => d.Id).ToList();

    // The generator writes with AppendLine, so its lines end in the platform's
    // newline ("\r\n" on Windows). Facts match multi-line spans with "\n", so
    // the text is read with one line ending everywhere.
    private static string Generated(string source)
        => string.Join("\n", Drive(source).result.GeneratedSources.Select(s => s.SourceText.ToString()))
            .Replace("\r\n", "\n");

    private static (System.Reflection.MethodInfo Build, Type Caller, object Probe) RoundTrip()
    {
        var (compilation, _) = Drive(Good + Implementation);

        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success,
            "the snippet plus its generated half must compile:\n"
            + string.Join("\n", emitted.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));

        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        Type caller = assembly.GetType("NativeCaller")!;
        object probe = Activator.CreateInstance(assembly.GetType("Probe")!)!;
        return (caller.GetMethod("Build")!, caller, probe);
    }

    private static (Compilation compilation, GeneratorRunResult result) Drive(string source)
    {
        var tree = CSharpSyntaxTree.ParseText("using System;\nusing SourceSharp.Abi;\n" + source);

        var compilation = CSharpCompilation.Create(
            "ManagedVtableGeneratorTests.Subject." + Guid.NewGuid().ToString("N"),
            [tree],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var driver = CSharpGeneratorDriver.Create(new ManagedVtableGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        return (updated, driver.GetRunResult().Results.Single());
    }

    private static readonly MetadataReference[] References =
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0)
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .Append(MetadataReference.CreateFromFile(typeof(ManagedVtableAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(ManagedVtable).Assembly.Location))
            .Distinct()
            .ToArray();
}

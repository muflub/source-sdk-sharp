using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// That the five entity-factory slots (ABI <b>242</b>; the EntityLifecycle area's
/// since C6c) reach
/// <see cref="EntityFactories"/> on the SERVER and nowhere else.
///
/// <para>
/// <b>THE THING THESE FACTS PROTECT IS A DISTINCTION, NOT A FEATURE.</b>
/// <c>Available == false</c> means "the dictionary has not been asked" and
/// <c>Available == true</c> with an empty table means "the dictionary answered
/// nothing". <see cref="EntityFactories.OwnerOf"/> reads the first as
/// <c>Native</c> — the assumption every build before ABI 242 ran on — and the
/// second as <c>Nothing</c>, which refuses <c>ent_create prop_physics</c> for a
/// class that works and declares a hundred present capabilities absent.
/// </para>
///
/// <para>
/// So a wiring that fires on the CLIENT, which has no factory dictionary at
/// all, is not a harmless extra: it manufactures exactly the empty table that
/// distinction exists to prevent. Same for a wiring that fires against a native
/// half whose slots are null.
/// </para>
///
/// <para>
/// <see cref="EngineSide.BindEntityFactories"/> is driven on a side of the
/// test's own, attached to a <see cref="FakeShimRoot"/>, and answers a table of
/// its own (plan_abi_hotreload D7: the providers are no longer static), so
/// these facts touch nothing another suite shares.
/// </para>
/// </summary>
public unsafe class EntityFactoryAbiWiringTests : IDisposable
{
    public void Dispose() => _root.Dispose();

    private readonly FakeShimRoot _root = new();

    [UnmanagedCallersOnly] private static int Count(void* self) => 0;

    /// <summary>
    /// A server side whose EntityLifecycle area is attached, as LoadSideV2
    /// leaves it. Since C6c the five slots are that area's and not the struct's,
    /// and the area's vtable is complete or refused whole by
    /// <see cref="ShimAreas.TryAcquire"/>: the half-wired tables the struct
    /// allowed (a count without its names, two of the three refusal slots)
    /// cannot be built any more, so nothing here tests them.
    /// </summary>
    private EngineSide Attached(Side which = Side.Server)
    {
        var side = new EngineSide(which);
        _root.Live("SourceSharpEntityLifecycle001", nameof(ShimEntityLifecycleFwd.EntityFactoryCount),
                   (nint)(delegate* unmanaged<void*, int>)&Count);
        Assert.True(side.AttachShims(_root.Bind(), out string why), why);
        return side;
    }

    [Fact]
    public void TheServerArmWiresTheTable()
    {
        Assert.True(Attached().BindEntityFactories().Available);
    }

    /// <summary>
    /// A bound table arrives PULLED. A cs_sv_reload builds a new composition
    /// and so a new table (plan_abi_hotreload D7), and no map load follows to
    /// refresh it: unpulled, the dev API read 150 native classes before the
    /// reload and 0 after, and ent_create went back to hedging.
    /// </summary>
    [Fact]
    public void TheServerArmHandsOverAPulledTable()
        => Assert.True(Attached().BindEntityFactories().Pulled);

    [Fact]
    public void TheServerArmWiresAllThreeRefusalSlots()
    {
        var factories = Attached().BindEntityFactories();

        Assert.NotNull(factories.RefusalCount);
        Assert.NotNull(factories.RefusalNameAt);
        Assert.NotNull(factories.RefusalTimesAt);
    }

    /// <summary>
    /// The CLIENT wires nothing. It has no factory dictionary, so a wiring
    /// there would answer zero for every question — an EMPTY table, which is
    /// the one answer worse than no table.
    /// </summary>
    [Fact]
    public void TheClientArmWiresNothingEvenWithTheAreaAttached()
    {
        Assert.False(Attached(Side.Client).BindEntityFactories().Available);
    }

    [Fact]
    public void TheClientArmLeavesTheRefusalLedgerUnwiredToo()
    {
        Assert.Null(Attached(Side.Client).BindEntityFactories().RefusalCount);
    }

    /// <summary>
    /// A server side with no shims attached leaves the table UNASKED rather
    /// than empty: there is nothing to ask it with.
    /// </summary>
    [Fact]
    public void AServerWithoutItsShimsWiresNothing()
    {
        var factories = new EngineSide(Side.Server).BindEntityFactories();

        Assert.False(factories.Available);
        Assert.Null(factories.NativeCount);
    }

    //-------------------------------------------------------------------------
    // The wrappers themselves, with no engine under them. Nothing in this
    // process has called Engine.Initialize, so every one of them is on its
    // guarded arm — which is the arm a unit test can reach and the arm a build
    // without the native half takes.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheCountWrapperAnswersZeroWithNoEngine()
        => Assert.Equal(0, new EngineSide(Side.Server).EntityFactoryCount());

    [Fact]
    public void TheNameWrapperAnswersNullWithNoEngine()
        => Assert.Null(new EngineSide(Side.Server).EntityFactoryNameAt(0));

    /// <summary>
    /// NULL AS NULL, and not as <c>""</c>.
    ///
    /// <para>
    /// <c>Engine.FromUtf8</c> is the house marshaller and the obvious way to
    /// write the two indexed reads — and it answers <c>string.Empty</c> for a
    /// null pointer. The contract in <c>sourcesharp.h</c> is that NULL means
    /// "past the end". This is the line that keeps them apart, and the wrappers
    /// above cannot test it: with no engine bound they return on their null-slot
    /// guard and never reach the marshal at all.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMarshallerAnswersNullForANullPointer()
        => Assert.Null(EngineSide.Utf8OrNull(null));

    /// <summary>
    /// And it is a real marshaller, not a method that answers null for
    /// everything — which would end <c>Refresh</c>'s walk at the first name and
    /// report the whole dictionary as empty.
    /// </summary>
    [Fact]
    public void TheMarshallerReadsARealName()
    {
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes("func_useableladder\0");

        fixed (byte* p = utf8)
            Assert.Equal("func_useableladder", EngineSide.Utf8OrNull(p));
    }

    [Fact]
    public void TheRefusalCountWrapperAnswersZeroWithNoEngine()
        => Assert.Equal(0, new EngineSide(Side.Server).EntityFactoryRefusalCount());

    [Fact]
    public void TheRefusalNameWrapperAnswersNullWithNoEngine()
        => Assert.Null(new EngineSide(Side.Server).EntityFactoryRefusalNameAt(0));

    [Fact]
    public void TheRefusalTimesWrapperAnswersZeroWithNoEngine()
        => Assert.Equal(0, new EngineSide(Side.Server).EntityFactoryRefusalTimesAt(0));

    //-------------------------------------------------------------------------
    // The version. The append is five slots; the BUMP is owed by a sixth
    // change nobody can see from the field list — `diag-visibility` retyped
    // EntitiesInvalidateMdlCacheAll from void to int32_t on both sides. A
    // signature change with the version standing still is exactly what this
    // number exists to catch.
    //-------------------------------------------------------------------------

    /// <summary>
    /// And the retype that owes the bump is really there: the slot answers an
    /// <c>int</c>. A <c>void</c> slot could not tell a working body from an
    /// empty one, which is the state the client half was in.
    /// </summary>
    [Fact]
    public void TheRetypedMdlCacheSlotAnswersACount()
    {
        // A shared slot since plan_client_vtables CC-last deleted the struct:
        // the client's IShimModels and the server's area that extends it share
        // this layout, whose only parameter is the object.
        var field = typeof(ShimModelsFwd).GetField(nameof(ShimModelsFwd.EntitiesInvalidateMdlCacheAll));

        Assert.True(field is { FieldType.IsFunctionPointer: true });
        Assert.Equal(typeof(int), field!.FieldType.GetFunctionPointerReturnType());
        Assert.Equal(new[] { typeof(void*) }, field.FieldType.GetFunctionPointerParameterTypes());
    }

    //-------------------------------------------------------------------------
    // THE FIVE SLOTS' SHAPES, on the EntityLifecycle vtable since C6c. The
    // vtable-layout check compares the two sides by NAME and in ORDER; these
    // read the types. A `const char *` mirrored as an `int` would hand managed
    // code a pointer to marshal as a number. Every vtable slot takes the
    // object first.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheTwoCountSlotsAnswerAnIntAndTakeOnlyTheObject()
    {
        foreach (string name in new[]
                 {
                     nameof(ShimEntityLifecycleFwd.EntityFactoryCount),
                     nameof(ShimEntityLifecycleFwd.EntityFactoryRefusalCount),
                 })
        {
            Type slot = Slot(name);

            Assert.Equal(typeof(int), slot.GetFunctionPointerReturnType());
            Assert.Equal(new[] { typeof(void*) }, slot.GetFunctionPointerParameterTypes());
        }
    }

    [Fact]
    public void TheTwoNameSlotsAnswerAStringPointerAndTakeAnIndex()
    {
        foreach (string name in new[]
                 {
                     nameof(ShimEntityLifecycleFwd.EntityFactoryNameAt),
                     nameof(ShimEntityLifecycleFwd.EntityFactoryRefusalNameAt),
                 })
        {
            Type slot = Slot(name);

            Assert.Equal(typeof(byte*), slot.GetFunctionPointerReturnType());
            Assert.Equal(new[] { typeof(void*), typeof(int) }, slot.GetFunctionPointerParameterTypes());
        }
    }

    [Fact]
    public void TheRefusalTimesSlotAnswersAnIntAndTakesAnIndex()
    {
        Type slot = Slot(nameof(ShimEntityLifecycleFwd.EntityFactoryRefusalTimesAt));

        Assert.Equal(typeof(int), slot.GetFunctionPointerReturnType());
        Assert.Equal(new[] { typeof(void*), typeof(int) }, slot.GetFunctionPointerParameterTypes());
    }

    // TheFiveAreNotEngineApiFields went with the struct (plan_client_vtables
    // CC-last 2c): there are no struct fields left for the five to be in.
    // AbiParityTests.TheEngineApiStructIsGone holds the absence.

    //-------------------------------------------------------------------------
    // AND THE FIVE WRAPPERS ARE NOT PART OF THE ENGINE SURFACE GAME CODE SEES.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <b>A SECOND PUBLIC ROUTE TO THIS DATA WOULD BE A ROUTE ROUND THE ONE
    /// DISTINCTION THAT MATTERS.</b> <see cref="EntityFactories.Available"/>
    /// answers "was the dictionary asked"; a caller reading
    /// <c>new EngineSide(Side.Server).EntityFactoryCount()</c> straight would get 0 from an unwired
    /// build and read it as "the table is empty", which is exactly the
    /// misreading that refuses <c>ent_create prop_physics</c> for a class that
    /// works.
    ///
    /// <para>
    /// <c>EngineAreaTests.EveryEngineMemberIsOnAnArea</c> requires every PUBLIC
    /// <c>Engine</c> member to be forwarded by an area binding, so making one of
    /// these public reddens that too — but it reddens with "add a binding",
    /// which is the wrong repair. This says which repair is right.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFiveWrappersAreNotPublicOnEngineSide()
    {
        // plan_abi_hotreload D8: the static Engine forwards nothing now; the
        // five live on each side's EngineSide, and are internal there.
        string[] published = typeof(EngineSide)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .Where(n => n.StartsWith("EntityFactory", StringComparison.Ordinal))
            .ToArray();

        Assert.True(published.Length == 0,
            "EngineSide publishes a factory-dictionary read to game code: "
            + string.Join(", ", published)
            + ". EntityFactories is the only consumer, and it is what keeps "
            + "\"not asked\" distinguishable from \"empty\".");
    }

    /// <summary>
    /// And the scan is not vacuous — <c>EngineSide</c> really does publish
    /// instance methods, so an empty answer above means "none of THESE" rather
    /// than "the reflection found nothing"; and the five are there to find.
    /// </summary>
    [Fact]
    public void TheScanForPublishedWrappersReallyReadsEngineSide()
    {
        Assert.Contains(typeof(EngineSide).GetMethods(BindingFlags.Public | BindingFlags.Instance),
                        m => m.Name == nameof(EngineSide.EntityIsValid));
        Assert.Equal(5, typeof(EngineSide).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                            .Count(m => m.Name.StartsWith("EntityFactory", StringComparison.Ordinal)));
    }

    private static Type Slot(string name)
    {
        var field = typeof(ShimEntityLifecycleFwd).GetField(name);

        Assert.True(field is not null, $"ShimEntityLifecycleFwd no longer declares {name}");
        Assert.True(field!.FieldType.IsFunctionPointer,
                    $"ShimEntityLifecycleFwd.{name} is no longer a function pointer");

        return field.FieldType;
    }
}

extern alias client;
// The client's interop is referenced under the client alias alone, since it
// compiles its own copy of the shared interop (plan_shim_areas_split phase 3).
using EngineClient = client::SourceSharp.Abi.EngineClient;
using IFwdEngineClient = client::SourceSharp.Abi.IFwdEngineClient;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

//=============================================================================
// THE VTABLE BINDING, AFTER THE VERIFICATION APPARATUS WAS REMOVED.
//
// What is left to test is small, and being small is the point. There is no
// symbol route, no RTTI route, no `this`-adjustment confirmation and no live
// differential -- the owner's ruling is that the engine's factory interfaces are
// stable and the version string is the contract, so none of that was answering
// a question anybody had.
//
// Two things remain testable and both are here (the engine's interfaces are
// called through their forwarders now, plan_engine_forwarders.md, so the
// managed layouts of the engine's own tables, and their per-ABI slot numbers,
// went with phase 6; fwd_engine_test holds each forwarder to its method):
//
//   1. THE MECHANISM. A struct field's POSITION is the slot number, and a call
//      through it reaches the function in that slot. Tested against a table
//      this file builds, so it needs no engine.
//
//   2. POLICY, WHICH IS NOW EXPRESSED ONLY BY ABSENCE. There is no table saying
//      ClientCmd_Unrestricted must not be called; there is simply no method for
//      it. That is only a real rule if something notices when a method appears,
//      which is what the surface tests below do. (Running a console command at
//      all is allowed since 2026-10-05, plan_client_novalve.md §8; what stays
//      absent is the raw slots, not the capability.)
//=============================================================================
public unsafe class VtableBindingTests
{
    //-------------------------------------------------------------------------
    // 1. THE MECHANISM, over a vtable built here.
    //
    // THE IMPL CONSTRUCTORS ARE PRIVATE AND STAY PRIVATE. In production nothing
    // needs to reach one -- `Acquire` is a static member of the same class and
    // calls it directly -- so there is no accessor anywhere in the tree.
    //
    // This file reaches one by REFLECTION, which is a thing only a test may do,
    // and it is spelled out rather than hidden behind an accessor attribute: an
    // IL-level accessor would be a second, permanent door into a class whose
    // whole point is that it has one, and it would compile in the shipped
    // assembly. Pointer.Box is what lets a pointer argument through Invoke.
    //-------------------------------------------------------------------------
    private static IUniformRandom NewStream(void* self, void** table)
    {
        ConstructorInfo ctor = typeof(UniformRandomStream)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single();

        return (IUniformRandom)ctor.Invoke(new object?[]
        {
            Pointer.Box(self, typeof(void*)),
            Pointer.Box(table, typeof(UniformRandomStreamFwd*)),
        });
    }

    [UnmanagedCallersOnly]
    private static float SlotOneAnswers(void* self, float min, float max) => 111f;

    [UnmanagedCallersOnly]
    private static float SlotZeroAnswers(void* self, float min, float max) => 999f;

    [UnmanagedCallersOnly]
    private static int SlotTwoAnswers(void* self, int min, int max) => min + max;

    // The forwarder slot of a field of UniformRandomStreamFwd: its offset.
    private static int Slot(string field)
        => (int)(Marshal.OffsetOf<UniformRandomStreamFwd>(field) / sizeof(void*));

    /// <summary>
    /// An object and a forwarder table the size of UniformRandomStreamFwd, with
    /// a distinct function in each slot: RandomFloat's answers 111, RandomInt's
    /// sums its arguments, and RandomFloatExp's slot holds a function of
    /// RandomFloat's own shape that answers 999, for the fact that swaps them.
    /// </summary>
    private static void* FakeStream(out void** table)
    {
        table = (void**)NativeMemory.AllocZeroed(
            (nuint)Forwarders.SlotCount<UniformRandomStreamFwd>(), (nuint)sizeof(void*));

        table[Slot("RandomFloatExp")] = (delegate* unmanaged<void*, float, float, float>)&SlotZeroAnswers;
        table[Slot("RandomFloat")] = (delegate* unmanaged<void*, float, float, float>)&SlotOneAnswers;
        table[Slot("RandomInt")] = (delegate* unmanaged<void*, int, int, int>)&SlotTwoAnswers;

        void** self = (void**)NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        *self = table;

        return self;
    }

    [Fact]
    public void TheImplConstructorIsReachableOnlyByReflection()
    {
        void* self = FakeStream(out void** table);

        Assert.NotNull(NewStream(self, table));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void ACallThroughAFieldReachesTheFunctionInThatSlot()
    {
        void* self = FakeStream(out void** table);
        IUniformRandom stream = NewStream(self, table);

        // 111 is the RandomFloat slot's answer and 999 the one beside it.
        Assert.Equal(111f, stream.RandomFloat(0f, 1f));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void TheFieldsPositionIsTheIndexAndNotItsName()
    {
        void* self = FakeStream(out void** table);
        IUniformRandom stream = NewStream(self, table);

        // THE ARM THAT MAKES THE ONE ABOVE MEAN SOMETHING. Swap the two
        // pointers and the SAME call answers the other number: nothing about
        // the binding is keyed on a name, only on where the field sits.
        // Written out rather than as a tuple swap: `void*` may not be a type
        // argument, and a tuple is generic.
        void* first = table[Slot("RandomFloat")];
        table[Slot("RandomFloat")] = table[Slot("RandomFloatExp")];
        table[Slot("RandomFloatExp")] = first;

        Assert.Equal(999f, stream.RandomFloat(0f, 1f));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void ADifferentlyShapedSlotIsADifferentField()
    {
        void* self = FakeStream(out void** table);
        IUniformRandom stream = NewStream(self, table);

        // RandomInt's slot takes two ints and returns their sum, so this also
        // says the arguments arrive: a call landing on RandomFloat's would
        // answer 111.
        Assert.Equal(7, stream.RandomInt(3, 4));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    //-------------------------------------------------------------------------
    // ACQUIRE REFUSES WITH A REASON when there is no native half, which in a
    // test process is always. The reason matters: "could not acquire" tells a
    // caller nothing, and the two failures — no such interface, and a length
    // that does not match — are completely different problems.
    //-------------------------------------------------------------------------
    [Fact]
    public void AcquireRefusesWithoutTheNativeHalf()
    {
        Assert.Null(UniformRandomStream.Acquire(Unattached(), out string why));
        Assert.False(string.IsNullOrWhiteSpace(why));
    }

    [Fact]
    public void TheRefusalNamesTheInterfaceItWasAskedFor()
    {
        UniformRandomStream.Acquire(Unattached(), out string why);
        Assert.Contains("VEngineRandom001", why);
    }

    /// <summary>
    /// The server's cs_vtable asks for IVEngineClient through ShimProbes, since
    /// EngineClient lives in Client.Interop (lane side-audit-2); the name it
    /// asks for must be the one the client's acquire passes.
    /// </summary>
    [Fact]
    public void TheServerProbeAsksForTheEngineClientByTheClientsName()
        => Assert.Equal(EngineClient.InterfaceVersion, ShimProbes.EngineClientInterfaceVersion);

    /// <summary>
    /// And reports the refusal, naming the interface, rather than skipping it:
    /// a refusal nobody printed is indistinguishable from a call nobody made.
    /// </summary>
    [Fact]
    public void TheServerProbeReportsTheEngineClientRefused()
    {
        string line = ShimProbes.EngineClient(new ModuleEngine(EngineSide.Create(Side.Server)));

        Assert.True(line.StartsWith("IVEngineClient: REFUSED", StringComparison.Ordinal)
                    && line.Contains("VEngineClient014", StringComparison.Ordinal),
                    $"the server's probe line reports the refusal by name: {line}");
    }

    [Fact]
    public void EveryFactoryRefusesRatherThanThrowingWithNoNativeHalf()
    {
        Assert.Null(EngineModelInfo.Acquire(Unattached(), out _));
        Assert.Null(EngineBaseFileSystem.Acquire(Unattached(), out _));
        Assert.Null(EngineClient.Acquire(new client::SourceSharp.Abi.ModuleEngine(client::SourceSharp.Abi.EngineSide.Create(Side.Client)), out _));
        Assert.Null(EngineServer.Acquire(Unattached(), out _));
    }

    /// <summary>
    /// A server engine of the test's own with no native half (plan_abi_hotreload
    /// D8): every acquire names the module it acquires for.
    /// </summary>
    private static ModuleEngine Unattached() => new(EngineSide.Create(Side.Server));

    //-------------------------------------------------------------------------
    // 2. POLICY, EXPRESSED ONLY BY ABSENCE.
    //
    // There is no refusal table any more. A method that must not be callable is
    // one that is not written, and these are what make that a rule rather than
    // an accident of the day somebody typed the file.
    //-------------------------------------------------------------------------
    // ACQUIRE IS THE ONE STATIC MEMBER AN IMPL CLASS IS ALLOWED, and it is
    // excluded here rather than tolerated in each caller: every test below asks
    // "what else is on this class", and the answer must be nothing.
    private static string[] PublicMethodsOf(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                           | BindingFlags.DeclaredOnly)
               .Select(m => m.Name)
               .Where(n => n != "Acquire")
               .ToArray();

    [Fact]
    public void TheRandomStreamOffersNoSetSeed()
        // Slot 0 is TYPED in the struct — the ABI is what it is — and managed
        // code must not be a source of server randomness, which demo playback
        // replays. The absence is the whole of how that is said.
        => Assert.DoesNotContain("SetSeed", PublicMethodsOf(typeof(UniformRandomStream)));

    /// <summary>
    /// Console commands run through members named for their purpose -- the UI
    /// console's free text among them, since the rule against running them was
    /// lifted (user, 2026-10-05, plan_client_novalve.md §8) -- and never through
    /// the raw slots, which add nothing ClientCmd's purpose-named members need.
    /// </summary>
    [Fact]
    public void TheEngineClientRunsConsoleCommandsOnlyThroughNamedMembers()
    {
        // What game code is offered is the seam's surface: the client's
        // implementation is reached only through it (plan_shim_areas_split
        // phase 10).
        string[] offered = PublicMethodsOf(typeof(IFwdEngineClient));

        Assert.Contains("SubmitConsoleLine", offered);
        foreach (string forbidden in new[]
                 { "ClientCmd", "ClientCmd_Unrestricted", "ExecuteClientCmd", "LoadModel" })
        {
            Assert.DoesNotContain(forbidden, offered);
        }
    }

    [Fact]
    public void TheEngineServerChangesTheWorldOnlyThroughNamedMembers()
    {
        // THE READ-ONLY WINDOW WAS LIFTED BY THE OWNER, for exactly these:
        // plan_server_native_min P2.2 folds IVEngineServer's ServerCommand/
        // Execute, SetView and LightStyle through the forwarders, and
        // plan_server_native_v2 T3 (ruled 2026-10-18) carries it unchanged. Each
        // arrives as one member named for its purpose; the raw slots and what
        // creates or precaches stay absent.
        string[] offered = PublicMethodsOf(typeof(EngineServer));

        foreach (string named in new[] { "ServerCommandNow", "SetView", "LightStyle" })
            Assert.Contains(named, offered);

        foreach (string forbidden in new[]
                 { "CreateEdict", "ServerCommand", "ServerExecute", "ClientCommand",
                   "PrecacheModel", "EmitAmbientSound" })
        {
            Assert.DoesNotContain(forbidden, offered);
        }
    }

    [Fact]
    public void TheFileSystemOffersNothingThatWrites()
    {
        string[] offered = PublicMethodsOf(typeof(EngineBaseFileSystem));

        foreach (string forbidden in new[]
                 { "Write", "WriteFile", "SetFileWritable", "Open", "Close", "UnzipFile" })
        {
            Assert.DoesNotContain(forbidden, offered);
        }
    }

    [Fact]
    public void AnImplClassOffersEXACTLYItsInterfaceAndNothingElse()
    {
        // THE ARM THAT CATCHES THE NEXT ONE RATHER THAN THE LISTED ONES. The
        // tests above name methods somebody already decided about; this one goes
        // red for ANY member added to an impl class, including one nobody has
        // thought to forbid — which is the only form the rule can take now that
        // there is no table to consult.
        //
        // Property getters and the object members are excluded; what is left
        // must be the interface's own methods, one for one.
        foreach ((Type impl, Type contract) in new[]
                 {
                     (typeof(UniformRandomStream), typeof(IUniformRandom)),
                     (typeof(EngineModelInfo), typeof(IFwdModelInfo)),
                     (typeof(EngineBaseFileSystem), typeof(IFwdBaseFileSystem)),
                     (typeof(EngineClient), typeof(IFwdEngineClient)),
                     (typeof(EngineServer), typeof(IFwdEngineServer)),
                 })
        {
            string[] offered = PublicMethodsOf(impl).OrderBy(n => n).ToArray();
            string[] declared = contract.GetMethods().Select(m => m.Name).OrderBy(n => n).ToArray();

            Assert.Equal(declared, offered);
        }
    }

    [Fact]
    public void AnImplClassHasNoPUBLICSTATEOfAnyKind()
    {
        // `Self` is gone. It was public so the deleted differential's controls
        // could reach an arbitrary slot, and a public pointer on an impl class
        // is precisely a way to reach every slot the class does not expose.
        //
        // The version-string CONSTANTS are allowed and nothing else is. A const
        // is a compile-time literal -- it carries no object state and cannot be
        // written -- and it belongs on the class whose Acquire passes it.
        foreach (Type impl in new[]
                 {
                     typeof(UniformRandomStream), typeof(EngineModelInfo),
                     typeof(EngineBaseFileSystem), typeof(EngineClient), typeof(EngineServer),
                 })
        {
            FieldInfo[] fields = impl.GetFields(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly);

            Assert.All(fields, f => Assert.True(
                f.IsLiteral && f.FieldType == typeof(string) && f.Name.EndsWith("InterfaceVersion"),
                $"{impl.Name}.{f.Name} is public state, not a version-string constant"));

            Assert.Empty(impl.GetProperties(BindingFlags.Public | BindingFlags.Instance
                                            | BindingFlags.Static | BindingFlags.DeclaredOnly));
        }
    }

    [Fact]
    public void AcquireIsTheONLYStaticMethodOnAnImplClass()
    {
        // The counterpart to PublicMethodsOf excluding it: something has to
        // notice if a SECOND static appears, or the exclusion above would hide
        // it from every other test in this file.
        foreach (Type impl in new[]
                 {
                     typeof(UniformRandomStream), typeof(EngineModelInfo),
                     typeof(EngineBaseFileSystem), typeof(EngineClient), typeof(EngineServer),
                 })
        {
            string[] statics = impl
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => m.Name)
                .ToArray();

            Assert.Equal(new[] { "Acquire" }, statics);
        }
    }

    [Fact]
    public void AcquireAnswersTheINTERFACERatherThanTheConcreteClass()
    {
        // So a caller still sees no pointer and needs no `unsafe`. A return type
        // of the impl class would hand every caller the concrete type and make
        // the interface decorative.
        foreach ((Type impl, Type contract) in new[]
                 {
                     (typeof(UniformRandomStream), typeof(IUniformRandom)),
                     (typeof(EngineModelInfo), typeof(IFwdModelInfo)),
                     (typeof(EngineBaseFileSystem), typeof(IFwdBaseFileSystem)),
                     (typeof(EngineClient), typeof(IFwdEngineClient)),
                     (typeof(EngineServer), typeof(IFwdEngineServer)),
                 })
        {
            MethodInfo acquire = impl.GetMethod("Acquire", BindingFlags.Public
                                                | BindingFlags.Static | BindingFlags.DeclaredOnly)!;

            Assert.Equal(contract, acquire.ReturnType);
        }
    }

    [Fact]
    public void AnImplClassHasExactlyOneConstructorAndItIsPrivate()
    {
        foreach (Type impl in new[]
                 {
                     typeof(UniformRandomStream), typeof(EngineModelInfo),
                     typeof(EngineBaseFileSystem), typeof(EngineClient), typeof(EngineServer),
                 })
        {
            ConstructorInfo[] made = impl.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.Single(made);
            Assert.True(made[0].IsPrivate, impl.Name + "'s constructor is not private");
        }
    }

    //-------------------------------------------------------------------------
    // THE MARSHALLING UTILS, which are utils precisely so a delegation stays one
    // line.
    //-------------------------------------------------------------------------
    [Fact]
    public void Utf8TerminatesTheString()
    {
        byte[] bytes = Vtable.Utf8("ab");

        Assert.Equal(new byte[] { (byte)'a', (byte)'b', 0 }, bytes);
    }

    [Fact]
    public void TextOfStopsAtTheTerminator()
    {
        byte[] bytes = Vtable.Utf8("maps/dm_lockdown.bsp");

        fixed (byte* p = bytes)
            Assert.Equal("maps/dm_lockdown.bsp", Vtable.TextOf(p, Vtable.MaxPath));
    }

    [Fact]
    public void TextOfStopsAtTheCAPWhenThereIsNoTerminator()
    {
        // THE REASON THERE IS A CAP AT ALL. A pointer into something that is not
        // a string must read a bounded amount rather than walking to the end of
        // the mapping.
        byte[] bytes = { (byte)'a', (byte)'b', (byte)'c', (byte)'d' };

        fixed (byte* p = bytes)
            Assert.Equal("ab", Vtable.TextOf(p, 2));
    }

    [Fact]
    public void TextOfAnswersNullForANullPointer()
        => Assert.Null(Vtable.TextOf(null, Vtable.MaxPath));

    [Fact]
    public void BitIsTheSingleByteCppPasses()
    {
        Assert.Equal((byte)1, Vtable.Bit(true));
        Assert.Equal((byte)0, Vtable.Bit(false));
    }
}

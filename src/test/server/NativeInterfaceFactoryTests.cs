using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

//=============================================================================
// THE ACQUISITION CONTRACT, DRIVEN THROUGH A FAKE SWITCH.
//
// plan_native_api.md Part 5c. The half of NativeInterfaceFactory that matters
// is what happens when an acquire FAILS, and that half cannot be exercised in a
// real process without arranging for the process to be broken. So the seam is
// the native slot itself: NativeInterfaceFactory takes an AcquireFn, production
// hands it Engine.AcquireInterface, and a fact hands it a delegate that answers
// for known names out of a table this file builds.
//
// WHAT KEEPS THE FAKE HONEST is not in this file and is not meant to be: the
// live probes (cs_vtable, cs_vtable_client) run the SAME acquisition against
// the real switch in a real process, and the closed-set arm below is mirrored
// by a gtest walking the same enum natively. A fake that drifted from the
// switch would still pass here; it would not still pass there.
//=============================================================================
public unsafe class NativeInterfaceFactoryTests
{
    // The four literals, exactly as shell_vtable_interfaces.cpp spells them.
    // Written out rather than read from anywhere, because a test that read them
    // from the thing under test would agree with itself whatever they said.
    private const string NotYetAvailable = "factory not yet available";
    private const string NoSuchFactory = "no factory of that kind on this side";
    private const string UnknownInterface = "factory does not know that interface";
    private const string UnknownFactoryType = "unknown factory type";

    private static readonly string[] TheClosedSet =
        { NotYetAvailable, NoSuchFactory, UnknownInterface, UnknownFactoryType };

    /// <summary>
    /// An object with a one-entry vtable, so a successful acquire has something
    /// real to hand back.
    /// </summary>
    private static void* AnObject()
    {
        void** table = (void**)NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        void** self = (void**)NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));

        *self = table;
        return self;
    }

    private static string NameAt(byte* utf8)
    {
        int n = 0;

        while (utf8[n] != 0)
            n++;

        return Encoding.UTF8.GetString(utf8, n);
    }

    private static void Say(byte* into, int size, string reason)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(reason);
        int n = Math.Min(bytes.Length, size - 1);

        for (int i = 0; i < n; i++)
            into[i] = bytes[i];

        into[n] = 0;
    }

    /// <summary>
    /// A switch that serves one name from one factory kind and produces each
    /// failure reason on demand, keyed on what it was asked for.
    /// </summary>
    private static NativeInterfaceFactoryTestSeam Seam(void* served = null, string? forcedReason = null)
        => new(served, forcedReason);

    private sealed class NativeInterfaceFactoryTestSeam
    {
        private readonly void* _served;
        private readonly string? _forced;

        public int Calls { get; private set; }

        public NativeInterfaceFactoryTestSeam(void* served, string? forced)
        {
            _served = served;
            _forced = forced;
        }

        public void* Acquire(SourceSharpFactoryType type, byte* version,
                             int* entries, byte* error, int errorBytes)
        {
            Calls++;

            if (entries != null)
                *entries = 0;

            string name = NameAt(version);

            if (_forced is not null)
            {
                Say(error, errorBytes, _forced);
                return null;
            }

            if (type is not (SourceSharpFactoryType.AppSystem or SourceSharpFactoryType.Physics
                             or SourceSharpFactoryType.FileSystem or SourceSharpFactoryType.Internal))
            {
                Say(error, errorBytes, UnknownFactoryType);
                return null;
            }

            if (type != SourceSharpFactoryType.AppSystem)
            {
                Say(error, errorBytes, NoSuchFactory);
                return null;
            }

            if (name != "TestInterface001" || _served == null)
            {
                Say(error, errorBytes, UnknownInterface);
                return null;
            }

            if (entries != null)
                *entries = 7;

            return _served;
        }
    }

    private static NativeInterfaceFactory Over(NativeInterfaceFactoryTestSeam seam)
    {
        ConstructorInfo ctor = typeof(NativeInterfaceFactory)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];

        Type fn = typeof(NativeInterfaceFactory).GetNestedType(
            "AcquireFn", BindingFlags.NonPublic)!;

        Delegate acquire = Delegate.CreateDelegate(fn, seam, nameof(seam.Acquire));

        return (NativeInterfaceFactory)ctor.Invoke(new object?[] { acquire });
    }

    private static bool TryCreate(NativeInterfaceFactory factory, SourceSharpFactoryType type,
                                  string name, out nint self, out int entries, out string? error)
    {
        object?[] args = { type, name, null, null, null };

        bool ok = (bool)typeof(NativeInterfaceFactory)
            .GetMethod("TryCreate", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(factory, args)!;

        self = (nint)args[2]!;
        entries = (int)args[3]!;
        error = (string?)args[4];
        return ok;
    }

    private static nint Create(NativeInterfaceFactory factory, SourceSharpFactoryType type,
                               string name, out int entries)
    {
        object?[] args = { type, name, null };

        try
        {
            nint self = (nint)typeof(NativeInterfaceFactory)
                .GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(factory, args)!;

            entries = (int)args[2]!;
            return self;
        }
        catch (TargetInvocationException wrapped)
        {
            // Reflection wraps whatever the method threw. The test is about the
            // inner exception's type and message, so it is what comes out.
            throw wrapped.InnerException!;
        }
    }

    //-------------------------------------------------------------------------
    // ONE FACT PER REASON LITERAL. Each asserts the reason arrives AS THAT
    // REASON rather than merely as a failure -- a factory that reported
    // "something went wrong" four times would pass a test that only checked
    // `false`.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheNotYetAvailableReasonArrivesAsItself()
    {
        var factory = Over(Seam(forcedReason: NotYetAvailable));

        Assert.False(TryCreate(factory, SourceSharpFactoryType.AppSystem,
                               "TestInterface001", out _, out _, out string? error),
                     "an acquire before the factories are captured fails");

        Assert.Contains(NotYetAvailable, error);
    }

    [Fact]
    public void TheNoSuchFactoryReasonArrivesAsItself()
    {
        var factory = Over(Seam(AnObject()));

        Assert.False(TryCreate(factory, SourceSharpFactoryType.Physics,
                               "TestInterface001", out _, out _, out string? error),
                     "a kind this side does not have fails");

        Assert.Contains(NoSuchFactory, error);
    }

    [Fact]
    public void TheUnknownInterfaceReasonArrivesAsItself()
    {
        var factory = Over(Seam(AnObject()));

        Assert.False(TryCreate(factory, SourceSharpFactoryType.AppSystem,
                               "NobodyRegisteredThis001", out _, out _, out string? error),
                     "a name no factory registered fails");

        Assert.Contains(UnknownInterface, error);
    }

    [Fact]
    public void TheUnknownFactoryTypeReasonArrivesAsItself()
    {
        var factory = Over(Seam(AnObject()));

        Assert.False(TryCreate(factory, (SourceSharpFactoryType)99,
                               "TestInterface001", out _, out _, out string? error),
                     "a factory kind native does not know fails");

        Assert.Contains(UnknownFactoryType, error);
    }

    //-------------------------------------------------------------------------
    // THE THROW/RETURN SPLIT.
    //-------------------------------------------------------------------------

    [Fact]
    public void TryCreateReturnsTheReasonWhereCreateThrowsIt()
    {
        var factory = Over(Seam(AnObject()));

        TryCreate(factory, SourceSharpFactoryType.AppSystem,
                  "NobodyRegisteredThis001", out _, out _, out string? error);

        var thrown = Assert.Throws<InterfaceAcquisitionException>(
            () => Create(factory, SourceSharpFactoryType.AppSystem,
                         "NobodyRegisteredThis001", out _));

        Assert.True(thrown.Message == error,
                    "Create's message is TryCreate's error, composed once");
    }

    [Fact]
    public void TheExceptionCarriesTheFactoryKindTheNameAndTheNativeReasonSeparately()
    {
        var factory = Over(Seam(AnObject()));

        var thrown = Assert.Throws<InterfaceAcquisitionException>(
            () => Create(factory, SourceSharpFactoryType.AppSystem,
                         "NobodyRegisteredThis001", out _));

        Assert.Equal(SourceSharpFactoryType.AppSystem, thrown.FactoryType);
        Assert.Equal("NobodyRegisteredThis001", thrown.InterfaceVersion);
        Assert.Equal(UnknownInterface, thrown.Reason);
    }

    [Fact]
    public void AcquisitionFailureIsCatchableDistinctlyFromInvalidOperation()
    {
        var factory = Over(Seam(AnObject()));

        Assert.False(typeof(InvalidOperationException)
                         .IsAssignableFrom(typeof(InterfaceAcquisitionException)),
                     "an acquisition failure is its own type, not a flavour of another");
    }

    //-------------------------------------------------------------------------
    // THE MESSAGE CARRIES THE CONTEXT MANAGED ADDS, which is the whole reason
    // native does not interpolate the caller's own string.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheComposedErrorNamesTheVersionStringNativeNeverEchoed()
    {
        var factory = Over(Seam(AnObject()));

        TryCreate(factory, SourceSharpFactoryType.AppSystem,
                  "NobodyRegisteredThis001", out _, out _, out string? error);

        Assert.Contains("NobodyRegisteredThis001", error);
        Assert.Contains("AppSystem", error);
    }

    //-------------------------------------------------------------------------
    // SUCCESS, THE CACHE, AND THE NO-STALE-ERROR RULE.
    //-------------------------------------------------------------------------

    [Fact]
    public void ASuccessfulAcquireCarriesThePointerAndTheEntryCount()
    {
        void* served = AnObject();
        var factory = Over(Seam(served));

        Assert.True(TryCreate(factory, SourceSharpFactoryType.AppSystem,
                              "TestInterface001", out nint self, out int entries, out _),
                    "a served name acquires");

        Assert.Equal((nint)served, self);
        Assert.Equal(7, entries);
    }

    [Fact]
    public void ASuccessfulAcquireAfterAFailedOneReportsNoError()
    {
        var factory = Over(Seam(AnObject()));

        TryCreate(factory, SourceSharpFactoryType.AppSystem,
                  "NobodyRegisteredThis001", out _, out _, out string? first);

        Assert.NotNull(first);

        TryCreate(factory, SourceSharpFactoryType.AppSystem,
                  "TestInterface001", out _, out _, out string? second);

        Assert.True(second is null,
                    "a success reports no reason at all, so a stale one cannot be read");
    }

    [Fact]
    public void ASecondAcquireOfTheSameNameDoesNotAskNativeAgain()
    {
        var seam = Seam(AnObject());
        var factory = Over(seam);

        TryCreate(factory, SourceSharpFactoryType.AppSystem, "TestInterface001", out _, out _, out _);
        TryCreate(factory, SourceSharpFactoryType.AppSystem, "TestInterface001", out _, out _, out _);

        Assert.True(seam.Calls == 1,
                    "the (side, kind, name) cache answers the second acquire without crossing");
    }

    [Fact]
    public void AFailedAcquireIsNotCached()
    {
        var seam = Seam(AnObject());
        var factory = Over(seam);

        TryCreate(factory, SourceSharpFactoryType.AppSystem, "NobodyRegisteredThis001", out _, out _, out _);
        TryCreate(factory, SourceSharpFactoryType.AppSystem, "NobodyRegisteredThis001", out _, out _, out _);

        Assert.True(seam.Calls == 2,
                    "a refusal is asked again -- an interface can arrive later, and a "
                    + "cached 'no' would outlive the reason for it");
    }

    //-------------------------------------------------------------------------
    // TRUNCATION.
    //-------------------------------------------------------------------------

    [Fact]
    public void AReasonLongerThanTheBufferComesBackTruncatedAndTerminated()
    {
        string overlong = new('x', Vtable.MaxAcquireError * 2);
        var factory = Over(Seam(forcedReason: overlong));

        TryCreate(factory, SourceSharpFactoryType.AppSystem,
                  "TestInterface001", out _, out _, out string? error);

        // The composed message is the reason plus managed's own context, so the
        // REASON's length is what is bounded, not the message's.
        int xs = 0;

        foreach (char c in error!)
            if (c == 'x')
                xs++;

        Assert.True(xs == Vtable.MaxAcquireError - 1,
                    $"a reason is truncated to {Vtable.MaxAcquireError - 1} bytes plus a "
                    + $"terminator, and {xs} came back");
    }

    [Fact]
    public void TruncationDoesNotRefuseTheReport()
    {
        string overlong = new('x', Vtable.MaxAcquireError * 2);
        var factory = Over(Seam(forcedReason: overlong));

        TryCreate(factory, SourceSharpFactoryType.AppSystem,
                  "TestInterface001", out _, out _, out string? error);

        // NOT `IsNullOrEmpty(error)`, AND THAT WAS A CHECK THAT COULD NOT FAIL.
        // The composed message always carries managed's own context -- the
        // factory kind and the version string -- so it is non-empty however
        // little native said, and this arm stayed green against the very
        // mutation it exists to catch (a reason at the buffer's length being
        // discarded). What has to survive is the REASON, so that is what is
        // asserted: some of the reported text, and not the placeholder that
        // stands in when nothing was reported at all.
        Assert.Contains("x", error);
        Assert.DoesNotContain("no reason was reported", error);
    }

    //-------------------------------------------------------------------------
    // THE CLOSED SET, which is complete rather than a sample precisely because
    // the set is closed. Its gtest twin walks the same enum natively.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryReasonLiteralIsPureAscii()
    {
        foreach (string reason in TheClosedSet)
            foreach (char c in reason)
                Assert.True(c < 0x80,
                            $"\"{reason}\" is ASCII, which is what makes the no-rune-splitting "
                            + "guarantee true by construction rather than by discipline");
    }

    [Fact]
    public void NoReasonLiteralCarriesAFormatSpecifier()
    {
        foreach (string reason in TheClosedSet)
            Assert.DoesNotContain("%", reason);
    }

    [Fact]
    public void TheClosedSetHasExactlyFourReasons()
        // Four cases, four literals. The number is here because the set being
        // CLOSED is the load-bearing property: a fifth reason arriving without
        // this arm moving is a reason nothing above has been asserted about.
        => Assert.Equal(4, TheClosedSet.Length);

    [Fact]
    public void EveryReasonLiteralFitsTheBufferWithRoomToSpare()
    {
        foreach (string reason in TheClosedSet)
            Assert.True(reason.Length < Vtable.MaxAcquireError - 1,
                        $"\"{reason}\" fits {Vtable.MaxAcquireError} bytes without truncating");
    }

    //-------------------------------------------------------------------------
    // THE BUFFER IS OPTIONAL.
    //-------------------------------------------------------------------------

    [Fact]
    public void AFactoryTypeAndNameAreEnoughToFailWithoutReadingAStaleBuffer()
    {
        // The seam writes nothing at all, which is what a native diagnostic path
        // with a NULL buffer looks like from managed's side.
        var silent = new SilentSeam();
        var factory = Over2(silent);

        Assert.False(TryCreate(factory, SourceSharpFactoryType.AppSystem,
                               "TestInterface001", out _, out _, out string? error),
                     "a null answer is a failure whether or not a reason was written");

        Assert.Contains("no reason was reported", error);
    }

    private sealed class SilentSeam
    {
        public void* Acquire(SourceSharpFactoryType type, byte* version,
                             int* entries, byte* error, int errorBytes)
        {
            if (entries != null)
                *entries = 0;

            if (error != null && errorBytes > 0)
                error[0] = 0;

            return null;
        }
    }

    private static NativeInterfaceFactory Over2(SilentSeam seam)
    {
        ConstructorInfo ctor = typeof(NativeInterfaceFactory)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];

        Type fn = typeof(NativeInterfaceFactory).GetNestedType(
            "AcquireFn", BindingFlags.NonPublic)!;

        return (NativeInterfaceFactory)ctor.Invoke(
            new object?[] { Delegate.CreateDelegate(fn, seam, nameof(seam.Acquire)) });
    }
}

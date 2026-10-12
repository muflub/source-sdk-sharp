using System;
using System.IO;
using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE MANAGED HALF OF <c>plan_entity.md</c> §3.8 STEP 3b — the id with a
/// serial, and the agreement between this side's decoding of it and native's.
///
/// <para>
/// What the table DOES is pinned on the native leg by
/// <c>sourcesharp/test/physics_ids_test.cpp</c>, because it is arithmetic over
/// PODs with no managed side to it. What is pinned HERE is the one thing that
/// leg cannot see: that <see cref="PhysicsId"/> takes the word apart the same
/// way the header puts it together. A stand-in for native drifts silently, so
/// the agreement is asserted rather than assumed — a split that moved on one
/// side alone would build clean on both and resolve a live id to a different
/// slot, which is the exact bug the serial exists to prevent, reintroduced by
/// the fix.
/// </para>
/// </summary>
public class PhysicsIdTests
{
    private static string Native(string relative)
        => File.ReadAllText(Path.Combine(RepoLayout.SdkTree, "src", relative));

    /// <summary>
    /// The native text with comments blanked. Every prose block in these files
    /// quotes the constants it is about — the header's own commentary names
    /// "12 bits of index and 20 of serial" in a sentence — so a fact matched
    /// against the raw text would pass on a file whose actual <c>#define</c>
    /// had been deleted.
    /// </summary>
    private static string NativeCode(string relative)
        => SourceFileNamesExistTests.StripComments(Native(relative));

    // The constraint and spring table: managed's own since
    // plan_physics_bones_native_zero 2.4 (native's left with shell_constraints).
    private const string Constraints = "sourcesharp/managed/SourceSharp.Server/PhysicsConstraints.cs";
    // The motion-controller table: managed's own since 2.4.
    private const string Controllers = "sourcesharp/managed/SourceSharp.Server/PhysicsMotionControllers.cs";

    //=========================================================================
    // THE SPLIT, BY VALUE. It was pinned to physics_shell.h's SS_PHYSID_* and
    // physics_shell.cpp's asserts until plan_physics_bones_native_zero 2.3
    // deleted the native id table (no native producer was left); the numbers
    // are the ones that table chose, hand-copied from it at bc21e4d46.
    //=========================================================================

    [Fact]
    public void TheSplitIsTwelveBitsOfIndexAndTwentyOfSerial()
    {
        Assert.Equal(12, PhysicsId.IndexBits);
        Assert.Equal(20, PhysicsId.SerialBits);
        Assert.Equal(4095, PhysicsId.MaxSlots);
    }

    [Fact]
    public void TheFieldsTileTheWordOnThisSideToo()
    {
        Assert.Equal(32, PhysicsId.IndexBits + PhysicsId.SerialBits);
        Assert.Equal(0xFFFu, PhysicsId.IndexMask);
        Assert.Equal(0xFFFFFu, PhysicsId.SerialMask);
    }

    //=========================================================================
    // THE SENTINEL. Zero is safe here and is a trap on the entity side, and
    // the difference is worth a fact of its own.
    //=========================================================================

    [Fact]
    public void NoneIsZeroAndNamesNoSlot()
    {
        Assert.True(PhysicsId.None.IsNone);
        Assert.Equal(0u, PhysicsId.None.Value);
        Assert.Equal(-1, PhysicsId.None.Index);
    }

    [Fact]
    public void SlotZeroIsARealIdAndIsNotNone()
    {
        // The combination that would BE zero if the low field held the raw
        // index: the first slot, never yet reused. On the entity side the
        // equivalent word is worldspawn and reads as "no entity"; here it must
        // not.
        var first = PhysicsId.Make(0, 0);

        Assert.False(first.IsNone);
        Assert.Equal(0, first.Index);
        Assert.Equal(0u, first.Serial);
        Assert.Equal(1u, first.Value);
    }

    [Fact]
    public void NoIdOfAnySlotOrGenerationIsEverZero()
    {
        for (int slot = 0; slot < PhysicsId.MaxSlots; slot += 37)
        {
            Assert.False(PhysicsId.Make(slot, 0).IsNone, $"slot {slot}");
            Assert.False(PhysicsId.Make(slot, PhysicsId.SerialMask).IsNone, $"slot {slot}");
        }
    }

    [Fact]
    public void MakeRefusesASlotTheTableDoesNotHave()
    {
        Assert.True(PhysicsId.Make(PhysicsId.MaxSlots, 0).IsNone);
        Assert.True(PhysicsId.Make(-1, 3).IsNone);
    }

    //=========================================================================
    // THE SERIAL.
    //=========================================================================

    [Fact]
    public void TheTwoFieldsSurviveARoundTripAtTheirWidest()
    {
        var id = PhysicsId.Make(PhysicsId.MaxSlots - 1, PhysicsId.SerialMask);

        Assert.Equal(PhysicsId.MaxSlots - 1, id.Index);
        Assert.Equal(PhysicsId.SerialMask, id.Serial);
    }

    [Fact]
    public void TwoGenerationsOfOneSlotAreDifferentWords()
    {
        var stale = PhysicsId.Make(17, 4);
        var live = PhysicsId.Make(17, 5);

        Assert.Equal(stale.Index, live.Index);
        Assert.NotEqual(stale, live);
        Assert.NotEqual(stale.Value, live.Value);
    }

    [Fact]
    public void ALargeSerialDoesNotBleedIntoTheIndex()
    {
        // The failure this catches is a shift by the wrong field width: the
        // serial would land over the index and the id would name slot
        // something-else while still decoding to a plausible number.
        var id = PhysicsId.Make(1, PhysicsId.SerialMask);

        Assert.Equal(1, id.Index);
    }

    [Fact]
    public void ToStringNamesTheSlotAndTheGenerationRatherThanTheWord()
    {
        Assert.Equal("#17:5", PhysicsId.Make(17, 5).ToString());
        Assert.Equal("none", PhysicsId.None.ToString());
    }

    [Fact]
    public void EqualityIsTheWholeWordAndNotTheSlot()
    {
        Assert.Equal(PhysicsId.Make(3, 9), PhysicsId.Make(3, 9));
        Assert.True(PhysicsId.Make(3, 9) == PhysicsId.Make(3, 9));
        Assert.True(PhysicsId.Make(3, 9) != PhysicsId.Make(3, 10));
    }

    //=========================================================================
    // THE FLAT SLOTS ARE RETARGETED. Absence facts, so each is paired with the
    // presence of what replaced it -- a file that failed to load, or one this
    // step had emptied by accident, would otherwise pass the absence half.
    //=========================================================================

    [Fact]
    public void TheConstraintVectorIsGoneAndTheIdTableIsWhatIsThere()
    {
        string code = NativeCode(Constraints);

        Assert.DoesNotContain("g_Constraints", code);
        Assert.Contains("PhysicsJointKind.Constraint", code);
    }

    [Fact]
    public void TheSpringVectorIsGoneAndTheIdTableIsWhatIsThere()
    {
        string code = NativeCode(Constraints);

        Assert.DoesNotContain("g_Springs", code);
        Assert.Contains("PhysicsJointKind.Spring", code);
    }

    [Fact]
    public void TheMotionControllerVectorIsGoneAndTheIdTableIsWhatIsThere()
    {
        string code = NativeCode(Controllers);

        Assert.DoesNotContain("g_MotionControllers", code);
        Assert.Contains("PhysicsJointKind.Controller", code);
    }

    [Fact]
    public void EveryConstraintLookupNamesTheKindItExpects()
    {
        // The kind argument is what stops a spring id resolving into a
        // constraint's row. A Lookup that dropped it would compile and would
        // answer the wrong live pointer.
        // Managed's table since 2.4: Lookup<T>( id, PhysicsJointKind.X ).
        string code = NativeCode(Constraints);

        foreach (Match m in Regex.Matches(code, @"\.Lookup<\w+>\(\s*[A-Za-z_][A-Za-z0-9_]*\s*,\s*([A-Za-z_][A-Za-z0-9_.]*)\s*\)"))
            Assert.StartsWith("PhysicsJointKind.", m.Groups[1].Value);

        Assert.NotEmpty(Regex.Matches(code, @"\.Lookup<"));
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two stage enums: named positions, spaced so nothing has to be renumbered.
///
/// <para>
/// <b>Why the spacing is worth a test.</b> Renumbering is the operation to
/// avoid: a participant whose stage value moved still compiles, still runs, and
/// simply runs somewhere else — there is no error, only a different game. The
/// gap is what makes inserting a stage never require moving another one, so it
/// is an invariant rather than a style preference.
/// </para>
/// </summary>
public class LifecycleStageTests
{
    private const int Gap = 100;

    private static (string Name, int Value)[] Members<TEnum>() where TEnum : struct, Enum
        => Enum.GetValues<TEnum>()
            .Select(v => (v.ToString()!, Convert.ToInt32(v)))
            .OrderBy(x => x.Item2)
            .ToArray();

    public static TheoryData<Type> StageEnums => new() { typeof(LifecycleStage), typeof(FrameStage) };

    // ------------------------------------------------------------------
    // The spacing.
    // ------------------------------------------------------------------

    [Fact]
    public void EveryLifecycleStageIsAMultipleOfTheGap()
        => AssertSpacing(Members<LifecycleStage>());

    [Fact]
    public void EveryFrameStageIsAMultipleOfTheGap()
        => AssertSpacing(Members<FrameStage>());

    /// <summary>
    /// A stage has to fit between any two of these, so no two may be adjacent
    /// by value.
    /// </summary>
    private const int MinRoom = 2;

    /// <summary>
    /// THE ASSERTION USED TO CONTRADICT ITS OWN REASON, and this is the repair.
    ///
    /// <para>
    /// It required every value to be a multiple of 100 AND every consecutive
    /// pair to be exactly 100 apart, with the reason written above:
    /// <i>"the gap is what makes inserting a stage never require moving another
    /// one"</i>. Read literally, those two clauses make an insertion IMPOSSIBLE
    /// — any value between two multiples of 100 fails the first clause and both
    /// its neighbours fail the second. The three earlier phases that added
    /// stages all appended at the tail, which is the one case the old form
    /// allowed, so nothing had noticed.
    /// </para>
    ///
    /// <para>
    /// plan_gamedll_after.md Phase A3's FIFTH system is the case that could not
    /// append. <c>CSourceSharpStandaloneSystem</c>'s frame stage ran before
    /// every existing frame participant, so its value has to be below
    /// <see cref="FrameStage.Soundscapes"/> — and between
    /// <see cref="FrameStage.Unspecified"/> (0, fixed: it is the interface
    /// default) and 100 there is no legal value at all under the old rule. The
    /// only way to satisfy it was to renumber the other five, which is the
    /// operation the reason above says to avoid and which
    /// <c>FrameLifecycleTests.EveryFrameParticipantStillDeclaresTheStageItDeclaredBefore</c>
    /// refuses by name.
    /// </para>
    ///
    /// <para>
    /// <b>So the two clauses are replaced by the two properties they were
    /// standing in for</b>, and both still bite: no pair may be adjacent (a
    /// closed gap is a stage that cannot be inserted), and no pair may be
    /// further apart than <see cref="Gap"/> (a bigger jump is a hole that reads
    /// as a deleted stage). "Somebody renumbered a participant" is not this
    /// fact's job and never was — it is the two per-participant records in
    /// <c>LifecycleTests</c> and <c>FrameLifecycleTests</c>, which name every
    /// participant and the value it declared.
    /// </para>
    /// </summary>
    private static void AssertSpacing((string Name, int Value)[] members)
    {
        foreach (var (name, value) in members)
        {
            Assert.True(value >= 0,
                name + " = " + value + " is negative, so it sorts below "
                + "Unspecified -- which is where a participant with no opinion "
                + "lands and is supposed to be first.");
        }

        for (int i = 1; i < members.Length; i++)
        {
            int room = members[i].Value - members[i - 1].Value;

            Assert.True(room >= MinRoom,
                members[i - 1].Name + " and " + members[i].Name + " are " + room
                + " apart, want at least " + MinRoom + ". A closed gap is a stage "
                + "that cannot be inserted between them without renumbering one, "
                + "and renumbering a participant is a silent change: it compiles, "
                + "it runs, it runs elsewhere.");

            Assert.True(room <= Gap,
                members[i - 1].Name + " and " + members[i].Name + " are " + room
                + " apart, want at most " + Gap + ". A jump bigger than the gap "
                + "reads as a deleted stage -- a reader cannot tell it from a "
                + "value that used to be there.");
        }
    }

    [Fact]
    public void EveryStageValueIsDistinct()
    {
        foreach (var members in new[] { Members<LifecycleStage>(), Members<FrameStage>() })
        {
            var duplicates = members.GroupBy(m => m.Value).Where(g => g.Count() > 1).ToArray();

            Assert.True(duplicates.Length == 0,
                "stages share a value: " + string.Join("; ",
                    duplicates.Select(g => g.Key + " = " + string.Join(", ", g.Select(x => x.Name))))
                + ". Two names for one position is two participants whose "
                + "relative order is then decided by registration order rather "
                + "than by either name.");
        }
    }

    /// <summary>
    /// Both enums start at an explicit zero, and it is the interface default.
    /// A participant that never states a stage has to land somewhere defined.
    /// </summary>
    [Fact]
    public void UnspecifiedIsZeroAndIsWhatTheInterfacesDefaultTo()
    {
        Assert.Equal(0, (int)LifecycleStage.Unspecified);
        Assert.Equal(0, (int)FrameStage.Unspecified);

        // Through the interface: a default implementation is not on the class.
        Assert.Equal((int)LifecycleStage.Unspecified, ((IGameLifecycle)new Silent()).LifecycleOrder);
        Assert.Equal((int)FrameStage.Unspecified, ((IFrameLifecycle)new SilentFrame()).FrameOrder);
    }

    private sealed class Silent : IGameLifecycle { }
    private sealed class SilentFrame : IFrameLifecycle { }

    // ------------------------------------------------------------------
    // The two enums are separate, and stay separate.
    // ------------------------------------------------------------------

    /// <summary>
    /// A frame stage must not be usable where a level stage is meant. They are
    /// separate types precisely because a service's position in one says
    /// nothing about its position in the other — Soundscapes is last at level
    /// scope and first in the frame — and both erase to int, so nothing else
    /// would report the mix-up.
    /// </summary>
    [Fact]
    public void TheTwoEnumsAreDistinctTypes()
    {
        Assert.NotEqual(typeof(LifecycleStage), typeof(FrameStage));

        Assert.True((int)LifecycleStage.Soundscapes > (int)LifecycleStage.Arsenal,
            "Soundscapes is expected LAST at level scope");
        Assert.True((int)FrameStage.Soundscapes < (int)FrameStage.Arsenal,
            "...and FIRST in the frame. One enum could not say both.");
    }

    // ------------------------------------------------------------------
    // Every participant names a stage rather than a number.
    // ------------------------------------------------------------------

    /// <summary>
    /// The point of the enums is that the order can be read off one list. A
    /// participant answering a bare integer is not in that list, so the list
    /// stops being the answer — and it still compiles, because the property is
    /// an int either way.
    /// </summary>
    [Fact]
    public void EveryParticipantsStageIsANamedStage()
    {
        var levelValues = Enum.GetValues<LifecycleStage>().Select(v => (int)v).ToHashSet();
        var frameValues = Enum.GetValues<FrameStage>().Select(v => (int)v).ToHashSet();

        foreach (var type in ParticipantTypes())
        {
            if (typeof(IGameLifecycle).IsAssignableFrom(type))
            {
                int order = Declared<IGameLifecycle>(type, "get_LifecycleOrder");
                Assert.True(levelValues.Contains(order),
                    type.Name + " answers LifecycleOrder " + order + ", which is "
                    + "not a LifecycleStage. The enum is meant to be the whole "
                    + "list of positions; a bare number is invisible in it.");
            }

            if (typeof(IFrameLifecycle).IsAssignableFrom(type))
            {
                int order = Declared<IFrameLifecycle>(type, "get_FrameOrder");
                Assert.True(frameValues.Contains(order),
                    type.Name + " answers FrameOrder " + order + ", which is not "
                    + "a FrameStage.");
            }
        }
    }

    /// <summary>Every participant this build ships, found rather than listed.</summary>
    private static IEnumerable<Type> ParticipantTypes()
        => typeof(GameRules).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                     && (typeof(IGameLifecycle).IsAssignableFrom(t)
                      || typeof(IFrameLifecycle).IsAssignableFrom(t)))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    /// <summary>
    /// There ARE participants to check. A reflection sweep that found none
    /// would pass every assertion above it without testing anything.
    /// </summary>
    [Fact]
    public void TheSweepFindsTheParticipants()
    {
        var found = ParticipantTypes().ToArray();

        Assert.True(found.Length >= 7,
            "found only " + found.Length + " participant(s): "
            + string.Join(", ", found.Select(t => t.Name)));
    }

    private static int Declared<TIface>(Type t, string getter)
    {
        var target = t.GetInterfaceMap(typeof(TIface)).TargetMethods
                      .FirstOrDefault(m => m.Name.EndsWith(getter, StringComparison.Ordinal));

        if (target is null)
            return typeof(TIface) == typeof(IGameLifecycle)
                ? (int)LifecycleStage.Unspecified
                : (int)FrameStage.Unspecified;

        return (int)target.Invoke(
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t), null)!;
    }
}

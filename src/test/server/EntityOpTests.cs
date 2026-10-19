using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;
using SourceSharp.Server.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// THE OP TABLE — plan_entity.md §3.4, step 1f.
///
/// <para>
/// Three things are held here and they fail in three different ways.
/// </para>
///
/// <list type="number">
///   <item>
///   <b>The numbers.</b> They cross the ABI as a bare int and as bit positions.
///   Renumbering one side turns every touch into a think, and nothing would say
///   so — the C# class would simply start receiving the wrong callback, which
///   reads as a bug in the C# and is not one. Pinned here, pinned again in
///   <c>test/entity_op_test.cpp</c>, and cross-read between the two so a pin
///   on one side cannot go stale while the other moves.
///   </item>
///   <item>
///   <b>The layout.</b> <c>OpArgs</c> is one struct declared twice. It is
///   passed by pointer and read field-for-field, so a disagreement is a wild
///   read rather than a compile error.
///   </item>
///   <item>
///   <b>The mask.</b> The override bitmask is the crossing budget. A bit that
///   should be set and is not is an entity that silently stops thinking; a bit
///   that should be clear and is not is a crossing nobody needed. Both
///   directions are asserted.
///   </item>
/// </list>
/// </summary>
public sealed class EntityOpTests
{
    // This test class's own counters (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's counts.
    private readonly Crossings _crossings = new();

    //-------------------------------------------------------------------------
    // 1. The numbers.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every op's value, written out. Not a loop over the enum — a loop would
    /// re-derive the numbers from the declaration under test and pass whatever
    /// they became.
    /// </summary>
    [Fact]
    public void OpNumbersAreFixed()
    {
        Assert.Equal(0, (int)EntityOp.Spawn);
        Assert.Equal(1, (int)EntityOp.KeyValue);
        Assert.Equal(2, (int)EntityOp.Input);
        Assert.Equal(3, (int)EntityOp.Think);
        Assert.Equal(4, (int)EntityOp.Removed);
        Assert.Equal(5, (int)EntityOp.StartTouch);
        Assert.Equal(6, (int)EntityOp.Touch);
        Assert.Equal(7, (int)EntityOp.EndTouch);
        Assert.Equal(8, (int)EntityOp.Impact);
        Assert.Equal(9, (int)EntityOp.MoveDone);
        Assert.Equal(10, (int)EntityOp.Blocked);
        Assert.Equal(11, (int)EntityOp.Damage);
        Assert.Equal(12, (int)EntityOp.Killed);
        Assert.Equal(13, (int)EntityOp.FilterPasses);
        Assert.Equal(14, (int)EntityOp.PositionChanged);
        Assert.Equal(15, (int)EntityOp.PhysicsCreated);
    }

    /// <summary>
    /// 0..N-1 with no gaps, which is what lets <see cref="Crossings"/> index its
    /// per-op counter array by the enum value directly and what lets the shell
    /// range-check with one unsigned compare.
    /// </summary>
    [Fact]
    public void OpValuesAreContiguousFromZero()
    {
        var values = Enum.GetValues<EntityOp>().Select(v => (int)v).OrderBy(v => v).ToArray();

        Assert.Equal(Enumerable.Range(0, values.Length).ToArray(), values);
    }

    /// <summary>
    /// The pins above name every op there is. Without this, an op added to the
    /// enum and to native would be unpinned on this side and could be
    /// renumbered freely.
    /// </summary>
    [Fact]
    public void EveryOpIsPinnedAbove()
    {
        string source = Read(
            "src/sourcesharp/managed/SourceSharp.Server.Tests/EntityOpTests.cs");

        int start = source.IndexOf("public void OpNumbersAreFixed()", StringComparison.Ordinal);
        int end = source.IndexOf('}', source.IndexOf('{', start));
        string body = source[start..end];

        var unpinned = Enum.GetNames<EntityOp>()
            .Where(n => !body.Contains($"EntityOp.{n})", StringComparison.Ordinal))
            .ToArray();

        Assert.True(unpinned.Length == 0,
            "these ops have no pinned number: " + string.Join(", ", unpinned));
    }

    //-------------------------------------------------------------------------
    // The other side of the boundary, read off the filesystem.
    //-------------------------------------------------------------------------

    private const string NativeHeader = "src/sourcesharp/game/shared/entity_op.h";

    /// <summary>
    /// <c>ENTITY_OP_START_TOUCH</c> is <c>StartTouch</c>. The two spellings are
    /// a convention, so it is written down once and both directions use it.
    /// </summary>
    private static string NativeNameOf(EntityOp op)
        => "ENTITY_OP_" + Regex.Replace(op.ToString(), "(?<!^)([A-Z])", "_$1").ToUpperInvariant();

    private static Dictionary<string, int> NativeOps()
    {
        string header = Read(NativeHeader);
        var ops = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(header, @"^\t(ENTITY_OP_\w+) = (\d+),", RegexOptions.Multiline))
            ops[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);

        return ops;
    }

    /// <summary>
    /// The parser finds something. A cross-check whose subject it cannot read
    /// is a check that cannot fail, and this file's whole job is the two sides
    /// agreeing.
    /// </summary>
    [Fact]
    public void TheNativeHeaderCanBeRead()
    {
        Assert.True(NativeOps().Count >= Enum.GetValues<EntityOp>().Length,
            "entity_op.h parsed to fewer enumerators than C# has ops; the header moved or the "
            + "declaration is no longer one-per-line");
    }

    /// <summary>
    /// Name for name and number for number. A pin on one side alone cannot see
    /// a renumbering on the other, which is the whole failure this catches.
    /// </summary>
    [Fact]
    public void TheNativeEnumAgreesOpForOp()
    {
        var native = NativeOps();
        var wrong = new List<string>();

        foreach (EntityOp op in Enum.GetValues<EntityOp>())
        {
            string name = NativeNameOf(op);

            if (!native.TryGetValue(name, out int value))
                wrong.Add($"{op} has no {name} in entity_op.h");
            else if (value != (int)op)
                wrong.Add($"{op} is {(int)op} here and {value} as {name}");
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>
    /// And nothing extra on the native side. An op native knows about and C#
    /// does not is a dispatch this build would drop on the floor.
    /// </summary>
    [Fact]
    public void TheNativeEnumHasNoOpsCsharpLacks()
    {
        var known = Enum.GetValues<EntityOp>().Select(NativeNameOf).ToHashSet(StringComparer.Ordinal);

        // ENTITY_OP_COUNT is the array bound, not an op.
        var extra = NativeOps().Keys
            .Where(n => n != "ENTITY_OP_COUNT" && !known.Contains(n))
            .ToArray();

        Assert.True(extra.Length == 0,
            "entity_op.h declares ops C# has never heard of: " + string.Join(", ", extra));
    }

    /// <summary>
    /// <c>ENTITY_OP_COUNT</c> is one past the last op on both sides, because it
    /// is what the shell range-checks against.
    /// </summary>
    [Fact]
    public void TheNativeCountIsOnePastTheLastOp()
    {
        Assert.Equal(Enum.GetValues<EntityOp>().Length, NativeOps()["ENTITY_OP_COUNT"]);
    }

    //-------------------------------------------------------------------------
    // 2. The layout.
    //-------------------------------------------------------------------------

    /// <summary>
    /// 64 bytes, asserted rather than trusted. The struct is laid out so there
    /// is no interior padding at all; a field added in the wrong place would
    /// introduce some, and the two compilers would then be free to disagree
    /// about where everything after it lives.
    /// </summary>
    [Fact]
    public void OpArgsIsSixtyFourBytes()
    {
        Assert.Equal(64, Marshal.SizeOf<OpArgs>());
    }

    /// <summary>
    /// Every field where the native declaration puts it. The struct crosses by
    /// pointer and is read field-for-field, so a disagreement is a wild read
    /// rather than a compile error.
    /// </summary>
    [Fact]
    public void OpArgsFieldsAreWhereNativePutsThem()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<OpArgs>(nameof(OpArgs.Blob)));
        Assert.Equal(8, (int)Marshal.OffsetOf<OpArgs>(nameof(OpArgs.BlobBytes)));
        Assert.Equal(12, (int)Marshal.OffsetOf<OpArgs>(nameof(OpArgs.Other)));
        Assert.Equal(16, (int)Marshal.OffsetOf<OpArgs>(nameof(OpArgs.Text0)));
        Assert.Equal(24, (int)Marshal.OffsetOf<OpArgs>(nameof(OpArgs.Text1)));
        Assert.Equal(32, (int)Marshal.OffsetOf<OpArgs>(nameof(OpArgs.Text2)));
        Assert.Equal(40, (int)Marshal.OffsetOf<OpArgs>(nameof(OpArgs.Vec0)));
        Assert.Equal(48, (int)Marshal.OffsetOf<OpArgs>(nameof(OpArgs.Vec1)));
        Assert.Equal(56, (int)Marshal.OffsetOf<OpArgs>(nameof(OpArgs.Float0)));
        Assert.Equal(60, (int)Marshal.OffsetOf<OpArgs>(nameof(OpArgs.Int0)));
    }

    /// <summary>
    /// And the offsets above cover every field. A field added without a pin
    /// would otherwise be free to sit anywhere.
    /// </summary>
    [Fact]
    public void EveryOpArgsFieldIsPinned()
    {
        string source = Read(
            "src/sourcesharp/managed/SourceSharp.Server.Tests/EntityOpTests.cs");

        int start = source.IndexOf("public void OpArgsFieldsAreWhereNativePutsThem()",
                                   StringComparison.Ordinal);
        int end = source.IndexOf('}', source.IndexOf('{', start));
        string body = source[start..end];

        var unpinned = typeof(OpArgs)
            .GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(f => f.Name)
            .Where(n => !body.Contains($"OpArgs.{n})", StringComparison.Ordinal))
            .ToArray();

        Assert.True(unpinned.Length == 0,
            "these OpArgs fields have no pinned offset: " + string.Join(", ", unpinned));
    }

    //-------------------------------------------------------------------------
    // 3. The mask.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Four ops, and exactly four. The set is the crossing budget's floor: an
    /// op added to it is a crossing nobody can ever switch off again, so it
    /// wants to be a decision rather than a drift.
    /// </summary>
    [Fact]
    public void TheAlwaysMaskIsTheFourBookkeepingOps()
    {
        Assert.Equal(
            new[] { EntityOp.Spawn, EntityOp.KeyValue, EntityOp.Removed, EntityOp.FilterPasses },
            EntityOpMask.Armed(EntityOpMask.Always));
    }

    /// <summary>
    /// The native constant is the same four. A native side with a wider floor
    /// would pay crossings managed code thinks it has switched off; a narrower
    /// one would switch off the call that CREATES the managed entity.
    /// </summary>
    [Fact]
    public void TheNativeAlwaysMaskIsTheSameFour()
    {
        string header = Read(NativeHeader);

        int start = header.IndexOf("#define ENTITY_OP_ALWAYS_MASK", StringComparison.Ordinal);
        Assert.True(start > 0, "entity_op.h no longer defines ENTITY_OP_ALWAYS_MASK");

        int end = header.IndexOf("ENTITY_OP_FILTER_PASSES ) )", start, StringComparison.Ordinal);
        Assert.True(end > start, "the always-mask macro no longer ends where this fact reads it");

        string macro = header[start..(end + "ENTITY_OP_FILTER_PASSES ) )".Length)];

        var named = Regex.Matches(macro, @"ENTITY_OP_BIT\( (ENTITY_OP_\w+) \)")
                         .Select(m => m.Groups[1].Value)
                         .ToArray();

        Assert.Equal(
            EntityOpMask.Armed(EntityOpMask.Always).Select(NativeNameOf).ToArray(),
            named);
    }

    /// <summary>Bit n is 1 &lt;&lt; n, which is what makes the shell's test one AND.</summary>
    [Fact]
    public void TheBitIsTheShift()
    {
        foreach (EntityOp op in Enum.GetValues<EntityOp>())
            Assert.Equal(1u << (int)op, EntityOpMask.Bit(op));
    }

    //-------------------------------------------------------------------------
    // Reflection over real classes.
    //-------------------------------------------------------------------------

    private sealed class Inert : ScriptedEntity
    {
    }

    private sealed class Thinker : ScriptedEntity
    {
        public override void OnThink()
        {
        }
    }

    private sealed class Toucher : ScriptedEntity
    {
        public override void OnTouch(EntityHandle toucher)
        {
        }
    }

    private class TouchingBase : ScriptedEntity
    {
        public override void OnTouch(EntityHandle toucher)
        {
        }
    }

    private sealed class TouchingLeaf : TouchingBase
    {
    }

    private sealed class Breakable : ScriptedEntity
    {
        public override int OnDamaged(EntityHandle attacker, float damage, DamageType type) => 1;
    }

    private sealed class Glass : ScriptedEntity
    {
        public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType type,
                                        Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic) => 1;
    }

    /// <summary>
    /// A class that overrides nothing arms nothing beyond the floor. This is
    /// the crossing saving, stated as a fact: on <c>dm_lockdown</c> most
    /// entities are of exactly this shape.
    /// </summary>
    [Fact]
    public void AClassThatOverridesNothingArmsOnlyTheFloor()
    {
        Assert.Equal(EntityOpMask.Always, EntityOpMask.For(typeof(Inert), hasBoundInputs: false));
    }

    [Fact]
    public void OverridingOnThinkArmsThink()
    {
        uint mask = EntityOpMask.For(typeof(Thinker), hasBoundInputs: false);

        Assert.Equal(EntityOpMask.Always | EntityOpMask.Bit(EntityOp.Think), mask);
    }

    /// <summary>
    /// One override, three ops and an impact. The shell fires them at three
    /// different rates but the managed contract has one call for "somebody is
    /// in me", so a class arms or disarms all of them together.
    /// </summary>
    [Fact]
    public void OverridingOnTouchArmsTheThreeTouchOpsAndTheImpact()
    {
        uint mask = EntityOpMask.For(typeof(Toucher), hasBoundInputs: false);

        Assert.Equal(
            EntityOpMask.Always
                | EntityOpMask.Bit(EntityOp.StartTouch)
                | EntityOpMask.Bit(EntityOp.Touch)
                | EntityOpMask.Bit(EntityOp.Impact),
            mask);
    }

    /// <summary>
    /// And not EndTouch, which has its own virtual. A mask that armed the
    /// leaving edge from the arriving one would put a crossing back on every
    /// pickup in the map.
    /// </summary>
    [Fact]
    public void OverridingOnTouchDoesNotArmEndTouch()
    {
        uint mask = EntityOpMask.For(typeof(Toucher), hasBoundInputs: false);

        Assert.Equal(0u, mask & EntityOpMask.Bit(EntityOp.EndTouch));
    }

    /// <summary>
    /// A LEAF THAT INHERITS THE OVERRIDE STILL ARMS IT. The obvious spelling of
    /// this check — <c>DeclaringType != typeof(ScriptedEntity)</c> compared
    /// against the leaf — gets this wrong: the leaf declares nothing, so the
    /// bit would be dropped and every entity of that class would silently stop
    /// being touched.
    /// </summary>
    [Fact]
    public void ALeafInheritsItsBasesOverride()
    {
        uint mask = EntityOpMask.For(typeof(TouchingLeaf), hasBoundInputs: false);

        Assert.NotEqual(0u, mask & EntityOpMask.Bit(EntityOp.Touch));
    }

    [Fact]
    public void OverridingOnDamagedArmsDamage()
    {
        uint mask = EntityOpMask.For(typeof(Breakable), hasBoundInputs: false);

        Assert.Equal(EntityOpMask.Always | EntityOpMask.Bit(EntityOp.Damage), mask);
    }

    /// <summary>
    /// The other damage virtual arms the same op. Native calls one op for both,
    /// so a mask that read only <c>OnDamaged</c> would make every glass pane in
    /// the game invulnerable.
    /// </summary>
    [Fact]
    public void OverridingOnDamagedAtArmsDamageToo()
    {
        uint mask = EntityOpMask.For(typeof(Glass), hasBoundInputs: false);

        Assert.Equal(EntityOpMask.Always | EntityOpMask.Bit(EntityOp.Damage), mask);
    }

    /// <summary>
    /// A bound <c>[ScriptedEvent]</c> arms Input on its own. Such a method is
    /// reached WITHOUT passing through <c>OnInput</c>, so a class that binds one
    /// and overrides nothing would otherwise never hear an input — every
    /// logic_relay in the map would stop firing.
    /// </summary>
    [Fact]
    public void ABoundInputArmsInputWithNoOverride()
    {
        uint mask = EntityOpMask.For(typeof(Inert), hasBoundInputs: true);

        Assert.Equal(EntityOpMask.Always | EntityOpMask.Bit(EntityOp.Input), mask);
    }

    /// <summary>
    /// <see cref="ScriptedEntity"/> itself overrides nothing of its own.
    /// </summary>
    [Fact]
    public void TheBaseClassOverridesNoneOfItsOwnVirtuals()
    {
        foreach (var names in EntityOpMask.Virtuals.Values)
            foreach (string name in names)
                Assert.False(EntityOpMask.Overrides(typeof(ScriptedEntity), name),
                             $"{name} counted as an override on the class that declares it");
    }

    /// <summary>
    /// Every name in the table is a real virtual on <see cref="ScriptedEntity"/>.
    /// A typo, or a rename of the virtual, would otherwise silently mean "no
    /// class ever overrides this op" — a mask that is quietly always clear.
    /// </summary>
    [Fact]
    public void EveryNamedVirtualExists()
    {
        foreach (var (op, names) in EntityOpMask.Virtuals)
        {
            foreach (string name in names)
            {
                var found = typeof(ScriptedEntity)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.Name == name && m.IsVirtual)
                    .ToArray();

                Assert.True(found.Length > 0, $"{op} names {name}, which is not a virtual here");
            }
        }
    }

    /// <summary>
    /// EVERY OP IS ACCOUNTED FOR: either it cannot be switched off, or it names
    /// the virtual its bit is computed from. An op in neither set would be
    /// dispatched by native and never armed by managed code — a callback that
    /// silently never arrives.
    /// </summary>
    [Fact]
    public void EveryInboundOpIsEitherAlwaysOnOrHasAVirtual()
    {
        var unaccounted = Enum.GetValues<EntityOp>()
            // PositionChanged is OUTBOUND — managed telling native — so there is
            // no bit for the shell to test and no virtual to reflect over.
            .Where(op => op != EntityOp.PositionChanged)
            .Where(op => (EntityOpMask.Always & EntityOpMask.Bit(op)) == 0
                         && !EntityOpMask.Virtuals.ContainsKey(op))
            .ToArray();

        Assert.True(unaccounted.Length == 0,
            "these ops are neither always-on nor computed from a virtual: "
            + string.Join(", ", unaccounted));
    }

    //-------------------------------------------------------------------------
    // The counters the override-bit gate is read out of.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <see cref="Crossings"/> counts per op, not only per thunk.
    ///
    /// <para>
    /// This is what lets the gate tell "the op was not dispatched" from "the op
    /// was dispatched and did nothing". One <c>ServerDispatch</c> counter
    /// carries fourteen events; a claim about one of them cannot be tested
    /// against a total that lumps thinks in with keyvalues.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCountersAreKeptPerOp()
    {
        _crossings.Reset();

        _crossings.CountEntityOp((int)EntityOp.Think);
        _crossings.CountEntityOp((int)EntityOp.Think);
        _crossings.CountEntityOp((int)EntityOp.Touch);

        Assert.Equal(2, _crossings.Read(EntityOp.Think));
        Assert.Equal(1, _crossings.Read(EntityOp.Touch));
        Assert.Equal(0, _crossings.Read(EntityOp.Killed));
    }

    /// <summary>
    /// A reset zeroes the per-op half too. A stage that reset the totals and
    /// kept these would report a rate against a frame count from a different
    /// interval.
    /// </summary>
    [Fact]
    public void AResetZeroesThePerOpCounters()
    {
        _crossings.CountEntityOp((int)EntityOp.Blocked);
        _crossings.Reset();

        Assert.Equal(0, _crossings.Read(EntityOp.Blocked));
    }

    /// <summary>
    /// An op number outside the enum is IGNORED rather than throwing. It runs
    /// above the thunk's try/catch, and the only way to reach it is a native
    /// side newer than this managed build — a case that must degrade, not
    /// unwind into the engine's C++ frames.
    /// </summary>
    [Fact]
    public void AnUnknownOpNumberIsIgnored()
    {
        _crossings.Reset();

        _crossings.CountEntityOp(int.MaxValue);
        _crossings.CountEntityOp(-1);

        Assert.Empty(_crossings.ActiveEntityOps());
    }

    /// <summary>
    /// One counter per op, so indexing the name table by the enum value is safe.
    /// </summary>
    [Fact]
    public void ThereIsOneCounterPerOp()
    {
        Assert.Equal(Enum.GetValues<EntityOp>().Length, Crossings.EntityOpCount);
    }

    //-------------------------------------------------------------------------
    // The thunk, and what it replaced.
    //-------------------------------------------------------------------------

    // The side's load, which left the Bootstrap's Entry.cs for each side's own
    // entry at plan_side_entries SE2: where a thunk would be wired if it came back.
    private const string EntryPath =
        "src/sourcesharp/managed/SourceSharp.Shared.Interop/SideLoad.cs";

    // DecodeArgs and the dispatch moved here at plan_engineapi_vtables E9.
    private const string BindingPath =
        "src/sourcesharp/managed/SourceSharp.Server.Interop/EntitiesBinding.cs";

    /// <summary>
    /// THE TWELVE THUNKS ARE GONE, not merely unused.
    ///
    /// <para>
    /// The slots stay declared — removing thirteen pointers from the middle of
    /// <c>SourceSharpManagedApi</c> renumbers every slot behind them — but a
    /// managed thunk nothing calls is dead code, and dead code that still
    /// counts a crossing would put twelve names back in the report that can
    /// never be non-zero. If one came back, the honest reading of its counter
    /// would be "this op crossed", and it would be false.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTwelvePerEventThunksAreGone()
    {
        string entry = Read(EntryPath);

        string[] gone =
        {
            "ServerOnScriptedSpawn", "ServerOnScriptedKeyValue", "ServerOnScriptedInput",
            "ServerOnScriptedThink", "ServerOnScriptedRemoved", "ServerOnScriptedDamaged",
            "ServerOnScriptedKilled", "ServerOnScriptedMoveDone", "ServerOnScriptedBlocked",
            "ServerOnScriptedEndTouch", "ServerOnEntityTouched", "ServerOnScriptedFilterPasses",
        };

        // The DEFINITION and the ADDRESS-OF, not any mention: one of these
        // names survives in a comment explaining what the dispatch thunk's
        // liveness-mirror push used to belong to, and a fact that banned the
        // word would be banning the explanation.
        var left = gone
            .Where(n => Regex.IsMatch(entry, @"private static [^\n]*\b" + n + @"\s*\(")
                        || entry.Contains("&" + n, StringComparison.Ordinal))
            .ToArray();

        Assert.True(left.Length == 0,
            "SideLoad.cs still defines or takes the address of thunks the op table "
            + "replaced: " + string.Join(", ", left));

        // And the ban is not vacuous: the file was read.
        Assert.Contains("LoadSideV2", entry, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the slots they filled are left NULL. A wiring left behind would be a
    /// live route round the override bit — the shell would still have a slot to
    /// call, and a later native edit could start calling it again with nothing
    /// to say the crossing had come back.
    /// </summary>
    [Fact]
    public void TheSlotsThoseThunksFilledAreLeftUnwired()
    {
        string entry = Read(EntryPath);

        string[] slots =
        {
            "pOut->OnScriptedSpawn", "pOut->OnScriptedKeyValue", "pOut->OnScriptedInput",
            "pOut->OnScriptedThink", "pOut->OnScriptedRemoved", "pOut->OnScriptedDamaged",
            "pOut->OnScriptedKilled", "pOut->OnScriptedMoveDone", "pOut->OnScriptedBlocked",
            "pOut->OnScriptedEndTouch", "pOut->OnEntityTouched", "pOut->OnScriptedFilterPasses",
        };

        var wired = slots.Where(s => entry.Contains(s + " ", StringComparison.Ordinal)
                                     || entry.Contains(s + "=", StringComparison.Ordinal))
                         .ToArray();

        Assert.True(wired.Length == 0,
            "SideLoad.cs still wires slots the op table replaced: " + string.Join(", ", wired));
    }

    /// <summary>
    /// <c>Dispatch</c> is wired on the SERVER branch and not the client's. The
    /// client's entity half is a later phase; a slot wired on the wrong side is
    /// a null on the side that needs it.
    ///
    /// <para>
    /// Asserted by SPLITTING the method at the client arm rather than searching
    /// the whole file, the way
    /// <c>ClientFrameStageTests.OnlyTheClientBranchWiresClientStage</c> does it.
    /// </para>
    /// </summary>
    [Fact]
    public void DispatchIsTheServersManagedBuiltAreaMethod()
    {
        // Since plan_engineapi_vtables E9 Dispatch is a method of the
        // managed-built IManagedEntities (EntitiesBinding), on the server only,
        // and no arm of Entry.cs wires a struct slot for it.
        string entry = Read(EntryPath);
        string binding = Read(BindingPath);

        Assert.DoesNotContain("pOut->Dispatch", entry);
        Assert.Contains("public int Dispatch(uint a0, int a1, void* a2)", binding);
    }

    /// <summary>
    /// A NULL <c>Dispatch</c> KILLS THE SERVER AT LOAD rather than at play.
    ///
    /// <para>
    /// It is the one route every entity event takes. A null makes every relay
    /// return early, so no map entity ever reaches C# — no keyvalue lands, no
    /// instance is built, no trigger fires — and not one message anywhere says
    /// why, because every relay guards the pointer. That is precisely what
    /// <c>SOURCESHARP_SHELL_REQUIRED</c> is for.
    /// </para>
    /// </summary>
    [Fact]
    public void TheEntitiesAreaIsRequiredAtLoad()
    {
        // The area is acquired on every load at its compiler's count, and a
        // refusal fails the load -- the guard SOURCESHARP_SHELL_REQUIRED was. The
        // retired struct slot must not be required any more.
        string acquire = Read("src/sourcesharp/game/shared/shell_managed_areas.inc");
        string required = Read("src/sourcesharp/game/server/managed_runtime_server.cpp");

        Assert.Contains("ManagedAccept< IManagedEntities >", acquire);
        Assert.DoesNotContain("f( Dispatch )", required);
    }

    //-------------------------------------------------------------------------
    // THE INPUT VALUE'S FIELD TYPE, ACROSS THE WIRE -- the `sub-variant` lane's
    // one-line ask, landed by `abi-tail`.
    //
    // Two ends of one wire, read from the two files that hold them and compared.
    // Neither half is worth anything alone: the shim writing nInt0 with nobody
    // reading it is a one-sided slot, and Entry.cs reading an Int0 nobody writes
    // is a plausible zero. A one-sided slot that answers a plausible zero has
    // cost this port two lanes already.
    //-------------------------------------------------------------------------

    private const string DispatchPath =
        "src/sourcesharp/game/server/shell_dispatch.cpp";

    /// <summary>
    /// The <c>EntityOp.Input</c> arm of <c>DecodeArgs</c> carries
    /// <c>Int0</c> through as <see cref="EntityOpArgs.Flags"/>.
    ///
    /// <para>
    /// Read out of the arm ITSELF and not out of the whole file: every other
    /// case in that switch already spells <c>Flags = args-&gt;Int0</c>, so a
    /// whole-file needle would have been green before this line existed, which
    /// is check-that-cannot-fail number forty-four.
    /// </para>
    /// </summary>
    [Fact]
    public void TheInputArmCarriesTheFieldTypeThrough()
        => Assert.Contains("Flags = args->Int0", DecodeArm("Input"));

    /// <summary>
    /// And the arm this fact reads is really the Input one — it carries the two
    /// strings and the number that only Input takes. Without this the extractor
    /// could be handing back some other arm and the fact above would still pass.
    /// </summary>
    [Fact]
    public void TheArmThisReadsIsTheInputArm()
    {
        string arm = DecodeArm("Input");

        Assert.Contains("Text1 = Utf8(args->Text1)", arm);
        Assert.Contains("Text2 = Utf8(args->Text2)", arm);
        Assert.Contains("Number = args->Float0", arm);
    }

    /// <summary>
    /// The extractor STOPS at the end of its arm. A slice that ran on into the
    /// next case would find <c>Flags = args-&gt;Int0</c> in
    /// <c>EntityOp.Damage</c> and report the wire as present with the line
    /// deleted.
    /// </summary>
    [Fact]
    public void TheArmExtractorStopsAtTheEndOfTheArm()
    {
        string arm = DecodeArm("Input");

        Assert.DoesNotContain("case EntityOp.Damage", arm);
        Assert.DoesNotContain("DamageType", arm);
    }

    /// <summary>
    /// The far end WRITES it, in <c>DispatchInput</c> and nowhere else on that
    /// path: <c>args.nInt0 = nFieldType + 1</c>.
    /// </summary>
    [Fact]
    public void TheShimWritesTheFieldTypePlusOne()
    {
        string dispatch = Read(DispatchPath);

        int start = dispatch.IndexOf("static bool DispatchInput(", StringComparison.Ordinal);
        Assert.True(start > 0, "shell_dispatch.cpp no longer defines DispatchInput where this fact reads it");

        // To the next definition at column zero, so the slice is DispatchInput's
        // body and not the whole file.
        int end = dispatch.IndexOf("\n}\n", start, StringComparison.Ordinal);
        Assert.True(end > start, "DispatchInput's body does not close where this fact reads it");

        Assert.Contains("args.nInt0    = nFieldType + 1;", Strip(dispatch[start..end]));
    }

    /// <summary>
    /// PLUS ONE, and the plus one is the whole point: zero has to mean "the shim
    /// did not say" rather than <c>FIELD_VOID</c>, which is a real type. A
    /// mutation to a bare <c>nFieldType</c> reddens this.
    /// </summary>
    [Fact]
    public void TheShimNeverWritesTheBareFieldType()
    {
        string dispatch = Strip(Read(DispatchPath));

        Assert.DoesNotContain("args.nInt0    = nFieldType;", dispatch);
        Assert.DoesNotContain("args.nInt0 = nFieldType;", dispatch);
    }

    /// <summary>
    /// One arm of <c>DecodeArgs</c>, from its <c>case</c> label to the
    /// <c>};</c> that closes the object initialiser, with comments stripped so
    /// that prose about a field is not read as the field.
    /// </summary>
    private static string DecodeArm(string op)
    {
        string entry = Read(BindingPath);

        int decode = entry.IndexOf("private static EntityOpArgs DecodeArgs", StringComparison.Ordinal);
        Assert.True(decode > 0, "EntitiesBinding.cs no longer declares DecodeArgs where this reads it");

        int start = entry.IndexOf("case EntityOp." + op + ":", decode, StringComparison.Ordinal);
        Assert.True(start > 0, $"DecodeArgs has no {op} arm");

        int end = entry.IndexOf("\n                };", start, StringComparison.Ordinal);
        Assert.True(end > start, $"the {op} arm is not the object-initialiser shape this reads");

        return Strip(entry[start..end]);
    }

    /// <summary>
    /// Line comments out. Prose counts as code to a raw grep, and both files
    /// this compares NAME the thing under test in their comments.
    /// </summary>
    private static string Strip(string text)
        => string.Join("\n", text.Split('\n').Select(l =>
        {
            int i = l.IndexOf("//", StringComparison.Ordinal);
            return i < 0 ? l : l[..i];
        }));

    //-------------------------------------------------------------------------

    private static string Read(string relative) => File.ReadAllText(Locate(relative));

    /// <summary>
    /// The file in the compat farm. A scan that read nothing would
    /// pass every check that looks inside it, so a miss is a failure and not a
    /// skip: these are cross-boundary facts and there is no useful half of them.
    /// </summary>
    private static string Locate(string relative)
    {
        string? candidate = RepoLayout.FindSdkFile(relative);
        if (candidate is not null)
            return candidate;

        Assert.Fail($"{relative} was not found in {RepoLayout.SdkTree}; a scan that "
                    + "read nothing would pass every check that looks inside it");
        return "";
    }
}

extern alias client;
using HudMessageArgs = client::SourceSharp.Shared.HudMessageArgs;
using DamageMessage = client::SourceSharp.Shared.DamageMessage;
using HudMsgMessage = client::SourceSharp.Shared.HudMsgMessage;
using HudTextMessage = client::SourceSharp.Shared.HudTextMessage;
using KeyHintTextMessage = client::SourceSharp.Shared.KeyHintTextMessage;
using SourceSharp.Server;
using SourceSharp.Shared;
using client::SourceSharp.Client;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;
using SourceSharp.Abi;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The checks belonging to no particular subsystem: the ABI's own shapes,
/// handle semantics, convar parsing, entity layout, the blittable structs.
///
/// <para>
/// This file was the hand-rolled runner as well as a test file — a Main, a
/// pass/fail tally, and every suite in the tree dispatched from one list. xUnit
/// is the runner now, so what is left is the tests that were always here.
/// </para>
/// </summary>
public class CoreTests
{

    //-------------------------------------------------------------------------

    /// <summary>
    /// Regression test for a real bug: there are two engine tables (client.so
    /// and server.so). A single static table meant the second LoadSide
    /// overwrote the first, so the server's entity functions read back null at
    /// runtime. Each side is its own <see cref="EngineSide"/> instance now
    /// (plan_abi_hotreload D8), so initialising one leaves the other alone.
    /// </summary>
    [Fact]
    public void EngineIsPerSide()
    {
        var server = new EngineSide(Side.Server);
        var client = new EngineSide(Side.Client);

        Assert.True(!server.IsInitialized && !client.IsInitialized, "a fresh side is not initialised");
        server.Initialize();
        Assert.True(server.IsInitialized, "initialising the server side initialises it");
        Assert.True(!client.IsInitialized, "and leaves the client side as it was");
    }

    [Fact]
    public void EntityHandleSemantics()
    {
        var a = new EntityHandle(0x0001_0002u);
        var b = new EntityHandle(0x0001_0002u);
        var c = new EntityHandle(0x0001_0003u);

        Assert.True(a == b && a != c, "handle equality by value");
        Assert.True(a.GetHashCode() == b.GetHashCode(), "handle hash matches value");
        Assert.True(a.Value == 0x0001_0002u, "handle round-trips its raw value");
        Assert.True(EntityHandle.None.Value == uint.MaxValue, "None is the invalid sentinel");

        // Without an engine, nothing resolves — the side's entity area must
        // fail closed rather than throw. Asked through an unattached side's
        // own area: the handle no longer calls the engine itself
        // (plan_abi_hotreload D8).
        var entities = ScriptedEntityRegistry.UnattachedEngine().Require<IEngineEntities>();
        Assert.True(!entities.IsValid(a), "invalid handle is not valid");
        Assert.True(entities.GetIndex(a) == -1, "invalid handle index is -1");
        Assert.True(entities.GetClassName(a).Length == 0, "invalid handle classname is empty");
        Assert.True(a.ToString() == "#2", "a handle prints its entry without asking the engine");
        Assert.True(EntityHandle.None.ToString() == "(invalid handle)", "the invalid handle ToString is explicit");
    }

    [Fact]
    public void A_bool_or_int_convar_reads_a_decimal_default_as_the_engine_does()
    {
        // ConVar::InternalSetValue: m_fValue = atof, m_nValue = (int)m_fValue;
        // GetBool is m_nValue != 0. Stock declares mp_allowspectators "1.0",
        // which a whole-string integer parse read as false.
        var i = new TestIntConVar();
        var b = new TestBoolConVar();

        Bind(b, "cvar_b", "1.0");
        Assert.True(b.Value, "\"1.0\" is true");
        Bind(b, "cvar_b", "0.5");
        Assert.True(!b.Value, "\"0.5\" truncates to 0, false");
        Bind(i, "cvar_i", "3.7");
        Assert.True(i.Value == 3, "an int convar truncates \"3.7\" to 3");
    }

    [Fact]
    public void ConVarParsing()
    {
        var i = new TestIntConVar();
        var f = new TestFloatConVar();
        var b = new TestBoolConVar();
        var s = new TestStringConVar();

        Bind(i, "cvar_i", "42");
        Bind(f, "cvar_f", "1.5");
        Bind(b, "cvar_b", "1");
        Bind(s, "cvar_s", "hello");

        Assert.True(i.Value == 42, "int convar parses");
        Assert.True(Math.Abs(f.Value - 1.5f) < 0.0001f, "float convar parses");
        Assert.True(b.Value, "bool convar parses non-zero as true");
        Assert.True(s.Value == "hello", "string convar passes through");

        // Unparseable input must not throw — it reads as the type's zero.
        Bind(i, "cvar_i", "not-a-number");
        Assert.True(i.Value == 0, "bad int reads as 0");

        Bind(b, "cvar_b", "0");
        Assert.True(!b.Value, "bool convar parses 0 as false");

        // Change notification must deliver both old and new.
        var tracked = new TestIntConVar();
        Bind(tracked, "cvar_t", "1");
        tracked.Observed.Clear();
        Notify(tracked, "7", "1");
        Assert.True(tracked.Observed.Count == 1 && tracked.Observed[0] == (1, 7), "convar OnChanged sees old and new");
        Assert.True(tracked.Value == 7, "convar caches the new value");
    }

    [Fact]
    public void ConVarRegistryRejectsBadDeclarations()
    {
        var registry = new ConVarRegistry(TestServices.UnattachedConsole());

        // No engine, so registration cannot succeed — the point is that it
        // reports rather than throwing, and binds each convar to what it
        // declared anyway. A refused convar still counts, which is the whole
        // reason it can be examined here.
        //
        // Counted loosely on purpose: discovery reads every loaded assembly, so
        // this sees the mod's convars as well as the two ConVarDeclarationTests
        // declares, and pinning the total would make an unrelated new convar
        // fail this test. What matters is that the scan completed and the
        // declarations it was pointed at came back.
        int n = registry.RegisterAll(typeof(CoreTests).Assembly);
        Assert.True(n >= 2, "convar registry survives a no-engine scan");
        Assert.True(registry.Get<BoundedTestConVar>() is not null, "convar registry finds a declaration in the scanned assemblies");
        Assert.True(!registry.NotifyChanged(999, "a", "b"), "convar registry reports unknown ids");
    }

    [Fact]
    public void ConCommandRegistryRejectsBadDeclarations()
    {
        var registry = new ConCommandRegistry();

        // No engine, so every registration was refused — and, as with the
        // convars above, a refused command is KEPT: it was still declared, it
        // was still built, and it is still what the listing has to show. It has
        // no id, which is the only thing an engine would have given it.
        //
        // Counted loosely for the same reason as the convars: discovery reads
        // every loaded assembly, so this sees the mod's commands as well as the
        // ones ServiceRegistryTests declares, and pinning the total would make
        // an unrelated new command fail this test.
        int n = registry.RegisterAll(typeof(CoreTests).Assembly, TestServices.UnattachedConsole());
        Assert.True(n >= 1, "concommand registry survives a no-engine scan");
        Assert.True(registry.ByName("ss_test_di_plain") is not null, "concommand registry keeps what the engine refused");
        Assert.True(registry.All.Any(c => c.Name == "ss_test_di_plain" && !c.IsRegistered), "and marks it as unregistered rather than dropping it");
        Assert.True(!registry.Dispatch(999, ""), "concommand registry reports unknown ids");
    }

    [Fact]
    public unsafe void UserCmdIsBlittableAndButtonsWork()
    {
        // The native side memcpys this struct, so its size must be stable and
        // it must contain no managed references.
        Assert.True(!System.Runtime.CompilerServices
            .RuntimeHelpers.IsReferenceOrContainsReferences<UserCmd>(), "UserCmd is blittable");

        var cmd = default(UserCmd);
        cmd.Buttons = (int)(InputButtons.Jump | InputButtons.Duck);

        Assert.True(cmd.IsPressed(InputButtons.Jump), "IsPressed finds a set button");
        Assert.True(cmd.IsPressed(InputButtons.Duck), "IsPressed finds the other set button");
        Assert.True(!cmd.IsPressed(InputButtons.Attack), "IsPressed rejects an unset button");

        cmd.Pitch = 10f; cmd.Yaw = 20f; cmd.Roll = 30f;
        Assert.True(cmd.Pitch == 10f && cmd.Yaw == 20f && cmd.Roll == 30f, "view angles round-trip");
    }

    [Fact]
    public void DiagnosticsDescribeIncludesChain()
    {
        var inner = new InvalidOperationException("inner cause");
        var outer = new ApplicationException("outer failure", inner);

        string text = ManagedDiagnostics.Describe(outer);

        Assert.True(text.Contains("outer failure"), "Describe names the outer exception");
        Assert.True(text.Contains("inner cause"), "Describe follows the inner chain");
        Assert.True(text.Contains("caused by"), "Describe marks causation");
        Assert.True(ManagedDiagnostics.Describe(null).Length > 0, "Describe handles null");
    }

    /// <summary>
    /// The banned-names rule is pure string logic, so it is tested directly
    /// rather than needing a running server.
    /// </summary>
    [Fact]
    public void AdmissionRuleLogic()
    {
        Assert.True(IsBanned("", "alice") == false, "empty ban list admits everyone");
        Assert.True(IsBanned("mallory", "mallory"), "exact name is refused");
        Assert.True(IsBanned("Mallory", "mallory"), "match is case-insensitive");
        Assert.True(IsBanned("alice, mallory , bob", "mallory"), "list entries are trimmed");
        Assert.True(IsBanned("alice,bob", "carol") == false, "non-listed name is admitted");
        Assert.True(IsBanned("mallory", "mal") == false, "partial name is NOT refused");
        Assert.True(IsBanned(",,,", "alice") == false, "empty entries are ignored");
    }

    // Mirrors ServerModule.OnClientConnect's rule.
    private static bool IsBanned(string bannedList, string name)
    {
        foreach (var entry in bannedList.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(entry.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The relationship hook is tri-state: true, false, or null meaning "use the
    /// stock rule". The bootstrap maps that to 1 / 0 / -1, and getting the null
    /// case wrong would silently override engine behaviour.
    /// </summary>
    [Fact]
    public void RelationshipTriState()
    {
        Assert.True(Encode(true) == 1, "true maps to teammate");
        Assert.True(Encode(false) == 0, "false maps to not-teammate");
        Assert.True(Encode(null) == -1, "null maps to use-stock");
    }

    private static int Encode(bool? r) => r is null ? -1 : (r.Value ? 1 : 0);

    /// <summary>
    /// The property that matters most: client and server generate this layout
    /// independently, so identical metadata must produce byte-identical offsets.
    /// Reflection order is not guaranteed stable, which is why the generator
    /// sorts explicitly — this test is what stops that guarantee regressing.
    /// </summary>
    [Fact]
    public void EntityLayoutIsDeterministic()
    {
        var a = EntityLayout.Build(typeof(SampleEntity));
        var b = EntityLayout.Build(typeof(SampleEntity));

        Assert.True(a.Fields.Count == b.Fields.Count, "layout field count is stable");
        Assert.True(a.Size == b.Size, "layout size is stable");

        bool identical = true;
        for (int i = 0; i < a.Fields.Count; i++)
        {
            if (a.Fields[i].Name != b.Fields[i].Name ||
                a.Fields[i].Offset != b.Fields[i].Offset ||
                a.Fields[i].Size != b.Fields[i].Size)
            {
                identical = false;
            }
        }
        Assert.True(identical, "layout is byte-identical across builds");

        // Layout follows source declaration order, via metadata token — never
        // reflection order, which carries no guarantee. Reordering the properties
        // in SampleEntity is a wire-format change and should break this.
        Assert.True(string.Join(",", a.Fields.Select(f => f.Name)) == "Health,Armor,Ammo,Alive,Origin", "layout follows declaration order");
    }

    [Fact]
    public void EntityLayoutAlignsAndSizes()
    {
        var layout = EntityLayout.Build(typeof(SampleEntity));

        // Alignment is measured against the ELEMENT width, not the field size:
        // a Vector is three 4-byte floats, so a multiple of 4 is correct and a
        // multiple of 12 would be an accident of its total size.
        foreach (var f in layout.Fields)
        {
            int align = f.Type == typeof(Vector) ? sizeof(float) : f.Size;
            Assert.True(f.Offset % align == 0, $"{f.Name} is naturally aligned");
        }

        Assert.True(layout["Health"].Size == 4, "Health is an int");
        Assert.True(layout["Alive"].Size == 1, "Alive is one byte");
        Assert.True(layout["Origin"].Size == 12, "Origin is 12 bytes");
        Assert.True(layout.Size >= layout.Fields.Max(f => f.Offset + f.Size), "size covers the last field");
        Assert.True(layout.Size % 4 == 0, "size is 4-byte aligned");
        Assert.True(!layout.TryGet("NoSuchField", out _), "unknown field lookup fails cleanly");
    }

    /// <summary>
    /// An unsupported field type must throw, not be silently skipped — a missing
    /// field would desync client and server in a way that is very hard to trace
    /// back to its cause.
    /// </summary>
    [Fact]
    public void EntityLayoutRejectsUnsupportedTypes()
    {
        bool threw = false;
        try { EntityLayout.Build(typeof(BadEntity)); }
        catch (NotSupportedException) { threw = true; }

        Assert.True(threw, "unsupported field type throws");
    }

    // ENTITYDATAFAILSCLOSED WENT WITH ITS SUBJECT -- step 8a.
    //
    // It asserted that `static class EntityData`'s four accessors fail closed
    // with no engine: a read from a missing blob answers false and leaves the
    // out-param at its zero, a write to one is refused, a blob shorter than the
    // field is refused rather than read past the end of borrowed native memory.
    // The class is deleted (see EntityData.cs's closing block for the
    // differential that proved it dead), so there is nothing left to fail
    // closed.
    //
    // NOTHING IS UNCOVERED BY THIS. The fail-closed rule it checked belongs to
    // the path that replaced it: NetBlobView's bounds checks over the generated
    // layout, and Engine.GetEntityBlob answering an empty span with no engine --
    // both of which have their own facts. What is NOT re-covered anywhere is the
    // `EntityFieldLayout`-with-an-absurd-offset case, because no API takes a
    // hand-built field layout any more.

    //-------------------------------------------------------------------------
    // The field kinds the schema hash is computed over.
    //
    // These used to be the wire description handed to native, one field at a
    // time, and native built a typed prop from each. Step 7 deleted that: what
    // these numbers decide now is the layout FINGERPRINT, which both sides
    // compute over their own copy of the declarations and which travels with
    // every update. Renumbering one is still a wire change on whichever side is
    // rebuilt, so they are still pinned.
    //-------------------------------------------------------------------------
    [Fact]
    public void NetworkedLayoutTests()
    {
        var layout = EntityLayout.Build(typeof(SampleEntity));

        Assert.True(NetworkedLayout.TypeCodeOf(typeof(int))    == 0, "int maps to type 0");
        Assert.True(NetworkedLayout.TypeCodeOf(typeof(float))  == 1, "float maps to type 1");
        Assert.True(NetworkedLayout.TypeCodeOf(typeof(Vector)) == 2, "vector maps to type 2");
        Assert.True(NetworkedLayout.TypeCodeOf(typeof(bool))   == 3, "bool maps to type 3");

        // Determinism is the whole contract, and more of it than before: client
        // and server compute these offsets independently and nothing native
        // arbitrates between them any more. Two builds over the same type must
        // reach the same layout and the same fingerprint.
        var again = EntityLayout.Build(typeof(SampleEntity));

        Assert.True(again.Size == layout.Size && again.Fields.Count == layout.Fields.Count &&
            again.Fields.Zip(layout.Fields).All(
                p => p.First.Name == p.Second.Name && p.First.Offset == p.Second.Offset &&
                     p.First.Size == p.Second.Size), "the layout is deterministic");

        Assert.True(again.Fingerprint == layout.Fingerprint, "and so is the fingerprint the wire carries");

        // A Vector is 12 bytes of 4-byte floats, so it aligns to 4 and must
        // pack tight against the field before it. Aligning to the whole 12-byte
        // size instead rounds to a multiple of 12 — not an alignment, and it
        // leaves a hole. This was caught by dumping the built table in-game.
        var lay = EntityLayout.Build(typeof(PackedEntity));
        Assert.True(lay["After"].Offset == 4 && lay["Vec"].Offset == 8, "vector aligns to 4, not to its size");
        Assert.True(lay.Size == 20, "no hole is left before a vector");

        // Declaration order, which is what the offsets are laid out in and what
        // both sides have to agree on with nothing arbitrating between them.
        Assert.True(string.Join(",", again.Fields.Select(f => f.Name)) ==
            string.Join(",", layout.Fields.Select(f => f.Name)), "field order is stable");
    }

    /// <summary>Declaration order here IS the expected layout order.</summary>
    private sealed class SampleEntity
    {
        [EntityField] public int Health { get; set; }
        [EntityField] public int Armor { get; set; }
        [EntityField] public int Ammo { get; set; }
        [EntityField] public bool Alive { get; set; }
        [EntityField] public Vector Origin { get; set; }
        public int NotAField { get; set; }
    }

    //-------------------------------------------------------------------------
    // User messages. The registration ORDER is the wire protocol — a message
    // travels as an index into the list — so these tests are about ordering and
    // about refusing anything that would shift an index, not just about parsing.
    //-------------------------------------------------------------------------
    [Fact]
    public void UserMessageTests()
    {
        // Payload parsing. Every read is bounds-checked because the bytes come
        // off the network and a malformed message must not fault the runtime.
        var bytes = new byte[]
        {
            0x2A,                       // byte   42
            0x00, 0x00,                 // short  0
            0x01, 0x00, 0x00, 0x00,     // int    1  (little-endian)
            (byte)'h', (byte)'i', 0x00, // string "hi"
        };
        var r = new UserMessageReader(bytes);

        Assert.True(r.ReadByte() == 0x2A, "reads a byte");
        Assert.True(r.ReadShort() == 0, "reads a short");
        Assert.True(r.ReadInt() == 1, "reads an int");
        Assert.True(r.ReadString() == "hi", "reads a string");
        Assert.True(r.Remaining == 0, "payload fully consumed");
        Assert.True(!r.Overran, "no overrun on a well-formed read");

        // Past the end: default value, flagged, and no throw.
        var empty = new UserMessageReader(Array.Empty<byte>());
        Assert.True(empty.ReadInt() == 0, "read past the end returns default");
        Assert.True(empty.Overran, "read past the end is flagged");

        // An unterminated string must stop at the end, not run off it.
        var noTerm = new UserMessageReader(new byte[] { (byte)'a', (byte)'b' });
        Assert.True(noTerm.ReadString() == "ab", "unterminated string stops at the end");
        Assert.True(noTerm.Overran, "unterminated string is flagged");

        // Ordering IS the protocol: the index a message travels under is its
        // registration position, so these pin the sequence rather than just
        // checking the set.
        var registry = new UserMessageRegistry(new RecordingLog());
        Assert.True(!Throws(() =>
            registry.RegisterAll(typeof(SourceSharp.Shared.FadeMessage).Assembly)), "the declared set registers");

        var msgs = registry.Messages;
        // Counted from the assembly rather than hardcoded: every declared message
        // must register, because skipping one shifts every later wire index.
        int declaredCount = typeof(SourceSharp.Shared.FadeMessage).Assembly.GetTypes()
            .Count(t => t.GetCustomAttribute<UserMessageAttribute>() is not null);
        Assert.True(msgs.Count == declaredCount, "every declared message registers");
        Assert.True(msgs.All(m => m.Name.Length != 0), "names come from the attribute");

        // KEY ORDER (plan_side_isolation P2b): an id is its key's place in the
        // ordinal order of the keys, never the scan's or the source's order.
        // The SsUserMessages table carries it to each client.
        Assert.True(msgs.Select(m => m.Name).SequenceEqual(msgs.Select(m => m.Name).Order(StringComparer.Ordinal)),
            "ids follow the ordinal order of the keys");
        Assert.True(msgs.Select(m => m.Index).SequenceEqual(Enumerable.Range(0, msgs.Count)),
            "each declaration's id is its place in that order");

        // Two independent registries over the same assembly must agree exactly,
        // which is the property the client relies on across restarts.
        var again = new UserMessageRegistry(new RecordingLog());
        again.RegisterAll(typeof(SourceSharp.Shared.FadeMessage).Assembly);
        Assert.True(msgs.Select(m => m.Name).SequenceEqual(again.Messages.Select(m => m.Name)), "registration order is deterministic");

        Assert.True(msgs.Select(m => m.Name).Distinct().Count() == msgs.Count, "names are unique");

        // Dispatch by an index that came off the wire must be range-checked.
        Assert.True(!Throws(() => registry.Dispatch(9999, Array.Empty<byte>())), "out-of-range dispatch is ignored");
        Assert.True(!Throws(() => registry.Dispatch(-1, Array.Empty<byte>())), "negative dispatch is ignored");

        // A Fade payload really does reach the declared handler. Found by name
        // rather than by a fixed index: the index is whatever declaration order
        // makes it, so pinning one here turns every added message into a test
        // failure that says nothing about what broke.
        int fadeIndex = msgs.Select((m, i) => (m, i)).First(x => x.m.Name == "Fade").i;
        Assert.True(fadeIndex >= 0, "Fade is declared");

        var fade = (SourceSharp.Shared.FadeMessage)msgs[fadeIndex].Handler!;
        registry.Dispatch(fadeIndex, new byte[] { 0x10, 0x00, 0x20, 0x00, 0x03, 0x00, 1, 2, 3, 4 });
        Assert.True(fade.Last.Duration == 0x10 && fade.Last.HoldTime == 0x20 && fade.Last.Flags == 3, "dispatch reaches the declared handler");
    }

    //-------------------------------------------------------------------------
    // The layout fingerprint exchanged at join.
    //
    // Its whole job is to differ when the two sides would generate incompatible
    // datatables, so these tests are about what MUST change it, not just about
    // it being stable.
    //-------------------------------------------------------------------------
    [Fact]
    public void LayoutFingerprintTests()
    {
        var a = EntityLayout.Build(typeof(SampleEntity));
        var b = EntityLayout.Build(typeof(SampleEntity));

        Assert.True(a.Fingerprint == b.Fingerprint, "fingerprint is stable for the same type");
        Assert.True(a.Fingerprint != 0, "fingerprint is non-zero");

        // Different field ORDER — same names, same types, same total size.
        // The offsets differ, so the tables are incompatible and the fingerprint
        // must say so.
        var reordered = EntityLayout.Build(typeof(ReorderedEntity));
        var straight  = EntityLayout.Build(typeof(StraightEntity));
        Assert.True(reordered.Fingerprint != straight.Fingerprint, "field order changes the fingerprint");

        // Different field TYPE at the same offset — int vs float are both 4
        // bytes, so size alone would not catch this.
        var asInt   = EntityLayout.Build(typeof(IntFieldEntity));
        var asFloat = EntityLayout.Build(typeof(FloatFieldEntity));
        Assert.True(asInt.Fingerprint != asFloat.Fingerprint, "field type changes the fingerprint");
        Assert.True(asInt.Size == asFloat.Size, "the two really are the same size");

        // Different field NAME, same shape.
        var renamed = EntityLayout.Build(typeof(RenamedFieldEntity));
        Assert.True(renamed.Fingerprint != asInt.Fingerprint, "field name changes the fingerprint");

        // The registry, over the assembly set an anchor reaches.
        //
        // WHAT IT FINDS HERE IS THIS FILE'S OWN FIXTURES. NetEntityState was the
        // last [EntityField] type in SourceSharp.Shared and went with the
        // netentity shell, so IN THE GAME this discovers zero; it is non-zero
        // here only because LoadedAssemblies.Types sweeps the loaded set. The
        // two facts under test are unaffected — discovery finds every declared
        // layout, and it is deterministic — but whether [EntityField] and
        // EntityLayoutRegistry survive at all is now a real question, and not
        // this test's to answer.
        var reg1 = new EntityLayoutRegistry(new RecordingLog());
        var reg2 = new EntityLayoutRegistry(new RecordingLog());
        int n1 = reg1.DiscoverAll(TestServices.ServerGame);
        int n2 = reg2.DiscoverAll(TestServices.ServerGame);

        Assert.True(n1 > 0, "registry discovers the layouts the anchor's assembly set declares");
        Assert.True(n1 == n2 && reg1.Fingerprint == reg2.Fingerprint, "registry discovery is deterministic");

        // Verify: agreement passes, and each kind of disagreement fails. The
        // engine-block hash is the fourth argument and its own two facts live
        // in LayoutHandshakeTests; here it is passed matching so the three
        // assertions below keep testing what they were written to test.
        Assert.True(reg1.Verify("Test", reg2.Fingerprint, reg2.Count, reg2.BlockFingerprint), "matching fingerprint verifies");
        Assert.True(!reg1.Verify("Test", reg2.Fingerprint ^ 1u, reg2.Count, reg2.BlockFingerprint), "a different fingerprint is rejected");
        Assert.True(!reg1.Verify("Test", reg2.Fingerprint, reg2.Count + 1, reg2.BlockFingerprint), "a different layout count is rejected");
    }

    private sealed class StraightEntity
    {
        [EntityField] public int   A { get; set; }
        [EntityField] public float B { get; set; }
    }

    private sealed class ReorderedEntity
    {
        [EntityField] public float B { get; set; }
        [EntityField] public int   A { get; set; }
    }

    private sealed class IntFieldEntity   { [EntityField] public int   Value { get; set; } }
    private sealed class FloatFieldEntity { [EntityField] public float Value { get; set; } }
    private sealed class RenamedFieldEntity { [EntityField] public int Other { get; set; } }

    //-------------------------------------------------------------------------
    // The decision enums.
    //
    // Their numeric values ARE the ABI, because the bootstrap casts straight to
    // int rather than mapping. So these tests pin the values, not the names —
    // renaming a member is harmless, renumbering one silently changes what the
    // shell does.
    //-------------------------------------------------------------------------
    [Fact]
    public void DecisionEnumTests()
    {
        Assert.True((int)Decision.UseStockRule == -1, "Decision.UseStockRule is -1");
        Assert.True((int)Decision.No  == 0, "Decision.No is 0");
        Assert.True((int)Decision.Yes == 1, "Decision.Yes is 1");

        Assert.True((int)PlayerRelationship.UseStockRule == -1, "PlayerRelationship.UseStockRule is -1");
        Assert.True((int)PlayerRelationship.NotTeammate == 0, "PlayerRelationship.NotTeammate is 0");
        Assert.True((int)PlayerRelationship.Teammate    == 1, "PlayerRelationship.Teammate is 1");

        Assert.True((int)CommandResult.NotHandled == 0, "CommandResult.NotHandled is 0");
        Assert.True((int)CommandResult.Handled    == 1, "CommandResult.Handled is 1");

        // Native tests "< 0" for "C# declined", so every deferral member must be
        // negative — that is what lets the shell stay unchanged as members are
        // added.
        Assert.True((int)Decision.UseStockRule < 0 && (int)PlayerRelationship.UseStockRule < 0, "every deferral value is negative");

        // And every decided value must be non-negative, or the shell would read
        // a real answer as a deferral.
        Assert.True((int)Decision.No >= 0 && (int)Decision.Yes >= 0 &&
            (int)PlayerRelationship.NotTeammate >= 0 && (int)PlayerRelationship.Teammate >= 0 &&
            (int)CommandResult.NotHandled >= 0 && (int)CommandResult.Handled >= 0, "every decided value is non-negative");
    }

    //-------------------------------------------------------------------------
    // The generated layout vs what .NET actually lays out.
    //
    // A whole state struct is read in one MemoryMarshal.Read, which is only
    // valid while the two agree. If .NET ever laid a field somewhere else,
    // every replicated value would be silently wrong.
    //
    // The fixture is declared HERE. It used to be NetEntityState, which went
    // with the deleted netentity shell — but what is under test was never that
    // type: it is EntityLayout.Build's arithmetic against the CLR's. Four field
    // kinds deliberately, because bool is the one whose storage size and wire
    // width differ and four ints would agree for reasons that say nothing.
    //-------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    private struct LayoutProbe
    {
        [EntityField] public int    SpawnCount;
        [EntityField] public float  LastDamage;
        [EntityField] public Vector SpawnOrigin;
        [EntityField] public bool   IsBroken;
    }

    [Fact]
    public void BlittableLayoutTests()
    {
        var layout = EntityLayout.Build(typeof(LayoutProbe), "ss_layout_probe");

        Assert.True(layout.Fields.Count == 4, "every [EntityField] is in the layout");
        Assert.True(layout.Size >= Unsafe.SizeOf<LayoutProbe>(), "generated size matches the struct");

        bool offsetsMatch = true;
        foreach (var f in layout.Fields)
        {
            int actual = (int)Marshal.OffsetOf<LayoutProbe>(f.Name);
            if (actual != f.Offset)
            {
                Console.WriteLine($"       {f.Name}: generated {f.Offset}, .NET {actual}");
                offsetsMatch = false;
            }
        }
        Assert.True(offsetsMatch, "every generated offset matches Marshal.OffsetOf");

        // THE ROUND TRIP THROUGH RAW BYTES WENT WITH EntityData -- step 8a.
        //
        // It wrote the four fields into a byte[] at the layout's offsets and read
        // them back as the struct through EntityData.TryRead<LayoutProbe>, then
        // refused a short and an empty blob. It was the WEAKER form of the claim
        // the loop above already makes: "the generated layout matches what .NET
        // lays out for a sequential blittable struct" is asserted field by field
        // against Marshal.OffsetOf up there, and a whole-struct MemoryMarshal.Read
        // agreeing with it is that same fact read once more.
        //
        // The short-blob refusal it also carried is NOT re-covered here, and it is
        // not uncovered either: it moved with the API, and the path that replaced
        // it (NetBlobView over the generated layout) bounds-checks in its own
        // facts.

        // And the size a layout of these four kinds comes to, rounded to a
        // blob. This is the half of the tier question that belongs in C#: the
        // BOUNDARY rule lives in generic_nettable.h and is tested in the gtest,
        // and asserting a tier index here would be a managed reimplementation
        // agreeing with itself.
        Assert.True(NetSchema.BlobBytesFor(NetSchema.FromLayout(NoTiers, layout, true).Fields) == 32, "int + float + Vector + bool rounds to a 32-byte blob");
    }

    //-------------------------------------------------------------------------
    // PayloadAgreementTests and NetEntityRegistryTests went with their subject.
    // NEITHER FACT IS NOW UNCOVERED, which is the only reason deleting them is
    // allowed: the layout arithmetic and the short-blob refusal moved into
    // BlittableLayoutTests above; the registry's lifetime rules belong to
    // ClientStandaloneRegistry — the live one — and are in EntityInjectionTests;
    // the end-to-end round trip is checked on a live join by
    // cs_sv_payload_probe / cs_cl_payload_probe. DataUpdateType's values are ABI
    // and outlived the shell, so they are pinned here rather than lost with the
    // test that held them.
    //-------------------------------------------------------------------------
    [Fact]
    public void DataUpdateTypeValuesAreTheEnginesTests()
    {
        Assert.True((int)DataUpdateType.Created == 0, "DataUpdateType.Created is 0");
        Assert.True((int)DataUpdateType.DataTableChanged == 1, "DataUpdateType.DataTableChanged is 1");
    }

    //-------------------------------------------------------------------------
    // The game event registry.
    //
    // Unlike user messages, order carries no meaning here — the engine
    // dispatches by name — so these are about subscription, fan-out and the
    // static-class contract rather than sequence.
    //-------------------------------------------------------------------------
    [Fact]
    public void GameEventRegistryTests()
    {
        var registry = new GameEventRegistry(new RecordingEngine(), new RecordingLog());

        int a = 0, b = 0;
        GameEventDispatch dispatchA = _ => a++;
        GameEventDispatch dispatchB = _ => b++;

        registry.Subscribe("player_death", dispatchA);
        Assert.True(registry.Count == 1, "subscribing registers the event");

        // Two claims on one event is legitimate — it keeps unrelated concerns
        // apart — and both must run.
        registry.Subscribe("player_death", dispatchB);
        Assert.True(registry.Count == 1, "a second claim does not add a second event");

        registry.Dispatch("player_death", IntPtr.Zero);
        Assert.True(a == 1 && b == 1, "every claim on an event runs");

        // An event nobody claimed must be silently ignored: the engine can fire
        // events we never subscribed to.
        Assert.True(!Throws(() => registry.Dispatch("player_say", IntPtr.Zero)), "an unclaimed event is ignored");
        Assert.True(a == 1, "an unclaimed event runs nothing");

        // Event names come from the engine, so matching is case-insensitive.
        registry.Dispatch("PLAYER_DEATH", IntPtr.Zero);
        Assert.True(a == 2, "event lookup is case-insensitive");

        // One throwing must not rob the others of the event.
        int after = 0;
        var isolated = new GameEventRegistry(new RecordingEngine(), new RecordingLog());
        isolated.Subscribe("player_hurt", _ => throw new InvalidOperationException("fault"));
        isolated.Subscribe("player_hurt", _ => after++);

        Assert.True(!Throws(() => isolated.Dispatch("player_hurt", IntPtr.Zero)), "a throwing dispatch does not escape");
        Assert.True(after == 1, "a throwing dispatch does not stop the others");

        // The instance-class contract. A static class (one subscriber list for
        // the whole process) or one without the dispatch method is skipped with
        // a warning rather than taken.
        var services = new ServiceContainer();
        GameEventRegistry.AddEventsTo(services, typeof(CoreTests).Assembly, new RecordingLog());
        var discovered = new GameEventRegistry(new RecordingEngine(), new RecordingLog());
        discovered.RegisterAll(typeof(CoreTests).Assembly, services);
        Assert.True(discovered.Names.Contains("test_good"), "a well-formed event class is discovered");
        Assert.True(!discovered.Names.Contains("test_static"), "a static class is skipped");
        Assert.True(!discovered.Names.Contains("test_no_dispatch"), "a class without Dispatch is skipped");

        // And what it subscribed is the CONTAINER's instance: dispatching the
        // name raises the event on the object a subscriber would be handed.
        var good = services.Require<GoodTestEvent>();
        Assert.Same(good, services.Require<IGameEvent<int>>());
        int heard = 0;
        good.Fired += _ => heard++;
        discovered.Dispatch("test_good", IntPtr.Zero);
        Assert.True(heard == 1, "the engine's event reaches the container's instance");
    }

    /// <summary>
    /// A reader whose every field reads as its default: over a lenient engine,
    /// since a reader reads through its registry's event area (D8).
    /// </summary>
    private static GameEventReader DefaultFields()
        => new(new RecordingEngine { Strict = false }, IntPtr.Zero);

    [Fact]
    public void TwoGameEventRegistriesSubscribeThroughTheirOwnEngine()
    {
        // plan_abi_hotreload D8: the registry was built over the static Engine,
        // which subscribed on whichever side was active. Each is now built over
        // its side's event area, and only that one hears the subscription.
        var mine = new RecordingEngine();
        var theirs = new RecordingEngine();
        var registry = new GameEventRegistry(mine, new RecordingLog());
        _ = new GameEventRegistry(theirs, new RecordingLog());

        registry.Subscribe("player_death", _ => { });

        Assert.Equal(1, mine.CountOf("GameEvents.ListenForGameEvent"));
        Assert.Equal("player_death", mine.Calls.Single(c => c.Member == "GameEvents.ListenForGameEvent").ArgAs<string>(0));
        Assert.Equal(0, theirs.CountOf("GameEvents.ListenForGameEvent"));
    }

    [Fact]
    public void AGameEventReaderReadsThroughItsRegistrysEngine()
    {
        // The reader a dispatch hands out reads the event's fields through the
        // same area, not through whichever side is active.
        var mine = new RecordingEngine().Returns("GameEvents.EventGetInt", 7);
        var theirs = new RecordingEngine().Returns("GameEvents.EventGetInt", 9);
        var registry = new GameEventRegistry(mine, new RecordingLog());
        _ = new GameEventRegistry(theirs, new RecordingLog());

        int read = 0;
        registry.Subscribe("player_death", reader => read = reader.GetInt("userid"));
        registry.Dispatch("player_death", new IntPtr(0x1234));

        Assert.Equal(7, read);
        var call = mine.Calls.Single(c => c.Member == "GameEvents.EventGetInt");
        Assert.Equal(new IntPtr(0x1234), call.ArgAs<IntPtr>(0));
        Assert.Equal("userid", call.ArgAs<string>(1));
        Assert.Equal(0, theirs.CountOf("GameEvents.EventGetInt"));
    }

    [Fact]
    public void TwoContainersDoNotShareAGameEvent()
    {
        // The property the static events lacked: a subscriber to one module's
        // event hears nothing raised on another's.
        var first = new ServiceContainer();
        var second = new ServiceContainer();
        GameEventRegistry.AddEventsTo(first, typeof(PlayerDeathEvent).Assembly, new RecordingLog());
        GameEventRegistry.AddEventsTo(second, typeof(PlayerDeathEvent).Assembly, new RecordingLog());

        var mine = first.Require<IGameEvent<PlayerDeathArgs>>();
        var theirs = second.Require<IGameEvent<PlayerDeathArgs>>();
        Assert.NotSame(mine, theirs);

        int heard = 0;
        mine.Fired += _ => heard++;

        theirs.Raise(new PlayerDeathArgs(2, 5, "smg1", false));
        Assert.True(heard == 0, "another container's death does not reach this subscriber");

        mine.Raise(new PlayerDeathArgs(2, 5, "smg1", false));
        Assert.True(heard == 1, "this container's does");
    }

    /// <summary>The shape a declared event class must have.</summary>
    [GameEvent("test_good")]
    public sealed class GoodTestEvent : IGameEvent<int>
    {
        public event Action<int>? Fired;
        public void Raise(int args) => Fired?.Invoke(args);
        public void Dispatch(GameEventReader reader) => Raise(1);
    }

    /// <summary>Static — must be skipped: its event would be process-wide.</summary>
    [GameEvent("test_static")]
    public static class StaticTestEvent
    {
        public static void Dispatch(GameEventReader reader) { }
    }

    /// <summary>An instance class missing the dispatch method — must be skipped.</summary>
    [GameEvent("test_no_dispatch")]
    public sealed class NoDispatchTestEvent
    {
        public void SomethingElse() { }
    }

    //-------------------------------------------------------------------------
    // A subscriber to the static events — the shape gameplay code takes.
    //
    // Scoring is where "both sides see the event" stops being harmless, so most
    // of this is about which side acts and what does not count as a kill.
    //-------------------------------------------------------------------------
    [Fact]
    public void ScoreboardTests()
    {
        // Its own board. It was a process-wide static, so this test opened by
        // clearing one every other suite could also be writing to.
        var host = Fake().Returns("Host.ActiveSide", Side.Server);
        var disconnects = new PlayerDisconnectEvent();
        var board = new Scoreboard(host, disconnects, new RecordingLog());

        board.Subscribe();

        // Subscribe is guarded: a managed reload re-runs module init, and a
        // second subscription would double every score.
        board.Subscribe();
        host.Returns("Host.ActiveSide", Side.Server);
        // A lenient engine answers every field with its default, which is what
        // the static Engine answered with no game behind it.
        new PlayerDeathEvent().Dispatch(DefaultFields());

        // Attacker 0 is a world death and must not credit anyone.
        Assert.True(board.KillsFor(0) == 0, "a world death credits nobody");

        // The credit rule itself, which is what subscribers rely on.
        Assert.True(new PlayerDeathArgs(2, 5, "smg1", false).IsCredited, "a normal kill is credited");
        Assert.True(!new PlayerDeathArgs(5, 5, "world", false).IsCredited, "a suicide is not credited");
        Assert.True(!new PlayerDeathArgs(5, 0, "world", false).IsCredited, "a world death is not credited");

        // Typed args carry what the decoder read.
        var death = new PlayerDeathArgs(2, 5, "crossbow", true);
        Assert.True(death.UserId == 2 && death.AttackerUserId == 5 &&
            death.Weapon == "crossbow" && death.Headshot, "args carry the decoded fields");

        var connect = new PlayerConnectArgs("player", 3, false, "loopback");
        Assert.True(connect.Name == "player" && connect.UserId == 3 &&
            !connect.IsBot && connect.Address == "loopback", "connect args carry the decoded fields");

        // Record structs compare by value, which is what makes them usable as
        // test expectations at all.
        Assert.True(new PlayerTeamArgs(1, 2, 3, false) == new PlayerTeamArgs(1, 2, 3, false), "args compare by value");

        board.Reset();
        Assert.True(board.Kills.Count == 0, "reset clears the board");
    }

    //-------------------------------------------------------------------------
    // Gameplay policy.
    //
    // These rules can only ever REFUSE damage or override a delay; they must
    // never make damage land that the stock rules would have blocked. Most of
    // these tests are about that direction, not about the happy path.
    //-------------------------------------------------------------------------
    //-------------------------------------------------------------------------
    // What the engine calls a map, and what everything else does.
    //
    // The engine says "maps/dm_lockdown.bsp". The development API takes a name,
    // and refuses anything with a separator in it — a name arriving from a
    // browser must not be able to reach out of the maps directory. Passing the
    // engine's version straight through had the API rejecting its own map, and
    // the entity debugger showing nothing for the map it was looking at.
    //-------------------------------------------------------------------------
    [Fact]
    public void MapNamesAreNames()
    {
        Assert.True(ServerModule.BareMapName("maps/dm_lockdown.bsp") == "dm_lockdown", "the engine's path becomes a name");

        Assert.True(ServerModule.BareMapName("dm_lockdown") == "dm_lockdown", "a name is left alone");

        // Not this engine's, but the same string reaches here on Windows.
        Assert.True(ServerModule.BareMapName(@"maps\dm_lockdown.bsp") == "dm_lockdown", "either separator counts");

        // A map called dm_bsp.bsp is a real possibility and only the extension
        // should go.
        Assert.True(ServerModule.BareMapName("maps/dm_bsp.bsp") == "dm_bsp", "only the extension is dropped");

        Assert.True(ServerModule.BareMapName("") == "", "nothing is nothing");
    }

    [Fact]
    public void GameRulesTests()
    {
        // A host and a board of this test's own, rather than the process-wide
        // side and a shared static tally.
        var host  = Fake().Returns("Host.ActiveSide", Side.Server);
        var board = new Scoreboard(host, new PlayerDisconnectEvent(), new RecordingLog());
        var deaths = new PlayerDeathEvent();

        var convars = new ConVarRegistry(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        var rules = new GameRules(new RecordingLog(), convars, host,
            Fake().Ignores("Players.GiveItem", "Players.GiveAmmo", "Players.FindByUserId",
                           "Players.GetTeam", "Players.GetUserId"),
            Fake().Returns("Entities.IsValid", false), board, deaths);

        var victim   = new EntityHandle(0x0001_0001u);
        var attacker = new EntityHandle(0x0002_0001u);

        // With no engine behind it, team lookups return -1 ("not a player"), so
        // friendly fire cannot fire and everything defers. That is the correct
        // shape: an unanswerable rule must defer, not guess.
        Assert.True(rules.CanTakeDamage(victim, attacker, 10f, 0, 0f) == Decision.UseStockRule, "damage defers without team information");

        // Self-damage always defers, so falling still hurts whatever the team
        // rules say.
        Assert.True(rules.CanTakeDamage(victim, victim, 10f, 0, 0f) == Decision.UseStockRule, "self-damage always defers");

        // Respawn delay is the ConVar, and its default defers.
        Assert.True(rules.GetRespawnDelay(victim) < 0f, "respawn delay defers by default");

        Bind(convars.Get<RespawnDelayConVar>()!, "cs_sv_respawn_delay", "7.5");
        Assert.True(Math.Abs(rules.GetRespawnDelay(victim) - 7.5f) < 0.001f, "respawn delay follows the convar");

        // Spawn protection. Off by default, so a fresh spawn is not protected.
        rules.OnPlayerSpawned(victim, 100f);
        Assert.True(!rules.IsProtected(victim, 100f), "no protection when disabled");

        Bind(convars.Get<SpawnProtectConVar>()!, "cs_sv_spawn_protect", "5");
        rules.OnPlayerSpawned(victim, 100f);

        Assert.True(rules.IsProtected(victim, 100f), "protected immediately after spawning");
        Assert.True(rules.IsProtected(victim, 104.9f), "still protected inside the window");
        Assert.True(!rules.IsProtected(victim, 105f), "not protected at the boundary");
        Assert.True(!rules.IsProtected(victim, 110f), "not protected after the window");

        // Protection refuses damage outright — including from the world, which
        // is why it is checked before the self-damage rule.
        Assert.True(rules.CanTakeDamage(victim, attacker, 10f, 0, 101f) == Decision.No, "protection refuses damage");
        Assert.True(rules.CanTakeDamage(victim, victim, 10f, 0, 101f) == Decision.No, "protection refuses self-damage too");
        Assert.True(rules.CanTakeDamage(victim, attacker, 10f, 0, 110f) == Decision.UseStockRule, "damage defers once protection expires");

        // A player nobody spawned is not protected — the map has no entry.
        var unknown = new EntityHandle(0x0009_0001u);
        Assert.True(!rules.IsProtected(unknown, 100f), "an unspawned player is not protected");

        // Forgetting drops the protection window, so a stale entry cannot make a
        // reused handle spawn-protected.
        rules.Forget(victim);
        Assert.True(!rules.IsProtected(victim, 101f), "forgetting clears protection");

        // --- win conditions ---

        board.Reset();
        rules.ResetRound();

        // Off by default, so Think must do nothing however many kills exist.
        Assert.True(!rules.RoundOver, "no frag limit by default");
        rules.Think();
        Assert.True(!rules.RoundOver, "think with no limit does not end the round");

        Bind(convars.Get<FragLimitConVar>()!, "cs_sv_fraglimit", "3");
        rules.Think();
        Assert.True(!rules.RoundOver, "an empty scoreboard does not end the round");

        // Two kills is under the limit.
        for (int i = 0; i < 2; i++)
            deaths.Dispatch(DefaultFields());   // world deaths credit nobody

        rules.Think();
        Assert.True(!rules.RoundOver, "uncredited deaths do not reach the limit");

        // ResetRound has to clear the latch, or the round after a map change
        // could never end.
        rules.ResetRound();
        Assert.True(!rules.RoundOver, "reset clears the round-over latch");
        Assert.True(board.Kills.Count == 0, "reset clears the scoreboard");
        Assert.True(!rules.IsProtected(victim, 100f), "reset clears spawn protection");

        Assert.True(rules.CanRespawn(victim) == Decision.UseStockRule, "respawn is deferred");

        // The loadout is empty because the mod has no giveable entity classes —
        // weapons and items were HL2MP C++ code and were stripped. Spawning must
        // still work, and must not fault on an empty list.
        Assert.True(!Throws(() => rules.OnPlayerSpawned(victim, 200f)), "spawning with an empty loadout is fine");
        Assert.True(rules.IsProtected(victim, 200f), "an empty loadout still starts the protection clock");
    }

    //-------------------------------------------------------------------------
    // Pickups.
    //
    // A touch fires repeatedly while a player stands in the trigger, so most of
    // what can go wrong here is collecting the same pickup more than once — and
    // that is what these test.
    //-------------------------------------------------------------------------
    [Fact]
    public void PickupTests()
    {
        var engine = ScriptedEntityRegistry.UnattachedEngine();
        var pickups = engine.Create<Pickups>();

        Assert.True(pickups.LiveCount == 0 && pickups.PendingCount == 0, "starts empty");

        // Without an engine, CreateEntity returns None and nothing is tracked —
        // the failure must not leave a half-registered pickup behind.
        var spawned = pickups.Spawn(Pickups.Health, new Vector(1f, 2f, 3f));
        Assert.True(!engine.Require<IEngineEntities>().IsValid(spawned), "a failed spawn returns None");
        Assert.True(pickups.LiveCount == 0, "a failed spawn tracks nothing");

        // Touching something that is not a live pickup does nothing. This is the
        // path a second touch in the same frame takes.
        var stranger = new EntityHandle(0x0007_0001u);
        var player   = new EntityHandle(0x0001_0001u);
        Assert.True(!pickups.OnTouched(stranger, player, 0f), "touching an unknown entity collects nothing");

        // Think with nothing pending must be a no-op, since it runs every frame.
        Assert.True(!Throws(() => pickups.Think(0f)), "think with nothing pending is safe");
        Assert.True(pickups.LiveCount == 0, "think with nothing pending spawns nothing");

        // Reset clears both sides of the bookkeeping.
        Assert.True(!Throws(() => pickups.Reset()), "reset is safe when empty");
        Assert.True(pickups.LiveCount == 0 && pickups.PendingCount == 0, "reset clears everything");

        Assert.True(!pickups.IsLive(stranger), "a handle that was never spawned is not live");

        // The kind description itself, which is the part a mod author edits.
        Assert.True(Pickups.Health.HealthGiven > 0, "health pickup gives health");
        Assert.True(Pickups.Health.Model.EndsWith(".mdl"), "health pickup has a model");
        Assert.True(Pickups.Health.RespawnDelay > 0f, "health pickup respawns");
        Assert.True(Pickups.Kinds.All(k => !string.IsNullOrEmpty(k.Model)), "every declared kind has a model");
        Assert.True(Pickups.Kinds.All(k => k.SizeX > 0 && k.SizeY > 0 && k.SizeZ > 0), "every declared kind has a non-zero trigger");

        // Record equality, which is what makes a kind usable as a lookup key.
        Assert.True(Pickups.Health with { } == Pickups.Health, "kinds compare by value");
    }


    //-------------------------------------------------------------------------
    // The HUD.
    //
    // Runs once a frame with no engine behind it in tests, so what is worth
    // pinning is the arithmetic: the fade curve and the low-health threshold,
    // both of which are silently wrong rather than visibly broken if they drift.
    //-------------------------------------------------------------------------
    [Fact]
    public void HudTests()
    {
        // The HUD reads the local player every paint. This test is about the
        // paint not throwing when there is no player, so it says which reads it
        // does not care about rather than pretending it made no reads.
        var hud = new client::SourceSharp.Client.Hud(new RecordingLog(), Fake().Ignores("Clock.Now"),
            // The five Hud.LocalWeapon* reads were named here and are gone: the
            // slots behind them were removed at ABI 249 and the facade methods
            // with them, so a name left here would be an ignore for a read that
            // cannot happen.
            Fake().Ignores("Hud.LocalPlayerHealth", "Hud.LocalPlayerOrigin",
                           "Hud.Localise",
                           "Hud.LookupBinding", "Hud.TextMessageGet"),
            new DamageMessage(), new HudMsgMessage(), new HudTextMessage(), new KeyHintTextMessage(),
            new BuildInfo());

        // Never damaged: no indicator at all, and no division by a zero elapsed.
        Assert.True(hud.DamageFraction(0f) == 0f, "no damage means no indicator");
        Assert.True(hud.DamageFraction(1000f) == 0f, "no damage at a later time either");

        // Colour helpers, which the whole HUD's alpha handling rests on.
        Assert.True(Color32.Red.WithAlpha(64).A == 64, "WithAlpha replaces alpha");
        Assert.True(Color32.Red.WithAlpha(9999).A == 255, "WithAlpha clamps high");
        Assert.True(Color32.Red.WithAlpha(-5).A == 0, "WithAlpha clamps low");
        Assert.True(Color32.White.WithAlpha(200).Fade(0.5f).A == 100, "Fade halves alpha");
        Assert.True(Color32.White.Fade(0f).A == 0, "Fade to zero is invisible");
        Assert.True(Color32.White.WithAlpha(100).Fade(5f).A == 100, "Fade clamps above one");
        Assert.True(Color32.Red.Fade(0.5f) is { R: 255, G: 64, B: 64 }, "Fade preserves rgb");

        // Colours compare by value, which is what makes the threshold tests
        // meaningful rather than reference comparisons.
        Assert.True(new Color32(1, 2, 3, 4) == new Color32(1, 2, 3, 4), "colours compare by value");
        Assert.True(new Color32(1, 2, 3, 4) != new Color32(1, 2, 3, 5), "different alpha is a different colour");

    }

    /// <summary>
    /// game_text, which a map uses to say anything at all.
    ///
    /// The handler for this message was registered and empty until now, so a
    /// map could talk and nothing listened. These cover the two halves that can
    /// be wrong without a game: when a message is visible, and where it sits.
    /// </summary>
    [Fact]
    public void HudMessageTests()
    {
        // Fade in, hold, fade out, gone.
        Assert.True(HudMessages.Alpha(0f, 1f, 2f, 1f) == 0f, "a message starts invisible");
        Assert.True(HudMessages.Alpha(0.5f, 1f, 2f, 1f) == 0.5f, "it fades in");
        Assert.True(HudMessages.Alpha(2f, 1f, 2f, 1f) == 1f, "it holds fully opaque");
        Assert.True(HudMessages.Alpha(2.99f, 1f, 2f, 1f) == 1f, "it is still up at the end of the hold");
        Assert.True(Math.Abs(HudMessages.Alpha(3.5f, 1f, 2f, 1f) - 0.5f) < 0.001f, "it fades out");
        Assert.True(HudMessages.Alpha(4f, 1f, 2f, 1f) == 0f, "and then it is gone");
        Assert.True(HudMessages.Alpha(1000f, 1f, 2f, 1f) == 0f, "long past the end it stays gone");

        // A map that leaves the fade keys at zero means "instantly", not a
        // division by zero.
        Assert.True(HudMessages.Alpha(0f, 0f, 2f, 0f) == 1f, "a zero fade-in is instant");
        Assert.True(HudMessages.Alpha(2f, 0f, 2f, 0f) == 0f, "a zero fade-out ends cleanly");

        // Channels: one message each, and a new one replaces what was there.
        var messages = new HudMessages(new RecordingLog(), Fake(), Fake());

        messages.Show(Message(1, "first"), 0f);
        Assert.True(messages.ActiveCount(0.5f) == 1, "a message is showing");

        messages.Show(Message(1, "second"), 0f);
        Assert.True(messages.ActiveCount(0.5f) == 1, "the same channel still holds one message");

        messages.Show(Message(2, "other"), 0f);
        Assert.True(messages.ActiveCount(0.5f) == 2, "a second channel is its own message");

        Assert.True(messages.ActiveCount(100f) == 0, "a message expires");

        // A fresh board from here on: a channel stays live until it expires, so
        // counting on a board that already has two messages up would measure the
        // earlier cases rather than this one.
        var cleared = new HudMessages(new RecordingLog(), Fake(), Fake());
        cleared.Show(Message(3, "up"), 0f);
        Assert.True(cleared.ActiveCount(0.5f) == 1, "a message is up before it is cleared");

        // An empty message is how a map takes a sign down early.
        cleared.Show(Message(3, ""), 0f);
        Assert.True(cleared.ActiveCount(0.5f) == 0, "an empty message clears the channel");

        // Out of range is dropped rather than throwing or corrupting a channel.
        var outOfRange = new HudMessages(new RecordingLog(), Fake(), Fake());
        Assert.True(!Throws(() => outOfRange.Show(Message(99, "nowhere"), 0f)), "a channel the engine does not have is ignored");
        Assert.True(outOfRange.ActiveCount(0.5f) == 0, "and shows nothing");

        // Refreshing the UI's view is safe with nothing to say and with something to say.
        var empty = new HudMessages(new RecordingLog(), Fake(), Fake());
        Assert.True(!Throws(() => empty.Refresh(0f)) && empty.MessageCount == 0, "an empty message board shows nothing");

        var multiline = new HudMessages(new RecordingLog(), Fake(), Fake());
        multiline.Show(Message(0, "a\nb"), 0f);
        Assert.True(multiline.ActiveCount(0.5f) == 1, "a multi-line message is one channel");
        multiline.Refresh(0.5f);
        Assert.True(multiline.MessageCount == 1 && multiline.Message(0).Text == "a\nb", "a multi-line message is one line of the UI's view, whole");
    }

    private static HudMessageArgs Message(int channel, string text)
        => new(channel, 0.1f, 0.7f, Color32.White, Color32.Amber,
               0, 0f, 1f, 5f, 0f, text);

    private static bool Throws(Action a)
    {
        try { a(); return false; } catch { return true; }
    }

    /// <summary>int, int, Vector — the vector must land at 8, not 12.</summary>
    private sealed class PackedEntity
    {
        [EntityField] public int    First  { get; set; }
        [EntityField] public int    After  { get; set; }
        [EntityField] public Vector Vec    { get; set; }
    }

    /// <summary>
    /// Abstract so that discovery skips it while Build can still be pointed at
    /// it by hand. Layouts are discovered across every loaded assembly now, and
    /// a fixture whose whole purpose is to be unbuildable would otherwise take
    /// the registry down with it — which is correct for a real entity and
    /// useless in a test that wants to catch the throw itself.
    /// </summary>
    private abstract class BadEntity
    {
        [EntityField] public string Unsupported { get; set; } = string.Empty;
    }

    //-------------------------------------------------------------------------
    // The cache is seeded from the ENGINE, not from the declared default.
    //
    // Found by lane `defect-convar-notify`'s live differential rather than by
    // reading: run 1 set sv_unlockedchapters to 7 in a live dm_lockdown, the
    // engine ARCHIVED it, and run 2 came up with the engine holding 7 while
    // managed code reported its declared 1. Nothing corrects that later --
    // ConVar::ChangeStringValue fires no callback when the value does not
    // actually change -- so "set it to 7" left managed at 1 permanently.
    //
    // This is a SECOND staleness, distinct from the notify bug the same lane
    // fixed: that one lost changes, this one starts wrong.
    //-------------------------------------------------------------------------

    [Fact]
    public void ConVarCacheIsSeededFromTheEngineAndNotFromTheDeclaredDefault()
    {
        var i = new TestIntConVar();
        BindWithEngineValue(i, "cvar_archived", "1", "7");

        Assert.Equal(7, i.Value);
        Assert.Equal("7", i.RawValue);
    }

    [Fact]
    public void ConVarDefaultValueStaysTheDECLAREDOneWhenTheEngineDisagrees()
    {
        // RawValue and DefaultValue were the same field before this change, so
        // the seed must not be allowed to rewrite what "default" means -- the
        // debug-api listing renders both columns and a reset reads this one.
        var i = new TestIntConVar();
        BindWithEngineValue(i, "cvar_archived", "1", "7");

        Assert.Equal("1", i.DefaultValue);
    }

    [Fact]
    public void ConVarFallsBackToTheDeclaredDefaultWhenThereIsNoEngineToAsk()
    {
        // A refused convar, or any run with no native side, has nothing to read
        // back -- and the declared default is then the only answer there is.
        var i = new TestIntConVar();
        BindWithEngineValue(i, "cvar_refused", "3", null);

        Assert.Equal(3, i.Value);
        Assert.Equal("3", i.DefaultValue);
    }

    [Fact]
    public void ConVarSeedReachesTheTypedLayerAndNotOnlyTheRawString()
    {
        // OnRawChanged has to be handed the SEEDED value, not the declared one:
        // the typed layer caches a parsed copy and every read goes through it,
        // so a raw string that agrees with the engine over a parsed value that
        // does not is the same defect one layer down.
        var tracked = new TestIntConVar();
        BindWithEngineValue(tracked, "cvar_seeded", "1", "9");

        Assert.Equal(9, tracked.Value);

        // (0, 9), not (1, 9) and not (9, 9): the typed layer's cached copy
        // starts at the type's zero and Bind is its first notification, so the
        // OLD value it reports is that zero rather than either string. That is
        // pre-existing and is asserted rather than assumed -- what matters here
        // is that the NEW value is the engine's 9 and not the declared 1.
        Assert.Contains((0, 9), tracked.Observed);
    }

    //-------------------------------------------------------------------------
    // Helpers. Bind/NotifyChanged are internal to the ABI assembly, so the test
    // project reaches them through InternalsVisibleTo.
    //-------------------------------------------------------------------------

    private static void Bind(ConVarBase cvar, string name, string value) => cvar.Bind(-1, name, value);
    private static void BindWithEngineValue(ConVarBase cvar, string name, string declared, string? live)
        => cvar.Bind(-1, name, declared, "", ConVarFlags.None, null, null, live);
    private static void Notify(ConVarBase cvar, string newValue, string oldValue)
        => cvar.NotifyChanged(newValue, oldValue);



    private sealed class TestIntConVar : IntConVar
    {
        public readonly List<(int, int)> Observed = new();
        protected override void OnChanged(int oldValue, int newValue) => Observed.Add((oldValue, newValue));
    }

    private sealed class TestFloatConVar : FloatConVar { }
    private sealed class TestBoolConVar : BoolConVar { }
    private sealed class TestStringConVar : StringConVar { }
}

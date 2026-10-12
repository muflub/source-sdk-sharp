using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// worldspawn's networked state, as the managed side owns it
/// (plan_gamedll_after.md Part B2).
///
/// <para>
/// THE C# HALF OF THE SHIM. <c>world_shim_test.cpp</c> pins the shape on the
/// native side — no <c>DECLARE_SERVERCLASS</c>, no datadesc, the base table at
/// prop zero, each side against its own blob base. What is here is the half
/// that can be RUN: the ten props, the arithmetic that keeps them off the
/// parallel hierarchy, and every decision the nine map keys carry.
/// </para>
///
/// <para>
/// Each fact binds its own state through <see cref="NetState.BindLocal"/> and
/// drives <see cref="WorldNet.Apply"/> against it, so nothing here shares
/// static storage and nothing depends on an engine being up.
/// </para>
/// </summary>
public class WorldNetTests
{
    private static WorldNetState Fresh()
    {
        var state = new WorldNetState();
        state.BindLocal();
        return state;
    }

    // ---- the declaration itself ------------------------------------------

    [Fact]
    public void TheDeclarationCarriesTheTenPropsStockSent()
    {
        // DT_WORLD had ten SendProps and this declares ten fields with the same
        // meanings. Named rather than counted: a count that stays at ten while
        // one name is swapped for another is a count that said nothing, and the
        // native side resolves every one of them BY NAME.
        var layout = StandaloneNetStates.Of(typeof(WorldNetState));

        string[] expected =
        {
            "WaveHeight", "WorldMins", "WorldMaxs", "StartDark", "MaxOccludeeArea",
            "MinOccluderArea", "MaxPropScreenSpaceWidth", "MinPropScreenSpaceWidth",
            "DetailSpriteMaterial", "ColdWorld",
        };

        var got = layout.Fields.Select(f => f.Name).OrderBy(n => n).ToArray();

        Assert.Equal(expected.OrderBy(n => n).ToArray(), got);
    }

    [Fact]
    public void TheDetailSpriteMaterialReservesTheBufferTheClientAlwaysHad()
    {
        // C_World::MAX_DETAIL_SPRITE_MATERIAL_NAME_LENGTH was 256 and stock's
        // SendPropStringT allowed 512 — DT_MAX_STRING_BUFFERSIZE — so anything
        // past 256 was truncated on arrival and always had been. The
        // declaration reserves the number the client could actually hold, which
        // moves the truncation to where it is written down.
        var layout = StandaloneNetStates.Of(typeof(WorldNetState));

        Assert.Equal(256, layout["DetailSpriteMaterial"].Bytes);
        Assert.Equal(NetFieldKind.String, layout["DetailSpriteMaterial"].Kind);
    }

    [Fact]
    public void TheTwoFlagsShareTheirWordAndDoNotShareABit()
    {
        // The packer put StartDark and ColdWorld in ONE word at shifts 0 and 1.
        // That is why the native side asks NetStateFieldBits for both: a caller
        // that read a byte at StartDark's offset would get ColdWorld's bit with
        // it, which is exactly the hazard that slot exists for.
        var layout = StandaloneNetStates.Of(typeof(WorldNetState));

        var dark = layout["StartDark"];
        var cold = layout["ColdWorld"];

        Assert.Equal(dark.Offset, cold.Offset);
        Assert.NotEqual(dark.BitShift, cold.BitShift);
        Assert.True(dark.IsPacked);
        Assert.True(cold.IsPacked);
    }

    // ---- and it is NOT on the parallel hierarchy --------------------------

    [Fact]
    public void TheWorldIsDeclaredOffTheParallelHierarchy()
    {
        Assert.False(StandaloneNetStates.IsOnHierarchy(
            StandaloneNetStates.Of(typeof(WorldNetState))));
    }

    [Fact]
    public void TheHierarchyBlobIsNotGrownByAStateThatIsNotOnIt()
    {
        // WHAT HierarchyBlobBytes DOES WITH THE FLAG, asked of the function
        // rather than of the tree: the max is taken over on-hierarchy
        // declarations only, so an off-hierarchy one does not raise it whatever
        // its size. Two hand-picked types, so this stays a statement about the
        // function no matter what the real population happens to hold.
        //
        // IT IS NOT A STATEMENT ABOUT TODAY'S COST. Step 8a measured that
        // separately -- see TheHierarchyBlobIsNotACTUALLYGrownByTheWorldToday
        // below -- and the answer was that the world currently fits under the
        // max anyway. The comment here used to say "a 304-byte world"; the world
        // is 800 bytes now and the hierarchy blob is 848.
        var world = StandaloneNetStates.Of(typeof(WorldNetState));
        var onHierarchy = StandaloneNetStates.Of(typeof(EnvSpriteNetState));

        int withoutWorld = StandaloneNetStates.HierarchyBlobBytes(new[] { onHierarchy });
        int withWorld = StandaloneNetStates.HierarchyBlobBytes(new[] { onHierarchy, world });

        Assert.Equal(withoutWorld, withWorld);

        // THE CONTROL, because "the number did not move" is also what a broken
        // HierarchyBlobBytes answers. An ON-hierarchy declaration bigger than
        // the sprite's does move it.
        var bigger = StandaloneNetStates.Of(typeof(ColorCorrectionNetState));
        int withBigger = StandaloneNetStates.HierarchyBlobBytes(new[] { onHierarchy, bigger });
        Assert.True(withBigger > withoutWorld,
            "the control declaration did not grow the hierarchy blob either, so " +
            "this fact is measuring a scan that stopped working rather than the " +
            "world being excluded from it");
    }

    [Fact]
    public void TheHierarchyBlobIsNotACTUALLYGrownByTheWorldToday()
    {
        // STEP 8a: THE ARGUMENT FOR THE FLAG, MEASURED OVER THE REAL POPULATION
        // INSTEAD OF ASSERTED.
        //
        // StandaloneNetStateAttribute.OnHierarchy was defended in its own doc
        // comment by arithmetic: folding the world into HierarchyBlobBytes would
        // put its bytes on every entity on the hierarchy. That is what the fact
        // above proves the FUNCTION does. This one asks what it would COST here,
        // and the answer today is NOTHING: the world is smaller than the
        // greediest on-hierarchy declaration, so the max would not move.
        //
        // Pinned rather than left as prose because it cuts both ways. While it
        // holds, anyone defending the flag on byte count is defending it with a
        // vacuous argument, and the real reason -- the separate publish path
        // through WorldNetDeclare -- is the one to give. If the world ever grows
        // past the hierarchy's max this fact goes RED, the arithmetic argument
        // comes back, and the doc comment has to be rewritten again.
        var all = StandaloneNetStates.Discover(TestServices.ServerGame, new RecordingLog());
        var world = StandaloneNetStates.Of(typeof(WorldNetState));

        // The vacuity floor. Discover() answering an empty or tiny list would
        // make every comparison below true of nothing.
        Assert.True(all.Count > 50,
            $"only {all.Count} declarations discovered; this fact is measuring an "
            + "assembly scan that stopped working, not the world's size");
        Assert.Contains(all, s => ReferenceEquals(s, world) || s.Type == typeof(WorldNetState));

        int hierarchy = StandaloneNetStates.HierarchyBlobBytes(all);
        int worldRounded = StandaloneNetStates.HierarchyBlobBytes(
            all.Where(s => s.Type == typeof(WorldNetState))
               .Select(RegardedAsOnHierarchy).ToArray());

        Assert.True(worldRounded <= hierarchy,
            $"the world's blob is {worldRounded}b and the hierarchy's is {hierarchy}b. "
            + "The world no longer fits under the max, so folding it in WOULD now "
            + "cost every on-hierarchy entity those bytes -- the arithmetic "
            + "argument in StandaloneNetStateAttribute.OnHierarchy's doc comment "
            + "is live again and that comment says it is vacuous. Fix the comment.");
    }

    /// <summary>
    /// The same declaration, seen as if it were on the hierarchy, so
    /// <see cref="StandaloneNetStates.HierarchyBlobBytes"/> will measure it.
    /// The attribute is read off the TYPE, so there is no way to ask the
    /// question except by handing the function a list it will not skip — and a
    /// <see cref="StandaloneNetState"/> carries its own fields, so re-wrapping
    /// it changes nothing but which branch the max loop takes.
    /// </summary>
    private static StandaloneNetState RegardedAsOnHierarchy(StandaloneNetState s)
        => new(s.ClassName, typeof(WorldNetTests), s.Size, s.Fields);

    // ---- the nine keys ----------------------------------------------------

    [Fact]
    public void EveryKeyTheDatadescHeldIsAnsweredHere()
    {
        // CWorld's BEGIN_DATADESC is gone by the owner's ruling, and these nine
        // are what its DEFINE_KEYFIELD rows parsed. A key that stops being
        // answered is not a compile error — it is a value left at its zero, and
        // for eight of these nine that is a rendering difference.
        string[] expected =
        {
            "world_mins", "world_maxs", "startdark", "maxoccludeearea",
            "minoccluderarea", "maxpropscreenwidth", "minpropscreenwidth",
            "detailmaterial", "coldworld",
        };

        Assert.Equal(expected.OrderBy(k => k).ToArray(),
                     WorldNet.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public void TheChapterTitleAndGametitleAreNotThisDeclarationsKeys()
    {
        // Neither was ever networked. chaptertitle is special-cased in
        // CWorld::KeyValue because it has a live reader on the server;
        // gametitle is not parsed at all and its loss is recorded in
        // docs/divergences.md. Putting either here would put a value on
        // DT_WORLD that no client reads.
        Assert.DoesNotContain("chaptertitle", WorldNet.Keys);
        Assert.DoesNotContain("gametitle", WorldNet.Keys);
    }

    [Fact]
    public void AWorldBoundsKeyBecomesThreeFloats()
    {
        // Stock's `sscanf( szValue, "%f %f %f", &x, &y, &z )`, which lived in
        // CWorld::KeyValue until the parsing moved to the side that owns the
        // value.
        var state = Fresh();

        Assert.True(WorldNet.Apply(state, "world_mins", "-4608 1328 -144", new RecordingLog()));
        Assert.True(WorldNet.Apply(state, "world_maxs", "-2412 6976 652", new RecordingLog()));

        Assert.Equal(new Vector(-4608f, 1328f, -144f), state.WorldMins);
        Assert.Equal(new Vector(-2412f, 6976f, 652f), state.WorldMaxs);
    }

    [Fact]
    public void AFlagKeyIsANumberAndAnythingNonZeroIsTrue()
    {
        // Stock's FIELD_BOOLEAN parse. NOT bool.TryParse — a map writes "1",
        // not "True", and a map that wrote "True" got false from stock too.
        var state = Fresh();

        Assert.True(WorldNet.Apply(state, "coldworld", "1", new RecordingLog()));
        Assert.True(state.ColdWorld);

        Assert.True(WorldNet.Apply(state, "coldworld", "0", new RecordingLog()));
        Assert.False(state.ColdWorld);

        Assert.True(WorldNet.Apply(state, "coldworld", "2", new RecordingLog()));
        Assert.True(state.ColdWorld);
    }

    [Fact]
    public void SettingOneFlagLeavesTheOtherAlone()
    {
        // They share a word. A setter that wrote the whole word rather than its
        // own bits would clear the neighbour, and the symptom would be a level
        // that stopped fading up from black on a map that asked to.
        var state = Fresh();

        Assert.True(WorldNet.Apply(state, "startdark", "1", new RecordingLog()));
        Assert.True(WorldNet.Apply(state, "coldworld", "1", new RecordingLog()));
        Assert.True(state.StartDark);
        Assert.True(state.ColdWorld);

        Assert.True(WorldNet.Apply(state, "coldworld", "0", new RecordingLog()));
        Assert.True(state.StartDark);
        Assert.False(state.ColdWorld);
    }

    [Fact]
    public void AFloatKeyIsParsedInvariantOfTheMachinesLocale()
    {
        // A map's "-1" and "0.25" are written with a full stop wherever the
        // server runs. A parse that followed the machine's locale would read a
        // comma-decimal locale's numbers as integers and put the level's prop
        // fade range somewhere nobody chose, with nothing in any log.
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

            var state = Fresh();
            Assert.True(WorldNet.Apply(state, "maxoccludeearea", "0.25", new RecordingLog()));
            Assert.Equal(0.25f, state.MaxOccludeeArea);

            Assert.True(WorldNet.Apply(state, "maxpropscreenwidth", "-1", new RecordingLog()));
            Assert.Equal(-1f, state.MaxPropScreenSpaceWidth);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TheDetailMaterialIsCarriedAndIsTruncatedToTheBufferItDeclared()
    {
        var state = Fresh();

        Assert.True(WorldNet.Apply(state, "detailmaterial", "detail/detailsprites", new RecordingLog()));
        Assert.Equal("detail/detailsprites", state.DetailSpriteMaterial);

        // LONGER THAN THE FIELD RESERVED IS REFUSED, NOT TRUNCATED, and this is
        // the one place the port behaves differently from stock. Stock sent up
        // to DT_MAX_STRING_BUFFERSIZE and let the client's char[256] take the
        // first 255 bytes; the generated setter throws instead, with the
        // argument that a shortened material path is a lookup that succeeds
        // against the wrong file or fails against none.
        //
        // What this fact pins is that the throw does not reach the native
        // caller. WorldNet catches it, keeps the default and says so — an
        // exception unwinding into Source's C++ frames is what the ABI's own
        // comments refuse.
        var state2 = Fresh();
        Assert.True(WorldNet.Apply(state2, "detailmaterial", "detail/ok", new RecordingLog()));
        string tooLong = new string('m', 400);

        Assert.True(WorldNet.Apply(state2, "detailmaterial", tooLong, new RecordingLog()));
        Assert.Equal("detail/ok", state2.DetailSpriteMaterial);
    }

    [Fact]
    public void AKeyThatIsNotTheWorldsIsRefusedSoTheShimCanFallThrough()
    {
        // The shim OFFERS every key it does not itself answer, because a list of
        // the world's key names on the C++ side would be a second copy of this
        // declaration. False is what sends "targetname" on to
        // CBaseEntity::KeyValue, exactly as it always went.
        var state = Fresh();

        Assert.False(WorldNet.Apply(state, "targetname", "lockdown", new RecordingLog()));
        Assert.False(WorldNet.Apply(state, "skyname", "sky_day02_09", new RecordingLog()));
        Assert.False(WorldNet.Apply(state, "fogcolor", "168 186 193", new RecordingLog()));
    }

    [Fact]
    public void AValueThatWillNotParseIsClaimedAndLeftAtItsDefault()
    {
        // TRUE, NOT FALSE, and the distinction is the point. The key IS
        // worldspawn's — it just could not be parsed — and answering false would
        // send it on to CBaseEntity::KeyValue, which would report it as an
        // unknown key and hide the real complaint behind a second, wrong one.
        var state = Fresh();

        Assert.True(WorldNet.Apply(state, "world_mins", "not a vector", new RecordingLog()));
        Assert.Equal(new Vector(0f, 0f, 0f), state.WorldMins);

        Assert.True(WorldNet.Apply(state, "world_maxs", "1 2", new RecordingLog()));
        Assert.Equal(new Vector(0f, 0f, 0f), state.WorldMaxs);

        Assert.True(WorldNet.Apply(state, "minoccluderarea", "wide", new RecordingLog()));
        Assert.Equal(0f, state.MinOccluderArea);
    }

    [Fact]
    public void EveryKeyLandsOnItsOwnFieldAndOnNoOther()
    {
        // The map hands nine keys to one object in a row, so a table row that
        // pointed at the wrong field would be invisible in any test that set one
        // key and read it back. This sets all nine to distinguishable values and
        // reads all ten fields.
        var state = Fresh();

        Assert.True(WorldNet.Apply(state, "world_mins", "1 2 3", new RecordingLog()));
        Assert.True(WorldNet.Apply(state, "world_maxs", "4 5 6", new RecordingLog()));
        Assert.True(WorldNet.Apply(state, "startdark", "1", new RecordingLog()));
        Assert.True(WorldNet.Apply(state, "maxoccludeearea", "7", new RecordingLog()));
        Assert.True(WorldNet.Apply(state, "minoccluderarea", "8", new RecordingLog()));
        Assert.True(WorldNet.Apply(state, "maxpropscreenwidth", "9", new RecordingLog()));
        Assert.True(WorldNet.Apply(state, "minpropscreenwidth", "10", new RecordingLog()));
        Assert.True(WorldNet.Apply(state, "detailmaterial", "detail/x", new RecordingLog()));
        Assert.True(WorldNet.Apply(state, "coldworld", "1", new RecordingLog()));

        Assert.Equal(new Vector(1f, 2f, 3f), state.WorldMins);
        Assert.Equal(new Vector(4f, 5f, 6f), state.WorldMaxs);
        Assert.True(state.StartDark);
        Assert.Equal(7f, state.MaxOccludeeArea);
        Assert.Equal(8f, state.MinOccluderArea);
        Assert.Equal(9f, state.MaxPropScreenSpaceWidth);
        Assert.Equal(10f, state.MinPropScreenSpaceWidth);
        Assert.Equal("detail/x", state.DetailSpriteMaterial);
        Assert.True(state.ColdWorld);

        // The tenth has no key and never had one: stock's m_flWaveHeight was a
        // DEFINE_FIELD, written by a water volume's spawn and by nothing else.
        // It is carried because stock networked it, and it must stay zero
        // through a whole key parse.
        Assert.Equal(0f, state.WaveHeight);
    }

    // ---- the binding: one per module, kept between keys ----------------------

    /// <summary>A binding that counts its binds and hands out local states.</summary>
    private static (WorldNetBinding Binding, Func<int> Binds) Counting()
    {
        int binds = 0;
        var binding = new WorldNetBinding(_ =>
        {
            binds++;
            return Fresh();
        }, new RecordingLog());
        return (binding, () => binds);
    }

    [Fact]
    public void TheBindingIsKeptBetweenOneWorldsKeys()
    {
        var (binding, binds) = Counting();
        var world = new EntityHandle(0x0000_0001u);

        Assert.True(binding.ApplyKey(world, "startdark", "1"));
        Assert.True(binding.ApplyKey(world, "coldworld", "1"));

        Assert.True(binds() == 1, "one world's keys bind once");
        Assert.True(binding.Bound!.StartDark && binding.Bound.ColdWorld,
                    "and both keys land in the one bound state");
    }

    [Fact]
    public void AKeyThatIsNotTheWorldsDoesNotBind()
    {
        var (binding, binds) = Counting();

        Assert.False(binding.ApplyKey(new EntityHandle(0x0000_0001u), "targetname", "x"));
        Assert.True(binds() == 0, "a key that is not the world's costs no bind");
    }

    [Fact]
    public void UnbindMakesTheNextKeyBindAgain()
    {
        var (binding, binds) = Counting();
        var world = new EntityHandle(0x0000_0001u);

        binding.ApplyKey(world, "startdark", "1");
        binding.Unbind();

        Assert.Null(binding.Bound);
        binding.ApplyKey(world, "startdark", "1");
        Assert.True(binds() == 2, "after an unbind the next key resolves the world again");
    }

    [Fact]
    public void TwoBindingsDoNotShareAWorld()
    {
        // The property the two static fields lacked: a world bound through one
        // module's binding is not the state another module's keys land in.
        var (mine, _) = Counting();
        var (theirs, theirBinds) = Counting();
        var world = new EntityHandle(0x0000_0001u);

        mine.ApplyKey(world, "startdark", "1");

        Assert.Null(theirs.Bound);
        theirs.ApplyKey(world, "coldworld", "1");
        Assert.True(theirBinds() == 1, "the second binding binds for itself");
        Assert.False(theirs.Bound!.StartDark, "and does not see the first binding's key");
    }
}

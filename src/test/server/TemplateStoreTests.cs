using SourceSharp.Abi;
using System.Text;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The template store — <c>game/server/TemplateEntities.cpp</c>, ported
/// (plan_server_all.md Wave 1, docs/plan-audit.md D11).
///
/// <para>
/// The tokeniser underneath it has its own agreement with the real
/// <c>mapentities_shared.cpp</c> in <see cref="MapEntityBlockTests"/>. What
/// these add is the layer stock built on top of it: which values get the fixup
/// marker, which templates get flagged for the stamp, what the stamp is, and
/// when the deferred precache list is drained.
/// </para>
///
/// <para>
/// Every one of these was untestable while the store was C++ — not because the
/// C++ was wrong, but because <c>g_Templates</c> and
/// <c>g_iCurrentTemplateInstance</c> were file-scope, so reaching the store at
/// all meant loading a map. The class has no statics and a test makes its own.
/// </para>
/// </summary>
public class TemplateStoreTests
{
    /// <summary>
    /// A block of map text for one entity, as the BSP's entity lump spells one.
    /// </summary>
    private static byte[] Block(params (string Key, string Value)[] pairs)
    {
        var text = new StringBuilder();

        foreach (var (key, value) in pairs)
            text.Append('"').Append(key).Append("\" \"").Append(value).Append("\"\n");

        return Encoding.UTF8.GetBytes(text.Append("}\n").ToString());
    }

    private static string TextOf(byte[] data) => Encoding.UTF8.GetString(data);

    private static TemplateStore Relays(out int alpha, out int beta,
                                        string alphaOutput = "beta,Kill,,0,-1")
    {
        var store = new TemplateStore(new RecordingLog());

        alpha = store.Add("alpha", "logic_relay",
                          Block(("classname", "logic_relay"),
                                ("targetname", "alpha"),
                                ("OnTrigger", alphaOutput)));

        beta = store.Add("beta", "logic_relay",
                         Block(("classname", "logic_relay"),
                               ("targetname", "beta")));

        return store;
    }

    // ---- the store ---------------------------------------------------------

    [Fact]
    public void AStoredTemplateComesBackAsTheTextItWentInAs()
    {
        var store = new TemplateStore(new RecordingLog());
        byte[] text = Block(("classname", "prop_physics"), ("targetname", "crate"));

        int index = store.Add("crate", "prop_physics", text);

        Assert.True(index == 0, "the first template is index zero");
        Assert.True(TextOf(store.MapData(index)) == TextOf(text),
            "and its text comes back byte for byte — it is handed to the native "
            + "parser, which will read the classname out of it");
    }

    /// <summary>
    /// Stock's <c>-1</c>, and stock's reason: a template is looked up by
    /// targetname, so one with no name could never be found again.
    /// </summary>
    [Fact]
    public void ATemplateWithNoNameIsRefused()
    {
        var store = new TemplateStore(new RecordingLog());

        Assert.True(store.Add(null, "prop_physics", Block(("classname", "prop_physics"))) == -1,
            "a null name is not a name");
        Assert.True(store.Add("", "prop_physics", Block(("classname", "prop_physics"))) == -1,
            "and neither is an empty one");
        Assert.True(store.Count == 0, "and neither is stored");
    }

    /// <summary>
    /// The stored copy is allocated with room for every key to grow by the fixup
    /// marker. Not decoration: <c>SetValue</c> refuses a replacement that would
    /// not fit, so a template sized to its text alone would silently drop a
    /// connection the moment the reconnect pass tried to write one.
    /// </summary>
    [Fact]
    public void AStoredTemplateHasRoomForEveryKeyToGrowByTheMarker()
    {
        var store = new TemplateStore(new RecordingLog());
        byte[] text = Block(("classname", "logic_relay"),
                            ("targetname", "alpha"),
                            ("OnTrigger", "beta,Kill,,0,-1"));

        int index = store.Add("alpha", "logic_relay", text);

        // Three keys, six bytes of room each (the five-character marker plus a
        // terminator), plus one for the text's own.
        Assert.True(store.StringSize(index) == text.Length + 3 * 6 + 1,
            "the allocation is the text plus room for the marker on every key");
    }

    [Fact]
    public void ATemplateIsFoundByItsTargetnameWhateverItsCase()
    {
        var store = new TemplateStore(new RecordingLog());
        store.Add("Crate_01", "prop_physics", Block(("targetname", "Crate_01")));

        Assert.True(store.FindByTargetName("crate_01").Length > 0,
            "targetname matching is case-insensitive, as stock's stricmp is");
        Assert.True(store.FindByTargetName("crate_02").Length == 0,
            "and a name nothing carries finds nothing");
    }

    [Fact]
    public void AnIndexIntoNothingAnswersEmptyRatherThanThrowing()
    {
        var store = new TemplateStore(new RecordingLog());

        Assert.True(store.MapData(0).Length == 0, "an index into an empty store is empty");
        Assert.True(store.StringSize(-1) == 0, "and so is a negative one");
        Assert.False(store.RequiresEntityIoFixup(7), "and it needs no fixup");
    }

    // ---- the reconnect -----------------------------------------------------

    [Fact]
    public void AnOutputNamingAnotherTemplateInTheGroupGetsTheMarker()
    {
        var store = Relays(out int alpha, out int beta);

        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: true);

        Assert.True(TextOf(store.MapData(alpha)).Contains("beta&0000,Kill,,0,-1"),
            "the marker is inserted before the entity-I/O delimiter, not appended to "
            + "the whole value — the target is only the leading field");
    }

    [Fact]
    public void TheTemplateBeingPointedAtIsRenamedToMatch()
    {
        var store = Relays(out int alpha, out int beta);

        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: true);

        Assert.True(TextOf(store.MapData(beta)).Contains("\"targetname\" \"beta&0000\""),
            "or the rewritten output would point at a name nothing carries");
    }

    [Fact]
    public void BothEndsAreFlaggedForTheStamp()
    {
        var store = Relays(out int alpha, out int beta);

        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: true);

        Assert.True(store.RequiresEntityIoFixup(alpha), "the end holding the connection");
        Assert.True(store.RequiresEntityIoFixup(beta), "and the end being pointed at");
    }

    /// <summary>
    /// A group wired to itself in a DIFFERENT CASE.
    ///
    /// <para>
    /// Stock matches a group's targetnames with <c>stricmp</c>, and Hammer does
    /// not normalise the case a mapper typed: an output pointing at
    /// <c>Beta</c> and an entity named <c>beta</c> is an ordinary map. A
    /// case-sensitive match leaves the pair unreconnected, so the copies spawn
    /// still pointing at the originals — which have just been eaten — and the
    /// group does nothing, with no error anywhere.
    /// </para>
    ///
    /// <para>
    /// FOUND BY THE MUTATION HARNESS AND NOT BY REVIEW. Every other fact in
    /// this file names its templates in lower case, so
    /// <c>src/sourcesharp/test/template_store_mutate.sh</c>'s
    /// case-sensitivity arm stayed GREEN — eleven of its twelve arms reddened
    /// and this one measured nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void AGroupWiredToItselfInAnotherCaseIsStillReconnected()
    {
        var store = Relays(out int alpha, out int beta, alphaOutput: "BETA,Kill,,0,-1");

        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: true);

        Assert.True(TextOf(store.MapData(alpha)).Contains("BETA&0000,Kill,,0,-1"),
            "the reference is matched case-insensitively and rewritten in the case "
            + "the mapper wrote it");
        Assert.True(store.RequiresEntityIoFixup(beta),
            "and the entity being pointed at is flagged, whatever case it was named in");
    }

    [Fact]
    public void AnOutputNamingSomethingOutsideTheGroupIsLeftAlone()
    {
        var store = Relays(out int alpha, out int beta, alphaOutput: "a_door,Open,,0,-1");

        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: true);

        Assert.True(TextOf(store.MapData(alpha)).Contains("\"OnTrigger\" \"a_door,Open,,0,-1\""),
            "a name that belongs to the MAP and not to the group must not be renamed — "
            + "the copy is supposed to reach the real door");
        Assert.False(store.RequiresEntityIoFixup(alpha), "and nothing needs stamping");
    }

    /// <summary>
    /// A targetname is what everything else points AT. Renaming it inside the
    /// key walk would do the last loop's job in the middle of this one, and the
    /// value it was matched against would change underneath the walk.
    /// </summary>
    [Fact]
    public void ATargetnameIsNotTreatedAsAReferenceToItself()
    {
        var store = Relays(out int alpha, out int beta);

        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: true);

        Assert.False(TextOf(store.MapData(alpha)).Contains("alpha&0000"),
            "nothing in the group points at alpha, so alpha keeps its own name");
    }

    /// <summary>
    /// A value that is a bare name rather than an entity-I/O connection —
    /// <c>parentname</c>, say — gets the marker appended to the whole thing.
    /// </summary>
    [Fact]
    public void ABareNameGetsTheMarkerAppended()
    {
        var store = new TemplateStore(new RecordingLog());

        int child = store.Add("child", "prop_physics",
                              Block(("classname", "prop_physics"),
                                    ("targetname", "child"),
                                    ("parentname", "parent")));

        int parent = store.Add("parent", "prop_physics",
                               Block(("classname", "prop_physics"),
                                     ("targetname", "parent")));

        store.ReconnectIoForGroup(new[] { child, parent }, allowNameFixup: true);

        Assert.True(TextOf(store.MapData(child)).Contains("\"parentname\" \"parent&0000\""),
            "a value with no delimiter in it is a whole name and the marker goes on the end");
    }

    [Fact]
    public void PreserveNamesRewritesNothingAtAll()
    {
        var store = Relays(out int alpha, out int beta);

        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: false);

        Assert.True(TextOf(store.MapData(alpha)).Contains("\"OnTrigger\" \"beta,Kill,,0,-1\""),
            "the mapper asked for the group's own names and gets them");
        Assert.False(store.RequiresEntityIoFixup(alpha), "so there is nothing to stamp");
        Assert.False(store.RequiresEntityIoFixup(beta), "at either end");
    }

    [Fact]
    public void AGroupOfNothingIsNotAnError()
    {
        var store = new TemplateStore(new RecordingLog());

        store.ReconnectIoForGroup(Array.Empty<int>(), allowNameFixup: true);

        Assert.True(store.Count == 0, "an empty group leaves an empty store empty");
    }

    // ---- the stamp ---------------------------------------------------------

    [Fact]
    public void TheStampCarriesTheCurrentInstanceNumber()
    {
        var store = Relays(out int alpha, out int beta);
        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: true);

        store.StartUniqueInstance();

        Assert.True(TextOf(store.EntityIoFixedMapData(alpha)).Contains("beta&0001"),
            "the marker's four digits are replaced with this instance's number");
    }

    /// <summary>
    /// The one that makes two instances of a group work at all: each stamped
    /// copy carries its OWN number, so an output inside the second copy reaches
    /// the second copy's entities.
    /// </summary>
    [Fact]
    public void EachInstanceStampsADifferentNumber()
    {
        var store = Relays(out int alpha, out int beta);
        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: true);

        store.StartUniqueInstance();
        string first = TextOf(store.EntityIoFixedMapData(alpha));

        store.StartUniqueInstance();
        string second = TextOf(store.EntityIoFixedMapData(alpha));

        Assert.True(first.Contains("beta&0001"), "the first instance is number one");
        Assert.True(second.Contains("beta&0002"), "and the second is number two");
    }

    /// <summary>
    /// The stamp is the same LENGTH as the marker, which is what lets the same
    /// working copy be rewritten for ever without the offsets moving. A stamp
    /// that grew would corrupt the block from its second use onwards.
    /// </summary>
    [Fact]
    public void StampingDoesNotChangeTheLengthOfTheBlock()
    {
        var store = Relays(out int alpha, out int beta);
        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: true);

        int marked = store.MapData(alpha).Length;

        store.StartUniqueInstance();
        Assert.True(store.EntityIoFixedMapData(alpha).Length == marked,
            "the stamp is the same width as the marker it replaces");

        store.StartUniqueInstance();
        Assert.True(store.EntityIoFixedMapData(alpha).Length == marked,
            "and it still is on the second pass over the same copy");
    }

    /// <summary>
    /// Only a marker is stamped: an ampersand followed by exactly four digits.
    /// A map value that happens to contain one is left exactly as the mapper
    /// wrote it.
    /// </summary>
    [Fact]
    public void AnAmpersandThatIsNotAMarkerIsLeftAlone()
    {
        var store = new TemplateStore(new RecordingLog());

        int index = store.Add("sign", "point_message",
                              Block(("classname", "point_message"),
                                    ("targetname", "sign"),
                                    ("message", "Bolts & Nuts")));

        store.StartUniqueInstance();

        Assert.True(TextOf(store.EntityIoFixedMapData(index)).Contains("Bolts & Nuts"),
            "an ampersand with no four digits after it is not a fixup marker");
    }

    /// <summary>
    /// Stock wrapped at the number of digits the marker holds and asserted on
    /// the way past. It wraps silently here: ten thousand instances in one level
    /// is a map doing something unusual, not a program in trouble, and an assert
    /// halts a debug server.
    /// </summary>
    [Fact]
    public void TheInstanceNumberWrapsAtTheWidthOfTheMarker()
    {
        var store = new TemplateStore(new RecordingLog());

        for (int i = 0; i < 9999; i++)
            store.StartUniqueInstance();

        Assert.True(store.CurrentInstance == 9999, "the last number the four digits hold");

        store.StartUniqueInstance();
        Assert.True(store.CurrentInstance == 0, "and then it wraps rather than overflowing the field");
    }

    // ---- the deferred precache ---------------------------------------------

    [Fact]
    public void SchedulingAGroupQueuesOneEntryPerTemplate()
    {
        var store = Relays(out int alpha, out int beta);

        store.SchedulePrecache(new[] { alpha, beta }, allowNameFixup: true);

        Assert.True(store.PendingPrecacheCount == 2, "one per template, in the order given");
    }

    [Fact]
    public void DrainingEmptiesTheListAndHandsBackWhatWasInIt()
    {
        var store = Relays(out int alpha, out int beta);
        store.SchedulePrecache(new[] { alpha, beta }, allowNameFixup: true);

        var drained = store.DrainPendingPrecaches();

        Assert.True(drained.Count == 2, "everything scheduled comes back");
        Assert.True(store.PendingPrecacheCount == 0, "and the list is empty afterwards");
        Assert.True(store.DrainPendingPrecaches().Count == 0, "so a second drain finds nothing");
    }

    /// <summary>
    /// The drain burns an instance number FIRST, and it is stock's: the entities
    /// the precache pass builds are real for as long as it takes to precache
    /// them, so sharing a number with the first thing actually spawned would
    /// give two live entities the same fixed-up targetname.
    /// </summary>
    [Fact]
    public void TheDrainBurnsAnInstanceNumberBeforeHandingAnythingBack()
    {
        var store = Relays(out int alpha, out int beta);
        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: true);
        store.SchedulePrecache(new[] { alpha }, allowNameFixup: true);

        var drained = store.DrainPendingPrecaches();

        Assert.True(store.CurrentInstance == 1, "the drain took number one");
        Assert.True(TextOf(drained[0].MapData).Contains("beta&0001"),
            "and what it hands back is stamped with it");
    }

    [Fact]
    public void ADrainWithNothingInItBurnsNoNumber()
    {
        var store = new TemplateStore(new RecordingLog());

        store.DrainPendingPrecaches();

        Assert.True(store.CurrentInstance == 0,
            "a level with no templates must not spend an instance number, or the "
            + "first thing spawned would be number two");
    }

    /// <summary>
    /// A template whose group was NOT rewritten is precached from the stored
    /// text, not from a stamped copy. All three readers of the fixup decision
    /// have to agree or a group is precached as one thing and spawned as
    /// another.
    /// </summary>
    [Fact]
    public void APreserveNamesGroupIsPrecachedFromTheStoredText()
    {
        var store = Relays(out int alpha, out int beta);
        store.ReconnectIoForGroup(new[] { alpha, beta }, allowNameFixup: false);
        store.SchedulePrecache(new[] { alpha }, allowNameFixup: false);

        var drained = store.DrainPendingPrecaches();

        Assert.True(TextOf(drained[0].MapData).Contains("\"OnTrigger\" \"beta,Kill,,0,-1\""),
            "the copy that gets precached is the one that will be spawned");
    }

    // ---- the level lifecycle -----------------------------------------------

    [Fact]
    public void ALevelShutdownEmptiesTheStore()
    {
        var store = Relays(out int alpha, out _);
        store.SchedulePrecache(new[] { alpha }, allowNameFixup: true);

        store.OnLevelShutdownPostEntity();

        Assert.True(store.Count == 0, "the templates go");
        Assert.True(store.PendingPrecacheCount == 0, "and so does anything still waiting");
    }

    /// <summary>
    /// And the init half, which is what stock's <c>CTemplatesHook</c> existed
    /// for: a level that shut down between a schedule and its drain — a load
    /// cancelled, a map changed twice — would otherwise carry indices into a
    /// store that has been emptied into the next level.
    /// </summary>
    [Fact]
    public void ALevelInitEmptiesWhateverSurvivedTheLastOne()
    {
        var store = Relays(out int alpha, out _);
        store.SchedulePrecache(new[] { alpha }, allowNameFixup: true);

        store.OnLevelInitPreEntity("dm_lockdown");

        Assert.True(store.PendingPrecacheCount == 0,
            "nothing scheduled in the last level is drained in this one");
        Assert.True(store.Count == 0, "and no index into the last level's store survives");
    }
}

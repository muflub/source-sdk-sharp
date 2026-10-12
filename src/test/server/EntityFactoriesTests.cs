using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <b>The port's managed view of <c>EntityFactoryDictionary()</c></b> — lane
/// <c>sub-factory</c>.
///
/// <para>
/// The dictionary moved into the shim
/// (<c>src/sourcesharp/game/server/shell_standalone_class.cpp</c>) and its name
/// table is gtested behaviourally in <c>entity_factory_table_test</c>. This file
/// is the other half: the join between that table and the managed registry,
/// which is the only question NEITHER side can answer alone.
/// </para>
///
/// <para>
/// <b>THE PROVIDERS ARE FED HERE, AND THAT IS NOT A SUBSTITUTE FOR NATIVE.</b>
/// <c>EntityFactories</c> reaches the dictionary through five delegates, wired
/// to the ABI 242 slots by <c>EngineSide.BindEntityFactories</c> in the game. These
/// facts drive the delegates directly, so what is proved here is the LOGIC
/// above the edge —
/// the join, the ordering rule, the de-duplication, and above all the
/// distinction between "asked and got nothing" and "could not ask". The
/// agreement between the delegate and the real dictionary is a live check, not
/// this one, and saying so is the point: a fixture that stands in for native
/// drifts silently unless somebody asserts the agreement somewhere else.
/// </para>
///
/// <para>
/// Each fact has a table of its own (xUnit builds the class per fact), and the
/// helpers still reset it first, as a reload's pull does.
/// </para>
/// </summary>
public class EntityFactoriesTests
{
    // This fact class's own table: an instance since plan_abi_hotreload D7, so
    // nothing here is process-wide any more.
    private readonly EntityFactories _f = new();

    private static Func<string, bool> None => _ => false;

    private static Func<string, bool> Managed(params string[] names)
    {
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    /// <summary>Reset, and point the providers at a fixed native table.</summary>
    private void GiveNative(params string[] names)
    {
        _f.Reset();
        _f.NativeCount = () => names.Length;
        _f.NativeNameAt = i => i >= 0 && i < names.Length ? names[i] : null;
        _f.RefusalCount = null;
        _f.RefusalNameAt = null;
        _f.RefusalTimesAt = null;
        _f.Refresh();
    }

    /// <summary>
    /// Re-point the providers and pull again, WITHOUT resetting first — the
    /// shape a cs_sv_reload takes against a table that is already populated.
    /// </summary>
    private void Rewire(params string[] names)
    {
        _f.NativeCount = () => names.Length;
        _f.NativeNameAt = i => i >= 0 && i < names.Length ? names[i] : null;
        _f.Refresh();
    }

    /// <summary>Reset to the state this build actually ships in: unwired.</summary>
    private void GiveNoNative()
    {
        _f.Reset();
        _f.NativeCount = null;
        _f.NativeNameAt = null;
        _f.RefusalCount = null;
        _f.RefusalNameAt = null;
        _f.RefusalTimesAt = null;
        _f.Refresh();
    }

    // -----------------------------------------------------------------------
    // 1. NOT ASKED IS NOT EMPTY.
    //
    // The single most dangerous way to get this class wrong. With the slots
    // unwired an empty table would report Nothing for every native class, so
    // `ent_create prop_physics` would refuse a class that works and a map load
    // would declare 100+ absent capabilities that are all present.
    // -----------------------------------------------------------------------

    [Fact]
    public void WithNoProvidersTheTableIsNotAvailable()
    {
        GiveNoNative();

        Assert.False(_f.Available);
    }

    [Fact]
    public void WithNoProvidersNothingWasPulled()
    {
        GiveNoNative();

        Assert.False(_f.Pulled);
    }

    [Fact]
    public void AnUnaskedTableCallsAnUnknownClassNativeRatherThanNothing()
    {
        GiveNoNative();

        Assert.Equal(EntityFactoryOwner.Native,
                     _f.OwnerOf("prop_physics", None));
    }

    [Fact]
    public void AnUnaskedTableStillKnowsItsOwnManagedClasses()
    {
        GiveNoNative();

        Assert.Equal(EntityFactoryOwner.Managed,
                     _f.OwnerOf("ss_pickup", Managed("ss_pickup")));
    }

    [Fact]
    public void AnUnaskedTableDeclaresNothingAbsent()
    {
        GiveNoNative();

        var (answered, unanswered) = _f.SplitResidue(
            new[] { "worldspawn", "func_useableladder", "info_node" }, None);

        Assert.Equal(3, answered.Count);
        Assert.Empty(unanswered);
    }

    // -----------------------------------------------------------------------
    // 2. THE JOIN, once the dictionary can be asked.
    // -----------------------------------------------------------------------

    [Fact]
    public void APulledTableIsAvailableAndPulled()
    {
        GiveNative("worldspawn", "prop_physics");

        Assert.True(_f.Available);
        Assert.True(_f.Pulled);
    }

    [Fact]
    public void AClassWithANativeFactoryIsNative()
    {
        GiveNative("worldspawn", "prop_physics");

        Assert.Equal(EntityFactoryOwner.Native,
                     _f.OwnerOf("worldspawn", None));
    }

    [Fact]
    public void AClassWithNoFactoryAnywhereIsNothing()
    {
        GiveNative("worldspawn", "prop_physics");

        Assert.Equal(EntityFactoryOwner.Nothing,
                     _f.OwnerOf("func_useableladder", None));
    }

    [Fact]
    public void ManagedWinsOverNativeForAClassThatIsBoth()
    {
        // Every [ScriptedEntity] ALSO has a native factory — that is what
        // SourceSharp_InstallStandaloneClass installs — so a name in both lists
        // is a managed class seen from the other side, not a collision.
        GiveNative("ss_pickup");

        Assert.Equal(EntityFactoryOwner.Managed,
                     _f.OwnerOf("ss_pickup", Managed("ss_pickup")));
    }

    [Fact]
    public void TheNativeListIsCaseInsensitiveLikeTheDictionaryItMirrors()
    {
        // CEntityFactoryTable is built on k_eDictCompareTypeCaseInsensitive and
        // the shim's own class walk uses Q_stricmp. A managed mirror that
        // compared ordinally would report a map spelling `Prop_Physics` as a
        // class nothing can create.
        GiveNative("prop_physics");

        Assert.Equal(EntityFactoryOwner.Native,
                     _f.OwnerOf("PROP_PHYSICS", None));
    }

    [Fact]
    public void AnEmptyOrNullClassnameOwnsNothing()
    {
        GiveNative("worldspawn");

        Assert.Equal(EntityFactoryOwner.Nothing, _f.OwnerOf("", None));
        Assert.Equal(EntityFactoryOwner.Nothing, _f.OwnerOf(null, None));
    }

    // -----------------------------------------------------------------------
    // 3. THE PULL ITSELF.
    // -----------------------------------------------------------------------

    [Fact]
    public void TheWalkStopsAtANullNameRatherThanReadingPast()
    {
        // The contract is dense indices with null past the end. A null in the
        // MIDDLE means the far side is not the table this was written against,
        // and walking on reads whatever came after it.
        //
        // THERE IS A NAME AFTER THE NULL, and that is the whole fixture. With
        // only nulls after it, `continue` and `break` produce the same two
        // names and the fact passes over a walk that does not stop -- measured:
        // that mutation was GREEN before this line was added.
        _f.Reset();
        _f.NativeCount = () => 5;
        _f.NativeNameAt = i => i switch
        {
            0 => "worldspawn",
            1 => "prop_physics",
            3 => "ss_pickup",
            _ => null,
        };
        _f.Refresh();

        Assert.Equal(2, _f.NativeNames.Count);
        Assert.Equal(EntityFactoryOwner.Nothing,
                     _f.OwnerOf("ss_pickup", None));
    }

    [Fact]
    public void RefreshReplacesTheTableRatherThanAddingToIt()
    {
        GiveNative("worldspawn", "prop_physics");
        Assert.Equal(2, _f.NativeNames.Count);

        // RE-POINTED AND RE-PULLED, WITHOUT A Reset() BETWEEN, which is the
        // whole fact: a cs_sv_reload re-declares every class against a live
        // table and nothing calls Reset on that path. Going through GiveNative
        // here -- which resets first -- tested Reset instead, and a Refresh with
        // its Clear() removed passed it. Measured, before this was rewritten.
        Rewire("ss_pickup");

        Assert.Single(_f.NativeNames);
        Assert.Equal(EntityFactoryOwner.Nothing,
                     _f.OwnerOf("prop_physics", None));
    }

    [Fact]
    public void ResetPutsTheTableBackToNotAsked()
    {
        GiveNative("worldspawn");
        Assert.True(_f.Pulled);

        _f.Reset();

        Assert.False(_f.Pulled);
        Assert.Empty(_f.NativeNames);
        Assert.Equal(EntityFactoryOwner.Native,
                     _f.OwnerOf("func_useableladder", None));
    }

    [Fact]
    public void TheRefusalLedgerIsPulledWithItsCounts()
    {
        _f.Reset();
        _f.NativeCount = () => 1;
        _f.NativeNameAt = i => i == 0 ? "worldspawn" : null;

        var names = new[] { "info_node", "func_useableladder", "info_ladder_dismount" };
        var times = new[] { 2, 6, 28 };
        _f.RefusalCount = () => names.Length;
        _f.RefusalNameAt = i => i >= 0 && i < names.Length ? names[i] : null;
        _f.RefusalTimesAt = i => times[i];
        _f.Refresh();

        // The numbers dm_lockdown actually produced over one load: two Create
        // calls per residue block, 1 + 3 + 14 blocks.
        Assert.Equal(3, _f.Refusals.Count);
        Assert.Equal(("info_ladder_dismount", 28), _f.Refusals[2]);
    }

    // -----------------------------------------------------------------------
    // 4. THE RESIDUE SPLIT — dm_lockdown's own numbers.
    // -----------------------------------------------------------------------

    [Fact]
    public void TheResidueSplitsIntoWhatAnswersAndWhatDoesNot()
    {
        GiveNative("worldspawn");

        var (answered, unanswered) = _f.SplitResidue(
            new[] { "worldspawn", "info_node", "func_useableladder", "info_ladder_dismount" },
            None);

        Assert.Equal(new[] { "worldspawn" }, answered);
        Assert.Equal(new[] { "info_node", "func_useableladder", "info_ladder_dismount" },
                     unanswered);
    }

    [Fact]
    public void TheSplitDeduplicatesBecauseFourteenBlocksAreOneMissingCapability()
    {
        GiveNative("worldspawn");

        // The shape of the real residue list: one entry per BLOCK.
        var residue = new List<string> { "worldspawn" };
        residue.AddRange(Enumerable.Repeat("info_ladder_dismount", 14));
        residue.AddRange(Enumerable.Repeat("func_useableladder", 3));
        residue.Add("info_node");

        var (answered, unanswered) = _f.SplitResidue(residue, None);

        Assert.Single(answered);
        Assert.Equal(3, unanswered.Count);
    }

    [Fact]
    public void TheSplitKeepsFirstSeenOrder()
    {
        GiveNative();

        var (_, unanswered) = _f.SplitResidue(
            new[] { "zulu", "alpha", "zulu", "mike" }, None);

        Assert.Equal(new[] { "zulu", "alpha", "mike" }, unanswered);
    }

    [Fact]
    public void AManagedClassIsNeverResidue()
    {
        GiveNative("worldspawn");

        var (answered, unanswered) = _f.SplitResidue(
            new[] { "ss_pickup" }, Managed("ss_pickup"));

        Assert.Equal(new[] { "ss_pickup" }, answered);
        Assert.Empty(unanswered);
    }

    // -----------------------------------------------------------------------
    // 4b. WHAT ent_create IS TOLD, and what it does NOT have to go and look up.
    //
    // These two came out of DevCommandsLive because the decision they encode is
    // the factory dictionary's, not the console's -- and because putting them
    // here is what makes them testable at all. The live-entity walk they gate is
    // an engine call, and it used to sit behind a bare `&&`.
    // -----------------------------------------------------------------------

    [Fact]
    public void AManagedClassNeedsNoLiveCheck()
    {
        GiveNoNative();

        Assert.False(_f.NeedsLiveCheck(EntityFactoryOwner.Managed));
    }

    [Fact]
    public void AnAssumedNativeClassStillNeedsALiveCheck()
    {
        // Unwired: "Native" is what every unknown name reads as, and it means
        // assumed, not known. The walk is the only evidence available.
        GiveNoNative();

        Assert.True(_f.NeedsLiveCheck(EntityFactoryOwner.Native));
    }

    [Fact]
    public void AKnownNativeClassNeedsNoLiveCheck()
    {
        GiveNative("prop_physics");

        Assert.False(_f.NeedsLiveCheck(EntityFactoryOwner.Native));
    }

    [Fact]
    public void AClassNothingAnswersToAlwaysNeedsALiveCheck()
    {
        GiveNative("prop_physics");

        Assert.True(_f.NeedsLiveCheck(EntityFactoryOwner.Nothing));
    }

    [Fact]
    public void AKnownNativeClassIsAdvisedNothing()
    {
        GiveNative("prop_physics");

        Assert.Null(_f.CreateAdvice("ent_create", "prop_physics",
                                                 EntityFactoryOwner.Native));
    }

    [Fact]
    public void AClassNothingAnswersToIsRefusedByName()
    {
        GiveNative("prop_physics");

        string? advice = _f.CreateAdvice(
            "ent_create", "func_useableladder", EntityFactoryOwner.Nothing);

        Assert.NotNull(advice);
        Assert.Contains("func_useableladder", advice);
        Assert.Contains("dumpentityfactories", advice);
    }

    [Fact]
    public void AnUnaskedTableKeepsTheOldHedgeWordForWord()
    {
        // The behaviour this build actually ships until the ABI slots land, and
        // it must not change shape: the same sentence DevCommandsLive printed
        // before the dictionary was reachable at all.
        GiveNoNative();

        string? advice = _f.CreateAdvice(
            "ent_create", "prop_physics", EntityFactoryOwner.Native);

        Assert.NotNull(advice);
        Assert.Contains("is not a registered managed class and nothing of that class is live",
                        advice);
        Assert.Contains("if the native factory has no such name either", advice);
    }

    // -----------------------------------------------------------------------
    // 5. THE DECLARATION.
    // -----------------------------------------------------------------------

    [Fact]
    public void NothingUnansweredDeclaresNothing()
    {
        Assert.Null(EntityFactories.DeclareUnanswered(Array.Empty<string>(), 19));
    }

    [Fact]
    public void TheDeclarationNamesEveryClassAndTheBlockCount()
    {
        string? line = EntityFactories.DeclareUnanswered(
            new[] { "info_node", "func_useableladder", "info_ladder_dismount" }, 19);

        Assert.NotNull(line);
        Assert.Contains("info_node", line);
        Assert.Contains("func_useableladder", line);
        Assert.Contains("info_ladder_dismount", line);
        Assert.Contains("3 entity class(es)", line);
        Assert.Contains("19 block(s)", line);
    }

    [Fact]
    public void TheDeclarationSaysTheCapabilityIsAbsentRatherThanThatSomethingFailed()
    {
        // plan_entity.md §7: a refusal is declared, and what it declares is the
        // absence of a capability -- not an error that a retry might clear.
        string? line = EntityFactories.DeclareUnanswered(new[] { "func_useableladder" }, 18);

        Assert.NotNull(line);
        Assert.Contains("absent", line);
        Assert.StartsWith("[SourceSharp]", line);
        Assert.EndsWith("\n", line);
    }
}

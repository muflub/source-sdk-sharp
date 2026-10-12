using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// D19's <c>EntityGetClassName</c> inversion: "is this handle a hierarchy
/// player", answered from the registry that already drives the entity instead
/// of asking the shell for a string 6.000 times a server frame.
///
/// <para>
/// <b>What these facts are about is the WRITE SET and the ORACLE, not the
/// dictionary.</b> The registry is not a new mirror — it is the object that
/// already owns this entity's spawn, keyvalues, thinks, inputs and outputs, so
/// a stale entry is not a new failure class: it is an entity that had already
/// stopped working. What IS new is that a classname question now has a second
/// answerer, so the two things worth pinning are that the call sites really did
/// stop crossing, and that the gate comparing the two answers reads the SLOT
/// rather than a second look at managed's own belief.
/// </para>
/// </summary>
public class PlayerMirrorTests
{
    private static List<PlayerMirrorComparison> Compare(string slotClassName, bool registrySaysPlayer)
    {
        var rows = new List<PlayerMirrorComparison>();
        PlayerMirrorCheckCommand.Compare(0x0D19_0001u, slotClassName, registrySaysPlayer, rows.Add);
        return rows;
    }

    //-------------------------------------------------------------------------
    // The comparison unit.
    //-------------------------------------------------------------------------

    [Fact]
    public void BothSayPlayerIsNoDivergence()
    {
        Assert.Empty(Compare(EntityNames.SsPlayer, registrySaysPlayer: true));
    }

    [Fact]
    public void BothSayNotAPlayerIsNoDivergence()
    {
        Assert.Empty(Compare("prop_physics", registrySaysPlayer: false));
    }

    [Fact]
    public void ASpawnPushThatNeverFiredIsReported()
    {
        var rows = Compare(EntityNames.SsPlayer, registrySaysPlayer: false);

        Assert.True(rows.Count == 1 && !rows[0].Registry && rows[0].Slot,
            "the engine calls it ss_player and the registry has no instance: the arsenal would "
            + "refuse to serve a real player, and every pickup they walk over would fall through "
            + "to the native arm that answers nothing for a standalone");
    }

    [Fact]
    public void AnOrphanedInstanceIsReported()
    {
        var rows = Compare("prop_physics", registrySaysPlayer: true);

        Assert.True(rows.Count == 1 && rows[0].Registry && !rows[0].Slot,
            "the dangerous direction: the registry answers 'player' for something the engine "
            + "does not, so the arsenal serves weapons and ammo to a crate");
    }

    /// <summary>
    /// THE CASE RULE, PINNED. Stock matches classnames with <c>stricmp</c>, and
    /// a mutation arm on this project once stayed green because every fact
    /// named its subject in lower case. The comparison here is ORDINAL on
    /// purpose: a shell that started answering <c>SS_PLAYER</c> is a divergence
    /// the gate must SEE, not one it normalises away before anybody can act on
    /// it.
    /// </summary>
    [Fact]
    public void AClassnameThatDiffersOnlyInCaseIsADivergence()
    {
        var rows = Compare(EntityNames.SsPlayer.ToUpperInvariant(), registrySaysPlayer: true);

        Assert.True(rows.Count == 1 && rows[0].SlotClassName == "SS_PLAYER",
            "the slot's string is reported verbatim, so a case divergence is legible rather than "
            + "silently equal");
    }

    [Fact]
    public void TheReportNamesTheHandleAndBothAnswers()
    {
        var rows = Compare("prop_physics", registrySaysPlayer: true);

        string text = rows[0].ToString();

        Assert.True(text.Contains("prop_physics", StringComparison.Ordinal)
                    && text.Contains("registry=player", StringComparison.Ordinal)
                    && text.Contains("slot=not", StringComparison.Ordinal),
            $"a divergence line has to say what diverged and which way round; got '{text}'");
    }

    //-------------------------------------------------------------------------
    // THE CALL SITES, AS FACTS.
    //
    // This is the half a commit message cannot do. The whole prize of the row
    // is that these three sites stopped crossing; a later edit that puts a
    // GetClassName back is a red test here rather than 6 crossings a frame
    // nobody notices for a month.
    //-------------------------------------------------------------------------

    private static string ServerSource(string file)
    {
        string? path = RepoLayout.FindSdkFile(Path.Combine("src", "sourcesharp", "managed",
                                                 "SourceSharp.Server", file));

        Assert.True(path is not null, $"{file} not found in {RepoLayout.SdkTree}");

        return StripComments(File.ReadAllText(path!));
    }

    private static string StripComments(string code)
    {
        code = Regex.Replace(code, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        code = Regex.Replace(code, @"///[^\n]*", " ");
        code = Regex.Replace(code, @"//[^\n]*", " ");
        return code;
    }

    [Fact]
    public void ArsenalAsksTheRegistryRatherThanTheShell()
    {
        string code = ServerSource("Arsenal.cs");

        Assert.True(Regex.IsMatch(code,
                @"internal\s+bool\s+IsStandalonePlayer\s*\(\s*EntityHandle\s+player\s*\)\s*=>\s*_directory\.Get\(\s*player\s*\)\s+is\s+SsPlayer\s*;"),
            "Arsenal.IsStandalonePlayer is the registry lookup D19 made it; anchored on the whole "
            + "signature and body rather than on a prefix, because tools/vacuous-assert-audit "
            + "ratchets 117 sites that matched a prefix and could not fail");
    }

    [Fact]
    public void ArsenalHasNoGetClassNameLeft()
    {
        string code = ServerSource("Arsenal.cs");

        Assert.False(code.Contains("GetClassName", StringComparison.Ordinal),
            "both of Arsenal's classname crossings left with D19 — IsStandalonePlayer and the "
            + "bullet-hit blood check; a third one added later is 6 crossings a frame back");
    }

    [Fact]
    public void ArsenalPlayersDefersToArsenalRatherThanRepeatingTheRule()
    {
        string code = ServerSource("ArsenalPlayers.cs");

        Assert.True(Regex.IsMatch(code,
                @"private\s+bool\s+IsStandalone\s*\(\s*EntityHandle\s+player\s*\)\s*=>\s*_arsenal\.IsStandalonePlayer\(\s*player\s*\)\s*;"),
            "the decorator defers rather than repeating the rule, so the two halves cannot drift "
            + "apart on what a hierarchy player is");
    }

    /// <summary>
    /// THE ENGINE AREA IS NOT TAKEN AT ALL. Asking for
    /// <c>IEngineEntities</c> and leaving it unread would compile, pass every
    /// fact above, and leave the next reader believing the decorator still has
    /// a reason to cross.
    /// </summary>
    [Fact]
    public void ArsenalPlayersNoLongerTakesTheEntitiesArea()
    {
        string code = ServerSource("ArsenalPlayers.cs");

        Assert.True(Regex.IsMatch(code,
                @"public\s+ArsenalPlayers\s*\(\s*IEnginePlayers\s+inner\s*,\s*Arsenal\s+arsenal\s*\)"),
            "the constructor takes two arguments now; IEngineEntities was there for the classname "
            + "crossing and nothing else");
    }

    //-------------------------------------------------------------------------
    // THE ORACLE.
    //-------------------------------------------------------------------------

    /// <summary>
    /// X2/X3's divergence gate found a real bug <c>make check</c> was green
    /// over: the census control was corrupting the mirror it measured. The
    /// equivalent trap here is a gate that asks the REGISTRY twice — it would
    /// agree with itself whatever the shells did.
    /// </summary>
    [Fact]
    public void TheGateComparesAgainstTheSlotAndNotAgainstItself()
    {
        string code = ServerSource("PlayerMirrorCheckCommand.cs");

        // THE NEEDLE HAS TO BE THE CALL, NOT THE SUBSTRING. This fact first
        // read `code.Contains("_entities.GetClassName(entity)")`, and the
        // mutation arm that replaces PASS ONE's oracle with a second look at
        // the registry left it GREEN — because pass TWO reads the slot through
        // the same expression, so the substring was still there. That is the
        // check-that-cannot-fail this project has logged 43 times: a substring
        // needle misses added text, and here it missed REMOVED text for the
        // same reason. Anchored on pass one's whole argument list, and on the
        // count, so removing either reading reddens.
        Assert.True(Regex.IsMatch(code,
                @"Compare\(\s*entity\.Value\s*,\s*_entities\.GetClassName\(\s*entity\s*\)\s*,"),
            "pass one's oracle is the EntityGetClassName slot, read through IEngineEntities, "
            + "which is still the straight call to SourceSharp_EntityGetClassName");

        Assert.True(Regex.Matches(code, @"_entities\.GetClassName\(").Count == 2,
            "exactly two readings of the slot — one per direction of the walk; a pass that stops "
            + "asking the engine is a pass that agrees with managed whatever the shells did");

        Assert.True(code.Contains("_directory.OfClass(EntityNames.SsPlayer)", StringComparison.Ordinal),
            "pass two walks what the registry holds — the direction a walk of the engine's list "
            + "structurally cannot see");
    }

    /// <summary>
    /// The slot is KEPT rather than deleted, for the same reason X5 kept
    /// <c>EntityIsValid</c>: without it there is no independent oracle left to
    /// gate against.
    /// </summary>
    [Fact]
    public void TheEntityGetClassNameSlotIsStillOnTheAbi()
    {
        // A shim-area method since plan_client_vtables CC-last deleted the
        // struct: its row in the slot table and its field in the area's layout.
        Assert.True(EngineApiAreasTests.SlotField("EntityGetClassName") is not null,
            "the slot stays: it is the only answer to 'what does the engine call this' that "
            + "managed did not write itself");
    }
}

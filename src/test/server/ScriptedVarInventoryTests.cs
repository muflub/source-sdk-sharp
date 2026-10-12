using SourceSharp.Abi;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// EVERY MAP KEY IN THE BUILD, WRITTEN DOWN.
///
/// <para>
/// IT WAS WRITTEN FOR ONE JOB. The declared keys moved out of attributed FIELDS,
/// bound by reflection at registration, into <c>[ScriptVars]</c> classes whose
/// dispatch is generated — and the way those two would have disagreed is by
/// quietly losing a key: a private field on a base class, a name only the
/// attribute knew, a class the generator never saw. A list taken from the OLD
/// binding, before any of it moved, and checked against the new one afterwards
/// is the only thing that could say the sets were identical rather than assumed
/// to be. They were.
/// </para>
///
/// <para>
/// IT DID NOT EXPIRE WITH THE MOVE, and it is now one of the two things standing
/// where the reflective binding used to. What it is is the answer to "what may a
/// map send this entity" for the whole build, in one file: a key that vanishes
/// in a refactor fails here by name rather than by the entity going quiet in a
/// map somebody loads a month later. (The other is the generator's own
/// diagnostics, which catch at build time what this catches after it.)
/// </para>
///
/// <para>
/// TO REGENERATE, when a key is deliberately added or renamed: run the test
/// binary with <c>SOURCESHARP_DUMP_VARS=1</c> and paste what it prints over
/// <c>ScriptedVarInventory.cs</c>. Regenerating it to make a failure go away is
/// how this test would become the check that cannot fail, so read the diff.
/// </para>
/// </summary>
public class ScriptedVarInventoryTests
{
    /// <summary>The registry, and the inventory it produces, folded to lower case.</summary>
    private static (string[] Actual, string[] Expected) Compare()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(ScriptedVarInventoryTests).Assembly);
        registry.Services = Services();

        var actual = Inventory(registry);

        if (Environment.GetEnvironmentVariable("SOURCESHARP_DUMP_VARS") == "1")
        {
            Console.WriteLine("--- BEGIN SCRIPTED VAR INVENTORY ---");
            foreach (string line in actual)
                Console.WriteLine(line);
            Console.WriteLine("--- END SCRIPTED VAR INVENTORY ---");
        }

        // FOLDED TO LOWER CASE, and only the key part of it moves. The list was
        // taken when each key carried the map's own spelling in an attribute;
        // most now take the property's, because a property IS its key and map
        // keys have always matched case-insensitively. "startdisabled" and
        // "StartDisabled" are one key to the engine, so a case difference is not
        // a difference in what a map may send.
        //
        // That fold is only lossless because two keys on one class cannot differ
        // by case alone — the generator fails the build on the pair (SSVAR004)
        // rather than letting one win — and the check below is what says so here
        // rather than trusting it.
        var expected = ScriptedVarInventory.Lines
                           .Select(l => l.ToLowerInvariant()).ToArray();

        actual = actual.Select(l => l.ToLowerInvariant()).ToArray();

        return (actual, expected);
    }

    [Fact]
    public void NoTwoKeysOnOneClassDifferOnlyByCase()
    {
        var (actual, _) = Compare();

        Assert.True(actual.Length == actual.Distinct(StringComparer.Ordinal).Count(), "no two keys on one class differ only by case");
    }

    [Fact]
    public void NoDeclaredMapKeyHasGone()
    {
        // Named rather than counted. "596 keys, expected 597" sends the reader
        // to a diff tool; "trigger_push lost 'speed'" sends them to the class.
        var (actual, expected) = Compare();

        var missing = expected.Except(actual, StringComparer.Ordinal).ToArray();

        foreach (string line in missing.Take(20))
            Assert.Fail($"map key still declared: {line}");

        Assert.True(missing.Length == 0, $"every declared map key is still declared ({expected.Length})");
    }

    [Fact]
    public void NoMapKeyIsMissingFromTheInventory()
    {
        var (actual, expected) = Compare();

        var added = actual.Except(expected, StringComparer.Ordinal).ToArray();

        foreach (string line in added.Take(20))
            Assert.Fail($"map key not in the inventory: {line}");

        Assert.True(added.Length == 0, $"every map key the build declares is in the inventory ({actual.Length})");
    }

    /// <summary>
    /// One line per (entity class, key): what a map may write, and what it is
    /// read as. Sorted, so the file is a diff rather than a listing.
    /// </summary>
    private static string[] Inventory(ScriptedEntityRegistry registry)
        => registry.DescribeClasses()
                   .SelectMany(c => c.Vars.Select(v => $"{c.ClassName}|{v.Name}|{v.TypeName}"))
                   .OrderBy(l => l, StringComparer.Ordinal)
                   .ToArray();
}

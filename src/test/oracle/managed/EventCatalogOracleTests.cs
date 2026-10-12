using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.OracleTests;

/// <summary>
/// The animation-event table's type masks, against the registrations in
/// <c>game/shared/eventlist.cpp</c> they were transcribed from.
///
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests</b> (EventCatalogTests, in
/// AnimListCatalogTests.cs). Neither DLL compiles eventlist.cpp any more, so the
/// farm no longer links it and the old <c>[SharedSourceFact("eventlist.cpp")]</c>
/// reported SKIPPED; this reads the pristine upstream clone through
/// <see cref="ValveSdk"/>, where a missing file is a failure. The enum-order
/// fact over <c>eventlist.h</c>, which the build still compiles, stays in
/// EventCatalogTests.
/// </para>
/// </summary>
public class EventCatalogOracleTests
{
    [Fact]
    public void EveryMaskIsTheOneStockRegistered()
    {
        string cpp = ValveSdk.Find("src/game/shared/eventlist.cpp");

        var native = Regex.Matches(File.ReadAllText(cpp),
                                   @"REGISTER_SHARED_ANIMEVENT\s*\(\s*(\w+)\s*,\s*([A-Z_0-9|\s]+?)\s*\)")
                          .Select(m => (Name: m.Groups[1].Value,
                                        Mask: MaskOf(m.Groups[2].Value)))
                          .ToList();

        Assert.True(native.Count > 40,
                    $"the event list was parsed rather than missed ({native.Count})");
        Assert.Equal(native.Select(e => e.Name).ToList(), EventCatalog.Ordered);
        Assert.Equal(native.Select(e => e.Mask).ToList(), EventCatalog.NativeTypes);
    }

    private static int MaskOf(string expression)
    {
        int mask = 0;

        foreach (string flag in expression.Split('|', StringSplitOptions.TrimEntries))
        {
            mask |= flag switch
            {
                "AE_TYPE_SERVER" => 1 << 0,
                "AE_TYPE_SCRIPTED" => 1 << 1,
                "AE_TYPE_SHARED" => 1 << 2,
                "AE_TYPE_WEAPON" => 1 << 3,
                "AE_TYPE_CLIENT" => 1 << 4,
                "AE_TYPE_FACEPOSER" => 1 << 5,
                _ => 0,
            };
        }

        return mask;
    }
}

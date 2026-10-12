using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.OracleTests;

/// <summary>
/// The side catalog, against the event list it mirrors.
///
/// <para>
/// This is the fixture-versus-native rule applied to a table that would
/// otherwise be a hand-copied list nobody could check: a wrong side flag here
/// makes the client act on the server's events, or silently drop its own, and
/// nothing anywhere reports either.
/// </para>
///
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests</b> (AnimationEventCatalogTests,
/// in AnimationEventDispatchTests.cs). Neither DLL compiles
/// <c>game/shared/eventlist.cpp</c> any more, so the farm no longer links it;
/// this fact reads the pristine upstream clone through <see cref="ValveSdk"/>,
/// and a missing file is a failure rather than the pass its old not-found arm
/// reported.
/// </para>
/// </summary>
public class AnimationEventCatalogOracleTests
{
    [Fact]
    public void EveryRegisteredEventIsInTheCatalogWithTheSideNativeGivesIt()
    {
        string path = ValveSdk.Find("src/game/shared/eventlist.cpp");

        var native = new Dictionary<string, AnimationEventSide>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(File.ReadAllText(path),
                                          @"REGISTER_SHARED_ANIMEVENT\(\s*(\w+)\s*,\s*([A-Z_0-9|\s]+?)\s*\)"))
        {
            AnimationEventSide side = AnimationEventSide.Unknown;

            foreach (string flag in m.Groups[2].Value.Split('|', StringSplitOptions.TrimEntries))
            {
                side |= flag switch
                {
                    "AE_TYPE_SERVER" => AnimationEventSide.Server,
                    "AE_TYPE_CLIENT" => AnimationEventSide.Client,
                    "AE_TYPE_WEAPON" => AnimationEventSide.Weapon,
                    _ => AnimationEventSide.Unknown,
                };
            }

            native[m.Groups[1].Value] = side;
        }

        Assert.True(native.Count > 40,
                    $"the event list was parsed rather than missed ({native.Count} registrations found)");

        foreach (var (name, side) in native)
        {
            Assert.True(AnimationEventCatalog.SideOf(name) == side,
                        $"the catalog gives {name} the side eventlist.cpp does " +
                        $"({AnimationEventCatalog.SideOf(name)} against {side})");
        }

        foreach (string name in AnimationEventCatalog.All.Keys)
        {
            Assert.True(native.ContainsKey(name),
                        $"the catalog's {name} is an event eventlist.cpp still registers");
        }
    }
}

extern alias client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using client::SourceSharp.Client;
using client::SourceSharp.Client.Standalone;
using client::SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// <see cref="ClientTempEntFrame"/>'s prop and door sets against stock's own
/// <c>LINK_ENTITY_TO_CLASS</c> lines in <c>props.cpp</c>, <c>doors.cpp</c> and
/// <c>BasePropDoor.h</c>.
///
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> Neither DLL compiles those
/// three files any more, so the farm no longer links them; this fact reads the
/// pristine upstream clone through <see cref="ValveSdk"/>, where a missing file
/// is a failure rather than a skip. The rest of TempEntFrameTests stays where
/// it was.
/// </para>
/// </summary>
public class TempEntFrameOracleTests
{
    /// <summary>
    /// The prop and door sets are stock's own <c>LINK_ENTITY_TO_CLASS</c>
    /// lines, read off disk.
    ///
    /// <para>
    /// A MANAGED SET COMPARED AGAINST A MANAGED SET AGREES WITH ITSELF. What
    /// makes this catch anything is that <c>props.cpp</c> and <c>doors.cpp</c>
    /// are the tree's own answer to "what is a CDynamicProp", and two of the
    /// entries are surprises nobody would write from memory:
    /// <c>prop_door_rotating</c> is a DYNAMIC PROP (its class derives from
    /// <c>CDynamicProp</c> through <c>CBasePropDoor</c>) and <c>func_water</c>
    /// is a DOOR.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDynamicPropAndDoorSetsAreStocksOwnLinkEntityToClassLines()
    {
        string props = ValveSdk.Server("props.cpp");
        string doors = ValveSdk.Server("doors.cpp");
        string basePropDoor = ValveSdk.Server("BasePropDoor.h");

        // Every class LINK_ENTITY_TO_CLASS'd straight to CDynamicProp.
        var linked = new HashSet<string>();

        foreach (Match m in Regex.Matches(File.ReadAllText(props),
                     @"LINK_ENTITY_TO_CLASS\(\s*(\w+)\s*,\s*CDynamicProp\s*\)"))
        {
            linked.Add(m.Groups[1].Value);
        }

        Assert.True(linked.Count >= 3,
            $"props.cpp links at least dynamic_prop, prop_dynamic and prop_dynamic_override "
          + $"to CDynamicProp; found {linked.Count}");

        foreach (string name in linked)
        {
            Assert.True(ClientTempEntFrame.IsDynamicProp(name),
                $"'{name}' is LINK_ENTITY_TO_CLASS'd to CDynamicProp in props.cpp and the "
              + "collision arm must answer true for it, or a gib falls through it");
        }

        // COrnamentProp derives from CDynamicProp, so its classname is one too.
        Assert.True(Regex.IsMatch(File.ReadAllText(props),
                        @"class\s+COrnamentProp\s*:\s*public\s+CDynamicProp"),
            "COrnamentProp still derives from CDynamicProp");
        Assert.True(ClientTempEntFrame.IsDynamicProp(EntityNames.PropDynamicOrnament),
            "so prop_dynamic_ornament is a dynamic prop");

        // And so does CBasePropDoor, which is why prop_door_rotating is one.
        Assert.True(Regex.IsMatch(File.ReadAllText(basePropDoor),
                        @"CBasePropDoor\s*:\s*public\s+CDynamicProp"),
            "CBasePropDoor still derives from CDynamicProp");
        Assert.True(ClientTempEntFrame.IsDynamicProp(EntityNames.PropDoorRotating),
            "so prop_door_rotating answers stock's PROP cast, before the door cast is reached");

        // The doors, likewise off doors.cpp.
        string doorText = File.ReadAllText(doors);

        foreach (Match m in Regex.Matches(doorText,
                     @"LINK_ENTITY_TO_CLASS\(\s*(\w+)\s*,\s*C(?:Base|Rot)Door\s*\)"))
        {
            string name = m.Groups[1].Value;

            Assert.True(ClientTempEntFrame.IsDoor(name),
                $"'{name}' is LINK_ENTITY_TO_CLASS'd to a CBaseDoor in doors.cpp and the "
              + "collision arm must answer true for it");
        }

        Assert.True(ClientTempEntFrame.IsDoor(EntityNames.FuncWater),
            "func_water most of all -- doors.cpp links it straight to CBaseDoor, which nothing "
          + "about the name suggests");
    }
}

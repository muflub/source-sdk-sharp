using System.Text.RegularExpressions;

using SourceSharp.Server;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// PropBreak's two spawnflag numbers against the #defines in the pristine
/// props_shared.h they copy. Only the number crosses to the engine, so a
/// renumbering nobody mirrored would make every piece inherit motion-disabled
/// from a bolted-down crate, or stop counting gibs. Neither produces an error
/// anywhere. No DLL compiles props_shared.h any more, so this reads the
/// upstream clone: a missing header fails here rather than skipping, as the
/// farm read in PropBreakTests did.
/// </summary>
public sealed class PropBreakSpawnFlagsOracleTests
{
    private static int Flag(string name)
    {
        string text = ValveSdk.Read("src/game/shared/props_shared.h");
        Match m = Regex.Match(text, @"#define\s+" + Regex.Escape(name) + @"\s+0x([0-9A-Fa-f]+)");
        Assert.True(m.Success, $"props_shared.h declares {name}");
        return Convert.ToInt32(m.Groups[1].Value, 16);
    }

    [Fact]
    public void SpawnFlagMotionDisabledIsPropsSharedsMotionDisabled()
        => Assert.Equal(PropBreak.SpawnFlagMotionDisabled, Flag("SF_PHYSPROP_MOTIONDISABLED"));

    [Fact]
    public void SpawnFlagIsGibIsPropsSharedsIsGib()
        => Assert.Equal(PropBreak.SpawnFlagIsGib, Flag("SF_PHYSPROP_IS_GIB"));
}

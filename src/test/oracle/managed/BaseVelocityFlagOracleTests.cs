extern alias client;

using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// FL_BASEVELOCITY as each side's managed movement restates it, against the
/// block of the pristine const.h this build compiles. const.h defines the
/// entity flags twice: the single-player games' block (FL_BASEVELOCITY is
/// 1&lt;&lt;23) and the multiplayer block under its #else (1&lt;&lt;24). Nothing
/// in this build defines HL2_DLL, PORTAL, HL2_EPISODIC or HL2_LOSTCOAST, so
/// the multiplayer block is the one that applies. The client's prediction once
/// restated the flag as 1&lt;&lt;5, which is FL_INRAIN there. It then folded
/// base velocity into momentum on a frame the server did not, and the replay
/// diverged on conveyors and push triggers.
/// </summary>
public sealed class BaseVelocityFlagOracleTests
{
    // The multiplayer block: from the #else after the single-player block's
    // FL_BASEVELOCITY to its #endif.
    private static string MultiplayerBlock()
    {
        string text = Regex.Replace(ValveSdk.Read("src/public/const.h"), @"//[^\n]*", string.Empty);
        int sp = text.IndexOf("#if !defined( HL2MP ) && ( defined( PORTAL )", StringComparison.Ordinal);
        Assert.True(sp >= 0, "const.h opens the single-player flag block");
        int open = text.IndexOf("\n#else", sp, StringComparison.Ordinal);
        int close = text.IndexOf("\n#endif", open, StringComparison.Ordinal);
        Assert.True(open > sp && close > open, "const.h's flag block has its multiplayer #else arm");
        return text[open..close];
    }

    private static int Flag(string block, string name)
    {
        Match m = Regex.Match(block, $@"^\s*#define\s+{name}\s+\(1<<(\d+)\)", RegexOptions.Multiline);
        Assert.True(m.Success, $"the multiplayer block defines {name} as (1<<N)");
        return 1 << int.Parse(m.Groups[1].Value);
    }

    [Fact]
    public void TheClientsPredictionUsesTheMultiplayerFlagBit()
    {
        FieldInfo? field = typeof(client::SourceSharp.Client.ClientPlayerPrediction)
            .GetField("FlBaseVelocity", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(Flag(MultiplayerBlock(), "FL_BASEVELOCITY"), (int)(uint)field!.GetRawConstantValue()!);
    }

    [Fact]
    public void TheServersCommandRulesUseTheMultiplayerFlagBit()
        => Assert.Equal(ServerCommandRules.FlBaseVelocity, Flag(MultiplayerBlock(), "FL_BASEVELOCITY"));

    // The control: the block read is the multiplayer one, whose FL_INRAIN is
    // the bit the client's prediction had mistaken for FL_BASEVELOCITY.
    [Fact]
    public void TheBlockReadIsTheMultiplayerOne()
    {
        string block = MultiplayerBlock();
        Assert.Equal(1 << 24, Flag(block, "FL_BASEVELOCITY"));
        Assert.Equal(1 << 5, Flag(block, "FL_INRAIN"));
    }
}

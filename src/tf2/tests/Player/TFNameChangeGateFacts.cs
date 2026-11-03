using SourceSharp.Server;

using TF2.Server;
using TF2.Server.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <c>CTFGameRules::ChangePlayerName</c>'s gate
/// (tf_gamerules.cpp:10217) as <see cref="TFNameChangeGate"/>: the rename
/// happens exactly while <c>tf_allow_player_name_change</c>
/// (tf_gamerules.cpp:731) is on, whatever the names are.
/// </summary>
public class TFNameChangeGateFacts
{
    private static TFNameChangeGate Rig(string value)
    {
        var convar = new TFAllowPlayerNameChangeConVar();
        convar.Bind(-1, "tf_allow_player_name_change", value);
        return new TFNameChangeGate(convar);
    }

    [Fact]
    public void The_rename_is_allowed_while_the_gate_is_on()
    {
        Assert.True(Rig("1").MayChange(2, "alice", "bob"));
    }

    [Fact]
    public void The_rename_is_refused_once_the_gate_is_off()
    {
        Assert.False(Rig("0").MayChange(2, "alice", "bob"));
    }

    [Fact]
    public void The_verdict_takes_no_notice_of_the_names()
    {
        var gate = Rig("1");
        Assert.True(gate.MayChange(2, "", ""));
        Assert.True(gate.MayChange(2, "a", "a"));
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server;

/// <summary>What can be voted on in TF2: rounds and wins rather than frags, which it does not play to.</summary>
[SideService(Side.Server, As = [typeof(IVoteSettingSource)])]
public sealed class TFVoteSettings : IVoteSettingSource
{
    public IReadOnlyList<VoteSetting> VoteSettings { get; } =
    [
        GameVoteOptions.FriendlyFire(),
        GameVoteOptions.TimeLimit(),
        new VoteSetting("mp_maxrounds", ("0", "#SS_UI_Vote_NoLimit"), ("3", "3"), ("5", "5"), ("10", "10")),
        new VoteSetting("mp_winlimit", ("0", "#SS_UI_Vote_NoLimit"), ("2", "2"), ("3", "3"), ("5", "5")),
    ];
}

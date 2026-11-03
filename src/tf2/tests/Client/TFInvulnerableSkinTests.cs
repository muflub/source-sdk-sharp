extern alias client;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

using Xunit;

using ClientTFPlayerData = client::TF2.Client.Player.ClientTFPlayerData;
using ClientCond = client::TF2.Shared.TFCond;
using ClientState = client::TF2.Shared.Player.TFPlayerNetState;
using ClientWords = client::TF2.Shared.Player.TFPlayerNetStateViews;
using Mask = client::TF2.Shared.Conditions.TFCondMask;
using TFTeam = client::TF2.Shared.TFTeam;

namespace TF2.Tests.Client;

/// <summary>
/// The invulnerable body's skin, stock <c>C_TFPlayer::GetSkin</c>'s
/// <c>+2</c> arm (c_tf_player.cpp:7827-7833): skins 2 and 3, the team's base
/// plus two, the disguise's included.
/// </summary>
public sealed class TFInvulnerableSkinTests
{
    private const int red = (int)TFTeam.Red, blue = (int)TFTeam.Blue;

    [Fact]
    public void An_invulnerable_red_player_wears_skin_two()
        => Assert.Equal(2, ClientTFPlayerData.SkinFor(With(ClientCond.Invulnerable), red, red));

    [Fact]
    public void An_invulnerable_blue_player_wears_skin_three()
        => Assert.Equal(3, ClientTFPlayerData.SkinFor(With(ClientCond.Invulnerable), blue, blue));

    [Fact]
    public void A_vulnerable_player_wears_the_plain_team_skin()
        => Assert.Equal(1, ClientTFPlayerData.SkinFor(With(), blue, red));

    [Fact]
    public void Every_invulnerable_condition_wears_the_skin()
    {
        Assert.Equal(2, ClientTFPlayerData.SkinFor(With(ClientCond.InvulnerableUserBuff), red, red));
        Assert.Equal(2, ClientTFPlayerData.SkinFor(With(ClientCond.InvulnerableCardEffect), red, red));
        Assert.Equal(2, ClientTFPlayerData.SkinFor(With(ClientCond.InvulnerableHideUnlessDamaged), red, red));
    }

    [Fact]
    public void An_invulnerable_disguise_wears_the_disguises_ueber_skin()
    {
        ClientState spy = With(ClientCond.Disguised, ClientCond.Invulnerable);
        spy.DisguiseTeam = blue;

        Assert.Equal(3, ClientTFPlayerData.SkinFor(spy, red, blue));
    }

    [Fact]
    public void An_unknown_team_wears_nothing_until_known()
        => Assert.Null(ClientTFPlayerData.SkinFor(With(ClientCond.Invulnerable), 0, red));

    /// <summary>A bound carrier word with the named conditions.</summary>
    private static ClientState With(params ClientCond[] conds)
    {
        var state = new ClientState();
        state.BindLocal();
        ClientWords.SetConditions(state, Mask.Of([.. conds]));
        return state;
    }
}

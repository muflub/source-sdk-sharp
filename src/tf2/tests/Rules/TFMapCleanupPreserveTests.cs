using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// TF2's rows in the round cleanup's preserve chain (D10): the service-made
/// classes every filter's cleanup must leave standing — most sharply when
/// <c>sv_mode null</c> cleans with the core's HL2MP list, which names none of
/// them (the round gate's mode step, where the missing rows killed a joined
/// player's carrier). The chain's answer end-to-end — a real cleanup leaving
/// a standing carrier — is <see cref="Player.TFPlayerCarrierStalenessTests"/>.
/// </summary>
public sealed class TFMapCleanupPreserveTests
{
    [Theory]
    [InlineData("tf_player_data")]          // the carrier: what the mode step lost
    [InlineData("TF_PLAYER_DATA")]          // the list matches Q_stricmp-style
    [InlineData("tf_player_manager")]
    [InlineData("tf_wearable")]
    [InlineData("tf_building_resource")]
    [InlineData("tf_objective_resource")]
    [InlineData("TF_Team_Resource")]
    public void The_hook_preserves_every_class_TF2s_services_make(string className)
        => Assert.True(new TFMapCleanupPreserve().Preserves(className));

    [Theory]
    [InlineData("logic_relay")]             // an ordinary map entity: gone, and made again
    [InlineData("info_target")]
    [InlineData("weapon_scout")]
    [InlineData("keyframe_rope")]           // TF's own filter list keeps this; the hook does not
    [InlineData("player")]                  // the filter's, not the hook's
    [InlineData("")]
    public void The_hook_names_nothing_else(string className)
        => Assert.False(new TFMapCleanupPreserve().Preserves(className));

    /// <summary>
    /// The chain reaches cleanup only through the server's registration:
    /// the attribute is the registration (ServerModule composes the
    /// <c>IMapCleanupPreserve</c> hooks from it), and a hook that stops
    /// being registered goes back to silently killing the carrier.
    /// </summary>
    [Fact]
    public void The_hook_is_a_server_service_serving_the_preserve_contract()
    {
        var attribute = Assert.Single(typeof(TFMapCleanupPreserve).GetCustomAttributes(typeof(SideServiceAttribute), false));
        var side = Assert.IsType<SideServiceAttribute>(attribute);
        Assert.Equal(Side.Server, side.Side);
        Assert.Contains(typeof(IMapCleanupPreserve), side.As);
    }
}

extern alias client;

using System.Reflection;

using SourceSharp.Abi;

using Xunit;

using ClientCollisionGroup = client::SourceSharp.Abi.EngineCollisionGroup;
using ClientSideServiceAttribute = client::SourceSharp.Abi.SideServiceAttribute;
using IBreakPieceGroupRule = client::SourceSharp.Client.TempEnts.IBreakPieceGroupRule;
using TFBreakPieceGroup = client::TF2.Client.TempEnts.TFBreakPieceGroup;

namespace TF2.Tests.TempEnts;

/// <summary>
/// TF_CLIENT_DLL's arm of the client BreakModelCreateSingle (physpropclientside.cpp:752-754):
/// a broken piece is DEBRIS, after Initialize made it PUSHAWAY.
/// </summary>
public sealed class TFBreakPieceGroupTests
{
    [Fact]
    public void A_broken_piece_on_the_client_is_DEBRIS()
    {
        IBreakPieceGroupRule rule = new TFBreakPieceGroup();

        Assert.Equal(ClientCollisionGroup.Debris, rule.Group(ClientCollisionGroup.PushAway));
    }

    [Fact]
    public void The_rule_is_a_client_service()
    {
        Assert.Contains(typeof(TFBreakPieceGroup).GetCustomAttributes<ClientSideServiceAttribute>(), a => a.Side == Side.Client);
    }
}

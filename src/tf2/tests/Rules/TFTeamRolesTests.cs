using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;

using TF2.Tests.Entities;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The team roles (<c>CTFTeam::SetRole</c>/<c>GetRole</c>, tf_team.h:57-58;
/// the rules proxy's <c>SetRedTeamRole</c>/<c>SetBlueTeamRole</c> inputs,
/// tf_gamerules.cpp:1605-1606 and their handlers :1785-1806): one role per
/// team, named by the map, cleared with the level.
/// </summary>
public sealed class TFTeamRolesTests
{
    /// <summary>The proxy's integer input names RED's role in the store.</summary>
    [Fact]
    public void TheProxyInputSetsRedsRole()
    {
        var rig = new ObjectiveRig();
        var roles = new TFTeamRoles();
        rig.Services.AddInstance<ITFTeamRoles>(roles);
        var proxy = rig.Spawn<TFGameRulesProxy>(TFGameRulesProxy.MapClass);

        // Stock fires the FIELD_INTEGER value on the number slot; TEAM_ROLE_DEFENDERS is 1.
        rig.Registry.Input(proxy.Handle, "SetRedTeamRole", string.Empty, 1f, EntityHandle.None);

        Assert.Equal(TFTeamRole.Defenders, roles.RoleOf(TFTeam.Red));
        Assert.Equal(TFTeamRole.None, roles.RoleOf(TFTeam.Blue));
    }

    /// <summary>A new level clears the store, as stock remakes the teams with it.</summary>
    [Fact]
    public void RolesResetAtLevelInit()
    {
        var roles = new TFTeamRoles();
        roles.SetRole(TFTeam.Red, TFTeamRole.Attackers);

        ((IGameLifecycle)roles).OnLevelInit("cp_dustbowl");

        Assert.Equal(TFTeamRole.None, roles.RoleOf(TFTeam.Red));
    }

    /// <summary>A team no one named stands at None (stock's zero-initialized m_iRole).</summary>
    [Fact]
    public void RoleOfAnUnsetTeamIsNone()
    {
        Assert.Equal(TFTeamRole.None, new TFTeamRoles().RoleOf(TFTeam.Blue));
    }
}

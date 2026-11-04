//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using TF2.Shared;
using TF2.Shared.Buildings;
using TF2.Shared.Player;

namespace TF2.Client.Buildings;

/// <summary>
/// The client half of <c>tf_building_resource</c>: the decoded building
/// columns the engineer's HUD reads (stock's <c>C_BaseObject</c> rows).
/// Kept in <see cref="TFClientBuildingResources"/> from its first update to
/// its removal.
/// </summary>
[ClientStandaloneEntity(TFEntityNames.BuildingResource)]
public sealed class ClientTFBuildingResource : ClientStandaloneEntity
{
    private readonly TFClientBuildingResources _resources;

    public ClientTFBuildingResource(IEngineEntityRender render, TFClientBuildingResources resources)
        : base(render)
    {
        _resources = resources;
    }

    public override string ClassName => TFEntityNames.BuildingResource;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public TFBuildingResourceNetState State = new();

    /// <summary>How many updates have decoded into <see cref="State"/>.</summary>
    public int Decodes { get; private set; }

    public override void OnSpawn() => _resources.Live = this;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        Decodes++;
        _resources.Live = this;
    }

    public override void OnRemoved()
    {
        if (ReferenceEquals(_resources.Live, this))
            _resources.Live = null;
    }
}

/// <summary>The client's live <c>tf_building_resource</c>, when one has arrived.</summary>
[SideService(Side.Client)]
public sealed class TFClientBuildingResources
{
    public ClientTFBuildingResource? Live { get; set; }

    /// <summary>The rows the local slot carries, one line each.</summary>
    public string Report(int localSlot)
    {
        if (Live is not { } resource)
            return $"[TF2] {TFEntityNames.BuildingResource}: none has arrived";

        var state = resource.State;
        var text = new System.Text.StringBuilder($"[TF2] {TFEntityNames.BuildingResource}: {resource.Decodes} update(s)");
        for (int row = localSlot * TFBuildingResourceNetState.Kinds;
             row < (localSlot + 1) * TFBuildingResourceNetState.Kinds;
             row++)
        {
            if (state.Flags[row] == 0)
                continue;

            text.Append($"\n  row {row % TFBuildingResourceNetState.Kinds}: " +
                        $"level {state.Level[row]}, health {state.Health[row]}/{state.MaxHealth[row]}, " +
                        $"build {state.BuildProgress[row]}%, ammo {state.Ammo[row]}/{state.MaxAmmo[row]}, " +
                        $"flags {state.Flags[row]}");
        }

        return text.ToString();
    }
}

/// <summary>The console door onto the local player's building rows.</summary>
[ConCommand("tf2_cl_building_resource", Help = "The tf_building_resource rows of the local player this client has decoded")]
public sealed class TFBuildingResourceReportCommand(ILog log, IEngineHud screen, TFClientBuildingResources resources)
{
    public void Execute() => log.Msg(resources.Report(screen.LocalPlayerIndex) + "\n");
}

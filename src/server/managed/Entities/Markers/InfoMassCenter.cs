//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// CInfoMassCenter (physobj.cpp): the mass centre of the physics prop or
/// physbox its target names goes where this point stands, and the point goes.
/// </summary>
[ScriptedEntity(EntityNames.InfoMassCenter)]
public sealed class InfoMassCenter : ScriptedEntity
{
    public InfoMassCenter(MassCenterOverrides massCenters)
    {
        _massCenters = massCenters;
    }

    private readonly MassCenterOverrides _massCenters;

    /// <summary>What a map may set on this entity. See <see cref="InfoMassCenterVars"/>.</summary>
    [ScriptVars] public InfoMassCenterVars InfoMassCenterVars = new();

    public override void OnSpawn()
    {
        if (InfoMassCenterVars.Target.Length == 0)
            return;

        _massCenters.Add(MassCenterOverride.SnapToPoint(InfoMassCenterVars.Target, Origin));
        Remove();
    }
}

/// <summary>
/// The map keys <see cref="InfoMassCenter"/> declares.
/// </summary>
[ScriptVars]
public partial class InfoMassCenterVars
{
    /// <summary>The physics prop or physbox whose mass centre this is.</summary>
    public string Target { get; set; } = string.Empty;
}

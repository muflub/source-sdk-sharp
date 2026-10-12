//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>Passes entities with a given name.</summary>
[ScriptedEntity(EntityNames.FilterActivatorName)]
public sealed class FilterActivatorName : FilterBase
{
    /// <summary>What a map may set on this entity. See <see cref="FilterActivatorNameVars"/>.</summary>
    [ScriptVars] public FilterActivatorNameVars FilterActivatorNameVars = new();

    protected override bool Matches(EntityHandle entity)
        => FilterActivatorNameVars.Name.Length > 0 && string.Equals(Entities.GetName(entity), FilterActivatorNameVars.Name, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The map keys <see cref="FilterActivatorName"/> declares.
/// </summary>
[ScriptVars]
public partial class FilterActivatorNameVars
{
    [VarName("filtername")]
    public string Name { get; set; } = string.Empty;
}

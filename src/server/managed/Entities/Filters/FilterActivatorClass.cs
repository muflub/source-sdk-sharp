//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>Passes entities of a given class.</summary>
[ScriptedEntity(EntityNames.FilterActivatorClass)]
public sealed class FilterActivatorClass : FilterBase
{
    /// <summary>What a map may set on this entity. See <see cref="FilterActivatorClassVars"/>.</summary>
    [ScriptVars] public FilterActivatorClassVars FilterActivatorClassVars = new();

    protected override bool Matches(EntityHandle entity)
        => FilterActivatorClassVars.Class.Length > 0 && string.Equals(Entities.GetClassName(entity), FilterActivatorClassVars.Class, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The map keys <see cref="FilterActivatorClass"/> declares.
/// </summary>
[ScriptVars]
public partial class FilterActivatorClassVars
{
    [VarName("filterclass")]
    public string Class { get; set; } = string.Empty;
}

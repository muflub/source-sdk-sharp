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
/// CBaseProp::Spawn's refusals for a dynamic prop (props.cpp): a prop with no
/// model, and one whose model's prop data says it is a physics prop's (or is
/// bad), are deleted -- unless the class is prop_dynamic_override, which is
/// how a map keeps one anyway (CDynamicProp::OverridePropdata).
/// </summary>
internal static class DynamicPropSpawn
{
    /// <summary>
    /// Why stock deletes this prop as it spawns, or null when it keeps it.
    /// The caller removes it and logs <see cref="Line"/>; the origin is read
    /// only then.
    /// </summary>
    public static string? Refusal(string className, string model, IEnginePrecache precache,
                                  IEngineEntityRender render, IServerPropData propData)
    {
        if (model.Length == 0)
            return "missing modelname";

        precache.PrecacheModel(model);

        if (string.Equals(className, EntityNames.PropDynamicOverride, StringComparison.OrdinalIgnoreCase))
            return null;

        return propData.ModelPropDataKind(render.GetModelKeyValueText(model)) switch
        {
            PropDataParse.BadData => $"uses model {model}, which has an invalid prop_data type. DELETED.",
            PropDataParse.Succeeded => $"uses model {model}, which has propdata which means that it be used on a prop_physics. DELETED.",
            _ => null,
        };
    }

    /// <summary>Stock's console line for a refusal.</summary>
    public static string Line(string className, Vector origin, string refusal)
        => refusal == "missing modelname"
            ? $"prop at {origin.X:0} {origin.Y:0} {origin.Z:0} missing modelname\n"
            : $"{className} at {origin.X:0} {origin.Y:0} {origin.Z:0} {refusal}\n";
}

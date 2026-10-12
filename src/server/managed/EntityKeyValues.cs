//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A key applied to a RUNNING entity, as stock's <c>CBaseEntity::KeyValue</c>
/// applies it (baseentity_shared.cpp): "origin" is <c>SetAbsOrigin</c> and
/// "angles" is <c>SetAbsAngles</c>, which mark the network state changed, and
/// every key, those two included, then reaches the entity's own KeyValue.
///
/// <para>
/// The shell's arm of KeyValue only RECORDS origin and angles, because the map
/// spawner hands it keys before Spawn places the entity. Asked after Spawn
/// (ent_keyvalue, AddOutput), that record moved the server's copy and told the
/// client nothing: 7e's gate read the server 400 units on and the client 0.
/// The move is decided here and carried out by the engine's SetOrigin /
/// SetAngles.
/// </para>
/// </summary>
public sealed class EntityKeyValues(IEngineEntities entities)
{
    /// <summary>Applies one key; true when the entity's KeyValue took it.</summary>
    public bool Apply(EntityHandle entity, string key, string value)
    {
        if (key.Equals("origin", StringComparison.OrdinalIgnoreCase))
            entities.SetOrigin(entity, StringToVector(value));
        else if (key.Equals("angles", StringComparison.OrdinalIgnoreCase))
            entities.SetAngles(entity, StringToVector(value));

        return entities.SetKeyValue(entity, key, value);
    }

    /// <summary>UTIL_StringToVector: up to three numbers; a missing one is zero.</summary>
    public static Vector StringToVector(string value)
    {
        Span<float> v = stackalloc float[3];
        var parts = (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < 3 && i < parts.Length; i++)
            v[i] = float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
        return new Vector(v[0], v[1], v[2]);
    }
}

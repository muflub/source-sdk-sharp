//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Server;

/// <summary>
/// <b>Stock's "warn on out of order precache"</b>, for a model
/// (CBaseEntity::PrecacheModel, baseentity.cpp): when the precache window is
/// shut and the engine does not already have the model, a developer message
/// says so. The precache itself is made either way. Precache_Model
/// (precache_model.cpp) made this check in C++ until plan_server_native_v2 T4e;
/// it now hands over the name and the window's flag, once per precache.
/// </summary>
/// <param name="isModelPrecached"><c>engine-&gt;IsModelPrecached( name )</c>.
/// Asked only when the window is shut, as stock asks it.</param>
/// <param name="devMsg">Stock's <c>DevMsg</c>: the console under <c>developer 1</c>.</param>
public sealed class LatePrecache(Func<string, bool> isModelPrecached, Action<string> devMsg)
{
    /// <summary>Checks one model about to be precached; <paramref name="allowed"/> is the window's flag.</summary>
    public void CheckModel(string name, bool allowed)
    {
        if (string.IsNullOrEmpty(name) || allowed)
            return;

        if (!isModelPrecached(name))
            devMsg($"Late precache of {name} -- not necessarily a bug now that we allow ~everything to be dynamically loaded.\n");
    }
}

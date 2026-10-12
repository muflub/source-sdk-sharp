//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// clienteffectprecachesystem.cpp's level-time decisions: at a level's start
/// every registered client effect references its materials, then the
/// material system caches what is used and the impact effects take their
/// particle material handles; at its end every registration lets go. The
/// registry is native's (each CLIENTEFFECT_REGISTER object adds itself at
/// static init). Replayed against the recorded system (ClientEffectPrecacheTests).
/// </summary>
public sealed class ClientEffectPrecache(IFxPrecache registry) : IClientEffectPrecacheHost
{
    /// <summary>Level starts and ends seen since the load (cs_cl_fx_report).</summary>
    public int LevelInits { get; private set; }

    public int LevelShutdowns { get; private set; }

    public void LevelInit()
    {
        LevelInits++;

        // Precache all known effects.
        int n = registry.Count();
        for (int i = 0; i < n; i++)
            registry.Cache(i, true);

        // Finally, force the cache of these materials, then cache off the
        // material handles.
        registry.CacheUsedMaterials();
        registry.CacheMaterialHandles();
    }

    public void LevelShutdown()
    {
        LevelShutdowns++;

        // Mark all known effects as free.
        int n = registry.Count();
        for (int i = 0; i < n; i++)
            registry.Cache(i, false);
    }
}

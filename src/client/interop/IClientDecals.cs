//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Client;

/// <summary>
/// The client's decal name and index tables: <c>game/shared/decals.cpp</c>'s
/// <c>CDecalEmitterSystem</c> as client.so compiled it (plan_client_novalve
/// cluster 18, lane c2-sims). What answers <c>decalsystem</c> natively is a
/// forwarding shim (<c>shell_decals_client.cpp</c>) over this, so the client's
/// two callers (the impact effects and the temp entity backends) are unedited.
/// </summary>
public interface IClientDecals
{
    /// <summary>Stock's <c>Init</c>: reads <c>scripts/decals_subrect.txt</c>.</summary>
    void Init();

    /// <summary>Stock's <c>LevelInitPreEntity</c>: every entry's index from the engine.</summary>
    void LevelInitPreEntity();

    /// <summary>Stock's <c>Shutdown</c>: the tables cleared.</summary>
    void Shutdown();

    /// <summary>A weighted pick over one group, as a decal index; −1 for none.</summary>
    int IndexForName(string? name);

    /// <summary>The entry precached under <paramref name="index"/>, or null (stock's <c>""</c>).</summary>
    string? NameForIndex(int index);

    /// <summary>The group a game material turns an impact into; null for the caller's own name.</summary>
    string? TranslateForGameMaterial(string decalName, int gameMaterial);
}

namespace SourceSharp.Abi;

/// <summary>What a map decal's precache asks of the server's engine.</summary>
internal interface IDecalPrecacheEngine
{
    /// <summary><c>CBaseEntity::IsPrecacheAllowed()</c>: whether the precache window is open.</summary>
    bool PrecacheAllowed();

    /// <summary><c>engine->IsDecalPrecached( name )</c>.</summary>
    bool IsDecalPrecached(string name);

    /// <summary><c>engine->PrecacheDecal( name, preload )</c>: the decal's index, or -1.</summary>
    int PrecacheDecal(string name, bool preload);

    /// <summary>The console's <c>Warning</c>.</summary>
    void Warning(string text);
}

/// <summary>
/// A map decal's precache (plan_server_native_v2 T4d): the
/// IShimPrecache::PrecacheDecal slot's guard and stock's UTIL_PrecacheDecal,
/// over IVEngineServer's forwarders and the level's precache window.
/// </summary>
internal static class DecalPrecache
{
    /// <summary>
    /// The decal's index, or -1: for no name (asking the engine nothing), and
    /// for a material that would not load, which is a mapper's typo the caller
    /// reports. Preloaded, as the slot asked. A precache after the window has
    /// closed warns, as UTIL_PrecacheDecal does, unless the decal was already
    /// precached -- and is made anyway.
    /// </summary>
    public static int Precache<T>(T engine, string? name) where T : IDecalPrecacheEngine
    {
        if (string.IsNullOrEmpty(name))
            return -1;

        if (!engine.PrecacheAllowed() && !engine.IsDecalPrecached(name))
            engine.Warning($"Late precache of {name}\n");

        return engine.PrecacheDecal(name, true);
    }
}

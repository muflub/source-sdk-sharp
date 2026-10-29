//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client.Flashlight;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// The two renderer settings a client decal is gated on, both the ENGINE's
/// ConVars (engine.so declares each; the client reads them by name):
/// <c>r_decals</c>, which stock's world, BSP, footprint and player decal temp
/// entities test before they draw, and <c>r_drawmodeldecals</c>, which
/// <c>C_BaseEntity::AddStudioDecal</c> tests before a decal goes on a model.
/// </summary>
public interface IDecalSettings
{
    /// <summary><c>r_decals.GetInt() != 0</c>.</summary>
    bool Decals { get; }

    /// <summary><c>r_drawmodeldecals.GetBool()</c>.</summary>
    bool ModelDecals { get; }
}

/// <summary>An <see cref="IDecalSettings"/> over two reads.</summary>
public sealed class DecalSettings(Func<bool> decals, Func<bool> modelDecals) : IDecalSettings
{
    /// <summary>Both on: what the engine's defaults are.</summary>
    public static DecalSettings On() => new(() => true, () => true);

    public bool Decals => decals();

    public bool ModelDecals => modelDecals();
}

/// <summary>
/// The two ConVars, watched (IShimConVarsClient): read once when the watch
/// starts and again on each change, so a decal asks no crossing.
/// </summary>
public sealed class DecalConVarsWatch(WatchedConVarValues watch) : IDecalSettings
{
    public const string DecalsName = "r_decals";
    public const string ModelDecalsName = "r_drawmodeldecals";

    public void Start() => watch.Watch([DecalsName, ModelDecalsName]);

    /// <summary>A watched ConVar changed; false for an id this watch does not hold.</summary>
    public bool OnChanged(int id, string newValue) => watch.OnChanged(id, newValue);

    public bool Decals => watch.Bool(DecalsName);

    public bool ModelDecals => watch.Bool(ModelDecalsName);
}

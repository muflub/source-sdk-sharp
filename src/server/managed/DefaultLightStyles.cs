//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The level's default light styles (plan_server_native_v2 T4d): what
/// CWorld::Precache wrote (world.cpp, by way of world_adapter.cpp) -- every
/// preset of <see cref="LightStyles"/> in its own slot, then style 63, stock's
/// "testing" style, dark. Styles 32-62 are the switchable lights' and are left
/// to them.
///
/// <para>
/// At level init, before any map entity spawns, which is where the world's
/// Precache ran: a light that sets its own style when it spawns comes after
/// this and wins, as it did.
/// </para>
/// </summary>
public sealed class DefaultLightStyles(IEngineVisibility visibility) : IGameLifecycle
{
    /// <summary>Stock's "63 testing" style, set dark.</summary>
    public const int TestingStyle = 63;

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.DefaultLightStyles;

    void IGameLifecycle.OnLevelInitPreEntity(string mapName)
    {
        for (int style = 0; style < LightStyles.PresetCount; style++)
            visibility.SetLightStyle(style, LightStyles.Default(style));

        visibility.SetLightStyle(TestingStyle, LightStyles.Off);
    }
}

//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared;

/// <summary>
/// The stock flamethrower's flight, its item schema's static attributes
/// (<c>items_game.txt</c>'s <c>weapon_baseflamethrower</c> prefab): the server's
/// damage points and the client's particle points both fly by these.
/// </summary>
public static class TFFlameDefs
{
    /// <summary><c>flame_speed</c>.</summary>
    public const float Speed = 2450f;

    /// <summary><c>flame_lifetime</c>.</summary>
    public const float LifeTime = 0.6f;

    /// <summary><c>flame_drag</c>.</summary>
    public const float Drag = 8.5f;

    /// <summary><c>flame_up_speed</c>.</summary>
    public const float Up = 50f;
}

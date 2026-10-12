//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The server's half of physics_shared.cpp, decided here (plan_physics_bones_native_zero
/// 2.6): CPhysicsHook::LevelInitPreEntity asks for the world's physics
/// (stock's <c>PhysCreateWorld</c>: the static props, then
/// <c>PhysCreateWorld_Shared</c>) and the surface sounds' precache
/// (<c>PrecachePhysicsSounds</c>). A standalone's physics init parses its
/// model's solid itself (EntityPhysicsCreation over <c>ModelSolidParse</c>).
/// </summary>
public sealed class ServerPhysicsWorld(IPhysicsWorldVPhysics vphysics, ISurfaceSoundTable surfaces,
                                       Func<string, short> precacheScript, nint defaultName) : IServerPhysicsWorld
{
    /// <summary>
    /// The handle stock's server <c>GameSound_PrecacheScript</c> answers:
    /// <c>PrecacheScriptSound</c> precaches the script's waves and answers the
    /// script's index in the sound emitter's table (-1, the invalid handle, for
    /// a name it does not hold).
    /// </summary>
    public static Func<string, short> ServerPrecache(Action<string> precacheSound, Func<string, int>? soundIndex)
    {
        ArgumentNullException.ThrowIfNull(precacheSound);

        return name =>
        {
            int index = soundIndex?.Invoke(name) ?? -1;
            precacheSound(name);
            return unchecked((short)index);
        };
    }

    public nint CreateWorld()
        => PhysicsWorldBuilder.Create(vphysics, 0, ObjectParams.Defaults(defaultName), staticProps: true);

    public int PrecacheSurfaceSounds() => SurfaceSoundPrecache.Run(surfaces, precacheScript);
}

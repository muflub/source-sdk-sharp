//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Physics;

/// <summary>
/// physics_shared.cpp's client half, decided here (plan_physics_bones_native_zero
/// 2.6). Fork game/client/physics.cpp still calls the stock names --
/// <c>PhysParseSurfaceData</c> from its DLL init, <c>PrecachePhysicsSounds</c>
/// at level init, <c>PhysCreateWorld_Shared</c> when it makes the environment,
/// and hands <c>g_SolidSetup</c> to the static prop manager -- and the native
/// forwarders that stay for that file ask this.
/// </summary>
public sealed class ClientPhysicsWorld(IPhysicsWorldVPhysics vphysics, ISurfaceSoundTable surfaces,
                                       Func<int> parseSurfaceData, Func<string, short> precacheScript,
                                       ISolidKeyTable keys) : IClientPhysicsWorld
{
    /// <summary>
    /// The handle the client's <c>GameSound_PrecacheScript</c> answers: the
    /// script's index in the sound emitter's table (stock's client precaches
    /// nothing, the server does), -1 for a name it does not hold.
    /// </summary>
    public static Func<string, short> ClientPrecache(Func<string, int>? soundIndex)
        => name => unchecked((short)(soundIndex?.Invoke(name) ?? -1));

    // The client's world makes no static props in the build: stock's client
    // asked the static prop manager after the world (CreateStaticProps).
    public nint CreateWorld()
        => PhysicsWorldBuilder.Create(vphysics, 0, ObjectParams.Defaults(keys.Solids.DefaultName), staticProps: false);

    public void CreateStaticProps() => vphysics.CreateStaticPropPhysics(vphysics.Environment());

    public int ParseSurfaceData() => parseSurfaceData();

    public int PrecacheSurfaceSounds() => SurfaceSoundPrecache.Run(surfaces, precacheScript);
}

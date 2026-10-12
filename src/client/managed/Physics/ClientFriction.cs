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

/// <summary>What a client scrape reaches: the shared FrictionSounds' two halves.</summary>
public interface IFrictionScrapes
{
    /// <summary>FrictionSounds.Friction: the MP throttle, VPhysicsFriction's scrape, then PhysFrictionEffect.</summary>
    void Scrape(EntityHandle owner, float energy, int surfaceProps, int surfacePropsHit, float now, Vector point, Vector velocity);

    /// <summary>PhysFrictionEffect alone, for an object with no owner.</summary>
    void Effect(float energy, int surfaceProps, int surfacePropsHit, Vector point, Vector velocity);
}

/// <summary>
/// The client's CCollisionEvent::Friction (game/client/physics.cpp), on
/// managed's collision-event table (plan_client_novalve C6, cluster 9; lane
/// c6-input): under 0.05 energy or with no surface nothing; the contact point
/// and the object's velocity there; an owned object's scrape -- the shared
/// FrictionSounds, the server's own port of the same stock arms -- and for an
/// object with no owner the effect alone, as stock's PhysFrictionEffect runs
/// whether or not the game data names anyone.
/// </summary>
public sealed class ClientFriction(IVPhysicsObjects objects, IFrictionScrapes scrapes, Func<float> curTime) : IClientFrictionSink
{
    public void Friction(nint physicsObject, float energy, int surfaceProps, int surfacePropsHit, nint collisionData)
    {
        if (energy < 0.05f || surfaceProps < 0)
            return;

        // Get our friction information
        Vector point = objects.ContactPoint(collisionData);
        Vector velocity = objects.VelocityAtPoint(physicsObject, point);

        EntityHandle owner = PhysicsGameData.Owner(objects.GameData(physicsObject));
        if (owner != EntityHandle.None)
            scrapes.Scrape(owner, energy, surfaceProps, surfacePropsHit, curTime(), point, velocity);
        else
            scrapes.Effect(energy, surfaceProps, surfacePropsHit, point, velocity);
    }
}

/// <summary>The shared FrictionSounds as the client's scrapes.</summary>
public sealed class FrictionSoundsScrapes(FrictionSounds sounds) : IFrictionScrapes
{
    public void Scrape(EntityHandle owner, float energy, int surfaceProps, int surfacePropsHit, float now, Vector point, Vector velocity)
        => sounds.Friction(owner, energy, surfaceProps, surfacePropsHit, now, point, velocity);

    public void Effect(float energy, int surfaceProps, int surfacePropsHit, Vector point, Vector velocity)
        => sounds.Effect(energy, surfaceProps, surfacePropsHit, point, velocity);
}

/// <summary>
/// The client's g_pEffects for a scrape: its own effects (the managed IEffects
/// the native thunk hands every client call to), dust and metal sparks.
/// </summary>
public sealed class ClientFrictionEffects(Func<IClientEffects?> effects) : IFrictionEffects
{
    public void Dust(Vector point, Vector direction) => effects()?.Dust(point, direction, 1f, 16f);

    public void MetalSparks(Vector point, Vector direction) => effects()?.MetalSparks(point, direction);
}

using System.Text;

namespace SourceSharp.Abi;

/// <summary>A surfaceprop's impact audio: what stock's PlayImpactSounds reads from physprops.</summary>
public readonly record struct SurfaceImpactAudio(string? ImpactHard, string? ImpactSoft, float HardnessFactor,
                                                 float HardThreshold, float HardVelocityThreshold);

/// <summary>What the client's impact sounds read and carry out through the engine.</summary>
public interface IClientImpactSoundWorld
{
    /// <summary><c>IPhysicsObject::IsStatic</c>.</summary>
    bool IsStatic(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetGameData</c>: the owner word (0 is none).</summary>
    nint GameData(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetPosition</c>.</summary>
    Vector Position(nint physicsObject);

    /// <summary><c>physprops-&gt;GetSurfaceData( surfaceProps )</c>'s audio, or null when there is none.</summary>
    SurfaceImpactAudio? Surface(int surfaceProps);

    /// <summary>
    /// The carry-out of one queued sound: the script's parameters, a PAS filter at
    /// <paramref name="origin"/> at their level, and an emit from the world on
    /// <paramref name="channel"/> at the script's volume times <paramref name="volume"/>.
    /// False when the script has no parameters (stock's GameSound_GetParameters failing).
    /// </summary>
    bool Emit(string soundName, Vector origin, int channel, float volume);
}

/// <summary>The client splash's reads and effects (IShimPhysicsClient).</summary>
public interface IClientSplashCarryOut
{
    /// <summary><c>IPhysicsObject::GetVelocity</c>, linear.</summary>
    Vector Velocity(nint physicsObject);

    /// <summary><c>physcollision-&gt;CollideGetExtent</c> of the object's collide at a transform.</summary>
    Vector CollideExtent(nint physicsObject, Vector origin, Vector angles, Vector direction);

    /// <summary><c>enginetrace-&gt;GetPointContents</c>.</summary>
    int PointContents(Vector point);

    /// <summary><c>FX_GetSplashLighting</c>.</summary>
    (Vector Color, float Luminosity) SplashLighting(Vector point);

    /// <summary>One fx_water.h effect: 0 FX_GunshotSplash, 1 FX_GunshotSlimeSplash, 2 FX_WaterRipple.</summary>
    void SplashEffect(int kind, Vector origin, Vector normal, float scale, Vector color, float lifetime, float alpha);
}

/// <summary>The physics-object reads the client's collision solver makes.</summary>
public interface IClientPhysicsObjectReads
{
    /// <summary><c>IPhysicsObject::GetGameFlags</c>.</summary>
    ushort GameFlags(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetContents</c>.</summary>
    uint Contents(nint physicsObject);

    /// <summary>Whether <c>IPhysicsObject::GetShadowController</c> answers one.</summary>
    bool HasShadowController(nint physicsObject);
}

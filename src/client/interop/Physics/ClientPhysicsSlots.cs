using System.Text;

namespace SourceSharp.Abi;

/// <summary>The client's level physics environment (ClientPhysicsSystem's), for the ragdolls and the world build.</summary>
public interface IClientPhysicsEngine
{
    /// <summary>The level's IPhysicsEnvironment, or 0 outside a level.</summary>
    nint Environment();
}

/// <summary>
/// IShimPhysicsClient over the client's engine (plan_client_novalve C6, cluster
/// 9; lane c6-input), and the physics objects' own vtables for the object reads.
/// </summary>
internal sealed unsafe class ClientPhysicsSlots(PhysicsClientArea physicsClient, VPhysicsTables vphysics, VPhysicsObjects objects) : IClientImpactSoundWorld, IClientPhysicsObjectReads,
                                                                IClientSplashCarryOut
{
    private const int MaxName = 128;

    private readonly VPhysicsObjects _objects = objects;

    public ushort GameFlags(nint physicsObject) => _objects.GameFlags(physicsObject);

    public uint Contents(nint physicsObject)
    {
        if (physicsObject == 0)
            return 0;

        var vtbl = vphysics.PhysicsObject;
        return vtbl->GetContents((void*)physicsObject);
    }

    public bool HasShadowController(nint physicsObject)
    {
        if (physicsObject == 0)
            return false;

        var vtbl = vphysics.PhysicsObject;
        return vtbl->GetShadowController((void*)physicsObject) != null;
    }

    public bool IsStatic(nint physicsObject) => _objects.IsStatic(physicsObject);

    public nint GameData(nint physicsObject) => _objects.GameData(physicsObject);

    public Vector Position(nint physicsObject)
    {
        if (physicsObject == 0)
            return Vector.Zero;

        Vector position;
        var vtbl = vphysics.PhysicsObject;
        vtbl->GetPosition((void*)physicsObject, &position, null);
        return position;
    }

    public SurfaceImpactAudio? Surface(int surfaceProps)
    {
        byte* hard = stackalloc byte[MaxName];
        byte* soft = stackalloc byte[MaxName];
        float* audio = stackalloc float[3];
        if (physicsClient.PhysicsClientSurfaceAudio(surfaceProps, hard, MaxName, soft, MaxName, audio) == 0)
            return null;

        return new SurfaceImpactAudio(Name(hard), Name(soft), audio[0], audio[1], audio[2]);
    }

    public bool Emit(string soundName, Vector origin, int channel, float volume)
    {
        int bytes = Encoding.UTF8.GetByteCount(soundName);
        byte* name = stackalloc byte[bytes + 1];
        Encoding.UTF8.GetBytes(soundName, new Span<byte>(name, bytes));
        name[bytes] = 0;
        float* at = stackalloc float[] { origin.X, origin.Y, origin.Z };
        return physicsClient.PhysicsClientEmitImpactSound(name, at, channel, volume) != 0;
    }

    public Vector Velocity(nint physicsObject)
    {
        if (physicsObject == 0)
            return Vector.Zero;

        Vector velocity;
        var vtbl = vphysics.PhysicsObject;
        vtbl->GetVelocity((void*)physicsObject, &velocity, null);
        return velocity;
    }

    public Vector CollideExtent(nint physicsObject, Vector origin, Vector angles, Vector direction)
    {
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        float* a = stackalloc float[] { angles.X, angles.Y, angles.Z };
        float* d = stackalloc float[] { direction.X, direction.Y, direction.Z };
        float* r = stackalloc float[3];
        physicsClient.PhysicsClientCollideExtent((void*)physicsObject, o, a, d, r);
        return new Vector(r[0], r[1], r[2]);
    }

    public int PointContents(Vector point)
    {
        float* p = stackalloc float[] { point.X, point.Y, point.Z };
        return physicsClient.PhysicsClientPointContents(p);
    }

    public (Vector Color, float Luminosity) SplashLighting(Vector point)
    {
        float* p = stackalloc float[] { point.X, point.Y, point.Z };
        float* r = stackalloc float[4];
        physicsClient.PhysicsClientSplashLighting(p, r);
        return (new Vector(r[0], r[1], r[2]), r[3]);
    }

    public void SplashEffect(int kind, Vector origin, Vector normal, float scale, Vector color, float lifetime, float alpha)
    {
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        float* n = stackalloc float[] { normal.X, normal.Y, normal.Z };
        float* c = stackalloc float[] { color.X, color.Y, color.Z };
        physicsClient.PhysicsClientSplashEffect(kind, o, n, scale, c, lifetime, alpha);
    }

    private static string? Name(byte* text)
    {
        int length = 0;
        while (length < MaxName && text[length] != 0)
            length++;
        return length == 0 ? null : Encoding.UTF8.GetString(text, length);
    }
}

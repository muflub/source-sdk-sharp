using System.Globalization;

using SourceSharp.Abi;

namespace SourceSharp.Client.Physics;

/// <summary>
/// The client collision solver's facts over the engine. Every owner a client
/// physics object names is a ragdoll body (RagdollLibrary_Create is the only
/// client maker of owned objects), and stock's BecomeRagdollOnClient puts a
/// client ragdoll in COLLISION_GROUP_DEBRIS, so that is the group.
/// cl_ragdoll_collide is read when the solver reaches it, which for two
/// debris bodies is never.
/// </summary>
public sealed class ClientCollisionFacts(IClientPhysicsObjectReads objects, IEngineConsole console) : IClientCollisionFacts
{
    public ushort GameFlags(nint physicsObject) => objects.GameFlags(physicsObject);

    public uint Contents(nint physicsObject) => objects.Contents(physicsObject);

    public bool HasShadowController(nint physicsObject) => objects.HasShadowController(physicsObject);

    public EngineCollisionGroup GroupOf(EntityHandle owner) => EngineCollisionGroup.Debris;

    public bool RagdollCollide
        => int.TryParse(console.FindConVarValue("cl_ragdoll_collide"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
           && v != 0;
}

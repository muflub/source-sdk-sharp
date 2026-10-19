using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;

namespace SourceSharp.Client.Physics;

/// <summary>
/// The client collision solver's facts over the engine. An owner a client
/// physics object names is a ragdoll body or a clientside physics prop: a
/// prop answers its own group (<see cref="IClientPhysicsPropOwners"/>), and
/// any other owner is a ragdoll, which stock's BecomeRagdollOnClient puts in
/// COLLISION_GROUP_DEBRIS.
/// cl_ragdoll_collide is read when the solver reaches it, which for two
/// debris bodies is never.
/// </summary>
public sealed class ClientCollisionFacts(IClientPhysicsObjectReads objects, IEngineConsole console,
                                         IClientPhysicsPropOwners? props = null) : IClientCollisionFacts
{
    public ushort GameFlags(nint physicsObject) => objects.GameFlags(physicsObject);

    public uint Contents(nint physicsObject) => objects.Contents(physicsObject);

    public bool HasShadowController(nint physicsObject) => objects.HasShadowController(physicsObject);

    public EngineCollisionGroup GroupOf(EntityHandle owner)
        => props is not null && props.TryGroup(owner, out EngineCollisionGroup group) ? group : EngineCollisionGroup.Debris;

    public bool RagdollCollide
        => int.TryParse(console.FindConVarValue("cl_ragdoll_collide"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
           && v != 0;
}

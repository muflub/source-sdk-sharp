namespace SourceSharp.Abi;

//=============================================================================
// THE SERVER'S SKELETON, AS THE ENGINE BINDINGS ASK IT (plan
// physics_bones_native_zero step 1.2).
//
// Every server bone question managed code asks -- where is this attachment,
// how many bones are cached, which sequence plays, how big is the model worn --
// used to cross into CStandaloneEntity, which posed the skeleton in C++ and
// answered. The skeleton is managed now (SourceSharp.Shared.Bones'
// ServerBoneDriver, held per entity by SourceSharp.Server's ServerSkeletons),
// so those questions are answered on this side of the boundary and cost no
// crossing beyond the one engine-block read that says where the entity is.
//
// The engine bindings (EngineEntityRender, EngineEntities) live in this
// assembly and the skeleton in the game assembly above it, so the bindings
// reach it through this interface, by way of a ROUTE the server's composition
// registers before its engine areas are built and fills once the skeleton
// exists: the skeleton itself reads the engine through those same areas, so a
// constructor parameter would be a cycle. The client registers its own route
// (SourceSharp.Client's ClientSkeletons, the same step's client half), with
// the asks only the client poses from on IClientStandaloneSkeletons.
//=============================================================================

/// <summary>
/// Where a standalone entity's engine block is, for the skeleton (either side's): the block
/// says which model the entity wears, where it stands and which sequence its
/// skeleton plays. Empty for a handle that names no standalone entity.
/// </summary>
public interface IStandaloneBlocks
{
    Span<byte> Block(EntityHandle entity);
}

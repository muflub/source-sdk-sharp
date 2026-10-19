using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>What the physics frame asks native: the environment, and the entities' transforms written back.</summary>
public interface IPhysicsFrameNative
{
    /// <summary>The level's IPhysicsEnvironment, or 0 between levels.</summary>
    nint Environment();

    /// <summary>
    /// Each active object's owner told the solver moved it (the engine-block
    /// transform written back, native's PhysicsOwner_t::VPhysicsUpdate), and the
    /// entities each object is the OWN object of written into
    /// <paramref name="owners"/>, in order. Each such entity also holds the
    /// velocity <paramref name="velocities"/> gives its object (three floats per
    /// object), the one its riders' ground reads. The count written.
    /// </summary>
    int UpdateOwners(ReadOnlySpan<nint> objects, ReadOnlySpan<float> velocities, Span<uint> owners);
}

namespace SourceSharp.Abi;

/// <summary>
/// The one thing the world physics build asks of the server's static prop
/// manager: make every static prop's physics in the level's environment
/// (<c>IStaticPropMgrServer::CreateVPhysicsRepresentations</c>). Shared, so
/// <see cref="PhysicsWorldVPhysics"/> can ask it; implemented by
/// Server.Interop's <c>StaticPropMgrServer</c>, over the engine's object and
/// libss_fwd_server's table (plan_engine_forwarders.md, one library per side).
/// </summary>
public interface IStaticPropPhysics
{
    /// <summary>
    /// Builds the static props' physics in <paramref name="environment"/>,
    /// their solids' keys answered by <paramref name="keyHandler"/> (an
    /// IVPhysicsKeyHandler).
    /// </summary>
    void CreateVPhysicsRepresentations(nint environment, nint keyHandler);
}

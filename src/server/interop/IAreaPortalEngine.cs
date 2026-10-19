using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// What the area portal records ask of IVEngineServer (plan_server_native_v2
/// T4d): the three portal calls shell_areaportal.cpp and ClientSetupVisibility
/// made natively. <c>EngineServer</c> answers them through its forwarders.
/// </summary>
public interface IAreaPortalEngine
{
    /// <summary><c>SetAreaPortalState( portalNumber, isOpen )</c>.</summary>
    void SetAreaPortalState(int portalNumber, bool open);

    /// <summary><c>SetAreaPortalStates( portalNumbers, isOpen, nPortals )</c>: the two spans' common length.</summary>
    void SetAreaPortalStates(ReadOnlySpan<int> portalNumbers, ReadOnlySpan<int> isOpen);

    /// <summary>
    /// <c>GetAreaPortalPlane( viewOrigin, portalKey, &amp;plane )</c>: the
    /// portal's plane as the viewer sees it (a VPlane, normal then distance),
    /// or false when the engine has none.
    /// </summary>
    bool GetAreaPortalPlane(Vector viewOrigin, int portalKey, out Vector normal, out float distance);
}

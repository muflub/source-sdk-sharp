using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>The local player as native reads it for the weather.</summary>
public readonly record struct PrecipitationPlayerRead(Vector Origin, Vector Eye, float Yaw, Vector Center, float Fov, int DefaultFov);

/// <summary>
/// IShimPrecipitationClient: the engine reads the client weather makes
/// (shell_precipitation_client.cpp). Each is one engine call.
/// </summary>
public interface IClientPrecipitationEngine
{
    /// <summary><c>UTIL_TraceLine</c> through CTraceFilterSimple; the local player ignored when asked.</summary>
    bool TraceLine(in Vector start, in Vector end, uint mask, bool ignorePlayer, out float fraction, out Vector endPos, out ushort surfaceFlags);

    /// <summary><c>UTIL_TraceModel</c> against the owner's brush.</summary>
    bool TraceVolume(EntityHandle owner, in Vector start, in Vector end, in Vector hullMin, in Vector hullMax,
                     out bool hit, out float fraction, out bool startSolid, out Vector endPos);

    /// <summary><c>enginetrace-&gt;GetPointContents</c>.</summary>
    int PointContents(in Vector position);

    /// <summary><c>IsRenderableInPVS</c> for the owner.</summary>
    bool InPvs(EntityHandle owner);

    /// <summary>The local player, or null when there is none.</summary>
    PrecipitationPlayerRead? Player();

    /// <summary>The world entity's bounds.</summary>
    (Vector Mins, Vector Maxs) WorldBounds();

    /// <summary><c>ScreenWidth()</c>.</summary>
    int ScreenWidth();
}

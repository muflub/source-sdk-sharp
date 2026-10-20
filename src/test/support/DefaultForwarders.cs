namespace SourceSharp.Abi;

/// <summary>
/// The engine forwarders a side with no engine behind it answers with, for a
/// fact (TestSides): an empty world and no surface properties. The forwarder
/// counterpart of the default areas (ShimAreaDefaults.g.cs), under the same
/// ruling: managed logic never asks whether an engine is attached, so a test
/// without one is given interfaces that answer good defaults.
/// </summary>
internal static class DefaultForwarders
{
    /// <summary>Holds the defaults on <paramref name="side"/>'s bound set; a root's attach replaces them.</summary>
    public static void HoldOn(EngineSide side)
    {
        side.Bound.Trace.Hold(new EmptyWorldTrace());
        side.Bound.SurfaceProps.Hold(new NoSurfaceProps());
    }

    /// <summary>A world with nothing in it: every point empty, every ray unblocked.</summary>
    private sealed class EmptyWorldTrace : IFwdEngineTrace
    {
        public int PointContents(Vector point) => 0;

        public EngineGameTrace TraceRay(in EngineRay ray, uint mask, nint filter)
            => new() { StartPos = ray.Start, EndPos = ray.Start + ray.Delta, Fraction = 1f };
    }

    /// <summary>No surface table: no names, no indices, the default surface's movement answers.</summary>
    private sealed class NoSurfaceProps : IFwdPhysicsSurfaceProps
    {
        public int ParseSurfaceData(string fileName, string text) => 0;

        public int SurfacePropCount() => 0;

        public string? PropName(int index) => null;

        public int SurfaceIndex(string name) => -1;

        public int GameMaterial(int surfaceProps) => 0;

        public MoveSurface Surface(int surfaceProps) => default;

        public string? ScrapeSound(int surfaceProps, bool smooth) => null;
    }
}

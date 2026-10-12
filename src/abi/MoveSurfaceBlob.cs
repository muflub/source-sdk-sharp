using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>One surfaceprop as the loop reads it — <c>SourceSharpMoveSurface</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MoveSurfaceBlob
{
    public int Valid;
    public float Friction;
    public int Material;
    public int Climbable;
    public float MaxSpeedFactor;
    public float JumpFactor;
    public float Density;
    public float RoughnessFactor;
    public float RoughThreshold;
}

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// A user command, flattened to blittable fields. Mirrors SourceSharpUserCmd.
///
/// Copied in and out around the call rather than shared, which is fine here:
/// CreateMove runs once per command, not per entity per prop. Entity state uses
/// the shared-memory design instead (plan.md Part 4).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct UserCmd
{
    public uint StructSize;

    private float _pitch, _yaw, _roll;

    public float ForwardMove;
    public float SideMove;
    public float UpMove;
    public int   Buttons;
    public int   Impulse;
    public int   WeaponSelect;
    public int   CommandNumber;
    public int   TickCount;
    public int   MouseDx;
    public int   MouseDy;

    /// <summary>View angles in degrees (pitch, yaw, roll).</summary>
    public float Pitch { get => _pitch; set => _pitch = value; }
    public float Yaw   { get => _yaw;   set => _yaw   = value; }
    public float Roll  { get => _roll;  set => _roll  = value; }

    public bool IsPressed(InputButtons button) => (Buttons & (int)button) != 0;
}

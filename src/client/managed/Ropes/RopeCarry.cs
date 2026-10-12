using SourceSharp.Abi;

namespace SourceSharp.Client.Ropes;

/// <summary>A vector through the reload writer: three floats, bit for bit.</summary>
internal static class RopeCarry
{
    public static void Write(ReloadWriter w, in Vector v)
    {
        w.Write(v.X);
        w.Write(v.Y);
        w.Write(v.Z);
    }

    public static Vector Read(ReloadReader r) => new(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
}

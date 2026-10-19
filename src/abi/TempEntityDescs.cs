using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

// THE BLITTABLE MIRRORS of what crosses: laid out to match sourcesharp.h, where a
// field inserted on one side only shifts every field after it.
//
// plan_server_native_v2 T4b: the server's tables, planned in C# and stored by
// native. One block per table (BitBlock: an int32 bit count, then the payload):
// a TeTableHeader, the rows, and the names at the payload offsets they give.
// Laid out to match SourceSharpTeTableHeader_t, SourceSharpSendPropRow_t and
// SourceSharpTeBuiltTable_t in sourcesharp.h (TempEntTableAbiParityTests).

[StructLayout(LayoutKind.Sequential)]
public struct TeTableHeader
{
    public int  Props;
    public int  RegisterClass;
    public int  ClassName;
    public int  TableName;
    public uint ShapeHash;
}

[StructLayout(LayoutKind.Sequential)]
public struct SendPropRowDesc
{
    public int   Type;
    public int   Offset;
    public int   Flags;
    public int   Bits;
    public float LowValue;
    public float HighValue;
    public float HighLowMul;
    public int   Elements;
    public int   ElementStride;
    public int   Proxy;
    public int   DataTable;
    public int   Name;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct TeBuiltTable
{
    public int   Existed;
    public uint  ShapeHash;
    public void* SendTable;
    public int*  ClassId;
}

// The client's tables, planned in C# and stored by native the same way: one
// block per table, a TeTableHeader, RecvPropRowDesc rows and the names. Laid
// out to match SourceSharpRecvPropRow_t and SourceSharpTeRecvBuilt_t in
// sourcesharp.h (ClientTempEntEngineTests).

[StructLayout(LayoutKind.Sequential)]
public struct RecvPropRowDesc
{
    public int Type;
    public int Offset;
    public int Flags;
    public int Elements;
    public int ElementStride;
    public int Proxy;
    public int DataTable;
    public int Name;
}

[StructLayout(LayoutKind.Sequential)]
public struct TeRecvBuilt
{
    public int  Existed;
    public uint ShapeHash;
}

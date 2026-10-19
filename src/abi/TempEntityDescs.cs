using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

//=============================================================================//
// THE BLITTABLE MIRRORS. These are what actually cross the boundary; the types
// above are what C# reads. Laid out to match SourceSharpTeField_t
// and SourceSharpTeClass_t in sourcesharp.h — a field
// inserted on one side only shifts every field after it, and the symptom is a
// prop built with somebody else's width.
//=============================================================================//

[StructLayout(LayoutKind.Sequential)]
public unsafe struct TeFieldDesc
{
    public byte* Name;
    public int   Kind;
    public int   Offset;
    public int   SizeBytes;
    public int   Bits;
    public int   BitsFrom;
    public int   Flags;
    public float Low;
    public float High;
    public byte* SubTable;
    public int   Elems;
    public int   ElemStride;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct TeClassDesc
{
    public byte*        ClassName;
    public byte*        TableName;
    public byte*        BaseTable;
    public int          BlobBytes;
    public int          RegisterClass;
    public int          FieldCount;
    public TeFieldDesc* Fields;
}

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

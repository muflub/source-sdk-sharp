using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

//=============================================================================//
// THE BLITTABLE MIRRORS. These are what actually cross the boundary; the types
// above are what C# reads. Laid out to match SourceSharpTeField_t,
// SourceSharpTeClass_t and SourceSharpTeFilter_t in sourcesharp.h — a field
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

[StructLayout(LayoutKind.Sequential)]
public unsafe struct TeFilterDesc
{
    public int   Kind;
    public fixed float Origin[3];
    public int   PlayerIndex;
    public float Attenuation;
    public int   IgnorePredictionCull;
}

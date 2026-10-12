using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// Writes a sprite model's material into a block's payload: the bytes
/// written, -1 when the sprite has no material, or -2 when they do not fit.
/// </summary>
public delegate int SpriteMaterialWriter(string spriteName, Span<byte> payload);

/// <summary>
/// Encodes pixels (kind: 0 RGB888 TGA, 1 RGBA8888 TGA, 2 RGB888 VTF; padded to
/// powers of two first when asked) into <c>file</c>: the bytes written, or the
/// bytes needed when <c>file</c> is empty; -1 when the kind or size is refused,
/// -2 when <c>file</c> is too small.
/// </summary>
public delegate int ImageEncoder(int kind, int width, int height, bool padPowerOfTwo, ReadOnlySpan<byte> pixels, Span<byte> file);

/// <summary>
/// <see cref="IManagedMaterialFilesClient"/>, managed-built (plan_client_novalve
/// C9, KeyValues K4): CEngineSprite::Init (shell_sprite_leaf.cpp) asks a sprite
/// model's material here, and rebuilds one procedural material a render mode
/// from the block. What goes in the block is the game's (SpriteMaterialFiles,
/// KeyValuesBlock), handed in as a function.
/// </summary>
public sealed unsafe class MaterialFilesClientBinding(Crossings crossings, ILog log, SpriteMaterialWriter spriteMaterial, ImageEncoder encodeImage)
    : IManagedMaterialFilesClient
{
    // The material's name and the .vmt's first root in one BitBlock.
    // Answers the payload's bytes, -1 when the sprite has no material (said
    // by the reader), or -2 when the block is too small.
    public int SpriteMaterial(byte* a0, void* a1, int a2)
    {
        crossings.Count(CrossingOp.ClientMaterialFilesSpriteMaterial);
        try
        {
            if (a1 == null || a2 < BitBlock.HeaderBytes) return -2;
            string name = a0 == null ? "" : System.Text.Encoding.Latin1.GetString(new ReadOnlySpan<byte>(a0, StrLen(a0)));
            var block = new Span<byte>(a1, a2);
            int bytes = spriteMaterial(name, BitBlock.Payload(block));
            if (bytes < 0) return bytes == -1 ? -1 : -2;
            BitBlock.SetBits(block, bytes * 8);
            return bytes;
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedMaterialFilesClient.SpriteMaterial", e); return -1; }
    }

    // A screenshot's pixels (one BitBlock: its bit count, then the bytes)
    // encoded as the file native writes. Answers the bytes written, the bytes
    // needed when pOut is null, -1 for a refused kind or size, -2 for a
    // short output or a malformed block.
    public int EncodeImage(int a0, int a1, int a2, int a3, void* a4, int a5, void* a6, int a7)
    {
        crossings.Count(CrossingOp.ClientMaterialFilesEncodeImage);
        try
        {
            if (a4 == null || a5 < BitBlock.HeaderBytes || a7 < 0) return -2;
            var block = new ReadOnlySpan<byte>(a4, a5);
            int bits = BitBlock.Bits(block);
            if (bits < 0 || bits % 8 != 0 || bits / 8 > a5 - BitBlock.HeaderBytes) return -2;
            ReadOnlySpan<byte> pixels = block.Slice(BitBlock.HeaderBytes, bits / 8);
            Span<byte> file = a6 == null ? Span<byte>.Empty : new Span<byte>(a6, a7);
            return encodeImage(a0, a1, a2, a3 != 0, pixels, file);
        }
        catch (Exception e) { ManagedVtable.Fail(log, "IManagedMaterialFilesClient.EncodeImage", e); return -1; }
    }

    private static int StrLen(byte* s)
    {
        int n = 0;
        while (s[n] != 0) n++;
        return n;
    }
}

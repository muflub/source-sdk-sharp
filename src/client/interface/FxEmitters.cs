using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The engine's answers a muzzle flash reads (IShimFxClient): attachments,
/// the entity index behind a handle, the elight, client.so's <c>rand()</c>,
/// and whether the impact effects' muzzle materials were cached.
/// </summary>
public interface IFxFlashReads
{

    /// <summary>
    /// <c>FX_GetAttachmentTransform( h, att, &amp;origin, &amp;angles )</c>
    /// (fx.cpp, C# since lane c5-particles): nothing for an invalid handle or
    /// an attachment below 1, else the renderable's attachment (false, and
    /// zeros, when the handle has no renderable).
    /// </summary>
    bool AttachmentTransform(uint handle, int attachment, out Vector origin, out QAngle angles);

    /// <summary>
    /// <c>FX_GetAttachmentTransform( h, att, matrix )</c>: the matrix's
    /// translation, which <c>AngleMatrix</c> copies from the origin unchanged.
    /// </summary>
    bool AttachmentMatrixOrigin(uint handle, int attachment, out Vector origin);

    /// <summary>The handle's renderable and its <c>GetAttachment</c>; false when it has no renderable.</summary>
    bool RenderableAttachment(uint handle, int attachment, out Vector origin, out QAngle angles);

    /// <summary>The entity index behind a handle; -1 when none.</summary>
    int EntityIndex(uint handle);

    /// <summary><c>effects-&gt;CL_AllocElight( key )</c>, filled.</summary>
    void Elight(int key, in Vector origin, byte r, byte g, byte b, sbyte exponent, float radius, float decay, float die);

    /// <summary><c>Vector::Random( lo, hi )</c>: the C runtime's rand(), not the engine stream.</summary>
    Vector VectorRandom(float lo, float hi);

    /// <summary>Whether the impact effects' muzzle materials were cached this level: bit 0 the SMG's, bit 1 the Combine's.</summary>
    int MaterialsCached();
}

/// <summary>The tempent pool's models by name (the shell casings, the level's list).</summary>
public interface IFxTempEntModels
{
    /// <summary>A tempent of the named model in a slot the managed pool reserved; false when none was made.</summary>
    bool CreateNamed(int slot, in Vector origin, string model, int priority);

    /// <summary><c>engine-&gt;LoadModel( name )</c>.</summary>
    void LoadModel(string model);

    /// <summary><c>modelinfo-&gt;GetModelFrameCount</c> of the loaded model; -1 for none.</summary>
    int FrameCount(string model);
}

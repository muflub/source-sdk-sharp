namespace SourceSharp.Abi;

//=============================================================================
// A MODEL'S STUDIO DATA, COPIED OUT OF THE ENGINE'S MODEL CACHE (plan
// physics_bones_native_zero step 1.2, the studio header handoff).
//
// The managed bone port reads a model through SourceSharp.Shared.Bones'
// StudioHdr, which is a view over ONE byte image. The engine holds the model as
// a studiohdr_t in mdlcache memory, its $includemodel parts as further
// studiohdr_ts, and its demand-loaded animation blocks (.ani) as separate
// buffers. This is the read of those three, through the engine's own
// IVModelInfo -- the interface the engine hands game DLLs for exactly this, and
// one managed code already binds (EngineModelInfo) -- so no slot is added for
// it:
//
//   GetModel + GetStudiomodel   the model's studiohdr_t, `length` bytes long
//   FindModel                   an include, by the name its includer records
//   GetAnimBlock                one .ani block, (dataend - datastart) bytes
//
// COPIED, ONCE PER MODEL PER LEVEL, rather than read in place. The copy is made
// on the game thread, which is the only thread that flushes the model cache
// (OnInvalidateMdlCache arrives on it), so nothing can free the studio data
// between the pointer and the copy; after it, the image is managed memory and
// no MDLCACHE_CRITICAL_SECTION is owed. The caller drops its images at level
// shutdown and on a model-cache flush, which is when the engine's would go.
//=============================================================================

/// <summary>A side's model cache, read as bytes.</summary>
public interface IStudioMemory
{
    /// <summary>
    /// The precached model's studio header, the whole of it
    /// (<c>studiohdr_t::length</c> bytes), or null for an index that names no
    /// studio model.
    /// </summary>
    byte[]? ModelImage(int modelIndex);

    /// <summary>
    /// One of the model's animation blocks, exactly <c>dataend - datastart</c>
    /// bytes, or null when the engine has none for it (block 0, an index out
    /// of range, a block the model cache could not load).
    /// </summary>
    byte[]? ModelAnimBlock(int modelIndex, int block);

    /// <summary>
    /// An <c>$includemodel</c> of the model, by the name the includer's
    /// <c>mstudiomodelgroup_t</c> records, or null when the engine cannot find
    /// it.
    /// </summary>
    byte[]? IncludeImage(int modelIndex, string includeName);

    /// <summary>One animation block of an include (see <see cref="ModelAnimBlock"/>).</summary>
    byte[]? IncludeAnimBlock(int modelIndex, string includeName, int block);
}

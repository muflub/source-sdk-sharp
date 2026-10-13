using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

//=============================================================================
// IVModelInfo, WHOSE VIRTUAL DESTRUCTOR RESERVES THE FIRST TWO SLOTS.
//
// This is the interface where an index being wrong is worst. `ivmodelinfo.h`
// declares a virtual destructor, so under the Itanium ABI slots 0 and 1 are the
// complete-object destructor (D1) and the deleting destructor (D0), and the
// first real method is at 2. An index one too low near the top of this table
// does not return a wrong number -- it frees the engine's singleton.
//
// The generated struct names both of those slots "destructor 1" and
// "destructor 2", types them as `nint`, and nothing here reaches them.
//
//-----------------------------------------------------------------------------
// BOTH SIDES SERVE IT UNDER THEIR OWN NAME. The engine registers
// VModelInfoServer004 and VModelInfoClient006 for two objects of the same class
// -- ivmodelinfo.h declares one IVModelInfo -- so the table is the same length
// either way and native answers both spellings with whichever pointer its own
// DLL was handed.
//=============================================================================

/// <summary>
/// What the engine knows about a precached model, as game code should see it —
/// no pointer arithmetic, no slot number.
/// </summary>
public interface IVtableModelInfo
{
    /// <summary>The engine's <c>model_t *</c> for a precached index, or zero.</summary>
    nint Model(int modelIndex);

    /// <summary>The precached index of a model path, or -1.</summary>
    int ModelIndex(string name);

    /// <summary>The path a <c>model_t *</c> was loaded from, or null.</summary>
    string? ModelName(nint model);

    /// <summary>What kind of model it is.</summary>
    ModelType ModelTypeOf(nint model);

    //-------------------------------------------------------------------------
    // TWO BY-INDEX MEMBERS, plan_native_api.md Phase 3.
    //
    // The flat entries these replace took a MODEL INDEX and the interface takes
    // a MODEL POINTER, so the resolution -- GetModel, then the question -- is
    // here rather than at each call site. That is the shell body's own shape,
    // moved: it is where the "index <= 0 is not a model" and "GetModel answered
    // null" cases were decided, and both answers are -1 rather than 0, which is
    // the distinction the whole pair exists to keep.
    //-------------------------------------------------------------------------

    /// <summary>
    /// How many frames a precached model has, or −1 for an index that names no
    /// model.
    ///
    /// <para>
    /// <c>CSprite</c> reads this and keeps it minus one as <c>m_flMaxFrame</c>:
    /// what the animation wraps at, and what <c>SF_SPRITE_ONCE</c> compares
    /// against to switch a one-shot sprite off after a single cycle. It cannot
    /// be worked out managed-side at all — the count lives on the loaded model.
    /// </para>
    /// </summary>
    int FrameCountAt(int modelIndex);

    /// <summary>
    /// What kind of model an index names, or <see cref="ModelType.Bad"/> for an
    /// index that names none.
    ///
    /// <para>
    /// <b>−1 IS NOT AVAILABLE HERE AND THAT IS THE HAZARD.</b> <c>mod_bad</c> is
    /// ZERO, so "no such model" and "that model is broken" are the same answer
    /// at the type. The flat slot said the same thing beside itself in
    /// sourcesharp.h; the caller that cares asks
    /// <see cref="FrameCountAt"/> as well, which can tell them apart.
    /// </para>
    /// </summary>
    ModelType TypeAt(int modelIndex);

    /// <summary>
    /// The model's $keyvalues text (IVModelInfo::GetModelKeyValueText), null
    /// for no model. plan_server_novalve.md P2.4: read in C# so the text is
    /// parsed by KeyValuesText rather than by tier1's KeyValues on the server.
    /// </summary>
    string? KeyValueText(nint model);

    /// <summary>
    /// The keyvalue text of a model's collision data (the .phy's, reached as
    /// <c>IVModelInfo::GetVCollide( modelIndex )-&gt;pKeyValues</c>), or null when
    /// the model has no collision model. Its "break" blocks are the pieces a
    /// breakable prop turns into; its "solid" and "ragdollconstraint" blocks are
    /// VPhysics'. Not the model's $keyvalues (<see cref="KeyValueText"/>): the
    /// two are different texts, and break pieces live only in this one.
    /// </summary>
    string? CollisionKeyValueText(int modelIndex);

    /// <summary>
    /// A precached model's studio header, copied whole
    /// (<c>studiohdr_t::length</c> bytes), or -- with
    /// <paramref name="includeName"/> -- the header of one of its
    /// <c>$includemodel</c>s, found by name through <c>FindModel</c>. Null for
    /// an index that names no studio model, an include the engine cannot find,
    /// and a header that is not one (bad id, a length out of range). See
    /// <see cref="IStudioMemory"/>.
    /// </summary>
    byte[]? StudioImage(int modelIndex, string? includeName);

    /// <summary>
    /// One animation block of the model (or of its include), exactly
    /// <c>dataend - datastart</c> bytes as the header's
    /// <c>mstudioanimblock_t</c> table declares, copied from
    /// <c>GetAnimBlock</c>; null when the engine answers none.
    /// </summary>
    byte[]? StudioAnimBlock(int modelIndex, string? includeName, int block);
}

/// <summary>
/// <c>vcollide_t</c> (public/vcollide.h), read in place: the engine owns it and
/// server.so reads it by layout. Two unsigned shorts (the solid count and packed
/// bit share the first), padding, then two pointers -- so the keyvalue text is
/// at offset 16 on every 64-bit build. engine_vcollide_layout_oracle_test holds
/// that offset to the pristine header; VCollideLayoutTests holds this struct to
/// the same number.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VCollideLayout
{
    public ushort SolidCountAndPacked;
    public ushort DescSize;
    public nint Solids;
    public byte* KeyValues;

    /// <summary>The offset the native oracle pins for <c>pKeyValues</c>.</summary>
    public const int KeyValuesOffset = 16;
}

/// <summary>
/// <see cref="IVtableModelInfo"/> over the engine's own object, called through
/// its vtable.
/// </summary>
public sealed unsafe class EngineModelInfo : IVtableModelInfo
{
    /// <summary>The server's factory name for it.</summary>
    public const string ServerInterfaceVersion = "VModelInfoServer004";

    /// <summary>The client's, for the same class and the same table length.</summary>
    public const string ClientInterfaceVersion = "VModelInfoClient006";

    private readonly void* _self;

    // THE FORWARDERS, NOT THE VTABLE (plan_engine_forwarders.md). Each slot is
    // a native function that calls one IVModelInfo method through _self's own
    // vtable, compiled for the ABI the engine was built with, so nothing here
    // knows a slot number, the destructor pair or MSVC's overload order.
    private readonly ModelInfoFwd* _fwd;

    private EngineModelInfo(void* self, void** forwarders)
    {
        _self = self;
        _fwd = (ModelInfoFwd*)forwarders;
    }

    /// <summary>
    /// The engine's model info, or null with the reason.
    ///
    /// <para>
    /// Through <paramref name="engine"/>'s own side's interface factory
    /// (plan_abi_hotreload D8), never the process-wide one that followed the
    /// thread's active side.
    /// </para>
    /// </summary>
    /// <param name="engine">The asking module's engine.</param>
    /// <param name="why">One line saying what failed. Empty on success.</param>
    /// <param name="interfaceVersion">
    /// Which spelling to ask for. Native serves both on both sides, so the
    /// default is the server's and a client caller may pass its own.
    /// </param>
    public static IVtableModelInfo? Acquire(
        ModuleEngine engine, out string why, string interfaceVersion = ServerInterfaceVersion)
        => Acquire((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, out why, interfaceVersion);

    /// <summary>
    /// The same, through one side's own interface factory (plan_abi_hotreload
    /// D8): what <see cref="EngineBound"/> holds per side.
    /// </summary>
    internal static IVtableModelInfo? Acquire(
        NativeInterfaceFactory factory, out string why, string interfaceVersion)
        => Forwarders.TryAcquire(
               factory, SourceSharpFactoryType.AppSystem,
               interfaceVersion, out void* self, out ModelInfoFwd* forwarders, out why)
            ? new EngineModelInfo(self, (void**)forwarders)
            : null;

    /// <inheritdoc/>
    public nint Model(int modelIndex) => (nint)_fwd->GetModel(_self, modelIndex);

    /// <inheritdoc/>
    public int ModelIndex(string name)
    {
        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
            return _fwd->GetModelIndex(_self, p);
    }

    /// <inheritdoc/>
    public string? ModelName(nint model)
        => Vtable.TextOf(_fwd->GetModelName(_self, (void*)model), Vtable.MaxModelPath);

    /// <inheritdoc/>
    public ModelType ModelTypeOf(nint model) => Kind(_fwd->GetModelType(_self, (void*)model));

    // Studio $keyvalues are a few hundred bytes; the bound only stops a missing
    // terminator from walking off the end.
    private const int MaxKeyValueText = 1 << 20;

    public string? KeyValueText(nint model)
        => model == 0 ? null : Vtable.TextOf(_fwd->GetModelKeyValueText(_self, (void*)model), MaxKeyValueText);

    // A .phy's keyvalue text is a few kilobytes for a ragdoll with every
    // constraint; the bound only stops a missing terminator from walking off.
    private const int MaxCollisionKeyValueText = 1 << 20;

    /// <inheritdoc/>
    public string? CollisionKeyValueText(int modelIndex)
    {
        if (modelIndex < 0)
            return null;

        var collide = (VCollideLayout*)_fwd->GetVCollide_2(_self, modelIndex);

        return collide == null || collide->KeyValues == null
            ? null
            : Vtable.TextOf(collide->KeyValues, MaxCollisionKeyValueText);
    }

    /// <inheritdoc/>
    public int FrameCountAt(int modelIndex)
    {
        if (modelIndex <= 0)
            return -1;

        void* model = _fwd->GetModel(_self, modelIndex);

        return model == null ? -1 : _fwd->GetModelFrameCount(_self, model);
    }

    /// <inheritdoc/>
    public ModelType TypeAt(int modelIndex)
    {
        if (modelIndex <= 0)
            return ModelType.Bad;

        void* model = _fwd->GetModel(_self, modelIndex);

        return model == null ? ModelType.Bad : Kind(_fwd->GetModelType(_self, model));
    }

    //-------------------------------------------------------------------------
    // THE STUDIO DATA, COPIED (IStudioMemory). Layout reads of studiohdr_t at
    // the offsets SourceSharp.Shared.Bones' StudioHdr pins (its docs/bones-offsets.txt
    // dump): id @0, length @76, numanimblocks @352, animblockindex @356, and
    // mstudioanimblock_t { int datastart; int dataend; } eight bytes a block.
    // Nothing here decides anything about the model: it hands over bytes.
    //-------------------------------------------------------------------------

    private const int StudioId = 0x54534449;          // 'IDST'
    private const int StudioHeaderBytes = 408;         // the smallest header StudioHdr accepts
    private const int MaxStudioBytes = 64 << 20;        // a bound, not a budget: stops a corrupt length
    private const int MaxAnimBlockBytes = 64 << 20;

    private byte* StudioHeader(int modelIndex, string? includeName)
    {
        if (modelIndex <= 0)
            return null;

        void* model = _fwd->GetModel(_self, modelIndex);

        if (model == null || Kind(_fwd->GetModelType(_self, model)) != ModelType.Studio)
            return null;

        byte* header = (byte*)_fwd->GetStudiomodel(_self, model);

        if (header == null || includeName is null)
            return header;

        // CModelInfo::FindModel( pStudioHdr, &cache, name ): the engine resolves
        // the name itself (the model loader, by path) and fills `cache`, which
        // this asker does not keep -- the image is copied and the next level
        // asks again.
        void* cache = null;
        byte[] name = Vtable.Utf8(includeName);

        fixed (byte* pName = name)
            return (byte*)_fwd->FindModel(_self, header, &cache, pName);
    }

    /// <inheritdoc/>
    public byte[]? StudioImage(int modelIndex, string? includeName)
    {
        byte* header = StudioHeader(modelIndex, includeName);

        if (header == null || *(int*)header != StudioId)
            return null;

        int length = *(int*)(header + 76);

        if (length < StudioHeaderBytes || length > MaxStudioBytes)
            return null;

        return new ReadOnlySpan<byte>(header, length).ToArray();
    }

    /// <inheritdoc/>
    public byte[]? StudioAnimBlock(int modelIndex, string? includeName, int block)
    {
        byte* header = StudioHeader(modelIndex, includeName);

        if (header == null || *(int*)header != StudioId || block <= 0)
            return null;

        int length = *(int*)(header + 76);
        int count = *(int*)(header + 352);
        int table = *(int*)(header + 356);

        if (block >= count || table <= 0 || (long)table + 8L * count > length)
            return null;

        int start = *(int*)(header + table + 8 * block);
        int end = *(int*)(header + table + 8 * block + 4);

        if (start < 0 || end <= start || end - start > MaxAnimBlockBytes)
            return null;

        byte* data = _fwd->GetAnimBlock(_self, header, block);

        return data == null ? null : new ReadOnlySpan<byte>(data, end - start).ToArray();
    }

    // `modtype_t` runs mod_bad(0) .. mod_studio(3). Anything else is not one of
    // its values and is folded onto Bad rather than leaked into the enum, which
    // a plain cast would do.
    private static ModelType Kind(int answer)
        => answer is < 0 or > (int)ModelType.Studio ? ModelType.Bad : (ModelType)answer;
}

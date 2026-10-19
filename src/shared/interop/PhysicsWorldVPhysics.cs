//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>A model's <c>vcollide_t</c>: the pointer, its solid count and its key text (0 for none).</summary>
public readonly record struct VCollideView(nint Pointer, int SolidCount, nint KeyValues);

/// <summary>
/// One <c>solid_t</c> as <c>IVPhysicsKeyParser::ParseSolid</c> left it, with the
/// solid key handler's <c>contents</c> (stock's <c>g_SolidSetup.GetContentsMask()</c>).
/// </summary>
public readonly record struct ParsedSolid(int Index, uint ContentsMask, ObjectParams Params);

/// <summary>One <c>fluid_t</c> as <c>IVPhysicsKeyParser::ParseFluid</c> left it: its solid and its surfaceprop.</summary>
public readonly record struct ParsedFluid(int Index, string SurfaceProp);

/// <summary>
/// What the world's physics build asks vphysics and the engine, one call each
/// (plan_physics_bones_native_zero 2.6). The decisions -- which block makes
/// which object, with which params -- are the shared PhysicsWorldBuilder's
/// and ModelSolidParse's; this carries them out.
/// </summary>
public interface IPhysicsWorldVPhysics
{
    /// <summary>The level's IPhysicsEnvironment (physenv), or 0 between levels.</summary>
    nint Environment();

    /// <summary><c>modelinfo-&gt;GetVCollide( modelIndex )</c>; default when the model has none.</summary>
    VCollideView Collide(int modelIndex);

    /// <summary><c>vcollide_t::solids[ index ]</c>; 0 outside the solids.</summary>
    nint CollideSolid(in VCollideView collide, int index);

    /// <summary><c>physprops-&gt;GetSurfaceIndex( name )</c>.</summary>
    int SurfaceIndex(string name);

    /// <summary>Native text that stays readable for the process, for an object's debug name.</summary>
    nint NativeText(string text);

    /// <summary><c>physenv-&gt;CreatePolyObjectStatic( collide, surface, vec3_origin, vec3_angle, &amp;params )</c>; 0 when vphysics makes none.</summary>
    nint CreatePolyObjectStatic(nint collide, int surfaceIndex, in ObjectParams objectParams);

    /// <summary><c>SetCallbackFlags( GetCallbackFlags() | CALLBACK_NEVER_DELETED )</c>.</summary>
    void MarkNeverDeleted(nint physicsObject);

    /// <summary><c>IPhysicsObject::SetContents</c>.</summary>
    void SetContents(nint physicsObject, uint contents);

    /// <summary><c>physenv-&gt;CreateFluidController( water, &amp;fluid.params )</c> over the last fluid parsed, its game data set first.</summary>
    nint CreateFluidController(nint waterObject, nint gameData);

    /// <summary><c>physcollision-&gt;VPhysicsKeyParserCreate( keyValues )</c>.</summary>
    nint KeyParserCreate(nint keyValues);

    /// <summary><c>physcollision-&gt;VPhysicsKeyParserDestroy</c>.</summary>
    void KeyParserDestroy(nint parser);

    /// <summary><c>IVPhysicsKeyParser::Finished</c>.</summary>
    bool Finished(nint parser);

    /// <summary><c>IVPhysicsKeyParser::GetCurrentBlockName</c>.</summary>
    string BlockName(nint parser);

    /// <summary>
    /// <c>IVPhysicsKeyParser::ParseSolid</c> into a zeroed scratch <c>solid_t</c>
    /// with the solid key handler: its defaults are <c>g_PhysDefaultObjectParams</c>
    /// and it keeps the <c>contents</c> key. The scratch stays live until the next parse.
    /// </summary>
    ParsedSolid ParseSolid(nint parser);

    /// <summary>
    /// The last parsed solid copied into the caller's <c>solid_t</c> at
    /// <paramref name="solid"/> (stock's <c>solid = tmpSolid</c>).
    /// </summary>
    void CopyParsedSolid(nint solid);

    /// <summary>The caller's <c>solid_t</c> zeroed and given <c>g_PhysDefaultObjectParams</c>.</summary>
    void ResetSolid(nint solid);

    /// <summary>The caller's <c>solid_t</c>'s <c>params.enableCollisions</c>.</summary>
    void SetSolidEnableCollisions(nint solid, bool enable);

    /// <summary><c>IVPhysicsKeyParser::ParseFluid( &amp;fluid, NULL )</c> into the scratch <c>fluid_t</c>.</summary>
    ParsedFluid ParseFluid(nint parser);

    /// <summary><c>IVPhysicsKeyParser::ParseSurfaceTable( table, NULL )</c>.</summary>
    void ParseSurfaceTable(nint parser, Span<int> table);

    /// <summary><c>physprops-&gt;SetWorldMaterialIndexTable( table, count )</c>.</summary>
    void SetWorldMaterialIndexTable(ReadOnlySpan<int> table);

    /// <summary><c>IVPhysicsKeyParser::SkipBlock</c>.</summary>
    void SkipBlock(nint parser);

    /// <summary><c>physcollision-&gt;SupportsVirtualMesh</c>.</summary>
    bool SupportsVirtualMesh();

    /// <summary><c>modelinfo-&gt;GetCollideForVirtualTerrain( index )</c>; 0 for none.</summary>
    nint VirtualTerrainCollide(int index);

    /// <summary><c>staticpropmgr-&gt;CreateVPhysicsRepresentations( physenv, &amp;g_SolidSetup, NULL )</c>.</summary>
    void CreateStaticPropPhysics(nint environment);
}

/// <summary>
/// <c>surfacedata_t</c>'s ten sound names and handles, in their field order:
/// stepleft, stepright, impactSoft, impactHard, scrapeSmooth, scrapeRough,
/// bulletImpact, rolling, breakSound, strainSound.
/// </summary>
public interface ISurfaceSoundTable
{
    /// <summary><c>physprops-&gt;SurfacePropCount()</c>.</summary>
    int Count();

    /// <summary>The surface's ten <c>sounds.*</c> string indices (0 is "no sound"); false when vphysics answers no surface.</summary>
    bool SoundNames(int surface, Span<ushort> names);

    /// <summary><c>physprops-&gt;GetString( index )</c>.</summary>
    string? Text(ushort index);

    /// <summary>The surface's ten <c>soundhandles.*</c>, written in place.</summary>
    void WriteHandles(int surface, ReadOnlySpan<short> handles);
}

/// <summary>
/// vphysics' <c>IVPhysicsKeyHandler</c>'s managed side (plan_physics_bones_native_zero
/// Phase 3, shared since 2.6): libss_fwd builds the object vphysics calls
/// (<see cref="PhysicsCallbackObjects"/>), whose overrides call this table. The keys
/// a .phy block holds that vphysics does not parse itself, handed over one at a
/// time from inside <c>IVPhysicsKeyParser::ParseSolid</c> / <c>ParseCustom</c>.
/// </summary>
[ManagedVtable(typeof(ManagedVPhysicsKeyHandlerTable))]
public unsafe interface IVPhysicsKeyHandlerSink
{
    void ParseKeyValue(void* pData, byte* pKey, byte* pValue);

    void SetDefaults(void* pData);
}

/// <summary>
/// Stock's <c>CSolidSetDefaults</c> (physics_shared.cpp's <c>g_SolidSetup</c>):
/// a solid's defaults are <c>g_PhysDefaultObjectParams</c>, its contents
/// <c>CONTENTS_SOLID</c> unless a <c>contents</c> key says otherwise. One per
/// key-handler table, as stock's was one object.
/// </summary>
public sealed unsafe class SolidKeyDefaults(nint defaultName)
{
    /// <summary>bspflags.h's <c>CONTENTS_SOLID</c>.</summary>
    public const uint ContentsSolid = 0x1;

    // A .phy key or value is a token of a few dozen bytes; the bound only
    // stops a missing terminator from walking off.
    private const int MaxToken = 64;

    /// <summary>The name <c>g_PhysDefaultObjectParams</c> carries ("DEFAULT"), as native text.</summary>
    public nint DefaultName => defaultName;

    /// <summary>The last solid's <c>contents</c> (<c>CSolidSetDefaults::GetContentsMask</c>).</summary>
    public uint ContentsMask { get; private set; }

    public void ParseKeyValue(byte* pKey, byte* pValue)
    {
        if (string.Equals(Vtable.TextOf(pKey, MaxToken), "contents", StringComparison.OrdinalIgnoreCase))
            ContentsMask = unchecked((uint)VariantParse.Atoi(Vtable.TextOf(pValue, MaxToken)));
    }

    public void SetDefaults(void* pData)
    {
        if (pData != null)
            *(ObjectParams*)((byte*)pData + SolidLayout.Params) = ObjectParams.Defaults(defaultName);

        ContentsMask = ContentsSolid;
    }
}

/// <summary>A side's key-handler table as the world build hands it to vphysics.</summary>
public interface ISolidKeyTable
{
    /// <summary>The table vphysics is handed (its address), or 0 before it is built.</summary>
    nint Table { get; }

    /// <summary>The solid half's state.</summary>
    SolidKeyDefaults Solids { get; }

    /// <summary>Whether a ParseSolid is in flight: a table that also serves custom blocks answers as the solid handler while set.</summary>
    bool SolidInFlight { get; set; }
}

/// <summary><c>vcollide_parse.h</c>'s <c>fluid_t</c> and <c>fluidparams_t</c>, by offset.</summary>
public static class FluidLayout
{
    public const int Index = 0;
    public const int SurfaceProp = 4;
    public const int SurfacePropCapacity = 512;
    public const int Params = 520;
    public const int ParamsGameData = 40;
    public const int Size = 576;
}

/// <summary><c>surfacedata_t</c>'s two arrays of ten: the sound names and the handles.</summary>
public static class SurfaceDataLayout
{
    public const int SoundNames = 44;
    public const int SoundHandles = 76;
}

/// <summary>
/// <see cref="IPhysicsWorldVPhysics"/> and <see cref="ISurfaceSoundTable"/>
/// over the real engine: every member one call through a generated vtable.
/// Holds the interfaces it was handed, a scratch <c>solid_t</c> and
/// <c>fluid_t</c>, and the debug names it gave vphysics.
/// </summary>
public sealed unsafe class PhysicsWorldVPhysics : IPhysicsWorldVPhysics, ISurfaceSoundTable, IDisposable
{
    /// <summary>vphysics_interface.h's <c>CALLBACK_NEVER_DELETED</c>.</summary>
    public const ushort CallbackNeverDeleted = 0x0200;

    private readonly Func<nint> _environment;
    private readonly void* _collision;
    private readonly PhysicsCollisionFwd* _collisionFwd;
    private readonly void* _props;
    private readonly PhysicsSurfacePropsFwd* _propsFwd;
    private readonly void* _modelInfo;
    private readonly ModelInfoFwd* _modelInfoFwd;
    private readonly IStaticPropPhysics? _staticProps;
    private readonly ISolidKeyTable _keys;
    private readonly VPhysicsTables _vphysics;

    // A name vphysics is handed may be kept as the object's name for the
    // object's life, which can outlive this load (a hot reload keeps the
    // level), so the names are allocated once and never freed: "world",
    // "fluid" and at most 2048 "vdisp_%04d", a few tens of kilobytes at most.
    private readonly Dictionary<string, nint> _texts = new(StringComparer.Ordinal);

    private byte* _solid;
    private byte* _fluid;

    /// <param name="environment">The level's IPhysicsEnvironment, asked per call.</param>
    /// <param name="staticProps">The server's static prop manager (Server.Interop's StaticPropMgrServer), or null on a side that does not make the static props' physics here.</param>
    /// <param name="collisionFwd">IPhysicsCollision's forwarders (<c>PhysicsCollisionFwd</c>); the other
    /// <c>*Fwd</c> parameters likewise. Untyped because the forwarder structs are internal.</param>
    /// <param name="vphysics">The side's vphysics forwarder tables, for the objects this calls.</param>
    public PhysicsWorldVPhysics(Func<nint> environment, void* collision, void** collisionFwd, void* props, void** propsFwd,
                                void* modelInfo, void** modelInfoFwd, IStaticPropPhysics? staticProps,
                                ISolidKeyTable keys, VPhysicsTables vphysics)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _collision = collision;
        _collisionFwd = (PhysicsCollisionFwd*)collisionFwd;
        _props = props;
        _propsFwd = (PhysicsSurfacePropsFwd*)propsFwd;
        _modelInfo = modelInfo;
        _modelInfoFwd = (ModelInfoFwd*)modelInfoFwd;
        _staticProps = staticProps;
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _vphysics = vphysics ?? throw new ArgumentNullException(nameof(vphysics));
        _solid = (byte*)NativeMemory.AllocZeroed(SolidLayout.Size);
        _fluid = (byte*)NativeMemory.AllocZeroed(FluidLayout.Size);
    }

    /// <summary>
    /// From <paramref name="engine"/>'s own side's factories: vphysics'
    /// collision and surface tables and the side's model info, with the static
    /// prop manager the server hands in (<paramref name="staticProps"/>: its
    /// wrapper and table are Server.Interop's and libss_fwd_server's; null on
    /// the client). Null, with the reason, when one does not answer.
    /// </summary>
    public static PhysicsWorldVPhysics? Acquire(ModuleEngine engine, Func<nint> environment, IStaticPropPhysics? staticProps,
                                                ISolidKeyTable keys, out string why)
    {
        NativeInterfaceFactory factory = (engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces;

        if (!Forwarders.TryAcquire<PhysicsCollisionFwd>(factory, SourceSharpFactoryType.Physics, "VPhysicsCollision007",
                                                        out void* collision, out PhysicsCollisionFwd* collisionFwd, out why)
            || !Forwarders.TryAcquire<PhysicsSurfacePropsFwd>(factory, SourceSharpFactoryType.Physics,
                                                              PhysicsSurfaceProps.InterfaceVersion,
                                                              out void* props, out PhysicsSurfacePropsFwd* propsFwd, out why)
            || !Forwarders.TryAcquire<ModelInfoFwd>(factory, SourceSharpFactoryType.AppSystem,
                                                 EngineBound.ModelInfoVersion(engine.Api.Side),
                                                 out void* modelInfo, out ModelInfoFwd* modelInfoFwd, out why))
        {
            return null;
        }

        return new PhysicsWorldVPhysics(environment, collision, (void**)collisionFwd, props, (void**)propsFwd,
                                        modelInfo, (void**)modelInfoFwd, staticProps, keys, engine.Api.VPhysics);
    }

    public void Dispose()
    {
        NativeMemory.Free(_solid);
        NativeMemory.Free(_fluid);
        _solid = null;
        _fluid = null;
    }

    private PhysicsEnvironmentFwd* Env(nint environment) => _vphysics.PhysicsEnvironment;

    private PhysicsObjectFwd* Obj(nint physicsObject) => _vphysics.PhysicsObject;

    private VPhysicsKeyParserFwd* Parser(nint parser) => _vphysics.VPhysicsKeyParser;

    public nint Environment() => _environment();

    public VCollideView Collide(int modelIndex)
    {
        byte* collide = (byte*)_modelInfoFwd->GetVCollide_2(_modelInfo, modelIndex);
        if (collide == null)
            return default;

        int solidCount = *(ushort*)(collide + VCollideFields.SolidCountAndPacked) & VCollideFields.SolidCountMask;
        return new VCollideView((nint)collide, solidCount, *(nint*)(collide + VCollideFields.KeyValues));
    }

    public nint CollideSolid(in VCollideView collide, int index)
    {
        if (collide.Pointer == 0 || index < 0 || index >= collide.SolidCount)
            return 0;

        nint* solids = *(nint**)((byte*)collide.Pointer + VCollideFields.Solids);
        return solids == null ? 0 : solids[index];
    }

    public int SurfaceIndex(string name)
    {
        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
            return _propsFwd->GetSurfaceIndex(_props, p);
    }

    public nint NativeText(string text)
    {
        if (_texts.TryGetValue(text, out nint p))
            return p;

        byte[] bytes = Vtable.Utf8(text);
        byte* copy = (byte*)NativeMemory.AllocZeroed((nuint)bytes.Length + 1);
        bytes.CopyTo(new Span<byte>(copy, bytes.Length));
        _texts[text] = (nint)copy;
        return (nint)copy;
    }

    public nint CreatePolyObjectStatic(nint collide, int surfaceIndex, in ObjectParams objectParams)
    {
        nint environment = _environment();
        if (environment == 0 || collide == 0)
            return 0;

        Vector origin = Vector.Zero;
        QAngle angles = default;
        ObjectParams p = objectParams;
        return (nint)Env(environment)->CreatePolyObjectStatic((void*)environment, (void*)collide, surfaceIndex, &origin, &angles, &p);
    }

    public void MarkNeverDeleted(nint physicsObject)
    {
        if (physicsObject == 0)
            return;

        PhysicsObjectFwd* vt = Obj(physicsObject);
        vt->SetCallbackFlags((void*)physicsObject, (ushort)(vt->GetCallbackFlags((void*)physicsObject) | CallbackNeverDeleted));
    }

    public void SetContents(nint physicsObject, uint contents)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->SetContents((void*)physicsObject, contents);
    }

    public nint CreateFluidController(nint waterObject, nint gameData)
    {
        nint environment = _environment();
        if (environment == 0 || waterObject == 0)
            return 0;

        *(nint*)(_fluid + FluidLayout.Params + FluidLayout.ParamsGameData) = gameData;
        return (nint)Env(environment)->CreateFluidController((void*)environment, (void*)waterObject, _fluid + FluidLayout.Params);
    }

    public nint KeyParserCreate(nint keyValues) => (nint)_collisionFwd->VPhysicsKeyParserCreate(_collision, (byte*)keyValues);

    public void KeyParserDestroy(nint parser)
    {
        if (parser != 0)
            _collisionFwd->VPhysicsKeyParserDestroy(_collision, (void*)parser);
    }

    public bool Finished(nint parser) => parser == 0 || Parser(parser)->Finished((void*)parser) != 0;

    // A block name is a short token.
    public string BlockName(nint parser) => Vtable.TextOf(Parser(parser)->GetCurrentBlockName((void*)parser), 256) ?? string.Empty;

    public ParsedSolid ParseSolid(nint parser)
    {
        new Span<byte>(_solid, SolidLayout.Size).Clear();

        nint table = _keys.Table;
        _keys.SolidInFlight = true;
        try
        {
            Parser(parser)->ParseSolid((void*)parser, _solid, (void*)table);
        }
        finally
        {
            _keys.SolidInFlight = false;
        }

        return new ParsedSolid(*(int*)(_solid + SolidLayout.Index), _keys.Solids.ContentsMask,
                               *(ObjectParams*)(_solid + SolidLayout.Params));
    }

    public void CopyParsedSolid(nint solid)
    {
        if (solid == 0)
            return;

        byte* dest = (byte*)solid;
        new ReadOnlySpan<byte>(_solid, SolidLayout.Size).CopyTo(new Span<byte>(dest, SolidLayout.Size));

        // The parser points params.massCenterOverride at the solid's own
        // override when the block names one. Stock's `solid = tmpSolid` copied
        // that pointer into the caller's solid still naming the temporary; it
        // names the caller's own copy here.
        ObjectParams* p = (ObjectParams*)(dest + SolidLayout.Params);
        if (p->MassCenterOverride == (nint)(_solid + SolidLayout.MassCenterOverride))
            p->MassCenterOverride = (nint)(dest + SolidLayout.MassCenterOverride);
    }

    public void ResetSolid(nint solid)
    {
        if (solid == 0)
            return;

        new Span<byte>((void*)solid, SolidLayout.Size).Clear();
        *(ObjectParams*)((byte*)solid + SolidLayout.Params) = ObjectParams.Defaults(_keys.Solids.DefaultName);
    }

    public void SetSolidEnableCollisions(nint solid, bool enable)
    {
        if (solid != 0)
            ((ObjectParams*)((byte*)solid + SolidLayout.Params))->EnableCollisions = enable ? (byte)1 : (byte)0;
    }

    public ParsedFluid ParseFluid(nint parser)
    {
        new Span<byte>(_fluid, FluidLayout.Size).Clear();
        Parser(parser)->ParseFluid((void*)parser, _fluid, null);

        return new ParsedFluid(*(int*)(_fluid + FluidLayout.Index),
                               Vtable.TextOf(_fluid + FluidLayout.SurfaceProp, FluidLayout.SurfacePropCapacity) ?? string.Empty);
    }

    public void ParseSurfaceTable(nint parser, Span<int> table)
    {
        fixed (int* p = table)
            Parser(parser)->ParseSurfaceTable((void*)parser, p, null);
    }

    public void SetWorldMaterialIndexTable(ReadOnlySpan<int> table)
    {
        fixed (int* p = table)
            _propsFwd->SetWorldMaterialIndexTable(_props, p, table.Length);
    }

    public void SkipBlock(nint parser) => Parser(parser)->SkipBlock((void*)parser);

    public bool SupportsVirtualMesh() => _collisionFwd->SupportsVirtualMesh(_collision) != 0;

    public nint VirtualTerrainCollide(int index) => (nint)_modelInfoFwd->GetCollideForVirtualTerrain(_modelInfo, index);

    public void CreateStaticPropPhysics(nint environment)
    {
        if (_staticProps == null || environment == 0)
            return;

        _keys.SolidInFlight = true;
        try
        {
            _staticProps.CreateVPhysicsRepresentations(environment, _keys.Table);
        }
        finally
        {
            _keys.SolidInFlight = false;
        }
    }

    // ---- the surface sounds ---------------------------------------------------

    public int Count() => _propsFwd->SurfacePropCount(_props);

    public bool SoundNames(int surface, Span<ushort> names)
    {
        byte* data = (byte*)_propsFwd->GetSurfaceData(_props, surface);
        if (data == null)
            return false;

        new ReadOnlySpan<ushort>(data + SurfaceDataLayout.SoundNames, names.Length).CopyTo(names);
        return true;
    }

    // A soundscript name is a short token.
    public string? Text(ushort index) => Vtable.TextOf(_propsFwd->GetString(_props, index), 128);

    public void WriteHandles(int surface, ReadOnlySpan<short> handles)
    {
        byte* data = (byte*)_propsFwd->GetSurfaceData(_props, surface);
        if (data != null)
            handles.CopyTo(new Span<short>(data + SurfaceDataLayout.SoundHandles, handles.Length));
    }
}

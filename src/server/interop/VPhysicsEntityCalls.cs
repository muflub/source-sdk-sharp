//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>A parsed <c>solid_t</c>'s three fields an entity's physics init reads.</summary>
public readonly record struct EntitySolid(int Index, string SurfaceProp, ObjectParams Params);

/// <summary>
/// Every vphysics call an entity's physics init makes beyond the world
/// build's (plan_physics_bones_native_zero, InitPhysics to managed): each a
/// call through its generated forwarders -- <see cref="PhysicsEnvironmentFwd"/>,
/// <see cref="PhysicsObjectFwd"/>. A 0 pointer is a no-op answering zero, as
/// stock's NULL checks are.
/// </summary>
public interface IVPhysicsEntityCalls
{
    /// <summary>A <c>solid_t</c> in native memory the parse fills (stock's stack <c>solid_t</c>), alive until the next parse.</summary>
    nint ScratchSolid { get; }

    /// <summary>The index, surfaceprop and params of the <c>solid_t</c> at <paramref name="solid"/>.</summary>
    EntitySolid ReadSolid(nint solid);

    /// <summary>
    /// <c>physenv-&gt;CreatePolyObject</c>, or <c>CreatePolyObjectStatic</c>
    /// when <paramref name="isStatic"/>, at the pose given; 0 when vphysics makes none.
    /// </summary>
    nint CreatePolyObject(nint environment, nint collide, int surfaceIndex, Vector origin, QAngle angles,
                          in ObjectParams objectParams, bool isStatic);

    /// <summary><c>physenv-&gt;DestroyObject</c>, its game data cleared first (native's DestroyPhysics).</summary>
    void DestroyObject(nint environment, nint physicsObject);

    /// <summary><c>IPhysicsObject::GetGameFlags</c>.</summary>
    ushort GameFlags(nint physicsObject);

    /// <summary><c>IPhysicsObject::SetGameFlags</c>.</summary>
    void SetGameFlags(nint physicsObject, ushort flags);

    /// <summary><c>IPhysicsObject::Sleep</c>.</summary>
    void Sleep(nint physicsObject);

    /// <summary><c>IPhysicsObject::Wake</c>.</summary>
    void Wake(nint physicsObject);

    // ---- stock's TransferPhysicsObject (physobj.cpp) ----------------------

    /// <summary><c>IPhysicsObject::IsStatic</c>.</summary>
    bool IsStatic(nint physicsObject);

    /// <summary><c>IPhysicsObject::RemoveShadowController</c>.</summary>
    void RemoveShadowController(nint physicsObject);

    /// <summary><c>IPhysicsObject::SetGameData</c>.</summary>
    void SetGameData(nint physicsObject, nint gameData);

    /// <summary><c>IPhysicsObject::EnableMotion</c>.</summary>
    void EnableMotion(nint physicsObject, bool enable);

    /// <summary><c>IPhysicsObject::EnableGravity</c>.</summary>
    void EnableGravity(nint physicsObject, bool enable);

    /// <summary><c>IPhysicsObject::RecheckCollisionFilter</c>.</summary>
    void RecheckCollisionFilter(nint physicsObject);
}

/// <summary>The real vphysics. Holds one scratch <c>solid_t</c>, freed on dispose.</summary>
public sealed unsafe class VPhysicsEntityCalls : IVPhysicsEntityCalls, IDisposable
{
    private readonly VPhysicsTables _vphysics;

    /// <summary>Over <paramref name="vphysics"/>, its side's vphysics forwarder tables.</summary>
    public VPhysicsEntityCalls(VPhysicsTables vphysics)
        => _vphysics = vphysics ?? throw new ArgumentNullException(nameof(vphysics));

    private byte* _solid = (byte*)NativeMemory.AllocZeroed(SolidLayout.Size);

    private PhysicsEnvironmentFwd* Env(nint e) => _vphysics.PhysicsEnvironment;

    private PhysicsObjectFwd* Obj(nint o) => _vphysics.PhysicsObject;

    public nint ScratchSolid => (nint)_solid;

    public EntitySolid ReadSolid(nint solid)
    {
        if (solid == 0)
            return new EntitySolid(0, string.Empty, default);

        byte* s = (byte*)solid;
        return new EntitySolid(*(int*)(s + SolidLayout.Index),
                               Vtable.TextOf(s + SolidLayout.SurfaceProp, SolidLayout.NameCapacity) ?? string.Empty,
                               *(ObjectParams*)(s + SolidLayout.Params));
    }

    public nint CreatePolyObject(nint environment, nint collide, int surfaceIndex, Vector origin, QAngle angles,
                                 in ObjectParams objectParams, bool isStatic)
    {
        if (environment == 0 || collide == 0)
            return 0;

        ObjectParams p = objectParams;
        return isStatic
            ? (nint)Env(environment)->CreatePolyObjectStatic((void*)environment, (void*)collide, surfaceIndex,
                                                             &origin, &angles, &p)
            : (nint)Env(environment)->CreatePolyObject((void*)environment, (void*)collide, surfaceIndex,
                                                       &origin, &angles, &p);
    }

    public void DestroyObject(nint environment, nint physicsObject)
    {
        if (environment == 0 || physicsObject == 0)
            return;

        Obj(physicsObject)->SetGameData((void*)physicsObject, null);
        Env(environment)->DestroyObject((void*)environment, (void*)physicsObject);
    }

    public ushort GameFlags(nint o) => o == 0 ? (ushort)0 : Obj(o)->GetGameFlags((void*)o);

    public void SetGameFlags(nint o, ushort flags)
    {
        if (o != 0)
            Obj(o)->SetGameFlags((void*)o, flags);
    }

    public void Sleep(nint o)
    {
        if (o != 0)
            Obj(o)->Sleep((void*)o);
    }

    public void Wake(nint o)
    {
        if (o != 0)
            Obj(o)->Wake((void*)o);
    }

    public bool IsStatic(nint o) => o != 0 && Obj(o)->IsStatic((void*)o) != 0;

    public void RemoveShadowController(nint o)
    {
        if (o != 0)
            Obj(o)->RemoveShadowController((void*)o);
    }

    public void SetGameData(nint o, nint gameData)
    {
        if (o != 0)
            Obj(o)->SetGameData((void*)o, (void*)gameData);
    }

    public void EnableMotion(nint o, bool enable)
    {
        if (o != 0)
            Obj(o)->EnableMotion((void*)o, enable ? (byte)1 : (byte)0);
    }

    public void EnableGravity(nint o, bool enable)
    {
        if (o != 0)
            Obj(o)->EnableGravity((void*)o, enable ? (byte)1 : (byte)0);
    }

    public void RecheckCollisionFilter(nint o)
    {
        if (o != 0)
            Obj(o)->RecheckCollisionFilter((void*)o);
    }

    public void Dispose()
    {
        NativeMemory.Free(_solid);
        _solid = null;
    }
}

/// <summary><see cref="IEntityPhysicsNative"/> over one side's engine slots.</summary>
public sealed class EngineEntityPhysicsNative : IEntityPhysicsNative
{
    private readonly EngineSide _side;

    internal EngineEntityPhysicsNative(EngineSide side) => _side = side ?? throw new ArgumentNullException(nameof(side));

    /// <summary>The module's own side.</summary>
    public static EngineEntityPhysicsNative ForEngine(ModuleEngine engine)
        => new((engine ?? throw new ArgumentNullException(nameof(engine))).Api);

    public bool Swap(EntityHandle entity, nint physicsObject, out nint previous)
        => _side.EntitySwapPhysicsObject(entity, physicsObject, out previous);

    /// <summary>
    /// The module's side's <c>InitPhysics</c> and <c>SetPhysicsStatic</c>
    /// answered by <paramref name="route"/> (null: they make nothing).
    /// </summary>
    public static void Route(ModuleEngine engine, IEntityPhysicsRoute? route)
        => (engine ?? throw new ArgumentNullException(nameof(engine))).Api.PhysicsInit = route;
}

using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// Standalone parenting's calls into the managed hierarchy driver,
/// managed-built (plan_engineapi_vtables E8): the Entry.cs thunks' bodies,
/// moved. The driver is null until the module has built it, and each slot
/// answers then as the thunks did: nothing parented, nothing converted, 0.
/// </summary>
public sealed unsafe class HierarchyBinding(Crossings crossings, IHierarchyHost host) : IManagedHierarchy
{
    private void Enter(CrossingOp op)
    {
        crossings.Count(op);
    }

    private static string Utf8(byte* p)
        => p == null ? string.Empty : (Marshal.PtrToStringUTF8((nint)p) ?? string.Empty);

    private HierarchyDriver? Driver => host.Hierarchy;

    public void HierarchyParentMoved(uint a0) { Enter(CrossingOp.ServerHierarchyParentMoved); Driver?.ParentMoved(new EntityHandle(a0)); }

    public void HierarchyChildMoved(uint a0) { Enter(CrossingOp.ServerHierarchyChildMoved); Driver?.ChildMoved(new EntityHandle(a0)); }

    public int HierarchySetParent(uint a0, uint a1, int a2)
    {
        Enter(CrossingOp.ServerHierarchySetParent);
        return Driver?.SetParent(new EntityHandle(a0), new EntityHandle(a1), a2) == true ? 1 : 0;
    }

    public int HierarchyGetParentAttachment(uint a0)
    {
        Enter(CrossingOp.ServerHierarchyGetParentAttachment);
        return Driver?.GetParentAttachment(new EntityHandle(a0)) ?? 0;
    }

    public void HierarchySetParentName(uint a0, byte* a1)
    {
        Enter(CrossingOp.ServerHierarchySetParentName);
        Driver?.SetParentName(new EntityHandle(a0), Utf8(a1));
    }

    public void HierarchySetParentAttachmentName(uint a0, byte* a1)
    {
        Enter(CrossingOp.ServerHierarchySetParentAttachmentName);
        Driver?.SetParentAttachmentName(new EntityHandle(a0), Utf8(a1));
    }

    public int HierarchyToLocal(uint a0, void* a1, void* a2) { Enter(CrossingOp.ServerHierarchyToLocal); return Convert(a0, (float*)a1, (float*)a2, toLocal: true); }

    public int HierarchyToWorld(uint a0, void* a1, void* a2) { Enter(CrossingOp.ServerHierarchyToWorld); return Convert(a0, (float*)a1, (float*)a2, toLocal: false); }

    public void HierarchySetLocal(uint a0, void* a1, void* a2)
    {
        Enter(CrossingOp.ServerHierarchySetLocal);
        float* pOrigin = (float*)a1, pAngles = (float*)a2;
        if (pOrigin == null || pAngles == null)
            return;

        Driver?.SetLocal(new EntityHandle(a0),
                         new Vector(pOrigin[0], pOrigin[1], pOrigin[2]),
                         new QAngle(pAngles[0], pAngles[1], pAngles[2]));
    }

    // In and out through the caller's float triples, either of which may be absent.
    private int Convert(uint child, float* pOrigin, float* pAngles, bool toLocal)
    {
        if (Driver is not { } driver)
            return 0;

        var handle = new EntityHandle(child);
        var origin = pOrigin != null ? new Vector(pOrigin[0], pOrigin[1], pOrigin[2]) : default;
        var angles = pAngles != null ? new QAngle(pAngles[0], pAngles[1], pAngles[2]) : default;

        bool converted = toLocal
            ? driver.ToLocal(handle, ref origin, ref angles)
            : driver.ToWorld(handle, ref origin, ref angles);

        if (!converted)
            return 0;

        if (pOrigin != null)
        {
            pOrigin[0] = origin.X; pOrigin[1] = origin.Y; pOrigin[2] = origin.Z;
        }

        if (pAngles != null)
        {
            pAngles[0] = angles.Pitch; pAngles[1] = angles.Yaw; pAngles[2] = angles.Roll;
        }

        return 1;
    }
}

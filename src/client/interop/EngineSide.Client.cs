using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// The client's half of <see cref="EngineSide"/> (plan_shim_areas_split phase 7).
///
/// <para>
/// Each side compiles its own <see cref="EngineSide"/> since phase 3, so what
/// differs by side is said once, here and in the server's file, rather than
/// chosen at run time: each side's half registers its own areas
/// (<see cref="ClientAreaRegistration"/> here), and a slot both sides serve is
/// on a shared base area both register, so the shared source compiles against
/// either. No <c>#if</c>: the side is the assembly.
/// </para>
///
/// <para>
/// And every member that reaches only the client's areas: they live here, in
/// the client's project, rather than in the shared file, where a server's
/// build compiled them only to answer a default (review of phase 7). The
/// bindings that call them are split the same way (EngineBinding.Client.cs).
/// </para>
/// </summary>
internal sealed unsafe partial class EngineSide
{
    /// <summary>
    /// Attaches the side's own areas: the client's shared bases and its own,
    /// acquired all or none from the root and registered by type
    /// (<see cref="ClientAreaRegistration"/>). Called from LoadSideV2.
    /// </summary>
    internal bool AttachOwnShims(ShimRoot root, out string why)
    {
        var areas = new ServiceContainer();
        if (!ClientAreaRegistration.TryAcquire(root, (type, area) => areas.AddInstance(type, area), out why))
            return false;

        Attached(root, areas);
        return true;
    }

    /// <summary>
    /// The client's own services read only the shared four areas
    /// (EngineSide.cs), so there is nothing of the client's to take here.
    /// </summary>
    private static void OwnAreas(IServiceLocator areas) { }

    /// <summary>Every client area over no table: what the side holds before its attach.</summary>
    private static ServiceContainer DetachedAreas()
    {
        var areas = new ServiceContainer();
        ClientAreaRegistration.AddDetached((type, area) => areas.AddInstance(type, area));
        return areas;
    }

    /// <summary>
    /// The model precache the side's break models use: none, as the client's
    /// slot was null, so a missing model answers -1.
    /// </summary>
    private Func<string, int>? OwnModelPrecache => null;

    /// <summary>
    /// <c>IVEngineClient::Con_NPrintf</c>, which is varargs and so cannot be
    /// issued through a fixed indirect signature at all.
    ///
    /// <para>
    /// The native side passes <paramref name="text"/> as an ARGUMENT —
    /// <c>Con_NPrintf( pos, "%s", text )</c> — and never as the format string.
    /// Caller-controlled text in the format position reads the stack on any
    /// <c>%s</c> that happens to be in a model name or a chat line.
    /// </para>
    /// </summary>
    public unsafe void ConNPrintf(IViewClientArea viewArea, void* engineClient, int position, string text)
    {
        if (engineClient == null)
            return;

        byte[] bytes = Vtable.Utf8(text);

        fixed (byte* p = bytes)
            viewArea.ConNPrintf(engineClient, position, p);
    }

    /// <summary>
    /// <c>IVEngineClient::Con_NXPrintf</c>, with <c>con_nprint_s</c> flattened
    /// into its scalars.
    ///
    /// <para>
    /// The struct's layout stays native's business. <c>con_nprint.h</c> says of
    /// it that the "structure cannot be changed by mods", and a C# copy would be
    /// a second declaration of it with nothing holding the two in step.
    /// </para>
    /// </summary>
    public unsafe void ConNXPrintf(
        IViewClientArea viewArea, void* engineClient, int index, float timeToLive,
        float red, float green, float blue, bool fixedWidthFont, string text)
    {
        if (engineClient == null)
            return;

        byte[] bytes = Vtable.Utf8(text);
        float* colour = stackalloc float[3] { red, green, blue };

        fixed (byte* p = bytes)
            viewArea.ConNXPrintf(engineClient, index, timeToLive, colour,
                            Vtable.Bit(fixedWidthFont), p);
    }

    /// <summary>
    /// <c>IVEngineClient::GetLightForPoint</c>, which returns <c>Vector</c> BY
    /// VALUE — three floats in <c>xmm0</c> and <c>xmm1</c> under the SysV
    /// classification, which a <c>delegate* unmanaged&lt;...&gt;</c> has no
    /// spelling for. The floats come back through a pointer instead.
    /// </summary>
    /// <returns>False when nothing answered, in which case nothing is written.</returns>
    public unsafe bool TryGetLightForPoint(
        IViewClientArea viewArea, void* engineClient, in Vector position, bool clamp, out Vector light)
        => TryLightForPoint(viewArea, fast: false, engineClient, position, clamp, out light);

    /// <summary>
    /// <c>IVEngineClient::GetLightForPointFast</c>, the other half of the pair
    /// above and refused for the same reason.
    /// </summary>
    public unsafe bool TryGetLightForPointFast(
        IViewClientArea viewArea, void* engineClient, in Vector position, bool clamp, out Vector light)
        => TryLightForPoint(viewArea, fast: true, engineClient, position, clamp, out light);

    // plan_client_vtables CC7: the two slots are routed (Slot_/Has_), so the
    // choice between them is a flag rather than a struct field passed along.
    private unsafe bool TryLightForPoint(
        IViewClientArea viewArea, bool fast, void* engineClient, in Vector position, bool clamp, out Vector light)
    {
        light = default;

        if (engineClient == null)
            return false;

        float* input = stackalloc float[3] { position.X, position.Y, position.Z };
        float* answer = stackalloc float[3];

        // NOT WRITTEN ON A MISS, deliberately: native leaves the buffer alone
        // when it refuses, so "the light here is black" and "there was no
        // engine" stay different answers.
        answer[0] = float.NaN;

        if (fast)
            viewArea.GetLightForPointFast(engineClient, input, Vtable.Bit(clamp), answer);
        else
            viewArea.GetLightForPoint(engineClient, input, Vtable.Bit(clamp), answer);

        if (float.IsNaN(answer[0]))
            return false;

        light = new Vector(answer[0], answer[1], answer[2]);
        return true;
    }

    /// <summary>
    /// True when a client entity is DORMANT — out of the local player's PVS,
    /// so every value it holds is the last one that arrived rather than the
    /// current one. Client-side.
    ///
    /// <para>
    /// Ask this before believing any client-side reading, and print it in any
    /// report. A dormant entity answers a query without complaint, and that is
    /// not hypothetical: a viewmodel whose bounds sat below the floor (v_
    /// models are authored in CAMERA space) was culled, went dormant, and
    /// carried on answering plausible stale numbers to everything that asked.
    /// </para>
    /// </summary>
    public bool StandaloneIsDormant(IStandaloneClientArea standaloneArea, EntityHandle entity)
    {
        return standaloneArea.StandaloneIsDormant(entity.Value) != 0;
    }
}

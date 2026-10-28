using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of func_breakable_surf.
///
/// <para>
/// Most of what this entity does on the client is DRAWN. The mesh building is
/// shell_surface_render.cpp's — a mesh builder is not something to reach across
/// the boundary per vertex — and everything it builds from is decided here:
/// which materials the broken surface draws with, which panes still stand and
/// which edges are cracked (<see cref="BreakableSurfaceDraw"/>, lane M1-A).
/// </para>
///
/// <para>
/// A class with networked state of its own gets a table on a side only if that
/// side has an entity class to host it, and without one here the client
/// published nothing while the server published a SendTable. The engine is
/// unusually helpful about that particular mistake —
/// </para>
/// <code>
/// DataTable warning: No matching RecvTable for SendTable 'DT_func_breakable_surf'.
/// Client missing DT class func_breakable_surf
/// </code>
/// <para>
/// — and then the client begins joining and stops. So this exists to claim the
/// classname, and it holds the decoded state the draw is decided from.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.FuncBreakableSurf)]
public sealed class ClientBreakableSurf : ClientStandaloneEntity
{
    public ClientBreakableSurf(IEngineEntityRender render, IBreakableSurfaceRenderer surface)
        : base(render)
    {
        Surface = surface;
    }

    /// <summary>The native mesh builder the draw is handed to.</summary>
    private readonly IBreakableSurfaceRenderer Surface;

    public override string ClassName => EntityNames.FuncBreakableSurf;

    /// <summary>
    /// The same declaration the server writes through, decoded by the registry
    /// on every update.
    /// </summary>
    [NetState] public BreakableSurfaceNetState State = new();

    private bool _reported;

    // The materials, chosen once: at the first update that finds the surface broken.
    private bool _materialsChosen;

    // A plan has been handed over (so an unbroken update clears it).
    private bool _planned;

    /// <summary>
    /// A pane's crack pattern, kept so it does not reshuffle as its neighbours
    /// go. Chosen once per pane, the first time that pane is asked about.
    /// </summary>
    private readonly byte[] _styles = new byte[BreakableSurfaceNetState.MaxPanels
                                             * BreakableSurfaceNetState.MaxPanels];
    private readonly bool[] _styled = new bool[BreakableSurfaceNetState.MaxPanels
                                             * BreakableSurfaceNetState.MaxPanels];
    private readonly Random _random = new();

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (!State.Broken)
        {
            // Nothing to draw until the sheet breaks: the grid does not exist before then.
            if (_planned)
                Surface.SetPlan(Handle, ReadOnlySpan<int>.Empty);
            _planned = false;
            return;
        }

        if (!_materialsChosen)
        {
            _materialsChosen = true;
            SurfaceMaterialFacts facts = Surface.MaterialFacts(Handle);
            SurfaceMaterialChoice choice = BreakableSurfaceDraw.ChooseMaterials(facts, State.SurfaceType, out string? warning);
            if (warning is not null)
                Log.Warning(warning);
            Surface.SetMaterials(Handle, choice);

            // Whether the surface named a cracked variant decides whether ANY
            // crack sprite is drawn, so a window that shows clean holes and no
            // cracks is answered here rather than by staring at it.
            Log.Msg($"[SourceSharp] surface renderer: material '{facts.Material}', cracked variant " +
                    $"{(choice.SwapsDetail ? choice.Pieces : "NONE")}\n");
        }

        var edges = PaneEdges.Build(State.Wide, State.High, (w, h) => IsSolid(w, h), StyleOf);
        int[] plan = BreakableSurfaceDraw.Plan(State.Wide, State.High, State.Normal, State.Corner,
                                               State.PanelWidth, State.PanelHeight, State.SurfaceType,
                                               State.IsPaneSolid, edges);
        Surface.SetPlan(Handle, plan);
        _planned = true;

        // Once, when it goes. A window reports its grid at the moment it breaks
        // and says nothing on the updates that follow, which are panes falling
        // out one at a time.
        if (_reported)
            return;

        _reported = true;

        Log.Msg($"[SourceSharp/C# client] {ClassName} broke into " +
                   $"{State.Wide}x{State.High} panes of " +
                   $"{State.PanelWidth:F1}x{State.PanelHeight:F1}, corner {State.Corner}\n");
    }

    /// <summary>
    /// Outside the grid counts as SOLID, which is what stops the window's frame
    /// being drawn as a cracked edge.
    /// </summary>
    private bool IsSolid(int w, int h)
        => w < 0 || h < 0 || w >= State.Wide || h >= State.High || State.IsPaneSolid(w, h);

    private byte StyleOf(int w, int h)
    {
        int i = w + h * BreakableSurfaceNetState.MaxPanels;

        if (!_styled[i])
        {
            _styles[i] = (byte)_random.Next(0, PaneEdges.Styles);
            _styled[i] = true;
        }

        return _styles[i];
    }
}

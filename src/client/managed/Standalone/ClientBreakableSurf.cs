using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of func_breakable_surf.
///
/// <para>
/// Most of what this entity does on the client is DRAWN, and drawing is
/// shell_surface_render.cpp — a mesh builder is not something to reach across
/// the boundary per vertex. So this class does very little, and it is not
/// optional for that reason.
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
/// classname, and it holds the decoded state because anything the glass wants to
/// decide on this side later belongs here rather than in the renderer.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.FuncBreakableSurf)]
public sealed class ClientBreakableSurf : ClientStandaloneEntity
{
    public ClientBreakableSurf(IEngineEntityRender render, IEngineBrushMovement movement)
        : base(render)
    {
        Movement = movement;
    }

    /// <summary>moving a brush, and reporting when it arrives.</summary>
    private readonly IEngineBrushMovement Movement;


    public override string ClassName => EntityNames.FuncBreakableSurf;

    /// <summary>
    /// The same declaration the server writes through. Decoded by the registry
    /// on every update; the renderer reads the same bytes natively, at offsets
    /// taken from the same table.
    /// </summary>
    [NetState] public BreakableSurfaceNetState State = new();

    private bool _reported;

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
        // Which cracks show is decided HERE, from the pane bits, and pushed to
        // whatever draws this entity. Native does the mesh building and nothing
        // else — see PaneEdges.
        if (State.Broken)
        {
            var edges = PaneEdges.Build(State.Wide, State.High,
                                        (w, h) => IsSolid(w, h), StyleOf);

            Movement.SetSurfaceEdges(Handle, PaneEdges.Pack(edges), edges.Count);
        }

        // Once, when it goes. A window reports its grid at the moment it breaks
        // and says nothing on the updates that follow, which are panes falling
        // out one at a time — and that is the state the renderer is drawing, so
        // a line per pane per window would bury everything else.
        if (!State.Broken || _reported)
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

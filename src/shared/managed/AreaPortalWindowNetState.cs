using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What an area-portal window tells the client: how it fades, and what sits
/// behind it.
///
/// <para>
/// Everything here is about DRAWING, and that is the split the entity is built
/// on. The server decides whether the hole is worth flowing visibility through —
/// which is the same two distances, applied to a different question — and the
/// client decides how solid the pane looks from where the player is standing.
/// Both read the same numbers, and they arrive here rather than being computed
/// twice.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.FuncAreaportalwindow)]
public partial class AreaPortalWindowNetState
{
    /// <summary>Nearer than this and the pane is at its most transparent.</summary>
    public partial float FadeStartDist { get; set; }

    /// <summary>Further than this and the pane is solid — and the server stops sending through it.</summary>
    public partial float FadeDist { get; set; }

    /// <summary>How transparent it is allowed to get, 0..1. The map's "TranslucencyLimit".</summary>
    public partial float TranslucencyLimit { get; set; }

    /// <summary>
    /// A second brush model drawn behind the pane at full opacity, or -1.
    ///
    /// An index rather than a name because both sides precached the same list
    /// and already agree what the number means — the same reason a sprite
    /// travels as one in <see cref="BeamNetState"/>.
    /// </summary>
    public partial int BackgroundModelIndex { get; set; }
}

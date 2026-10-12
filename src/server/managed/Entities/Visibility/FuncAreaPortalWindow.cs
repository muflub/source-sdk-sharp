//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// func_areaportalwindow: an area portal you can see through, up to a point.
///
/// <para>
/// The same hole in the world as <see cref="FuncAreaPortal"/> and the same
/// portal number, with one difference that changes everything about it: it is
/// never shut by a map, it is shut by DISTANCE. Past FadeDist the server stops
/// flowing visibility through the hole, and the client draws the pane as solid
/// so that the player cannot tell. That pairing is the whole trick — the fade is
/// not decoration covering an optimisation, it IS the optimisation.
/// </para>
///
/// <para>
/// So this class owns both halves of one decision: it tells the native portal
/// entry what distance to shut at, and it networks the same distances so the
/// client's shell can fade the pane over exactly that band. There is no second
/// copy of the numbers.
/// </para>
///
/// <para>
/// Unlike the plain portal this one is a real brush — it is drawn — so it is a
/// <see cref="ScriptedEntityKind.Brush"/>, and it can WEAR somebody else's
/// brush: a mapper points "target" at another brush entity and the window takes
/// that entity's model and position and hides the original. Stock does the same,
/// in Activate, and the reason for the indirection is that the drawn pane and
/// the compiled portal hole are usually not the same brush.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncAreaportalwindow, ScriptedEntityKind.Brush)]
public sealed class FuncAreaPortalWindow : ScriptedEntity
{
    public FuncAreaPortalWindow(IEnginePrecache precache, IEngineEntityRender render, IEngineVisibility visibility)
    {
        Precache = precache;
        Render = render;
        Visibility = visibility;
    }

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;

    /// <summary>what can be seen, and how it is lit.</summary>
    private readonly IEngineVisibility Visibility;
    /// <summary>What a map may set on this entity. See <see cref="FuncAreaPortalWindowVars"/>.</summary>
    [ScriptVars] public FuncAreaPortalWindowVars Vars = new();

    [NetState] public AreaPortalWindowNetState State = new();

    private uint _portal;

    public override void OnSpawn()
    {
        State.FadeStartDist        = Vars.FadeStartDist;
        State.FadeDist             = Vars.FadeDist;
        State.TranslucencyLimit    = Vars.TranslucencyLimit;
        State.BackgroundModelIndex = -1;

        _portal = Visibility.AreaPortalCreate(Vars.PortalNumber, Vars.PortalVersion);

        if (_portal == 0)
        {
            Log.Msg($"[SourceSharp] {ClassName}: no portal number " +
                       $"({Vars.PortalNumber}); the pane will draw and cull nothing\n");
        }

        // Everything below names another entity, and at spawn the map is still
        // being read — the brush this window wears may not exist yet. Deferred
        // to the first think, which is what every other class here that resolves
        // a name does.
        SetNextThink(0f);
    }

    public override void OnRemoved()
    {
        Visibility.AreaPortalDestroy(_portal);
        _portal = 0;
    }

    //-------------------------------------------------------------------------
    // What stock does in Activate, once every entity in the map exists.
    //
    // The order matters and is stock's: the background is found first because
    // it is drawn behind, then the target, whose model and origin REPLACE this
    // entity's. Both originals are hidden, since this window is now drawing for
    // them.
    //-------------------------------------------------------------------------
    public override void OnThink()
    {
        if (Vars.BackgroundModel.Length > 0)
        {
            var background = Entities.FindByName(Vars.BackgroundModel);

            if (Entities.IsValid(background))
            {
                // Through the precache list, which is how a name becomes an
                // index both sides agree on. A brush model is already in the
                // BSP, so this is a lookup rather than a load.
                State.BackgroundModelIndex = Precache.PrecacheModel(Render.GetModelName(background));
                Render.SetVisible(background, false);
            }
            else
            {
                Log.Warning($"[SourceSharp] {ClassName}: no entity named " +
                               $"'{Vars.BackgroundModel}' to draw behind it\n");
            }
        }

        if (Vars.Target.Length > 0)
        {
            var target = Entities.FindByName(Vars.Target);

            if (Entities.IsValid(target))
            {
                string model = Render.GetModelName(target);

                if (model.Length > 0)
                {
                    SetModel(model);
                    Origin = Entities.GetOrigin(target);
                }

                Render.SetVisible(target, false);
            }
            else
            {
                Log.Warning($"[SourceSharp] {ClassName}: no entity named " +
                               $"'{Vars.Target}' to take a brush from\n");
            }
        }

        // LAST, and that ordering is the bug waiting to happen. The native
        // portal measures the fade distance from this entity's volume, and the
        // volume is whatever model it is wearing — so asking before the model
        // has been swapped measures from the brush the mapper did not want.
        Visibility.AreaPortalSetWindow(_portal, Handle, Vars.FadeDist);
    }
}

/// <summary>
/// The map keys <see cref="FuncAreaPortalWindow"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncAreaPortalWindowVars
{
    /// <summary>vbsp's, not a mapper's.</summary>
    public int PortalNumber { get; set; } = -1;

    public int PortalVersion { get; set; }

    /// <summary>Nearer than this and the pane is at its most transparent.</summary>
    public float FadeStartDist { get; set; }

    /// <summary>Further than this and the pane is solid and the hole is shut.</summary>
    public float FadeDist { get; set; }

    /// <summary>How transparent it is allowed to get, 0..1.</summary>
    public float TranslucencyLimit { get; set; }

    /// <summary>A brush entity drawn behind the pane, opaque, by name.</summary>
    [VarName("BackgroundBModel")]
    public string BackgroundModel { get; set; } = string.Empty;

    /// <summary>The brush whose model and position this window wears, by name.</summary>
    public string Target { get; set; } = string.Empty;
}

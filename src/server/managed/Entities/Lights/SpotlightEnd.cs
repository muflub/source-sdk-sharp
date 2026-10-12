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
/// spotlight_end: the bright patch on the floor under a spotlight.
///
/// <para>
/// Not something a mapper places. A <c>point_spotlight</c> makes one, moves it
/// to wherever its beam lands, and sizes it from how wide the beam is there —
/// so this entity's whole existence is being the far end of something else.
/// It is registered as a classname because that is how the spotlight creates
/// it, and because a map that has been decompiled and recompiled can end up
/// with one baked in.
/// </para>
///
/// <para>
/// It networks a size and a colour and nothing else. The client asks the engine
/// for a dynamic light and fills it in; there is no model, no collision and
/// nothing to touch.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SpotlightEnd, ScriptedEntityKind.Effect)]
public sealed class SpotlightEnd : ScriptedEntity
{
    public SpotlightEnd(IEngineEntityRender render)
    {
        Render = render;
    }

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;
    /// <summary>The same declaration the client half reads.</summary>
    [NetState] public SpotlightEndNetState State = new();

    public override void OnSpawn()
    {
        // Stock's default, and it matters: a spotlight_end that nothing
        // configures should still be a light rather than nothing at all.
        if (State.LightScale == 0f)
            State.LightScale = 100f;

        // Opaque unless something has already faded it. Written as a whole
        // colour because that is what the field is now; the three channels a
        // fresh entity holds are zero, so this is the black-and-opaque a
        // spotlight_end starts at and SetColour fills in.
        if (State.Tint.A == 0)
            State.Tint = State.Tint.WithAlpha(255);

        // The pool of light under a spotlight is visible from further away than
        // the entity marking it, so PVS culling by that point loses it early.
        Render.SetTransmitAlways(Handle, true);
    }

    /// <summary>Where the beam landed, and how bright the patch should be.</summary>
    public void Place(Vector position, float lightScale, byte alpha)
    {
        Origin = position;
        State.LightScale = lightScale;
        State.Tint = State.Tint.WithAlpha(alpha);
    }

    /// <summary>The colour, taken from whatever spotlight owns this one.</summary>
    public void SetColour(byte r, byte g, byte b)
        // The alpha is Place's and is left alone: how bright the patch is and
        // what colour it is are set by two different callers at two different
        // times, and a colour that carried its own alpha would have the second
        // one quietly undo the first.
        => State.Tint = new Color32(r, g, b, State.Tint.A);
}

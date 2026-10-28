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
/// _plasma: the flame a plasma fire is made of.
///
/// <para>
/// <see cref="SsFiresmoke"/>'s sibling and the other half of what
/// <c>env_fire</c> can build — <c>firetype</c> 1 rather than 0. It replaces
/// <c>_plasma</c>, a native <c>CPlasma</c> and <c>C_Plasma</c>, and the same
/// argument applies: the classname is code-spawned, appears in no map's entity
/// lump, and its only caller is already C#.
/// </para>
///
/// <para>
/// WHERE IT DIFFERS FROM ITS SIBLING, which is more than the sprite. A natural
/// flame is a particle system that is started and then left alone — stock's
/// client half had been worn down until every update method was an empty body.
/// A plasma flame is still fully alive: seven sprites drawn every frame at
/// positions derived from a scale that is itself being interpolated. So this one
/// networks a ramp and an on/off bit that its sibling has no use for, and its
/// client half draws rather than delegating.
/// </para>
///
/// <para>
/// The three sprites are precached and their indices networked, which is what
/// stock's <c>CPlasma</c> constructor does — and it is the one thing that
/// constructor did that had to survive, because a model index means nothing
/// without the precache that produced it.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.Plasma, ScriptedEntityKind.Effect)]
public sealed class SsPlasma : ScriptedEntity
{
    public SsPlasma(IEnginePrecache precache)
    {
        Precache = precache;
    }

    /// <summary>loading what it is drawn with.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>The flame sprite. Stock's, including its own TEMP comment.</summary>
    public const string FlameSprite = "sprites/plasma1.vmt";

    /// <summary>The glow laid on the floor beneath the flames.</summary>
    public const string GlowSprite = "sprites/fire_floor.vmt";

    /// <summary>
    /// The same declaration the client half reads. Written here, decoded there.
    /// </summary>
    [NetState] public PlasmaNetState State = new();

    /// <summary>
    /// The sprites, loaded and their indices put on the wire.
    ///
    /// <para>
    /// Stock does this in <c>CPlasma</c>'s CONSTRUCTOR, which is a place a
    /// precache should never happen — it runs whenever an entity is made, which
    /// on a save/restore is after the level has finished loading. Doing it at
    /// spawn is the same work at the moment the engine expects it.
    /// </para>
    ///
    /// <para>
    /// The two flame indices are the same material. That is stock's, and its own
    /// comment marks it <c>//&lt;&lt;TEMP&gt;&gt;</c>; both are sent rather than
    /// one being assumed to serve for both, so a mod that parts them needs no
    /// wire change.
    /// </para>
    /// </summary>
    public override void OnSpawn()
    {
        int flame = Precache.PrecacheModel(FlameSprite);
        int glow  = Precache.PrecacheModel(GlowSprite);

        State.PlasmaModelIndex  = flame;
        State.PlasmaModelIndex2 = flame;
        State.GlowModelIndex    = glow;

        // Asked HERE because it can only be asked here: ModelFrameCount is a
        // server-side slot, and the client -- which is the side that wraps the
        // animation with it -- gets a silent -1 for its trouble.
        State.MaxFrames = Precache.ModelFrameCount(flame);
    }

    /// <summary>
    /// Sets the size the flame is heading for, and how long it may take.
    ///
    /// <para>
    /// Stock's two-argument <c>CBaseFire::Scale</c>. The client turns the
    /// duration into a pair of instants on receipt; a duration of zero means
    /// "be that size now", which is the value <c>env_fire</c> sends when it
    /// builds one and the value stock's client divided by.
    /// </para>
    /// </summary>
    public void ScaleTo(float size, float seconds)
    {
        State.Scale     = size;
        State.ScaleTime = seconds;
    }

    /// <summary>
    /// Sets the size it starts from as well, which is stock's three-argument
    /// <c>Scale</c> and is only ever used when the flame is built.
    /// </summary>
    public void ScaleFrom(float startSize, float size, float seconds)
    {
        State.StartScale = startSize;
        ScaleTo(size, seconds);
    }

    /// <summary>
    /// Lit or not. Unlike the natural flame's, this bit is read on the client:
    /// stock's <c>C_Plasma::AddEntity</c> returns without drawing when it is
    /// clear.
    /// </summary>
    public bool Active
    {
        get => State.Active;
        set => State.Active = value;
    }
}

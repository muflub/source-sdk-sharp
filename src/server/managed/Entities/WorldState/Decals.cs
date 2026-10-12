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
/// The two map decals, and the honest thing to say about both is that they are
/// a SHAPE rather than a behaviour.
///
/// <para>
/// Every one of them removes itself. An unnamed decal paints once when the map
/// has finished loading and is gone in the same breath; a named one waits for
/// an <c>Activate</c> input, sprays, and is gone a tenth of a second later.
/// There is no such thing as a live <c>infodecal</c> after the first frame, and
/// `dm_lockdown` places eighty-one of them — so the largest single class in this
/// batch by instance count contributes exactly zero live entities.
/// </para>
///
/// <para>
/// That is the same finding <c>trigger_cdaudio</c> produced and it is recorded
/// the same way: the port is worth having because the decisions in it are real
/// — which material, permanent or sprayed, low priority or not, and whether
/// deathmatch skips it — and because leaving eighty-one native registrations in
/// place to do that much is the thing this migration exists to stop.
/// </para>
///
/// <para>
/// The painting itself is one ABI call. It has to be: the engine's permanent
/// form wants the position in the local space of whatever the trace hit, with
/// that entity's index and model index, and none of the three survives a round
/// trip through a managed trace result. See PaintDecal in sourcesharp.h.
/// </para>
/// </summary>
public abstract class MapDecal : ScriptedEntity
{
    public MapDecal(IEngineEffects effects, IEnginePrecache precache, IEntityRoster<MapDecal> pending)
    {
        Effects = effects;
        Precache = precache;
        _pending = pending;
    }

    /// <summary>the small effects.</summary>
    protected readonly IEngineEffects Effects;

    /// <summary>asking for what this entity will need before the level runs.</summary>
    protected readonly IEnginePrecache Precache;
    /// <summary>
    /// Every decal the map placed that has not painted yet.
    ///
    /// <para>
    /// A registry and a pass, because the moment to paint is AFTER every entity
    /// in the map has spawned — the trace has to be able to hit a door that
    /// only reached its position during its own Spawn. Stock says this with
    /// <c>Activate</c>, which the parallel hierarchy has no equivalent of; the
    /// equivalent it does have is level init, which the shell calls once all
    /// spawning is done.
    /// </para>
    ///
    /// <para>
    /// The server module's roster, shared by every map decal; it was a static
    /// list.
    /// </para>
    /// </summary>
    private readonly IEntityRoster<MapDecal> _pending;

    /// <summary>Skips the decal in a deathmatch game. Stock's 2048.</summary>
    protected const int NotInDeathmatchFlag = 2048;

    /// <summary>How long a sprayed decal's entity outlives the spray. Stock's tenth of a second.</summary>
    private const float SprayLinger = 0.1f;

    /// <summary>What a map may set on this entity. See <see cref="MapDecalVars"/>.</summary>
    [ScriptVars] public MapDecalVars Vars = new();

    /// <summary>The engine's index for the material, or -1 if it would not load.</summary>
    protected int Texture { get; private set; } = -1;

    /// <summary>
    /// Precached at spawn rather than when the key arrives.
    ///
    /// <para>
    /// Stock precaches inside <c>KeyValue</c>, which is earlier than it needs to
    /// be and earlier than a [ScriptVars] key can be. Spawn is still during level
    /// load, which is the only requirement a precache has — an index claimed
    /// after the string tables are sent means nothing to a client.
    /// </para>
    /// </summary>
    public override void OnSpawn()
    {
        if (Vars.Texture.Length == 0)
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': no texture key, nothing to paint\n");
            Remove();
            return;
        }

        Texture = Precache.PrecacheDecal(Vars.Texture);

        if (Texture < 0)
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': can't find decal '{Vars.Texture}'\n");
            Remove();
            return;
        }

        // Stock's deathmatch opt-out. This mod is always deathmatch, so the
        // flag means "never" here rather than "sometimes" — which is worth
        // saying out loud, because a mapper reading the flag name would expect
        // a condition and there is not one.
        if ((SpawnFlags & NotInDeathmatchFlag) != 0)
        {
            Remove();
            return;
        }

        _pending.Add(this);
    }

    public override void OnRemoved() => _pending.Remove(this);

    /// <summary>
    /// Paints everything the map placed, once the map has finished loading.
    ///
    /// <para>
    /// Called from the server module's level init, which is the parallel
    /// hierarchy's <c>Activate</c>: every map entity has spawned and taken its
    /// position, and nothing has been sent to a client yet.
    /// </para>
    ///
    /// <para>
    /// The list is copied before it is walked, because an unnamed decal removes
    /// itself from inside <see cref="Paint"/> — through <c>OnRemoved</c>, into
    /// this same list.
    /// </para>
    /// </summary>
    /// <param name="pending">The decals waiting to paint: the module's roster.</param>
    public static void PaintPlaced(IEntityRoster<MapDecal> pending, ILog log)
    {
        if (pending.Count == 0)
            return;

        int painted = 0;
        int waiting = 0;

        foreach (var decal in pending.ToArray())
        {
            // A named decal is one the map intends to trigger, so it waits.
            // That is stock's rule and it is the only thing the name means
            // here: there is no other reason for a decal to have one.
            if (decal.Name.Length != 0)
            {
                waiting++;
                continue;
            }

            if (decal.Paint(permanent: true))
                painted++;

            decal.Remove();
        }

        // Said out loud because a decal leaves NOTHING to inspect afterwards.
        // The entity removes itself, the mark belongs to the engine's decal
        // table, and `debug-api ents infodecal` correctly answers zero whether
        // every one of them painted or none of them did. This line is the
        // difference between those two.
        log.Msg($"[SourceSharp/Server] map decals: {painted} painted, {waiting} waiting to be triggered\n");
    }


    /// <summary>
    /// The <c>Activate</c> input: spray it now, at whoever is connected, and go.
    ///
    /// <para>
    /// Not permanent, and the difference matters — a decal sprayed at a moment
    /// is a thing that happened, and a client joining afterwards did not see it
    /// happen. Stock draws the same line.
    /// </para>
    /// </summary>
    [ScriptedEvent("Activate")]
    private void OnActivateInput()
    {
        Paint(permanent: false);
        SetNextThink(SprayLinger);
    }

    /// <summary>The linger stock gives a sprayed decal before its entity goes.</summary>
    public override void OnThink() => Remove();

    /// <summary>Answers whether anything was actually marked.</summary>
    protected abstract bool Paint(bool permanent);
}

/// <summary>
/// infodecal: a mark stamped on whatever surface it was placed against.
///
/// <para>
/// Eighty-one of these in `dm_lockdown`, none of them named, so all eighty-one
/// paint during level init and are gone before the first frame is drawn.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.Infodecal)]
public sealed class InfoDecal : MapDecal
{
    public InfoDecal(IEngineEffects effects, IEnginePrecache precache, IEntityRoster<MapDecal> pending)
        : base(effects, precache, pending)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="InfoDecalVars"/>.</summary>
    [ScriptVars] public InfoDecalVars InfoDecalVars = new();

    protected override bool Paint(bool permanent)
        => Effects.PaintDecal(Texture, Origin, angles: null, projectDistance: 0f,
                             permanent: permanent, lowPriority: InfoDecalVars.LowPriority);
}

/// <summary>
/// info_projecteddecal: a mark thrown along a facing rather than stamped where
/// it stands.
///
/// <para>
/// The difference from <see cref="InfoDecal"/> is that this one needs no trace
/// on our side at all — the engine's own projection finds the surface, which is
/// what makes it work against a prop that <c>infodecal</c>'s filter would have
/// refused.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.InfoProjecteddecal)]
public sealed class InfoProjectedDecal : MapDecal
{
    public InfoProjectedDecal(IEngineEffects effects, IEnginePrecache precache, IEntityRoster<MapDecal> pending)
        : base(effects, precache, pending)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="InfoProjectedDecalVars"/>.</summary>
    [ScriptVars] public InfoProjectedDecalVars InfoProjectedDecalVars = new();

    protected override bool Paint(bool permanent)
        => Effects.PaintDecal(Texture, Origin, InfoProjectedDecalVars.Angles, InfoProjectedDecalVars.Distance,
                             permanent: permanent, lowPriority: false);
}

/// <summary>
/// The map keys <see cref="MapDecal"/> declares.
/// </summary>
[ScriptVars]
public partial class MapDecalVars
{
    public string Texture { get; set; } = string.Empty;
}

/// <summary>
/// The map keys <see cref="InfoDecal"/> declares.
/// </summary>
[ScriptVars]
public partial class InfoDecalVars
{
    /// <summary>
    /// Lets the client reuse the decal slot in preference to keeping it.
    ///
    /// <para>
    /// Stock's comment is the explanation: it is deliberately NOT marked
    /// permanent on the client, so a busy firefight can overwrite a scenery
    /// mark rather than run out of decals. A map uses it for background grime.
    /// </para>
    /// </summary>
    public bool LowPriority { get; set; }
}

/// <summary>
/// The map keys <see cref="InfoProjectedDecal"/> declares.
/// </summary>
[ScriptVars]
public partial class InfoProjectedDecalVars
{
    public Vector Angles { get; set; }

    /// <summary>How far along the facing to look for something to mark.</summary>
    public float Distance { get; set; }
}

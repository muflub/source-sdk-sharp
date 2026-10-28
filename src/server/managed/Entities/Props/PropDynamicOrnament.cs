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
/// A decoration that rides on another entity: the ornament on the tree, the
/// badge on the machine. It has no life of its own — it is invisible and
/// insubstantial until something is named to carry it, and it goes back to being
/// invisible when that thing lets go.
///
/// <para>
/// THIS IS THE ENTITY THE MISSING PARENTING COST. props.cpp gave up its
/// classname rather than port it, with a comment saying so in as many words:
/// "prop_dynamic_ornament is a prop_dynamic that parents itself to another
/// entity by name, and the managed hierarchy has no parenting at all — porting
/// it without one would silently produce an ornament that never attaches, which
/// is worse than not answering the name." It is the only guard in that file whose
/// comment says <i>given up</i> rather than <i>moved</i>.
/// </para>
///
/// <para>
/// WHAT IS FAITHFUL. Invisible and non-solid until attached, which is stock's
/// <c>DetachFromOwner</c> and is why an unattached one cannot be seen in a map.
/// The <c>InitialOwner</c> keyvalue, the <c>SetAttached</c> and <c>Detach</c>
/// inputs, and re-attaching to something else while already attached.
/// </para>
///
/// <para>
/// WHAT DIVERGES, and it is one thing. Stock's <c>FollowEntity</c> is two
/// operations — <c>SetParent</c> and <c>EF_BONEMERGE</c> — and only the first is
/// the server's to decide. Bone merging is a rendering question, so on this port
/// it lives on the client and is driven from the networked
/// <see cref="OrnamentNetState.AttachedToIndex"/> below rather than from an
/// effect flag. An ornament on a model that does not animate is identical either
/// way; one on a model that does now follows the bones rather than the origin,
/// which is the behaviour stock's flag was for.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PropDynamicOrnament, ScriptedEntityKind.Prop)]
public sealed class PropDynamicOrnament : ScriptedEntity
{
    public PropDynamicOrnament(IEnginePrecache precache, IEngineEntityRender render, IServerPropData propData)
    {
        Precache = precache;
        Render = render;
        PropDataTable = propData;
    }

    private readonly IEnginePrecache Precache;
    private readonly IEngineEntityRender Render;
    private readonly IServerPropData PropDataTable;

    /// <summary>What a map may set. See <see cref="PropDynamicOrnamentVars"/>.</summary>
    [ScriptVars] public PropDynamicOrnamentVars Vars = new();

    /// <summary>What the client needs to draw this on the right bones.</summary>
    [NetState] public OrnamentNetState State = new();

    public override void OnSpawn()
    {
        // COrnamentProp is a CDynamicProp: CBaseProp::Spawn's refusals apply.
        if (DynamicPropSpawn.Refusal(ClassName, Vars.Model, Precache, Render, PropDataTable) is { } refusal)
        {
            Log.Msg(DynamicPropSpawn.Line(ClassName, SpawnOrigin, refusal));
            Remove();
            return;
        }

        SetModel(Vars.Model);

        // Solid-but-never-simulated, so the shell builds the right kind of
        // physics object moments from now. It is made non-solid immediately
        // below; this decides which KIND of object exists to be made non-solid.
        MakePhysicsStatic();

        Detach();

        // THE ATTACH IS DEFERRED BY A FRAME, and that is stock's shape rather
        // than a convenience. COrnamentProp attaches in Activate(), not Spawn(),
        // because the entity it names may not have been parsed yet — a map
        // writes its entities in whatever order Hammer wrote them, and half of
        // them do not exist when the first one spawns.
        //
        // This hierarchy has no Activate, so the one-shot think does the same
        // job: a think scheduled at spawn runs on the first frame, by which time
        // the whole map has been parsed and every name can be found. The same
        // reason StandaloneHierarchy_ResolveParentNames is a pass of its own.
        if (Vars.InitialOwner.Length > 0)
            SetNextThink(0.0f);
    }

    public override void OnThink()
    {
        // One shot. Nothing reschedules, so an ornament whose InitialOwner does
        // not exist complains once rather than once a frame for the map's life.
        AttachTo(Vars.InitialOwner);
    }

    [ScriptedEvent] private void OnSetAttached(string target) => AttachTo(target);

    [ScriptedEvent] private void OnDetach() => Detach();

    //-------------------------------------------------------------------------
    // Ride on the named entity.
    //
    // The parent may be on either hierarchy and this does not care which — a
    // map's own func_door and a managed prop are both things an ornament can
    // hang on, and SetParent resolves the name through whichever holds it.
    //-------------------------------------------------------------------------
    private void AttachTo(string target)
    {
        if (target.Length == 0)
            return;

        // Looked up HERE rather than left to SetParent(string), because the
        // entindex has to go on the wire for the client's bone merge and the
        // handle is what carries it. One lookup, two uses.
        var parent = Entities.FindByName(target);

        // Compared with None rather than asked IsValid, and the difference is
        // one engine round trip: FindByName has just resolved this handle, so
        // IsValid would resolve it a second time to learn what the first call
        // already answered. None is what FindByName returns for a name nothing
        // carries.
        if (parent == EntityHandle.None)
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': nothing is called '{target}', so it stays detached\n");
            return;
        }

        if (!SetParent(parent))
        {
            // A refusal is already explained on the console by the hierarchy —
            // a cycle, or an entity that has not spawned. Staying detached is
            // the safe half: an ornament nobody can see is a smaller wrong than
            // one standing in mid-air where its parent used to be.
            return;
        }

        // FollowEntity: on the parent's own origin and facing, where the map's
        // placement is only where the ornament waited to be attached. Stock's
        // stands at the owner's origin; this one stood 48 units above it on
        // ss_sandbox's door (2026-10-26).
        Entities.SetLocalTransform(Handle, Vector.Zero, Vector.Zero);

        // Visible only once it is actually riding something, which is the whole
        // of stock's RemoveEffects(EF_NODRAW) in AttachTo — and it is on this
        // side of the SetParent for a reason: an ornament made visible before a
        // link that then failed would be left standing in the map, which is
        // exactly the "silently produce an ornament that never attaches" the
        // props.cpp comment refused to ship.
        SetVisible(true);

        // What the client draws it against. An ENTINDEX rather than a handle,
        // for the reason every other cross-wire entity reference here uses one:
        // an index means the same thing on both sides without the two having to
        // agree about serial numbers.
        State.AttachedToIndex = Entities.GetIndex(parent);
    }

    //-------------------------------------------------------------------------
    // Stock's DetachFromOwner, and the order matters as much as the contents.
    //
    // It is also the spawn state: an ornament that has never been attached is
    // in exactly the condition of one that has been let go.
    //-------------------------------------------------------------------------
    private void Detach()
    {
        ClearParent();

        // Told BEFORE it stops being drawn, so the client is not briefly asked
        // to bone-merge onto an entity this one no longer follows.
        State.AttachedToIndex = -1;

        SetVisible(false);

        // Non-solid as well as invisible. Stock's AddSolidFlags(FSOLID_NOT_SOLID),
        // and without it a detached ornament is an invisible wall — the classic
        // symptom, and one a player finds long before a mapper does.
        SetSolidNone(SolidFlags.NotSolid);
    }
}

/// <summary>What a map may set on a <see cref="PropDynamicOrnament"/>.</summary>
[ScriptVars]
public partial class PropDynamicOrnamentVars
{
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The entity to ride, named at map time. Stock's <c>InitialOwner</c>,
    /// spelled as stock spells it because that is what existing maps write.
    /// </summary>
    public string InitialOwner { get; set; } = string.Empty;
}

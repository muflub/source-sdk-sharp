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
/// Declares that two named entities never collide with each other.
///
/// The exclusion itself is a per-pair entry in the solver's collision hash and
/// has to be an engine call — but everything that decides whether to make one
/// lives here: which two entities, whether the map wanted it from the start, and
/// whether an input is asking for a state it is already in.
/// </summary>
[ScriptedEntity(EntityNames.LogicCollisionPair)]
public sealed class LogicCollisionPair : ScriptedEntity
{
    public LogicCollisionPair(EntityPairCollisions pairs)
    {
        Pairs = pairs;
    }

    /// <summary>The entity pairs the solver refuses (stock's g_EntityCollisionHash).</summary>
    private readonly EntityPairCollisions Pairs;
    /// <summary>What a map may set on this entity. See <see cref="LogicCollisionPairVars"/>.</summary>
    [ScriptVars] public LogicCollisionPairVars Vars = new();

    /// <summary>
    /// Whether the pair was actually formed, which is not the same as whether
    /// collisions are disabled: an attach naming an entity with no physics gives
    /// nothing to key the hash on. Stock keeps the same flag, and both inputs
    /// read it — an input that repeats the state it already achieved does
    /// nothing, but one that never took effect is retried.
    /// </summary>
    private bool _succeeded;

    /// <summary>
    /// Deferred to the first think, exactly as the constraint entities do it:
    /// the two ends are named, and a name cannot be resolved while the map is
    /// still being parsed.
    /// </summary>
    public override void OnSpawn()
    {
        if (Vars.Disabled)
            SetNextThink(0f);
    }

    public override void OnThink()
    {
        StopThinking();
        Apply(false);
    }

    [ScriptedEvent]
    private void OnDisableCollisions()
    {
        if (_succeeded && Vars.Disabled)
            return;

        Apply(false);
    }

    [ScriptedEvent]
    private void OnEnableCollisions()
    {
        if (_succeeded && !Vars.Disabled)
            return;

        Apply(true);
    }

    private void Apply(bool enable)
    {
        _succeeded = Pairs.SetEnabled(Resolve(Vars.Attach1), Resolve(Vars.Attach2), enable);

        if (_succeeded)
            Vars.Disabled = !enable;
    }

    /// <summary>
    /// An empty name is the world and is not a lookup failure. A name that was
    /// given and does not resolve is a map error and says so, because the pair
    /// then silently becomes "this thing and the world" — which would disable
    /// exactly the collision the mapper was relying on.
    /// </summary>
    private EntityHandle Resolve(string name)
    {
        if (string.IsNullOrEmpty(name))
            return EntityHandle.None;

        EntityHandle found = Entities.FindByName(name);

        if (!Entities.IsValid(found))
            Log.Warning($"[SourceSharp] logic_collision_pair '{Name}': no entity named '{name}'\n");

        return found;
    }
}

/// <summary>
/// The map keys <see cref="LogicCollisionPair"/> declares.
/// </summary>
[ScriptVars]
public partial class LogicCollisionPairVars
{
    /// <summary>
    /// The two entities to separate. Either may be left empty, which means the
    /// world — that is how a map drops something through the floor.
    /// </summary>
    public string Attach1 { get; set; } = string.Empty;

    public string Attach2 { get; set; } = string.Empty;

    [VarName("startdisabled")]
    public bool Disabled { get; set; }
}

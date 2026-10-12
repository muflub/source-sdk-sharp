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
/// Rewrites the "target" key of every entity with a given name, on demand.
///
/// The one entity in this file that edits ANOTHER entity's map data rather than
/// its state. A point_teleport moves something; this reaches into what something
/// is POINTING AT and changes it, and then lets whatever reads that key notice on
/// its own schedule. Nothing is told; the next reader gets the new answer.
///
/// The stock comment on the class is "this is a really bad idea", and it is
/// right about why: a keyvalue is normally load-time data, so an entity that
/// caches what its target resolved to at spawn will not see this at all, and one
/// that re-resolves every think will. Which of those an entity is, is an
/// implementation detail a mapper cannot see. That is a property of the design,
/// not of the port, and it is carried over rather than fixed — a map that works
/// because a light re-resolves its target every 0.1s must keep working.
///
/// Its living reader in this build is <c>CDynamicLight::DynamicLightThink</c>,
/// which calls GetNextTarget() every tick and aims at whatever comes back; the
/// port was verified by swinging a light_dynamic from one info_target to another
/// and reading the angles.
/// </summary>
[ScriptedEntity(EntityNames.AiChangetarget)]
public sealed class AiChangeTarget : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="AiChangeTargetVars"/>.</summary>
    [ScriptVars] public AiChangeTargetVars Vars = new();

    /// <summary>
    /// The name a map writes to mean "whatever set this off".
    ///
    /// Resolved here rather than by the search, because the search has no
    /// activator to resolve it against — <c>FindEntityByName</c> takes a name
    /// and nothing else. Stock hands its own activator and caller down into the
    /// engine's version, which is where "!activator" and "!caller" are
    /// understood.
    /// </summary>
    private const string ActivatorName = "!activator";

    /// <summary>The name a map writes to mean the entity doing the asking.</summary>
    private const string SelfName = "!self";

    [ScriptedEvent("Activate")]
    private void OnActivate(InputData input)
    {
        if (Vars.Target.Length == 0)
        {
            // Stock searches for the empty string, which matches nothing, and
            // says nothing about it. An ai_changetarget with no target is an
            // entity that can only ever do nothing, and the only way to find
            // that out in stock is to fire it and watch for the absence of an
            // effect.
            Log.Warning($"[SourceSharp] ai_changetarget '{Name}': fired with no target to retarget\n");
            return;
        }

        foreach (var victim in Victims(input.Activator))
        {
            if (!Entities.IsValid(victim))
                continue;

            // Through KeyValue rather than by writing a field, and this is the
            // decision the whole port turns on. "target" is
            // CBaseEntity::m_target on a stock entity and a [ScriptVars] key
            // on one of ours; KeyValue is the only channel that reaches both,
            // because each side resolves the name its own way and neither has
            // to be identified from here.
            if (Entities.SetKeyValue(victim, "target", Vars.NewTarget))
                continue;

            // Nothing claimed it. A CBaseEntity always does — "target" is in
            // its datadesc — so this is an entity on the parallel hierarchy
            // whose class never declared the key, and retargeting it is a
            // request with no meaning rather than a request that failed.
            Log.Warning(
                $"[SourceSharp] ai_changetarget '{Name}': '{Entities.GetName(victim)}' has no 'target' to change\n");
        }
    }

    /// <summary>
    /// Everything the input should reach.
    ///
    /// Both hierarchies, deliberately — see docs/divergences.md. The engine's
    /// FindEntityByName searches only CBaseEntity; this build's searches the
    /// parallel hierarchy as well, and taking that reach is the choice that
    /// keeps the OTHER ports visible. Roughly a hundred and seventy classes
    /// have moved off CBaseEntity, and a retargeter that could not see them
    /// would have turned every one of those ports into a place where
    /// ai_changetarget quietly stopped working.
    /// </summary>
    private List<EntityHandle> Victims(EntityHandle activator)
    {
        if (Vars.Target == ActivatorName)
            return Entities.IsValid(activator) ? [activator] : [];

        if (Vars.Target == SelfName)
            return [Handle];

        return Entities.FindAllByName(Vars.Target);
    }
}

/// <summary>
/// The map keys <see cref="AiChangeTarget"/> declares.
/// </summary>
[ScriptVars]
public partial class AiChangeTargetVars
{
    /// <summary>
    /// Who gets rewritten — matched against targetname, not against target.
    ///
    /// Worth stating because the entity has two "target"s in play and they mean
    /// opposite things: THIS one selects the victims, and the one written into
    /// them is <see cref="AiChangeTargetVars.NewTarget"/>.
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// What their "target" becomes.
    ///
    /// The key really is spelt with the member's own prefix. It is an old Valve
    /// slip that shipped in every map that uses this entity, so the FGD, the
    /// maps and the datadesc all agree on "m_iszNewTarget" and correcting it
    /// here would only stop those maps working.
    /// </summary>
    [VarName("m_iszNewTarget")]
    public string NewTarget { get; set; } = string.Empty;
}

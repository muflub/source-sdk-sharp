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
/// env_entity_igniter: sets something else on fire.
///
/// The division here is the one the whole effects phase turns on. What BURNS is
/// an effect — a networked entity with a client-side renderer — and none of that
/// is a decision. What is a decision is which entity, and for how long, and
/// that is the whole of this class.
/// </summary>
[ScriptedEntity(EntityNames.EnvEntityIgniter)]
public sealed class EnvEntityIgniter : ScriptedEntity
{
    public EnvEntityIgniter(Flames flames)
    {
        Flames = flames;
    }

    /// <summary>
    /// setting things alight.
    ///
    /// <para>
    /// NOT <c>IEngineDamage.Ignite</c> any more, and the reason is the whole of
    /// this commit. That slot resolved the target to a <c>CBaseEntity</c> and
    /// handed it to <c>CEntityFlame::Create</c>, which parents a native flame to
    /// it — so it could only ever burn something on the SDK's hierarchy, and the
    /// last prop on that hierarchy became C# some time ago. Every ignite in this
    /// mod was a silent no-op, and the sandbox's burn station demonstrated the
    /// refusal. The flame is <c>ss_entityflame</c> now and parents to either
    /// kind; the engine slot is retired.
    /// </para>
    /// </summary>
    private readonly Flames Flames;
    /// <summary>What a map may set on this entity. See <see cref="EnvEntityIgniterVars"/>.</summary>
    [ScriptVars] public EnvEntityIgniterVars Vars = new();

    [ScriptedEvent("Ignite")]
    private void OnIgnite()
    {
        if (Vars.Target.Length == 0)
            return;

        // Every entity of that name, not the first: a map naming a row of
        // barrels means the row, and stock's loop walks the whole list.
        using NameMatches victims = NameLookup.Named(Vars.Target);

        if (victims.Count == 0)
        {
            Log.Warning($"[SourceSharp] env_entity_igniter '{Name}': nothing named '{Vars.Target}' to set alight\n");
            return;
        }

        foreach (EntityHandle victim in victims)
            Flames.Ignite(victim, Vars.Lifetime);
    }
}

/// <summary>
/// The map keys <see cref="EnvEntityIgniter"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvEntityIgniterVars
{
    /// <summary>Name of what to set alight.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Seconds it burns. Zero burns until something puts it out.</summary>
    public float Lifetime { get; set; }
}

using System.Collections.Generic;
using Debug.Api.Server.Interfaces;

namespace Debug.Api;

/// <summary>
/// How a written target resolves to the entities that will hear it.
///
/// <para>
/// The console's own rule, which the development API did not have. An
/// <c>ent_fire</c> target is a NAME first and a CLASSNAME second, and it
/// addresses EVERY entity that answers -- that is what makes
/// <c>ent_fire func_button Use</c> and six lights sharing one targetname work.
/// The API resolved a single entity by name and answered "no entity named" for
/// anything else, so <c>./debug-api fire trigger_multiple Disable</c> was refused
/// against forty-seven live triggers.
/// </para>
///
/// <para>
/// Both hierarchies, for both searches. The engine's finders behind
/// <see cref="IEngineEntities"/> already walk the CBaseEntity list and the
/// parallel one -- so this needs no hierarchy question of its own, and would be
/// wrong if it asked one: which half an entity lives on is not something a
/// person typing a name knows or should have to.
/// </para>
///
/// <para>
/// Separated from the HTTP router so it can be tested. The router reaches the
/// engine through a static holder, which is honest for a tool and useless for a
/// test; this takes the engine it is given.
/// </para>
/// </summary>
internal static class EntityTargets
{
    /// <summary>How a target was matched, for the answer to say.</summary>
    public enum MatchKind
    {
        /// <summary>Nothing answers to it, under either reading.</summary>
        None,

        /// <summary>One or more entities carry it as their targetname.</summary>
        Name,

        /// <summary>Nothing is called that, but entities of that class exist.</summary>
        ClassName,
    }

    /// <summary>
    /// Every entity a written target addresses, in the console's order.
    ///
    /// <para>
    /// Name before class, and class only when the name matched NOTHING -- not
    /// both at once. That is <c>CEventQueue::ServiceEvents</c>'s own order and
    /// it matters: a map that names one <c>func_button</c> "func_button" would
    /// otherwise fire every button in the level.
    /// </para>
    /// </summary>
    public static List<uint> Resolve(IDebugWorld entities, string? target,
                                             out MatchKind matched)
    {
        matched = MatchKind.None;

        if (string.IsNullOrWhiteSpace(target))
            return [];

        var byName = entities.FindAllByName(target);
        if (byName.Count > 0)
        {
            matched = MatchKind.Name;
            return [.. byName];
        }

        var byClass = new List<uint>(entities.FindAllByClassname(target));
        if (byClass.Count > 0)
        {
            matched = MatchKind.ClassName;
            return byClass;
        }

        return [];
    }
}

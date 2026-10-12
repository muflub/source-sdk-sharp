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
/// Combines other filters. The one filter that asks other filters, which is why
/// it looks them up by name like everything else does.
/// </summary>
[ScriptedEntity(EntityNames.FilterMulti)]
public sealed class FilterMulti : FilterBase
{
    private const int MaxFilters = 5;

    /// <summary>What a map may set on this entity. See <see cref="FilterMultiVars"/>.</summary>
    [ScriptVars] public FilterMultiVars FilterMultiVars = new();

    private const int RequireAny = 1;

    protected override bool Matches(EntityHandle entity)
        => Combine(entity, damageType: 0, damage: false);

    // The same walk over the same members, asking them the other question. One
    // walk rather than two, because which question is asked is the only
    // difference and stock has the same shape — CFilterMultiple::PassesDamage-
    // FilterImpl is its entity walk with one call swapped.
    protected override bool MatchesDamage(DamageType damageType, EntityHandle attacker)
        => Combine(attacker, damageType, damage: true);

    private bool Combine(EntityHandle subject, DamageType damageType, bool damage)
    {
        bool any = false;
        bool sawOne = false;

        foreach (string name in new[] { FilterMultiVars.Filter01, FilterMultiVars.Filter02, FilterMultiVars.Filter03, FilterMultiVars.Filter04, FilterMultiVars.Filter05 })
        {
            if (name.Length == 0)
                continue;

            var filter = FindByName<FilterBase>(name);

            // A named filter that is not there is a mapping mistake. Skipped
            // rather than treated as a failure, because failing would make one
            // typo silently reject everything.
            if (filter is null)
                continue;

            sawOne = true;
            bool passed = damage ? filter.PassesDamage(damageType, subject) : filter.Passes(subject);

            if (FilterMultiVars.FilterType == RequireAny)
            {
                any |= passed;
            }
            else if (!passed)
            {
                return false;
            }
        }

        // With nothing to consult there is no reason to reject anybody.
        if (!sawOne)
            return true;

        return FilterMultiVars.FilterType == RequireAny ? any : true;
    }
}

/// <summary>
/// The map keys <see cref="FilterMulti"/> declares.
/// </summary>
[ScriptVars]
public partial class FilterMultiVars
{
    /// <summary>0 requires all of them, 1 requires any.</summary>
    public int FilterType { get; set; }

    public string Filter01 { get; set; } = string.Empty;

    public string Filter02 { get; set; } = string.Empty;

    public string Filter03 { get; set; } = string.Empty;

    public string Filter04 { get; set; } = string.Empty;

    public string Filter05 { get; set; } = string.Empty;
}

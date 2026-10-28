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
/// info_ladder_dismount: a place a player is slid to when getting off a
/// func_useableladder (stock's <c>CInfoLadderDismount</c>,
/// game/shared/func_ladder.cpp). A ladder claims the ones near it when it
/// activates; one with a <c>target</c> is claimed only by the ladder it names.
///
/// <para>
/// Networked, as stock's is, because the client's prediction dismounts too:
/// the client claims every point near a ladder, target or not, which is
/// stock's client arm.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.InfoLadderDismount, ScriptedEntityKind.Effect)]
public sealed class InfoLadderDismount : ScriptedEntity
{
    public InfoLadderDismount(UseableLadders ladders)
    {
        Ladders = ladders;
    }

    private readonly UseableLadders Ladders;

    /// <summary>What a map may set on this entity. See <see cref="InfoLadderDismountVars"/>.</summary>
    [ScriptVars] public InfoLadderDismountVars Vars = new();

    [NetState] public LadderDismountNetState State = new();

    public override void OnSpawn()
    {
        State.Origin = Origin;
        Ladders.AddDismount(this);
    }

    /// <summary>A hot reload rebuilt this point from its saved state: it joins the new composition's ladders.</summary>
    public override void LoadState(SavedEntity state)
    {
        base.LoadState(state);
        Ladders.AddDismount(this);
    }

    public override void OnRemoved() => Ladders.RemoveDismount(this);
}

/// <summary>The map keys <see cref="InfoLadderDismount"/> declares.</summary>
[ScriptVars]
public partial class InfoLadderDismountVars
{
    /// <summary>The ladder this point belongs to, by name; empty for any ladder near it.</summary>
    [VarName("target")]
    public string Target { get; set; } = string.Empty;
}

//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Server.Entities.Items;
using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Server.Entities.Triggers;

/// <summary>
/// What tf_triggers.cpp's player triggers share over the core volume: their
/// work is stock's <c>StartTouch</c>/<c>EndTouch</c>, once per entry and
/// exit and never behind the volume's re-fire wait.
/// </summary>
public abstract class TFPlayerTrigger(IGameClock clock) : TriggerVolume(clock)
{
    /// <summary>The entry has no work of its own: <see cref="StartTouch"/> carries it.</summary>
    protected sealed override void Fire(EntityHandle toucher) { }

    /// <summary>Stock's <c>StartTouch</c>, for an entry the volume accepted.</summary>
    protected abstract void StartTouch(EntityHandle toucher);

    /// <summary>Stock's <c>EndTouch</c>, for one that was inside.</summary>
    protected virtual void EndTouch(EntityHandle toucher) { }

    public override void OnTouch(EntityHandle toucher)
    {
        bool wasInside = Inside.Contains(toucher);
        base.OnTouch(toucher);
        if (!wasInside && Inside.Contains(toucher))
            StartTouch(toucher);
    }

    public override void OnEndTouch(EntityHandle toucher)
    {
        bool wasInside = Inside.Contains(toucher);
        base.OnEndTouch(toucher);
        if (wasInside)
            EndTouch(toucher);
    }

    /// <summary>The map's <c>condition</c> key as a TF condition, or null for <c>TF_COND_INVALID</c> or one out of range.</summary>
    protected static TFCond? Condition(int value)
        => value >= 0 && value < (int)TFCond.Last ? (TFCond)value : null;
}

/// <summary>
/// trigger_add_tf_player_condition (stock's <c>CTriggerAddTFPlayerCondition</c>,
/// tf_triggers.cpp): a player entering gets the condition for its duration;
/// a permanent one (-1) is taken away again on the way out.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class TriggerAddTFPlayerCondition(IGameClock clock, IEngineEntities entities, ITFMapPlayers players)
    : TFPlayerTrigger(clock)
{
    public const string MapClass = "trigger_add_tf_player_condition";

    [ScriptVars] public TFConditionTriggerVars ConditionVars = new();

    protected override void StartTouch(EntityHandle toucher)
    {
        if (!entities.IsPlayer(toucher))
            return;

        if (Condition(ConditionVars.Condition) is not { } cond)
        {
            Log.Warning($"Invalid Condition ID [{ConditionVars.Condition}] in trigger {Name}\n");
            return;
        }

        players.AddCond(toucher, cond, ConditionVars.Duration);
    }

    protected override void EndTouch(EntityHandle toucher)
    {
        if (ConditionVars.Duration != TFConditions.Permanent)
            return;

        if (Disabled || !Passes(toucher) || !entities.IsPlayer(toucher))
            return;

        if (Condition(ConditionVars.Condition) is not { } cond)
        {
            Log.Warning($"Invalid Condition ID [{ConditionVars.Condition}] in trigger {Name}\n");
            return;
        }

        players.RemoveCond(toucher, cond);
    }
}

/// <summary>
/// trigger_remove_tf_player_condition (stock's
/// <c>CTriggerRemoveTFPlayerCondition</c>, tf_triggers.cpp): a player entering
/// loses the condition, or every condition when the key is
/// <c>TF_COND_INVALID</c> (-1).
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class TriggerRemoveTFPlayerCondition(IGameClock clock, IEngineEntities entities, ITFMapPlayers players)
    : TFPlayerTrigger(clock)
{
    public const string MapClass = "trigger_remove_tf_player_condition";

    [ScriptVars] public TFConditionTriggerVars ConditionVars = new();

    protected override void StartTouch(EntityHandle toucher)
    {
        if (!entities.IsPlayer(toucher))
            return;

        if (ConditionVars.Condition == (int)TFCond.Invalid)
        {
            players.RemoveAllCond(toucher);
            return;
        }

        if (Condition(ConditionVars.Condition) is { } cond)
            players.RemoveCond(toucher, cond);
        else
            Log.Warning($"Invalid Condition ID [{ConditionVars.Condition}] in trigger {Name}\n");
    }
}

/// <summary>The map keys the condition triggers declare (tf_triggers.cpp's datadescs).</summary>
[ScriptVars]
public partial class TFConditionTriggerVars
{
    /// <summary><c>m_nCondition</c>; stock's default is the zeroed field, condition 0.</summary>
    [VarName("condition")] public int Condition { get; set; }

    /// <summary><c>m_flDuration</c>: seconds, or -1 (PERMANENT_CONDITION) for while inside.</summary>
    [VarName("duration")] public float Duration { get; set; }
}

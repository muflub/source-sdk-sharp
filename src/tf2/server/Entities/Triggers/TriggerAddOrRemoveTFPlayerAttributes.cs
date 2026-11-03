//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared.Econ;

namespace TF2.Server.Entities.Triggers;

/// <summary>
/// trigger_add_or_remove_tf_player_attributes (stock's
/// <c>CTriggerAddOrRemoveTFPlayerAttributes</c>, tf_triggers.cpp:873-1010):
/// a player entering has the named attribute added to their runtime list for
/// the <c>value</c> and <c>duration</c> keys, or — with <c>add_or_remove</c>
/// set — has it removed. The way out removes only an added permanent one: a
/// timed attribute expires on the player's own clock
/// (<see cref="TFPlayerAttributes.Tick"/>), and a removal entry has nothing
/// to undo.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class TriggerAddOrRemoveTFPlayerAttributes(IGameClock clock, IEngineEntities entities, ItemSchema schema,
                                                        TFPlayerCarriers carriers)
    : TFPlayerTrigger(clock)
{
    public const string MapClass = "trigger_add_or_remove_tf_player_attributes";

    [ScriptVars] public TriggerAddOrRemoveTFPlayerAttributesVars AttributeVars = new();

    /// <summary><c>m_bValidAttribute</c>: the spawn-time check gates both arms.</summary>
    private bool _validAttribute;

    public override void OnSpawn()
    {
        base.OnSpawn();

        _validAttribute = false;
        if (AttributeVars.AttributeName.Length == 0)
            return;

        if (schema.Attribute(AttributeVars.AttributeName) is not null)
        {
            _validAttribute = true;
        }
        else
        {
            Log.Warning($"Invalid attribute name '{AttributeVars.AttributeName}' from trigger {MapClass} name '{Name}'\n");
        }
    }

    /// <summary><c>CTriggerAddOrRemoveTFPlayerAttributes::StartTouch</c> (:925-964).</summary>
    protected override void StartTouch(EntityHandle toucher)
    {
        if (!_validAttribute || !entities.IsPlayer(toucher))
            return;

        if (carriers.Part<TFPlayerAttributes>(toucher.EntryIndex) is not { } attributes)
            return;

        if (AttributeVars.Remove)
            attributes.Remove(AttributeVars.AttributeName);
        else
            attributes.Add(AttributeVars.AttributeName, AttributeVars.Value, AttributeVars.Duration);
    }

    /// <summary><c>CTriggerAddOrRemoveTFPlayerAttributes::EndTouch</c> (:967-1010).</summary>
    protected override void EndTouch(EntityHandle toucher)
    {
        if (Disabled || !_validAttribute)
            return;

        // Only run auto remove on added attribute (:979); ignore a timed one,
        // it removes itself (:985).
        if (AttributeVars.Remove || AttributeVars.Duration > 0f)
            return;

        if (!Passes(toucher) || !entities.IsPlayer(toucher))
            return;

        carriers.Part<TFPlayerAttributes>(toucher.EntryIndex)?.Remove(AttributeVars.AttributeName);
    }
}

/// <summary>The map keys the attribute trigger declares (tf_triggers.cpp:893-898's datadesc).</summary>
[ScriptVars]
public partial class TriggerAddOrRemoveTFPlayerAttributesVars
{
    /// <summary><c>m_bRemove</c>: take the attribute away on entry instead of adding it.</summary>
    [VarName("add_or_remove")] public bool Remove { get; set; }

    /// <summary><c>m_iszAttributeName</c>: the attribute definition's name.</summary>
    [VarName("attribute_name")] public string AttributeName { get; set; } = string.Empty;

    /// <summary><c>m_flAttributeValue</c>: the value the attribute takes.</summary>
    [VarName("value")] public float Value { get; set; }

    /// <summary><c>m_flDuration</c>: seconds, or not positive for while the player is in the volume.</summary>
    [VarName("duration")] public float Duration { get; set; }
}

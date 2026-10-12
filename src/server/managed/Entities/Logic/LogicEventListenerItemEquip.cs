using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A logic_eventlistener wired to one event, item_equip, with two extra filters
/// for which weapon was drawn.
///
/// Stock is a subclass that reimplements its parent line for line rather than
/// overriding anything — same keys, same inputs, same output, one hardcoded event
/// name. This is the subclass it should have been: the event name is the only
/// thing it decides.
///
/// **Nothing in this build fires item_equip.** The event is not declared in
/// SourceSharp.Shared/GameEvents.cs and no code in src/game raises it either, so
/// the entity resolves, accepts its keys, answers Enable and Disable, and never
/// reports. That is what stock does here too, for the same reason.
///
/// A caution for whoever declares item_equip one day: the two keys below are
/// carried but not consulted, because the feed hands a listener a name, a userid
/// and a team — not the weapon. Declaring the event without also carrying "item"
/// and "weptype" through would turn a filtered listener into an unfiltered one,
/// which is worse than one that never fires.
/// </summary>
[ScriptedEntity(EntityNames.LogicEventlistenerItemequip)]
public sealed class LogicEventListenerItemEquip : LogicEventListener
{
    public LogicEventListenerItemEquip(IEngineGameEvents events, IGameEventFeed feed)
        : base(events, feed)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="LogicEventListenerItemEquipVars"/>.</summary>
    [ScriptVars] public LogicEventListenerItemEquipVars LogicEventListenerItemEquipVars = new();

    protected override string Listens => "item_equip";
}

/// <summary>
/// The map keys <see cref="LogicEventListenerItemEquip"/> declares.
/// </summary>
[ScriptVars]
public partial class LogicEventListenerItemEquipVars
{
    /// <summary>Only report this weapon, by class name. Empty means any.</summary>
    public string WeaponClassname { get; set; } = string.Empty;

    /// <summary>Only report this weapon type. Below zero means any.</summary>
    public int WeaponType { get; set; } = -1;
}

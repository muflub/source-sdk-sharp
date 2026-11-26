//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Ai.TF2.Senses;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// A friendly dispenser as a heal place (round 2's S8): a hurt bot in a lull goes to one it sees, and stands at it until it is
/// full (stock's CTFBotGetHealth, tf_bot_get_health.cpp). The ammo errand's dispensers are TFDecider.Ammo.cs's.
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>A friendly dispenser seen within this long is "in sight" for a hurt bot (round 2's S8): a glance away does not lose it.</summary>
    public const float DispenserHealSeconds = 3f;

    /// <summary>
    /// The bot stands this near a dispenser it heals at, inside the dispenser's reach (TFDispenser's <c>DispenserRadius</c>,
    /// stock's 64), and is at it within twice that reach.
    /// </summary>
    public const float DispenserStandUnits = 40f;

    /// <summary>The verdict of a decide standing at a dispenser for health.</summary>
    public const string DispenserVerdict = "at a dispenser";

    /// <summary>
    /// The stand at a dispenser ends when the bot's health has not risen in this long (ours: a dispenser gives health every
    /// second; this ends the stand of a kit whose most is under the class's, as stock's own GetMaxHealth would, and of a
    /// dispenser that heals nobody).
    /// </summary>
    public const float DispenserStallSeconds = 3f;

    private bool _healAtDispenser, _dispenserStanding;
    private int _dispenserHealth;
    private Tick _dispenserRose;

    /// <summary>Notes the bot's health at the dispenser it stands at: when it last rose (the stand's start counts as a rise).</summary>
    private void NoteDispenserHealth(bool standing)
    {
        if (standing && (!_dispenserStanding || _self.Health > _dispenserHealth))
            _dispenserRose = _now;
        (_dispenserStanding, _dispenserHealth) = (standing, _self.Health);
    }

    /// <summary>
    /// The nearest friendly dispenser the bot saw within <see cref="DispenserHealSeconds"/>, built and with no sapper on it
    /// (stock's CTFBotGetHealth takes a dispenser that is not building, placing or disabled, tf_bot_get_health.cpp:77-84).
    /// </summary>
    private bool DispenserInSight(out AiVector at)
    {
        at = default;
        float best = float.MaxValue;
        foreach (KnownBuilding b in FriendlyBuildings)
        {
            if (b.Type != TFThingType.Dispenser || SecondsSince(b.Last) > DispenserHealSeconds
                || (b.Flags & (int)(Knowledge.TFBuildingFlags.Sapped | Knowledge.TFBuildingFlags.Building)) != 0)
                continue;
            float d = _self.Origin.DistanceTo(b.At);
            if (d < best)
                (best, at) = (d, b.At);
        }

        return best < float.MaxValue;
    }

    /// <summary>Whether the bot is at the dispenser at <paramref name="at"/>: within twice its reach.</summary>
    private bool AtDispenser(AiVector at) => _self.Origin.DistanceTo(at) <= 2f * global::TF2.Server.Buildings.TFDispenser.DispenserRadius;

    /// <summary>
    /// A bot that healed at a dispenser last decide stays at it until it is full or its health stops rising
    /// (<see cref="DispenserStallSeconds"/>), unless it has a target in sight: stock's CTFBotGetHealth waits at a dispenser
    /// until "I've been healed", and gives up only to fight (tf_bot_get_health.cpp:251-264).
    /// </summary>
    private bool TopsUpAtDispenser
        => _healAtDispenser && _now - _healLast <= DecideSchedule.Period + 1 && HealthShare < 1f && !_targetSighted
           && SecondsSince(_dispenserRose) <= DispenserStallSeconds && DispenserInSight(out AiVector at) && AtDispenser(at);
}

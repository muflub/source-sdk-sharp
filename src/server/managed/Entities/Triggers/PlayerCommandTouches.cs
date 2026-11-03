//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// A trigger that touches a player once per command, as stock's
/// <c>CMoveHelperServer::ProcessImpacts</c> has every trigger touch the player
/// after each command's move (<c>PhysicsTouchTriggers</c>).
/// </summary>
public interface IPlayerCommandTouch
{
    /// <summary>
    /// This command's touch of <paramref name="player"/>. False when the
    /// trigger no longer holds them (left, disabled), which ends the
    /// registration.
    /// </summary>
    bool TouchFromCommand(EntityHandle player);
}

/// <summary>
/// Which triggers touch which player every command: the server's half of
/// <see cref="IPlayerCommandImpacts"/>. A trigger registers a player it holds;
/// each command of that player then asks every registered trigger for its
/// touch, and a trigger that answers false, or is gone, is dropped. Keyed by
/// handle and resolved per command, so a removed trigger or a new level leaves
/// nothing behind to call.
/// </summary>
public sealed class PlayerCommandTouches(Func<EntityHandle, ScriptedEntity?> resolve, ServerPlayerMoves? moves = null) : IPlayerCommandImpacts
{
    private readonly Dictionary<EntityHandle, List<EntityHandle>> _byPlayer = new();

    /// <summary>
    /// <paramref name="trigger"/> touches <paramref name="player"/> every
    /// command from now on, until it answers false. Registering twice is once.
    /// </summary>
    public void Hold(EntityHandle player, EntityHandle trigger)
    {
        if (!_byPlayer.TryGetValue(player, out List<EntityHandle>? triggers))
            _byPlayer[player] = triggers = new List<EntityHandle>();

        if (!triggers.Contains(trigger))
            triggers.Add(trigger);
    }

    /// <summary>Whether <paramref name="trigger"/> is registered for <paramref name="player"/>.</summary>
    public bool Holds(EntityHandle player, EntityHandle trigger)
        => _byPlayer.TryGetValue(player, out List<EntityHandle>? triggers) && triggers.Contains(trigger);

    public void ProcessImpacts(EntityHandle player)
    {
        TouchTriggers(player);
        TouchWhatTheMoveRanInto(player);
    }

    /// <summary>
    /// The rest of ProcessImpacts: each entity the move's traces ran into has its touch
    /// (<c>PhysicsImpact</c>), which is how a player walking into a func_rotating is
    /// thrown by it and one running into plate glass breaks it: nothing else reports a
    /// solid brush's contact. To the classes that hear every touch (SF_BRUSH_HURT,
    /// SF_BREAK_TOUCH).
    /// </summary>
    private void TouchWhatTheMoveRanInto(EntityHandle player)
    {
        if (moves?.Find(player) is not { } record)
            return;

        int count = Math.Min(record.Touched, record.TouchedEntities.Length);
        for (int i = 0; i < count; i++)
        {
            if (resolve(new EntityHandle(record.TouchedEntities[i])) is { TouchEveryTick: true } touched)
                touched.OnTouch(player);
        }
    }

    private void TouchTriggers(EntityHandle player)
    {
        if (!_byPlayer.TryGetValue(player, out List<EntityHandle>? triggers))
            return;

        // In registration order; a trigger that lets go is dropped as it goes.
        for (int i = 0; i < triggers.Count; i++)
        {
            if (resolve(triggers[i]) is IPlayerCommandTouch touch && touch.TouchFromCommand(player))
                continue;

            triggers.RemoveAt(i--);
        }

        if (triggers.Count == 0)
            _byPlayer.Remove(player);
    }
}

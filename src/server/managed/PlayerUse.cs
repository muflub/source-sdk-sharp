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
/// CHL2_Player::PlayerUse (hl2_player.cpp:2826), which ItemPreFrame runs once
/// per command before the move, and the deny sound ItemPostFrame plays after
/// it. What the key is aimed at is <see cref="PlayerUseFinder"/>'s; what a
/// use does is the target's own "Use" input, with the player as activator.
///
/// <para>
/// Left out, with nothing on this side to reach them: taking a tracktrain's
/// controls (a tracktrain is never a player's ground here; FuncTrackTrain's
/// OnControls), m_flTimeUseSuspended (only an episodic melee hit sets it),
/// PFLAG_USING (only that same episodic branch reads it) and the
/// single-primary-weapon pickup (HL2_SINGLE_PRIMARY_WEAPON_MODE is 0).
/// </para>
/// </summary>
public sealed class PlayerUse
{
    /// <summary>The press found something usable.</summary>
    public const string UseSound = "HL2Player.Use";

    /// <summary>The press found nothing (played after the move).</summary>
    public const string DenySound = "HL2Player.UseDeny";

    private readonly IUseTargetFinder _finder;
    private readonly IEngineEntities _entities;
    private readonly IEnginePlayerMovement _movement;
    private readonly IEngineSound _sound;
    private readonly IEntityDirectory _directory;

    public PlayerUse(IUseTargetFinder finder, IEngineEntities entities, IEnginePlayerMovement movement,
                     IEngineSound sound, IEntityDirectory directory)
    {
        _finder = finder;
        _entities = entities;
        _movement = movement;
        _sound = sound;
        _directory = directory;
    }

    /// <summary>
    /// ItemPreFrame's PlayerUse, for one command's <paramref name="buttons"/>,
    /// with UpdateButtonState's pressed and released taken against the last
    /// command's.
    /// </summary>
    public void BeforeMove(EntityHandle player, int buttons)
    {
        if (_directory.Get(player) is not SsPlayer p)
            return;

        int last = p.UseButtonsLast;
        p.UseButtonsLast = buttons;

        int changed = last ^ buttons;
        bool held = (buttons & GameMovement.InUse) != 0;
        bool pressed = (changed & buttons & GameMovement.InUse) != 0;
        bool released = (changed & ~buttons & GameMovement.InUse) != 0;

        if (!held && !pressed && !released)
            return;

        // Tracker 3926: nothing is used from a ladder.
        if (pressed && p.State.MoveType == GameMovement.MoveTypeLadder)
            return;

        EntityHandle target = _finder.Find(player);
        bool usedSomething = false;

        if (target != EntityHandle.None && _directory.Get(target) is { } instance)
        {
            UseCaps caps = instance.ObjectCaps;

            if (pressed)
                _sound.EntityEmitSound(player, UseSound);

            if ((held && caps.HasFlag(UseCaps.ContinuousUse))
                || (pressed && (caps & (UseCaps.ImpulseUse | UseCaps.OnOffUse)) != 0))
            {
                _entities.FireInput(target, "Use", null, player);
                usedSomething = true;
            }
            // BUGBUG (stock's): the release of an on/off use is the "off" use.
            else if (released && caps.HasFlag(UseCaps.OnOffUse))
            {
                _entities.FireInput(target, "Use", null, player);
                usedSomething = true;
            }
        }
        else if (pressed)
        {
            // Played after the move, which a ladder the press mounted cancels.
            _movement.QueueUseDenySound(player);
        }

        // Debounce the use key: the move sees it held.
        if (usedSomething)
            _movement.SwallowUse(player);
    }

    /// <summary>
    /// CHL2_Player::ItemPostFrame's deny sound. PostThink runs ItemPostFrame
    /// only for a live player, so a dead one's stays due until it lives again.
    /// </summary>
    public void AfterMove(EntityHandle player)
    {
        if (_directory.Get(player) is not SsPlayer p || !p.State.Alive)
            return;

        if (_movement.TakeUseDenySound(player))
            _sound.EntityEmitSound(player, DenySound);
    }
}

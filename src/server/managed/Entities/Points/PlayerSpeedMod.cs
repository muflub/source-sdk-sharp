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
/// Slows a player down, and takes away whatever they should not be able to do
/// while they are slow.
///
/// The speed is the name and the spawnflags are the reason it exists: a map does
/// not slow somebody to 20% for the sake of the walking speed, it does it because
/// the player is wading, or being dragged, or watching something happen, and a
/// player who can still sprint and shoot out of it is not in that state at all.
/// So the entity's real decision is the SET — one number and eight suppressions,
/// applied and lifted together — and the whole of it is here.
///
/// <para>
/// The speed itself is the one thing that could not be a movement rule.
/// <c>Movement.MaxSpeedScale</c> is the obvious home for it and will not do: it
/// runs on the client too, once for every command the server has not
/// acknowledged, so a per-player factor only the server knows about makes the
/// two sides disagree about where the player is and the player is dragged
/// backwards. The engine's lagged-movement value is NETWORKED, which is exactly
/// the difference, so this reaches for that instead — see
/// <see cref="IEnginePlayerMovement.SetLaggedMovement"/>.
/// </para>
///
/// <para>
/// One divergence, and it is a suppression that had nothing to suppress: stock
/// also puts the flashlight out and forbids it while slowed. CBasePlayer's
/// flashlight methods are empty bodies — only CHL2_Player implements a
/// flashlight, and this build's player derives straight from CBasePlayer — so
/// there is no flashlight in this mod to take away, and an ABI call for it would
/// have been a crossing to a no-op. It is left out rather than faked.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PlayerSpeedmod)]
public sealed class PlayerSpeedMod : ScriptedEntity
{
    public PlayerSpeedMod(IEnginePlayerMovement playerMovement, IEnginePlayers players)
    {
        PlayerMovement = playerMovement;
        Players = players;
    }

    /// <summary>how a player moves, and what may stop them.</summary>
    private readonly IEnginePlayerMovement PlayerMovement;

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();


    /// <summary>
    /// player_speedmod's own spawnflags -- eight things it can take away from a
    /// player while it holds them.
    ///
    /// <para>
    /// One domain and a dense one: eight consecutive bits, all the same shape of
    /// question. That density is the argument for the type rather than against it,
    /// because six of the eight are translated into <see cref="InputButtons"/> by
    /// <see cref="DisabledButtons"/> and the two tables are numbered nothing alike.
    /// Getting one crossed produces a player who can still jump out of a scripted
    /// moment, which reads as the map being broken rather than as this.
    /// </para>
    ///
    /// <para>
    /// SpawnFlag.SuppressWeapons and SpawnFlag.SuppressHud are the two with no button behind them; they
    /// are handled apart from the mask rather than being absent from it.
    /// </para>
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        SuppressWeapons = 0b0000_0001,
        SuppressHud = 0b0000_0010,
        SuppressJump = 0b0000_0100,
        SuppressDuck = 0b0000_1000,
        SuppressUse = 0b0001_0000,
        SuppressSpeed = 0b0010_0000,
        SuppressAttack = 0b0100_0000,
        SuppressZoom = 0b1000_0000,
    }

    /// <summary>
    /// Normal speed, and the value that means "give it all back".
    ///
    /// Stock compares the incoming float against 1 exactly, and this keeps that:
    /// the number comes from map text, so it is the literal 1 a mapper typed
    /// rather than the end of an accumulation, and a tolerance here would only
    /// make 0.999 mean something different from what it says.
    /// </summary>
    private const float Unmodified = 1f;

    [ScriptedEvent("ModifySpeed")]
    private void OnModifySpeed(InputData input)
    {
        // Stock's second branch — UTIL_GetLocalPlayer when the activator is not a
        // player — is guarded by !IsDeathmatch(), and this build's rules answer
        // true unconditionally. So the activator is the whole rule, and a map
        // that fires this from a timer reaches nobody in stock either.
        if (!Entities.IsPlayer(input.Activator))
            return;

        var player = input.Activator;
        float scale = input.Number;

        if (scale != Unmodified)
            Suppress(player);
        else
            Restore(player);

        PlayerMovement.SetLaggedMovement(player, scale);
    }

    private void Suppress(EntityHandle player)
    {
        // Before the buttons and the HUD, because holstering is the one part that
        // asks the weapon to run its own cleanup and stock is explicit about
        // wanting that to happen immediately.
        if (Flags.HasFlag(SpawnFlag.SuppressWeapons))
            Players.HolsterActiveWeapon(player);

        SetButtons(player, disabled: true);
        SetHud(player, visible: false);
    }

    private void Restore(EntityHandle player)
    {
        // Only into empty hands, which is stock's own guard: a player who picked
        // something up while slowed is holding what they chose, and putting the
        // remembered weapon back would take it off them.
        if (Flags.HasFlag(SpawnFlag.SuppressWeapons)
            && Players.GetActiveWeapon(player).Length == 0)
        {
            Players.DeployLastWeapon(player);
        }

        SetButtons(player, disabled: false);
        SetHud(player, visible: true);
    }

    private void SetButtons(EntityHandle player, bool disabled)
    {
        var mask = DisabledButtons((SpawnFlag)SpawnFlags);

        // A mapper who set no button flags gets no crossing rather than a call
        // that ORs zero into the mask.
        if (mask != 0)
            PlayerMovement.SetButtonsDisabled(player, mask, disabled);
    }

    /// <summary>
    /// The HUD, through the player's own SetHUDVisibility input.
    ///
    /// No ABI call for this, and that is worth saying because one looked
    /// necessary: CBasePlayer already declares SetHUDVisibility, and its body is
    /// the same <c>HIDEHUD_ALL</c> bit set and cleared that stock's speedmod
    /// reaches into m_iHideHUD to do by hand. The bit is networked and the
    /// client's HUD reads it, so firing the input is not an approximation of
    /// stock — it is stock's two lines, already written, already reachable.
    /// </summary>
    private void SetHud(EntityHandle player, bool visible)
    {
        if (!Flags.HasFlag(SpawnFlag.SuppressHud))
            return;

        Entities.FireInput(player, "SetHUDVisibility", visible ? "1" : "0", Handle);
    }

    /// <summary>
    /// Which buttons the spawnflags take away.
    ///
    /// Static and taking the flags, so the mapping can be checked without an
    /// engine — it is a table of eight bits translated into another eight, and
    /// the failure mode of getting one wrong is a player who can still jump out
    /// of a scripted moment, which reads as the map being broken rather than as
    /// this.
    /// </summary>
    internal static InputButtons DisabledButtons(SpawnFlag spawnFlags)
    {
        InputButtons mask = 0;

        if (spawnFlags.HasFlag(SpawnFlag.SuppressJump))
            mask |= InputButtons.Jump;

        if (spawnFlags.HasFlag(SpawnFlag.SuppressDuck))
            mask |= InputButtons.Duck;

        if (spawnFlags.HasFlag(SpawnFlag.SuppressUse))
            mask |= InputButtons.Use;

        if (spawnFlags.HasFlag(SpawnFlag.SuppressSpeed))
            mask |= InputButtons.Speed;

        // Both triggers, from one flag. A mapper who suppressed the attack meant
        // the alt-fire too, and stock says so in the same line.
        if (spawnFlags.HasFlag(SpawnFlag.SuppressAttack))
            mask |= InputButtons.Attack | InputButtons.Attack2;

        if (spawnFlags.HasFlag(SpawnFlag.SuppressZoom))
            mask |= InputButtons.Zoom;

        return mask;
    }
}

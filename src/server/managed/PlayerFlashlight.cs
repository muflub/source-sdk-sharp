//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>A player's flashlight, as the things that switch it ask: its state, on and off.</summary>
public interface IPlayerFlashlight
{
    /// <summary>Whether the player's flashlight is lit (EF_DIMLIGHT is set).</summary>
    bool IsOn(EntityHandle player);

    /// <summary>Lights it, if the server allows the flashlight and the player is alive.</summary>
    void TurnOn(EntityHandle player);

    /// <summary>Puts it out.</summary>
    void TurnOff(EntityHandle player);
}

/// <summary>
/// The player's flashlight on the server: the bit and the sound. The light
/// itself is the client's (SourceSharp.Client.Flashlight), which draws it
/// for a local player whose EF_DIMLIGHT is set, as stock's
/// C_BasePlayer::UpdateFlashlight does.
///
/// <para>
/// Ports CHL2MP_Player::FlashlightIsOn, FlashlightTurnOn and
/// FlashlightTurnOff (hl2mp_player.cpp), CBasePlayer::ImpulseCommands' impulse
/// 100 (player.cpp), and the turn-off in CBasePlayer::Event_Killed.
/// </para>
///
/// <para>
/// A DIVERGENCE FROM THIS MOD'S BASE, ON PURPOSE. This build's player derives
/// from CBasePlayer, whose flashlight methods are empty bodies, so impulse 100
/// did nothing and no server code set EF_DIMLIGHT; the client's flashlight
/// (flashlighteffect.cpp) was unreachable. The game is Source SDK 2013
/// multiplayer, whose player is CHL2MP_Player, and these are its bodies:
/// <c>flashlight.GetInt() &gt; 0 &amp;&amp; IsAlive()</c> lights it with
/// "HL2Player.FlashlightOn"; off clears the bit and plays
/// "HL2Player.FlashlightOff" for a living player. stock's <c>flashlight</c>
/// ConVar (mp_flashlight) is this mod's cs_sv_allow_flashlight, read as a
/// bool (non-zero), where stock tests &gt; 0.
/// </para>
/// </summary>
[Precache(PrecacheKind.Sound, OnSound)]
[Precache(PrecacheKind.Sound, OffSound)]
public sealed class PlayerFlashlight(IEntityEffectBits effects, IEnginePlayers players, IEngineSound sound)
    : IPlayerFlashlight
{
    /// <summary>Stock's flashlight impulse (CBasePlayer::ImpulseCommands, case 100).</summary>
    public const int Impulse = 100;

    /// <summary>CHL2MP_Player::FlashlightTurnOn's sound.</summary>
    public const string OnSound = "HL2Player.FlashlightOn";

    /// <summary>CHL2MP_Player::FlashlightTurnOff's sound.</summary>
    public const string OffSound = "HL2Player.FlashlightOff";

    /// <summary>
    /// Whether the server allows the flashlight: stock's
    /// <c>flashlight.GetInt() &gt; 0</c>. Wired to cs_sv_allow_flashlight by the
    /// module once the ConVars are registered; open until then.
    /// </summary>
    public Func<bool> Allowed { get; set; } = () => true;

    public bool IsOn(EntityHandle player)
        => (effects.Read(player) & (int)EntityEffect.DimLight) != 0;

    public void TurnOn(EntityHandle player)
    {
        if (!Allowed() || !players.IsAlive(player))
            return;

        effects.Set(player, EntityEffect.DimLight, on: true);
        sound.EntityEmitSound(player, OnSound);
    }

    public void TurnOff(EntityHandle player)
    {
        effects.Set(player, EntityEffect.DimLight, on: false);

        if (players.IsAlive(player))
            sound.EntityEmitSound(player, OffSound);
    }

    /// <summary>
    /// One command's impulse: 100 toggles the flashlight and is consumed (true).
    /// Anything else, and any impulse from a dead player, is not this class's
    /// (false): stock ran ImpulseCommands from ItemPostFrame, which only a living
    /// player reaches, and handed every other number to CheatImpulseCommands.
    /// </summary>
    public bool OnImpulse(EntityHandle player, int impulse)
    {
        if (impulse != Impulse || !players.IsAlive(player))
            return false;

        if (IsOn(player))
            TurnOff(player);
        else
            TurnOn(player);
        return true;
    }
}

//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.MaterialProxies;

namespace SourceSharp.Client.TempEnts;

/// <summary>How a spray ended: drawn, or which of stock's early-outs it took.</summary>
public enum SprayOutcome
{
    Sprayed = 0,
    /// <summary><c>r_decals</c> is off (C_TEPlayerDecal::PostDataUpdate).</summary>
    DecalsOff,
    /// <summary><c>cl_spraydisable</c> is on (TE_PlayerDecal).</summary>
    SpraysOff,
    /// <summary>No player slot or a negative entity: nothing to ask about.</summary>
    Refused,
    /// <summary>The target entity is not on this client ("No valid target?").</summary>
    NoEntity,
    /// <summary>The engine answers nothing for the player.</summary>
    NoPlayer,
    /// <summary>The player has no logo ("Doesn't have a logo?").</summary>
    NoLogo,
    /// <summary>The logo has not been downloaded yet.</summary>
    NotDownloaded,
    /// <summary>The download could not be copied under materials/.</summary>
    CopyFailed,
}

/// <summary>A player's spray logo on whatever it was aimed at.</summary>
public interface IPlayerSprays
{
    SprayOutcome Spray(Vector position, int player, int entity);
}

/// <summary>
/// Stock c_te_playerdecal.cpp's <c>C_TEPlayerDecal::PostDataUpdate</c>,
/// <c>TE_PlayerDecal</c> and <c>CreateTempMaterialForPlayerLogo</c>, decided
/// here since lane M1-C (they were shell_te_backends_client.cpp's
/// SourceSharp_ClientPlayerDecalShoot): the two settings, the target, the
/// player's logo CRC, the material and texture names it makes, and the
/// download protocol (a logo under <c>materials/temp</c>, else copied there
/// out of <c>download/user_custom</c>). What only the engine can do -- point
/// the logo material at the texture and shoot the decal -- is
/// <see cref="IClientSprayEngine"/>'s.
///
/// <para>
/// ONE ORDER DIFFERS FROM STOCK'S, at an edge: stock finds the logo MATERIAL
/// (and gives up if it is the error material) before it looks for the files;
/// here the files are settled first and the material is the carry-out's. The
/// sixty-four logo materials are precached (shell_te_backends_client.cpp's
/// PrecachePlayerDecal), so the two orders differ only on a broken install,
/// where this one may copy a downloaded logo stock would not have.
/// </para>
/// </summary>
public sealed class PlayerSprays(IDecalSettings decals, Func<bool> spraysDisabled, IFwdEngineClient engine,
                                 IFwdBaseFileSystem files, IEngineEffects effects, IClientSprayEngine spray)
    : IPlayerSprays
{
    public SprayOutcome Spray(Vector position, int player, int entity)
    {
        // Decals disabled?
        if (!decals.Decals)
            return SprayOutcome.DecalsOff;

        if (spraysDisabled())
            return SprayOutcome.SpraysOff;

        // A player SLOT (1-based) and an entity index; anything else names nothing.
        if (player <= 0 || entity < 0)
            return SprayOutcome.Refused;

        // No valid target?
        if (!effects.ClientEntityOrigin(entity, out _))
            return SprayOutcome.NoEntity;

        // Find player logo for shooter.
        if (!engine.TryGetPlayerInfo(player, out ClientPlayerInfo info))
            return SprayOutcome.NoPlayer;

        // Doesn't have a logo?
        if (info.LogoFile == 0)
            return SprayOutcome.NoLogo;

        string texture = PlayerLogoProxy.TextureName(info.LogoFile);
        string hex = texture["temp/".Length..];

        // See if logo has been downloaded.
        string fullTexture = $"materials/temp/{hex}.vtf";
        if (!files.FileExists(fullTexture, null))
        {
            // It may have been downloaded but not copied under the materials
            // folder, which is the only place the material system reads a .vtf.
            string custom = $"download/user_custom/{hex[0]}{hex[1]}/{hex}.dat";
            if (!files.FileExists(custom, null))
                return SprayOutcome.NotDownloaded;

            if (!engine.CopyLocalFile(custom, fullTexture))
                return SprayOutcome.CopyFailed;
        }

        spray.PlayerLogoDecal(position, player, entity, LogoMaterial(player), texture);
        return SprayOutcome.Sprayed;
    }

    /// <summary><c>VarArgs( "decals/playerlogo%2.2d", player )</c>: the logo material a player's spray draws with.</summary>
    public static string LogoMaterial(int player)
        => "decals/playerlogo" + player.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
}

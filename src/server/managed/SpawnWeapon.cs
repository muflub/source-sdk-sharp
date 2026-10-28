//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>What a spawning player ends up holding, once their kit is in their hands.</summary>
public interface ISpawnWeapon
{
    void Choose(EntityHandle player);
}

/// <summary>
/// CHL2MP_Player::GiveDefaultItems' tail: the weapon the client's
/// <c>cl_defaultweapon</c> names (a userinfo convar, the gravity gun by
/// default) if the player owns one, else the gravity gun.
/// </summary>
public sealed class DefaultSpawnWeapon : ISpawnWeapon
{
    /// <summary>The fallback, and cl_defaultweapon's own default.</summary>
    public const string Fallback = EntityNames.WeaponPhyscannon;

    private readonly Arsenal _arsenal;
    private readonly IEngineEntities _entities;
    private readonly Func<int, string?> _defaultWeaponOf;

    public DefaultSpawnWeapon(Arsenal arsenal, IEngineEntities entities, Func<int, string?> defaultWeaponOf)
    {
        _arsenal = arsenal;
        _entities = entities;
        _defaultWeaponOf = defaultWeaponOf;
    }

    /// <summary>Over the engine's GetClientConVarValue( index, "cl_defaultweapon" ).</summary>
    public static DefaultSpawnWeapon Of(Arsenal arsenal, IEngineEntities entities, IFwdEngineServer server)
        => new(arsenal, entities, index => server.ClientConVarValue(index, "cl_defaultweapon"));

    public void Choose(EntityHandle player)
    {
        string? wanted = _defaultWeaponOf(_entities.GetIndex(player));
        if (string.IsNullOrEmpty(wanted) || _arsenal.Switch(player, wanted) == SwitchOutcome.NotOwned)
            _arsenal.Switch(player, Fallback);
    }
}

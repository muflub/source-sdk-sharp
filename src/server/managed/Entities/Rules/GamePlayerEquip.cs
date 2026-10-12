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
/// Hands the activator a set of weapons. How a map arms a player on arrival —
/// an entry room, a loadout zone, a scripted moment.
/// </summary>
[ScriptedEntity(EntityNames.GamePlayerEquip)]
public sealed class GamePlayerEquip : ScriptedEntity
{
    public GamePlayerEquip(IEnginePlayers players)
    {
        Players = players;
    }

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    private const int UseOnly = 1;

    // Weapon class names, in the order the map listed them. A list rather than
    // fixed fields because the keys are arbitrary: a mapper writes the weapon
    // class as the key and a count as the value.
    private readonly List<string> _weapons = new();

    /// <summary>
    /// Every key that is not one the engine claimed is a weapon to give. There
    /// is no fixed set to declare, which is why this cannot be [ScriptVars] keys
    /// fields.
    /// </summary>
    public override bool OnKeyValue(string key, string value)
    {
        if (!key.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
            return false;

        // The value is a count, but giving the same weapon twice does nothing a
        // player can tell apart, so the name alone is kept.
        _weapons.Add(key);
        return true;
    }

    [ScriptedEvent("EquipPlayer")]
    private void OnEquipPlayer(InputData input) => Equip(input.Activator);

    [ScriptedEvent("Use")]
    private void OnUsed(InputData input) => Equip(input.Activator);

    private void Equip(EntityHandle player)
    {
        if (!Entities.IsValid(player) || Players.GetTeam(player) < 0)
            return;

        foreach (string weapon in _weapons)
            Players.GiveItem(player, weapon);
    }
}

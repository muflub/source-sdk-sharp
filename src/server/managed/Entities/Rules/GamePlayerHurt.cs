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
/// Hurts the player who used it, or heals them when the damage is negative:
/// stock's CGamePlayerHurt::Use (maprules.cpp, commented out in the SDK and
/// compiled by tools/stock-ab). DMG_GENERIC from the entity itself, as
/// CTakeDamageInfo( this, this, ... ) has it; spawnflag 1 fires once.
/// </summary>
[ScriptedEntity(EntityNames.GamePlayerHurt)]
public sealed class GamePlayerHurt : ScriptedEntity
{
    public GamePlayerHurt(IEngineDamage damage)
    {
        Damage = damage;
    }

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>What a map may set on this entity. See <see cref="GamePlayerHurtVars"/>.</summary>
    [ScriptVars] public GamePlayerHurtVars Vars = new();

    /// <summary>SF_PKILL_FIREONCE.</summary>
    private const int FireOnce = 0x0001;

    [ScriptedEvent("Use")]
    private void OnUsed(InputData input)
    {
        EntityHandle activator = input.Activator;

        if (Entities.IsValid(activator) && Entities.IsPlayer(activator))
        {
            if (Vars.Damage < 0f)
            {
                // TakeHealth: up to full health, never past it.
                Entities.SetHealth(activator, Math.Min(100, Entities.GetHealth(activator) + (int)-Vars.Damage));
            }
            else
            {
                Damage.Apply(activator, Handle, Vars.Damage, DamageType.Generic);
            }
        }

        if ((SpawnFlags & FireOnce) != 0)
            Remove();
    }
}

/// <summary>
/// The map keys <see cref="GamePlayerHurt"/> declares.
/// </summary>
[ScriptVars]
public partial class GamePlayerHurtVars
{
    /// <summary>m_flDamage: 0 until the map gives one.</summary>
    [VarName("dmg")]
    public float Damage { get; set; }
}

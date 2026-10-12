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
/// Hurts whoever set it off, or heals them: the damage key is negative for
/// healing, which is how the stock entity works and how maps are authored.
/// </summary>
[ScriptedEntity(EntityNames.GamePlayerHurt)]
public sealed class GamePlayerHurt : ScriptedEntity
{
    public GamePlayerHurt(IEngineDamage damage, IEnginePlayers players)
    {
        Damage = damage;
        Players = players;
    }

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    /// <summary>What a map may set on this entity. See <see cref="GamePlayerHurtVars"/>.</summary>
    [ScriptVars] public GamePlayerHurtVars Vars = new();

    [ScriptedEvent("HurtPlayer")]
    private void OnHurtPlayer(InputData input) => Apply(input.Activator);

    // The stock entity fires on Use as well as on the named input.
    [ScriptedEvent("Use")]
    private void OnUsed(InputData input) => Apply(input.Activator);

    private void Apply(EntityHandle activator)
    {
        if (!Entities.IsValid(activator) || Players.GetTeam(activator) < 0)
            return;

        if (Vars.Damage < 0f)
        {
            // Healing. Capped at full health, because a map that heals should
            // not be able to push somebody above what the rules allow.
            Entities.SetHealth(activator, Math.Min(100, Entities.GetHealth(activator) + (int)-Vars.Damage));
            return;
        }

        Damage.Apply(activator, activator, Vars.Damage, DamageType.Bullet);
    }
}

/// <summary>
/// The map keys <see cref="GamePlayerHurt"/> declares.
/// </summary>
[ScriptVars]
public partial class GamePlayerHurtVars
{
    [VarName("dmg")]
    public float Damage { get; set; } = 10f;
}

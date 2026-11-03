//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>What a headshot multiplies by. Stock's convar defaults to 2, and every HL2MP game runs 3: mod_hl2mp's cfg/skill.cfg.</summary>
[ConVar("sk_player_head", "3")]
public sealed class PlayerHeadDamageConVar : FloatConVar { }

[ConVar("sk_player_chest", "1")]
public sealed class PlayerChestDamageConVar : FloatConVar { }

[ConVar("sk_player_stomach", "1")]
public sealed class PlayerStomachDamageConVar : FloatConVar { }

[ConVar("sk_player_arm", "1")]
public sealed class PlayerArmDamageConVar : FloatConVar { }

[ConVar("sk_player_leg", "1")]
public sealed class PlayerLegDamageConVar : FloatConVar { }

/// <summary>
/// A mod whose hit locations mean something else (TF2: CTFPlayer::TraceAttack makes a
/// headshot a crit, for the weapons that say so). One of these turns off
/// <see cref="HitGroupDamage"/>.
/// </summary>
public interface IHitGroupDamageOwner
{
}

/// <summary>
/// CBasePlayer::TraceAttack's switch on the hit group: a hit on the head does three times
/// the damage. Only a traced hit has a group, so a blast or a fall stays
/// <see cref="HitGroup.Generic"/> and is not scaled.
/// </summary>
public sealed class HitGroupDamage(ConVarRegistry convars, IHooks<IHitGroupDamageOwner>? owners = null)
    : IDamageModifier, IOrderedHook
{
    /// <summary>First in the chain: stock scales in TraceAttack, before OnTakeDamage asks anybody.</summary>
    public const int Order = -1000;

    private readonly PlayerHeadDamageConVar? _head = convars.Get<PlayerHeadDamageConVar>();
    private readonly PlayerChestDamageConVar? _chest = convars.Get<PlayerChestDamageConVar>();
    private readonly PlayerStomachDamageConVar? _stomach = convars.Get<PlayerStomachDamageConVar>();
    private readonly PlayerArmDamageConVar? _arm = convars.Get<PlayerArmDamageConVar>();
    private readonly PlayerLegDamageConVar? _leg = convars.Get<PlayerLegDamageConVar>();

    public int HookOrder => Order;

    public float Modify(in DamageContext hit, float damage)
    {
        if (owners is { All.Count: > 0 })
            return damage;

        return hit.HitGroup switch
        {
            HitGroup.Head => damage * (_head?.Value ?? 1f),
            HitGroup.Chest => damage * (_chest?.Value ?? 1f),
            HitGroup.Stomach => damage * (_stomach?.Value ?? 1f),
            HitGroup.LeftArm or HitGroup.RightArm => damage * (_arm?.Value ?? 1f),
            HitGroup.LeftLeg or HitGroup.RightLeg => damage * (_leg?.Value ?? 1f),
            _ => damage,
        };
    }
}

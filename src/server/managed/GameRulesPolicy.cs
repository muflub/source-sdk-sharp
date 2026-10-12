//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The rules' constants, stated here and pushed to native once at startup.
///
/// <para>
/// <b>Two values, down from seventeen.</b> The other fifteen — item and weapon
/// respawn timing, the corpse drop rules, points per kill, the team id, the
/// health charger — fed native methods that nothing in this build ever called,
/// and those methods are gone. A constant whose only reader was a deleted
/// method is dead data with a managed declaration standing behind it, which is
/// worse than not offering it: it reads as a knob and turns nothing.
/// </para>
///
/// <para>
/// <b>Why a table and not a call.</b> The same trade <see cref="DamageRules"/>
/// makes: both are values a mod picks once, both are read on paths that must
/// not pay for a managed call to be told a startup answer, and native holding
/// the defaults is what lets them be read before the managed runtime is up.
/// </para>
///
/// <para>
/// <b>What must never move here.</b> Anything whose answer depends on who is
/// asking — fall damage, whether a player may connect, what two players are to
/// each other — is a managed CALL, because a table holds one answer.
/// </para>
/// </summary>
public static class GameRulesPolicy
{
    /// <summary>
    /// This is a multiplayer game.
    ///
    /// <para>
    /// Read through <c>IsMultiplayer</c>, whose live caller is
    /// util_shared.cpp:1226.
    /// </para>
    /// </summary>
    public const bool Multiplayer = true;

    // THE BLANKET DAMAGE SWITCH WENT AT plan_shim.md §5 3e. It was read through
    // CSourceSharpGameRules::AllowDamage, whose one call site was
    // baseentity.cpp:1750 -- and game/server/baseentity.cpp is in neither vpc,
    // so the method had no caller and the constant behind it was pushed across
    // once per level to be read by nobody. It is not the per-hit rule either:
    // that was FPlayerCanTakeDamage, removed earlier for the same reason. A
    // warmup or intermission that wants to suppress damage states it in
    // GameRules.CanTakeDamage, which IS asked, per hit, with who is involved.

    /// <summary>
    /// Push them across. Called once, beside <see cref="DamageRules.Register"/>,
    /// for the same reason and at the same moment.
    /// </summary>
    public static void Register(IEngineGameRulesPolicy engine)
    {
        engine.RegisterPolicy(new GameRulesPolicyBlob
        {
            Multiplayer = Multiplayer ? 1 : 0,
        });
    }
}

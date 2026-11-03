using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>One hit on a player, as its <c>player_hurt</c> says it: who, by whom, how much after the armour, and the health left.</summary>
public readonly record struct PlayerHurt(EntityHandle Player, EntityHandle Attacker, int Taken, int HealthLeft);

/// <summary>
/// Told of each hit on a player, where the <c>player_hurt</c> event is made
/// (stock's <c>CBasePlayer::OnTakeDamage</c>), on the game thread: the hook a
/// mod reads hits by (plan_hl2mp_ai §4.3: the AI's damage tap), since a mod
/// does not subscribe to the core's game events.
/// </summary>
public interface IPlayerHurtHook
{
    void OnPlayerHurt(in PlayerHurt hurt);
}

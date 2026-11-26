using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// Everything a client is sent about a player and would draw, by player index:
/// the real class and team beside the disguise as rendered (class, team, the
/// borrowed name's player index), the invisibility level as the client draws
/// it, the air-dash count and the six conditions a client renders. Nothing a
/// client is not sent rides here — health, clip and the ammo pools have their
/// own rows on <see cref="TFPlayerNetState"/>.
///
/// <para>
/// The disguise fields mirror stock's: the disguise class answers
/// <c>TF_CLASS_CIVILIAN</c> while nobody is borrowed (an untouched carrier
/// holds zero, and stock's own default for <c>m_nDisguiseClass</c> is the
/// civilian class), and the borrowed index is the carrier's own player index,
/// zero for none (stock's <c>m_hDisguiseTarget</c> says none with an invalid
/// handle; the carrier writes -1, <see cref="TFSpy"/>).
/// </para>
/// </summary>
public readonly record struct TFActorFacts(
    /// <summary>The real class (a client sees the model).</summary>
    TFClass Class,
    /// <summary>The REAL team: the AI binding renders the disguise itself from the fields below.</summary>
    TFTeam Team,
    /// <summary>Civilian/0 when not disguised.</summary>
    TFClass DisguiseClass,
    /// <summary>The team a disguised spy shows.</summary>
    TFTeam DisguiseTeam,
    /// <summary>0 when none: the player whose name the disguise borrows (stock's target id, tf_hud_target_id.cpp:248).</summary>
    int DisguiseTargetIndex,
    /// <summary>0..1 as the client draws it (<see cref="TFInvisibility.Percent"/>).</summary>
    float PercentInvisible,
    /// <summary>Stock's <c>m_iAirDash</c>: air dashes taken since last on ground.</summary>
    int AirDash,
    bool Burning,
    bool Stunned,
    bool Ubered,
    bool Disguised,
    bool Cloaked,
    bool Taunting,
    /// <summary>Drawn yellow: soaked in Jarate (<c>TF_COND_URINE</c>).</summary>
    bool Jarated = false,
    /// <summary>A cloak flickering where something struck it (<c>TF_COND_STEALTHED_BLINK</c>).</summary>
    bool Blinking = false,
    /// <summary>Bleeding (<c>TF_COND_BLEEDING</c>): the blood every client draws.</summary>
    bool Bleeding = false,
    /// <summary>The health a disguised Spy's disguise shows an enemy (stock's <c>m_iDisguiseHealth</c>): 0 when not disguised.</summary>
    int DisguiseHealth = 0,
    /// <summary>Soaked in Mad Milk (<c>TF_COND_MAD_MILK</c>): its drips, <c>peejar_drips_milk</c> (tf_player_shared.cpp:3827).</summary>
    bool Milked = false,
    /// <summary>Soaked in gas (<c>TF_COND_GAS</c>), the Gas Passer's.</summary>
    bool Gassed = false,
    /// <summary>
    /// A condition stock draws the mark for death over (<c>C_TFPlayer::UpdatedMarkedForDeathEffect</c>,
    /// c_tf_player.cpp:5181): <c>TF_COND_MARKEDFORDEATH</c>, its silent twin, or the PASS Time penalty.
    /// </summary>
    bool MarkedForDeath = false,
    /// <summary>Thrown by a blast (<c>TF_COND_BLASTJUMPING</c>, set with the blast-jump state, tf_player.cpp:8701).</summary>
    bool BlastJumping = false,
    /// <summary>Flying on a Thermal Thruster (<c>TF_COND_ROCKETPACK</c>).</summary>
    bool Rocketpack = false,
    /// <summary>Scoped (<c>TF_COND_ZOOMED</c>): a zoomed Sniper, his dot drawn for everyone (tf_weapon_sniperrifle.cpp:541).</summary>
    bool Zoomed = false);

/// <summary>What a client renders of one player, by player index.</summary>
public interface ITFActorFacts
{
    /// <summary>False for a slot with no carrier.</summary>
    bool TryRead(int playerIndex, out TFActorFacts facts);
}

/// <summary>
/// The server's <see cref="ITFActorFacts"/>: one read of the player's networked
/// carrier (<see cref="ITFPlayerData.Networked"/>), the real team from the
/// round roster, and the invisibility level from the carrier's cloak state and
/// <see cref="TFPlayerNetState.InvisChangeCompleteTime"/> through
/// <see cref="TFInvisibility.Percent"/> — the same recomputation stock's client
/// runs from the networked fields alone. The six bools ride the carrier's
/// condition words, asked through <see cref="TFPlayerNetStateViews.InCond"/>:
/// Ubered is stock's IsInvulnerable (tf_player_shared.cpp:8099-8107), the
/// medigun charge's condition (tf_weapon_medigun.cpp:51). The Vaccinator's
/// resists are a charge type of their own and do not count.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFActorFacts)])]
public sealed class TFServerActorFacts(ITFPlayerData players, ITeamRoundRoster roster,
                                       IEnginePlayers enginePlayers, IGameClock clock) : ITFActorFacts
{
    public bool TryRead(int playerIndex, out TFActorFacts facts)
    {
        facts = default;
        if (players.Networked(playerIndex) is not { } state)
            return false;

        EntityHandle player = enginePlayers.GetHandle(playerIndex);

        facts = new TFActorFacts(
            Class: state.Class(),
            Team: roster.TeamOf(player),
            DisguiseClass: state.DisguiseClass == 0 ? TFClass.Civilian : state.DisguiseAs(),
            DisguiseTeam: (TFTeam)state.DisguiseTeam,
            DisguiseTargetIndex: state.DisguiseTargetIndex > 0 ? state.DisguiseTargetIndex : 0,
            PercentInvisible: TFInvisibility.Percent(state, clock.Now),
            AirDash: state.AirDash,
            Burning: state.InCond(TFCond.Burning),
            Stunned: state.InCond(TFCond.Stunned),
            Ubered: state.IsInvulnerable(),
            Disguised: state.InCond(TFCond.Disguised),
            Cloaked: state.InCond(TFCond.Stealthed),
            Taunting: state.InCond(TFCond.Taunting),
            Jarated: state.InCond(TFCond.Urine),
            Blinking: state.InCond(TFCond.StealthedBlink),
            Bleeding: state.InCond(TFCond.Bleeding),
            DisguiseHealth: state.InCond(TFCond.Disguised) ? state.DisguiseHealth : 0,
            Milked: state.InCond(TFCond.MadMilk),
            Gassed: state.InCond(TFCond.Gas),
            MarkedForDeath: state.InCond(TFCond.Markedfordeath) || state.InCond(TFCond.MarkedfordeathSilent)
                            || state.InCond(TFCond.PasstimePenaltyDebuff),
            BlastJumping: state.InCond(TFCond.Blastjumping),
            Rocketpack: state.InCond(TFCond.Rocketpack),
            Zoomed: state.InCond(TFCond.Zoomed));
        return true;
    }
}

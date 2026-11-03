//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Conditions;

namespace TF2.Server.Player;

/// <summary>
/// The blast jump state, <c>CTFPlayer::m_iBlastJumpState</c>
/// (tf_player.h:989): the bits <c>SetBlastJumpState</c> ORs
/// (tf_player.cpp:8676-8678). Stock's
/// <c>TF_PLAYER_ROCKET_JUMPED</c>/<c>TF_PLAYER_STICKY_JUMPED</c> ordinals are
/// not in the headers this port reads; the state is a plain server member
/// nowhere networked, so the numbering carries no parity weight.
/// </summary>
[Flags]
public enum TFBlastJump
{
    None = 0,
    RocketJumped = 1,
    StickyJumped = 1 << 1,
    EnemyBlastedMe = 1 << 2,
}

/// <summary>
/// The player's blast jump: <c>CTFPlayer::SetBlastJumpState</c>
/// (tf_player.cpp:8676-8704) and <c>ClearBlastJumpState</c> (:8707-8713), as
/// <see cref="TFBlastApplier"/>'s self-blast arms call them
/// (:8919-8922, :9150-9182), and the landing half of <c>TFPlayerThink</c>
/// (:1444-1483) in <see cref="Tick"/>. The state itself is a plain server
/// member in stock — the client sees <c>TF_COND_BLASTJUMPING</c> in the
/// condition words and the <c>rocket_jump</c>/<c>sticky_jump</c> (and
/// <c>_landed</c>) events.
///
/// <para>
/// Stock's <c>m_bCreatedRocketJumpParticles</c> arm (:8707, :1509-1515) is
/// the one half not here: the feet-attached <c>rocketjump_smoke</c> needs an
/// entity-attached particle effect, which the temp-entity seam cannot name
/// (docs/plans/tf2/deferred.tsv).
/// </para>
/// </summary>
public sealed class TFBlastJumpState(TFPlayerCarriers carriers, TFPlayerConditions conditions,
                                     IEngineNetworking networking, IEntityDirectory directory,
                                     IEnginePlayers players, IEngineGameEvents events) : IPlayerComponent
{
    private TFBlastJump _state = TFBlastJump.None;
    private bool _blastLatched;
    private float _landTime;
    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;

    /// <summary>Between the condition countdown (20) and the stun stack (30):
    /// the landing think follows the condition tick's ground contact, as
    /// stock's <c>TFPlayerThink</c arm does after the shared condition think.</summary>
    public int Order => 25;

    /// <summary><c>m_iBlastJumpState</c>.</summary>
    public TFBlastJump State => _state;

    /// <summary><c>m_flBlastJumpLandTime</c>, the last <see cref="Clear"/>.</summary>
    public float LandTime => _landTime;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        _state = TFBlastJump.None;
        carriers.EnterPart(_index, this);
    }

    /// <summary>
    /// <c>TFPlayerThink</c>'s landing arm (:1441-1483): airborne is no ground
    /// and dry — its <c>m_iLeftGroundHealth</c> half is the health lane's —
    /// and everything else is a landing, told once. The sticky jump is named
    /// first when both bits ride (:1462-1466); the landed event carries the
    /// userid and nothing else (:1470-1476).
    /// </summary>
    public void Tick(float now)
    {
        if (_player == EntityHandle.None)
            return;

        bool airborne = !networking.EntityGetState(_player, out _).HasFlag(EntityState.OnGround)
                        && directory.Get(_player) is not SsPlayer { State.WaterLevel: not 0 };
        if (airborne || _state == TFBlastJump.None)
            return;

        string? landed = (_state & TFBlastJump.StickyJumped) != 0 ? "sticky_jump_landed"
            : (_state & TFBlastJump.RocketJumped) != 0 ? "rocket_jump_landed" : null;

        Clear(now);

        if (landed is not null)
            Fire(landed, playSound: false, withPlaySound: false);
    }

    /// <summary>
    /// <c>SetBlastJumpState</c> (:8676-8704): the bit, then the jump's event —
    /// <c>userid</c> and <c>playsound</c>, the latter the caller's read of the
    /// weapon's <c>no_self_blast_dmg</c> — then <c>TF_COND_BLASTJUMPING</c>,
    /// permanent as stock's bare <c>AddCond</c> (:8701).
    /// </summary>
    public void Set(TFBlastJump jump, bool playSound = false)
    {
        _state |= jump; // :8677

        string? eventName = jump == TFBlastJump.StickyJumped ? "sticky_jump"
            : jump == TFBlastJump.RocketJumped ? "rocket_jump" : null; // :8679-8689
        if (eventName is not null)
            Fire(eventName, playSound);

        conditions.Add(_player, TFCond.Blastjumping); // :8701
    }

    /// <summary>
    /// <c>ClearBlastJumpState</c> (:8707-8713): the state goes, the landing
    /// time is stamped, the condition with it. The particles flag of :8707 is
    /// the deferred arm.
    /// </summary>
    public void Clear(float now)
    {
        _state = TFBlastJump.None; // :8708
        _landTime = now;           // :8709
        conditions.Remove(_player, TFCond.Blastjumping); // :8710
    }

    /// <summary>
    /// The blast-damage latch, stock's <c>m_bTakenBlastDamageSinceLastMovement</c>:
    /// <see cref="NoteEnemyBlast"/> raises it when another combatant's blast
    /// pushes (:10474-10477), <see cref="EnemyBlasted"/> consumes it on the
    /// rise (:2361-2364), and <see cref="EndCommand"/> drops it with the
    /// command (:335) — so one blast claims its jump on the next command's
    /// rise and on no later one.
    /// </summary>
    public void NoteEnemyBlast() => _blastLatched = true;

    /// <summary>
    /// The rise arm's read (:2361-2364): with the latch up the rise names
    /// <c>TF_PLAYER_ENEMY_BLASTED_ME</c> — a state bit with no event, which
    /// <see cref="Set"/> answers for the exact-equality arms — and the
    /// condition with it (:8701). The latch itself is the command's, not the
    /// read's: only <see cref="EndCommand"/> drops it.
    /// </summary>
    public void EnemyBlasted()
    {
        if (_blastLatched)
            Set(TFBlastJump.EnemyBlastedMe);
    }

    /// <summary>The end-of-command clear (:335): the latch lives exactly one command.</summary>
    public void EndCommand() => _blastLatched = false;

    /// <summary>
    /// <c>CTFPlayer::Spawn</c>'s <c>m_iBlastJumpState = 0</c> and the latch's
    /// <c>= false</c> (tf_player.cpp:3746, :3748): a death in the air lands
    /// under the new life with no jump and with no blast held over. The
    /// condition is already gone — the spawn clears every one — and the
    /// landing time is stock's plain member, untouched here.
    /// </summary>
    public void ClearOnSpawn()
    {
        _state = TFBlastJump.None;
        _blastLatched = false;
    }

    public void Detach()
    {
        if (_index >= 0)
            carriers.LeavePart(_index, this);
        _index = -1;
        _player = EntityHandle.None;
        _state = TFBlastJump.None;
    }

    private void Fire(string name, bool playSound, bool withPlaySound = true)
    {
        IntPtr e = events.EventCreate(name);
        if (e == IntPtr.Zero)
            return;
        events.EventSetInt(e, "userid", players.GetUserId(_player));
        if (withPlaySound)
            events.EventSetBool(e, "playsound", playSound);
        events.EventFire(e);
    }
}

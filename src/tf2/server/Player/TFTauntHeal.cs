//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using SourceSharp.Shared;

using TF2.Server.Medigun;

using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// The Amputator's healing taunt: while a base-weapon taunt holds the medic,
/// every second a pulse reaches the hurt around them. The start is stock's
/// <c>OnAddTaunting</c> (tf_player_shared.cpp:3994-4011), whose active-weapon
/// <c>enables_aoo_heal</c> arm calls <c>Heal_Radius(true)</c>; the pulse is
/// <c>PulseMedicRadiusHeal</c> (:14278-14382) — the validity gate :14294, the
/// one-second rescan :14301, the alive, self, team-and-disguise (:14306-14320)
/// and healing-blocked (:14322-14330) skips, the 450-unit reach :14333, the
/// brush-only sightline :14337-14340, the ring condition :14343, the
/// <c>Heal(25, 1, 1)</c> :14349 and the out-of-reach release :14352-14359 — and
/// the end is <c>OnRemoveTaunting</c>'s <c>Heal_Radius(false)</c> :4050, whose
/// server arm (:14251-14262) stops every tracked patient.
///
/// <para>
/// Stock runs the pulse from the shared's <c>ConditionThink</c> (:3167), which
/// this component's tick is. The start there rides the condition's add hook;
/// here it is watched on the tick, so the first pulse is the start tick's —
/// the same frame, stock's zeroed <c>m_flRadiusHealCheckTime</c>(:14301) being
/// always past. Stock's <c>Heal_Radius(false)</c> also clears the client's
/// ring particles (:14265-14271, :14366-14381); the ring is the
/// <c>TF_COND_RADIUSHEAL</c> condition here, whose wear the client draws.
/// </para>
/// </summary>
/// <remarks>
/// The self-heal and validity arms read <c>TF_COND_RADIUSHEAL_ON_DAMAGE</c>
/// as stock's do (:14294, :14310); its grant is deferred
/// (docs/plans/tf2/conditions.tsv), so today they answer only the medic arm.
/// A detached player's heal entries die with the healing lane's own
/// departure walk (<see cref="TFPlayerHealing.OnClientDisconnected"/>).
/// </remarks>
public sealed class TFTauntHeal(ITFPlayerData players, ITFPlayerConditions conditions,
                                IEnginePlayers enginePlayers, IEngineEntities entities,
                                IEngineWorldQuery world, ITFWeaponOwners owners,
                                AttributeHooks hooks, ITFPlayerHealing healing)
    : IPlayerComponent
{
    /// <summary>The aura's reach, the radius of stock's squared test (:14333).</summary>
    public const float Radius = 450f;

    /// <summary>The same reach squared, the test's own shape (:14333).</summary>
    public const float RadiusSquared = Radius * Radius;

    /// <summary>The heal's rate, stock's <c>Heal(m_pOuter, 25.f, 1.f, 1.f)</c> (:14349).</summary>
    public const float HealPerSecond = 25f;

    /// <summary>The seconds between pulses (:14362).</summary>
    public const float PulseSeconds = 1f;

    /// <summary>The ring condition's wear, refreshed every pulse (:14343).</summary>
    public const float RingSeconds = 1.2f;

    /// <summary>The start arm's attribute, the Amputator's own (:4006).</summary>
    public const string EnablesAoeHeal = "enables_aoe_heal";

    /// <summary>The target skip's attribute (:14327), as the healing bolt's arm reads it too.</summary>
    public const string BlocksHealing = "weapon_blocks_healing";

    private EntityHandle _player = EntityHandle.None;
    private int _index = -1;
    private bool _healing;
    private float _nextCheck;
    private readonly List<int> _targets = [];

    /// <summary>After the taunt attack (27): the same taunt, its heal beside it;
    /// unique among components, so the stable sort's tie-break never decides for it.</summary>
    public int Order => 28;

    public void Attach(EntityHandle player)
    {
        _player = player;
        _index = player.EntryIndex;
        _healing = false;
        _nextCheck = 0f;
        _targets.Clear();
    }

    /// <summary>One frame of the aura: the validity and start arms, then the pulse.</summary>
    public void Tick(float now)
    {
        if (_player == EntityHandle.None)
            return;

        var carrier = players.Predicted(_index);
        bool taunting = carrier is { } state && state.InCond(TFCond.Taunting);

        // The pulse's validity gate (:14294): a living medic — or a
        // radius-heal-on-damage carrier, whose grant is deferred — healing
        // while the taunt holds. Losing any of it is Heal_Radius(false)
        // (:14296, and OnRemoveTaunting's :4050).
        bool valid = taunting && enginePlayers.IsAlive(_player)
            && carrier is { } alive
            && ((TFClass)alive.PlayerClass == TFClass.Medic || alive.InCond(TFCond.RadiushealOnDamage));
        if (_healing && !valid)
        {
            Stop(now);
            return;
        }

        if (!valid)
            return;

        // The start arm (:4003-4010): the base-weapon taunt — every taunt of
        // ours is one — and the held weapon's healing say-so.
        if (!_healing)
        {
            if (hooks.Int(0, EnablesAoeHeal, owners.ActiveItemOf(_index)) != 1)
                return;

            _healing = true;
            _nextCheck = now;   // stock's zeroed check time: this tick pulses
        }

        if (now < _nextCheck)
            return;

        Pulse(now);
        _nextCheck = now + PulseSeconds;   // :14362
    }

    public void Detach()
    {
        _player = EntityHandle.None;
        _index = -1;
        _healing = false;
        _targets.Clear();
    }

    /// <summary>One rescan (:14303-14360): the players, the gates, the ring, the heal.</summary>
    private void Pulse(float now)
    {
        int medicTeam = enginePlayers.GetTeam(_player);
        Vector centre = entities.GetOrigin(_player);

        for (int index = 1; index < TFPlayerResourceNetState.Slots; index++)
        {
            if (players.Networked(index) is not { } state)
                continue;   // :14305, nobody there.

            EntityHandle patient = enginePlayers.GetHandle(index);
            if (!enginePlayers.IsAlive(patient))
                continue;   // :14306.

            // :14309-14311 — the caster heals themselves under the proc only.
            if (index == _index && !state.InCond(TFCond.RadiushealOnDamage))
                continue;

            // The team gate (:14313-14320): a teammate; another team's player
            // only cloaked or disguised, the disguise onto this team.
            if (enginePlayers.GetTeam(patient) != medicTeam)
            {
                bool stealthed = state.IsStealthed();
                bool disguised = state.InCond(TFCond.Disguised);
                if ((!stealthed && !disguised) || (disguised && state.DisguiseTeam != medicTeam))
                    continue;
            }

            // A healing-blocked hand (:14322-14330).
            if (owners.ActiveItemOf(index) is { } active
                && hooks.Int(0, BlocksHealing, active) != 0)
                continue;

            // Out of reach: the tracked heal ends (:14352-14359); the rest
            // waits outside the radius for a closer pass.
            if ((entities.GetOrigin(patient) - centre).LengthSquared > RadiusSquared)
            {
                if (_targets.Remove(index))
                    healing.StopHealing(patient, _player, now);
                continue;
            }

            // A wall between the centres hides them (:14336-14340); the
            // blocked stay targeted, as stock's continue leaves them.
            TraceResult seen = world.TraceLine(entities.GetWorldSpaceCenter(patient),
                                               entities.GetWorldSpaceCenter(_player), TraceMask.Brushes);
            if (seen.Fraction < 1f)
                continue;

            // The ring, refreshed every pulse (:14343).
            conditions.Add(patient, TFCond.Radiusheal, RingSeconds);

            // The heal, once per patient until it ends (:14346-14350).
            if (!_targets.Contains(index))
            {
                _targets.Add(index);
                healing.Heal(patient, _player, HealPerSecond, now, overhealBonus: 1f, decayMult: 1f);
            }
        }
    }

    /// <summary><c>Heal_Radius(false)</c>'s server arm (:14251-14262): every
    /// tracked patient's heal ends, the list with them.</summary>
    private void Stop(float now)
    {
        foreach (int index in _targets)
        {
            EntityHandle patient = enginePlayers.GetHandle(index);
            if (patient != EntityHandle.None)
                healing.StopHealing(patient, _player, now);
        }

        _targets.Clear();
        _healing = false;
    }
}

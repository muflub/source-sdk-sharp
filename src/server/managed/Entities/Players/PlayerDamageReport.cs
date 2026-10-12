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
/// The player's damage report: what the HUD is told about the hits it took,
/// once a frame. Stock's <c>m_DmgTake</c>, <c>m_DmgSave</c>,
/// <c>m_DmgOrigin</c>, <c>m_bitsDamageType</c> and <c>m_bitsHUDDamage</c>,
/// written by <c>CBasePlayer::OnTakeDamage</c> and read out as the
/// <c>Damage</c> user message by <c>CHL2_Player::UpdateClientData</c>
/// (hl2_player.cpp).
///
/// <para>
/// <b>ONE MESSAGE A FRAME, NOT ONE A HIT.</b> Stock adds each hit to the
/// totals and sends them when the client data is next updated, so a shotgun
/// blast's pellets arrive as one message with their damage summed. The armour
/// figure is NOT summed: stock assigns <c>m_DmgSave = flArmor</c> per hit, so
/// the message carries the last hit's armour, and so does this.
/// </para>
///
/// <para>
/// <b>The bits.</b> Every hit's type is remembered; the message carries only
/// the kinds the HUD has art for (<see cref="DamageRules.ShowOnHud"/>), and
/// poison only when the player was poisoned within <see cref="PoisonWindow"/>.
/// After a send the non-time-based kinds are forgotten
/// (<see cref="DamageRules.TimeBased"/>), and a change in the remembered kinds
/// is itself a reason to send: a spawn sends one empty report, and the frame
/// after a burn-and-acid hit sends acid alone.
/// </para>
/// </summary>
public sealed class PlayerDamageReport
{
    /// <summary>Both totals go out as a byte: stock's <c>clamp( x, 0, 255 )</c>.</summary>
    public const int ByteMax = 255;

    /// <summary>Poison shows only if the player was poisoned this long ago or less (stock's 0.1 s).</summary>
    public const float PoisonWindow = 0.1f;

    private float _take;
    private float _save;
    private Vector _origin;
    private int _bitsDamageType;
    private int _bitsHudDamage = -1;
    // Stock's m_tbdPrev: the last poison hit, and the last time-based check.
    private float _poisonedAt;

    /// <summary>Health taken since the last send (stock's <c>m_DmgTake</c>).</summary>
    public float Take => _take;

    /// <summary>The last hit's armour (stock's <c>m_DmgSave</c>).</summary>
    public float Save => _save;

    /// <summary>The kinds remembered (stock's <c>m_bitsDamageType</c>).</summary>
    public int Bits => _bitsDamageType;

    /// <summary>
    /// A spawn: nothing taken, nothing remembered, and the HUD's copy unknown,
    /// so the next update sends (stock's <c>CBasePlayer::Spawn</c>).
    /// </summary>
    public void Reset()
    {
        _take = 0f;
        _save = 0f;
        _bitsDamageType = 0;
        _bitsHudDamage = -1;
    }

    /// <summary>The suit soaked <paramref name="armour"/> of a hit: stock's <c>m_DmgSave = flArmor</c>.</summary>
    public void Absorbed(float armour) => _save = armour;

    /// <summary>
    /// A hit landed: <paramref name="health"/> taken, of <paramref name="type"/>,
    /// from <paramref name="origin"/> (the inflictor's origin; null keeps the
    /// last one, as stock does for an inflictor with no edict).
    /// </summary>
    public void Took(int health, DamageType type, Vector? origin, float now)
    {
        if (origin is Vector from)
            _origin = from;

        _take += health;

        // A fresh hit of a time-based kind restarts its countdown.
        for (int i = 0; i < TimeBasedKinds; i++)
        {
            int bit = (int)DamageType.Paralyze << i;
            if ((bit & (int)type & (int)DamageRules.TimeBased) != 0)
                _timeBased[i] = 0;
        }

        _bitsDamageType |= (int)type;
        _bitsHudDamage = -1;

        // Stock stamps m_tbdPrev in the suit's diagnosis of a poison hit, and
        // counts what poison took so it can be given back.
        if ((type & DamageType.Poison) != 0)
        {
            _poisonDamage += health;
            _poisonedAt = now;
            _timeBased[PoisonRecoverIndex] = 0;
        }
    }

    //-- TIME-BASED DAMAGE (stock's CheckTimeBasedDamage) ----------------------

    /// <summary>CDMG_TIMEBASED: the kinds from DMG_PARALYZE up, by itbd_ index.</summary>
    public const int TimeBasedKinds = 8;

    /// <summary>How often the time-based kinds are checked.</summary>
    public const float TimeBasedInterval = 2f;

    /// <summary>Poison gives back at most this much an interval.</summary>
    public const int PoisonRecoverStep = 10;

    // Stock's live defines (player.cpp:132-151) and the two recover cases'
    // literals, by itbd_ index: paralyze, nerve gas, poison recover,
    // radiation, drown recover, acid, slow burn, slow freeze.
    public static readonly IReadOnlyList<byte> Durations = [2, 2, 9, 2, 4, 2, 2, 2];

    private readonly byte[] _timeBased = new byte[TimeBasedKinds];
    private int _poisonDamage;
    private int _poisonRestored;

    /// <summary>
    /// Every <see cref="TimeBasedInterval"/>: each time-based kind carried
    /// starts its duration, or counts it down and is forgotten when done.
    /// Answers the health poison gives back this interval (TakeHealth's to
    /// apply). Drowning is not ported, so drown recovery has nothing to give.
    /// Stock deals no time-based damage: its OnTakeDamage calls are commented out.
    /// </summary>
    public int CheckTimeBased(float now)
    {
        if ((_bitsDamageType & (int)DamageRules.TimeBased) == 0)
            return 0;

        if (MathF.Abs(now - _poisonedAt) < TimeBasedInterval)
            return 0;

        _poisonedAt = now;
        int heal = 0;

        for (int i = 0; i < TimeBasedKinds; i++)
        {
            int bit = (int)DamageType.Paralyze << i;
            if ((bit & (int)DamageRules.TimeBased) == 0 || (_bitsDamageType & bit) == 0)
                continue;

            if (i == PoisonRecoverIndex && _poisonDamage > _poisonRestored)
            {
                int dif = Math.Min(_poisonDamage - _poisonRestored, PoisonRecoverStep);
                heal += dif;
                _poisonRestored += dif;
            }

            if (_timeBased[i] != 0)
            {
                if (--_timeBased[i] == 0)
                    _bitsDamageType &= ~bit;
            }
            else
            {
                _timeBased[i] = Durations[i];
            }
        }

        return heal;
    }

    /// <summary>itbd_PoisonRecover.</summary>
    public const int PoisonRecoverIndex = 2;

    /// <summary>
    /// The client-data update: the message to send, if anything changed since
    /// the last one (stock's <c>CHL2_Player::UpdateClientData</c> damage block).
    /// </summary>
    public bool TryFlush(float now, out Damage message)
    {
        message = default;
        if (_take == 0f && _save == 0f && _bitsHudDamage == _bitsDamageType)
            return false;

        int visible = _bitsDamageType & (int)DamageRules.ShowOnHud;

        if ((visible & (int)DamageType.Poison) != 0 && now - _poisonedAt > PoisonWindow)
            visible &= ~(int)DamageType.Poison;

        message = new Damage
        {
            Armour = (byte)(int)Math.Clamp(_save, 0f, ByteMax),
            Health = (byte)(int)Math.Clamp(_take, 0f, ByteMax),
            DamageBits = visible,
            Origin = _origin,
        };

        _take = 0f;
        _save = 0f;
        _bitsHudDamage = _bitsDamageType;
        _bitsDamageType &= (int)DamageRules.TimeBased;
        return true;
    }
}

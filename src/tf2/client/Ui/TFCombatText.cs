//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Vector2 = System.Numerics.Vector2;

using TF2.Shared;
using TF2.Shared.Movement;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The combat text's read (stock's <c>CAccountPanel</c>, tf_hud_account.cpp):
/// each <c>player_hurt</c> whose attacker is the local player records one
/// delta item of <c>ACCOUNT_DELTA_DAMAGE</c> — the post-armour
/// <c>damageamount</c>, its crit bit and its show time (FireGameEvent
/// :675-697, DisplayDamageFeedback :569-646). The self-damage skip is
/// stock's (attacker == victim returns, :546-547), as is the
/// <c>hud_combattext</c> gate (:620) — read by name, the pyro family's
/// pattern. The stealthed-or-disguised-enemy-spy skip (:550-566) reads the
/// victim's carrier: an enemy <see cref="TFClass.Spy"/> hidden when the team
/// their disguise shows is the local player's or they are stealthed (the
/// dead ringer's feign-ready exemption, :552-559, waits on the ready bit no
/// read carries). The position half is stock's too: the occlusion trace
/// (<c>WorldSpaceCenter</c> to <c>MainViewOrigin</c>,
/// MASK_SOLID_BRUSHONLY, :622-625) gates the record, and the anchor is the
/// victim's origin crowned by the hull and the distance (m_bWorldSpace,
/// :630-639), held beside the number the way the taken hits' origins are.
/// <see cref="Screen"/> is the paint's projection (:1120-1131).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFCombatText)])]
public sealed class TFCombatText : ITFCombatText
{
    /// <summary>Stock's CPanelAnimationVar m_flDeltaLifetime (tf_hud_account.cpp:225).</summary>
    public const float DeltaLifetime = 2f;

    /// <summary>Stock keeps every delta (a CUtlVector); the cap only bounds a
    /// pathological flood, the oldest expiring first — the indicator's rule.</summary>
    public const int MaxTexts = 16;

    private readonly IGameClock _clock;
    private readonly IEngineHud _screen;
    private readonly IClientStandalones _standalones;
    private readonly HudCombatTextConVar _enabled;
    private readonly ITFPlayerData _players;
    private readonly IClientBeamExecutor _beams;
    private readonly IEngineWorldQuery _world;
    private readonly IEngineEffects _effects;
    private readonly IFxRender _fx;
    private readonly TFCombatNumber[] _numbers = new TFCombatNumber[MaxTexts];
    private readonly Vector[] _worlds = new Vector[MaxTexts];
    private readonly bool[] _anchored = new bool[MaxTexts];
    private int _count;
    private float _seen;

    public TFCombatText(IGameClock clock, IEngineHud screen, IClientStandalones standalones,
                        HudCombatTextConVar enabled, ITFPlayerData players, IClientBeamExecutor beams,
                        IEngineWorldQuery world, IEngineEffects effects, IFxRender fx, EventLink link)
    {
        (_clock, _screen, _standalones, _enabled, _players, _beams, _world, _effects, _fx)
            = (clock, screen, standalones, enabled, players, beams, world, effects, fx);
        link.OnHurt(OnHurt);
    }

    /// <summary>One player_hurt (the engine's dispatch, through the mod's
    /// EventLink).</summary>
    private void OnHurt(PlayerHurtArgs hurt)
    {
        if (!Enabled)
            return;                                  // hud_combattext off: stock's :620

        if (hurt.AttackerUserId == 0 || hurt.AttackerUserId != LocalUserId())
            return;                                  // only what the local player dealt

        if (hurt.AttackerUserId == hurt.UserId)
            return;                                  // the self-damage skip, stock's :546

        if (hurt.DamageAmount <= 0)
            return;

        int row = RowOf(hurt.UserId);
        if (HiddenByCarrier(row))
            return;                                  // the stealthed or disguised enemy spy, stock's :550-566

        // The anchor, stock's :622-639: the trace gates the record itself
        // (an occluded victim records nothing, :625), and what records is
        // crowned by hull and distance. A victim the client has no row for
        // has no centre to trace; it records anchorless, the projection's
        // false, as the taken hits' missing origin does.
        Vector? anchor = null;
        if (row >= 0)
        {
            if (!_effects.ClientEntityOrigin(row, out var origin) || !Visible(origin))
                return;
            anchor = Above(origin);
        }

        if (_count >= MaxTexts)
            RemoveAt(0);                             // the oldest makes room
        _numbers[_count] = new TFCombatNumber(hurt.DamageAmount, hurt.Crit, _clock.Now);
        _worlds[_count] = anchor ?? default;
        _anchored[_count] = anchor is not null;
        _count++;
    }

    /// <summary>Whether the text draws: stock's hud_combattext.</summary>
    private bool Enabled => _enabled.Value;

    /// <summary>The local player's user id, the resource rows' way (the
    /// death screen's read).</summary>
    private int LocalUserId()
    {
        int index = _screen.LocalPlayerIndex;
        return index > 0 && index < PlayerResourceNetState.Slots
            && Core() is { } core ? core.State.UserId[index] : 0;
    }

    /// <summary>
    /// Stock's spy skip (tf_hud_account.cpp:550-566): an enemy
    /// <see cref="TFClass.Spy"/> hides their damage when the team their
    /// disguise shows is the local player's (<c>GetDisguiseTeam</c>, read raw
    /// as stock reads it) or they are stealthed (<c>IsStealthed</c>,
    /// tf_player_shared.cpp:8112's three bits, off the carrier's condition
    /// words). The dead ringer's feign-ready spy shows regardless (:552-559);
    /// <c>m_bFeignDeathReady</c> (tf_player_shared.h:1087) rides no read of
    /// ours, so the exemption waits with the weapon itself.
    /// </summary>
    private bool HiddenByCarrier(int row)
    {
        if (row < 0)
            return false;

        int local = _screen.LocalPlayerIndex;
        if (local <= 0 || local >= PlayerResourceNetState.Slots || Core() is not { } core)
            return false;

        int localTeam = core.State.Team[local];
        return _players.Networked(row) is { } victim
            && victim.Class() == TFClass.Spy
            && core.State.Team[row] != localTeam
            && (victim.DisguiseTeam == localTeam || victim.IsStealthed());
    }

    /// <summary>The resource row of a user id, -1 when no connected row
    /// carries it (the scan the spy skip and the anchor share).</summary>
    private int RowOf(int userId)
    {
        if (Core() is not { } core)
            return -1;

        for (int i = 1; i < PlayerResourceNetState.Slots; i++)
            if (core.State.IsConnected(i) && core.State.UserId[i] == userId)
                return i;

        return -1;
    }

    /// <summary>
    /// Stock's occlusion trace (tf_hud_account.cpp:622-625): the victim's
    /// centre to the main view's origin, brushes only, seen when nothing
    /// stops it (fraction one). The centre is the standing hull's middle;
    /// stock's <c>WorldSpaceCenter</c> is the OBB's, a ducked victim's
    /// lower — the hull's constancy buys the read. The main view, not the
    /// current one: the hurt arrives outside the render's access window,
    /// where the current view's read is stock's asserted one.
    /// </summary>
    private bool Visible(Vector origin)
    {
        float middle = TFViewVectors.Params.HullMax.Z * 0.5f;
        var centre = new Vector(origin.X, origin.Y, origin.Z + middle);
        return _world.ClientTraceLine(centre, _fx.MainViewOrigin(), TraceMask.Brushes).Fraction >= 1f;
    }

    /// <summary>The anchor, stock's :630-633: the origin's crown — the
    /// hull's height, <c>VEC_HULL_MAX_SCALED</c>'s z — raised again by how
    /// far the victim stands from the local player, one to 16 units over
    /// the 200.</summary>
    private Vector Above(Vector origin)
    {
        Vector local = _screen.LocalPlayerOrigin ?? default;
        float offset = CritBucket.RemapValClamped((origin - local).LengthSquared, 0f, 200f * 200f, 1f, 16f);
        return new Vector(origin.X, origin.Y, origin.Z + TFViewVectors.Params.HullMax.Z + offset);
    }

    /// <summary>The core's <c>tf_player_manager</c> row source, the local
    /// index's and the user ids' holder (the death screen's read).</summary>
    private ClientPlayerResource? Core()
        => _standalones.OfClass(EntityNames.PlayerManager).OfType<ClientPlayerResource>().FirstOrDefault();

    public int Count
    {
        get
        {
            Refresh();
            return _count;
        }
    }

    public TFCombatNumber Number(int i) => _numbers[i];

    /// <summary>Stock's paint (tf_hud_account.cpp:1120-1131): the anchor
    /// risen by the lifetime's share of the 32 units, through the engine's
    /// hud projection; the pixels are the screen's to place.</summary>
    public bool Screen(int i, float lifetimePercent, out Vector2 hud)
    {
        hud = default;
        if (i < 0 || i >= _count || !_anchored[i])
            return false;

        Vector world = _worlds[i];
        world.Z += TFCombatNumber.Rise * lifetimePercent;   // :638, the float up
        Vector px = _beams.ScreenPosition(in world);
        hud = new(px.X, px.Y);
        return true;
    }

    /// <summary>The expired leave; the clock going back (a new level) purges
    /// the rest, as stock's Reset does.</summary>
    private void Refresh()
    {
        float now = _clock.Now;
        bool newLevel = now < _seen;
        _seen = now;
        for (int i = _count - 1; i >= 0; i--)
            if (newLevel || _numbers[i].At + DeltaLifetime < now)
                RemoveAt(i);
    }

    private void RemoveAt(int i)
    {
        for (int j = i; j < _count - 1; j++)
        {
            _numbers[j] = _numbers[j + 1];
            _worlds[j] = _worlds[j + 1];
            _anchored[j] = _anchored[j + 1];
        }

        _count--;
        _numbers[_count] = default;
        _worlds[_count] = default;
        _anchored[_count] = false;
    }
}

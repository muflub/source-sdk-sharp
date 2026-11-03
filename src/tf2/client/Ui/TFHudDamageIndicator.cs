//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

namespace TF2.Client.Ui;

/// <summary>
/// The damage indicator's reads (stock's <c>CHudDamageIndicator</c>,
/// tf_hud_damageindicator.cpp): each Damage user message a hit recorded
/// with its scale, origin and show time (<c>MsgFunc_Damage</c> :281-314),
/// kept until <c>At + Life</c> and resolved against the frame's view
/// (<c>GetDamagePosition</c> reads MainViewOrigin/Angles at paint time).
/// The scale is what was dealt — the armour save plus the health take, our
/// payload's split of stock's <c>WRITE_SHORT(info.GetDamage())</c>
/// (tf_player.cpp:9525) — capped at the MaximumDamage 100 (:289-292), and
/// the show time remaps off it (:297). A hit with no origin records
/// nothing (:286-287) and the server's empty reports change nothing (the
/// core Hud's rule). The delta is stock's: taken at message time against
/// that moment's view, normalized, jittered by <c>m_flNoise</c> — one
/// <c>random->RandomFloat(-Noise, Noise)</c> per axis — and normalized
/// again (:304-311); <c>GetDamagePosition</c> only ever turns the frozen
/// direction to the frame's view angles (:161-162). The floating damage
/// numbers (stock's hud_combattext,
/// tf_hud_account.cpp:620-646) key off <c>player_hurt</c> for damage the
/// local player DEALT — a carrier we do not have yet — so the screen shows
/// these hits' amounts in their direction instead.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFDamageHistory)])]
public sealed class TFHudDamageIndicator : ITFDamageHistory, IDamageListener
{
    /// <summary>Stock's CPanelAnimationVar MaximumDamage (the scale's cap).</summary>
    public const int MaximumDamage = 100;

    /// <summary>Stock's MinimumTime/MaximumTime: the show time remaps from
    /// the amount over these (tf_hud_damageindicator.cpp:297).</summary>
    public const float MinimumTime = 1f, MaximumTime = 2f;

    /// <summary>Stock's CPanelAnimationVar Noise, the per-axis jitter drawn
    /// at message time (tf_hud_damageindicator.cpp:69). The panel reads no
    /// .res values of ours, so the declared default stands.</summary>
    public const float Noise = 0.1f;

    /// <summary>Stock keeps every hit (a CUtlVector); a frame draws at most
    /// a handful, and the oldest expiring first keeps the list short — the
    /// cap only bounds a pathological flood.</summary>
    public const int MaxDamages = 16;

    private readonly IGameClock _clock;
    private readonly ICurrentView _view;
    private readonly IUniformRandom _random;
    private readonly IFxRender _fx;
    private readonly TFDamageTaken[] _hits = new TFDamageTaken[MaxDamages];
    private readonly Vector[] _deltas = new Vector[MaxDamages];
    private int _count;
    private float _seen;
    private bool _resolved;
    private int _viewId;

    public TFHudDamageIndicator(IGameClock clock, ICurrentView view, IUniformRandom random, IFxRender fx)
        => (_clock, _view, _random, _fx) = (clock, view, random, fx);

    /// <summary>One Damage message (<c>MsgFunc_Damage</c>), handed over by
    /// the core Hud's hook.</summary>
    void IDamageListener.OnDamage(DamageArgs hit)
    {
        if (hit.Origin.X == 0f && hit.Origin.Y == 0f && hit.Origin.Z == 0f)
            return;                                  // no origin: the !ReadOneBit arm

        int amount = Math.Min(hit.Armour + hit.Health, MaximumDamage);
        if (amount <= 0)
            return;                                  // the server's empty reports

        // RemapVal(iScale, 0, m_iMaximumDamage, m_flMinimumTime, m_flMaximumTime).
        float life = MinimumTime + (MaximumTime - MinimumTime) * amount / MaximumDamage;

        // The delta of the moment against the main view's origin — the
        // message can arrive outside the render's access window, where the
        // current view's read is stock's asserted one (the message-time
        // arm's `vecFrom - MainViewOrigin()`, tf_hud_damageindicator's
        // stock :357); then the noise, one RandomFloat(-m_flNoise,
        // m_flNoise) per axis, and the renormalize (:304-311).
        Vector delta = (hit.Origin - _fx.MainViewOrigin()).Normalized;
        delta = new Vector(delta.X + _random.RandomFloat(-Noise, Noise),
                           delta.Y + _random.RandomFloat(-Noise, Noise),
                           delta.Z + _random.RandomFloat(-Noise, Noise)).Normalized;

        if (_count >= MaxDamages)
            RemoveAt(0);                             // the oldest makes room
        _hits[_count] = new TFDamageTaken(amount, 0f, _clock.Now, life);
        _deltas[_count] = delta;
        _count++;
    }

    public int Count
    {
        get
        {
            Refresh();
            Resolve();
            return _count;
        }
    }

    public TFDamageTaken Damage(int i) => _hits[i];

    /// <summary>The expired leave; the clock going back (a new level) purges
    /// the rest, as stock's Reset does.</summary>
    private void Refresh()
    {
        float now = _clock.Now;
        bool newLevel = now < _seen;
        _seen = now;
        for (int i = _count - 1; i >= 0; i--)
            if (newLevel || _hits[i].At + _hits[i].Life < now)
                RemoveAt(i);
    }

    /// <summary>The direction against the frame's view, resolved once per
    /// view id: the frozen delta only ever turns to the frame's forward —
    /// <c>GetDamagePosition</c>'s front/side dots (:161-162), the angle the
    /// yaw already is.</summary>
    private void Resolve()
    {
        var now = _view.Read();
        if (_resolved && now.Id == _viewId)
            return;
        (_resolved, _viewId) = (true, now.Id);
        float viewYaw = MathF.Atan2(now.Forward.Y, now.Forward.X) * (180f / MathF.PI);
        for (int i = 0; i < _count; i++)
        {
            float sourceYaw = MathF.Atan2(_deltas[i].Y, _deltas[i].X) * (180f / MathF.PI);
            float yaw = ((viewYaw - sourceYaw) % 360f + 540f) % 360f - 180f;
            _hits[i] = _hits[i] with { Yaw = yaw };
        }
    }

    private void RemoveAt(int i)
    {
        for (int j = i; j < _count - 1; j++)
        {
            _hits[j] = _hits[j + 1];
            _deltas[j] = _deltas[j + 1];
        }
        _count--;
        _hits[_count] = default;
        _deltas[_count] = default;
    }
}

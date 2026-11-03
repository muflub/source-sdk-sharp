//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Shared.Combat;

namespace TF2.Client.Ui;

/// <summary>
/// The death notices the kill feed screen draws (stock's
/// <c>CTFHudDeathNotice</c>, tf_hud_deathnotice.cpp over
/// hud_basedeathnotice.cpp): each <c>player_death</c> becomes a line, with
/// the decode stock's <c>FireGameEvent</c> does — the names and teams off the
/// player resource (<c>IKillFeedPlayers</c> here, g_PR there :473-482,
/// :509-510), the suicide arm (<c>!killer || killer == victim</c>, :517-520),
/// the <c>d_</c> + weapon icon (:513-516), the custom-kill overrides
/// (<c>OnGameEvent</c>, tf_hud_deathnotice.cpp:975-1039) and the crit mark
/// (<c>damagebits</c> &amp; DMG_CRITICAL, hud_basedeathnotice.cpp:497-501).
///
/// <para>
/// The queue is stock's: hud_deathnotice_time 6, 0 showing none
/// (:406-409); at most MaxDeathNotices 4 (hudlayout.res:510), the overflow
/// culled non-local-involved lines first and then the oldest
/// (<c>RetireExpiredDeathNotices</c>, hud_basedeathnotice.cpp:352-382); a
/// line the local player is involved in stays twice as long
/// (<c>GetExpiryTime</c>, :923-931); the clock going back (a new level)
/// purges the queue as stock's VidInit does (:77-80). The console record and
/// the event subscription are the core feed's
/// (<see cref="SourceSharp.Client.Vgui.IKillFeedListener"/>: stock's subclass
/// decodes before <c>AddDeathNotice</c> stores); this queue holds only what
/// the screen draws.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFKillFeed)])]
public sealed class TFKillFeedSource : ITFKillFeed, IKillFeedListener
{
    /// <summary>Stock's MaxDeathNotices (hudlayout.res:510; the .h's default too).</summary>
    public const int MaxLines = 4;

    private readonly IGameClock _clock;
    private readonly IKillFeedPlayers _players;
    private readonly HudDeathNoticeTimeConVar? _time;
    private readonly TFKillFeedLine[] _lines = new TFKillFeedLine[MaxLines];
    private readonly float[] _expires = new float[MaxLines];
    private int _count;
    private float _seen;

    public TFKillFeedSource(IGameClock clock, IKillFeedPlayers players, HudDeathNoticeTimeConVar? time = null)
        => (_clock, _players, _time) = (clock, players, time);

    void IKillFeedListener.OnDeath(PlayerDeathArgs death) => Add(death);

    public int LineCount
    {
        get
        {
            Refresh();
            return _count;
        }
    }

    public TFKillFeedLine Line(int i) => _lines[i];

    /// <summary>One player_death (<c>FireGameEvent</c>'s player-death arm).</summary>
    public void Add(PlayerDeathArgs death)
    {
        float display = _time?.Value ?? 6f;
        if (display == 0f)
            return;

        bool hasKiller = _players.TryFind(death.AttackerUserId, out string killer, out int killerTeam, out bool killerLocal);
        bool hasVictim = _players.TryFind(death.UserId, out string victim, out int victimTeam, out bool victimLocal);
        if (!hasKiller)
            (killer, killerTeam, killerLocal) = ("", 0, false);
        if (!hasVictim)
            (victim, victimTeam, victimLocal) = ("", 0, false);

        // Stock: iSuicide = ( !killer || killer == victim ), on indices (:517).
        bool suicide = !hasKiller || death.AttackerUserId == death.UserId;
        bool local = (hasKiller && killerLocal) || (hasVictim && victimLocal);
        bool selfKill = death.AttackerUserId == death.UserId;

        string icon = Icon(death.Weapon ?? "", death.Custom, selfKill);

        if (_count >= MaxLines)
            RemoveAt(CullFirst());
        _lines[_count] = new TFKillFeedLine(suicide ? "" : killer, killerTeam, victim, victimTeam,
                                            icon, Inverted(icon, local),
                                            (death.Bits & (int)TFDamageBits.Critical) != 0,
                                            suicide, local);
        // GetExpiryTime: the local player's lines last twice as long (:923-931).
        _expires[_count] = _clock.Now + (local ? display * 2f : display);
        _seen = MathF.Max(_seen, _clock.Now);
        _count++;
    }

    /// <summary>
    /// Drops the lines whose time is up, culls the overflow non-local first
    /// (<c>RetireExpiredDeathNotices</c>), and purges on the clock going back
    /// (VidInit's RemoveAll on a new level).
    /// </summary>
    private void Refresh()
    {
        float now = _clock.Now;
        bool newLevel = now < _seen;
        _seen = now;
        for (int i = _count - 1; i >= 0; i--)
            if (newLevel || _expires[i] < now)
                RemoveAt(i);

        for (int over = _count - MaxLines; over > 0; over--)
            RemoveAt(CullFirst());
    }

    /// <summary>Who to drop first: a non-local-involved line, else the
    /// oldest (hud_basedeathnotice.cpp:355-367 — the walk never touches the
    /// newest, and the oldest-first pass takes the rest).</summary>
    private int CullFirst()
    {
        for (int i = 0; i < _count - 1; i++)
            if (!_lines[i].LocalInvolved)
                return i;
        return 0;
    }

    private void RemoveAt(int i)
    {
        for (int j = i; j < _count - 1; j++)
        {
            _lines[j] = _lines[j + 1];
            _expires[j] = _expires[j + 1];
        }
        _count--;
        _lines[_count] = default;
    }

    /// <summary>The inverted art's name for a line the local player is in:
    /// <c>dneg_</c> + the suffix (stock's GetIcon kDeathNoticeIcon_Inverted
    /// arm, hud_basedeathnotice.cpp:894-913 — only a <c>d_</c>-prefixed icon
    /// has one). Built once per death, never per frame.</summary>
    private static string Inverted(string icon, bool local)
        => local && icon.Length > 2 ? string.Concat("dneg_", icon.AsSpan(2)) : "";

    /// <summary>
    /// The icon: <c>d_</c> + weapon (hud_basedeathnotice.cpp:513-516), then the
    /// custom-kill overrides (<c>OnGameEvent</c>'s switch,
    /// tf_hud_deathnotice.cpp:975-1039). The per-weapon headshot art keys off
    /// the weapon name the event carries. An empty weapon names no icon —
    /// stock's szIcon stays unset and the skull fallback answers.
    /// </summary>
    public static string Icon(string weapon, int custom, bool selfKill)
    {
        string icon = weapon.Length > 0 ? $"d_{weapon}" : "";
        switch ((TFDamageCustom)custom)
        {
            case TFDamageCustom.Backstab:
                icon = "d_backstab";                       // :984 (no sharp dresser yet)
                break;
            case TFDamageCustom.Headshot or TFDamageCustom.HeadshotDecapitation:
                icon = weapon switch                       // :990-1009
                {
                    "ambassador" => "d_ambassador_headshot",
                    "huntsman" => "d_huntsman_headshot",
                    _ => "d_headshot",                     // (no penetration count yet)
                };
                break;
            case TFDamageCustom.Burning when selfKill:
                icon = "d_firedeath";                      // :1014-1020: suicide by fire only
                break;
            case TFDamageCustom.BurningArrow:
                icon = "d_huntsman_burning";               // :1023-1027
                break;
            case TFDamageCustom.Flyingburn:
                icon = "d_huntsman_flyingburn";            // :1029-1033
                break;
            case TFDamageCustom.PumpkinBomb:
                icon = "d_pumpkindeath";                   // :1035-1039
                break;
        }
        return icon;
    }
}

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
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Client.Ui;

/// <summary>
/// The target ID's reads (tf_hud_target_id.cpp): the view's centre ray
/// (UpdateIDTarget, c_tf_player.cpp:7041-7120 — the wall trace first, then
/// the players before it, past the local player), the aimed player's
/// identity over the engine's player info and the <c>tf_player_manager</c>
/// row, and the disguise and charge fields off the aimed player's decoded
/// <c>tf_player_data</c> carrier. One <see cref="CurrentViewState"/>
/// resolves the frame: every property the screen asks in a Describe answers
/// the same ray, and the resolution is allocation-free (the clip's candidate
/// indices are one scratch array, refilled only when the tracer changes).
/// </summary>
[SideService(Side.Client, As = [typeof(ITFTargetId)])]
public sealed class TFHudTargetId(IEngineHud screen, IEngineWorldQuery world, IPixelVisSlots view,
                                  IClientStandalones standalones, ITFPlayerData data,
                                  TFClientPlayerResources resources, IPlayerNames names,
                                  WeaponCatalog catalog, TFClassTable classes) : ITFTargetId
{
    /// <summary>MAX_TRACE_LENGTH, as UpdateIDTarget traces.</summary>
    private const float MaxTrace = 56756f;

    /// <summary>TEAM_SPECTATOR (tf_shareddefs.h), the validity gate's own arm.</summary>
    private const int Spectator = 1;

    /// <summary>item_shop_medigun's classname — the hand whose charge the data string reads.</summary>
    private const string MedigunName = "tf_weapon_medigun";

    private bool _resolved;
    private int _viewId;
    private float _ox, _oy, _oz, _fx, _fy, _fz;

    private bool _present;
    private string _name = "";
    private int _team, _health, _maxHealth;
    private bool _showsHealth;
    private TFTargetIdLine _line;

    public bool Present => Resolve();
    public string Name => Resolve() ? _name : "";
    public int Team => Resolve() ? _team : 0;
    public bool ShowsHealth => Resolve() && _showsHealth;
    public int Health => Resolve() ? _health : 0;
    public int MaxHealth => Resolve() ? _maxHealth : 0;
    public TFTargetIdLine Line => Resolve() ? _line : default;

    /// <summary>
    /// The frame's ray, resolved once per view. Stock's UpdateID names
    /// nothing for a plain enemy (the printFormatString arm,
    /// tf_hud_target_id.cpp:806-816, answers only the validity gate's teams
    /// and a spy/medic/heavy local — a scout aiming at an enemy draws
    /// nothing, <c>IsValidIDTarget</c> :455), so <see cref="Present"/> is
    /// that gate plus "something would show".
    /// </summary>
    private bool Resolve()
    {
        var now = view.View();
        if (_resolved && now.Id == _viewId && now.Origin.X == _ox && now.Origin.Y == _oy && now.Origin.Z == _oz
            && now.Forward.X == _fx && now.Forward.Y == _fy && now.Forward.Z == _fz)
            return _present;

        (_viewId, _ox, _oy, _oz, _fx, _fy, _fz) = (now.Id, now.Origin.X, now.Origin.Y, now.Origin.Z,
                                                   now.Forward.X, now.Forward.Y, now.Forward.Z);
        _resolved = true;
        _present = false;
        _name = "";
        _team = 0;
        _health = 0;
        _maxHealth = 0;
        _showsHealth = false;
        _line = default;

        int local = screen.LocalPlayerIndex;
        if (local is not (> 0 and < TFPlayerResourceNetState.Slots))
            return false;

        // The observer arm (CMainTargetID::ShouldDraw :1201-1210): the panel
        // belongs to a playing player; the spectator bar has its own.
        if (standalones.GetByIndex(local) is ClientSsPlayer { State.ObserverMode: > 0 })
            return false;

        var localCarrier = data.Networked(local);
        if (localCarrier is { } lc && lc.InCond(TFCond.Taunting))
            return false;                                // the taunt arm (:525-529)

        // The ray: the wall first (the solid trace), then the players before
        // it — a player behind a wall is nobody's target.
        Vector end = now.Origin + now.Forward * MaxTrace;
        var wall = world.ClientTraceLine(now.Origin, end, TraceMask.Solid, TraceFilter.Simple, local);
        var hit = world.ClientClipRayToEntities(now.Origin, wall.Hit ? wall.End : end,
                                                TraceMask.Shot, AllBut(local));
        if (!hit.Hit || hit.EntityIndex <= 0)
            return false;

        if (standalones.GetByIndex(hit.EntityIndex) is not ClientSsPlayer target)
            return false;        // players only: stock's buildings and flags are the later game-state feeds

        var carrier = data.Networked(hit.EntityIndex);
        if (carrier is null)
            return false;        // no carrier: no class, no disguise, no team the panel could name

        // A cloaked target is nobody's (IsValidIDTarget :414-419, :771).
        if (carrier.IsStealthed())
            return false;

        var state = carrier;
        bool disguised = state.InCond(TFCond.Disguised);

        int realTeam = TeamOf(hit.EntityIndex);
        int localRealTeam = TeamOf(local);
        int displayedTeam = disguised ? state.DisguiseTeam : realTeam;
        int localDisplayedTeam = localCarrier is { } l && l.InCond(TFCond.Disguised)
            ? l.DisguiseTeam : localRealTeam;
        int localClass = localCarrier?.PlayerClass ?? 0;

        // The validity gate (:455): the spectator's, the real teams', the
        // displayed teams' (InSameDisguisedTeam's three-way,
        // c_tf_player.cpp:10129-10154) or a local spy's — and a cloaked
        // target is none of them (:414-419).
        bool spectator = localRealTeam == Spectator;
        bool displayedSame = displayedTeam == localDisplayedTeam;
        bool realSame = realTeam == localRealTeam;
        bool same = displayedSame || realSame || displayedTeam == localRealTeam;
        bool localSpy = (TFClass)localClass == TFClass.Spy;
        if (!(spectator || realSame || displayedSame || localSpy))
            return false;

        // The content gate (:806-810): the name and the health cross.
        bool showName = spectator || same || localSpy
            || (TFClass)localClass == TFClass.Medic
            || (TFClass)localClass == TFClass.HeavyWeapons;
        if (showName && names.TryGet(hit.EntityIndex, out ClientPlayerInfo info))
            _name = info.Name;

        _team = disguised && !realSame ? state.DisguiseTeam : realTeam;
        _maxHealth = MaxHealthOf(hit.EntityIndex);
        _showsHealth = showName && _maxHealth > 0;
        _health = Math.Max(0, target.State.Health);

        // The disguised enemy's row (tf_hud_target_id.cpp:785-794, :822-827):
        // the borrowed name — the disguise target's — and the fake health
        // over the disguise class's max (GetDisguiseMaxHealth,
        // tf_player_shared.cpp:8358-8369, whose no-data arm answers the
        // target's own row here).
        if (disguised && !realSame && state.DisguiseTargetIndex > 0
            && names.TryGet(state.DisguiseTargetIndex, out ClientPlayerInfo borrowed))
        {
            _name = borrowed.Name;
            _health = Math.Max(0, state.DisguiseHealth);
            _maxHealth = DisguiseMaxHealth((TFClass)state.DisguiseClass, hit.EntityIndex);
        }

        // GetTargetIDDataString (c_tf_player.cpp:9748-9829), in its order:
        // the disguise lines first, then the medic's charge over them.
        var line = default(TFTargetIdLine);
        if (disguised)
        {
            var disguiseClass = (TFClass)state.DisguiseClass;
            if (realSame)
                line = new(TFTargetIdLineKind.Disguise, TFClassNames.NonLocalized(disguiseClass),
                           Enemy: state.DisguiseTeam != realTeam, 0);
            else if (disguiseClass == TFClass.Spy)
                line = new(TFTargetIdLineKind.Disguise, TFClassNames.NonLocalized(TFClass.Spy),
                           Enemy: true, 0);              // the fake friendly-spy line (:9771-9779)
        }

        if ((TFClass)state.PlayerClass == TFClass.Medic)
        {
            // MedicGetChargeLevel: the target's own medigun's charge, 0 when
            // the hands hold something else (c_tf_player.cpp:9783-9789).
            float charge = CarriedWeapon(hit.EntityIndex) is { } gun
                           && gun.WeaponTypeId == catalog.IndexOf(MedigunName)
                ? Math.Clamp(gun.BehaviourMeter, 0f, 1f) : 0f;
            line = new(TFTargetIdLineKind.MedicCharge, "", false, (int)(charge * 100f));
        }
        else if (disguised && (TFClass)state.DisguiseClass == TFClass.Medic && !realSame)
            line = new(TFTargetIdLineKind.MedicCharge, "", false, 0);   // the fake charge (:9801-9805)
        _line = line;

        _present = _name.Length > 0 || _line.Kind != TFTargetIdLineKind.None || _showsHealth;
        return _present;
    }

    /// <summary>A player's team (<c>g_TF_PR-&gt;GetTeamNumber</c>) and max
    /// health (<c>GetMaxHealth</c>) off the live cross's rows; 0 without
    /// the row (the :835-838 arm hides the health).</summary>
    private int TeamOf(int playerIndex) =>
        resources.Live is { } live && playerIndex > 0 && playerIndex < TFPlayerResourceNetState.Slots
            ? live.State.Team[playerIndex] : 0;

    private int MaxHealthOf(int playerIndex) =>
        resources.Live is { } live && playerIndex > 0 && playerIndex < TFPlayerResourceNetState.Slots
            ? live.State.MaxHealth[playerIndex] : 0;

    /// <summary>The disguise class's max health (<c>GetDisguiseMaxHealth</c>,
    /// tf_player_shared.cpp:8358-8369); a class without data answers the
    /// target's own row, as stock's answers the spy's own max.</summary>
    private int DisguiseMaxHealth(TFClass disguiseClass, int targetIndex)
    {
        int max = classes[disguiseClass].MaxHealth;
        return max > 0 ? max : MaxHealthOf(targetIndex);
    }

    /// <summary>A player's carried weapon's state — the one entity the core
    /// holds per armed player — or null when they hold nothing.</summary>
    private WeaponNetState? CarriedWeapon(int playerIndex)
        => standalones.Live().OfType<ClientSsWeapon>()
            .FirstOrDefault(w => w.State.OwnerIndex == playerIndex)?.State;

    /// <summary>The clip's candidate indices: every entity but the tracer's
    /// own (the standalone shells are in no spatial partition — the same
    /// list the client's weapon traces clip against), kept in one scratch
    /// array and refilled only when the tracer changes.</summary>
    private ReadOnlySpan<int> AllBut(int tracerIndex)
    {
        int[] candidates = _candidates ??= new int[EngineLimits.MaxEdicts - 2];
        if (_builtFor != tracerIndex)
        {
            int count = 0;
            for (int index = 1; index < EngineLimits.MaxEdicts && count < candidates.Length; index++)
            {
                if (index != tracerIndex)
                    candidates[count++] = index;
            }

            _builtFor = tracerIndex;
        }

        return candidates;
    }

    private int[]? _candidates;
    private int _builtFor = -1;
}

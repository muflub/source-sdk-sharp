using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// The local player as the HUD's panels read it (plan_client_ui U2), one
/// interface per concern over one snapshot: the health the managed player
/// publishes and the Arsenal's active weapon, read once a frame in
/// <see cref="Refresh"/> so the panels' reads cross nothing.
///
/// <para>
/// Armour is the local player's networked <c>PlayerNetState.Armour</c>; the
/// secondary pool is the active weapon's alt-fire ammunition against its carry
/// cap; the damage direction is the last hit's origin against the current view
/// (NaN, every edge, when the hit had no origin). The weapon's name is its
/// script's print name, localised (<see cref="WeaponNames"/>). The team is the
/// local slot's on the player resource, team play the roster's answer
/// (<see cref="TeamPlay"/>), and the team's name the scoreboard's through the
/// mods' rules (<see cref="TeamNames"/>). Each optional source left out
/// answers "none".
/// </para>
/// </summary>
public sealed class LocalPlayerUi(IEngineHud screen, ILocalArsenal arsenal, Hud hud,
                                  IClientStandalones? standalones = null, WeaponNames? names = null,
                                  ViewReadout? view = null, TeamNames? teamNames = null, Vgui.IActiveMode? catalog = null)
    : IPlayerLife, IPlayerHealth, IPlayerArmour, IPlayerWeapon, IPlayerAmmo, IPlayerDamage, IPlayerSuitPower, IPlayerTeam
{
    private readonly TeamNames _teamNames = teamNames ?? TeamNames.Stock();
    private Standalone.ClientPlayerResource? _resource;
    private bool _teamplay;
    private int _team;
    private string _teamName = string.Empty;
    private int _teamNamed = -1;

    private int _armour;
    private float _suitPower = 100f;
    private int _suitDevices;
    private int _secondary = -1, _secondaryMax;
    private float _damageYaw = float.NaN;

    public const int Max = 100;

    private int _health = -1;
    private bool _hasWeapon;
    private string _weapon = string.Empty;
    private int _clip = -1, _reserve = -1;

    public void Refresh()
    {
        _health = screen.LocalPlayerHealth;
        _hasWeapon = arsenal.TryGetActive(out string name, out _clip, out _reserve);
        _weapon = _hasWeapon ? (names?.Display(name) ?? name) : string.Empty;
        if (!_hasWeapon)
            (_clip, _reserve) = (-1, -1);
        if (!_hasWeapon || !arsenal.TryGetSecondary(out _secondary, out _secondaryMax))
            (_secondary, _secondaryMax) = (-1, 0);

        _armour = 0;
        (_suitPower, _suitDevices) = (100f, 0);
        if (standalones is not null && _health > 0
            && standalones.GetByIndex(screen.LocalPlayerIndex) is Standalone.ClientSsPlayer local)
        {
            _armour = local.State.Armour;
            (_suitPower, _suitDevices) = (local.State.SuitPower, local.State.SuitDevices);
        }

        _damageYaw = view is { Known: true } ? DamageYaw(view.Eye, view.Yaw, hud.LastDamageOrigin) : float.NaN;
        RefreshTeam();
    }

    /// <summary>The local slot's team and whether the game is team play, off the player resource.</summary>
    private void RefreshTeam()
    {
        (_teamplay, _team) = (false, 0);
        if (standalones is null)
            return;
        // Found again only when the one held has left (a new level): no search a frame.
        if (_resource is null || !standalones.Live().Contains(_resource))
            _resource = standalones.OfClass(EntityNames.PlayerManager).OfType<Standalone.ClientPlayerResource>().FirstOrDefault();
        if (_resource is not { } r)
            return;
        var state = r.State;
        _teamplay = TeamPlay.Of(catalog, state);
        int local = screen.LocalPlayerIndex;
        _team = local > 0 && local < PlayerResourceNetState.Slots ? state.Team[local] : 0;
        if (_team != _teamNamed)
        {
            _teamNamed = _team;
            _teamName = _teamNames.Of(_team, ScoreboardUi.TeamName(_team));
        }
    }

    /// <summary>
    /// Where a hit came from, in degrees from straight ahead: + to the right,
    /// - to the left, ±180 behind (the damage panel's convention). NaN when the
    /// hit has no origin (zero: the world, or a game that did not say).
    /// </summary>
    public static float DamageYaw(Vector eye, float viewYaw, Vector origin)
    {
        if (origin.X == 0f && origin.Y == 0f && origin.Z == 0f)
            return float.NaN;
        float dx = origin.X - eye.X, dy = origin.Y - eye.Y;
        if (dx == 0f && dy == 0f)
            return float.NaN;
        // Source's yaw turns left (counter-clockwise from above); the panel's to the right.
        float sourceYaw = MathF.Atan2(dy, dx) * (180f / MathF.PI);
        float d = viewYaw - sourceYaw;
        d = ((d % 360f) + 540f) % 360f - 180f;
        return d;
    }

    /// <summary>Alive: a player with health. -1 is no player (the menu, between levels).</summary>
    public bool Alive => _health > 0;

    public int Health => Math.Max(0, _health);

    /// <summary>The health as the engine answered it: -1 when there is no player.</summary>
    public int RawHealth => _health;

    public int MaxHealth => Max;

    public int Armour => _armour;

    public int MaxArmour => Max;

    /// <summary>The suit's power, full when there is no player (so nothing shows).</summary>
    public float SuitPower => _suitPower;

    public int SuitDevices => _suitDevices;

    public bool HasWeapon => _hasWeapon;

    public string WeaponName => _weapon;

    public int Clip => _clip;

    public int Reserve => _reserve;

    public int Secondary => _secondary;

    public int SecondaryMax => _secondaryMax;

    public double LastDamageAt => hud.LastDamageAt;

    public float LastDamageYaw => _damageYaw;

    public bool Teamplay => _teamplay;

    public int Team => _team;

    public string TeamName => _teamName;
}

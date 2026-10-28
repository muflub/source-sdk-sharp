//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>The local player as the weapon selection reads them, once per call.</summary>
public interface IWeaponSelectionPlayer
{
    /// <summary>A local player to select for.</summary>
    bool Exists { get; }

    /// <summary>
    /// CHudElement::ShouldDraw for the selection's hidden bits
    /// (HIDEHUD_WEAPONSELECTION, PLAYERDEAD, INVEHICLE, NEEDSUIT): alive, here.
    /// </summary>
    bool HudAllowed { get; }

    /// <summary>FL_FROZEN or dead: OnThink closes an open selection.</summary>
    bool FrozenOrDead { get; }

    /// <summary>PlayerNetState.CarriedWeapons: bit per WeaponTypes index.</summary>
    uint Carried { get; }

    /// <summary>PlayerNetState.EmptyWeapons: the carried ones CanBeSelected refuses.</summary>
    uint Empty { get; }

    /// <summary>The weapon in hand, as a WeaponTypes index; -1 for none.</summary>
    int Active { get; }
}

/// <summary>What the selection does to the world: a choice, the last weapon, and its sounds.</summary>
public interface IWeaponSelectionActions
{
    /// <summary>input-&gt;MakeWeaponSelection: switch to this WeaponTypes index.</summary>
    void Select(int weaponType);

    /// <summary>SwitchToLastWeapon: the weapon held before this one.</summary>
    void LastWeapon();

    /// <summary>player-&gt;EmitSound: a sound the local player makes.</summary>
    void Sound(string soundName);
}

/// <summary>A weapon type's place in the HUD: its script's bucket and bucket_position.</summary>
public interface IWeaponSlots
{
    /// <summary>WeaponTypes rows.</summary>
    int Count { get; }

    int Slot(int weaponType);

    int Position(int weaponType);
}

/// <summary>
/// The weapon selection: stock's CBaseHudWeaponSelection (weapon_selection.cpp)
/// and the choices of HL2's CHudWeaponSelection (hl2/hud_weaponselection.cpp)
/// that HL2MP draws, for hud_fastswitch 0 (the buckets) and 1 (fastswitch, which
/// switches at once and shows nothing). The panel reads its state; the binds,
/// the cancel key and the attack buttons drive it.
/// </summary>
public sealed class WeaponSelectionHud : Input.IHudProcessInput
{
    public const int HudTypeBuckets = 0, HudTypeFastSwitch = 1;

    /// <summary>MAX_WEAPON_SLOTS, MAX_WEAPON_POSITIONS.</summary>
    public const int MaxSlots = 6, MaxPositions = 20;

    /// <summary>SELECTION_TIMEOUT_THRESHOLD and SELECTION_FADEOUT_TIME.</summary>
    public const float SelectionTimeout = 0.5f, SelectionFadeoutTime = 0.75f;

    /// <summary>FASTSWITCH_DISPLAY_TIMEOUT and FASTSWITCH_FADEOUT_TIME.</summary>
    public const float FastSwitchTimeout = 1.5f, FastSwitchFadeoutTime = 1.5f;

    /// <summary>OpenWeaponSelectionMenu's 0.1 s, FadeOutWeaponSelectionMenu's 0.5 s delay and 1.0 s fade.</summary>
    public const float OpenTime = 0.1f, FadeDelay = 0.5f, FadeTime = 1f;

    /// <summary>The menu's Alpha (128) against its SelectionAlpha (255): what an unselected box is drawn at.</summary>
    public const float UnselectedAlpha = 128f / 255f;

    private readonly IWeaponSelectionPlayer _player;
    private readonly IWeaponSelectionActions _actions;
    private readonly IWeaponSlots _slots;
    private readonly IGameClock _clock;
    private readonly Func<int> _fastSwitch;

    public WeaponSelectionHud(IWeaponSelectionPlayer player, IWeaponSelectionActions actions, IWeaponSlots slots,
                              IGameClock clock, Func<int> fastSwitch)
    {
        _player = player;
        _actions = actions;
        _slots = slots;
        _clock = clock;
        _fastSwitch = fastSwitch;
    }

    private bool _visible;
    private int _selected = -1;
    private float _selectionTime;
    private bool _fadingOut;

    // When FadeOutWeaponSelectionMenu started: the Think that saw the timeout.
    private float _fadeStart;

    // OpenWeaponSelectionMenu ramps to full from wherever the fade had got.
    private float _rampStart;
    private float _rampFrom;

    /// <summary>IsInSelectionMode: the selection is open.</summary>
    public bool Open => _visible;

    /// <summary>The weapon the selection is on, as a WeaponTypes index; -1 for none.</summary>
    public int Selected => _selected;

    /// <summary>hud_fastswitch, as stock reads it each time.</summary>
    public int Mode => _fastSwitch();

    //-------------------------------------------------------------------------
    // What the panel draws.
    //-------------------------------------------------------------------------

    /// <summary>
    /// CHudWeaponSelection::ShouldDraw, for the buckets (fastswitch draws
    /// nothing: its Paint has no case for it).
    /// </summary>
    public bool Showing => _player.Exists && _player.HudAllowed && _visible && Mode == HudTypeBuckets;

    /// <summary>
    /// SelectionAlpha over 255 at this instant: up over 0.1 s as it opens, held,
    /// and down over a second from half a second after the Think that saw the
    /// timeout; a key while it fades brings it back up from where it got.
    /// </summary>
    public float Opacity
    {
        get
        {
            float now = _clock.Now;
            float opened = Math.Clamp(_rampFrom + (1f - _rampFrom) * ((now - _rampStart) / OpenTime), 0f, 1f);
            if (!_fadingOut)
                return opened;

            float fade = Math.Clamp((now - _fadeStart - FadeDelay) / FadeTime, 0f, 1f);
            return opened * (1f - fade);
        }
    }

    /// <summary>A weapon type's bucket, from its script.</summary>
    public int SlotOf(int weaponType) => _slots.Slot(weaponType);

    /// <summary>Whether the player carries a weapon in this bucket (GetFirstPos).</summary>
    public bool SlotHasWeapon(int slot) => FirstInSlot(slot) >= 0;

    /// <summary>GetLastPosInSlot: the furthest position carried in a bucket, -1 for none.</summary>
    public int LastPositionInSlot(int slot)
    {
        int last = -1;
        uint carried = _player.Carried;
        for (int t = 0; t < TypeCount; t++)
        {
            if ((carried & (1u << t)) != 0 && _slots.Slot(t) == slot && _slots.Position(t) > last)
                last = _slots.Position(t);
        }

        return last;
    }

    /// <summary>GetWeaponInSlot: the carried weapon at a bucket's position, -1 for none.</summary>
    public int WeaponInSlot(int slot, int position)
    {
        uint carried = _player.Carried;
        for (int t = 0; t < TypeCount; t++)
        {
            if ((carried & (1u << t)) != 0 && _slots.Slot(t) == slot && _slots.Position(t) == position)
                return t;
        }

        return -1;
    }

    /// <summary>CanBeSelected for a carried weapon: it has something to fire.</summary>
    public bool CanBeSelected(int weaponType)
        => weaponType >= 0 && weaponType < 32 && (_player.Empty & (1u << weaponType)) == 0;

    //-------------------------------------------------------------------------
    // The binds.
    //-------------------------------------------------------------------------

    /// <summary>UserCmd_SlotN: SelectSlot( N ).</summary>
    public void SlotCommand(int number)
    {
        // If we're not allowed to draw, ignore weapon selections
        if (!_player.HudAllowed)
            return;

        UpdateSelectionTime();
        SelectWeaponSlot(number);
    }

    /// <summary>UserCmd_NextWeapon.</summary>
    public void NextWeapon()
    {
        if (!_player.HudAllowed)
            return;

        CycleToNextWeapon();
        if (Mode > 0)
            SelectWeapon();
        UpdateSelectionTime();
    }

    /// <summary>UserCmd_PrevWeapon.</summary>
    public void PrevWeapon()
    {
        if (!_player.HudAllowed)
            return;

        CycleToPrevWeapon();
        if (Mode > 0)
            SelectWeapon();
        UpdateSelectionTime();
    }

    /// <summary>UserCmd_LastWeapon.</summary>
    public void LastWeapon()
    {
        if (!_player.HudAllowed)
            return;

        _actions.LastWeapon();
    }

    /// <summary>
    /// UserCmd_Close (cancelselect): an open selection closes; with none open
    /// the answer is false, for stock's "escape".
    /// </summary>
    public bool Close() => CancelWeaponSelection();

    /// <summary>KeyInput: the cancel key closes an open selection and is used.</summary>
    public bool CancelKey()
    {
        if (!_visible)
            return false;

        HideSelection();
        return true;
    }

    /// <summary>
    /// ProcessInput: attack or attack2 with the selection open draws the weapon
    /// on it, and the button is swallowed (true).
    /// </summary>
    public bool AttackPressed()
    {
        if (!_visible)
            return false;

        SelectWeapon();
        return true;
    }

    /// <summary>OnThink: a frozen or dead player's selection closes, and the selection times out.</summary>
    public void Think()
    {
        if (_player.FrozenOrDead && _visible)
            CancelWeaponSelection();

        float now = _clock.Now;

        // Time out after awhile of inactivity
        if (now - _selectionTime > Timeout)
        {
            if (!_fadingOut)
            {
                // start fading out
                _fadingOut = true;
                _fadeStart = now;
            }
            else if (now - _selectionTime > Timeout + FadeoutTime)
            {
                // finished fade, close
                HideSelection();
            }
        }
        else if (_fadingOut)
        {
            // stop us fading out, show the animation again
            Ramp();
            _fadingOut = false;
        }
    }

    private float Timeout => Mode != 0 ? FastSwitchTimeout : SelectionTimeout;

    private float FadeoutTime => Mode != 0 ? FastSwitchFadeoutTime : SelectionFadeoutTime;

    private int TypeCount => Math.Min(32, _slots.Count);

    //-------------------------------------------------------------------------
    // Stock's selection.
    //-------------------------------------------------------------------------

    private void UpdateSelectionTime() => _selectionTime = _clock.Now;

    private void OpenSelection()
    {
        _visible = true;
        _rampFrom = 0f;
        _rampStart = _clock.Now;
    }

    private void Ramp()
    {
        _rampFrom = Opacity;
        _rampStart = _clock.Now;
    }

    private void HideSelection()
    {
        _visible = false;
        _fadingOut = false;
    }

    /// <summary>SelectWeapon: the weapon on the selection is drawn, or refused with a sound.</summary>
    private void SelectWeapon()
    {
        if (_selected < 0)
        {
            CancelWeaponSelection();
            return;
        }

        // Don't allow selections of weapons that can't be selected (out of ammo, etc)
        if (!CanBeSelected(_selected))
        {
            _actions.Sound("Player.DenyWeaponSelection");
            return;
        }

        _actions.Select(_selected);
        _selected = -1;

        // Play the "weapon selected" sound; stock's "cancelselect" runs after it.
        _actions.Sound("Player.WeaponSelected");
        CancelWeaponSelection();
    }

    /// <summary>CancelWeaponSelection: closed with its sound when it would draw.</summary>
    private bool CancelWeaponSelection()
    {
        // ShouldDraw, not IsActive: a fastswitch's selection is closed the same
        // frame it opened, before the panel ever showed.
        if (_player.Exists && _player.HudAllowed && (_visible || FastSwitchShowing))
        {
            HideSelection();
            _selected = -1;

            // Play the "close weapon selection" sound
            _actions.Sound("Player.WeaponSelectionClose");
            return true;
        }

        return false;
    }

    // ShouldDraw's fastswitch arm: a little longer, to see what was selected.
    private bool FastSwitchShowing
        => Mode != 0 && _clock.Now - _selectionTime < FastSwitchTimeout + FastSwitchFadeoutTime;

    private bool CanBeSelectedInHud(int weaponType) => CanBeSelected(weaponType);

    private bool Carries(int weaponType) => (_player.Carried & (1u << weaponType)) != 0;

    /// <summary>GetFirstPos: the lowest position carried in a bucket.</summary>
    private int FirstInSlot(int slot)
    {
        int lowest = MaxPositions, first = -1;
        for (int t = 0; t < TypeCount; t++)
        {
            if (Carries(t) && _slots.Slot(t) == slot && _slots.Position(t) <= lowest)
            {
                lowest = _slots.Position(t);
                first = t;
            }
        }

        return first;
    }

    /// <summary>GetNextActivePos: the lowest selectable position in a bucket at or after one.</summary>
    private int NextActivePos(int slot, int position)
    {
        if (position >= MaxPositions || slot >= MaxSlots)
            return -1;

        int lowest = MaxPositions, next = -1;
        for (int t = 0; t < TypeCount; t++)
        {
            if (Carries(t) && CanBeSelectedInHud(t) && _slots.Slot(t) == slot
                && _slots.Position(t) <= lowest && _slots.Position(t) >= position)
            {
                lowest = _slots.Position(t);
                next = t;
            }
        }

        return next;
    }

    /// <summary>FindNextWeaponInWeaponSelection: the closest selectable weapon after a place.</summary>
    private int FindNext(int slot, int position)
    {
        int lowestSlot = MaxSlots, lowestPosition = MaxPositions, next = -1;
        for (int t = 0; t < TypeCount; t++)
        {
            if (!Carries(t) || !CanBeSelectedInHud(t))
                continue;

            int s = _slots.Slot(t), p = _slots.Position(t);
            if ((s > slot || (s == slot && p > position)) && (s < lowestSlot || (s == lowestSlot && p < lowestPosition)))
            {
                lowestSlot = s;
                lowestPosition = p;
                next = t;
            }
        }

        return next;
    }

    /// <summary>FindPrevWeaponInWeaponSelection: the closest selectable weapon before a place.</summary>
    private int FindPrev(int slot, int position)
    {
        int lowestSlot = -1, lowestPosition = -1, prev = -1;
        for (int t = 0; t < TypeCount; t++)
        {
            if (!Carries(t) || !CanBeSelectedInHud(t))
                continue;

            int s = _slots.Slot(t), p = _slots.Position(t);
            if ((s < slot || (s == slot && p < position)) && (s > lowestSlot || (s == lowestSlot && p > lowestPosition)))
            {
                lowestSlot = s;
                lowestPosition = p;
                prev = t;
            }
        }

        return prev;
    }

    private void CycleToNextWeapon()
    {
        int next;
        if (_visible)
        {
            // find the next selection spot
            if (_selected < 0)
                return;
            next = FindNext(_slots.Slot(_selected), _slots.Position(_selected));
        }
        else
        {
            // open selection at the current place
            int active = _player.Active;
            next = active >= 0 ? FindNext(_slots.Slot(active), _slots.Position(active)) : -1;
        }

        // wrap around back to start
        if (next < 0)
            next = FindNext(-1, -1);

        if (next >= 0)
        {
            _selected = next;
            if (!_visible)
                OpenSelection();

            // Play the "cycle to next weapon" sound
            _actions.Sound("Player.WeaponSelectionMoveSlot");
        }
    }

    private void CycleToPrevWeapon()
    {
        int next;
        if (_visible)
        {
            if (_selected < 0)
                return;
            next = FindPrev(_slots.Slot(_selected), _slots.Position(_selected));
        }
        else
        {
            int active = _player.Active;
            next = active >= 0 ? FindPrev(_slots.Slot(active), _slots.Position(active)) : -1;
        }

        // wrap around back to end of weapon list
        if (next < 0)
            next = FindPrev(MaxSlots, MaxPositions);

        if (next >= 0)
        {
            _selected = next;
            if (!_visible)
                OpenSelection();

            _actions.Sound("Player.WeaponSelectionMoveSlot");
        }
    }

    /// <summary>FastWeaponSwitch: the next weapon in the bucket, at once, or the deny sound.</summary>
    private void FastWeaponSwitch(int slot)
    {
        // see where we should start selection
        int active = _player.Active;
        int position = active >= 0 && _slots.Slot(active) == slot ? _slots.Position(active) : -1;

        // search for the weapon after the current one, in the same bucket
        int next = FindNext(slot, position);
        if (next < 0 || _slots.Slot(next) != slot)
            next = FindNext(slot, -1);

        if (next >= 0 && next != active && _slots.Slot(next) == slot)
            _actions.Select(next);
        else if (next != active)
            _actions.Sound("Player.DenyWeaponSelection");

        // kill any fastswitch display
        _selectionTime = 0f;
    }

    /// <summary>SelectWeaponSlot: a number key, as the HUD type answers it.</summary>
    private void SelectWeaponSlot(int number)
    {
        // iSlot is one higher than it should be, since it's the number key
        int slot = number - 1;
        if (slot >= MaxSlots || !_player.Exists)
            return;

        switch (Mode)
        {
            case HudTypeFastSwitch:
                FastWeaponSwitch(slot);
                return;

            case HudTypeBuckets:
            {
                int position = 0;

                // start later in the list
                if (_visible && _selected >= 0 && _slots.Slot(_selected) == slot)
                    position = _slots.Position(_selected) + 1;

                // find the weapon in this slot
                int weapon = NextActivePos(slot, position);
                if (weapon < 0)
                    weapon = NextActivePos(slot, 0);

                if (weapon >= 0)
                {
                    if (!_visible)
                        OpenSelection();

                    _selected = weapon;
                }

                break;
            }
        }

        _actions.Sound("Player.WeaponSelectionMoveSlot");
    }
}

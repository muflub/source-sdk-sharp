//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// CHudHistoryResource (history_resource.cpp): what the player has just picked
/// up, one row per pickup up the right of the screen from the bottom, each for
/// hud_drawhistory_time seconds (a refused ammo pickup half that), fading as
/// its time runs out. Rows wrap to the bottom once the panel is full.
/// </summary>
public sealed class PickupHistory
{
    /// <summary>HISTSLOT_*.</summary>
    public enum Kind { Empty = 0, Ammo = 1, Weapon = 2, Item = 3, AmmoDenied = 4 }

    /// <summary>HIST_ITEM: what one row shows and until when.</summary>
    public record struct Entry(Kind Type, int Id, string Icon, int Count, float DisplayTime);

    /// <summary>
    /// How many rows fit: hudlayout.res's HudHistoryResource is 320 tall with a
    /// history_gap of 56, and AddIconToHistory starts again from the bottom
    /// once ( gap * ( slot + 1 ) ) > tall.
    /// </summary>
    public const int MaxSlots = 5;

    /// <summary>Paint's fade: the row's alpha is the seconds it has left times 80, at most 255.</summary>
    public const float AlphaPerSecond = 80f;

    public PickupHistory(IGameClock clock, Func<float> drawTime)
    {
        _clock = clock;
        _drawTime = drawTime;
    }

    private readonly IGameClock _clock;
    private readonly Func<float> _drawTime;
    private readonly List<Entry> _slots = new();

    /// <summary>m_iCurrentHistorySlot: where the next pickup lands.</summary>
    private int _current;

    /// <summary>The rows, by slot from the bottom; an empty one has <see cref="Kind.Empty"/>.</summary>
    public IReadOnlyList<Entry> Slots => _slots;

    /// <summary>ShouldDraw's count: rows are waiting to be drawn.</summary>
    public bool Showing => _current > 0;

    /// <summary>Reset: on a new level and a HUD reset.</summary>
    public void Reset()
    {
        _slots.Clear();
        _current = 0;
    }

    /// <summary>
    /// AddToHistory( C_BaseCombatWeapon ): exhaustible weapons (grenades, which
    /// show as an ammo pickup) are left out, and a weapon already listed is not
    /// listed twice.
    /// </summary>
    public void AddWeapon(int weaponType, string icon, bool exhaustible)
    {
        if (exhaustible)
            return;

        foreach (Entry entry in _slots)
        {
            if (entry.Type == Kind.Weapon && entry.Id == weaponType)
                return;
        }

        AddIcon(Kind.Weapon, weaponType, icon, 0);
    }

    /// <summary>
    /// AddToHistory( HISTSLOT_AMMO ): nothing for no count; a refusal of the
    /// same ammo is cleared and the pickup takes its row.
    /// </summary>
    public void AddAmmo(int ammoType, int count, string icon)
    {
        if (count == 0)
            return;

        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Type == Kind.AmmoDenied && _slots[i].Id == ammoType)
            {
                _slots[i] = _slots[i] with { DisplayTime = 0f };
                _current = i;
                break;
            }
        }

        AddIcon(Kind.Ammo, ammoType, icon, count);
    }

    /// <summary>MsgFunc_ItemPickup: an item with an icon.</summary>
    public void AddItem(string icon) => AddIcon(Kind.Item, 1, icon, 0);

    /// <summary>
    /// MsgFunc_AmmoDenied: nothing while a pickup of that ammo is listed; a
    /// refusal already listed has its time renewed; otherwise a new row.
    /// </summary>
    public void AmmoDenied(int ammoType, string icon)
    {
        foreach (Entry entry in _slots)
        {
            if (entry.Type == Kind.Ammo && entry.Id == ammoType)
                return;
        }

        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Type == Kind.AmmoDenied && _slots[i].Id == ammoType)
            {
                _slots[i] = _slots[i] with { DisplayTime = _clock.Now + _drawTime() / 2f };
                return;
            }
        }

        AddIcon(Kind.AmmoDenied, ammoType, icon, 0);
    }

    /// <summary>AddIconToHistory.</summary>
    private void AddIcon(Kind type, int id, string icon, int count)
    {
        if (_current >= MaxSlots)
            _current = 0;

        while (_slots.Count < _current + 1)
            _slots.Add(default);

        // A refusal never takes a row a pickup is still showing in.
        if (type == Kind.AmmoDenied && _slots[_current].DisplayTime != 0f)
            return;

        float time = type == Kind.AmmoDenied ? _drawTime() / 2f : _drawTime();
        _slots[_current] = new Entry(type, id, icon, count, _clock.Now + time);
        _current++;
    }

    /// <summary>
    /// Paint's bookkeeping, each frame: no row outlasts hud_drawhistory_time
    /// from now, an expired row is emptied, and with every row empty the next
    /// pickup starts from the bottom again (CheckClearHistory).
    /// </summary>
    public void Think()
    {
        float now = _clock.Now;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Type == Kind.Empty)
                continue;

            float until = Math.Min(_slots[i].DisplayTime, now + _drawTime());
            if (until <= now)
            {
                _slots[i] = default;
                CheckClear();
                continue;
            }

            _slots[i] = _slots[i] with { DisplayTime = until };
        }
    }

    private void CheckClear()
    {
        foreach (Entry entry in _slots)
        {
            if (entry.Type != Kind.Empty)
                return;
        }

        _current = 0;
    }

    /// <summary>A row's alpha, 0 to 1: the seconds it has left times 80, out of 255.</summary>
    public float Opacity(int slot)
    {
        if (slot < 0 || slot >= _slots.Count || _slots[slot].Type == Kind.Empty)
            return 0f;

        float elapsed = _slots[slot].DisplayTime - _clock.Now;
        return Math.Clamp(elapsed * AlphaPerSecond, 0f, 255f) / 255f;
    }
}

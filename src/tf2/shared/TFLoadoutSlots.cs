//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared;

/// <summary>
/// The visible-slot table the loadout screen and the reply builder share:
/// stock's <c>g_VisibleLoadoutSlotsPerClass</c>
/// (game/client/tf/vgui/class_loadout_panel.cpp:105-195), a panel position
/// per slot where 0 is hidden. Every class but the spy (whose sapper takes
/// the building position and hides the primary) and the engineer (whose
/// toolbox takes the PDA one) wears the default row; the taunt slots are
/// the tabbed taunt panels, not a class's normal screen. As switches
/// (SS0010: no static tables).
/// </summary>
public static class TFLoadoutSlots
{
    /// <summary>The most panels a normal class shows: the spy's and the engineer's eight.</summary>
    public const int MaxPanels = 8;

    public static int Position(TFClass cls, TFLoadoutSlot slot)
    {
        if (slot < TFLoadoutSlot.Primary || slot > TFLoadoutSlot.Misc2)
            return 0;

        // The default row (class_loadout_panel.cpp:105-129): primary 1,
        // secondary 2, melee 3, head 5, misc 6, misc2 7, action 8; the
        // utility/building/pda/pda2 rows 0 (their classes' own arms below).
        int position = slot switch
        {
            TFLoadoutSlot.Primary => 1,
            TFLoadoutSlot.Secondary => 2,
            TFLoadoutSlot.Melee => 3,
            TFLoadoutSlot.Head => 5,
            TFLoadoutSlot.Misc => 6,
            TFLoadoutSlot.Misc2 => 7,
            TFLoadoutSlot.Action => 8,
            _ => 0,
        };

        return cls switch
        {
            // :131-153: the sapper takes the building position (4), the
            // watch pda2 (3); the primary and the disguise kit's pda hide.
            TFClass.Spy => slot switch
            {
                TFLoadoutSlot.Primary => 0,
                TFLoadoutSlot.Secondary => 1,
                TFLoadoutSlot.Melee => 2,
                TFLoadoutSlot.Building => 4,
                TFLoadoutSlot.Pda2 => 3,
                _ => position,
            },

            // :156-178: the toolbox as pda (4); pda2 and building hide.
            TFClass.Engineer => slot switch
            {
                TFLoadoutSlot.Pda => 4,
                TFLoadoutSlot.Pda2 => 0,
                TFLoadoutSlot.Building => 0,
                _ => position,
            },

            >= TFClass.Scout and < TFClass.Civilian => position,
            _ => 0,
        };
    }

    /// <summary>
    /// The class's visible slots, in panel order; empty outside
    /// Scout..Engineer (stock stops at <c>TF_LAST_NORMAL_CLASS</c>).
    /// </summary>
    public static TFLoadoutSlot[] Visible(TFClass cls)
    {
        Span<(TFLoadoutSlot Slot, int Position)> visible = stackalloc (TFLoadoutSlot, int)[MaxPanels];
        int count = 0;
        for (var slot = TFLoadoutSlot.Primary; slot <= TFLoadoutSlot.Misc2; slot++)
        {
            int position = Position(cls, slot);
            if (position is > 0 and <= MaxPanels)
                visible[count++] = (slot, position);
        }

        // Positions are 1..8 over at most eight slots; an insertion sort of
        // that many is a fixed, allocation-free pass.
        for (int i = 1; i < count; i++)
        {
            var entry = visible[i];
            int j = i - 1;
            while (j >= 0 && visible[j].Position > entry.Position)
            {
                visible[j + 1] = visible[j];
                j--;
            }
            visible[j + 1] = entry;
        }

        var slots = new TFLoadoutSlot[count];
        for (int i = 0; i < count; i++)
            slots[i] = visible[i].Slot;
        return slots;
    }
}

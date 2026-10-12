using System.Text;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

//=============================================================================
// ActivityCatalogTests IS RETIRED WITH ITS SUBJECT -- plan_shim.md Phase 2d.
//
// It read game/shared/ai_activity.h's enum and activitylist.cpp's registration
// order and asserted ActivityCatalog.Ordered was exactly that, in both
// directions: 1,947 names, the index IS the Activity ordinal, and
// IndexForName is case-insensitive the way stock's CUtlDict is.
//
// ActivityCatalog.cs is deleted. THE EXPERIMENT THAT SETTLED IT: its only
// non-test reader anywhere was Entry.cs's AnimListActivityNames thunk -- the
// feed into a NATIVE registry whose whole purpose was to number the table so
// that game/shared/animation.cpp could write an ordinal into mdl memory and a
// native reader could compare it against an enum. Nothing compares activity
// ordinals now: the sequence lookups are one managed rule over a
// ModelDescriptor, which carries activity NAMES.
//
//     grep -rn "ActivityCatalog\." --include=*.cs src/sourcesharp/managed \
//         | grep -v SourceSharp.Tests | grep -v ActivityCatalog.cs
//     -> SourceSharp.Bootstrap/Entry.cs:2943 only
//
// A table of 1,947 names, and the 2,048 lines holding it, existed to give a
// number to a comparison nobody makes.
//
// WHAT DOES NOT RETIRE: EventCatalogTests below. The fifty EVENT names stay,
// because managed code reads them itself -- SourceSharp.Shared's
// AnimationEventCatalog is a view of EventCatalog, and
// sourcesharp/game/shared/anim_event_names.h's five native rows are checked
// against it by anim_event_names_test.cpp. That is the difference between the
// two tables and it is why only one of them went.
//=============================================================================

/// <summary>
/// The animation-event table, and the side catalog that is now a view of it.
/// </summary>
public class EventCatalogTests
{
    [Fact]
    public void AStockEventResolvesItsIndex()
    {
        Assert.Equal(0, EventCatalog.IndexForName("AE_EMPTY"));
        Assert.Equal("AE_EMPTY", EventCatalog.Ordered[0]);
    }

    [Fact]
    public void AnUnknownEventNameAnswersInvalid()
    {
        Assert.Equal(-1, EventCatalog.IndexForName("AE_NOBODY_REGISTERED_THIS"));
        Assert.Equal(-1, EventCatalog.IndexForName(string.Empty));

        // Zero, not -1, for the MASK of an unheld name — see the remarks on
        // NativeTypeMask: -1 is what EventList_GetEventType answers for an
        // unknown INDEX, and the two questions have different answers.
        Assert.Equal(0, EventCatalog.NativeTypeMask("AE_NOBODY_REGISTERED_THIS"));
    }

    [Fact]
    public void AWeaponEventCarriesBothOfItsBits()
    {
        // AE_NPC_WEAPON_FIRE is the only entry in stock's table whose mask is
        // not one bit or the obvious client|server pair.
        const int AeTypeServer = 1 << 0;
        const int AeTypeWeapon = 1 << 3;

        Assert.Equal(AeTypeServer | AeTypeWeapon,
                     EventCatalog.NativeTypeMask("AE_NPC_WEAPON_FIRE"));
    }

    [SharedSourceFact("eventlist.h")]
    public void TheTableIsTheAnimeventEnumInTheEnumsOrder()
    {
        string header = AnimListSources.Find("eventlist.h")!;

        List<string> enumOrder = AnimListSources.EnumMembers(File.ReadAllText(header), "Animevent");
        enumOrder.RemoveAll(n => n is "AE_INVALID" or "LAST_SHARED_ANIMEVENT");

        Assert.Equal(enumOrder, EventCatalog.Ordered);
    }

    [Fact]
    public void TheSideCatalogIsAViewOfThisTableAndNotASecondCopy()
    {
        // AnimationEventCatalog used to hold its own hand-written fifty side
        // flags. It derives them now, and this is the fact that says the two
        // tables really are one: same names, same count, and every side the
        // mask implies.
        Assert.Equal(EventCatalog.Count, AnimationEventCatalog.All.Count);

        for (int i = 0; i < EventCatalog.Count; i++)
        {
            string name = EventCatalog.Ordered[i];
            AnimationEventSide side = AnimationEventCatalog.SideOf(name);
            int mask = EventCatalog.NativeTypes[i];

            Assert.Equal((mask & (1 << 0)) != 0, (side & AnimationEventSide.Server) != 0);
            Assert.Equal((mask & (1 << 4)) != 0, (side & AnimationEventSide.Client) != 0);
            Assert.Equal((mask & (1 << 3)) != 0, (side & AnimationEventSide.Weapon) != 0);
        }
    }
}

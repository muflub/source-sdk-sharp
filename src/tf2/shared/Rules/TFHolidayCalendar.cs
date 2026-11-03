//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace TF2.Shared.Rules;

/// <summary>Stock's <c>EHoliday</c> (econ_item_constants.h:966-983), in its order.</summary>
public enum TFHoliday
{
    None,
    TFBirthday,
    Halloween,
    Christmas,
    CommunityUpdate,
    EOTL,
    Valentines,
    MeetThePyro,
    FullMoon,
    HalloweenOrFullMoon,
    HalloweenOrFullMoonOrValentines,
    AprilFools,
    Soldier,
    Summer,
}

/// <summary>
/// The real-world holiday calendar, <c>econ_holidays.cpp</c>: the table of
/// date windows <c>EconHolidays_IsHolidayActive</c> answers from, the name
/// lookup <c>EconHolidays_GetHolidayForString</c>, and
/// <c>EconHolidays_GetActiveHolidayString</c>, the first active holiday's
/// name (the main menu's background condition, <c>if_&lt;name&gt;</c>).
///
/// <para>
/// STATELESS: the clock is the caller's argument, so nothing is remembered;
/// stock caches each window per year, which changes nothing a reader sees.
/// Windows are local wall-clock dates, as <c>CRTime::RTime32FromString</c>
/// reads them through <c>mktime</c>; a window's end is its date's midnight,
/// inclusive (<c>timeCurrent &lt;= m_rtEndTime</c>), so "11-08" closes as
/// November 8th begins. The active-holiday name reads the year in GMT
/// (<c>timeNow.SetToGMT( true )</c>), the plain check in local time.
/// </para>
/// </summary>
public static class TFHolidayCalendar
{
    private const int Count = (int)TFHoliday.Summer + 1;

    // The full moon's cycle (g_Holiday_FullMoon, :319): from 2025-10-06, every
    // (int)( 29.53f * 24 * 60 * 60 ) seconds, the float product truncated as
    // stock's, with a day of bonus time either side.
    private const long FullMoonCycleSeconds = (long)(29.53f * 86400);

    private const long FullMoonBonusSeconds = 86400;

    /// <summary>Each holiday's name (<c>s_HolidayChecks</c>, econ_holidays.cpp:338-354).</summary>
    public static string Name(TFHoliday holiday) => holiday switch
    {
        TFHoliday.TFBirthday => "birthday",
        TFHoliday.Halloween => "halloween",
        TFHoliday.Christmas => "christmas",
        TFHoliday.CommunityUpdate => "community_update",
        TFHoliday.EOTL => "eotl_launch",
        TFHoliday.Valentines => "valentines",
        TFHoliday.MeetThePyro => "meet_the_pyro",
        TFHoliday.FullMoon => "fullmoon",
        TFHoliday.HalloweenOrFullMoon => "halloween_or_fullmoon",
        TFHoliday.HalloweenOrFullMoonOrValentines => "halloween_or_fullmoon_or_valentines",
        TFHoliday.AprilFools => "april_fools",
        TFHoliday.Soldier => "soldier",
        TFHoliday.Summer => "summer",
        _ => "none",
    };

    /// <summary><c>EconHolidays_GetHolidayForString</c>: case-blind, and
    /// <see cref="TFHoliday.None"/> for a name no holiday has.</summary>
    public static TFHoliday ForName(string? name)
    {
        if (name is null)
            return TFHoliday.None;
        for (int i = 0; i < Count; i++)
        {
            if (string.Equals(Name((TFHoliday)i), name, StringComparison.OrdinalIgnoreCase))
                return (TFHoliday)i;
        }
        return TFHoliday.None;
    }

    /// <summary><c>EconHolidays_IsHolidayActive</c> at a local time.</summary>
    public static bool IsActive(TFHoliday holiday, DateTime now) => IsActive(holiday, now, now.Year);

    /// <summary><c>EconHolidays_GetActiveHolidayString</c>: the first holiday
    /// in table order active now, the year read in GMT; null with none.</summary>
    public static string? ActiveName(DateTimeOffset now)
    {
        DateTime local = now.LocalDateTime;
        int gmtYear = now.UtcDateTime.Year;
        for (int i = 0; i < Count; i++)
        {
            if (IsActive((TFHoliday)i, local, gmtYear))
                return Name((TFHoliday)i);
        }
        return null;
    }

    private static bool IsActive(TFHoliday holiday, DateTime now, int year) => holiday switch
    {
        TFHoliday.TFBirthday => Within(now, year, 8, 23, 8, 25),
        TFHoliday.Halloween => Within(now, year, 10, 1, 11, 8),
        // christmas1 12-01 to "12-31 23:59:59", or christmas2 01-01 to 01-08.
        TFHoliday.Christmas => Within(now, Day(year, 12, 1), Day(year, 12, 31).AddSeconds(86399))
                               || Within(now, year, 1, 1, 1, 8),
        TFHoliday.CommunityUpdate => Within(now, Day(2015, 9, 1), Day(2015, 11, 5)),
        TFHoliday.EOTL => Within(now, Day(2014, 12, 3), Day(2015, 1, 5)),
        TFHoliday.Valentines => Within(now, year, 2, 13, 2, 15),
        TFHoliday.MeetThePyro => Within(now, Day(2012, 6, 26), Day(2012, 7, 5)),
        TFHoliday.FullMoon => FullMoon(now),
        TFHoliday.HalloweenOrFullMoon => IsActive(TFHoliday.Halloween, now, year) || FullMoon(now),
        TFHoliday.HalloweenOrFullMoonOrValentines => IsActive(TFHoliday.HalloweenOrFullMoon, now, year)
                                                    || IsActive(TFHoliday.Valentines, now, year),
        TFHoliday.AprilFools => Within(now, year, 3, 31, 4, 2),
        TFHoliday.Soldier => Within(now, year, 4, 12, 4, 14),
        TFHoliday.Summer => Within(now, Day(2025, 7, 16), Day(2025, 9, 16)),
        _ => false,
    };

    private static DateTime Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Local);

    /// <summary>CDateBasedHolidayNoSpecificYear: the dates in the given year.</summary>
    private static bool Within(DateTime now, int year, int startMonth, int startDay, int endMonth, int endDay)
        => Within(now, Day(year, startMonth, startDay), Day(year, endMonth, endDay));

    /// <summary>CDateBasedHoliday: start and end both inclusive.</summary>
    private static bool Within(DateTime now, DateTime start, DateTime end) => now >= start && now <= end;

    /// <summary>
    /// CCyclicalHoliday::IsActive: the seconds into the current cycle, a C
    /// remainder (negative before the first cycle, which the first test then
    /// takes as active, as stock does), within the bonus of either end.
    /// </summary>
    private static bool FullMoon(DateTime now)
    {
        // time_t arithmetic: elapsed seconds, whatever daylight saving did between.
        long since = (long)(now.ToUniversalTime() - Day(2025, 10, 6).ToUniversalTime()).TotalSeconds;
        long into = since % FullMoonCycleSeconds;
        return into < FullMoonBonusSeconds || into > FullMoonCycleSeconds - FullMoonBonusSeconds;
    }
}

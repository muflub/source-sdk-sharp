using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The holiday calendar (econ_holidays.cpp): each window's edges as stock's
/// table writes them, the full moon's cycle, the name lookup, and the first
/// active name the main menu's background condition reads.
/// </summary>
public sealed class TFHolidayCalendarTests
{
    private static DateTime At(int year, int month, int day, int hour = 0, int minute = 0, int second = 0)
        => new(year, month, day, hour, minute, second, DateTimeKind.Local);

    [Fact]
    public void Halloween_runs_from_october_first_to_the_start_of_november_eighth()
    {
        Assert.False(TFHolidayCalendar.IsActive(TFHoliday.Halloween, At(2026, 9, 30, 23, 59, 59)));
        Assert.True(TFHolidayCalendar.IsActive(TFHoliday.Halloween, At(2026, 10, 1)));
        Assert.True(TFHolidayCalendar.IsActive(TFHoliday.Halloween, At(2026, 11, 1, 12)));
        Assert.True(TFHolidayCalendar.IsActive(TFHoliday.Halloween, At(2026, 11, 8)));
        Assert.False(TFHolidayCalendar.IsActive(TFHoliday.Halloween, At(2026, 11, 8, 0, 0, 1)));
    }

    [Fact]
    public void Christmas_spans_the_new_year_in_its_two_parts()
    {
        Assert.False(TFHolidayCalendar.IsActive(TFHoliday.Christmas, At(2026, 11, 30, 23)));
        Assert.True(TFHolidayCalendar.IsActive(TFHoliday.Christmas, At(2026, 12, 31, 23, 59, 59)));
        Assert.True(TFHolidayCalendar.IsActive(TFHoliday.Christmas, At(2027, 1, 1, 0, 0, 0)));
        Assert.True(TFHolidayCalendar.IsActive(TFHoliday.Christmas, At(2027, 1, 8)));
        Assert.False(TFHolidayCalendar.IsActive(TFHoliday.Christmas, At(2027, 1, 8, 12)));
    }

    [Fact]
    public void A_dated_holiday_holds_only_in_its_own_year()
    {
        Assert.True(TFHolidayCalendar.IsActive(TFHoliday.Summer, At(2025, 8, 1)));
        Assert.False(TFHolidayCalendar.IsActive(TFHoliday.Summer, At(2026, 8, 1)));
    }

    /// <summary>The cycle starts 2025-10-06 with a day's bonus either side:
    /// active on the day, not mid-cycle, active again around the next one
    /// (29.53 days on: 2025-11-04, 12:43 local).</summary>
    [Fact]
    public void The_full_moon_comes_round_every_cycle_with_a_day_either_side()
    {
        Assert.True(TFHolidayCalendar.IsActive(TFHoliday.FullMoon, At(2025, 10, 6, 12)));
        Assert.False(TFHolidayCalendar.IsActive(TFHoliday.FullMoon, At(2025, 10, 21)));
        Assert.True(TFHolidayCalendar.IsActive(TFHoliday.FullMoon, At(2025, 11, 4, 12)));
        Assert.False(TFHolidayCalendar.IsActive(TFHoliday.FullMoon, At(2025, 11, 7)));
    }

    [Fact]
    public void The_or_holidays_answer_either_side()
    {
        Assert.True(TFHolidayCalendar.IsActive(TFHoliday.HalloweenOrFullMoon, At(2026, 10, 15)));
        Assert.True(TFHolidayCalendar.IsActive(TFHoliday.HalloweenOrFullMoonOrValentines, At(2026, 2, 14)));
        Assert.False(TFHolidayCalendar.IsActive(TFHoliday.HalloweenOrFullMoon, At(2026, 6, 15)));
    }

    [Fact]
    public void The_active_name_is_the_first_active_holiday_in_table_order()
    {
        Assert.Equal("halloween", TFHolidayCalendar.ActiveName(new DateTimeOffset(At(2026, 11, 1, 12))));
        Assert.Equal("birthday", TFHolidayCalendar.ActiveName(new DateTimeOffset(At(2026, 8, 24, 12))));
        Assert.Null(TFHolidayCalendar.ActiveName(new DateTimeOffset(At(2026, 6, 15, 12))));
    }

    [Fact]
    public void A_name_finds_its_holiday_whatever_its_case()
    {
        Assert.Equal(TFHoliday.HalloweenOrFullMoon, TFHolidayCalendar.ForName("Halloween_Or_FullMoon"));
        Assert.Equal(TFHoliday.Christmas, TFHolidayCalendar.ForName("christmas"));
        Assert.Equal(TFHoliday.None, TFHolidayCalendar.ForName(""));
        Assert.Equal(TFHoliday.None, TFHolidayCalendar.ForName("nope"));
        Assert.Equal("summer", TFHolidayCalendar.Name(TFHoliday.Summer));
    }
}

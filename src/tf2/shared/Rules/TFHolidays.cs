//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Rules;

/// <summary>
/// The holiday calendar's convar arms — <c>CTFGameRules::IsBirthday</c>
/// (<c>tf_gamerules.cpp:17082</c>) and the <c>TF_IsHolidayActive</c> it calls
/// (<c>tf_gamerules.cpp:1030</c>).
///
/// <para>
/// STATELESS ON PURPOSE: the rules object these live on in stock is gamerules
/// state this port has no client mirror of, and every arm that remains reads a
/// convar, so the console comes in as an argument and nothing is remembered
/// between calls. <c>IsX360()</c> is false on every target this builds.
/// </para>
///
/// <para>
/// ONE ARM ANSWERS FALSE AND THAT IS SAID WHERE IT WOULD BE, not hidden:
/// <c>TFGameRules()-&gt;IsHolidayMap(kHoliday_TFBirthday)</c> reads
/// <c>m_nMapHolidayType</c>, the rules' networked state — the same answer a
/// non-holiday map gives, which is what every map this port runs is until a
/// rules mirror carries it. The last arm, <c>UTIL_IsHolidayActive</c>, is the
/// real-world calendar (econ_holidays.cpp, <see cref="TFHolidayCalendar"/>),
/// at the time the caller hands in.
/// </para>
/// </summary>
public static class TFHolidays
{
    /// <summary><c>kHoliday_TFBirthday</c> (<c>econ_item_constants.h:969</c>).</summary>
    public const int TFBirthday = 1;

    /// <summary>
    /// <c>CTFGameRules::IsBirthday</c>: <c>tf_birthday || IsHolidayActive(
    /// kHoliday_TFBirthday )</c>. The second disjunct re-reads
    /// <c>tf_birthday</c> a line later in stock's own chain, which changes
    /// nothing a reader could observe.
    /// </summary>
    public static bool IsBirthday(IEngineConsole console, DateTime now)
        => Flag(console, "tf_birthday") || IsHolidayActive(console, TFBirthday, now);

    /// <summary>
    /// <c>TF_IsHolidayActive</c> for the birthday: the off switch, the two
    /// forced-holiday cvars (<c>BIsCvarIndicatingHolidayIsActive</c>,
    /// <c>tf_gamerules.cpp:988</c>), <c>tf_birthday</c> itself, then the
    /// calendar.
    /// </summary>
    private static bool IsHolidayActive(IEngineConsole console, int holiday, DateTime now)
    {
        if (Flag(console, "tf_force_holidays_off"))
            return false;

        if (Indicates(console, "tf_forced_holiday", holiday))
            return true;

        if (Indicates(console, "tf_item_based_forced_holiday", holiday))
            return true;

        // ( eHoliday == kHoliday_TFBirthday ) && tf_birthday.GetBool()
        if (holiday == TFBirthday && Flag(console, "tf_birthday"))
            return true;

        return TFHolidayCalendar.IsActive((TFHoliday)holiday, now);
    }

    /// <summary>
    /// <c>BIsCvarIndicatingHolidayIsActive</c>: a non-zero cvar naming this
    /// holiday exactly — "Holidays are not a proper bitfield", stock's own
    /// comment says.
    /// </summary>
    private static bool Indicates(IEngineConsole console, string name, int holiday)
    {
        string? raw = console.FindConVarValue(name);

        return int.TryParse(raw, out int value) && value != 0 && value == holiday;
    }

    private static bool Flag(IEngineConsole console, string name)
    {
        string? raw = console.FindConVarValue(name);

        return raw is not null && (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase));
    }
}

using SourceSharp.Abi;

using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// The holiday calendar's convar arms (<see cref="TFHolidays"/>), each against
/// a name table the way the game's console reads.
/// </summary>
public sealed class TFHolidayTests
{
    [Fact]
    public void tf_birthday_alone_is_the_birthday()
    {
        Assert.True(TFHolidays.IsBirthday(With(("tf_birthday", "1"))));
    }

    [Fact]
    public void tf_forced_holiday_1_is_the_birthday()
    {
        Assert.True(TFHolidays.IsBirthday(With(("tf_forced_holiday", "1"))));
    }

    [Fact]
    public void tf_item_based_forced_holiday_1_is_the_birthday()
    {
        Assert.True(TFHolidays.IsBirthday(With(("tf_item_based_forced_holiday", "1"))));
    }

    [Fact]
    public void tf_force_holidays_off_wins_over_the_holiday_calendar_but_not_over_tf_birthday()
    {
        // Stock's own order — tf_gamerules.cpp:17087 is
        // "tf_birthday.GetBool() || IsHolidayActive( kHoliday_TFBirthday )" —
        // reads tf_birthday FIRST, so the off switch, which lives inside
        // IsHolidayActive, never reaches it. The calendar arms lose; the
        // birthday cvar does not.
        Assert.False(TFHolidays.IsBirthday(With(
            ("tf_force_holidays_off", "1"),
            ("tf_forced_holiday", "1"))));
        Assert.True(TFHolidays.IsBirthday(With(
            ("tf_force_holidays_off", "1"),
            ("tf_birthday", "1"))));
    }

    [Fact]
    public void A_forced_holiday_that_names_another_is_not_the_birthday()
    {
        // "Holidays are not a proper bitfield": 2 is halloween, not 1.
        Assert.False(TFHolidays.IsBirthday(With(("tf_forced_holiday", "2"))));
        Assert.False(TFHolidays.IsBirthday(With(("tf_item_based_forced_holiday", "2"))));
    }

    [Fact]
    public void A_zero_forced_holiday_is_not_active()
    {
        Assert.False(TFHolidays.IsBirthday(With(("tf_forced_holiday", "0"))));
    }

    [Fact]
    public void With_no_cvars_set_nothing_is_active()
    {
        Assert.False(TFHolidays.IsBirthday(With()));
    }

    private static IEngineConsole With(params (string Name, string Value)[] entries)
        => new ConsoleFake(entries.ToDictionary(e => e.Name, e => e.Value));

    /// <summary>The engine's console, as a name table.</summary>
    private sealed class ConsoleFake(Dictionary<string, string> values) : IEngineConsole
    {
        public void ExecuteServerCommand(string command) => throw new NotSupportedException();
        public string? FindConVarValue(string name) => values.GetValueOrDefault(name);
        public string GetConVarValue(int id) => throw new NotSupportedException();
        public void Msg(string message) { }
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => throw new NotSupportedException();
        public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null) => throw new NotSupportedException();
        public void SendClientCommand(EntityHandle player, string command) => throw new NotSupportedException();
        public void SetConVarValue(int id, string value) => throw new NotSupportedException();
        public void SetConVarValueByName(string name, string value) => throw new NotSupportedException();
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }
}

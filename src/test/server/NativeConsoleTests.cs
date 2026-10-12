using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The proxy over the engine's own console registry, in the part that needs no
/// engine: what a raw flag word is rendered as, and what an entry says about
/// itself once it has been read.
///
/// The walk cannot be tested here — there is no registry to walk without a
/// running game — and it does not need to be: everything the listing shows past
/// the name and the value is decided by this code.
/// </summary>
public class NativeConsoleTests
{

    [Fact]
    public void FlagNaming()
    {
        Assert.True(ConVarFlagNames.Describe(0) == "None", "no flags reads as None, not an empty string");

        Assert.True(ConVarFlagNames.Describe((int)ConVarFlags.Cheat) == "Cheat", "a single flag is named");

        // In bit order rather than the order they were written, so the same set
        // of flags always renders the same way and two rows can be compared.
        Assert.True(ConVarFlagNames.Describe(
                (int)(ConVarFlags.Replicated | ConVarFlags.GameDll | ConVarFlags.Archive))
            == "GameDll, Archive, Replicated", "several flags come out in bit order");

        // Three of the 32 bits are unassigned in this SDK. A convar carrying one
        // is not this tool's to explain, but it is this tool's to not hide.
        Assert.True(ConVarFlagNames.Describe(1 << 19) == "Bit19", "an unnamed bit is reported by position rather than swallowed");

        // The top bit is the one a naive implementation loses: 1 << 31 is
        // negative as an int, and a signed comparison drops it.
        Assert.True(ConVarFlagNames.Describe(unchecked((int)0x8000_0000u)) == "ExecDespiteDefault", "the top bit survives being signed");

        Assert.True(ConVarFlagNames.Describe((int)ConVarFlags.Hidden | (1 << 26))
            == "Hidden, Bit26", "a named and an unnamed bit list together");
    }

    [Fact]
    public void WhatAnEntrySaysAboutItself()
    {
        var gameDll = new NativeConsoleEntry("sv_gravity", "800", "600", "World gravity",
            (int)ConVarFlags.GameDll, false);

        Assert.True(gameDll.DeclaredBy == "server.dll", "a convar registered by the game DLL says so");

        Assert.True(gameDll.IsModified, "a value away from its default is modified");

        Assert.True(gameDll.FlagNames == "GameDll", "a convar's flags render through the entry");

        var clientDll = new NativeConsoleEntry("cl_showfps", "0", "0", "",
            (int)ConVarFlags.ClientDll, false);

        Assert.True(clientDll.DeclaredBy == "client.dll", "a convar registered by the client DLL says so");

        Assert.True(!clientDll.IsModified, "a value sitting on its default is not modified");

        var engine = new NativeConsoleEntry("host_timescale", "1", "1", "", 0, false);
        Assert.True(engine.DeclaredBy == "engine", "a convar with neither DLL flag belongs to the engine");

        // A command has no value and no default, and the two being equal must
        // not read as "unchanged" — there was never anything to change.
        var command = new NativeConsoleEntry("changelevel", "", "", "Change the map",
            (int)ConVarFlags.GameDll, true);

        Assert.True(!command.IsModified, "a command is never modified, having nothing to modify");
    }

    [Fact]
    public void NoEngineMeansAnEmptyListing()
    {
        // The managed tests run with no engine behind them, which is exactly the
        // state this has to survive: a listing that throws would take the whole
        // development API down with it rather than showing nothing. The
        // console is a side of this test's own with nothing attached
        // (plan_abi_hotreload D8), where the engine-less overloads asked the
        // process's.
        IEngineConsole console = TestServices.UnattachedConsole();

        Assert.True(NativeConsole.Snapshot(console).Count == 0, "a snapshot with no engine is empty rather than a throw");

        Assert.True(NativeConsole.ValueOf(console, "sv_cheats") is null, "a value lookup with no engine is null rather than a throw");
    }

    /// <summary>
    /// plan_abi_hotreload D8: the console asked is the one handed in, so two
    /// game instances each read their own engine's convars.
    /// </summary>
    [Fact]
    public void AValueIsReadFromTheConsoleItIsHanded()
    {
        var first = new RecordingEngine().Returns("Console.FindConVarValue", "1");
        var second = new RecordingEngine().Returns("Console.FindConVarValue", "0");

        Assert.Equal("1", NativeConsole.ValueOf(first, "sv_cheats"));
        Assert.Equal("0", NativeConsole.ValueOf(second, "sv_cheats"));
        Assert.Equal(1, first.CountOf("Console.FindConVarValue"));
        Assert.Equal(1, second.CountOf("Console.FindConVarValue"));
    }

    [Fact]
    public void ASnapshotWalksTheConsoleItIsHanded()
    {
        var entry = new NativeConsoleEntry("host_timescale", "1", "1", "", 0, false);
        var console = new RecordingEngine()
            .Returns("Console.Snapshot", 2)
            .Returns("Console.SnapshotEntry", true)
            .Returns("Console.SnapshotEntry.entry", entry);
        var silent = new RecordingEngine().Returns("Console.Snapshot", 0);

        Assert.Equal(2, NativeConsole.Snapshot(console).Count);
        Assert.Empty(NativeConsole.Snapshot(silent));
        Assert.Equal(2, console.CountOf("Console.SnapshotEntry"));
    }
}

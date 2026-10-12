using System;
using System.Collections.Generic;
using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The damage-rule cheats (DamageCheats.cs) and the rule they set
/// (SsPlayer.DamageTaken) — stock's god and buddha, ported managed because the
/// client.cpp registrations resolve a CBasePlayer the standalone player never
/// is, and no-oped silently.
///
/// What can regress: the sv_cheats gate (both directions — it has been wrong
/// both ways on this project), the toggle actually flipping, god refusing
/// everything, and buddha stopping exactly one point short of death.
/// </summary>
public class DamageCheatsTests
{
    /// <summary>A directory holding exactly the entities a test placed in it.</summary>
    private sealed class StubDirectory : IEntityDirectory
    {
        public readonly Dictionary<EntityHandle, ScriptedEntity> Entities = new();

        public ScriptedEntity? Get(EntityHandle handle)
            => Entities.TryGetValue(handle, out var entity) ? entity : null;

        public ScriptedEntity? FindByName(string name) => null;

        public IEnumerable<ScriptedEntity> OfClass(string className)
            => Array.Empty<ScriptedEntity>();

        public bool IsRegistered(string className) => false;

        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    private static readonly EntityHandle Player = new(3u);

    private static (DamageCheats Cheats, SsPlayer Subject) Rig(bool cheats = true)
    {
        var directory = new StubDirectory();
        var subject = new SsPlayer(TestServices.Fake(), TestServices.Fake(), TestServices.Impacts(TestServices.Fake()), TestServices.Fluid());
        directory.Entities[Player] = subject;

        var damageCheats = new DamageCheats(new RecordingLog(), directory, TestServices.Fake())
        {
            CheatsEnabled = () => cheats,
        };

        return (damageCheats, subject);
    }

    //-------------------------------------------------------------------------
    // The gate.
    //-------------------------------------------------------------------------

    [Fact]
    public void NothingTogglesWithCheatsOff()
    {
        var (cheats, subject) = Rig(cheats: false);

        cheats.ToggleGod(Player);
        cheats.ToggleBuddha(Player);

        Assert.True(!subject.GodMode, "god does not switch on while sv_cheats is off");
        Assert.True(!subject.BuddhaMode, "buddha does not switch on while sv_cheats is off");
    }

    [Fact]
    public void AFlagAlreadyOnSurvivesCheatsGoingOff()
    {
        var (cheats, subject) = Rig();

        cheats.ToggleGod(Player);
        Assert.True(subject.GodMode, "god switches on with cheats on");

        cheats.CheatsEnabled = () => false;
        cheats.ToggleGod(Player);

        Assert.True(subject.GodMode,
            "sv_cheats dropping to 0 neither clears the flag nor lets the toggle run — stock's FL_GODMODE behaved the same");
    }

    //-------------------------------------------------------------------------
    // The toggles.
    //-------------------------------------------------------------------------

    [Fact]
    public void GodTogglesOnThenOff()
    {
        var (cheats, subject) = Rig();

        cheats.ToggleGod(Player);
        Assert.True(subject.GodMode, "the first god turns god mode on");

        cheats.ToggleGod(Player);
        Assert.True(!subject.GodMode, "the second god turns god mode off");
    }

    [Fact]
    public void BuddhaTogglesOnThenOffAndIsItsOwnFlag()
    {
        var (cheats, subject) = Rig();

        cheats.ToggleBuddha(Player);
        Assert.True(subject.BuddhaMode, "the first buddha turns buddha on");
        Assert.True(!subject.GodMode, "buddha does not touch god");

        cheats.ToggleBuddha(Player);
        Assert.True(!subject.BuddhaMode, "the second buddha turns buddha off");
    }

    [Fact]
    public void AHandleThatIsNotAPlayerTogglesNothing()
    {
        var directory = new StubDirectory();
        var cheats = new DamageCheats(new RecordingLog(), directory, TestServices.Fake()) { CheatsEnabled = () => true };

        // Nothing at the handle at all: must not throw, must not toggle.
        cheats.ToggleGod(Player);
        cheats.ToggleBuddha(Player);

        Assert.True(directory.Entities.Count == 0, "an empty directory stays empty — the toggle made nothing up");
    }

    //-------------------------------------------------------------------------
    // The rule itself, pure.
    //-------------------------------------------------------------------------

    [Fact]
    public void WithoutCheatsTheWholeHitLands()
    {
        Assert.True(SsPlayer.DamageTaken(100, 50, god: false, buddha: false) == 50,
            "a plain player takes the whole hit, as stock");
    }

    [Fact]
    public void GodRefusesAllDamage()
    {
        Assert.True(SsPlayer.DamageTaken(100, 50, god: true, buddha: false) == 0,
            "god takes nothing from an ordinary hit");
        Assert.True(SsPlayer.DamageTaken(1, 10000, god: true, buddha: false) == 0,
            "god takes nothing from a killing blow at 1 health");
        Assert.True(SsPlayer.DamageTaken(100, 50, god: true, buddha: true) == 0,
            "god outranks buddha when both are on");
    }

    [Fact]
    public void BuddhaTakesDamageButNeverDies()
    {
        Assert.True(SsPlayer.DamageTaken(100, 50, god: false, buddha: true) == 50,
            "buddha takes a survivable hit in full");
        Assert.True(SsPlayer.DamageTaken(100, 10000, god: false, buddha: true) == 99,
            "a killing blow leaves a buddha at exactly 1 health");
        Assert.True(SsPlayer.DamageTaken(100, 100, god: false, buddha: true) == 99,
            "a hit for exactly current health also stops at 1");
        Assert.True(SsPlayer.DamageTaken(1, 50, god: false, buddha: true) == 0,
            "a buddha already at 1 health takes nothing");
    }

    /// <summary>
    /// The gate's default reads the console it was GIVEN (plan_abi_hotreload
    /// D8), not whichever side is active: two instances over two consoles
    /// answer each from its own.
    /// </summary>
    [Fact]
    public void TheDefaultGateReadsItsOwnConsole()
    {
        var on = new DamageCheats(new RecordingLog(), new StubDirectory(),
            TestServices.Fake().Returns("Console.FindConVarValue", "1"));
        var off = new DamageCheats(new RecordingLog(), new StubDirectory(),
            TestServices.Fake().Returns("Console.FindConVarValue", "0"));

        Assert.True(on.CheatsEnabled(), "sv_cheats 1 on this console opens this gate");
        Assert.False(off.CheatsEnabled(), "sv_cheats 0 on the other console keeps its gate shut");
    }
}

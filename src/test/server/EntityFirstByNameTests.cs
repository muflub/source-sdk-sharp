using System.Collections.Generic;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// EntityDirectory.FirstByName: the single entity a name lookup answers, in the
/// order Engine.FindEntityByName always answered (the native standalone walk,
/// then the shell-less walk). Held live by cs_sv_names_agree: 107 of 107
/// queries on dm_lockdown, first load and after a changelevel.
/// </summary>
public class EntityFirstByNameTests
{
    private const string ShellLessClass = "logic_relay";

    private sealed class Probe : ScriptedEntity
    {
    }

    private static EntityHandle H(int entry, int serial = 1)
        => new((uint)entry | ((uint)serial << 16));

    private static bool IsShellLess(ScriptedEntity e) => e.ClassName == ShellLessClass;

    private static EntityHandle Add(EntityDirectory d, int entry, string className, string name)
    {
        EntityHandle h = H(entry);
        Assert.True(d.Bind(h, new Probe { ClassName = className }));
        d.SetName(h, name);
        return h;
    }

    [Fact]
    public void AShelledEntityWinsOverAShellLessOneWhateverWasCreatedFirst()
    {
        var d = new EntityDirectory(new RecordingLog());
        Add(d, 3000, ShellLessClass, "door");
        EntityHandle shelled = Add(d, 4000, "info_target", "door");

        Assert.Equal(shelled, d.FirstByName("door", IsShellLess));
    }

    [Fact]
    public void AmongShelledEntitiesTheFirstCreatedWinsWhateverItsEntry()
    {
        var d = new EntityDirectory(new RecordingLog());
        EntityHandle first = Add(d, 900, "info_target", "spot");
        Add(d, 100, "info_target", "spot");

        Assert.Equal(first, d.FirstByName("spot", IsShellLess));
    }

    [Fact]
    public void AmongShellLessEntitiesTheLowestEntryWinsWhateverWasCreatedFirst()
    {
        var d = new EntityDirectory(new RecordingLog());
        Add(d, 7954, ShellLessClass, "relay");
        EntityHandle lower = Add(d, 7811, ShellLessClass, "relay");

        Assert.Equal(lower, d.FirstByName("relay", IsShellLess));
    }

    [Fact]
    public void AWildcardIsAnsweredInTheSameOrder()
    {
        var d = new EntityDirectory(new RecordingLog());
        Add(d, 7952, ShellLessClass, "antspawn");
        EntityHandle lower = Add(d, 7885, ShellLessClass, "ant_spawner_counter");

        // dm_lockdown's own case: 'ant*' is the counter, not the relay.
        Assert.Equal(lower, d.FirstByName("ant*", IsShellLess));
    }

    [Fact]
    public void NoMatchAndNoQueryAnswerNone()
    {
        var d = new EntityDirectory(new RecordingLog());
        Add(d, 10, "info_target", "a");

        Assert.Equal(EntityHandle.None, d.FirstByName("b", IsShellLess));
        Assert.Equal(EntityHandle.None, d.FirstByName("", IsShellLess));
        Assert.Equal(EntityHandle.None, d.FirstByName(null, IsShellLess));
    }

    [Fact]
    public void NamesMatchWithoutRegardToCase()
    {
        var d = new EntityDirectory(new RecordingLog());
        EntityHandle h = Add(d, 10, "info_target", "Showers_Gate3");

        Assert.Equal(h, d.FirstByName("showers_gate3", IsShellLess));
    }

    [Fact]
    public void ARenamedEntityAnswersToItsNewNameOnly()
    {
        var d = new EntityDirectory(new RecordingLog());
        EntityHandle h = Add(d, 10, "info_target", "before");

        d.SetName(h, "after");

        Assert.Equal(EntityHandle.None, d.FirstByName("before", IsShellLess));
        Assert.Equal(h, d.FirstByName("after", IsShellLess));
    }

    [Fact]
    public void RebindingTheSameHandleKeepsItsPlace()
    {
        var d = new EntityDirectory(new RecordingLog());
        EntityHandle first = Add(d, 900, "info_target", "spot");
        Add(d, 100, "info_target", "spot");

        Assert.True(d.Bind(first, d.Resolve(first)!));   // the same handle again

        Assert.Equal(first, d.FirstByName("spot", IsShellLess));
    }

    [Fact]
    public void AllMatchesComeShelledInCreationOrderThenShellLessByEntry()
    {
        var d = new EntityDirectory(new RecordingLog());
        EntityHandle relayHigh = Add(d, 7954, ShellLessClass, "lights");
        EntityHandle shelledLate = Add(d, 100, "info_target", "lights");
        EntityHandle relayLow = Add(d, 7811, ShellLessClass, "lights");
        EntityHandle shelledEarlyBoundLater = Add(d, 900, "info_target", "lights");

        var all = new List<EntityHandle>();
        Assert.Equal(4, d.AllByName("lights", IsShellLess, all, 512));
        Assert.Equal(new[] { shelledLate, shelledEarlyBoundLater, relayLow, relayHigh }, all);
    }

    [Fact]
    public void TheLimitBoundsTheShelledHalfAndTheShellLessHalfFollowsWhenThereIsRoom()
    {
        // The old composition exactly: the native walk stopped at the limit;
        // the shell-less walk appended everything if the list was still short.
        var d = new EntityDirectory(new RecordingLog());
        EntityHandle a = Add(d, 10, "info_target", "n");
        Add(d, 11, "info_target", "n");
        EntityHandle r1 = Add(d, 7000, ShellLessClass, "n");
        EntityHandle r2 = Add(d, 7001, ShellLessClass, "n");

        var one = new List<EntityHandle>();
        Assert.Equal(1, d.AllByName("n", IsShellLess, one, 1));
        Assert.Equal(new[] { a }, one);

        var three = new List<EntityHandle>();
        Assert.Equal(4, d.AllByName("n", IsShellLess, three, 3));
        Assert.Equal(r1, three[2]);
        Assert.Equal(r2, three[3]);
    }

    [Fact]
    public void AProceduralNameIsNeverMatchedByString()
    {
        // Stock's FindGameEntityByName refuses '!' names (entitylist.cpp:686);
        // they resolve against a caller, and a lookup has none.
        var d = new EntityDirectory(new RecordingLog());
        Add(d, 10, "info_target", "!player");
        Add(d, 7000, ShellLessClass, "!activator");

        Assert.Equal(EntityHandle.None, d.FirstByName("!player", IsShellLess));
        Assert.Equal(EntityHandle.None, d.FirstByName("!activator", IsShellLess));

        var all = new List<EntityHandle>();
        Assert.Equal(0, d.AllByName("!player", IsShellLess, all, 512));
        Assert.Equal(0, d.AllByName("!*", IsShellLess, all, 512));
    }
}

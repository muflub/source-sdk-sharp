extern alias client;
using SourceSharp.Abi;
using client::SourceSharp.Client.Standalone;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// ss_barrel's think count, and the false alarm its loss produced on the other
/// side of the wire.
///
/// <para>
/// <b>THE MEASUREMENT.</b> Two live <c>tools/live-gate</c> runs, six
/// <c>DataTableChanged</c> updates across two map loads, all six reading
/// <c>own table MISMATCH</c>:
/// </para>
///
/// <code>
/// ss_barrel Created:          thinks=0 lastDamage=0.00 spawnOrigin=(-3747, 2894, 40)
///                             broken=False model='models/...' -- own table OK
/// ss_barrel DataTableChanged: thinks=1 lastDamage=0.00 spawnOrigin=(-3747, 2894, 40)
///                             broken=False model='models/...' -- own table MISMATCH
/// </code>
///
/// <para>
/// Every printed value decoded correctly, and the class's own doc says the
/// model string is the field a mislaid offset garbles first. The failing clause
/// was <c>thinks &gt;= previous - 1</c>, and what broke it was
/// <c>TestBarrel._thinks</c> - a plain managed field, neither a map key nor a
/// networked value, so the automatic save tier did not see it. Every
/// <c>cs_sv_reload</c> handed the rebuilt barrel a fresh zero and the networked
/// count went DOWN. The MISMATCH lines land immediately after the reload line
/// in both logs.
/// </para>
///
/// <para>
/// <b>NOT FIXED BY WEAKENING THE CHECK.</b> The barrel in the world did not
/// restart; only its managed half was swapped. So the counter is carried, which
/// is what <c>[SaveField]</c> is for, and the client's clause is left able to
/// fail on the thing it was written for.
/// </para>
/// </summary>
public class TestBarrelReloadTests
{
    // This class's own side state (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's.
    private readonly ReloadStore _reload = new(Side.Server);

    private const string Model = "models/props_c17/oildrum001.mdl";

    private static ScriptedEntityRegistry NewRegistry()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(TestBarrel).Assembly);

        // NAMED RATHER THAN DEFAULTED. OnSpawn logs the barrel's health and
        // SetModel goes to the engine; neither is this file's subject, and
        // both would otherwise throw out of the strict double.
        registry.Services = Ignoring("Entities.GetHealth", "Entities.SetModel", "Entities.GetOrigin");
        return registry;
    }

    private static TestBarrel Barrel(ScriptedEntityRegistry registry, EntityHandle handle)
    {
        registry.KeyValue(handle, EntityNames.SsBarrel, "model", Model);
        registry.Spawn(handle, EntityNames.SsBarrel);
        return (TestBarrel)registry.Get(handle)!;
    }

    /// <summary>
    /// The counter is carried across the reload, which is the fix.
    /// </summary>
    [Fact]
    public void TheThinkCountSurvivesAHotReload()
    {
        _reload.Clear();

        var handle = new EntityHandle(0x0004_0031u);
        var registry = NewRegistry();
        var live = Barrel(registry, handle);

        live.OnThink();
        live.OnThink();
        live.OnThink();

        Assert.Equal((short)3, live.State.Thinks);

        registry.SaveForReload(_reload);

        var reloaded = NewRegistry();
        Assert.Equal(1, reloaded.RestoreAfterReload(_reload));

        var back = (TestBarrel)reloaded.Get(handle)!;

        Assert.NotSame(live, back);

        // The NEXT think is what the client sees, and it must keep going up.
        back.OnThink();

        Assert.Equal((short)4, back.State.Thinks);
    }

    /// <summary>
    /// And the networked value never travels backwards, which is the property
    /// the client's clause is actually asserting.
    /// </summary>
    [Fact]
    public void TheNetworkedCountNeverGoesBackwardsAcrossAReload()
    {
        _reload.Clear();

        var handle = new EntityHandle(0x0004_0032u);
        var registry = NewRegistry();
        var live = Barrel(registry, handle);

        for (int i = 0; i < 7; i++)
            live.OnThink();

        short before = live.State.Thinks;

        registry.SaveForReload(_reload);

        var reloaded = NewRegistry();
        reloaded.RestoreAfterReload(_reload);

        var back = (TestBarrel)reloaded.Get(handle)!;
        back.OnThink();

        Assert.True(back.State.Thinks > before,
                    $"the count went {before} -> {back.State.Thinks} across a reload");
    }

    //-------------------------------------------------------------------------
    // The client's predicate, driven directly. It takes its values as arguments
    // precisely so it can be - a test that had to bind a net state to reach it
    // would be testing the binding.
    //-------------------------------------------------------------------------

    private static readonly Vector Somewhere = new(-3747f, 2894f, 40f);

    /// <summary>A healthy update names no failing clause.</summary>
    [Fact]
    public void AGoodUpdateNamesNoClause()
        => Assert.Null(ClientTestBarrel.Failure(Model, Somewhere, 1, 0f));

    /// <summary>
    /// The exact update from the live log - the one that printed MISMATCH - is
    /// NOT a mismatch. That is the false alarm, and this is the fact that would
    /// have caught it.
    /// </summary>
    [Fact]
    public void TheUpdateTheLiveRunsCalledAMismatchIsNotOne()
        => Assert.Null(ClientTestBarrel.Failure(
               "models/humans/group03/male_01.mdl", Somewhere, 1, 0f));

    /// <summary>A garbled model string is named, and it is named FIRST.</summary>
    [Fact]
    public void AGarbledModelIsNamed()
    {
        string? why = ClientTestBarrel.Failure("mdl", Somewhere, 1, 0f);

        Assert.NotNull(why);
        Assert.Contains("model", why);
    }

    /// <summary>A zero spawn origin is named.</summary>
    [Fact]
    public void AZeroSpawnOriginIsNamed()
    {
        string? why = ClientTestBarrel.Failure(Model, default, 1, 0f);

        Assert.NotNull(why);
        Assert.Contains("spawnOrigin", why);
    }

    /// <summary>Negative damage is named.</summary>
    [Fact]
    public void NegativeDamageIsNamed()
    {
        string? why = ClientTestBarrel.Failure(Model, Somewhere, 1, -3f);

        Assert.NotNull(why);
        Assert.Contains("lastDamage", why);
    }

    /// <summary>
    /// A count read at the wrong offset is negative about half the time, and
    /// that is the reading the clause keeps.
    /// </summary>
    [Fact]
    public void ANegativeThinkCountIsNamed()
    {
        string? why = ClientTestBarrel.Failure(Model, Somewhere, -21894, 0f);

        Assert.NotNull(why);
        Assert.Contains("thinks", why);
    }

    /// <summary>NaN damage is named rather than passing a >= test.</summary>
    [Fact]
    public void NaNDamageIsNamed()
    {
        string? why = ClientTestBarrel.Failure(Model, Somewhere, 1, float.NaN);

        Assert.NotNull(why);
        Assert.Contains("lastDamage", why);
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A trigger's own state across a <c>cs_sv_reload</c>. Stock saves
/// <c>m_bDisabled</c> (<c>DEFINE_KEYFIELD</c>, triggers.cpp:103) and the
/// re-arm wait as the saved think (<c>SetNextThink( curtime + m_flWait )</c>,
/// :988). The port carried neither, and the reload sweep cannot see it: the
/// default <c>StartDisabled</c> and an unfired timer read the same on both
/// sides. So a trigger the map had switched off came back on.
/// </summary>
public class TriggerStateReloadTests
{
    private static readonly EntityHandle Ent = new(0x0001_0071);
    private static readonly EntityHandle Toucher = new(0x0001_0091);

    private readonly ReloadStore _reload = new(Side.Server);

    private static ScriptedEntityRegistry Registry(out RecordingEngine engine, float now)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(TriggerMultiple).Assembly);
        var services = Services(out engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Clock.Now", now);
        // A trigger_multiple hears touches from players only (DeliverTouchTo).
        engine.Returns("Entities.IsPlayer", true);
        engine.Returns("Players.IsAlive", true);
        engine.Returns("Entities.GetSolidFlags", (SolidFlags)0);
        engine.Returns("Entities.GetSpawnFlags", 1);                       // clients
        engine.Returns("Networking.EntityGetState", EntityState.Player);
        engine.Returns("Networking.EntityGetState.moveType", MoveType.Walk);
        return registry;
    }

    private static int Triggers(ScriptedEntityRegistry registry)
        => ((TriggerMultiple)registry.Get(Ent)!).OnTrigger.FireCount;

    /// <summary>Spawned with <paramref name="wait"/>, set up by <paramref name="before"/>, saved and rebuilt at <paramref name="nowAfter"/>.</summary>
    private ScriptedEntityRegistry Reloaded(string wait, Action<ScriptedEntityRegistry> before, float nowAfter)
    {
        _reload.Clear();
        var first = Registry(out _, now: 10f);
        first.KeyValue(Ent, EntityNames.TriggerMultiple, "wait", wait);
        first.Spawn(Ent, EntityNames.TriggerMultiple);
        before(first);
        first.SaveForReload(_reload);

        var after = Registry(out _, now: nowAfter);
        Assert.Equal(1, after.RestoreAfterReload(_reload));
        return after;
    }

    [Theory]
    [InlineData(false, 1)]   // the control: an enabled trigger fires after the reload
    [InlineData(true, 0)]
    public void A_trigger_switched_off_before_a_reload_stays_off(bool disable, int fired)
    {
        var after = Reloaded("0", r =>
        {
            if (disable)
                Assert.True(r.Input(Ent, "Disable", "", 0f, EntityHandle.None));
        }, nowAfter: 10f);

        int before = Triggers(after);
        after.Touch(Ent, Toucher);
        Assert.Equal(fired, Triggers(after) - before);
    }

    [Theory]
    [InlineData(12f, 0)]     // inside the 10 s wait it fired at t=10
    [InlineData(25f, 1)]     // the control: after the wait it fires again
    public void A_trigger_waiting_to_re_arm_keeps_waiting_across_a_reload(float nowAfter, int fired)
    {
        var after = Reloaded("10", r =>
        {
            r.Touch(Ent, Toucher);
            Assert.Equal(1, Triggers(r));
            r.EndTouch(Ent, Toucher);
        }, nowAfter);

        int before = Triggers(after);
        after.Touch(Ent, Toucher);
        Assert.Equal(fired, Triggers(after) - before);
    }
}

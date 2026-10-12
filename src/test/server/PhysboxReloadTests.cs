using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// func_physbox's own state across a hot reload: the health it started with,
/// whether it is already broken, and whether its motion is enabled.
///
/// Stock carries all three on the entity (CBreakable's m_iMaxHealth, the
/// physics object's motion flag, and a broken box is simply gone); the port
/// kept them in plain fields the reload dropped. The sweep harness cannot see
/// <c>_startingHealth</c>: default keyvalues give health 0, so the before and
/// after values agree. These facts spawn with a real health keyvalue.
/// </summary>
public class PhysboxReloadTests
{
    private static readonly EntityHandle Box = new(0x0001_00B0);

    private readonly ReloadStore _reload = new(Abi.Side.Server);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Side(int spawnFlags = 0)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(FuncPhysbox).Assembly);
        var services = Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Strict = false;
        engine.Returns("Entities.GetSpawnFlags", spawnFlags);
        engine.Returns("Entities.GetHealth", 60);
        engine.Returns("EntityPhysics.InitPhysics", true);
        return (registry, engine);
    }

    private FuncPhysbox Reloaded(ScriptedEntityRegistry before, int spawnFlags = 0)
        => Reloaded(before, out _, spawnFlags);

    private FuncPhysbox Reloaded(ScriptedEntityRegistry before, out ScriptedEntityRegistry after,
                                 int spawnFlags = 0)
    {
        _reload.Clear();
        Assert.Equal(1, before.SaveForReload(_reload));

        (after, _) = Side(spawnFlags);
        Assert.Equal(1, after.RestoreAfterReload(_reload));
        return (FuncPhysbox)after.Get(Box)!;
    }

    [Fact]
    public void A_breakable_box_is_still_breakable_after_the_reload()
    {
        var (before, _) = Side();
        before.KeyValue(Box, EntityNames.FuncPhysbox, "health", "60");
        before.Spawn(Box, EntityNames.FuncPhysbox);

        var box = Reloaded(before);

        // Without its starting health the rebuilt box read as indestructible
        // and took nothing.
        Assert.Equal(25, box.OnDamaged(EntityHandle.None, 25f, DamageType.Bullet));
        Assert.True(box.OnHealthChanged.FireCount == 1, "the hit reports a health change");
    }

    [Fact]
    public void A_broken_box_stays_broken_after_the_reload()
    {
        var (before, _) = Side();
        before.KeyValue(Box, EntityNames.FuncPhysbox, "health", "60");
        before.Spawn(Box, EntityNames.FuncPhysbox);
        var original = (FuncPhysbox)before.Get(Box)!;
        original.OnKilled(EntityHandle.None);
        Assert.True(original.OnBreak.FireCount == 1, "the control: it broke");

        var box = Reloaded(before);

        Assert.Equal(0, box.OnDamaged(EntityHandle.None, 25f, DamageType.Bullet));
        box.OnKilled(EntityHandle.None);
        Assert.True(box.OnBreak.FireCount == 1, "it does not break a second time");
    }

    [Fact]
    public void A_motion_disabled_box_still_announces_being_enabled_after_the_reload()
    {
        const int MotionDisabled = 0x8000;
        var (before, _) = Side(MotionDisabled);
        before.Spawn(Box, EntityNames.FuncPhysbox);

        var box = Reloaded(before, out var after, MotionDisabled);
        int fired = box.OnMotionEnabled.FireCount;

        after.Input(Box, "EnableMotion", "", 0f, EntityHandle.None);

        Assert.Equal(fired + 1, box.OnMotionEnabled.FireCount);
    }
}

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// trigger_apply_impulse's input, read off stock's
/// <c>CTriggerApplyImpulse::InputApplyImpulse</c> (triggers.cpp):
/// <c>ApplyAbsVelocityImpulse( m_flForce * m_vecImpulseDir )</c> on everything
/// touching it, and nothing more -- no <c>SetGroundEntity( NULL )</c>, which
/// now matters, because an impulse reaches a player's movement record.
/// </summary>
public sealed class TriggerApplyImpulseTests
{
    private static readonly EntityHandle Ent = new(0x0011_0011u);
    private static readonly EntityHandle Toucher = new(0x0012_0012u);

    [Fact]
    public void TheImpulseIsTheForceAlongTheDirectionAndKeepsTheGround()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerApplyImpulse).Assembly);
        var services = TestServices.Services(out RecordingEngine engine);
        engine.Ignores("Clock.Now", "Host.RandomFloat", "Entities.GetSpawnOrigin", "Entities.GetSpawnAngles");
        engine.Returns("Entities.IsValid", true);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        registry.KeyValue(Ent, EntityNames.TriggerApplyImpulse, "wait", "0");
        registry.KeyValue(Ent, EntityNames.TriggerApplyImpulse, "impulse_dir", "-90 0 0");
        registry.KeyValue(Ent, EntityNames.TriggerApplyImpulse, "force", "300");
        registry.Spawn(Ent, EntityNames.TriggerApplyImpulse);
        registry.Touch(Ent, Toucher);

        registry.Input(Ent, "ApplyImpulse", "", 0f, EntityHandle.None);

        EngineCall? kick = engine.Last("Entities.ApplyVelocityImpulse");
        Assert.True(kick is { } k && k.ArgAs<EntityHandle>(0) == Toucher && MathF.Abs(k.ArgAs<Vector>(1).Z - 300f) < 0.001f
                    && !k.ArgAs<bool>(2),
                    $"impulse {kick?.ArgAs<Vector>(1)}, clearGround {kick?.ArgAs<bool>(2)}");
    }
}

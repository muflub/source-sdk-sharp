using SourceSharp.Abi;
using Debug.Api;
using Xunit;
using static SourceSharp.Tests.DebugApiGolden;

namespace SourceSharp.Tests;

/// <summary>The members of <see cref="DebugApiGoldenWorldTests"/> other test files use, linked into every test project that needs them (plan_side_isolation P6): the facts stay in one project.</summary>
public partial class DebugApiGoldenWorldTests
{
    internal static RecordingEngine Engine()
    {
        var engine = new RecordingEngine { Strict = false };
        engine.Returns("Entities.FindAllByClassname", new List<EntityHandle> { First, Second })
              .Returns("Entities.FindAllByName", new List<EntityHandle> { First })
              .Returns("Entities.FindByName", First)
              .Returns("Entities.IsValid", true)
              .Returns("Entities.GetIndex", 33)
              .Returns("Entities.GetClassName", "prop_golden")
              .Returns("Entities.GetHealth", 75)
              .Returns("Entities.GetName", "golden")
              .Returns("Entities.GetSolidType", SolidType.VPhysics)
              .Returns("Entities.GetSolidFlags", SolidFlags.NotSolid)
              .Returns("Entities.GetOrigin", new Vector(1.5f, -2f, 3.25f))
              .Returns("Entities.GetAngles", new Vector(0f, 90f, 0f))
              .Returns("Entities.FireInput", true)
              .Returns("Players.GetHandle", Player)
              .Returns("WorldQuery.TracePlayerAim", Second)
              .Returns("WorldQuery.TracePlayerAim.hitPoint", new Vector(10f, 20f, 30f))
              .Returns("Clock.Now", 12.5f)
              .Returns("Host.ManagedDirectory", "/nonexistent/mod/bin/managed");
        return engine;
    }

    internal static ServiceContainer Container(RecordingEngine engine)
    {
        var services = new ServiceContainer();
        services.AddInstance<IEngineEntities>(engine);
        services.AddInstance<IEngineDamage>(engine);
        services.AddInstance<IEnginePlayers>(engine);
        services.AddInstance<IEngineWorldQuery>(engine);
        services.AddInstance<IEngineConsole>(engine);
        services.AddInstance<IEngineHost>(engine);
        services.AddInstance<IGameClock>(engine);
        return services;
    }

    internal static readonly EntityHandle First = new(0x0004_0021u);

    internal static readonly EntityHandle Second = new(0x0005_0022u);

    internal static readonly EntityHandle Player = new(0x0001_0001u);
}

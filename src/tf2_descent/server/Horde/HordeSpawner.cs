using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Shared;

namespace TF2.Descent.Server.Horde;

/// <summary>Makes the horde's entities, and counts the engine's.</summary>
public interface IHordeSpawner
{
    /// <summary>A <c>tf2_descent_horde</c> controller, or null when the engine refuses one.</summary>
    HordeController? Controller();

    /// <summary>A parked <c>tf2_descent_robot</c> body, or null when the engine refuses one.</summary>
    RobotBody? Body();

    /// <summary>A hidden <c>tf2_descent_robot_weapon</c> for <paramref name="body"/>, parented to it, or null when refused.</summary>
    RobotWeapon? Weapon(RobotBody body);

    /// <summary>How many edicts the engine holds now.</summary>
    int Edicts();
}

/// <summary>The engine's create, spawn and directory, and its edict count.</summary>
[SideService(Side.Server, As = [typeof(IHordeSpawner)])]
public sealed class EngineHordeSpawner(IEngineEntities entities, IEntityDirectory directory) : IHordeSpawner
{
    public HordeController? Controller() => Make<HordeController>(HordeController.EntityClass);

    public RobotBody? Body() => Make<RobotBody>(RobotNetState.ClassName);

    public RobotWeapon? Weapon(RobotBody body)
    {
        var weapon = Make<RobotWeapon>(RobotWeaponNetState.ClassName);
        weapon?.Attach(body);
        return weapon;
    }

    public int Edicts() => entities.Count();

    private T? Make<T>(string className) where T : ScriptedEntity
    {
        var h = entities.Create(className);
        if (h == EntityHandle.None)
            return null;

        entities.Spawn(h);
        if (directory.Get(h) is T made)
            return made;

        entities.Remove(h);
        return null;
    }
}

/// <summary>The server's tick, and its rate.</summary>
public interface IHordeClock
{
    /// <summary>The server tick now.</summary>
    long Tick { get; }

    /// <summary>Ticks a second.</summary>
    double TicksPerSecond { get; }
}

/// <summary>The engine's globals: <c>tickcount</c> and <c>1 / interval_per_tick</c>.</summary>
[SideService(Side.Server, As = [typeof(IHordeClock)])]
public sealed class EngineHordeClock(IEngineEnvironment environment) : IHordeClock
{
    /// <summary>The rate when the globals are not there yet: TF2's 66.67.</summary>
    public const double DefaultTicksPerSecond = 1 / 0.015;

    public long Tick => environment.Globals is { IsAvailable: true } g ? g.TickCount : 0;

    public double TicksPerSecond =>
        environment.Globals is { IsAvailable: true, IntervalPerTick: > 0f } g ? 1.0 / g.IntervalPerTick : DefaultTicksPerSecond;
}

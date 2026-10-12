//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Diagnostics;
using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// Stock's <c>air_density</c> (physics.cpp's CC_AirDensity), managed
/// (plan_physics_bones_native_zero 2.3): with no argument it prints the
/// environment's air density, with one it sets it (<c>atof</c>, so a word is 0).
/// Nothing between levels.
/// </summary>
[ConCommand("air_density", Help = "Changes the density of air for drag computations.", Flags = ConVarFlags.Cheat)]
public sealed class AirDensityCommand(IVPhysicsInspect inspect, IEngineEntityPhysics physics, IEngineConsole console)
    : IConCommand
{
    public void Execute(string args) => AirDensity.Run(args, physics.EnvironmentPointer(), inspect, console.Msg);
}

/// <summary>The body of <see cref="AirDensityCommand"/>, testable without a container.</summary>
public static class AirDensity
{
    public static void Run(string args, nint environment, IVPhysicsInspect inspect, Action<string> msg)
    {
        if (environment == 0)
            return;

        string[] words = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 1)
        {
            msg(string.Create(CultureInfo.InvariantCulture,
                $"air_density <value>\nCurrent air density is {inspect.AirDensity(environment):F2}\n"));
            return;
        }

        inspect.SetAirDensity(environment, VariantParse.Atof(words[0]));
    }
}

/// <summary>
/// Stock's <c>physics_budget</c> (physics.cpp; native's physics_debug.cpp at
/// bc21e4d46), managed (plan_physics_bones_native_zero 2.3): one step timed with
/// every active owner awake, then again with each in turn put to sleep with all
/// before it, and each owner's share of the difference printed. A heuristic, as
/// stock says. One object per owner; the owner is the object's game data, the
/// world by identity.
/// </summary>
public sealed class PhysicsBudget
{
    private readonly IVPhysicsInspect _inspect;
    private readonly Action<nint> _forceToSleep;
    private readonly Func<nint, uint?> _ownerKey;
    private readonly Func<nint, (string ClassName, string DebugName)> _names;
    private readonly Func<double> _seconds;
    private readonly Action<string> _msg;
    private readonly Action<string> _developer2;

    /// <param name="forceToSleep">Stock's PhysForceEntityToSleep for one object.</param>
    /// <param name="ownerKey">The object's owner as a key (the world's included), or null for none.</param>
    /// <param name="names">The owner's classname, and its name or else its classname.</param>
    /// <param name="seconds">The wall clock (stock's engine-&gt;Time()).</param>
    /// <param name="developer2">Prints under <c>developer 2</c> (stock's DevMsg( 2, ... )).</param>
    public PhysicsBudget(IVPhysicsInspect inspect, Action<nint> forceToSleep, Func<nint, uint?> ownerKey,
                         Func<nint, (string ClassName, string DebugName)> names, Func<double> seconds,
                         Action<string> msg, Action<string> developer2)
    {
        _inspect = inspect;
        _forceToSleep = forceToSleep;
        _ownerKey = ownerKey;
        _names = names;
        _seconds = seconds;
        _msg = msg;
        _developer2 = developer2;
    }

    public void Run(nint environment, float tickInterval)
    {
        nint[] active = _inspect.ActiveObjects(environment);
        if (active.Length == 0)
            return;

        var ents = new List<nint>();
        var keys = new List<uint>();
        foreach (nint o in active)
        {
            if (_ownerKey(o) is not uint key || keys.Contains(key))
                continue;
            keys.Add(key);
            ents.Add(o);
        }

        if (ents.Count == 0)
            return;

        var times = new float[ents.Count];
        float totalTime = 0f;
        float full = (float)_seconds();
        _inspect.Simulate(environment, tickInterval);
        full = (float)_seconds() - full;
        float lastTime = full;

        for (int i = 0; i < ents.Count; i++)
        {
            for (int j = 0; j < i; j++)
                Sleep(ents[j]);

            float start = (float)_seconds();
            _inspect.Simulate(environment, tickInterval);
            float end = (float)_seconds();

            float elapsed = end - start;
            float avgTime = lastTime - elapsed;
            times[i] = Math.Clamp(avgTime, 0.00001f, 1.0f);
            totalTime += times[i];
            lastTime = elapsed;
        }

        totalTime = MathF.Max(totalTime, 0.001f);
        for (int i = 0; i < ents.Count; i++)
        {
            float fraction = times[i] / totalTime;
            (string className, string debugName) = _names(ents[i]);
            Vector at = _inspect.Read(ents[i]).Position;
            _msg(string.Create(CultureInfo.InvariantCulture,
                $"{className} ({debugName}): {fraction * totalTime * 1000.0f:F3}ms ({fraction * 100.0f:F3}%) @ ({at.X:F6}, {at.Y:F6}, {at.Z:F6})\n"));
        }
    }

    // PhysForceEntityToSleep, with its developer-2 line for a moveable object.
    private void Sleep(nint o)
    {
        if (_inspect.Read(o).Moveable)
            _developer2($"Putting entity to sleep: {_names(o).ClassName}\n");
        _forceToSleep(o);
    }
}

/// <summary>
/// The <c>[SS-PHYSWORLD-NAN]</c> line: what the player shadows' non-finite
/// guard refused (plan_physics_bones_native_zero 2.5 moved the guard to
/// managed with the shadow, its only caller; native's ss_physworld_bounds
/// printed its own ledger, which nothing feeds since). Native's format,
/// <c>%u</c>, <c>%d</c> and three <c>%f</c>, so tools/defect-shadow-probe reads
/// it unchanged.
/// </summary>
public static class PhysWorldNan
{
    public static string Line(PhysicsNanGuard guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        Vector v = guard.FirstValue;
        return string.Create(CultureInfo.InvariantCulture,
            $"[SS-PHYSWORLD-NAN] refusals={guard.Refusals} firstSite={guard.FirstSite} firstValue=({v.X:F6} {v.Y:F6} {v.Z:F6})\n");
    }
}

#if DEBUG
/// <summary><c>ss_physworld_nan</c>: <see cref="PhysWorldNan"/>, to the report's sink (tools/defect-shadow-probe drives it).</summary>
[ConCommand("ss_physworld_nan", Help = "SourceSharp: what the player shadows' non-finite guard refused")]
public sealed class PhysWorldNanCommand(PlayerShadowBodies bodies, IEngineConsole console) : IConCommand
{
    public void Execute(string args) => new PhysWorldReportSink(console).Emit(PhysWorldNan.Line(bodies.Nan));
}

/// <summary><c>ss_physworld_report</c>: the SS-PHYSWORLD report, on demand (tools/3f-gatebisect drives it).</summary>
[ConCommand("ss_physworld_report", Help = "SourceSharp: is there a world for VPhysics to stand on?")]
public sealed class PhysWorldReportCommand(PhysWorldReport report) : IConCommand
{
    public void Execute(string args) => report.Run();
}

/// <summary><c>physics_budget</c>: <see cref="PhysicsBudget"/>, for the server admin.</summary>
[ConCommand("physics_budget", Help = "Times the cost of each active object")]
public sealed class PhysicsBudgetCommand(PhysicsSolver solver, IVPhysicsInspect inspect, IEngineEntityPhysics physics,
                                         IEngineEntities entities, IEnginePlayers players, IEngineHost host,
                                         IEngineEnvironment environment, IEngineConsole console) : IConCommand
{
    public void Execute(string args)
    {
        if (!ServerAdmin.MayRun(players, host))
            return;

        nint env = physics.EnvironmentPointer();
        if (env == 0)
            return;

        nint world = physics.WorldObject();
        var objects = new VPhysicsSolverObjects();
        var clock = Stopwatch.StartNew();

        uint? OwnerKey(nint o)
        {
            if (o == world)
                return EntityPairCollisions.WorldKey;
            EntityHandle owner = PhysicsGameData.Owner(objects.GameData(o));
            return owner != EntityHandle.None && entities.IsValid(owner) ? owner.Value : null;
        }

        (string, string) Names(nint o)
        {
            if (o == world)
                return ("worldspawn", "worldspawn");
            EntityHandle owner = PhysicsGameData.Owner(objects.GameData(o));
            string className = entities.GetClassName(owner);
            string name = entities.GetName(owner);
            return (className, string.IsNullOrEmpty(name) ? className : name);
        }

        void Developer2(string text)
        {
            if (int.TryParse(NativeConsole.ValueOf(console, "developer"), out int level) && level >= 2)
                console.Msg(text);
        }

        new PhysicsBudget(inspect, solver.ForceEntityToSleep, OwnerKey, Names, () => clock.Elapsed.TotalSeconds,
                          console.Msg, Developer2)
            .Run(env, environment.Globals.IntervalPerTick);
    }
}
#endif

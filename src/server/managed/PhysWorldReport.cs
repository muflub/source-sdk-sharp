//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// Where the SS-PHYSWORLD lines go: the console, and a per-process file
/// (<c>/tmp/ss-physworld-&lt;pid&gt;.log</c>) so parallel probes each read their
/// own (tools/3f-gatebisect, gateseq-probe, 8e-physfreeze).
/// </summary>
public interface IPhysWorldReportSink
{
    void Emit(string text);
}

/// <summary>The console and the per-pid file, as native's PhysWorldReportEmit wrote them.</summary>
public sealed class PhysWorldReportSink(IEngineConsole console) : IPhysWorldReportSink
{
    /// <summary>The file a process' lines are appended to.</summary>
    public static string PathFor(int pid) => $"/tmp/ss-physworld-{pid}.log";

    public void Emit(string text)
    {
        console.Msg(text);

        try
        {
            File.AppendAllText(PathFor(Environment.ProcessId), text);
        }
        catch (IOException)
        {
            // The console line is the report; the file is a convenience.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Lane 3f-shadow's SS-PHYSWORLD report, managed (plan_physics_bones_native_zero
/// 2.3; native's SourceSharp_PhysWorldReport, physics.cpp at bc21e4d46). It
/// answers which of three things makes every prop fall: no world physics object,
/// a world object with no collision, or a rule that refuses a prop against the
/// world -- and, for the first few objects below the map or with collisions
/// off, every input that rule read.
///
/// <para>
/// Diagnostics, run on demand (<c>ss_physworld_report</c>, debug builds) or once
/// per level when <c>sourcesharp_physworld_report</c> is on. The rule it asks
/// is managed's own (<see cref="PhysicsSolver.AgainstTheWorld"/>); native's
/// rule table went with it.
/// </para>
/// </summary>
public sealed class PhysWorldReport : IGameLifecycle
{
    /// <summary>An object this far below the origin has fallen out of the map.</summary>
    public const float BelowTheMap = -1000f;

    /// <summary>How many objects of each kind get a line of their own.</summary>
    public const int Named = 4;

    private readonly IVPhysicsInspect _inspect;
    private readonly Func<nint> _environment;
    private readonly Func<nint> _world;
    private readonly PhysicsSolver _solver;
    private readonly IPhysWorldReportSink _sink;
    private readonly Func<(int Live, int Outlived)> _shadows;
    private readonly Func<bool> _automatic;

    /// <param name="environment">The level's IPhysicsEnvironment, or 0 between levels.</param>
    /// <param name="world">The level's world physics object, or 0.</param>
    /// <param name="shadows">The player shadows in the solver, and those that outlived their environment.</param>
    /// <param name="automatic"><c>sourcesharp_physworld_report</c>: run once per level at post-entity init.</param>
    public PhysWorldReport(IVPhysicsInspect inspect, Func<nint> environment, Func<nint> world, PhysicsSolver solver,
                           IPhysWorldReportSink sink, Func<(int Live, int Outlived)> shadows, Func<bool> automatic)
    {
        _inspect = inspect;
        _environment = environment;
        _world = world;
        _solver = solver;
        _sink = sink;
        _shadows = shadows;
        _automatic = automatic;
    }

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PhysWorldReport;

    /// <summary>The automatic run, at the moment the failure it chases shows.</summary>
    public void OnLevelInitPostEntity(string mapName)
    {
        if (_automatic())
            Run();
    }

    /// <summary>One report: the detail lines first, then the summary line.</summary>
    public void Run()
    {
        nint env = _environment();
        nint world = _world();

        var head = new StringBuilder();
        head.Append(Inv($"[SS-PHYSWORLD] physenv={P(env)} world={P(world)}"));

        if (env == 0)
        {
            head.Append(" NO ENVIRONMENT\n");
            _sink.Emit(head.ToString());
            return;
        }

        nint[] objects = _inspect.Objects(env);
        head.Append(Inv($" objects={objects.Length}"));

        if (world == 0)
        {
            head.Append(" NO WORLD OBJECT -- every prop falls\n");
            _sink.Emit(head.ToString());
            return;
        }

        PhysicsObjectReadout w = _inspect.Read(world);
        head.Append(Inv($" worldStatic={B(w.Static)} worldEnabled={B(w.CollisionEnabled)} worldContents={w.Contents:x8}"));

        // The world is known by identity: it is always owned, and always the world.
        head.Append(" worldIsOwned=1 worldIsWorld=1");

        // The rules: the group rows the matrix sets, and stock's two constants
        // (an unowned pair collides; every solid type but SOLID_NONE can).
        head.Append(Inv($" ruleRowsSet={RowsSet()} unownedCollides=1 collidableSolids={0xFFFFFFFEu:x8}"));

        int moveable = 0, refused = 0, shadowed = 0, collisionsOff = 0, below = 0;

        foreach (nint o in objects)
        {
            if (o == 0 || o == world)
                continue;

            PhysicsObjectReadout r = _inspect.Read(o);
            if (r.Static)
                continue;

            moveable++;

            // Collisions off is not a rule: the table cannot see it, and an
            // object with it off falls through the world whatever the rules say.
            if (!r.CollisionEnabled)
            {
                collisionsOff++;
                if (collisionsOff <= Named)
                {
                    _sink.Emit(Inv($"[SS-PHYSWORLD-NOCOLLIDE] obj={P(o)} name={r.Name ?? "?"} ") +
                               Inv($"static={B(r.Static)} moveable={B(r.Moveable)} contents={r.Contents:x8}\n"));
                }
            }

            WorldVerdict verdict = _solver.AgainstTheWorld(o);
            CollisionSide side = verdict.Side;

            if (side.Shadow)
                shadowed++;

            if (r.Position.Z < BelowTheMap)
            {
                below++;
                if (below <= Named)
                {
                    // Props of one model share one collide: a fallen one nobody
                    // shares with has a hull the healthy ones do not.
                    int sharesCollide = 0;
                    foreach (nint other in objects)
                    {
                        if (other != 0 && other != o && _inspect.Read(other).Collide == r.Collide)
                            sharesCollide++;
                    }

                    _sink.Emit(
                        Inv($"[SS-PHYSWORLD-FELL] obj={P(o)} name={r.Name ?? "?"} ") +
                        Inv($"at=({r.Position.X:F1} {r.Position.Y:F1} {r.Position.Z:F1}) collisions={B(r.CollisionEnabled)} asleep={B(r.Asleep)} ") +
                        Inv($"valid={B(side.Valid)} solid={side.Solid} solidFlags={side.SolidFlags:x8} moveType={side.MoveType} ") +
                        Inv($"group={side.CollisionGroup} contents={side.Contents:x8} solidMask={side.SolidMask:x8} ") +
                        Inv($"collide={P(r.Collide)} sharesCollide={sharesCollide} mass={r.Mass:F1} ") +
                        Inv($"sphere={r.SphereRadius:F2}\n"));
                }
            }

            if (verdict.Answer != 0)
                continue;

            refused++;
            if (refused > Named)
                continue;

            CollisionSide ws = verdict.World;
            _sink.Emit(
                Inv($"[SS-PHYSWORLD-REFUSED] obj={P(o)} name={r.Name ?? "?"} valid={B(side.Valid)} solid={side.Solid} ") +
                Inv($"solidFlags={side.SolidFlags:x8} moveType={side.MoveType} group={side.CollisionGroup} contents={side.Contents:x8} ") +
                Inv($"solidMask={side.SolidMask:x8} gameFlags={side.GameFlags:x8} moveable={B(side.Moveable)} trigger={B(side.Trigger)} ") +
                Inv($"shadow={B(side.Shadow)} sameEntity={B(verdict.SameEntity)} pairInHash=0 | world solid={ws.Solid} ") +
                Inv($"solidFlags={ws.SolidFlags:x8} moveType={ws.MoveType} group={ws.CollisionGroup} contents={ws.Contents:x8} ") +
                Inv($"solidMask={ws.SolidMask:x8}\n"));
        }

        (int live, int outlived) = _shadows();
        head.Append(Inv($" moveable={moveable} shadowed={shadowed} refusedByTheWorld={refused} collisionsOff={collisionsOff}"));
        head.Append(Inv($" objectsBelowTheMap={below} liveShadows={live} outlivedTheirEnvironment={outlived}\n"));
        _sink.Emit(head.ToString());
    }

    /// <summary>How many of the twenty collision-group rows allow anything.</summary>
    private int RowsSet() => _solver.Rules.RowsSet(20);

    /// <summary>glibc's <c>%p</c>: <c>(nil)</c> for 0.</summary>
    public static string P(nint pointer) => pointer == 0 ? "(nil)" : $"0x{(ulong)pointer:x}";

    private static int B(bool b) => b ? 1 : 0;

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

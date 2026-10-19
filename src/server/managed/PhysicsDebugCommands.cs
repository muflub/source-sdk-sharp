//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using BigInteger = System.Numerics.BigInteger;

namespace SourceSharp.Server;

/// <summary>
/// glibc's <c>printf( "%f" )</c> of a float, which a varargs call promotes to
/// double: six decimals, correctly rounded (half to even), <c>-0.000000</c> for
/// a negative zero, and <c>nan</c>, <c>-nan</c>, <c>inf</c>, <c>-inf</c>. The
/// SS-PHYSWORLD probes' lines are parsed by tools, so they keep native's digits.
/// </summary>
public static class PrintfFixed
{
    private static readonly BigInteger Million = 1_000_000;

    public static string F6(float value)
    {
        bool negative = BitConverter.SingleToUInt32Bits(value) >> 31 != 0;

        if (float.IsNaN(value))
            return negative ? "-nan" : "nan";
        if (float.IsInfinity(value))
            return negative ? "-inf" : "inf";

        // The exact value, |d| = mantissa * 2^exponent, scaled by 10^6 and
        // rounded half to even: what glibc prints, digit for digit.
        ulong bits = BitConverter.DoubleToUInt64Bits(Math.Abs((double)value));
        int rawExponent = (int)(bits >> 52);
        ulong mantissa = bits & ((1UL << 52) - 1);
        if (rawExponent != 0)
            mantissa |= 1UL << 52;
        int exponent = (rawExponent == 0 ? 1 : rawExponent) - 1075;

        BigInteger scaled = new BigInteger(mantissa) * Million;
        if (exponent >= 0)
        {
            scaled <<= exponent;
        }
        else
        {
            BigInteger denominator = BigInteger.One << -exponent;
            BigInteger quotient = BigInteger.DivRem(scaled, denominator, out BigInteger remainder);
            BigInteger twice = remainder * 2;
            if (twice > denominator || (twice == denominator && !quotient.IsEven))
                quotient += 1;
            scaled = quotient;
        }

        BigInteger whole = BigInteger.DivRem(scaled, Million, out BigInteger fraction);
        return string.Create(CultureInfo.InvariantCulture,
            $"{(negative ? "-" : "")}{whole}.{(int)fraction:D6}");
    }
}

/// <summary>
/// physics_debug.cpp's three SS-PHYSWORLD instruments, managed (2026-10-18):
/// <c>ss_physworld_recheck</c>, <c>ss_physworld_bounds</c> and
/// <c>ss_physworld_object</c>, with native's lines and sinks, which
/// tools/3f-gatebisect, defect-shadow-probe, phys-wakesleep-gate and
/// c8-clientsystems-gate parse.
/// </summary>
public sealed class PhysWorldProbes
{
    /// <summary>MAX_COORD_INTEGER (16384) twice: nothing in a legal map is past it.</summary>
    public const float BoundsLimit = 2f * 16384f;

    /// <summary>How many out-of-bounds objects get a line of their own.</summary>
    public const int OutOfBoundsNamed = 8;

    /// <summary>MAX_MAP_DISPINFO: the displacement indices that may have a virtual-terrain collide.</summary>
    public const int MaxMapDispInfo = 2048;

    private readonly IVPhysicsInspect _inspect;
    private readonly Func<nint> _environment;
    private readonly Func<nint, (Vector Mins, Vector Maxs)>? _hull;
    private readonly Func<int, nint> _virtualTerrain;
    private readonly IPhysWorldReportSink _sink;
    private readonly Action<string> _msg;

    /// <param name="environment">The level's IPhysicsEnvironment, or 0 between levels.</param>
    /// <param name="hull">IPhysicsCollision::CollideGetAABB of a collide at the origin; null when there is no collision interface.</param>
    /// <param name="virtualTerrain">modelinfo-&gt;GetCollideForVirtualTerrain( index ), or 0.</param>
    /// <param name="msg">The console alone (ss_physworld_recheck's no-environment line).</param>
    public PhysWorldProbes(IVPhysicsInspect inspect, Func<nint> environment,
                           Func<nint, (Vector Mins, Vector Maxs)>? hull, Func<int, nint> virtualTerrain,
                           IPhysWorldReportSink sink, Action<string> msg)
    {
        _inspect = inspect ?? throw new ArgumentNullException(nameof(inspect));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _hull = hull;
        _virtualTerrain = virtualTerrain ?? throw new ArgumentNullException(nameof(virtualTerrain));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _msg = msg ?? throw new ArgumentNullException(nameof(msg));
    }

    /// <summary>
    /// <c>ss_physworld_recheck</c>: re-ask vphysics' cached collision filter of
    /// every moveable object. ivp asks IPhysicsCollisionSolver::ShouldCollide
    /// once per pair and caches the answer until RecheckCollisionFilter, so a
    /// pair refused once stays refused; if a falling object lands after this,
    /// the cache was the problem and not the rule.
    /// </summary>
    public void Recheck()
    {
        nint env = _environment();
        if (env == 0)
        {
            _msg("[SS-PHYSWORLD] no environment\n");
            return;
        }

        int asked = 0;
        foreach (nint o in _inspect.Objects(env))
        {
            if (o == 0 || _inspect.Read(o).Static)
                continue;
            _inspect.RecheckCollisionFilter(o);
            asked++;
        }

        _sink.Emit(Inv($"[SS-PHYSWORLD-RECHECK] re-asked the filter for {asked} moveable object(s)\n"));
    }

    /// <summary>
    /// <c>ss_physworld_bounds</c>: every object at a non-finite or out-of-map
    /// position, or with a non-finite hull. A NaN becomes 0x80000000 when x86
    /// converts it to an integer, so one such object makes the broadphase
    /// enclose a point 2^31 cells away; this names it. The tests read bits, as
    /// native's did for its -ffast-math build.
    /// </summary>
    public void Bounds()
    {
        nint env = _environment();
        if (env == 0)
        {
            _sink.Emit("[SS-PHYSWORLD-BOUNDS] usable=0 why=no-environment\n");
            return;
        }

        // The displacement terrain's collides, whose hull vphysics cannot
        // answer: CollideGetAABB segfaults on a virtual mesh. Matched by
        // pointer, never by the "vdisp_%04d" name.
        var terrain = new HashSet<nint>();
        for (int i = 0; i < MaxMapDispInfo; i++)
        {
            nint collide = _virtualTerrain(i);
            if (collide != 0)
                terrain.Add(collide);
        }

        uint limitBits = BitConverter.SingleToUInt32Bits(BoundsLimit);
        nint[] objects = _inspect.Objects(env);
        int bad = 0;
        int printed = 0;

        for (int i = 0; i < objects.Length; i++)
        {
            nint o = objects[i];
            if (o == 0)
                continue;

            PhysicsObjectReadout r = _inspect.Read(o);
            bool finite = PhysicsNanGuard.IsFinite(r.Position) && PhysicsNanGuard.IsFinite(r.Angles);
            bool inBounds = (BitConverter.SingleToUInt32Bits(r.Position.X) & 0x7FFFFFFFu) < limitBits
                         && (BitConverter.SingleToUInt32Bits(r.Position.Y) & 0x7FFFFFFFu) < limitBits
                         && (BitConverter.SingleToUInt32Bits(r.Position.Z) & 0x7FFFFFFFu) < limitBits;

            // Its size as well as its place; a virtual mesh counts as sane.
            bool saneHull = true;
            Vector mins = Vector.Zero, maxs = Vector.Zero;
            if (r.Collide != 0 && !terrain.Contains(r.Collide) && _hull is not null)
            {
                (mins, maxs) = _hull(r.Collide);
                saneHull = PhysicsNanGuard.IsFinite(mins) && PhysicsNanGuard.IsFinite(maxs);
            }

            if (finite && inBounds && saneHull)
                continue;

            bad++;
            if (printed >= OutOfBoundsNamed)
                continue;
            printed++;

            _sink.Emit(
                Inv($"[SS-PHYSWORLD-OUTOFBOUNDS] index={i} name='{r.Name ?? "(null)"}' pos=({V(r.Position)}) ") +
                Inv($"ang=({V(r.Angles)}) hull=({V(mins)})..({V(maxs)}) ") +
                Inv($"finite={B(finite)} inBounds={B(inBounds)} saneHull={B(saneHull)} static={B(r.Static)} asleep={B(r.Asleep)}\n"));
        }

        _sink.Emit(Inv($"[SS-PHYSWORLD-BOUNDS] objects={objects.Length} outOfBounds={bad} limit={PrintfFixed.F6(BoundsLimit)}\n"));
    }

    /// <summary>
    /// <c>ss_physworld_object &lt;name substring&gt;</c>: where vphysics thinks
    /// each matching body is, and whether it sleeps. "*" is every object
    /// (<see cref="PhysWorldMatch"/>).
    /// </summary>
    /// <param name="argv">The words after the verb.</param>
    public void Object(string[] argv)
    {
        ArgumentNullException.ThrowIfNull(argv);

        if (argv.Length < 1)
        {
            _sink.Emit("[SS-PHYSWORLD-OBJECT] usage: ss_physworld_object <name substring>\n");
            return;
        }

        nint env = _environment();
        if (env == 0)
        {
            _sink.Emit("[SS-PHYSWORLD-OBJECT] no environment\n");
            return;
        }

        string wanted = argv[0];
        nint[] objects = _inspect.Objects(env);
        int found = 0;

        foreach (nint o in objects)
        {
            if (o == 0)
                continue;

            PhysicsObjectReadout r = _inspect.Read(o);
            if (!PhysWorldMatch.Matches(r.Name, wanted))
                continue;

            found++;
            _sink.Emit(
                Inv($"[SS-PHYSWORLD-OBJECT] name='{r.Name ?? ""}' pos=({V(r.Position)}) vel=({V(r.Velocity)}) ") +
                Inv($"asleep={B(r.Asleep)} moveable={B(r.Moveable)} mass={PrintfFixed.F6(r.Mass)} gameFlags={r.GameFlags:x4}\n"));
        }

        _sink.Emit(Inv($"[SS-PHYSWORLD-OBJECT] matched={found} of {objects.Length} for '{wanted}'\n"));
    }

    private static string V(Vector v) => $"{PrintfFixed.F6(v.X)} {PrintfFixed.F6(v.Y)} {PrintfFixed.F6(v.Z)}";

    private static int B(bool b) => b ? 1 : 0;

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Stock's <c>physics_report_active</c> (physics.cpp's IterateActivePhysicsEntities
/// and CallbackReport): one <c>classname - name</c> line per active object
/// whose owner is valid. The name is the entity's, or its classname when it has
/// none (native's PhysicsOwner_t::GetDebugName; stock fell back to the model
/// name).
/// </summary>
public static class PhysicsActiveReport
{
    /// <param name="owner">The object's owner's classname and name, or null for no valid owner.</param>
    /// <returns>How many objects were reported.</returns>
    public static int Run(nint[] active, Func<nint, (string ClassName, string DebugName)?> owner, Action<string> msg)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(msg);

        int reported = 0;
        foreach (nint o in active)
        {
            if (owner(o) is not (string className, string debugName))
                continue;
            msg($"{className} - {debugName}\n");
            reported++;
        }

        return reported;
    }
}

/// <summary>
/// Stock's <c>physics_select</c> (physics.cpp's PhysicsCommand and
/// MarkVPhysicsDebug): with no argument the entity under the player's aim,
/// otherwise every entity of that name, or of that classname when none has
/// the name (FindEntityGeneric's order); each one's physics object has its
/// CALLBACK_MARKED_FOR_TEST bit toggled.
/// </summary>
public static class PhysicsSelect
{
    /// <summary>CALLBACK_MARKED_FOR_TEST (vphysics_interface.h).</summary>
    public const ushort CallbackMarkedForTest = 0x8000;

    public static List<EntityHandle> Subjects(string[] argv, Func<EntityHandle> aim,
                                              Func<string, IReadOnlyList<EntityHandle>> byName,
                                              Func<string, IEnumerable<EntityHandle>> byClass)
    {
        ArgumentNullException.ThrowIfNull(argv);
        ArgumentNullException.ThrowIfNull(aim);
        ArgumentNullException.ThrowIfNull(byName);
        ArgumentNullException.ThrowIfNull(byClass);

        if (argv.Length < 1)
        {
            EntityHandle hit = aim();
            return hit == EntityHandle.None ? [] : [hit];
        }

        IReadOnlyList<EntityHandle> named = byName(argv[0]);
        return named.Count > 0 ? named.ToList() : byClass(argv[0]).ToList();
    }

    /// <returns>How many physics objects were toggled.</returns>
    public static int Mark(IEnumerable<EntityHandle> subjects, Func<EntityHandle, nint> physicsObject,
                           IVPhysicsInspect inspect)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(physicsObject);
        ArgumentNullException.ThrowIfNull(inspect);

        int marked = 0;
        foreach (EntityHandle entity in subjects)
        {
            nint o = physicsObject(entity);
            if (o == 0)
                continue;
            inspect.SetCallbackFlags(o, (ushort)(inspect.CallbackFlags(o) ^ CallbackMarkedForTest));
            marked++;
        }

        return marked;
    }
}

#if DEBUG
/// <summary><c>ss_physworld_recheck</c>: <see cref="PhysWorldProbes.Recheck"/> (tools/3f-gatebisect drives it).</summary>
[ConCommand("ss_physworld_recheck", Help = "SourceSharp: re-ask vphysics's cached collision filter for every object")]
public sealed class PhysWorldRecheckCommand(PhysWorldProbes probes) : IConCommand
{
    public void Execute(string args) => probes.Recheck();
}

/// <summary><c>ss_physworld_bounds</c>: <see cref="PhysWorldProbes.Bounds"/> (tools/defect-shadow-probe drives it).</summary>
[ConCommand("ss_physworld_bounds", Help = "SourceSharp: list physics objects at a non-finite or out-of-map position")]
public sealed class PhysWorldBoundsCommand(PhysWorldProbes probes) : IConCommand
{
    public void Execute(string args) => probes.Bounds();
}

/// <summary><c>ss_physworld_object &lt;substring&gt;</c>: <see cref="PhysWorldProbes.Object"/>.</summary>
[ConCommand("ss_physworld_object", Help = "SourceSharp: position, velocity and sleep state of every physics object whose name contains the argument")]
public sealed class PhysWorldObjectCommand(PhysWorldProbes probes) : IConCommand
{
    public void Execute(string args) => probes.Object(CommandTokens.Split(args));
}

/// <summary>
/// Stock's <c>physics_highlight_active</c>, for the server admin. Stock set
/// OVERLAY_ABSBOX_BIT | OVERLAY_PIVOT_BIT in each active owner's
/// <c>m_debugOverlays</c>, which CBaseEntity::DrawDebugGeometryOverlays drew;
/// no entity of this build has that field or that drawer, so it marks nothing,
/// as native's did.
/// </summary>
[ConCommand("physics_highlight_active", Help = "Turns on the absbox for all active physics objects")]
public sealed class PhysicsHighlightActiveCommand(IEnginePlayers players, IEngineHost host) : IConCommand
{
    public void Execute(string args)
    {
        if (!ServerAdmin.MayRun(players, host))
            return;
    }
}

/// <summary>Stock's <c>physics_report_active</c>: <see cref="PhysicsActiveReport"/>, for the server admin.</summary>
[ConCommand("physics_report_active", Help = "Lists all active physics objects")]
public sealed class PhysicsReportActiveCommand(IVPhysicsInspect inspect, IEngineEntityPhysics physics,
                                               IEngineEntities entities, IEnginePlayers players, IEngineHost host,
                                               IEngineConsole console, VPhysicsTables vphysics) : IConCommand
{
    public void Execute(string args)
    {
        if (!ServerAdmin.MayRun(players, host))
            return;

        nint env = physics.EnvironmentPointer();
        if (env == 0)
            return;

        nint world = physics.WorldObject();
        var objects = new VPhysicsSolverObjects(vphysics);

        (string, string)? Owner(nint o)
        {
            if (o == world)
                return ("worldspawn", "worldspawn");
            EntityHandle owner = PhysicsGameData.Owner(objects.GameData(o));
            if (owner == EntityHandle.None || !entities.IsValid(owner))
                return null;
            string className = entities.GetClassName(owner);
            string name = entities.GetName(owner);
            return (className, string.IsNullOrEmpty(name) ? className : name);
        }

        PhysicsActiveReport.Run(inspect.ActiveObjects(env), Owner, console.Msg);
    }
}

/// <summary>
/// Stock's <c>physics_select [name]</c>: <see cref="PhysicsSelect"/>, for the
/// server admin. The aim is traced MAX_COORD_RANGE (16384) from the player
/// who typed it.
/// </summary>
[ConCommand("physics_select", Help = "Dumps debug info for an entity")]
public sealed class PhysicsSelectCommand(IVPhysicsInspect inspect, IEngineEntityPhysics physics,
                                         IEngineEntities entities, IEngineWorldQuery world,
                                         IEnginePlayers players, IEngineHost host) : IConCommand
{
    public const float AimRange = 16384f;

    public void Execute(string args)
    {
        if (!ServerAdmin.MayRun(players, host))
            return;

        EntityHandle Aim()
        {
            EntityHandle issuer = DevContext.WhoTyped(players);
            return issuer == EntityHandle.None ? EntityHandle.None : world.TracePlayerAim(issuer, AimRange, out _);
        }

        List<EntityHandle> subjects = PhysicsSelect.Subjects(CommandTokens.Split(args), Aim,
                                                             name => entities.FindAllByName(name),
                                                             entities.FindAllByClassname);
        PhysicsSelect.Mark(subjects, physics.PhysicsObject, inspect);
    }
}
#endif

using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>An engine interface whose answers a fact names by method; any other call is a failure.</summary>
public class FakeEngine : DispatchProxy
{
    private Func<MethodInfo, object?[], object?>? _answer;

    public static T Of<T>(Func<MethodInfo, object?[], object?> answer) where T : class
    {
        T proxy = Create<T, FakeEngine>();
        ((FakeEngine)(object)proxy)._answer = answer;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args) => _answer!(method!, args ?? []);

    public static Exception Unasked(MethodInfo m) => new InvalidOperationException($"asked {m.DeclaringType?.Name}.{m.Name}");
}

/// <summary>
/// A world for the senses' facts: players as boxes of CONTENTS_MONSTER, and
/// occluders as boxes of named contents (a wall, a door, a crate, a window, a
/// grate). A line stops at the nearest box whose contents the mask holds and
/// that is not the pass entity, as the engine's trace does.
/// </summary>
public sealed class SenseWorld
{
    public const uint Solid = 0x1, Window = 0x2, Grate = 0x8, Moveable = 0x4000, Monster = 0x2000000;

    public sealed class Player
    {
        public EntityHandle Handle;
        public int Slot;
        public AiVector Origin;
        public AiVector Angles;
        public AiVector Velocity;
        public float EyeHeight = 64f;
        public bool Alive = true;
        public string Weapon = "weapon_smg1";
    }

    public readonly record struct Box(AiVector Min, AiVector Max, uint Contents, EntityHandle Entity);

    public readonly List<Player> Players = new();
    public readonly List<Box> Boxes = new();

    /// <summary>Items lying in the world: a class, a place, a handle (index 500 up).</summary>
    public readonly List<(string Class, AiVector At, EntityHandle Handle)> Items = new();

    /// <summary>The PVS's answer: (bot, box mins, box maxs) to visible. Everything, unless a fact says otherwise.</summary>
    public Func<EntityHandle, Vector, Vector, bool> Pvs { get; set; } = (_, _, _) => true;

    public int Lines { get; private set; }

    public Player Add(int slot, AiVector origin, float yaw = 0f)
    {
        var p = new Player { Handle = new EntityHandle((uint)(slot | (7 << ActorDirectory.EntryBits))), Slot = slot, Origin = origin, Angles = new AiVector(0, yaw, 0) };
        Players.Add(p);
        return p;
    }

    public void Occluder(AiVector min, AiVector max, uint contents, int entity = 0)
        => Boxes.Add(new Box(min, max, contents, entity == 0 ? EntityHandle.None : new EntityHandle((uint)entity)));

    private Player By(EntityHandle h) => Players.Single(p => p.Handle == h);

    private static Vector V(AiVector v) => new(v.X, v.Y, v.Z);

    public IEngineEntities Entities() => FakeEngine.Of<IEngineEntities>((m, a) => m.Name switch
    {
        "GetOrigin" => Items.Any(i => i.Handle == (EntityHandle)a[0]!) ? V(Items.First(i => i.Handle == (EntityHandle)a[0]!).At) : V(By((EntityHandle)a[0]!).Origin),
        "GetEyePosition" => V(By((EntityHandle)a[0]!).Origin + new AiVector(0, 0, By((EntityHandle)a[0]!).EyeHeight)),
        "GetVelocity" => V(By((EntityHandle)a[0]!).Velocity),
        "GetEyeAngles" => V(By((EntityHandle)a[0]!).Angles),
        "FindAllByClassname" => Items.Where(i => i.Class == (string)a[0]!).Select(i => i.Handle).ToArray(),
        "GetIndex" => (int)((EntityHandle)a[0]!).Value & 0x1FFF,
        _ => throw FakeEngine.Unasked(m),
    });

    public IEnginePlayers Engine() => FakeEngine.Of<IEnginePlayers>((m, a) => m.Name switch
    {
        "BoxInPvs" => Pvs((EntityHandle)a[0]!, (Vector)a[1]!, (Vector)a[2]!),
        "GetActiveWeapon" => By((EntityHandle)a[0]!).Weapon,
        _ => throw FakeEngine.Unasked(m),
    });

    public ITraceQueries Traces() => new Tracer(this);

    public SensedActor[] Living(params SensedBot[] bots)
        => [.. Players.Where(p => p.Alive).OrderBy(p => p.Slot)
                      .Select(p => new SensedActor(bots.FirstOrDefault(b => b.Player == p.Handle).Id is { IsValid: true } id ? id : ActorDirectory.HumanActor(p.Slot, p.Handle), p.Handle))];

    private sealed class Tracer(SenseWorld w) : ITraceQueries
    {
        public TraceAnswer Line(Vector start, Vector end, uint mask, EntityHandle pass)
        {
            w.Lines++;
            var s = new AiVector(start.X, start.Y, start.Z);
            var e = new AiVector(end.X, end.Y, end.Z);
            float best = 1f;
            EntityHandle hit = EntityHandle.None;
            foreach (Box b in w.Boxes)
            {
                if ((b.Contents & mask) == 0 || (b.Entity != EntityHandle.None && b.Entity == pass))
                    continue;
                if (Clip(s, e, b.Min, b.Max) is float t && t < best)
                    (best, hit) = (t, b.Entity);
            }

            foreach (Player p in w.Players)
            {
                if (!p.Alive || p.Handle == pass || (Monster & mask) == 0)
                    continue;
                if (Clip(s, e, p.Origin + new AiVector(-16, -16, 0), p.Origin + new AiVector(16, 16, 72)) is float t && t < best)
                    (best, hit) = (t, p.Handle);
            }

            return new TraceAnswer(best, start + (end - start) * best, hit, false);
        }

        public TraceAnswer Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask, EntityHandle pass) => throw new InvalidOperationException("no hulls");

        /// <summary>The fraction along s..e where it enters the box, or null (slab test).</summary>
        private static float? Clip(AiVector s, AiVector e, AiVector min, AiVector max)
        {
            float t0 = 0f, t1 = 1f;
            ReadOnlySpan<float> ss = [s.X, s.Y, s.Z], ee = [e.X, e.Y, e.Z], lo = [min.X, min.Y, min.Z], hi = [max.X, max.Y, max.Z];
            for (int i = 0; i < 3; i++)
            {
                float d = ee[i] - ss[i];
                if (MathF.Abs(d) < 1e-6f)
                {
                    if (ss[i] < lo[i] || ss[i] > hi[i])
                        return null;
                    continue;
                }

                float a = (lo[i] - ss[i]) / d, b = (hi[i] - ss[i]) / d;
                if (a > b)
                    (a, b) = (b, a);
                t0 = MathF.Max(t0, a);
                t1 = MathF.Min(t1, b);
                if (t0 > t1)
                    return null;
            }

            return t0;
        }
    }
}

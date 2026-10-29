using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// cs_sv_physics_watch: the awake objects near a point, a line each after each
/// of a level's first steps, as the stock build's ab_physwatch prints them, so
/// the two logs line up step for step.
/// </summary>
public class PhysicsWatchTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 1.02f;
    }

    /// <summary>The contacts each object reports; nothing else is asked.</summary>
    private sealed class Touches : IVPhysicsSolverObjects
    {
        public readonly Dictionary<nint, SnapshotContact[]> Of = new();

        public int Contacts(nint physicsObject, Span<SnapshotContact> into)
        {
            if (!Of.TryGetValue(physicsObject, out var contacts))
                return 0;
            contacts.AsSpan(0, Math.Min(contacts.Length, into.Length)).CopyTo(into);
            return contacts.Length;
        }

        public nint GameData(nint physicsObject) => throw new NotSupportedException();
        public uint GameFlags(nint physicsObject) => throw new NotSupportedException();
        public void SetGameFlags(nint physicsObject, uint flags) => throw new NotSupportedException();
        public uint Contents(nint physicsObject) => throw new NotSupportedException();
        public bool IsMoveable(nint physicsObject) => throw new NotSupportedException();
        public bool IsStatic(nint physicsObject) => throw new NotSupportedException();
        public bool IsTrigger(nint physicsObject) => throw new NotSupportedException();
        public bool HasShadowController(nint physicsObject) => throw new NotSupportedException();
        public ushort CallbackFlags(nint physicsObject) => throw new NotSupportedException();
        public float Mass(nint physicsObject) => throw new NotSupportedException();
        public void RecheckCollisionFilter(nint physicsObject) => throw new NotSupportedException();
        public void Sleep(nint physicsObject) => throw new NotSupportedException();
        public void ClearVelocityAndStrain(nint physicsObject) => throw new NotSupportedException();
        public bool TryShadow(nint physicsObject, out ShadowState state) => throw new NotSupportedException();
        public nint ContactObject(nint physicsObject) => throw new NotSupportedException();
    }

    private static readonly Touches NoTouches = new();

    private static (PhysicsWatch Watch, FakeInspect Inspect, RecordingLog Log) Made(string setting, Touches? touches = null)
    {
        var inspect = new FakeInspect();
        inspect.Add(0x10, "crate", z: 10f);
        inspect.Add(0x20, "far", z: 500f);
        inspect.Active.AddRange([0x10, 0x20]);
        var log = new RecordingLog();
        return (new PhysicsWatch(inspect, touches ?? NoTouches, new Clock(), log, () => setting), inspect, log);
    }

    private static string[] Lines(RecordingLog log)
        => log.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.StartsWith("[PW]", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void An_awake_object_in_the_radius_is_logged_and_one_outside_is_not()
    {
        var (watch, _, log) = Made("1 2 0 50 10");

        watch.AfterStep(0x1);

        Assert.Equal(["[PW] 1 1.020 crate 1.00 2.00 10.00 0.0 0.0 0.0 0.0 0.0 0.0 m25.0 r12.5"], Lines(log));
    }

    [Fact]
    public void It_stops_after_its_steps_and_a_new_environment_counts_again()
    {
        var (watch, _, log) = Made("1 2 0 50 2");

        watch.AfterStep(0x1);
        watch.AfterStep(0x1);
        watch.AfterStep(0x1);
        watch.AfterStep(0x2);

        Assert.Equal(["1", "2", "1"], Lines(log).Select(l => l.Split(' ')[1]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1 2 0 50")]
    [InlineData("1 2 0 50 0")]
    [InlineData("a 2 0 50 10")]
    public void A_setting_that_is_not_a_point_radius_and_steps_logs_nothing(string setting)
    {
        var (watch, _, log) = Made(setting);

        watch.AfterStep(0x1);

        Assert.Empty(Lines(log));
    }

    [Fact]
    public void What_an_object_touches_is_named_with_its_force()
    {
        var touches = new Touches();
        touches.Of[0x10] = [new SnapshotContact(0x20, 812.4f, Vector.Zero, Vector.Zero)];
        var (watch, _, log) = Made("1 2 0 50 10", touches);

        watch.AfterStep(0x1);

        Assert.EndsWith(" | far:812", Assert.Single(Lines(log)));
    }
}

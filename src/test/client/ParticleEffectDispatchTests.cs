using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The ParticleEffect and ParticleEffectStop client effects (lane
/// c5-particles): stock's ParticleEffectCallback / ParticleEffectStopCallback
/// (c_particle_system.cpp), absent from this build until now, so every
/// DispatchParticleEffect drew nothing. Read from the stock source: the
/// world arm creates the named system with control point 0 at the origin and
/// 1 at the start, 0 oriented by the angles, then the custom colours on 9 and
/// 10, then the dispatched control point 1 over the start.
/// </summary>
public class ParticleEffectDispatchTests
{
    private sealed class OneShots : IClientParticleOneShots
    {
        public readonly List<(string Name, Vector Sort, ParticleControlPoint[] Points, Vector F, Vector R, Vector U)> Started = [];

        public string? NameFromIndex(int index) => index == 7 ? "blood_impact_red_01" : null;

        public bool Start(string name, Vector sortOrigin, ReadOnlySpan<ParticleControlPoint> points, Vector forward, Vector right, Vector up)
        {
            Started.Add((name, sortOrigin, points.ToArray(), forward, right, up));
            return true;
        }
    }

    private sealed class Entities : IClientStandalones
    {
        public readonly ClientEntityDirectory Directory = new();

        public IReadOnlyList<ClientStandaloneEntity> Live() => [];

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => Directory.ByIndex(entityIndex);

        public IEnumerable<ClientStandaloneEntity> OfClass(string? className) => Directory.OfClass(className);

        public IEnumerable<ClientStandaloneEntity> OfName(string? name) => Directory.OfName(name);
    }

    private static readonly EntityHandle Owner = new(0x0100_0041u);

    private static (ParticleEffectHandler Handler, ParticleEffectStopHandler Stop, OneShots Shots, RecordingEngine Engine) Make()
    {
        var engine = new RecordingEngine();
        engine.Returns("Effects.ParticleSystemStart", true);
        var entities = new Entities();
        var entity = new ClientSsWeapon(engine) { Handle = Owner };
        entities.Directory.Bind(entity.Handle, (int)(Owner.Value & EntityHandle.EntryMask), entity);
        var shots = new OneShots();
        return (new ParticleEffectHandler(shots, engine, entities), new ParticleEffectStopHandler(engine, entities), shots, engine);
    }

    [Fact]
    public void A_world_dispatch_starts_the_named_system_at_its_origin_with_its_start_and_angles()
    {
        var (handler, _, shots, _) = Make();
        var data = new EffectData { HitBox = 7, Origin = new Vector(1, 2, 3), Start = new Vector(4, 5, 6), Angles = new Vector(10, 20, 0) };

        handler.Dispatch(data);

        var s = Assert.Single(shots.Started);
        Assert.Equal("blood_impact_red_01", s.Name);
        Assert.Equal(new Vector(1, 2, 3), s.Sort);
        Assert.Equal([new ParticleControlPoint(0, new Vector(1, 2, 3)), new ParticleControlPoint(1, new Vector(4, 5, 6))], s.Points);
        MoveVec.AngleVectors(new Vector(10, 20, 0), out Vector f, out Vector r, out Vector u);
        Assert.Equal((f, r, u), (s.F, s.R, s.U));
    }

    [Fact]
    public void Custom_colours_go_to_points_9_and_10_and_the_dispatched_point_1_comes_last()
    {
        var (handler, _, shots, _) = Make();
        var data = new EffectData
        {
            HitBox = 7, Origin = new Vector(1, 2, 3), Start = new Vector(4, 5, 6),
            CustomColors = 1, Color1 = new Vector(1, 0, 0), Color2 = new Vector(0, 1, 0),
            ControlPoint1 = 1, ControlPointOffset = new Vector(7, 8, 9),
        };

        handler.Dispatch(data);

        Assert.Equal([0, 1, 9, 10, 1], Assert.Single(shots.Started).Points.Select(p => p.Point));
        Assert.Equal(new Vector(7, 8, 9), shots.Started[0].Points[^1].Position);
    }

    [Fact]
    public void An_index_with_no_name_starts_nothing()
    {
        var (handler, _, shots, _) = Make();
        handler.Dispatch(new EffectData { HitBox = 99 });
        Assert.Empty(shots.Started);
    }

    [Fact]
    public void An_entity_dispatch_starts_the_system_on_the_entity_and_a_reset_stops_it_first()
    {
        var (handler, _, shots, engine) = Make();
        int index = (int)(Owner.Value & EntityHandle.EntryMask);
        handler.Dispatch(new EffectData { HitBox = 7, EntIndex = index, Flags = ParticleEffectHandler.FromEntity | ParticleEffectHandler.ResetParticles,
                                          DamageType = ParticleEffectHandler.AttachAbsOriginFollow });

        Assert.Empty(shots.Started);
        Assert.Equal(["Effects.ParticleSystemStop", "Effects.ParticleSystemStart"],
                     engine.Calls.Where(c => c.Member.StartsWith("Effects.ParticleSystem", StringComparison.Ordinal)).Select(c => c.Member));
        Assert.Equal("blood_impact_red_01", engine.Calls.Last(c => c.Member == "Effects.ParticleSystemStart").Arg(1));

        handler.Dispatch(new EffectData { HitBox = 7, EntIndex = index, Flags = ParticleEffectHandler.FromEntity, DamageType = 3 });
        Assert.Equal(1, handler.UnsupportedAttachments);
    }

    [Fact]
    public void The_stop_effect_stops_the_entitys_system()
    {
        var (_, stop, _, engine) = Make();
        stop.Dispatch(new EffectData { EntIndex = (int)(Owner.Value & EntityHandle.EntryMask) });
        Assert.Equal(Owner, engine.Calls.Single(c => c.Member == "Effects.ParticleSystemStop").Arg(0));
    }
}

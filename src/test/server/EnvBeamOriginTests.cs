using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Where an env_beam is: stock's CEnvBeam is itself the beam, and
/// BeamUpdateVars puts a static point start (IsStaticPointEntity: no parent,
/// and no model or a point class) through SetAbsStartPos, which is
/// SetLocalOrigin. Found beside stock on ss_sandbox (2026-10-26): the effects
/// fence's env_beam stayed where the map drew it, 208 units from stock's.
/// </summary>
public class EnvBeamOriginTests
{
    private static readonly EntityHandle Beam = new(0x0004_0611u);
    private static readonly EntityHandle Owner = new(0x0004_0612u);
    private static readonly EntityHandle Start = new(0x0004_0613u);
    private static readonly EntityHandle End = new(0x0004_0614u);
    private static readonly EntityHandle Carrier = new(0x0004_0615u);

    private static readonly Vector StartAt = new(112f, 800f, 96f);

    private static RecordingEngine Struck(string startModel, string startClass, EntityHandle startParent)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvBeam).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        bool made = false;
        engine.Returns("Precache.PrecacheModel", 7)
              .Returns("Entities.GetSpawnFlags", 1)
              .ReturnsEachCall("Entities.IsValid", () =>
              {
                  var h = engine.Calls.Last().ArgAs<EntityHandle>(0);
                  return h == Start || h == End || h == Carrier || (h == Beam && made);
              })
              .ReturnsEachCall("Entities.Create", () => { made = true; return Beam; })
              .ReturnsEachCall("Entities.FindByName", () => (string)engine.Calls.Last().Args[0]! == "a" ? Start : End)
              .ReturnsEachCall("Entities.GetOrigin", () =>
              {
                  var h = engine.Calls.Last().ArgAs<EntityHandle>(0);
                  return h == Start ? StartAt : h == End ? new Vector(528f, 800f, 96f) : new Vector(320f, 800f, 96f);
              })
              .ReturnsEachCall("Entities.GetParent", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Start ? startParent : EntityHandle.None)
              .ReturnsEachCall("EntityRender.GetModelName", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Start ? startModel : "")
              .ReturnsEachCall("Entities.GetClassName", () => engine.Calls.Last().ArgAs<EntityHandle>(0) == Start ? startClass : "");

        registry.Spawn(Beam, EntityNames.Beam);
        registry.KeyValue(Owner, EntityNames.EnvBeam, "texture", "sprites/laserbeam.vmt");
        registry.KeyValue(Owner, EntityNames.EnvBeam, "LightningStart", "a");
        registry.KeyValue(Owner, EntityNames.EnvBeam, "LightningEnd", "b");
        registry.Spawn(Owner, EntityNames.EnvBeam);
        registry.Get(Owner)!.OnThink();
        return engine;
    }

    private static Vector[] OwnerMoves(RecordingEngine engine)
        => engine.AllOf("Entities.SetOrigin").Where(c => c.ArgAs<EntityHandle>(0) == Owner)
                 .Select(c => c.ArgAs<Vector>(1)).ToArray();

    [Fact]
    public void A_beam_from_an_info_target_stands_at_its_start()
        => Assert.Equal([StartAt], OwnerMoves(Struck("", EntityNames.InfoTarget, EntityHandle.None)));

    [Fact]
    public void A_beam_from_a_modelled_point_class_stands_at_its_start_too()
        => Assert.Equal([StartAt], OwnerMoves(Struck("models/marker.mdl", EntityNames.PathCorner, EntityHandle.None)));

    [Fact]
    public void A_beam_from_a_prop_or_a_parented_point_stays_where_it_was_drawn()
    {
        Assert.Empty(OwnerMoves(Struck("models/crate.mdl", EntityNames.PropPhysics, EntityHandle.None)));
        Assert.Empty(OwnerMoves(Struck("", EntityNames.InfoTarget, Carrier)));
    }
}

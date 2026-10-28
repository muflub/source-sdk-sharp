using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Standalone;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The physcannon's blast effect (CallbackPhyscannonImpact): the carrier sees
/// the explosion at the impact and a beam from the gun to it; everyone else
/// sees the explosion at the gun.
/// </summary>
public class PhysCannonImpactEffectTests
{
    private const int Carrier = 1;
    private static readonly Vector Muzzle = new(0f, 0f, 60f);
    private static readonly Vector Impact = new(100f, 0f, 60f);

    private sealed class Sparks : IClientSparks
    {
        public readonly List<(Vector Position, Vector Direction, int Type)> Gauss = [];

        public void ElectricSpark(Vector position, int magnitude, int trailLength, Vector? direction) { }
        public void MetalSpark(Vector position, Vector direction, Vector surfaceNormal, int scale = 1) { }
        public void EnergySplash(Vector position, Vector normal, bool explosive) { }
        public void GaussExplosion(Vector position, Vector direction, int type) => Gauss.Add((position, direction, type));
    }

    private sealed class Standalones(params ClientStandaloneEntity[] entities) : IClientStandalones
    {
        public IReadOnlyList<ClientStandaloneEntity> Live() => entities;
        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private static (PhysCannonImpactEffect Effect, Sparks Sparks, RecordingEngine Engine) Rig(int localPlayer)
    {
        var engine = TestServices.Mock()
            .Returns("Hud.LocalPlayerIndex", localPlayer)
            .Returns("EntityRender.GetAttachment", true)
            .Returns("EntityRender.GetAttachment.origin", Muzzle)
            .Returns("EntityRender.GetAttachment.angles", Vector.Zero);

        var viewModel = new ClientSsViewModel(engine, engine);
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = Carrier;
        var weapon = new ClientSsWeapon(engine);
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = Carrier;

        var sparks = new Sparks();
        var effect = new PhysCannonImpactEffect(sparks, new RecordingBeams(engine),
                                                new Standalones(viewModel, weapon), engine, engine);
        return (effect, sparks, engine);
    }

    private static EffectData Blast() => new() { Origin = Impact, EntIndex = Carrier };

    [Fact]
    public void TheCarrierSeesTheExplosionAtTheImpactAndABeamFromTheGun()
    {
        var (effect, sparks, engine) = Rig(localPlayer: Carrier);

        effect.Dispatch(Blast());

        Assert.Equal([(Impact, new Vector(-1f, 0f, 0f), 0)], sparks.Gauss);
        var beam = (BeamInfo)Assert.Single(engine.Calls, c => c.Member == "Beams.CreateBeamPoints(BeamInfo)").Args[0]!;
        Assert.Equal((Muzzle, Impact, PhysCannonImpactEffect.BeamSprite, 0.1f, 12f, 4f, 16),
                     (beam.Start, beam.End, beam.ModelName, beam.Life, beam.Width, beam.EndWidth, beam.Segments));
    }

    [Fact]
    public void AnotherPlayerSeesTheExplosionAtTheGunAndNoBeam()
    {
        var (effect, sparks, engine) = Rig(localPlayer: 2);

        effect.Dispatch(Blast());

        Assert.Equal([(Muzzle, new Vector(1f, 0f, 0f), 0)], sparks.Gauss);
        Assert.DoesNotContain(engine.Calls, c => c.Member.StartsWith("Beams.", StringComparison.Ordinal));
    }

    [Fact]
    public void ABlastNamingNobodyDrawsNothing()
    {
        var (effect, sparks, _) = Rig(localPlayer: Carrier);

        effect.Dispatch(new EffectData { Origin = Impact, EntIndex = 0 });

        Assert.Empty(sparks.Gauss);
    }
}

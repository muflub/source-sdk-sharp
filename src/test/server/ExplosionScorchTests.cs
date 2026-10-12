using System.Linq;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The scorch mark an explosion leaves, as stock's UTIL_DecalTrace( &amp;tr,
/// "Scorch" ) leaves it (util_shared.cpp, CWorld::DecalTrace).
///
/// <para>
/// THE BUG. Explosions.Fire precached the decal NAME "Scorch" at detonation,
/// and EnvExplosion at spawn. "Scorch" is a group in decals_subrect.txt, not a
/// texture, and a precache at detonation is late: the live gate's log check
/// caught "UTIL_PrecacheDecal: too late / Late precache of Scorch" on a load
/// where nothing had asked for it in time. Stock precaches the group's images
/// at level init (the decal system) and only ever picks among them.
/// </para>
/// </summary>
public class ExplosionScorchTests
{
    private const int ScorchIndex = 7;

    private static readonly EntityHandle Ground = new(0u);

    private readonly RecordingEngine _engine = TestServices.Mock();
    private readonly RecordingDecals _decals = new();

    public ExplosionScorchTests()
    {
        _engine.Returns("Precache.PrecacheModel", 1)
               // Answered rather than refused, so that a precache of the name
               // is COUNTED below instead of throwing: the count is the fact.
               .Returns("Precache.PrecacheDecal", 2)
               .Returns("WorldQuery.PointContents", Contents.Empty)
               .Ignores("Effects.PaintDecal")
               .Ignores("Effects.ExplosionEffect");
    }

    private void Explode(float groundFraction)
    {
        _engine.Returns("WorldQuery.TraceLine",
                        new TraceResult(groundFraction < 1f, groundFraction, new Vector(0f, 0f, -16f),
                                        new Vector(0f, 0f, 1f), Ground));

        Explosions.Fire(_engine, _engine, _engine, _engine, _engine, new RecordingBlastSweep(), _decals,
                        Vector.Zero, magnitude: 100f, radius: 0f,
                        owner: EntityHandle.None, ignored: EntityHandle.None);
    }

    private int[] PaintedIndices()
        => _engine.Calls.Where(c => c.Member == "Effects.PaintDecal").Select(c => c.ArgAs<int>(0)).ToArray();

    [Fact]
    public void AnExplosionPrecachesNoDecal()
    {
        _decals.Answers("Scorch", ScorchIndex);

        Explode(groundFraction: 0.5f);

        int precaches = _engine.Calls.Count(c => c.Member == "Precache.PrecacheDecal");
        Assert.True(precaches == 0, $"an explosion precaches no decal ({precaches} precache(s))");
    }

    [Fact]
    public void TheScorchIsTheDecalSystemsPickFromItsScorchGroup()
    {
        _decals.Answers("Scorch", ScorchIndex);

        Explode(groundFraction: 0.5f);

        Assert.True(_decals.Asked.SequenceEqual(new[] { "Scorch" }) && PaintedIndices().SequenceEqual(new[] { ScorchIndex }),
                    $"asked [{string.Join(", ", _decals.Asked)}], painted [{string.Join(", ", PaintedIndices())}]");
    }

    [Fact]
    public void NoGroundUnderTheBlastLeavesNoScorch()
    {
        _decals.Answers("Scorch", ScorchIndex);

        Explode(groundFraction: 1f);

        Assert.True(_decals.Asked.Count == 0 && PaintedIndices().Length == 0,
                    $"no ground, no pick and no paint (asked {_decals.Asked.Count}, painted {PaintedIndices().Length})");
    }

    [Fact]
    public void AGroupTheDecalSystemDoesNotKnowPaintsNothing()
    {
        Explode(groundFraction: 0.5f);

        Assert.True(_decals.Asked.Count == 1 && PaintedIndices().Length == 0,
                    $"an unknown group is asked for and not painted (asked {_decals.Asked.Count}, painted {PaintedIndices().Length})");
    }
}

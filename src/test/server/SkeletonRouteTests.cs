using System.Collections.Generic;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The flip (plan physics_bones_native_zero step 1.2): with the server's
/// skeleton route filled, the engine bindings' bone members answer from the
/// managed skeleton and never from native; with it empty they ask native as
/// before (the client, and the server before its composition fills it).
/// </summary>
public class SkeletonRouteTests
{
    private static readonly EntityHandle Standalone = new(0x00010007u);
    private static readonly EntityHandle Brush = new(0x00010008u);

    private sealed class Fake : IStandaloneSkeletons
    {
        public List<string> Calls { get; } = [];
        public SkeletonScaleChange Scale { get; set; } = SkeletonScaleChange.Changed;

        public bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out QAngle angles, out bool standalone)
        {
            Calls.Add("attachment");
            standalone = entity == Standalone;
            origin = standalone ? new Vector(1f, 2f, 3f) : default;
            angles = standalone ? new QAngle(4f, 5f, 6f) : default;
            return standalone;
        }

        public int LookupAttachment(EntityHandle entity, string name) { Calls.Add("lookup"); return 7; }
        public int CachedBoneCount(EntityHandle entity) { Calls.Add("count"); return 9; }
        public StandaloneBones? BoneMatrices(EntityHandle entity) { Calls.Add("matrices"); return null; }
        public bool IsPoseable(EntityHandle entity) { Calls.Add("poseable"); return true; }
        public string StudioName(EntityHandle entity) { Calls.Add("name"); return "x.mdl"; }

        public bool SetSequence(EntityHandle entity, int sequence, float playbackRate, bool restart)
        {
            Calls.Add($"seq {sequence} {playbackRate} {restart}");
            return true;
        }

        public SkeletonScaleChange SetModelScale(EntityHandle entity, float scale)
        {
            Calls.Add("scale");
            return Scale;
        }
    }

    private static (IEngineEntityRender Render, IEngineEntities Entities, Fake Fake, StandaloneSkeletonRoute Route) Rig(bool filled)
    {
        var fake = new Fake();
        var route = new StandaloneSkeletonRoute { Skeletons = filled ? fake : null };
        var side = EngineSide.Create(Side.Server);
        return (SideBindings.Of<IEngineEntityRender>(side, new StandaloneMirror(), route), SideBindings.Of<IEngineEntities>(side, route), fake, route);
    }

    [Fact]
    public void AFilledRouteAnswersEveryBoneMemberFromTheSkeleton()
    {
        var (render, _, fake, _) = Rig(filled: true);

        Assert.True(render.GetAttachment(Standalone, 1, out Vector origin, out Vector angles));
        Assert.Equal((1f, 5f), (origin.X, angles.Y));
        Assert.Equal(7, render.LookupAttachment(Standalone, "muzzle"));
        render.BoneCount(Standalone, out int cached);
        Assert.Equal(9, cached);
        Assert.True(render.IsPoseable(Standalone));
        Assert.Equal("x.mdl", render.StudioName(Standalone));
        Assert.Null(render.BoneMatrices(Standalone));

        Assert.Equal(new[] { "attachment", "lookup", "count", "poseable", "name", "matrices" }, fake.Calls);
    }

    [Fact]
    public void AnEmptyRouteNeverAsksTheSkeleton()
    {
        var (render, entities, fake, _) = Rig(filled: false);

        render.GetAttachment(Standalone, 1, out _, out _);
        render.LookupAttachment(Standalone, "muzzle");
        render.SetSequence(Standalone, 3);
        render.SetModelScale(Standalone, 2f);
        entities.GetAttachmentFrame(Standalone, 1, out _, out _);

        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void SetSequencePlaysFromTheStartAtRateOneAndRefusesANegative()
    {
        var (render, _, fake, _) = Rig(filled: true);

        render.SetSequence(Standalone, 3);
        render.SetSequence(Standalone, -1);
        render.PlaySequence(Standalone, 2, 0.5f, restart: false);

        Assert.Equal(new[] { "seq 3 1 True", "seq 2 0.5 False" }, fake.Calls);
    }

    [Fact]
    public void TheAttachmentFrameOfAStandaloneIsTheSkeletonsAndOfAnythingElseIsNatives()
    {
        var (_, entities, fake, _) = Rig(filled: true);

        Assert.True(entities.GetAttachmentFrame(Standalone, 1, out Vector origin, out _));
        Assert.Equal(1f, origin.X);

        // Not a standalone: the skeleton says so, and native (unattached here)
        // answers "not found".
        Assert.False(entities.GetAttachmentFrame(Brush, 1, out _, out _));
        Assert.Equal(2, fake.Calls.Count);
    }

    [Fact]
    public void ALookupOnEitherHierarchyIsTheSkeletons()
    {
        var (_, entities, fake, _) = Rig(filled: true);

        Assert.Equal(7, entities.LookupAttachmentAny(Brush, "x"));
        Assert.Equal(new[] { "lookup" }, fake.Calls);
    }

    [Fact]
    public void AnEmptyRouteLooksUpNoAttachment()
    {
        var (_, entities, fake, _) = Rig(filled: false);

        Assert.Equal(0, entities.LookupAttachmentAny(Brush, "x"));
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public void ARefusedScaleIsNotStoredNatively()
    {
        // The store is native and unattached here, so what is pinned is that
        // the skeleton was asked first, once, whatever it answered.
        var (render, _, fake, _) = Rig(filled: true);
        fake.Scale = SkeletonScaleChange.Refused;

        render.SetModelScale(Standalone, 0f);

        Assert.Equal(new[] { "scale" }, fake.Calls);
    }
}

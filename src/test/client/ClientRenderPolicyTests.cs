using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared.Bones;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE CLIENT'S RENDER POLICY — plan_entity.md step 6b, §3.9.
///
/// <para>
/// Sixteen of these facts are <c>shadow_cast_policy_test.cpp</c>'s sixteen
/// gtests, moved with their subject: what kind of shadow an entity casts is a
/// decision — stock spells the same one <c>EF_NOSHADOW</c> and a
/// <c>disableshadows</c> keyvalue — so the rule is C#'s and the header that held
/// it is deleted. The reasoning each one carried came with it.
/// </para>
///
/// <para>
/// The rest are the half that could not exist while the rule was in a
/// header-only C++ file with no block in scope: that the decisions are WRITTEN
/// INTO THE BLOCK, at the offsets the shell loads them from, and that they are
/// written when the inputs move and not otherwise.
/// </para>
/// </summary>
public class ClientRenderPolicyTests
{
    //-------------------------------------------------------------------------
    // A drawn studio prop, as the facts below vary it.
    //-------------------------------------------------------------------------
    private static ClientRenderFacts Prop() => new()
    {
        Dormant             = false,
        ViewModel           = false,
        CustomDraw          = false,
        Translucent         = false,
        Fade                = FadeStyle.None,
        ShadowChoice        = ShadowCastChoice.Auto,
        ModelType           = ClientRenderFacts.Studio,
        SequenceCount       = 4,
        PoseParameterCount  = 0,
        BoneControllerCount = 0,
        IkChainCount        = 0,
    };

    //=========================================================================
    // SHOULD DRAW.
    //=========================================================================

    [Fact]
    public void ADormantEntityDrawsNothing()
    {
        var facts = Prop();
        facts.Dormant = true;

        Assert.False(ClientRenderPolicy.Drawn(facts));
    }

    [Fact]
    public void AnEntityWithNoModelDrawsNothing()
    {
        // Having a model IS the question. An entity with none is not hidden, it
        // is a thing with nothing to draw — a trigger, a marker, a rule.
        var facts = Prop();
        facts.ModelType = ClientRenderFacts.NoModel;

        Assert.False(ClientRenderPolicy.Drawn(facts));
    }

    [Fact]
    public void AnEntityWithNoDrawSetDrawsNothing()
    {
        // THE STEP 8e-render RULE. EF_NODRAW is the server saying "do not draw
        // this", and it is a different statement from "this has no model": a
        // hidden func_dust keeps its brush so the client can ask the volume
        // whether a mote is inside it.
        var facts = Prop();
        facts.NoDraw = true;

        Assert.False(ClientRenderPolicy.Drawn(facts));
    }

    [Fact]
    public void AnEntityWithNoDrawStillHasItsModel()
    {
        // The half of the fix a "does it look right" check cannot see. The
        // entity is NOT drawn and its model type is still a real model — which
        // is the state that did not exist before, because "invisible" was
        // expressed by sending no model at all.
        var facts = Prop();
        facts.NoDraw = true;

        Assert.False(ClientRenderPolicy.Drawn(facts));
        Assert.NotEqual(ClientRenderFacts.NoModel, facts.ModelType);
    }

    [Fact]
    public void NoDrawBeatsTheCustomDrawArm()
    {
        // A beam draws itself and needs no model, so the custom-draw arm says
        // yes for it whatever else is true. EF_NODRAW is the server having
        // decided, and it is tested first — stock's C_BaseEntity::ShouldDraw
        // refuses on it ahead of everything but dormancy.
        var facts = Prop();
        facts.ModelType = ClientRenderFacts.NoModel;
        facts.CustomDraw = true;
        facts.NoDraw = true;

        Assert.False(ClientRenderPolicy.Drawn(facts));
    }

    [Fact]
    public void NoDrawCastsNoShadow()
    {
        // A shadow projected from something the renderer is not drawing is a
        // dark patch on the floor under nothing — 7d's own finding, reached
        // here through Drawn rather than restated as a second rule.
        var facts = Prop();
        facts.NoDraw = true;

        Assert.Equal(ShadowCast.None, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void AnEntityThatDrawsItselfNeedsNoModel()
    {
        // A beam has none by design — it IS a drawing — so asking whether it has
        // one answers no for something that must draw every frame.
        var facts = Prop();
        facts.ModelType = ClientRenderFacts.NoModel;
        facts.CustomDraw = true;

        Assert.True(ClientRenderPolicy.Drawn(facts));
    }

    [Fact]
    public void AnEntityThatDrawsItselfStillDrawsNothingWhileDormant()
    {
        var facts = Prop();
        facts.ModelType = ClientRenderFacts.NoModel;
        facts.CustomDraw = true;
        facts.Dormant = true;

        Assert.False(ClientRenderPolicy.Drawn(facts));
    }

    [Fact]
    public void TheViewModelIsJudgedByItsModelAndNotByTheCustomDrawBit()
    {
        // The native rule tested the viewmodel arm FIRST, so a viewmodel that
        // also carried the custom-draw bit was asked for a model anyway. Kept,
        // because SetViewModelPass clears the bit and the ordering is what makes
        // that clearing matter rather than being decorative.
        var facts = Prop();
        facts.ViewModel = true;
        facts.CustomDraw = true;
        facts.ModelType = ClientRenderFacts.NoModel;

        Assert.False(ClientRenderPolicy.Drawn(facts));
    }

    [Fact]
    public void OnlyTheViewModelIsHiddenWhileTheCameraSeesItsOwner()
    {
        // WHICH entities carry the rule is the decision and lives here; WHETHER
        // the camera can see the owner right now is an engine fact the shell ANDs
        // in at the draw. See EngineBlockClientTail.HideWhenOwnerSeen.
        Assert.False(ClientRenderPolicy.HideWhenOwnerSeen(Prop()));

        var viewModel = Prop();
        viewModel.ViewModel = true;

        Assert.True(ClientRenderPolicy.HideWhenOwnerSeen(viewModel));
    }

    //=========================================================================
    // TRANSPARENCY AND THE RENDER GROUP.
    //=========================================================================

    [Fact]
    public void APlainPropIsOpaqueAndInTheOpaqueEntityGroup()
    {
        Assert.False(ClientRenderPolicy.Transparent(Prop()));
        Assert.Equal(ClientRenderGroup.OpaqueEntity, ClientRenderPolicy.Group(Prop()));
    }

    [Fact]
    public void AFadingEntityIsTransparent()
    {
        var facts = Prop();
        facts.Fade = FadeStyle.Lod;

        Assert.True(ClientRenderPolicy.Transparent(facts));
    }

    [Fact]
    public void AnEntityThatAskedToBlendIsTransparent()
    {
        // The sprite family: an additive sprite must be in the translucent list
        // or DrawTranslucentRenderable never sees it at all.
        var facts = Prop();
        facts.Translucent = true;

        Assert.True(ClientRenderPolicy.Transparent(facts));
    }

    [Fact]
    public void AFadingEntityIsInTheTranslucentGroupBeforeItStartsFading()
    {
        // The group is chosen when the renderable is INSERTED. A brush drawn
        // opaque at half alpha writes depth for pixels it did not paint, which
        // reads as the world vanishing behind it.
        var facts = Prop();
        facts.Fade = FadeStyle.PortalWindow;

        Assert.Equal(ClientRenderGroup.TranslucentEntity, ClientRenderPolicy.Group(facts));
    }

    [Fact]
    public void TheViewModelIsInTheViewModelGroupEvenWhenItWouldOtherwiseBlend()
    {
        // The viewmodel pass first: the leaf system keeps these in a list of
        // their own and never in the leaves, which is what makes it right that
        // the eye pin skips RenderableChanged.
        var facts = Prop();
        facts.ViewModel = true;
        facts.Translucent = true;

        Assert.Equal(ClientRenderGroup.ViewModelOpaque, ClientRenderPolicy.Group(facts));
    }

    [Fact]
    public void ABrushEntityIsNotSortedIntoTheBrushGroup()
    {
        // MEASURED, NOT CHOSEN, and the opposite of C_BaseEntity::GetRenderGroup.
        // A renderable placed in the brush group was registered with the right
        // flags, the right bounds and ShouldDraw answering true, and was then
        // never asked to draw: the renderer does extra work for that group that
        // assumes a renderable the SDK made.
        var facts = Prop();
        facts.ModelType = 1;  // mod_brush

        Assert.NotEqual(ClientRenderGroup.OpaqueBrush, ClientRenderPolicy.Group(facts));
        Assert.Equal(ClientRenderGroup.OpaqueEntity, ClientRenderPolicy.Group(facts));
    }

    //=========================================================================
    // THE SHADOW RULE — shadow_cast_policy_test.cpp's sixteen, moved.
    //=========================================================================

    [Fact]
    public void DrawnStudioPropCasts()
    {
        Assert.Equal(ShadowCast.RenderToTexture, ClientRenderPolicy.Shadow(Prop()));
    }

    [Fact]
    public void NotDrawnCastsNothing()
    {
        // A shadow under an entity the viewer cannot see is worse than no
        // shadow, because there is no object to explain it.
        var facts = Prop();
        facts.Dormant = true;

        Assert.Equal(ShadowCast.None, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void OverrideCannotMakeAnUndrawnEntityCast()
    {
        // A dormant entity holds its last known origin, so an override that
        // outranked the draw test would leave a shadow standing where the entity
        // was last seen.
        var facts = Prop();
        facts.Dormant = true;
        facts.ShadowChoice = ShadowCastChoice.Simple;

        Assert.Equal(ShadowCast.None, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void ViewModelCastsNothing()
    {
        // Stock's viewmodel casts nothing for the same reason: it is drawn at
        // the eye with a viewmodel FOV, so its shadow would be a pair of arms on
        // the floor wherever the player looks.
        var facts = Prop();
        facts.ViewModel = true;

        Assert.Equal(ShadowCast.None, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void OverrideCannotMakeTheViewModelCast()
    {
        var facts = Prop();
        facts.ViewModel = true;
        facts.ShadowChoice = ShadowCastChoice.Simple;

        Assert.Equal(ShadowCast.None, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void BrushModelCastsNothingUnderAuto()
    {
        // A brush model is lit by the world and casts nothing in stock either.
        var facts = Prop();
        facts.ModelType = 1;  // mod_brush

        Assert.Equal(ShadowCast.None, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void SimpleOverridesTheNoStudioRefusal()
    {
        // An entity that draws ITSELF has no studio header to be judged by and
        // would otherwise never cast at all.
        var facts = Prop();
        facts.ModelType = ClientRenderFacts.NoModel;
        facts.CustomDraw = true;
        facts.ShadowChoice = ShadowCastChoice.Simple;

        Assert.Equal(ShadowCast.Simple, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void NeverCastsNothingFromAModelThatWould()
    {
        var facts = Prop();
        facts.ShadowChoice = ShadowCastChoice.Never;

        Assert.Equal(ShadowCast.None, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void SequencesUnavailableCastsNothing()
    {
        // Stock returns NONE and recomputes on the next update, and so does
        // this — the policy runs again on every change.
        var facts = Prop();
        facts.SequenceCount = ClientRenderFacts.SequencesUnavailable;

        Assert.Equal(ShadowCast.None, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void ZeroSequencesIsStillARenderToTextureShadow()
    {
        // Nothing can move, so the shadow is baked once.
        var facts = Prop();
        facts.SequenceCount = 0;

        Assert.Equal(ShadowCast.RenderToTexture, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void PoseParametersMakeItDynamic()
    {
        var facts = Prop();
        facts.PoseParameterCount = 2;

        Assert.Equal(ShadowCast.RenderToTextureDynamic, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void BoneControllersMakeItDynamic()
    {
        var facts = Prop();
        facts.BoneControllerCount = 1;

        Assert.Equal(ShadowCast.RenderToTextureDynamic, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void IkChainsMakeItDynamic()
    {
        var facts = Prop();
        facts.IkChainCount = 3;

        Assert.Equal(ShadowCast.RenderToTextureDynamic, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void ZeroSequencesBeatsTheDynamicTest()
    {
        // A model with pose parameters but no sequences cannot animate: there is
        // nothing to drive them.
        var facts = Prop();
        facts.SequenceCount = 0;
        facts.PoseParameterCount = 4;
        facts.IkChainCount = 2;

        Assert.Equal(ShadowCast.RenderToTexture, ClientRenderPolicy.Shadow(facts));
    }

    [Fact]
    public void OnlyRenderToTextureTypesWantATextureSlot()
    {
        Assert.Equal(ClientShadowFlag.None, ClientRenderPolicy.ShadowFlags(ShadowCast.None));
        Assert.Equal(ClientShadowFlag.Shadow, ClientRenderPolicy.ShadowFlags(ShadowCast.Simple));

        Assert.True(ClientRenderPolicy.ShadowFlags(ShadowCast.RenderToTexture)
                    .HasFlag(ClientShadowFlag.UseRenderToTexture));
        Assert.True(ClientRenderPolicy.ShadowFlags(ShadowCast.RenderToTextureDynamic)
                    .HasFlag(ClientShadowFlag.UseRenderToTexture));
    }

    [Fact]
    public void OnlyTheDynamicTypeIsAnAnimatingSource()
    {
        Assert.False(ClientRenderPolicy.ShadowFlags(ShadowCast.Simple)
                     .HasFlag(ClientShadowFlag.AnimatingSource));
        Assert.False(ClientRenderPolicy.ShadowFlags(ShadowCast.RenderToTexture)
                     .HasFlag(ClientShadowFlag.AnimatingSource));
        Assert.True(ClientRenderPolicy.ShadowFlags(ShadowCast.RenderToTextureDynamic)
                    .HasFlag(ClientShadowFlag.AnimatingSource));
    }

    [Fact]
    public void EveryShadowThatExistsCarriesTheShadowBit()
    {
        // Stock's CreateShadow opens with SHADOW_FLAGS_SHADOW and adds to it.
        // Without that bit the manager has a projected texture that is not a
        // shadow, which is a flashlight.
        foreach (ShadowCast kind in new[]
                 { ShadowCast.Simple, ShadowCast.RenderToTexture, ShadowCast.RenderToTextureDynamic })
        {
            Assert.True(ClientRenderPolicy.ShadowFlags(kind).HasFlag(ClientShadowFlag.Shadow),
                $"{kind} was given flags with no SHADOW_FLAGS_SHADOW in them");
        }
    }

    [Fact]
    public void ValuesMatchShadowTypeT()
    {
        // The native side static_asserts these against ShadowType_t at the
        // shell's one call site, which is the only place both are in scope. This
        // is the managed half of that pair: the numbers cross the boundary as
        // a block field, so they are fixed forever.
        Assert.Equal(0, (int)ShadowCast.None);
        Assert.Equal(1, (int)ShadowCast.Simple);
        Assert.Equal(2, (int)ShadowCast.RenderToTexture);
        Assert.Equal(3, (int)ShadowCast.RenderToTextureDynamic);
    }

    [Fact]
    public void RenderGroupValuesMatchTheEngineEnum()
    {
        // RenderGroup_t with RENDER_GROUP_CFG_NUM_OPAQUE_ENT_BUCKETS == 4.
        // Static-asserted natively in AddToLeafSystem; this is the managed half.
        Assert.Equal(7, (int)ClientRenderGroup.OpaqueEntity);
        Assert.Equal(8, (int)ClientRenderGroup.TranslucentEntity);
        Assert.Equal(10, (int)ClientRenderGroup.ViewModelOpaque);
        Assert.Equal(12, (int)ClientRenderGroup.OpaqueBrush);
    }

    [Fact]
    public void ShadowFlagValuesMatchTheEngineBits()
    {
        Assert.Equal(2, (int)ClientShadowFlag.Shadow);
        Assert.Equal(4, (int)ClientShadowFlag.UseRenderToTexture);
        Assert.Equal(8, (int)ClientShadowFlag.AnimatingSource);
    }

    //=========================================================================
    // THE PASS: the decisions land IN THE BLOCK.
    //=========================================================================

    /// <summary>
    /// One entity's engine block, and a list of one. The bytes are a real
    /// <see cref="EngineBlock.SizeBytes"/>-long buffer, so every offset the pass
    /// writes at is the offset the shell loads from.
    /// </summary>
    private sealed class FakeBlocks : IRenderPolicyBlocks
    {
        private readonly byte[][] _blocks;

        public FakeBlocks(int count)
        {
            _blocks = new byte[count][];
            for (int i = 0; i < count; i++)
                _blocks[i] = new byte[EngineBlock.SizeBytes];
        }

        /// <summary>A SHORT block — what a networked-payload view of one looks like.</summary>
        public void Truncate(int index, int bytes) => _blocks[index] = new byte[bytes];

        public int Begin() => _blocks.Length;

        public Span<byte> Block(int index) => _blocks[index];

        public byte[] Raw(int index) => _blocks[index];
    }

    private static int Tail(string field)
        => (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Client))
         + (int)Marshal.OffsetOf<EngineBlockClientTail>(field);

    private static int HeadHandle
        => (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Head))
         + (int)Marshal.OffsetOf<EngineBlockHead>(nameof(EngineBlockHead.Handle));

    private static int HeadEffects
        => (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Head))
         + (int)Marshal.OffsetOf<EngineBlockHead>(nameof(EngineBlockHead.Effects));

    private static void Write(byte[] block, int offset, int value)
        => BitConverter.TryWriteBytes(block.AsSpan(offset, sizeof(int)), value);

    private static int Read(byte[] block, int offset)
        => BitConverter.ToInt32(block, offset);

    private static uint ReadU(byte[] block, int offset)
        => BitConverter.ToUInt32(block, offset);

    //-------------------------------------------------------------------------
    // The side's studio headers, which the four model counts are read from
    // (studio.cpp's eviction): one model per index, built by StudioHdrBuilder.
    //-------------------------------------------------------------------------

    private const int PropModel = 5;      // four sequences, nothing posable
    private const int PosedModel = 6;     // four sequences, two pose parameters
    private const int IkModel = 7;        // four sequences, one IK chain

    private sealed class FakeModels : IBoneModels
    {
        public List<int> Asked { get; } = new();

        public bool HasStudioModel(int modelIndex) => OpenStudioHdr(modelIndex) is not null;

        public StudioHdr? OpenStudioHdr(int modelIndex)
        {
            Asked.Add(modelIndex);
            return modelIndex switch
            {
                PropModel => Hdr(0, 0, 0),
                PosedModel => Hdr(2, 0, 0),
                IkModel => Hdr(0, 0, 1),
                _ => null,
            };
        }

        private static StudioHdr Hdr(int poseParams, int controllers, int ikChains)
        {
            var b = new StudioHdrBuilder { ModelName = "prop" };
            b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root" });
            b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "tip", Parent = 0 });
            b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a" });
            for (int i = 0; i < 4; i++)
                b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = $"s{i}" });
            for (int i = 0; i < poseParams; i++)
                b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = $"p{i}" });
            for (int i = 0; i < controllers; i++)
                b.Controllers.Add(new StudioHdrBuilder.ControllerDef { Bone = 0 });
            for (int i = 0; i < ikChains; i++)
                b.IkChains.Add(new StudioHdrBuilder.IkChainDef { Name = $"ik{i}", Links = { (0, System.Numerics.Vector3.Zero), (1, System.Numerics.Vector3.Zero) } });

            return StudioHdr.TryCreate(b.Build(), null, out StudioHdr? h) && h is not null
                ? h
                : throw new InvalidOperationException("fixture image rejected by TryCreate");
        }
    }

    private static ClientRenderPolicy Policy(IBoneModels? models = null)
    {
        IBoneModels fake = models ?? new FakeModels();
        return new ClientRenderPolicy(() => fake);
    }

    private static int HeadModelIndex
        => (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Head))
         + (int)Marshal.OffsetOf<EngineBlockHead>(nameof(EngineBlockHead.ModelIndex));

    /// <summary>A block holding a drawn studio prop, on the handle given.</summary>
    private static FakeBlocks OneProp(uint handle = 0x00010007u)
    {
        var blocks = new FakeBlocks(1);
        byte[] block = blocks.Raw(0);

        Write(block, HeadHandle, unchecked((int)handle));
        Write(block, HeadModelIndex, PropModel);
        Write(block, Tail(nameof(EngineBlockClientTail.ModelType)), ClientRenderFacts.Studio);

        return blocks;
    }

    [Fact]
    public void TheRenderGroupIsWrittenIntoTheBlock()
    {
        var blocks = OneProp();
        Policy().Pass(blocks);

        Assert.Equal((int)ClientRenderGroup.OpaqueEntity,
                     Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.RenderGroup))));
    }

    [Fact]
    public void TheFadeChoiceIsWrittenIntoTheBlockAsTheGroupAndTheTransparency()
    {
        // THE FADE'S TWO OUTPUTS, which is what "fade choices asserted as block
        // writes" means: the style itself is an INPUT the shell stored, and what
        // the policy decides from it is the group and the transparency.
        var blocks = OneProp();
        Write(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.FadeStyle)), (int)FadeStyle.Lod);

        Policy().Pass(blocks);

        Assert.Equal((int)ClientRenderGroup.TranslucentEntity,
                     Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.RenderGroup))));
        Assert.Equal(1, Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.Transparent))));
    }

    [Fact]
    public void TheShadowChoiceIsWrittenIntoTheBlockAsATypeAndItsFlags()
    {
        var blocks = OneProp();
        Write(blocks.Raw(0), HeadModelIndex, PosedModel);

        Policy().Pass(blocks);

        Assert.Equal((int)ShadowCast.RenderToTextureDynamic,
                     Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.ShadowType))));
        Assert.Equal((int)(ClientShadowFlag.Shadow | ClientShadowFlag.UseRenderToTexture
                           | ClientShadowFlag.AnimatingSource),
                     Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.ShadowFlags))));
    }

    [Fact]
    public void AManagedNeverIsWrittenIntoTheBlockAsNoShadowAndNoFlags()
    {
        var blocks = OneProp();
        Write(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.ShadowChoice)),
              (int)ShadowCastChoice.Never);

        Policy().Pass(blocks);

        Assert.Equal((int)ShadowCast.None,
                     Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.ShadowType))));
        Assert.Equal(0, Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.ShadowFlags))));
    }

    [Fact]
    public void TheDrawAnswerAndItsThirdPersonTermAreWrittenIntoTheBlock()
    {
        var blocks = OneProp();
        Write(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.ViewModel)), 1);

        Policy().Pass(blocks);

        Assert.Equal(1, Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.Drawn))));
        Assert.Equal(1, Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.HideWhenOwnerSeen))));
    }

    [Fact]
    public void ADormantEntityIsWrittenIntoTheBlockAsNotDrawn()
    {
        var blocks = OneProp();
        Write(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.Dormant)), 1);

        Policy().Pass(blocks);

        Assert.Equal(0, Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.Drawn))));
    }

    [Fact]
    public void TheNoDrawBitIsReadFromTheHeadsEffectsWordIntoTheBlocksDrawnAnswer()
    {
        // END TO END OVER REAL BYTES, and the offset matters: EF_NODRAW arrives
        // in EngineBlockHead.Effects, which the engine's own decoder writes,
        // NOT in the client tail the rest of the render policy lives in. A
        // ReadFacts that looked in the tail would find a neighbouring field and
        // this would answer 1.
        var blocks = OneProp();
        Write(blocks.Raw(0), HeadEffects, (int)EntityEffect.NoDraw);

        Policy().Pass(blocks);

        Assert.Equal(0, Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.Drawn))));
    }

    [Fact]
    public void AnEffectsWordWithoutNoDrawLeavesTheEntityDrawn()
    {
        // The control for the fact above, and the reason it is here: a rule
        // that answered "not drawn" for every entity would pass that one.
        // EF_DIMLIGHT is a real bit in the same word and means nothing to the
        // draw.
        var blocks = OneProp();
        Write(blocks.Raw(0), HeadEffects, (int)EntityEffect.DimLight);

        Policy().Pass(blocks);

        Assert.Equal(1, Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.Drawn))));
    }

    [Fact]
    public void TheOutputSerialMovesWhenSomethingWasDecided()
    {
        // The shell applies an entity's policy when this differs from what it
        // last applied. A serial that never moved would be a leaf system and a
        // shadow manager holding the first frame's answers forever.
        var blocks = OneProp();
        Policy().Pass(blocks);

        Assert.NotEqual(0u, ReadU(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.PolicyOutputs))));
    }

    [Fact]
    public void ThePolicyIsNotRecomputedWhileTheInputsStandStill()
    {
        // §3.9: written when its inputs change, NOT per frame. The pass still
        // walks every block — that is how a list is walked — but an entity whose
        // inputs have not moved costs one load and one compare.
        var blocks = OneProp();
        var policy = Policy();

        Assert.Equal(1, policy.Pass(blocks));
        Assert.Equal(0, policy.Pass(blocks));
        Assert.Equal(0, policy.Pass(blocks));

        Assert.Equal(3, policy.Passes);
        Assert.Equal(3, policy.Seen);
        Assert.Equal(1, policy.Decisions);
    }

    [Fact]
    public void TheOutputSerialStandsStillWhileTheInputsDo()
    {
        var blocks = OneProp();
        var policy = Policy();

        policy.Pass(blocks);
        uint after = ReadU(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.PolicyOutputs)));

        policy.Pass(blocks);

        Assert.Equal(after, ReadU(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.PolicyOutputs))));
    }

    [Fact]
    public void BumpingTheInputSerialIsWhatMakesThePolicyRunAgain()
    {
        var blocks = OneProp();
        var policy = Policy();

        policy.Pass(blocks);

        // What the shell's BumpPolicyInputs does, and the ONLY thing that says
        // an entity needs deciding for again.
        Write(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.Translucent)), 1);
        Write(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.PolicyInputs)), 1);

        Assert.Equal(1, policy.Pass(blocks));
        Assert.Equal((int)ClientRenderGroup.TranslucentEntity,
                     Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.RenderGroup))));
    }

    [Fact]
    public void AChangeWithNoSerialBumpIsNotNoticed()
    {
        // THE OTHER HALF OF THE FACT ABOVE, and it is what makes the serial a
        // real mechanism rather than a decoration: a build that changed an input
        // and forgot to bump would be a renderable stuck on last frame's policy,
        // and this is what says so.
        var blocks = OneProp();
        var policy = Policy();

        policy.Pass(blocks);

        Write(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.Translucent)), 1);

        Assert.Equal(0, policy.Pass(blocks));
        Assert.Equal((int)ClientRenderGroup.OpaqueEntity,
                     Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.RenderGroup))));
    }

    [Fact]
    public void ARecycledSlotIsDecidedForAgainEvenWithTheSameInputSerial()
    {
        // A DEFAULT HANDLE IS ENTRY ZERO, A REAL SLOT. Handles are recycled, so
        // the entry index alone cannot say "already decided for this entity" —
        // the serial inside the handle is the other half. Without it, the entity
        // that inherits a slot inherits the previous entity's render policy and
        // keeps it until one of its own inputs moves.
        var blocks = OneProp(handle: 0x00010007u);
        var policy = Policy();

        policy.Pass(blocks);
        Assert.Equal(0, policy.Pass(blocks));

        // Same entry, next serial. Nothing else about the block changes.
        Write(blocks.Raw(0), HeadHandle, unchecked((int)0x00020007u));

        Assert.Equal(1, policy.Pass(blocks));
    }

    [Fact]
    public void AFullUpdatesFreshBlockOnTheSameHandleIsDecidedForAgain()
    {
        // A FULL UPDATE RE-CREATES EVERY ENTITY ON THE HANDLE IT ALREADY HAD
        // (cl_fullupdate, or a client whose delta base was lost to a gap in the
        // server's traffic). The new shell's block is zeroed, its input serial
        // starts again from the same value, and neither half of the
        // (handle, inputs) test moves. Skipped, the block kept RenderGroup 0,
        // RENDER_GROUP_OPAQUE_STATIC_HUGE, so the shell was drawn as a static
        // prop: the engine's DrawStaticProps read a C_StandaloneEntity as a
        // CStaticProp and segfaulted (steam-auth-game's k3s gateway restart,
        // and cl_fullupdate on dm_lockdown). An output serial of zero is a
        // block nothing has decided for: the pass writes from 1.
        const uint handle = 0x00010007u;
        var policy = Policy();
        var first = OneProp(handle);
        Write(first.Raw(0), Tail(nameof(EngineBlockClientTail.PolicyInputs)), 1);
        policy.Pass(first);

        var recreated = OneProp(handle);
        Write(recreated.Raw(0), Tail(nameof(EngineBlockClientTail.PolicyInputs)), 1);

        Assert.Equal(1, policy.Pass(recreated));
        Assert.Equal((int)ClientRenderGroup.OpaqueEntity,
                     Read(recreated.Raw(0), Tail(nameof(EngineBlockClientTail.RenderGroup))));
        Assert.NotEqual(0u, ReadU(recreated.Raw(0), Tail(nameof(EngineBlockClientTail.PolicyOutputs))));
        Assert.Equal(0, policy.Pass(recreated));    // and decided once, not every frame
    }

    [Fact]
    public void EntryZeroIsDecidedForOnItsFirstSight()
    {
        // THE SENTINEL THAT IS NOT A DEFAULT. Entry 0 with serial 0 is handle 0,
        // which is what a zero-initialised "last seen" table would already hold —
        // so the world, or whatever holds entry 0, would be skipped on its first
        // pass and drawn on a zeroed block. Closed by construction: the table
        // starts at INVALID_EHANDLE_INDEX.
        var blocks = OneProp(handle: 0u);

        Assert.Equal(1, Policy().Pass(blocks));
        Assert.Equal((int)ClientRenderGroup.OpaqueEntity,
                     Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.RenderGroup))));
    }

    [Fact]
    public void LevelInitForgetsEveryEntryAndTheNextPassDecidesAgain()
    {
        // Handles start again from the beginning on every map, so an entity that
        // kept its mark would carry the render policy of whatever held its slot
        // on the last one.
        var blocks = OneProp();
        var policy = Policy();

        policy.Pass(blocks);
        Assert.Equal(0, policy.Pass(blocks));

        policy.Clear();

        Assert.Equal(1, policy.Pass(blocks));
    }

    [Fact]
    public void AShortBlockIsSkippedAndNotDecidedFor()
    {
        // THE FAILURE THIS STEP IS MOST LIKELY TO HIT SILENTLY. The networked
        // payload and the engine block overlap and have different lengths — 32
        // bytes for a class that declared no state — and every field the policy
        // reads or writes lives past the end of every tier. A short span is a
        // legal span, so a pass handed one would refuse every access without
        // reporting anything. It is skipped and, because LastSeen does not count
        // it, cs_cl_policy shows the list shrinking rather than showing health.
        var blocks = new FakeBlocks(1);
        blocks.Truncate(0, 32);

        var policy = Policy();

        Assert.Equal(0, policy.Pass(blocks));
        Assert.Equal(0, policy.LastSeen);
    }

    [Fact]
    public void AnEmptyListIsAPassThatDecidesNothing()
    {
        var policy = Policy();

        Assert.Equal(0, policy.Pass(NoRenderPolicyBlocks.Instance));
        Assert.Equal(1, policy.Passes);
        Assert.Equal(0, policy.Seen);
    }

    [Fact]
    public void EveryEntityInTheListIsSeenEveryPass()
    {
        // WHAT MAKES "decided is flat" A CLAIM THAT CAN FAIL. seen climbing
        // while decided stands still is the design working; both at zero is a
        // build that stopped doing the thing, and cs_cl_policy prints the pair
        // for exactly that reason.
        var blocks = new FakeBlocks(3);
        for (int i = 0; i < 3; i++)
        {
            Write(blocks.Raw(i), HeadHandle, unchecked((int)(0x00010000u | (uint)(i + 1))));
            Write(blocks.Raw(i), Tail(nameof(EngineBlockClientTail.ModelType)), ClientRenderFacts.Studio);
        }

        var policy = Policy();

        Assert.Equal(3, policy.Pass(blocks));
        Assert.Equal(0, policy.Pass(blocks));

        Assert.Equal(6, policy.Seen);
        Assert.Equal(3, policy.Decisions);
    }

    [Fact]
    public void ReadFactsAndReadChoicesRoundTripThroughTheBlock()
    {
        // The reader the shell's loads mirror. If the two disagreed about an
        // offset, every fact above would still pass — they write and read
        // through the same pair.
        var blocks = OneProp();
        Write(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.FadeStyle)), (int)FadeStyle.PortalWindow);
        Write(blocks.Raw(0), HeadModelIndex, IkModel);

        Policy().Pass(blocks);

        ClientRenderFacts facts = ClientRenderPolicy.ReadFacts(blocks.Raw(0), new FakeModels());
        ClientRenderChoices choices = ClientRenderPolicy.ReadChoices(blocks.Raw(0));

        Assert.Equal(FadeStyle.PortalWindow, facts.Fade);
        Assert.Equal(1, facts.IkChainCount);
        Assert.Equal(ClientRenderPolicy.Decide(facts).Group, choices.Group);
        Assert.Equal(ClientRenderPolicy.Decide(facts).Shadow, choices.Shadow);
    }

    //=========================================================================
    // THE MODEL COUNTS: read from the side's managed header, not the block.
    //=========================================================================

    [Fact]
    public void AStudioModelsCountsAreReadFromItsHeaderByTheBlocksModelIndex()
    {
        var models = new FakeModels();
        var blocks = OneProp();
        Write(blocks.Raw(0), HeadModelIndex, PosedModel);

        ClientRenderFacts facts = ClientRenderPolicy.ReadFacts(blocks.Raw(0), models);

        Assert.Equal((4, 2, 0, 0), (facts.SequenceCount, facts.PoseParameterCount, facts.BoneControllerCount, facts.IkChainCount));
        Assert.Equal(new[] { PosedModel }, models.Asked);
    }

    [Fact]
    public void AStudioModelWhoseHeaderCannotOpenHasNoSequencesToDecideFromYet()
    {
        var blocks = OneProp();
        Write(blocks.Raw(0), HeadModelIndex, 99);

        ClientRenderFacts facts = ClientRenderPolicy.ReadFacts(blocks.Raw(0), new FakeModels());

        Assert.Equal((ClientRenderFacts.SequencesUnavailable, 0, 0, 0),
                     (facts.SequenceCount, facts.PoseParameterCount, facts.BoneControllerCount, facts.IkChainCount));
    }

    [Fact]
    public void BeforeTheSkeletonsAreComposedNoModelHasSequencesToDecideFromYet()
    {
        ClientRenderFacts facts = ClientRenderPolicy.ReadFacts(OneProp().Raw(0), null);

        Assert.Equal(ClientRenderFacts.SequencesUnavailable, facts.SequenceCount);
    }

    [Fact]
    public void ANonStudioModelIsNeverAskedOfTheHeaders()
    {
        var models = new FakeModels();
        var blocks = OneProp();
        Write(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.ModelType)), 1);   // mod_brush

        ClientRenderFacts facts = ClientRenderPolicy.ReadFacts(blocks.Raw(0), models);

        Assert.Equal(ClientRenderFacts.SequencesUnavailable, facts.SequenceCount);
        Assert.Empty(models.Asked);
    }

    [Fact]
    public void ThePassReadsTheCountsFromTheModelsItWasGiven()
    {
        // The shadow a posable model earns (RTT dynamic) is only reachable
        // through the header's pose-parameter count.
        var blocks = OneProp();
        Write(blocks.Raw(0), HeadModelIndex, PosedModel);
        var models = new FakeModels();

        Policy(models).Pass(blocks);

        Assert.Contains(PosedModel, models.Asked);
        Assert.Equal((int)ShadowCast.RenderToTextureDynamic,
                     Read(blocks.Raw(0), Tail(nameof(EngineBlockClientTail.ShadowType))));
    }
}

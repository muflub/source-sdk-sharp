using System.Reflection;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE ENGINE BLOCK, ASSERTED FIELD BY FIELD (plan_entity.md §3.2, and §7's
/// "engine block drift").
///
/// <para>
/// The block is declared ONCE, in <c>SourceSharp.Abi/EngineBlock.cs</c>, because
/// a shell getter has to be one load at a constant offset and managed code has to
/// write the value that load reads. <c>EngineBlock_t</c> and the
/// <c>static_assert</c>s about it are emitted from that declaration into
/// <c>engine_block_generated.h</c>; <c>engine_block.h</c> is prose and an
/// <c>#include</c>. Three checks stand behind it. This file is two of them — the
/// per-field offsets and the fingerprint, plus the facts that keep the generated
/// header honest — and the third is <c>engine_block_layout_test.cpp</c>, whose
/// numbers are hand-written and therefore the one genuinely second opinion.
/// </para>
///
/// <list type="number">
///   <item>
///     ONE FACT PER FIELD, against <see cref="Marshal.OffsetOf(System.Type, string)"/>
///     — the runtime that will actually lay the struct out, not this file's
///     arithmetic about how it ought to. The C# fields' offsets are what native's
///     <c>offsetof</c> is compared against, so a wrong number here would be
///     agreed with rather than caught.
///   </item>
///   <item>
///     THE FINGERPRINT, pinned to a literal, plus the three edits that must move
///     it: reorder, retype at equal width, add. A hash that failed to move on one
///     of those would let two builds disagree about the block and still shake
///     hands at join.
///   </item>
/// </list>
///
/// <para>
/// AND ONE FACT THAT MAKES THE OTHER FORTY-THREE MEAN SOMETHING.
/// <see cref="TheHeadDeclaresExactlyTheFieldsThisFileChecks"/> and its two
/// siblings pin the FIELD NAME SET of each struct. Without them a field added to
/// the block would simply have no fact about it, every existing fact would go on
/// passing, and the per-field coverage this file claims would quietly become
/// per-field coverage of an older block.
/// </para>
/// </summary>
public class EngineBlockTests
{    //-------------------------------------------------------------------------
    // EngineBlockHead: 21 fields, 160 bytes.
    //-------------------------------------------------------------------------
    [Fact]
    public void HeadHandleIsAt0()
        => Assert.Equal(0, (int)Marshal.OffsetOf<EngineBlockHead>("Handle"));

    [Fact]
    public void HeadClassnameIndexIsAt4()
        => Assert.Equal(4, (int)Marshal.OffsetOf<EngineBlockHead>("ClassnameIndex"));

    [Fact]
    public void HeadModelIndexIsAt8()
        => Assert.Equal(8, (int)Marshal.OffsetOf<EngineBlockHead>("ModelIndex"));

    [Fact]
    public void HeadCollisionModelIndexIsAt12()
        => Assert.Equal(12, (int)Marshal.OffsetOf<EngineBlockHead>("CollisionModelIndex"));

    [Fact]
    public void HeadOriginIsAt16()
        => Assert.Equal(16, (int)Marshal.OffsetOf<EngineBlockHead>("Origin"));

    [Fact]
    public void HeadAnglesIsAt28()
        => Assert.Equal(28, (int)Marshal.OffsetOf<EngineBlockHead>("Angles"));

    [Fact]
    public void HeadMinsIsAt40()
        => Assert.Equal(40, (int)Marshal.OffsetOf<EngineBlockHead>("Mins"));

    [Fact]
    public void HeadMaxsIsAt52()
        => Assert.Equal(52, (int)Marshal.OffsetOf<EngineBlockHead>("Maxs"));

    [Fact]
    public void HeadWorldTransformIsAt64()
        => Assert.Equal(64, (int)Marshal.OffsetOf<EngineBlockHead>("WorldTransform"));

    [Fact]
    public void HeadSolidTypeIsAt112()
        => Assert.Equal(112, (int)Marshal.OffsetOf<EngineBlockHead>("SolidType"));

    [Fact]
    public void HeadSolidFlagsIsAt116()
        => Assert.Equal(116, (int)Marshal.OffsetOf<EngineBlockHead>("SolidFlags"));

    [Fact]
    public void HeadCollisionGroupIsAt120()
        => Assert.Equal(120, (int)Marshal.OffsetOf<EngineBlockHead>("CollisionGroup"));

    [Fact]
    public void HeadParentHandleIsAt124()
        => Assert.Equal(124, (int)Marshal.OffsetOf<EngineBlockHead>("ParentHandle"));

    [Fact]
    public void HeadOwnerHandleIsAt128()
        => Assert.Equal(128, (int)Marshal.OffsetOf<EngineBlockHead>("OwnerHandle"));

    [Fact]
    public void HeadMoveTypeIsAt132()
        => Assert.Equal(132, (int)Marshal.OffsetOf<EngineBlockHead>("MoveType"));

    [Fact]
    public void HeadFlagsIsAt136()
        => Assert.Equal(136, (int)Marshal.OffsetOf<EngineBlockHead>("Flags"));

    [Fact]
    public void HeadEffectsIsAt140()
        => Assert.Equal(140, (int)Marshal.OffsetOf<EngineBlockHead>("Effects"));

    [Fact]
    public void HeadWaterLevelIsAt144()
        => Assert.Equal(144, (int)Marshal.OffsetOf<EngineBlockHead>("WaterLevel"));

    /// <summary>
    /// The word that was <c>Padding0</c>: same offset, and now a field with a
    /// job — see <c>EngineBlockHead.ChildCount</c>.
    /// </summary>
    [Fact]
    public void HeadChildCountIsAt148()
        => Assert.Equal(148, (int)Marshal.OffsetOf<EngineBlockHead>("ChildCount"));

    /// <summary>
    /// The teleport counter the server bumps and the wire carries — see
    /// <c>SourceSharp.Abi.TeleportDiscontinuity</c>. Its offset is also the
    /// offset the SendProp and the change mark are both built from, so a fact
    /// about it is a fact about the wire.
    /// </summary>
    [Fact]
    public void HeadInterpolationFrameIsAt152()
        => Assert.Equal(152, (int)Marshal.OffsetOf<EngineBlockHead>("InterpolationFrame"));

    /// <summary>
    /// And the value the client last acted on, beside it. The two words are
    /// what keeps the head a multiple of eight; one alone would have left a
    /// hole behind it, which is what the head's own prose refuses.
    /// </summary>
    [Fact]
    public void HeadOldInterpolationFrameIsAt156()
        => Assert.Equal(156, (int)Marshal.OffsetOf<EngineBlockHead>("OldInterpolationFrame"));

    [Fact]
    public void HeadIs160BytesWide()
        => Assert.Equal(160, Marshal.SizeOf<EngineBlockHead>());

    [Fact]
    public void TheHeadDeclaresExactlyTheFieldsThisFileChecks()
    {
        // THE FACT THE PER-FIELD FACTS DEPEND ON. Each of them names one field;
        // none of them notices a field nobody named. A block that grew a
        // fourteenth placement value would pass every offset fact above and be
        // checked by none of them, on either side.
        Assert.Equal(
            new[] { "Handle", "ClassnameIndex", "ModelIndex", "CollisionModelIndex", "Origin", "Angles", "Mins", "Maxs", "WorldTransform", "SolidType", "SolidFlags", "CollisionGroup", "ParentHandle", "OwnerHandle", "MoveType", "Flags", "Effects", "WaterLevel", "ChildCount", "InterpolationFrame", "OldInterpolationFrame" },
            EngineBlock.FieldsOf(typeof(EngineBlockHead)).Select(f => f.Name).ToArray());
    }

    //-------------------------------------------------------------------------
    // EngineBlockServerTail: 4 fields, 120 bytes.
    //-------------------------------------------------------------------------
    [Fact]
    public void ServerPvsInfoIsAt0()
        => Assert.Equal(0, (int)Marshal.OffsetOf<EngineBlockServerTail>("PvsInfo"));

    [Fact]
    public void ServerPlayerStateIsAt40()
        => Assert.Equal(40, (int)Marshal.OffsetOf<EngineBlockServerTail>("PlayerState"));

    [Fact]
    public void ServerViewOriginIsAt104()
        => Assert.Equal(104, (int)Marshal.OffsetOf<EngineBlockServerTail>("ViewOrigin"));

    [Fact]
    public void ServerViewOriginFrameIsAt116()
        => Assert.Equal(116, (int)Marshal.OffsetOf<EngineBlockServerTail>("ViewOriginFrame"));

    [Fact]
    public void ServerIs120BytesWide()
        => Assert.Equal(120, Marshal.SizeOf<EngineBlockServerTail>());

    [Fact]
    public void TheServerDeclaresExactlyTheFieldsThisFileChecks()
    {
        // THE FACT THE PER-FIELD FACTS DEPEND ON. Each of them names one field;
        // none of them notices a field nobody named. A block that grew a
        // fourteenth placement value would pass every offset fact above and be
        // checked by none of them, on either side.
        Assert.Equal(
            new[] { "PvsInfo", "PlayerState", "ViewOrigin", "ViewOriginFrame" },
            EngineBlock.FieldsOf(typeof(EngineBlockServerTail)).Select(f => f.Name).ToArray());
    }

    //-------------------------------------------------------------------------
    // EngineBlockClientTail: 36 fields, 208 bytes.
    //-------------------------------------------------------------------------
    [Fact]
    public void ClientRenderOriginIsAt0()
        => Assert.Equal(0, (int)Marshal.OffsetOf<EngineBlockClientTail>("RenderOrigin"));

    [Fact]
    public void ClientRenderAnglesIsAt12()
        => Assert.Equal(12, (int)Marshal.OffsetOf<EngineBlockClientTail>("RenderAngles"));

    [Fact]
    public void ClientRenderMinsIsAt24()
        => Assert.Equal(24, (int)Marshal.OffsetOf<EngineBlockClientTail>("RenderMins"));

    [Fact]
    public void ClientRenderMaxsIsAt36()
        => Assert.Equal(36, (int)Marshal.OffsetOf<EngineBlockClientTail>("RenderMaxs"));

    [Fact]
    public void ClientRenderColorRIsAt48()
        => Assert.Equal(48, (int)Marshal.OffsetOf<EngineBlockClientTail>("RenderColorR"));

    [Fact]
    public void ClientRenderColorGIsAt49()
        => Assert.Equal(49, (int)Marshal.OffsetOf<EngineBlockClientTail>("RenderColorG"));

    [Fact]
    public void ClientRenderColorBIsAt50()
        => Assert.Equal(50, (int)Marshal.OffsetOf<EngineBlockClientTail>("RenderColorB"));

    [Fact]
    public void ClientRenderColorAIsAt51()
        => Assert.Equal(51, (int)Marshal.OffsetOf<EngineBlockClientTail>("RenderColorA"));

    [Fact]
    public void ClientFxBlendIsAt52()
        => Assert.Equal(52, (int)Marshal.OffsetOf<EngineBlockClientTail>("FxBlend"));

    [Fact]
    public void ClientSkinIsAt56()
        => Assert.Equal(56, (int)Marshal.OffsetOf<EngineBlockClientTail>("Skin"));

    [Fact]
    public void ClientBodyIsAt60()
        => Assert.Equal(60, (int)Marshal.OffsetOf<EngineBlockClientTail>("Body"));

    [Fact]
    public void ClientShadowChoiceIsAt64()
        => Assert.Equal(64, (int)Marshal.OffsetOf<EngineBlockClientTail>("ShadowChoice"));

    [Fact]
    public void ClientRenderGroupIsAt68()
        => Assert.Equal(68, (int)Marshal.OffsetOf<EngineBlockClientTail>("RenderGroup"));

    [Fact]
    public void ClientSequenceIsAt72()
        => Assert.Equal(72, (int)Marshal.OffsetOf<EngineBlockClientTail>("Sequence"));

    [Fact]
    public void ClientCycleIsAt76()
        => Assert.Equal(76, (int)Marshal.OffsetOf<EngineBlockClientTail>("Cycle"));

    [Fact]
    public void ClientPlaybackRateIsAt80()
        => Assert.Equal(80, (int)Marshal.OffsetOf<EngineBlockClientTail>("PlaybackRate"));

    [Fact]
    public void ClientAnimTimeIsAt84()
        => Assert.Equal(84, (int)Marshal.OffsetOf<EngineBlockClientTail>("AnimTime"));

    [Fact]
    public void ClientSimulationTimeIsAt88()
        => Assert.Equal(88, (int)Marshal.OffsetOf<EngineBlockClientTail>("SimulationTime"));

    [Fact]
    public void ClientRenderTransformIsAt92()
        => Assert.Equal(92, (int)Marshal.OffsetOf<EngineBlockClientTail>("RenderTransform"));

    [Fact]
    public void ClientDrawnIsAt140()
        => Assert.Equal(140, (int)Marshal.OffsetOf<EngineBlockClientTail>("Drawn"));

    [Fact]
    public void ClientTransparentIsAt144()
        => Assert.Equal(144, (int)Marshal.OffsetOf<EngineBlockClientTail>("Transparent"));

    [Fact]
    public void ClientShadowTypeIsAt148()
        => Assert.Equal(148, (int)Marshal.OffsetOf<EngineBlockClientTail>("ShadowType"));

    [Fact]
    public void ClientShadowFlagsIsAt152()
        => Assert.Equal(152, (int)Marshal.OffsetOf<EngineBlockClientTail>("ShadowFlags"));

    [Fact]
    public void ClientDormantIsAt156()
        => Assert.Equal(156, (int)Marshal.OffsetOf<EngineBlockClientTail>("Dormant"));

    [Fact]
    public void ClientViewModelIsAt160()
        => Assert.Equal(160, (int)Marshal.OffsetOf<EngineBlockClientTail>("ViewModel"));

    [Fact]
    public void ClientCustomDrawIsAt164()
        => Assert.Equal(164, (int)Marshal.OffsetOf<EngineBlockClientTail>("CustomDraw"));

    [Fact]
    public void ClientTranslucentIsAt168()
        => Assert.Equal(168, (int)Marshal.OffsetOf<EngineBlockClientTail>("Translucent"));

    [Fact]
    public void ClientOwnerSeenRuleIsAt172()
        => Assert.Equal(172, (int)Marshal.OffsetOf<EngineBlockClientTail>("OwnerSeenRule"));

    [Fact]
    public void ClientFadeStyleIsAt176()
        => Assert.Equal(176, (int)Marshal.OffsetOf<EngineBlockClientTail>("FadeStyle"));

    [Fact]
    public void ClientFadeStartDistanceIsAt180()
        => Assert.Equal(180, (int)Marshal.OffsetOf<EngineBlockClientTail>("FadeStartDistance"));

    [Fact]
    public void ClientFadeEndDistanceIsAt184()
        => Assert.Equal(184, (int)Marshal.OffsetOf<EngineBlockClientTail>("FadeEndDistance"));

    [Fact]
    public void ClientFadeMinAlphaIsAt188()
        => Assert.Equal(188, (int)Marshal.OffsetOf<EngineBlockClientTail>("FadeMinAlpha"));

    [Fact]
    public void ClientModelTypeIsAt192()
        => Assert.Equal(192, (int)Marshal.OffsetOf<EngineBlockClientTail>("ModelType"));

    [Fact]
    public void ClientPolicyInputsIsAt196()
        => Assert.Equal(196, (int)Marshal.OffsetOf<EngineBlockClientTail>("PolicyInputs"));

    [Fact]
    public void ClientPolicyOutputsIsAt200()
        => Assert.Equal(200, (int)Marshal.OffsetOf<EngineBlockClientTail>("PolicyOutputs"));

    [Fact]
    public void ClientHitboxSetIsAt204()
        => Assert.Equal(204, (int)Marshal.OffsetOf<EngineBlockClientTail>("HitboxSet"));

    [Fact]
    public void ClientIs208BytesWide()
        => Assert.Equal(208, Marshal.SizeOf<EngineBlockClientTail>());

    [Fact]
    public void TheClientDeclaresExactlyTheFieldsThisFileChecks()
    {
        // THE FACT THE PER-FIELD FACTS DEPEND ON. Each of them names one field;
        // none of them notices a field nobody named. A block that grew a
        // fourteenth placement value would pass every offset fact above and be
        // checked by none of them, on either side.
        Assert.Equal(
            new[] { "RenderOrigin", "RenderAngles", "RenderMins", "RenderMaxs", "RenderColorR", "RenderColorG", "RenderColorB", "RenderColorA", "FxBlend", "Skin", "Body", "ShadowChoice", "RenderGroup", "Sequence", "Cycle", "PlaybackRate", "AnimTime", "SimulationTime", "RenderTransform", "Drawn", "Transparent", "ShadowType", "ShadowFlags", "Dormant", "ViewModel", "CustomDraw", "Translucent", "OwnerSeenRule", "FadeStyle", "FadeStartDistance", "FadeEndDistance", "FadeMinAlpha", "ModelType", "PolicyInputs", "PolicyOutputs", "HitboxSet" },
            EngineBlock.FieldsOf(typeof(EngineBlockClientTail)).Select(f => f.Name).ToArray());
    }

    //-------------------------------------------------------------------------
    // EngineBlock: 3 fields, 488 bytes.
    //-------------------------------------------------------------------------
    [Fact]
    public void BlockHeadIsAt0()
        => Assert.Equal(0, (int)Marshal.OffsetOf<EngineBlock>("Head"));

    [Fact]
    public void BlockServerIsAt160()
        => Assert.Equal(160, (int)Marshal.OffsetOf<EngineBlock>("Server"));

    [Fact]
    public void BlockClientIsAt280()
        => Assert.Equal(280, (int)Marshal.OffsetOf<EngineBlock>("Client"));

    [Fact]
    public void BlockIs488BytesWide()
        => Assert.Equal(488, Marshal.SizeOf<EngineBlock>());

    [Fact]
    public void TheBlockDeclaresExactlyTheFieldsThisFileChecks()
    {
        // THE FACT THE PER-FIELD FACTS DEPEND ON. Each of them names one field;
        // none of them notices a field nobody named. A block that grew a
        // fourteenth placement value would pass every offset fact above and be
        // checked by none of them, on either side.
        Assert.Equal(
            new[] { "Head", "Server", "Client" },
            EngineBlock.FieldsOf(typeof(EngineBlock)).Select(f => f.Name).ToArray());
    }

    //-------------------------------------------------------------------------
    // No implicit padding anywhere in the block.
    //-------------------------------------------------------------------------
    [Fact]
    public void TheHeadHasNoPaddingNobodyDeclared()
        => Assert.Equal(Marshal.SizeOf<EngineBlockHead>(), DeclaredWidth(typeof(EngineBlockHead)));

    [Fact]
    public void TheServerHasNoPaddingNobodyDeclared()
        => Assert.Equal(Marshal.SizeOf<EngineBlockServerTail>(), DeclaredWidth(typeof(EngineBlockServerTail)));

    [Fact]
    public void TheClientHasNoPaddingNobodyDeclared()
        => Assert.Equal(Marshal.SizeOf<EngineBlockClientTail>(), DeclaredWidth(typeof(EngineBlockClientTail)));

    [Fact]
    public void TheBlockHasNoPaddingNobodyDeclared()
        => Assert.Equal(Marshal.SizeOf<EngineBlock>(), DeclaredWidth(typeof(EngineBlock)));

    //-------------------------------------------------------------------------
    // The size constant the generator reads.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheSizeConstantIsWhatTheRuntimeActuallyLaysOut()
    {
        // THE ONE NUMBER THE GENERATOR TRUSTS. NetStateGenerator cannot run
        // Marshal.SizeOf while it is compiling, so it reads EngineBlock.SizeBytes
        // out of the compilation and starts every entity's fields after it. If
        // that constant were larger than the struct, entities would carry dead
        // bytes; if it were smaller, the first declared field of every class on
        // the hierarchy would sit on top of the block's handle and origin and
        // the two would overwrite each other with nothing reporting it.
        // Constant first, runtime second: xUnit reads the first argument as the
        // expectation, and the constant is what the generator already believed
        // when it placed every field.
        Assert.Equal(EngineBlock.SizeBytes, EngineBlock.MarshalledBytes);
    }

    //-------------------------------------------------------------------------
    // The fingerprint, and the three edits that have to move it.
    //-------------------------------------------------------------------------

    /// <summary>
    /// The value this build puts on the join wire, written down.
    ///
    /// <para>
    /// A LITERAL, ON PURPOSE. Every other fact here is computed from the
    /// declaration, so every other fact moves with it; this one does not, and
    /// that is what makes it the mutation gate. Reorder two fields, retype one
    /// at the same width, or add one, and this is what goes red. It is also the
    /// number compiled into the native side, so changing the block is a
    /// deliberate two-file act rather than something that happens on the way
    /// past.
    /// </para>
    /// </summary>
    // Lane M1-A renamed the client tail's HideWhenOwnerSeen to OwnerSeenRule
    // (the viewer's own body joined the viewmodel under it): 0x027436fc -> 0xb4839a12.
    private const uint PinnedFingerprint = 0xb4839a12u;

    [Fact]
    public void TheFingerprintIsTheValueBothSidesWereBuiltWith()
        => Assert.Equal(PinnedFingerprint, EngineBlockFingerprint.Current);

    [Fact]
    public void TheFingerprintIsComputedFromTheBlockAndNothingElse()
        => Assert.Equal(EngineBlockFingerprint.Of(typeof(EngineBlock)),
                        EngineBlockFingerprint.Current);

    [Fact]
    public void ReorderingTwoFieldsInsideTheHeadMovesTheFingerprint()
    {
        // THE MUTATION A TOP-LEVEL WALK CANNOT SEE, which is why this is not
        // the same fact as LayoutHandshakeTests' flat one. The real block's own
        // field list is three names -- Head, Server, Client -- and swapping two
        // equally wide fields INSIDE the head leaves all three names, all three
        // offsets and all three sizes exactly as they were. These two stand-ins
        // have that shape: identical at the top level, different one level down.
        Assert.Equal(Marshal.SizeOf<NestedOuter>(), Marshal.SizeOf<NestedOuterReordered>());
        Assert.Equal(TopLevelShape(typeof(NestedOuter)), TopLevelShape(typeof(NestedOuterReordered)));

        Assert.NotEqual(EngineBlockFingerprint.Of(typeof(NestedOuter)),
                        EngineBlockFingerprint.Of(typeof(NestedOuterReordered)));
    }

    [Fact]
    public void RetypingAFieldInsideTheHeadAtTheSameWidthMovesTheFingerprint()
    {
        // int to float: same width, same offset, same name, same struct size.
        // A size check misses it entirely; the recursive walk mixes the nested
        // field's own width, which is where the two part company.
        Assert.Equal(Marshal.SizeOf<NestedOuter>(), Marshal.SizeOf<NestedOuterRetyped>());

        Assert.NotEqual(EngineBlockFingerprint.Of(typeof(NestedOuter)),
                        EngineBlockFingerprint.Of(typeof(NestedOuterRetyped)));
    }

    [Fact]
    public void AddingAFieldInsideTheHeadMovesTheFingerprint()
        => Assert.NotEqual(EngineBlockFingerprint.Of(typeof(NestedOuter)),
                           EngineBlockFingerprint.Of(typeof(NestedOuterExtended)));

    [Fact]
    public void ResizingAReservationMovesTheFingerprint()
    {
        // THE SERVER TAIL'S TWO RESERVATIONS ARE SIZES AND NOTHING ELSE -- their
        // C# type says how much room, never what goes in it -- so a resize is an
        // edit the hash has to move on and no name, offset or total width would
        // report. (Step 3c added two ordinary fields behind them; the
        // reservations themselves are still opaque.) These two
        // stand-ins differ in nothing else a walk could notice: same field names,
        // same offsets, same total struct size, and even the same trailing
        // padding -- seven bytes and eight bytes both round to sixteen.
        Assert.Equal(Marshal.SizeOf<ReservationEight>(), Marshal.SizeOf<ReservationSeven>());

        Assert.NotEqual(EngineBlockFingerprint.Of(typeof(ReservationEight)),
                        EngineBlockFingerprint.Of(typeof(ReservationSeven)));
    }

    [Fact]
    public void MarshalSizeOfReportsAFixedBuffersWholeWidth()
    {
        // WHAT THE FACT ABOVE ACTUALLY RESTS ON, said out loud because it is not
        // obvious and because the fingerprint would be blind to a resized
        // reservation if it stopped being true. `fixed byte Room[8]` compiles to
        // a field whose type is a generated struct holding ONE byte; the
        // marshaller nonetheless reports that type as eight bytes wide, and that
        // is the number the fingerprint mixes. A runtime that reported 1 would
        // give both reservations above the same shape and the hash would stop
        // moving -- silently, with every other fact in this file still green.
        FieldInfo eight = typeof(ReservationEight).GetField("Room",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        FieldInfo seven = typeof(ReservationSeven).GetField("Room",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;

        Assert.Equal(8, Marshal.SizeOf(eight.FieldType));
        Assert.Equal(7, Marshal.SizeOf(seven.FieldType));
    }

    //-------------------------------------------------------------------------
    // The generated native header.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheGeneratedNativeHeaderOnDiskIsWhatThisDeclarationProduces()
    {
        // THE LINK BETWEEN THE TWO DECLARATIONS, and the reason a C++-only edit
        // cannot pass. engine_block_generated.h carries one static_assert per
        // field, built from the C# struct; the native build stops if
        // EngineBlock_t disagrees with it. Editing that header to make the
        // native build pass is the obvious way round, and this is what refuses
        // it: the file on disk has to be, byte for byte, what the C# side says.
        string path = Locate(EngineBlock.NativeHeaderPath);
        string want = EngineBlock.NativeHeaderText;
        string got  = File.ReadAllText(path);

        if (want == got)
            return;

        if (Environment.GetEnvironmentVariable("SOURCESHARP_WRITE_ENGINE_BLOCK_HEADER") == "1")
        {
            File.WriteAllText(path, want);

            Assert.Fail(
                $"{EngineBlock.NativeHeaderPath} was stale and has been REGENERATED. "
                + "Rebuild the native half and run again without "
                + "SOURCESHARP_WRITE_ENGINE_BLOCK_HEADER=1. This fails rather than "
                + "passes because a run that rewrote its own subject is not evidence "
                + "that the subject was right.");
        }

        Assert.Fail(
            $"{EngineBlock.NativeHeaderPath} does not match SourceSharp.Abi.EngineBlock. "
            + "Either the C# block changed and the header was not regenerated, or the "
            + "header was edited by hand. Regenerate with "
            + "SOURCESHARP_WRITE_ENGINE_BLOCK_HEADER=1.");
    }

    [Fact]
    public void TheGeneratedHeaderCarriesTheFingerprintThisBuildSends()
    {
        // Named separately from the byte comparison above because it is a
        // different failure: this one says the native half was built against a
        // DIFFERENT BLOCK, which is precisely what the join handshake exists to
        // refuse and precisely what a stale .h would cause.
        Assert.Contains($"#define SOURCESHARP_ENGINE_BLOCK_FINGERPRINT 0x{EngineBlockFingerprint.Current:x8}u",
                        File.ReadAllText(Locate(EngineBlock.NativeHeaderPath)));
    }

    [Fact]
    public void TheGeneratedHeaderAssertsOneOffsetPerFieldExceptEachStructsFirst()
    {
        // The C++ half of "one check per field". A generated header that emitted
        // only the four sizeof() assertions would compile, look generated, and
        // agree with a C++ struct whose fields had been swapped.
        //
        // MINUS ONE PER STRUCT, and the subtraction is the point of the fact
        // rather than an allowance made for it. A C++ struct with no base class
        // puts its first member at offset zero, so `offsetof( X, <first> ) == 0`
        // could not fail when EngineBlock_t was hand-written and cannot fail now
        // that it is generated. Those four were deleted; this number is what
        // says they stayed deleted, and that nothing else was.
        string header = File.ReadAllText(Locate(EngineBlock.NativeHeaderPath));

        int expected = EngineBlock.NativeStructs.Sum(s => EngineBlock.FieldsOf(s.Managed).Length)
                     - EngineBlock.NativeStructs.Count;

        Assert.Equal(expected,
                     header.Split("static_assert(offsetof(").Length - 1);
    }

    [Fact]
    public void TheGeneratedHeaderAssertsNoOffsetThatCannotDiffer()
    {
        // THE DELETION, STATED AS A PROPERTY RATHER THAN AS A COUNT. The fact
        // above would still pass if the generator emitted `== 0` for one field
        // and dropped a real assert somewhere else to keep the total. This one
        // says what the deleted asserts had in common: an expected offset of
        // zero, which the C++ standard supplies for free.
        string header = File.ReadAllText(Locate(EngineBlock.NativeHeaderPath));

        Assert.DoesNotContain(") == 0,", header);
    }

    [Fact]
    public void TheGeneratedHeaderDeclaresEveryStructTheNativeSideUses()
    {
        // WHAT REPLACED THE HAND-WRITTEN engine_block.h. The structs are build
        // output now, so "the header still declares them" is a fact rather than
        // something a reader of the .h can see. A generator that emitted the
        // asserts and not the structs would leave the native build with four
        // undefined types -- loud -- but a generator that emitted three of four
        // would leave a working build missing the tail nobody happened to touch.
        string header = File.ReadAllText(Locate(EngineBlock.NativeHeaderPath));

        foreach ((string cppName, _) in EngineBlock.NativeStructs)
            Assert.Contains($"\nstruct {cppName}\n{{\n", header);
    }

    [Fact]
    public void TheGeneratedHeaderDeclaresEveryFieldWithItsFixedWidthCppType()
    {
        // ONE EMITTED LINE PER FIELD, spelled with the width in the name. A
        // field emitted as `int` rather than `int32_t` lays out identically on
        // linux64 today and is exactly the drift the fingerprint cannot see,
        // because the fingerprint is computed from the C# side alone.
        string header = File.ReadAllText(Locate(EngineBlock.NativeHeaderPath));

        foreach ((_, Type managed) in EngineBlock.NativeStructs)
        {
            foreach (FieldInfo field in EngineBlock.FieldsOf(managed))
            {
                (string cppType, string declarator) = EngineBlock.NativeDeclarationOf(field);

                Assert.Contains($"\t{cppType}", header);
                Assert.Contains($" {declarator};\n", header);
            }
        }
    }

    [Fact]
    public void TheGeneratedHeaderSpellsEveryPrimitiveAtAFixedWidth()
    {
        // The rule the map exists to keep, said once about the whole file: no
        // bare `int`, `unsigned` or `char` reaches the native declaration.
        string header = File.ReadAllText(Locate(EngineBlock.NativeHeaderPath));

        foreach (string line in header.Split('\n'))
        {
            if (!line.StartsWith('\t') || line.TrimStart().StartsWith("//"))
                continue;

            Assert.DoesNotMatch(@"^\t(int|unsigned|char|long|short|double)\s", line);
        }
    }

    [Fact]
    public void EveryFieldOfTheBlockCarriesTheProseTheNativeHeaderEmits()
    {
        // THE PROSE IS PART OF THE DECLARATION NOW. engine_block.h used to
        // explain each field beside it; generating the struct moved that text
        // into [NativeDoc] attributes, and a field added without one would
        // arrive in the native header bare -- the reasoning lost rather than
        // moved. BuildNativeHeader throws on it; this says so in the run.
        foreach ((_, Type managed) in EngineBlock.NativeStructs)
        {
            Assert.NotNull(managed.GetCustomAttribute<NativeDocAttribute>());

            foreach (FieldInfo field in EngineBlock.FieldsOf(managed))
                Assert.NotNull(field.GetCustomAttribute<NativeDocAttribute>());
        }
    }

    [Fact]
    public void AManagedTypeWithNoCppSpellingIsRefusedRatherThanGuessed()
    {
        // THE ONE WAY THIS GENERATOR COULD DO REAL DAMAGE. A fallback -- emit
        // `int32_t` for anything unrecognised -- would produce a native struct
        // that lays out differently from the C# one WITHOUT moving the
        // fingerprint, because the fingerprint reads the C# declaration alone
        // and the C# declaration would be unchanged. That is the exact failure
        // the block exists to make impossible, so the map throws.
        Assert.Throws<NotSupportedException>(() => EngineBlock.NativeTypeOf(typeof(decimal)));
    }

    [Fact]
    public void TheCppSpellingOfEveryTypeTheBlockUsesIsDeclaredInOnePlace()
    {
        // Every field resolves, which is what makes the throw above a guard
        // rather than a trap: the four structs' own types are all mapped today.
        foreach ((_, Type managed) in EngineBlock.NativeStructs)
            foreach (FieldInfo field in EngineBlock.FieldsOf(managed))
                Assert.False(string.IsNullOrEmpty(EngineBlock.NativeDeclarationOf(field).CppType));
    }

    [Fact]
    public void AReservationIsEmittedAsAnArrayOfTheWidthItReserves()
    {
        // A fixed buffer is the one field shape whose C++ spelling is not just a
        // type name, and getting the bound wrong would silently resize the room
        // the engine writes PVSInfo_t and CPlayerState into.
        FieldInfo pvs = EngineBlock.FieldsOf(typeof(EngineBlockServerTail))
            .Single(f => f.Name == "PvsInfo");

        Assert.Equal(("uint64", "PvsInfo[5]"), EngineBlock.NativeDeclarationOf(pvs));
    }

    //-------------------------------------------------------------------------
    // The block is the first thing in every generated layout.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryGeneratedLayoutLeavesRoomForTheBlockBeforeItsOwnFields()
    {
        // WHAT "AT OFFSET ZERO" MEANS IN PRACTICE. The blob an entity carries
        // starts with the block, so a declared field that landed inside
        // [0, SizeBytes) would be sharing storage with the handle, the origin or
        // the collision bounds -- and the symptom would be a networked value
        // that changed when the entity moved.
        var states = StandaloneNetStates.Discover(TestServices.ServerGame, new RecordingLog());

        Assert.NotEmpty(states);

        foreach (var state in states)
        {
            foreach (var field in state.Fields)
            {
                Assert.True(field.Offset >= EngineBlock.SizeBytes,
                    $"{state.ClassName}.{field.Name} is at {field.Offset}, inside the "
                    + $"engine block's first {EngineBlock.SizeBytes} bytes");
            }
        }
    }

    [Fact]
    public void EveryGeneratedLayoutIsAtLeastAsBigAsTheBlock()
    {
        var states = StandaloneNetStates.Discover(TestServices.ServerGame, new RecordingLog());

        Assert.NotEmpty(states);

        foreach (var state in states)
        {
            Assert.True(state.Size >= EngineBlock.SizeBytes,
                $"{state.ClassName} declares {state.Size} bytes, which cannot hold the "
                + $"{EngineBlock.SizeBytes}-byte block it begins with");
        }
    }

    [Fact]
    public void EveryGeneratedLayoutSaysHowWideTheBlockItStartsAfterIs()
    {
        // The generator emits the number into the generated half of each state
        // class. Read back here so that a generator which stopped emitting it --
        // or emitted a stale one -- is a failure rather than a silence: the two
        // facts above would go on passing against a block that had shrunk.
        var states = StandaloneNetStates.Discover(TestServices.ServerGame, new RecordingLog());

        Assert.NotEmpty(states);

        foreach (var state in states)
        {
            FieldInfo? emitted = state.Type.GetField("EngineBlockBytes",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            Assert.True(emitted is not null,
                $"{state.ClassName}'s generated half declares no EngineBlockBytes");
            Assert.Equal(EngineBlock.SizeBytes, (int)emitted!.GetRawConstantValue()!);
        }
    }

    //-------------------------------------------------------------------------
    // Fixtures and helpers.
    //-------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct NestedInner
    {
        public int Origin;
        public int Angles;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NestedInnerReordered
    {
        public int Angles;
        public int Origin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NestedInnerRetyped
    {
        public float Origin;
        public int Angles;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NestedInnerExtended
    {
        public int Origin;
        public int Angles;
        public int Effects;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NestedOuter
    {
        public NestedInner Head;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NestedOuterReordered
    {
        public NestedInnerReordered Head;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NestedOuterRetyped
    {
        public NestedInnerRetyped Head;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NestedOuterExtended
    {
        public NestedInnerExtended Head;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ReservationEight
    {
        public fixed ulong Word[1];
        public fixed byte Room[8];
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ReservationSeven
    {
        public fixed ulong Word[1];
        public fixed byte Room[7];
    }

    /// <summary>
    /// Everything a walk that stopped at the top level would see: each field's
    /// name, offset and marshalled type size, and the struct's own size. Two
    /// types that agree on this and disagree on the fingerprint disagree
    /// somewhere the shallow walk never looked.
    /// </summary>
    private static string TopLevelShape(Type type)
        => string.Join(";", EngineBlock.FieldsOf(type).Select(f =>
               $"{f.Name}@{Marshal.OffsetOf(type, f.Name)}:{Marshal.SizeOf(f.FieldType)}"))
           + $"|{Marshal.SizeOf(type)}";

    /// <summary>
    /// The bytes the declaration actually spends, fixed buffers counted at their
    /// declared length rather than at the one element the compiler's generated
    /// type holds.
    /// </summary>
    private static int DeclaredWidth(Type type)
    {
        int total = 0;

        foreach (FieldInfo field in EngineBlock.FieldsOf(type))
        {
            var buffer = field.GetCustomAttribute<System.Runtime.CompilerServices.FixedBufferAttribute>();

            total += buffer is not null
                ? buffer.Length * Marshal.SizeOf(buffer.ElementType)
                : Marshal.SizeOf(field.FieldType);
        }

        return total;
    }

    private static string Locate(string relative)
    {
        string? candidate = RepoLayout.FindSdkFile(relative);
        if (candidate is not null)
            return candidate;

        Assert.Fail($"{relative} was not found in {RepoLayout.SdkTree}; a scan that "
                    + "read nothing would pass every check that looks inside it");
        return "";
    }
}

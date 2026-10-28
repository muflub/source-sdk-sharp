//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// How the live "the camera sees the owner" fact gates an entity's draw, the
/// block's <c>OwnerSeenRule</c>; the shell's <c>STANDALONE_OWNER_SEEN_*</c>
/// are the same numbers (ClientRenderPolicyTests pins the pair).
/// </summary>
public enum ClientOwnerSeenRule
{
    /// <summary>Not at all.</summary>
    Always = 0,

    /// <summary>Hidden while the camera sees the owner: the viewmodel, drawn at the eye.</summary>
    HiddenWhileSeen = 1,

    /// <summary>Drawn only while the camera sees the owner: the viewer's own body.</summary>
    DrawnOnlyWhileSeen = 2,
}

/// <summary>
/// Everything the render policy is decided FROM, as numbers.
///
/// <para>
/// A struct of plain values rather than an entity, for the reason
/// <c>shadow_cast_policy.h</c>'s <c>ShadowCastFacts_t</c> gave when the
/// shadow half of this rule was C++: it keeps the decision a function of its
/// inputs, so a fact can supply a combination the game would take a particular
/// map to produce.
/// </para>
/// </summary>
public struct ClientRenderFacts
{
    /// <summary>
    /// The viewer's own body: the entity at the engine's local player index
    /// (<c>engine-&gt;GetLocalPlayer()</c>), as stock's <c>C_BasePlayer::IsLocalPlayer</c>.
    /// </summary>
    public bool LocalPlayer;

    /// <summary>Out of the client's PVS. The engine's word, not this side's.</summary>
    public bool Dormant;

    /// <summary>
    /// <c>EF_NODRAW</c> is set in the entity's <c>m_fEffects</c> word — the
    /// server said "do not draw this" (step 8e-render).
    ///
    /// <para>
    /// SEPARATE FROM <see cref="HasModel"/>, and that separation is the whole
    /// of the fix: before this the server expressed "invisible" by sending a
    /// model index of zero, so the client could not tell a hidden brush from a
    /// shapeless one, and the shims that draw INSIDE a brush volume — dust,
    /// smoke, precipitation — refused every one of them for having no model.
    /// A hidden entity now keeps its shape and loses only its draw.
    /// </para>
    /// </summary>
    public bool NoDraw;

    /// <summary>EF_NOSHADOW: the server said it casts nothing.</summary>
    public bool NoShadow;

    /// <summary>Drawn in the viewmodel pass.</summary>
    public bool ViewModel;

    /// <summary>Draws itself through <c>OnDraw</c>.</summary>
    public bool CustomDraw;

    /// <summary>Blended, as <c>SetBlending</c> said.</summary>
    public bool Translucent;

    /// <summary>
    /// Its managed class draws something of its own in the translucent pass
    /// beside an opaque model (<see cref="IRenderPolicyWishes.WantTwoPass"/>):
    /// stock's weapons that answer <c>RENDER_GROUP_TWOPASS</c>.
    /// </summary>
    public bool TwoPass;

    /// <summary>What <see cref="FadeStyle"/> this entity fades by.</summary>
    public FadeStyle Fade;

    /// <summary>What a managed class asked for about its shadow.</summary>
    public ShadowCastChoice ShadowChoice;

    /// <summary>
    /// The model's <c>modtype_t</c>, or −1 for an entity with no model at all.
    /// See <c>EngineBlockClientTail.ModelType</c>: −1 and <c>mod_bad</c> are not
    /// the same state.
    /// </summary>
    public int ModelType;

    /// <summary>
    /// The studio header's sequence count, or −1 when the side cannot open the
    /// header (not paged in yet, or an include the engine cannot find). Zero
    /// is "cannot move"; −1 is "cannot be decided from yet". Read from the
    /// side's managed header (<see cref="ClientRenderPolicy.ReadFacts"/>), not
    /// the block: the shell's CStudioHdr read left with studio.cpp.
    /// </summary>
    public int SequenceCount;

    /// <summary>The studio header's pose-parameter count.</summary>
    public int PoseParameterCount;

    /// <summary>The studio header's bone-controller count.</summary>
    public int BoneControllerCount;

    /// <summary>The studio header's IK-chain count.</summary>
    public int IkChainCount;

    /// <summary>
    /// <c>EF_NORECEIVESHADOW</c> is set: the server said no shadow lands on
    /// this. Read by <see cref="ClientRenderPolicy.ReceivesProjectedTextures"/>.
    /// </summary>
    public bool NoReceiveShadow;

    /// <summary>
    /// The render mode (<c>RenderMode_t</c>, <c>kRenderNormal</c> 0). The engine
    /// block carries no RenderMode_t: the pass reads <c>kRenderTransTexture</c>
    /// for an entity its managed class blends (the shell's Translucent, which
    /// SetBlending writes) and <c>kRenderNormal</c> otherwise (C7 cluster 12 S2).
    /// </summary>
    public int RenderMode;

    /// <summary>The render colour's alpha, 0 to 255 (the block's RenderColorA).</summary>
    public int RenderAlpha;

    /// <summary><c>mod_studio</c>, which is the only model type the shadow rule can grade.</summary>
    public const int Studio = 3;

    /// <summary>No model at all, which is what <c>GetModel() == NULL</c> means.</summary>
    public const int NoModel = -1;

    /// <summary>The studio header is there and its sequences have not loaded.</summary>
    public const int SequencesUnavailable = -1;

    /// <summary>Has something to draw with.</summary>
    public readonly bool HasModel => ModelType != NoModel;

    /// <summary>Is a studio model, which is the only kind the auto shadow rule grades.</summary>
    public readonly bool IsStudioModel => ModelType == Studio;
}

/// <summary>
/// What a managed class asks of its render policy beyond what its block says:
/// the choices stock made in a class's own override (<c>GetRenderGroup</c>).
/// </summary>
public interface IRenderPolicyWishes
{
    /// <summary>
    /// Put <paramref name="entity"/> in the two-pass group, or take it out:
    /// its model drawn in the opaque pass and the class's own
    /// <see cref="ClientStandaloneEntity.OnDrawExtras"/> in the translucent
    /// one. Decided on the next pass.
    /// </summary>
    void WantTwoPass(EntityHandle entity, bool twoPass);
}

/// <summary>No render policy to ask: a class built on its own (a test's).</summary>
public sealed class NoRenderPolicyWishes : IRenderPolicyWishes
{
    public void WantTwoPass(EntityHandle entity, bool twoPass) { }
}

/// <summary>
/// What the shell loads. One value per block field the policy writes.
/// </summary>
public struct ClientRenderChoices
{
    /// <summary>What <c>ShouldDraw</c> answers, before the live third-person term.</summary>
    public bool Drawn;

    /// <summary>
    /// How the live "the camera sees the owner" fact gates the draw
    /// (<see cref="EngineBlockClientTail.OwnerSeenRule"/>).
    /// </summary>
    public ClientOwnerSeenRule OwnerSeen;

    /// <summary>What <c>IsTransparent</c> answers.</summary>
    public bool Transparent;

    /// <summary>Which list the leaf system is told to put the renderable in.</summary>
    public ClientRenderGroup Group;

    /// <summary>What <c>ShadowCastType</c> answers.</summary>
    public ShadowCast Shadow;

    /// <summary>The bits <c>CreateShadow</c> is given.</summary>
    public ClientShadowFlag ShadowFlags;

    /// <summary>
    /// What <c>ShouldReceiveProjectedTextures</c> answers, for a shadow and
    /// for a flashlight (<see cref="ClientShadowFlag.ReceivesShadows"/>,
    /// <see cref="ClientShadowFlag.ReceivesFlashlight"/>; C7 cluster 12 S2):
    /// written into the block's ShadowFlags word above the shadow flags.
    /// </summary>
    public ClientShadowFlag Receives;
}

/// <summary>
/// THE CLIENT'S RENDER POLICY — should draw, render group, translucency, shadow
/// type and shadow flags (plan_entity.md §3.9, step 6b).
///
/// <para>
/// <b>Why it is here and not in the shell.</b> §3.2: these are getters the
/// renderer calls per draw and the leaf system calls per move. They cannot
/// cross, and they cannot be native policy either — so they are answered from
/// memory that managed code writes. This class is the writer. The shell reads
/// <c>EngineBlockClientTail.Drawn</c>, <c>.RenderGroup</c>, <c>.Transparent</c>,
/// <c>.ShadowType</c> and <c>.ShadowFlags</c>, and each of those reads is one
/// load.
/// </para>
///
/// <para>
/// <b>What moved here, and from where.</b> Four native functions in
/// <c>shell_standalone_client.cpp</c> — <c>RenderGroup()</c>,
/// <c>ShouldDraw()</c>, <c>IsTransparent()</c> and <c>ShadowCastType()</c> — and
/// the whole of <c>shadow_cast_policy.h</c>, whose sixteen gtests are the
/// sixteen facts in <c>ClientRenderPolicyTests</c>. What stayed native is the
/// engine data those rules read: opening a studio header is
/// <c>mdlcache</c>'s business, so the shell reads the counts and publishes them
/// into the block, and the RULE over them is here.
/// </para>
///
/// <para>
/// <b>NO INBOUND SLOT AND NO <c>CrossingOp</c>.</b> The pass runs on
/// <see cref="ClientFrameStage.NetUpdatePostDataUpdateEnd"/>, a stage
/// <c>ClientStage</c> already delivers, and fetches its whole list in one
/// outbound call — the same shape 6a used, and the reason 5b's <c>ClientStage</c>
/// was paid for once rather than per phase.
/// </para>
/// </summary>
public sealed class ClientRenderPolicy : IRenderPolicyWishes
{
    //-------------------------------------------------------------------------
    // THE RULES. Static and pure: a fact can supply any combination of inputs,
    // and nothing here can read anything it was not given.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Whether the entity is drawn at all, before the one term the shell has to
    /// combine live (see <see cref="OwnerSeen"/>).
    ///
    /// <para>
    /// A dormant entity draws nothing, whatever else it is. Then: an entity that
    /// draws ITSELF needs no model, because it IS a drawing — a beam asked
    /// whether it has a model answers no for something that must draw every
    /// frame. Everything else needs one: an entity with no model is not hidden,
    /// it is a thing with nothing to draw — a trigger, a marker, a rule.
    /// </para>
    /// </summary>
    public static bool Drawn(in ClientRenderFacts facts)
    {
        if (facts.Dormant)
            return false;

        // EF_NODRAW BEFORE THE CUSTOM-DRAW ARM, deliberately. A beam draws
        // itself and has no model, so the arm below says yes for it whatever
        // its model says — but EF_NODRAW is the server having decided, and
        // stock's C_BaseEntity::ShouldDraw refuses on it ahead of everything
        // except dormancy for the same reason. Above HasModel too, so that a
        // hidden brush that KEEPS its model (which is the point of step
        // 8e-render) is still not drawn.
        if (facts.NoDraw)
            return false;

        // THE VIEWER'S OWN BODY draws by whether it has a model, and whether
        // the camera can see it is the live half (OwnerSeen). Stock's
        // C_BasePlayer::ShouldDraw: ShouldDrawThisPlayer, then the base's.
        if (facts.LocalPlayer && !facts.ViewModel)
            return facts.HasModel;

        if (facts.CustomDraw && !facts.ViewModel)
            return true;

        return facts.HasModel;
    }

    /// <summary>
    /// How the live "the camera sees the owner" fact gates the entity's draw:
    /// the viewmodel is hidden while the third-person camera can see its owner,
    /// the viewer's own body is drawn only while it can, and everything else
    /// ignores it.
    ///
    /// <para>
    /// THE VIEWER'S OWN BODY is the one model a client must NOT draw most of the
    /// time: stock's <c>C_BasePlayer::ShouldDraw</c> opens with the same refusal
    /// (<c>!IsLocalPlayer() || input->CAM_IsThirdPerson()</c>), and every OTHER
    /// player draws by whether it has a model. Decided natively in the shell's
    /// ShouldDraw until lane M1-A.
    /// </para>
    ///
    /// <para>
    /// The viewmodel belongs to the eye, and in third person there is no eye to
    /// hang it on: the camera is behind the player and a pair of arms drawn at
    /// viewmodel FOV, with the depth hack on, lands as a smear across the middle
    /// of the frame in front of the player's own model. Stock refuses the same
    /// draw from <c>C_BaseViewModel::ShouldDraw</c>.
    /// </para>
    ///
    /// <para>
    /// SEES OWNER rather than IS THIRD PERSON, deliberately: a third-person
    /// camera a wall has pushed back onto the eye is a first-person camera, and
    /// the arms belong back on screen for it.
    /// </para>
    /// </summary>
    public static ClientOwnerSeenRule OwnerSeen(in ClientRenderFacts facts)
    {
        if (facts.ViewModel)
            return ClientOwnerSeenRule.HiddenWhileSeen;
        if (facts.LocalPlayer)
            return ClientOwnerSeenRule.DrawnOnlyWhileSeen;
        return ClientOwnerSeenRule.Always;
    }

    /// <summary>
    /// Transparent while it is FADING, and while a managed class that draws
    /// itself says so.
    ///
    /// <para>
    /// The leaf system reads this to choose which pass an entity is drawn in,
    /// and a brush drawn in the opaque pass at anything but full alpha writes
    /// depth for pixels it did not paint — which shows as the world disappearing
    /// behind it rather than as a fade. The sprite family is the other half: an
    /// additive sprite must be in the translucent list or
    /// <c>DrawTranslucentRenderable</c> never sees it at all.
    /// </para>
    /// </summary>
    public static bool Transparent(in ClientRenderFacts facts)
        => facts.Fade != FadeStyle.None || facts.Translucent;

    /// <summary>
    /// Which list the renderer walks this in.
    ///
    /// <para>
    /// The viewmodel pass first: the leaf system keeps these in a list of their
    /// own — the one <c>CollateViewModelRenderables</c> reads — and never in the
    /// leaves, which is what makes it right that the eye pin skips
    /// <c>RenderableChanged</c>.
    /// </para>
    ///
    /// <para>
    /// Then translucency. An entity that fades has to be in the translucent list
    /// and has to be there BEFORE it starts fading: the group is chosen when the
    /// renderable is inserted, and moving between lists is a re-insertion.
    /// </para>
    ///
    /// <para>
    /// <b>The ENTITY group for everything else, including brush models, and that
    /// is the opposite of what <c>C_BaseEntity::GetRenderGroup</c> does</b> — it
    /// sorts brushes into <see cref="ClientRenderGroup.OpaqueBrush"/>. Measured,
    /// not chosen: a renderable placed in the brush group was registered with the
    /// right flags, the right bounds and <c>ShouldDraw</c> answering true, and was
    /// then never asked to draw. Nothing reported anything; the door simply was
    /// not there. The renderer does extra work for that group,
    /// <c>UpdateBrushModelLightmap</c> among it, that assumes a renderable the SDK
    /// made; this hierarchy's <c>DrawModel</c> dispatches on the model type and
    /// calls <c>DrawBrushModelEx</c> itself, so the group buys nothing it needs
    /// and costs it being drawn at all. What is given up is being sorted with the
    /// other brushes: if brush entities ever z-fight against the world, this is
    /// the first place to look.
    /// </para>
    /// </summary>
    public static ClientRenderGroup Group(in ClientRenderFacts facts)
    {
        if (facts.ViewModel)
            return ClientRenderGroup.ViewModelOpaque;

        // A class that asked: C_WeaponStunStick's and C_WeaponPhysCannon's
        // GetRenderGroup answer RENDER_GROUP_TWOPASS whatever else is true.
        if (facts.TwoPass)
            return ClientRenderGroup.TwoPass;

        if (Transparent(facts))
            return ClientRenderGroup.TranslucentEntity;

        return ClientRenderGroup.OpaqueEntity;
    }

    /// <summary>
    /// What kind of shadow this entity casts. <c>shadow_cast_policy.h</c>'s rule,
    /// unchanged, in the language the rest of the policy is written in.
    ///
    /// <para>
    /// WHAT IT HAS TO AGREE WITH. Two stock rules, because the parallel
    /// hierarchy's one class stands in for both of stock's:
    /// <c>C_BaseEntity::ShadowCastType</c> is NONE unless the model is a studio
    /// model and RENDER_TO_TEXTURE when it is;
    /// <c>C_BaseAnimating::ShadowCastType</c> refines that — NONE without
    /// sequences, RENDER_TO_TEXTURE_DYNAMIC when the skeleton can move under its
    /// own power. <c>C_StandaloneEntity</c> draws studio models through the same
    /// studio path <c>C_BaseAnimating</c> does, so the animating rule is the one
    /// to match, and the base-entity rule is what falls out of it for a brush
    /// model, which has no studio header at all.
    /// </para>
    ///
    /// <para>
    /// WHY THE DISTINCTION IS INVISIBLE TODAY, AND KEPT ANYWAY.
    /// <c>r_shadowrendertotexture</c> defaults to 0, and
    /// <c>CClientShadowMgr::GetActualShadowCastType</c> collapses every
    /// render-to-texture flavour to SIMPLE while it is off — so a blob is what
    /// both a managed prop and a stock one actually get in this build. Answering
    /// SIMPLE here would photograph the same. It is answered properly regardless,
    /// because the collapse is a runtime convar's doing and not this rule's: a
    /// build that turns that convar on would otherwise find every managed prop
    /// silently downgraded to a blob beside stock props with real shadows.
    /// </para>
    /// </summary>
    public static ShadowCast Shadow(in ClientRenderFacts facts)
    {
        // Not drawn, not shadowed — BEFORE the override, because Never and
        // Simple are statements about a VISIBLE entity and neither is a licence
        // to cast from something that is not there. A dormant entity holds its
        // last known origin, so an override that outranked this would leave a
        // shadow standing where the entity was last seen.
        if (!Drawn(facts))
            return ShadowCast.None;

        // Same, and for the same reason it is not the managed side's to reverse:
        // the viewmodel is drawn at the eye with a viewmodel FOV, so its shadow
        // would be a pair of arms on the floor wherever the player looks.
        if (facts.ViewModel)
            return ShadowCast.None;

        // C_BaseEntity::ShadowCastType: EF_NOSHADOW casts nothing, as EF_NODRAW does.
        if (facts.NoShadow || facts.ShadowChoice == ShadowCastChoice.Never)
            return ShadowCast.None;

        // The cheap blob, for something the auto rule would refuse: an entity
        // that draws ITSELF has no studio header to be judged by and would
        // otherwise never cast at all.
        if (facts.ShadowChoice == ShadowCastChoice.Simple)
            return ShadowCast.Simple;

        // Auto from here down. A brush model is lit by the world and casts
        // nothing in stock either; a sprite has no geometry to cast with.
        if (!facts.IsStudioModel)
            return ShadowCast.None;

        // A studio model whose sequences have not been loaded is not a model
        // this can be decided from yet. Stock returns NONE and recomputes on the
        // next update, and so does this — the policy runs again on every change.
        if (facts.SequenceCount == ClientRenderFacts.SequencesUnavailable)
            return ShadowCast.None;

        // No sequences at all: nothing can move, so the shadow is baked once.
        if (facts.SequenceCount == 0)
            return ShadowCast.RenderToTexture;

        // A skeleton that can change shape without the entity moving. Stock's
        // list, unchanged: pose parameters, bone controllers, IK chains.
        // Ragdolls are stock's fourth exclusion and this hierarchy has none, so
        // there is nothing to exclude.
        if (facts.PoseParameterCount > 0 || facts.BoneControllerCount > 0 || facts.IkChainCount > 0)
            return ShadowCast.RenderToTextureDynamic;

        return ShadowCast.RenderToTexture;
    }

    /// <summary>
    /// The bits <c>CreateShadow</c> is given for a shadow of that kind —
    /// <c>C_BaseEntity::CreateShadow</c>'s two lines, here so they are decided
    /// beside the type that decides them. Getting either wrong is a shadow that
    /// exists and is the wrong KIND, which no error reports and only a photograph
    /// shows.
    ///
    /// <para>
    /// Written as a positive list rather than stock's "not SIMPLE", so a new enum
    /// value cannot fall into the wrong side of an inequality.
    /// </para>
    /// </summary>
    public static ClientShadowFlag ShadowFlags(ShadowCast shadow)
    {
        if (shadow == ShadowCast.None || shadow == ShadowCast.Unknown)
            return ClientShadowFlag.None;

        ClientShadowFlag flags = ClientShadowFlag.Shadow;

        if (shadow == ShadowCast.RenderToTexture || shadow == ShadowCast.RenderToTextureDynamic)
            flags |= ClientShadowFlag.UseRenderToTexture;

        if (shadow == ShadowCast.RenderToTextureDynamic)
            flags |= ClientShadowFlag.AnimatingSource;

        return flags;
    }

    /// <summary><c>kRenderNormal</c>.</summary>
    private const int RenderNormal = 0;

    // engine/ishadowmgr.h's projected-texture kinds.
    private const int ShadowFlagsFlashlight = 1 << 0;
    private const int ShadowFlagsShadow = 1 << 1;

    /// <summary><c>kRenderTransTexture</c>: what the policy reads for an entity a managed class blends.</summary>
    private const int RenderTransTexture = 2;

    /// <summary>
    /// Whether a projected texture of the kinds in <paramref name="flags"/>
    /// lands on the entity: stock's <c>C_BaseEntity::ShouldReceiveProjectedTextures</c>
    /// (c_baseentity.cpp, the fork's lines 1458-1481), ported for Q2 of
    /// docs/plans/c7_shadow_manager.md (user ruling 2026-10-19, "match stock").
    ///
    /// <para>
    /// Nothing that is not drawn takes one. A flashlight (the flashlight bit
    /// is tested first, so a mask of both kinds is asked as a flashlight)
    /// lights everything but an invisible blended entity. A shadow lands on
    /// anything but a studio model, unless EF_NORECEIVESHADOW says no:
    /// studio models take their shadows in the render-to-texture pass, not as
    /// projected decals. C_StandaloneEntity answers it from the block
    /// (<see cref="Receives"/>), since S2.
    /// </para>
    /// </summary>
    public static bool ReceivesProjectedTextures(in ClientRenderFacts facts, int flags)
    {
        if (facts.NoDraw)
            return false;

        if ((flags & ShadowFlagsFlashlight) != 0)
        {
            if (facts.RenderMode > RenderNormal && facts.RenderAlpha == 0)
                return false;
            return true;
        }

        if (facts.NoReceiveShadow)
            return false;

        if (facts.ModelType == ClientRenderFacts.Studio)
            return false;

        return true;
    }

    /// <summary>Every choice, from every fact. The whole of the policy in one call.</summary>
    public static ClientRenderChoices Decide(in ClientRenderFacts facts)
    {
        ShadowCast shadow = Shadow(facts);

        return new ClientRenderChoices
        {
            Drawn             = Drawn(facts),
            OwnerSeen         = OwnerSeen(facts),
            Transparent       = Transparent(facts),
            Group             = Group(facts),
            Shadow            = shadow,
            ShadowFlags       = ShadowFlags(shadow),
            Receives          = Receives(facts),
        };
    }

    /// <summary>
    /// <see cref="ReceivesProjectedTextures"/> for each kind, as the block
    /// carries them: what C_StandaloneEntity::ShouldReceiveProjectedTextures
    /// answers (Q2).
    /// </summary>
    public static ClientShadowFlag Receives(in ClientRenderFacts facts)
        => (ReceivesProjectedTextures(facts, ShadowFlagsShadow) ? ClientShadowFlag.ReceivesShadows : ClientShadowFlag.None)
         | (ReceivesProjectedTextures(facts, ShadowFlagsFlashlight) ? ClientShadowFlag.ReceivesFlashlight : ClientShadowFlag.None);

    /// <summary>The block's ShadowFlags word's receive answers.</summary>
    public const ClientShadowFlag ReceiveMask = ClientShadowFlag.ReceivesShadows | ClientShadowFlag.ReceivesFlashlight;

    //-------------------------------------------------------------------------
    // THE PASS.
    //-------------------------------------------------------------------------

    /// <summary>How many passes have run. The readout's denominator.</summary>
    public long Passes { get; private set; }

    /// <summary>
    /// How many blocks the last pass LOOKED at. Together with
    /// <see cref="Decisions"/> this is what makes "the policy is not recomputed
    /// per frame" a claim that can fail: a build whose serial stopped moving
    /// would show these two the same number.
    /// </summary>
    public int LastSeen { get; private set; }

    /// <summary>How many blocks the last pass actually DECIDED for.</summary>
    public int LastDecided { get; private set; }

    /// <summary>Every decision this level, across every pass.</summary>
    public long Decisions { get; private set; }

    /// <summary>Every block looked at this level, across every pass.</summary>
    public long Seen { get; private set; }

    /// <summary>
    /// Entry index → the handle whose input serial <see cref="_inputs"/> holds.
    /// Handles are recycled, so the serial in the handle is what says the entry
    /// belongs to the entity being looked at — a bare index would let one entity
    /// inherit the "already decided" mark of whatever held its slot before it.
    /// </summary>
    private readonly uint[] _handles = new uint[MaxEntities];

    /// <summary>Entry index → the input serial this side last decided for.</summary>
    private readonly uint[] _inputs = new uint[MaxEntities];

    /// <summary>
    /// The serial this side stamps into <c>PolicyOutputs</c>. It is this
    /// object's own counter rather than an increment of what it read, so a block
    /// whose memory was recycled cannot land on the value the shell already
    /// applied. Starts at 1 so that zero — what a fresh block holds — is never a
    /// serial anything wrote.
    /// </summary>
    private uint _outputSerial = 1;

    /// <summary><c>NUM_ENT_ENTRIES</c>: the engine's entity-slot count.</summary>
    private const int MaxEntities = 1 << 13;

    /// <summary>
    /// <c>INVALID_EHANDLE_INDEX</c>, and what every entry of
    /// <see cref="_handles"/> starts as.
    ///
    /// <para>
    /// A DEFAULT HANDLE IS ENTRY ZERO, WHICH IS A REAL SLOT. Zero is index 0
    /// with serial 0 — a handle an entity can genuinely wear — so a table left
    /// at its default would answer "already decided for" about entry 0 before
    /// anything had been decided, and whatever holds that slot would be drawn on
    /// a zeroed block until one of its own inputs moved. Closed by construction
    /// here rather than guarded at the comparison, which is 3b's allocator's
    /// pattern.
    /// </para>
    /// </summary>
    private const uint NoHandle = 0xFFFFFFFFu;

    // The side's studio headers, which the four model counts are read from.
    // A provider because the policy is made with the module and the
    // skeletons that own the headers are composed later; null until then.
    private readonly Func<IBoneModels?> _models;

    // The engine's local player index (engine->GetLocalPlayer()), asked once
    // by a pass that decides anything; 0 and below before there is one.
    private readonly Func<int> _localPlayer;

    public ClientRenderPolicy(Func<IBoneModels?> models)
        : this(models, () => 0)
    {
    }

    public ClientRenderPolicy(Func<IBoneModels?> models, Func<int> localPlayer)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _localPlayer = localPlayer ?? throw new ArgumentNullException(nameof(localPlayer));
        Forget();
    }

    private void Forget()
    {
        Array.Fill(_handles, NoHandle);
        Array.Clear(_inputs);
    }

    /// <summary>The handles whose classes asked for the two-pass group.</summary>
    private readonly HashSet<uint> _twoPass = [];

    /// <inheritdoc/>
    public void WantTwoPass(EntityHandle entity, bool twoPass)
    {
        bool changed = twoPass ? _twoPass.Add(entity.Value) : _twoPass.Remove(entity.Value);
        if (!changed)
            return;

        // Decided again on the next pass: the wish is an input the block's
        // serial cannot see, so the entry's "already decided" mark goes.
        int index = (int)(entity.Value & EntryMask);
        if ((uint)index < MaxEntities)
            _handles[index] = NoHandle;
    }

    /// <summary>The entry-index bits of a <c>CBaseHandle</c>.</summary>
    private const uint EntryMask = (1u << 12) - 1u;

    /// <summary>
    /// <see cref="ClientFrameStage.NetUpdatePostDataUpdateEnd"/>: every entity
    /// whose inputs moved since the last pass gets its policy decided and
    /// written into its block.
    ///
    /// <para>
    /// <b>The stage is the one the data updates just finished on</b>, so a
    /// policy input a class set from <c>OnDataChanged</c> is decided in the same
    /// frame it was set, before the shell's apply sweep at <c>PreRender</c> and
    /// long before anything draws.
    /// </para>
    ///
    /// <para>
    /// <b>The serial test is what keeps this off the frame's critical path.</b>
    /// §3.9 says render policy is written when its inputs change, not per frame.
    /// The pass walks every block every frame because that is how a list is
    /// walked, but an entity whose inputs have not moved costs one load and one
    /// compare — no arithmetic, no write, and no <c>StateChanged</c>.
    /// </para>
    /// </summary>
    /// <returns>How many entities were decided for.</returns>
    public int Pass(IRenderPolicyBlocks blocks)
    {
        Passes++;

        int count = blocks.Begin();
        LastSeen = 0;
        LastDecided = 0;

        if (count <= 0)
            return 0;

        int decided = 0;
        int? localPlayer = null;

        for (int i = 0; i < count; i++)
        {
            Span<byte> block = blocks.Block(i);

            // THE BLOCK, NOT THE NETWORKED PAYLOAD. A span shorter than the
            // block is what a payload-length view of it looks like, and every
            // field below lives past the end of every tier -- so a short span
            // would make this loop write nothing and say nothing about it.
            if (block.Length < EngineBlock.SizeBytes)
                continue;

            LastSeen++;

            uint handle = ReadUInt(block, HandleOffset);
            int index = (int)(handle & EntryMask);
            if ((uint)index >= MaxEntities)
                continue;

            uint inputs = ReadUInt(block, InputsOffset);

            // Nothing has moved for this entity since it was last decided for.
            // The handle is half the test because slots are recycled: the entity
            // now in this slot is a different entity from the one whose serial
            // was recorded, whatever the serial says.
            //
            // AND A BLOCK NOTHING HAS WRITTEN IS NEVER "ALREADY DECIDED". A full
            // update re-creates every entity on the handle it already had, with a
            // zeroed block whose input serial starts from the same value, so both
            // halves above match an entity that no longer exists. Its output
            // serial is still 0, which this side never writes (it starts at 1
            // and skips 0 on wrap). Missing this left RenderGroup 0, a static-prop
            // bucket, and the engine's DrawStaticProps segfaulted on the shell.
            if (_handles[index] == handle && _inputs[index] == inputs && ReadUInt(block, OutputsOffset) != 0)
                continue;

            localPlayer ??= _localPlayer();
            ClientRenderFacts facts = ReadFacts(block, _models(), localPlayer.Value);
            facts.TwoPass = _twoPass.Contains(handle);
            ClientRenderChoices choices = Decide(facts);
            WriteChoices(block, choices);

            _handles[index] = handle;
            _inputs[index] = inputs;
            decided++;
        }

        LastDecided = decided;
        Decisions += decided;
        Seen += LastSeen;
        return decided;
    }

    /// <summary>
    /// Every entry forgotten. Called at level init for the reason 6a's rings are:
    /// handles start again from the beginning on every map, so an entity on this
    /// map would otherwise inherit the "already decided" mark of whatever held
    /// its slot on the last one and keep the render policy of a different thing.
    /// </summary>
    public void Clear()
    {
        Forget();
        _twoPass.Clear();
        LastSeen = 0;
        LastDecided = 0;
    }

    //-------------------------------------------------------------------------
    // The block, as bytes. SourceSharp.Client is compiled WITHOUT /unsafe, so
    // the block arrives as a bounds-checked span and every read and write here
    // is one.
    //-------------------------------------------------------------------------

    private static int Head(string field)
        => (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Head))
         + (int)Marshal.OffsetOf<EngineBlockHead>(field);

    private static int Tail(string field)
        => (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Client))
         + (int)Marshal.OffsetOf<EngineBlockClientTail>(field);

    private static readonly int HandleOffset = Head(nameof(EngineBlockHead.Handle));
    private static readonly int ModelIndexOffset = Head(nameof(EngineBlockHead.ModelIndex));
    private static readonly int InputsOffset = Tail(nameof(EngineBlockClientTail.PolicyInputs));
    private static readonly int OutputsOffset = Tail(nameof(EngineBlockClientTail.PolicyOutputs));

    /// <summary>
    /// The EF_* word. A HEAD offset, not a tail one: <c>m_fEffects</c> is
    /// networked into <see cref="EngineBlockHead.Effects"/> by the engine's own
    /// decoder, which is why <c>C_StandaloneEntity::PostDataUpdate</c> has to
    /// compare it against <c>m_nPublishedEffects</c> to notice it moved.
    /// </summary>
    private static readonly int EffectsOffset = Head(nameof(EngineBlockHead.Effects));

    private static readonly int DormantOffset = Tail(nameof(EngineBlockClientTail.Dormant));
    private static readonly int ViewModelOffset = Tail(nameof(EngineBlockClientTail.ViewModel));
    private static readonly int CustomDrawOffset = Tail(nameof(EngineBlockClientTail.CustomDraw));
    private static readonly int TranslucentOffset = Tail(nameof(EngineBlockClientTail.Translucent));
    private static readonly int FadeStyleOffset = Tail(nameof(EngineBlockClientTail.FadeStyle));
    private static readonly int ShadowChoiceOffset = Tail(nameof(EngineBlockClientTail.ShadowChoice));
    private static readonly int ModelTypeOffset = Tail(nameof(EngineBlockClientTail.ModelType));
    private static readonly int RenderAlphaOffset = Tail(nameof(EngineBlockClientTail.RenderColorA));

    private static readonly int DrawnOffset = Tail(nameof(EngineBlockClientTail.Drawn));
    private static readonly int OwnerSeenOffset = Tail(nameof(EngineBlockClientTail.OwnerSeenRule));
    private static readonly int TransparentOffset = Tail(nameof(EngineBlockClientTail.Transparent));
    private static readonly int RenderGroupOffset = Tail(nameof(EngineBlockClientTail.RenderGroup));
    private static readonly int ShadowTypeOffset = Tail(nameof(EngineBlockClientTail.ShadowType));
    private static readonly int ShadowFlagsOffset = Tail(nameof(EngineBlockClientTail.ShadowFlags));

    /// <summary>
    /// One entity's facts: out of its block, and the four model counts out of
    /// <paramref name="models"/>' header for the block's model index.
    /// </summary>
    public static ClientRenderFacts ReadFacts(ReadOnlySpan<byte> block, IBoneModels? models)
        => ReadFacts(block, models, 0);

    /// <summary>
    /// The facts, with the entity the engine names as its local player
    /// (<paramref name="localPlayer"/>, 0 and below for none) marked as the
    /// viewer's own body.
    /// </summary>
    public static ClientRenderFacts ReadFacts(ReadOnlySpan<byte> block, IBoneModels? models, int localPlayer)
    {
        ClientRenderFacts facts = ReadBlockFacts(block);
        facts.LocalPlayer = localPlayer > 0 && (int)(ReadUInt(block, HandleOffset) & EntryMask) == localPlayer;
        ReadModelCounts(ref facts, ReadInt(block, ModelIndexOffset), models);
        return facts;
    }

    /// <summary>
    /// The counts the shadow rule grades a studio model by, as the shell's
    /// PublishModelFacts read them through the native CStudioHdr before
    /// studio.cpp left: a studio model whose header opens answers its
    /// sequence, pose-parameter, bone-controller and IK-chain counts (each over
    /// the folded virtual model); anything else answers
    /// <see cref="ClientRenderFacts.SequencesUnavailable"/> and zeros.
    /// </summary>
    public static void ReadModelCounts(ref ClientRenderFacts facts, int modelIndex, IBoneModels? models)
    {
        facts.SequenceCount = ClientRenderFacts.SequencesUnavailable;
        facts.PoseParameterCount = 0;
        facts.BoneControllerCount = 0;
        facts.IkChainCount = 0;

        if (facts.ModelType != ClientRenderFacts.Studio || models is null || modelIndex <= 0)
            return;

        if (models.OpenStudioHdr(modelIndex) is not { } hdr)
            return;

        facts.SequenceCount = hdr.NumSequences;
        facts.PoseParameterCount = hdr.NumPoseParameters;
        facts.BoneControllerCount = hdr.NumBoneControllers;
        facts.IkChainCount = hdr.NumIKChains;
    }

    private static ClientRenderFacts ReadBlockFacts(ReadOnlySpan<byte> block) => new()
    {
        Dormant             = ReadInt(block, DormantOffset) != 0,
        NoDraw              = (ReadInt(block, EffectsOffset) & (int)EntityEffect.NoDraw) != 0,
        NoShadow            = (ReadInt(block, EffectsOffset) & (int)EntityEffect.NoShadow) != 0,
        NoReceiveShadow     = (ReadInt(block, EffectsOffset) & (int)EntityEffect.NoReceiveShadow) != 0,
        RenderAlpha         = block[RenderAlphaOffset],
        // The render mode: the block carries no RenderMode_t, and an entity's
        // only blending is what its managed class asked for (SetBlending, the
        // shell's Translucent): a blended entity's mode is past kRenderNormal.
        RenderMode          = ReadInt(block, TranslucentOffset) != 0 ? RenderTransTexture : RenderNormal,
        ViewModel           = ReadInt(block, ViewModelOffset) != 0,
        CustomDraw          = ReadInt(block, CustomDrawOffset) != 0,
        Translucent         = ReadInt(block, TranslucentOffset) != 0,
        Fade                = (FadeStyle)ReadInt(block, FadeStyleOffset),
        ShadowChoice        = (ShadowCastChoice)ReadInt(block, ShadowChoiceOffset),
        ModelType           = ReadInt(block, ModelTypeOffset),
    };

    /// <summary>One entity's choices, into its block, with the output serial last.</summary>
    private void WriteChoices(Span<byte> block, in ClientRenderChoices choices)
    {
        WriteInt(block, DrawnOffset, choices.Drawn ? 1 : 0);
        WriteInt(block, OwnerSeenOffset, (int)choices.OwnerSeen);
        WriteInt(block, TransparentOffset, choices.Transparent ? 1 : 0);
        WriteInt(block, RenderGroupOffset, (int)choices.Group);
        WriteInt(block, ShadowTypeOffset, (int)choices.Shadow);
        WriteInt(block, ShadowFlagsOffset, (int)(choices.ShadowFlags | (choices.Receives & ReceiveMask)));

        // LAST, and that ordering is the whole of the handshake. The shell
        // applies an entity's policy when this serial differs from the one it
        // last applied, so a serial written before the values it describes would
        // let a sweep between the two apply the PREVIOUS frame's choices and
        // then believe it had applied these.
        WriteUInt(block, OutputsOffset, _outputSerial++);

        // Zero means "never decided" to the pass above, so the counter skips it.
        if (_outputSerial == 0)
            _outputSerial = 1;
    }

    /// <summary>What the shell reads back, for a test that wants the written value.</summary>
    public static ClientRenderChoices ReadChoices(ReadOnlySpan<byte> block) => new()
    {
        Drawn             = ReadInt(block, DrawnOffset) != 0,
        OwnerSeen         = (ClientOwnerSeenRule)ReadInt(block, OwnerSeenOffset),
        Transparent       = ReadInt(block, TransparentOffset) != 0,
        Group             = (ClientRenderGroup)ReadInt(block, RenderGroupOffset),
        Shadow            = (ShadowCast)ReadInt(block, ShadowTypeOffset),
        ShadowFlags       = (ClientShadowFlag)ReadInt(block, ShadowFlagsOffset) & ~ReceiveMask,
        Receives          = (ClientShadowFlag)ReadInt(block, ShadowFlagsOffset) & ReceiveMask,
    };

    private static int ReadInt(ReadOnlySpan<byte> block, int offset)
        => BitConverter.ToInt32(block.Slice(offset, sizeof(int)));

    private static uint ReadUInt(ReadOnlySpan<byte> block, int offset)
        => BitConverter.ToUInt32(block.Slice(offset, sizeof(uint)));

    private static void WriteInt(Span<byte> block, int offset, int value)
        => BitConverter.TryWriteBytes(block.Slice(offset, sizeof(int)), value);

    private static void WriteUInt(Span<byte> block, int offset, uint value)
        => BitConverter.TryWriteBytes(block.Slice(offset, sizeof(uint)), value);
}

/// <summary>
/// WHAT THE RENDER-POLICY PASS ACTUALLY DID, off a live client.
///
/// <para>
/// <b>It exists because the step's own claim is otherwise untestable.</b> 6b's
/// claim is that the render policy costs no inbound crossing and is not
/// recomputed per frame — and a build that quietly stopped deciding anything
/// would satisfy both perfectly. The crossing counters would read the same, and
/// "not recomputed per frame" would be true because nothing was computed at
/// all. 1g hit this shape and added <c>status.think</c> for it; 6a added
/// <c>cs_cl_interp</c>; this is the third.
/// </para>
///
/// <para>
/// <b>Read <c>seen</c> and <c>decided</c> together.</b> <c>seen</c> climbing
/// with <c>decided</c> flat is the design working: every entity is looked at,
/// almost none needs deciding. <c>decided</c> at zero from the first frame is
/// the failure this command exists to show — no entity ever had a policy
/// written, so every renderable is drawing on whatever a zeroed block said.
/// </para>
///
/// <para>
/// A console command rather than a <c>debug-api</c> status key, for
/// <c>cs_cl_interp</c>'s reason: the debug-api server is the server's, and this is
/// client state.
/// </para>
/// </summary>
[ConCommand("cs_cl_policy",
            Help = "What the client render-policy pass did: passes, blocks seen, " +
                   "policies decided, and the last pass's split.")]
public sealed class RenderPolicyReportCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly ClientRenderPolicy _policy;

    /// <summary>The module's render policy, from its container.</summary>
    public RenderPolicyReportCommand(ILog log, ClientRenderPolicy policy)
    {
        _log = log;
        _policy = policy;
    }

    public void Execute(string args)
    {
        ClientRenderPolicy policy = _policy;

        _log.Msg($"[SourceSharp/Client] policy: {policy.Passes} pass(es), "
              + $"{policy.Seen} block(s) seen, {policy.Decisions} decision(s)\n");

        _log.Msg($"[SourceSharp/Client] policy: last pass saw {policy.LastSeen} and "
              + $"decided {policy.LastDecided}\n");

        // THE READING, SAID OUT LOUD, because the number that matters is a
        // RATIO and a reader looking at two large counts cannot see which way
        // it went.
        if (policy.Seen == 0)
        {
            _log.Msg("[SourceSharp/Client] policy: nothing seen at all — either no standalone "
                  + "client entities exist, or ClientPolicyBlocks is not filled\n");
        }
        else if (policy.Decisions == 0)
        {
            _log.Msg("[SourceSharp/Client] policy: nothing DECIDED — every renderable is drawing "
                  + "on a zeroed block, which is not drawing\n");
        }
    }
}

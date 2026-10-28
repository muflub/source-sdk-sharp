extern alias client;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Part 4b.3's Audits 1 and 2: slot side-ness derived from the fill block's
/// #ifdef positions, and the rule that an assembly may not lean on a slot its
/// own side leaves null.
///
/// <para>
/// The predecessor could not fail this way: AbiParityTests parses the same
/// fill block into a flat set, so a slot wired only under #ifdef CLIENT_DLL
/// satisfied "every slot is wired" while being null on the server — which is
/// exactly the StandaloneSetBlending and EntityGetModelName bugs. Every rule
/// here is a pure function a test can provoke, and the provocations below do.
/// </para>
/// </summary>
public class SideAuditTests
{
    //-------------------------------------------------------------------------
    // Audit 1 — the partition, pinned by name where one-sidedness is a hazard.
    //
    // The server-only set is deliberately NOT pinned: appending an ordinary
    // server slot is the common, benign change and stays green. What goes red
    // by name: a slot arriving client-only, a slot moving between regions, a
    // dual implementation appearing or disappearing. A server-only slot that
    // the CLIENT needs is Audit 2's catch, from the caller's side.
    //-------------------------------------------------------------------------

    private static readonly HashSet<string> ClientOnly = new(StringComparer.Ordinal)
    {
        // lane 1e-abi. Every client shell re-evaluates whether the leaf system
        // should be showing it, answering how many did.
        //
        // CLIENT-ONLY BECAUSE THE QUESTION IS, not because the server's answer
        // is hard. Visibility on the server is the PVS and the transmit rules —
        // a different mechanism answering a different question — so a server
        // arm here would be a slot that means one thing on one side and
        // something else on the other, which is the shape DualWired exists to
        // mark and this is not an instance of it.
        //
        // It exists so that a rendering toggle whose whole effect is its CHANGE
        // CALLBACK can be a managed [ConVar]. `vcollide_wireframe` is the
        // first: it draws nothing by itself, and lane 1c-cl could not move its
        // declaration out of C++ for want of exactly this.
        "EntitiesUpdateVisibilityAll",

        // plan_client_novalve C2 (lane c2-sims): the draw executor for the
        // client's managed particle sims. Simple particles are the client's
        // drawing; the server has no particle manager.
        "ParticlesCreate", "ParticlesConfigure", "ParticlesDestroy",
        "WindPublish",
        "WorldTextMeshDestroy", "WorldTextMeshUpdate",
        "ParticlesTraceLine",
        "ParticlesSetLights",
        // lane c5-particles: the particles shell's started systems and the
        // definition reads, for /client/{slot}/particles. The client's alone:
        // the server starts no CNewParticleEffect.
        "ParticleSystemsRead", "ParticleDefinitionReads", "ParticleQueryUnanswered", "ParticlesMaterialVarInt", "ParticlesCreateAt", "ParticleSystemNameFromIndex", "ParticleSystemOneShot", "ParticlesWorldTrace",
        "ParticlesView",
        "ParticlesTraceMask",
        "ParticlesSetSpace",
        "ParticlesBrushPointContents",

        // The user-message payload model: what the stock hooks did, over values
        // C# parsed. The view effects, the viewport and the voice status are
        // the client's own state; the server has none of them. (Rumble's slot
        // left with c_rumble.cpp: the mixer is managed, lane c6-input.)
        "ClientViewportQuery",
        "ClientViewportShow",

        // THE VTABLE BINDING'S FOUR PROXIES. Each exists because a fixed
        // unmanaged signature cannot spell the call at all -- Con_NPrintf and
        // Con_NXPrintf are varargs, GetLightForPoint and its Fast twin return
        // Vector by value -- and all four are IVEngineClient013's, so the
        // server has no object to call them on.
        //
        // NOT MOVABLE TO BothSides, and the reason is not symmetry. The server
        // declares its OWN Con_NPrintf and Con_NXPrintf on IVEngineServer and
        // they get no proxy on purpose: they are refused for a second reason as
        // well, ClientCommand's -- "it runs a command on a client". A proxy for
        // a call nothing may make is an ABI slot with no caller.
        "ConNPrintf", "ConNXPrintf", "GetLightForPoint", "GetLightForPointFast",

        // plan_entity.md step 6a. The engine blocks of every entity the client
        // interpolates. Client-only because the LIST is: there is no such thing
        // on a server, which is why the managed side answers an empty list
        // there rather than a printed refusal -- "no client entities" is the
        // true answer on that side, not a declined capability.
        "ClientInterpBlocks",

        // plan_entity.md step 6b. The engine blocks of EVERY standalone client
        // entity, for the render-policy pass. Client-only for the same reason,
        // and one more: a render policy is a decision about what the client
        // DRAWS, and a server has nothing to draw it for.
        "ClientPolicyBlocks",

        // plan_client_novalve §4.4, the nettables route: the client's recv
        // tables as the engine walks them, and the payload decoder's schema
        // refusals. Client-only: recv tables and the decoder exist only there.
        "ClientRecvProp",
        "ClientSchemaRefusal",

        // plan physics_bones_native_zero 1.2, client half: the managed
        // skeleton reads a client standalone's engine block by handle, and
        // hands native's per-frame cycle the clock it chose. A client
        // standalone and its cycle exist only in client.so.
        "StandaloneEngineBlock",
        "StandaloneStoreSequence",

        // plan physics_bones_native_zero Phase 3: managed's ragdolls tell the
        // client shell the solver owns an entity's pose. A client standalone
        // and its render tail exist only in client.so.
        "StandaloneSetRagdoll",
        // The skin, client-only since plan_server_native_v2 T2: the server's
        // arm was an empty body (see DualWired's note where it was listed).
        "EntitySetSkin",
        // The ammo table, client-only since the same plan: only the client
        // registers (ClientModule), and server.so keeps no table.
        "RegisterAmmoType",

        // The same plan's netgraph and absences routes: the client's net
        // channel (a client has the one to its server) and its diagnostics
        // ledgers, which exist only in client.so.
        "ClientNetFlow",
        "ClientNetChannel",
        "ClientNetPacket",
        "ClientNotShipped",
        "ClientRefusal",

        // plan_client_novalve §2 cluster 1 (lane c5-proxies): what the managed
        // material proxies read through the engine, by the trampoline's handle.
        // Material proxies exist only in client.so.
        "ProxyFindVar",
        "ProxyFindTexture",
        "ProxyVarTexture",
        "ProxyBindEntity",
        "ProxyReadView",
        "ProxyPlayerCustomFile",
        "ProxyNotShipped",

        // plan_client_novalve §2 cluster 4: the client's effect registrations
        // (DECLARE_CLIENT_EFFECT), read and run by walk index for the managed
        // dispatch table. Client effects exist only in client.so.
        "EffectRegistrationCount",
        "EffectRegistrationName",
        "EffectRegistrationRun",

        // And IEffects' carry-out (cluster 3): the FX_* routines the managed
        // ClientEffects decision ends in, and SuppressTE's two answers. (The
        // spark routines are C# since lane c5-particles.)
        "FxCanPredict",
        "FxRecipientCount",

        // And the client-side effect list's carry-out (cluster 3): materials by
        // id, the quad draw, the view reads, the near-miss, the tracers' asks.
        "FxMaterialFind",
        "FxMaterialAddRef",
        "FxMaterialRelease",
        "FxMaterialName",
        "FxDrawQuads",
        "FxView",
        "FxMainViewOrigin",
        "FxLocalPlayer",
        "FxViewAngles",
        "FxEntityAttachment",
        "FxDispatchParticleEffect",
        "FxEntityRenderAngles",
        "FxEjectBrass",
        "FxSpriteDrawBegin", "FxSpriteDrawEnd", "FxSpriteDrawStore",
        "FxPrecacheCount",
        "FxPrecacheCache",
        "FxCacheUsedMaterials",
        "FxCacheMaterialHandles",
        "FxRenderableAttachment",
        "FxHandleEntityIndex",
        "FxElight",
        "FxVectorRandom",
        "FxMaterialsCached",
        // (FxImpactOverlay left with the glow overlays' port, C7.)
        "FxTempEntCreateNamed",
        "FxLoadModel", "FxModelFrameCount",

        // Sound cluster 17 (lane c2-sims): the client soundscape system's
        // executors (IShimSoundscapeClient), for the managed ClientSoundscapes.
        "SoundscapeSetRoomType", "SoundscapeSetPlayerDsp", "SoundscapeMainView",
        "SoundscapeMakingResLists", "SoundscapeTouchSoundFile",

        // The client weather's engine reads (IShimPrecipitationClient, lane c2-sims).
        "PrecipitationTraceLine", "PrecipitationTraceVolume", "PrecipitationPointContents",
        "PrecipitationInPvs", "PrecipitationPlayer", "PrecipitationWorldBounds", "PrecipitationScreenWidth",

        // plan_entity.md step 8e-render. What fog the viewer is standing in.
        // Client-only in the same shape as PlayerRunMove is server-only: the
        // fog is DECIDED by the master env_fog_controller on the server, and
        // the server has no renderer to hand it to. The push exists because
        // world fog did not render on this client at all -- fogcontroller.cpp
        // is entirely `#ifndef SOURCESHARP`, so server.so declares no
        // DT_FogController and there was no sender for it.
        "SetViewerFogParams",

        // C7 item 5: the observer's camera traces and reads drawn poses on the
        // client, the only side that has a camera.
        "ClientTraceHull", "ClientEntityRenderPose", "ViewOverrideSet",

        // The predicting client's shot, clipped against the shells: only the
        // client predicts a shot, and only its shells are outside a partition.
        "ClientClipRayToEntities",

        // The defect-skybox lane. What 3D skybox the viewer is shown, and the
        // OTHER END of SetSkyboxParams -- which is server-only for the mirror
        // reason. One map's sky camera makes one round trip: sky_camera pushes
        // its block on the server, playerlocaldata.cpp reads it back there, the
        // values ride in PlayerNetState, and this slot is where they land on the
        // client. A server has no renderer to hand them to, so this side of the
        // pair is a side with no such thing rather than a declined capability.
        "SetViewerSkyboxParams",
        // Lane camproto: the second camera's executor. A view drawn into a render
        // target, and the target's facts for the HUD panel that shows it: the
        // server has no renderer and no HUD.
        "RenderViewToTarget",
        "ViewTargetInfo",
        // plan_entity.md step 6f. The managed client replay's three asks: the
        // engine's input ring, the movement bracket (which arms step 3e's four
        // trace slots for one command), and the published view.
        //
        // CLIENT-ONLY BECAUSE A REPLAY IS. The server runs each command once
        // and its answer is the authoritative one, so there is nothing to
        // replay, no input ring to replay it from, and no IPrediction to be
        // asked. That is a side with no such thing rather than a declined
        // capability, which is why these are here and not printed refusals --
        // the same distinction ClientInterpBlocks above is making.
        //
        // The four MoveTrace slots they arm are NOT here and must not be: those
        // are the movement loop's world, which BOTH sides run. Binding those
        // four one-sided is the defect step 3e measured -- the client's loop got
        // a NULL MoveTrace, every trace answered "nothing in the way", and the
        // player fell through the floor.
        "ClientGetUserCmd", "MoveBeginClientCommand", "MoveEndClientCommand",
        "ClientPlayerPredictionDrive",

        // plan_entity.md step 8e-sound. Where a soundscape push LANDS: the body
        // is Soundscape_Update() in shell_soundscape_client.cpp.
        //
        // CLIENT-ONLY BECAUSE HEARING IS. The server half of a soundscape is
        // Soundscapes.cs deciding which one a player is in -- range, line of
        // sight, nearest, trigger volume -- and all of that is managed code that
        // crosses nothing. What travels is the ANSWER, and it travels as the
        // `Soundscape` user message rather than through an engine slot, which is
        // why there is no server counterpart to declare here. The server's old
        // PlayerSetSoundscape slot is dead and named in
        // .claude/entity-port/abi-requests/8e-sound.md for removal.

        "ActivateOccluder", "ClientSetWorldRenderState",
        "DrawSprite", "DrawSpriteOffset",
        // The flame: ss_plasma draws its seven sprites through the offset entry
        // and ss_firesmoke owns a particle system between frames. Both are
        // drawing, so both are this side only. ss_entityflame's follow mask is
        // the same thing said about control points -- which point of a running
        // system is fed is a fact about the renderer's copy of it, and the
        // server has no copy.
        "ParticleSystemStart", "ParticleSystemStop", "ParticleSystemFollowPoints",
        "DynamicLightRelease", "DynamicLightUpdate",
        // The five local-weapon slots -- GetLocalWeaponCount, GetLocalWeaponName,
        // GetLocalActiveWeapon, GetLocalWeaponClip and GetLocalWeaponReserve --
        // were here and are REMOVED at ABI 249. They read the retired
        // C_BasePlayer inventory for the selection HUD, which reads the managed
        // Arsenal now. This list names slots that still exist; a name left
        // behind by a removed slot reads as a live client-only crossing.
        "GetLocalPlayerHealth", "GetLocalPlayerIndex",
        "GetLocalPlayerOrigin",
        "HudLocalise", "HudLookupBinding", "HudTextMessageGet",
        // The client's own net channel to its server: a server has no server.
        "SendServerMessage",
        // The rope executor (shell_rope_render.cpp) and the view, light and
        // world-hull reads a managed rope makes: drawing, so this side only.
        "RopeMaterial", "RopeDraw", "ClientViewState", "ClientComputeLighting", "ClientTraceHullWorld",
        // The beam executor (shell_beam_render.cpp) and the reads the managed
        // beam system makes: drawing, and the client's view of the world.
        "BeamView", "BeamModelInfo", "BeamModelIndex", "BeamEntityAt", "BeamEntityHasModel",
        "BeamEntityFacts", "BeamBoxVisibility", "BeamScreenPosition",
        "BeamRenderableAdd", "BeamRenderableChanged", "BeamRenderableRemove", "BeamBindSprite",
        "BeamSetHdrColorScale", "BeamGlow", "BeamDrawStrip", "BeamDrawMesh",
        // The UI's draw primitives (shell_ui.cpp, plan_client_ui U2): vgui's
        // surface is the client's only.
        "UiDrawFilledRect", "UiDrawFilledRectFade", "UiDrawOutlinedRect", "UiDrawLine",
        "UiDrawTexturedRect", "UiDrawPolygon", "UiDrawText", "UiMeasureText", "UiSetClip",
        "UiTextureCreate", "UiTextureUpload", "UiFontAddFile", "UiFontCreate", "UiFontMetrics",
        "UiFontAdvances", "UiPlaySound", "UiSetMenuInput", "UiSetCursor", "UiDebugOverlayText", "UiDebugOverlayClearDead",
        // The managed input's native asks (cluster 13, lane c6-input): vgui's
        // cursor, the HUD, the view, prediction and the viewport are the client's.
        "InputCursorState", "InputGetCursorPos", "InputSetCursorPos", "InputSetCursorArrow",
        "InputVguiScreenSize", "InputHudFacts", "InputStopPitchDrift", "InputSetLocalViewAngles",
        "InputViewerCameraModeChanged", "InputViewerAimIsAssisted", "InputShowScoreboard",
        // The client physics callbacks' native asks (C6 cluster 9, lane c6-input):
        // the client's own environment, handler, surfaceprops and sound emit.
        "PhysicsClientSurfaceAudio",
        "PhysicsClientCollideExtent", "PhysicsClientPointContents",
        "PhysicsClientSplashLighting", "PhysicsClientSplashEffect",
        // The convar watch and the decided values (C8, lane c6-input): the
        // client's bounded convars and its interpolation amount.
        "ConVarsClientWatch", "ConVarsClientSetValue",
        // The flashlight's executor (C7 cluster 12): the client shadow manager
        // and its texture, which exist only in client.so.
        "FlashlightTexture", "FlashlightSupportsBorderColor",
        // The named render targets' store (C7, rendertexture.cpp's getters):
        // the material system's render targets exist only in client.so.
        "RenderTargetDeclare",
        // The client mode's layout (C7, the viewport system's Init): the
        // client mode exists only in client.so.
        "ClientModeLayout",
        // The pixel visibility's slots (C7, c_pixel_visibility.cpp): the
        // render context's occlusion queries and the current view are
        // client.so's.
        "PixelVisQueryCreate", "PixelVisQueryDestroy", "PixelVisQueryReset", "PixelVisQueryPixels",
        "PixelVisDiameters", "PixelVisView",
        // The shells' surrounding boxes for the per-view fade (lane M1-A):
        // client.so's standalone shells.
        "StandaloneSurroundingBounds",
        // A broken surface's draw (lane M1-A): client.so's surface renderer.
        "SurfaceMaterialFacts", "SurfaceSetMaterials", "SurfaceSetPlan",
        // The colour-correction lookups on client.so's render context (lane M1-A).
        "ColorLookupAdd", "ColorLookupSetWeight", "ColorLookupRemove", "ColorLookupResetWeights",
        // The interpolation pass's admitted set, for client.so's PreRender walk (lane M1-A).
        "ClientInterpAdmitted",
        // The glow overlays' material find (C7, glow_overlay.cpp): client.so's materials.
        "GlowMaterialFind",
        // The managed bullet impact's engine calls (lane M1-C, fx_impact.cpp's
        // chain): the client trace, its lighting reads, the client's effect
        // ledger and its particle systems.
        "ClientShotTrace", "ClientWorldLighting", "ClientStaticPropLighting", "ClientImpactLedger",
        "ParticleSystemImpact",
        // A player's spray, carried out (lane M1-C, c_te_playerdecal.cpp).
        "ClientPlayerLogoDecal",
        "InputMailbox",
        "SpriteTrailDestroy", "SpriteTrailUpdate",
        "StandaloneSetBlending",
        // A shadow is a thing the RENDERER does, so the server has no version
        // of either of these. Phase 5's gate: the shell answered
        // SHADOWS_SIMPLE from ShadowCastType() while GetShadowHandle() was
        // hard-coded invalid, so the client shadow manager held nothing and
        // projected nothing. Moving these to BothSides would be a claim the
        // server has a shadow to report, which it does not.
        "StandaloneShadowCast",
        "StandaloneSetCustomDraw", "StandaloneSetFade",
        "StandaloneSetOverlayModel", "StandaloneSetViewModel",
        // (Phase 5's pose parameters, the animation clock's two reads, the
        // ground speed, the procedural counts and the layer table left with
        // their native bodies when the client's skeleton became managed:
        // plan physics_bones_native_zero 1.2, client half.)
        // Dormancy is a CLIENT concept: it means the server has stopped
        // sending this entity, which the server itself has no version of.
        "StandaloneIsDormant",
        // The temp entity backends (plan_tmp.md Part 4). Client-only because
        // they are DRAWING: a ported c_te_*.cpp moves its decode and its
        // decision to C# and calls one of these at the end, which is the same
        // division ClientSprite, ClientBeam and ClientTrails already use.
        // Moving one to BothSides would be a claim that the server draws
        // effects, which it does not -- it sends a table and stops. C-Phase 2
        // adds ~16 more beside this one.
        "ClientEnergySplash",
        // C-Phase 2's fifteen (less the six beams, managed now), declared here rather than worked around. Same
        // reason as the one above: a beam, a spark, a decal and a dispatched
        // effect are all DRAWING, and the server draws nothing -- it sends a
        // table and stops. ClientDecalIndexFromName is here too even though it
        // answers rather than draws: it asks the CLIENT's decal table, which
        // the server does not have.
        "ClientSparks", "ClientRicochet", "ClientMetalSparks",
        "ClientSmoke", "ClientMuzzleFlash",
        "ClientDecalShoot", "ClientPlayerDecalShoot", "ClientDecalIndexFromName",
        // C-Phase 3 lane CP's eight, for the DT_TEParticleSystem subtree.
        // Client-only for the reason above them: an explosion, a funnel, a
        // blood stream, a line of particles, a dust puff and a gauss discharge
        // are all DRAWING, and the server draws nothing. Eight rather than
        // Part 4's pencilled ONE ClientParticleTeSpawn(kind, params), because
        // the seven TEs on that base share a TABLE and not a backend --
        // TempEntBackendTests carries the whole argument.
        // (ClientExplosionFireball, their last, left with the glow overlays' port, C7.)
        // C-Phase 3 lane C4's six, for the LEGACY TEMP ENTITY POOL. Client-only
        // for a sharper reason than the ones above them: the pool IS a client
        // structure -- c_te_legacytempents.cpp's 500 C_LocalTempEntity, which
        // are drawables. The server has no pool of gibs to allocate from and
        // never had one; it sends a table and stops. Four of these six do not
        // draw at all (they allocate, free, read and write a lifetime), which
        // is why the "they are DRAWING" argument above does not cover them and
        // this note exists instead.
        "TempEntPoolCapacity", "TempEntPoolDestroy",
        "TempEntPoolReadLife", "TempEntPoolWriteLife",
        "TempEntPoolAddVisible",
        "TempEntPoolReadTransform", "TempEntPoolWriteTransform", "ClientEntityOrigin",
        // C-Phase 6 lane FRAME's two, and client-only for the six above them's
        // reason: TempEntPoolReadStatics reaches the same pool of drawables,
        // and ClientWindSpeed is GetWindspeedAtTime, which is what
        // FTENT_WINDBLOWN accelerates a CLIENT-side tempent towards. The
        // server's own wind is a different question asked of a different table.
        "TempEntPoolReadStatics", "ClientWindSpeed",
        // C-Phase 3 lane CE's two, now one: ClientShatterSurface, the pane's
        // per-shard geometry, is C# since lane c5-particles (ShardEffects).
        // EffectDispatchName ANSWERS rather than draws, and is
        // client-only for ClientDecalIndexFromName's reason read backwards --
        // it reads the CLIENT's copy of the EffectDispatch string table, which
        // is the side that has to turn an index into a callback. The SERVER's
        // half of that same table is no slot since plan_server_native_v2 T4b:
        // ServerTempEntEngine ADDS names through the string table forwarders,
        // and this READS them, so which side owns the numbering is still plain.
        "EffectDispatchName",
        // The client's temp entity tables, planned in C# (ClientTempEntEngine)
        // and stored from one block per table: RecvProp[] -> RecvTable ->
        // ClientClass. The server's are stored through its own TempEntBuild.
        "TempEntBuildClient",
        // C-Phase 3 lane CX's four, for the last three client decal halves.
        // Client-only, and only ONE of them is drawing. ClientDecalStaticProp
        // is the engine's static prop renderer, which the server has no arm of.
        // The other three are QUESTIONS, and each is a question only this side
        // can answer: ClientTraceLine reads the CLIENT's copy of the BSP and
        // the client's entity list -- the copy that decides what the client
        // draws, which is PointContents' argument one slot further along;
        // ClientIsLowViolence reads this viewer's convars and this viewer's
        // engine, and one player's gore setting is not a fact about the match;
        // ClientDecalNameFromIndex reads the game decal script through
        // `decalsystem`, whose precache indices are the CLIENT's numbering.
        //
        // ClientTraceLine is NOT DualWired with TraceLine and must not be made
        // so. The server's TraceLine answers no hitbox and hands back an
        // EHANDLE; this one answers a hitbox and hands back an entity INDEX,
        // because a client handle read back through CBaseHandle::Get() is
        // ledger row C8. They are two slots that trace, not one slot twice.
        "ClientDecalStaticProp", "ClientTraceLine", "ClientIsLowViolence",
        "ClientDecalNameFromIndex",
        // C-Phase 3 lane DECALS's three, closing behaviour ledger rows C2 and
        // C3 -- the two arms of C_BaseEntity::AddDecal this hierarchy never had
        // and the question the switch between them is made from. Client-only
        // for the block above's reasons, both of them: the two decal arms are
        // DRAWING (modelrender and the engine's decal system), and what an
        // entity's model IS on this side is a question about THIS side's entity
        // list -- which holds managed entities the server has no version of.
        //
        // ClientEntityModelType is NOT DualWired with ModelType and must not be
        // made so, which is the ClientTraceLine paragraph above read the other
        // way round. ModelType takes a PRECACHE index and answers about a file,
        // which is why it IS dual; this takes an ENTITY index and answers about
        // this client's entity list. Making them one slot would let a server
        // caller ask about a client entity and get a plausible wrong answer.
        "ClientEntityModelType", "ClientDecalStudioModel", "ClientDecalBrushModel",
        // C-Phase 5's four, the other direction of lane C4's pool above.
        // Client-only for that block's reason and not for a new one: the pool
        // is a CLIENT structure, so a spawn helper that fills one of its slots
        // can only exist on this side. The server's half of every one of these
        // ten temp entities is a send table and nothing else.
        //
        // The two model questions those helpers open with are NOT here: they
        // are DualWired below, because a model's frame count and a model's type
        // are facts about a FILE and both sides load the same file.
        "TempEntPoolCreate", "TempEntPoolWriteSpawn",
        // Lane FIZZ's one slot, for behaviour ledger row C6. Client-only for
        // ClientEntityOrigin's reason two blocks above, which is the slot it is
        // wired beside: it reads cl_entitylist, which is THIS side's table, and
        // it answers what the client DRAWS a bubble column across. The server
        // has an entity of the same name and a modelinfo that would answer, and
        // wiring it there anyway would be a claim that a fizz is spread over
        // the server's copy of the volume -- which nothing sends, nothing
        // reads, and nobody could tell apart from the right answer.
        "ClientEntityModelBounds",
        // plan_server_native_v2 T4a: THE CLIENT'S SOUND PATCHES. The five were
        // shared IShimEngine slots with a body on each side; the server's
        // patches are managed now (ServerSoundPatches, through EngineSide's
        // IServerSoundRoute), so the slots are the client's alone, on
        // IShimSoundscapeClient, and EngineSide reaches them only from its
        // client arms.
        "SoundPatchEmit", "SoundPatchStop", "SoundPatchState", "SoundPatchFree", "SoundPatchCreate",
    };

    private static readonly HashSet<string> BothSides = new(StringComparer.Ordinal)
    {
        // GetCurrentGravity is movevars_shared and answers on both sides. Only
        // the client's tempent physics asks for it today, and it is wired on
        // both anyway: a slot wired on one side answers the managed fallback on
        // the other, which is a plausible number rather than a refusal — the
        // hazard SOURCESHARP_TRACE_ALL's note describes for the trace masks.
        "CurrentGravity",

        // The vtable binding's one entry point. BOTH SIDES because each side
        // serves the interfaces its own DLL was handed: IVEngineClient exists
        // only on the client and IVEngineServer only on the server, and the
        // other three are on both. A NULL answer for a string this side does
        // not serve is the designed reply rather than a failure, so one slot
        // with a per-side answer is honest where two slots would not be.
        "AcquireInterface",

        // plan_entity.md step 3e. The trace ABI the managed movement loop runs
        // on, and BOTH SIDES is the whole point of the step rather than a
        // convenience: the client replays ProcessMovement for every command the
        // server has not acknowledged, so one copy of the loop needs one world
        // to ask on each side.
        //
        // Wired on the server only, the client's loop gets a NULL MoveTrace and
        // takes the managed fallback -- a clear trace, "nothing in the way" --
        // and the player falls through the floor for the rest of the round.
        // That is not hypothetical: it is where these four first went, and it
        // is what tools/movemath-gate reported (16 of 88 compared commands
        // disagreeing on Origin, Velocity, Flags and GroundEntity, against 0 in
        // the stock arm).
        "MoveTrace", "MoveEffect",

        "ConsoleSnapshot", "ConsoleSnapshotEntry",
        "EventGetBool", "EventGetFloat", "EventGetInt", "EventGetString",
        "FindConVarValue", "GetConVarValue", "GetCurrentTime",

        // plan_crossings.md Phase X1, and it is the same hazard CurrentGravity's
        // note above describes rather than bookkeeping. Each game DLL has its
        // OWN gpGlobals, pointing at its own side's CGlobalVars, so the slot only
        // does its job if both binaries fill it. Wired server-only it compiles,
        // links, and passes AbiParityTests' "every declared slot is wired" scan
        // -- which reads the shim as text and cannot know what the preprocessor
        // keeps -- while the client's pointer stays null and the client goes on
        // paying 3.4 crossings a frame for a clock read it could load. Nothing
        // fails; the phase simply half-happens. This row is what says otherwise.
        "GetGlobalVarsPtr",
        "ListenForGameEvent", "Msg",
        "NetHierarchyDeclare", "NetPublishType", "NetTierInfo",
        // RegisterAmmoType is client-only since plan_server_native_v2 T2.
        "RegisterConCommand", "RegisterConVar",
        // Both sides since Phase 9: movement is predicted, so the client must
        // replay with the same parameter block the server ticks with. This was
        // the masked hazard Part 4b.3 led with — server-only wiring left the
        // client predicting with shell_movement.cpp's hard-coded stock copy,
        // equal to Movement.cs by coincidence alone. Regressing this to
        // server-only is that hazard coming back.
        "RegisterMovementParams",
        // Both sides since lane side-audit (2026-10-02), the same hazard as
        // RegisterMovementParams one table over: ShouldCollide is asked during
        // prediction, and while the slot was server-only the client's
        // CollisionRules.Register was a silent no-op, its table equal to the
        // managed matrix by coincidence alone (CollisionRulesTests'
        // TheBuiltMatrixIsTheNativeDefaultTable). The shared IShimCollision
        // base holds it; the server's other collision slots moved to
        // IShimCollisionServer.
        "RegisterCollisionRules",
        // RegisterWeaponType went in plan_server_native_v2 T2: its table had
        // no reader on either side.
        "SetConVarValue", "SetNamedConVarValue", "RevertNamedConVar", "Warning",
        // plan_server_native_v2 T4e: both sides' convar registries refuse an
        // engine-owned name, so both read the flags.
        "FindConVarFlags",
    };

    private static readonly HashSet<string> DualWired = new(StringComparer.Ordinal)
    {
        // TempEntRegister WAS HERE, one slot with two arms over one TeClass.
        // plan_server_native_v2 T4b took the server's arm into C#
        // (ServerTempEntEngine plans the table and IShimEffectsServer::
        // TempEntBuild stores it), so the slot is the client's alone (ClientOnly
        // above), and then the client's went too (TempEntBuildClient). The two
        // tables still cannot drift: both are planned from the same TeClass by
        // the same TePropPlanner, and TePropPlannerTests holds that plan to the
        // same answers.
        // WHERE AN ENTITY IS, answered by each side off its own table -- the
        // server through the edict, the client off C_StandaloneEntity's own
        // transform, which is the same read ClientEntityOrigin already does by
        // index. The twin of EntityGetAngles, which has been dual for a while.
        //
        // It was SERVER-ONLY and WAIVED, and the waiver named the cost: every
        // color_correction measuring falloff from the world origin and every
        // spotlight_end pooling its light at the map origin, because asking a
        // client entity where it was answered (0,0,0) with no error. That zero
        // also reads exactly like an entity that really is at the map origin,
        // which makes it a trap for anything diagnosing with it.
        "EntityGetOrigin",
        // WHICH INDEX AN ENTITY IS, answered by each side from its own table:
        // the server through the edict, the client off C_StandaloneEntity's
        // own m_nEntIndex. The same question with the same answer, which is
        // what a dual slot means.
        //
        // It was SERVER-ONLY, and the waiver below records what that cost:
        // GetIndex answered -1 for every client entity, so every dynamic light
        // shared one pool key, and ClientStandaloneRegistry's index map --
        // which records an instance only `if ( index >= 0 )` -- was ALWAYS
        // EMPTY, so IClientStandalones.GetByIndex never matched anything and
        // every caller silently took its miss branch.
        "EntityGetIndex",
        // WHETHER A HANDLE STILL NAMES AN ENTITY, answered by each side from its
        // own list: the server through SourceSharp_ResolveEntity, the client
        // through its slot table and world arm, each comparing the serial --
        // stock's CHandle::Get() != NULL on either side. Dual since lane
        // side-audit-2 (2026-10-02).
        //
        // It was SERVER-ONLY and the client answered false for every handle.
        // Its one client caller is a restore: SavedHandles re-checks each
        // handle cs_cl_reload carries across, so every restored EntityHandle
        // field came back None. Regressing this to server-only is that bug.
        "EntityIsValid",
        // The "(no model)" bug's fix: one slot, two implementations, one per
        // side. Regressing this to one side is the bug coming back.
        "EntityGetModelName",
        // The same shape, found the same way and one phase later. This slot
        // was SERVER-ONLY for the whole port, so C# asking a client entity
        // which way it faces got (0,0,0) with no error — and the player's
        // animation reads exactly that to decide whether a step is forwards or
        // sideways, so every strafe blend was measured against a facing nobody
        // had. Dual rather than BothSides: the two sides answer from their own
        // copy of the entity, and a client interpolating between two networked
        // angles is not required to match the server's current one frame by
        // frame.
        "EntityGetAngles",
        // (Phase 5's dual skeleton slots -- the attachment lookup and frame,
        // the bone count, the sequence, the bone matrices, the subject test and
        // the studio name -- left when both sides' skeletons became managed:
        // plan physics_bones_native_zero 1.2. Each side's EngineEntityRender
        // asks its own skeleton.)
        // EntitySetSkin WAS HERE, dual, and is client-only since
        // plan_server_native_v2 T2. Its server arm wrote m_nSkin on a native
        // CBaseAnimating, and the body had been empty since no entity on the
        // standalone hierarchy has one. The client's arm, which lands the skin
        // where modelrender reads it, is the slot. The server's skin travels as
        // the m_nSkin prop since 2026-10-19 (NetworkedSkin): it writes its block
        // and marks the offset, and needs no skin slot at all.
        // The model SCALE, and it is dual for a reason the skin beside it is
        // not. A skin is read once by modelrender on the machine doing the
        // drawing, so the server has no question to answer. A SIZE is asked by
        // both skeletons: Studio_BuildMatrices takes it beside the origin and
        // the angles, so the server's copy decides where an ATTACHMENT lands on
        // a scaled model -- which is what a parented child's networked origin
        // is composed off -- and scaled_collision_rule.h reads it to decide
        // whether a trace may use the solver's scaled hull.
        //
        // This is the pose parameters' argument coming out the other way. Those
        // are client-only because "the server has no skeleton"; since Phase 5
        // it has one, and a size is a fact about that skeleton rather than
        // about pixels. Regressing this to client-only puts the server's bones
        // back to building every model at size 1 while the client draws it at
        // whatever the mapper asked for.
        "StandaloneSetModelScale",
        // PointContents (dual) folded onto IEngineTrace's forwarders on each side,
        // 2026-10-19.
        // SurfacePropGameMaterial (dual, C-Phase 5) folded onto IPhysicsSurfaceProps'
        // forwarders on both sides, 2026-10-19.

        // The four dual string-table slots (plan_gamedll_after.md Phase A8)
        // went in plan_server_native_v2 T3: each side's managed
        // ForwardedStringTables asks its own container through the forwarders,
        // and creating and adding are still the server's alone.

        // plan_gamedll_after.md Part B2. ONE SLOT, TWO IMPLEMENTATIONS, and it
        // is the same shape as TempEntRegister at the top of this set: a
        // [StandaloneNetState] declaration in SourceSharp.Shared is handed to
        // this slot on both sides, the server's arm builds
        // SendProp[] -> SendTable -> ServerClass out of it and the client's
        // builds RecvProp[] -> RecvTable -> ClientClass out of the SAME
        // declaration, so the two tables physically cannot drift.
        //
        // Dual and not BothSides, and here the distinction is load-bearing
        // rather than bookkeeping: the two arms resolve their props against
        // DIFFERENT BLOB BASES, because a CWorld and a C_World are unrelated
        // C++ classes with unrelated layouts. What the two sides agree on is
        // the payload word INDEX; the offset is each side's own arithmetic over
        // its own sizeof.
        //
        // Regressing this to server-only is the failure plan 2E describes and
        // A5's ruling forbids: the server would send DT_WORLD to a client with
        // no table to decode it, and the eleven values the renderer reads --
        // the world bounds, the two occlusion areas, the two prop widths, the
        // detail sprite material -- would arrive nowhere, with no crash and no
        // log line.
        "WorldNetDeclare",

        // lane diag-visibility. The engine has flushed its model cache; each
        // side drops the studio state IT holds.
        //
        // IT WAS SERVER-ONLY, AND THAT WAS HALF OF A REAL DEFECT. Bound on the
        // server arm alone, Engine.EntitiesInvalidateMdlCacheAll() met its null
        // check on the client and did nothing -- so the client kept every
        // m_CachedBones it had built out of studio data mdlcache had freed, and
        // nothing anywhere said so.
        //
        // Dual and NOT BothSides, and the distinction is the usual one made
        // sharp: the question is identical and the two answers have nothing in
        // common. The server's arm walks gEntList and g_Standalones and frees
        // CBaseAnimating::m_pStudioHdr; the client's walks cl_entitylist and
        // g_StandaloneClients and clears a bone cache. Neither list exists on
        // the other side.
        "EntitiesInvalidateMdlCacheAll",

        // The four game-file read slots (lane c2-sims, C9) folded onto
        // IBaseFileSystem's forwarders, 2026-10-19 (the user exposed file
        // handles to managed); each side reads through its own VBaseFileSystem011.
        // The sound patches' five (sound cluster 17, lane c2-sims; C6 cluster 9)
        // were here, both sides' shims owning patches the shared envelope
        // controller drives. They are CLIENT-ONLY since plan_server_native_v2
        // T4a: the server's patches are managed (ServerSoundPatches), so the
        // slots are listed with the client's above.
    };

    [Fact]
    public void PartitionMatchesTheDeclaredSides()
    {
        string? gamesystem = RepoLayout.FindSdkFile(Path.Combine("game", "shared", "sourcesharp_gamesystem.cpp"));
        if (gamesystem is null)
        {
            Assert.True(true, "gamesystem found for the side audit (skipped, not found)");
            return;
        }

        var sides = SideAudit.SlotSidesFromServed(EngineApiCensusTests.ClientMethods(), EngineApiCensusTests.ServerMethods());

        // The parse proving it parsed: the ABI is dominated by server-only
        // slots, so a count this low means a source went unread, not that the
        // ABI shrank.
        int serverOnly = sides.Count(kv => kv.Value == SlotSide.Server);
        // A floor for the reader: 200 -> 150 when plan_server_native_v2 T2
        // deleted the dead server slots (197 left); 150 -> 140 when T4a retired
        // IShimSound (146 left); 140 -> 110 when T4d began retiring the entity
        // API and world slots (139 left at its first step).
        Assert.True(serverOnly >= 110, $"the server's areas and the fill found the server-only bulk ({serverOnly} found)");

        // Both and Dual are one expectation since C6c: the server's half of a
        // two-sided slot is an area method, not a fill arm beside the client's,
        // so nothing left in the source says whether the two share a body.
        // DualWired stays as the record of why each of its slots is two-sided.
        var complaints = SideAudit.DiffPartition(sides, ClientOnly,
            BothSides.Union(DualWired).ToHashSet(StringComparer.Ordinal), new HashSet<string>());
        Assert.True(complaints.Count == 0,
            "every one-sided or dual slot is declared as such:\n" + string.Join("\n", complaints));
    }

    [Fact]
    public void AMisplacedSlotIsNamedByTheDiff()
    {
        // The next StandaloneSetBlending: an agent wires a slot under the
        // client guard and nothing declares it. The flat parse shipped two of
        // these; this one is a red naming the slot.
        var sides = SideAudit.SlotSides("""
            engineApi.Msg = SourceSharp_EngineMsg;
            #ifdef CLIENT_DLL
            engineApi.ShinyNewThing = SourceSharp_ShinyNewThing;
            #endif
            """);

        Assert.True(sides["ShinyNewThing"] == SlotSide.Client, "the guard is seen");

        var complaints = SideAudit.DiffPartition(sides,
            expectedClient: new HashSet<string>(),
            expectedBoth: new HashSet<string> { "Msg" },
            expectedDual: new HashSet<string>());

        Assert.True(complaints.Count == 1 && complaints[0].Contains("ShinyNewThing"),
            "an undeclared one-sided slot is a complaint naming the slot: " + string.Join("; ", complaints));
    }

    [Fact]
    public void AnElseFlipsTheGuardAndNestingHolds()
    {
        var sides = SideAudit.SlotSides("""
            #ifndef CLIENT_DLL
            engineApi.ServerThing = A;
            #else
            engineApi.ClientThing = B;
            #endif
            #ifndef CLIENT_DLL
            #ifdef SOMETHING_ELSE
            engineApi.StillServer = C;
            #endif
            engineApi.AlsoServer = D;
            #endif
            engineApi.Everywhere = E;
            """);

        Assert.True(sides["ServerThing"] == SlotSide.Server, "an #ifndef region is server");
        Assert.True(sides["ClientThing"] == SlotSide.Client, "an #else flips it to client");
        Assert.True(sides["StillServer"] == SlotSide.Server, "an unrelated inner guard changes nothing");
        Assert.True(sides["AlsoServer"] == SlotSide.Server, "an inner #endif does not close the outer region");
        Assert.True(sides["Everywhere"] == SlotSide.Both, "outside every guard is both");
    }

    [Fact]
    public void TheServersAreasJoinTheClientsFill()
    {
        // C6c's shape: the fill is the client's whole, and the server's slots
        // are the methods of its areas.
        var sides = SideAudit.SlotSides("""
            #ifdef CLIENT_DLL
            engineApi.Shared = A;
            engineApi.ClientThing = B;
            #endif
            """, new HashSet<string> { "Shared", "ServerThing" });

        Assert.True(sides["Shared"] == SlotSide.Both, "filled by the client and served by the server is both");
        Assert.True(sides["ClientThing"] == SlotSide.Client, "filled by the client alone is client");
        Assert.True(sides["ServerThing"] == SlotSide.Server, "served by the server alone is server");
    }

    //-------------------------------------------------------------------------
    // Audit 2 — the chain from an interface member to the slots behind it,
    // and the rule over what each shipped assembly references.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheChainKnowsTheFourBugSlots()
    {
        var (bindingMap, wrapperSlots, _) = LoadChain();
        if (bindingMap is null || wrapperSlots is null)
        {
            Assert.Fail("the ABI sources for the chain are found: a missing input is a failure, never a skip");
            return;
        }

        Assert.True(bindingMap.Count > 200, $"the binding parse found the forwarders ({bindingMap.Count} members)");

        string[] SlotsOf(string iface, string member)
            => bindingMap.TryGetValue((iface, member), out var wrappers)
                ? SideAudit.SlotsOf(wrappers, wrapperSlots).ToArray()
                : Array.Empty<string>();

        Assert.True(SlotsOf("IEngineEntities", "GetOrigin").Contains("EntityGetOrigin"),
            "GetOrigin is known to lean on EntityGetOrigin");
        Assert.True(SlotsOf("IEngineEntities", "GetIndex").Contains("EntityGetIndex"),
            "GetIndex is known to lean on EntityGetIndex");
        Assert.True(SlotsOf("IEngineEntityRender", "SetBlending").Contains("StandaloneSetBlending"),
            "SetBlending is known to lean on StandaloneSetBlending");
        Assert.True(SlotsOf("IEngineEntityRender", "GetModelName").Contains("EntityGetModelName"),
            "GetModelName is known to lean on EntityGetModelName");
    }

    // What each assembly is known to reference against the other side's slots
    // today. These are BUGS AND DOCUMENTED FALLBACKS, not permissions — each is
    // recorded in plan_baseentity.md Part 4b with the phase that deletes it,
    // and the exact match below means fixing one without deleting its line here
    // is also a red. The lists can only shrink.

    /// <summary>
    /// EMPTY, and it is meant to stay that way: the list could only ever
    /// shrink, and both of the survey's live client-side bugs are now fixed
    /// rather than waived.
    ///
    /// <para>
    /// GetIndex "answered -1, so every dynamic light shares one pool key"
    /// (ClientDynamicLight.cs:120, :240, :271), and was also — unadvertised —
    /// what kept ClientStandaloneRegistry's index map permanently empty.
    /// GetOrigin "answers (0,0,0) on this side, so every color_correction
    /// measures falloff from the world origin and every spotlight_end pools
    /// its light at the map origin" (ClientColorCorrection.cs:182). Each is a
    /// dual-wired slot the client now fills from its own entity.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> ClientWaiver = new(StringComparer.Ordinal);

    private static readonly HashSet<string> ServerWaiver = new(StringComparer.Ordinal)
    {
        // THE SOUND PATCHES (plan_server_native_v2 T4a). The shared envelope
        // controller drives IEngineSound's five patch members on both sides;
        // the client's half carries them out on IShimSoundscapeClient's
        // client-only slots, and the server's half on the managed
        // SoundEmitter's route (EngineSound.SidePatch*, over
        // IServerSoundRoute), which is no slot. The binding map joins both
        // halves under one member, so the client's slots read as the server's
        // too; the route is the server's answer, and this audit cannot see a
        // managed answer. The lines go if the audit learns to read a member per
        // side.
        "IEngineSound.SoundPatchCreate is backed by Client-only slot 'SoundPatchCreate'",
        "IEngineSound.SoundPatchEmit is backed by Client-only slot 'SoundPatchEmit'",
        "IEngineSound.SoundPatchFree is backed by Client-only slot 'SoundPatchFree'",
        "IEngineSound.SoundPatchState is backed by Client-only slot 'SoundPatchState'",
        "IEngineSound.SoundPatchStop is backed by Client-only slot 'SoundPatchStop'",
    };

    /// <summary>
    /// Shared code that leans on server-only slots.
    ///
    /// <para>
    /// The sound patches' five (see ServerWaiver) are its only lines. It had
    /// been EMPTY, and it emptied exactly the way its old comment predicted: every
    /// line in it was WeaponLogic's engine surface, waived "until weapon
    /// prediction moves managed (Phase 9c/10) — at which point these slots go
    /// dual and the lines come off". Phase 10 moved the machine onto the
    /// declared states; the shared assembly's weapon code now touches no
    /// engine slot at all (the per-side ARMS do, from the per-side
    /// assemblies), so there is nothing left to waive.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> SharedWaiver = new(StringComparer.Ordinal)
    {
        // The sound patches' five, as ServerWaiver says.
        "IEngineSound.SoundPatchCreate is backed by Client-only slot 'SoundPatchCreate'",
        "IEngineSound.SoundPatchEmit is backed by Client-only slot 'SoundPatchEmit'",
        "IEngineSound.SoundPatchFree is backed by Client-only slot 'SoundPatchFree'",
        "IEngineSound.SoundPatchState is backed by Client-only slot 'SoundPatchState'",
        "IEngineSound.SoundPatchStop is backed by Client-only slot 'SoundPatchStop'",
    };

    //-------------------------------------------------------------------------
    // The KNOWN lists: wrong-side reaches hidden behind a helper in Abi or an
    // Interop assembly, which the direct audit could not see. Found on
    // 2026-10-02 (lane side-audit, base 80bff18) the first time the audit
    // followed calls into those assemblies.
    //
    // EACH ENTRY IS A BUG REPORT, NOT A PASS. The waivers above stay empty;
    // these exist only because the walk found more than one lane should fix,
    // and each line's ruling is in the commit that found it. Every list has a
    // ceiling equal to its length: fixing one is a red until its line goes
    // and the ceiling falls with it. Never raise a ceiling.
    //
    // ALL THREE ARE EMPTY since lane side-audit-2 (2026-10-02): the ten
    // entries were ruled one per commit (EntityIsValid dual-wired; RunMove's
    // stages to Server.Interop; NetStateBinding's two arms; the world bind to
    // ServerModule; EngineClient to Client.Interop). The lists and their
    // zero ceilings stay so a new finding has somewhere to be ruled.
    //-------------------------------------------------------------------------

    private const int ClientKnownCeiling = 0;

    private static readonly HashSet<string> ClientKnown = new(StringComparer.Ordinal)
    {
    };

    private const int SharedKnownCeiling = 0;

    private static readonly HashSet<string> SharedKnown = new(StringComparer.Ordinal)
    {
    };

    private const int SharedInteropKnownCeiling = 0;

    private static readonly HashSet<string> SharedInteropKnown = new(StringComparer.Ordinal)
    {
    };

    private static IReadOnlySet<string> NoneKnown => new HashSet<string>(StringComparer.Ordinal);

    [Fact]
    public void EachKnownListIsAtItsCeiling()
    {
        Assert.True(ClientKnown.Count == ClientKnownCeiling,
            $"the client's known list is at its ceiling ({ClientKnown.Count} listed, ceiling {ClientKnownCeiling})");
        Assert.True(SharedKnown.Count == SharedKnownCeiling,
            $"the shared known list is at its ceiling ({SharedKnown.Count} listed, ceiling {SharedKnownCeiling})");
        Assert.True(SharedInteropKnown.Count == SharedInteropKnownCeiling,
            $"Shared.Interop's known list is at its ceiling ({SharedInteropKnown.Count} listed, ceiling {SharedInteropKnownCeiling})");
    }

    private const string Abi = "SourceSharp.Abi";
    private const string ServerInterop = "SourceSharp.Server.Interop";
    private const string ClientInterop = "SourceSharp.Client.Interop";

    // Each side's engine seams (plan_shim_areas_split phase 4). A call to a
    // seam names a type in one of these, so the logic's audits follow them as
    // they follow the interop: a seam is how logic reaches a slot. The
    // interop's own audits do not: inside the interop a seam call is the
    // binding chain, which was pass-through when the seams were the interop's
    // own types, and stays so.
    private const string ServerInterface = "SourceSharp.Server.Interface";
    private const string ClientInterface = "SourceSharp.Client.Interface";

    private static bool ServerFills(SlotSide side) => side is SlotSide.Server or SlotSide.Both or SlotSide.Dual;
    private static bool ClientFills(SlotSide side) => side is SlotSide.Client or SlotSide.Both or SlotSide.Dual;
    private static bool BothFill(SlotSide side) => side is SlotSide.Both or SlotSide.Dual;

    [Fact]
    public void ServerAssemblyLeansOnlyOnSlotsItsSideFills()
        => CheckAssembly(typeof(SourceSharp.Server.ServerModule).Assembly.Location,
            ServerFills, ServerWaiver, NoneKnown, "SourceSharp.Server", Abi, ServerInterop, ServerInterface);

    [Fact]
    public void ClientAssemblyLeansOnlyOnSlotsItsSideFills()
        => CheckAssembly(typeof(client::SourceSharp.Client.ClientModule).Assembly.Location,
            ClientFills, ClientWaiver, ClientKnown, "SourceSharp.Client", Abi, ClientInterop, ClientInterface);

    [Fact]
    public void SharedAssemblyLeansOnlyOnSlotsBothSidesFill()
    {
        // The shared source has no assembly of its own since plan_side_isolation
        // P7; inside either side's it is mixed with that side's code, which may
        // lean on its own slots. So it is compiled alone, as SourceSharp.Shared
        // was, and that is what the both-sides rule is held over.
        string built = SharedSourceBuild.Build();
        try
        {
            CheckAssembly(built, BothFill, SharedWaiver, SharedKnown, SharedSourceBuild.Name, Abi, SharedSourceBuild.InteropName,
                          SharedSourceBuild.InterfaceName);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(built)!, recursive: true);
        }
    }

    [Fact]
    public void ServerInteropLeansOnlyOnSlotsTheServerFills()
        => CheckAssembly(Beside(ServerInterop), ServerFills, NoWaiver, NoneKnown, ServerInterop, Abi);

    [Fact]
    public void ClientInteropLeansOnlyOnSlotsTheClientFills()
        => CheckAssembly(Beside(ClientInterop), ClientFills, NoWaiver, NoneKnown, ClientInterop, Abi);

    [Fact]
    public void SharedInteropLeansOnlyOnSlotsBothSidesFill()
    {
        // The shared interop has no assembly of its own since
        // plan_shim_areas_split phase 3: each side's interop compiles it beside
        // that side's own code, which may lean on its own slots. So it is
        // compiled alone, as SourceSharp.Shared.Interop was, and that is what the
        // both-sides rule is held over.
        string dir = SharedSourceBuild.NewDirectory();
        try
        {
            string built = SharedSourceBuild.BuildInterop(dir);
            CheckAssembly(built, BothFill, NoWaiver, SharedInteropKnown, SharedSourceBuild.InteropName, Abi);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>The Interop assemblies were never audited, so they start with no waiver at all.</summary>
    private static IReadOnlySet<string> NoWaiver => new HashSet<string>(StringComparer.Ordinal);

    /// <summary>An Interop assembly, from the directory the side assemblies were loaded from.</summary>
    private static string Beside(string name)
        => Path.Combine(Path.GetDirectoryName(typeof(SourceSharp.Server.ServerModule).Assembly.Location)!, name + ".dll");

    private static void CheckAssembly(string assemblyPath, Func<SlotSide, bool> allowed,
                                      IReadOnlySet<string> waiver, IReadOnlySet<string> known, string what,
                                      params string[] helpers)
    {
        var (bindingMap, wrapperSlots, slotSides) = LoadChain();
        if (bindingMap is null || wrapperSlots is null || slotSides is null)
        {
            Assert.Fail($"the sources to audit {what} are found: a missing input is a failure, never a skip");
            return;
        }

        Assert.True(File.Exists(assemblyPath), $"{what} is built beside the tests ({assemblyPath})");

        // Its own references AND everything its calls into Abi and the Interop
        // assemblies reach: a helper is no longer a way round the audit.
        // The audit's own chain (EngineSide and the binding classes) is
        // pass-through wherever it sits, the audited assembly included.
        var reach = SideReach.EngineReach(assemblyPath, new HashSet<string>(helpers, StringComparer.Ordinal), out var unresolved,
                                          Chain());
        Assert.True(reach.Count > 0, $"{what} reaches engine areas at all ({reach.Count} found)");
        Assert.True(unresolved.Count == 0, $"every helper {what} calls resolved:\n" + string.Join("\n", unresolved));

        var violations = SideReach.ReachViolations(reach, bindingMap, wrapperSlots, slotSides, allowed)
                                  .ToHashSet(StringComparer.Ordinal);

        var newOnes = violations.Except(waiver).Except(known).OrderBy(v => v, StringComparer.Ordinal).ToList();
        var stale = waiver.Union(known).Except(violations).OrderBy(v => v, StringComparer.Ordinal).ToList();

        Assert.True(newOnes.Count == 0,
            $"{what} leans only on slots its side fills — new violations:\n" + string.Join("\n", newOnes));
        Assert.True(stale.Count == 0,
            $"every waived or known violation in {what} still exists — fixed ones must lose their line:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void AProvokedWrongSideReferenceIsNamed()
    {
        // A synthetic client assembly that calls a server-only-backed member:
        // the join names the member and the slot, which is what the shipped
        // flat parse could never do.
        var bindingMap = new Dictionary<(string, string), HashSet<string>>
        {
            [("IEngineEntities", "GetOrigin")] = new(StringComparer.Ordinal) { "EntityGetOrigin" },
        };
        var wrapperSlots = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["EntityGetOrigin"] = new(StringComparer.Ordinal) { "EntityGetOrigin" },
        };
        var slotSides = new Dictionary<string, SlotSide>(StringComparer.Ordinal)
        {
            ["EntityGetOrigin"] = SlotSide.Server,
        };

        var violations = SideAudit.UsageViolations(
            new[] { ("IEngineEntities", "GetOrigin") },
            bindingMap, wrapperSlots, slotSides,
            side => side is SlotSide.Client or SlotSide.Both or SlotSide.Dual);

        Assert.True(violations.Count == 1
                    && violations[0].Contains("GetOrigin") && violations[0].Contains("EntityGetOrigin"),
            "a wrong-side reference is one complaint naming member and slot: " + string.Join("; ", violations));
    }

    /// <summary>
    /// The binding map over a shared file and two halves (plan_shim_areas_split
    /// phase 7): a member in a half maps under the shared declaration's
    /// interface, and a shared member whose logic calls a private partial
    /// helper maps to the engine member the helper's half calls. The other
    /// half's answer calls nothing, and its helper line is not counted as the
    /// member before it.
    /// </summary>
    [Fact]
    public void TheBindingMapReadsTheHalvesAndFollowsTheirHelpers()
    {
        const string shared = """
            internal sealed partial class EngineThings : IEngineThings
            {
                public bool IsDormant(EntityHandle entity) => Mirrored ? false : EngineIsDormant(entity);
                private partial bool EngineIsDormant(EntityHandle entity);
                public int Shared(int x) => E.SharedThing(x);
            }
            """;
        const string client = """
            internal sealed partial class EngineThings
            {
                public int Health => E.LocalPlayerHealth;

                private partial bool EngineIsDormant(EntityHandle entity) => E.StandaloneIsDormant(entity);
            }
            """;
        const string server = """
            internal sealed partial class EngineThings
            {
                public void SetSkin(EntityHandle entity, int skin) => E.EntitySetSkin(entity, skin);
                public int Health => -1;
                private partial bool EngineIsDormant(EntityHandle entity) => false;
            }
            """;

        var map = SideAudit.BindingMap(shared, server, client);

        Assert.Equal(["StandaloneIsDormant"], map[("IEngineThings", "IsDormant")]);
        Assert.Equal(["EntitySetSkin"], map[("IEngineThings", "SetSkin")]);
        Assert.Equal(["LocalPlayerHealth"], map[("IEngineThings", "Health")]);
        Assert.Equal(["SharedThing"], map[("IEngineThings", "Shared")]);
        Assert.False(map.ContainsKey(("IEngineThings", "EngineIsDormant")), "a helper is folded, not a member of its own");
    }

    //-------------------------------------------------------------------------

    /// <summary>
    /// The chain's types: EngineSide (the wrapper map) and every binding class
    /// the binding map is parsed from. Never empty: a parse that found no
    /// binding class would let their bodies count as uses again.
    /// </summary>
    private static HashSet<string> Chain()
    {
        string? binding = RepoLayout.FindSdkFile(Path.Combine("managed", "SourceSharp.Shared.Interop", "EngineBinding.cs"));
        Assert.True(binding is not null, "EngineBinding.cs is found for the chain");
        var chain = SideAudit.BindingClasses(File.ReadAllText(binding!));
        chain.Add("EngineSide");
        Assert.True(chain.Count > 10, $"the binding classes are read from EngineBinding.cs ({chain.Count} found)");
        return chain;
    }

    private static (Dictionary<(string, string), HashSet<string>>?,
                    Dictionary<string, HashSet<string>>?,
                    Dictionary<string, SlotSide>?) LoadChain()
    {
        // Each type is the shared file and the two sides' halves
        // (plan_shim_areas_split phase 7). Every one must be found.
        string[]? binding = Sources("EngineBinding");
        string[]? engine = Sources("EngineSide");
        string? gamesystem = RepoLayout.FindSdkFile(Path.Combine("game", "shared", "sourcesharp_gamesystem.cpp"));

        if (binding is null || engine is null || gamesystem is null)
            return (null, null, null);

        return (SideAudit.BindingMap(binding),
                SideAudit.WrapperSlots(string.Join("\n", engine)),
                SideAudit.SlotSidesFromServed(EngineApiCensusTests.ClientMethods(), EngineApiCensusTests.ServerMethods()));
    }

    /// <summary>A shared type's file and its two halves, or null when any is missing.</summary>
    private static string[]? Sources(string type)
    {
        string?[] paths =
        [
            RepoLayout.FindSdkFile(Path.Combine("managed", "SourceSharp.Shared.Interop", type + ".cs")),
            RepoLayout.FindSdkFile(Path.Combine("managed", "SourceSharp.Server.Interop", type + ".Server.cs")),
            RepoLayout.FindSdkFile(Path.Combine("managed", "SourceSharp.Client.Interop", type + ".Client.cs")),
        ];
        return paths.Any(p => p is null) ? null : paths.Select(p => File.ReadAllText(p!)).ToArray();
    }
}

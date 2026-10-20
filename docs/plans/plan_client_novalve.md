# plan_client_novalve.md: client.so with as little native code as possible

Status: DRAFT for review (2026-10-04). Uncommitted until the user has read it.
Measured at main `db15948c` against the fork pinned by `sdk-root.lock`, debug
config (`farm/debug/build`). Every number carries the command that produced it
(§9); re-run them at the tip before starting a phase.

This is the client wave that `plan_native_bindings_only.md` §3 named N3 (the
client shim's logic) and N4 ("a client-novalve plan, the analogue of
plan_server_novalve"). `plan_server_novalve.md` is the precedent and its
machinery is reused wherever it already has a client arm (§4.3 lists what does
not). The client's engine-interface wave (`plan_client_vtables.md`) is
complete: managed already reaches the engine through 7 client shim areas and
native reaches managed through 9 `IManaged*Client` interfaces, so this plan
adds areas; it never adds a free `SourceSharp_*` function.

## The goal, as ruled (user, 2026-10-04)

`client_sourcesharp` (client.so):

- **compiles no fork source**, and no fork header outside one generated client
  allowlist: the engine, renderer, material-system, vgui and input-system
  interface headers, mathlib, and their include closure;
- **links** `libmathlib.a` and, by ruling, `particles.a` + `dmxloader.a`
  (§3.4). Not tier0, tier1, vstdlib, tier2, tier3, bitmap, vtf, steam_api,
  vgui_controls or matsys_controls;
- **keeps native only bindings** in `plan_native_bindings_only.md` §1's sense:
  B1 vtable thunks the engine calls, B2 layouts it reads, B3 registration,
  B4 bound `IShim*` areas, B5 host. Native may carry out a decision; it never
  makes one;
- **UI**: one native vgui Panel; everything drawn on it is managed (§3.2),
  **including the main menu, the pause menu, every dialog, the loading screen
  and the console**: `GameUI.so` is replaced by a module of ours (§3.2a);
- **renderer**: decisions in C#, one native render-pass executor; the leaf
  system and shadow manager stay engine-called machinery with their policy in
  C# (§3.3);
- **deletes** SteamVR and the in-game video panel. The FPS, blocking-file-I/O
  and net-graph panels go as panels, and what they showed becomes dev API
  routes (`frame`, `io`, `netgraph`, §4.4). Joystick, Steam Controller and
  rumble are ported, not dropped.

What is left out rather than ported is a divergence with its caller, recorded
in `docs/absence-client.md`, which must be empty at the plan's close except for
the two deletions above (VR, video).

---

## 1. Where client.so stands

| | count | instrument |
|---|---|---|
| objects in the client link | 423 | `ninja -C farm/debug/build -t commands out/client.so`, the `-c` lines |
| of which the DLL's own TUs | 260: **157 fork** (127 `game/client`, 14 `game/shared`, 16 `public`) + 103 ours (60 client, 42 shared, 1 host) | same, split by path prefix |
| of which fork members of in-tree archives | 163: vgui_controls 77, tier1 42, mathlib 22, matsys_controls 21, lzma 1 | same |
| lines in the 157 fork sources | 87,644 | `wc -l` over the `${SS_SRC}` entries of `src/client/native/CMakeLists.txt` |
| our client native, by `SS_REASON` letter | a 27 files / 27,356 lines; b 11 / 7,513; g 10 / 6,281; d 4 / 1,831; h 2; c 2; e 2; f 2; 31 headers (8,846) unlettered | line-2 markers, §9 |
| fork headers in the dep closure | **1,060** (385,097 lines): public 483, game 396, vgui2 99, tier1 45, mathlib 25 | `ninja -t deps` over the 423 objects, `readlink -f` |
| fork links the census attributes to the client | 643 (`sdk-links/client.txt`); server 8, shared 150 | `make sdk-census` |
| Valve libraries on the link line | bitmap, dmxloader, steam_api, particles, tier2, tier3, vtf, mathlib, matsys_controls, tier1, vgui_controls, tier0, vstdlib (+ uuid, z, rt, dl) | the link command in the same `-t commands` output |
| direct imports at the shim close | vgui_controls 660, tier1 191, mathlib 105, particles 41, tier2 30, tier3 16 | `docs/shim-reasons.md` §"imports" (re-take in C0) |
| managed client code | 131 files / 26,774 lines (`TempEnts/` 56, `Standalone/` 51) vs server 381 / 74,962 | `find src/client/managed -name '*.cs'` |

For comparison the server (`docs/server-novalve-ratchet.tsv`): fork sources 0,
forbidden libraries 0, 157 fork headers of which 45 outside the allowlist.

---

## 2. The subsystem census

Eighteen clusters cover the 157 fork sources exactly (157 files, 87,644
lines; the per-cluster `wc -l` is in §9). For each: the engine contract that
pins a shell native (CLAUDE.md: a C++ vtable the engine calls, a struct read
by layout, or a pointer held), what stays as that shell, what moves to C#, and
what is already managed. "Our shells" are the `src/client/native` files on top
of the cluster today.

| # | cluster | files / lines | pin (what stays native, as a binding) | moves to C# | already managed |
|---|---|---:|---|---|---|
| 1 | material proxies (`*proxy.cpp`, `mathproxy`, `cl_mat_stub`) | 24 / 3,896 | `IMaterialProxy` vtable, found by name through the factory: **one** native trampoline class, registered once per proxy name (B1+B3) | every `OnBind` body: pure arithmetic over material vars | nothing (`shell_materialproxies.cpp` exposes 4 for HL2MP parity) |
| 2 | particles glue (`particlemgr`, `particles_new/simple/localspace/ez`, `particle_collision/sphererenderer/proxies`, `particlesystemquery`) | 10 / 5,129 | `IClientRenderable` on the effect binding, `IParticleSystemQuery` the particles lib calls back, `particles.a` itself (§3.4) | effect selection, attachment, lifetime, the old `CParticleMgr` simple-particle sim and its emitters | `ClientDust/SmokeStack/SmokeVolume/SteamJet/Trails/Plasma/Embers.cs`, `ParticleReloadPolicy.cs`, manifest |
| 3 | fx and client-side effects (`fx*.cpp`, `c_tracer`, `clientsideeffects*`, `clienteffectprecachesystem`, `EffectsClient`, `smoke_fog_overlay`, `warp_overlay`) | 19 / 8,297 | `IEffects` (exposed interface) as a thunk; mesh builds behind `IMatRenderContext` as B4 draw slots | all decisions: blood, sparks, explosions, tracers, shell eject, water, the effect-list bookkeeping, precache lists | `TempEnts/ImpactEffects.cs`, `ImpactChoice.cs`, per-TE classes |
| 4 | tempents / effect dispatch (`c_te_legacytempents`, `c_te_effect_dispatch`, `effect_dispatch_data`) | 3 / 4,167 | the pool's `IClientRenderable` | the rest of `C_LocalTempEntity` (3,677 lines: lifetimes, bounce, gibs, muzzle flash choice) and the dispatch-by-name table | 56 files under `TempEnts/`; `ClientTempEntPool`, `ClientTeEffectDispatch` |
| 5 | beams and sprites (`beamdraw`, `view_beams`, `spritemodel`) | 3 / 4,274 | beam/sprite mesh builder as B4 draw slots (`DrawBeamPoints/Quadratic`, `DrawSprite` exist) | `IViewRenderBeams` bookkeeping, beam lifetimes, ring/follow/ent-point logic, sprite frame/orientation choice | `ClientBeam.cs`, `ClientQuadraticBeam.cs`, `ClientSprite.cs`, `ClientSpriteRenderRules.cs` |
| 6 | rope (`c_rope`, `rope_helpers`, `rope_physics`, `simple_physics`) | 4 / 2,659 | `IClientRenderable` + mesh build (B4 `RopeUpdate` exists) | verlet simulation, wind, LOD, slack | `ClientRope.cs`, `RopeNetState.cs` |
| 7 | ragdoll / bones / studio (`ragdoll`, `bone_merge_cache`, `bone_accessor`, `bonelist`, `studio_shared`, `sequence_Transitioner`, `posedebugger`, `studio_stats`) | 8 / 1,660 | `ragdoll_t` holds `IPhysicsObject*` (pointer held): creation and step stay a B1/B4 shell | sequence transitions, bone-merge policy, the ragdoll settle/magnet rules. Bone setup itself is `plan_native_bindings_only.md` N2 (shared), **test first**, and is not re-planned here | `ClientSsRagdoll.cs`, `ClientBoneMerge.cs`, `RagdollPolicy.cs`, `ClientProceduralBones.cs` |
| 8 | datatables / interpolation (`dt_recv`, `dt_utlvector_recv`, `client_class`, `networkvar`, `renamed_recvtable_compat`, `recvproxy`, `interpolatedvar`, `interpolatortypes`) | 8 / 1,477 | **hard pin**: the engine reads `RecvTable`/`RecvProp`/`ClientClass` by layout and calls `RecvVarProxyFn` pointers (B2, letter f); `shell_recvtable.cpp` builds them from C# declarations today | `CInterpolatedVar` (already `Interpolation.cs`), the proxies' value conversions where they decide anything, `interpolatortypes` (no caller) | `Interpolation.cs`, `*NetState.cs`, `ProtocolManifest.cs` |
| 9 | physics / movehelper / trace (`physics`, `movehelper_client`, `gametrace_client`) | 3 / 1,893 | VPhysics callback vtables (`IPhysicsCollisionEvent/Solver/ObjectEvent`), `IMoveHelper` vtable, mid-step answers (B1/e) | the same arms the server ported in `plan_physics_stock_parity.md` (impact sounds, friction, touch) on the client's prediction side; trace composition | `ClientPlayerPrediction.cs`, `PredictionRing.cs`, `Movement.cs` |
| 10 | leaf system / shadows / detail / colour correction (`clientleafsystem`, `clientshadowmgr`, `detailobjectsystem`, `colorcorrectionmgr`) | 4 / 9,322 | `IClientLeafSystemEngine` (exposed, engine-called), the `IShadowMgr` callback pair, `CDetailModel`'s `IClientRenderable`, the shadow/model `IMaterialProxy`s: all B1, rewritten under a Valve header as ours (§3.3) | policy: shadow choice per entity (`ClientRenderPolicy.Shadow` exists, unreached), fade distances incl. the FOV factor (absence row 6), detail density and sprite sorting parameters, colour-correction weights | `ClientColorCorrection(.Volume).cs`, `ClientWorldState.cs`, `ClientRenderPolicy.cs` |
| 11 | view / viewrender / postprocess (`viewrender`, `viewpostprocess`, `view`, `view_effects`, `viewdebug`, `rendertexture`, `view_scene`) | 7 / 12,747 | `CViewSetup` by layout; the calls into `IVRenderView`/`IMatRenderContext`: **one native render-pass executor** (§3.3, B2+B4) | the per-frame render plan: pass order, water/refract/reflect/monitor decisions, fog, bloom/tonemap/motion-blur parameters, screen fades and shake (`IViewEffects`), the debug views | `ClientSsViewModel.cs`, `ClientSkybox.cs`, `ClientWorldFog.cs` |
| 12 | glow / screen-space / pixelvis / flashlight (`glow_outline_effect`, `glow_overlay`, `ScreenSpaceEffects`, `flashlighteffect`, `c_pixel_visibility`) | 5 / 2,688 | pixel-visibility occlusion queries and the flashlight shadow handle as B4 slots | glow overlay (sun) placement and fade, screen-space effect selection, **flashlight on/off/pose (port, not delete: it is player behaviour behind an arm we bypassed)** | `EntityEffects.cs` carries the flag only |
| 13 | input (`in_main`, `in_camera`, `in_joystick`, `in_mouse`, `in_steamcontroller`, `cam_thirdperson`, `c_rumble`) | 7 / 5,935 | `IInput` (52 virtuals behind `IBaseClientDLL::IN_*`/`CreateMove`/`KeyEvent`), `kbutton_t` returned to the engine, the usercmd codec (f) | key state machine, mouse accel/filter/sensitivity, joystick and Steam Controller mapping, third-person camera, rumble user messages | `ClientPlayerPrediction.cs`, `UserCmdCodec.cs`, `DecodedUserCmd.cs` (CreateMove already routes to C#) |
| 14 | vgui panels, game_controls, clientmode, text_message | 29 / 11,975 | **one** `vgui::Panel` (exists: `shell_hud.cpp`) plus the by-name interfaces the engine asks for (`ICenterPrint`, `IMessageChars`, `ILoadingDisc`, `IConsole`, `IClientMode` hooks) as thunks | scoreboard, team menu, spectator GUI, MOTD window, centre print, loading disc, hints, chat, clientmode's per-frame decisions, `vgui_int` startup order (§3.2) | `Hud.cs`, `HudMessages.cs`, `HudHints.cs`, `HudBuildStamp.cs`, `Scoreboard.cs` |
| 15 | Steam context / voice / VR (`clientsteamcontext`, `voice_status`, `voice_banmgr`, `client_virtualreality`) | 4 / 2,385 | `IVoiceStatusHelper` hooks as thunks | Steam: C# calls `libsteam_api`'s flat API itself, as the server does (`plan_server_novalve.md` §3.5); voice status and ban list; **VR deleted** | voice user-message schema |
| 16 | util / init / registries / tools (`cdll_util`, `cdll_bounded_cvars`, `initializer`, `classmap`, `client_factorylist`, `client_thinklist`, `c_user_message_register`, `precache_register`, `gamevars_shared`, `secure_command_line`, `sceneentity_shared`, `crtmemdebug`, `memoverride`, `entity_client_tools`, `toolframework_client`) | 15 / 4,901 | `IClientTools` (exposed): keep as a refusal-armed thunk or search the engine for its consumer and delete; `memoverride` goes as on the server (P4) | the registries become C# tables; `cdll_util` bodies move to the callers' side; think list to the managed frame | `ConVars.cs`, `Commands.cs` |
| 17 | sound (`soundenvelope`, `sentence`, `SoundParametersInternal`) | 3 / 3,878 | none (`CSoundControllerImp` is a per-frame client system: B3 registration only) | sound envelopes/patches (the server has `IManagedSoundController`, 13 methods: reuse), sentence and sound-parameter parsing | `SoundscapeLibrary.cs` |
| 18 | decals (`decals.cpp`) | 1 / 361 | none | decal name/index tables | `ClientTe*Decal.cs`, `ClientEntityDecal.cs` |

Our own 60 native files sit on these clusters; the big ones and their fate:

| file | lines | letter | fate |
|---|---:|---|---|
| `shell_standalone_client.cpp` | 7,003 | a | the client half of the managed-entity tree, "mostly drawing": N1.3's treatment, keep the `IClientRenderable`/`IClientEntity`/`IClientNetworkable` vtables, move per-op bookkeeping and every draw decision to C# |
| `shell_effects_sim.cpp`, `shell_trail_sim.cpp`, `shell_smokestack/smokevolume/steamjet/worldtext_sim.cpp` | 8,956 | a | simulation to C# (N3); the `*_render.cpp` siblings become the B4 draw slots |
| `shell_clientdll.cpp` | 2,852 | b | `IBaseClientDLL` (79 slots) stays; the singleton wiring it does for fork subsystems leaves with each cluster |
| `shell_impact_effects.cpp` | 2,219 | g | leaf math over surface data: C# (it already takes its decisions from `ImpactChoice.cs`) |
| `shell_te_backends_client.cpp`, `shell_te_pool_client.cpp`, `shell_tempents_client.cpp` | 4,526 | a/b | backends shrink to draw slots as cluster 3/4 move |
| `shell_soundscape_client.cpp` | 1,706 | a | `c_soundscape.cpp` moved whole: the DSP/position logic to C#, the `IEngineSound` calls stay B4 |
| `shell_sprite_render.cpp`, `shell_sprite_leaf.cpp`, `shell_surface_render.cpp`, `shell_rope_render.cpp`, `shell_dust_render.cpp` | 4,218 | g/a | mesh builders: stay as B4 executors (the one place letter g is a binding: the mesh is handed to the renderer by layout) |
| `world_adapter_client.cpp`, `shell_cliententitylist.cpp`, `shell_recvtable.cpp`, `shell_world_nettable_client.cpp`, `managed_runtime_client.cpp` | 2,425 | d/b/f/h | permanent bindings |
| `shell_client_orphans.cpp`, `shell_player_orphans.cpp` | 302 | g/d | linker-satisfaction files: empty out as their owners leave, then delete |

### 2.1 Fork TUs with no caller in the build

The census found no in-build or in-repo caller for: `glow_outline_effect`,
`flashlighteffect`, `text_message`, `classmap`, `posedebugger`,
`sequence_Transitioner`, `bonelist`, `interpolatortypes`, `sentence`,
`studio_stats`, `clientsideeffects_test`, `bone_merge_cache` (its own header
says it has no consumer), and probably `secure_command_line`. Two rules apply
before any of them is deleted (memory: *port dead stock arms*):

- a TU is dead because **stock has no use for it in this mod** (glow outline
  is a TF2/L4D feature; posedebugger and studio_stats are tools) → delete, after
  `nm -D` of the engine binaries shows no by-name consumer and the static-init
  census shows no registration;
- a TU is dead because **we bypassed the arm that called it** (flashlight from
  `C_BasePlayer`, sequence transitions from `C_BaseAnimating`, sentences from
  the sound emitter, `text_message` from the HUD) → **port** unless the managed
  tree already covers the behaviour, shown by a fact, not a grep.

C1 rules each one in `docs/client-novalve-rulings.tsv` with its evidence.

---

### 2.2 Cluster-3 TUs deferred out of C2 (lane c2-fx, 2026-10-05)

C2 ported every cluster-3 decision that stands on its own (the effect
dispatch table, IEffects, the client-side effect list and its tracers, the
shell-eject effects, the effect precache system, the smoke fog overlay; each
replayed against a recorded stock fixture, docs/client_evictions.md). These
stay compiled, each for a reason a later phase owns. Nothing in them is dead:
each is reached by a live caller today.

| TU | deferred to | why it cannot move in C2 |
|---|---|---|
| `fx.cpp` | C5 (cluster 2) | defines emitter subclasses that simulate their own particles (`CTrailParticles` and the FX_* helpers that drive `CSimpleEmitter`s); evicting it needs the managed particle simulation, or a fork relocation of the emitters, and C2 may not edit the fork |
| `fx_blood.cpp` | C5 | its blood drops and puffs are `CTrailParticles`/`CSimpleEmitter` emitters configured here and simulated per particle by the particle glue |
| `fx_explosion.cpp` | C5 | `CExplosionParticle` and the explosion emitters simulate per particle |
| `fx_sparks.cpp` | C5 | `CTrailParticles`, `CSimpleGlowEmitter` and the spark emitters simulate per particle |
| `fx_water.cpp` | C5 | the splash and ripple emitters simulate per particle |
| `fx_fleck.cpp` | C5 | `CFleckParticles` simulate per particle (impact flecks, `fx-impact-gate`'s ledger) |
| `c_tracer.cpp` | C5 | `Tracer_ComputeVerts`/`Tracer_Draw` are the mesh helpers the spark emitters (`fx_sparks.cpp`, `CTrailParticles`) and the sims' `shell_effects_sim.cpp` render per-particle tracers with; they leave with those renderers |
| `warp_overlay.cpp` | C7 (cluster 12) | `CWarpOverlay` (and fx.cpp's `CImpactOverlay` on it) is a `CGlowOverlay` subclass whose draw is the glow overlay's obstruction and sprite basis; it moves with the glow overlay |

Their decisions (which effect, how many particles, which material, the
colours) move with them, test first as everywhere else in C2.

#### 2.2.1 What the deferred fx TUs need from the C5 particle base (lane c5-particles, 2026-10-05)

Built: `ClientParticleEffects` (C#) with `IShimParticlesClient::ParticlesCreateAt`,
the managed half of `CParticleMgr` for effects no entity owns: started at a
world point on their own executor, answered by id, released when finished,
carried across `cs_cl_reload` by kind. `SimpleEmitter` (lane c2-sims) is the
`CSimpleEmitter` an effect simulates with, `UniformStream` the `random` stream,
`SphereLighting` the lit quad colour. Measured with the client's symbol graph
(`nm` over the client objects; the stale `c_te_legacytempents.o` excluded):

| TU (lines) | what calls into it | emitters and particle features it uses | what the base still lacks for it |
|---|---|---|---|
| `fx_fleck.cpp` (239) | `CFleckParticles::Create` from shell_impact_effects.cpp, fx_explosion, fx_sparks | `CFleckParticles` (a `CSimpleEmitter`): the merge list (an effect of the same name whose box stays under 120 units takes the new flecks), `CParticleCollision` moves (gravity, dampen, bounce, roll damping), ColorSizeAngle quads | a merge-by-name-and-bounds lookup on `ClientParticleEffects`; `particle_collision.cpp` (391) to C#, over a world trace that answers fraction, plane and allsolid (`MASK_SOLID_BRUSHONLY`, `COLLISION_GROUP_NONE`; `ParticlesTraceLine` answers fraction and plane, not allsolid) |
| `c_tracer.cpp` (151) | `Tracer_Draw` from fx_sparks and shell_effects_sim.cpp | none: the mesh helper for velocity-stretched quads | a tracer quad kind in the executor (`Tracer_Draw` from managed's start, delta, width, colour) |
| `fx_sparks.cpp` (1,538) | `FX_MetalSpark`, `FX_ElectricSpark`, `FX_EnergySplash` from IEffects (shell_effects_client.cpp) and impacts; `CTrailParticles` from fx_blood, fx_explosion, fx_water; `EffectOccluded` from fx.cpp; `ManhackSparks` by dispatch name | `CTrailParticles` (collision, velocity dampen, tracer quads), `CSimpleGlowEmitter` (glow sprites faded by pixel visibility), `CFleckParticles`, a dlight | the tracer kind and the collision above; a pixel-visibility answer per emitter (a query handle the executor keeps, its fraction read back); dlights through the existing `DynamicLights` |
| `fx_water.cpp` (612) | `FX_GunshotSplash`, `FX_GunshotSlimeSplash`, `FX_WaterRipple` from physics.cpp and shell_effects_sim.cpp; `CSplashParticle` and `UTIL_GetNormalizedColorTintAndLuminosity` from fx_explosion; three dispatch names | `CSplashParticle` (clip height), `CTrailParticles`, ripples; `DispatchParticleEffect( "slime_splash_01" )` | the clip-height quad (a clip plane on the quad) and the collision; the new-style dispatch below |
| `fx_blood.cpp` (596) | dispatch names only (`bloodspray`, `BloodImpact`, `HunterDamage`) | `CBloodSprayEmitter` (gravity), `CTrailParticles`, `CSimple3DEmitter` (3D particles with angles and collision), `DispatchParticleEffect` | a 3D-particle quad kind (pitch and yaw), the collision; the new-style dispatch below |
| `fx_explosion.cpp` (1,454) | `BaseExplosionEffect()`, `WaterExplosionEffect()` from shell_te_backends_client.cpp; `Explosion`, `HelicopterMegaBomb`, `WaterSurfaceExplosion` by dispatch name | `CSimpleEmitter`, `CTrailParticles`, `CFleckParticles`, `CSplashParticle`, a dlight | all of the above; it goes after fx_sparks, fx_fleck and fx_water |
| `fx.cpp` (1,386) | `FX_MuzzleEffect(Attached)`, `FX_Smoke`, `FX_RicochetSound`, the gunship and strider muzzle effects from IEffects and the tempent pool; `FX_GetAttachmentTransform`; `TeslaZap` by dispatch name | `CSimpleEmitter`, `CSmokeEmitter`, `CLocalSpaceEmitter` (particles in an attachment's space), `muzzleflash_light` | a local-space effect: the executor draws its particles through the attachment's transform each frame (managed asks the transform, or the executor follows an owner's attachment); the attachment read itself |

The new-style dispatch: `DispatchParticleEffect( name, origin, angles )`
(fx_blood, fx_water) sends the `ParticleEffect` client effect, and **no
`ParticleEffect` or `ParticleEffectStop` handler exists in this build**
(neither in `EffectDispatchTable` nor natively), so those calls draw
nothing today. Stock's handler (`ParticleEffectCallback`,
c_te_effect_dispatch.cpp) starts a `CNewParticleEffect` by index at the
origin and angles, or on an entity's attachment. It is a dead stock arm to
port (memory *port dead stock arms*) with the particles shell's one-shot
start, before or with fx_blood and fx_water.

The particle query's hit-box arm (ruled PORT, integrator 2026-10-05, memory
*port dead stock arms*): stock's `GetRandomPointsOnControllingObjectHitBox`,
`GetControllingObjectHitBoxInfo` and `IsPointInControllingObjectHitBox` asked
the controlling object's model for its hit boxes and bone transforms; the
fork's edit reduced them to the control-point fallback, which is what
`ParticleSystemQuery` answers today. **Blocked on N2** (shared bone setup and
hit-box data for managed entities): it needs a control point bound to a
managed owner and that owner's hit-box set and bone matrices. The fact that
proves it: burning_character on a managed model (ss_entityflame on the
player) records points inside the model's hit-box OBBs, against a stock
recording at the same pose, and the `rp` rows of a new
particlequery-stock recording carry hasobject 1 with points that are not the
control point.

Order (ruled, integrator 2026-10-05: one lane, this order; first the
`ParticleEffect`/`ParticleEffectStop` handlers and a check row for the shim's
hand-kept vtable order): c_tracer and particle_collision first (the two leaves everything else
draws and moves with), then fx_fleck, fx_sparks, fx_water, fx_blood,
fx_explosion, fx.cpp. Each with its probe copy recorded at the commit before
its port, as C2's were. particlemgr.cpp, particles_simple, particles_new,
particles_localspace, particles_ez and particle_simple3d leave when the last
of these and the shells that still build `CSimpleEmitter`s natively
(shell_impact_effects.cpp, shell_effects_sim.cpp, shell_dust_render.cpp,
shell_te_backends_client.cpp, shell_te_particle_render.cpp) have moved.

## 3. Design

### 3.1 What stays a fork header, and what gets written

As on the server (`plan_server_novalve.md` §3.1): the allowlist declares the
engine ABI and nothing in it is re-declared. The client roots add to the
server's: `cdll_int.h`, `icliententitylist.h`, `iclientrenderable.h`,
`iclientunknown.h`/`icliententity.h`/`iclientnetworkable.h`,
`iclientleafsystem.h`, `ivrenderview.h`, `iefx.h`, `ivieweffects.h`,
`engine/ivmodelrender.h`, `engine/ishadowmgr.h`, `engine/IEngineSound.h`,
`materialsystem/*`, `shaderapi/ishaderapi.h` (only if a type leaks),
`vgui/*` (the interfaces: `ISurface`, `IPanel`, `IInput`, `IScheme`,
`ISystem`), `inputsystem/*`, `dt_recv.h`/`client_class.h`, `view_shared.h`,
`particles/particles.h` (§3.4), `iprediction.h`, `igameevents.h`, `icvar.h`,
`filesystem.h`, mathlib. `tier1/convar.h`, `bitbuf.h`, `KeyValues.h`,
`utlvector.h`, `tier0/dbg.h` and `threadtools.h` are inside the closure, as
they were for the server: compiled against, out-of-line members ported.

Written new, in `src/engineabi/native/include/engineabi/`: `client.h` (what
our client TUs used of `cbase.h`, found TU by TU in C9), `recv.h` (the
`RecvTable`/`RecvProp`/`ClientClass` builders over the allowlisted types),
`viewsetup.h` only if `view_shared.h` cannot be a root. The naming rule holds:
a type the engine sees keeps its Valve name with K2 proving its layout; a type
only client.so sees never reuses a Valve name.

### 3.2 The UI: one Panel, managed drawing

Today: `shell_hud.cpp` is one `vgui::Panel` whose `Paint` calls
`IManagedHudClient` and whose draw primitives are `IShimHudClient`
(`HudDrawRect`, `HudDrawText`, `HudMeasureText`, `HudLocalise`,
`HudLookupBinding`, `HudTextMessageGet`). That is the whole shape; this plan
widens it to every panel the mod has:

- **One root panel per layer** the engine distinguishes (the viewport panel
  that receives mouse/keyboard focus when a menu is up, and the HUD overlay
  that never does); both are the same native class. Input events the panel
  receives (`OnMousePressed`, `OnKeyCodeTyped`, `OnCursorMoved`, focus) are
  forwarded as one `IManagedUiClient` call per event; `SetMouseInputEnabled`
  and cursor show/hide become shim slots, because the engine owns the cursor.
- **The by-name interfaces** the engine resolves (`ICenterPrint`,
  `IMessageChars`, `ILoadingDisc`, `IConsole`'s client half, `IClientMode`'s
  hooks) stay as thunks that hand their argument to managed and draw nothing.
- **Draw primitives** grow from 6 to about 14 `IShimUiClient` slots over
  `vgui::ISurface`: filled/outlined rect, textured rect (texture by id),
  textured polygon, line/polyline, text with a font handle, measure, scissor
  push/pop, **texture upload** (`CreateNewTextureID` + `DrawSetTextureRGBA`,
  so managed can rasterise its own atlases), font lookup by scheme name,
  screen size, and play-sound for UI clicks. Each slot is a primitive the
  surface already has; none decides anything.
- **The layer's shape: Vellum's, over vgui** (user, 2026-10-04: "something
  like Vellum, but with vgui"). UI code is immediate-mode and produces a
  **render list** each frame (filled and textured quads, glyph runs, scissor
  rects); one backend, `VguiRenderer`, turns the list into `IShimUiClient`
  calls. The render list is what tests assert on (no engine needed), what the
  `/ui` route serialises, and why the layer holds almost nothing across a
  reload. Whether the layer *is* [VellumUI](https://packages.nuget.org/packages/VellumUI)
  (its `Vellum.Rendering.IRenderer` implemented over our slots; its text
  rasteriser's atlases uploaded through `DrawSetTextureRGBA`, or its text
  swapped for vgui fonts) or an in-house layer of the same shape is the C3
  spike's first ruling: adopt Vellum if its renderer interface maps one to one
  onto the slots, its text can be routed either way, and it carries no static
  state across a UI-assembly reload; otherwise write the same shape ourselves
  (the render list, a dozen widgets, layout helpers: small). Myra
  (`Myra.PlatformAgnostic`, retained, pure C#) is the fallback if a retained
  tree turns out to be needed; it fits the same slots.
- **The web option** (user, 2026-10-04). Stock already does this for one
  panel: the MOTD window is `vgui::HTML`, which is Steam's offscreen browser
  (`ISteamHTMLSurface`, 127 uses in `vgui_controls/HTML.cpp`: the Steam
  client's CEF paints BGRA buffers with dirty rects into `HTML_NeedsPaint_t`
  and takes input back through the same interface). Done from managed over the
  Steam flat API, a web UI needs exactly the native surface the render list
  needs (one Panel, texture upload, input events) and nothing else. The page
  talks to the game over a local WebSocket served by managed (the debug
  channel's handshake exists, `src/devapi/DebugChannelHandshake.cs`); the repo
  already builds and tests a web UI (`src/webui`, vitest). What decides it:
  an extra frame of latency and a BGRA copy per repaint, wrong for the HUD
  (numbers every frame) and fine for menus; a Steam-client dependency for
  drawing (no Steam, no UI: `-nosteam`, offline and the headless gates must be
  checked in C0's probes); two languages for mods; a JS test leg.
  **Recommendation:** the render list for the HUD and every per-frame overlay;
  the web page as the C3 spike's second arm for the *menus* (scoreboard, team
  menu, MOTD, which is a web page already), with the kill criteria above. The
  gate reads the same `/ui` route either way (the page reports its DOM state
  over the socket).
- **Its own assembly** (user, 2026-10-04). The layer lives in
  `SourceSharp.Client.Ui`, loaded by the client composition into its own
  collectible `AssemblyLoadContext`, so a UI-only change reloads the UI alone:
  unload, load, construct the layer again through DI; no entity save/restore,
  no trap table over the game's vtables, a fraction of a full `cs_cl_reload`.
  Rules that make it work. **Three assemblies, by what they may contain**
  (user, 2026-10-04: a `*.Interface` assembly holds interfaces, enums and
  pure data structs, never behaviour):
  - `SourceSharp.Client.Interface` (never reloads): `IUiLayer`, `IUiBackend`,
    `IUiScreen`, the `ScreenInfo` struct, the render-list primitives
    (`UiQuad`, `UiTexturedQuad`, `UiGlyphRun`, `UiScissor`, as readonly record
    structs) and the anchor/unit enums. Nothing with a method body. A fact
    walks both `*.Interface` assemblies and fails on any type that is not an
    interface, an enum, or a record/struct whose only members are fields,
    auto-properties and compiler-generated ones; it lands in C0 and covers
    `Server.Interface` too.
  - `SourceSharp.Client.Ui.Toolkit` (reloads with the UI): the behaviour,
    i.e. the immediate-mode layout engine, the widgets, the render-list
    builder, the font/atlas cache policy, and VellumUI if adopted. Loaded into
    the UI `AssemblyLoadContext` beside the screens, so a toolkit change is
    also a UI-only reload, and a toolkit type never crosses into the core (the
    core only ever sees `Interface` structs, which is what makes the ALC split
    safe).
  - `SourceSharp.Client.Ui` (reloads with the UI): the game's screens
    (scoreboard, team menu, …) as `IUiScreen` implementations over the
    toolkit, composed by DI. Mods that ship UI are the same shape: screens
    over the toolkit, loaded where the mod-assembly pattern loads them, with
    the toolkit in the same context.
  - `VguiRenderer` is behaviour with shim access, so it lives in the core
    (`SourceSharp.Client`), consuming the render list as `Interface` structs.
  The core holds the UI only through `IUiLayer` and drops the reference on
  unload (the `ManagedCaches` discipline: one cached delegate or Type from the
  UI context keeps the old UI alive); native never sees the UI object
  (`IManagedUiClient` is implemented by the core, which forwards to the current
  layer); the carried state is the open-panel set and scroll positions, as
  bytes. The file watcher classifies a deploy: only `SourceSharp.Client.Ui.dll`
  or `SourceSharp.Client.Ui.Toolkit.dll` changed → UI reload; anything else →
  the full side reload. Mods that ship UI
  follow the same shape (the mod-assembly pattern exists). The web arm takes
  this further: a page reload is no compile at all. The `/reload` route
  reports which kind ran and how long it took, and the `hot-reload` step gets
  a `ui-only` arm (reload the UI assembly with the scoreboard open: still
  open, same rows, old context dead).
- **Resolutions, aspect ratios, and changing them live** (user,
  2026-10-04). Nothing in the layer is a pixel. Layout is expressed in
  **anchors + units**: an element is placed relative to an edge, corner or
  centre of the screen (or of its parent), with offsets and sizes in design
  units that scale by height, capped by width (`scale = min(screenH / 1080, screenW / 1920) × ui_scale`, user ruling 2026-10-05, see plan_client_ui.md §5.1; where
  `ui_scale` is a user convar for DPI and taste), so 4:3, 16:9, 21:9, 32:9 and
  portrait keep every element on screen and at the same visual size, and a
  wider screen gains space between anchored groups rather than stretching
  them. Text sizes are design units too: vgui fonts are rasterised per pixel
  height, so the backend keeps a font cache keyed by (scheme name, pixel
  height) and re-requests on change; a self-rasterised atlas (the Vellum text
  path) is regenerated at the new scale and re-uploaded under the same
  texture id. **Screen size is an input, never a cache:** the render list is
  built each frame against `ScreenInfo {w, h, scale, safeInsets}` read from
  the surface, and the Panel's `OnScreenSizeChanged(oldW, oldH)` is forwarded
  as `IManagedUiClient.ScreenSizeChanged` so caches (fonts, atlases, the web
  surface's `SetSize`) invalidate the same frame; `/ui` reports the
  `ScreenInfo` in force. A video-mode change (`mat_setvideomode`, alt-enter,
  a window dragged to another monitor) therefore needs no restart and no
  reload. The web arm is responsive by construction (CSS media queries), and
  gets the same `SetSize` call. Mods see only anchors and units: the API has
  no pixel overload. Proof: a hermetic fact lays out the scoreboard at
  1024×768, 1920×1080, 2560×1080 and 1080×1920 and checks every quad is on
  screen, anchored elements sit at their proportional positions and font
  heights scale with the height; and the `ui-resize` live step (§5.3) changes
  the mode three times with the scoreboard open.
- **A second backend: the test renderer** (user, 2026-10-04: "a visual rep
  of what it should look like in game"). `UiTestRenderer` implements the
  same `IUiBackend` as `VguiRenderer` and draws a render list to **SVG**
  (pure C#, no raster dependency: diffable text, rendered by any browser,
  a PNG one click away in the dev web UI). Fonts: vgui fonts do not exist
  offline, so `/ui/fonts` records each scheme font's metrics (height,
  advances) from the running game once into `Fixtures/ui-fonts.tsv`; the
  test renderer measures with those and emits a web font of matching
  metrics, so layout in the preview matches layout in game to the pixel and
  only glyph shapes differ. Three uses:
  1. **Previews**: `tools/ui-preview` renders every `IUiScreen` with sample
     state (a 12-player scoreboard, a team menu mid-hover, the MOTD) at the
     four §3.2 sizes into `bin/<config>/ui-preview/<screen>-<w>x<h>.svg`; a
     screen's review starts from its SVGs.
  2. **Goldens**: `Golden/Ui/<screen>-<w>x<h>.svg` per screen and size,
     checked by a fact; a layout change is a visible SVG diff in the commit,
     regenerated deliberately (`UI_GOLDEN_WRITE=1`), never on the fly.
  3. **Live**: the dev web UI (`src/webui`) gets a "UI" panel that draws the
     live render list from `/client/{slot}/ui` on a canvas beside the game,
     with hit-test highlighting, so what the game is about to draw can be
     inspected frame by frame without a screenshot.
  Fidelity between the two backends is a gate, not an assumption: the
  `ui-fidelity` fact feeds one render list to both and compares the
  **draw ledger** `VguiRenderer` emits (primitive kind, bounds, texture id,
  font, text) against the SVG's elements one to one; vgui's glyph rasterisation
  is the only thing it cannot compare, and the engine's `jpeg` screenshot
  stays the way to eyeball that. The web arm needs none of this: a page in a
  browser is its own preview. The test renderer lands as C3's first step, so
  no screen is written without a picture of it.
- **The inventory: 27 fork panel classes, and what the engine actually
  asks for.** `grep` over the 29 compiled vgui TUs and their headers for
  classes deriving from `vgui::Panel`/`EditablePanel`/`Frame`/`Label`/
  `Button`/`Menu`/`HTML` finds 27 (plus our `CSourceSharpRootPanel`, which is
  the one panel and stays):

  | group | classes | fate |
  |---|---|---|
  | viewport screens (`IViewPortPanel`) | `CClientScoreBoardDialog`, `CTeamMenu`, `CSpectatorGUI`, `CSpectatorMenu`, `CTextWindow` (+ `CMOTDHTML`), `CNavProgress` | 5 re-made as managed screens; `CNavProgress` (nav-mesh generation progress) is a C1 delete candidate |
  | global panels `VGui_CreateGlobalPanels` makes | `CCenterStringLabel`, `CMessageCharsPanel`, `CLoadingDiscPanel`, `CDebugOverlay`, `CVoicePanel` | 5 re-made as managed screens (centre print, message chars, loading disc, debug-overlay text, voice indicator) |
  | moved to the dev API | `CFPSPanel`, `CBlockingFileIOPanel`, `CNetGraphPanel` | 3 panels deleted; what they showed becomes the `frame` and `netgraph` routes (§4.4; `io` dropped, see §8), drawn by the dev web UI |
  | deleted by ruling | `VideoPanel`, `VideoPlayerPanel` | 2 deleted (C1) |
  | widgets and plumbing the toolkit replaces | `CBaseViewport`, `CBackGroundPanel`, `CommandMenu`, `CSpecButton`, `CIconPanel`, `CBasePanel`, `CHudLabel`, `CBitmapButton`, `CBitmapPanel`, `CAvatarImagePanel` | 10 retired; the toolkit's widgets and the Steam avatar fetch (managed, flat API) take their place |

  So **10 in-game screens** are written in managed, not 27 panels; the HUD's
  elements are already managed (`shell_hud_framework.cpp` removed the stock
  HUD). GameUI's screens (§3.2a) come on top: 8 more, of which the server
  browser is the big one.
  **None of them is an engine contract.** `strings` over the engine the game
  runs on (Source SDK Base 2013 MP, appid 243750, `bin/linux64/engine.so`; C0 probe §8: the TF2 build agrees) shows it resolves from
  the client only `VClient017`, `VClientEntityList003`, `ClientLeafSystem002`
  and the optional `ClientVirtualReality001`; `ICenterPrint`'s `EXPOSE` is
  client-internal, and `IFPSPanel`/`INetGraphPanel`/`IMessageChars`/
  `ILoadingDisc` are globals the client's own `vgui_int.cpp` creates. The only
  vgui object the engine sees is the root panel the client returns for
  `PANEL_CLIENTDLL`, which is exactly the one-panel design. (C0's probe still
  confirms the engine tolerates `ClientVirtualReality001` being absent.)
- **The home screen is ours too** (user, 2026-10-04: "the UI system should
  cover the main menu, all dialogs, game load UI"). See §3.2a.
- **Mods** get the layer through DI (`IUiLayer`) like every other service;
  `vgui` `.res` layout files are not read: layouts are code or data the mod
  owns.
- vgui_controls (77 archive members, 660 direct imports) and matsys_controls
  leave the link when the last fork panel is gone; `vgui_controls.cpp` and
  `panelmetaclassmgr` go with them.

### 3.2a GameUI: the main menu, the dialogs, the loading screen, the console

What the engine needs from the GameUI module is small and known. `strings`
over the 243750 install's `engine.so` (the one `mods.yaml` runs; TF2's agrees) shows it resolving exactly two interfaces
from it: **`GameUI011`** (38 virtuals, `common/GameUI/IGameUI.h`, in the fork)
and **`GameConsole004`** (6 virtuals, `IGameConsole.h`). `VGuiModuleLoader003`
and `RunGameEngine005` appear only inside `GameUI.so` itself: they are what
GameUI offers to the vgui modules it loads (`ServerBrowser.so`), not what the
engine asks for. The stock `GameUI.so` needs `libtier0`, `libvstdlib`,
`libsteam_api` and SDL2; ours needs none of them.

**How we get in: the launcher** (user: "can we hook/shim the GameUI.so in our
launcher?"). Yes, and without interposing `dlopen`. The launcher already
composes an engine directory view for the dedicated server
(`Launcher.EngineView`, `src/launcher/Launcher.cs:327`, used at `:153`:
links to every entry of the install, run from there). The client gets the
same: `engines/client`, every TF2 `bin/linux64` entry linked **except
`GameUI.so`, which is ours**. If the engine resolves `GameUI` through
`GAMEBIN` rather than `EXECUTABLE_PATH` (its strings carry both, so only a
run can tell), the view is not even needed: the file in our game dir's
`bin/linux64` wins. C0's probe is a logging stub `GameUI.so` placed first in
the game bin, then in a view; whichever is loaded is the mechanism, and the
stub's log of every `IGameUI` call across a full session (launch, menu, map
load, play, disconnect, quit) is the **oracle** the replacement is written
against. A `dlopen` interposer stays the fallback if neither path is taken.

**The module: `gameui_sourcesharp.so`, a forwarder with a strangler inside.**
It exports `CreateInterface`. `GameUI011` and `GameConsole004` are B1 thunks
into managed (`IManagedGameUi`, `IManagedGameConsole`, generated like every
other `IManaged*Client` area); it links nothing but libc and
`libmanaged_host`. For the length of the migration it can also `dlopen` the
stock `GameUI.so` from the install and **forward any call managed does not
yet answer** (per method, decided by managed: `Handled`/`Forward`), so the
stock options dialog or server browser keeps working on the day the main menu
is ours. The forwarding count is a ratchet (`/client/{slot}/gameui` reports
it); it reaches zero when the last stock dialog is replaced, and the `dlopen`
goes with it. Ordering: whether the engine acquires `GameUI011` before or
after `VClient017` is a C0 probe; if GameUI comes first, the thunks queue
`Initialize`/`PostInit` until the managed root exists (one CoreCLR per
process; the host is already there), and `RunFrame` is a no-op until then.

**Step zero needs no module at all.** `IGameUI` 011 has
`SetMainMenuOverride(VPANEL)` and `SendMainMenuCommand(const char*)`: the
client hands the stock GameUI a panel of its own to use as the main menu, and
asks it to open its dialogs by name. That is how the TF2 client supplies its
main menu, and this engine is the same Source 2013 MP branch (C0 probe: the override is honoured on 243750). So the first commit of C4 is a managed
main menu on our one panel, parented through the override, with every button
still opening a **stock** dialog. The module replaces GameUI only after that
is green; each stock dialog then falls to a managed screen.

**The screens, in the order they land** (each a managed `IUiScreen`, each
with its SVG goldens and a live step):

| screen | driven by | needs from the engine |
|---|---|---|
| main menu, pause menu (ESC in game) | `OnGameUIActivated`/`Hidden`, `IsMainMenuVisible`, `SetMainMenuOverride` | the background map renders underneath; nothing else |
| loading screen | `OnLevelLoadingStarted(bShowProgress)`, `UpdateProgressBar(progress, text)`, `SetShowProgressText`, `SetProgressOnStart`, `OnLevelLoadingFinished(error, reason, extended)` | the engine pumps vgui from inside the progress callback, so the paint runs on the engine thread mid-load (precedent: `plan_map_load` P6 pumped the window at the load steps the client owns) |
| disconnect / failure dialogs, confirm quit | `OnDisconnectFromServer(eSteamLoginFailure)`, `OnLevelLoadingFinished` with `bError`, `OnConfirmQuit`, `ShowMessageDialog` | the reason strings; `engine->ClientCmd_Unrestricted("quit")` |
| console | `GameConsole004`: `Activate`, `Hide`, `Clear`, `IsConsoleVisible`, `SetParent` | console text through `ICvar::InstallConsoleDisplayFunc` (a shim slot); commands through `ClientCmd_Unrestricted`; the dev API's `/console` already reads the same stream |
| options: video, audio, keyboard, mouse, voice, multiplayer | menu commands | convars (managed already); key bindings through `IGameUIFuncs` (`HudLookupBinding` exists) and `bind`; video modes through two new B4 slots over `IMaterialSystem::GetModeCount/GetModeInfo` and `mat_setvideomode` (which `ui-resize` already exercises) |
| create server | menu command | `map`/`maxplayers`/`sv_*` convars and the mod's map list (`maplist.txt`, managed) |
| player list / mute | menu command | the voice-ban path the client already has (`GameClientExports001`) |
| server browser | menu command | `ISteamMatchmakingServers` through the managed Steam flat API (Steam is managed from C6 on). **Last**, because it is the largest; until then the stock browser runs through the forwarder |

Not implemented, by ruling (a multiplayer mod): new game / chapters, load /
save game, bonus maps, achievements, commentary, credits, the Xbox session
and storage-device methods. Each of those `IGameUI` methods answers inertly
and is listed in `docs/absence-client.md` as one ruling row, "GameUI
single-player surface".

**What this changes elsewhere.** K1 gains the new module's export set
(`GameUI011`, `GameConsole004`, nothing else). `make client-only-gate`
(client.so with no server) becomes the main-menu gate. The hot-reload table
gains a row: native keeps the override `VPANEL` and the engine's notion of
"GameUI active"; managed carries which screen is up; a reload with the pause
menu open leaves it open. `ui-resize` runs once with the main menu up. The
UI assembly split (§3.2) covers these screens unchanged: they are
`IUiScreen`s in `SourceSharp.Client.Ui`.

### 3.3 The renderer: decisions in C#, one native executor

`viewrender.cpp` is a 6.5K-line decision tree that ends in a few dozen engine
calls per frame. The design separates the two:

- **The plan.** Each frame managed builds a `RenderPlan`: an ordered list of
  passes (3D skybox, world, opaque entities, translucent, water reflection /
  refraction with their clip planes, monitors, view model, screen-space
  effects, overlays), each with its `CViewSetup` fields and flags, plus the
  frame's fog, bloom/tonemap/motion-blur parameters and screen fade/shake.
  It is a flat struct array in a pinned buffer: one crossing per frame to hand
  it over, as the physics event ring is one crossing per step.
- **The executor** (`shell_view_executor.cpp`, B2+B4) replays the plan against
  `IVRenderView`, `IVModelRender`, `IMatRenderContext` and the leaf system. It
  holds the render targets and material handles (pointers held). It contains
  no `if` on game state: the plan says what to draw, the executor says how to
  ask the engine. An executor that needs a decision missing from the plan is a
  plan schema change, never a native branch.
- **Leaf system and shadow manager** are engine-called (`IClientLeafSystemEngine`
  is resolved by name; the shadow manager is driven by the engine's shadow
  callbacks every frame for every shadow). They are rewritten as ours under a
  Valve header, kept native as B1 machinery, and stripped of policy: shadow
  choice per entity (`ClientRenderPolicy.Shadow`, written but unreached: this
  closes `docs/client-shell.md` §1's two slots), fade distances with the FOV
  factor (absence row 6: needs `fov` on `PlayerNetState`), detail-object
  density and sort parameters, come from C# as per-level or per-entity policy
  blocks, read by layout as the engine block is today.
- **Video-mode changes.** The engine hands `View_Render` the new viewport
  each frame, so the plan's `CViewSetup` fields (width, height, aspect, FOV)
  are per-frame inputs, never cached in managed. The executor owns render
  targets and registers the material system's release/restore callbacks
  (`AddReleaseFunc`/`AddRestoreFunc`, as stock `rendertexture.cpp` relies on)
  so a mode change recreates them at the new size with no managed
  involvement; `/render` reports the viewport in force, and `ui-resize`
  (§5.3) reads it after each change.
- **Budget.** One plan crossing + one bone batch crossing + the draw slots the
  effect renderers already use. Measured in C0 against today's native frame
  (`live-gate-budget.tsv` gets a `render` row recorded, not chosen) and
  re-measured when each pass moves. CLAUDE.md's rule applies: the move lands
  first; a blown budget is a boundary-design task (batch the plan, suppress
  the GC transition on the executor entry), never a retreat to C++.

### 3.4 particles.a stays, as a bound library (user ruling)

`particles.a` + `dmxloader.a` are treated like vphysics: engine-grade
libraries the client binds to. The allowlist admits `particles/particles.h`
and the dmx headers it needs; the ratchet's "forbidden libraries" list leaves
them out by name with this ruling cited. Native keeps `CNewParticleEffect`'s
`IClientRenderable` shell and `IParticleSystemQuery`; which system to start,
on which attachment, for how long, and the per-level definition reload
(`ParticleReloadPolicy.cs` already) are C#. The old `CParticleMgr` simple
particles (sparks, smoke, dust, embers) are not the library: their simulation
moves to C# (cluster 2/3) and their quads go through the draw slots.

### 3.5 Datatables stay a layout binding

The engine walks `ClientClass`/`RecvTable`/`RecvProp` by layout and calls the
proxy function pointers; `shell_recvtable.cpp` already builds them from C#
declarations. The fork `dt_recv.cpp` and friends are replaced by our builders
in `engineabi/recv.h` with K2 parity over `RecvProp`, `RecvTable`,
`ClientClass`, `CRecvProxyData` and `CRecvDecoder`'s visible part; the proxy
bodies that convert (`RecvProxy_IntToEHandle`, the vector/angle/string ones)
are ported (letter f, binding), the ones that decide anything hand the value
to managed. `interpolatedvar.cpp`'s instantiations go: `Interpolation.cs` is
the implementation and the recorded-fixture oracle its proof.

### 3.6 Tier libraries, Steam, allocation, logging: the server's answers

Every ruling in `plan_server_novalve.md` §3.2–§3.6 carries over unchanged, and
most of the code already exists on the server side and only needs to compile
on the client target too: `convar_port.cpp` (declarations in C#, one carrier),
`tier1_port.cpp`, `tier_connect.cpp`, the tier0 spew/thread/time ports,
`vstdlib_random_port.cpp`, `MD5_PseudoRandom` (done on the client,
`md5_pseudorandom.cpp`), `alloc_check`, the version script. Client additions:
`ConVar_ServerBounded` (`cdll_bounded_cvars`) becomes a managed rule over the
replicated value; `KeyValues` parsing at the client's own sites (scheme/res
files are not read, so these are the sound and particle manifests, already
C#); Steam avatars and the Steam Controller go through C#'s flat-API calls.
`bitmap.a` and `vtf.a` are only needed by the camo proxy and the vgui bitmap
panels: they leave with clusters 1 and 14.

### 3.7 Order of the moves

By subsystem, each move removing its fork TUs **and** its shim logic in the
same batch, so a tier library leaves the link in the commit where its last
importer leaves (an archive with a live importer fails the link; that is the
gate). The header move (C9) runs last as waves over TUs, like the server's P5,
because until then moved and unmoved TUs link together through the Valve
names.

### 3.8 Hot reload: every port must survive `cs_cl_reload`

What exists (`plan_managed_vtable_reload.md`, `plan_abi_hotreload.md` D6):
a reload is one synchronous call at a frame boundary — save, unload the
side's collectible context, load, patch every managed-built vtable **in
place**, restore — with a trap table under every slot for the duration.
Native acquires each `IManaged*Client` object once per process and never
sees a NULL. State crosses as **bytes only** (`ReloadStore`, per side;
`ReloadCarrier`'s sections `entities`, `io`, `shell-less`, `parts`), because
one object or Type held in the default context keeps the old assembly alive
and the reload silently runs the code it was meant to replace
(`ManagedCaches` exists to drop those). The DI container is rebuilt on
load, so every service starts from its constructor. Two client facts matter
for this plan: **the client entity base carries only its `[NetState]`
objects** (`SaveTiers.cs`: `[SaveField]` on a client class is silently
dropped today), and **nothing but entities is carried at all** — no service
state. Every subsystem this plan moves is a service, not an entity, so the
reload story has to be designed per subsystem, not inherited.

The principle: **native owns handles, managed owns decisions.** A handle
(texture id, render target, particle-system instance, shadow handle, pool
slot, font handle, `kbutton_t`) lives in a native shell and survives the
swap; managed holds it as an integer and must either re-derive it or carry
it as bytes. A decision is recomputed on the next frame from inputs that
were themselves carried or are re-read from the engine. Anything managed
holds that is neither (a GC handle native stored, a delegate, a Type) is a
reload bug by construction, and the trap table finds the call.

| subsystem | native keeps across the swap | managed carries (bytes) | recomputed after restore | proves it |
|---|---|---|---|---|
| UI (§3.2) | the Panel, the surface's texture ids and font handles, focus/cursor state | which panels are open, scroll/selection per panel, uploaded-texture id→name map | layout, every frame's draw list | reload with the scoreboard open: it is still open, same rows, no second upload of its atlas (upload counter flat) |
| render plan (§3.3) | render targets, materials, the executor's last plan buffer (replayed unchanged for the one frame the swap takes) | screen fade/shake in flight, per-level policy blocks | the whole plan, next frame | reload at the fixed pose: the pass sequence before and after is byte-equal |
| effects / tempents / beams / ropes | pool slots and their renderables, mesh buffers | per-slot lifetime, velocity, kind (the pool's `ReadLife/WriteLife` slots already cross this way) | nothing | reload with 20 TEs alive: the same 20 slots alive after, counters continue, none freeze or duplicate |
| particles glue | `CNewParticleEffect` instances (the library owns them) | instance handle → owning entity/attachment, requested lifetime | follow-points | reload with a looping system attached: one instance, still following |
| material proxies | the proxy objects the material system holds | nothing | `OnBind` output from the entity's current state | reload with an animated texture on screen: frame counter continues, no bind refusal |
| input | `kbutton_t` states, the engine's command buffer | mouse accumulators, camera mode/distance, controller mapping cache | the next usercmd | reload while `+forward` is held: the next usercmd still moves forward; no stuck key |
| leaf/shadow policy | shadow handles, leaf links | nothing | per-entity policy on next `Policy` read | reload: shadow-handle count unchanged, policy counter flat |
| prediction / physics callbacks | the callback objects | the ring's tolerance table, last acked command | the ring refills | `second-client` + reload: no mispredict burst above the budget row |
| soundscapes / envelopes | engine sound patches (guids) | guid → owner, envelope targets | the next envelope step | reload inside a scape: the same patches continue (no new guids) |
| registries (user messages, convars, commands, nettables) | the carriers and the `ClientClass` list (the engine walked it once; **never rebuilt**) | nothing | the managed handler tables, re-registered against the same native carriers | the handler count before = after, and a message received across the swap is handled once |

Design rules this adds:

- Every ported service that holds per-level or per-frame state implements
  **one** interface, `IReloadParticipant` (`Save(ReloadWriter)` /
  `Restore(ReloadReader)`), registered through DI, walked by the client
  module in its save and restore steps; the section name is the service's
  contract name, so a renamed service fails loudly rather than restoring
  nothing. This is the client's missing "service tier" beside the entity
  tiers; the entity base's `[SaveField]` gap closes with it (`SaveTiers`).
- A native shell never stores a managed delegate or GC handle except through
  a managed-built interface's block. No `IManaged*Client` grows a slot that
  returns a managed object.
- An `IShim*` slot that returns a handle returns an integer the engine
  issued, never a pointer managed would hold.
- The file watcher that triggers a reload on `dotnet build` stays the
  deploy path (CLAUDE.md: a managed change is deployed by `dotnet build`);
  the `hot-reload` live step grows a per-subsystem arm per the table's last
  column, run in both passes, plus a **leak arm**: after the swap, the old
  context's `WeakReference` is dead within N GCs and `ManagedCaches` reports
  a drop count of exactly **zero** (C0 findings lane, measured: since
  statics-to-zero the client binds no `ManagedCaches` route, so Forget has
  nothing to drop; -1/missing means Forget was never reached, > 0 means a
  route outlived Shutdown). The old-context `WeakReference` is the leak
  proof; the count guards the two failure shapes.
- No reload mid-call: the executor and the UI paint are managed calls, so
  the frame-boundary rule covers them; the trap table proves it.

---

## 4. Gates and instruments

A gate can fail, and every removal gate has a retention half.

### 4.1 Removal gates (ratchet rows, fall only)

| # | gate | instrument |
|---|---|---|
| R1 | fork headers in client.so's dep closure not in `docs/client-fork-allowlist.txt`: 1,060 → ~150 (the server closed at 157 total, 45 outside) | `tools/client-novalve-census` (C0): `ninja -t deps`, `readlink -f`, set difference; a missing deps log is a failure |
| R2 | fork sources in the target: 157 → 0 | same, over the `.o` inputs of `out/client.so` |
| R3 | forbidden archives/libs on the link line: `tier0 tier1 vstdlib tier2 tier3 bitmap vtf steam_api vgui_controls matsys_controls`, `--wrap=`, `devtools/` → 0 (particles/dmxloader exempt by name) | `ninja -t commands out/client.so` |
| R4 | `DT_NEEDED` on `libtier0.so`/`libvstdlib.so`/`libsteam_api.so` → 0 | `readelf -d out/client.so` |
| R5 | include directories outside `public`, `public/tier0`, `public/tier1`, `public/mathlib`, `vgui2/include` (interfaces only) and ours | `compile_commands.json`, every client entry |
| R6 | native ConVar/ConCommand declarations in client TUs → 0 | the census, anchored on whole declarations |
| R7 | LOGIC lines in `src/client/native` by the N0 function ledger → 0 or a named ruling each | `tools/physics-scope`'s shape generalised (N0) |
| R8 | `sdk-links/client.txt` → 0 lines; `shared.txt` only what both still use | `make sdk-census` |

### 4.2 Retention gates (must not change, or change only as named)

| # | gate | instrument |
|---|---|---|
| K1 | the exported interface set of client.so (`VClient017`, `VClientEntityList003`, `ClientLeafSystem002`, `IEffects`, `VClientTools`, the proxies, `GameClientExports001`, …) and, from C4, of `gameui_sourcesharp.so` (`GameUI011`, `GameConsole004`, nothing else) | `nm -D out/client.so` + the `CreateInterface` registry dumped by a gtest, diffed against the C0 snapshot; a name that leaves needs a ruling row |
| K2 | layouts the engine reads on the client: `RecvProp`, `RecvTable`, `ClientClass`, `CRecvProxyData`, `CViewSetup`, the engine block's client tail, the studio records, `vcollide_t`. **Ruled out** (user, 2026-10-04, on lane abi's measurement at c6c53802): `CPlayerState`, `kbutton_t`, `CUserCmd`: nothing reads them by layout on the client, so they get no K2 row and are not exposed to managed; revisit when a port takes over the write side (C6: `CUserCmd` encoding, `kbutton_t` state), adding the row in that commit | `tools/abi-parity` gains a client type list and compiles the probe with the client's include set (C0); mutation-proved |
| K3 | the recv tables: full dump (names, types, flags, bits, offsets) at load, and the send/recv agreement (`cs_cl_payload_agree`) | `tools/novalve-snapshot` gains client exports and `nettables` from the client side (C0) |
| K4 | ports match stock | oracle facts: recorded-fixture oracles (the `interpolation-stock.tsv` recipe: record from the code being REPLACED, with arm state, before the port; replay after) for every arithmetic port: beam/sprite mesh vertices, rope verlet positions, material-proxy outputs, mouse/joystick → usercmd, view setup fields per pass, shadow/fade decisions. Upstream-vs-ours gtests where a port is native (recv proxies, the executor's call sequence against a recorded stock sequence) |
| K5 | live behaviour | `make live-gate` (both map loads) with the client steps grown as in §5.3; `make client-only-gate`; a full join that reads a client value written by the server; the counter A/B gates (`7a-effect-gate`, `7c-view-gate`, `fx-impact-gate`, `7e-particle-sound-gate`) kept green |
| K6 | test sizes | `tools/leg-floor`, not below C0 except tests deleted with their subject and named in the commit |
| K7 | the two ruled deletions (VR, video) and nothing else in `docs/absence-client.md` at the close | the file, read by a fact |

### 4.3 Instruments to build (C0), because the server's have no client arm

| tool | gap | what C0 adds |
|---|---|---|
| `tools/server-novalve-census` | server target only; its only "client" logic is treating `CLIENT_DLL` branches as dead | `tools/client-novalve-census` (or `--target client`): R1–R8 over `out/client.so`, `docs/client-fork-roots.txt` → `docs/client-fork-allowlist.txt`, `docs/client-novalve-ratchet.tsv`. Self-tests: a `game/` include in a client TU goes red; a `game/` line in the allowlist goes red |
| `tools/abi-parity` | server types, server include set | a `side` column in `docs/abi-parity.tsv`; the client probe TU |
| `tools/novalve-snapshot` | `nm -D server.so`, server nettables | client exports; recv-table dump through `cs_cl_*` or a new `/client/{slot}/nettables` route |
| `tools/devapi` `/client/{slot}/…` | closed set: `status`, `prediction` | `entities` (the client's own list: class, origin, render handle), `render` (last frame's plan summary and pass count), `ui` (visible panels and their hit-test tree), `input` (key states, last usercmd); each a `DebugChannelQuery` kind |
| `tools/live-gate` | 4 of ~30 steps read the client | §5.3's steps |
| a function-level LOGIC ledger (N0) | none for the client | `tools/shim-scope` generalising `tools/physics-scope`: `fnmap.tsv` + `rulings.tsv` per file, ratchet on LOGIC lines |

### 4.4 The dev API surface for the client: test, debug, troubleshoot

Today the client answers two questions over the debug channel
(`DebugChannelQuery.Status`, `.Prediction`), proxied by the server at
`/client/{slot}/…`; everything else is a `cs_cl_*` console command whose
output is scraped from `console.log`. The server's own routes (`/entity/…`,
`/events`, `/nettables`, `/convars`, `/map/…`, `/entity/{n}/payload`) are
what made its ports gateable. Each subsystem this plan moves gets a read
route **before** it moves (C0 and the first commit of each phase), so the
port's gate reads the same instrument the troubleshooting session will. A
route is a `DebugChannelQuery` kind answered by a `IDev*Client` service in
the client container (the P3 pattern: `ClientDevBuild`, `ClientDevLog`),
JSON over the channel, server-proxied, with a golden in
`Golden/DevApi` (`DevApiClientGoldenTests`). Reads are free; **writes** are
cheat-gated and named as such.

| route (`/client/{slot}/…`) | answers | used by |
|---|---|---|
| `status`, `prediction` | exist | — |
| `entities[?class=]` | the client's own entity list: index, class, origin, model, render handle, dormant, interpolation ring depth, shadow handle, policy block | every cluster; `second-client`, `shadow-policy`, `flashlight` |
| `entity/{n}` | one entity's `[NetState]` blocks as JSON (the client twin of `payload`), its interpolated vs latest values | datatables (C8), interpolation, prediction |
| `render` | last frame's plan: pass list with kind, view setup fields, clip planes, RT binds; the executor's per-primitive counters; ms per stage | C7 at every step; `render-plan` |
| `render/compare` (write, cheats) | run the stock path and the plan path on the next frame at the same pose and return the sequence diff | C7.1 A/B |
| `ui` | the open panels, each with its widget tree, bounds, focus, hit-test at `?x=&y=`; upload/draw counters; the chosen layer; the `ScreenInfo` in force (w, h, scale, insets) | C3; `ui-scoreboard`, `ui-teammenu` |
| `ui/event` (write) | inject a key or mouse event into the managed layer | `ui-teammenu` |
| `ui/fonts` | each scheme font in force: name, pixel height, ascent, advances | the `ui-fonts.tsv` fixture for the test renderer |
| `input` | key states, mouse accumulators, sensitivity chain, last 16 usercmds decoded, controller mapping in force | C6; `input-usercmd` |
| `input/inject` (write, cheats) | a scripted mouse delta / key press for one frame | `input-usercmd` |
| `effects` | pool occupancy by kind, live TEs with lifetimes, beams/ropes/sprites with segment counts, the effect ledger counters (`shell_effect_ledger.h` today) | C2; `rope-settle`, `beam-ring`, `7a-effect-gate` |
| `particles` | `CNewParticleEffect` instances: system name, owner, attachment, age; definition reload count | C5; `7e-particle-sound-gate` |
| `proxies` | registered proxy names, binds per frame per name, last output per bound material | C5; `proxy-animated` |
| `shadows` | shadow handles by entity, the policy decision and the reason, fade factor in force (incl. the FOV factor) | C7.3; `shadow-policy` |
| `sound` | active patches/envelopes, current soundscape and its positions | C2 |
| `nettables` | the client's recv tables as the engine sees them (names, types, bits, offsets) and the send/recv agreement result | K3 |
| `reload` | last reload: sections carried with byte sizes, participants saved/restored, drop count from `ManagedCaches`, old-context liveness | §3.8's leak arm; `hot-reload` |
| `reload/trigger` (write) | request a client reload at the next frame boundary (what `cs_cl_reload` does, scriptable) | `hot-reload` arms |
| `frame` | `ClientFrameStats`: frames per level, usercmds, per-stage notifications, crossings per stage against the budget row; **what `CFPSPanel` showed**: fps, frame time (min/avg/max over the last second), the local pose (`cl_showpos`) | every phase close; replaces the FPS panel |
| `netgraph` | **what `CNetGraphPanel` showed**, from `INetChannelInfo`: latency, choke, loss, in/out packet sizes and rates per frame, interp amount, server frame time, the per-frame message-class breakdown for the last 64 frames | replaces the net-graph panel; the dev web UI draws the graph |
| ~~`io`~~ | **DROPPED** (user, 2026-10-04): `TRACK_BLOCKING_IO` is defined nowhere, so the stock panel compiles to nothing and `IFileSystem` exposes no blocking history; there is nothing to report | — |
| `absences` | the `SourceSharp_NotShipped`/refusal rows raised this run, with callers | K7; a row here is a failing gate |
| `gameui` | the last 64 `IGameUI`/`IGameConsole` calls received (method, args, handled or forwarded), the loading-progress history, the forwarding count (a ratchet to zero), which screen is up, `IsMainMenuVisible` | C4 every step; `menu-*`, `loading-ui`, `disconnect-dialog` |

Two rules: a route exists only with a golden and a fact that reads a
non-zero through it in the positive arm (a route that answers `[]` on a map
with 473 entities is a bug, not a baseline); and the live gate reads routes,
never `console.log`, for anything a route covers, so the `cs_cl_*_report`
commands retire as their routes land (each retirement in its own commit,
with the gate step switched first).

---

## 5. How each kind of port is verified

### 5.1 Pure arithmetic (proxies, beams, ropes, sprites, interpolation, input maths, view setup)

The `Fixtures/interpolation-stock.tsv` recipe, which is the one bit-exact
client oracle that exists today:

1. patch a probe into the stock TU (a `tools/<step>-probe-*.diff` kept in the
   repo), run dm_lockdown headless, record inputs **and** outputs **and** the
   arm state for ≥ 30 frames or ≥ 200 samples;
2. commit the fixture with a README naming the commit and the probe;
3. write the managed fact that replays it bit-for-bit (or to a stated ULP for
   transcendental paths, with the tolerance in the fact's name);
4. show it red against an empty implementation (the red output is quoted in
   the commit message, per the test-first ruling), port until green;
5. delete the stock TU in the next commit.

### 5.2 Decisions with an engine effect (render plan, shadows, effects, UI)

A counter the native executor increments per primitive (the pattern of
`shell_effect_ledger.h`), read through `cs_cl_*_report`, and a **causal A/B in
the same run**: the gate flips the managed decision (a convar or devapi
toggle), reads the counter flat on one side and grown on the other, three
rounds. A gate that cannot show the flat side is not a gate.
For the render plan specifically: a recorded stock call sequence (pass kinds,
view setup fields, render-target binds) for one frame of dm_lockdown at a
fixed pose (`setpos_exact`/`setang_exact`), diffed against the executor's
sequence from the managed plan. Visual eyeballing uses the engine's `jpeg`
console command through `tools/devapi cmd` but nothing gates on it. UI
screens are the exception: their look is reviewed and pinned offline through
the SVG test renderer and its goldens (§3.2), and `ui-fidelity` ties the SVG
to the in-game draw ledger.

### 5.3 Live gate steps to add (each must prove it can fail in-run)

| step | reads | fails when |
|---|---|---|
| `ui-scoreboard` | `/client/{slot}/ui` | the scoreboard is not visible after `+showscores`, or its row count ≠ player count |
| `ui-teammenu` | same + a key event | a key press on the menu does not change the team (server reads the join) |
| `render-plan` | `/client/{slot}/render` | pass count or kinds differ from the recorded baseline at the fixed pose; a water map adds the reflection pass |
| `flashlight` | `/client/{slot}/entities` + a shadow counter | `impulse 100` does not add exactly one flashlight shadow |
| `input-usercmd` | `/client/{slot}/input` | a scripted mouse delta does not move the view angle by the sensitivity-scaled amount |
| `proxy-animated` | the executor's bind counter | an animated texture's frame does not advance over 1 s |
| `rope-settle`, `beam-ring` | effect counters | no segments/quads drawn after the TE fires |
| `shadow-policy` | `cs_cl_policy` | an entity ruled `NONE` by `ClientRenderPolicy.Shadow` still has a shadow handle |
| `menu-main` | `/ui` on a client-only launch (`make client-only-gate`) | the main menu screen is not up within 10 s of launch, or a button's command does not reach the engine (`/gameui` last command) |
| `menu-pause` | `/ui` + `/gameui` | ESC in game does not show the pause menu; ESC again does not hide it; `IsMainMenuVisible` disagrees with `/ui` |
| `loading-ui` | `/gameui` progress history across `map-change` | fewer than 5 `UpdateProgressBar` calls reached the screen, or the last progress < 1.0, or the loading screen is still up 2 s after `OnLevelLoadingFinished` |
| `console-ui` | `/ui` + `/console` | the console screen does not show a line the server just printed, or a command typed through `ui/event` does not execute |
| `disconnect-dialog` | `/ui` after the server kicks the client | no dialog, or a dialog without the reason string |
| `ui-resize` | `/client/{slot}/ui` + `/render` after `mat_setvideomode` 1024 768, 2560 1080, 1920 1080 with the scoreboard open | any quad off screen, an anchored element not at its proportional position, a font height that did not scale, a render pass at the old viewport size, or a texture upload counter that did not move (atlases must be regenerated) |

Every one keeps running on the second map load, and `second-client` stays.

### 5.4 Deletions

K1 before and after, `nm -D` on the engine binaries for the interface name
(CLAUDE.md: the engine asks for some by name only), the static-init census
(`make globals-client-census`) showing the registration gone, and the live
gate. A deletion of behaviour (VR, video) gets its absence row in the same
commit; a panel whose data moved to a route (FPS, blocking I/O, net graph) is
deleted only after the route's golden and positive-arm fact exist.

---

## 6. Phases

One worktree cut from `main` at the reviewed tip, its own `make setup`. Per
commit: build + the tests the change touches (`make managed-test
FILTER=…`, `buildcpptests NAME`); `make check` and the full live gate at each
phase's close and before the merge (memory: *make check at merge only*,
*live gate cadence*). Ledgers (`sdk-census --write`, globals-client-ledger,
shim table, the new ratchet) regenerate BEFORE `make check`. After every
build, `git -C ../source-sdk-2013 status --porcelain` is empty.

**C0: instruments and baseline** (no behaviour change)
1. `tools/client-novalve-census` with R1–R8 and `--allowlist`; the roots
   file; the ratchet; self-tests.
2. `tools/abi-parity --side client` with the K2 list; mutation proof.
3. `tools/novalve-snapshot` client arm; snapshots: K1, K3, cvarlist, the
   effect counters at the fixed pose, K6 counts.
4. The first devapi client routes (§4.4): `entities`, `entity/{n}`, `frame`,
   `netgraph`, `reload`, `reload/trigger`, `absences`, `nettables`; the others land in
   the first commit of the phase that needs them.
5. `tools/shim-scope` (N0 for the client): the function ledger and the LOGIC
   ratchet over `src/client/native`.
6. `live-gate-budget.tsv`: record a `render` crossing row at today's tip.
8. `IReloadParticipant` (§3.8) in the client module's save/restore walk, the
   `[SaveField]` gap in `ClientStandaloneEntity.SaveDeclaredState` closed,
   and the `hot-reload` step's leak arm; a first participant (the HUD's
   message queue) proves the round trip: before ≠ after.
7. Probes written into §8 before C1: the particles-lib callback set
   (`IParticleSystemQuery` methods actually called), the engine's by-name
   consumers of `VClientTools`, `IClientVirtualReality`, `INetGraphPanel`,
   `IFPSPanel`, **GameUI**: which path loads the module (`GAMEBIN` or the
   view), the order of `GameUI011` vs `VClient017`, whether the engine paints
   vgui from inside `UpdateProgressBar`, whether `SetMainMenuOverride` is
   honoured, and the full `IGameUI` call sequence of one session (the
   logging stub, §3.2a), whether vgui textures uploaded through `DrawSetTextureRGBA`
   survive `mat_setvideomode` or must be re-uploaded, whether the Steam HTML
   surface draws with `-nosteam` and in the headless gate, and the
   `CLIENT_DLL` preludes of the six shared TUs (absence row 1).

**C1: deletions and dead TUs**
VR and the video panel (ruled); the FPS and net-graph panels
once C0's `frame` and `netgraph` routes exist, and the blocking-I/O panel
with no replacement (its code compiles to nothing; `io` route dropped); `client_virtualreality`'s
`COM_GetModDirectory` goes with it (absence row 2). The §2.1 list ruled one by
one in `docs/client-novalve-rulings.tsv`. `memoverride.cpp` the server way.
Each deletion: K1 diff, globals census, live gate.

**C2: effects, tempents, beams, sprites, ropes, decals, sound** (clusters
3–6, 17, 18 and the shim sims)
The clusters that are already half managed; each TU's simulation moves with a
§5.1 fixture, its renderer becomes a draw slot with a §5.2 counter. `IEffects`
becomes a thunk. `shell_effects_sim`/`shell_trail_sim`/the `*_sim.cpp` files
empty out. The server's `IManagedSoundController` is reused for envelopes.

**C3: the UI** (§3.2)
0. `UiTestRenderer` (SVG), `tools/ui-preview`, the `ui-fonts.tsv` fixture
   recorded from the game, the `Golden/Ui` fact and the `ui-fidelity` fact
   over the HUD that already exists, so the first new screen arrives with
   its previews.
1. `IShimUiClient` slots and `IManagedUiClient` events; the root panel per
   layer; the by-name interfaces as thunks.
2. The spike: the scoreboard three times over the same slots and the same
   `/ui` route: in-house render list, VellumUI, and a web page over the Steam
   HTML surface; the UI assembly's own reload measured on each; rulings
   recorded in §8 with numbers (lines, reload time, draw calls, latency).
3. Scoreboard, team menu, spectator GUI, MOTD window, centre print, loading
   disc, chat/hints on the chosen layer; `clientmode_shared`'s per-frame
   decisions to C#.
4. `vgui_controls`, `matsys_controls`, `bitmap`'s vgui users leave the link.

**C4: GameUI** (§3.2a)
0. C0's logging-stub oracle re-taken at this tip; the launcher's
   `engines/client` view (or the game-bin placement, whichever the probe
   chose) under a fact that proves the stub, not the stock module, was loaded.
1. The managed main menu through `SetMainMenuOverride`, stock GameUI still
   loaded; every button opens a stock dialog by `SendMainMenuCommand`. Live
   step `menu-main`. **(live)**
2. `gameui_sourcesharp.so`: `GameUI011` + `GameConsole004` thunks,
   `IManagedGameUi`/`IManagedGameConsole`, forwarding to the stock module for
   everything managed does not answer; the `/gameui` route and its forwarding
   ratchet. Nothing visible changes; the oracle sequence replays equal.
3. Pause menu, loading screen, disconnect/failure/confirm-quit dialogs,
   console (`InstallConsoleDisplayFunc` slot). Steps `menu-pause`,
   `loading-ui`, `console-ui`, `disconnect-dialog`. **(live)**
4. Options (video-mode slots), create server, player list.
5. Server browser over the managed Steam flat API (after C6 moves Steam); the
   forwarding ratchet reaches zero; the `dlopen` of the stock module and the
   single-player methods' rulings land; `GameUI.so` leaves the view.

**C5: material proxies and particles glue** (clusters 1, 2)
One trampoline proxy class; 24 `OnBind` bodies to C# with fixtures; the
`particles.a` shell (§3.4); `CParticleMgr`'s simple particles to C#. `vtf`
and `bitmap` leave the link.

**C6: input, physics callbacks, movehelper** (clusters 9, 13)
`IInput` as a thunk set; key/mouse/joystick/Steam Controller/camera logic to
C# with recorded usercmd fixtures (`usercmd-decode` fixture exists);
`c_rumble` as a managed user-message handler. The VPhysics callback arms on
the client get the server's `plan_physics_stock_parity` treatment (test
first). Steam to managed (`plan_server_novalve.md` §3.5); `libsteam_api.so`
leaves the link.

**C7: the renderer** (clusters 10–12, §3.3)
1. `RenderPlan` schema + executor beside the stock `viewrender`, behind a
   convar, with the recorded call-sequence oracle; A/B until the sequences
   agree on dm_lockdown at three poses and on a water map.
2. Flip the default; delete `viewrender`/`viewpostprocess`/`view*` TU by TU
   as each pass is owned by the plan.
3. Leaf system and shadow manager rewritten as ours; policy blocks from C#;
   the FOV factor (`fov` on `PlayerNetState`) closes absence row 6; shadow
   choice reaches `StandaloneSetShadowCast`.
4. Flashlight, glow overlay, pixelvis, screen-space effects to C# decisions.
5. Observer views (player-msgs lane, 2026-10-05): the server networks
   `PlayerNetState.ObserverMode`/`ObserverTarget` (stock m_iObserverMode,
   m_hObserverTarget; no target = 0xFFFFFFFF). The render plan computes stock's
   `CalcInEyeCamView` (the target's eye angles; position already arrives through
   OverrideView), `CalcChaseCamView` (orbit behind the target) and
   `CalcDeathCamView` from those fields. Today in-eye uses the local angles and
   chase/death cam have no view of their own.
6. Second camera to a render target (user question, 2026-10-05): a plan pass
   that renders a managed-chosen `CViewSetup` (third-person, overview, ortho)
   into a named render target, shown by the UI's `ui.Texture`. No PVS change:
   the second view shows only what the player's own PVS sends (user ruling
   2026-10-11, below).
   PROTOTYPED by lane camproto (2026-10-05): `shell_view_targets.cpp` exports
   `ClientRenderTargets001` (stock's three targets plus `_rt_SSView0`, 512x512,
   own depth), IShimViewClient's `RenderViewToTarget` queues one managed
   `ViewTargetSetup` and the client mode's `PreRender` draws it through
   `CViewRender::RenderView` into the pushed target (DrawMonitors' point);
   `ViewCameraService` decides mode (`cs_cl_overview 0/1/2`), rate
   (`cs_cl_overview_every`) and placement; `ViewCameraHud` shows it with
   `ui.Texture` over the target's vgui texture and the part the view fills (the
   lane's own `ui.ViewImage` was dropped on the port to main 2eafcb77: main's
   `ui.Texture` is the one engine-surface leaf, given a uv). Live step `view-camera`. What C7 keeps: the queue-then-draw
   shape (the request is one plan entry), the IClientRenderTargets export, the
   opaque material (no `$vertexalpha`: the scene writes depth into the target's
   alpha) and the field-for-field `CViewSetup` copy. What C7 changes: draw the
   pass with its own view id rather than through `RenderView`, which treats it
   as `VIEW_MAIN` (the prototype switches `r_dopixelvisibility` off around it,
   or every glow query is issued twice a frame), and target sizes from the plan
   rather than a native constant. Measured (debug, dm_lockdown spawn, own game,
   `/client/1/frame` and `/status`, 3 rounds): frame 4.73 ms off, 4.79 ms third
   person, 4.79 ms overview; inbound crossings 18.2/26.2/27.2 per frame (the
   second view's ClientStandaloneDraw +3/+4, ClientFxSpritePlan +3,
   ClientFxDrawEffects +1, ClientFxSmokeFogPlan +1) against the `render` row's
   25.98 ceiling; every other frame: 22.2/22.7. PVS: the overview box held 37
   server entities of which 20 were absent on the client (props in the next
   rooms). USER RULING 2026-10-11 (option 4 of the PVS question): no PVS
   change at all. The server adds no camera origin (no net message, no
   `AddOriginToPVS` in `ClientSetupVisibility`), and the overview shows only
   what the player's own PVS already sends. Entities outside it are missing
   from the overview by ruling (docs/todo.md, known limitation).
   USER RULING 2026-10-11: the prototype is a DEBUG camera, not gameplay.
   `cs_cl_overview` and `cs_cl_overview_every` are FCVAR_CHEAT and the service
   holds the camera off without `sv_cheats 1`; the HUD panel draws only while
   it is on (default off, never in normal play); `cs_cl_overview_every`
   defaults to 2. Tooling drives it through the dev API:
   `/client/{slot}/viewcam` reads mode, counters and the executor's drawn
   count, `?mode=0|1|2` switches it (cheat-gated). The render-target
   mechanism itself (ClientRenderTargets001, `RenderViewToTarget`,
   `ViewTargetInfo`, `ui.Texture` with a uv) stays generic, for C7's monitor
   pass and Descent's 3D portrait (2eafcb77 only declares the surface handle
   today) to reuse rather than build a second.
7. The render budget's driver (live gate `render`, 2026-10-05): at the fixed
   renderStand (dm_lockdown, -4116 4600, yaw -90) the client crosses 32.2 times
   a client frame, and `ClientStandaloneDraw` -- one crossing per drawn
   standalone entity -- is 16.0 of them, half the rate; it is also the term that
   moved the rate from 21 to 32 between spawns. The render plan draws standalone
   entities in one batch (or decides them in managed) and the row is re-recorded.


**C8: `shell_standalone_client.cpp`, util, registries, datatables**
(clusters 7, 8, 16 and the 7K-line shell)
Per-op bookkeeping and draw decisions out of the standalone shell (N1.3's
treatment); registries to C# tables; `cdll_util` bodies to their callers'
side; `engineabi/recv.h` replaces `dt_recv`/`client_class`/`networkvar` with
K2 over each; `interpolatedvar`/`interpolatortypes` deleted against the
fixture oracle. The six `IGameEntity` slots and `game_entity_relays.h`'s nine
client sites (client-shell.md §2.2) resolve here. Bone setup stays N2's.

**C9: tier libraries and the header move**
tier3 → tier2 → tier1 → tier0/vstdlib by compiling the server's port files
into the client target and finishing the client-only imports; an archive
leaves the `LINK_GROUP` in the commit its import list reaches zero. Then the
header waves: leaf TUs first, `cbase.h` includers last, each swapped onto
`engineabi/client.h` and given only what the error list asks for; the six
shared-TU preludes become one arm (absence row 1). R1, R5 reach their floors;
R2, R3, R4, R6, R8 reach zero.

**C10: close**
`make sdk-census --write` (client.txt empties; shared lines only the server
uses move to server.txt); move-ledger rows; `make debug GC_SECTIONS=0`; a
release build and release live gate; re-measure every §1 number with §9's
commands into a before/after table, each with its commit; the LOGIC ratchet
reads zero or lists each named exception; `docs/absence-client.md` holds the
two rulings and nothing else.

Rough size, by lines that change hands: C2 ≈ 29K (fork 19.4K + shim 9K),
C7 ≈ 24.7K, C3 ≈ 12K + the layer, C4 = 8 screens and one small module (no
fork lines leave: GameUI was never ours), C8 ≈ 14K, C6 ≈ 7.8K, C5 ≈ 9K,
C1 ≈ 5K deleted. C3, C4 and C7 carry the novelty (no server precedent); C2,
C5, C6 and C8 are
the server's patterns repeated.

---

## 7. Risks and the measurement that retires each

| risk | measurement |
|---|---|
| per-frame crossings (render plan, UI paint, bone batch, effect draws) blow the frame budget | the `render` budget row from C0; `ClientFrameStats` per-stage counters; a measured ms/frame at the fixed pose before and after each C7 step; batching is the remedy, not C++ |
| the executor grows branches on game state | `tools/shim-scope`'s rule: a B-classified function that gains a branch on a plan field other than a kind switch is flagged red |
| a UI framework's statics and hot reload | the C3 spike measures `cs_cl_reload` and the UI-only reload with a menu open; `plan_managed_vtable_reload.md`'s rules apply to `IManagedUiClient` |
| fixtures recorded from the code under test (prove nothing) | README per fixture names the stock commit and probe diff; a fact checks the fixture predates the port's commit |
| deleting an arm we bypassed instead of porting it | §2.1's two rules; `docs/client-novalve-rulings.tsv` carries the evidence per TU |
| listen server: client and server share one CoreCLR | side isolation already done (`plan_side_isolation.md`); `SideAuditTests` waivers stay empty |
| the second map load | every new live step runs in both passes; `client-only-gate` covers the no-server shape |
| `particles.a` pulls tier symbols | C0's import probe over the archive (`nm` by symbol type); whatever it needs from tier1/tier0 is ported like the server's mathlib imports were |

---

## 8. Rulings and remaining decisions

Ruled by the user on 2026-10-04:
- UI: one native Panel, everything drawn from managed code.
- Renderer: decisions in C#, one native render-pass executor; leaf system
  and shadow manager stay engine-called machinery with policy in C#.
- `particles.a` (+ `dmxloader.a`) stays as a bound external library.
- Delete: SteamVR and the in-game video panel. The FPS, blocking-file-I/O and
  net-graph panels are deleted as panels; their data moves to the dev API.
  Port: joystick, Steam Controller, rumble.
- `classmap.cpp` is deleted (user, 2026-10-05): it is empty in this build
  (nothing registers into it) and its one real job, client-predicted entity
  creation by classname, has no caller in HL2MP.
  **FOLLOW-UP (user, 2026-10-05): fix prediction of weapon-spawned
  projectiles.** Today a grenade, crossbow bolt or RPG rocket appears only when
  the server's entity arrives, a round trip after the shot. The fix is a
  managed design, not a classmap port: the weapon's predicted fire creates a
  client-side stand-in through the managed entity registry, keyed so the
  server's entity claims it on arrival (stock's `PredictableId` idea), and the
  stand-in is dropped on a misprediction or timeout. It needs the server's
  creation to carry the key. Planned with C8 (registries); gated by a live
  step that fires on a lagged client (`net_fakelag`) and reads the projectile
  on the client before the server's entity arrives, then one entity after it.
- **Managed code may run console commands** (user, 2026-10-05, lifting the
  earlier rule "as more and more is in managed code"). `IFwdEngineClient`
  no longer has to keep `ClientCmd` behind fixed-text members; free text
  (the UI console window's `SubmitConsoleLine` first) goes to the engine's
  command buffer, which runs it locally or forwards it to the server as stock
  does.
- **TODO (user, 2026-10-05): team changes on the server.** The team menu
  (U3) sends `jointeam N`; the server hears it and refuses it, because the
  standalone player has no team change. Server-side work, not scheduled:
  port stock's `CHL2MP_Player::ClientCommand("jointeam")` / `ChangeTeam`
  (team entity membership, respawn, model by team, the gamerules' team-play
  checks), with a fact red first and the `ui-teammenu` live step extended to
  read the new team on the scoreboard.
- `memoverride.cpp` stays linked until C3 retires the vgui panels, then goes
  as on the server (user, 2026-10-05; C1 found vgui2.so freeing KeyValues
  strings client.so builds and posts through `ivgui()->PostMessage`).
- Fork callers into deleted subsystems (VR, FPS/net-graph panels,
  studio_stats) are answered by no-op stubs in our shell with absence rows,
  NOT by `#ifndef SOURCESHARP` fork edits (user, 2026-10-05); the stubs leave
  with their callers (C3, C7).
- The `io` route is dropped (C0 routes lane: no data source; `TRACK_BLOCKING_IO`
  undefined, so `CBlockingFileIOPanel` is empty). C1 deletes the panel with no
  replacement.
- R1 roots `particles/particles.h` and `inputsystem/iinputsystem.h` are NOT
  allowlisted (C0 census: under `CLIENT_DLL` the first includes
  `game/client/c_pixel_visibility.h`, the second reaches 7 `steam/` headers).
  They count against R1 until C5 (particles behind our boundary header) and
  C6 (input thunks, Steam Controller over the managed flat API) retire them.
  No fork edit.

Carried over from earlier rulings: move first, measure after (CLAUDE.md);
test first for physics and bones (memory, 2026-10-01); stock arms we bypass
are ported, never deleted (memory, 2026-10-01); DI only, no statics in
managed code (memory, 2026-09-29); Steam to managed, tier libs off the link,
no `memoverride`, C++17 (`plan_server_novalve.md` §6).

Open, to be decided by the C3 spike with numbers (§3.2): the render list
in-house vs VellumUI, and the web page for the menus. Recommendation: render
list for the HUD either way; web for menus only if the Steam-surface probe and
the latency number allow it.

Ruled by the user on 2026-10-04 (after the first draft): effects (C2) before
the UI (C3); the UI in its own assembly with its own reload; the UI
resolution- and aspect-independent, following video-mode changes live (§3.2);
the UI system also covers the main menu, every dialog, the loading screen and
the console, replacing `GameUI.so` through the launcher (§3.2a, phase C4).

Open, for the user: whether N2 (shared bone setup, test first) runs inside
this plan's worktree or beside it. Proposed: beside it, by a second lane that
owns `src/shared/native/bone_*` and `studio.cpp`, since both DLLs consume it.

Probe results (C0 step 7) are written here before C1.

### C0 probe results (lane probes, kit at 338674e9, measured 2026-10-05)

Instrument: `tools/probes/` at 338674e9. The client half
(`tools/probes/client/shell_probes_debug.cpp`) is compiled only into a debug
client.so configured with `-DSS_CLIENT_PROBES=ON`. The default debug build and
every release build leave it out, so no ratchet counts it. Each answer below
comes from one `tools/probes/c0-session OUT STEPS [opts]` run (one game, its
own TMPDIR, DEBUG_API_DIR and `-port`, launched with `make headless`'s flags
plus `+sv_lan 1 +fps_max 60`), summarised by `tools/probes/c0-analyse
OUT/probe.log [--sequence]`. All runs were at 338674e9 with probes ON. The
build was switched back OFF afterwards. Engine: **Source SDK Base 2013
Multiplayer (appid 243750)**, `bin/linux64/engine.so` sha256 06d934bb…. This
is the engine `mods.yaml` runs (`engine.client.appid: 243750`). It is not the
TF2 install that §3.2 and §3.2a name; TF2's engine.so (fc0e47db…) is a
different build. The by-name findings agree for both.

**1. The particles-lib callback set.** The stock `g_pParticleSystemQuery` is
swapped for a counting forwarder before `ParticleMgr()->Init` passes it to
particles.a.
- *A dm_lockdown session with effects* (`sessions/particles.steps`: an
  `ss_firesmoke` whose `env_fire_small` drew 22 live particles by
  `cs_cl_particle_report`, plus an `env_explosion` and an `env_entity_igniter`,
  about 20 s in game). Calls: **`GetCollisionGroupFromName` ×28**, all made
  while the .pcf definitions load. Every other method was called **0** times.
  The IAppSystem five were also 0.
- *Upper bound* (`sessions/particle-sweep.steps`,
  `cs_cl_probe_particle_sweep`): each of the **245** loaded definitions is
  started in turn in front of the view for 0.8 s, and all 245 created valid
  effects. Methods called, with the number of definitions that called each:
  `TraceLine` (11 systems, e.g. `blood_spurt_synth_01`),
  `GetLightingAtPoint` (64, e.g. `blood_impact_red_01`),
  `GetControllingObjectHitBoxInfo` and
  `GetRandomPointsOnControllingObjectHitBox` (10 each, the
  `blood_antlionguard_injured_*` family), and `GetCollisionGroupFromName`
  (28, at load). Call totals depend on frame timing (TraceLine 6,828 at
  338674e9 against 2,356 in an earlier run of the same sweep), so only the
  per-method system counts are stable. **Never called by any definition:**
  `MovePointInsideControllingObject`, `IsPointInControllingObjectHitBox`,
  `GetLocalPlayerPos`, `GetLocalPlayerEyeVectors`, `GetPixelVisibility`,
  `SetUpLightingEnvironment`, and the five `IAppSystem` methods.
- So C5's `IParticleSystemQuery` needs 5 live methods: `TraceLine`,
  `GetLightingAtPoint`, the two hit-box ones and `GetCollisionGroupFromName`.
  The other 6 can answer as the interface's own inline defaults do.

**2. By-name consumers.** `tools/probes`-style `strings -a` over every ELF in
the 243750 install, the TF2 install's `bin/` and the 244310 dedicated install
(searching `VCLIENTTOOLS|ClientTools|ClientVirtualReality|NetGraph|FPSPanel`):
- `VCLIENTTOOLS001`: **no consumer** in any binary. Only our own client.so
  carries the string.
- `ClientVirtualReality001`: **engine.so only** (one string, in both
  installs).
- `INetGraphPanel` / `IFPSPanel`: **no version string exists**. They are
  client-internal globals (`vgui_netgraphpanel.cpp:1553`,
  `vgui_fpspanel.cpp:403`), and nothing outside client.so names them.
- Live check: the client's `CreateInterface` list is wrapped and logs every
  request. Over a whole session (menu, map, play, disconnect, quit) engine.so
  asked for `VClient017`, `VClientDllSharedAppSystems001`,
  `VClientPrediction001`, `VClientEntityList003`, `VCENTERPRINT002`,
  `ClientLeafSystem002`, `GameClientExports001` and the material proxies
  (`*_IMaterialProxy003`). It **never asked for `ClientVirtualReality001`**
  (0 requests in the `view` and `gamebin` runs). The string in engine.so is
  for a path this engine does not take by default.
- **Tolerance confirmed:** with `--no-vr` (SOURCESHARP_PROBE_NO_VR=1) the
  entry is unlinked from `InterfaceReg::s_pInterfaceRegs`. A self-check then
  got `CreateInterface("ClientVirtualReality001") -> NULL (rc=1)`, and the
  game launched with `+map dm_lockdown`, joined (`[SourceSharp/Hud] first
  paint` ×1), disconnected and quit with exit 0 (`sessions/no-vr.steps`).
  **C1 can delete client_virtualreality.cpp without a stub export.**

**3. GameUI** (logging stub `tools/probes/gameui-stub`, forwarding to the
stock module):
- *Which path loads GameUI.so: the view, not GAMEBIN.* With the stub at
  `bin/debug/bin/linux64/GameUI.so` (the mod's `gamebin`, beside client.so),
  the stub never loaded (0 stub lines). `/proc/<pid>/maps` showed only the
  install's `bin/linux64/GameUI.so`. With an engine view (every install entry
  linked, `bin/linux64/GameUI.so` replaced, run through the launcher's
  existing `-engine <dir>`), the view's GameUI.so is the one mapped and its
  `CreateInterface` gets `GameUI011` and `GameConsole004` from the view's
  engine.so. **§3.2a's mechanism is the client engine view.** No dlopen
  interposer is needed.
- *Order: `VClient017` is resolved before `GameUI011`, but GameUI is
  initialised before the client.* Times are from the first probe line (run
  `override`):
  - `VClient017` 0.000 s
  - `GameUI011` / `GameConsole004` 0.456 s
  - `IGameUI::Initialize` +0.078 s after that, then `Start`, `IGameConsole
    Initialize` / `SetParent`, `OnGameUIActivated`
  - **`IBaseClientDLL::Init` enters only after `OnGameUIActivated`** (it is
    where `LoadManagedRuntime` brings the CLR up) and returns 0.57 s later
  - then `IGameUI::Connect`, client `PostInit`, `IGameUI::PostInit`, the
    first `RunFrame`

  So the thunks **must queue `Initialize`, `Start`, `OnGameUIActivated` and
  the console's `Initialize`/`SetParent`** until the managed root exists, as
  §3.2a anticipated. `Connect` and `PostInit` arrive after it.
- *Does the engine paint vgui from inside `UpdateProgressBar`?* **Yes, when
  GameUI returns true.** Paints were counted at `ISurface::PaintTraverseEx`
  (vtable slot 114 of vguimatsurface's surface, every call from engine.so).
  - Plain stub, load from the command line (`show_progress=0`): 250 calls,
    all returned false, 44 paints in the window.
  - Plain stub, in-game `map` (`show_progress=1`): 20 calls returned true and
    were followed by 56 paints; 26 returned false and were followed by 13.
  - With the stub forcing true (`SOURCESHARP_PROBE_PROGRESS_FORCE=1`) on the
    same command-line load: **285 paints after 250 true returns**, against 44.

  The managed loading screen gets painted by returning true. Loads started
  from the console at the menu, or from `+map`, pass `show_progress=0`.
- *Is `SetMainMenuOverride` honoured?* **Yes.** A hidden panel parented to
  `PANEL_GAMEUIDLL` was passed through slot 36. GameUI made it visible within
  0.25 s (`menu-override-visible 1`) and hid its own menu. Screenshot
  magenta share of the top half: before 0.00, after 1.00. The panel is hidden
  while in game (0.00) and shown again after disconnect (1.00).
- *The `IGameUI` sequence of one session* (`sessions/gameui-full.steps`:
  menu, `map`, play, `disconnect`, `quit`; `c0-analyse --sequence`):
  1. `CreateInterface GameUI011`, `GameConsole004`
  2. `IGameConsole::IsConsoleVisible`
  3. `Initialize`, `Start`
  4. `IGameConsole::Initialize`, `SetParent`
  5. `OnGameUIActivated`
  6. [client Init]
  7. `Connect`, `PostInit`, `RunFrame` (every frame from here on)
  8. ServerBrowser.so asks the stub's factory for `RunGameEngine005`
     (answered) and six tool interfaces (all NULL)
  9. On `map`: `OnDisconnectFromServer(0)`, `OnLevelLoadingStarted(0)`,
     `UpdateProgressBar` ×220, `OnDisconnectFromServer(0)`,
     `UpdateProgressBar` ×29, `OnConnectToServer2(game, 127.0.0.1)`,
     `UpdateProgressBar`, `OnLevelLoadingFinished(0,"","")`,
     `OnGameUIHidden` ×2
  10. On `disconnect`: `OnDisconnectFromServer(0)`, `OnGameUIActivated`
  11. On `quit`: `OnDisconnectFromServer(0)`, `OnGameUIActivated`,
      `Shutdown`

  **Never called in the session:** `IsMainMenuVisible`,
  `SetShowProgressText`, `SetProgressOnStart`, `OnConfirmQuit` (console
  `quit` skips it), `ShowMessageDialog`, `SendMainMenuCommand`, every
  bonus-map/Xbox/session method, and `IGameConsole::Activate`/`Hide`
  (`toggleconsole` through the debug API did not reach them).
  `UpdateProgressBar` status texts: `#LoadingProgress_*` tokens for 6
  steps, `(null)` for the rest.

**4. Textures across `mat_setvideomode`: they survive; no re-upload is
needed.** A 64×64 four-colour texture was uploaded once through
`CreateNewTextureID` + `DrawSetTextureRGBA` and drawn at (16,16)+256
(`sessions/texture-videomode.steps`). `tools/probes/c0-texture-check` read
all four quadrants correctly (within ±1) in the shot at 1280×720, after
`mat_setvideomode 1024 768 1` (shot size 1024×768) and after the change
back. `IsTextureIDValid` stayed 1, and `OnScreenSizeChanged` fired twice.
Negative control: a shot without the panel fails all four. Measured
**windowed** only; a fullscreen switch was not tried on the shared desktop.

**5. The Steam HTML surface.** `cs_cl_probe_html` calls
`ISteamHTMLSurface::Init`, `CreateBrowser`, `SetSize(256,256)` and `LoadURL`
on a solid red data: page, and answers `HTML_StartRequest_t`.
- Headless (`make headless`'s flags), at the menu and in game: browser ready,
  `HTML_NeedsPaint_t` 256×256, **65536/65536 pixels red**. **It draws.**
- `-nosteam`: **no effect on this engine.** No binary in the 243750 install
  contains a `-nosteam` switch (`strings`: only `-nosteamcontroller` in
  inputsystem.so). The run is identical: Steam game server logged on, and the
  surface draws 65536/65536 red.
- Steam unreachable (`HOME` pointed at an empty dir, so
  `SteamAPI_IsSteamRunning()` fails): **the game does not start at all.**
  engine.so prints `No SteamApps connection.` and no client.so loads. "No
  Steam, no UI" therefore cannot happen: without Steam there is no game to
  draw a UI in.
- So the web arm's Steam dependency adds no failure mode the engine does
  not already have. The remaining C3 kill criteria are latency and the BGRA
  copy.

**6. The `CLIENT_DLL` preludes of the six shared TUs (absence row 1).**
Measured from `farm/debug/build/build.ninja` (`grep -E "^build [^:]*/<tu>.cpp.o:"`)
and the sources at 338674e9:

| TU | compiled by | client prelude now | `#else` (server) arm |
|---|---|---|---|
| `game/shared/entitylist_base.cpp` | **neither DLL** | n/a | n/a |
| `game/shared/predictableid.cpp` | **neither DLL** | n/a | n/a |
| `game/shared/takedamageinfo.cpp` | **neither DLL** (both link `src/shared/native/takedamageinfo_library.cpp`) | n/a | n/a |
| `src/shared/native/gamestringpool.cpp` (relocated) | client + server | `string_t.h`, `client_class.h`, `icliententityinternal.h`, `shell_clientsystem.h`; then `igamesystem.h`, `gamestringpool.h`, `tier1/stringpool.h` | no `cbase.h`: `shell_serversystem.h`, `ss_gamestringpool.h`, `gamestringpool_internal.h`, `tier0/dbg.h`, `server_string_pool.h` (7 `CLIENT_DLL` conditionals in all) |
| `src/shared/native/movevars_shared.cpp` (relocated) | client + server | `client_class.h`, `movevars_shared.h` | no `cbase.h`: `managed_convar_ref.h` + one declaration (3 conditionals) |
| `src/shared/native/usercmd.cpp` (relocated) | **client only** (server's ReadUsercmd is C#, UserCmdCodec.cs) | `predictable_entity.h`, `cdll_util.h`, `client_class.h` | `#include "cbase.h"`: **dead**, since no target compiles the server arm (3 conditionals) |

Row 1's premise ("the `#else` arm still carries `cbase.h` for the server")
no longer holds. Three TUs left both builds, two have server arms without
`cbase.h` (P5), and the one `cbase.h` arm left (usercmd) has no compiler. The
`.h` files of the three departed TUs are still in `sdk-links/client.txt`
(`entitylist_base.h:334`, `predictableid.h:370`, `takedamageinfo.h:395`).
Closing row 1 is now a client-side edit: drop usercmd's dead `#else`, and
make the remaining client arms one prelude when C9 moves the headers. No
server step is needed.

---

## 9. Instruments (as run for §1, at db15948c)

```
cd farm/debug/build
ninja -t commands out/client.so > $S/client_cmds.txt
grep -c ' -c ' $S/client_cmds.txt                                   # 423
grep ' -c ' $S/client_cmds.txt | grep -o ' -c [^ ]*' | awk '{print $2}' > $S/client_srcs.txt
grep -c '^.*/farm/' $S/client_srcs.txt                               # 320 fork (157 DLL + 163 archive members)
grep -v '/farm/' $S/client_srcs.txt | sed -E 's#.*/src/([^/]+)/.*#\1#' | sort | uniq -c   # client 60, shared 42, host 1
grep -v ' -c ' $S/client_cmds.txt | grep client.so | tr ' ' '\n' | grep -E '\.a$|^-l|\.so$' | sort -u
ninja -t deps $(grep ' -c ' $S/client_cmds.txt | grep -o ' -o [^ ]*' | awk '{print $2}') \
  | grep '^    ' | awk '{print $1}' | sort -u | while read f; do readlink -f "$f"; done | sort -u \
  | grep -c '^$HOME/git/source-sdk-2013/'                      # 1060
cd ../../..
grep -oE '\$\{SS_SRC\}/[^ ]+\.cpp' src/client/native/CMakeLists.txt | sed 's#${SS_SRC}#farm/debug/src#' | xargs wc -l | tail -1   # 87644
for f in src/client/native/*.cpp src/client/native/*.h; do grep -m1 -o 'SS_REASON[^a-z]*[a-z]' "$f" | tail -c2; done | sort | uniq -c
wc -l sdk-links/*.txt
awk -F'\t' 'NR>1 && $1!~/^#/ {print $3}' docs/engineapi-areas.tsv | sort | uniq -c   # client areas: 7
```

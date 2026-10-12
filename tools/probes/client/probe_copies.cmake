# tools/probes/client/probe_copies.cmake -- PROBE COPIES of fork TUs (lane c2-fx).
#
# plan_client_novalve.md §5.1 step 1: an oracle is recorded from the code being
# REPLACED, and the fork is read-only. So a probe build compiles a COPY of the
# stock TU with the lane's logging applied instead of the stock TU itself. The
# logging is a unified diff in this directory, <file>.probe.diff, against the
# farm's src/ (patch -p1 from farm/<config>), applied at configure time into the
# build tree; the stock TU is marked HEADER_FILE_ONLY so only the copy compiles.
# Nothing here exists in a default or release build (the caller's
# SS_CLIENT_PROBES guard), and the copies never reach the repo.
#
# Each copy includes fx_probe_log.h (this directory) for FxProbeLog, whose body
# is in fx_probes_debug.cpp.

target_include_directories(client_sourcesharp PRIVATE ${PROJECT_SOURCE_DIR}/tools/probes/client)

function(ss_client_probe_copy rel)
  get_filename_component(_name ${rel} NAME)
  set(_diff ${PROJECT_SOURCE_DIR}/tools/probes/client/${_name}.probe.diff)
  set(_in ${SS_SRC}/${rel})
  set(_out ${CMAKE_CURRENT_BINARY_DIR}/probe_copies/${_name})
  file(MAKE_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}/probe_copies)
  # The farm's file is a link into the fork; patch reads its target and writes
  # only the copy (-o), so nothing is written beside the fork's file.
  file(REAL_PATH ${_in} _real)
  execute_process(COMMAND patch --quiet --no-backup-if-mismatch -r - -p1 -o ${_out} ${_real} -i ${_diff}
                  RESULT_VARIABLE _rc OUTPUT_VARIABLE _o ERROR_VARIABLE _e)
  if(NOT _rc EQUAL 0)
    message(FATAL_ERROR "probe copy of ${rel}: ${_diff} does not apply (${_rc}): ${_o}${_e}")
  endif()
  set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS ${_diff} ${_in})
  set_source_files_properties(${_in} PROPERTIES HEADER_FILE_ONLY ON)
  target_sources(client_sourcesharp PRIVATE ${_out})
endfunction()

# The copies recorded so far. A copy leaves this list in the commit that evicts
# its stock TU (the farm no longer links the file); its diff stays beside this
# file with the fixture README that says at which commit it applies.
#   EffectsClient.cpp.probe.diff   ieffects-stock.tsv (evicted after acea937b)
#   clientsideeffects.cpp, clientsideeffects_test.cpp, fx_line.cpp, fx_quad.cpp,
#   fx_staticline.cpp, fx_discreetline.cpp, fx_tracer.cpp .probe.diff
#                                  effectlist-stock.tsv.gz (evicted after 13df3d85)
#   fx_shelleject.cpp.probe.diff   shelleject-stock.tsv (evicted after e489497f)


# The same for OUR shells (lane c2-fx's sprite decisions): the diff is against
# this repo's file, which is the code being replaced when the decision lives in
# a shell rather than a fork TU.
function(ss_client_probe_copy_ours rel)
  get_filename_component(_name ${rel} NAME)
  # An optional second argument names a diff other than <file>.probe.diff
  # (a file recorded twice, for two subjects, keeps both diffs).
  if(ARGC GREATER 1)
    set(_diff ${PROJECT_SOURCE_DIR}/tools/probes/client/${ARGV1})
  else()
    set(_diff ${PROJECT_SOURCE_DIR}/tools/probes/client/${_name}.probe.diff)
  endif()
  set(_in ${PROJECT_SOURCE_DIR}/${rel})
  set(_out ${CMAKE_CURRENT_BINARY_DIR}/probe_copies/${_name})
  file(MAKE_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}/probe_copies)
  execute_process(COMMAND patch --quiet --no-backup-if-mismatch -r - -p1 -o ${_out} ${_in} -i ${_diff}
                  RESULT_VARIABLE _rc OUTPUT_VARIABLE _o ERROR_VARIABLE _e)
  if(NOT _rc EQUAL 0)
    message(FATAL_ERROR "probe copy of ${rel}: ${_diff} does not apply (${_rc}): ${_o}${_e}")
  endif()
  set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS ${_diff} ${_in})
  set_source_files_properties(${_in} PROPERTIES HEADER_FILE_ONLY ON)
  target_sources(client_sourcesharp PRIVATE ${_out})
endfunction()

#   shell_sprite_leaf.cpp, shell_sprite_render.cpp .probe.diff
#                                  sprite-stock.tsv.gz, spritetrail-stock.tsv.gz
#                                  (apply up to 8b5cec0c; the shells became
#                                  executors at f14a30e4 and 94ba1434)

#   clienteffectprecachesystem.cpp.probe.diff   effectprecache-stock.tsv (evicted after e09ceb8d)

#   smoke_fog_overlay.cpp.probe.diff   smokefog-stock.tsv (evicted after 7a1a3d69)

#   c_te_legacytempents.cpp.probe.diff   tempents-*-stock.tsv(.gz), with its battery cs_cl_probe_tebattery
#   (evicted after the commit that recorded the muzzle flashes' CRT draws)

#   shell_managed_particles.cpp.probe.diff   sphere-stock.tsv.gz (lane c5-particles; applies at a9d8f9fd,
#                                  the sphere-renderer path; the lighting is C#'s from the next commit)
#   particle_collision.cpp.probe.diff   particlecollision-stock.tsv.gz (lane c5-particles; applies at a9d8f9fd)
#   c_tracer.cpp.probe.diff   tracer-stock.tsv.gz (lane c5-particles; applies at a9d8f9fd)
#   shell_impact_effects.cpp.probe.diff, fx_fleck.cpp.probe.diff   fleck-stock.tsv.gz (lane c5-particles; apply at a9d8f9fd)
#     (shell_impact_effects.cpp.probe.diff's gx* rows: gauss-stock.tsv.gz, with fx_sparks.cpp.probe.diff)
#   fx_sparks.cpp.probe.diff   sparks-stock.tsv.gz (lane c5-particles; apply at a9d8f9fd with particle_collision.cpp.probe.diff)
#   fx_water.cpp.probe.diff   watersplash-stock.tsv.gz (lane c5-particles; apply at a9d8f9fd with fx_sparks.cpp.probe.diff, particle_collision.cpp.probe.diff)
#   particle_simple3d.cpp.probe.diff   simple3d-stock.tsv.gz (lane c5-particles; apply at a9d8f9fd with particle_collision.cpp.probe.diff)
#   fx_blood.cpp.probe.diff, particles_simple.cpp.probe.diff   bloodspray-stock.tsv.gz (lane c5-particles; apply at a9d8f9fd with fx_sparks, particle_simple3d and particle_collision .probe.diff; c5p_dispatch in c5particles_probes_debug.cpp)
#   fx_explosion.cpp.probe.diff   explosion-stock.tsv.gz (lane c5-particles; apply at a9d8f9fd with particles_simple, fx_sparks, fx_fleck, fx_water and particle_collision .probe.diff)
#   shell_impact_effects.cpp.probe.diff (gl* rows), shell_te_backends_client.cpp.probe.diff   shards-stock.tsv.gz (lane c5-particles; apply at a9d8f9fd with the probe copies above)
#   particles_simple.cpp.probe.diff (eg* rows)   tempent-emitters-stock.tsv.gz (lane c5-particles; apply at a9d8f9fd with the probe copies above; c5p_te_muzzle in c5particles_probes_debug.cpp)
#   fx.cpp.probe.diff   fx-stock.tsv.gz (lane c5-particles; apply at a9d8f9fd with fx_sparks and particle_collision .probe.diff; c5p_ieffects, c5p_muzzle_attached and c5p_dispatch in c5particles_probes_debug.cpp)
#   shell_impact_effects.cpp.probe.diff (sdi/sti/sbi/sui/spp rows), particles_simple.cpp.probe.diff (dg* rows)   sprays-stock.tsv.gz (lane c5-particles; apply at a9d8f9fd with the probe copies above; c5p_spray, since removed, in c5particles_probes_debug.cpp at 68740bf5)
#   shell_te_backends_client.cpp.probe.diff (lf* rows)   funnel-stock.tsv.gz (lane c5-particles; apply at a9d8f9fd with the probe copies above; c5p_funnel, since removed, in c5particles_probes_debug.cpp at b3ec2fc0)

#   shell_dust_render.cpp.probe.diff   dust-stock.tsv.gz (lane c5-particles; the dust motes'
#                                  CSourceSharpDustVolume, recorded over tools/probes/sessions/c5p-dust.steps
#                                  on ss_sandbox; applies at 5aeeef63, before the volume left for C#)

#   shell_dust_render.cpp.puff.probe.diff   dustpuff-stock.tsv.gz (lane c5-particles; FX_Dust, the dust
#                                  puff, recorded over tools/probes/sessions/c5p-dustpuff.steps with
#                                  c5p_ieffects dust; applies at 1cba7038, before the puff left for C#)

#   shell_te_particle_render.cpp.tep.probe.diff, shell_te_backends_client.cpp.tep.probe.diff
#                                  teparticles-stock.tsv.gz (lane c5-particles; CTEParticleRenderer, the blood
#                                  stream and the debug line, over tools/probes/sessions/c5p-teparticles.steps;
#                                  apply at 832b0d4e, before they left for C#)
# lane c6-input: the input and rumble oracles (input-stock.tsv,
# rumble-stock.tsv). The bodies of the hooks are in c6input_probes_debug.cpp.
#   in_main.cpp, in_camera.cpp, in_steamcontroller.cpp .probe.diff   input-stock.tsv.gz (evicted after f9d7f6eb)
#   c_rumble.cpp.probe.diff                                         rumble-stock.tsv (evicted after f9d7f6eb)

#   particles_ez.cpp.probe.diff   particle-singletons-stock.tsv (lane c5-particles; which DrawParticleSingletons
#                                  call draws the dust puff's particles, over c5p-singletons.steps; applies at
#                                  fef8d244, before particles_ez.cpp left)

# C7 step 2: DoImageSpaceMotionBlur's inputs and values per frame (motionblur-stock.tsv), over
# tools/probes/sessions/c7-motionblur.steps; the tonemap's histogram, readbacks and scale (t* rows,
# tonemap-stock.tsv.gz) over c7-tonemap.steps. Applies at 3a4533e20, before viewpostprocess.cpp left (C7).
#   viewpostprocess.cpp.probe.diff   motionblur-stock.tsv, tonemap-stock.tsv.gz

# C7: view_effects.cpp's shakes and fades per call and frame (view-effects-stock.tsv), over
# tools/probes/sessions/c7-vieweffects.steps. Applies at 283add399, before view_effects.cpp left (C7).
#   view_effects.cpp.probe.diff   view-effects-stock.tsv

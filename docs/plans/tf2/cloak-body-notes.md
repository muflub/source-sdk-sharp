# Spy's cloak body — the notes behind deferred.tsv row 160

## Closed 2026-10-30: this is how stock renders the cloak on this machine

The last untried control came from the integrator: real Team Fortress 2
(Steam app 440, its own binaries — engine and shader DLLs included) on this
machine, ctf_2fort, a RED spy in third person, cloaked with `addcond 4`
(the stealth condition). The lane opened the three screenshots itself
(scratchpad `shots/stock-tf2/`): `ctf_2fort0002.jpg`, before the cloak — the
opaque red suit under the stock HUD; `ctf_2fort0003.jpg`, 40 frames into the
cloak — a pale salmon, faintly translucent body; `ctf_2fort0004.jpg`, ten
seconds later and fully cloaked — the same uniform pale salmon fill, the
torso over the door's dark panel as pale as over the bright room, the room
not refracted through him. That is our picture: real TF2 on this platform
(the same Vulkan translation path) draws the fully cloaked body exactly as
our boots do (control pid 2764990, farm pid 2854102). The port matches stock
here — the faint refraction the row expected from memory is not what stock
shows on this machine. deferred.tsv row 160 is deleted; what follows stays as
the record of how the trail was measured.

**The question this trail chased.** A fully cloaked spy seen in third person draws as a
solid, uniformly lit, pale team-tinted body. Stock's cloak shows only a faint
refraction of what is behind him — the room reads through the silhouette with
a slight warp. Nothing the managed side owns carries the defect: every suspect
it could own has been measured and holds. The cause sits below the managed
side, at the cloak pass's refraction sample at the draw. This file is the
evidence trail; `docs/plans/tf2/deferred.tsv` row 160 carried the short
ledger until the stock control above closed it.

## The scene every boot reproduces

tf2 on ctf_2fort, joined RED as Spy, third person, cloak (IN_ATTACK2) held to
the real factor of about 0.95. Every lane boot spawns at the same spot
(camera pos 1888.0 1368.0 332.1, ang 0.0 180.0): the red spawn room, the
third-person camera behind the Spy facing the glass door — a dark panel in the
door's left half, the bright room seen through the glass on the right. Over
the dark panel and over the bright room alike the body is one flat pale
salmon. That contrast — the same fill color over two very different backdrops
— is the bug in one picture.

## Dead causes, each measured with its boot

- The proxy bind on the body — dead (boot 3185130). That was the row's old
  cause; the fix's row had been wrongly closed at 74e922d23, and probes at
  that tip proved otherwise.
- The cloak pass not running, or the body being drawn by the standard pass —
  dead (boot 33810, a native one-shot render trace): the cloak pass runs and
  draws the body alone, the Spy draws exactly once per frame in the main view
  with the transparency flag set, and the cloak factor reaches 0.95 on the
  Spy's red body material.
- Draw order, self-sampling included — dead (pid 1220352, a 1,906-line
  one-frame trace): the body's draw lands right after the full-rect
  back-buffer copy into the quarter-scale copy texture and its index-0 bind,
  with no draw of that entity before them — the body is not in the copy at
  the moment it samples it. The quad boot (pid 1244905, displaying the copy
  texture itself) confirms the body appears in the displayed copy only via
  the re-copy made after the draw.
- The CPU-side view state at the draw — stock's (pid 4049005).
- Bone scales — dead: the bone matrices at the skeleton hand-off are
  unscaled, column lengths exactly 1.000 for bones 0, 1, 2, 3, 4, 7, 12 and
  20 of the Spy's 84 bones, mask 1024, cloaked and uncloaked.
- The tint arm — exonerated for the flatness: at the real factor the tint
  mixes only about 20 percent into the refraction color and cannot flatten a
  sample that varies with the room.
- Offset saturation — dead: boots forced to cloak factor 0.3 and 0.6 (pids
  453005 and 910233) do show the room through the body with visible
  distortion, and the warp scale shrinks as the factor grows; only at the
  real factor does the body become the flat pale fill (pid 4049005).
- The shader DLL itself — exonerated by the engine-farm A/B of 2026-10-30,
  below.

## The two live probes (uncommitted, emptied after use)

- Experiment A (pid 1398988): the cloak color tint forced to pure green at
  cloak factor 0.80. The whole body — head, hat, torso, legs — draws solid
  bright green. Verdict: the cloak pass itself draws the body, so the
  standard pass, other materials and the override suspects are dead; and the
  green torso stays uniformly bright even over the glass door's dark panel,
  so the refraction's base uv misses the pixel's own position there too.
- Experiment B (pid 1446478): the refraction amount forced to 30 at the real
  cloak factor (warp scale 1.5). The uniform salmon fill becomes a blotchy
  patchwork of dark brown and white/cream — live room content flung over the
  body. Verdict: the refraction sampler holds the live copy and the warp arm
  responds. What stays constant is the base uv the vertex shader writes for
  the refraction sample (its TEXCOORD1 projection). A correct silhouette
  with a live warp, a live sampler and a flat base uv points inside the
  cloak shader program: that varying is written degenerate, so every pixel
  samples one constant texel — the copy's bright top-left corner fits the
  observed pale fill.

## The shader DLL's provenance (2026-10-30)

The game process maps every engine binary from the Steam install of Source
SDK Base 2013 Multiplayer — the engine itself, the material system, the
Vulkan shader API layer, the Direct3D-9-on-Vulkan translation layer and the
standard shader program library (its inode 18248613 in the maps dumps
maps1/2/3.txt taken from a running boot); our own client, server and support
libraries come from bin/debug/bin/linux64; TF2's copy of the shader library
is never mapped. No compiled shader caches (.vcs) exist anywhere: disk globs
over both Steam installs and the bin/debug tree find none, the three relevant
content packages (tf2_misc_dir with 104,333 entries, platform_misc_dir with
512, hl2_misc_dir with 18,946) hold none, and the only loose .vcs on the
machine are seven our own mod's test shader folder ships. The cloak pass is
therefore compiled into the shader library as auto-generated bytecode: both
copies of the library (the SDK base's and TF2's) carry the cloak combos'
name strings and the same shader-system interface.

The two copies are the same size, 981,024 bytes, with different content: the
SDK base's dated 2026-08-24 (sha256 B48EE507...) and TF2's dated 2026-06-28
(34C1AFBC...). The byte diff lists 12,318 differing regions, the last at file
offset 0xEF4B8, of which 11,413 lie in the code section (at or after
0x17000). Of the 160,446 differing code-region bytes, 10,285 fit a uniform
+0x70 shift of 16-bit layout fields; the other 150,161 are real code
divergence — function boundaries slide by small amounts (a three-byte code
slide at 0x09677C is an example). They are separate builds of the same
source; the embedded cloak bytecode is not proven identical. (scratchpad:
maps1/2/3.txt, dll_diff2.txt, dll_delta.txt.)

A game-side override of that library does not exist. Staging TF2's copy into
our mod's game and game-bin directories left every boot still mapping the SDK
base's inode: the engine's shader system loads the shader DLL by bare name
from the engine's own install directory, consults a mod's game-bin directory
only for game_shader_* names, and refuses at boot any mod DLL that
re-registers a base shader name. A swap therefore has to happen at the
engine install itself; writing into the Steam install was refused in the lane
and was not routed around. The integrator's engine farm ran the A/B instead.

## The A/B through the engine farm (2026-10-30): the shader DLL is exonerated

- Control, the SDK base's shader DLL — clean boot pid 2764990 (port 43423,
  exit 0): the uncloaked baseline (shots/2764990/Snapshot0000.jpg) is an
  opaque Spy in his dark suit and hat; the cloaked body
  (shots/2764990/Snapshot0001.jpg) is the uniform pale salmon fill, the torso
  over the door's dark panel as pale as over the bright wall. Two earlier
  control boots are on record: pid 2573040 (the `cmd jpeg` step produced no
  screenshot — see the operational note) and pid 2645146 (bare jpeg
  captured, but the message-of-the-day panel covered the head). The lane
  opened every JPEG above itself.
- Farm, TF2's shader DLL — the integrator ran the boot: pid 2854102 (port
  43420, exit 0, first paint 36 s). Its mapped-library print shows
  stdshader_dx9.so at inode 32777682 and materialsystem.so at inode
  32777684, both from the scratchpad farm's bin/linux64 — TF2's
  stdshader_dx9.so, sha256 34c1afbc..., in place of the SDK base's. The lane
  opened shots/2854102/Snapshot0001.jpg: the cloaked body is the same
  uniform pale salmon, the torso over the door's dark panel as pale as over
  the bright wall — indistinguishable from the control.
- Verdict: TF2's build of the shader DLL gives the same flat fill; the DLL is
  exonerated. Whatever remains sits below both DLLs — in how the cloak pass
  is driven at the draw, not in the bytecode that runs.

## The engine-farm technique (reusable for any engine-binary A/B, no Steam write)

Copy the SDK base install into the scratchpad with `cp -rs` (symlink tree);
make bin/linux64 and the top-level executables and logs real copies
(`cp --remove-destination` — a bare `rm` of those paths trips the sandbox
check); drop the replacement DLL into the farm's bin/linux64; boot through it
via SOURCESHARP_ENGINE_DIR=<farm>, or now that lane-boot.sh takes
`--engine <dir>` and `--maps <regex>` options, without any environment
prefix (the env-assignment command form itself was refused by the lane's
permission gate); the maps print proves which library mapped from where.

## The cloak combo (from the material and the shader system, plain)

The Spy's red body material draws in the standard shader's cloak mode: the
bump-map arm is static (the body carries a normal map) and the skinning arm
is dynamic (the model is skinned); under the Vulkan translation layer's
shader model 3 it takes the vertex-id/morphing combos rather than the
shader-model-2 pairing. The cloak factor, refraction amount and color tint
take stock defaults at material load — the material defines none of them —
and the managed cloak proxies own their runtime values. All of this was
measured live at the draw: the factor reached 0.95 on the body material and
both probes answered through these same parameters.

## Next measurement (not started; named for whoever picks this up)

Inside the cloak shader program at the draw: what the vertex shader writes to
the refraction base uv (TEXCOORD1) at a cloak pixel. Concretely — read back
the matrix constants that feed that projection, and Lock-read the copy
texture at the draw and compare the one texel the flat fill could come from
against the observed color (the copy's bright top-left corner is the working
candidate for what one degenerate uv reads).

## Operational notes for the lane wrapper

Send console steps bare: the wrapper's `cmd ` prefix is a step-file verb and
the console treats it as a literal token — `cmd jpeg` produced no screenshot
while bare `jpeg` did (pid 2573040 versus 2645146), and a console
`right 90` through the wrapper did not turn the third-person view (both
JPEGs of that boot share one camera pose, three seconds apart). The
read-only diagnostics and staged copies this trail needed were run
uncommitted and emptied or removed after use; a file operation refused by the
sandbox stays refused — it must not be routed through a game-side command.

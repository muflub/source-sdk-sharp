# SourceSharp
#
# A thin front on farm/<config>/src/buildsourcesharp, which does the actual
# work: managed C#, then native C++ with CMake + Ninja (host clang 22 + lld).
# `make ci-build` runs the same CMake inside the steamrt sniper container.
#
#   make setup ROOT=../source-sdk-2013
#                      build the farms (farm/<config>) and the game directories
#                      (bin/<config>) against a source-sdk-2013 checkout; ROOT
#                      is remembered, so later runs need only `make setup`
#   make               what the targets are
#   make debug         build everything with symbols kept in the binaries
#   make debug GC_SECTIONS=0   ...keeping code nothing refers to
#   make release       build everything, then split debug info into .dbg files
#   make run           build debug, launch, and rebuild the UI as it is edited
#   make test          run every test leg -- managed (xUnit), native (gtest),
#                      the UI's, and the standalone checkers -- reporting each
#                      one separately
#   make check         build debug and run the tests — what to do before a commit
#   make ui            build just the web UI
#   make ui-watch      rebuild the web UI as its sources change
#   make sandbox       regenerate the every-entity demo map source
#   make map           compile a .vmf from game/mod_test/maps into the mod, with
#                      the compilers in $(MAPTOOLS_DIR)
#
# Each configuration has its own output: bin/debug and bin/release are each a
# whole game directory (the launcher, the shared bin/, and mods/test/), built
# from their own farm, farm/debug and farm/release. Switching between them is
# CONFIG=release on the targets that take a configuration, not a rebuild.

SHELL := /bin/bash

# The .NET SDK: ~/.dotnet, never Ubuntu's /usr/bin/dotnet, whose runtime aborts
# under concurrent tiered compilation. A bare `dotnet` would take whichever is
# first on PATH. buildsourcesharp uses the same default.
DOTNET ?= $(HOME)/.dotnet/dotnet

# How every dotnet/MSBuild call this Makefile makes runs (user, 2026-10-04).
# Exported, so recipes and the scripts they call (tools/run-test-legs, the
# managed builds) inherit them; `?=` keeps a value already set in the
# environment.
#   DOTNET_CLI_USE_MSBUILD_SERVER  one long-lived MSBuild server shared by builds
#   DOTNET_CLI_TELEMETRY_OPTOUT,
#   DOTNET_NOLOGO                  no telemetry, no banners in the logs
#   MSBUILDTERMINALLOGGER          the terminal logger where there is a TTY;
#                                  `auto` falls back to plain output in logs
export DOTNET_CLI_USE_MSBUILD_SERVER ?= true
export DOTNET_CLI_TELEMETRY_OPTOUT   ?= 1
export DOTNET_NOLOGO                 ?= 1
export MSBUILDTERMINALLOGGER         ?= auto

# The configuration the running, testing and instrument targets use. `debug`
# and `release` always build their own; `run-release` runs release.
CONFIG    ?= debug

# Recursive (=), not :=, so a target-specific CONFIG -- run-release's -- reaches
# every path built from it.
FARM       = farm/$(CONFIG)
GAME       = bin/$(CONFIG)
MOD        = $(CURDIR)/bin/$(CONFIG)/mods/test
# The launcher: source# (src/launcher), run from bin/<config>. It reads
# mods.yaml beside it for the mod, so no -game; tools get the same one.
# `#` starts a make comment, hence the backslash (tools/launcher-name-test).
LAUNCH     = ./source\#
LAUNCH_MOD =
TESTS     := src/test/managed
WEBUI     := src/webui

# The linker drops every function and variable nothing reachable refers to. It
# is on for every build; GC_SECTIONS=0 turns it off, which is the first thing to
# try when a symbol goes missing without a link error. See
# SS_GC_SECTIONS in cmake/SourceSharp.cmake. Crossing this line rebuilds everything,
# because it changes the compiler command line and not only the link.
#
# KEEP THIS WORKING. GC_SECTIONS=0 is a diagnostic, so it is only ever reached
# for while something else is already wrong -- which means a build that fails
# ONLY with it off costs the next person their whole investigation before they
# get to their own bug. It was broken once exactly that way, on BOTH sides and
# by two different evictions: props.cpp took three symbols off the server and
# physpropclientside.cpp took one of them off the client, while
# game/shared/props_shared.cpp -- which both DLLs compile -- went on naming
# them. The normal build hid it because --gc-sections drops the callers first.
# Supplied by sourcesharp/game/shared/shell_props_absent.cpp, one file on both
# link lines; pinned by sourcesharp/test/gc_sections_link_test.cpp.
#
# The general shape: evicting a .cpp whose symbols a RETAINED file still names
# is invisible to `make`, to `make test`, and to src/evict_probe -- which
# relinks with the same flags the normal build uses, so it cannot see this class
# at all. Run `make debug GC_SECTIONS=0` after an eviction.
# tools/nogc-relink asks the same question of the normal build in a quarter of
# a second (both .so relinked without the collector); orphan_reach_test runs it.
GC_SECTIONS ?= 1
# NATIVE_TESTS=0 builds the game libraries without the native tests
# (buildsourcesharp --no-tests: SS_BUILD_TESTS=OFF). CI's release build uses
# it; the tests are built and run in the debug tree by make check.
NATIVE_TESTS ?= 1
BUILD_FLAGS := $(if $(filter 0,$(GC_SECTIONS)),--no-gc-sections) $(if $(filter 0,$(NATIVE_TESTS)),--no-tests)

# The UI is TypeScript and React, so it needs Node. tools/get-node downloads a
# pinned one into the tree rather than making it a prerequisite that will be
# missing or the wrong version on somebody's machine.
NODE_BIN  := $(CURDIR)/tools/node/bin
UI_ENV    := PATH="$(NODE_BIN):$$PATH" \
             npm_config_cache="$(CURDIR)/tools/.npm-cache" \
             TMPDIR="$(CURDIR)/tools/.npm-tmp"
MAP       ?= dm_lockdown

# Where the UI is served from during a run, and where the game puts its socket.
# The proxy is what a browser can talk to: the game listens on a Unix socket, and
# a browser cannot open one.
UI_PORT   ?= 27110
SOCKET_DIR := $(or $(DEBUG_API_DIR),/tmp/sourcesharp-debug-api)

# One temp directory per run. The engine allows a single instance per user and
# takes the lock on $$TMPDIR/source_engine_<uid>, so without this a second
# "make run" dies at startup saying only one instance may run — which, launched
# from a terminal, looks like a crash rather than a refusal.
RUN_ID    := $(shell id -u)-$(shell date +%s%N)
RUN_TMP   := /tmp/sourcesharp-run-$(RUN_ID)

# Passed through to the game. The dev API is on by default here because that is
# the point of a development run; -debugapisocket gives it a per-process socket so
# several runs can be driven at once.
# -nomessagebox and -nocrashdialog keep a failure from parking on a modal dialog
# that nobody is there to dismiss, which is what makes an unattended run hang
# rather than end. -assertlog is ours: assertions report and the game continues,
# instead of the first one halting it.
#
# -nohltv AND +tv_enable 0 ARE THE DROPPED-FEATURE ENFORCEMENT, and they are
# here rather than left to a default. plan_shim.md step 5c drops HLTV and
# Replay from this mod. Before the flags, HLTV failed to start only because
# this mod happens to ship no director module -- `+tv_enable 1` still got as
# far as `maxplayers set to 5 (extra slot was added for SourceTV)` and
# `ClientPutInServer (server): SourceTV`, and only then did `tv_status` say
# "SourceTV not active: Mod doesn't support SourceTV. No director module
# found." A client object for a feature we dropped was still being created.
#
# -nohltv is the engine's own switch and is what stops that; +tv_enable 0 is
# belt and braces for a cfg that sets it. Both are asserted, by VALUE, in
# tools/live-gate --self-test and in the managed suite (HltvDroppedTests), so
# removing one is a red test and not a silent regression.
# THE FLAGS EVERY LOCAL GAME CARRIES (user rulings 2026-10-04); the Python
# tools get the same list from tools/local_launch.py, whose --self-test holds
# the two equal. Not the dedicated server's: it is the hosted-style server.
#
# +sv_lan 1 -hushsteam: a LAN-only listen server with no Steam game server
# (user approval 2026-10-05). +sv_lan 1: the server validates no Steam ticket.
# -insecure (mods.yaml) only drops VAC; without LAN a listen server validates
# the client's ticket, and with a second game on the box under the same Steam
# account that failed with code 6 ("logged in elsewhere", 2026-10-04).
# -hushsteam (engine.so 0x4949b0) skips
# SteamGameServer_InitSafe: the server logs "This server will operate in LAN
# mode only", makes no game-server logon and starts no BeginAuthSession, so
# the second client of the same account logs no "S3: Duplicate client
# connection". The CLIENT still fetches a ticket for any game with maxplayers
# > 1 (GetChallengeType never reads either flag); nobody validates it.
# tools/local_launch.py has the measurements of several joined games at once.
# STEAM_AUTH=1 leaves both out, for the Steam-auth features (TF2Descent
# Reauth, type 60). `make join` is a client of a hosted server and drops
# -hushsteam; the dedicated server never carries these flags.
#
# +fps_max 60: every local game capped at 60 frames per second, so several
# at once cost less CPU and GPU each. A listen server still simulates at its
# tick rate; the cap is on frames, not ticks.
#
# -background (BACKGROUND=1): the game window does not take the desktop focus
# (user ruling 2026-10-04: agents' games kept stealing it). The launcher names
# the window's X11 class sourcesharp-bg, which tools/kwin-bg-rule's KWin rule
# keeps unfocused and below (never minimised: SDL2 then spins in start-up; see
# that tool), and sets +engine_no_focus_sleep 0 so an unfocused game still runs
# at 60 fps (19 without it). OFF for the user's
# `make run` and `make run-debug`; ON for `make headless` (below) and for every
# Python tool (tools/local_launch.py), which are all automated. BACKGROUND=0/1
# on the command line overrides any of them. No global default: a global one
# would stop a target's own `BACKGROUND ?= 1` from taking effect, and for the
# same reason these three are recursive (=), expanded per target.
#
# -instance (INSTANCE=1): the game runs on a game directory of its own under
# /tmp/ssi-<user>/<pid> (src/launcher/Launcher.cs), so several games started from one
# tree never share a file the engine rewrites in place: videoconfig_linux.cfg,
# 33 truncate-and-rewrites per start-up, and cfg/config.cfg at shutdown.
# console.log and the +con_logfile file still land in mods/<id>/. Same rule
# as -background: OFF for `make run` and `make run-debug`, whose settings are
# the user's and persist; ON for `make headless` and every Python tool.
LOCAL_LAN_ARGS  := $(if $(filter 1,$(STEAM_AUTH)),,+sv_lan 1 -hushsteam)
LOCAL_FPS_ARGS  := +fps_max 60
LOCAL_BG_ARGS    = $(if $(filter 1,$(BACKGROUND)),-background)
LOCAL_INSTANCE_ARGS = $(if $(filter 1,$(INSTANCE)),-instance)
LOCAL_GAME_ARGS  = $(LOCAL_LAN_ARGS) $(LOCAL_FPS_ARGS) $(LOCAL_BG_ARGS) $(LOCAL_INSTANCE_ARGS)

RUN_ARGS  ?= -debugapi -debugapisocket -condebug -nomessagebox -nocrashdialog \
             -novid -windowed -w 1280 -h 720 -nohltv +tv_enable 0 $(LOCAL_GAME_ARGS)

# A SECOND, TIMESTAMPED LOG beside console.log, for DEDICATED servers only.
# -condebug's console.log never takes con_timestamp; con_logfile does
# ("10/02/2026 - 06:51:45: <line>", one-second resolution). A dedicated server
# then writes BOTH files (measured 2026-10-02). A client does NOT: its
# +con_logfile REPLACES console.log (measured under make headless: console.log
# stopped during startup, "first paint" reached only the timed file), so
# RUN_ARGS must never carry one -- tools and CLAUDE.md read console.log.
DEDICATED_TIMED_LOG_ARGS ?= +con_timestamp 1 +con_logfile console-dedicated.timed.log

# Everything above plus assertions demoted to log lines. For driving the game
# from a script, where stopping on the first assert defeats the point.
HEADLESS_ARGS ?= $(RUN_ARGS) -assertlog

# The GPU the game must render on. Sourced by every launch below, and by
# tools/debug-api run, so the two cannot drift: tools/gpu-offload is the one place the
# variables are written down. Read it — the whole explanation is there, and the
# part worth knowing in advance is that getting this wrong produces a log with
# ZERO [SourceSharp] lines in it, which reads like a broken port and is not one.
#
# Overridable without editing anything: SS_GPU_OFFLOAD=0 make run sets neither
# variable, and setting either one in the environment replaces it.
GPU_OFFLOAD := $(CURDIR)/tools/gpu-offload

# The files git does not carry. A fresh clone has no bin/<config>/bin/dotnet --
# it is 80MB of runtime, gitignored -- and a game without it does not fail
# where you would look: it STARTS, logs "no bundled libhostfxr" and segfaults in
# SourceSharp_Acquire, which reads exactly like a broken port and is not one. It
# cost a lane a run before anyone spotted it, so every target that builds or
# launches checks first and says the one command that fixes it. The farm is the
# same kind of thing: a build from a farm that is missing links fails deep in
# CMake or ninja, naming a file rather than the cause.
#
# --check is a report, so this costs a stat per entry and changes nothing.
# $(1) is the configuration to check.
define require-setup
@tools/setup --check --config $(1) > /dev/null 2>&1 || { echo "==> farm/$(1) or bin/$(1) is not set up:"; tools/setup --check --config $(1) 2>&1 | sed 's/^/    /'; echo "==> run: make setup"; exit 1; }
endef

.DEFAULT_GOAL := help
HOST_ROOT ?= $(abspath ../source-sdk-sharp-host)

# The solution every build and test leg builds; buildsourcesharp reads SS_SLN.
SLN := SourceSharp.slnx
export SS_SLN := $(SLN)
# What `make setup` must find in $(HOST_ROOT)/bin/nupkg, else it runs host-pack.
HOST_PACKAGES := $(HOST_ROOT)/bin/nupkg/SourceSharp.Host.Contracts.1.0.0.nupkg \
                 $(HOST_ROOT)/bin/nupkg/SourceSharp.Host.Sdk.2.6.0.nupkg

PHONY_LOADPROF := loadprof loadprof-probe
PHONY_TESTS := managed-test managed-legs descent-test
.PHONY: $(PHONY_LOADPROF) $(PHONY_TESTS) host-pack sdk-upstream dedicated join town mod-image local-hosted help setup debug release ci-build run run-snapshot run-debug headless run-release test test-fast test-slow managed-legs-fast managed-legs-slow cpp-test-slow test-full test-public cpp-test census-test oracle-test check check-public clean clean-sockets \
        ui ui-watch ui-test ui-preview ui-icons ui-lint node map sandbox live-gate client-only-gate movemath-gate cmdgate \
        entity-census entity-gates entity-ratchet \
        globals-census globals-ledger globals-client-census globals-client-ledger sdk-census

help:
	@echo "SourceSharp"
	@echo
	@echo "  make setup ROOT=../source-sdk-2013   build the farms and bin/ (ROOT is remembered)"
	@echo "  make host-pack [HOST_ROOT=..]       build the host's SDK packages the TF2Descent mod restores from"
	@echo "  make dedicated [DEDICATED_MOD=tf2_descent] [HOSTED=1]   a dedicated server (unhosted unless HOSTED=1)"
	@echo "  make join ADDR=host:port [JOIN_MOD=tf2_descent]         a client joining a server"
	@echo "  make town                            generate and compile tf2_descent_town into the tf2_descent mod"
	@echo "  make mod-image [CONFIG=release] [MOD_IMAGE=ref]      build (never push) the hosted mod image"
	@echo "  make local-hosted                    the tf2_descent mod against the host's local service"
	@echo "  make debug      build with symbols kept in the binaries"
	@echo "  make release    build and split debug info into .dbg sidecars"
	@echo "  make ci-build   CI's native build in the sniper container (+ GLIBC check)"
	@echo "  make run        build debug, launch, and rebuild the UI as you edit"
	@echo "  make run-debug  launch and wait for a C# debugger to attach"
	@echo "  make headless   run with no dialogs and assertions logged, not fatal"
	@echo "  make run-release  build release and launch it"
	@echo "  make test       the routine test tier (per commit); each leg reported separately"
	@echo "  make test-full  every test leg: routine + self-tests, oracles, census (before a merge)"
	@echo "  make check      build debug and run every test leg (make test-full)"
	@echo "  make check-public  build debug and run the public tier: what the public inputs can be held to (CI)"
	@echo "  make entity-census  plan_entity.md's §1 table, diffed against its baseline"
	@echo "  make entity-gates   plan_entity.md's three definition-of-done gates"
	@echo "  make entity-ratchet the 'entity' test leg: census + gates ratchet + budget"
	@echo "  make ui-preview [PNG=1] [WATCH=1]   UI mock-ups to bin/<config>/ui-preview (WATCH serves #mockups)"
	@echo "  make ui-icons                       rasterise src/client/ui/icons into its atlas (commit the result)"
	@echo "  make ui-lint    the UI design lint over every screen sample"
	@echo "  make live-gate  build debug, then load a map TWICE and gate both"
	@echo "  make movemath-gate  E1's movement port, A/B'd inside one process:"
	@echo "                  stock C++ arm then managed arm, same map, same connect"
	@echo "  make cmdgate    step 3d's claim: N commands, one crossing, measured"
	@echo "                  live at two very different N"
	@echo "  make clean      remove build output (CONFIG=debug|release for one)"
	@echo
	@echo "  make sandbox                    regenerate game/mod_test/maps/ss_sandbox.vmf"
	@echo "  make map MAP=ss_sandbox         compile it with $(MAPTOOLS_DIR)"
	@echo
	@echo "  make debug GC_SECTIONS=0              keep code nothing refers to"
	@echo "  make run MAP=dm_powerhouse            pick the map"
	@echo "  make run RUN_ARGS='-debugapi -novid'  replace the launch flags"
	@echo "  make test CONFIG=release              test (or run) another configuration"
	@echo "  SS_GPU_OFFLOAD=0 make run             do not force the NVIDIA card"
	@echo
	@echo "  tools/debug-api list                  instances currently up"

# ROOT is optional after the first time: tools/setup remembers it in
# sdk-root.lock.local. SETUP_ARGS passes anything else through
# (--accept-root, --repin-root, --config debug, --no-provision). VALVE_SDK is where the
# pristine upstream clone the oracle tests read lives (default
# ../source-sdk-2013-valve; remembered in valve-sdk.lock.local).
SETUP_ARGS ?=
# The host's packages (SourceSharp.Host.Sdk, SourceSharp.Host.Contracts) that
# the TF2Descent projects restore from $(HOST_ROOT)/bin/nupkg (src/tf2_descent/
# Directory.Build.props: Sdk 2.6.0, Contracts 1.0.0). A host change within a
# version keeps the same package identity, so the machine-wide NuGet cache is
# cleared of them first, or restore keeps a stale copy.
host-pack:
	@test -d $(HOST_ROOT) || { echo "host-pack: HOST_ROOT=$(HOST_ROOT) is not a checkout of source-sdk-sharp-host"; exit 1; }
	rm -rf $(HOME)/.nuget/packages/sourcesharp.host.sdk $(HOME)/.nuget/packages/sourcesharp.host.contracts
	$(MAKE) -C $(HOST_ROOT) pack

# The TF2Descent projects in SourceSharp.slnx restore from the host's packages,
# so a tree without them cannot build at all: setup makes them when they are
# missing, and fails (naming HOST_ROOT) when there is no host checkout.
setup:
	@tools/setup $(if $(ROOT),--root $(ROOT)) $(if $(VALVE_SDK),--valve-sdk $(VALVE_SDK)) $(SETUP_ARGS)
	@if ! ls $(HOST_PACKAGES) >/dev/null 2>&1; then \
	    echo "setup: the host packages are missing; building them (make host-pack)"; $(MAKE) --no-print-directory host-pack; fi

# ROOT from stock upstream instead of the fork: clone Valve's
# source-sdk-2013 into SDK_DIR (or reuse a clean clone of it there), check out
# the commit patches/source-sdk-2013/UPSTREAM pins, apply our series with a
# deterministic `git am` (the same sha on every machine), then set up against
# it. tools/setup accepts that sha with no flag and leaves sdk-root.lock alone.
SDK_DIR ?= ../source-sdk-2013-upstream
sdk-upstream:
	@tools/sdk-upstream --sdk-dir $(SDK_DIR)
	@$(MAKE) --no-print-directory setup ROOT=$(SDK_DIR)

# The oracle tests (src/test/oracle) find the pristine clone here; a missing
# clone fails them. `make setup` provisions it and records its path.
SOURCESHARP_VALVE_SDK ?= $(shell cat $(CURDIR)/valve-sdk.lock.local 2>/dev/null)
export SOURCESHARP_VALVE_SDK

# Each builds its own configuration, from its own farm, whatever CONFIG says.
debug:
	$(call require-setup,debug)
	@farm/debug/src/buildsourcesharp debug $(BUILD_FLAGS)

release:
	$(call require-setup,release)
	@farm/release/src/buildsourcesharp release $(BUILD_FLAGS)

# CI's build, reproduced: the same CMake project inside Valve's sniper SDK
# image (glibc 2.31), with the image's gcc, into bin/ci-<config>/linux64, then
# the GLIBC ceiling check. Native only, and the one target that needs podman.
# Release unless CONFIG is named on the command line.
CI_CONFIG = $(if $(filter command line,$(origin CONFIG)),$(CONFIG),release)
# TARGET=<one CMake target> builds just that library (CI builds each in its own
# step); without it, everything, then the GLIBC gate.
CI_TARGET = $(if $(filter command line,$(origin TARGET)),$(TARGET),)
ci-build:
	$(call require-setup,$(CI_CONFIG))
	@tools/build/ci-build $(CI_CONFIG) $(CI_TARGET)

#-----------------------------------------------------------------------------
# Running.
#
# Not backgrounded: this is meant to be watched. Ctrl-C ends it, and the temp
# directory goes with it.
#-----------------------------------------------------------------------------
# A development run is three things, not one: the game, the bundler watching the
# UI sources, and the proxy that puts a browser in front of the game — which the
# game cannot do for itself, since it listens on a Unix socket and no browser can
# open one.
#
# A debug build serves the interface straight out of the tree the bundler writes
# into, so an edit is on disk a second after it is saved and the page reloads
# itself when it notices. Both helpers are killed with the game, however it ends.
#
# One recipe on one line, because make gives each line its own shell and a
# background job started in an earlier one cannot be stopped from a later one.
#-----------------------------------------------------------------------------
# A private copy of the binaries to run against.
#
# Rebuilding while a game is up is the normal way to work here, and it used to
# reach into the running session. The build already renames rather than
# overwrites, which protects a .so the process has already mapped -- but not
# everything is loaded at startup. The development API assembly is loaded on
# demand, so a game that had not opened it yet would load whatever the rebuild
# had just put there, against a native side from before it.
#
# So the game runs out of a directory of its own, and everything in it is
# shared unless the build rewrites it:
#
#   the mod's content  symlinked. Maps, materials, sound, gameinfo -- a build
#                      never touches any of it, and sharing means a map compiled
#                      mid-session is picked up without restarting.
#
#   bin/dotnet         symlinked. 80MB of bundled .NET runtime that no build
#                      writes; it is placed once and read thereafter.
#
#   bin/linux64        hard linked. 205MB of debug .so that every build
#                      replaces -- by rename, so the link keeps the old inode
#                      and the running game keeps the binaries it started with.
#
#   bin/managed        really copied, and this is the one that has to be. MSBuild
#                      writes its output in place rather than renaming, so a hard
#                      link or a symlink would be written THROUGH and corrupt the
#                      assemblies of the running game. It is 3MB.
#
# Anything new under bin/ is hard linked, which is the safe default: it isolates
# the running game from a rewrite whether or not anyone remembers to classify it.
#
# Removed with the rest of the run directory afterwards. Dropping a hard link
# frees nothing and touches nothing.
#-----------------------------------------------------------------------------
# Beside the mod, not under TMPDIR. A hard link cannot cross a filesystem and
# /tmp is its own, so a snapshot there falls back to copying 205MB every run.
# The two directories answer different needs anyway: TMPDIR is where the engine
# takes its single-instance lock, this is where its binaries live.
RUN_SNAP := $(CURDIR)/tools/.run/$(RUN_ID)
RUN_MOD  := $(RUN_SNAP)/mod
run-snapshot:
	@mkdir -p $(RUN_MOD)/bin
	@for entry in $(MOD)/*; do \
	    name=$$(basename "$$entry"); \
	    if [ "$$name" != bin ]; then ln -sfn "$$entry" "$(RUN_MOD)/$$name"; fi; \
	done
	@for entry in $(MOD)/bin/*; do \
	    name=$$(basename "$$entry"); \
	    case "$$name" in \
	      dotnet) \
	        ln -sfn "$$entry" "$(RUN_MOD)/bin/$$name" ;; \
	      managed) \
	        cp -a "$$entry" "$(RUN_MOD)/bin/$$name" ;; \
	      *) \
	        cp -al "$$entry" "$(RUN_MOD)/bin/$$name" 2>/dev/null \
	          || cp -a "$$entry" "$(RUN_MOD)/bin/$$name" ;; \
	    esac; \
	done
	@echo "==> running against a snapshot in $(RUN_MOD) — rebuilding will not disturb it"

# The launcher is run from bin/<config> and handed the mod with -game. It finds
# libsteam_api.so beside itself, in the shared bin/linux64; without -game it
# would look for a mod folder named after itself (mod_sharp), which this layout
# does not have.
run: $(CONFIG) node run-snapshot
	$(call require-setup,$(CONFIG))
	@mkdir -p $(RUN_TMP) tools/.npm-tmp
	@echo "==> running $(MAP) from $(GAME) (temp dir $(RUN_TMP))"
	@echo "==> web UI at http://127.0.0.1:$(UI_PORT)/ — it rebuilds and reloads as you edit"
	@cd $(WEBUI) && [ -d node_modules ] || env $(UI_ENV) npm ci --no-audit --no-fund
	@known="$$(ls $(SOCKET_DIR)/*.sock 2>/dev/null | sort)"; \
	  ( cd $(WEBUI) && env $(UI_ENV) node build.mjs --watch ) > $(CURDIR)/tools/.ui-watch.log 2>&1 & \
	  watcher=$$!; \
	  ( for _ in $$(seq 240); do \
	      [[ -n "$$(comm -13 <(echo "$$known") <(ls $(SOCKET_DIR)/*.sock 2>/dev/null | sort))" ]] && break; \
	      sleep 0.5; \
	    done; \
	    exec $(CURDIR)/tools/debug-api-proxy $(UI_PORT) ) > $(RUN_TMP)/ui.log 2>&1 & \
	  proxy=$$!; \
	  trap 'kill $$watcher $$proxy 2> /dev/null' EXIT INT TERM; \
	  cd $(GAME) && . $(GPU_OFFLOAD) && TMPDIR=$(RUN_TMP) $(LAUNCH) -game $(RUN_MOD) $(RUN_ARGS) +map $(MAP); \
	  status=$$?; kill $$watcher $$proxy 2> /dev/null; \
	  cp -f $(RUN_TMP)/ui.log $(CURDIR)/tools/.ui-proxy.log 2>/dev/null; \
	  rm -rf $(RUN_TMP) $(RUN_SNAP); exit $$status

# A run you can attach a debugger to.
#
# Three differences from `run`, all of them forced by how debuggers find things:
#
#   - TMPDIR is left alone. CoreCLR puts its debugger transport pipes in $TMPDIR,
#     and the debugger looks in its own — normally /tmp. A private one per run,
#     which is what lets two games run at once, hides the runtime from the
#     debugger entirely. So a debug session is one game at a time.
#   - SOURCESHARP_DEBUG is set, which makes the shell allow a debugger to attach
#     (Linux refuses it otherwise) and makes the managed side wait for one before
#     it runs anything.
#   - No web UI watcher or proxy: the game is stopped at a breakpoint most of the
#     time, and a UI polling a frozen game is noise. Use `make run` for that.
run-debug: $(CONFIG)
	$(call require-setup,$(CONFIG))
	@echo "==> running $(MAP) from $(GAME) for debugging"
	@echo "==> it will wait for a debugger; attach with F5 in VS Code (\"Debug the game (C#)\")"
	@cd $(GAME) && . $(GPU_OFFLOAD) && SOURCESHARP_DEBUG=1 $(LAUNCH) $(LAUNCH_MOD) $(RUN_ARGS) +map $(MAP)

# Unattended: no dialogs, assertions logged rather than fatal, and in the
# background (BACKGROUND=0 to have it take the focus).
headless: BACKGROUND ?= 1
headless: INSTANCE ?= 1
headless: $(CONFIG)
	$(call require-setup,$(CONFIG))
	@mkdir -p $(RUN_TMP)
	@echo "==> running $(MAP) from $(GAME) headless (temp dir $(RUN_TMP))"
	@cd $(GAME) && . $(GPU_OFFLOAD) && TMPDIR=$(RUN_TMP) $(LAUNCH) $(LAUNCH_MOD) $(HEADLESS_ARGS) +map $(MAP); \
	  status=$$?; rm -rf $(RUN_TMP); exit $$status

# A DEDICATED SERVER (plan_host_sdk §2.5): the mod's engine.dedicated role
# from mods.yaml, no window and no GPU. Unhosted (-unhosted, decision 6.1:
# local testing only) unless HOSTED=1, which connects to the local hosting
# service's file (make local-hosted starts the service). Its own port, so it
# can run beside a client.
DEDICATED_MOD   ?= tf2_descent
DEDICATED_MAP   ?= $(MAP)
DEDICATED_PORT  ?= 27015
DEDICATED_ARGS  ?= -debugapi -debugapisocket -condebug -nomessagebox -nocrashdialog -assertlog +maxplayers 8 $(DEDICATED_TIMED_LOG_ARGS)
HOST_LOCAL_FILE ?= $(HOST_ROOT)/bin/local/local-host.json

dedicated: $(CONFIG)
	$(call require-setup,$(CONFIG))
	@mkdir -p $(RUN_TMP)
	@echo "==> dedicated $(DEDICATED_MOD) on $(DEDICATED_MAP), port $(DEDICATED_PORT)$(if $(HOSTED), hosted by $(HOST_LOCAL_FILE), unhosted)"
	@cd $(GAME) && TMPDIR=$(RUN_TMP) $(LAUNCH) -mod $(DEDICATED_MOD) -dedicated $(DEDICATED_ARGS) -port $(DEDICATED_PORT) \
	  $(if $(HOSTED),-hostlocal $(HOST_LOCAL_FILE),-unhosted) +map $(DEDICATED_MAP); \
	  status=$$?; rm -rf $(RUN_TMP); exit $$status

# A player's client on a mod, joining ADDR (host:port): how a Steam client
# reaches a hosted tf2_descent server, locally or through the gateway.
JOIN_MOD ?= tf2_descent
join: $(CONFIG)
	$(call require-setup,$(CONFIG))
	@test -n "$(ADDR)" || { echo "join: ADDR=<host:port> is required"; exit 2; }
	@case "$(ADDR)" in 127.*) echo "join: warning: a dedicated server ignores connectionless packets from 127.0.0.1 (plan_host_sdk P6); dial this machine's LAN address";; esac
	@mkdir -p $(RUN_TMP)
	@cd $(GAME) && . $(GPU_OFFLOAD) && TMPDIR=$(RUN_TMP) $(LAUNCH) -mod $(JOIN_MOD) $(filter-out -hushsteam,$(RUN_ARGS)) +connect $(ADDR); \
	  status=$$?; rm -rf $(RUN_TMP); exit $$status

run-release: override CONFIG = release
run-release: release
	$(call require-setup,$(CONFIG))
	@mkdir -p $(RUN_TMP)
	@echo "==> running $(MAP) against the release build ($(GAME))"
	@cd $(GAME) && . $(GPU_OFFLOAD) && TMPDIR=$(RUN_TMP) $(LAUNCH) $(LAUNCH_MOD) $(RUN_ARGS) +map $(MAP); \
	  status=$$?; rm -rf $(RUN_TMP); exit $$status

#-----------------------------------------------------------------------------
# Maps.
#
# The compilers are not in this repo. They are source-sdk-map-tools, a fully
# managed vbsp/vvis/vrad (`ssmap`), checked out beside this one:
#
#   make map MAP=ss_sandbox                        compile game/mod_test/maps/ss_sandbox.vmf
#   make map MAP=x MAPTOOLS_DIR=/path/to/map-tools a checkout somewhere else
#
# The Wine, Proton, steamcmd and SDK-toolset machinery that used to live here
# (map-tools, map-tools-check, toolgame, hammer, tools/maptools) went with the
# move to source-sdk-sharp: ssmap runs natively and reads the mod's gameinfo.txt
# itself, so none of it has anything left to find.
#-----------------------------------------------------------------------------
MAPTOOLS_DIR ?= ../source-sdk-map-tools
MAPCOMPILE   := $(MAPTOOLS_DIR)/src/SourceSharp.MapCompile

# How ssmap is run. `$(DOTNET) run` builds it on first use; point SSMAP at a
# published ssmap binary to skip that.
SSMAP     ?= $(DOTNET) run --project $(MAPCOMPILE) -c Release --

# Map sources live with the mod, in the repo; tools/setup links each one into
# bin/<config>/mods/test/maps, which is a real directory so the compiled .bsp
# can land beside the links.
MAPSRC    := game/mod_test/maps
MAPGEN    := src/mapgen

# vvis and vrad are the slow half. -fast is what makes an edit-and-look loop
# possible at all; a final compile is the same targets with VIS_ARGS/RAD_ARGS
# emptied, which is why they are variables rather than literals.
VBSP_ARGS  ?=
VIS_ARGS   ?= -fast
RAD_ARGS   ?= -both -fast

# ss_sandbox is generated rather than drawn: it places one worked example of
# every entity the mod implements, and SourceSharp.Tests fails when one is
# missing. So the .vmf is build output -- regenerate it here, then
# "make map MAP=ss_sandbox" to compile it. Needs no map tools.
#
# The destination is named, not left to MapGen's own search for the repo root,
# so it is the mod's map source in this repo and nowhere else.
sandbox:
	@$(DOTNET) run --project $(MAPGEN) -- $(CURDIR)/$(MAPSRC)/ss_sandbox.vmf

# The compile happens in the mod's maps/ directory: ssmap, like vbsp, writes the
# .bsp beside the .vmf it was given, and the .vmf there is tools/setup's link to
# the source in $(MAPSRC). The side files (.prt, .lin, .log) are removed after.
#
# Both ends are checked rather than assumed. A compile that half worked and a
# map that went nowhere look identical from the console, and the next thing
# anyone does is launch the game and get "map not found" -- which reads as a
# broken map rather than a build that never finished.
#
# ss_sandbox's source is generated, so compiling it has to regenerate it first.
# Skipping that cost a debugging round trip already: a fix landed in the
# generator, "make map" compiled the .vmf still sitting on disk from before it,
# and the bug appeared unfixed in game.
GENERATED_MAPS := ss_sandbox

# tf2_descent_town (plan_host_sdk §2.10): generated by MapGen's `town`, compiled
# into the tf2_descent mod's maps/. Both files are per-tree build output, never
# committed or copied between trees (CLAUDE.md, on ss_sandbox.bsp). A leak
# fails the build: vbsp then skips visibility, and the town would load
# fullbright with nothing to say why.
TOWN_MOD := $(CURDIR)/bin/$(CONFIG)/mods/tf2_descent
town:
	@test -d "$(MAPCOMPILE)" || { echo "error: no map compiler at $(MAPCOMPILE) (MAPTOOLS_DIR)"; exit 1; }
	$(call require-setup,$(CONFIG))
	@mkdir -p $(TOWN_MOD)/maps
	@$(DOTNET) run --project $(MAPGEN) -- town $(TOWN_MOD)/maps/tf2_descent_town.vmf
	@echo "==> ssmap all tf2_descent_town"
	@set -o pipefail; $(SSMAP) all -game $(TOWN_MOD) $(TOWN_MOD)/maps/tf2_descent_town.vmf \
	    --vbsp $(VBSP_ARGS) --vvis $(VIS_ARGS) --vrad $(RAD_ARGS) 2>&1 | tee $(TOWN_MOD)/maps/tf2_descent_town.compile.log
	@! grep -qi "leak" $(TOWN_MOD)/maps/tf2_descent_town.compile.log || { \
	    echo "error: tf2_descent_town leaks (see $(TOWN_MOD)/maps/tf2_descent_town.compile.log)"; exit 1; }
	@! grep -q "Error loading\|Can't load skybox" $(TOWN_MOD)/maps/tf2_descent_town.compile.log || { \
	    echo "error: tf2_descent_town names a model or sky the compiler could not load (see the compile log)"; exit 1; }
	@rm -f $(TOWN_MOD)/maps/tf2_descent_town.prt $(TOWN_MOD)/maps/tf2_descent_town.log $(TOWN_MOD)/maps/tf2_descent_town.lin
	@test -f $(TOWN_MOD)/maps/tf2_descent_town.bsp || { echo "error: no tf2_descent_town.bsp"; exit 1; }
	@echo "==> $(TOWN_MOD)/maps/tf2_descent_town.bsp  ($$(du -h $(TOWN_MOD)/maps/tf2_descent_town.bsp | cut -f1))"

# The tf2_descent mod against the real hosting service, locally: the host's local
# service, a hosted hub on tf2_descent_town, and a server per level the service
# creates (tools/local-hosted; Ctrl-C stops what it started).
local-hosted:
	@tools/local-hosted --config $(CONFIG) --host-root $(HOST_ROOT)

# The mod image a hosted pod copies to /game (tools/mod-image: built locally,
# never pushed). CONFIG=release for the real tier; MOD_IMAGE names the tag.
MOD_IMAGE ?= localhost/tf2-descent-mod:dev
mod-image:
	@tools/mod-image --config $(CONFIG) --mod tf2_descent --ref $(MOD_IMAGE)

#-----------------------------------------------------------------------------
# Map-load profiling (plan_map_load.md P0). tools/loadprof/lp drives a listen
# server through five loads with the LD_PRELOAD probe and prints per-phase
# costs; `lp hosted` does the same for a remote client of the k3s stack. The
# probe is original work built against no SDK header, so it is compiled here
# with the host compiler rather than through the CMake project.
#
#   make loadprof [CONFIG=release] [LOADPROF_ARGS=--perf]
#   tools/loadprof/lp phases tools/.loadprof/<run>
#
# A load's GPU-bound half depends on what else shares the render GPU: compare
# runs taken with the same number of other games up (each run's meta.json).
LOADPROF_PROBE := bin/$(CONFIG)/loadprof/ssprobe.so
LOADPROF_ARGS  ?=

loadprof-probe: $(LOADPROF_PROBE)
$(LOADPROF_PROBE): tools/loadprof/ssprobe.c
	@mkdir -p $(dir $@)
	clang -shared -fPIC -O1 -Wall -Wextra -Werror -o $@ $< -ldl -lpthread -lrt

loadprof: $(CONFIG) loadprof-probe
	$(call require-setup,$(CONFIG))
	tools/box-ready gate --wait
	tools/loadprof/lp listen --config $(CONFIG) $(LOADPROF_ARGS)

map: $(if $(filter $(MAP),$(GENERATED_MAPS)),sandbox)
	@test -d "$(MAPCOMPILE)" || { \
	    echo "error: no map compiler at $(MAPCOMPILE)"; \
	    echo "       The compilers are source-sdk-map-tools, a separate repo. Clone it"; \
	    echo "       beside this one (as $(MAPTOOLS_DIR)), or point MAPTOOLS_DIR at"; \
	    echo "       your checkout: make map MAP=$(MAP) MAPTOOLS_DIR=/path/to/it"; \
	    exit 1; }
	@test -f $(MAPSRC)/$(MAP).vmf || { \
	    echo "error: no $(MAPSRC)/$(MAP).vmf"; exit 1; }
	$(call require-setup,$(CONFIG))
	@mkdir -p $(MOD)/maps
	@ln -sfn $(CURDIR)/$(MAPSRC)/$(MAP).vmf $(MOD)/maps/$(MAP).vmf
	@echo "==> ssmap all $(MAP)"
	@$(SSMAP) all -game $(MOD) $(MOD)/maps/$(MAP).vmf \
	    --vbsp $(VBSP_ARGS) --vvis $(VIS_ARGS) --vrad $(RAD_ARGS)
	@rm -f $(MOD)/maps/$(MAP).prt $(MOD)/maps/$(MAP).log $(MOD)/maps/$(MAP).lin
	@test -f $(MOD)/maps/$(MAP).bsp || { \
	    echo "error: the compiler produced no $(MOD)/maps/$(MAP).bsp"; exit 1; }
	@echo "==> $(MOD)/maps/$(MAP).bsp  ($$(du -h $(MOD)/maps/$(MAP).bsp | cut -f1))"
# The coverage rule, against the map that was just produced.
#
# It runs HERE and not in `make test` because `make test` has no .bsp to look
# at: compiling one needs the map tools, which the suite deliberately does
# without. That gap is not academic — the compiled ss_sandbox.bsp once sat 72
# classnames behind the generator, env_smokestack among them, while both
# `make test` and the live gate stayed green, and the entity that was missing
# is the one whose missing default crashed the client.
#
# Only for the generated maps: dm_lockdown is a hand-drawn map that has never
# claimed to demonstrate every class, and holding it to this rule would say
# nothing true.
	$(if $(filter $(MAP),$(GENERATED_MAPS)),@echo "==> coverage: $(MAP).bsp against every class the mod registers")
	$(if $(filter $(MAP),$(GENERATED_MAPS)),@$(DOTNET) run --project $(MAPGEN) --no-build -- mapcheck $(MOD)/maps/$(MAP).bsp)
# And the staleness rule, which asks a different question: not "is every class
# in there" but "does what came out match what went in".
#
# Zero by construction here, since the .vmf was regenerated two rules up — which
# is the point. It is the check that catches a compile that used the .vmf
# already sitting on disk instead of the one just written, which has happened,
# and it catches the wrong-keyvalue case coverage cannot see: a map with every
# class present and spawnflags 0 where the generator says 2.
#
# The same judgement runs in `make test` (SandboxArtefactTests), so a map that
# goes stale between compiles is caught without anyone re-running this. Here it
# fails on the spot, while the person who compiled it is still watching.
	$(if $(filter $(MAP),$(GENERATED_MAPS)),@echo "==> staleness: $(MAP).bsp against the generator that produced it")
	$(if $(filter $(MAP),$(GENERATED_MAPS)),@$(DOTNET) run --project $(MAPGEN) --no-build -- mapdrift $(MOD)/maps/$(MAP).bsp)
#-----------------------------------------------------------------------------
# Tests.
#
# Separate from the build because the test project is not in the solution: it
# references every other project and would drag the whole graph into a build
# that only wants the shipping assemblies.
#-----------------------------------------------------------------------------
# One variable per test leg. `make test` hands all of them to
# tools/run-test-legs; `make cpp-test` and `make ui-test` run one directly. The
# command is written down once, so a standalone target and the leg `make test`
# runs cannot drift apart.
#
# WHY A RUNNER AND NOT THREE RECIPE LINES. This was:
#
#	test: ui
#		@$(DOTNET) test $(TESTS) --nologo
#		@$(MAKE) --no-print-directory cpp-test
#		@$(MAKE) --no-print-directory ui-test
#
# and make abandons a recipe at the first line that fails. So ANY managed
# failure took the native suite (963 gtests across 81 binaries, plus 4
# must-not-compile checks) and the UI suite (349 tests) with it -- and reported
# NOTHING about them. Not "skipped", not "not run". Nothing. A leg that never
# ran looked exactly like a leg that passed, and two managed failures left
# 1,300-odd tests unrun with no line anywhere saying so.
#
# tools/run-test-legs runs every leg whatever the ones before it did, prints a
# summary naming each leg's result at the tail where it will be read, and exits
# non-zero if any leg failed, ran nothing, or never ran. Read its header: the
# distinctions it draws are the point, and its --self-test is the first leg
# below because an instrument that reports "passed" for a leg it never measured
# is the exact bug this replaced.
# The native leg is the ctest suite src/test/native/CMakeLists.txt defines,
# built by the <config> preset on the host and run by the farm's
# src/buildcpptests (tools/build/buildcpptests) in the tree its tests were
# written against: cwd farm/<config>/src, binaries in bin/<config>/cpptest. The
# entity leg reads that farm's ninja graph and bin/<config>'s .so files, and
# SOURCESHARP_CONFIG is how it is told which.
MANAGED_TEST  = $(DOTNET) test $(TESTS) --nologo
# The suite is split by side (plan_side_isolation P6): each side's facts in a
# project that references that side alone, and the cross-side facts (and the
# side-neutral ones) in src/test/managed, which references both.
SERVER_TEST   = $(DOTNET) test src/test/server --nologo
CLIENT_TEST   = $(DOTNET) test src/test/client --nologo
CPP_TEST      = $(FARM)/src/buildcpptests
# The oracles (src/test/oracle): ports checked against upstream Valve code read
# from the pristine clone (valve-sdk.lock, SOURCESHARP_VALVE_SDK above) -- the
# managed project, then the native binaries labelled `oracle`.
ORACLE_MANAGED_TEST = $(DOTNET) test src/test/oracle/managed --nologo
# The TF2Descent mod's facts (src/tf2_descent/tests, plan_host_sdk): their own project,
# because they need the host packages the core tests must never reference.
DESCENT_TEST  = $(DOTNET) test src/tf2_descent/tests --nologo
ORACLE_NATIVE_TEST  = $(FARM)/src/buildcpptests --oracle
# THE MANAGED LEGS RUN THE BUILT ASSEMBLIES. `dotnet test <project>` walks
# MSBuild's whole up-to-date graph first: 5.5 s for the client project's 999
# tests, against 0.7 s for `dotnet test <its dll>` (79d3b8af+, warm), and the
# legs paid it five times in a row -- in one @dotnet group, because concurrent
# MSBuild runs have hung here. So `make test` builds the solution ONCE (every
# test project is in SourceSharp.slnx; 1.7 s warm), and the legs hand VSTest
# the assemblies: no MSBuild in any leg, so they run at once. The managed
# tests are always the Debug build, whatever CONFIG says, as before. The
# standalone targets (managed-test, descent-test, oracle-test) still build.
TEST_BUILD    = $(DOTNET) build $(SLN) -v q --nologo -c Debug
TEST_DLLS    := $(CURDIR)/bin/debug
MANAGED_LEG   = $(DOTNET) test $(TEST_DLLS)/tests/SourceSharp.Shared.Tests.dll --nologo
SERVER_LEG    = $(DOTNET) test $(TEST_DLLS)/tests-server/SourceSharp.Server.Tests.dll --nologo
CLIENT_LEG    = $(DOTNET) test $(TEST_DLLS)/tests-client/SourceSharp.Client.Tests.dll --nologo
DESCENT_LEG   = $(DOTNET) test $(TEST_DLLS)/tf2_descent-tests/TF2.Descent.Tests.dll --nologo
ORACLE_MANAGED_LEG = $(DOTNET) test $(TEST_DLLS)/oracle-tests/SourceSharp.OracleTests.dll --nologo
# The census tier (ss_census() in src/test/native/CMakeLists.txt): make test-full only.
CENSUS_TEST   = $(FARM)/src/buildcpptests --census
ORACLE_TEST   = $(ORACLE_MANAGED_TEST) && $(ORACLE_NATIVE_TEST)
# The tree checks (tools/checks.tsv): every instrument that scans the tree,
# each after its own self-test, run at once by tools/run-checks. They were one
# serial `&&` chain in the runner leg, 33 s at 79d3b8af; at once they take as
# long as the slowest.
CHECKS_TEST   = export SOURCESHARP_CONFIG=$(CONFIG) && tools/run-checks
ENTITY_TEST   = export SOURCESHARP_CONFIG=$(CONFIG) && tools/entity-ratchet --self-test && tools/entity-ratchet && tools/server-novalve-census --self-test && tools/server-novalve-census && tools/client-novalve-census --self-test && tools/client-novalve-census
UI_TEST      := echo "==> type-checking and testing the web UI" && \
                mkdir -p tools/.npm-tmp && \
                cd $(WEBUI) && \
                env $(UI_ENV) npm run check --silent && \
                env $(UI_ENV) npm test --silent

# `ui` first, and not as a convenience: the dev UI is embedded into
# Debug.Api.Server at compile time from a generated, gitignored directory, so
# in a fresh clone or worktree there is nothing to embed and the UI tests fail
# for a reason that looks nothing like the cause. The csproj now refuses to
# build without it; this makes `make test` satisfy that on its own. It stays a
# prerequisite rather than a leg: with no UI to embed there is nothing for the
# managed or UI legs to test, and make reports a failed prerequisite loudly.
#
# Single quotes around each leg: $(UI_ENV) contains double quotes and a $$PATH
# that must survive to be expanded by the leg's own shell, not this one.
#
# `castaudit` is a check nothing else can make. It looks for casts
# that CHANGE MEANING when Phase 13's alias is armed without changing whether
# the code compiles: `(IHandleEntity *)pEntity` is a derived->base conversion
# while CBaseEntity is a class and a silent reinterpret_cast once CBaseEntity is
# IGameEntity, which has no base. No diagnostic, at any -W level, on either side
# of the switch -- so the armed compile survey cannot see them BY DEFINITION,
# and neither can the other legs. It is a source analysis, needs no toolchain,
# and takes about ten seconds.
#
# It runs its own fixtures first (`--self-test`) and only then the tree, because
# the dangerous result for this instrument is ZERO: an analysis that has quietly
# stopped working reports a clean tree. The self-test drives the production scan
# over a committed fixture tree carrying the real bug and a planted decoy, and
# the gate fails if any baseline site goes MISSING -- so a scan that finds
# nothing fails loudly instead of reading as green.
#
# `checkers` is the seventh leg and it exists because of what the first six
# were NOT running. This port has written several dozen standalone checkers
# along the way -- the census, the shard classifiers, the route verifiers, the
# lane instruments -- and every one of them was invoked by hand out of a
# README. One of them, docs/typedef-dryrun/lane_u/lane_u_instruments_test.py,
# had been RED across an unknown number of merges and nobody found out, because
# nothing ran it. `make test` was green throughout. That is worse than a check
# that cannot fail: it CAN fail, it DID fail, and the failure went nowhere.
#
# Running them is the smaller half. The other half is tools/checkers' SWEEP:
# every test-named file in docs/, src/ and tools/ must be classified in its
# manifest as `run` or as `harness` (with the reason it must not be run), every
# src/**/*_test.cpp must be named by src/test/native/CMakeLists.txt, and every *Tests.cs in
# the managed suite must carry an attribute xUnit will actually discover. A
# file that is none of those fails the leg naming the file. That is what stops
# the tree growing a new orphan next week.
#
# It runs its own fixtures first (`--self-test`) for the same reason castaudit
# does: the dangerous result for a sweep is a clean report.
#
# Hermetic and about ten seconds. The ONE part that needs a build -- lane U's
# liveness numbers, which are read from farm/release/build -- runs
# only when a release build is there and reports NOT MEASURED when it is not.
# Never a skip and never a pass: see lane_u_liveness.py's CANNOT MEASURE block,
# and note that the leg's check count in the summary below is smaller on a run
# that could not measure, so the tail of `make test` says so.
#
# EVERY LEG STARTS BY DECLARING HOW BIG IT IS. `tools/leg-floor <leg>` reads
# the tree and prints the leg's size on stdout; tools/run-test-legs counts what
# the leg then produced and fails it if the two do not agree. This is here
# because of a real summary:
#
#     native     passed      exit 0     287 gtests in 21 binaries
#     ALL 6 TEST LEGS PASSED
#
# on a leg that has 116 binaries in it. The count was real and non-zero, so
# every state run-test-legs could distinguish said "passed" -- its floor was
# ONE. A merge had landed underneath the run and truncated it, and nothing
# anywhere knew how many binaries there were supposed to be.
#
# The `&&` is load-bearing twice over. It puts the declaration in the same log
# the counts come out of, which is what ties them together; and it stops the
# leg when the tree cannot be sized, because a leg nobody can measure is
# exactly the leg that reports a fifth of itself as a pass.
#
# No number is written down here or in tools/leg-floor. src/test/native/CMakeLists.txt names
# its binaries, tools/checkers' manifest names its checkers, vitest.config.ts
# names the globs, the managed suite's fact attributes are in its sources --
# each floor is counted out of those at the moment the leg starts. Add a test
# and the floor rises on its own. That is deliberate: this port has had two
# mutation rows expire on a count that moved 599 -> 666, and the fix for a
# stale pin is to edit the pin, which teaches the reflex a floor exists to
# prevent. Read tools/leg-floor's header before adding one.
# THE LEGS RUN IN PARALLEL (tools/run-test-legs; 82 s one after another on
# 2026-09-29). name@group puts legs that must not overlap in one group, run in
# order: the two `dotnet test` legs share MSBuild (concurrent runs have hung
# here), and the native and entity legs share one ninja build directory, which
# two ninja runs at once would corrupt. oracle-native has its own
# (farm/<config>/build-oracle, the oracle-<config> preset), so it runs alone.
# THE TWO TIERS. `make test` is the routine tier, run per commit: every leg
# below except TEST_FULL_LEGS. `make test-full` (and `make check`, before a
# merge to main) runs both. Nothing is dropped: test-full's legs are exactly
# the old single tier's, plus the census leg that the native leg no longer
# runs. The routine tier leaves out what is slow and changes only with its
# own subject: the instruments' self-tests (runner, checks-full), the oracles
# (they compile upstream Valve code) and the census tier, the six native
# tests that drive the census tools over the whole build (2-15 s each, about
# 70% of the native leg's CPU; ss_census() in src/test/native/CMakeLists.txt).
# Legs in one group run in this order, so census follows native and entity
# in the ninja group.
# The managed legs read no native build output, only the CMake graph a
# configure writes, so CI runs them on their own (`make managed-legs`) before
# it compiles anything native, and then `make check CHECK_MANAGED=0`.
MANAGED_TEST_LEGS = \
    'managed:tools/leg-floor managed && $(MANAGED_LEG)' \
    'managed-server:tools/leg-floor managed-server && $(SERVER_LEG)' \
    'managed-client:tools/leg-floor managed-client && $(CLIENT_LEG)' \
    'tf2_descent:tools/leg-floor tf2_descent && $(DESCENT_LEG)'
CHECK_MANAGED ?= 1
# NATIVE_TIER=fast runs the native leg's fast tier in place of the whole leg
# (tools/test-tiers); CI's linux-check does, and linux-native-slow runs the
# rest (make cpp-test-slow).
NATIVE_TIER ?=
TEST_LEGS = \
    'checks:tools/leg-floor checks && $(CHECKS_TEST) routine' \
    $(if $(filter 0,$(CHECK_MANAGED)),,$(MANAGED_TEST_LEGS)) \
    $(if $(NATIVE_TIER),$(call TIER_LEG,native-$(NATIVE_TIER)),'native@ninja:tools/leg-floor native && $(CPP_TEST)') \
    'entity@ninja:tools/leg-floor entity && $(ENTITY_TEST)' \
    'ui:tools/leg-floor ui && $(UI_TEST)'
TEST_FULL_LEGS = \
    'runner:tools/leg-floor runner && tools/run-test-legs --self-test && tools/leg-floor --self-test' \
    'checks-full:tools/leg-floor checks-full && $(CHECKS_TEST) full' \
    'oracle-managed:tools/leg-floor oracle-managed && $(ORACLE_MANAGED_LEG)' \
    'oracle-native:tools/leg-floor oracle-native && $(ORACLE_NATIVE_TEST)' \
    'census@ninja:tools/leg-floor census && $(CENSUS_TEST)'

test: ui
	@$(TEST_BUILD)
	@tools/run-test-legs $(TEST_LEGS)

# Only the managed legs (managed, managed-server, managed-client, tf2_descent):
# the test assemblies built and run, no native build. Their CMake-graph facts
# read farm/debug/build, so that tree must be configured (not built) first;
# the launcher and mod-image facts run bin/debug/source#, so the launcher is
# published here as make debug publishes it.
managed-legs: ui
	@$(TEST_BUILD)
	@$(DOTNET) publish src/launcher/SourceSharp.Launcher.csproj -v q --nologo -c Debug
	@tools/run-test-legs $(MANAGED_TEST_LEGS)

# A QUICK LOCAL LOOP: `make test` without its slow tests, and the slow tests
# alone (tools/test-tiers; tools/test-tiers.tsv lists the slow tier, measured:
# a managed fact or native binary of 2 s or more, or a class whose facts add up
# to 20 s). The two together run what `make test` runs. Locally neither is a
# gate: `make test` and `make check` run every leg whole. CI runs the two tiers
# in jobs of their own (managed-legs-<tier>, NATIVE_TIER=fast and
# cpp-test-slow below), which together run every test. Each tier leg has
# its own floor (tools/leg-floor <leg>-fast / <leg>-slow), and a fast run names
# any fast test that has grown past the threshold. Regenerate the list with
# `tools/test-tiers measure --write` after `make debug`.
TIER_LEG = '$(if $(filter native-%,$(1)),$(1)@ninja,$(1)):tools/leg-floor $(1) && tools/test-tiers run $(1)'
TEST_FAST_LEGS = \
    'checks:tools/leg-floor checks && $(CHECKS_TEST) routine' \
    $(foreach l,$(shell tools/test-tiers legs fast),$(call TIER_LEG,$(l))) \
    'entity@ninja:tools/leg-floor entity && $(ENTITY_TEST)' \
    'ui:tools/leg-floor ui && $(UI_TEST)'
TEST_SLOW_LEGS = $(foreach l,$(shell tools/test-tiers legs slow),$(call TIER_LEG,$(l)))

# The list is read before the legs are: a list that does not parse would leave
# $(shell ...) empty, and the run would drop the managed and native legs.
test-fast: ui
	@tools/test-tiers legs fast > /dev/null
	@$(TEST_BUILD)
	@tools/run-test-legs $(TEST_FAST_LEGS)

test-slow: ui
	@tools/test-tiers legs slow > /dev/null
	@$(TEST_BUILD)
	@tools/run-test-legs $(TEST_SLOW_LEGS)

# One tier of the managed legs alone, as managed-legs runs the whole of them:
# CI's linux-managed and windows-managed jobs, one per tier.
managed-legs-fast managed-legs-slow: managed-legs-%: ui
	@tools/test-tiers legs $* > /dev/null
	@$(TEST_BUILD)
	@$(DOTNET) publish src/launcher/SourceSharp.Launcher.csproj -v q --nologo -c Debug
	@tools/run-test-legs $(foreach l,$(filter-out native-%,$(shell tools/test-tiers legs $*)),$(call TIER_LEG,$(l)))

# The native leg's slow tier alone: buildcpptests builds and runs those tests
# by name, so this needs a configured tree and not the whole debug build.
cpp-test-slow:
	@tools/test-tiers legs slow > /dev/null
	@tools/run-test-legs $(call TIER_LEG,native-slow)

# Every leg: the routine tier and the milestone tier together.
test-full: ui
	@$(TEST_BUILD)
	@tools/run-test-legs $(TEST_LEGS) $(TEST_FULL_LEGS)

# THE PUBLIC TIER: the legs whose result depends on this tree alone, which is
# what a machine with only the public inputs (CI, a fresh contributor: `make
# sdk-upstream`, no Steam, no fork) can be held to. The other legs were run
# that way at 0805b2f (make check without the host SDK package, clang 22.1.8, glibc 2.39, an
# Intel Xeon with AVX-512, 2026-10-12) and each fails for a reason that is
# not this tree's code:
#   - managed-server (6 of 6,779), managed-client (24 of 2,623),
#     oracle-native (beamsegdraw): bit-for-bit goldens recorded on one
#     machine; this one's stock C++ and .NET arithmetic differ in the last
#     bit (7.5 vs 7.499999, -3.76478958 vs -3.76478934).
#   - native (8 binaries of 314), census, entity, oracle-managed: facts that
#     read the fork's edits to Valve files, which the patch series carries
#     without comments (the markers they look for), or line counts of them.
#   - managed-server (TF2DescentTownTests), oracle-native (engine_heap): a
#     Team Fortress 2 or Source SDK Base install, which a runner has not.
# `make check-public` is `make check` over this tier. It is a FLOOR, not the
# gate: `make check` on a machine with the fork and Steam stays the gate, and
# a leg joins this tier when it passes on the public inputs, never by being
# made unable to fail. managed, tf2_descent and oracle-managed joined it
# 2026-10-12: CI's ubuntu-24.04 runner passed all three on the public inputs
# at 1f22fed (2111, 172 and 175 tests, 0 failed), with the host packages
# packed from the public host repo.
TEST_PUBLIC_LEGS = \
    'checks:tools/leg-floor checks && $(CHECKS_TEST) routine' \
    'ui:tools/leg-floor ui && $(UI_TEST)' \
    'runner:tools/leg-floor runner && tools/run-test-legs --self-test && tools/leg-floor --self-test' \
    'checks-full:tools/leg-floor checks-full && $(CHECKS_TEST) full' \
    'managed:tools/leg-floor managed && $(MANAGED_LEG)' \
    'tf2_descent:tools/leg-floor tf2_descent && $(DESCENT_LEG)' \
    'oracle-managed:tools/leg-floor oracle-managed && $(ORACLE_MANAGED_LEG)'

test-public: ui
	@$(TEST_BUILD)
	@tools/run-test-legs $(TEST_PUBLIC_LEGS)

# The native tests alone: configure the preset, build the test targets, run
# ctest (tools/build/buildcpptests). `farm/<config>/src/buildcpptests NAME` runs one.
cpp-test:
	@$(CPP_TEST)

# The census tier alone (make test-full's census leg): the six native tests
# that drive the census tools over the build. cpp-test leaves them out.
census-test:
	@$(CENSUS_TEST)

# The managed tests alone, or a subset of them:
#   make managed-test FILTER='FullyQualifiedName~JitWarmup'
# Tests run through make, so they get the MSBuild settings above; a run that
# hangs is a test to fix, not a reason to set MSBuild options by hand.
# All three managed projects run (plan_side_isolation P6): the cross-side
# src/test/managed, then src/test/server and src/test/client. A FILTER usually
# matches one project only, so a project with no match is not an error by
# itself; a FILTER that matches nothing in any of the three fails (a typo'd
# filter is not a pass), as does any project that fails.
# TEST_ARGS goes to every `dotnet test`, e.g. a trx logger for per-test times:
#   make managed-test TEST_ARGS='--logger trx --results-directory /tmp/x'
MANAGED_TEST_PROJECTS := $(TESTS) src/test/server src/test/client
managed-test:
	@set -o pipefail; rc=0; ran=0; log=$$(mktemp); \
	for p in $(MANAGED_TEST_PROJECTS); do \
	    $(DOTNET) test $$p --nologo $(if $(FILTER),--filter '$(FILTER)') $(TEST_ARGS) 2>&1 | tee $$log || rc=1; \
	    n=$$(sed -n 's/.*Total: *\([0-9][0-9]*\).*/\1/p' $$log | tail -1); \
	    ran=$$((ran + $${n:-0})); \
	done; rm -f $$log; \
	echo "==> managed-test: $$ran test(s) over $(words $(MANAGED_TEST_PROJECTS)) project(s), rc=$$rc"; \
	if [ $$ran -eq 0 ]; then echo "error: no managed test ran$(if $(FILTER), for FILTER='$(FILTER)')" >&2; exit 1; fi; \
	exit $$rc

# The TF2Descent facts alone (the tf2_descent leg of `make test`), FILTER as above.
descent-test:
	@$(DESCENT_TEST) $(if $(FILTER),--filter '$(FILTER)' -- RunConfiguration.TreatNoTestsAsError=true)

# The oracle leg alone.
oracle-test:
	@$(ORACLE_TEST)

# plan_entity.md's instruments, against the DEPLOYED binaries.
#
#   make entity-census   the §1 table, cast census and cross-edges, diffed
#                        against docs/entity-census.txt; non-zero on drift
#   make entity-gates    the three definition-of-done gates; non-zero until
#                        the port is finished, which is the point of them
#   make entity-ratchet  the `entity` leg of `make test`, on its own
#
# THE `entity` LEG (step 8c) IS WIRED TO tools/entity-ratchet AND NOT TO
# tools/entity-gates, and the difference is the whole design. entity-gates
# answers "is the port finished?", so it exits non-zero today by design and a
# leg on its exit code would be red until the last phase lands -- a leg nobody
# can keep green is a leg everybody learns to re-run. entity-ratchet compares
# every one of its rows against docs/entity-ratchet.tsv instead: red when a
# number goes UP, green when it goes down. Read that tool's header.
#
# The leg reads bin/<config>/bin/linux64 and farm/<config>/build, both
# of which are BUILD OUTPUT, so it needs `make debug` to have run in this
# checkout. (The `native` leg builds what it needs itself.) On a tree with no build it FAILS naming the
# command, and never reports a clean tree it could not measure.
#
# The hermetic halves of the two instruments -- entity_census_test and
# entity_gates_test, which drive both tools over built fixtures -- stay in the
# native leg. Those are the halves that decide whether a red here means
# anything; this leg is what makes a red arrive at all.
#
# census and gates read the tree the way the build sees it -- build/
# and game/mod_sharp/bin/linux64, relative to where they run -- so they run in
# the farm, where those paths exist.
entity-census:
	@cd $(FARM) && tools/entity-census $(CENSUS_ARGS)

CENSUS_ARGS ?=
GATES_ARGS ?=
entity-gates:
	@cd $(FARM) && tools/entity-gates $(GATES_ARGS)

entity-ratchet:
	@$(ENTITY_TEST)

#-----------------------------------------------------------------------------
# plan_globals.md step G0: static-storage state on the server link line.
#
# Run in the debug farm: the tool reads build/ (the CMake binary dir), which only the
# farm has, and finds its root from its own (unresolved) path.
#
# `globals-census` REPORTS; `globals-ledger` RECONCILES docs/globals-ledger.tsv
# against the tree -- it carries every existing ruling forward and never
# regenerates. Both read the DEBUG graph and refuse (exit 2) rather than answer
# when they cannot measure: a missing object, or a graph-built archive newer
# than the .so it links into, which is what `make debug && make release` leaves
# behind because lib/public/linux64/*.a is not config-qualified.
#-----------------------------------------------------------------------------
# sdk-links/*.txt is the Valve code we still depend on, split by consumer
# (client, server, shared, libs, tests). This prints each list's count and
# fails if a list names a fork file nothing uses (not the build graph of either
# configuration, and not a traced read recorded in tests.txt), or a file whose
# use moved to another list. Re-take the trace with tools/sdk-census --trace
# LOG --write.
sdk-census:
	@tools/sdk-census

globals-census:
	@cd farm/debug && python3 tools/globals-census summary
	@echo
	@cd farm/debug && python3 tools/globals-census gate

globals-ledger:
	@cd farm/debug && python3 tools/globals-census reseed --write

# The client's half: docs/globals-client-ledger.tsv, the same tool with
# --side client. Separate targets so the server's output is unchanged.
globals-client-census:
	@cd farm/debug && python3 tools/globals-census summary --side client
	@echo
	@cd farm/debug && python3 tools/globals-census gate --side client

globals-client-ledger:
	@cd farm/debug && python3 tools/globals-census reseed --side client --write

# THE UI MOCK-UPS (plan_client_ui §7): every screen x sample x size, and the
# gallery, to bin/<config>/ui-preview as SVG. PNG=1 adds PNGs (SkiaSharp, over
# the dm_lockdown backdrops). WATCH=1 re-renders on every save and serves them
# with the dev web UI's Mockups tab at http://127.0.0.1:7191/#mockups.
# Managed only: no native build, no game.
ui-preview:
	@tools/ui-preview --config $(CONFIG) $(if $(filter 1,$(PNG)),--png) $(if $(filter 1,$(WATCH)),--watch)

# The icon set's atlas (src/client/ui/icons/atlas), rasterised from its SVGs.
ui-icons:
	@tools/ui-preview --config $(CONFIG) --icons

# The UI design lint (plan_client_ui §5.8.5) over every sample at every size.
ui-lint:
	@tools/ui-lint --config $(CONFIG)

ui-test: node
	@$(UI_TEST)

# Downloads the pinned Node, once. Everything that needs it depends on this.
node:
	@./tools/get-node

# In the mode $(CONFIG)'s build uses (buildsourcesharp: --dev for debug), so
# `make debug` and `make test` agree on what Debug.Api.Server embeds; the
# build writes only the files that changed (src/webui/build.mjs). Before
# both, every make test rebuilt the bundle for production, every make debug
# rebuilt it --dev, and each recompiled Debug.Api.Server and its dependents.
ui: node
	@mkdir -p tools/.npm-tmp
	@cd $(WEBUI) && [ -d node_modules ] || env $(UI_ENV) npm ci --no-audit --no-fund
	@cd $(WEBUI) && env $(UI_ENV) npm run build --silent $(if $(filter debug,$(CONFIG)),-- --dev)

ui-watch: node
	@mkdir -p tools/.npm-tmp
	@cd $(WEBUI) && [ -d node_modules ] || env $(UI_ENV) npm ci --no-audit --no-fund
	@cd $(WEBUI) && env $(UI_ENV) npm run watch

# Prerequisites, in order, and make stops if `debug` fails -- which is the same
# mechanism as the bug above but not the same defect: a failed build is loud,
# make names the target and the error, and there is nothing meaningful to test
# once the binaries the tests load did not link. `test` is what has to survive
# its own parts failing, and it does.
#
# DEBUG AND NOT RELEASE, DELIBERATELY, and the question has been asked and
# answered rather than left open. `checkers` runs one `measured` entry --
# lane U's build-dependent pins -- which reads farm/release/build
# and reports NOT MEASURED here, every time, because nothing in `check` builds
# release. That reads like a gate defect. It is not one, twice over:
#
#   1. NOT MEASURED IS ALREADY NOT A FAILURE. tools/checkers scores exit 3 from
#      a `measured` entry as NOT MEASURED and excludes it from the leg's count,
#      so the tail of `make test` reports a SMALLER n on a run that could not
#      measure -- loud, and not a pass. Both halves of that are pinned in its
#      self-test: exit 3 is not a failure, and a `measured` entry that ran and
#      DISAGREED still fails the leg. A real lane-U regression is red here
#      whether or not this target builds release.
#   2. ADDING `release` WOULD NOT MAKE THE LEG GREEN -- it would make it RED,
#      for a reason that is not this gate's to settle. lane U's ledger §0'.1a
#      records the measured half failing against a FRESH release build: the two
#      (N) pins are 50/48 and 54/50/4 and the tree measures single digits,
#      because plan_tmp.md evicted the forty-four temp-entity classes. The
#      ledger says in the same breath that the pins are deliberately NOT
#      updated, because writing a measured number into a pin to make it green
#      is the ritual response its own header exists to refuse. Building release
#      here would hand `make check` a permanent red whose only cheap fix is the
#      edit lane U has already declined to make.
#
# So the split stands: `check` is the hermetic gate, and lane U's measured half
# is settled by whoever re-measures its ledger, with `--remeasure` and a diff.
check: debug test-full

# The public tier's gate: the whole build, then the legs the public inputs
# can be held to (TEST_PUBLIC_LEGS says which, and why the rest are not).
check-public: debug test-public

# The LIVE half of the gate, and the second map load is the point of it.
#
# `check` is hermetic: it builds and runs tests, and no test in it starts a
# game. What every lane then did by hand was launch one, read `debug-api ents "*"`,
# and stop -- ONE MAP LOAD, every time, by everybody. A crash lived on the
# integration branch for weeks in the gap that leaves: CWorld stopped being
# freed at level shutdown, which is invisible on the first map and takes the
# process down on the second. Nothing about any lane's gate was wrong; they were
# all measuring a game that had loaded one map.
#
# So the reload is a target rather than a sentence in a plan document. See
# tools/live-gate for what it checks and why each check is there.
#
#   make live-gate                       dm_lockdown, 473 entities / 61 classes
#   make live-gate LIVE_ARGS='--map dm_powerhouse --ents N --classes M'
#
# A SKIPPED STEP FAILS THIS TARGET unless it is recorded in
# tools/live-gate-skips.tsv, and a recorded step that STARTS PASSING fails it
# too so the row gets deleted. That is not tidiness: on 2026-09-06
# `second-client` went from PASS PASS to SKIP SKIP between two merges and this
# target still exited 0, because a skip used to cost nothing. The only thing
# that noticed was a person comparing "4 skipped" with "6 skipped" by eye.
LIVE_ARGS ?=
live-gate: $(CONFIG)
	@SOURCESHARP_CONFIG=$(CONFIG) tools/live-gate $(LIVE_ARGS)

# THE SHAPE `live-gate` CANNOT SEE -- client.so with no server.so in the process.
#
# Every gate in this tree loads a map, and a game that loads a map is a listen
# server: both DLLs in one process, ONE CoreCLR between them, and the SERVER's
# DLLInit brings it up first. So client.so's own path to the managed runtime is
# never the thing that ran, and a break in it would not show.
#
# It is not enough to launch without a map, either: the engine loads the game
# DLL at startup whether or not a map follows, so a mapless game STILL loads
# server.so and still acquires at refcount 1 on the server. `-nogamedll` is the only switch that changes that, and it is what
# tools/client-only-probe launches.
#
# Not part of `make check`: it needs a game and a GPU, like every other live
# gate here.
client-only-gate: $(CONFIG)
	@SOURCESHARP_CONFIG=$(CONFIG) tools/client-only-probe

#-----------------------------------------------------------------------------
# Live gates. Each needs a game, a map and a GPU, so none is part of `make
# check`; the hermetic half of each is a leg of `make test`.
#
# The gates of the earlier plans -- gate5, d19-gate, abtest, golden-lap,
# guard-reach and the `coverage` build they fed -- went with their scripts in
# b6c429e1, which removed the port's one-off apparatus. plan_entity.md does not
# reference them. Do not re-add a target without re-adding the tool it runs:
# a target that shells out to a deleted script fails with "No such file", and
# a `. $(GPU_OFFLOAD)` on a missing file silently takes `make run` with it.

# docs/plan-audit.md E1's live gate: the twelve CGameMovement methods that moved
# to C#, measured against the stock C++ bodies they forward from.
#
# AN A/B INSIDE ONE PROCESS, and that shape is the point. Run before and after a
# change, tools/prediction-jump-gate compares two builds that differ by
# everything that landed between them; the misprediction meter is
# delta-compressed per dword, so a low count is not proof on its own
# (docs/divergences.md). sourcesharp_movement_managed is FCVAR_REPLICATED, so
# both arms are taken from one connect on one map with both sides always running
# the same half of the movement code.
#
# AND IT REFUSES TO PASS ON A DEAD PORT. A build where managed never bound the
# ten slots reports zero mispredictions too, because every forwarder returns
# false and the stock body runs. So the gate reads cs_{sv,cl}_move_managed_report
# and requires the counters to be FLAT with the switch off and to have GROWN ON
# BOTH SIDES with it on.
#
# NOT part of `make check`, for live-gate's and gate5's reason: it needs a game,
# a map and a GPU. The arithmetic it gates is covered hermetically by
# MoveMathTests (52 facts) in the managed leg.
MOVEMATH_ARGS ?=
movemath-gate: $(CONFIG)
	@SOURCESHARP_CONFIG=$(CONFIG) tools/movemath-gate $(MOVEMATH_ARGS)

# plan_entity.md step 3d's own gate: the usercmd path costs ONE crossing per
# ProcessUsercmds whatever N is, where it used to cost three per COMMAND.
#
# It hangs on the RATIO of commands actually run to crossings that carried them,
# because the crossing counter alone cannot fail -- a build that queued commands
# and never ran one posts the same number. `ran` comes from inside the managed
# loop and zero is a failure.
#
# NOT part of `make check`, for movemath-gate's reason: it needs a game, a map
# and a GPU. The flatten and the loop's ORDER are covered hermetically by
# PlayerCommandTests (40 facts) in the managed leg.
CMDGATE_ARGS ?=
cmdgate: $(CONFIG)
	@SOURCESHARP_CONFIG=$(CONFIG) tools/3d-cmdgate $(CMDGATE_ARGS)

# Every configuration unless one is named: `make clean CONFIG=release`.
# bin/<config> is the whole game directory, so this also drops what tools/setup
# provisioned into it (the mod links, the bundled runtime): `make setup` puts
# it back, and the build says so if it is missing. The farm's links stay; only
# its CMake build trees (farm/<config>/build, build-ci) go, and CI's output.
CLEAN_CONFIGS = $(if $(filter command line,$(origin CONFIG)),$(CONFIG),debug release)
clean:
	@echo "==> removing build output ($(CLEAN_CONFIGS))"
	@for c in $(CLEAN_CONFIGS); do rm -rf bin/$$c bin/ci-$$c farm/$$c/build farm/$$c/build-ci farm/$$c/src/_vpc_; done
	@rm -rf tools/.run
	@rm -rf src/debug_api/ui

# A socket file outlives a crashed game and then looks like a live instance.
clean-sockets:
	@rm -rf /tmp/sourcesharp-debug-api
	@echo "==> dev API sockets cleared"

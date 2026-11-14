"""The flags every LOCAL game launch carries: the Python half.

The Makefile's half is LOCAL_GAME_ARGS; `python3 tools/local_launch.py
--self-test` holds the two equal (a routine row in tools/checks.tsv).

    +sv_lan 1 -hushsteam
                   a LAN-only listen server with no Steam game server (user
                   approval 2026-10-05). +sv_lan 1: the server validates no
                   Steam ticket. -hushsteam (engine.so
                   0x4949b0) skips SteamGameServer_InitSafe, so the server logs
                   "This server will operate in LAN mode only", makes no
                   anonymous game-server logon and starts no BeginAuthSession
                   for the joining client (no "S3: Duplicate client
                   connection"). The CLIENT still fetches a ticket for any game
                   with maxplayers > 1 (GetChallengeType, engine.so 0x4b8bf0,
                   never reads either flag); nobody validates it.
                   MEASURED 2026-10-05 at 8febc29c (debug, -maxplayers 4,
                   background; each game joined, then live-gate's
                   second-client step): 30 rounds of 4 or 6 concurrent games,
                   15 without -hushsteam and 15 with. 249 joins (119 without,
                   130 with), no "GetAuthSessionTicket failed",
                   "RequireSteam", "STEAMAUTH: Client" or "Disconnect: " line.
                   Without the switch each second-client join logged one
                   "S3: Duplicate client connection" (56); with it, none.
                   So several joined games need no lease. The failures were
                   start-up stalls before any network traffic (docs/todo.md).
                   STEAM_AUTH=1 leaves both out, for the Steam-auth features
                   (TF2Descent Reauth, type 60). Hosted and dedicated servers
                   are not local games and do not use this, and `make join`
                   (a client of a hosted server) drops -hushsteam.
    +fps_max 60    every local game capped at 60 frames per second (user
                   ruling 2026-10-04): less CPU and GPU per game when several
                   run at once. The server's ticks are not frames: a listen
                   server still simulates at its tick rate.
    -background    the window does not take the user's desktop focus (user
                   ruling 2026-10-04): agents' games kept stealing it. The
                   launcher (src/launcher/Launcher.cs) names the window's X11
                   class sourcesharp-bg, which tools/kwin-bg-rule's KWin rule
                   keeps unfocused and below (never minimised: SDL2 spins
                   forever on a window minimised during start-up; see that
                   tool), and sets
                   +engine_no_focus_sleep 0 so an unfocused game still runs
                   at 60 fps (19 fps without it). ON for every Python tool,
                   since they are all automated; BACKGROUND=0 turns it off.
                   The Makefile is the other way round: OFF for the user's
                   `make run` and `make run-debug`, ON for `make headless`
                   (target-specific BACKGROUND ?= 1), and BACKGROUND=0/1
                   overrides either.
    -instance      the game runs on a game directory of its own under
                   /tmp/ssi-<user>/<pid> (src/launcher/Launcher.cs), never
                   under TMPDIR: that dir is -game, and libtier0 refuses a
                   command line over 512 bytes. Links to the tree's
                   mods/<id>/, private copies of the files the engine rewrites
                   in place. MEASURED 2026-10-05 (strace, one game): 33
                   truncate-and-rewrites of videoconfig_linux.cfg during
                   start-up, 37 reads between them, cfg/config.cfg rewritten
                   at shutdown. Several games started at once read each
                   other's half-written videoconfig ("KeyValues Error:
                   LoadFromBuffer: missing {"). console.log and the
                   +con_logfile file still land in the tree's mods/<id>/,
                   where the tools read them. ON for every Python tool and
                   `make headless`; OFF for `make run` and `make run-debug`
                   (the user's settings persist). INSTANCE=0/1 overrides.
    -toolgame      the game is a tool's, a gate's or a lane's, not one a
                   person plays: it keeps no file of the user's own play. The
                   bots' experience store (ai_experience_db, plan_bots_teams_tf2
                   Part G, G.3) defaults to a file in the user's data
                   directory, and no gate may depend on what this box has
                   played, so in a tool's game that default is off; a launch
                   that wants a store names a private path
                   (+ai_experience_db /tmp/...). The launcher
                   (src/launcher/Launcher.cs) also adds it to every
                   -background or -instance game, which covers a tool that
                   builds its own flag list. ALWAYS ON here, whatever
                   BACKGROUND and INSTANCE say; `make headless` carries it in
                   its recipe; `make run`, `make run-debug`, `make dedicated`
                   and `make join` never do (a played game keeps the user's
                   store). --self-test holds all of that, and that every tool
                   that starts source# takes one of the three flags.
"""

import os
import re
import subprocess
import sys

LAN = ["+sv_lan", "1", "-hushsteam"]
FPS = ["+fps_max", "60"]
BACKGROUND = ["-background"]
INSTANCE = ["-instance"]
TOOL = ["-toolgame"]

# The flags that make a launch a tool's game: -toolgame itself, and the two the
# launcher takes for it.
TOOL_GAME_FLAGS = TOOL + BACKGROUND + INSTANCE

# Tools that name source# and start no game with it, each with why.
NOT_A_GAME = {
    "tools/launcher-name-test": "runs source# -help only",
    "tools/mod-image": "runs source# -resolve only, and copies the file",
    "tools/package-release": "names the file it packages",
    "tools/build/buildsourcesharp": "builds the file",
}

# How a tool spells the launcher where it starts it: ./source#, ./source\#,
# ./'source#', or the path joined from its parts.
LAUNCHER_WORD = re.compile(r"""source\\?#""")


def background(environ=None):
    """Whether a Python tool's game starts in the background: yes unless BACKGROUND=0."""
    environ = os.environ if environ is None else environ
    return environ.get("BACKGROUND", "1") != "0"


def instance(environ=None):
    """Whether a Python tool's game gets a game dir of its own: yes unless INSTANCE=0."""
    environ = os.environ if environ is None else environ
    return environ.get("INSTANCE", "1") != "0"


def shared_args(environ=None):
    """The flags a tool's launch shares with the Makefile's LOCAL_GAME_ARGS:
    LAN and -hushsteam unless STEAM_AUTH=1, the fps cap, -background unless
    BACKGROUND=0, and -instance unless INSTANCE=0."""
    environ = os.environ if environ is None else environ
    return (([] if environ.get("STEAM_AUTH") == "1" else list(LAN)) + list(FPS)
            + (list(BACKGROUND) if background(environ) else [])
            + (list(INSTANCE) if instance(environ) else []))


def local_args(environ=None):
    """The flags of a Python tool's launch, as argv entries: shared_args, and
    -toolgame always (a tool's game never touches the user's own files)."""
    return shared_args(environ) + list(TOOL)


def recipe_launches(makefile_text, target):
    """The lines of TARGET's recipe that start the game ($(LAUNCH) ...), with
    their continuation lines joined; [] when the target has none."""
    m = re.search(r"^%s:(?!=)[^\n]*\n((?:(?:\t[^\n]*|[ ]*|[A-Za-z_-]+:[ ]+[A-Z_]+ [?:]?=[^\n]*)\n)*)" % re.escape(target), makefile_text, re.M)
    if not m:
        return []
    # A target's own variable lines (`headless: BACKGROUND ?= 1`) come before
    # its rule: take the recipe of the rule line itself.
    rule = re.search(r"^%s:(?![^\n]*[?:]?=)[^\n]*\n((?:\t[^\n]*\n)*)" % re.escape(target), makefile_text, re.M)
    body = (rule or m).group(1).replace("\\\n", " ")
    return [line for line in body.split("\n") if "$(LAUNCH)" in line]


def tool_game_gaps(tools):
    """TOOLS is {repo path: text}. The tools that start source# with none of
    the tool-game flags and without this module's flags: each is a launch path
    that would run on the user's own experience store."""
    gaps = []
    for path in sorted(tools):
        text = tools[path]
        if path in NOT_A_GAME or path.endswith("local_launch.py") or not LAUNCHER_WORD.search(text):
            continue
        code = "\n".join(l for l in text.split("\n") if not l.lstrip().startswith("#"))
        if "local_launch" in code or any(flag in code for flag in TOOL_GAME_FLAGS):
            continue
        gaps.append(path)
    return gaps


def tracked_tools(root):
    """Every text file under tools/ that is tracked or new (not ignored), as {repo path: text}."""
    names = subprocess.run(["git", "-C", root, "ls-files", "-z", "-co", "--exclude-standard", "tools"],
                           capture_output=True, check=True).stdout.decode().split("\0")
    tools = {}
    for name in names:
        if not name:
            continue
        try:
            with open(os.path.join(root, name), encoding="utf-8") as f:
                tools[name] = f.read()
        except (UnicodeDecodeError, OSError):
            continue
    return tools


def makefile_args(makefile_text, steam_auth=False, background=False, instance=False):
    """LOCAL_GAME_ARGS as the Makefile defines it, expanded the way make would."""
    def var(name):
        m = re.search(r"^%s\s*:?=\s*(.*)$" % re.escape(name), makefile_text, re.M)
        if not m:
            raise ValueError("the Makefile defines no %s" % name)
        return m.group(1).strip()
    lan = var("LOCAL_LAN_ARGS")
    m = re.fullmatch(r"\$\(if \$\(filter 1,\$\(STEAM_AUTH\)\),,(.*)\)", lan)
    if not m:
        raise ValueError("LOCAL_LAN_ARGS is not $(if $(filter 1,$(STEAM_AUTH)),,...): %s" % lan)
    bg = var("LOCAL_BG_ARGS")
    b = re.fullmatch(r"\$\(if \$\(filter 1,\$\(BACKGROUND\)\),(.*)\)", bg)
    if not b:
        raise ValueError("LOCAL_BG_ARGS is not $(if $(filter 1,$(BACKGROUND)),...): %s" % bg)
    ins = var("LOCAL_INSTANCE_ARGS")
    n = re.fullmatch(r"\$\(if \$\(filter 1,\$\(INSTANCE\)\),(.*)\)", ins)
    if not n:
        raise ValueError("LOCAL_INSTANCE_ARGS is not $(if $(filter 1,$(INSTANCE)),...): %s" % ins)
    whole = var("LOCAL_GAME_ARGS")
    want = "$(LOCAL_LAN_ARGS) $(LOCAL_FPS_ARGS) $(LOCAL_BG_ARGS) $(LOCAL_INSTANCE_ARGS)"
    if whole != want:
        raise ValueError("LOCAL_GAME_ARGS is not %s: %s" % (want, whole))
    return (([] if steam_auth else m.group(1).split()) + var("LOCAL_FPS_ARGS").split()
            + (b.group(1).split() if background else [])
            + (n.group(1).split() if instance else []))


def target_default(makefile_text, target, name):
    """A target's own default for NAME (`target: NAME ?= 1`), or None."""
    m = re.search(r"^%s:\s*%s\s*\?=\s*(\S+)\s*$" % (re.escape(target), re.escape(name)), makefile_text, re.M)
    return m.group(1) if m else None


def target_background(makefile_text, target):
    """A target's own default for BACKGROUND (`target: BACKGROUND ?= 1`), or None."""
    return target_default(makefile_text, target, "BACKGROUND")


def self_test():
    checks = []

    def ok(cond, name):
        checks.append((bool(cond), name))

    ok(local_args({}) == ["+sv_lan", "1", "-hushsteam", "+fps_max", "60", "-background", "-instance", "-toolgame"],
       "by default: LAN, no Steam game server, the 60 fps cap, the background window, a game dir of its own, and a tool's game")
    ok("-hushsteam" not in local_args({"STEAM_AUTH": "1"}), "STEAM_AUTH=1 keeps the Steam game server")
    ok(local_args({"STEAM_AUTH": "1"}) == ["+fps_max", "60", "-background", "-instance", "-toolgame"], "STEAM_AUTH=1 drops LAN and keeps the rest")
    ok(local_args({"STEAM_AUTH": "0"}) == local_args({}), "only STEAM_AUTH=1 opts in")
    ok(local_args({"BACKGROUND": "0"}) == ["+sv_lan", "1", "-hushsteam", "+fps_max", "60", "-instance", "-toolgame"], "BACKGROUND=0 takes the focus as before")
    ok(local_args({"BACKGROUND": "1"}) == local_args({}), "BACKGROUND=1 is the default")
    ok(local_args({"INSTANCE": "0"}) == ["+sv_lan", "1", "-hushsteam", "+fps_max", "60", "-background", "-toolgame"],
       "INSTANCE=0 shares the tree's mod dir as before")
    ok(all("-toolgame" in local_args(dict(BACKGROUND=b, INSTANCE=i, STEAM_AUTH=a)) for b in "01" for i in "01" for a in "01"),
       "a tool's launch is a tool's game whatever BACKGROUND, INSTANCE and STEAM_AUTH say")
    ok("-toolgame" not in shared_args({}), "-toolgame is no part of what the Makefile's played games share")
    ok(local_args({"INSTANCE": "1"}) == local_args({}), "INSTANCE=1 is the default")
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    text = open(os.path.join(root, "Makefile")).read()
    try:
        ok(makefile_args(text, background=True, instance=True) == shared_args({}), "the Makefile's LOCAL_GAME_ARGS is the same list")
        ok(makefile_args(text, True, True, True) == shared_args({"STEAM_AUTH": "1"}), "and the same under STEAM_AUTH=1")
        ok(makefile_args(text, instance=True) == shared_args({"BACKGROUND": "0"}), "and the same under BACKGROUND=0")
        ok(makefile_args(text, background=True) == shared_args({"INSTANCE": "0"}), "and the same under INSTANCE=0")
    except ValueError as exc:
        ok(False, "the Makefile's LOCAL_GAME_ARGS reads: %s" % exc)
    ok(target_background(text, "headless") == "1", "make headless starts in the background")
    ok(target_default(text, "headless", "INSTANCE") == "1", "make headless gets a game dir of its own")
    for target in ("run", "run-debug"):
        ok(target_background(text, target) is None and re.search(r"^%s:" % target, text, re.M),
           "make %s takes the focus (the user's interactive game)" % target)
        ok(target_default(text, target, "INSTANCE") is None and re.search(r"^%s:" % target, text, re.M),
           "make %s keeps the tree's mod dir, so the user's settings persist" % target)
    ok(not re.search(r"^BACKGROUND\s*:?=", text, re.M) and not re.search(r"^BACKGROUND\s*\?=", text, re.M),
       "BACKGROUND has no global default, so a target's own ?= 1 can take effect")
    ok(not re.search(r"^INSTANCE\s*[:?]?=", text, re.M),
       "INSTANCE has no global default, so a target's own ?= 1 can take effect")
    ok(target_background(text.replace("headless: BACKGROUND ?= 1", ""), "headless") is None,
       "a Makefile without headless's default is seen to lack it")
    bad = text.replace("+fps_max 60", "+fps_max 120")
    ok(makefile_args(bad) != shared_args({}), "a Makefile that drifts is seen to differ")
    ok(re.search(r"\$\(LAUNCH\) -mod \$\(JOIN_MOD\) \$\(filter-out -hushsteam,\$\(RUN_ARGS\)\) \+connect", text),
       "make join (a client of a hosted server) drops -hushsteam")
    run_args = re.search(r"^RUN_ARGS\s*\?=\s*(.*?)(?<!\\)\n", text, re.M | re.S)
    ok(run_args and "$(LOCAL_GAME_ARGS)" in run_args.group(1), "RUN_ARGS carries LOCAL_GAME_ARGS")

    # A tool's game never touches the user's own experience store (-toolgame).
    headless = recipe_launches(text, "headless")
    ok(len(headless) == 1 and "-toolgame" in headless[0].split(),
       "make headless is a tool's game: its recipe carries -toolgame, whatever HEADLESS_ARGS a tool passes")
    for target in ("run", "run-debug", "run-release", "dedicated", "join"):
        launches = recipe_launches(text, target)
        ok(launches and not any("-toolgame" in l for l in launches),
           "make %s is a played game: it keeps the user's store" % target)
    uncommented = "\n".join(l for l in text.split("\n") if not l.lstrip().startswith("#"))
    ok(uncommented.count("-toolgame") == 1,
       "nothing else in the Makefile says -toolgame (RUN_ARGS and LOCAL_GAME_ARGS are make run's too)")
    lost = recipe_launches(text.replace(" -toolgame +map", " +map"), "headless")
    ok(len(lost) == 1 and "-toolgame" not in lost[0].split(), "a Makefile whose headless recipe lost -toolgame is seen to lack it")
    ok(recipe_launches(text, "no-such-target") == [], "a target the Makefile lacks has no launch line")
    try:
        tools = tracked_tools(root)
    except (OSError, subprocess.CalledProcessError) as exc:
        tools = {}
        ok(False, "the tracked tools could not be listed: %s" % exc)
    starters = [p for p in tools if LAUNCHER_WORD.search(tools[p]) and p not in NOT_A_GAME and not p.endswith("local_launch.py")]
    ok(len(starters) >= 6, "the scan sees the tools that start source# (%d: the gates, debug-api, the probes)" % len(starters))
    ok("tools/live-gate" in starters and "tools/debug-api" in starters, "tools/live-gate and tools/debug-api among them")
    gaps = tool_game_gaps(tools)
    ok(not gaps, "every tool that starts source# takes this module's flags or names a tool-game flag%s" % ("" if not gaps else ": NOT " + ", ".join(gaps)))
    ok(all(p in tools for p in NOT_A_GAME), "each tool excused from the scan still exists")
    ok(tool_game_gaps({"tools/new-gate": "subprocess.Popen(['./source#', '-debugapi', '+map', m])\n"}) == ["tools/new-gate"],
       "a tool that starts source# with none of the flags is named")
    ok(tool_game_gaps({"tools/new-gate": "# -background would do\nexec ./source\\# -debugapi\n"}) == ["tools/new-gate"],
       "a flag named only in a comment does not count")
    ok(tool_game_gaps({"tools/new-gate": "args = ['./source#'] + local_launch.local_args()\n"}) == [], "a tool on this module's flags is not")
    ok(tool_game_gaps({"tools/new-gate": "exec ./source\\# -background +map x\n"}) == [], "nor one that names -background itself")
    for good, name in checks:
        print("  %s  %s" % ("ok  " if good else "FAIL", name))
    bad_n = sum(1 for g, _ in checks if not g)
    print("\n%d check(s), %d failed" % (len(checks), bad_n))
    return 1 if bad_n else 0


if __name__ == "__main__":
    if sys.argv[1:] == ["--self-test"]:
        sys.exit(self_test())
    print(" ".join(local_args()))

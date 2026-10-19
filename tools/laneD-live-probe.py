#!/usr/bin/env python3
"""Lane D phase-0.5 live gate (docs/plans/tf2/stage0_core_seams.md Phase 0.5).

What the plan names, on real instances and nothing else:
  1. a headless game (the test mod, dm_lockdown) with its solo player,
  2. a second client that JOINS (assert the join before any client reading),
  3. `jointeam 2` sent by the newcomer through tools/debug-api,
  4. the team read back after the join, all required:
       a. the scoreboard's source on the server - player_manager's Team.<slot>
       b. ent_info's `team     N` line - the console readout THIS phase added,
          captured through the API's /command?capture=1
       c. the SAME field as the client decoded it, through the server's debug
          channel (a datatable change is verified only by a full join: write
          on the server, read on the client)
       d. the control: the player who never joined keeps team 0.

Built on tools/live-gate's own launch helpers (free ports, gpu-offload,
strict-port, no-HLTV, private temp dirs) - the same lines the gate launches,
imported from the worktree that holds this file, located from its own path.

Exit 0 only when every check passed. Numbers, not adjectives.
"""
import os
import subprocess
import sys
import time
ROOT = os.path.dirname(os.path.dirname(os.path.realpath(__file__)))
from importlib.machinery import SourceFileLoader
lg = SourceFileLoader("lg", os.path.join(ROOT, "tools", "live-gate")).load_module()

MAP = "dm_lockdown"
import tempfile
scratch = tempfile.mkdtemp(prefix="laneD-live-")

failures = []
def check(ok, what):
    print(("  PASS " if ok else "  FAIL ") + what)
    if not ok:
        failures.append(what)

def capture(pid, command):
    """One console command, with what it printed (tools/debug-api capture)."""
    env = dict(os.environ, DEBUG_API_PID=str(pid))
    child = subprocess.Popen([os.path.join(ROOT, "tools", "debug-api"), "capture", command],
                             stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                             text=True, env=env, start_new_session=True)
    try:
        out, _ = child.communicate(timeout=40)
        return out
    except subprocess.TimeoutExpired:
        try:
            os.killpg(os.getpgid(child.pid), 9)
        except OSError:
            pass
        return ""

server, port = lg.launch_with_free_port(lambda p: lg.server_launch(p, MAP),
                                        os.path.join(scratch, "server.log"),
                                        os.path.join(scratch, "server"))
if server is None:
    print("the server never opened a dev API socket; log tail:")
    print(open(os.path.join(scratch, "server.log")).read()[-3000:])
    sys.exit(2)
srv = lg.Api(server.pid, root=ROOT)
print("== server pid %s on port %d" % (server.pid, port))

client = None
try:
    solo = srv.wait_for(lambda: len(srv.ents("ss_player")) == 1, 180.0, 1.0)
    check(bool(solo), "the headless game holds its solo player")
    if not solo:
        sys.exit(2)

    client, _ = lg.launch_with_free_port(lg.client_launch,
                                         os.path.join(scratch, "client.log"),
                                         os.path.join(scratch, "client"))
    if client is None:
        print("a second instance never opened a dev API socket")
        sys.exit(2)

    for api in (srv, client.api):
        api.convar("net_usesocketsforloopback", 1)
    time.sleep(1.0)
    client.api.cmd("connect 127.0.0.1:%d" % port)
    joined = srv.wait_for(lambda: len(srv.ents("ss_player")) >= 2, 90.0, 1.0)
    # ASSERT THE JOIN before trusting any client reading (CLAUDE.md).
    check(bool(joined), "the second client joined: %d players on the server"
                        % len(srv.ents("ss_player")))
    if not joined:
        sys.exit(2)
    first = min(p["index"] for p in srv.ents("ss_player"))
    second = next(p["index"] for p in srv.ents("ss_player") if p["index"] != first)

    def board_row(index):
        managers = srv.ents("player_manager")
        if not managers:
            return None
        fields = srv.fields(managers[0]["index"])
        v = fields.get("Team.%d" % index)
        return int(v) if v is not None else None

    def client_team():
        """Team.<second> as the newcomer's client decoded the mirror."""
        managers = srv.ents("player_manager")
        if not managers:
            return None
        blob = srv.get("client", str(second), "entity/%d" % managers[0]["index"])
        fields = {f["name"]: f["value"] for b in blob.get("states", []) for f in b.get("fields", [])}
        v = fields.get("Team.%d" % second)
        return int(v) if v is not None else None

    # BEFORE the join: the mirror reads team 0. The differential the after-arms
    # measure against; if the before is not 0 the stimulus proves nothing.
    # (wait_for hands back the truthy VALUE the predicate returned, and a
    # board row of 0 is falsy - so the predicate tests `is not None`.)
    seen_before = srv.wait_for(lambda: 1 if board_row(second) is not None else None, 20.0)
    before = board_row(second)
    check(seen_before is not None and before == 0,
          "BEFORE the join: player_manager Team.%d reads %r (unassigned)" % (second, before))

    # THE STIMULUS, the game's own console, like a player's team menu.
    client.api.cmd("cmd jointeam 2")

    moved = srv.wait_for(lambda: 1 if board_row(second) == 2 else None, 10.0, 0.25)
    check(bool(moved), "AFTER `jointeam 2`: the server's scoreboard source reads "
                       "Team.%d = %r" % (second, board_row(second)))

    # The console readout THIS phase added. ent_info needs sv_cheats (its own
    # help says so) and is ISSUED BY THE PLAYER'S OWN CONSOLE (the picker is
    # issuer-bound and refuses from the server console with two players in;
    # it has no index reading, so name the class - the report then holds BOTH
    # players and their two different teams). The managed Log ring is where
    # managed Log.Msg lands, and read from the CURRENT head so the startup
    # flood cannot bury the answer.
    srv.cmd("sv_cheats 1")
    log_from = srv.heads().get("log", 0)     # BEFORE issuing: read from here up
    client.api.cmd("cmd ent_info ss_player")
    ring = ""
    for _ in range(10):
        ring = "".join(l["text"] for l in srv.get("log", log_from, 200).get("lines", []))
        if "team     2" in ring:
            break
        time.sleep(0.5)
    check("team     2" in ring,
          "ent_info prints `team     2` for the joined player (ring %d bytes)" % len(ring))
    if "team     2" not in ring:
        print("    ring tail: %r" % ring[-800:])

    seen = srv.wait_for(lambda: client_team() == 2, 10.0, 0.25)
    check(bool(seen), "and the CLIENT decoded the same mirror: Team.%d = %r"
                      % (second, client_team()))

    check(board_row(first) == 0, "the control: the player who never joined still reads team %r"
                                  % board_row(first))
finally:
    if client is not None:
        client.kill()
    server.kill()

print("== %d failed" % len(failures))
sys.exit(1 if failures else 0)

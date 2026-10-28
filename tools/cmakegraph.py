"""What the CMake build compiles and links, for tools and gates that read it.

Phase 6b replaced VPC with CMake. A tool that used to read
game/{server,client}/*_csharp.vpc or VPC's ninja graph
(_vpc_/ninja/sdk_csharp_<cfg>.ninja, objects under _vpc_/ninja/<cfg>/objs/)
reads the CMake build of farm/<cfg> here instead:

  * which sources a target compiles -- compile_commands.json, cross-checked
    against the target's hand-owned CMakeLists source list;
  * the objects on a target's link edge, and what it links -- build.ninja.
    Never an `ls` of the object directory: ninja keeps the .o of a TU whose
    edge went away, and a census over the directory counted those ghosts.

EVERY READER FAILS LOUDLY: a missing, empty, unparsable or stale input raises
GraphError. A reader that found nothing must never answer "not compiled" or
"no consumers". Stale means compile_commands.json disagrees with the
CMakeLists list (a build not re-run after a list edit).

Paths are farm-relative: game/server/player.cpp,
sourcesharp/game/server/shell_gamedll.cpp. The lists name a Valve file through
the farm (${SS_SRC}/game/...) but THIS repo's files where they live
(${CMAKE_CURRENT_SOURCE_DIR}/x.cpp, ${PROJECT_SOURCE_DIR}/src/shared/native/x.cpp),
and compile_commands.json names them there too; both are mapped back to their
farm spelling through tools/module-map.tsv (repo_to_farm), so a caller asking
"is sourcesharp/game/server/x.cpp compiled" is answered either way. A local
path no module-map row covers is a GraphError, never a guess. Objects are
absolute paths.

The C++ twin is src/test/native/cmake_graph.h, the C# one
src/test/common/CMakeGraph.cs.
"""

import json
import os
import re

SERVER = 'server_sourcesharp'   # server.so
CLIENT = 'client_sourcesharp'   # client.so
HOST = 'sourcesharp'            # libmanaged_host.so
SDK_LIBS = ('tier1', 'mathlib', 'raytrace')

# The old VPC project names, which several tools print: server/client.
SIDES = {'server': SERVER, 'client': CLIENT}

# The repo whose CMakeLists are the source lists: this file's own, unless
# SOURCESHARP_REPO says otherwise (the ctest contract sets it to the repo root;
# a tool's fixture points it at a stand-in repo).
REPO = os.environ.get('SOURCESHARP_REPO') or os.path.dirname(os.path.dirname(os.path.realpath(__file__)))


class GraphError(Exception):
    pass


def build_dir(config='debug', repo=None):
    """farm/<config>/build, or $SOURCESHARP_BUILD_DIR when it is set."""
    env = os.environ.get('SOURCESHARP_BUILD_DIR')
    if env:
        return env
    return os.path.join(repo or REPO, 'farm', config, 'build')


def farm_src(bdir):
    return os.path.join(os.path.dirname(os.path.normpath(bdir)), 'src')


def _read(path, why):
    try:
        with open(path, encoding='utf-8', errors='replace') as f:
            text = f.read()
    except OSError as e:
        raise GraphError('cannot open %s (%s): %s' % (path, why, e.strerror))
    if not text:
        raise GraphError('%s is EMPTY (%s)' % (path, why))
    return text


def list_file(target):
    """The repo-relative CMakeLists that owns a target's source list."""
    if target == SERVER:
        return 'src/server/native/CMakeLists.txt'
    if target == CLIENT:
        return 'src/client/native/CMakeLists.txt'
    if target == HOST:
        return 'src/host/native/CMakeLists.txt'
    if target in SDK_LIBS:
        return 'cmake/sdk/%s.cmake' % target
    raise GraphError("no CMakeLists is known for target '%s'" % target)


def list_text(target, repo=None):
    return _read(os.path.join(repo or REPO, list_file(target)), "the target's CMakeLists")


def _block(text, opener, where):
    at = text.find(opener)
    if at < 0:
        raise GraphError('%s has no `%s` block' % (where, opener.strip()))
    if text.find(opener, at + 1) >= 0:
        raise GraphError('%s has more than one `%s` block' % (where, opener.strip()))
    toks = []
    closed = False
    for line in text[at + len(opener):].split('\n'):
        code = line.split('#', 1)[0]
        if ')' in code:
            code = code[:code.index(')')]
            closed = True
        toks.extend(code.split())
        if closed:
            break
    if not closed:
        raise GraphError('%s: `%s` is never closed' % (where, opener.strip()))
    if not toks:
        raise GraphError('%s: `%s` lists nothing' % (where, opener.strip()))
    return toks


def list_token(tok, target, repo=None):
    """One source-list entry, farm-relative: ${SS_SRC}/x is the farm's x;
    ${LOCAL}/x (${CMAKE_CURRENT_SOURCE_DIR}/x), ${SHARED}/x (src/shared/native)
    and ${PROJECT_SOURCE_DIR}/x are this repo's, mapped through the module map;
    a bare entry is already farm-relative (the Valve libraries' lists)."""
    here = os.path.dirname(list_file(target))
    for var, base in (('${SS_SRC}/', None),
                      ('${LOCAL}/', here),
                      ('${CMAKE_CURRENT_SOURCE_DIR}/', here),
                      ('${SHARED}/', 'src/shared/native'),
                      ('${PROJECT_SOURCE_DIR}/', '')):
        if tok.startswith(var):
            rest = tok[len(var):]
            if base is None:
                return rest
            return repo_to_farm(os.path.normpath(os.path.join(base, rest)), repo)
    if '$' in tok:
        raise GraphError('%s: cannot read the source-list entry %r' % (list_file(target), tok))
    return tok


def listed_sources(target, repo=None):
    """The target's set(<target>_sources ...) list, in order, farm-relative."""
    toks = _block(list_text(target, repo), 'set(%s_sources\n' % target, list_file(target))
    return [list_token(t, target, repo) for t in toks]


def debug_listed_sources(target, repo=None):
    """The target's OPTIONAL set(<target>_debug_sources ...) list: sources the
    CMakeLists appends only in a debug build (the diagnostic console commands,
    user ruling 2026-10-01). Empty when the target has no such block."""
    text = list_text(target, repo)
    opener = 'set(%s_debug_sources\n' % target
    if text.find(opener) < 0:
        return []
    toks = _block(text, opener, list_file(target))
    out = [list_token(t, target, repo) for t in toks]
    both = set(out) & set(listed_sources(target, repo))
    if both:
        raise GraphError('%s lists %s in both %s_sources and %s_debug_sources'
                         % (list_file(target), sorted(both)[0], target, target))
    return out


def config_of(bdir):
    """debug or release, read off farm/<config>/build."""
    return os.path.basename(os.path.dirname(os.path.abspath(bdir)))


def compile_commands(bdir):
    path = os.path.join(bdir, 'compile_commands.json')
    text = _read(path, "CMake's compile database -- run `make debug` (or release)")
    try:
        entries = json.loads(text)
    except ValueError as e:
        raise GraphError('%s is not JSON: %s' % (path, e))
    if not isinstance(entries, list) or not entries:
        raise GraphError('%s has no entries' % path)
    for e in entries:
        if not (e.get('file') and e.get('output') and (e.get('command') or e.get('arguments'))):
            raise GraphError('%s: an entry lacks file, output or command' % path)
    return entries


def target_entries(target, bdir):
    mark = '/CMakeFiles/%s.dir/' % target
    found = [e for e in compile_commands(bdir) if mark in '/' + e['output']]
    if not found:
        raise GraphError("compile_commands.json names no object of target '%s'" % target)
    return found


def module_rows(repo=None):
    """tools/module-map.tsv's source rows: (farm path, kind, repo path)."""
    path = os.path.join(repo or REPO, 'tools', 'module-map.tsv')
    rows = []
    for line in _read(path, 'the module map').split('\n'):
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        parts = line.split('\t')
        if len(parts) != 3 or parts[1] not in ('dir', 'files', 'file'):
            raise GraphError('%s: bad row %r' % (path, line))
        if '{cfg}' not in parts[2]:
            rows.append(tuple(parts))
    if not rows:
        raise GraphError('%s has no rows' % path)
    return rows


def repo_to_farm(repo_rel, repo=None):
    """The farm-relative spelling of a file of this repo: the most specific
    module-map row that covers it (as tools/setup links it). A vendored Valve
    file (vendor/<side>/<path>) is named by its Valve path, as the farm named it."""
    parts = repo_rel.split('/', 2)
    if len(parts) == 3 and parts[0] == 'vendor' and parts[1] in ('shared', 'server', 'client'):
        return parts[2]
    best = None
    for farm, kind, rel in module_rows(repo):
        if kind == 'file':
            hit = repo_rel == rel
            rest = ''
        else:
            hit = repo_rel.startswith(rel + '/')
            rest = repo_rel[len(rel) + 1:]
        if hit and (best is None or len(rel) > len(best[0])):
            best = (rel, farm + ('/' + rest if rest else ''))
    if best is None or not best[1].startswith('src/'):
        raise GraphError('%s is a file of this repo that no tools/module-map.tsv '
                         'row links into the farm' % repo_rel)
    return best[1][len('src/'):]


def farm_relative(path, bdir):
    prefix = farm_src(bdir) + '/'
    p = os.path.normpath(path)
    if p.startswith(prefix):
        return p[len(prefix):]
    repo = REPO
    if p.startswith(repo + '/') and not p.startswith(repo + '/farm/'):
        return repo_to_farm(p[len(repo) + 1:], repo)
    raise GraphError("%s is neither under the farm's src/ (%s) nor a file of "
                     'this repo' % (path, prefix))


def compiled_sources(target, bdir, repo=None):
    """THE ANSWER TO "IS X COMPILED INTO T": {farm-relative source}, equal to
    the CMakeLists list or a GraphError."""
    compiled = {farm_relative(e['file'], bdir) for e in target_entries(target, bdir)}
    listed = set(listed_sources(target, repo))
    if config_of(bdir) == 'debug':
        listed |= set(debug_listed_sources(target, repo))
    if listed != compiled:
        lines = ['  listed, not compiled: ' + s for s in sorted(listed - compiled)[:10]]
        lines += ['  compiled, not listed: ' + s for s in sorted(compiled - listed)[:10]]
        raise GraphError("compile_commands.json disagrees with %s about '%s' -- a stale "
                         'build (re-run make), or a list the parser misread:\n%s'
                         % (list_file(target), target, '\n'.join(lines)))
    return compiled


def compile_command(target, source, bdir):
    for e in target_entries(target, bdir):
        if farm_relative(e['file'], bdir) == source:
            return e.get('command') or ' '.join(e['arguments'])
    raise GraphError("%s is not compiled into '%s'" % (source, target))


def ninja_tokens(line):
    """A ninja token list with `$ `, `$:` and `$$` unescaped."""
    out, cur, have = [], [], False
    i = 0
    while i < len(line):
        c = line[i]
        if c == '$' and i + 1 < len(line):
            cur.append(line[i + 1])
            have = True
            i += 2
            continue
        if c in ' \t':
            if have:
                out.append(''.join(cur))
            cur, have = [], False
        else:
            cur.append(c)
            have = True
        i += 1
    if have:
        out.append(''.join(cur))
    return out


def link_edge(target, bdir):
    """{'output', 'objects' (absolute), 'vars'} of the target's link edge."""
    path = os.path.join(bdir, 'build.ninja')
    lines = _read(path, "CMake's ninja file -- run `make debug` (or release)").replace('$\n', '').split('\n')
    rule_re = re.compile(r'_LIBRARY_LINKER__%s_[A-Za-z0-9]+$' % re.escape(target))
    for i, line in enumerate(lines):
        if not line.startswith('build '):
            continue
        toks = ninja_tokens(line[6:])
        k = next((j for j, t in enumerate(toks) if t.endswith(':')), None)
        if k is None or k + 1 >= len(toks) or not rule_re.search(toks[k + 1]):
            continue
        objects = []
        for t in toks[k + 2:]:
            if t in ('|', '||'):
                break
            objects.append(t if os.path.isabs(t) else os.path.join(bdir, t))
        variables = {}
        for v in lines[i + 1:]:
            if not v.startswith('  '):
                break
            name, eq, value = v.partition(' = ')
            if eq:
                variables[name.strip()] = value
        if not objects:
            raise GraphError("build.ninja's link edge for '%s' names no object" % target)
        out = toks[k][:-1]
        return {'output': out if os.path.isabs(out) else os.path.join(bdir, out),
                'objects': objects, 'vars': variables}
    raise GraphError("build.ninja has no link edge for target '%s'" % target)


def link_objects(target, bdir):
    """The objects on the target's link edge (absolute), cross-checked: one per
    compiled source, each present on disk."""
    objs = link_edge(target, bdir)['objects']
    n = len(compiled_sources(target, bdir))
    if len(objs) != n:
        raise GraphError("build.ninja links %d objects into '%s' but compile_commands.json "
                         'compiles %d sources' % (len(objs), target, n))
    missing = [o for o in objs if not os.path.isfile(o)]
    if missing:
        raise GraphError("%d object(s) on '%s''s link edge are not built, e.g. %s -- build first"
                         % (len(missing), target, missing[0]))
    return objs


def object_source(obj, target, bdir):
    """The farm-relative source an object of `target` was compiled from."""
    for e in target_entries(target, bdir):
        o = e['output'] if os.path.isabs(e['output']) else os.path.join(bdir, e['output'])
        if os.path.normpath(o) == os.path.normpath(obj):
            return farm_relative(e['file'], bdir)
    raise GraphError('%s is not an object of %s in compile_commands.json' % (obj, target))


def object_sources(target, bdir):
    """{absolute object path: farm-relative source} for every object on the
    target's link edge."""
    by_out = {}
    for e in target_entries(target, bdir):
        o = e['output'] if os.path.isabs(e['output']) else os.path.join(bdir, e['output'])
        by_out[os.path.normpath(o)] = farm_relative(e['file'], bdir)
    out = {}
    for obj in link_objects(target, bdir):
        src = by_out.get(os.path.normpath(obj))
        if src is None:
            raise GraphError('%s is on the link edge of %s but compile_commands.json '
                             'does not compile it' % (obj, target))
        out[obj] = src
    return out


def link_libraries(target, bdir):
    libs = link_edge(target, bdir)['vars'].get('LINK_LIBRARIES', '').strip()
    if not libs:
        raise GraphError("build.ninja's link edge for '%s' has no LINK_LIBRARIES" % target)
    return ninja_tokens(libs)


def linked_so(target, bdir):
    """The built shared object of a game target (build-dir output)."""
    out = link_edge(target, bdir)['output']
    if not os.path.isfile(out):
        raise GraphError('%s is not built -- build first' % out)
    return out


def deployed_so(target, bdir):
    """Where the build deploys the target's .so: cmake/deploy.cmake's DEST_DIR,
    read out of the link edge's POST_BUILD command."""
    edge = link_edge(target, bdir)
    m = re.search(r'-DDEST_DIR=(\S+)', edge['vars'].get('POST_BUILD', ''))
    if not m:
        raise GraphError("the link edge of '%s' has no deploy step (POST_BUILD -DDEST_DIR)" % target)
    return os.path.join(m.group(1), os.path.basename(edge['output']))


def main(argv):
    """cmakegraph.py {objects|sources|output|deployed|libs} TARGET [--build-dir D | --config C]

    objects   "<absolute object> <farm-relative source>" per object on the link edge
    sources   the farm-relative sources compiled into TARGET, sorted
    output    the linked .so/.a in the build dir
    deployed  where the .so is deployed
    libs      LINK_LIBRARIES, one token per line
    TARGET may be server/client (the old VPC side names) or a CMake target.
    Exit 2, naming the problem, when anything cannot be read.
    """
    import argparse
    ap = argparse.ArgumentParser(prog='cmakegraph.py', description=main.__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('what', choices=('objects', 'sources', 'output', 'deployed', 'libs'))
    ap.add_argument('target')
    ap.add_argument('--build-dir')
    ap.add_argument('--config', default=os.environ.get('SOURCESHARP_CONFIG') or 'debug')
    a = ap.parse_args(argv)
    target = SIDES.get(a.target, a.target)
    bdir = os.path.abspath(a.build_dir) if a.build_dir else build_dir(a.config)
    try:
        if a.what == 'objects':
            for obj, src in object_sources(target, bdir).items():
                print('%s %s' % (obj, src))
        elif a.what == 'sources':
            for src in sorted(compiled_sources(target, bdir)):
                print(src)
        elif a.what == 'output':
            print(link_edge(target, bdir)['output'])
        elif a.what == 'deployed':
            print(deployed_so(target, bdir))
        else:
            for t in link_libraries(target, bdir):
                print(t)
    except GraphError as e:
        print('cmakegraph: %s' % e, file=sys.stderr)
        return 2
    return 0


if __name__ == '__main__':
    import sys
    sys.exit(main(sys.argv[1:]))

"""Verbatim Valve-text detection, shared by tools/move-classify and
tools/valve-text-audit.

A file "contains Valve text" when it has a run of at least RUN consecutive
non-trivial lines that also appear, consecutively and in the same order, in a
file of Valve's upstream tree.

Why 12 non-trivial lines:
  - Trivial lines (blank, a lone brace, `#endif`, `return;`, a comment rule
    such as `//-----`) are dropped first, so a run is 12 lines that each carry
    content. Shared idiom (an include block, a DECLARE_* pair, a copyright
    banner of 3 content lines) does not reach 12.
  - A relocated or split Valve function body reaches 12 almost at once; the
    smallest relocated file measured (randoverride.cpp) matches for 40+.
  - Below 12 but at least BORDERLINE lines is reported as borderline, for a
    ruling by a person rather than by the threshold.

Lines are compared after stripping leading and trailing whitespace and
collapsing internal whitespace, so re-indentation does not hide a copy.

Two views are measured and the longer run wins: every content line, and code
lines only (`//` comments and comment-only lines removed on both sides). The
second exists because a copy whose comments were dropped or rewritten breaks
the first view's run every few lines -- a 67-line verbatim copy of
CGameRules::ShouldCollide measured 7 in it.
"""

import re
import subprocess

RUN = 12
BORDERLINE = 6

_TRIVIAL_WORDS = {
    "", "else", "endif", "return", "break", "continue", "public", "private",
    "protected", "default", "do", "try", "finally", "pragma once", "endregion",
    "region", "nullptr", "null", "true", "false", "0", "1",
}
_WS = re.compile(r"\s+")
_NONWORD = re.compile(r"[^A-Za-z0-9_ ]+")


def normalise(line):
    return _WS.sub(" ", line.strip())


def is_trivial(norm):
    """A line with no content of its own: punctuation, a bare keyword."""
    words = _NONWORD.sub(" ", norm).strip()
    words = _WS.sub(" ", words).lower()
    if words in _TRIVIAL_WORDS:
        return True
    # separator rules and short fragments: "//-----", "*/", "};", "x;"
    return len(words.replace(" ", "")) < 3


def content_lines(text):
    """The non-trivial, normalised lines of a text, in order."""
    out = []
    for raw in text.splitlines():
        n = normalise(raw)
        if not is_trivial(n):
            out.append(n)
    return out


_LINE_COMMENT = re.compile(r"\s//.*$|^//.*$")


def code_lines(text):
    """content_lines with comments removed: `//` tails, comment-only lines,
    and the `*` / `/*` lines of block comments."""
    out = []
    for raw in text.splitlines():
        n = normalise(raw)
        if n.startswith(("/*", "*", "#region", "#endregion")):
            continue
        n = _LINE_COMMENT.sub("", n).strip()
        if not is_trivial(n):
            out.append(n)
    return out


def views(text):
    return content_lines(text), code_lines(text)


def is_text(data):
    return b"\0" not in data[:65536]


def grams(lines, k):
    for i in range(len(lines) - k + 1):
        yield i, hash(tuple(lines[i:i + k]))


class ValveIndex:
    """Every RUN-gram and BORDERLINE-gram of content lines in a Valve tree."""

    def __init__(self):
        self.big = {}      # RUN-gram hash -> index into self.paths
        self.small = set()  # BORDERLINE-gram hash
        self.paths = []

    def add(self, path, data):
        if not is_text(data):
            return
        text = data.decode("utf-8", "replace")
        idx = len(self.paths)
        self.paths.append(path)
        for lines in views(text):
            for _, h in grams(lines, RUN):
                self.big.setdefault(h, idx)
            for _, h in grams(lines, BORDERLINE):
                self.small.add(h)

    @classmethod
    def from_git(cls, repo, rev):
        """Index every blob of `rev` in `repo`."""
        index = cls()
        for path, data in iter_blobs(repo, rev):
            index.add(path, data)
        return index

    def measure(self, data):
        """(longest verbatim run, matching Valve path or None, borderline run).

        The longest run is measured in content lines covered by consecutive
        matching RUN-grams, so it is >= RUN when any match exists and 0
        otherwise. The borderline run is the same over BORDERLINE-grams.
        """
        if not is_text(data):
            return 0, None, 0
        best, path, small = 0, None, 0
        for lines in views(data.decode("utf-8", "replace")):
            run, _ = _longest(lines, RUN, lambda h: h in self.big)
            if run > best:
                best = run
                for _, h in grams(lines, RUN):
                    if h in self.big:
                        path = self.paths[self.big[h]]
                        break
            s, _ = _longest(lines, BORDERLINE, lambda h: h in self.small)
            small = max(small, s)
        return best, path, small


def _longest(lines, k, hit):
    best = 0
    run_start = None
    last = None
    for i, h in grams(lines, k):
        if hit(h):
            if last is not None and i == last + 1:
                pass
            else:
                run_start = i
            last = i
            best = max(best, i - run_start + k)
    return best, run_start


def iter_blobs(repo, rev, paths=None):
    """(path, bytes) for every blob of `rev`, optionally limited to `paths`."""
    ls = subprocess.run(
        ["git", "-C", repo, "ls-tree", "-r", "-z", rev],
        check=True, capture_output=True).stdout
    entries = []
    for rec in ls.split(b"\0"):
        if not rec:
            continue
        meta, path = rec.split(b"\t", 1)
        mode, kind, sha = meta.split()
        if kind != b"blob":
            continue
        p = path.decode()
        if paths is not None and p not in paths:
            continue
        entries.append((p, sha.decode()))
    proc = subprocess.Popen(
        ["git", "-C", repo, "cat-file", "--batch"],
        stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    try:
        for p, sha in entries:
            proc.stdin.write(sha.encode() + b"\n")
            proc.stdin.flush()
            header = proc.stdout.readline().split()
            size = int(header[2])
            data = proc.stdout.read(size)
            proc.stdout.read(1)
            yield p, data
    finally:
        proc.stdin.close()
        proc.wait()


def blob_shas(repo, rev):
    """path -> blob sha for every blob of `rev`."""
    ls = subprocess.run(
        ["git", "-C", repo, "ls-tree", "-r", "-z", rev],
        check=True, capture_output=True).stdout
    out = {}
    for rec in ls.split(b"\0"):
        if not rec:
            continue
        meta, path = rec.split(b"\t", 1)
        mode, kind, sha = meta.split()
        if kind == b"blob":
            out[path.decode()] = sha.decode()
    return out

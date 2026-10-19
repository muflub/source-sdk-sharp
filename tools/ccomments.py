#!/usr/bin/env python3
"""C/C++ comments, found by a lexer rather than a regex.

One stripper for two jobs that must agree on what a comment is:

  - tools/entity-census counts a stock TU's CODE lines (lines with any
    character outside a comment), so the count is the same whether ROOT is
    the fork, whose files carry our comments, or the patched upstream
    (make sdk-upstream), whose series has none.
  - patches/source-sdk-2013 is regenerated without the comments the fork
    adds (--strip-added), so the series adds no comment text.

    tools/ccomments.py --count-code FILE...    "<code lines>\t<path>" per file
    tools/ccomments.py --strip-all FILE        the file with every comment out
    tools/ccomments.py --strip-added OLD NEW   NEW with comments removed from the
                                               lines it adds over OLD (stdout)
    tools/ccomments.py --self-test

The lexer knows string and character literals ("//" in a path is code), line
comments continued by a trailing backslash, and block comments over several
lines. Raw string literals are not handled: none is in the SDK files counted.
Files are read as latin-1, so any byte sequence round-trips.
"""

import difflib
import re
import sys


def comment_mask(text):
    """One bool per character of TEXT: True where it is inside a comment.
    Newlines inside a block comment are not marked, so line structure stays."""
    m = [False] * len(text)
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '"' or c == "'":
            i += 1
            while i < n and text[i] != c and text[i] != "\n":
                i += 2 if text[i] == "\\" else 1
            i += 1
            continue
        if text.startswith("//", i):
            j = i
            while j < n and text[j] != "\n":
                if text[j] == "\\" and j + 1 < n and text[j + 1] == "\n":
                    j += 2
                    continue
                j += 1
            for k in range(i, j):
                if text[k] != "\n":
                    m[k] = True
            i = j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            for k in range(i, j):
                if text[k] != "\n":
                    m[k] = True
            i = j
            continue
        i += 1
    return m


def strip_all(text):
    """TEXT with each comment replaced by one space; newlines kept."""
    m = comment_mask(text)
    out, prev = [], False
    for ch, f in zip(text, m):
        if f:
            if not prev:
                out.append(" ")
        else:
            out.append(ch)
        prev = f
    return "".join(out)


def code_lines(text):
    """Lines holding at least one non-blank character outside a comment."""
    n = 0
    for line in strip_all(text).split("\n"):
        if line.strip():
            n += 1
    return n


def join_runs(body, lm):
    """BODY (one line, no newline) with its comment runs removed, tidying the
    whitespace each leaves: a run at the indentation keeps the indentation, one
    before , ; or the line end takes its leading blank with it, and one between
    two tokens leaves a single space."""
    out, i, n = "", 0, len(body)
    while i < n:
        if not lm[i]:
            out += body[i]
            i += 1
            continue
        j = i
        while j < n and lm[j]:
            j += 1
        k = j
        while k < n and body[k] in " \t" and not lm[k]:
            k += 1
        left = out.rstrip(" \t")
        right = body[k] if k < n else ""
        if left.strip() == "":
            pass
        elif right == "" or right in ",;":
            out = left
        else:
            idl = left[-1].isalnum() or left[-1] == "_"
            idr = right.isalnum() or right == "_"
            out = left + " " if (out != left or k > j or (idl and idr)) else left
        i = k
    return out.rstrip()


def strip_added(old, new, added=None):
    """NEW with the comments removed from the lines it adds over OLD. A line
    whose only change is whitespace goes back to OLD's text. ADDED, when given,
    is the set of 0-based NEW line numbers to treat as added (git's own diff
    alignment); otherwise difflib decides."""
    ol = old.splitlines(keepends=True)
    nl = new.splitlines(keepends=True)
    m = comment_mask(new)
    restore = {}
    if added is None:
        added = set()
        sm = difflib.SequenceMatcher(None, ol, nl, autojunk=False)
        for tag, i1, i2, j1, j2 in sm.get_opcodes():
            if tag not in ("replace", "insert"):
                continue
            rem = {}
            for line in ol[i1:i2]:
                rem.setdefault(re.sub(r"\s+", "", line), line)
            for j in range(j1, j2):
                key = re.sub(r"\s+", "", nl[j])
                if key and key in rem:
                    restore[j] = rem[key]
                else:
                    added.add(j)
    out, off = [], 0
    for j, line in enumerate(nl):
        lm = m[off:off + len(line)]
        off += len(line)
        if j in restore:
            r = restore[j]
            if line.endswith("\n") and not r.endswith("\n"):
                r += "\n"
            out.append(r)
            continue
        if j not in added or not any(lm):
            out.append(line)
            continue
        body = line.rstrip("\n")
        code = join_runs(body, lm)
        if code.strip():
            out.append(code + line[len(body):])
    return "".join(out)


def read(path):
    with open(path, encoding="latin-1") as f:
        return f.read()


def self_test():
    bad = 0

    def check(name, got, want):
        nonlocal bad
        ok = got == want
        bad += not ok
        print(f"{'ok ' if ok else 'BAD'} {name}" + ("" if ok else
                                                    f": got {got!r}, want {want!r}"))

    check("a comment-only line is not code", code_lines("// x\nint a;\n"), 1)
    check("blank lines are not code", code_lines("\n  \nint a;\n\t\n"), 1)
    check("code with a trailing comment is code",
          code_lines("int a; // x\n"), 1)
    check("a block comment over three lines is none", code_lines(
        "/* a\n b\n c */\nint a;\n"), 1)
    check("code after a block comment's close is code",
          code_lines("/* a\n */ int b;\n"), 1)
    check("a // inside a string is code, and so is its line",
          code_lines('const char *p = "http://x"; // c\n'), 1)
    check("a quote inside a comment opens no string",
          code_lines("// don't\nint a;\n// it's\n"), 1)
    check("a line comment continued by a backslash covers the next line",
          code_lines("// a \\\nstill comment\nint a;\n"), 1)
    check("a /* inside a char literal is code", code_lines("c = '/'; d = '*';\n"),
          1)
    check("the count is the same with or without comments",
          code_lines("// head\nint a; // t\n/* b */\nint b;\n"),
          code_lines("int a;\nint b;\n"))
    check("strip_added drops a whole added comment line",
          strip_added("int a;\n", "// new\nint a;\n"), "int a;\n")
    check("strip_added keeps the code of an added line, not its comment",
          strip_added("int a;\n", "int a;\nint b; // new\n"), "int a;\nint b;\n")
    check("strip_added keeps an existing (unchanged) comment",
          strip_added("// valve\nint a;\n", "// valve\nint a;\nint b;\n"),
          "// valve\nint a;\nint b;\n")
    check("strip_added removes an inline block comment between tokens",
          strip_added("f( 0 );\n", "f( 0 /*x*/, 1 );\n"), "f( 0, 1 );\n")
    check("strip_added restores a whitespace-only change to the old text",
          strip_added("// Purpose: \nint a;\n", "// Purpose:\nint a;\n"),
          "// Purpose: \nint a;\n")
    print("self-test", "FAILED" if bad else "passed")
    return 1 if bad else 0


def main(argv):
    if argv[:1] == ["--self-test"]:
        return self_test()
    if argv[:1] == ["--count-code"]:
        for p in argv[1:]:
            print(f"{code_lines(read(p))}\t{p}")
        return 0
    if argv[:1] == ["--strip-all"] and len(argv) == 2:
        sys.stdout.buffer.write(strip_all(read(argv[1])).encode("latin-1"))
        return 0
    if argv[:1] == ["--strip-added"] and len(argv) == 3:
        sys.stdout.buffer.write(
            strip_added(read(argv[1]), read(argv[2])).encode("latin-1"))
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

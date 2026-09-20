"""Static checks for FeatureScript files, before pushing them to Onshape.

Every round trip to Onshape to discover a typo is slow. This catches the classes of
mistake that have actually reached Onshape from this repo:

  - calls to functions that exist in neither the given files nor std/
    (typically a function deleted by an edit while its call sites remain)
  - calls with the wrong number of arguments
  - two definitions with the same name AND arity, which Onshape rejects with
    "Multiple visible overloads with identical signature"
  - dot access on a reserved word -- `m.type` parses, `m["type"]` is required
  - unbalanced braces/parens, non-ASCII bytes, a UTF-8 BOM

It resolves names against the local std/ mirror, so keep that in sync.

What it does NOT do: type checking, unit checking, enum reachability, or anything
about runtime behaviour. A clean result means "worth pushing", not "correct".

Usage:
    python fscheck.py driven_offset/*.fs
    python fscheck.py path/to/one.fs path/to/two.fs

Pass every file of a document together -- a function defined in one tab and called
in another is only resolvable when both are given. Exits non-zero if anything is
found, so it can gate a push.
"""
import io
import os
import re
import sys

# Relative to this script, not the working directory, so it runs from anywhere.
STD_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "std")

KEYWORDS = {
    "if", "else", "for", "while", "return", "function", "const", "var", "is", "as",
    "true", "false", "undefined", "import", "export", "precondition", "annotation",
    "enum", "predicate", "throw", "try", "catch", "silent", "break", "continue",
    "type", "typecheck", "map", "array", "string", "number", "boolean", "builtin",
    "defineFeature", "definePredicate", "print", "in", "new", "this", "switch",
    "case", "default", "do", "loopBody",
}

# Words the parser will not accept after a "." -- `m.type` is a parse error and
# `m["type"]` is required.
#
# These are language KEYWORDS only. Do not add std function or type names here:
# `box`, `line`, `plane` and `transform` cannot be used as VARIABLE names, which is a
# different rule, but they are perfectly legal as map fields read with dot access --
# `res.transform` ships and works in curveMapping's linear fast path. Listing them
# produced false positives that cost two separate code reviews.
RESERVED_DOT = {
    "type", "function", "const", "var", "is", "as", "if", "else", "for", "while",
    "return", "map", "array", "string", "number", "boolean", "import", "export",
    "enum", "break", "continue", "throw", "try", "catch", "in", "precondition",
}


def std_symbols():
    """Every exported name in the local std/ mirror."""
    names = set()
    if not os.path.isdir(STD_DIR):
        return names
    pattern = re.compile(
        r"^export\s+(?:function|const|enum|predicate|type|import)\s+(\w+)", re.M)
    for entry in os.listdir(STD_DIR):
        if not entry.endswith(".fs"):
            continue
        text = io.open(os.path.join(STD_DIR, entry), encoding="utf-8",
                       errors="replace").read()
        names.update(pattern.findall(text))
        # enum cases are referenced as EnumName.CASE, not as calls, so skip them
    return names


def strip_noise(src):
    src = re.sub(r"/\*.*?\*/", "", src, flags=re.S)
    src = re.sub(r"//[^\n]*", "", src)
    return re.sub(r'"[^"\n]*"', '""', src)


DEF_PATTERN = re.compile(
    r"^(?:export\s+)?(?:function|predicate)\s+(\w+)\(((?:[^()]|\([^()]*\))*?)\)\s*(?:returns|precondition|\{|\n)",
    re.M | re.S)


def duplicate_defs(src):
    """Two definitions with the same name AND arity are a hard FS error:
    'Multiple visible overloads with identical signature'. Distinct arities are
    legal overloads, so only exact repeats count."""
    seen = {}
    for m in DEF_PATTERN.finditer(src):
        arity = len([p for p in m.group(2).split(",") if p.strip()])
        line = src[:m.start()].count("\n") + 1
        seen.setdefault((m.group(1), arity), []).append(line)
    return {k: v for k, v in seen.items() if len(v) > 1}


def definitions(src):
    out = {}
    pattern = re.compile(
        r"^(?:export\s+)?(?:function|predicate)\s+(\w+)\(((?:[^()]|\([^()]*\))*?)\)\s*(?:returns|precondition|\{|\n)",
        re.M | re.S)
    for m in pattern.finditer(src):
        params = [p for p in m.group(2).split(",") if p.strip()]
        out.setdefault(m.group(1), set()).add(len(params))
    return out


def count_args(body, open_index):
    i, depth, args, empty = open_index, 1, 1, True
    while i < len(body) and depth:
        c = body[i]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        elif c == "," and depth == 1:
            args += 1
        if depth and not c.isspace():
            empty = False
        i += 1
    return 0 if empty else args


def balance(text):
    depth = {"{": 0, "(": 0, "[": 0}
    close = {"}": "{", ")": "(", "]": "["}
    in_block = False
    for line in text.split("\n"):
        i = 0
        while i < len(line):
            if in_block:
                if line[i:i + 2] == "*/":
                    in_block = False
                    i += 2
                    continue
                i += 1
                continue
            if line[i:i + 2] == "/*":
                in_block = True
                i += 2
                continue
            if line[i:i + 2] == "//":
                break
            if line[i] == '"':
                i += 1
                while i < len(line) and line[i] != '"':
                    i += 2 if line[i] == "\\" else 1
                i += 1
                continue
            if line[i] in depth:
                depth[line[i]] += 1
            elif line[i] in close:
                depth[close[line[i]]] -= 1
            i += 1
    return depth


def main(paths):
    std = std_symbols()
    sources = {p: io.open(p, encoding="utf-8").read() for p in paths}
    defined = {}
    for src in sources.values():
        for name, arities in definitions(src).items():
            defined.setdefault(name, set()).update(arities)

    problems = 0
    for path, src in sources.items():
        raw = io.open(path, "rb").read()
        text = raw.decode("utf-8")
        body = strip_noise(src)

        for m in re.finditer(r"\b(\w+)\s*\(", body):
            name = m.group(1)
            line = body[:m.start()].count("\n") + 1
            if name in KEYWORDS:
                continue
            if name in defined:
                args = count_args(body, m.end())
                if args not in defined[name]:
                    print("%s:%d  ARITY %s() called with %d, defined for %s"
                          % (path, line, name, args, sorted(defined[name])))
                    problems += 1
            elif name not in std:
                print("%s:%d  UNDEFINED %s() is neither local nor in std/"
                      % (path, line, name))
                problems += 1

        for m in re.finditer(r"\.(\w+)", text):
            if m.group(1) in RESERVED_DOT:
                line = text[:m.start()].count("\n") + 1
                print("%s:%d  RESERVED dot access .%s" % (path, line, m.group(1)))
                problems += 1

        for (name, arity), lines in sorted(duplicate_defs(src).items()):
            print("%s  DUPLICATE %s() with %d arg(s) defined at lines %s"
                  % (path, name, arity, lines))
            problems += 1

        bad = balance(text)
        if any(bad.values()):
            print("%s  UNBALANCED %s" % (path, bad))
            problems += 1
        if raw[:3] == b"\xef\xbb\xbf":
            print("%s  BOM at file start" % path)
            problems += 1
        non_ascii = sum(1 for b in raw if b > 127)
        if non_ascii:
            print("%s  %d non-ASCII byte(s)" % (path, non_ascii))
            problems += 1

        print("%s: %d lines" % (path, len(text.splitlines())))

    print("\n%s" % ("clean" if problems == 0 else "%d problem(s)" % problems))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

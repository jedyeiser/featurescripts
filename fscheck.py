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


# ---------------------------------------------------------------------------
# println lint. User rule (2026-11-08): a feature never emits println unless the
# user switched on a Debug/print option. Purely lexical; see println_check().
# ---------------------------------------------------------------------------
PRINTLN_SKIP_PATH_RES = [      # matched against the '/'-normalised path
    r"_tests\.fs$", r"_spike\.fs$", r"_probe\.fs$",
    r"(^|/)std/", r"(^|/)archive/", r"(^|/)example_1/", r"(^|/)legacy",
]
GUARD_RE = re.compile(r"debug|print|verbose|trace|dbg|diag|log", re.I)
ALLOW_RE = re.compile(r"fscheck:\s*allow-println")
FUNC_RE = re.compile(
    r"\bfunction\s+(\w+)\s*\(|\b(?:const|var)\s+(\w+)\s*=\s*function\s*\(")


def println_skipped(path):
    norm = path.replace("\\", "/")
    return any(re.search(r, norm) for r in PRINTLN_SKIP_PATH_RES)


def blank_noise(src):
    """Comments and string contents -> spaces; length and newlines preserved, so
    offsets and line numbers match the original."""
    out, i, n = [], 0, len(src)
    while i < n:
        two = src[i:i + 2]
        if two == "//":
            j = src.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i)); i = j
        elif two == "/*":
            j = src.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append("".join(c if c == "\n" else " " for c in src[i:j])); i = j
        elif src[i] == '"':
            j = i + 1
            while j < n and src[j] != '"' and src[j] != "\n":
                j += 2 if src[j] == "\\" else 1
            j = min(j + 1, n)
            out.append('"' + " " * (j - i - 2) + '"' if j - i >= 2 else " " * (j - i)); i = j
        else:
            out.append(src[i]); i += 1
    return "".join(out)


class PrintlnFile(object):
    def __init__(self, path, raw):
        self.path, self.raw = path, raw
        self.lines = raw.split("\n")
        self.t = blank_noise(raw)
        self.match, self.rmatch, stack = {}, {}, []
        for i, c in enumerate(self.t):
            if c == "{":
                stack.append(i)
            elif c == "}" and stack:
                o = stack.pop(); self.match[o] = i; self.rmatch[i] = o
        self.funcs = []     # (name, body_open, body_close)
        for m in FUNC_RE.finditer(self.t):
            k = self.t.find("(", m.end() - 1)
            depth, j = 0, k
            while j < len(self.t):
                if self.t[j] == "(": depth += 1
                elif self.t[j] == ")":
                    depth -= 1
                    if depth == 0: break
                j += 1
            o = self.t.find("{", j)
            if o >= 0 and o in self.match:
                self.funcs.append((m.group(1) or m.group(2), o, self.match[o]))

    def line(self, pos):
        return self.t.count("\n", 0, pos) + 1

    def header(self, o):
        k = o
        while k > 0 and self.t[k - 1] not in ";{}":
            k -= 1
        return self.t[k:o].strip()

    def enclosing_func(self, pos):
        best = None
        for f in self.funcs:
            if f[1] < pos < f[2] and (best is None or f[1] > best[1]):
                best = f
        return best

    def block_guarded(self, o, depth=0):
        h = self.header(o)
        if re.match(r"(else\s+)?if\b", h):
            return bool(GUARD_RE.search(h))
        if h == "else" and depth < 8:           # lenient: else of a guard if
            k = o
            while k > 0 and self.t[k - 1].isspace():
                k -= 1
            if k > 0 and self.t[k - 1] == "}" and (k - 1) in self.rmatch:
                return self.block_guarded(self.rmatch[k - 1], depth + 1)
        return False

    def guarded(self, pos):
        """True if pos is gated lexically (excluding the enclosing-function-name rule)."""
        t = self.t
        k = pos
        while k > 0 and t[k - 1] not in ";{}":
            k -= 1
        pre = t[k:pos]                          # brace-less if / ternary / &&
        if GUARD_RE.search(pre) and (re.match(r"\s*(else\s+)?if\b", pre)
                                     or "?" in pre or "&&" in pre):
            return True
        for o, c in self.match.items():
            if o < pos < c:
                if self.block_guarded(o):
                    return True
                # early `if (!dbg) { return; }` earlier in this block
                for m in re.finditer(r"\bif\s*\(([^{};]*)\)\s*\{?\s*return\b", t[o:pos]):
                    if GUARD_RE.search(m.group(1)):
                        return True
        return False


def println_check(paths, sources):
    """Returns (problems, messages). Each println( must be gated by a condition
    naming debug/print/verbose/trace/dbg/diag/log (if-block, brace-less if,
    ternary, early return) or sit in a function whose name matches. Helper
    functions named that way are reported at low severity (not counted) unless a
    caller (in the given files) is itself ungated."""
    files = {p: PrintlnFile(p, sources[p]) for p in paths if not println_skipped(p)}
    problems, msgs, helpers = 0, [], {}
    for p, f in sorted(files.items()):
        for m in re.finditer(r"\bprintln\s*\(", f.t):
            ln = f.line(m.start())
            if any(ALLOW_RE.search(f.lines[i]) for i in (ln - 1, ln - 2) if 0 <= i < len(f.lines)):
                continue
            fn = f.enclosing_func(m.start())
            if f.guarded(m.start()):
                continue                    # gated by its own if / ternary: fine in any function
            if fn and GUARD_RE.search(fn[0]):
                helpers.setdefault(fn[0], []).append((p, ln))
            else:
                msgs.append("%s:%d  [println] unguarded println" % (p, ln)); problems += 1
    for name, sites in sorted(helpers.items()):
        bad = []
        for p, f in files.items():
            for m in re.finditer(r"\b%s\s*\(" % re.escape(name), f.t):
                if re.search(r"function\s+$", f.t[max(0, m.start() - 20):m.start()]) \
                        or re.search(r"=\s*function\s*$", f.t[max(0, m.start() - 20):m.start()]):
                    continue
                fn = f.enclosing_func(m.start())
                if (fn and GUARD_RE.search(fn[0])) or f.guarded(m.start()):
                    continue
                if f.line(m.start()) and any(ALLOW_RE.search(f.lines[i]) for i in
                                             (f.line(m.start()) - 1, f.line(m.start()) - 2)
                                             if 0 <= i < len(f.lines)):
                    continue
                bad.append((p, f.line(m.start())))
        for p, ln in sites[:1]:
            if bad:
                msgs.append("%s:%d  [println] unguarded println via helper %s(); ungated caller(s): %s"
                            % (p, ln, name, ", ".join("%s:%d" % b for b in bad[:5])))
                problems += 1
            else:
                msgs.append("%s:%d  [println] (low) helper prints; callers must be gated: %s()"
                            % (p, ln, name))
    return problems, msgs


def main(paths):
    do_println, only_println = True, False
    if "--no-println" in paths:
        do_println = False
    if "--println-only" in paths:
        only_println = True
    paths = [p for p in paths if not p.startswith("--")]
    if only_println:
        sources = {p: io.open(p, encoding="utf-8", errors="replace").read() for p in paths}
        n, msgs = println_check(paths, sources)
        for m in msgs:
            print(m)
        print("\n%s" % ("clean" if n == 0 else "%d problem(s)" % n))
        return 1 if n else 0
    std = std_symbols()
    sources = {p: io.open(p, encoding="utf-8").read() for p in paths}
    defined = {}
    for src in sources.values():
        for name, arities in definitions(src).items():
            defined.setdefault(name, set()).update(arities)

    problems = 0
    if do_println:
        n, msgs = println_check(paths, sources)
        for m in msgs:
            print(m)
        problems += n
    for path, src in sources.items():
        raw =io.open(path, "rb").read()
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

"""Prepare the curveMapping_public tabs from the engine tabs (curveMapping/): deform, wrapCurve, wrapAndLoft.

  PYTHONPATH=. python devtools/onshape/port_public.py                       # dry run: report what would change
  PYTHONPATH=. python devtools/onshape/port_public.py --write               # write the three public files locally (no Onshape calls)
  PYTHONPATH=. python devtools/onshape/port_public.py --write --version <private doc version id> --core-micro <core microversion at that version> --canonical-micro <canonical_edges microversion at that version>
                                                                            # also point the public core imports at that version

What it does
  deform.fs     the engine deform.fs with the public header: the core import by the private document's VERSION
                (path "<doc>/<version>/<core element>"), the IconNamespace import, and the Icon in the feature annotation.
  wrapCurve.fs / wrapAndLoft.fs
                the public files have their own (simpler) dialogs, so they are NOT regenerated. Each functional hunk of the engine
                file since BASELINE (the commit before the closed-curve work) is transplanted by matching its surrounding lines.
                Hunks that touch imports are skipped (imports are set by --version).
Only the user creates versions: the public tabs can import the core only through a version of the private document that contains
the current core; create it first (curveMapping V29), then pass --version and --core-micro.
"""
import difflib
import re
import subprocess
import sys

BASELINE = "5703da50af7712c8c842e4c1d408464bd3caa6a5"          # engine wrap files before the closed-curve work (2026-10-20)
PRIVATE_DOC = "08e8748f2ef24eea16072b75"
CORE_ELEMENT = "683d867c35fdab9c98d47556"
CANONICAL_ELEMENT = "9e2185ec79a911e453e72cf6"        # canonical_edges: the engine deform.fs imports it (same-document microversion pin)
ASCII = {"—": "-", "→": "->", "─": "-", "ε": "eps", "×": "x", "²": "^2", "≈": "~", "κ": "kappa"}

args = sys.argv[1:]
write = "--write" in args
version = args[args.index("--version") + 1] if "--version" in args else None
core_micro = args[args.index("--core-micro") + 1] if "--core-micro" in args else None
canonical_micro = args[args.index("--canonical-micro") + 1] if "--canonical-micro" in args else None   # canonical_edges microversion at that version


def read(path):
    return open(path, encoding="utf-8", newline="").read().replace("\r\n", "\n")


def ascii_only(t):
    for k, v in ASCII.items():
        t = t.replace(k, v)
    return t


def git_show(path):
    return ascii_only(subprocess.run(["git", "show", f"{BASELINE}:{path}"], capture_output=True, encoding="utf-8").stdout.replace("\r\n", "\n"))


def core_import(path_part):
    """The public core import line."""
    if version and core_micro:
        return 'export import(path : "%s/%s/%s", version : "%s");' % (PRIVATE_DOC, version, CORE_ELEMENT, core_micro)
    return None


def find_block(lines, block):
    """Start indices at which `block` (a list of lines) occurs in `lines`."""
    n = len(block)
    return [i for i in range(len(lines) - n + 1) if lines[i:i + n] == block]


def transplant(priv_path, pub_path):
    base = git_show(priv_path).split("\n")
    cur = read(priv_path).split("\n")
    pub_lines = read(pub_path).split("\n")
    report = []
    edits = []                      # (start, end, new lines) in the ORIGINAL public lines
    sm = difflib.SequenceMatcher(None, base, cur, autojunk=False)
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag == "equal":
            continue
        old, new = base[i1:i2], cur[j1:j2]
        if any("import(path" in l for l in old + new):
            report.append(("skipped (imports)", len(old), len(new)))
            continue
        before = base[max(0, i1 - 3):i1]
        after = base[i2:i2 + 3]
        hits = find_block(pub_lines, before + old + after)
        if len(hits) != 1:
            report.append(("NOT APPLIED (found %d times)" % len(hits), len(old), len(new), (old or new)[:1]))
            continue
        start = hits[0] + len(before)
        edits.append((start, start + len(old), new))
        report.append(("applied", len(old), len(new)))
    for start, end, new in sorted(edits, key=lambda e: -e[0]):
        pub_lines[start:end] = new
    return ascii_only("\n".join(pub_lines)), report


def main():
    out = {}
    for priv, pub in (("curveMapping/wrapCurve.fs", "curveMapping_public/wrapCurve.fs"), ("curveMapping/wrapAndLoft_i.fs", "curveMapping_public/wrapAndLoft.fs")):
        text, report = transplant(priv, pub)
        print(pub)
        for r in report:
            print("   ", r)
        out[pub] = text

    # deform.fs: the engine file with the public header
    d = ascii_only(read("curveMapping/deform.fs"))
    line = core_import(None) or 'export import(path : "%s/<private version id>/%s", version : "<core microversion>");' % (PRIVATE_DOC, CORE_ELEMENT)
    d, n = re.subn(r'export import\(path : "%s", version : "[0-9a-f]*"\);' % CORE_ELEMENT, line + '\n\nIconNamespace::import(path : "c20a7510f15da01b0a06a167", version : "f014fb4b0c37e21215ee7872");', d)
    assert n == 1, "core import line not found in the engine deform.fs"
    # canonical_edges: the public deform.fs is a COPY of the engine file, so its same-document pin cannot resolve here; import it through the private document's version, like the core
    canon = ('import(path : "%s/%s/%s", version : "%s");' % (PRIVATE_DOC, version, CANONICAL_ELEMENT, canonical_micro)) if (version and canonical_micro) else \
        'import(path : "%s/<private version id>/%s", version : "<canonical_edges microversion>");' % (PRIVATE_DOC, CANONICAL_ELEMENT)
    d, n = re.subn(r'(?m)^import\(path : "%s", version : "[0-9a-f]+"\);$' % CANONICAL_ELEMENT, canon, d)
    assert n == 1, "canonical_edges import line not found in the engine deform.fs"
    old_ann = 'Wraps the body from the from edges to the to edges" }'
    assert d.count(old_ann) == 1
    d = d.replace(old_ann, 'Wraps the body from the from edges to the to edges", "Icon" : IconNamespace::BLOB_DATA }')
    d = d.replace("// Pinned to the core tab's microversion", "// Pinned to the private document's version of the core (curveMapping_public)")
    out["curveMapping_public/deform.fs"] = d

    if version and core_micro:
        for pub in ("curveMapping_public/wrapCurve.fs", "curveMapping_public/wrapAndLoft.fs"):
            t = out[pub]
            t, n = re.subn(r'export import\(path : "[0-9a-f/]*%s", version : "[0-9a-f]+"\);' % CORE_ELEMENT, core_import(None), t)
            assert n == 1, (pub, n)
            out[pub] = t
    else:
        print("(core imports of wrapCurve/wrapAndLoft left as they are until --version and --core-micro are given)")

    for path, text in out.items():
        bad = [(i + 1, l[:80]) for i, l in enumerate(text.split("\n")) if any(ord(ch) > 127 for ch in l)]
        print(path, "lines", text.count("\n") + 1, "non-ASCII lines", len(bad))
        if write:
            open(path, "w", encoding="utf-8", newline="").write(text.replace("\n", "\r\n") if "\r\n" in open(path, encoding="utf-8", newline="").read() else text)
    print("written" if write else "dry run (use --write)")


main()

"""Fetch dense wire samples (read-only eval API) into data/wires_dense.txt.
usage (repo root): FS_SYNC_TIMEOUT=240 PYTHONPATH=. python driven_offset/docs/img/src/fetch_wires.py"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fetch_topsheet import run, HERE

if __name__ == "__main__":
    got = run(open(os.path.join(HERE, "wires_dense.fs")).read())
    print(len(got), "lines")
    open(os.path.join(HERE, "data", "wires_dense.txt"), "w").write("\n".join(got) + "\n")

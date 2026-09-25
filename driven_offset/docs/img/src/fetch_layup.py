"""Fetch reference wires, Front-plane part sections and the flat topsheet outline (read-only eval API)
into data/layup.txt. usage (repo root): FS_SYNC_TIMEOUT=240 PYTHONPATH=. python driven_offset/docs/img/src/fetch_layup.py"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fetch_topsheet import run, HERE

if __name__ == "__main__":
    got = run(open(os.path.join(HERE, "layup.fs")).read())
    print(len(got), "lines")
    open(os.path.join(HERE, "data", "layup.txt"), "w").write("\n".join(got) + "\n")

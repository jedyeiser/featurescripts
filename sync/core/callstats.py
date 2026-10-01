"""Summarise the Onshape call ledger (.api-calls.jsonl, written by OnshapeClient).

usage (repo root):  python -m sync.core.callstats [--days 7] [--log PATH]
Counts the calls that count against the yearly quota (status 2xx/3xx) separately from errors and never-answered calls.
"""
import argparse
import collections
import json
import os
import time
from pathlib import Path


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--days", type=float, default=7)
    ap.add_argument("--log", default=os.environ.get("ONSHAPE_CALL_LOG") or str(Path(__file__).resolve().parents[2] / ".api-calls.jsonl"))
    args = ap.parse_args()
    if not Path(args.log).exists():
        print("no ledger yet: %s" % args.log)
        return
    since = time.time() - args.days * 86400
    rows = [json.loads(x) for x in open(args.log, encoding="utf-8") if x.strip()]
    rows = [r for r in rows if r["t"] >= since]
    counted = [r for r in rows if 200 <= r["s"] < 400]
    print("last %g days: %d logged, %d counted (2xx/3xx), %d errors, %d no response" % (
        args.days, len(rows), len(counted), sum(1 for r in rows if r["s"] >= 400), sum(1 for r in rows if r["s"] == 0)))
    for title, key in (("by caller", lambda r: r["caller"]), ("by endpoint", lambda r: r["m"] + " " + r["p"]),
                       ("by day", lambda r: time.strftime("%Y-%m-%d", time.localtime(r["t"])))):
        print("\n" + title)
        for k, n in collections.Counter(key(r) for r in counted).most_common(15):
            print("  %5d  %s" % (n, k))


if __name__ == "__main__":
    main()

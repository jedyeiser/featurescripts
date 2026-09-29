"""Shared helpers for the drawing-template scripts (devtools/onshape/templates/).

Run from the repo root with PYTHONPATH=. (ASCII only in this file).
"""
import json
import time

from sync.core.client import OnshapeClient

c = OnshapeClient()

# "Ski Drawing Templates" (K2 enterprise)
TD, TW = "52b5bde03744944bff91bef9", "81e38c962fe4d7c8250c4fc5"
# "Publish & Drawing tools" test document
PD, PW, PPS = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761", "bb2cddb24faf53d9d043c38e"


def elements(d, w):
    return c.list_elements(d, w)


def modify(d, w, eid, requests, desc, quiet=False):
    r = c.post(f"/api/v6/drawings/d/{d}/w/{w}/e/{eid}/modify",
               json_data={"description": desc, "jsonRequests": requests})
    for _ in range(90):
        s = c.get(f"/api/v6/drawings/modify/status/{r['id']}")
        if s.get("requestState") != "ACTIVE":
            if not quiet and '"Failed"' in (s.get("output") or ""):
                print("  FAILED", desc, (s.get("output") or "")[:2000])
            return s
        time.sleep(2)
    return s


def pt(x, y):
    return {"coordinate": [x, y, 0], "type": "Onshape::Reference::Point"}

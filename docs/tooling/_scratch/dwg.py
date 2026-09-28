"""Drawing API helpers for the publish_tools demo (writes only to the demo drawings in doc 73271cfc)."""
import json, time, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D, W, PS = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761", "bb2cddb24faf53d9d043c38e"


def views(eid):
    return c.get(f"/api/v8/drawings/d/{D}/w/{W}/e/{eid}/views")


def geometry(eid, vid):
    return c.get(f"/api/v8/drawings/d/{D}/w/{W}/e/{eid}/views/{vid}/jsongeometry")


def modify(eid, requests, desc):
    r = c.post(f"/api/v6/drawings/d/{D}/w/{W}/e/{eid}/modify", json_data={"description": desc, "jsonRequests": requests})
    mrid = r.get("id")
    for _ in range(60):
        s = c.get(f"/api/v6/drawings/modify/status/{mrid}")
        if s.get("requestState") != "ACTIVE":
            return s
        time.sleep(2)
    return s

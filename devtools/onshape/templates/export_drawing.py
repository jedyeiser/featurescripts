"""Export an Onshape drawing via the translation API (storeInDocument false) and download the result.

usage (repo root): PYTHONPATH=. python devtools/onshape/templates/export_drawing.py <did> <wid> <eid> <FORMAT> <out_path>
FORMAT: DRAWING_JSON | PDF | DWG | DXF ...  Read-only for the drawing (nothing is stored in the document).
"""
import sys
import time

sys.path.insert(0, __file__.rsplit("\\", 1)[0].rsplit("/", 1)[0])
from _common import c  # noqa: E402


def export(did, wid, eid, fmt, out_path, extra=None):
    body = {"formatName": fmt, "storeInDocument": False}
    if fmt == "PDF":
        body.update({"colorMethod": "color", "showOverriddenDimensions": True, "destinationName": "export"})
    body.update(extra or {})
    r = c.post(f"/api/v10/drawings/d/{did}/w/{wid}/e/{eid}/translations", json_data=body)
    tid = r["id"]
    for _ in range(120):
        s = c.get(f"/api/v10/translations/{tid}")
        if s["requestState"] != "ACTIVE":
            break
        time.sleep(2)
    if s["requestState"] != "DONE":
        raise RuntimeError(str(s))
    ext = s["resultExternalDataIds"][0]
    headers = c.auth.get_headers(method="GET", path=f"/api/v10/documents/d/{did}/externaldata/{ext}", query_params=None,
                                 content_type="application/json")
    headers["Accept"] = "*/*"
    url = c.auth.get_full_url(f"/api/v10/documents/d/{did}/externaldata/{ext}", None)
    resp = c.session.get(url, headers=headers, timeout=120)
    resp.raise_for_status()
    open(out_path, "wb").write(resp.content)
    return out_path


if __name__ == "__main__":
    d, w, e, fmt, out = sys.argv[1:6]
    print(export(d, w, e, fmt, out))

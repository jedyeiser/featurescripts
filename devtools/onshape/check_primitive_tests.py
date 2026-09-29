"""Checks the Export primitive tests in "Primitive tests" (Publish & Drawing tools, built by build_primitive_tests.py).

Reads every "<prefix> PRIMITIVE" composite's attribute (schema primitive/1) through the eval API, plus the feature
statuses, and compares:
  * RD 20TAC known values (avg radius ~17.05 m, natural radius widest ~17.48 m / inflection ~16.18 m,
    RSL 1480, FRCPl 130, ARCPl 50) -- P1 / P2 / P3;
  * P4 (mirrored, tip -X) against P3: every value equal, x negated;
  * P5 (datum at x 500, z 10) against P1: every value equal, x - 500, z - 10;
  * structure: composite members, band bodies, key points.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_primitive_tests.py [--dump]
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761"
STUDIO = "Primitive tests"
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == STUDIO][0]

SCRIPT = r'''
function(context is Context, queries)
{
    var out = [];
    for (var body in evaluateQuery(context, qHasAttribute(qEverything(EntityType.BODY), "publishPrimitive")))
    {
        const data = getAttribute(context, { "entity" : body, "name" : "publishPrimitive" });
        var members = [];
        for (var m in evaluateQuery(context, qContainedInCompositeParts(body)))
        {
            members = append(members, getProperty(context, { "entity" : m, "propertyType" : PropertyType.NAME }));
        }
        out = append(out, "JSON " ~ toString({ "data" : data, "members" : members,
                    "name" : getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME }),
                    "bom" : getProperty(context, { "entity" : body, "propertyType" : PropertyType.EXCLUDE_FROM_BOM }) }));
    }
    return out;
}
'''


def parse_fs(text):
    """Parses FeatureScript's toString of maps / arrays ("{ key : value , ... }", "[ a , b ]") into Python.
    Relies on the primitive's strings never containing " , ", " : " or a bracket next to a space."""
    parts = re.split(r"(\{ | \}|\[ | \]| , | : )", text)
    # parts alternates text, separator, text, ...; texts may be "" (an empty string value).
    pos = [0]

    def text():
        t = parts[pos[0]]
        pos[0] += 1
        return t

    def sep():
        s = parts[pos[0]]
        pos[0] += 1
        return s

    def closed():
        # after a closing bracket comes an empty text before the next separator
        if pos[0] < len(parts):
            assert text() == ""

    def value():
        t = text()
        nxt = parts[pos[0]] if pos[0] < len(parts) else None
        if t == "" and nxt == "{ ":
            sep()
            out = {}
            while True:
                key = text()
                assert sep() == " : ", (key, pos[0])
                out[key] = value()
                s = sep()
                if s == " }":
                    closed()
                    return out
                assert s == " , ", s
        if t == "" and nxt == "[ ":
            sep()
            out = []
            while True:
                out.append(value())
                s = sep()
                if s == " ]":
                    closed()
                    return out
                assert s == " , ", s
        return scalar(t)

    def scalar(tok):
        try:
            return float(tok)
        except ValueError:
            return {"true": True, "false": False}.get(tok, tok)

    return value()


def read():
    r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT})
    blob = json.dumps(r.get("result"))
    strings = [bytes(s, "ascii").decode("unicode_escape") for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', blob)]
    return [s[5:] for s in strings if s.startswith("JSON ")], r


def statuses():
    f = c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")
    names = {x["featureId"]: x["name"] for x in f["features"]}
    return {names[k]: v["featureStatus"] for k, v in f["featureStates"].items() if k in names}


def primitives():
    items, raw = read()
    out = {}
    for s in items:
        v = parse_fs(s)
        out[v["data"]["prefix"]] = v
    return out


if __name__ == "__main__":
    prims = primitives()
    if "--dump" in sys.argv:
        for k, v in prims.items():
            d = v["data"]
            print("=====", k, "| bom", v["bom"], "| members", len(v["members"]))
            for sec in ("settings", "bands", "footprint"):
                print(sec, d[sec])
            for sec in ("scaleFactors", "metadata", "keyLocations", "baseline"):
                print("--", sec)
                for r in d[sec]:
                    print("   ", {kk: vv for kk, vv in r.items() if kk not in ("note",)})
            print("-- data")
            for r in d["data"]:
                print("   ", r)
            print("-- members", sorted(v["members"]))
        sys.exit(0)

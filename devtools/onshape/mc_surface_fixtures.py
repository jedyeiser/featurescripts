"""Surface fixtures shared by build_mmc_tests.py and build_mcp_tests.py: developable and B-spline faces.

  CYL   cylinder surface: arc centre (1000,0) r 100, 0 to 90 deg, extruded 100 up          (developable, analytic)
  CONE  cone surface: profile (2100,0)-(2040,100) revolved about the vertical axis at x = 2000   (developable, analytic)
  SPEX  extruded B-spline wall: fit spline (3000,0) (3050,30) (3100,0) (3150,30), extruded 100 up (developable, B-spline generator)
  LOFT  lofted B-spline surface between a Top-plane spline and a Front-plane spline            (not developable, a B-spline surface)

build(env) takes a dict of the builder's helpers (post, sketch, q, en, num, b, seg, arc, point, mm, SPECS) and returns
{ name: query expression of the surface's faces }.
"""
import copy
import math


def spec_feature(env, ftype, name, overrides, array_items=None):
    """A std feature from its spec: every parameter at its default, `overrides` (pid -> parameter dict) replacing some.
    array_items: pid -> list of {item pid: parameter dict}; each item starts from the item spec's defaults."""
    spec = env["SPECS"][ftype]
    params = []
    for p in spec["parameters"]:
        pid = p["parameterId"]
        if pid in overrides:
            params.append(overrides[pid])
        elif array_items and pid in array_items:
            items = []
            for over in array_items[pid]:
                item = []
                for ip in p["parameters"]:
                    iid = ip["parameterId"]
                    item.append(over[iid] if iid in over else dict(ip["defaultValue"], parameterId=iid))
                items.append({"btType": "BTMArrayParameterItem-1843", "parameters": item})
            params.append({"btType": "BTMParameterArray-2025", "parameterId": pid, "items": items})
        elif isinstance(p.get("defaultValue"), dict):
            params.append(dict(p["defaultValue"], parameterId=pid))
    return env["post"]({"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params})


def enum_value(env, ftype, pid, value):
    for p in env["SPECS"][ftype]["parameters"]:
        if p["parameterId"] == pid:
            d = dict(p["defaultValue"], parameterId=pid)
            d["value"] = value
            return d
    raise KeyError(pid)


def build(env):
    post, sketch, q, en, num, seg, arc, point, mm = (env[k] for k in ("post", "sketch", "q", "en", "num", "seg", "arc", "point", "mm"))
    sketch_on = env["sketch_on"]
    edges = lambda sk: 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % sk
    bodies = lambda fid: 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid
    faces = lambda fid: 'qCreatedBy(makeId("%s"), EntityType.FACE)' % fid
    out = {}

    def extrude_surface(name, sk):
        return post({"btType": "BTMFeature-134", "featureType": "extrude", "name": name, "parameters": [
            en("bodyType", "ExtendedToolBodyType", "SURFACE") | {"namespace": ""}, en("operationType", "NewBodyOperationType", "NEW") | {"namespace": ""},
            q("surfaceEntities", edges(sk)), en("endBound", "BoundingType", "BLIND") | {"namespace": ""}, num("depth", "100 mm")]})

    # cylinder
    sk = sketch("CYL arc (sketch) centre (1000,0) r 100", [arc("c", *mm(1000, 0, 100), 0.0, math.pi / 2)])
    out["CYL"] = faces(extrude_surface("CYL cylinder surface, 100 high", sk))

    # cone
    sk = sketch_on("Front", "CONE profile (sketch): axis x = 2000, line (2100,0)-(2040,100)",
                   [seg("ax", *mm(2000, 0, 2000, 150)), seg("pr", *mm(2100, 0, 2040, 100))])
    rev = spec_feature(env, "revolve", "CONE cone surface (revolved profile)", {
        "bodyType": enum_value(env, "revolve", "bodyType", "SURFACE"),
        "surfaceEntities": q("surfaceEntities", 'qCreatedBy(makeId("%s"), EntityType.EDGE)->qNthElement(1)' % sk),
        "axis": q("axis", 'qCreatedBy(makeId("%s"), EntityType.EDGE)->qNthElement(0)' % sk)})
    out["CONE"] = faces(rev)

    # extruded B-spline wall
    pts = sketch("SPEX points (3000,0) (3050,30) (3100,0) (3150,30)", [point("p%d" % i, *mm(x, y)) for i, (x, y) in enumerate([(3000, 0), (3050, 30), (3100, 0), (3150, 30)])])
    spline = spec_feature(env, "fitSpline", "SPEX fit spline", {"vertices": q("vertices", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % pts)})
    # a ruled surface aligned with the Top plane's normal over the spline: an extruded B-spline wall
    wall = spec_feature(env, "ruledSurface", "SPEX ruled surface along Z over the spline, 100 high", {
        "edges": q("edges", edges(spline)),
        "ruledType": enum_value(env, "ruledSurface", "ruledType", "ALIGNED_WITH_VECTOR"),
        "axis": q("axis", 'qCreatedBy(makeId("Top"), EntityType.FACE)'),
        "distance": num("distance", "100 mm")})
    out["SPEX"] = faces(wall)
    # lofted B-spline surface between a Top spline and a Front spline
    p1 = sketch("LOFT points 1 (Top)", [point("p%d" % i, *mm(x, y)) for i, (x, y) in enumerate([(4000, 0), (4050, 40), (4100, 0), (4150, 40)])])
    s1 = spec_feature(env, "fitSpline", "LOFT spline 1 (Top plane)", {"vertices": q("vertices", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % p1)})
    p2 = sketch_on("Front", "LOFT points 2 (Front)", [point("p%d" % i, *mm(x, y)) for i, (x, y) in enumerate([(4000, 100), (4050, 140), (4100, 100), (4150, 140)])])
    s2 = spec_feature(env, "fitSpline", "LOFT spline 2 (Front plane)", {"vertices": q("vertices", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % p2)})
    loft = spec_feature(env, "loft", "LOFT surface through the two splines", {"bodyType": enum_value(env, "loft", "bodyType", "SURFACE")},
                        {"wireProfilesArray": [{"wireProfileEntities": q("wireProfileEntities", bodies(s1))}, {"wireProfileEntities": q("wireProfileEntities", bodies(s2))}]})
    out["LOFT"] = faces(loft)
    return out

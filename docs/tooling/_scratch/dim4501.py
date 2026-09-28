import sys, json; sys.path.insert(0,"docs/tooling/_scratch")
from dwg import *
c._request("DELETE", f"/api/v6/elements/d/{D}/w/{W}/e/a73f5701ac0afd93274f10bd")
body={"drawingName":"4501 PLAN + PROFILE (demo)","border":True,"titleblock":True,"size":"A3","units":"millimeter","standard":"ISO"}
EID=c.post(f"/api/v6/drawings/d/{D}/w/{W}/create", json_data=body)["id"]; print("new", EID)
SC, CX = 1/5, 200.0
YP, YF = 215.0, 110.0
s=modify(EID,[{"messageName":"onshapeCreateViews","formatVersion":"2021-01-01","views":[
  {"viewType":"TopLevel","position":{"x":CX,"y":YP},"scale":{"scaleSource":"Custom","numerator":1,"denumerator":5},"orientation":"top","includeWires":True,"reference":{"elementId":PS,"idTag":"RsXD"}},
  {"viewType":"TopLevel","position":{"x":CX,"y":YF},"scale":{"scaleSource":"Custom","numerator":1,"denumerator":5},"orientation":"front","includeWires":True,"reference":{"elementId":PS,"idTag":"R+XD"}}]}],"4501 views")
print(s.get("output"))
import time; time.sleep(20)
vs=views(EID)["items"]
fmt={"dimdec":2,"type":"Onshape::Formatting::Dimension"}
anns=[]
want={"Rg":"Q_1","Rh":"Q_2","Ri":"Q_3","Rj":"Q_4","Rk":"Q_5","Ry":"Q_1","Rz":"Q_2","R0":"Q_3","R1":"Q_4","R2":"Q_5"}
for v in vs:
    VID=v["viewId"]; g=geometry(EID,VID)
    lines=[x for x in g["bodyData"] if x["type"]=="line" and x["deterministicId"][:2] in want and abs(x["data"]["start"][0]-x["data"]["end"][0])<1e-7]
    xs=[p[0] for x in g["bodyData"] if x["type"]=="line" for p in (x["data"]["start"],x["data"]["end"])]
    ox=(min(xs)+max(xs))/2*1000
    plan = abs(v["viewMatrix"][5]-1)<1e-6   # top view: model Y is sheet Y
    print(VID, "plan" if plan else "profile", len(lines), round(ox,1))
    for L in lines:
        a,b=L["data"]["start"],L["data"]["end"]
        xm=a[0]*1000
        def ref(p,snap): return {"coordinate":p,"type":"Onshape::Reference::Point","uniqueId":L["uniqueId"],"deterministicId":L["deterministicId"],"viewId":VID,"snapPointType":snap}
        ty = (YP if plan else YF + 18)
        anns.append({"type":"Onshape::Dimension::PointToPoint","pointToPointDimension":{"point1":ref(a,"ModeStart"),"point2":ref(b,"ModeEnd"),
            "textPosition":{"coordinate":[CX + (xm - ox) * SC + (9 if plan else 0), ty, 0],"type":"Onshape::Reference::Point"},"formatting":fmt}})
    s2=modify(EID,[{"messageName":"onshapeCreateAnnotations","formatVersion":"2021-01-01","annotations":anns}],"dims " + VID)
    print(" dims", s2.get("output")); anns=[]
anns.append({"type":"Onshape::Note","note":{"position":{"coordinate":[20, 280, 0],"type":"Onshape::Reference::Point"},
    "contents":"4501 core -- PLAN (widths) and PROFILE (thickness) at the same stations, both from Station geometry. No sketches.","textHeight":3.5}})
s=modify(EID,[{"messageName":"onshapeCreateAnnotations","formatVersion":"2021-01-01","annotations":anns}],"4501 dims")
print(s.get("output")[:300])
print(EID)

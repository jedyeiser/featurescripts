# Driver command file (ui_driver.py / uicmd.py): applies the drawing properties of
# publish_tools/research/drawing_style_guide.md to the open drawing. Controls are found by the text around them
# (section anchors + field label), not by DOM index. Set LOCK_FORMATS = True in the caller to also lock the
# format layers (do that last, after the frame / title block are on their layers).
f = [x for x in page.frames if 'drawing' in x.url][0]
MAP_JS = r"""() => { const h=[...document.querySelectorAll('*')].find(e=>e.childElementCount==0 && e.innerText && e.innerText.trim()=='Drawing properties'); let p=h; for(let k=0;k<4&&p;k++) p=p.parentElement;
 const ctrls=[...document.querySelectorAll('select, input')]; const res=[]; const walker=document.createTreeWalker(p, NodeFilter.SHOW_ELEMENT); let n; let texts=[];
 while(n=walker.nextNode()){
   if(n.tagName=='SELECT'||n.tagName=='INPUT'){ res.push({i: ctrls.indexOf(n), tag: n.tagName, type: n.type, ctx: texts.slice(-4), lab: ((n.closest('label')||{}).innerText||'').trim()}); }
   else if(n.childElementCount==0 && n.innerText && n.innerText.trim() && n.tagName!='OPTION'){ const t=n.innerText.trim(); if(texts[texts.length-1]!=t) texts.push(t);}
 } return res; }"""
TABS = [1249, 1287, 1325, 1363, 1401, 1439, 1477, 1515]
T = "0.25 mm"
RULES = [  # (anchors in order, field, kind, value)   kind: sel | inp | chk
    (["Units and precision"], "Decimal separator", "sel", "Period"),
    (["Units and precision"], "Precision", "sel", "0.1"),
    (["Units and precision"], "Angular precision", "sel", "0.1"),
    (["Annotations", "Notes"], "Hide property background", "chk", True),
    (["Views"], "Visible edges", "sel", "0.50 mm"),
    (["Views"], "View labels", "inp", "5"),
    (["Views"], "Scale label", "sel", "On"),
    (["Views", "Hidden edges"], "Thickness", "sel", T),
    (["Views", "Hidden edges", "Tangent edges"], "Thickness", "sel", T),
    (["Section views"], "Cutting lines", "sel", "0.50 mm"),
    (["View and region hatches"], "ISO02W100", "sel", T),
    (["Detail views"], "Detail profile lines", "sel", T),
    (["Auxiliary views"], "Viewing lines", "sel", T),
    (["Viewing line label"], "View label style", "sel", "A-A"),
    (["Break views"], "Break lines", "sel", T),
    (["Cosmetic threads"], "Thickness", "sel", T),
    (["Construction geometry"], "Centerlines", "sel", T),
    (["Construction geometry"], "Centermarks", "sel", T),
    (["Construction geometry"], "Sketch geometry", "sel", T),
    (["Construction geometry", "Pattern centermark"], "Thickness", "sel", T),
    (["Construction geometry", "Pattern centermark", "Centerlines"], "Thickness", "sel", T),
    (["Formats", "Border frame"], "Thickness", "sel", "0.70 mm"),
    (["Formats", "Border zone"], "Thickness", "sel", "0.35 mm"),
    (["Formats", "Title block"], "Thickness", "sel", T),
    (["BOM tables"], "Line thickness", "sel", T),
    (["Custom tables"], "Line thickness", "sel", T),
    (["Cut list tables"], "Line thickness", "sel", T),
    (["Hole tables"], "Line thickness", "sel", T),
    (["Revision tables"], "Line thickness", "sel", T),
    (["Revision tables"], "Title row text", "inp", "3.5"),
    (["Revision tables", "Revision callouts", "Tables"], "Line thickness", "sel", T),
    (["Revision tables", "Revision callouts", "Tables"], "Title row text", "inp", "3.5"),
]
if globals().get("LOCK_FORMATS"):
    RULES.append((["Formats"], "Lock", "sel", "Locked"))

if not f.get_by_text("Update properties from a template...").first.is_visible():
    page.mouse.click(1585, 539); page.wait_for_timeout(2500)
for x in TABS:                      # every tab once, so all controls are in the DOM
    page.mouse.click(x, 137); page.wait_for_timeout(1200)
ctrls = f.evaluate(MAP_JS)


def find(anchors, field, kind):
    j = 0
    for a in anchors:
        while j < len(ctrls) and a not in ctrls[j]["ctx"]:
            j += 1
    for c in ctrls[j:]:
        if kind == "chk":
            if c["type"] == "checkbox" and c["lab"] == field:
                return c
        elif c["ctx"] and c["ctx"][-1] == field and ((kind == "sel") == (c["tag"] == "SELECT")):
            return c
    return None


done = []
for anchors, field, kind, value in RULES:
    c = find(anchors, field, kind)
    if c is None:
        out("NOT FOUND", anchors, field); continue
    loc = f.locator("select, input").nth(c["i"])
    tab_of = None
    try:
        if not loc.is_visible():      # bring its tab forward
            for x in TABS:
                page.mouse.click(x, 137); page.wait_for_timeout(600)
                if loc.is_visible():
                    break
        loc.scroll_into_view_if_needed()
        if kind == "sel":
            loc.select_option(label=value)
        elif kind == "inp":
            loc.fill(value); loc.press("Enter")
        else:
            if loc.is_checked() != value:
                loc.click()
        page.wait_for_timeout(700)
        done.append("%s/%s=%s" % (anchors[-1], field, value))
    except Exception as e:
        out("FAILED", anchors, field, str(e)[:200])
out("applied:", len(done)); out("\n".join(done))

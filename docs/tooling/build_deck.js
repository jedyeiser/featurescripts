// Build one feature deck (.pptx) from a JSON spec.
//
// usage (repo root):  node docs/tooling/build_deck.js docs/decks/<slug>/spec.json
// then:               powershell -File docs/tooling/export_deck.ps1 docs/decks/<slug>/<slug>.pptx
//
// Spec (paths relative to the repo root):
// {
//   "slug": "split_plus", "title": "Split+", "tagline": "...", "document": "Reference_Side_Features",
//   "icon": "icons/final/split_plus_icon.svg", "status": "Draft 2026-09-25",
//   "slides": [ { "type": "...", ... }, ... ]
// }
// Slide types:
//   why       { title, problem, useWhen: [..], image, caption }
//   concept   { title, image, points: [{head, body}] }
//   dialog    { title, screenshot?, params: [[name, meaning], ..] }   (params split over slides of 8 rows)
//   examples  { title, cards: [{image?, head, body}] }                  (1-3 cards)
//   outputs   { title, keys: [[key, meaning]], messages: [[kind, text, meaning]] }
//   tips      { title, items: [{kind: "tip"|"limit", head, body}] }
//   reference { title, rows: [[label, value]] }
// A missing image or screenshot draws a labelled placeholder so gaps are visible, never silent.
const fs = require("fs");
const path = require("path");
const pptxgen = require("pptxgenjs");
const sharp = require("sharp");

const ROOT = path.resolve(__dirname, "..", "..");
const C = {
  dark: "22262B", ink: "1F2329", ink2: "52514E", muted: "8A8883", line: "D9D6CF", tint: "F3F5F8",
  blue: "1651B0", keep: "1BAF7A", warn: "C9820A", err: "C8453F", white: "FFFFFF",
};
const FONT = "Calibri";
const W = 10, H = 5.625, M = 0.5;

function abs(p) { return path.isAbsolute(p) ? p : path.join(ROOT, p); }
function exists(p) { return p && fs.existsSync(abs(p)); }

async function iconPng(svgPath, size) {
  if (!exists(svgPath)) return null;
  const buf = await sharp(abs(svgPath), { density: 600 }).resize(size, size).png().toBuffer();
  return "image/png;base64," + buf.toString("base64");
}

async function imageSize(p) {
  const m = await sharp(abs(p)).metadata();
  return { w: m.width, h: m.height };
}

// Fit an image inside a box, keeping its aspect ratio, centred.
async function addFitted(slide, p, x, y, w, h) {
  if (!exists(p)) {
    placeholder(slide, x, y, w, h, "IMAGE MISSING: " + (p || "(none)"));
    return;
  }
  const s = await imageSize(p);
  const r = Math.min(w / s.w, h / s.h);
  const iw = s.w * r, ih = s.h * r;
  slide.addImage({ path: abs(p), x: x + (w - iw) / 2, y: y + (h - ih) / 2, w: iw, h: ih });
}

function placeholder(slide, x, y, w, h, label) {
  slide.addShape("rect", { x, y, w, h, fill: { color: C.tint }, line: { color: C.muted, width: 1, dashType: "dash" } });
  slide.addText(label, { x, y, w, h, fontFace: FONT, fontSize: 11, color: C.muted, align: "center", valign: "middle", isTextBox: true, margin: 6 });
}

// Header motif: the feature icon in a white tile, then the slide title. No bars or rules.
function header(slide, spec, icon, title) {
  if (icon) {
    slide.addShape("roundRect", { x: M, y: 0.32, w: 0.52, h: 0.52, fill: { color: C.tint }, line: { color: C.line, width: 0.75 }, rectRadius: 0.08 });
    slide.addImage({ data: icon, x: M + 0.08, y: 0.40, w: 0.36, h: 0.36 });
  }
  slide.addText(title, { x: M + 0.68, y: 0.28, w: W - 2 * M - 0.68, h: 0.6, fontFace: FONT, fontSize: 24, bold: true, color: C.ink, valign: "middle", margin: 0, isTextBox: true });
  slide.addText(spec.title + "  ·  " + spec.document, { x: M, y: H - 0.38, w: 6, h: 0.25, fontFace: FONT, fontSize: 9, color: C.muted, margin: 0, isTextBox: true });
  slide.addText(spec.status || "", { x: W - M - 3, y: H - 0.38, w: 3, h: 0.25, fontFace: FONT, fontSize: 9, color: C.muted, align: "right", margin: 0, isTextBox: true });
}

function bullets(items, size) {
  return items.map((t, i) => ({ text: t, options: { bullet: { indent: 14 }, breakLine: i < items.length - 1, paraSpaceAfter: 6 } }));
}

function table(slide, rows, x, y, w, colW, opts) {
  const head = opts.head.map((t) => ({ text: t, options: { bold: true, color: C.white, fill: { color: C.dark } } }));
  const body = rows.map((r, i) => r.map((cell, j) => {
    const o = { fill: { color: i % 2 ? C.white : C.tint } };
    if (j === 0) { o.bold = true; o.color = opts.firstColor ? opts.firstColor(r) : C.ink; }
    return { text: String(cell), options: o };
  }));
  slide.addTable([head, ...body], {
    x, y, w, colW, fontFace: FONT, fontSize: opts.fontSize || 11, color: C.ink, valign: "middle",
    border: { type: "solid", pt: 0.5, color: C.line }, margin: [3, 6, 3, 6], autoPage: false,
  });
}

async function build(specPath, force) {
  const spec = JSON.parse(fs.readFileSync(specPath, "utf8"));
  // Never overwrite a deck the user has edited: it differs from the copy saved at the last build.
  const target = path.join(path.dirname(specPath), spec.slug + ".pptx");
  const saved = path.join(path.dirname(specPath), ".baseline", spec.slug + ".generated.pptx");
  if (!force && fs.existsSync(target) && fs.existsSync(saved)
      && !fs.readFileSync(target).equals(fs.readFileSync(saved))) {
    throw new Error(target + " was edited after it was generated. Fold the edits into the spec (docs/tooling/deck_diff.py), "
      + "or pass --force to overwrite them.");
  }
  const pres = new pptxgen();
  pres.layout = "LAYOUT_16x9";
  pres.author = "FeatureScript docs";
  pres.title = spec.title;
  const iconBig = await iconPng(spec.icon, 512);
  const iconSmall = await iconPng(spec.icon, 128);

  // ---- title slide (dark)
  {
    const s = pres.addSlide();
    s.background = { color: C.dark };
    s.addShape("roundRect", { x: M, y: 1.35, w: 1.7, h: 1.7, fill: { color: C.white }, line: { color: C.white }, rectRadius: 0.18 });
    if (iconBig) s.addImage({ data: iconBig, x: M + 0.25, y: 1.6, w: 1.2, h: 1.2 });
    else s.addText("no icon", { x: M, y: 1.35, w: 1.7, h: 1.7, fontFace: FONT, fontSize: 12, color: C.muted, align: "center", valign: "middle", isTextBox: true });
    s.addText(spec.title, { x: M + 2.1, y: 1.3, w: W - 2 * M - 2.1, h: 0.95, fontFace: FONT, fontSize: 44, bold: true, color: C.white, margin: 0, valign: "bottom", isTextBox: true });
    s.addText(spec.tagline, { x: M + 2.1, y: 2.3, w: W - 2 * M - 2.1, h: 0.9, fontFace: FONT, fontSize: 16, color: "C9CED6", margin: 0, valign: "top", isTextBox: true });
    s.addText(spec.document + "  ·  Onshape custom feature", { x: M, y: H - 0.9, w: 6, h: 0.3, fontFace: FONT, fontSize: 11, color: "9AA3AE", margin: 0, isTextBox: true });
    s.addText(spec.status || "", { x: W - M - 3, y: H - 0.9, w: 3, h: 0.3, fontFace: FONT, fontSize: 11, color: "9AA3AE", align: "right", margin: 0, isTextBox: true });
  }

  for (const sl of spec.slides) {
    if (sl.type === "why") {
      const s = pres.addSlide();
      header(s, spec, iconSmall, sl.title || "What it does");
      s.addText(sl.problem, { x: M, y: 1.1, w: 4.1, h: 1.5, fontFace: FONT, fontSize: 14, color: C.ink, margin: 0, valign: "top", isTextBox: true });
      s.addText("Use it when", { x: M, y: 2.7, w: 4.1, h: 0.3, fontFace: FONT, fontSize: 13, bold: true, color: C.blue, margin: 0, isTextBox: true });
      s.addText(bullets(sl.useWhen), { x: M, y: 3.02, w: 4.1, h: 1.9, fontFace: FONT, fontSize: 12.5, color: C.ink, margin: 0, valign: "top", isTextBox: true });
      await addFitted(s, sl.image, 4.9, 1.05, W - 4.9 - M, 3.55);
      if (sl.caption) s.addText(sl.caption, { x: 4.9, y: 4.62, w: W - 4.9 - M, h: 0.4, fontFace: FONT, fontSize: 10, italic: true, color: C.ink2, margin: 0, isTextBox: true });
    } else if (sl.type === "concept") {
      const s = pres.addSlide();
      header(s, spec, iconSmall, sl.title);
      const n = (sl.points || []).length;
      const imgH = n ? 2.75 : 3.9;
      await addFitted(s, sl.image, M, 1.0, W - 2 * M, imgH);
      if (n) {
        const gap = 0.25, cw = (W - 2 * M - gap * (n - 1)) / n, y = 1.0 + imgH + 0.15;
        sl.points.forEach((p, i) => {
          const x = M + i * (cw + gap);
          s.addShape("roundRect", { x, y, w: cw, h: 1.0, fill: { color: C.tint }, line: { color: C.tint }, rectRadius: 0.06 });
          s.addText([{ text: p.head, options: { bold: true, color: C.blue, breakLine: true } }, { text: p.body, options: { color: C.ink } }],
            { x: x + 0.12, y: y + 0.06, w: cw - 0.24, h: 0.88, fontFace: FONT, fontSize: 11, margin: 0, valign: "top", isTextBox: true });
        });
      }
    } else if (sl.type === "dialog") {
      const rows = sl.params;
      const per = sl.screenshot !== undefined ? 8 : 11;
      for (let k = 0; k < rows.length; k += per) {
        const s = pres.addSlide();
        header(s, spec, iconSmall, (sl.title || "The dialog") + (rows.length > per ? "  (" + (k / per + 1) + "/" + Math.ceil(rows.length / per) + ")" : ""));
        let x = M, w = W - 2 * M;
        if (sl.screenshot !== undefined) {
          await addFitted(s, sl.screenshot, M, 1.0, 2.9, 3.95);
          x = M + 3.15; w = W - x - M;
        }
        table(s, rows.slice(k, k + per), x, 1.0, w, [w * 0.3, w * 0.7], { head: ["Parameter", "What it means"], fontSize: 10.5 });
      }
    } else if (sl.type === "dialogshot") {
      // One real dialog screenshot with numbered badges on its fields, and the same numbers in the table.
      // Positions come from <screenshot>.json (written by onshape_shot.py) and sl.positions (manual, e.g. an arrow
      // button with no text): {name: [fx, fy]} or {name: [fx, fy, "center"]} in fractions of the image.
      const s = pres.addSlide();
      header(s, spec, iconSmall, sl.title);
      const auto = exists(sl.screenshot) && fs.existsSync(abs(sl.screenshot).replace(/\.png$/, ".json"))
        ? JSON.parse(fs.readFileSync(abs(sl.screenshot).replace(/\.png$/, ".json"), "utf8")) : {};
      const pos = Object.assign({}, auto, sl.positions || {});
      const boxH = 3.95, boxW = 3.0, x0 = M + 0.35, y0 = 1.0;
      let ix = x0, iy = y0, iw = boxW, ih = boxH;
      if (exists(sl.screenshot)) {
        const sz = await imageSize(sl.screenshot);
        const r = Math.min(boxW / sz.w, boxH / sz.h);
        iw = sz.w * r; ih = sz.h * r;
        s.addImage({ path: abs(sl.screenshot), x: ix, y: iy, w: iw, h: ih });
        s.addShape("rect", { x: ix, y: iy, w: iw, h: ih, fill: { type: "none" }, line: { color: C.line, width: 0.75 } });
      } else {
        placeholder(s, ix, iy, iw, ih, "IMAGE MISSING: " + sl.screenshot);
      }
      const rows = [];
      sl.params.forEach((p, k) => {
        const n = String(k + 1);
        rows.push([n, p[0], p[1]]);
        const at = pos[p[0]];
        if (!at) return;
        const d = 0.26;
        // badges sit just outside the screenshot at the field's height, so they never cover it
        const cx = at[2] === "right" ? ix + iw + 0.2 : ix - 0.2;
        const cy = iy + at[1] * ih;
        s.addShape("ellipse", { x: cx - d / 2, y: cy - d / 2, w: d, h: d, fill: { color: C.blue }, line: { color: C.white, width: 1 } });
        s.addText(n, { x: cx - d / 2, y: cy - d / 2, w: d, h: d, fontFace: FONT, fontSize: 10, bold: true, color: C.white, align: "center", valign: "middle", margin: 0, isTextBox: true });
      });
      const tx = x0 + boxW + 0.35, tw = W - tx - M;
      table(s, rows, tx, 1.0, tw, [0.35, tw * 0.3, tw - 0.35 - tw * 0.3], { head: ["#", "Parameter", "What it means"], fontSize: 10.5,
        firstColor: () => C.blue });
      if (sl.note) s.addText(sl.note, { x: tx, y: 4.55, w: tw, h: 0.45, fontFace: FONT, fontSize: 10, italic: true, color: C.ink2, margin: 0, isTextBox: true });
    } else if (sl.type === "example") {
      // One example per slide: a wide image, then the explanation.
      const s = pres.addSlide();
      header(s, spec, iconSmall, sl.title);
      await addFitted(s, sl.image, M, 1.0, W - 2 * M, 2.75);
      s.addText([{ text: sl.head, options: { bold: true, color: C.ink, breakLine: true } }, { text: sl.body, options: { color: C.ink2 } }],
        { x: M, y: 3.85, w: W - 2 * M, h: 1.2, fontFace: FONT, fontSize: 12.5, margin: 0, valign: "top", isTextBox: true });
    } else if (sl.type === "examples") {
      const s = pres.addSlide();
      header(s, spec, iconSmall, sl.title || "Examples");
      const n = sl.cards.length, gap = 0.25, cw = (W - 2 * M - gap * (n - 1)) / n;
      for (let i = 0; i < n; i++) {
        const c = sl.cards[i], x = M + i * (cw + gap);
        if (c.image !== undefined) await addFitted(s, c.image, x, 1.0, cw, 2.35);
        const ty = c.image !== undefined ? 3.45 : 1.0;
        s.addText([{ text: c.head, options: { bold: true, color: C.ink, breakLine: true } }, { text: c.body, options: { color: C.ink2 } }],
          { x, y: ty, w: cw, h: 4.9 - ty, fontFace: FONT, fontSize: 11, margin: 0, valign: "top", isTextBox: true });
      }
    } else if (sl.type === "outputs") {
      // Keys and messages side by side, or either one alone across the full width.
      const s = pres.addSlide();
      header(s, spec, iconSmall, sl.title || "Outputs and messages");
      const both = sl.keys && sl.messages;
      const lw = both ? 4.25 : W - 2 * M;
      if (sl.keys) {
        s.addText("Published for Extract variables", { x: M, y: 0.98, w: lw, h: 0.3, fontFace: FONT, fontSize: 12, bold: true, color: C.blue, margin: 0, isTextBox: true });
        table(s, sl.keys, M, 1.3, lw, [lw * (both ? 0.36 : 0.26), lw * (both ? 0.64 : 0.74)], { head: ["Key", "What it holds"], fontSize: both ? 9.5 : 10.5 });
      }
      if (sl.messages) {
        const rx = both ? M + lw + 0.3 : M, rw = W - rx - M;
        s.addText("Messages", { x: rx, y: 0.98, w: rw, h: 0.3, fontFace: FONT, fontSize: 12, bold: true, color: C.blue, margin: 0, isTextBox: true });
        const colour = (r) => (r[0] === "Error" ? C.err : r[0] === "Warning" ? C.warn : C.keep);
        table(s, sl.messages.map((m) => [m[0], m[1] + (m[2] ? "\n" + m[2] : "")]), rx, 1.3, rw, [rw * (both ? 0.2 : 0.12), rw * (both ? 0.8 : 0.88)],
          { head: ["", "Message / what to do"], fontSize: both ? 9.5 : 11, firstColor: colour });
      }
    } else if (sl.type === "tips") {
      const s = pres.addSlide();
      header(s, spec, iconSmall, sl.title || "Tips and limits");
      const n = sl.items.length, cols = n > 3 ? 2 : 1, rowsN = Math.ceil(n / cols);
      const cw = (W - 2 * M - 0.3 * (cols - 1)) / cols, rh = Math.min(1.15, 3.9 / rowsN);
      sl.items.forEach((it, i) => {
        const col = Math.floor(i / rowsN), row = i % rowsN;
        const x = M + col * (cw + 0.3), y = 1.0 + row * rh;
        const colr = it.kind === "limit" ? C.warn : C.keep;
        s.addShape("ellipse", { x, y: y + 0.05, w: 0.3, h: 0.3, fill: { color: colr }, line: { color: colr } });
        s.addText(it.kind === "limit" ? "!" : "✓", { x, y: y + 0.05, w: 0.3, h: 0.3, fontFace: FONT, fontSize: 12, bold: true, color: C.white, align: "center", valign: "middle", margin: 0, isTextBox: true });
        s.addText([{ text: it.head, options: { bold: true, color: C.ink, breakLine: true } }, { text: it.body, options: { color: C.ink2 } }],
          { x: x + 0.42, y, w: cw - 0.42, h: rh - 0.08, fontFace: FONT, fontSize: 11, margin: 0, valign: "top", isTextBox: true });
      });
    } else if (sl.type === "reference") {
      const s = pres.addSlide();
      header(s, spec, iconSmall, sl.title || "Where it lives");
      table(s, sl.rows, M, 1.05, W - 2 * M, [2.2, W - 2 * M - 2.2], { head: ["", ""], fontSize: 11 });
    } else {
      throw new Error("unknown slide type " + sl.type);
    }
  }

  const out = path.join(path.dirname(specPath), spec.slug + ".pptx");
  await pres.writeFile({ fileName: out });
  // Keep what was generated, so docs/tooling/deck_diff.py can show the user's edits later.
  const baseDir = path.join(path.dirname(specPath), ".baseline");
  fs.mkdirSync(baseDir, { recursive: true });
  fs.copyFileSync(out, path.join(baseDir, spec.slug + ".generated.pptx"));
  console.log("wrote", out);
}

build(process.argv[2], process.argv.includes("--force")).catch((e) => { console.error(e); process.exit(1); });

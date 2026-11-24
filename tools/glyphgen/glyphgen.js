// Renders the Arabic/Latin glyph textures used by the in-game text renderer.
//
// GTA V's fonts contain no Arabic letters, so the mod draws text itself from one small PNG per
// glyph form (ScriptHookV textures cannot be cut out of an atlas). This script renders every
// glyph with the Cairo font (SIL OFL, fonts/OFL.txt) in headless Chromium and writes:
//   <out>/glyphs/<weight><size>_<HEX>.png   white glyph on transparent background
//   <out>/glyphs.txt                         metrics parsed by GlyphFont.ParseSet
//
// Usage (from the repository root, Node + Playwright with Chromium installed):
//   dotnet run --project tools/OrganizationEmpire.PackTool -- --glyph-spec > spec.json
//   node tools/glyphgen/glyphgen.js spec.json tools/glyphgen/fonts assets/text
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const [specPath, fontDir, outDir] = process.argv.slice(2);
if (!specPath || !fontDir || !outDir) { console.error('usage: glyphgen.js <spec.json> <fontDir> <outDir>'); process.exit(2); }

const spec = JSON.parse(fs.readFileSync(specPath, 'utf8').replace(/^﻿/, ''));
const WEIGHTS = { r: 600, b: 800 };      // regular text, headings / condensed GTA font
const VARIANTS = [30, 60];               // em size in px of the small and large textures

const fontFace = (file, range) => `@font-face{font-family:Cairo;font-weight:200 1000;src:url(data:font/woff2;base64,${
  fs.readFileSync(path.join(fontDir, file)).toString('base64')}) format('woff2');unicode-range:${range};}`;
const css =
  fontFace('cairo-arabic.woff2', 'U+0600-06FF,U+0750-077F,U+0870-08FF,U+200C-200E,U+2010-2011,U+FB50-FDFF,U+FE70-FEFC') +
  fontFace('cairo-latinext.woff2', 'U+0100-02BA,U+02BD-02C5,U+02C7-02CC,U+02CE-02D7,U+02DD-02FF,U+1E00-1E9F,U+20A0-20AB,U+20AD-20C4') +
  fontFace('cairo-latin.woff2', 'U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+2000-206F,U+20AC,U+2122,U+2190-2193,U+2212,U+2215');

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await page.setContent(`<!doctype html><meta charset="utf-8"><style>${css}</style><body style="font-family:Cairo">ابتث abc</body>`);
  const ready = await page.evaluate(async (weights) => {
    for (const w of Object.values(weights)) {
      await document.fonts.load(`${w} 30px Cairo`, 'ابتثلا');
      await document.fonts.load(`${w} 30px Cairo`, 'abcdef');
    }
    await document.fonts.ready;
    return document.fonts.check('600 30px Cairo', 'ب') && document.fonts.check('600 30px Cairo', 'a');
  }, WEIGHTS);
  if (!ready) throw new Error('Cairo font did not load');

  const results = await page.evaluate(({ spec, weights, variants }) => {
    const out = { glyphs: [], space: {} };
    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    for (const [wk, wv] of Object.entries(weights)) {
      ctx.font = `${wv} 100px Cairo`;
      out.space[wk] = ctx.measureText(' ').width / 100;
      for (const g of spec) {
        const rec = { weight: wk, code: g.code, variants: [] };
        variants.forEach((em, vi) => {
          const W = Math.ceil(em * 4), H = Math.ceil(em * 3);
          canvas.width = W; canvas.height = H;
          ctx.clearRect(0, 0, W, H);
          ctx.font = `${wv} ${em}px Cairo`;
          ctx.textBaseline = 'alphabetic';
          ctx.textAlign = 'left';
          ctx.direction = 'ltr';
          ctx.fillStyle = '#fff';
          const x0 = Math.round(em * 1.2), y0 = Math.round(em * 1.8);
          ctx.fillText(g.text, x0, y0);
          const adv = ctx.measureText(g.text).width;
          if (vi === 0) rec.advance = adv / em;
          const img = ctx.getImageData(0, 0, W, H);
          let minX = W, minY = H, maxX = -1, maxY = -1;
          for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
            if (img.data[(y * W + x) * 4 + 3] > 2) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
          }
          if (maxX < 0) { rec.variants.push(null); return; }
          minX = Math.max(0, minX - 1); minY = Math.max(0, minY - 1); maxX = Math.min(W - 1, maxX + 1); maxY = Math.min(H - 1, maxY + 1);
          const cw = maxX - minX + 1, ch = maxY - minY + 1;
          const crop = ctx.getImageData(minX, minY, cw, ch);
          for (let i = 0; i < crop.data.length; i += 4) { crop.data[i] = 255; crop.data[i + 1] = 255; crop.data[i + 2] = 255; }
          const c2 = document.createElement('canvas'); c2.width = cw; c2.height = ch;
          c2.getContext('2d').putImageData(crop, 0, 0);
          rec.variants.push({ png: c2.toDataURL('image/png').split(',')[1], left: (minX - x0) / em, top: (minY - y0) / em, w: cw / em, h: ch / em,
                              edgeHit: minX === 0 || minY === 0 || maxX === W - 1 || maxY === H - 1 });
        });
        out.glyphs.push(rec);
      }
    }
    return out;
  }, { spec, weights: WEIGHTS, variants: VARIANTS });
  await browser.close();

  const glyphDir = path.join(outDir, 'glyphs');
  fs.rmSync(glyphDir, { recursive: true, force: true });
  fs.mkdirSync(glyphDir, { recursive: true });
  const hex = (c) => c.toString(16).toUpperCase().padStart(4, '0');
  const f = (v) => (Math.round(v * 10000) / 10000).toString();
  const lines = ['# Glyph metrics for the in-game Arabic text renderer. Generated by tools/glyphgen/glyphgen.js - do not edit.',
                 '# Font: Cairo (SIL Open Font License 1.1, see OFL-Cairo.txt). Units: em, y grows downwards from the baseline.'];
  let files = 0, problems = 0;
  for (const wk of Object.keys(WEIGHTS)) {
    const mine = results.glyphs.filter(g => g.weight === wk);
    const top = (chars) => Math.max(...chars.map(c => { const g = mine.find(x => x.code === c.codePointAt(0)); return g && g.variants[0] ? -g.variants[0].top : 0; }));
    const bottom = (chars) => Math.max(...chars.map(c => { const g = mine.find(x => x.code === c.codePointAt(0)); return g && g.variants[0] ? g.variants[0].top + g.variants[0].h : 0; }));
    const ascent = top(['H', 'd', 'l', 'ﺍ', 'ﻝ', 'ﻙ', 'ﻁ', 'ﺃ']);
    const descent = bottom(['g', 'y', 'p', 'ﻱ', 'ﺝ', 'ﻉ', 'ﺭ', 'ﻥ', 'ﻭ']);
    lines.push(`font ${wk} ${VARIANTS[0]} ${VARIANTS[1]} ${f(ascent)} ${f(descent)} ${f(results.space[wk])}`);
    for (const g of mine) {
      const box = (v) => v ? `${f(v.left)} ${f(v.top)} ${f(v.w)} ${f(v.h)}` : '0 0 0 0';
      lines.push(`g ${wk} ${hex(g.code)} ${f(g.advance)} ${box(g.variants[0])} ${box(g.variants[1])}`);
      g.variants.forEach((v, vi) => {
        if (!v) return;
        if (v.edgeHit) { problems++; console.warn(`glyph ${hex(g.code)} touches the render canvas edge`); }
        fs.writeFileSync(path.join(glyphDir, `${wk}${vi === 0 ? 's' : 'l'}_${hex(g.code)}.png`), Buffer.from(v.png, 'base64'));
        files++;
      });
    }
  }
  fs.writeFileSync(path.join(outDir, 'glyphs.txt'), lines.join('\n') + '\n');
  fs.copyFileSync(path.join(fontDir, 'OFL.txt'), path.join(outDir, 'OFL-Cairo.txt'));
  console.log(`wrote ${files} glyph textures, ${problems} warnings`);
})().catch(e => { console.error(e); process.exit(1); });

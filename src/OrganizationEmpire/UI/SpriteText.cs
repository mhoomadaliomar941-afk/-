using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using GTA.UI;
using OrganizationEmpire.Core.Localization;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.UI
{
    /// <summary>
    /// Draws text from pre-rendered glyph textures (text\glyphs\*.png), used for Arabic because GTA V's
    /// fonts have no Arabic letters and its text engine neither joins letters nor runs right-to-left.
    ///
    /// ScriptHookV textures are always drawn above the game's own UI, so glyphs are queued during the
    /// frame and emitted at the end in painter's order: any opaque rectangle the menu drew later on top
    /// of a queued line (a dialog, a popup, a toast) hides the glyphs underneath it, exactly as it hides
    /// native text. Limits handled here:
    ///  * a texture can be on screen at most 64 times per frame, so busy glyphs get extra "banks"
    ///    (copies of the PNG in text\cache, each its own texture);
    ///  * a drawn instance stays visible for ~100 ms, so instances used last frame but not this frame
    ///    are overwritten with transparent draws to avoid ghost text.
    /// </summary>
    internal sealed class SpriteText
    {
        private const int InstancesPerTexture = 64;
        private const int MaxBanks = 12;

        private sealed class Tex
        {
            public string Source;
            public readonly List<CustomSprite> Banks = new List<CustomSprite>();
            public int Count, LastCount;
            public bool Broken;
        }

        private struct Quad
        {
            public Tex Tex;
            public float X, Y, W, H;
            public Color C;
        }

        private struct Item
        {
            public float X0, Y0, X1, Y1;
            public int Seq, First, Count;
        }

        private struct Occluder
        {
            public float X, Y, W, H;
            public int Seq;
        }

        private readonly Dictionary<string, Tex> _textures = new Dictionary<string, Tex>(StringComparer.Ordinal);
        private readonly Dictionary<string, LineLayout> _layouts = new Dictionary<string, LineLayout>(StringComparer.Ordinal);
        private readonly List<Quad> _quads = new List<Quad>(4096);
        private readonly List<Item> _items = new List<Item>(512);
        private readonly List<Occluder> _occluders = new List<Occluder>(256);
        private readonly List<int> _hits = new List<int>();
        private Dictionary<string, GlyphFont> _fonts;
        private string _glyphDir, _cacheDir;
        private bool _warnedBanks;

        public bool Available { get; private set; }
        public string Problem { get; private set; }
        public bool HasQueued => _items.Count > 0;
        public int LastFrameGlyphs { get; private set; }

        public bool Load(string textDir)
        {
            Available = false;
            try
            {
                string metrics = Path.Combine(textDir, "glyphs.txt");
                _glyphDir = Path.Combine(textDir, "glyphs");
                _cacheDir = Path.Combine(textDir, "cache");
                if (!File.Exists(metrics) || !Directory.Exists(_glyphDir))
                {
                    Problem = "text\\glyphs.txt or text\\glyphs\\ is missing - reinstall the scripts\\OrganizationEmpire\\text folder.";
                    return false;
                }
                var problems = new List<string>();
                _fonts = GlyphFont.ParseSet(File.ReadAllText(metrics), problems);
                if (!_fonts.ContainsKey("r") || !_fonts.ContainsKey("b") || _fonts["r"].Count < 100)
                {
                    Problem = "text\\glyphs.txt is incomplete (" + problems.Count + " problems).";
                    return false;
                }
                // Bank copies from an older version could hold outdated shapes.
                if (Directory.Exists(_cacheDir))
                {
                    foreach (string f in Directory.GetFiles(_cacheDir, "*.png")) { try { File.Delete(f); } catch (IOException) { } }
                }
                Available = true;
                Problem = null;
                return true;
            }
            catch (Exception ex)
            {
                Problem = ex.Message;
                Log.Error("Arabic glyphs could not be loaded", ex);
                return false;
            }
        }

        public GlyphFont FontFor(GTA.UI.Font font) => font == GTA.UI.Font.ChaletComprimeCologne || font == GTA.UI.Font.Pricedown ? _fonts["b"] : _fonts["r"];

        public LineLayout Layout(string text, GTA.UI.Font font, bool rtl)
        {
            var f = FontFor(font);
            string key = f.Weight + (rtl ? "<" : ">") + text;
            if (_layouts.TryGetValue(key, out var lay)) return lay;
            if (_layouts.Count > 3000) _layouts.Clear();
            lay = f.Layout(text, rtl);
            _layouts[key] = lay;
            return lay;
        }

        public float Ascent(GTA.UI.Font font) => FontFor(font).Ascent;
        public float Descent(GTA.UI.Font font) => FontFor(font).Descent;

        /// <summary>
        /// Queues one line. <paramref name="x"/> is the anchor in screen space (left edge, center or right edge per
        /// <paramref name="align"/>), <paramref name="baseline"/> the baseline y; all in the renderer's virtual units.
        /// </summary>
        public void Queue(string text, float x, float baseline, float em, Color c, GTA.UI.Font font, Align align, bool rtl, bool shadow,
            int seq, float pxPerUnit)
        {
            if (string.IsNullOrEmpty(text) || c.A == 0 || em <= 0.5f) return;
            var f = FontFor(font);
            var lay = Layout(text, font, rtl);
            if (lay.Glyphs.Length == 0) return;
            float width = lay.Width * em;
            float left = align == Align.Left ? x : align == Align.Center ? x - width / 2f : x - width;
            // Snap the baseline to a device pixel so every glyph of the line samples the same rows.
            if (pxPerUnit > 0f) baseline = (float)Math.Round(baseline * pxPerUnit) / pxPerUnit;
            int variant = em * pxPerUnit > 40f ? 1 : 0;

            var item = new Item { X0 = left, X1 = left + width, Y0 = baseline - f.Ascent * em, Y1 = baseline + f.Descent * em, Seq = seq, First = _quads.Count };
            if (shadow)
            {
                float off = Math.Max(1f, em * 0.06f);
                var sc = Color.FromArgb(Math.Min(210, (int)c.A), 0, 0, 0);
                AddGlyphs(lay, f, variant, left + off, baseline + off, em, sc);
            }
            AddGlyphs(lay, f, variant, left, baseline, em, c);
            item.Count = _quads.Count - item.First;
            if (item.Count > 0) _items.Add(item);
        }

        private void AddGlyphs(LineLayout lay, GlyphFont f, int variant, float left, float baseline, float em, Color c)
        {
            foreach (var pg in lay.Glyphs)
            {
                var box = pg.Glyph.Boxes[variant];
                if (!box.Valid) continue;
                var tex = Texture(GlyphFont.FileName(f.Weight, variant, pg.Glyph.Code));
                if (tex == null) continue;
                _quads.Add(new Quad { Tex = tex, X = left + (pg.X + box.Left) * em, Y = baseline + box.Top * em, W = box.Width * em, H = box.Height * em, C = c });
            }
        }

        /// <summary>Registers an opaque rectangle drawn at <paramref name="seq"/>; it hides glyphs queued before it.</summary>
        public void Occlude(float x, float y, float w, float h, int seq)
        {
            if (_items.Count == 0) return; // nothing queued yet can be covered
            _occluders.Add(new Occluder { X = x, Y = y, W = w, H = h, Seq = seq });
        }

        private Tex Texture(string file)
        {
            if (_textures.TryGetValue(file, out var t)) return t.Broken ? null : t;
            t = new Tex { Source = Path.Combine(_glyphDir, file) };
            if (!File.Exists(t.Source)) t.Broken = true;
            _textures[file] = t;
            return t.Broken ? null : t;
        }

        private CustomSprite Bank(Tex t, int bank)
        {
            while (t.Banks.Count <= bank)
            {
                string path = t.Source;
                if (t.Banks.Count > 0)
                {
                    Directory.CreateDirectory(_cacheDir);
                    path = Path.Combine(_cacheDir, Path.GetFileNameWithoutExtension(t.Source) + "__" + t.Banks.Count + ".png");
                    if (!File.Exists(path)) File.Copy(t.Source, path, true);
                }
                t.Banks.Add(new CustomSprite(path, new SizeF(1, 1), new PointF(0, 0)));
            }
            return t.Banks[bank];
        }

        private void Emit(Tex t, float x, float y, float w, float h, Color c, float W, float H)
        {
            int bank = t.Count / InstancesPerTexture;
            if (bank >= MaxBanks)
            {
                if (!_warnedBanks) { _warnedBanks = true; Log.Warn("Arabic text: a glyph was needed more than " + (MaxBanks * InstancesPerTexture) + " times in one frame; extra copies skipped."); }
                return;
            }
            CustomSprite s;
            try { s = Bank(t, bank); }
            catch (Exception ex)
            {
                t.Broken = true;
                Log.Warn("Arabic text: texture " + Path.GetFileName(t.Source) + " failed: " + ex.Message);
                return;
            }
            t.Count++;
            // CustomSprite works in a 1280x720 reference space.
            s.Position = new PointF(x / W * Screen.Width, y / H * Screen.Height);
            s.Size = new SizeF(w / W * Screen.Width, h / H * Screen.Height);
            s.Color = c;
            s.Draw();
        }

        /// <summary>Draws everything queued this frame (call once per tick, after all UI drawing).</summary>
        public void Flush(float W, float H)
        {
            int drawn = 0;
            if (Available)
            {
                foreach (var it in _items)
                {
                    _hits.Clear();
                    for (int o = 0; o < _occluders.Count; o++)
                    {
                        var oc = _occluders[o];
                        if (oc.Seq <= it.Seq) continue;
                        if (oc.X >= it.X1 || oc.X + oc.W <= it.X0 || oc.Y >= it.Y1 || oc.Y + oc.H <= it.Y0) continue;
                        _hits.Add(o);
                    }
                    for (int q = it.First; q < it.First + it.Count; q++)
                    {
                        var quad = _quads[q];
                        if (_hits.Count > 0)
                        {
                            float cx = quad.X + quad.W / 2f, cy = quad.Y + quad.H / 2f;
                            bool hidden = false;
                            foreach (int o in _hits)
                            {
                                var oc = _occluders[o];
                                if (cx >= oc.X && cx <= oc.X + oc.W && cy >= oc.Y && cy <= oc.Y + oc.H) { hidden = true; break; }
                            }
                            if (hidden) continue;
                        }
                        Emit(quad.Tex, quad.X, quad.Y, quad.W, quad.H, quad.C, W, H);
                        drawn++;
                    }
                }
            }

            // Overwrite instances that were visible last frame but are unused now (ghost text).
            foreach (var t in _textures.Values)
            {
                if (t.Broken || t.Banks.Count == 0) { t.LastCount = t.Count = 0; continue; }
                for (int k = t.Count; k < t.LastCount; k++)
                {
                    int bank = k / InstancesPerTexture;
                    if (bank >= t.Banks.Count) break;
                    var s = t.Banks[bank];
                    s.Position = new PointF(-10f, -10f);
                    s.Size = new SizeF(0.1f, 0.1f);
                    s.Color = Color.FromArgb(0, 0, 0, 0);
                    s.Draw();
                }
                t.LastCount = t.Count;
                t.Count = 0;
            }
            LastFrameGlyphs = drawn;
            _quads.Clear();
            _items.Clear();
            _occluders.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace OrganizationEmpire.Core.Localization
{
    /// <summary>Bitmap rectangle of one glyph texture, relative to the pen position on the baseline, in em units (y grows downwards).</summary>
    public struct GlyphBox
    {
        public float Left, Top, Width, Height;
        public bool Valid => Width > 0f && Height > 0f;
    }

    public sealed class GlyphInfo
    {
        public char Code;
        public float Advance;
        /// <summary>Texture variants: index 0 = small (body text), 1 = large (headings).</summary>
        public readonly GlyphBox[] Boxes = new GlyphBox[2];
    }

    public struct PlacedGlyph
    {
        public GlyphInfo Glyph;
        /// <summary>Pen x in em units from the left edge of the line.</summary>
        public float X;
    }

    public sealed class LineLayout
    {
        public static readonly LineLayout Empty = new LineLayout { Glyphs = new PlacedGlyph[0] };
        public PlacedGlyph[] Glyphs;
        /// <summary>Total advance in em units.</summary>
        public float Width;
    }

    /// <summary>
    /// Metrics of one pre-rendered glyph set (see tools/glyphgen). The renderer multiplies every
    /// value by the em size in pixels; the texture file of a glyph is "{Weight}{variant}_{code:X4}.png".
    /// </summary>
    public sealed class GlyphFont
    {
        public string Weight = "r";
        public readonly float[] VariantEmPx = { 28f, 56f };
        public float Ascent = 0.95f, Descent = 0.45f;
        public float SpaceAdvance = 0.25f;
        private readonly Dictionary<char, GlyphInfo> _glyphs = new Dictionary<char, GlyphInfo>();

        public int Count => _glyphs.Count;
        public bool Has(char c) => _glyphs.ContainsKey(c);

        public GlyphInfo Get(char c) => _glyphs.TryGetValue(c, out var g) ? g : null;

        public static string FileName(string weight, int variant, char code) => weight + (variant == 0 ? "s" : "l") + "_" + ((int)code).ToString("X4", CultureInfo.InvariantCulture) + ".png";

        public void Set(GlyphInfo g) => _glyphs[g.Code] = g;

        /// <summary>
        /// Lays out one line (no wrapping): shapes Arabic, applies bidi, then places glyphs left to right.
        /// Characters without a glyph fall back to '?' so missing coverage is visible rather than silent.
        /// </summary>
        public LineLayout Layout(string logical, bool rtlParagraph)
        {
            if (string.IsNullOrEmpty(logical)) return LineLayout.Empty;
            string visual = ArabicText.ToVisual(logical.Replace('\t', ' '), rtlParagraph);
            var list = new List<PlacedGlyph>(visual.Length);
            float pen = 0f;
            GlyphInfo fallback = Get('?');
            foreach (char c in visual)
            {
                if (c == ' ' || c == ' ') { pen += SpaceAdvance; continue; }
                if (c < ' ' || ArabicText.IsMark(c)) continue;
                var g = Get(c) ?? fallback;
                if (g == null) { pen += SpaceAdvance; continue; }
                list.Add(new PlacedGlyph { Glyph = g, X = pen });
                pen += g.Advance;
            }
            return new LineLayout { Glyphs = list.ToArray(), Width = pen };
        }

        /// <summary>
        /// Parses glyphs.txt. Lines:
        ///   font &lt;weight&gt; &lt;emSmallPx&gt; &lt;emLargePx&gt; &lt;ascent&gt; &lt;descent&gt; &lt;space&gt;
        ///   g &lt;weight&gt; &lt;hex&gt; &lt;advance&gt; &lt;sL&gt; &lt;sT&gt; &lt;sW&gt; &lt;sH&gt; &lt;lL&gt; &lt;lT&gt; &lt;lW&gt; &lt;lH&gt;
        /// All numbers except the em sizes are in em units.
        /// </summary>
        public static Dictionary<string, GlyphFont> ParseSet(string text, List<string> problems = null)
        {
            var fonts = new Dictionary<string, GlyphFont>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return fonts;
            int lineNo = 0;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                lineNo++;
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    if (p[0] == "font" && p.Length >= 7)
                    {
                        var f = Font(fonts, p[1]);
                        f.VariantEmPx[0] = Num(p[2]);
                        f.VariantEmPx[1] = Num(p[3]);
                        f.Ascent = Num(p[4]);
                        f.Descent = Num(p[5]);
                        f.SpaceAdvance = Num(p[6]);
                    }
                    else if (p[0] == "g" && p.Length >= 12)
                    {
                        var f = Font(fonts, p[1]);
                        var g = new GlyphInfo
                        {
                            Code = (char)int.Parse(p[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                            Advance = Num(p[3]),
                        };
                        g.Boxes[0] = new GlyphBox { Left = Num(p[4]), Top = Num(p[5]), Width = Num(p[6]), Height = Num(p[7]) };
                        g.Boxes[1] = new GlyphBox { Left = Num(p[8]), Top = Num(p[9]), Width = Num(p[10]), Height = Num(p[11]) };
                        f.Set(g);
                    }
                    else problems?.Add("line " + lineNo + ": unknown record");
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException)
                {
                    problems?.Add("line " + lineNo + ": " + ex.Message);
                }
            }
            return fonts;
        }

        private static GlyphFont Font(Dictionary<string, GlyphFont> fonts, string weight)
        {
            if (!fonts.TryGetValue(weight, out var f)) fonts[weight] = f = new GlyphFont { Weight = weight };
            return f;
        }

        private static float Num(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

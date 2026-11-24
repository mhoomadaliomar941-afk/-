using System;
using System.Collections.Generic;
using System.Text;

namespace OrganizationEmpire.Core.Localization
{
    /// <summary>
    /// Arabic text preparation for renderers that draw glyphs one by one in a left-to-right pass
    /// (GTA V's text engine has no Arabic support, so the mod draws glyph textures itself).
    ///
    /// <see cref="Shape"/> picks the contextual form of each letter (isolated / final / initial /
    /// medial, plus the lam-alef ligatures) using the Unicode Presentation Forms, and
    /// <see cref="Reorder"/> applies the Unicode bidirectional algorithm for a single paragraph
    /// without explicit embeddings: weak/neutral resolution, implicit levels, run reversal and
    /// bracket mirroring. Numbers and Latin words inside Arabic sentences keep their reading order.
    /// </summary>
    public static class ArabicText
    {
        // ------------------------------------------------------------------ character classes

        public static bool IsArabicChar(char c) =>
            (c >= '؀' && c <= 'ۿ') || (c >= 'ݐ' && c <= 'ݿ') || (c >= 'ࢠ' && c <= 'ࣿ') ||
            (c >= 'ﭐ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿');

        public static bool ContainsArabic(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (IsArabicChar(c)) return true;
            return false;
        }

        /// <summary>Harakat and other marks that the glyph renderer does not position; they are dropped.</summary>
        public static bool IsMark(char c) =>
            (c >= 'ً' && c <= 'ٟ') || c == 'ٰ' || (c >= 'ۖ' && c <= 'ۭ') || c == '‌' || c == '‍' || c == '‎' || c == '‏';

        // ------------------------------------------------------------------ shaping

        private enum Joining { None, Right, Dual, Causing }

        // isolated, final, initial, medial (0 = form does not exist)
        private static readonly Dictionary<char, char[]> Forms = new Dictionary<char, char[]>
        {
            ['ء'] = new[] { 'ﺀ', '\0', '\0', '\0' },
            ['آ'] = new[] { 'ﺁ', 'ﺂ', '\0', '\0' },
            ['أ'] = new[] { 'ﺃ', 'ﺄ', '\0', '\0' },
            ['ؤ'] = new[] { 'ﺅ', 'ﺆ', '\0', '\0' },
            ['إ'] = new[] { 'ﺇ', 'ﺈ', '\0', '\0' },
            ['ئ'] = new[] { 'ﺉ', 'ﺊ', 'ﺋ', 'ﺌ' },
            ['ا'] = new[] { 'ﺍ', 'ﺎ', '\0', '\0' },
            ['ب'] = new[] { 'ﺏ', 'ﺐ', 'ﺑ', 'ﺒ' },
            ['ة'] = new[] { 'ﺓ', 'ﺔ', '\0', '\0' },
            ['ت'] = new[] { 'ﺕ', 'ﺖ', 'ﺗ', 'ﺘ' },
            ['ث'] = new[] { 'ﺙ', 'ﺚ', 'ﺛ', 'ﺜ' },
            ['ج'] = new[] { 'ﺝ', 'ﺞ', 'ﺟ', 'ﺠ' },
            ['ح'] = new[] { 'ﺡ', 'ﺢ', 'ﺣ', 'ﺤ' },
            ['خ'] = new[] { 'ﺥ', 'ﺦ', 'ﺧ', 'ﺨ' },
            ['د'] = new[] { 'ﺩ', 'ﺪ', '\0', '\0' },
            ['ذ'] = new[] { 'ﺫ', 'ﺬ', '\0', '\0' },
            ['ر'] = new[] { 'ﺭ', 'ﺮ', '\0', '\0' },
            ['ز'] = new[] { 'ﺯ', 'ﺰ', '\0', '\0' },
            ['س'] = new[] { 'ﺱ', 'ﺲ', 'ﺳ', 'ﺴ' },
            ['ش'] = new[] { 'ﺵ', 'ﺶ', 'ﺷ', 'ﺸ' },
            ['ص'] = new[] { 'ﺹ', 'ﺺ', 'ﺻ', 'ﺼ' },
            ['ض'] = new[] { 'ﺽ', 'ﺾ', 'ﺿ', 'ﻀ' },
            ['ط'] = new[] { 'ﻁ', 'ﻂ', 'ﻃ', 'ﻄ' },
            ['ظ'] = new[] { 'ﻅ', 'ﻆ', 'ﻇ', 'ﻈ' },
            ['ع'] = new[] { 'ﻉ', 'ﻊ', 'ﻋ', 'ﻌ' },
            ['غ'] = new[] { 'ﻍ', 'ﻎ', 'ﻏ', 'ﻐ' },
            ['ف'] = new[] { 'ﻑ', 'ﻒ', 'ﻓ', 'ﻔ' },
            ['ق'] = new[] { 'ﻕ', 'ﻖ', 'ﻗ', 'ﻘ' },
            ['ك'] = new[] { 'ﻙ', 'ﻚ', 'ﻛ', 'ﻜ' },
            ['ل'] = new[] { 'ﻝ', 'ﻞ', 'ﻟ', 'ﻠ' },
            ['م'] = new[] { 'ﻡ', 'ﻢ', 'ﻣ', 'ﻤ' },
            ['ن'] = new[] { 'ﻥ', 'ﻦ', 'ﻧ', 'ﻨ' },
            ['ه'] = new[] { 'ﻩ', 'ﻪ', 'ﻫ', 'ﻬ' },
            ['و'] = new[] { 'ﻭ', 'ﻮ', '\0', '\0' },
            ['ى'] = new[] { 'ﻯ', 'ﻰ', 'ﯨ', 'ﯩ' },
            ['ي'] = new[] { 'ﻱ', 'ﻲ', 'ﻳ', 'ﻴ' },
            ['پ'] = new[] { 'ﭖ', 'ﭗ', 'ﭘ', 'ﭙ' },
            ['چ'] = new[] { 'ﭺ', 'ﭻ', 'ﭼ', 'ﭽ' },
            ['ژ'] = new[] { 'ﮊ', 'ﮋ', '\0', '\0' },
            ['ڤ'] = new[] { 'ﭪ', 'ﭫ', 'ﭬ', 'ﭭ' },
            ['ک'] = new[] { 'ﮎ', 'ﮏ', 'ﮐ', 'ﮑ' },
            ['گ'] = new[] { 'ﮒ', 'ﮓ', 'ﮔ', 'ﮕ' },
            ['ی'] = new[] { 'ﯼ', 'ﯽ', 'ﯾ', 'ﯿ' },
        };

        private static readonly Dictionary<char, char[]> LamAlef = new Dictionary<char, char[]>
        {
            ['آ'] = new[] { 'ﻵ', 'ﻶ' },
            ['أ'] = new[] { 'ﻷ', 'ﻸ' },
            ['إ'] = new[] { 'ﻹ', 'ﻺ' },
            ['ا'] = new[] { 'ﻻ', 'ﻼ' },
        };

        /// <summary>All presentation-form characters the shaper can produce (used to build glyph sets).</summary>
        public static IEnumerable<char> AllShapedForms()
        {
            foreach (var kv in Forms) foreach (char f in kv.Value) if (f != '\0') yield return f;
            foreach (var kv in LamAlef) foreach (char f in kv.Value) yield return f;
        }

        /// <summary>For glyph generation: base letter and form index (0 iso, 1 final, 2 initial, 3 medial) of a presentation form, or the lam-alef pair.</summary>
        public static bool TryDescribeForm(char shaped, out string baseText, out int form)
        {
            foreach (var kv in Forms)
                for (int i = 0; i < 4; i++)
                    if (kv.Value[i] == shaped) { baseText = kv.Key.ToString(); form = i; return true; }
            foreach (var kv in LamAlef)
                for (int i = 0; i < 2; i++)
                    if (kv.Value[i] == shaped) { baseText = "ل" + kv.Key; form = i; return true; }
            baseText = null; form = -1;
            return false;
        }

        private static Joining JoiningOf(char c)
        {
            if (c == 'ـ') return Joining.Causing; // tatweel
            if (Forms.TryGetValue(c, out var f)) return f[2] != '\0' ? Joining.Dual : (f[1] != '\0' ? Joining.Right : Joining.None);
            return Joining.None;
        }

        private static bool JoinsForward(Joining j) => j == Joining.Dual || j == Joining.Causing;
        private static bool JoinsBackward(Joining j) => j == Joining.Dual || j == Joining.Right || j == Joining.Causing;

        /// <summary>Replaces Arabic letters with their contextual presentation forms (logical order is kept). Marks are removed.</summary>
        public static string Shape(string s)
        {
            if (string.IsNullOrEmpty(s) || !ContainsArabic(s)) return s ?? "";
            // Drop marks first so they never break joining.
            var chars = new List<char>(s.Length);
            foreach (char c in s) if (!IsMark(c)) chars.Add(c);

            var sb = new StringBuilder(chars.Count);
            for (int i = 0; i < chars.Count; i++)
            {
                char c = chars[i];
                var j = JoiningOf(c);
                if (j == Joining.None && c != 'ل')
                {
                    sb.Append(Forms.TryGetValue(c, out var iso) ? iso[0] : c);
                    continue;
                }
                bool prevJoins = i > 0 && JoinsForward(JoiningOf(chars[i - 1])) && JoinsBackward(j);

                // Lam followed by an alef becomes one ligature that only joins backwards.
                if (c == 'ل' && i + 1 < chars.Count && LamAlef.TryGetValue(chars[i + 1], out var lig))
                {
                    sb.Append(prevJoins ? lig[1] : lig[0]);
                    i++;
                    continue;
                }
                if (j == Joining.Causing) { sb.Append(c); continue; }

                bool nextJoins = i + 1 < chars.Count && JoinsForward(j) && JoinsBackward(JoiningOf(chars[i + 1]));
                var forms = Forms[c];
                char outC;
                if (prevJoins && nextJoins) outC = forms[3];
                else if (prevJoins) outC = forms[1];
                else if (nextJoins) outC = forms[2];
                else outC = forms[0];
                sb.Append(outC != '\0' ? outC : forms[0]);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ bidi

        private enum Bc : byte { L, R, AL, EN, AN, ES, ET, CS, WS, ON }

        private static Bc ClassOf(char c)
        {
            if (c >= '0' && c <= '9') return Bc.EN;
            if ((c >= '٠' && c <= '٩') || c == '٫' || c == '٬') return Bc.AN;
            if (c >= '۰' && c <= '۹') return Bc.EN;
            if (IsArabicChar(c))
            {
                if (c == '،' || c == '؛' || c == '؟') return Bc.AL; // Arabic punctuation behaves as AL
                return Bc.AL;
            }
            if (c >= '֐' && c <= '׿') return Bc.R;
            if (c == ' ' || c == '\t' || c == ' ' || c == ' ' || c == ' ') return c == ' ' ? Bc.CS : Bc.WS;
            if (c == '+' || c == '-' || c == '−') return Bc.ES;
            if (c == '#' || c == '$' || c == '%' || c == '°' || c == '¢' || c == '£' || c == '¥' || c == '€' || c == '‰') return Bc.ET;
            if (c == ',' || c == '.' || c == '/' || c == ':') return Bc.CS;
            if (char.IsLetter(c)) return Bc.L;
            return Bc.ON;
        }

        private static bool IsStrongRtl(Bc b) => b == Bc.R || b == Bc.AL;

        /// <summary>Direction a paragraph would get from its first strong character (rule P2/P3).</summary>
        public static bool FirstStrongIsRtl(string s)
        {
            if (s == null) return false;
            foreach (char c in s)
            {
                var b = ClassOf(c);
                if (b == Bc.L) return false;
                if (IsStrongRtl(b)) return true;
            }
            return false;
        }

        private static readonly Dictionary<char, char> Mirror = new Dictionary<char, char>
        {
            ['('] = ')', [')'] = '(', ['['] = ']', [']'] = '[', ['{'] = '}', ['}'] = '{',
            ['<'] = '>', ['>'] = '<', ['«'] = '»', ['»'] = '«', ['‹'] = '›', ['›'] = '‹',
        };

        /// <summary>
        /// Converts one line of (already shaped) text from logical to visual order, i.e. the order
        /// in which glyphs must be laid out from left to right.
        /// </summary>
        public static string Reorder(string s, bool rtlParagraph)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            int n = s.Length;
            var t = new Bc[n];
            for (int i = 0; i < n; i++) t[i] = ClassOf(s[i]);
            var orig = (Bc[])t.Clone();
            Bc sos = rtlParagraph ? Bc.R : Bc.L;

            // W2 deliberately skipped: the standard turns Western digits after Arabic letters into
            // "Arabic numbers", which detaches currency/percent signs ("$1,250" would show as "1,250$").
            // Keeping them European numbers makes amounts read naturally in the menus.
            // W3: AL -> R.
            Bc lastStrong;
            for (int i = 0; i < n; i++) if (t[i] == Bc.AL) t[i] = Bc.R;

            // W4: single separator between numbers of the same kind.
            for (int i = 1; i < n - 1; i++)
            {
                if (t[i] == Bc.ES && t[i - 1] == Bc.EN && t[i + 1] == Bc.EN) t[i] = Bc.EN;
                else if (t[i] == Bc.CS && t[i - 1] == Bc.EN && t[i + 1] == Bc.EN) t[i] = Bc.EN;
                else if (t[i] == Bc.CS && t[i - 1] == Bc.AN && t[i + 1] == Bc.AN) t[i] = Bc.AN;
            }
            // W5: terminators next to European numbers.
            for (int i = 0; i < n; i++)
            {
                if (t[i] != Bc.ET) continue;
                int j = i;
                while (j < n && t[j] == Bc.ET) j++;
                bool adj = (i > 0 && t[i - 1] == Bc.EN) || (j < n && t[j] == Bc.EN);
                if (adj) for (int k = i; k < j; k++) t[k] = Bc.EN;
                i = j - 1;
            }
            // W6: leftover separators/terminators are neutral.
            for (int i = 0; i < n; i++) if (t[i] == Bc.ES || t[i] == Bc.ET || t[i] == Bc.CS) t[i] = Bc.ON;
            // W7: EN after L (or at an LTR start) behaves as L.
            lastStrong = sos;
            for (int i = 0; i < n; i++)
            {
                if (t[i] == Bc.L || t[i] == Bc.R) lastStrong = t[i];
                else if (t[i] == Bc.EN && lastStrong == Bc.L) t[i] = Bc.L;
            }

            // N1/N2: neutral runs take the surrounding direction when both sides agree, else the paragraph's.
            for (int i = 0; i < n; i++)
            {
                if (t[i] != Bc.ON && t[i] != Bc.WS) continue;
                int j = i;
                while (j < n && (t[j] == Bc.ON || t[j] == Bc.WS)) j++;
                Bc before = i == 0 ? sos : Strongish(t[i - 1]);
                Bc after = j == n ? sos : Strongish(t[j]);
                Bc dir = before == after ? before : sos;
                for (int k = i; k < j; k++) t[k] = dir;
                i = j - 1;
            }

            // I1/I2: implicit levels.
            int baseLevel = rtlParagraph ? 1 : 0;
            var lv = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (baseLevel == 0) lv[i] = t[i] == Bc.R ? 1 : (t[i] == Bc.AN || t[i] == Bc.EN) ? 2 : 0;
                else lv[i] = t[i] == Bc.L || t[i] == Bc.EN || t[i] == Bc.AN ? 2 : 1;
            }
            // L1: trailing whitespace goes back to the paragraph level.
            for (int i = n - 1; i >= 0 && orig[i] == Bc.WS; i--) lv[i] = baseLevel;

            // L2: reverse runs from the highest level down to the lowest odd level.
            var chars = s.ToCharArray();
            int max = 0, minOdd = int.MaxValue;
            foreach (int l in lv) { if (l > max) max = l; if ((l & 1) == 1 && l < minOdd) minOdd = l; }
            if (minOdd == int.MaxValue) minOdd = max + 1;
            // L4: mirror brackets on RTL levels (before reversing, using the resolved level).
            for (int i = 0; i < n; i++) if ((lv[i] & 1) == 1 && Mirror.TryGetValue(chars[i], out char m)) chars[i] = m;
            for (int level = max; level >= minOdd; level--)
            {
                for (int i = 0; i < n; i++)
                {
                    if (lv[i] < level) continue;
                    int j = i;
                    while (j < n && lv[j] >= level) j++;
                    Array.Reverse(chars, i, j - i);
                    Array.Reverse(lv, i, j - i);
                    i = j - 1;
                }
            }
            return new string(chars);
        }

        private static Bc Strongish(Bc b) => b == Bc.EN || b == Bc.AN ? Bc.R : b;

        /// <summary>Shape + reorder one line. Paragraph direction: forced RTL when <paramref name="rtl"/> is true, else from the first strong character.</summary>
        public static string ToVisual(string line, bool? rtl = null)
        {
            if (string.IsNullOrEmpty(line)) return line ?? "";
            string shaped = Shape(line);
            bool dir = rtl ?? FirstStrongIsRtl(shaped);
            return Reorder(shaped, dir);
        }
    }
}

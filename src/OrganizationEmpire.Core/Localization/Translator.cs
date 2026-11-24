using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OrganizationEmpire.Core.Localization
{
    /// <summary>
    /// Translates display strings at the moment they are drawn. English text is the key, so the
    /// game logic never has to know which language is active, and anything without a translation
    /// simply stays English.
    ///
    /// Lookup order for a string:
    ///   1. exact entry (case-insensitive, so "MEMBERS" finds "Members");
    ///   2. the same with surrounding whitespace / trailing punctuation peeled off;
    ///   3. a template entry such as "{0} members ({1} deployed)", whose captured values are
    ///      translated recursively (so enum names inside sentences are translated too);
    ///   4. splitting on UI separators (" | ", " · ", ", ", line breaks, "Label: value") and
    ///      translating the pieces.
    /// Results are cached; the cache is bounded because dynamic strings (money, timers) are unique.
    /// </summary>
    public sealed class Translator
    {
        private sealed class Template
        {
            public string Key;
            public string Value;
            public Regex Pattern;
            public string Anchor;
            public int LiteralLength;
            public int Order;
        }

        private static readonly Regex PlaceholderRx = new Regex(@"\{(\d+)\}", RegexOptions.CultureInvariant);
        private static readonly string[] Separators = { "\n", "  |  ", " | ", " · ", " • ", " — ", " -- ", " - ", "   ", " / ", ", " };

        private readonly Dictionary<string, string> _exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Template> _templates = new List<Template>();
        private readonly Dictionary<string, Template> _templateByKey = new Dictionary<string, Template>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _sorted = true;

        public const int MaxCache = 6000;

        public int ExactCount => _exact.Count;
        public int TemplateCount => _templates.Count;

        /// <summary>Called for each target-language character class test; Arabic by default.</summary>
        public Func<string, bool> IsAlreadyTranslated = ArabicText.ContainsArabic;

        public bool Has(string english) => english != null && (_exact.ContainsKey(english) || _templateByKey.ContainsKey(english));

        public IEnumerable<string> Keys
        {
            get
            {
                foreach (var k in _exact.Keys) yield return k;
                foreach (var t in _templates) yield return t.Key;
            }
        }

        /// <summary>
        /// Loads "English&lt;TAB&gt;Translation" lines. '#' starts a comment line, "\n" and "\t" are escapes.
        /// Later entries replace earlier ones, so a user file can override the built-in table.
        /// Returns the number of entries read; malformed lines are reported through <paramref name="problems"/>.
        /// </summary>
        public int Load(string text, List<string> problems = null)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int n = 0, lineNo = 0;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                lineNo++;
                if (raw.Length == 0 || raw[0] == '#') continue;
                int tab = raw.IndexOf('\t');
                if (tab <= 0 || tab == raw.Length - 1)
                {
                    if (raw.Trim().Length > 0) problems?.Add("line " + lineNo + ": expected English<TAB>translation");
                    continue;
                }
                string key = Unescape(raw.Substring(0, tab));
                string value = Unescape(raw.Substring(tab + 1).TrimEnd('\t'));
                if (key.Trim().Length == 0 || value.Trim().Length == 0) continue;
                string error = Add(key, value);
                if (error != null) problems?.Add("line " + lineNo + ": " + error);
                else n++;
            }
            return n;
        }

        /// <summary>Adds or replaces one entry. Returns an error message for an unusable entry, else null.</summary>
        public string Add(string english, string translated)
        {
            if (string.IsNullOrEmpty(english) || translated == null) return "empty entry";
            _cache.Clear();
            var keyHoles = PlaceholderSet(english);
            if (keyHoles.Count == 0)
            {
                _exact[english] = translated;
                return null;
            }
            var valueHoles = PlaceholderSet(translated);
            foreach (int h in valueHoles)
                if (!keyHoles.Contains(h)) return "translation uses {" + h + "} which the English text does not have";

            // Build an anchored pattern: literal pieces escaped, holes captured.
            var sb = new StringBuilder("^");
            int pos = 0, literal = 0, holeCount = 0;
            string anchor = "";
            var holeOrder = new List<int>();
            bool lastWasHole = false;
            foreach (Match m in PlaceholderRx.Matches(english))
            {
                string lit = english.Substring(pos, m.Index - pos);
                if (lit.Length == 0 && lastWasHole) return "two placeholders next to each other are ambiguous";
                sb.Append(Regex.Escape(lit));
                literal += CountLetters(lit);
                if (lit.Trim().Length > anchor.Length) anchor = lit.Trim();
                holeCount++;
                holeOrder.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                sb.Append("(?<h").Append(holeOrder[holeOrder.Count - 1]).Append(">.+?)");
                pos = m.Index + m.Length;
                lastWasHole = true;
            }
            string tail = english.Substring(pos);
            sb.Append(Regex.Escape(tail));
            literal += CountLetters(tail);
            if (tail.Trim().Length > anchor.Length) anchor = tail.Trim();
            sb.Append('$');
            if (literal < 2) return "template needs some words besides placeholders";
            if (new HashSet<int>(holeOrder).Count != holeOrder.Count) return "a placeholder appears twice in the English text";

            Regex rx;
            try { rx = new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant); }
            catch (ArgumentException ex) { return "bad template: " + ex.Message; }

            var t = new Template { Key = english, Value = translated, Pattern = rx, Anchor = anchor, LiteralLength = literal };
            if (_templateByKey.TryGetValue(english, out var old)) _templates.Remove(old);
            t.Order = _templates.Count;
            _templates.Add(t);
            _templateByKey[english] = t;
            _sorted = false;
            return null;
        }

        public void Clear()
        {
            _exact.Clear();
            _templates.Clear();
            _templateByKey.Clear();
            _cache.Clear();
        }

        public string Translate(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (_cache.TryGetValue(s, out string hit)) return hit;
            // Fast exits that are not cached: wrapped/ellipsized pieces of already translated text, numbers.
            if (!HasLetter(s) || (IsAlreadyTranslated != null && IsAlreadyTranslated(s))) return s;
            if (!_sorted)
            {
                // Most specific (most literal text) first so "{0} members deployed" beats "{0} members".
                _templates.Sort((a, b) => a.LiteralLength != b.LiteralLength ? b.LiteralLength.CompareTo(a.LiteralLength) : a.Order.CompareTo(b.Order));
                _sorted = true;
            }
            string result = TranslateCore(s, 0) ?? s;
            if (_cache.Count >= MaxCache) _cache.Clear();
            _cache[s] = result;
            return result;
        }

        /// <summary>Returns the translation, or null when nothing applied.</summary>
        private string TranslateCore(string s, int depth)
        {
            if (s.Length == 0 || depth > 4) return null;
            if (IsAlreadyTranslated != null && IsAlreadyTranslated(s)) return null;
            if (!HasLetter(s)) return null;

            if (_exact.TryGetValue(s, out string exact)) return exact;
            // Raw enum names ("DefendPlayer") are looked up in their humanized form ("Defend Player").
            if (s.Length > 3 && s.IndexOf(' ') < 0 && char.IsUpper(s[0]))
            {
                string human = Core.Util.TextUtil.Humanize(s);
                if (human.Length != s.Length && _exact.TryGetValue(human, out string viaHuman)) return viaHuman;
            }

            // Peel whitespace and decoration, translate the core, put the decoration back.
            int a = 0, b = s.Length;
            while (a < b && (char.IsWhiteSpace(s[a]) || IsLeadDecoration(s[a]))) a++;
            while (b > a && (char.IsWhiteSpace(s[b - 1]) || IsTrailPunctuation(s[b - 1]))) b--;
            if (a > 0 || b < s.Length)
            {
                string core = s.Substring(a, b - a);
                if (core.Length > 0)
                {
                    string t = _exact.TryGetValue(core, out string e) ? e : TranslateTemplates(core, depth);
                    if (t != null) return s.Substring(0, a) + t + ConvertPunctuation(s.Substring(b));
                }
            }

            string viaTemplate = TranslateTemplates(s, depth);
            if (viaTemplate != null) return viaTemplate;

            foreach (string sep in Separators)
            {
                int idx = s.IndexOf(sep, StringComparison.Ordinal);
                if (idx < 0) continue;
                string[] parts = s.Split(new[] { sep }, StringSplitOptions.None);
                bool changed = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i].Length == 0 ? null : TranslateCore(parts[i], depth + 1);
                    if (p != null) { parts[i] = p; changed = true; }
                }
                if (changed) return string.Join(sep == ", " ? "، " : sep, parts);
            }

            // Several sentences: translate each one (dialogue lines are often assembled from parts).
            var sentences = SplitSentences(s);
            if (sentences != null)
            {
                bool changed = false;
                for (int i = 0; i < sentences.Count; i++)
                {
                    string p = TranslateCore(sentences[i], depth + 1);
                    if (p != null) { sentences[i] = p; changed = true; }
                }
                if (changed) return string.Join(" ", sentences);
            }

            // "Label: value" where only the label is a known phrase.
            int colon = s.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0)
            {
                string label = s.Substring(0, colon);
                string rest = s.Substring(colon + 2);
                string tl = _exact.TryGetValue(label.Trim(), out string el) ? el : null;
                if (tl != null)
                {
                    string tr = TranslateCore(rest, depth + 1) ?? rest;
                    return tl + ": " + tr;
                }
            }
            return null;
        }

        private string TranslateTemplates(string s, int depth)
        {
            foreach (var t in _templates)
            {
                if (t.Anchor.Length > 0 && s.IndexOf(t.Anchor, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var m = t.Pattern.Match(s);
                if (!m.Success) continue;
                return PlaceholderRx.Replace(t.Value, hole =>
                {
                    var g = m.Groups["h" + hole.Groups[1].Value];
                    if (!g.Success) return hole.Value;
                    string v = g.Value;
                    string tv = TranslateCore(v, depth + 1);
                    return tv ?? v;
                });
            }
            return null;
        }

        private static List<string> SplitSentences(string s)
        {
            List<string> list = null;
            int start = 0;
            for (int i = 0; i < s.Length - 1; i++)
            {
                char c = s[i];
                if ((c == '.' || c == '!' || c == '?') && s[i + 1] == ' ' && i + 2 < s.Length && !char.IsLower(s[i + 2]))
                {
                    // Not an ellipsis inside a sentence ("Uh... okay") - those are followed by lower case.
                    (list ?? (list = new List<string>())).Add(s.Substring(start, i + 1 - start).Trim());
                    start = i + 2;
                }
            }
            if (list == null) return null;
            string last = s.Substring(start).Trim();
            if (last.Length > 0) list.Add(last);
            return list.Count > 1 ? list : null;
        }

        private static bool IsLeadDecoration(char c) => c == '*' || c == '>' || c == '-' || c == '•' || c == '·' || c == '+';

        private static bool IsTrailPunctuation(char c) => c == ':' || c == '.' || c == '!' || c == '?' || c == ',' || c == ';' || c == '…';

        private static string ConvertPunctuation(string s)
        {
            if (s.IndexOf('?') < 0 && s.IndexOf(',') < 0 && s.IndexOf(';') < 0) return s;
            return s.Replace('?', '\u061F').Replace(',', '\u060C').Replace(';', '\u061B');
        }

        private static bool HasLetter(string s)
        {
            foreach (char c in s) if (char.IsLetter(c)) return true;
            return false;
        }

        private static int CountLetters(string s)
        {
            int n = 0;
            foreach (char c in s) if (char.IsLetter(c)) n++;
            return n;
        }

        private static HashSet<int> PlaceholderSet(string s)
        {
            var set = new HashSet<int>();
            foreach (Match m in PlaceholderRx.Matches(s)) set.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            return set;
        }

        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == 't') { sb.Append('\t'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "");
    }
}

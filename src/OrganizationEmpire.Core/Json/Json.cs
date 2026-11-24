using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OrganizationEmpire.Core.Json
{
    /// <summary>
    /// Minimal, dependency-free JSON reader/writer.
    /// Objects are Dictionary&lt;string, object&gt;, arrays are List&lt;object&gt;,
    /// numbers are long (integral) or double, plus string, bool and null.
    /// Shipping our own JSON avoids Newtonsoft version conflicts with other mods in the GTA scripts folder.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new Parser(text);
            p.SkipWhitespace();
            object value = p.ReadValue(0);
            p.SkipWhitespace();
            if (!p.AtEnd) throw p.Error("Unexpected trailing characters");
            return value;
        }

        public static bool TryParse(string text, out object value, out string error)
        {
            try
            {
                value = Parse(text);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                value = null;
                error = ex.Message;
                return false;
            }
        }

        public static string Stringify(object value, bool indented = false)
        {
            var sb = new StringBuilder(256);
            Write(sb, value, indented, 0);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ helpers for reading trees

        public static Dictionary<string, object> AsObject(object node) => node as Dictionary<string, object>;
        public static List<object> AsArray(object node) => node as List<object>;

        public static object Get(object node, string key)
        {
            var obj = node as Dictionary<string, object>;
            if (obj == null) return null;
            obj.TryGetValue(key, out object v);
            return v;
        }

        public static string GetString(object node, string key, string fallback = null)
        {
            var v = Get(node, key);
            return v as string ?? fallback;
        }

        public static double GetNumber(object node, string key, double fallback = 0)
        {
            var v = Get(node, key);
            if (v is long l) return l;
            if (v is double d) return d;
            return fallback;
        }

        // ------------------------------------------------------------------ writer

        private static void Write(StringBuilder sb, object value, bool indented, int depth)
        {
            if (depth > 128) throw new InvalidOperationException("JSON nesting too deep");
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    return;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    return;
                case double d:
                    WriteDouble(sb, d);
                    return;
                case float f:
                    WriteDouble(sb, f);
                    return;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    return;
                case Dictionary<string, object> obj:
                    WriteObject(sb, obj, indented, depth);
                    return;
                case List<object> arr:
                    WriteArray(sb, arr, indented, depth);
                    return;
                default:
                    if (value is IConvertible conv)
                    {
                        // other integral types
                        try
                        {
                            sb.Append(Convert.ToInt64(conv, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                            return;
                        }
                        catch { /* fall through */ }
                    }
                    WriteString(sb, value.ToString());
                    return;
            }
        }

        private static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append('0'); return; }
            string s = d.ToString("R", CultureInfo.InvariantCulture);
            sb.Append(s);
        }

        private static void WriteObject(StringBuilder sb, Dictionary<string, object> obj, bool indented, int depth)
        {
            if (obj.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            bool first = true;
            foreach (var kv in obj)
            {
                if (!first) sb.Append(',');
                first = false;
                if (indented) { sb.Append('\n'); Indent(sb, depth + 1); }
                WriteString(sb, kv.Key);
                sb.Append(indented ? ": " : ":");
                Write(sb, kv.Value, indented, depth + 1);
            }
            if (indented) { sb.Append('\n'); Indent(sb, depth); }
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, List<object> arr, bool indented, int depth)
        {
            if (arr.Count == 0) { sb.Append("[]"); return; }
            sb.Append('[');
            for (int i = 0; i < arr.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (indented) { sb.Append('\n'); Indent(sb, depth + 1); }
                Write(sb, arr[i], indented, depth + 1);
            }
            if (indented) { sb.Append('\n'); Indent(sb, depth); }
            sb.Append(']');
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            sb.Append(' ', depth * 2);
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || c == '\u2028' || c == '\u2029')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            WriteString(sb, s);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ parser

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s) { _s = s; }

            public bool AtEnd => _i >= _s.Length;

            public FormatException Error(string msg)
            {
                return new FormatException(msg + " at position " + _i.ToString(CultureInfo.InvariantCulture));
            }

            public void SkipWhitespace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') { _i++; continue; }
                    break;
                }
            }

            public object ReadValue(int depth)
            {
                if (depth > 256) throw Error("JSON nesting too deep");
                SkipWhitespace();
                if (AtEnd) throw Error("Unexpected end of input");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error("Expected '" + word + "'");
                _i += word.Length;
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var obj = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++; // {
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}') { _i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"') throw Error("Expected property name");
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':') throw Error("Expected ':'");
                    _i++;
                    object val = ReadValue(depth + 1);
                    obj[key] = val;
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated object");
                    char c = _s[_i++];
                    if (c == ',') continue;
                    if (c == '}') return obj;
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ReadArray(int depth)
            {
                var arr = new List<object>();
                _i++; // [
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']') { _i++; return arr; }
                while (true)
                {
                    arr.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unterminated array");
                    char c = _s[_i++];
                    if (c == ',') continue;
                    if (c == ']') return arr;
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("Unterminated escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("Bad unicode escape");
                            string hex = _s.Substring(_i, 4);
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw Error("Bad unicode escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            throw Error("Bad escape '\\" + e + "'");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = _i;
                bool isFloat = false;
                if (_s[_i] == '-') _i++;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c >= '0' && c <= '9') { _i++; continue; }
                    if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { isFloat = true; _i++; continue; }
                    break;
                }
                string token = _s.Substring(start, _i - start);
                if (!isFloat && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l))
                    return l;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    return d;
                throw Error("Invalid number '" + token + "'");
            }
        }
    }
}

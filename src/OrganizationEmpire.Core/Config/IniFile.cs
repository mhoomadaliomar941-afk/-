using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace OrganizationEmpire.Core.Config
{
    /// <summary>Documentation written above a key in the generated INI file.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ConfigCommentAttribute : Attribute
    {
        public string Text { get; }
        public ConfigCommentAttribute(string text) { Text = text; }
    }

    /// <summary>Very small INI reader/writer (sections, key=value, ';' or '#' comments).</summary>
    public sealed class IniFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public static IniFile Parse(string text)
        {
            var ini = new IniFile();
            string section = "";
            foreach (string raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line.EndsWith("]"))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                ini.Set(section, key, value);
            }
            return ini;
        }

        public void Set(string section, string key, string value)
        {
            if (!_sections.TryGetValue(section, out var dict))
            {
                dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _sections[section] = dict;
            }
            dict[key] = value;
        }

        public string Get(string section, string key, string fallback = null)
        {
            if (_sections.TryGetValue(section, out var dict) && dict.TryGetValue(key, out string v)) return v;
            return fallback;
        }

        public bool Has(string section, string key) => Get(section, key) != null;

        // ------------------------------------------------------------------ reflection binding

        /// <summary>Copies values for every public property of every section object found on <paramref name="root"/>.</summary>
        public List<string> BindTo(object root)
        {
            var warnings = new List<string>();
            foreach (var sectionProp in root.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object section = sectionProp.GetValue(root, null);
                if (section == null || sectionProp.PropertyType.IsPrimitive || sectionProp.PropertyType == typeof(string)) continue;
                foreach (var p in section.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.CanWrite) continue;
                    string raw = Get(sectionProp.Name, p.Name);
                    if (raw == null) continue;
                    if (TryConvert(raw, p.PropertyType, out object value)) p.SetValue(section, value, null);
                    else warnings.Add("[" + sectionProp.Name + "] " + p.Name + "='" + raw + "' is not a valid " + p.PropertyType.Name + "; using default.");
                }
            }
            return warnings;
        }

        public static string Write(object root, string header)
        {
            var sb = new StringBuilder();
            foreach (string h in (header ?? "").Split('\n')) sb.Append("; ").AppendLine(h.TrimEnd());
            sb.AppendLine();
            foreach (var sectionProp in root.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object section = sectionProp.GetValue(root, null);
                if (section == null || sectionProp.PropertyType.IsPrimitive || sectionProp.PropertyType == typeof(string)) continue;
                sb.Append('[').Append(sectionProp.Name).AppendLine("]");
                foreach (var p in section.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.CanRead || !p.CanWrite) continue;
                    var comment = p.GetCustomAttribute<ConfigCommentAttribute>();
                    if (comment != null)
                    {
                        foreach (string c in comment.Text.Split('\n')) sb.Append("; ").AppendLine(c);
                    }
                    if (p.PropertyType.IsEnum)
                    {
                        sb.Append("; Options: ").AppendLine(string.Join(", ", Enum.GetNames(p.PropertyType)));
                    }
                    sb.Append(p.Name).Append(" = ").AppendLine(Format(p.GetValue(section, null)));
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static string Format(object v)
        {
            switch (v)
            {
                case null: return "";
                case bool b: return b ? "true" : "false";
                case float f: return f.ToString("0.###", CultureInfo.InvariantCulture);
                case double d: return d.ToString("0.###", CultureInfo.InvariantCulture);
                case IFormattable fmt: return fmt.ToString(null, CultureInfo.InvariantCulture);
                default: return v.ToString();
            }
        }

        public static bool TryConvert(string raw, Type type, out object value)
        {
            value = null;
            raw = raw?.Trim() ?? "";
            if (type == typeof(string)) { value = raw; return true; }
            if (type == typeof(bool))
            {
                string r = raw.ToLowerInvariant();
                if (r == "true" || r == "1" || r == "yes" || r == "on") { value = true; return true; }
                if (r == "false" || r == "0" || r == "no" || r == "off") { value = false; return true; }
                return false;
            }
            if (type == typeof(int))
            {
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) { value = i; return true; }
                return false;
            }
            if (type == typeof(float))
            {
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) { value = f; return true; }
                return false;
            }
            if (type == typeof(double))
            {
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) { value = d; return true; }
                return false;
            }
            if (type.IsEnum)
            {
                try
                {
                    value = Enum.Parse(type, raw, true);
                    return Enum.IsDefined(type, value);
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }
            return false;
        }
    }
}

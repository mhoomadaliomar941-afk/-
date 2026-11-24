using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Localization
{
    /// <summary>
    /// Active interface language. Code keeps producing English; the renderer passes every string
    /// through <see cref="T"/> right before drawing it. Built-in Arabic strings are embedded in the
    /// DLL and an optional text\ar.txt next to the mod can add or override entries.
    /// </summary>
    public static class Loc
    {
        public const string ResourceName = "OrganizationEmpire.ar.txt";

        public static UiLanguage Language { get; private set; } = UiLanguage.English;
        public static bool IsArabic => Language == UiLanguage.Arabic;
        public static readonly Translator Arabic = new Translator();
        public static readonly List<string> LoadProblems = new List<string>();
        private static bool _loaded;

        public static void SetLanguage(UiLanguage language)
        {
            Language = language;
            if (language == UiLanguage.Arabic) EnsureLoaded(null);
        }

        /// <summary>Loads the embedded table, then the optional override file. Safe to call repeatedly.</summary>
        public static void EnsureLoaded(string overrideFile)
        {
            if (_loaded && overrideFile == null) return;
            if (!_loaded)
            {
                LoadProblems.Clear();
                Arabic.Clear();
                string builtIn = ReadBuiltIn();
                if (builtIn == null) LoadProblems.Add("embedded Arabic table missing");
                else Arabic.Load(builtIn, LoadProblems);
                _loaded = true;
            }
            if (overrideFile != null && File.Exists(overrideFile))
            {
                try
                {
                    int n = Arabic.Load(File.ReadAllText(overrideFile, Encoding.UTF8), LoadProblems);
                    Log.Info("Loaded " + n + " Arabic strings from " + Path.GetFileName(overrideFile) + ".");
                }
                catch (Exception ex) { LoadProblems.Add(Path.GetFileName(overrideFile) + ": " + ex.Message); }
            }
        }

        public static string ReadBuiltIn()
        {
            using (var stream = typeof(Loc).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        /// <summary>Translates a display string (identity in English).</summary>
        public static string T(string s) => IsArabic && !string.IsNullOrEmpty(s) ? Arabic.Translate(s) : s;

        /// <summary>string.Format with a translated format string.</summary>
        public static string F(string format, params object[] args)
        {
            string fmt = IsArabic ? Arabic.Translate(format) : format;
            try { return string.Format(CultureInfo.InvariantCulture, fmt, args); }
            catch (FormatException) { return string.Format(CultureInfo.InvariantCulture, format, args); }
        }

        /// <summary>Picks between an English and an Arabic literal (for text that is generated, not looked up).</summary>
        public static string Pick(string english, string arabic) => IsArabic && !string.IsNullOrEmpty(arabic) ? arabic : english;
    }
}

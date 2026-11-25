using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OrganizationEmpire.Core.Config;

namespace OrganizationEmpire.Core.Integration
{
    /// <summary>A .NET assembly loaded in the game (by ScriptHookVDotNet), described without exposing absolute paths.</summary>
    public sealed class LoadedAssemblyInfo
    {
        public string Name;
        public string Version;
        /// <summary>Path relative to the GTA V folder ("scripts/Foo.dll"), or null when not loaded from a file there.</summary>
        public string RelativePath;
        public List<string> References = new List<string>();
        /// <summary>Number of ScriptHookVDotNet Script classes it contains (0 = library).</summary>
        public int ScriptCount;
    }

    /// <summary>Read-only view of the game installation (real implementation in the game layer, fakes in tests).</summary>
    public interface IModEnvironment
    {
        /// <summary>Files under a folder relative to the GTA V folder, as relative paths with '/' separators.</summary>
        IEnumerable<string> Files(string relativeFolder, bool recursive);
        bool FileExists(string relativePath);
        bool FolderExists(string relativePath);
        /// <summary>Reads a small text file (returns null if missing or larger than the limit).</summary>
        string ReadText(string relativePath, int maxBytes = 262144);
        IEnumerable<string> NativeModules { get; }
        IEnumerable<LoadedAssemblyInfo> Assemblies { get; }
        string ShvdnVersion { get; }
    }

    public sealed class DetectedMod
    {
        public string Id;
        public string Name;
        public ModKind Kind;
        public ModCategory Category;
        public IntegrationLevel Level;
        public string Version;
        public string Evidence;
        public bool Known;
        public int ScriptCount;
        public KnownMod Catalog;
    }

    public enum IssueSeverity { Info, Warning, Error }
    public enum IssueKind { HotkeyConflict, Overlap, MissingDependency, Version, Duplicate, Notice }

    public sealed class CompatIssue
    {
        public IssueSeverity Severity;
        public IssueKind Kind;
        public string Text;
        public List<string> ModIds = new List<string>();
        /// <summary>Machine-readable fix the menu can offer: "rebind:QuickOrders:F9", "coexistence:On". Null = information only.</summary>
        public string Fix;
        public string FixLabel;
    }

    public sealed class ScanResult
    {
        public readonly List<DetectedMod> Mods = new List<DetectedMod>();
        public readonly List<CompatIssue> Issues = new List<CompatIssue>();
        public DateTime ScannedAtUtc = DateTime.UtcNow;

        public bool Has(string id) => Mods.Any(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        public DetectedMod Get(string id) => Mods.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        public bool AnyOverlap(string tag) => Mods.Any(m => m.Catalog != null && m.Catalog.Overlaps.Contains(tag));
    }

    /// <summary>Detects installed mods from read-only evidence and lists unknown ScriptHookVDotNet scripts too.</summary>
    public static class ModScanner
    {
        /// <summary>Our own assemblies and the frameworks' own assemblies are not reported as "unknown scripts".</summary>
        private static readonly string[] IgnoredAssemblies = { "OrganizationEmpire", "ScriptHookVDotNet", "ScriptHookVDotNet2", "ScriptHookVDotNet3" };

        public static ScanResult Scan(IModEnvironment env, IEnumerable<KnownMod> catalog = null)
        {
            var result = new ScanResult();
            var asms = SafeList(() => env.Assemblies);
            var modules = SafeList(() => env.NativeModules);
            var claimedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var km in catalog ?? ModCatalog.All)
            {
                string evidence = null, version = null;
                foreach (var sig in km.Signatures)
                {
                    evidence = Match(env, sig, asms, modules, out version, out string asmName);
                    if (evidence == null) continue;
                    if (asmName != null) claimedAssemblies.Add(asmName);
                    break;
                }
                if (evidence == null) continue;
                // Assemblies matched by file name are still "claimed" so they don't show up twice.
                foreach (var a in asms)
                    if (a.RelativePath != null && km.Signatures.Any(s => s.Kind == SignatureKind.File && Glob(s.Pattern, a.RelativePath)))
                    {
                        claimedAssemblies.Add(a.Name);
                        version = version ?? a.Version;
                    }
                result.Mods.Add(new DetectedMod
                {
                    Id = km.Id, Name = km.Name, Kind = km.Kind, Category = km.Category, Level = km.Level,
                    Version = version, Evidence = evidence, Known = true, Catalog = km,
                    ScriptCount = asms.Where(a => claimedAssemblies.Contains(a.Name)).Sum(a => a.ScriptCount),
                });
            }

            // Every other ScriptHookVDotNet script assembly is a mod too, even if we know nothing about it.
            foreach (var a in asms)
            {
                if (a.ScriptCount <= 0 || claimedAssemblies.Contains(a.Name)) continue;
                if (IgnoredAssemblies.Any(i => string.Equals(i, a.Name, StringComparison.OrdinalIgnoreCase))) continue;
                result.Mods.Add(new DetectedMod
                {
                    Id = "script:" + a.Name, Name = a.Name, Kind = ModKind.ShvdnScript, Category = ModCategory.NpcBehavior,
                    Level = IntegrationLevel.DetectedOnly, Version = a.Version, Evidence = a.RelativePath ?? a.Name, ScriptCount = a.ScriptCount,
                });
            }
            return result;
        }

        private static List<T> SafeList<T>(Func<IEnumerable<T>> get)
        {
            try { return get()?.ToList() ?? new List<T>(); }
            catch (Exception) { return new List<T>(); }
        }

        private static string Match(IModEnvironment env, ModSignature sig, List<LoadedAssemblyInfo> asms, List<string> modules, out string version, out string asmName)
        {
            version = null;
            asmName = null;
            try
            {
                switch (sig.Kind)
                {
                    case SignatureKind.Module:
                        return modules.FirstOrDefault(m => Glob(sig.Pattern, m)) != null ? "loaded module " + sig.Pattern : null;
                    case SignatureKind.Assembly:
                    {
                        var a = asms.FirstOrDefault(x => Glob(sig.Pattern, x.Name));
                        if (a == null) return null;
                        version = a.Version;
                        asmName = a.Name;
                        return "loaded assembly " + a.Name;
                    }
                    case SignatureKind.Folder:
                        return env.FolderExists(sig.Pattern) ? sig.Pattern + "/" : null;
                    default:
                    {
                        if (sig.Pattern.IndexOf('*') < 0) return env.FileExists(sig.Pattern) ? sig.Pattern : null;
                        int slash = sig.Pattern.LastIndexOf('/');
                        string folder = slash >= 0 ? sig.Pattern.Substring(0, slash) : "";
                        var hit = env.Files(folder, false).FirstOrDefault(f => Glob(sig.Pattern, f));
                        return hit;
                    }
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Case-insensitive wildcard match ('*' = any text) on '/'-normalized paths.</summary>
        public static bool Glob(string pattern, string text)
        {
            if (pattern == null || text == null) return false;
            string p = pattern.Replace('\\', '/');
            string t = text.Replace('\\', '/');
            string rx = "^" + Regex.Escape(p).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(t, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    /// <summary>Finds hotkey clashes, overlapping systems, missing dependencies, duplicates and version problems.</summary>
    public static class CompatibilityAnalyzer
    {
        public static readonly string[] SpareKeys = { "F9", "F10", "F11", "F6", "F5", "Insert", "Home", "End", "PageDown" };

        public static void Analyze(ScanResult scan, IModEnvironment env, ModConfig cfg)
        {
            var issues = scan.Issues;
            issues.Clear();
            var ours = OurKeys(cfg);

            // Hotkeys
            if (cfg.Integrations.CheckHotkeyConflicts)
            {
                var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var mod in scan.Mods.Where(m => m.Catalog != null))
                {
                    foreach (var k in mod.Catalog.Keys)
                    {
                        string key = KeyNames.Normalize(ReadConfiguredKey(env, k) ?? k.Key);
                        if (key == null) continue;
                        taken[key] = mod.Name;
                        foreach (var o in ours.Where(o => KeyNames.Normalize(o.Value) == key))
                        {
                            string spare = SpareKeys.FirstOrDefault(s => !ours.Values.Any(v => KeyNames.Normalize(v) == KeyNames.Normalize(s)) && !taken.ContainsKey(KeyNames.Normalize(s))
                                && !scan.Mods.Any(m => m.Catalog != null && m.Catalog.Keys.Any(x => KeyNames.Normalize(x.Key) == KeyNames.Normalize(s))));
                            issues.Add(new CompatIssue
                            {
                                Severity = IssueSeverity.Warning,
                                Kind = IssueKind.HotkeyConflict,
                                Text = string.Format(CultureInfo.InvariantCulture, "{0} uses {1} ({2}), the same key as {3}.", mod.Name, key, k.Purpose, o.Key),
                                ModIds = { mod.Id },
                                Fix = spare != null ? "rebind:" + o.Key + ":" + spare : null,
                                FixLabel = spare != null ? string.Format(CultureInfo.InvariantCulture, "Move {0} to {1}", o.Key, spare) : null,
                            });
                        }
                    }
                }
            }

            // Overlapping systems
            var gangMods = scan.Mods.Where(m => m.Catalog != null && m.Catalog.Overlaps.Contains("gang-wars")).ToList();
            if (gangMods.Count > 0)
            {
                bool on = Coexistence(scan, cfg);
                issues.Add(new CompatIssue
                {
                    Severity = on ? IssueSeverity.Info : IssueSeverity.Warning,
                    Kind = IssueKind.Overlap,
                    Text = string.Format(CultureInfo.InvariantCulture, "{0} also runs gang wars and territory. {1}",
                        string.Join(", ", gangMods.Select(m => m.Name)),
                        on ? "Coexistence mode is on: Organization Empire spawns fewer battle fighters and no ambient crews."
                           : "Coexistence mode is off: both mods spawn fighters, which can overload the streets."),
                    ModIds = gangMods.Select(m => m.Id).ToList(),
                    Fix = on ? null : "coexistence:On",
                    FixLabel = on ? null : "Turn coexistence mode on",
                });
            }
            if (scan.AnyOverlap("bodyguards"))
            {
                issues.Add(new CompatIssue
                {
                    Severity = IssueSeverity.Info,
                    Kind = IssueKind.Overlap,
                    Text = "A bodyguard/squad mod is installed. Its NPCs are script-owned, so Organization Empire never recruits, orders or deletes them.",
                    ModIds = scan.Mods.Where(m => m.Catalog != null && m.Catalog.Overlaps.Contains("bodyguards")).Select(m => m.Id).ToList(),
                });
            }
            if (scan.AnyOverlap("police"))
            {
                issues.Add(new CompatIssue
                {
                    Severity = IssueSeverity.Info,
                    Kind = IssueKind.Overlap,
                    Text = "A police mod is installed. Recruiting law enforcement stays off while it is present.",
                    ModIds = scan.Mods.Where(m => m.Catalog != null && m.Catalog.Overlaps.Contains("police")).Select(m => m.Id).ToList(),
                });
            }

            // Missing dependencies (catalog) and referenced-but-missing libraries (any script).
            foreach (var mod in scan.Mods.Where(m => m.Catalog != null))
            {
                foreach (var dep in mod.Catalog.Requires)
                {
                    if (scan.Has(dep)) continue;
                    var dm = ModCatalog.Get(dep);
                    issues.Add(new CompatIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Kind = IssueKind.MissingDependency,
                        Text = string.Format(CultureInfo.InvariantCulture, "{0} needs {1}, which was not found.", mod.Name, dm?.Name ?? dep),
                        ModIds = { mod.Id },
                    });
                }
            }
            var asms = SafeAssemblies(env);
            var loadedNames = new HashSet<string>(asms.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);
            var libraryIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LemonUI.SHVDN3"] = "LemonUI", ["LemonUI.SHVDN2"] = "LemonUI", ["NativeUI"] = "NativeUI", ["iFruitAddon2"] = "iFruitAddon2",
            };
            foreach (var a in asms.Where(a => a.ScriptCount > 0))
            {
                foreach (var r in a.References)
                {
                    if (!libraryIds.TryGetValue(r, out string lib) || loadedNames.Contains(r)) continue;
                    if (env.FileExists("scripts/" + r + ".dll")) continue;
                    issues.Add(new CompatIssue
                    {
                        Severity = IssueSeverity.Warning,
                        Kind = IssueKind.MissingDependency,
                        Text = string.Format(CultureInfo.InvariantCulture, "The script {0} references {1}, which is not installed. That mod may not start.", a.Name, lib),
                    });
                }
            }

            // Duplicate copies of this mod.
            var copies = SafeFiles(env, "scripts").Where(f => ModScanner.Glob("scripts/OrganizationEmpire*.dll", f)).ToList();
            if (copies.Count > 1)
                issues.Add(new CompatIssue
                {
                    Severity = IssueSeverity.Error,
                    Kind = IssueKind.Duplicate,
                    Text = "More than one copy of Organization Empire is in the scripts folder (" + string.Join(", ", copies) + "). Keep only OrganizationEmpire.dll.",
                });

            // Framework version.
            if (Version.TryParse(Trim(env.ShvdnVersion), out var v) && v < new Version(3, 6, 0))
                issues.Add(new CompatIssue
                {
                    Severity = IssueSeverity.Warning,
                    Kind = IssueKind.Version,
                    Text = "ScriptHookVDotNet " + env.ShvdnVersion + " is older than 3.6.0; some features may fail.",
                    ModIds = { "shvdn" },
                });

            if (scan.Has("rph"))
                issues.Add(new CompatIssue
                {
                    Severity = IssueSeverity.Info,
                    Kind = IssueKind.Notice,
                    Text = "RAGE Plugin Hook is installed. Its plugins (LSPDFR, Los Santos RED...) run in a separate hook; Organization Empire adapts to them but cannot call into them.",
                    ModIds = { "rph" },
                });
        }

        /// <summary>Is coexistence mode in effect for this scan?</summary>
        public static bool Coexistence(ScanResult scan, ModConfig cfg)
        {
            switch (cfg.Integrations.Coexistence)
            {
                case CoexistenceMode.On: return true;
                case CoexistenceMode.Off: return false;
                default: return scan != null && scan.AnyOverlap("gang-wars");
            }
        }

        public static Dictionary<string, string> OurKeys(ModConfig cfg) => new Dictionary<string, string>
        {
            ["OpenMenu"] = cfg.Hotkeys.OpenMenu,
            ["QuickOrders"] = cfg.Hotkeys.QuickOrders,
            ["Interact"] = cfg.Hotkeys.Interact,
            ["Talk"] = cfg.Hotkeys.Talk,
        };

        /// <summary>Applies a "rebind:Name:Key" fix to our own config. Returns false for unknown fixes.</summary>
        public static bool ApplyRebind(ModConfig cfg, string fix)
        {
            var parts = (fix ?? "").Split(':');
            if (parts.Length != 3 || parts[0] != "rebind") return false;
            switch (parts[1])
            {
                case "OpenMenu": cfg.Hotkeys.OpenMenu = parts[2]; return true;
                case "QuickOrders": cfg.Hotkeys.QuickOrders = parts[2]; return true;
                case "Interact": cfg.Hotkeys.Interact = parts[2]; return true;
                case "Talk": cfg.Hotkeys.Talk = parts[2]; return true;
                default: return false;
            }
        }

        private static string ReadConfiguredKey(IModEnvironment env, ModKey k)
        {
            if (k.ConfigFile == null) return null;
            try
            {
                string text = env.ReadText(k.ConfigFile, 65536);
                if (text == null) return null;
                string value = SimpleIni.Get(text, k.ConfigSection, k.ConfigKey);
                return value;
            }
            catch (Exception) { return null; }
        }

        private static List<LoadedAssemblyInfo> SafeAssemblies(IModEnvironment env)
        {
            try { return env.Assemblies?.ToList() ?? new List<LoadedAssemblyInfo>(); }
            catch (Exception) { return new List<LoadedAssemblyInfo>(); }
        }

        private static List<string> SafeFiles(IModEnvironment env, string folder)
        {
            try { return env.Files(folder, false).ToList(); }
            catch (Exception) { return new List<string>(); }
        }

        private static string Trim(string v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            var m = Regex.Match(v, @"\d+(\.\d+){1,3}");
            return m.Success ? m.Value : null;
        }
    }

    /// <summary>Minimal read-only INI reader for other mods' config files ("key=value", ';' or '//' comments).</summary>
    public static class SimpleIni
    {
        public static string Get(string text, string section, string key)
        {
            if (text == null) return null;
            string current = "";
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line.EndsWith("]")) { current = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (section != null && !string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) continue;
                string value = line.Substring(eq + 1);
                int c = value.IndexOf("//", StringComparison.Ordinal);
                if (c >= 0) value = value.Substring(0, c);
                c = value.IndexOf(';');
                if (c >= 0) value = value.Substring(0, c);
                return value.Trim();
            }
            return null;
        }
    }

    /// <summary>Normalizes key names (System.Windows.Forms.Keys names) and Windows virtual-key codes so they can be compared.</summary>
    public static class KeyNames
    {
        public static string Normalize(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            key = key.Trim();
            if (key.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(key.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex)) return FromVk(hex);
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vk)) return FromVk(vk);
            string k = key.Replace(" ", "");
            if (k.Length == 1 && char.IsLetter(k[0])) return k.ToUpperInvariant();
            if (k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1])) return k.Substring(1);
            if (k.Length == 1 && char.IsDigit(k[0])) return k;
            if (string.Equals(k, "Next", StringComparison.OrdinalIgnoreCase)) return "PageDown";
            if (string.Equals(k, "Prior", StringComparison.OrdinalIgnoreCase)) return "PageUp";
            if (k.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(k.Substring(1), out int f) && f >= 1 && f <= 24) return "F" + f;
            return char.ToUpperInvariant(k[0]) + k.Substring(1);
        }

        public static string FromVk(int vk)
        {
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x60 && vk <= 0x69) return "NumPad" + (vk - 0x60);
            switch (vk)
            {
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0x24: return "Home";
                case 0x23: return "End";
                case 0x21: return "PageUp";
                case 0x22: return "PageDown";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                default: return "VK" + vk.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}

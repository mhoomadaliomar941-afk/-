using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using OrganizationEmpire.Core.Integration;

namespace OrganizationEmpire.Integration
{
    /// <summary>
    /// Read-only view of the real GTA V folder and the running game process. Paths handed out are relative to the
    /// GTA V folder, so nothing about the user's drive layout or account name reaches logs or the menu.
    /// </summary>
    internal sealed class RuntimeModEnvironment : IModEnvironment
    {
        private const int MaxFilesPerFolder = 3000;
        private readonly string _root;

        public RuntimeModEnvironment(string gtaRoot) { _root = Path.GetFullPath(gtaRoot); }

        private string Full(string rel) => Path.GetFullPath(Path.Combine(_root, (rel ?? "").Replace('/', Path.DirectorySeparatorChar)));

        private string Rel(string full)
        {
            string r = full.Length > _root.Length ? full.Substring(_root.Length).TrimStart('\\', '/') : "";
            return r.Replace('\\', '/');
        }

        /// <summary>Refuses paths that escape the GTA V folder (e.g. "..\..\").</summary>
        private bool Inside(string full) => full.StartsWith(_root, StringComparison.OrdinalIgnoreCase);

        public IEnumerable<string> Files(string relativeFolder, bool recursive)
        {
            string dir = Full(relativeFolder);
            if (!Inside(dir) || !Directory.Exists(dir)) return Enumerable.Empty<string>();
            return Directory.EnumerateFiles(dir, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Take(MaxFilesPerFolder).Select(Rel).ToList();
        }

        public bool FileExists(string relativePath)
        {
            string f = Full(relativePath);
            return Inside(f) && File.Exists(f);
        }

        public bool FolderExists(string relativePath)
        {
            string f = Full(relativePath);
            return Inside(f) && Directory.Exists(f);
        }

        public string ReadText(string relativePath, int maxBytes = 262144)
        {
            string f = Full(relativePath);
            if (!Inside(f) || !File.Exists(f)) return null;
            var info = new FileInfo(f);
            if (info.Length > maxBytes) return null;
            using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs)) return reader.ReadToEnd();
        }

        public IEnumerable<string> NativeModules
        {
            get
            {
                var list = new List<string>();
                try
                {
                    foreach (ProcessModule m in Process.GetCurrentProcess().Modules) list.Add(m.ModuleName);
                }
                catch (Exception) { /* access can be refused; detection falls back to files */ }
                return list;
            }
        }

        public IEnumerable<LoadedAssemblyInfo> Assemblies
        {
            get
            {
                var list = new List<LoadedAssemblyInfo>();
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (a.IsDynamic) continue;
                        string location = null;
                        try { location = a.Location; } catch (Exception) { /* loaded from bytes */ }
                        string rel = null;
                        if (!string.IsNullOrEmpty(location))
                        {
                            string full = Path.GetFullPath(location);
                            if (!Inside(full)) continue;           // framework / GAC assemblies are not mods
                            rel = Rel(full);
                        }
                        var name = a.GetName();
                        list.Add(new LoadedAssemblyInfo
                        {
                            Name = name.Name,
                            Version = name.Version?.ToString(),
                            RelativePath = rel,
                            References = a.GetReferencedAssemblies().Select(r => r.Name).ToList(),
                            ScriptCount = CountScripts(a),
                        });
                    }
                    catch (Exception) { /* one odd assembly must not stop the scan */ }
                }
                return list;
            }
        }

        private static int CountScripts(Assembly a)
        {
            Type[] types;
            try { types = a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            catch (Exception) { return 0; }
            var scriptType = typeof(GTA.Script);
            return types.Count(t => t.IsClass && !t.IsAbstract && scriptType.IsAssignableFrom(t));
        }

        public string ShvdnVersion => typeof(GTA.Script).Assembly.GetName().Version?.ToString();
    }
}

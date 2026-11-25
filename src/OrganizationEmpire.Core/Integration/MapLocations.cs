using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Integration
{
    public sealed class MapLocation
    {
        public string Name;
        public string File;
        public string Format;
        public Vec3 Center;
        public int ObjectCount;
    }

    /// <summary>
    /// Reads map files made with Map Editor (Guadmaz) or Menyoo's Spooner to offer them as headquarters sites.
    /// Read-only and defensive: DTDs are disabled, files over 8 MB are skipped, malformed files are ignored.
    /// The center is the median of the placed objects so one far-away object does not move it.
    /// </summary>
    public static class MapLocations
    {
        public const int MaxBytes = 8 * 1024 * 1024;

        public static readonly string[] DefaultFolders = { "scripts/MapEditor", "scripts/MapLoaderAuto", "menyooStuff/Spooner" };

        public static List<MapLocation> Find(IModEnvironment env, IEnumerable<string> extraFolders = null)
        {
            var list = new List<MapLocation>();
            var folders = DefaultFolders.Concat(extraFolders ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim().Replace('\\', '/').TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in folders)
            {
                IEnumerable<string> files;
                try
                {
                    if (!env.FolderExists(folder)) continue;
                    files = env.Files(folder, true).Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).Take(200).ToList();
                }
                catch (Exception) { continue; }
                foreach (string f in files)
                {
                    string text;
                    try { text = env.ReadText(f, MaxBytes); }
                    catch (Exception) { continue; }
                    var loc = Parse(text, f);
                    if (loc != null) list.Add(loc);
                }
            }
            return list;
        }

        /// <summary>Parses one map file; returns null when it has no usable positions.</summary>
        public static MapLocation Parse(string xml, string file)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreWhitespace = true };
            var points = new List<Vec3>();
            Vec3? reference = null;
            string format = "XML";
            try
            {
                using (var sr = new StringReader(xml))
                using (var reader = XmlReader.Create(sr, settings))
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element) continue;
                        string name = reader.LocalName;
                        if (reader.Depth == 0)
                        {
                            if (name == "Map") format = "Map Editor";
                            else if (name == "SpoonerPlacements") format = "Menyoo";
                        }
                        if (name == "ReferenceCoords")
                        {
                            var p = ReadXyz(reader);
                            if (p.HasValue && !p.Value.IsZero) reference = p;
                        }
                        else if (name == "Position" || name == "PositionRotation")
                        {
                            var p = ReadXyz(reader);
                            if (p.HasValue && !p.Value.IsZero && Plausible(p.Value)) points.Add(p.Value);
                        }
                    }
                }
            }
            catch (XmlException) { return null; }
            if (points.Count == 0 && reference == null) return null;
            Vec3 center = reference ?? new Vec3(Median(points.Select(p => p.X)), Median(points.Select(p => p.Y)), Median(points.Select(p => p.Z)));
            return new MapLocation
            {
                Name = Path.GetFileNameWithoutExtension(file ?? "map"),
                File = file,
                Format = format,
                Center = center,
                ObjectCount = points.Count,
            };
        }

        private static bool Plausible(Vec3 p) => Math.Abs(p.X) < 10000 && Math.Abs(p.Y) < 10000 && p.Z > -500 && p.Z < 3000;

        private static Vec3? ReadXyz(XmlReader reader)
        {
            if (reader.IsEmptyElement) return null;
            int depth = reader.Depth;
            float? x = null, y = null, z = null;
            reader.Read();
            while (!reader.EOF && reader.Depth > depth)
            {
                if (reader.NodeType == XmlNodeType.Element && (reader.LocalName == "X" || reader.LocalName == "Y" || reader.LocalName == "Z"))
                {
                    string n = reader.LocalName;
                    string v = reader.ReadElementContentAsString(); // also moves to the next node
                    if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f))
                    {
                        if (n == "X") x = f; else if (n == "Y") y = f; else z = f;
                    }
                    continue;
                }
                reader.Read();
            }
            if (x == null || y == null) return null;
            return new Vec3(x.Value, y.Value, z ?? 0);
        }

        private static float Median(IEnumerable<float> values)
        {
            var v = values.OrderBy(x => x).ToList();
            if (v.Count == 0) return 0;
            return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2f;
        }
    }
}

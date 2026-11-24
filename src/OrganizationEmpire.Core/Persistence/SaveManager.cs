using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OrganizationEmpire.Core.Json;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Persistence
{
    public sealed class SaveSlotInfo
    {
        public string Slot;
        public string FilePath;
        public string FactionName;
        public string FactionType;
        public bool IsGovernment;
        public int Day;
        public int Members;
        public DateTime SavedAtUtc;
        public bool IsBackup;
        public bool Corrupt;
    }

    /// <summary>
    /// JSON save slots with atomic writes (write temp, then swap), a rolling .bak copy of the previous save,
    /// version migration hook, and automatic recovery from the backup when a file is corrupt.
    /// </summary>
    public sealed class SaveManager
    {
        public const string AutosaveSlot = "autosave";
        public const string PreTakeoverSlot = "backup_pre_takeover";
        private readonly string _dir;

        public SaveManager(string directory)
        {
            _dir = directory;
            Directory.CreateDirectory(_dir);
        }

        public string Directory_ => _dir;

        public static string SanitizeSlot(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot)) slot = "slot";
            var sb = new StringBuilder();
            foreach (char c in slot.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(char.ToLowerInvariant(c));
                else if (c == ' ') sb.Append('_');
            }
            string s = sb.ToString();
            if (s.Length == 0) s = "slot";
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }

        private string PathFor(string slot) => Path.Combine(_dir, SanitizeSlot(slot) + ".json");

        public OpResult Save(GameState state, string slot, bool includeMemories = true)
        {
            try
            {
                slot = SanitizeSlot(slot);
                state.SlotName = slot;
                state.SavedAtUtc = DateTime.UtcNow;
                state.Version = GameState.CurrentVersion;
                var memories = state.Memories;
                if (!includeMemories) state.Memories = new Dictionary<string, NpcMemory>();
                string json;
                try { json = JsonMapper.Serialize(state, true); }
                finally { state.Memories = memories; }

                string path = PathFor(slot);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                // Validate what we wrote before replacing the real file.
                Json.Json.Parse(File.ReadAllText(tmp));
                if (File.Exists(path))
                {
                    string bak = path + ".bak";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                }
                File.Move(tmp, path);
                Log.Info("Saved slot '" + slot + "' (" + json.Length / 1024 + " KB).");
                return OpResult.Success("Saved to '" + slot + "'.");
            }
            catch (Exception ex)
            {
                Log.Error("Save failed for slot '" + slot + "'", ex);
                return OpResult.Fail("Save failed: " + ex.Message);
            }
        }

        public GameState Load(string slot, out string message)
        {
            string path = PathFor(slot);
            message = null;
            var state = TryLoadFile(path, out string err);
            if (state == null)
            {
                string bak = path + ".bak";
                if (File.Exists(bak))
                {
                    state = TryLoadFile(bak, out string err2);
                    if (state != null)
                    {
                        message = "Save '" + slot + "' was damaged; restored the previous copy.";
                        Log.Warn(message + " (" + err + ")");
                    }
                    else err = err + " / backup: " + err2;
                }
                if (state == null)
                {
                    message = "Could not load '" + slot + "': " + err;
                    Log.Error(message);
                    return null;
                }
            }
            var fixes = Simulation.Validate(state);
            foreach (var fix in fixes) Log.Warn("Save repair: " + fix);
            state.SlotName = SanitizeSlot(slot);
            if (message == null) message = "Loaded '" + slot + "'.";
            return state;
        }

        private static GameState TryLoadFile(string path, out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(path)) { error = "file not found"; return null; }
                string text = File.ReadAllText(path);
                var tree = Json.Json.Parse(text);
                int version = (int)Json.Json.GetNumber(tree, "Version", 1);
                tree = Migrate(tree, version);
                var state = (GameState)JsonMapper.FromTree(tree, typeof(GameState));
                if (state == null || state.World == null) { error = "invalid content"; return null; }
                return state;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>Upgrades older save trees in place. Version 1 is current; this is the hook for future formats.</summary>
        private static object Migrate(object tree, int version)
        {
            if (version > GameState.CurrentVersion)
                Log.Warn("Save was written by a newer mod version (" + version + "); unknown data will be ignored.");
            return tree;
        }

        public List<SaveSlotInfo> ListSlots()
        {
            var list = new List<SaveSlotInfo>();
            if (!Directory.Exists(_dir)) return list;
            foreach (string file in Directory.GetFiles(_dir, "*.json"))
            {
                var info = new SaveSlotInfo
                {
                    Slot = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    SavedAtUtc = File.GetLastWriteTimeUtc(file),
                };
                info.IsBackup = info.Slot.StartsWith("backup_", StringComparison.Ordinal);
                try
                {
                    var tree = Json.Json.Parse(File.ReadAllText(file));
                    var faction = Json.Json.Get(tree, "Faction");
                    info.FactionName = Json.Json.GetString(faction, "Name", "(no faction)");
                    info.FactionType = Json.Json.GetString(faction, "Type", "");
                    var gov = Json.Json.Get(faction, "Government");
                    info.IsGovernment = gov is Dictionary<string, object> g && g.TryGetValue("Established", out object e) && e is bool b && b;
                    var members = Json.Json.Get(faction, "Members") as List<object>;
                    info.Members = members?.Count ?? 0;
                    var world = Json.Json.Get(tree, "World");
                    info.Day = (int)(Json.Json.GetNumber(world, "Tick", 0) / 24);
                    string saved = Json.Json.GetString(tree, "SavedAtUtc");
                    if (saved != null && DateTime.TryParse(saved, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime dt)) info.SavedAtUtc = dt;
                }
                catch
                {
                    info.Corrupt = true;
                    info.FactionName = "(damaged)";
                }
                list.Add(info);
            }
            return list.OrderByDescending(i => i.SavedAtUtc).ToList();
        }

        public SaveSlotInfo MostRecent() => ListSlots().FirstOrDefault(i => !i.Corrupt && !i.IsBackup);

        public OpResult Delete(string slot)
        {
            try
            {
                string path = PathFor(slot);
                if (!File.Exists(path)) return OpResult.Fail("Slot not found.");
                File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
                return OpResult.Success("Deleted '" + slot + "'.");
            }
            catch (Exception ex)
            {
                return OpResult.Fail("Delete failed: " + ex.Message);
            }
        }

        public bool Exists(string slot) => File.Exists(PathFor(slot));
    }
}

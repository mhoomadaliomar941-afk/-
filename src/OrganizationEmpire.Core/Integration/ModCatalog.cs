using System;
using System.Collections.Generic;

namespace OrganizationEmpire.Core.Integration
{
    public enum ModKind { Framework, ShvdnScript, AsiPlugin, RphPlugin, Library, Tool, DataMod }

    public enum ModCategory
    {
        Framework, GangsAndTerritory, NpcBehavior, Police, MilitaryCombat, Vehicles, InteriorsAndMaps,
        MissionsAndRobbery, Animation, UiAndMenus, Trainer, Library, Performance,
    }

    /// <summary>How far Organization Empire works with a mod.</summary>
    public enum IntegrationLevel
    {
        /// <summary>Implemented and covered by automated tests; works without touching the other mod.</summary>
        Supported,
        /// <summary>Implemented against the other mod's documented API but not verified in a running game.</summary>
        Experimental,
        /// <summary>Recognized; the mod adapts its own behavior (warnings, coexistence) but there is no API to connect to.</summary>
        DetectedOnly,
        /// <summary>Known to clash; the player is warned.</summary>
        Incompatible,
        /// <summary>Researched; no integration is possible from a ScriptHookVDotNet script (different hook, closed format...).</summary>
        Unavailable,
    }

    public enum SignatureKind { File, Folder, Module, Assembly }

    /// <summary>One piece of evidence that a mod is installed. Patterns are case-insensitive; '*' matches any text.</summary>
    public sealed class ModSignature
    {
        public SignatureKind Kind;
        /// <summary>File/folder path relative to the GTA V folder (wildcards allowed in the file name), module or assembly name.</summary>
        public string Pattern;

        public ModSignature(SignatureKind kind, string pattern) { Kind = kind; Pattern = pattern; }
    }

    /// <summary>A key another mod uses by default (or reads from its own config).</summary>
    public sealed class ModKey
    {
        public string Key;
        public string Purpose;
        /// <summary>"documented", "config file", or "commonly reported" (not confirmed by the author).</summary>
        public string Source;
        /// <summary>Optional: ini file (relative to the GTA V folder), section and key that override the default. Values are virtual-key codes or key names.</summary>
        public string ConfigFile, ConfigSection, ConfigKey;
    }

    /// <summary>A GTA V single-player mod Organization Empire knows about, from the research notes in docs/MOD_RESEARCH.md.</summary>
    public sealed class KnownMod
    {
        public string Id;
        public string Name;
        public string Author;
        public ModKind Kind;
        public ModCategory Category;
        public IntegrationLevel Level;
        public readonly List<ModSignature> Signatures = new List<ModSignature>();
        public readonly List<ModKey> Keys = new List<ModKey>();
        /// <summary>Systems that overlap with Organization Empire ("gang-wars", "bodyguards", "police", "hostile-spawns").</summary>
        public readonly List<string> Overlaps = new List<string>();
        /// <summary>Other catalog ids this mod needs to run.</summary>
        public readonly List<string> Requires = new List<string>();
        public string License;
        /// <summary>What Organization Empire does with it.</summary>
        public string Integration;

        public KnownMod Sig(SignatureKind kind, string pattern) { Signatures.Add(new ModSignature(kind, pattern)); return this; }
        public KnownMod Key(string key, string purpose, string source, string file = null, string section = null, string iniKey = null)
        {
            Keys.Add(new ModKey { Key = key, Purpose = purpose, Source = source, ConfigFile = file, ConfigSection = section, ConfigKey = iniKey });
            return this;
        }
        public KnownMod Overlap(params string[] tags) { Overlaps.AddRange(tags); return this; }
        public KnownMod Needs(params string[] ids) { Requires.AddRange(ids); return this; }
    }

    /// <summary>
    /// Curated list of mods relevant to faction gameplay. Detection is read-only: file/folder presence in the GTA V
    /// folder, native modules loaded in the game process and .NET assemblies loaded by ScriptHookVDotNet.
    /// No mod files are opened for writing, copied or redistributed.
    /// </summary>
    public static class ModCatalog
    {
        public static readonly IReadOnlyList<KnownMod> All = Build();

        public static KnownMod Get(string id)
        {
            foreach (var m in All) if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        private static List<KnownMod> Build()
        {
            var l = new List<KnownMod>();
            KnownMod Add(string id, string name, string author, ModKind kind, ModCategory cat, IntegrationLevel level, string license, string integration)
            {
                var m = new KnownMod { Id = id, Name = name, Author = author, Kind = kind, Category = cat, Level = level, License = license, Integration = integration };
                l.Add(m);
                return m;
            }

            // ---- frameworks this mod runs on
            Add("scripthookv", "Script Hook V", "Alexander Blade", ModKind.Framework, ModCategory.Framework, IntegrationLevel.Supported,
                "Freeware, redistribution not allowed", "Required runtime.")
                .Sig(SignatureKind.Module, "ScriptHookV.dll").Sig(SignatureKind.File, "ScriptHookV.dll");
            Add("shvdn", "ScriptHookVDotNet", "crosire and contributors", ModKind.Framework, ModCategory.Framework, IntegrationLevel.Supported,
                "zlib", "Required runtime (v3 API, 3.6.0 or newer recommended).")
                .Sig(SignatureKind.File, "ScriptHookVDotNet.asi").Sig(SignatureKind.Assembly, "ScriptHookVDotNet3").Needs("scripthookv");
            Add("asiloader", "ASI Loader (dinput8.dll)", "Alexander Blade / ThirteenAG", ModKind.Framework, ModCategory.Framework, IntegrationLevel.DetectedOnly,
                "Freeware", "Informational: loads .asi plugins such as ScriptHookVDotNet.")
                .Sig(SignatureKind.File, "dinput8.dll");
            Add("rph", "RAGE Plugin Hook", "RAGE Plugin Hook team", ModKind.Framework, ModCategory.Framework, IntegrationLevel.Unavailable,
                "Freeware", "Different hook from ScriptHookV: its plugins cannot be called from a ScriptHookVDotNet script. Both can run side by side.")
                .Sig(SignatureKind.File, "RAGEPluginHook.exe");

            // ---- libraries other scripts depend on
            Add("lemonui", "LemonUI", "Lemon (justalemon)", ModKind.Library, ModCategory.Library, IntegrationLevel.DetectedOnly,
                "MIT", "Not needed by Organization Empire (own interface). Detected so missing dependencies of other mods can be reported.")
                .Sig(SignatureKind.File, "scripts/LemonUI.SHVDN3.dll").Sig(SignatureKind.File, "scripts/LemonUI.SHVDN2.dll").Sig(SignatureKind.Assembly, "LemonUI.SHVDN3");
            Add("nativeui", "NativeUI (deprecated)", "Guad", ModKind.Library, ModCategory.Library, IntegrationLevel.DetectedOnly,
                "MIT-style notice (archived project)", "Not needed by Organization Empire. Detected for dependency checks of older mods.")
                .Sig(SignatureKind.File, "scripts/NativeUI.dll").Sig(SignatureKind.Assembly, "NativeUI");
            Add("ifruitaddon2", "iFruitAddon2", "Bob74 (original iFruitAddon by CamxxCore)", ModKind.Library, ModCategory.UiAndMenus, IntegrationLevel.Experimental,
                "Check the project's repository before redistributing; not bundled", "Optional: adds an 'Organization Empire' phone contact (open the menu / call your crew) through its public API, loaded by reflection.")
                .Sig(SignatureKind.File, "scripts/iFruitAddon2.dll").Sig(SignatureKind.Assembly, "iFruitAddon2");

            // ---- gangs, territory and NPC systems
            Add("gangandturf", "Gang and Turf Mod", "lucasvinbr", ModKind.ShvdnScript, ModCategory.GangsAndTerritory, IntegrationLevel.DetectedOnly,
                "Open source on GitHub (license not verified here; no code used)", "Overlaps with gang wars and territory. Coexistence mode trims Organization Empire's own street battles and crews so both don't flood the streets; vanilla gang-model suppression is paused.")
                .Sig(SignatureKind.File, "scripts/*Gang*Turf*.dll").Sig(SignatureKind.Assembly, "*GangAndTurf*")
                .Overlap("gang-wars", "hostile-spawns", "territory").Needs("lemonui");
            Add("lsred", "Los Santos RED", "thatoneguy650", ModKind.RphPlugin, ModCategory.GangsAndTerritory, IntegrationLevel.Unavailable,
                "Open source on GitHub (license not verified here; no code used)", "RAGE Plugin Hook plugin: no direct link. Coexistence mode applies because it runs its own gangs and police.")
                .Sig(SignatureKind.File, "Plugins/Los Santos RED.dll").Sig(SignatureKind.Folder, "Plugins/LosSantosRED")
                .Overlap("gang-wars", "police", "hostile-spawns").Needs("rph");
            Add("bodyguardsquads", "Bodyguard Squads", "Eddlm", ModKind.ShvdnScript, ModCategory.NpcBehavior, IntegrationLevel.DetectedOnly,
                "See the mod page", "Overlaps with member squads. Its bodyguards are script-owned entities, which Organization Empire never recruits or deletes.")
                .Sig(SignatureKind.File, "scripts/*Bodyguard*Squad*.dll").Sig(SignatureKind.Assembly, "*BodyguardSquad*").Overlap("bodyguards").Needs("nativeui");
            Add("squadsmanager", "Squads Manager (Enhanced Bodyguard Squads)", "community", ModKind.ShvdnScript, ModCategory.NpcBehavior, IntegrationLevel.DetectedOnly,
                "See the mod page", "Overlaps with member squads; same entity-ownership guard applies.")
                .Sig(SignatureKind.File, "scripts/*SquadsManager*.dll").Sig(SignatureKind.Assembly, "*SquadsManager*").Overlap("bodyguards");
            Add("policebodyguard", "Police Bodyguard", "community", ModKind.ShvdnScript, ModCategory.NpcBehavior, IntegrationLevel.DetectedOnly,
                "See the mod page", "Hotkey check only (opens with F6 by default).")
                .Sig(SignatureKind.File, "scripts/*PoliceBodyguard*.dll").Key("F6", "Open its menu", "documented").Overlap("bodyguards");

            // ---- police
            Add("lspdfr", "LSPD First Response", "G17 Media", ModKind.RphPlugin, ModCategory.Police, IntegrationLevel.DetectedOnly,
                "Proprietary freeware", "Runs under RAGE Plugin Hook (no API reachable from here). While detected, recruiting law enforcement is forced off so LSPDFR's officers are never pulled into a faction.")
                .Sig(SignatureKind.File, "Plugins/LSPD First Response.dll").Sig(SignatureKind.Folder, "Plugins/LSPDFR")
                .Overlap("police").Needs("rph");

            // ---- trainers and menus (hotkeys)
            Add("menyoo", "Menyoo PC", "MAFINS / ItsJustCurtis (2.0)", ModKind.AsiPlugin, ModCategory.Trainer, IntegrationLevel.Supported,
                "See the project page", "Hotkey conflict check (F8 by default, the same as Quick Orders). Spooner XML maps are offered as headquarters locations (read-only).")
                .Sig(SignatureKind.File, "Menyoo.asi").Sig(SignatureKind.Module, "Menyoo.asi").Sig(SignatureKind.Folder, "menyooStuff")
                .Key("F8", "Open Menyoo", "documented");
            Add("simpletrainer", "Simple Trainer for GTA V", "sjaak327", ModKind.AsiPlugin, ModCategory.Trainer, IntegrationLevel.Supported,
                "Freeware", "Hotkey conflict check using the keys in trainerv.ini ([KeyBindings] MenuKey / HideMenuKey).")
                .Sig(SignatureKind.File, "trainerv.asi").Sig(SignatureKind.Module, "trainerv.asi").Sig(SignatureKind.File, "trainerv.ini")
                .Key("F3", "Open menu", "config file", "trainerv.ini", "KeyBindings", "MenuKey")
                .Key("F4", "Hide menu", "config file", "trainerv.ini", "KeyBindings", "HideMenuKey");
            Add("nativetrainer", "Native Trainer (Script Hook V sample)", "Alexander Blade", ModKind.AsiPlugin, ModCategory.Trainer, IntegrationLevel.DetectedOnly,
                "Freeware", "Hotkey conflict check (F4, commonly reported).")
                .Sig(SignatureKind.File, "NativeTrainer.asi").Sig(SignatureKind.Module, "NativeTrainer.asi")
                .Key("F4", "Open menu", "commonly reported");
            Add("enhancednativetrainer", "Enhanced Native Trainer", "community", ModKind.AsiPlugin, ModCategory.Trainer, IntegrationLevel.DetectedOnly,
                "Open source (see project page)", "Hotkey conflict check (F4, commonly reported).")
                .Sig(SignatureKind.File, "EnhancedNativeTrainer.asi").Sig(SignatureKind.Module, "EnhancedNativeTrainer.asi")
                .Key("F4", "Open menu", "commonly reported");

            // ---- maps and interiors
            Add("mapeditor", "Map Editor", "Guadmaz", ModKind.ShvdnScript, ModCategory.InteriorsAndMaps, IntegrationLevel.Supported,
                "See the mod page", "Maps saved as Map Editor XML are listed as possible headquarters sites (read-only).")
                .Sig(SignatureKind.File, "scripts/MapEditor.dll").Sig(SignatureKind.Assembly, "MapEditor").Sig(SignatureKind.Folder, "scripts/MapEditor");
            Add("maploaderauto", "MapLoaderAuto", "community", ModKind.ShvdnScript, ModCategory.InteriorsAndMaps, IntegrationLevel.Supported,
                "See the mod page", "Reads the Map Editor XML maps in scripts\\MapLoaderAuto as headquarters sites (read-only).")
                .Sig(SignatureKind.Folder, "scripts/MapLoaderAuto").Needs("mapeditor");

            // ---- tools and performance
            Add("openiv", "OpenIV (ASI)", "OpenIV team", ModKind.Tool, ModCategory.Performance, IntegrationLevel.DetectedOnly,
                "Freeware", "Informational: needed only if you replace flag textures (docs/FLAG_ASSET_PIPELINE.md).")
                .Sig(SignatureKind.File, "OpenIV.asi").Sig(SignatureKind.Module, "OpenIV.asi");
            Add("heapadjuster", "Heap Limit Adjuster", "community", ModKind.AsiPlugin, ModCategory.Performance, IntegrationLevel.DetectedOnly,
                "Freeware", "Informational: recommended with many add-on models; nothing to configure here.")
                .Sig(SignatureKind.File, "HeapAdjuster.asi").Sig(SignatureKind.Module, "HeapAdjuster.asi");
            Add("packfileadjuster", "Packfile Limit Adjuster", "community", ModKind.AsiPlugin, ModCategory.Performance, IntegrationLevel.DetectedOnly,
                "Freeware", "Informational: recommended with many add-on models.")
                .Sig(SignatureKind.File, "PackfileLimitAdjuster.asi").Sig(SignatureKind.Module, "PackfileLimitAdjuster.asi");
            return l;
        }
    }
}

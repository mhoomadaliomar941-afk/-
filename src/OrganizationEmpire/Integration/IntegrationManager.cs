using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OrganizationEmpire.Api;
using OrganizationEmpire.Core.Integration;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Integration
{
    /// <summary>
    /// Owns mod detection and the optional integrations. Scanning (file system, loaded assemblies and modules) runs
    /// on a worker thread; starting/stopping integrations happens on the script thread through the isolated
    /// <see cref="IntegrationHost"/>, so a broken integration only switches itself off.
    /// </summary>
    internal sealed class IntegrationManager
    {
        private readonly ModContext _mod;
        private readonly IntegrationHost _host;
        private RuntimeModEnvironment _env;
        private Task<(ScanResult scan, List<MapLocation> maps)> _pending;
        private bool _announced;

        public IntegrationManager(ModContext mod)
        {
            _mod = mod;
            _host = new IntegrationHost(() => _mod.Config);
            _host.Register(new PublicApiModule(mod));
            _host.Register(new CoexistenceModule(mod));
            _host.Register(new PoliceModCompatModule(mod));
            _host.Register(new MapLocationsModule(this));
            _host.Register(new PhoneContactModule(mod, _host));
            _host.Register(new HotkeyCheckModule(this));
            _host.Register(new EntityGuardModule());
        }

        public IntegrationHost Host => _host;
        public ScanResult Scan => _host.LastScan;
        public List<MapLocation> Maps { get; private set; } = new List<MapLocation>();
        public bool Scanning => _pending != null && !_pending.IsCompleted;

        public string GtaRoot => Path.GetFullPath(Path.Combine(_mod.DataDir, "..", ".."));

        public void Initialize()
        {
            _env = new RuntimeModEnvironment(GtaRoot);
            if (_mod.Config.Integrations.Enabled && _mod.Config.Integrations.ScanOnStartup) Rescan();
            else _host.Apply(new ScanResult());
        }

        /// <summary>Starts a background scan (read-only). Results are applied on a later tick.</summary>
        public void Rescan()
        {
            if (Scanning || _env == null) return;
            var cfg = _mod.Config;
            var env = _env;
            _pending = Task.Run(() =>
            {
                var scan = ModScanner.Scan(env);
                CompatibilityAnalyzer.Analyze(scan, env, cfg);
                var extra = (cfg.Integrations.ExtraMapFolders ?? "").Split(';').Where(x => x.Trim().Length > 0).ToList();
                var maps = cfg.Integrations.MapLocations ? MapLocations.Find(env, extra) : new List<MapLocation>();
                return (scan, maps);
            });
        }

        /// <summary>Re-runs the analysis after a config change without touching the disk again.</summary>
        public void Reanalyze()
        {
            if (Scan == null || _env == null) return;
            try { CompatibilityAnalyzer.Analyze(Scan, _env, _mod.Config); }
            catch (Exception ex) { Log.Warn("[Integration] analysis failed: " + ex.Message); }
            _host.Apply(Scan);
        }

        public void Tick()
        {
            if (_pending != null && _pending.IsCompleted)
            {
                var task = _pending;
                _pending = null;
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    Maps = task.Result.maps;
                    _host.Apply(task.Result.scan);
                    LogScan(task.Result.scan);
                    Announce(task.Result.scan);
                }
                else
                {
                    Log.Warn("[Integration] mod scan failed: " + SecretRedactor.Redact(task.Exception?.GetBaseException().Message ?? "unknown"));
                    _host.Apply(new ScanResult());
                }
            }
            _host.Tick();
        }

        private void LogScan(ScanResult scan)
        {
            Log.Info("[Integration] " + scan.Mods.Count + " mod(s) detected: " + string.Join(", ", scan.Mods.Select(m => m.Name + (m.Version != null ? " " + m.Version : ""))));
            foreach (var i in scan.Issues) Log.Info("[Integration] " + i.Severity + ": " + i.Text);
            foreach (var s in _host.States) Log.Info("[Integration] " + s.Module.Id + ": " + s.Status + (s.Reason != null ? " (" + s.Reason + ")" : ""));
        }

        /// <summary>One toast per session for the things the player should act on.</summary>
        private void Announce(ScanResult scan)
        {
            if (_announced) return;
            _announced = true;
            int warnings = scan.Issues.Count(i => i.Severity != IssueSeverity.Info);
            if (warnings > 0)
                _mod.Ui.Notify.Push(LivingWorldSystemFmt("{0} mod compatibility warning(s). See Settings > Mods & integrations.", warnings), EventSeverity.Warning, 7000);
        }

        private static string LivingWorldSystemFmt(string f, params object[] a) => Core.Systems.LivingWorldSystem.Fmt(f, a);

        /// <summary>Applies a fix offered by an issue. Only ever changes this mod's own settings.</summary>
        public bool ApplyFix(CompatIssue issue)
        {
            if (issue?.Fix == null) return false;
            bool ok = false;
            if (issue.Fix.StartsWith("rebind:", StringComparison.Ordinal)) ok = CompatibilityAnalyzer.ApplyRebind(_mod.Config, issue.Fix);
            else if (issue.Fix == "coexistence:On") { _mod.Config.Integrations.Coexistence = Core.Config.CoexistenceMode.On; ok = true; }
            if (!ok) return false;
            _mod.SaveConfig();
            Reanalyze();
            _host.LogLine("fix", "applied " + issue.Fix);
            return true;
        }

        public void Shutdown()
        {
            _host.StopAll();
            ApiHub.Detach();
        }
    }

    // ====================================================================== integration modules

    /// <summary>Exposes <see cref="EmpireApi"/> to other scripts and runs their queued commands.</summary>
    internal sealed class PublicApiModule : IntegrationModule
    {
        private readonly ModContext _mod;
        public PublicApiModule(ModContext mod) { _mod = mod; }
        public override string Id => "api";
        public override string Name => "Public API for other scripts";
        public override string Description => "Other ScriptHookVDotNet mods can read faction, territory, battle and news data, subscribe to events and (if allowed) add factions to the living world.";
        public override IntegrationLevel Level => IntegrationLevel.Supported;
        public override string Unavailable(ScanResult scan) => _mod.Config.Integrations.PublicApi ? null : "Switched off in the ini (PublicApi).";

        public override void Start(ScanResult scan)
        {
            ApiHub.Attach(() => _mod.State, () => _mod.Config, ModContext.Version,
                handle => _mod.Members.IsMemberHandle(handle),
                (victim, killer) => _mod.ReportExternalKill(victim, killer),
                text => _mod.Ui.Notify.Push(text, EventSeverity.Info));
        }

        public override void Tick() => ApiHub.ProcessQueue();
        public override void Stop() => ApiHub.Detach();
        public override string Detail => _mod.Config.Integrations.AllowExternalCommands ? "Read + commands" : "Read-only (commands off)";
    }

    /// <summary>Trims this mod's own street spawns when another gang/war mod runs, so the two don't flood the streets.</summary>
    internal sealed class CoexistenceModule : IntegrationModule
    {
        private readonly ModContext _mod;
        private ScanResult _scan;
        public CoexistenceModule(ModContext mod) { _mod = mod; }
        public override string Id => "coexistence";
        public override string Name => "Coexistence with gang/war mods";
        public override string Description => "When Gang and Turf, Los Santos RED or a similar mod is installed: half as many battle fighters, no ambient crews or recruitment scenes, and no suppression of vanilla gang models.";
        public override IntegrationLevel Level => IntegrationLevel.Supported;

        public override void Start(ScanResult scan) { _scan = scan; Tick(); }
        public override void Tick() => _mod.LivingWorld.Coexistence = CompatibilityAnalyzer.Coexistence(_scan, _mod.Config);
        public override void Stop() => _mod.LivingWorld.Coexistence = false;
        public override string Detail => _mod.LivingWorld.Coexistence ? "On" : "Off";
    }

    /// <summary>With a police mod installed, law enforcement is never recruited into factions.</summary>
    internal sealed class PoliceModCompatModule : IntegrationModule
    {
        private readonly ModContext _mod;
        public PoliceModCompatModule(ModContext mod) { _mod = mod; }
        public override string Id => "police";
        public override string Name => "Police mod compatibility";
        public override string Description => "LSPD First Response / Los Santos RED detected: recruiting law enforcement is forced off so their officers are never pulled into a faction.";
        public override IntegrationLevel Level => IntegrationLevel.Supported;
        public override IEnumerable<string> RequiresAny => new[] { "lspdfr", "lsred" };
        public override void Start(ScanResult scan) => _mod.Sim.ForbidLawRecruitment = true;
        public override void Tick() => _mod.Sim.ForbidLawRecruitment = true; // survives new games / loads
        public override void Stop() => _mod.Sim.ForbidLawRecruitment = false;
        public override string Detail => _mod.Sim.ForbidLawRecruitment ? "Law enforcement recruitment blocked" : null;
    }

    /// <summary>Offers Map Editor / Menyoo map files as headquarters sites (the files are only read).</summary>
    internal sealed class MapLocationsModule : IntegrationModule
    {
        private readonly IntegrationManager _manager;
        public MapLocationsModule(IntegrationManager manager) { _manager = manager; }
        public override string Id => "maps";
        public override string Name => "Map mods as headquarters sites";
        public override string Description => "Reads Map Editor and Menyoo Spooner XML maps (scripts\\MapEditor, scripts\\MapLoaderAuto, menyooStuff\\Spooner and extra folders) and lists them on the Headquarters page.";
        public override IntegrationLevel Level => IntegrationLevel.Supported;
        public override string Unavailable(ScanResult scan) => _manager.Maps.Count == 0 ? "No map files found." : null;
        public override string Detail => _manager.Maps.Count + " map(s)";
    }

    /// <summary>Warns about hotkeys shared with other mods (see the issues list for one-click fixes).</summary>
    internal sealed class HotkeyCheckModule : IntegrationModule
    {
        private readonly IntegrationManager _manager;
        public HotkeyCheckModule(IntegrationManager manager) { _manager = manager; }
        public override string Id => "hotkeys";
        public override string Name => "Hotkey conflict check";
        public override string Description => "Compares this mod's keys with the defaults (or config files) of detected trainers and menus, e.g. Menyoo uses F8 like Quick Orders.";
        public override IntegrationLevel Level => IntegrationLevel.Supported;
        public override string Detail => (_manager.Scan?.Issues.Count(i => i.Kind == IssueKind.HotkeyConflict) ?? 0) + " conflict(s)";
    }

    /// <summary>Documents (and enforces in the recruitment/cleanup code) that other scripts' entities are left alone.</summary>
    internal sealed class EntityGuardModule : IntegrationModule
    {
        public override string Id => "entities";
        public override string Name => "Entity ownership guard";
        public override string Description => "NPCs and vehicles created by other scripts (mission entities) are never recruited, ordered, re-tasked or deleted by Organization Empire; it only cleans up what it spawned itself.";
        public override IntegrationLevel Level => IntegrationLevel.Supported;
        public override string Detail => "Always on";
    }
}

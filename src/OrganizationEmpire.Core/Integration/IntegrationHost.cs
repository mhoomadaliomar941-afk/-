using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Integration
{
    public enum IntegrationStatus { Idle, Active, Disabled, Unavailable, Failed }

    /// <summary>
    /// One optional integration. Implementations live in the game layer; the host guarantees that a failing
    /// integration is switched off on its own without affecting the rest of the mod.
    /// </summary>
    public abstract class IntegrationModule
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract IntegrationLevel Level { get; }
        /// <summary>Catalog ids of which at least one must be installed (empty = always available).</summary>
        public virtual IEnumerable<string> RequiresAny => Array.Empty<string>();
        /// <summary>Shown before the player enables an Experimental integration.</summary>
        public virtual string Warning => null;
        /// <summary>Extra availability check (e.g. a folder with map files). Return a reason when unavailable.</summary>
        public virtual string Unavailable(ScanResult scan) => null;
        public virtual void Start(ScanResult scan) { }
        public virtual void Tick() { }
        public virtual void Stop() { }
        /// <summary>One-line live status for the menu.</summary>
        public virtual string Detail => null;
    }

    public sealed class IntegrationState
    {
        public IntegrationModule Module;
        public IntegrationStatus Status = IntegrationStatus.Idle;
        public string Reason;
        public int Failures;
    }

    public sealed class IntegrationHost
    {
        public const int MaxFailures = 3;
        private readonly List<IntegrationState> _states = new List<IntegrationState>();
        private readonly Func<ModConfig> _config;
        private readonly List<string> _log = new List<string>();

        public IntegrationHost(Func<ModConfig> config) { _config = config; }

        public IReadOnlyList<IntegrationState> States => _states;
        public IReadOnlyList<string> RecentLog => _log;
        public ScanResult LastScan { get; private set; }

        public void Register(IntegrationModule module)
        {
            if (module == null || _states.Any(s => s.Module.Id == module.Id)) return;
            _states.Add(new IntegrationState { Module = module });
        }

        public IntegrationState Get(string id) => _states.FirstOrDefault(s => s.Module.Id == id);

        public bool IsActive(string id) => Get(id)?.Status == IntegrationStatus.Active;

        public bool IsDisabledByUser(string id)
        {
            string list = _config().Integrations.DisabledIntegrations ?? "";
            return list.Split(',').Any(x => string.Equals(x.Trim(), id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Applies a new scan: starts what became available, stops what was disabled or lost its dependency.</summary>
        public void Apply(ScanResult scan)
        {
            LastScan = scan;
            var cfg = _config();
            foreach (var s in _states)
            {
                string reason = null;
                IntegrationStatus want;
                if (!cfg.Integrations.Enabled) { want = IntegrationStatus.Disabled; reason = "All integrations are switched off."; }
                else if (IsDisabledByUser(s.Module.Id)) { want = IntegrationStatus.Disabled; reason = "Switched off."; }
                else if (s.Status == IntegrationStatus.Failed) { want = IntegrationStatus.Failed; reason = s.Reason; }
                else if ((reason = MissingDependency(s.Module, scan)) != null) want = IntegrationStatus.Unavailable;
                else
                {
                    try { reason = s.Module.Unavailable(scan); }
                    catch (Exception ex) { reason = SecretRedactor.Redact(ex.Message); }
                    want = reason != null ? IntegrationStatus.Unavailable : IntegrationStatus.Active;
                }

                if (want == IntegrationStatus.Active && s.Status != IntegrationStatus.Active)
                {
                    if (Run(s, () => s.Module.Start(scan), "start")) { s.Status = IntegrationStatus.Active; s.Reason = null; Note(s.Module.Id, "started"); }
                }
                else if (want != IntegrationStatus.Active)
                {
                    if (s.Status == IntegrationStatus.Active) { Run(s, s.Module.Stop, "stop"); Note(s.Module.Id, "stopped: " + reason); }
                    if (s.Status != IntegrationStatus.Failed) { s.Status = want; s.Reason = reason; }
                }
            }
        }

        private static string MissingDependency(IntegrationModule m, ScanResult scan)
        {
            var req = m.RequiresAny.ToList();
            if (req.Count == 0 || req.Any(id => scan != null && scan.Has(id))) return null;
            return "Needs " + string.Join(" or ", req.Select(id => ModCatalog.Get(id)?.Name ?? id)) + ".";
        }

        public void Tick()
        {
            foreach (var s in _states)
                if (s.Status == IntegrationStatus.Active) Run(s, s.Module.Tick, "tick");
        }

        public void StopAll()
        {
            foreach (var s in _states)
            {
                if (s.Status != IntegrationStatus.Active) continue;
                Run(s, s.Module.Stop, "stop");
                s.Status = IntegrationStatus.Idle;
            }
        }

        /// <summary>Runs integration code in isolation. Repeated failures switch only that integration off.</summary>
        public bool Run(IntegrationState s, Action action, string what)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                s.Failures++;
                string msg = SecretRedactor.Redact(ex.GetType().Name + ": " + ex.Message);
                Note(s.Module.Id, what + " failed (" + s.Failures + "/" + MaxFailures + "): " + msg);
                if (s.Failures >= MaxFailures || what == "start")
                {
                    if (s.Status == IntegrationStatus.Active) { try { s.Module.Stop(); } catch (Exception) { /* already failing */ } }
                    s.Status = IntegrationStatus.Failed;
                    s.Reason = "Stopped after an error: " + msg;
                }
                return false;
            }
        }

        /// <summary>Lets a failed integration try again (e.g. after the player fixed the other mod).</summary>
        public void Reset(string id)
        {
            var s = Get(id);
            if (s == null) return;
            s.Failures = 0;
            if (s.Status == IntegrationStatus.Failed) { s.Status = IntegrationStatus.Idle; s.Reason = null; }
        }

        public void SetEnabledByUser(string id, bool enabled)
        {
            var cfg = _config();
            var list = (cfg.Integrations.DisabledIntegrations ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            list.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            if (!enabled) list.Add(id);
            cfg.Integrations.DisabledIntegrations = string.Join(",", list);
            if (enabled) Reset(id);
        }

        private void Note(string id, string text)
        {
            string line = "[Integration:" + id + "] " + text;
            Log.Info(line);
            _log.Add(line);
            if (_log.Count > 40) _log.RemoveAt(0);
        }

        public void LogLine(string id, string text) => Note(id, text);
    }
}

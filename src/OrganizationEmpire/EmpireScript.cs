using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using GTA;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Persistence;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;
using OrganizationEmpire.World;

namespace OrganizationEmpire
{
    /// <summary>
    /// ScriptHookVDotNet v3 entry point. Every subsystem runs inside its own error guard: a failing subsystem is
    /// logged and, after repeated failures, disabled - it never takes the game (or the rest of the mod) down with it.
    /// </summary>
    [ScriptAttributes(Author = "Organization Empire", SupportURL = "")]
    public sealed class EmpireScript : Script
    {
        private ModContext _mod;
        private bool _initialized;
        private bool _initFailed;
        private int _nextStrategicTickAt;
        private int _nextAutosaveAt;
        private readonly Queue<KeyEventArgs> _keys = new Queue<KeyEventArgs>();
        private readonly Dictionary<string, int> _errors = new Dictionary<string, int>();
        private readonly HashSet<string> _disabled = new HashSet<string>();
        private readonly System.Diagnostics.Stopwatch _tickWatch = new System.Diagnostics.Stopwatch();
        public static float LastTickMs;

        public EmpireScript()
        {
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_keys.Count < 64) _keys.Enqueue(e);
        }

        private void TryInitialize()
        {
            if (!GtaUtil.PlayerReady() || Game.IsLoading || GTA.UI.Screen.IsFadedOut) return;
            try
            {
                string dataDir = Path.Combine(BaseDirectory, "OrganizationEmpire");
                _mod = new ModContext(dataDir);
                _mod.Initialize();
                CompatibilityChecks();
                _initialized = true;
                _nextStrategicTickAt = Game.GameTime + _mod.Config.Gameplay.StrategicTickSeconds * 1000;
                _nextAutosaveAt = Game.GameTime + Math.Max(1, _mod.Config.General.AutosaveMinutes) * 60000;
                string greeting = _mod.HasFaction
                    ? _mod.Faction.Name + " loaded. Press " + _mod.Config.Hotkeys.OpenMenu + " for the command menu."
                    : "Organization Empire ready. Press " + _mod.Config.Hotkeys.OpenMenu + " to found your faction.";
                _mod.Ui.Notify.Push(greeting, EventSeverity.Info);
                Log.Info("Initialization complete.");
            }
            catch (Exception ex)
            {
                _initFailed = true;
                Log.Error("Initialization failed", ex);
                GTA.UI.Notification.Show("~r~Organization Empire failed to start.~s~ See scripts\\OrganizationEmpire\\logs.");
            }
        }

        private void CompatibilityChecks()
        {
            try
            {
                var shvdn = typeof(Script).Assembly.GetName().Version;
                Log.Info("Game version: " + Game.Version + " | ScriptHookVDotNet: " + shvdn + " | .NET: " + Environment.Version);
                if (shvdn < new Version(3, 6, 0))
                {
                    Log.Warn("ScriptHookVDotNet " + shvdn + " is older than the tested 3.6.0. Some features may not work.");
                    _mod.Ui.Notify.Push("ScriptHookVDotNet " + shvdn + " detected; 3.6.0 or newer is recommended.", EventSeverity.Warning);
                }
                string probe = Path.Combine(_mod.DataDir, ".write_test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                if (!Native.KeyText.TryParseKey(_mod.Config.Hotkeys.OpenMenu, out _))
                {
                    _mod.Ui.Notify.Push("Invalid OpenMenu key '" + _mod.Config.Hotkeys.OpenMenu + "' in the ini; using F7.", EventSeverity.Warning);
                    _mod.Config.Hotkeys.OpenMenu = "F7";
                }
                foreach (string f in Directory.GetFiles(BaseDirectory, "*.dll"))
                {
                    string n = Path.GetFileName(f).ToLowerInvariant();
                    if (n.Contains("organizationempire") && n != "organizationempire.dll")
                        Log.Warn("Possible duplicate copy of this mod in scripts folder: " + n);
                }
            }
            catch (UnauthorizedAccessException)
            {
                _mod.Ui.Notify.Push("Cannot write to the scripts folder. Saves will fail - check folder permissions / run outside Program Files.", EventSeverity.Critical);
            }
            catch (Exception ex)
            {
                Log.Warn("Compatibility check error: " + ex.Message);
            }
        }

        private void Guard(string name, Action a)
        {
            if (_disabled.Contains(name)) return;
            try
            {
                a();
                if (_errors.ContainsKey(name)) _errors[name] = Math.Max(0, _errors[name] - 1);
            }
            catch (Exception ex)
            {
                _errors.TryGetValue(name, out int n);
                n += 10;
                _errors[name] = n;
                Log.Error("Subsystem '" + name + "' error", ex);
                if (n >= 60)
                {
                    _disabled.Add(name);
                    Log.Error("Subsystem '" + name + "' disabled after repeated errors.");
                    _mod?.Ui?.Notify.Push("Organization Empire: the '" + name + "' system hit repeated errors and was paused. See the log.", EventSeverity.Critical);
                }
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_initFailed) return;
            if (!_initialized)
            {
                TryInitialize();
                _keys.Clear();
                return;
            }
            _tickWatch.Restart();
            var mod = _mod;
            int now = Game.GameTime;

            if (mod.Relations.PlayerChanged())
            {
                Guard("relations", () => { mod.Relations.BindPlayer(); mod.Members.OnPlayerChanged(); });
            }

            while (_keys.Count > 0)
            {
                var k = _keys.Dequeue();
                Guard("input", () => mod.Ui.HandleKey(k));
            }

            bool cutscene = Game.IsCutsceneActive;
            if (!cutscene)
            {
                Guard("commands", () => mod.Commands.Update());
                Guard("members", () => mod.Members.Update());
                Guard("interaction", () => mod.Interaction.Update());
                Guard("headquarters", () => mod.Hq.Update());
                Guard("vehicles", () => mod.Vehicles.Update());
                Guard("territory", () => mod.Territories.Update());
                Guard("rivals", () => mod.Rivals.Update());
                Guard("missions", () => mod.Missions.Update());
            }
            Guard("dialogue", () => mod.Dialogue.Pump(mod.State, r => mod.Ui.OnDialogueResult(r)));

            if (mod.HasFaction && !Game.IsPaused && now >= _nextStrategicTickAt)
            {
                _nextStrategicTickAt = now + mod.Config.Gameplay.StrategicTickSeconds * 1000;
                Guard("simulation", () =>
                {
                    Simulation.Tick(mod.Sim, new WorldSignals { PlayerWantedLevel = Game.Player.WantedLevel });
                    mod.FlushNotifications();
                });
            }

            if (mod.HasFaction && mod.Config.General.AutosaveMinutes > 0 && now >= _nextAutosaveAt)
            {
                _nextAutosaveAt = now + mod.Config.General.AutosaveMinutes * 60000;
                if (!mod.Missions.IsActive)
                {
                    Guard("autosave", () =>
                    {
                        var r = mod.Save(string.IsNullOrEmpty(mod.State.SlotName) ? SaveManager.AutosaveSlot : mod.State.SlotName);
                        if (r.Ok) mod.Ui.Notify.Push("Autosaved.", EventSeverity.Info, 2500);
                    });
                }
            }

            Guard("ui", () => mod.Ui.Update(cutscene));
            _tickWatch.Stop();
            LastTickMs = (float)_tickWatch.Elapsed.TotalMilliseconds;
        }

        private void OnAborted(object sender, EventArgs e)
        {
            try
            {
                if (_mod != null && _initialized)
                {
                    if (_mod.HasFaction && !_mod.Missions.IsActive)
                    {
                        var r = _mod.Save(string.IsNullOrEmpty(_mod.State.SlotName) ? SaveManager.AutosaveSlot : _mod.State.SlotName);
                        Log.Info("Save on unload: " + r.Message);
                    }
                    _mod.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error during shutdown", ex);
            }
        }
    }
}

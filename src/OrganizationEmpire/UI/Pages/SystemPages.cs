using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrganizationEmpire.Core.AI;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Persistence;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;

namespace OrganizationEmpire.UI.Pages
{
    internal sealed class SettingsPage : Page
    {
        public override string Title => "Settings";
        public override string Subtitle => "Saved to scripts\\OrganizationEmpire\\OrganizationEmpire.ini";

        public override void Build(ItemList l)
        {
            l.Link("General & interface", () => new GeneralSettingsPage());
            l.Link("Hotkeys", () => new HotkeySettingsPage(), value: Mod.Config.Hotkeys.OpenMenu + " / " + Mod.Config.Hotkeys.QuickOrders + " / " + Mod.Config.Hotkeys.Interact + " / " + Mod.Config.Hotkeys.Talk);
            l.Link("Gameplay", () => new GameplaySettingsPage());
            l.Link("Spawning & performance", () => new SpawnSettingsPage());
            l.Link("World & factions", () => new WorldSettingsPage(), enabled: true);
            l.Link("AI dialogue", () => new AiSettingsPage(), value: Mod.Config.AI.Enabled ? Mod.Config.AI.Provider.ToString() : "disabled");
            l.Link("Mods & integrations", () => new ModsPage(), value: Mod.Integrations?.Scan != null ? Core.Systems.LivingWorldSystem.Fmt("{0} detected", Mod.Integrations.Scan.Mods.Count) : null);
            l.Link("Debug", () => new DebugSettingsPage());
            l.Spacer();
            l.Action("Reload settings from file", () =>
            {
                Mod.Config = ModConfig.LoadOrCreate(Mod.ConfigPath, out var warnings);
                Mod.Sim.Config = Mod.Config;
                Mod.RefreshAiKey();
                Ui.Notify.Push("Settings reloaded" + (warnings.Count > 0 ? " (" + warnings.Count + " warning(s), see log)" : "") + ".", EventSeverity.Info);
                foreach (var w in warnings) Log.Warn(w);
            }, "Use after editing the .ini by hand.");
        }
    }

    internal abstract class ConfigPage : Page
    {
        protected ModConfig C => Mod.Config;
        protected void Save() { Mod.SaveConfig(); }
    }

    internal sealed class GeneralSettingsPage : ConfigPage
    {
        public override string Title => "General & Interface";

        public override void Build(ItemList l)
        {
            var g = C.General;
            l.Header("Language");
            PageUtil.LanguageChoice(l, Mod);
            if (g.Language == UiLanguage.Arabic)
            {
                l.Toggle("Right-to-left layout", g.MirrorLayoutForArabic, v => { g.MirrorLayoutForArabic = v; Save(); }, "Mirror the menu so the sidebar is on the right.");
                l.Slider("Arabic text size", g.ArabicTextScale, 0.8f, 1.4f, 0.05f, v => { g.ArabicTextScale = v; Ui.Renderer.ClearTextCaches(); Save(); }, "0.00");
            }
            l.Header("Interface");
            l.Choice("While the menu is open", PageUtil.Name(g.PauseMode), d => { g.PauseMode = PageUtil.Next(g.PauseMode, d); Save(); }, "None keeps the world running (default). Slow motion or a full pause are optional.");
            l.Toggle("Block movement while menu is open", g.BlockMovementWhileMenuOpen, v => { g.BlockMovementWhileMenuOpen = v; Save(); });
            l.Toggle("Mouse support", g.EnableMouse, v => { g.EnableMouse = v; Save(); });
            l.Slider("Interface scale", g.UiScale, 0.75f, 1.25f, 0.05f, v => { g.UiScale = v; Nat.ClearTextCaches(); Save(); }, "0.00");
            l.Toggle("Show hints", g.ShowHints, v => { g.ShowHints = v; Save(); });
            l.Choice("Text input", PageUtil.Name(g.TextInput), d => { g.TextInput = PageUtil.Next(g.TextInput, d); Save(); }, "Inline typing follows your keyboard layout. Use the native keyboard if typing misbehaves.");
            l.Slider("Autosave every (minutes, 0 = off)", g.AutosaveMinutes, 0, 60, 5, v => { g.AutosaveMinutes = (int)v; Save(); }, "0");
            l.Toggle("Load last save on startup", g.AutoloadLastSave, v => { g.AutoloadLastSave = v; Save(); });
            l.Toggle("Stand down during story missions", g.StandDownDuringStoryMissions, v => { g.StandDownDuringStoryMissions = v; Save(); }, "Temporarily removes deployed members while a story mission runs so they can't break it.");
        }
    }

    internal sealed class HotkeySettingsPage : ConfigPage
    {
        public override string Title => "Hotkeys";

        public override void Build(ItemList l)
        {
            var h = C.Hotkeys;
            Row(l, "Open / close menu", h.OpenMenu, k => h.OpenMenu = k);
            Row(l, "Quick squad orders", h.QuickOrders, k => h.QuickOrders = k);
            Row(l, "Interact with NPC", h.Interact, k => h.Interact = k);
            Row(l, "Talk to NPC", h.Talk, k => h.Talk = k);
            l.Text("Menu navigation keys are fixed: arrows, Enter, Backspace, Tab, PgUp/PgDn, Esc. Mouse is optional.");
        }

        private void Row(ItemList l, string label, string current, Action<string> set)
        {
            l.Action(label, () => Ui.ShowDialog(new KeyCaptureDialog(label, k =>
            {
                string name = k.ToString();
                var all = new[] { C.Hotkeys.OpenMenu, C.Hotkeys.QuickOrders, C.Hotkeys.Interact, C.Hotkeys.Talk };
                if (all.Contains(name) && name != current) { Ui.Notify.Push(name + " is already used by another hotkey.", EventSeverity.Warning); return; }
                set(name);
                Save();
                Ui.Notify.Push(label + " = " + name, EventSeverity.Good);
            })), "Enter, then press the new key.", value: current);
        }
    }

    internal sealed class GameplaySettingsPage : ConfigPage
    {
        public override string Title => "Gameplay";

        public override void Build(ItemList l)
        {
            var g = C.Gameplay;
            l.Slider("Seconds per faction hour", g.StrategicTickSeconds, 10, 300, 5, v => { g.StrategicTickSeconds = (int)v; Save(); }, "0", "How fast the strategic simulation runs (income, loyalty, rivals...).");
            l.Slider("Income multiplier", g.IncomeMultiplier, 0.25f, 5f, 0.25f, v => { g.IncomeMultiplier = v; Save(); });
            l.Slider("Progression multiplier", g.ProgressionMultiplier, 0.25f, 5f, 0.25f, v => { g.ProgressionMultiplier = v; Save(); });
            l.Toggle("Permanent death", g.PermanentDeath, v => { g.PermanentDeath = v; Save(); }, "Off: fallen members are always wounded instead of killed.");
            l.Toggle("Recruitment from the world", g.RecruitmentEnabled, v => { g.RecruitmentEnabled = v; Save(); });
            l.Toggle("Allow recruiting law enforcement", g.AllowRecruitingLawEnforcement, v => { g.AllowRecruitingLawEnforcement = v; Save(); });
            l.Slider("Retry after refusal (seconds)", g.RecruitRetryCooldownSeconds, 0, 600, 30, v => { g.RecruitRetryCooldownSeconds = (int)v; Save(); }, "0");
            l.Slider("Max roster size", g.MaxRosterSize, 5, 200, 5, v => { g.MaxRosterSize = (int)v; Save(); }, "0");
            l.Toggle("Suspicion / heat system", g.SuspicionEnabled, v => { g.SuspicionEnabled = v; Save(); });
            l.Toggle("Rival attacks on your territory", g.RivalAttacksEnabled, v => { g.RivalAttacksEnabled = v; Save(); });
            l.Toggle("Random faction events", g.RandomEventsEnabled, v => { g.RandomEventsEnabled = v; Save(); });
        }
    }

    internal sealed class SpawnSettingsPage : ConfigPage
    {
        public override string Title => "Spawning & Performance";

        public override void Build(ItemList l)
        {
            var s = C.Spawning;
            l.Slider("Max deployed members", s.MaxDeployedMembers, 0, 30, 1, v => { s.MaxDeployedMembers = (int)v; Save(); }, "0", "Members physically present at once. Lower this if you see stutter.");
            l.Slider("Max mission hostiles", s.MaxMissionHostiles, 2, 40, 1, v => { s.MaxMissionHostiles = (int)v; Save(); }, "0");
            l.Slider("Max faction vehicles out", s.MaxActiveFactionVehicles, 0, 10, 1, v => { s.MaxActiveFactionVehicles = (int)v; Save(); }, "0");
            l.Slider("Max HQ guards", s.MaxHqGuards, 0, 10, 1, v => { s.MaxHqGuards = (int)v; Save(); }, "0");
            l.Slider("Max rival patrol NPCs", s.MaxRivalPatrolPeds, 0, 12, 1, v => { s.MaxRivalPatrolPeds = (int)v; Save(); }, "0");
            l.Slider("Max street battle fighters", s.MaxBattlePeds, 0, 24, 1, v => { s.MaxBattlePeds = (int)v; Save(); }, "0", "Both sides together. 0 = battles near you stay off-screen.");
            l.Slider("Max rival crew members around you", s.MaxAmbientCrewPeds, 0, 8, 1, v => { s.MaxAmbientCrewPeds = (int)v; Save(); }, "0");
            l.Slider("Global entity cap", s.GlobalEntityCap, 20, 120, 5, v => { s.GlobalEntityCap = (int)v; Save(); }, "0", "Safety net for everything this mod spawns.");
            l.Info("Currently spawned by the mod", Mod.Entities.Total.ToString());
        }
    }

    internal sealed class WorldSettingsPage : ConfigPage
    {
        public override string Title => "World & Factions";
        public override string Subtitle => "Removing a faction affects this mod's simulation; GTA's own files are never modified.";

        public override void Build(ItemList l)
        {
            var w = C.World;
            l.Header("Display");
            l.Toggle("Territory map overlay", w.ShowTerritoryOverlay, v => { w.ShowTerritoryOverlay = v; Save(); Mod.Territories.ForceRefresh(); });
            l.Toggle("Member blips", w.ShowMemberBlips, v => { w.ShowMemberBlips = v; Save(); Mod.Members.RefreshBlips(); });
            l.Toggle("HQ flag pole", w.ShowHeadquartersFlag, v => { w.ShowHeadquartersFlag = v; Save(); Mod.Hq.Refresh(true); });
            l.Header("Living world");
            l.Toggle("Living world", w.LivingWorld, v => { w.LivingWorld = v; Save(); if (!v) Mod.LivingWorld.DespawnAll(true); },
                "Rival factions recruit, arm, expand, fight each other, form alliances and change leaders on their own.");
            l.Choice("World news notifications", PageUtil.Name(w.LivingWorldNotifications), d => { w.LivingWorldNotifications = PageUtil.Next(w.LivingWorldNotifications, d); Save(); });
            l.Toggle("Street battles happen for real", w.PhysicalBattles, v => { w.PhysicalBattles = v; Save(); },
                "Near you, rival-vs-rival battles are fought by spawned fighters; their losses decide the battle.");
            l.Toggle("Battles on the map", w.ShowWorldEventsOnMap, v => { w.ShowWorldEventsOnMap = v; Save(); });
            l.Toggle("Rival crews and recruitment scenes", w.AmbientFactionCrews, v => { w.AmbientFactionCrews = v; Save(); });
            l.Toggle("Rival leaders appear in the world", w.RivalLeadersInWorld, v => { w.RivalLeadersInWorld = v; Save(); });
            l.Header("Rival behavior");
            l.Slider("Rival activity", w.RivalActivity, 0f, 2f, 0.1f, v => { w.RivalActivity = v; Save(); }, "0.0", "0 = passive rivals (no expansion or attacks).");
            l.Slider("Rival hostility growth", w.RivalHostility, 0f, 2f, 0.1f, v => { w.RivalHostility = v; Save(); }, "0.0");
            l.Toggle("Rival patrols in enemy territory", w.RivalPatrols, v => { w.RivalPatrols = v; Save(); });
            l.Toggle("Suppress GTA gang NPCs of removed factions", w.SuppressVanillaPedsOfDisabledRivals, v => { w.SuppressVanillaPedsOfDisabledRivals = v; Save(); Mod.Rivals.ApplySuppression(); },
                "Runtime only and fully reversible: stops GTA from spawning that gang's ambient models while the faction is removed. Nothing is deleted from game files.");
            if (!Mod.HasFaction) { l.Text("Create or load a faction to customize the factions in your world."); return; }
            l.Header("Factions in this world");
            foreach (var r in Mod.State.World.Rivals)
            {
                var rv = r;
                l.Toggle(r.Name + (r.Eliminated ? " (eliminated)" : ""), r.Enabled, v => { WarSystem.SetRivalEnabled(Mod.Sim, rv, v); Mod.Rivals.ApplySuppression(); Mod.Territories.ForceRefresh(); }, rv.Description);
            }
            l.Action("Remove ALL rival factions", () => Ui.Confirm("Remove all rivals?", "Every rival faction is removed from this world's simulation (reversible with 'Restore default factions').", () =>
            {
                foreach (var r in Mod.State.World.Rivals) WarSystem.SetRivalEnabled(Mod.Sim, r, false);
                Mod.Rivals.ApplySuppression();
                Mod.Territories.ForceRefresh();
                Ui.Notify.Push("All rival factions removed.", EventSeverity.Warning);
            }, true), danger: true);
            l.Action("Restore default factions", () =>
            {
                WorldFactory.RestoreDefaultRivals(Mod.State.World);
                Mod.Rivals.ApplySuppression();
                Mod.Territories.ForceRefresh();
                Ui.Notify.Push("Default factions restored.", EventSeverity.Good);
            }, "Re-enables every default faction and revives eliminated ones in weakened form.");
        }
    }

    internal sealed class AiSettingsPage : ConfigPage
    {
        private Task<ChatResult> _test;
        private CancellationTokenSource _testCts;
        private string _testResult;

        public override string Title => "AI Dialogue Settings";
        public override string Subtitle => "Key source: " + Mod.ApiKeySource + " | " + (string.IsNullOrEmpty(Mod.ResolvedApiKey) ? "no key" : SecretRedactor.Mask(Mod.ResolvedApiKey));

        public override void Build(ItemList l)
        {
            var a = C.AI;
            if (_test != null && _test.IsCompleted)
            {
                var r = _test.Status == TaskStatus.RanToCompletion ? _test.Result : ChatResult.Fail(AiErrorKind.Unknown, _test.Exception?.GetBaseException().Message ?? "failed");
                _testResult = r.Success ? "OK (" + r.LatencyMs + " ms" + (r.ServedModel != null ? ", " + r.ServedModel : "") + "): " + TextUtil.Truncate(r.Text, 60) : r.UserFacingError() + (r.ErrorMessage != null && r.Error != AiErrorKind.NotConfigured ? " " + r.ErrorMessage : "");
                Ui.Notify.Push("AI test: " + _testResult, r.Success ? EventSeverity.Good : EventSeverity.Bad);
                _test = null;
            }
            l.Header("Provider");
            l.Toggle("AI dialogue enabled", a.Enabled, v => { a.Enabled = v; Save(); }, "Disabled = the built-in offline dialogue engine is always used.");
            l.Action("Apply a provider preset...", () =>
            {
                var opts = ProviderPresets.All.Select(p => new PickOption(p.Name, p.Notes + (p.Endpoint.Length > 0 ? "  Endpoint: " + p.Endpoint : ""))).ToList();
                Ui.ShowPicker("Provider preset", opts, i =>
                {
                    var p = ProviderPresets.All[i];
                    a.Provider = p.Kind;
                    a.Endpoint = p.Endpoint;
                    if (!string.IsNullOrEmpty(p.Model) || p.Kind == AiProviderKind.Offline) a.Model = p.Model;
                    Save();
                    Ui.Notify.Push(p.Name + " selected." + (p.NeedsKey ? " Set your API key next." : "") + (string.IsNullOrEmpty(a.Model) && p.Kind != AiProviderKind.Offline ? " Enter a model name." : ""), EventSeverity.Info);
                });
            }, "OpenAI, Anthropic, Gemini, OpenRouter, Groq, Ollama, LM Studio... Endpoint and model stay editable.");
            l.Choice("Provider type", PageUtil.Name(a.Provider), d => { a.Provider = PageUtil.Next(a.Provider, d); Save(); });
            l.Action("Endpoint URL", () => Ui.AskText("Endpoint URL", a.Endpoint, 200, s => { a.Endpoint = s; Save(); }), "Base URL of the API.", value: string.IsNullOrEmpty(a.Endpoint) ? "(default)" : TextUtil.Truncate(a.Endpoint, 34));
            l.Action("Model", () => Ui.AskText("Model name", a.Model, 120, s => { a.Model = s; Save(); }), "Exactly as your provider names it.", value: string.IsNullOrEmpty(a.Model) ? "(not set)" : a.Model);

            l.Header("API key");
            l.Action("Set API key (plain text)", () => Ui.AskText("API key", "", 300, s => { a.ApiKey = s.Trim(); Save(); Mod.RefreshAiKey(); Ui.Notify.Push("API key saved.", EventSeverity.Good); }, "Stored in the .ini. It is never written to logs.", null, true),
                "Keys stored on your PC can't be fully secured. Prefer an environment variable or encryption, and use a key with a spending limit.", value: string.IsNullOrEmpty(Mod.ResolvedApiKey) ? "not set" : SecretRedactor.Mask(Mod.ResolvedApiKey));
            l.Action("Use environment variable...", () => Ui.AskText("Environment variable name", "", 64, s => { a.ApiKey = "env:" + s.Trim(); Save(); Mod.RefreshAiKey(); Ui.Notify.Push(string.IsNullOrEmpty(Mod.ResolvedApiKey) ? "Variable is empty or not set." : "Key read from environment.", EventSeverity.Info); }),
                "e.g. OPENAI_API_KEY or ANTHROPIC_API_KEY. The key never touches the .ini.");
            l.Action("Encrypt stored key (Windows DPAPI)", () =>
            {
                if (string.IsNullOrEmpty(Mod.ResolvedApiKey)) { Ui.Notify.Push("Set a key first.", EventSeverity.Warning); return; }
                if (!Mod.KeyProtector.Available) { Ui.Notify.Push("Encryption is only available on Windows.", EventSeverity.Warning); return; }
                try { a.ApiKey = "dpapi:" + Mod.KeyProtector.Protect(Mod.ResolvedApiKey); Save(); Mod.RefreshAiKey(); Ui.Notify.Push("Key encrypted for your Windows user account.", EventSeverity.Good); }
                catch (Exception ex) { Ui.Notify.Push("Encryption failed: " + ex.Message, EventSeverity.Bad); }
            }, "Encrypts the key so it's unreadable in the .ini and only decryptable by your Windows account.", a.ApiKey != null && !a.ApiKey.StartsWith("dpapi:") && !a.ApiKey.StartsWith("env:"), disabledReason: "Key is already protected or from the environment.");
            l.Action("Remove API key", () => { a.ApiKey = ""; Save(); Mod.RefreshAiKey(); }, danger: true);

            l.Header("Test");
            l.Action(_test != null ? "Testing..." : "Test connection", () =>
            {
                _testCts?.Cancel();
                _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(a.TimeoutSeconds + 5));
                _testResult = null;
                _test = Mod.Dialogue.TestConnectionAsync(_testCts.Token);
            }, "Sends a tiny request in the background (the game never freezes).", _test == null, _testResult ?? "", disabledReason: "Test in progress...");

            l.Header("Generation");
            l.Slider("Temperature (-1 = don't send)", a.Temperature, -1f, 2f, 0.1f, v => { a.Temperature = Math.Abs(v + 1f) < 0.05f ? -1f : Math.Max(0, v); Save(); }, "0.0", "Not sent to Anthropic.");
            l.Slider("Max response tokens", a.MaxResponseTokens, 32, 1024, 16, v => { a.MaxResponseTokens = (int)v; Save(); }, "0");
            l.Slider("Max reply words", a.MaxReplyWords, 10, 200, 5, v => { a.MaxReplyWords = (int)v; Save(); }, "0");
            l.Slider("Timeout (seconds)", a.TimeoutSeconds, 5, 120, 5, v => { a.TimeoutSeconds = (int)v; Save(); }, "0");
            l.Slider("Min seconds between requests", a.MinSecondsBetweenRequests, 0, 10, 0.5f, v => { a.MinSecondsBetweenRequests = v; Save(); }, "0.0");
            if (a.Provider == AiProviderKind.Anthropic)
            {
                var efforts = new[] { "low", "medium", "high" };
                l.Choice("Anthropic effort", efforts, a.AnthropicEffort, e => { a.AnthropicEffort = e; Save(); }, null, "Low is fastest and cheapest for short dialogue.");
                l.Toggle("Anthropic server-side fallbacks", a.AnthropicServerFallbacks, v => { a.AnthropicServerFallbacks = v; Save(); }, "If the model declines, the API retries on a fallback model.");
            }
            if (a.Provider == AiProviderKind.OpenAICompatible)
            {
                var headers = new[] { "Authorization", "api-key" };
                l.Choice("Auth header", headers, a.AuthHeader, hh => { a.AuthHeader = hh; Save(); }, null, "'api-key' for Azure OpenAI; 'Authorization' (Bearer) for everything else.");
            }

            l.Header("Memory");
            l.Slider("Dialogue turns sent per request", a.MemoryTurns, 0, 20, 1, v => { a.MemoryTurns = (int)v; Save(); }, "0", "More turns = more context and more tokens.");
            l.Slider("Long-term memories per NPC", a.MaxMemoryEntries, 0, 100, 5, v => { a.MaxMemoryEntries = (int)v; Save(); }, "0");
            l.Slider("Memories included per request", a.MemoriesPerRequest, 0, 20, 1, v => { a.MemoriesPerRequest = (int)v; Save(); }, "0");
            l.Toggle("Persistent memory (saved with the game)", a.PersistentMemory, v => { a.PersistentMemory = v; Save(); });
            l.Toggle("Radio conversations with distant members", a.AllowRadioChat, v => { a.AllowRadioChat = v; Save(); });
            l.Toggle("Conversations with non-members", a.AllowCivilianChat, v => { a.AllowCivilianChat = v; Save(); });
            l.Toggle("Persuasion improves recruitment odds", a.PersuasionAffectsRecruitment, v => { a.PersuasionAffectsRecruitment = v; Save(); }, "Never recruits automatically; only improves the next roll.");
            var personalities = new[] { "Random" }.Concat(Enum.GetNames(typeof(Personality))).ToList();
            l.Choice("Default personality (new NPCs)", personalities, a.DefaultPersonality, p => { a.DefaultPersonality = p; Save(); });
            if (Mod.HasFaction)
            {
                l.Action("Clear all conversation history", () => { MemoryStore.ClearDialogueOnly(Mod.State); Ui.Notify.Push("Cleared.", EventSeverity.Info); });
                l.Action("Clear all NPC memories", () => Ui.Confirm("Clear everything?", "All NPCs forget everything.", () => MemoryStore.ClearAll(Mod.State), true), danger: true);
            }
        }
    }

    internal sealed class DebugSettingsPage : ConfigPage
    {
        public override string Title => "Debug";

        public override void Build(ItemList l)
        {
            var d = C.Debug;
            l.Choice("Log level", PageUtil.Name(d.LogLevel), x => { d.LogLevel = PageUtil.Next(d.LogLevel, x); Save(); });
            l.Toggle("Debug overlay", d.ShowDebugOverlay, v => { d.ShowDebugOverlay = v; Save(); });
            l.Info("Log file", "logs\\OrganizationEmpire.log", Mod.LogPath);
            l.Info("Data folder", Mod.DataDir);
            l.Info("Entities spawned", Mod.Entities.Total.ToString());
            l.Info("Tick time", EmpireScript.LastTickMs.ToString("0.00") + " ms");
            l.Header("Recent log");
            foreach (var line in Log.RecentLines.Reverse().Take(12)) l.Text(line, Ui.Renderer.Theme.TextFaint);
        }
    }

    internal sealed class SaveLoadPage : Page
    {
        public override string Title => "Save / Load";
        public override string Subtitle => "Current slot: " + (string.IsNullOrEmpty(Mod.State.SlotName) ? "(none)" : Mod.State.SlotName);

        public override void Build(ItemList l)
        {
            l.Header("Save");
            if (Mod.HasFaction)
            {
                if (!string.IsNullOrEmpty(Mod.State.SlotName)) l.Action("Save to '" + Mod.State.SlotName + "'", () => Report(Mod.Save(Mod.State.SlotName)));
                l.Action("Save to a new slot...", () => Ui.AskText("Slot name", Mod.Faction.Name, 40, s =>
                {
                    string slot = SaveManager.SanitizeSlot(s);
                    if (Mod.Saves.Exists(slot)) Ui.Confirm("Overwrite?", "Slot '" + slot + "' exists. Overwrite it?", () => Report(Mod.Save(slot)), true, "Overwrite");
                    else Report(Mod.Save(slot));
                }));
            }
            else l.Text("Create a faction first to save.");
            if (Mod.Missions.IsActive) l.Text("Note: missions in progress are not saved; their entities are rebuilt only for completed results.", Ui.Renderer.Theme.Warn);

            l.Header("Slots");
            var slots = Mod.Saves.ListSlots();
            if (slots.Count == 0) l.Text("No saves yet.");
            foreach (var s in slots)
            {
                var slot = s;
                string label = (s.IsBackup ? "[backup] " : "") + s.Slot;
                string value = s.Corrupt ? "damaged" : (s.FactionName + (s.IsGovernment ? " (gov)" : "") + " | day " + s.Day);
                var it = l.Link(label, () => new SlotPage(slot), value: value);
                it.Hint = s.Corrupt ? "This file can't be read. A .bak copy may be recoverable by loading it." : s.FactionType + ", " + s.Members + " members. Saved " + s.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            l.Header("Info");
            l.Info("Folder", "scripts\\OrganizationEmpire\\saves", Mod.SavesDir);
            l.Text("GTA's own save system is separate: this mod stores its data in its own files and never touches your story saves.");
        }
    }

    internal sealed class SlotPage : Page
    {
        private readonly SaveSlotInfo _s;
        public SlotPage(SaveSlotInfo s) { _s = s; }
        public override string Title => "Slot: " + _s.Slot;

        public override void Build(ItemList l)
        {
            l.Info("Faction", _s.FactionName);
            l.Info("Type", _s.FactionType);
            l.Info("Day", _s.Day.ToString());
            l.Info("Saved", _s.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            l.Action("Load", () => Ui.Confirm("Load '" + _s.Slot + "'?", "Unsaved progress in the current session will be lost.", () =>
            {
                var r = Mod.Load(_s.Slot);
                Report(r);
                if (r.Ok) Ui.RebuildTabs();
            }));
            if (Mod.HasFaction) l.Action("Overwrite with current game", () => Ui.Confirm("Overwrite?", "Replace '" + _s.Slot + "' with the current game?", () => Report(Mod.Save(_s.Slot)), true, "Overwrite"));
            l.Action("Delete", () => Ui.Confirm("Delete '" + _s.Slot + "'?", "This can't be undone.", () => { Report(Mod.Saves.Delete(_s.Slot)); Ui.Back(); }, true, "Delete"), danger: true);
        }
    }
}

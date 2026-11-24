using System;
using System.IO;
using System.Linq;
using OrganizationEmpire.Core.AI;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Persistence;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Localization;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Missions;
using OrganizationEmpire.Native;
using OrganizationEmpire.UI;
using OrganizationEmpire.World;

namespace OrganizationEmpire
{
    /// <summary>Holds configuration, game state and every runtime service. One instance per script session.</summary>
    internal sealed class ModContext
    {
        public const string Version = "1.0.0";

        public string DataDir { get; }
        public string SavesDir => Path.Combine(DataDir, "saves");
        public string FlagsDir => Path.Combine(DataDir, "flags");
        public string ConfigPath => Path.Combine(DataDir, "OrganizationEmpire.ini");
        public string LogPath => Path.Combine(DataDir, "logs", "OrganizationEmpire.log");
        public string TextDir => Path.Combine(DataDir, "text");

        public ModConfig Config;
        public GameState State;
        public SimContext Sim;
        public SaveManager Saves;
        public Rng Rng = Rng.FromTime();
        public IKeyProtector KeyProtector = new DpapiKeyProtector();
        public string ResolvedApiKey = "";
        public string ApiKeySource = "none";

        public DialogueService Dialogue;
        public EntityTracker Entities;
        public RelationshipManager Relations;
        public MemberManager Members;
        public CommandSystem Commands;
        public InteractionController Interaction;
        public HeadquartersController Hq;
        public VehicleController Vehicles;
        public TerritoryController Territories;
        public RivalController Rivals;
        public MissionController Missions;
        public UiManager Ui;

        public ModContext(string dataDir)
        {
            DataDir = dataDir;
        }

        public FactionState Faction => State?.Faction;
        public bool HasFaction => State?.Faction != null;

        public void Initialize()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(SavesDir);
            Directory.CreateDirectory(FlagsDir);
            Config = ModConfig.LoadOrCreate(ConfigPath, out var warnings);
            Log.Initialize(LogPath, Config.Debug.LogLevel);
            Log.Info("==== GTA V: Organization Empire " + Version + " starting ====");
            foreach (var w in warnings) Log.Warn(w);
            RefreshAiKey();

            Saves = new SaveManager(SavesDir);
            Entities = new EntityTracker(() => Config);
            Relations = new RelationshipManager();
            Relations.Initialize();
            Members = new MemberManager(this);
            Commands = new CommandSystem(this);
            Interaction = new InteractionController(this);
            Hq = new HeadquartersController(this);
            Vehicles = new VehicleController(this);
            Territories = new TerritoryController(this);
            Rivals = new RivalController(this);
            Missions = new MissionController(this);
            Ui = new UiManager(this);
            ApplyLanguage(false);
            Dialogue = new DialogueService(() => Config.AI, () => ProviderFactory.Create(Config.AI, ResolvedApiKey));

            State = Simulation.NewGame(Environment.TickCount);
            Sim = new SimContext(State, Config, Rng);

            if (Config.General.AutoloadLastSave)
            {
                var latest = Saves.MostRecent();
                if (latest != null) Load(latest.Slot, false);
            }
        }

        private bool _arabicTableLoaded;

        /// <summary>Applies Config.General.Language. Arabic needs the glyph textures; without them the interface stays English.</summary>
        public void ApplyLanguage(bool notify)
        {
            var r = Ui.Renderer;
            if (r.Sprites == null)
            {
                r.Sprites = new SpriteText();
                if (r.Sprites.Load(TextDir)) Log.Info("Arabic glyph textures ready.");
                else Log.Warn("Arabic glyph textures unavailable: " + r.Sprites.Problem);
            }
            var lang = Config.General.Language;
            if (lang == UiLanguage.Arabic)
            {
                if (!_arabicTableLoaded)
                {
                    Loc.EnsureLoaded(Path.Combine(TextDir, "ar.txt"));
                    _arabicTableLoaded = true;
                    foreach (var p in Loc.LoadProblems) Log.Warn("Arabic strings: " + p);
                    Log.Info("Arabic strings: " + Loc.Arabic.ExactCount + " phrases, " + Loc.Arabic.TemplateCount + " templates.");
                }
                if (!r.SpritesReady)
                {
                    lang = UiLanguage.English;
                    Ui.Notify.Push("Arabic needs the scripts\\OrganizationEmpire\\text folder from the mod package (" + (r.Sprites.Problem ?? "missing") + "). Using English.", EventSeverity.Bad, 9000);
                }
            }
            Loc.SetLanguage(lang);
            Nat.ClearTextCaches();
            r.ClearTextCaches();
            if (notify) Ui.Notify.Push(Loc.IsArabic ? "تم تغيير اللغة إلى العربية." : "Language set to English.", EventSeverity.Good, 3000);
        }

        public void RefreshAiKey()
        {
            SecretRedactor.Clear();
            ResolvedApiKey = KeyResolver.Resolve(Config.AI.ApiKey, KeyProtector, out string src);
            ApiKeySource = src;
        }

        public void SaveConfig()
        {
            try
            {
                Config.Sanitize();
                Config.Save(ConfigPath);
                Log.MinLevel = Config.Debug.LogLevel;
            }
            catch (Exception ex)
            {
                Log.Error("Could not save configuration", ex);
                Ui?.Notify.Push("Could not save settings: " + ex.Message, EventSeverity.Bad);
            }
        }

        /// <summary>Moves simulation notifications into on-screen toasts.</summary>
        public void FlushNotifications()
        {
            if (Sim == null) return;
            foreach (var n in Sim.DrainNotifications()) Ui?.Notify.Push(n.Text, n.Severity);
        }

        public OpResult Save(string slot)
        {
            if (!HasFaction) return OpResult.Fail("Nothing to save yet - create a faction first.");
            var r = Saves.Save(State, slot, Config.AI.PersistentMemory);
            return r;
        }

        public OpResult Load(string slot, bool notify = true)
        {
            var s = Saves.Load(slot, out string message);
            if (s == null) return OpResult.Fail(message);
            ResetWorld();
            State = s;
            Sim = new SimContext(State, Config, Rng);
            if (!Config.AI.PersistentMemory) State.Memories.Clear();
            Log.Info("Loaded slot '" + slot + "' (" + (s.Faction?.Name ?? "no faction") + ").");
            if (notify) Ui?.Notify.Push(message, EventSeverity.Info);
            return OpResult.Success(message);
        }

        /// <summary>Begins a brand-new game state (used when creating a faction from scratch).</summary>
        public void NewState()
        {
            ResetWorld();
            State = Simulation.NewGame(Environment.TickCount);
            Sim = new SimContext(State, Config, Rng);
        }

        /// <summary>Removes everything this mod placed in the world (used before load/new game and on shutdown).</summary>
        public void ResetWorld()
        {
            try { Missions?.Abort("Game state changed", false); } catch (Exception ex) { Log.Error("Mission abort failed", ex); }
            try { Members?.DespawnAll(); } catch (Exception ex) { Log.Error("Member despawn failed", ex); }
            try { Vehicles?.DespawnAll(); } catch (Exception ex) { Log.Error("Vehicle despawn failed", ex); }
            try { Rivals?.DespawnAll(); } catch (Exception ex) { Log.Error("Rival despawn failed", ex); }
            try { Hq?.Cleanup(); } catch (Exception ex) { Log.Error("HQ cleanup failed", ex); }
            try { Territories?.ClearBlips(); } catch (Exception ex) { Log.Error("Blip cleanup failed", ex); }
            try { Ui?.CloseChat(); } catch { /* ignore */ }
        }

        public void Shutdown()
        {
            Log.Info("Shutting down; cleaning up world entities.");
            ResetWorld();
            try { Rivals?.ReleaseSuppression(); } catch { /* ignore */ }
            try { Relations?.Reset(State?.World); } catch { /* ignore */ }
            try { Entities?.CleanupAll(true); } catch { /* ignore */ }
            try { Ui?.RestoreGameState(); } catch { /* ignore */ }
        }

        public int MemberCountDeployed => Members?.DeployedCount ?? 0;

        public bool IsMenuBlocking => Ui != null && (Ui.IsOpen || Ui.IsCapturingInput);
    }
}

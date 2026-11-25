using System;
using System.Collections.Generic;
using System.IO;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Config
{
    public enum MenuPauseMode { None, SlowMotion, Pause }
    public enum TextInputMode { Inline, NativeKeyboard }
    public enum AiProviderKind { Offline, OpenAICompatible, Anthropic, Gemini }
    public enum UiLanguage { English, Arabic }
    public enum WorldNotifyLevel { None, Major, All }
    public enum CoexistenceMode { Auto, On, Off }

    public sealed class GeneralSettings
    {
        [ConfigComment("Interface language: English or Arabic. Arabic is drawn with the mod's own glyph textures (text\\glyphs) because GTA V's fonts have no Arabic letters.")]
        public UiLanguage Language { get; set; } = UiLanguage.English;

        [ConfigComment("Arabic only: mirror the menu layout right-to-left (sidebar on the right).")]
        public bool MirrorLayoutForArabic { get; set; } = true;

        [ConfigComment("Arabic only: size multiplier for Arabic text (0.8 - 1.4).")]
        public float ArabicTextScale { get; set; } = 1.0f;

        [ConfigComment("What happens to the game world while the faction menu is open.")]
        public MenuPauseMode PauseMode { get; set; } = MenuPauseMode.None;

        [ConfigComment("Block walking/driving while the menu is open (camera and combat are always blocked).")]
        public bool BlockMovementWhileMenuOpen { get; set; } = false;

        [ConfigComment("Show a mouse cursor and allow clicking/scrolling in the menu.")]
        public bool EnableMouse { get; set; } = true;

        [ConfigComment("Menu scale multiplier (0.75 - 1.25).")]
        public float UiScale { get; set; } = 1.0f;

        [ConfigComment("Show control hints at the bottom of the menu and on-screen prompts.")]
        public bool ShowHints { get; set; } = true;

        [ConfigComment("Inline = type directly in the menu. NativeKeyboard = GTA's on-screen keyboard (use if your keyboard layout misbehaves).")]
        public TextInputMode TextInput { get; set; } = TextInputMode.Inline;

        [ConfigComment("Minutes between automatic saves (0 disables autosave).")]
        public int AutosaveMinutes { get; set; } = 10;

        [ConfigComment("Load the most recent save automatically when the game starts.")]
        public bool AutoloadLastSave { get; set; } = true;

        [ConfigComment("Temporarily stand down deployed members while a story mission is active (prevents breaking missions).")]
        public bool StandDownDuringStoryMissions { get; set; } = true;
    }

    public sealed class HotkeySettings
    {
        [ConfigComment("Open/close the faction menu. Uses System.Windows.Forms.Keys names (F7, F8, Home, NumPad5, ...).")]
        public string OpenMenu { get; set; } = "F7";

        [ConfigComment("Open the quick squad orders popup.")]
        public string QuickOrders { get; set; } = "F8";

        [ConfigComment("Interact with the NPC you are looking at (recruit, profile, orders).")]
        public string Interact { get; set; } = "E";

        [ConfigComment("Talk (AI dialogue) to the NPC you are looking at, or the nearest member.")]
        public string Talk { get; set; } = "T";
    }

    public sealed class GameplaySettings
    {
        [ConfigComment("Real seconds per strategic simulation tick (1 tick = 1 in-faction hour).")]
        public int StrategicTickSeconds { get; set; } = 45;

        [ConfigComment("Global income multiplier.")]
        public float IncomeMultiplier { get; set; } = 1.0f;

        [ConfigComment("Global XP / progression multiplier.")]
        public float ProgressionMultiplier { get; set; } = 1.0f;

        [ConfigComment("Members killed in action are lost permanently (otherwise they are wounded and recover).")]
        public bool PermanentDeath { get; set; } = true;

        [ConfigComment("Allow recruiting NPCs from the world.")]
        public bool RecruitmentEnabled { get; set; } = true;

        [ConfigComment("Allow police/army NPCs to be recruited (not recommended: can interfere with wanted levels).")]
        public bool AllowRecruitingLawEnforcement { get; set; } = false;

        [ConfigComment("Seconds before you can retry recruiting an NPC that refused.")]
        public int RecruitRetryCooldownSeconds { get; set; } = 120;

        [ConfigComment("Maximum roster size (recruited members, deployed or not).")]
        public int MaxRosterSize { get; set; } = 60;

        [ConfigComment("Enable the suspicion/heat system (raids, investigations).")]
        public bool SuspicionEnabled { get; set; } = true;

        [ConfigComment("Rival factions attack your territory.")]
        public bool RivalAttacksEnabled { get; set; } = true;

        [ConfigComment("Random faction events (internal conflicts, family events, defections...).")]
        public bool RandomEventsEnabled { get; set; } = true;
    }

    public sealed class SpawnSettings
    {
        [ConfigComment("Maximum recruited members physically present in the world at once (hard cap 30).")]
        public int MaxDeployedMembers { get; set; } = 12;

        [ConfigComment("Maximum hostile NPCs a mission may have alive at once.")]
        public int MaxMissionHostiles { get; set; } = 18;

        [ConfigComment("Maximum faction vehicles spawned at once.")]
        public int MaxActiveFactionVehicles { get; set; } = 4;

        [ConfigComment("Maximum guards spawned at the headquarters.")]
        public int MaxHqGuards { get; set; } = 4;

        [ConfigComment("Maximum ambient rival patrol NPCs spawned near you.")]
        public int MaxRivalPatrolPeds { get; set; } = 6;

        [ConfigComment("Absolute cap on all entities spawned by this mod (safety net).")]
        public int GlobalEntityCap { get; set; } = 70;

        [ConfigComment("Fighters spawned for a rival-vs-rival street battle near you (both sides together).")]
        public int MaxBattlePeds { get; set; } = 10;

        [ConfigComment("Rival gang members hanging out in their own territory near you.")]
        public int MaxAmbientCrewPeds { get; set; } = 4;
    }

    public sealed class WorldSettings
    {
        [ConfigComment("Show territory areas on the map/minimap.")]
        public bool ShowTerritoryOverlay { get; set; } = true;

        [ConfigComment("Show blips for deployed members.")]
        public bool ShowMemberBlips { get; set; } = true;

        [ConfigComment("Spawn a flag pole prop at the headquarters.")]
        public bool ShowHeadquartersFlag { get; set; } = true;

        [ConfigComment("Rival activity multiplier (0 = passive, 1 = default, 2 = very active).")]
        public float RivalActivity { get; set; } = 1.0f;

        [ConfigComment("Rival hostility growth multiplier.")]
        public float RivalHostility { get; set; } = 1.0f;

        [ConfigComment("Spawn ambient rival patrols in rival territory you are at war with.")]
        public bool RivalPatrols { get; set; } = true;

        [ConfigComment("When a rival faction is disabled, also suppress GTA's ambient peds of that gang's models (runtime only, fully reversible, no game files touched).")]
        public bool SuppressVanillaPedsOfDisabledRivals { get; set; } = true;

        [ConfigComment("Living world: rival factions recruit, arm, expand, fight each other, ally and change leaders on their own.")]
        public bool LivingWorld { get; set; } = true;

        [ConfigComment("Which living-world news become on-screen notifications: None, Major or All.")]
        public WorldNotifyLevel LivingWorldNotifications { get; set; } = WorldNotifyLevel.Major;

        [ConfigComment("Rival-vs-rival battles near you are fought for real by spawned fighters (their losses decide the battle).")]
        public bool PhysicalBattles { get; set; } = true;

        [ConfigComment("Show rival crews hanging out in their territory, and recruitment scenes when they recruit nearby.")]
        public bool AmbientFactionCrews { get; set; } = true;

        [ConfigComment("Mark rival-vs-rival battles on the map so you can find them.")]
        public bool ShowWorldEventsOnMap { get; set; } = true;

        [ConfigComment("Rival leaders can be found at their faction's home turf, guarded by bodyguards.")]
        public bool RivalLeadersInWorld { get; set; } = true;
    }

    public sealed class AiSettings
    {
        [ConfigComment("Enable AI-generated dialogue. When disabled (or Provider=Offline) a built-in offline dialogue engine is used.")]
        public bool Enabled { get; set; } = true;

        [ConfigComment("Offline needs no setup. OpenAICompatible works with OpenAI, OpenRouter, Groq, Mistral, DeepSeek, Together, LM Studio, Ollama (/v1), KoboldCpp, etc.")]
        public AiProviderKind Provider { get; set; } = AiProviderKind.Offline;

        [ConfigComment("Base URL. Examples: https://api.openai.com/v1 | https://openrouter.ai/api/v1 | http://localhost:11434/v1 | https://api.anthropic.com | https://generativelanguage.googleapis.com/v1beta")]
        public string Endpoint { get; set; } = "";

        [ConfigComment("Model name exactly as your provider expects it.")]
        public string Model { get; set; } = "";

        [ConfigComment("API key. Plain text, or env:VARIABLE_NAME to read an environment variable, or dpapi:... (written by the in-game 'Encrypt key' option, Windows only).\nWARNING: keys stored on your PC cannot be guaranteed secure. Use a key with a spending limit.")]
        public string ApiKey { get; set; } = "";

        [ConfigComment("Sampling temperature (0-2). Set to -1 to not send it (required for some models/providers; never sent to Anthropic).")]
        public float Temperature { get; set; } = 0.8f;

        [ConfigComment("Maximum tokens per NPC reply.")]
        public int MaxResponseTokens { get; set; } = 220;

        [ConfigComment("Soft limit on reply length in words (told to the model).")]
        public int MaxReplyWords { get; set; } = 70;

        [ConfigComment("Request timeout in seconds.")]
        public int TimeoutSeconds { get; set; } = 30;

        [ConfigComment("How many recent dialogue turns are sent with each request.")]
        public int MemoryTurns { get; set; } = 8;

        [ConfigComment("Maximum long-term memory entries kept per NPC.")]
        public int MaxMemoryEntries { get; set; } = 40;

        [ConfigComment("How many long-term memories are included in each request.")]
        public int MemoriesPerRequest { get; set; } = 6;

        [ConfigComment("Keep NPC memories in save files.")]
        public bool PersistentMemory { get; set; } = true;

        [ConfigComment("Allow talking to members who are not nearby (radio/phone).")]
        public bool AllowRadioChat { get; set; } = true;

        [ConfigComment("Allow AI conversations with non-member NPCs.")]
        public bool AllowCivilianChat { get; set; } = true;

        [ConfigComment("A convincing conversation can improve the next recruitment roll for that NPC (never auto-recruits).")]
        public bool PersuasionAffectsRecruitment { get; set; } = true;

        [ConfigComment("Personality used when an NPC profile has none: Random or a personality name.")]
        public string DefaultPersonality { get; set; } = "Random";

        [ConfigComment("Minimum seconds between AI requests (rate limiting).")]
        public float MinSecondsBetweenRequests { get; set; } = 1.5f;

        [ConfigComment("Anthropic only: effort level (low/medium/high). Low is fastest and cheapest for short dialogue.")]
        public string AnthropicEffort { get; set; } = "low";

        [ConfigComment("Anthropic only: let the API retry a declined request on a fallback model (server-side fallbacks).")]
        public bool AnthropicServerFallbacks { get; set; } = true;

        [ConfigComment("OpenAI-compatible only: header that carries the key. 'Authorization' (Bearer) for most, 'api-key' for Azure OpenAI.")]
        public string AuthHeader { get; set; } = "Authorization";
    }

    public sealed class DebugSettings
    {
        [ConfigComment("Debug, Info, Warning or Error.")]
        public LogLevel LogLevel { get; set; } = LogLevel.Info;

        [ConfigComment("Draw a small debug overlay (entity counts, tick timings).")]
        public bool ShowDebugOverlay { get; set; } = false;
    }

    /// <summary>Root configuration persisted as OrganizationEmpire.ini.</summary>
    public sealed class IntegrationSettings
    {
        [ConfigComment("Master switch for detecting other mods and running optional integrations.")]
        public bool Enabled { get; set; } = true;

        [ConfigComment("Scan installed mods when the game starts (read-only: other mods' files are never modified).")]
        public bool ScanOnStartup { get; set; } = true;

        [ConfigComment("Warn when another mod uses the same hotkey as this mod.")]
        public bool CheckHotkeyConflicts { get; set; } = true;

        [ConfigComment("Coexistence with other gang/war mods: Auto (reduce our extra spawns when one is detected), On or Off.")]
        public CoexistenceMode Coexistence { get; set; } = CoexistenceMode.Auto;

        [ConfigComment("Expose the public API (OrganizationEmpire.Api.EmpireApi) so other scripts can read faction data and events.")]
        public bool PublicApi { get; set; } = true;

        [ConfigComment("Allow other scripts to change game state through the API (add factions, notifications, report kills). Off = read-only.")]
        public bool AllowExternalCommands { get; set; } = false;

        [ConfigComment("If iFruitAddon2 is installed, add an 'Organization Empire' contact to the phone (experimental).")]
        public bool PhoneContact { get; set; } = true;

        [ConfigComment("Offer locations from installed Map Editor / Menyoo maps as headquarters sites (read-only).")]
        public bool MapLocations { get; set; } = true;

        [ConfigComment("Extra folders (relative to the GTA V folder, separated by ;) that contain map XML files.")]
        public string ExtraMapFolders { get; set; } = "";

        [ConfigComment("Integration ids switched off by the player, separated by commas (managed in-game).")]
        public string DisabledIntegrations { get; set; } = "";
    }

    public sealed class ModConfig
    {
        public GeneralSettings General { get; set; } = new GeneralSettings();
        public HotkeySettings Hotkeys { get; set; } = new HotkeySettings();
        public GameplaySettings Gameplay { get; set; } = new GameplaySettings();
        public SpawnSettings Spawning { get; set; } = new SpawnSettings();
        public WorldSettings World { get; set; } = new WorldSettings();
        public AiSettings AI { get; set; } = new AiSettings();
        public DebugSettings Debug { get; set; } = new DebugSettings();
        public IntegrationSettings Integrations { get; set; } = new IntegrationSettings();

        public const string Header =
            "GTA V: Organization Empire - configuration\n" +
            "Most options can also be changed in-game (default key F7 > Settings).\n" +
            "This file is rewritten when settings are changed in-game.";

        /// <summary>Clamps values to safe ranges so a bad edit can never cause runaway spawning.</summary>
        public void Sanitize()
        {
            General.UiScale = MathEx.Clamp(General.UiScale, 0.75f, 1.25f);
            General.ArabicTextScale = MathEx.Clamp(General.ArabicTextScale, 0.8f, 1.4f);
            General.AutosaveMinutes = MathEx.Clamp(General.AutosaveMinutes, 0, 240);
            Gameplay.StrategicTickSeconds = MathEx.Clamp(Gameplay.StrategicTickSeconds, 10, 600);
            Gameplay.IncomeMultiplier = MathEx.Clamp(Gameplay.IncomeMultiplier, 0.1f, 10f);
            Gameplay.ProgressionMultiplier = MathEx.Clamp(Gameplay.ProgressionMultiplier, 0.1f, 10f);
            Gameplay.MaxRosterSize = MathEx.Clamp(Gameplay.MaxRosterSize, 5, 300);
            Gameplay.RecruitRetryCooldownSeconds = MathEx.Clamp(Gameplay.RecruitRetryCooldownSeconds, 0, 3600);
            Spawning.MaxDeployedMembers = MathEx.Clamp(Spawning.MaxDeployedMembers, 0, 30);
            Spawning.MaxMissionHostiles = MathEx.Clamp(Spawning.MaxMissionHostiles, 2, 40);
            Spawning.MaxActiveFactionVehicles = MathEx.Clamp(Spawning.MaxActiveFactionVehicles, 0, 10);
            Spawning.MaxHqGuards = MathEx.Clamp(Spawning.MaxHqGuards, 0, 10);
            Spawning.MaxRivalPatrolPeds = MathEx.Clamp(Spawning.MaxRivalPatrolPeds, 0, 12);
            Spawning.GlobalEntityCap = MathEx.Clamp(Spawning.GlobalEntityCap, 20, 120);
            Spawning.MaxBattlePeds = MathEx.Clamp(Spawning.MaxBattlePeds, 0, 24);
            Spawning.MaxAmbientCrewPeds = MathEx.Clamp(Spawning.MaxAmbientCrewPeds, 0, 8);
            if (Integrations.ExtraMapFolders == null) Integrations.ExtraMapFolders = "";
            if (Integrations.DisabledIntegrations == null) Integrations.DisabledIntegrations = "";
            World.RivalActivity = MathEx.Clamp(World.RivalActivity, 0f, 2f);
            World.RivalHostility = MathEx.Clamp(World.RivalHostility, 0f, 2f);
            AI.MaxResponseTokens = MathEx.Clamp(AI.MaxResponseTokens, 32, 2048);
            AI.MaxReplyWords = MathEx.Clamp(AI.MaxReplyWords, 10, 400);
            AI.TimeoutSeconds = MathEx.Clamp(AI.TimeoutSeconds, 5, 180);
            AI.MemoryTurns = MathEx.Clamp(AI.MemoryTurns, 0, 40);
            AI.MaxMemoryEntries = MathEx.Clamp(AI.MaxMemoryEntries, 0, 200);
            AI.MemoriesPerRequest = MathEx.Clamp(AI.MemoriesPerRequest, 0, 30);
            AI.MinSecondsBetweenRequests = MathEx.Clamp(AI.MinSecondsBetweenRequests, 0f, 30f);
            if (AI.Temperature > 2f) AI.Temperature = 2f;
            if (AI.Temperature < 0f && AI.Temperature != -1f) AI.Temperature = -1f;
            if (string.IsNullOrWhiteSpace(AI.AuthHeader)) AI.AuthHeader = "Authorization";
            if (string.IsNullOrWhiteSpace(AI.AnthropicEffort)) AI.AnthropicEffort = "low";
        }

        public static ModConfig LoadOrCreate(string path, out List<string> warnings)
        {
            warnings = new List<string>();
            var cfg = new ModConfig();
            try
            {
                if (File.Exists(path))
                {
                    var ini = IniFile.Parse(File.ReadAllText(path));
                    warnings.AddRange(ini.BindTo(cfg));
                }
                else
                {
                    warnings.Add("Configuration file not found; created defaults at " + path);
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Could not read configuration (" + ex.Message + "); using defaults.");
                cfg = new ModConfig();
            }
            cfg.Sanitize();
            try { cfg.Save(path); } catch (Exception ex) { warnings.Add("Could not write configuration: " + ex.Message); }
            return cfg;
        }

        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, IniFile.Write(this, Header));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}

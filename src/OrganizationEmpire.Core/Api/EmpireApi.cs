using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Api
{
    // ------------------------------------------------------------------ data transfer objects (stable, read-only copies)

    public sealed class ApiRival
    {
        public string Id, Name, Kind, LeaderName, LeaderTitle, RelationToPlayer, Goal;
        public float Strength, Wealth, Hostility;
        public int Manpower;
        public bool External;
        public string[] Territories;
    }

    public sealed class ApiTerritory
    {
        public string Id, Name, OwnerId, OwnerName, Type;
        public string[] Zones;
        public float X, Y, Z;
        public bool Contested;
    }

    public sealed class ApiBattle
    {
        public string Id, TerritoryId, AttackerId, DefenderId, Status;
        public float AttackerForce, DefenderForce;
    }

    public sealed class ApiNews
    {
        public int Day, Hour;
        public string Kind, Text, TerritoryId;
        public string[] FactionIds;
    }

    public sealed class ApiMemberEvent
    {
        public string MemberId, Name, Kind;
    }

    public sealed class ApiMissionEvent
    {
        public string MissionId, Title;
        public bool Success;
    }

    /// <summary>Description of a faction another mod adds to the living world.</summary>
    public sealed class ExternalFactionSpec
    {
        public string Id;
        public string Name;
        public string Color = "#888888";
        public string[] PedModels;
        public string Weapon = "WEAPON_PISTOL";
        public string[] HomeTerritories;
        public float Strength = 40;
        public string Description;
    }

    /// <summary>
    /// Public API of Organization Empire for other ScriptHookVDotNet scripts (reference OrganizationEmpire.dll or use
    /// reflection on "OrganizationEmpire.Api.EmpireApi, OrganizationEmpire"). Queries and events are always available
    /// while <see cref="IsReady"/>; commands that change the game need Integrations.AllowExternalCommands = true.
    /// Event handlers run on the script thread; a handler that throws 3 times is unsubscribed (see the log).
    /// </summary>
    public static class EmpireApi
    {
        public const string ApiVersion = "1.0";

        public static bool IsReady => ApiHub.State != null && (ApiHub.Config?.Integrations.PublicApi ?? false);
        public static string ModVersion => ApiHub.ModVersion;

        // ---- queries

        public static bool HasFaction => IsReady && ApiHub.State().Faction != null;
        public static string FactionName => IsReady ? ApiHub.State().Faction?.Name : null;
        public static string FactionType => IsReady ? ApiHub.State().Faction?.Type.ToString() : null;
        public static int MemberCount => IsReady ? ApiHub.State().Faction?.LivingMemberCount ?? 0 : 0;

        /// <summary>True when the ped (by handle) is a deployed member of the player's faction.</summary>
        public static bool IsFactionMember(int pedHandle) => IsReady && ApiHub.IsMemberPed != null && ApiHub.IsMemberPed(pedHandle);

        public static ApiTerritory GetTerritoryByZone(string zoneCode)
        {
            if (!IsReady || string.IsNullOrEmpty(zoneCode)) return null;
            var w = ApiHub.State().World;
            return ToApi(w.FindByZone(zoneCode));
        }

        public static ApiTerritory[] GetTerritories() => IsReady ? ApiHub.State().World.Territories.Select(ToApi).ToArray() : new ApiTerritory[0];

        public static ApiRival[] GetRivals() => IsReady ? ApiHub.State().World.Rivals.Where(r => r.Active).Select(ToApi).ToArray() : new ApiRival[0];

        public static ApiBattle[] GetActiveBattles() => IsReady ? (ApiHub.State().World.Living?.ActiveBattles ?? Enumerable.Empty<WorldBattle>()).Select(ToApi).ToArray() : new ApiBattle[0];

        public static ApiNews[] GetRecentNews(int max = 10)
        {
            if (!IsReady) return new ApiNews[0];
            var news = ApiHub.State().World.Living?.News ?? new List<WorldNews>();
            return news.Skip(Math.Max(0, news.Count - Math.Max(1, max))).Select(ToApi).ToArray();
        }

        // ---- events

        public static event Action<ApiNews> NewsPosted;
        public static event Action<ApiBattle> BattleStarted;
        public static event Action<ApiBattle> BattleEnded;
        public static event Action<ApiTerritory> TerritoryChanged;
        public static event Action<ApiMemberEvent> MemberRecruited;
        public static event Action<ApiMemberEvent> MemberLost;
        public static event Action<ApiMissionEvent> MissionFinished;

        // ---- commands (queued, executed on Organization Empire's next tick)

        /// <summary>Adds a faction to the living world (it recruits, expands, fights and gets a leader like the built-in ones).</summary>
        public static bool RegisterFaction(ExternalFactionSpec spec, out string error)
        {
            if (!CanCommand(out error)) return false;
            if (spec == null || string.IsNullOrWhiteSpace(spec.Id) || string.IsNullOrWhiteSpace(spec.Name)) { error = "Id and Name are required."; return false; }
            string id = "ext_" + new string(spec.Id.Where(char.IsLetterOrDigit).Take(24).ToArray()).ToLowerInvariant();
            if (id.Length <= 4) { error = "Id must contain letters or digits."; return false; }
            string caller = CallerName();
            ApiHub.Enqueue(() => ApiHub.AddExternalFaction(id, spec, caller));
            error = null;
            return true;
        }

        /// <summary>Reports that a ped was killed (used for territory influence and war score like kills seen by the mod).</summary>
        public static bool ReportKill(int victimPedHandle, int killerPedHandle)
        {
            if (!CanCommand(out _) || ApiHub.ReportKill == null) return false;
            ApiHub.Enqueue(() => ApiHub.ReportKill(victimPedHandle, killerPedHandle));
            return true;
        }

        /// <summary>Shows a notification in the mod's interface (plain text, max 200 characters).</summary>
        public static bool ShowNotification(string text)
        {
            if (!CanCommand(out _) || ApiHub.Notify == null || string.IsNullOrWhiteSpace(text)) return false;
            string t = TextUtil.SafeForGame(text, 200);
            ApiHub.Enqueue(() => ApiHub.Notify(t));
            return true;
        }

        private static bool CanCommand(out string error)
        {
            if (!IsReady) { error = "Organization Empire is not ready or its API is switched off."; return false; }
            if (!ApiHub.Config.Integrations.AllowExternalCommands) { error = "External commands are disabled (Settings > Mods & integrations)."; return false; }
            error = null;
            return true;
        }

        private static string CallerName()
        {
            try
            {
                var frame = new StackTrace().GetFrames()?.Select(f => f.GetMethod()?.DeclaringType?.Assembly).FirstOrDefault(a => a != null && a != typeof(EmpireApi).Assembly);
                return frame?.GetName().Name ?? "unknown";
            }
            catch (Exception) { return "unknown"; }
        }

        // ---- conversions

        internal static ApiRival ToApi(RivalFaction r) => r == null ? null : new ApiRival
        {
            Id = r.Id, Name = r.Name, Kind = r.Kind.ToString(), LeaderName = r.Leader?.Name, LeaderTitle = r.Leader?.Title,
            RelationToPlayer = r.Relation.ToString(), Goal = r.Goal.ToString(), Strength = r.Strength, Wealth = r.Wealth,
            Hostility = r.Hostility, Manpower = r.Manpower, External = r.External,
            Territories = ApiHub.State().World.Territories.Where(t => t.OwnerId == r.Id).Select(t => t.Id).ToArray(),
        };

        internal static ApiTerritory ToApi(Territory t)
        {
            if (t == null) return null;
            var s = ApiHub.State();
            return new ApiTerritory
            {
                Id = t.Id, Name = t.Name, OwnerId = t.OwnerId, OwnerName = s.World.FactionName(t.OwnerId, s.Faction), Type = t.Type.ToString(),
                Zones = t.Zones.ToArray(), X = t.Center.X, Y = t.Center.Y, Z = t.Center.Z, Contested = t.Contested,
            };
        }

        internal static ApiBattle ToApi(WorldBattle b) => b == null ? null : new ApiBattle
        {
            Id = b.Id, TerritoryId = b.TerritoryId, AttackerId = b.AttackerId, DefenderId = b.DefenderId, Status = b.Status.ToString(),
            AttackerForce = b.AttackerForce, DefenderForce = b.DefenderForce,
        };

        internal static ApiNews ToApi(WorldNews n) => n == null ? null : new ApiNews
        {
            Day = n.Day, Hour = n.Hour, Kind = n.Kind.ToString(), Text = n.Text, TerritoryId = n.TerritoryId, FactionIds = n.FactionIds.ToArray(),
        };

        // ---- raising (called by ApiHub)

        internal static void RaiseNews(ApiNews n) => ApiHub.Dispatch(NewsPosted, n, nameof(NewsPosted), d => NewsPosted -= (Action<ApiNews>)d);
        internal static void RaiseBattleStarted(ApiBattle b) => ApiHub.Dispatch(BattleStarted, b, nameof(BattleStarted), d => BattleStarted -= (Action<ApiBattle>)d);
        internal static void RaiseBattleEnded(ApiBattle b) => ApiHub.Dispatch(BattleEnded, b, nameof(BattleEnded), d => BattleEnded -= (Action<ApiBattle>)d);
        internal static void RaiseTerritoryChanged(ApiTerritory t) => ApiHub.Dispatch(TerritoryChanged, t, nameof(TerritoryChanged), d => TerritoryChanged -= (Action<ApiTerritory>)d);
        internal static void RaiseMemberRecruited(ApiMemberEvent e) => ApiHub.Dispatch(MemberRecruited, e, nameof(MemberRecruited), d => MemberRecruited -= (Action<ApiMemberEvent>)d);
        internal static void RaiseMemberLost(ApiMemberEvent e) => ApiHub.Dispatch(MemberLost, e, nameof(MemberLost), d => MemberLost -= (Action<ApiMemberEvent>)d);
        internal static void RaiseMissionFinished(ApiMissionEvent e) => ApiHub.Dispatch(MissionFinished, e, nameof(MissionFinished), d => MissionFinished -= (Action<ApiMissionEvent>)d);
    }

    /// <summary>
    /// Glue between the simulation and <see cref="EmpireApi"/>: the game layer attaches the live state and services,
    /// Core systems publish events here, and external commands are queued until the mod's own tick.
    /// </summary>
    public static class ApiHub
    {
        public const int MaxHandlerFailures = 3;
        internal static Func<GameState> State;
        internal static Func<ModConfig> ConfigSource;
        internal static ModConfig Config => ConfigSource?.Invoke();
        internal static string ModVersion = "1.0.0";
        internal static Func<int, bool> IsMemberPed;
        internal static Action<int, int> ReportKill;
        internal static Action<string> Notify;
        private static readonly Queue<Action> Pending = new Queue<Action>();
        private static readonly Dictionary<Delegate, int> Failures = new Dictionary<Delegate, int>();
        private static readonly object Gate = new object();

        public static void Attach(Func<GameState> state, Func<ModConfig> config, string version, Func<int, bool> isMemberPed, Action<int, int> reportKill, Action<string> notify)
        {
            State = state;
            ConfigSource = config;
            ModVersion = version ?? ModVersion;
            IsMemberPed = isMemberPed;
            ReportKill = reportKill;
            Notify = notify;
        }

        public static void Detach()
        {
            State = null;
            lock (Gate) Pending.Clear();
        }

        internal static void Enqueue(Action a)
        {
            lock (Gate)
            {
                if (Pending.Count < 200) Pending.Enqueue(a);
            }
        }

        /// <summary>Runs queued external commands; call from the mod's tick.</summary>
        public static int ProcessQueue()
        {
            int n = 0;
            while (true)
            {
                Action a;
                lock (Gate)
                {
                    if (Pending.Count == 0) break;
                    a = Pending.Dequeue();
                }
                try { a(); n++; }
                catch (Exception ex) { Log.Warn("[Integration:api] external command failed: " + SecretRedactor.Redact(ex.Message)); }
            }
            return n;
        }

        internal static void AddExternalFaction(string id, ExternalFactionSpec spec, string caller)
        {
            var w = State?.Invoke()?.World;
            if (w == null) return;
            var existing = w.GetRival(id);
            if (existing != null)
            {
                existing.Name = TextUtil.SafeForGame(spec.Name, 40);
                existing.Enabled = true;
                return;
            }
            var r = new RivalFaction
            {
                Id = id,
                Name = TextUtil.SafeForGame(spec.Name, 40),
                Kind = RivalKind.StreetGang,
                IsDefault = false,
                External = true,
                ExternalSource = caller,
                Color = Rgb.TryParse(spec.Color, out _) ? spec.Color : "#888888",
                Weapon = string.IsNullOrWhiteSpace(spec.Weapon) ? "WEAPON_PISTOL" : spec.Weapon,
                Strength = MathEx.Clamp(spec.Strength, 5, 90),
                Wealth = 40,
                Aggression = 0.5f,
                Description = TextUtil.SafeForGame(spec.Description ?? "Added by " + caller + ".", 160),
            };
            if (spec.PedModels != null) r.PedModels.AddRange(spec.PedModels.Where(m => !string.IsNullOrWhiteSpace(m)).Take(12));
            foreach (string tid in spec.HomeTerritories ?? new string[0])
            {
                var t = w.GetTerritory(tid);
                if (t == null || t.IsStrategic) continue;
                r.HomeTerritories.Add(t.Id);
                TerritorySystem.AddInfluence(t, r.Id, 30);
                TerritorySystem.UpdateOwnership(t);
            }
            w.Rivals.Add(r);
            Log.Info("[Integration:api] faction '" + r.Name + "' registered by " + caller + ".");
        }

        // ---- publishing from Core systems

        public static void PublishNews(WorldNews n) { if (Ready) EmpireApi.RaiseNews(EmpireApi.ToApi(n)); }
        public static void PublishBattleStarted(WorldBattle b) { if (Ready) EmpireApi.RaiseBattleStarted(EmpireApi.ToApi(b)); }
        public static void PublishBattleEnded(WorldBattle b) { if (Ready) EmpireApi.RaiseBattleEnded(EmpireApi.ToApi(b)); }
        public static void PublishTerritoryChanged(Territory t) { if (Ready) EmpireApi.RaiseTerritoryChanged(EmpireApi.ToApi(t)); }
        public static void PublishMemberRecruited(Member m) { if (Ready && m != null) EmpireApi.RaiseMemberRecruited(new ApiMemberEvent { MemberId = m.Id, Name = m.DisplayName, Kind = "recruited" }); }
        public static void PublishMemberLost(Member m, string kind) { if (Ready && m != null) EmpireApi.RaiseMemberLost(new ApiMemberEvent { MemberId = m.Id, Name = m.DisplayName, Kind = kind }); }
        public static void PublishMissionFinished(string id, string title, bool success) { if (Ready) EmpireApi.RaiseMissionFinished(new ApiMissionEvent { MissionId = id, Title = title, Success = success }); }

        private static bool Ready => EmpireApi.IsReady;

        /// <summary>Calls every subscriber in isolation; a subscriber that throws repeatedly is removed.</summary>
        internal static void Dispatch<T>(Action<T> handlers, T arg, string eventName, Action<Delegate> unsubscribe)
        {
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    ((Action<T>)d)(arg);
                }
                catch (Exception ex)
                {
                    int n;
                    lock (Gate)
                    {
                        Failures.TryGetValue(d, out n);
                        Failures[d] = ++n;
                    }
                    string who = d.Method?.DeclaringType?.Assembly.GetName().Name ?? "?";
                    Log.Warn("[Integration:api] " + who + " handler for " + eventName + " threw (" + n + "/" + MaxHandlerFailures + "): " + SecretRedactor.Redact(ex.Message));
                    if (n >= MaxHandlerFailures)
                    {
                        unsubscribe(d);
                        Log.Warn("[Integration:api] " + who + " was unsubscribed from " + eventName + ".");
                    }
                }
                if (sw.ElapsedMilliseconds > 50)
                    Log.Warn("[Integration:api] a " + eventName + " handler took " + sw.ElapsedMilliseconds + " ms; keep handlers fast.");
            }
        }
    }
}

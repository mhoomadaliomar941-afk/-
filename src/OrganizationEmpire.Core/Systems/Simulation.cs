using System;
using System.Collections.Generic;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    /// <summary>Signals from the live game world that feed the strategic simulation.</summary>
    public sealed class WorldSignals
    {
        public int PlayerWantedLevel;
    }

    /// <summary>Orchestrates one strategic tick (= one faction hour) across all systems.</summary>
    public static class Simulation
    {
        public static Ledger LastLedger { get; private set; }

        public static GameState NewGame(int seed)
        {
            var s = new GameState { World = WorldFactory.CreateDefaultWorld(seed) };
            return s;
        }

        public static void Tick(SimContext ctx, WorldSignals signals)
        {
            var f = ctx.Faction;
            if (f == null) return;
            signals = signals ?? new WorldSignals();
            ctx.World.Tick++;

            Guard("economy", () => { LastLedger = EconomySystem.Compute(ctx, true); });
            Guard("members", () => MemberSystem.Tick(ctx, LastLedger == null || LastLedger.PaidInFull));
            Guard("territory", () => TerritorySystem.Tick(ctx));
            Guard("war", () => WarSystem.Tick(ctx));
            Guard("suspicion", () => SuspicionSystem.Tick(ctx, signals.PlayerWantedLevel));
            Guard("takeover", () => TakeoverSystem.Tick(ctx));
            Guard("government", () => GovernmentSystem.Tick(ctx));
            Guard("events", () => EventSystem.Tick(ctx));
            Guard("vehicles", () => TickVehicles(ctx));
            Guard("board", () => MissionBoard.Refresh(ctx));

            // Faction-wide drifts
            f.Reputation = MathEx.Pct(f.Reputation - 0.01f);
            if (f.Type != FactionType.Militia && !f.IsGovernment) f.PublicSupport = MathEx.Pct(MathEx.MoveTowards(f.PublicSupport, 10 + f.Reputation * 0.2f, 0.05f));
            EconomySystem.ClampResources(f.Resources);
        }

        private static void TickVehicles(SimContext ctx)
        {
            int garage = ctx.Faction.Headquarters?.Level(HqUpgrade.Garage) ?? 0;
            foreach (var v in ctx.Faction.Vehicles)
            {
                if (!v.Destroyed) continue;
                v.RepairHoursLeft -= 1f + garage * 0.5f;
                if (v.RepairHoursLeft <= 0)
                {
                    v.Destroyed = false;
                    v.RepairHoursLeft = 0;
                    ctx.Report("Vehicles", v.DisplayName + " has been repaired and is available again.", EventSeverity.Info);
                }
            }
        }

        private static void Guard(string system, Action a)
        {
            try { a(); }
            catch (Exception ex) { Log.Error("Simulation system '" + system + "' failed", ex); }
        }

        /// <summary>Repairs inconsistencies in a loaded save (missing ids, dangling references).</summary>
        public static List<string> Validate(GameState s)
        {
            var fixes = new List<string>();
            if (s.World == null) { s.World = WorldFactory.CreateDefaultWorld(1337); fixes.Add("World was missing; recreated."); }
            if (s.World.Territories == null || s.World.Territories.Count == 0) { s.World.Territories = WorldFactory.CreateDefaultTerritories(); fixes.Add("Territories recreated."); }
            if (s.World.Rivals == null) { s.World.Rivals = WorldFactory.CreateDefaultRivals(); fixes.Add("Rivals recreated."); }
            if (s.Memories == null) s.Memories = new Dictionary<string, NpcMemory>();
            if (s.KnownNpcs == null) s.KnownNpcs = new Dictionary<string, NpcProfile>();
            if (s.MissionBoard == null) s.MissionBoard = new List<MissionOffer>();
            if (s.MissionHistory == null) s.MissionHistory = new List<MissionRecord>();
            foreach (var t in s.World.Territories) if (t.Influence == null) t.Influence = new Dictionary<string, float>();

            var f = s.Faction;
            if (f == null) return fixes;
            if (f.Ranks == null || f.Ranks.Count < 2) { f.Ranks = RankTemplates.For(f.Type); fixes.Add("Ranks recreated."); }
            RankSystem.Normalize(f);
            if (f.Identity == null) f.Identity = new Identity();
            if (f.Identity.Flag == null) f.Identity.Flag = FlagCatalog.DefaultFor(f.Type, f.Identity.PrimaryColor, f.Identity.SecondaryColor);
            if (f.Resources == null) f.Resources = new Resources();
            if (f.Takeover == null) f.Takeover = new TakeoverState();
            if (f.Stats == null) f.Stats = new FactionStats();
            if (f.Recruitment == null) f.Recruitment = new RecruitmentSettings();
            var lowest = f.LowestRank;
            foreach (var m in f.Members)
            {
                if (string.IsNullOrEmpty(m.Id)) { m.Id = Ids.New("mem"); fixes.Add("Member id assigned."); }
                if (f.GetRank(m.RankId) == null || f.GetRank(m.RankId).ReservedForPlayer) m.RankId = lowest.Id;
                if (m.Opinions == null) m.Opinions = new Dictionary<string, float>();
                if (m.Appearance == null) m.Appearance = new PedAppearance();
                if (m.SquadId != null && f.GetSquad(m.SquadId) == null) m.SquadId = null;
            }
            foreach (var sq in f.Squads)
            {
                if (sq.MemberIds == null) sq.MemberIds = new List<string>();
                sq.MemberIds.RemoveAll(id => f.GetMember(id) == null);
                foreach (var id in sq.MemberIds) { var m = f.GetMember(id); if (m != null) m.SquadId = sq.Id; }
            }
            SquadSystem.Cleanup(f);
            return fixes;
        }
    }
}

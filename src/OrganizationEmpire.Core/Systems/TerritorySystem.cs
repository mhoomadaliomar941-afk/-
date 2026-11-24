using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    public static class TerritorySystem
    {
        /// <summary>Influence needed (and being the top faction) to own a territory.</summary>
        public const float OwnershipThreshold = 50f;
        public const float ContestThreshold = 25f;

        /// <summary>
        /// Adds influence for one faction, taking it proportionally from others so the total never exceeds 100.
        /// Negative amounts simply remove influence.
        /// </summary>
        public static void AddInfluence(Territory t, string factionId, float amount)
        {
            if (t == null || factionId == null || amount == 0) return;
            float current = t.InfluenceOf(factionId);
            if (amount < 0)
            {
                t.Influence[factionId] = Math.Max(0, current + amount);
                return;
            }
            float newValue = Math.Min(100f, current + amount);
            float gained = newValue - current;
            t.Influence[factionId] = newValue;

            float othersTotal = t.Influence.Where(kv => kv.Key != factionId).Sum(kv => kv.Value);
            float overflow = othersTotal + newValue - 100f;
            if (overflow > 0 && othersTotal > 0)
            {
                foreach (var key in t.Influence.Keys.Where(k => k != factionId).ToList())
                {
                    float share = t.Influence[key] / othersTotal;
                    t.Influence[key] = Math.Max(0, t.Influence[key] - overflow * share);
                }
            }
            // Drop negligible entries to keep save files tidy.
            foreach (var key in t.Influence.Where(kv => kv.Value < 0.05f && kv.Key != factionId).Select(kv => kv.Key).ToList())
                t.Influence.Remove(key);
            _ = gained;
        }

        /// <summary>Recomputes owner and contested flag. Returns the previous owner if ownership changed, otherwise null.</summary>
        public static bool UpdateOwnership(Territory t, out string previousOwner)
        {
            previousOwner = t.OwnerId;
            string best = null;
            float bestValue = 0, second = 0;
            foreach (var kv in t.Influence)
            {
                if (kv.Value > bestValue) { second = bestValue; bestValue = kv.Value; best = kv.Key; }
                else if (kv.Value > second) second = kv.Value;
            }
            string newOwner = t.OwnerId;
            if (best != null && bestValue >= OwnershipThreshold) newOwner = best;
            else if (t.OwnerId != null && t.InfluenceOf(t.OwnerId) < OwnershipThreshold * 0.5f) newOwner = null;
            t.Contested = second >= ContestThreshold || (newOwner != null && bestValue < OwnershipThreshold + 10 && second > 10);
            bool changed = newOwner != t.OwnerId;
            t.OwnerId = newOwner;
            return changed;
        }

        public static bool UpdateOwnership(Territory t) => UpdateOwnership(t, out _);

        public static IEnumerable<Territory> OwnedBy(WorldState w, string factionId) => w.Territories.Where(t => t.OwnerId == factionId);

        public static int PlayerTerritoryCount(WorldState w) => w.Territories.Count(t => t.OwnerId == FactionIds.Player);

        /// <summary>Hourly passive influence: garrisons, HQ, rival upkeep and expansion.</summary>
        public static void Tick(SimContext ctx)
        {
            var f = ctx.Faction;
            var w = ctx.World;
            var cfg = ctx.Config;

            // Player garrisons
            foreach (var squad in f.Squads.Where(s => s.AssignedTerritoryId != null))
            {
                var t = w.GetTerritory(squad.AssignedTerritoryId);
                if (t == null) continue;
                int available = SquadSystem.Members(f, squad).Count(m => m.IsAvailable);
                if (available == 0) continue;
                float gain = Math.Min(3.0f, available * 0.35f);
                if (f.Behavior == FactionBehavior.Expansionist) gain *= 1.25f;
                if (t.Type == TerritoryType.Strategic) gain *= 0.4f;
                AddInfluence(t, FactionIds.Player, gain);
            }

            // HQ presence
            if (f.Headquarters != null)
            {
                var hqT = w.FindByZone(f.Headquarters.ZoneCode);
                if (hqT != null && hqT.Type != TerritoryType.Strategic) AddInfluence(hqT, FactionIds.Player, 0.4f + 0.1f * f.Headquarters.Level(HqUpgrade.Fortifications));
            }

            // Rivals maintain and expand
            float activity = cfg.World.RivalActivity * ctx.Difficulty.RivalAggression;
            foreach (var r in w.Rivals.Where(r => r.Active))
            {
                foreach (var tid in r.HomeTerritories)
                {
                    var t = w.GetTerritory(tid);
                    if (t == null) continue;
                    if (t.OwnerId == r.Id || t.InfluenceOf(r.Id) > 10) AddInfluence(t, r.Id, 0.15f * (r.Strength / 50f) * activity);
                }
                if (activity > 0 && ctx.Rng.Chance(0.01f * r.Aggression * activity * (r.Strength / 50f)))
                {
                    var targets = w.Territories.Where(t => t.Type != TerritoryType.Strategic && t.OwnerId != r.Id && t.OwnerId != FactionIds.Player).ToList();
                    var target = ctx.Rng.Pick(targets);
                    if (target != null)
                    {
                        AddInfluence(target, r.Id, ctx.Rng.Range(4, 10));
                    }
                }
            }

            // The state keeps a grip on strategic sites unless overthrown.
            if (!w.State.Overthrown)
            {
                foreach (var t in w.Territories.Where(t => t.Type == TerritoryType.Strategic))
                {
                    AddInfluence(t, FactionIds.State, 0.1f * (w.State.Authority / 80f));
                }
            }

            // Unrest decay & ownership
            foreach (var t in w.Territories)
            {
                t.Unrest = Math.Max(0, t.Unrest - 0.3f);
                if (UpdateOwnership(t, out string prev))
                {
                    OnOwnershipChanged(ctx, t, prev);
                }
            }
        }

        public static void OnOwnershipChanged(SimContext ctx, Territory t, string previousOwner)
        {
            var f = ctx.Faction;
            var w = ctx.World;
            if (t.OwnerId == FactionIds.Player)
            {
                f.Stats.TerritoriesCaptured++;
                ctx.Report("Territory", f.Name + " now controls " + t.Name + ".", EventSeverity.Good);
                f.Reputation = MathEx.Pct(f.Reputation + (t.Type == TerritoryType.Strategic ? 5 : 2));
                var rival = w.GetRival(previousOwner);
                if (rival != null) rival.Hostility = MathEx.Pct(rival.Hostility + 15 * ctx.Config.World.RivalHostility);
                if (previousOwner == FactionIds.State) w.State.Stability = MathEx.Pct(w.State.Stability - 4);
            }
            else if (previousOwner == FactionIds.Player)
            {
                f.Stats.TerritoriesLost++;
                ctx.Report("Territory", f.Name + " lost control of " + t.Name + " to " + w.FactionName(t.OwnerId, f) + ".", EventSeverity.Bad);
                f.Reputation = MathEx.Pct(f.Reputation - 3);
                foreach (var m in f.ActiveMembers) m.Morale = MathEx.Pct(m.Morale - 2);
            }
        }

        /// <summary>Called when the player's side kills enemies inside a territory.</summary>
        public static void OnCombatInTerritory(SimContext ctx, Territory t, string enemyFactionId, int kills)
        {
            if (t == null || kills <= 0) return;
            AddInfluence(t, FactionIds.Player, kills * 0.8f);
            if (enemyFactionId != null) AddInfluence(t, enemyFactionId, -kills * 1.0f);
            t.Unrest = MathEx.Pct(t.Unrest + kills * 1.5f);
            if (UpdateOwnership(t, out string prev)) OnOwnershipChanged(ctx, t, prev);
        }

        /// <summary>Player physically present in a territory with members (reported by the runtime every few seconds).</summary>
        public static void OnPresence(SimContext ctx, Territory t, int membersPresent, float hours)
        {
            if (t == null || membersPresent <= 0 || t.Type == TerritoryType.Strategic) return;
            AddInfluence(t, FactionIds.Player, Math.Min(1.0f, 0.15f * membersPresent) * hours);
        }

        public static float DefenseValue(SimContext ctx, Territory t)
        {
            var f = ctx.Faction;
            float def = t.Fortification;
            foreach (var s in f.Squads.Where(s => s.AssignedTerritoryId == t.Id)) def += SquadSystem.Strength(f, s);
            if (f.Headquarters != null && t.Zones.Any(z => string.Equals(z, f.Headquarters.ZoneCode, StringComparison.OrdinalIgnoreCase)))
            {
                def += 8 + f.Headquarters.Level(HqUpgrade.Fortifications) * 10;
                def += f.Headquarters.GuardMemberIds.Select(f.GetMember).Where(m => m != null && m.IsAvailable).Sum(MemberSystem.CombatValue);
            }
            foreach (var ally in ctx.World.Rivals.Where(r => r.Active && r.Relation == RelationStatus.Alliance)) def += ally.Strength * 0.1f;
            if (f.IsGovernment) def += f.Government.ForcesReadiness * 0.3f;
            return def;
        }
    }
}

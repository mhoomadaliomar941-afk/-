using System;
using System.Linq;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    /// <summary>Faction relations, conflicts, escalation, peace, alliances and elimination.</summary>
    public static class WarSystem
    {
        public const int AttackWindowHours = 8;
        public const int RaidWindowHours = 6;

        public static OpResult DeclareRivalry(SimContext ctx, RivalFaction r)
        {
            if (r == null || !r.Active) return OpResult.Fail("Faction unavailable.");
            if (r.Relation == RelationStatus.Alliance) return OpResult.Fail("Break the alliance first.");
            if (r.Relation == RelationStatus.War) return OpResult.Fail("Already at war.");
            SetRelation(ctx, r, RelationStatus.Rivalry);
            r.Hostility = MathEx.Pct(r.Hostility + 15);
            ctx.Faction.Reputation = MathEx.Pct(ctx.Faction.Reputation + 2);
            ctx.Report("Relations", ctx.Faction.Name + " declared a rivalry with " + r.Name + ".", EventSeverity.Warning);
            return OpResult.Success("Rivalry declared with " + r.Name + ".");
        }

        public static OpResult DeclareWar(SimContext ctx, RivalFaction r)
        {
            if (r == null || !r.Active) return OpResult.Fail("Faction unavailable.");
            if (r.Relation == RelationStatus.War) return OpResult.Fail("Already at war.");
            if (r.Relation == RelationStatus.Alliance)
            {
                ctx.Faction.Reputation = MathEx.Pct(ctx.Faction.Reputation - 10);
                foreach (var other in ctx.World.Rivals.Where(x => x.Active && x != r)) other.Hostility = MathEx.Pct(other.Hostility + 5);
            }
            SetRelation(ctx, r, RelationStatus.War);
            r.Hostility = MathEx.Pct(Math.Max(r.Hostility, 75) + 10);
            r.WarScore = 0;
            ctx.Faction.Reputation = MathEx.Pct(ctx.Faction.Reputation + 4);
            foreach (var m in ctx.Faction.ActiveMembers)
            {
                if (m.Personality == Personality.Aggressive) m.Morale = MathEx.Pct(m.Morale + 6);
                if (m.Personality == Personality.Cowardly) m.Morale = MathEx.Pct(m.Morale - 6);
            }
            ctx.Report("Relations", ctx.Faction.Name + " is now at WAR with " + r.Name + ".", EventSeverity.Critical);
            return OpResult.Success("War declared on " + r.Name + ".");
        }

        public static float AcceptanceChance(SimContext ctx, RivalFaction r, RelationStatus proposal)
        {
            float power = ProgressionSystem.Power(ctx);
            float powerDelta = (power - r.Strength) * 0.004f;
            float chance;
            switch (proposal)
            {
                case RelationStatus.Ceasefire:
                    chance = 0.25f + r.WarScore * 0.012f + powerDelta - r.Aggression * 0.15f;
                    break;
                case RelationStatus.Peace:
                    chance = 0.35f - r.Hostility * 0.006f + powerDelta + ctx.Faction.Reputation * 0.002f;
                    if (r.Relation == RelationStatus.Ceasefire) chance += 0.15f;
                    break;
                case RelationStatus.Alliance:
                    chance = 0.15f - r.Hostility * 0.008f + ctx.Faction.Reputation * 0.004f + powerDelta;
                    break;
                default:
                    chance = 0.5f;
                    break;
            }
            if (ctx.Faction.Type == FactionType.Mafia) chance += 0.08f; // negotiation specialists
            if (ctx.Faction.Personality == FactionPersonality.Honorable) chance += 0.05f;
            if (ctx.Faction.Personality == FactionPersonality.Ruthless) chance -= 0.05f;
            return MathEx.Clamp(chance, 0.03f, 0.95f);
        }

        public static OpResult Propose(SimContext ctx, RivalFaction r, RelationStatus proposal)
        {
            if (r == null || !r.Active) return OpResult.Fail("Faction unavailable.");
            var f = ctx.Faction;
            switch (proposal)
            {
                case RelationStatus.Ceasefire:
                    if (r.Relation != RelationStatus.War) return OpResult.Fail("A ceasefire requires an active war.");
                    break;
                case RelationStatus.Peace:
                    if (r.Relation == RelationStatus.War) return OpResult.Fail("Negotiate a ceasefire first.");
                    if (r.Relation == RelationStatus.Peace || r.Relation == RelationStatus.Alliance) return OpResult.Fail("Already at peace.");
                    break;
                case RelationStatus.Alliance:
                    if (r.Relation != RelationStatus.Peace) return OpResult.Fail("An alliance requires a peace treaty first.");
                    if (f.Reputation < 35) return OpResult.Fail("Your reputation (" + (int)f.Reputation + ") is too low; 35 needed.");
                    if (f.Resources.Influence < 15) return OpResult.Fail("Alliances cost 15 Influence.");
                    break;
                default:
                    return OpResult.Fail("Invalid proposal.");
            }
            int cooldown = ctx.World.Day - r.RelationChangedDay;
            if (cooldown < 1 && proposal != RelationStatus.Ceasefire) return OpResult.Fail(r.Name + " will not discuss terms again so soon.");

            float chance = AcceptanceChance(ctx, r, proposal);
            if (proposal == RelationStatus.Alliance) f.Resources.Influence -= 15;
            if (ctx.Rng.Chance(chance))
            {
                SetRelation(ctx, r, proposal);
                r.Hostility = MathEx.Pct(r.Hostility - (proposal == RelationStatus.Alliance ? 30 : 20));
                ctx.Report("Relations", r.Name + " accepted your " + proposal.ToString().ToLowerInvariant() + " proposal.", EventSeverity.Good);
                ProgressionSystem.AddXp(ctx, 120);
                return OpResult.Success(r.Name + " accepted (" + TextUtil.Pct(chance * 100) + " chance).");
            }
            r.Hostility = MathEx.Pct(r.Hostility + 4);
            r.RelationChangedDay = ctx.World.Day;
            ctx.Report("Relations", r.Name + " rejected your " + proposal.ToString().ToLowerInvariant() + " proposal.", EventSeverity.Warning);
            return OpResult.Fail(r.Name + " refused (" + TextUtil.Pct(chance * 100) + " chance).");
        }

        public static long TributeCost(RivalFaction r) => 4000 + (long)(r.Strength * 180);

        public static OpResult PayTribute(SimContext ctx, RivalFaction r)
        {
            if (r == null || !r.Active) return OpResult.Fail("Faction unavailable.");
            long cost = TributeCost(r);
            var res = EconomySystem.Spend(ctx.Faction, cost, "tribute");
            if (!res.Ok) return res;
            r.Hostility = MathEx.Pct(r.Hostility - 25);
            r.Wealth += 5;
            ctx.Faction.Reputation = MathEx.Pct(ctx.Faction.Reputation - 2);
            if (r.Relation == RelationStatus.War && r.Hostility < 50)
            {
                SetRelation(ctx, r, RelationStatus.Ceasefire);
                ctx.Report("Relations", "Tribute accepted. " + r.Name + " agreed to a ceasefire.", EventSeverity.Good);
            }
            else ctx.Report("Relations", "Paid tribute to " + r.Name + ".", EventSeverity.Info);
            return OpResult.Success("Paid " + TextUtil.Money(cost) + " to " + r.Name + ".");
        }

        /// <summary>Intel-based operation that weakens a rival (abstract; militia and mafia specialty).</summary>
        public static OpResult Sabotage(SimContext ctx, RivalFaction r)
        {
            var f = ctx.Faction;
            if (r == null || !r.Active) return OpResult.Fail("Faction unavailable.");
            int cost = f.Type == FactionType.Militia ? 8 : 12;
            if (f.Resources.Intel < cost) return OpResult.Fail("Requires " + cost + " Intel.");
            f.Resources.Intel -= cost;
            float success = 0.55f + (f.Type == FactionType.Militia ? 0.15f : 0) + f.Discipline * 0.002f;
            r.Hostility = MathEx.Pct(r.Hostility + 8);
            f.Suspicion = MathEx.Pct(f.Suspicion + 3 * ctx.Difficulty.Suspicion);
            if (ctx.Rng.Chance(success))
            {
                float dmg = ctx.Rng.Range(4, 9);
                r.Strength = Math.Max(0, r.Strength - dmg);
                r.Wealth = Math.Max(0, r.Wealth - dmg);
                if (r.Relation == RelationStatus.War) r.WarScore += 4;
                ctx.Report("Relations", "Covert operation against " + r.Name + " succeeded (strength -" + (int)dmg + ").", EventSeverity.Good);
                CheckElimination(ctx, r);
                return OpResult.Success("Operation succeeded.");
            }
            ctx.Report("Relations", "Covert operation against " + r.Name + " was exposed.", EventSeverity.Bad);
            return OpResult.Fail("Operation failed and was exposed.");
        }

        public static void SetRelation(SimContext ctx, RivalFaction r, RelationStatus status)
        {
            r.Relation = status;
            r.RelationChangedDay = ctx.World.Day;
            if (status != RelationStatus.War)
            {
                ctx.World.PendingAttacks.RemoveAll(a => a.AttackerId == r.Id && !a.IsRaid);
            }
        }

        /// <summary>Hourly: hostility drift, escalation, ceasefire expiry, rival-initiated attacks, regeneration.</summary>
        public static void Tick(SimContext ctx)
        {
            var w = ctx.World;
            var f = ctx.Faction;
            float hostMult = ctx.Config.World.RivalHostility;
            float activity = ctx.Config.World.RivalActivity * ctx.Difficulty.RivalAggression;

            foreach (var r in w.Rivals.Where(x => x.Active))
            {
                int ownedHome = r.HomeTerritories.Count(id => w.GetTerritory(id)?.OwnerId == FactionIds.Player);
                float drift = ownedHome * 0.4f * hostMult;
                if (f.Behavior == FactionBehavior.Aggressive) drift += 0.05f * hostMult;
                if (r.Relation == RelationStatus.Peace || r.Relation == RelationStatus.Alliance || r.Relation == RelationStatus.Ceasefire) drift -= 0.25f;
                else drift -= 0.06f;
                r.Hostility = MathEx.Pct(r.Hostility + drift);

                // Escalation by the rival
                if (r.Relation == RelationStatus.Neutral && r.Hostility >= 55)
                {
                    SetRelation(ctx, r, RelationStatus.Rivalry);
                    ctx.Report("Relations", r.Name + " now considers " + f.Name + " a rival.", EventSeverity.Warning);
                }
                else if (r.Relation == RelationStatus.Rivalry && r.Hostility >= 80 && activity > 0)
                {
                    SetRelation(ctx, r, RelationStatus.War);
                    ctx.Report("Relations", r.Name + " has declared WAR on " + f.Name + "!", EventSeverity.Critical);
                }
                else if (r.Relation == RelationStatus.Rivalry && r.Hostility < 25)
                {
                    SetRelation(ctx, r, RelationStatus.Neutral);
                }
                else if (r.Relation == RelationStatus.Ceasefire && w.Day - r.RelationChangedDay >= 3)
                {
                    if (r.Hostility >= 70) { SetRelation(ctx, r, RelationStatus.War); ctx.Report("Relations", "The ceasefire with " + r.Name + " collapsed.", EventSeverity.Bad); }
                    else { SetRelation(ctx, r, RelationStatus.Neutral); ctx.Report("Relations", "The ceasefire with " + r.Name + " ended peacefully.", EventSeverity.Info); }
                }

                // Rival attacks
                if (ctx.Config.Gameplay.RivalAttacksEnabled && activity > 0)
                {
                    float p = 0;
                    if (r.Relation == RelationStatus.War) p = 0.025f;
                    else if (r.Relation == RelationStatus.Rivalry) p = 0.006f;
                    p *= r.Aggression * 2f * activity * (0.5f + r.Strength / 100f);
                    if (p > 0 && ctx.Rng.Chance(p) && w.PendingAttacks.Count(a => a.AttackerId == r.Id) == 0)
                    {
                        var targets = TerritorySystem.OwnedBy(w, FactionIds.Player).ToList();
                        if (targets.Count > 0)
                        {
                            // Prefer territories the rival considers home.
                            var preferred = targets.Where(t => r.HomeTerritories.Contains(t.Id)).ToList();
                            var target = preferred.Count > 0 && ctx.Rng.Chance(0.6f) ? ctx.Rng.Pick(preferred) : ctx.Rng.Pick(targets);
                            CreateAttack(ctx, target, r.Id, r.Strength * ctx.Rng.Range(0.4f, 0.8f), false);
                        }
                    }
                }

                // Regeneration and economy of rivals
                r.Strength = MathEx.Clamp(r.Strength + 0.03f * (r.Wealth / 50f) * activity, 0, 100);
                r.Wealth = MathEx.Clamp(r.Wealth + 0.02f * w.Territories.Count(t => t.OwnerId == r.Id) - 0.01f, 0, 100);
                if (r.Relation == RelationStatus.War) r.WarScore *= 0.998f;
                if (r.Relation == RelationStatus.Alliance && ctx.Rng.Chance(0.004f))
                {
                    long gift = 1000 + (long)(r.Wealth * 50);
                    f.Resources.Cash += gift;
                    ctx.Report("Relations", r.Name + " sent a gift of " + TextUtil.Money(gift) + " to honor your alliance.", EventSeverity.Good);
                }
                CheckElimination(ctx, r);
            }

            ResolveExpiredAttacks(ctx);
        }

        public static AttackEvent CreateAttack(SimContext ctx, Territory target, string attackerId, float strength, bool isRaid)
        {
            int comms = ctx.Faction.Headquarters?.Level(HqUpgrade.Comms) ?? 0;
            var a = new AttackEvent
            {
                Id = Ids.New("atk"),
                TerritoryId = target.Id,
                AttackerId = attackerId,
                Strength = strength,
                CreatedTick = ctx.World.Tick,
                DeadlineTick = ctx.World.Tick + (isRaid ? RaidWindowHours : AttackWindowHours) + comms,
                IsRaid = isRaid,
            };
            ctx.World.PendingAttacks.Add(a);
            target.Contested = true;
            string who = ctx.World.FactionName(attackerId, ctx.Faction);
            if (isRaid)
                ctx.Report("Raid", "Intel warns of an imminent state raid on your headquarters in " + target.Name + "! Defend it (Missions) within " + (a.DeadlineTick - a.CreatedTick) + " hours.", EventSeverity.Critical);
            else
                ctx.Report("Territory", who + " is moving on " + target.Name + "! Defend it within " + (a.DeadlineTick - a.CreatedTick) + " hours or rely on your garrison.", EventSeverity.Critical);
            MissionBoard.AddDefenseOffer(ctx, a);
            return a;
        }

        private static void ResolveExpiredAttacks(SimContext ctx)
        {
            var w = ctx.World;
            foreach (var a in w.PendingAttacks.Where(a => w.Tick >= a.DeadlineTick).ToList())
            {
                ResolveAttackAbstract(ctx, a);
            }
        }

        /// <summary>Resolves an attack the player did not respond to, using garrison strength.</summary>
        public static void ResolveAttackAbstract(SimContext ctx, AttackEvent a)
        {
            var w = ctx.World;
            var f = ctx.Faction;
            w.PendingAttacks.Remove(a);
            ctx.State.MissionBoard.RemoveAll(o => o.AttackId == a.Id);
            var t = w.GetTerritory(a.TerritoryId);
            if (t == null) return;
            float defense = TerritorySystem.DefenseValue(ctx, t) * ctx.Rng.Range(0.7f, 1.3f);
            float attack = a.Strength * ctx.Rng.Range(0.7f, 1.3f);
            string who = w.FactionName(a.AttackerId, f);
            var rival = w.GetRival(a.AttackerId);

            if (defense >= attack)
            {
                if (rival != null) { rival.Strength = Math.Max(0, rival.Strength - 3); rival.WarScore += 4; }
                if (a.IsRaid) { f.Stats.RaidsSurvived++; f.Suspicion = MathEx.Pct(f.Suspicion - 10); }
                ctx.Report(a.IsRaid ? "Raid" : "Territory", "Your people repelled " + who + " in " + t.Name + ".", EventSeverity.Good);
                f.Reputation = MathEx.Pct(f.Reputation + 1);
            }
            else
            {
                if (a.IsRaid) ApplyRaidLosses(ctx);
                else
                {
                    TerritorySystem.AddInfluence(t, a.AttackerId, 35);
                    TerritorySystem.AddInfluence(t, FactionIds.Player, -30);
                    if (TerritorySystem.UpdateOwnership(t, out string prev)) TerritorySystem.OnOwnershipChanged(ctx, t, prev);
                    if (rival != null) rival.WarScore -= 6;
                    ctx.Report("Territory", who + " overran your defenses in " + t.Name + ".", EventSeverity.Bad);
                }
                // Garrison casualties
                foreach (var s in f.Squads.Where(s => s.AssignedTerritoryId == t.Id || (a.IsRaid && s.GuardsHeadquarters)))
                {
                    foreach (var m in SquadSystem.Members(f, s).Where(m => m.IsAvailable).ToList())
                    {
                        if (ctx.Rng.Chance(0.25f)) MemberSystem.OnKilled(ctx, m, ctx.Config.Gameplay.PermanentDeath);
                    }
                }
            }
            t.Unrest = MathEx.Pct(t.Unrest + 10);
            TerritorySystem.UpdateOwnership(t);
        }

        public static void ApplyRaidLosses(SimContext ctx)
        {
            var f = ctx.Faction;
            long seized = f.Resources.Cash / 4;
            f.Resources.Cash -= seized;
            f.Resources.Arms /= 2;
            f.Resources.Product /= 3;
            int jailed = 0;
            foreach (var m in f.Members.Where(m => m.IsAvailable).ToList())
            {
                if (ctx.Rng.Chance(0.2f))
                {
                    m.Status = MemberStatus.Jailed;
                    m.RecoveryHours = 48 + ctx.Rng.Next(0, 48);
                    jailed++;
                }
            }
            f.Suspicion = MathEx.Pct(f.Suspicion - 30);
            f.Reputation = MathEx.Pct(f.Reputation - 5);
            ctx.World.State.Stability = MathEx.Pct(ctx.World.State.Stability + 4);
            ctx.Report("Raid", "State forces raided your headquarters: " + TextUtil.Money(seized) + " seized, half your arms confiscated, " + jailed + " member(s) detained.", EventSeverity.Critical);
        }

        /// <summary>Called by missions when the player personally defends/attacks.</summary>
        public static void OnPlayerVictory(SimContext ctx, RivalFaction r, float damage)
        {
            if (r == null) return;
            r.Strength = Math.Max(0, r.Strength - damage);
            r.Wealth = Math.Max(0, r.Wealth - damage * 0.5f);
            if (r.Relation == RelationStatus.War) r.WarScore += damage * 1.5f;
            r.Hostility = MathEx.Pct(r.Hostility + 4 * ctx.Config.World.RivalHostility);
            CheckElimination(ctx, r);
        }

        public static void OnEnemiesKilled(SimContext ctx, RivalFaction r, int kills)
        {
            if (r == null || kills <= 0) return;
            r.Strength = Math.Max(0, r.Strength - kills * 0.25f);
            r.Hostility = MathEx.Pct(r.Hostility + kills * 2f * ctx.Config.World.RivalHostility);
            if (r.Relation == RelationStatus.War) r.WarScore += kills * 0.5f;
            ctx.Faction.Stats.EnemiesKilled += kills;
        }

        public static void CheckElimination(SimContext ctx, RivalFaction r)
        {
            if (r.Eliminated || r.Strength > 0.5f) return;
            bool holdsTerritory = ctx.World.Territories.Any(t => t.OwnerId == r.Id);
            if (holdsTerritory)
            {
                r.Strength = 0.5f;
                return;
            }
            // Only the player's own war earns the credit; collapses caused by other factions are world news.
            bool playerCaused = r.Relation == RelationStatus.War || r.WarScore > 0;
            r.Eliminated = true;
            r.Relation = RelationStatus.Neutral;
            ctx.World.PendingAttacks.RemoveAll(a => a.AttackerId == r.Id);
            foreach (var t in ctx.World.Territories) t.Influence.Remove(r.Id);
            if (!playerCaused) return;
            ctx.Faction.Stats.RivalsEliminated++;
            ctx.Faction.Reputation = MathEx.Pct(ctx.Faction.Reputation + 10);
            ctx.Report("Relations", r.Name + " has been ELIMINATED. Their remaining crews scatter.", EventSeverity.Good);
            ProgressionSystem.AddXp(ctx, 1500);
        }

        /// <summary>Removes a rival from the simulation entirely (Settings &gt; World). Reversible via restore defaults.</summary>
        public static void SetRivalEnabled(SimContext ctx, RivalFaction r, bool enabled)
        {
            r.Enabled = enabled;
            if (!enabled)
            {
                ctx.World.PendingAttacks.RemoveAll(a => a.AttackerId == r.Id);
                ctx.State.MissionBoard.RemoveAll(o => o.RivalId == r.Id);
                foreach (var t in ctx.World.Territories)
                {
                    if (t.Influence.Remove(r.Id)) TerritorySystem.UpdateOwnership(t);
                }
            }
        }
    }
}

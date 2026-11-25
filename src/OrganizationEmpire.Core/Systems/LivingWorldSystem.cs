using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    /// <summary>
    /// The living world: every rival faction runs on its own whether or not the player is involved.
    /// Each faction has a leader and lieutenants, money (wealth), manpower (strength), arms and morale; every few
    /// hours it picks a goal with a utility score shaped by its temperament, its leader's traits, its resources,
    /// its relations and what is happening around it (recruit, arm, expand, retaliate, seek allies, make peace,
    /// lay low, consolidate). Expansion between factions becomes a <see cref="WorldBattle"/> that lasts several
    /// hours; near the player the runtime spawns the fighters for real and reports casualties back here.
    /// Everything is O(rivals^2 + territories) per strategic hour.
    /// </summary>
    public static class LivingWorldSystem
    {
        public const int MaxNews = 80;
        public const int MaxActiveBattles = 4;
        public const float NeighborDistance = 2200f;

        // ------------------------------------------------------------------ setup

        public static void EnsureInitialized(SimContext ctx)
        {
            var w = ctx.World;
            if (w.Living == null) w.Living = new LivingWorldState();
            var lw = w.Living;
            foreach (var r in w.Rivals)
            {
                if (r.Lieutenants == null) r.Lieutenants = new List<RivalLeader>();
                if (r.Leader == null || string.IsNullOrEmpty(r.Leader.Name))
                {
                    r.Temperament = TemperamentFor(r, ctx.Rng);
                    r.Leader = NewLeader(ctx, r, false);
                    while (r.Lieutenants.Count < 2) r.Lieutenants.Add(NewLeader(ctx, r, true));
                    r.Morale = 55 + ctx.Rng.Range(0, 15);
                    r.Arms = 25 + r.Strength * 0.4f;
                    r.Influence = r.Kind == RivalKind.Mafia ? 45 : r.Kind == RivalKind.Paramilitary ? 35 : 15;
                }
                if (r.NextDecisionTick <= 0) r.NextDecisionTick = w.Tick + 1 + ctx.Rng.Next(0, 8);
            }
            if (!lw.Initialized)
            {
                SeedRelations(ctx);
                lw.Initialized = true;
            }
        }

        private static RivalTemperament TemperamentFor(RivalFaction r, Rng rng)
        {
            switch (r.Kind)
            {
                case RivalKind.Biker: return RivalTemperament.Aggressive;
                case RivalKind.Mafia: return rng.Chance(0.7f) ? RivalTemperament.Mercantile : RivalTemperament.Opportunist;
                case RivalKind.Cartel: return RivalTemperament.Expansionist;
                case RivalKind.Paramilitary: return RivalTemperament.Defensive;
                default:
                    if (r.Aggression >= 0.65f) return RivalTemperament.Aggressive;
                    return rng.Chance(0.5f) ? RivalTemperament.Expansionist : RivalTemperament.Opportunist;
            }
        }

        public static string LeaderTitle(RivalFaction r, bool lieutenant)
        {
            switch (r.Kind)
            {
                case RivalKind.Biker: return lieutenant ? "Sergeant-at-Arms" : "President";
                case RivalKind.Mafia: return lieutenant ? "Capo" : "Boss";
                case RivalKind.Cartel: return lieutenant ? "Lieutenant" : "Jefe";
                case RivalKind.Paramilitary: return lieutenant ? "Captain" : "Commander";
                default: return lieutenant ? "Lieutenant" : "Shot-caller";
            }
        }

        public static RivalLeader NewLeader(SimContext ctx, RivalFaction r, bool lieutenant)
        {
            var rng = ctx.Rng;
            var nameType = r.Kind == RivalKind.Mafia ? FactionType.Mafia : r.Kind == RivalKind.Paramilitary ? FactionType.Militia : FactionType.StreetGang;
            string first = NameGenerator.FirstName(rng, false);
            string last = NameGenerator.LastName(rng, nameType);
            string name = nameType == FactionType.StreetGang && rng.Chance(0.7f)
                ? first + " \"" + NameGenerator.Nickname(rng, nameType) + "\" " + last
                : first + " " + last;
            return new RivalLeader
            {
                Id = Ids.New("rl"),
                Name = name,
                Title = LeaderTitle(r, lieutenant),
                Age = lieutenant ? 24 + rng.Next(0, 16) : 32 + rng.Next(0, 24),
                Ambition = rng.Range(0.2f, 0.95f),
                Caution = rng.Range(0.15f, 0.9f),
                Brutality = MathEx.Clamp(r.Aggression + rng.Range(-0.25f, 0.25f), 0.05f, 1f),
                Charisma = rng.Range(0.2f, 0.95f),
                Loyalty = lieutenant ? rng.Range(45, 90) : 100,
                SinceDay = ctx.World.Day,
                PedModel = r.PedModels.Count > 0 ? rng.Pick(r.PedModels) : null,
            };
        }

        /// <summary>Known feuds of GTA's Los Santos as starting relations; everything else starts neutral.</summary>
        private static void SeedRelations(SimContext ctx)
        {
            var w = ctx.World;
            var lw = w.Living;
            var seeds = new[]
            {
                new { A = "ballas", B = "families", S = RelationStatus.War, H = 85f },
                new { A = "vagos", B = "aztecas", S = RelationStatus.War, H = 80f },
                new { A = "ballas", B = "vagos", S = RelationStatus.Rivalry, H = 60f },
                new { A = "marabunta", B = "vagos", S = RelationStatus.Rivalry, H = 62f },
                new { A = "lost", B = "oneil", S = RelationStatus.Rivalry, H = 58f },
                new { A = "triads", B = "kkangpae", S = RelationStatus.Rivalry, H = 57f },
                new { A = "madrazo", B = "lost", S = RelationStatus.Rivalry, H = 56f },
                new { A = "armenian", B = "triads", S = RelationStatus.Neutral, H = 40f },
            };
            foreach (var s in seeds)
            {
                if (w.GetRival(s.A) == null || w.GetRival(s.B) == null) continue;
                var rel = lw.GetOrCreate(s.A, s.B, w.Day);
                rel.Status = s.S;
                rel.Hostility = s.H;
            }
            var rivals = w.Rivals.ToList();
            for (int i = 0; i < rivals.Count; i++)
                for (int j = i + 1; j < rivals.Count; j++)
                {
                    var rel = lw.GetOrCreate(rivals[i].Id, rivals[j].Id, w.Day);
                    if (rel.Status == RelationStatus.Neutral && rel.Hostility == 20) rel.Hostility = 10 + ctx.Rng.Range(0, 25);
                }
        }

        // ------------------------------------------------------------------ hourly tick

        public static void Tick(SimContext ctx)
        {
            if (ctx.Config.World.LivingWorld == false) return;
            EnsureInitialized(ctx);
            var w = ctx.World;
            float activity = Math.Max(0f, ctx.Config.World.RivalActivity);
            var active = w.Rivals.Where(r => r.Active).ToList();

            foreach (var r in active) Economy(ctx, r);
            DriftRelations(ctx, active, activity);
            if (activity > 0)
            {
                foreach (var r in active)
                {
                    if (w.Tick < r.NextDecisionTick) continue;
                    // Faster decisions at higher activity; staggered so factions don't act in lockstep.
                    int interval = (int)Math.Round((6 + ctx.Rng.Next(0, 6)) / Math.Max(0.35f, activity));
                    r.NextDecisionTick = w.Tick + Math.Max(2, interval);
                    Decide(ctx, r);
                }
            }
            ProgressBattles(ctx);
            LeadershipTick(ctx, active);
            foreach (var r in active) CheckCollapse(ctx, r);

            var lw = w.Living;
            if (lw.News.Count > MaxNews) lw.News.RemoveRange(0, lw.News.Count - MaxNews);
            lw.Recruitments.RemoveAll(n => w.Tick - n.Tick > 6);
            var finished = lw.Battles.Where(b => !b.IsActive).ToList();
            if (finished.Count > 30) foreach (var b in finished.Take(finished.Count - 30)) lw.Battles.Remove(b);
        }

        private static void Economy(SimContext ctx, RivalFaction r)
        {
            var w = ctx.World;
            float income = 0.02f;
            foreach (var t in w.Territories) if (t.OwnerId == r.Id) income += t.BaseValue / 1000f * 0.035f;
            if (r.Temperament == RivalTemperament.Mercantile) income *= 1.3f;
            float upkeep = r.Manpower * 0.0012f;
            r.Wealth = MathEx.Clamp(r.Wealth + income - upkeep, 0, 100);
            int wars = WarsOf(w, r.Id).Count();
            float moraleTarget = 62 - wars * 6 + (r.Wealth < 10 ? -12 : 0);
            r.Morale = MathEx.Clamp(MathEx.MoveTowards(r.Morale, moraleTarget, 0.4f), 0, 100);
            r.Influence = MathEx.Clamp(r.Influence + (r.Kind == RivalKind.Mafia ? 0.03f : 0.01f) - 0.005f, 0, 100);
        }

        private static bool AreNeighbors(WorldState w, string a, string b)
        {
            var ta = w.Territories.Where(t => t.OwnerId == a).ToList();
            var tb = w.Territories.Where(t => t.OwnerId == b).ToList();
            foreach (var x in ta) foreach (var y in tb) if (x.Center.DistanceTo2D(y.Center) < NeighborDistance) return true;
            return false;
        }

        private static void DriftRelations(SimContext ctx, List<RivalFaction> active, float activity)
        {
            var w = ctx.World;
            var lw = w.Living;
            for (int i = 0; i < active.Count; i++)
            {
                for (int j = i + 1; j < active.Count; j++)
                {
                    var a = active[i];
                    var b = active[j];
                    var rel = lw.GetOrCreate(a.Id, b.Id, w.Day);
                    float drift;
                    switch (rel.Status)
                    {
                        case RelationStatus.Alliance: drift = -0.12f; break;
                        case RelationStatus.Peace: drift = -0.08f; break;
                        case RelationStatus.Ceasefire: drift = -0.05f; break;
                        case RelationStatus.War: drift = lw.ActiveBattles.Any(x => Pair(x, a.Id, b.Id)) ? 0.1f : -0.03f; break;
                        default: drift = -0.02f; break;
                    }
                    if (rel.Status != RelationStatus.Alliance && rel.Status != RelationStatus.Peace && AreNeighbors(w, a.Id, b.Id))
                        drift += 0.06f * (a.Aggression + b.Aggression) * activity;
                    // Contested ground: both hold real influence in the same territory.
                    if (w.Territories.Any(t => t.InfluenceOf(a.Id) > 12 && t.InfluenceOf(b.Id) > 12)) drift += 0.15f * activity;
                    rel.Hostility = MathEx.Pct(rel.Hostility + drift);

                    if (rel.Status == RelationStatus.Neutral && rel.Hostility >= 55) SetRelation(ctx, rel, RelationStatus.Rivalry, null);
                    else if (rel.Status == RelationStatus.Rivalry && rel.Hostility < 25) SetRelation(ctx, rel, RelationStatus.Neutral, null);
                    else if (rel.Status == RelationStatus.Ceasefire && w.Day - rel.SinceDay >= 2)
                        SetRelation(ctx, rel, rel.Hostility >= 70 ? RelationStatus.War : RelationStatus.Peace,
                            rel.Hostility >= 70 ? Fmt("The ceasefire between {0} and {1} collapsed.", Name(w, rel.A), Name(w, rel.B))
                                                : Fmt("{0} and {1} turned their ceasefire into a lasting peace.", Name(w, rel.A), Name(w, rel.B)));
                    else if (rel.Status == RelationStatus.Alliance && rel.Hostility > 50)
                        SetRelation(ctx, rel, RelationStatus.Rivalry, Fmt("The alliance between {0} and {1} broke apart.", Name(w, rel.A), Name(w, rel.B)));
                }
            }
        }

        private static bool Pair(WorldBattle b, string x, string y) => (b.AttackerId == x && b.DefenderId == y) || (b.AttackerId == y && b.DefenderId == x);

        // ------------------------------------------------------------------ decisions

        private sealed class Option
        {
            public RivalGoal Goal;
            public float Score;
            public string Target;
        }

        public static IEnumerable<FactionRelation> WarsOf(WorldState w, string id) =>
            (w.Living?.Relations ?? new List<FactionRelation>()).Where(r => r.Status == RelationStatus.War && r.Involves(id) && (w.GetRival(r.Other(id))?.Active ?? false));

        private static float TemperamentAggression(RivalFaction r)
        {
            switch (r.Temperament)
            {
                case RivalTemperament.Aggressive: return 1.3f;
                case RivalTemperament.Expansionist: return 1.25f;
                case RivalTemperament.Fanatic: return 1.35f;
                case RivalTemperament.Defensive: return 0.6f;
                case RivalTemperament.Mercantile: return 0.75f;
                default: return 1f;
            }
        }

        /// <summary>Scores every option for a faction (exposed for tests and the World page).</summary>
        public static List<(RivalGoal Goal, float Score, string Target)> ScoreOptions(SimContext ctx, RivalFaction r)
        {
            var w = ctx.World;
            var lw = w.Living;
            var leader = r.Leader ?? new RivalLeader();
            int owned = w.Territories.Count(t => t.OwnerId == r.Id);
            var wars = WarsOf(w, r.Id).ToList();
            var list = new List<Option>();

            // Recruit: fill the ranks the faction needs for its turf and its wars.
            float desired = 25 + 12 * owned + 15 * wars.Count;
            float need = MathEx.Clamp01((desired - r.Manpower) / desired);
            list.Add(new Option { Goal = RivalGoal.Recruit, Score = need * (r.Wealth > 12 ? 1f : 0.2f) * (0.6f + 0.4f * leader.Charisma) + (r.Temperament == RivalTemperament.Expansionist ? 0.08f : 0) });

            // Arm up, more when at war.
            list.Add(new Option { Goal = RivalGoal.Arm, Score = (1 - r.Arms / 100f) * (wars.Count > 0 ? 0.75f : 0.4f) * (r.Wealth > 18 ? 1f : 0.2f) });

            // Expand: the best reachable territory not held by the faction, an ally or a peace partner.
            var myTurf = w.Territories.Where(t => t.OwnerId == r.Id).ToList();
            Territory best = null;
            float bestScore = 0;
            foreach (var t in w.Territories)
            {
                if (t.IsStrategic || t.OwnerId == r.Id || t.OwnerId == FactionIds.Player || t.OwnerId == FactionIds.State) continue;
                if (lw.Battles.Any(b => b.IsActive && b.TerritoryId == t.Id)) continue;
                var owner = w.GetRival(t.OwnerId);
                if (t.OwnerId != null && (owner == null || !owner.Active)) continue;
                if (owner != null)
                {
                    var rel = lw.Get(r.Id, owner.Id);
                    if (rel != null && (rel.Status == RelationStatus.Alliance || rel.Status == RelationStatus.Peace || rel.Status == RelationStatus.Ceasefire)) continue;
                }
                float dist = myTurf.Count == 0 ? 2000 : myTurf.Min(x => x.Center.DistanceTo2D(t.Center));
                if (dist > 4500) continue;
                float s = t.BaseValue / 1000f * 0.18f - dist / 9000f;
                if (owner == null) s += 0.25f;                       // unclaimed ground is the easy prize
                else
                {
                    s += r.Strength > owner.Strength ? 0.25f : -0.2f;
                    var rel = lw.Get(r.Id, owner.Id);
                    if (rel != null && rel.Status == RelationStatus.War) s += 0.3f;
                    if (rel != null) s += rel.Hostility / 400f;
                }
                if (r.HomeTerritories.Contains(t.Id)) s += 0.35f;   // reclaiming lost home turf
                if (s > bestScore) { bestScore = s; best = t; }
            }
            if (best != null)
            {
                float agg = TemperamentAggression(r) * (0.6f + 0.5f * leader.Ambition) * (0.7f + 0.6f * r.Aggression);
                float ready = (r.Manpower > 18 ? 1f : 0.2f) * MathEx.Clamp(r.Morale / 60f, 0.3f, 1.2f) * (0.6f + 0.4f * r.Arms / 100f);
                list.Add(new Option { Goal = RivalGoal.Expand, Score = bestScore * agg * ready, Target = best.Id });
            }

            // Retaliate against the player when hostile enough.
            if (ctx.Faction != null && r.Hostility > 55)
                list.Add(new Option { Goal = RivalGoal.Retaliate, Score = r.Hostility / 100f * (0.5f + 0.6f * leader.Brutality) * (r.Relation == RelationStatus.War ? 0.9f : 0.35f) * (r.Manpower > 15 ? 1f : 0.3f) });

            // Seek allies against a common enemy.
            foreach (var war in wars)
            {
                string enemy = war.Other(r.Id);
                foreach (var other in w.Rivals.Where(o => o.Active && o.Id != r.Id && o.Id != enemy))
                {
                    var relOther = lw.Get(r.Id, other.Id);
                    if (relOther == null || relOther.Status == RelationStatus.Alliance || relOther.Status == RelationStatus.War || relOther.Hostility > 45) continue;
                    var otherVsEnemy = lw.Get(other.Id, enemy);
                    if (otherVsEnemy == null || (otherVsEnemy.Status != RelationStatus.War && otherVsEnemy.Hostility < 50)) continue;
                    float s = 0.35f + 0.3f * leader.Charisma + 0.15f * leader.Caution - relOther.Hostility / 200f;
                    list.Add(new Option { Goal = RivalGoal.SeekAllies, Score = s, Target = other.Id });
                }
            }

            // Make peace when losing or war-weary.
            foreach (var war in wars)
            {
                var enemy = w.GetRival(war.Other(r.Id));
                bool losing = enemy != null && r.Strength < enemy.Strength * 0.75f;
                bool weary = w.Day - war.SinceDay >= 5 || r.Morale < 35;
                if (losing || weary)
                    list.Add(new Option { Goal = RivalGoal.MakePeace, Score = 0.3f + 0.45f * leader.Caution + (losing ? 0.2f : 0) - (r.Temperament == RivalTemperament.Fanatic ? 0.4f : 0), Target = enemy?.Id });
            }

            if (r.Morale < 30 || r.Wealth < 6) list.Add(new Option { Goal = RivalGoal.LayLow, Score = 0.55f });
            list.Add(new Option { Goal = RivalGoal.Consolidate, Score = 0.15f + 0.2f * leader.Caution });
            return list.Select(o => (o.Goal, o.Score, o.Target)).ToList();
        }

        private static void Decide(SimContext ctx, RivalFaction r)
        {
            var options = ScoreOptions(ctx, r);
            var pick = options.OrderByDescending(o => o.Score + ctx.Rng.Range(-0.08f, 0.08f)).First();
            r.Goal = pick.Goal;
            r.GoalTargetId = pick.Target;
            Execute(ctx, r, pick.Goal, pick.Target);
        }

        /// <summary>Carries out a goal immediately (also used by tests).</summary>
        public static void Execute(SimContext ctx, RivalFaction r, RivalGoal goal, string target)
        {
            var w = ctx.World;
            var lw = w.Living;
            var leader = r.Leader ?? new RivalLeader();
            switch (goal)
            {
                case RivalGoal.Recruit:
                {
                    int n = 2 + ctx.Rng.Next(0, 4) + (int)(leader.Charisma * 4);
                    float cost = n * 0.55f;
                    if (r.Wealth < cost) n = Math.Max(1, (int)(r.Wealth / 0.55f));
                    if (n <= 0) return;
                    r.Wealth = Math.Max(0, r.Wealth - n * 0.55f);
                    r.Strength = MathEx.Clamp(r.Strength + n / 1.2f, 0, 100);
                    var home = w.Territories.Where(t => t.OwnerId == r.Id).ToList();
                    var where = home.Count > 0 ? ctx.Rng.Pick(home) : null;
                    lw.Recruitments.Add(new RecruitmentNotice { RivalId = r.Id, TerritoryId = where?.Id, Count = n, Tick = w.Tick });
                    News(ctx, WorldNewsKind.Recruitment, where != null
                        ? Fmt("{0} recruited {1} new members in {2}.", r.Name, n, where.Name)
                        : Fmt("{0} recruited {1} new members.", r.Name, n), false, where?.Id, r.Id);
                    break;
                }
                case RivalGoal.Arm:
                    r.Wealth = Math.Max(0, r.Wealth - 7);
                    r.Arms = MathEx.Clamp(r.Arms + 14, 0, 100);
                    break;
                case RivalGoal.Expand:
                {
                    var t = w.GetTerritory(target);
                    if (t == null) return;
                    if (lw.ActiveBattles.Count() >= MaxActiveBattles || lw.ActiveBattles.Any(b => b.AttackerId == r.Id))
                    {
                        // Too much fighting already: push influence quietly instead.
                        TerritorySystem.AddInfluence(t, r.Id, ctx.Rng.Range(3, 7));
                        return;
                    }
                    StartBattle(ctx, r, t);
                    break;
                }
                case RivalGoal.Retaliate:
                    r.Hostility = MathEx.Pct(r.Hostility + 6 * ctx.Config.World.RivalHostility);
                    if (ctx.Faction != null && r.Relation == RelationStatus.War && ctx.Config.Gameplay.RivalAttacksEnabled
                        && w.PendingAttacks.All(a => a.AttackerId != r.Id))
                    {
                        var turf = TerritorySystem.OwnedBy(w, FactionIds.Player).Where(t => !t.IsStrategic).ToList();
                        if (turf.Count > 0)
                        {
                            var t = turf.OrderBy(x => w.Territories.Where(y => y.OwnerId == r.Id).Select(y => y.Center.DistanceTo2D(x.Center)).DefaultIfEmpty(5000).Min()).First();
                            WarSystem.CreateAttack(ctx, t, r.Id, r.Strength * ctx.Rng.Range(0.45f, 0.85f), false);
                        }
                    }
                    break;
                case RivalGoal.SeekAllies:
                {
                    var other = w.GetRival(target);
                    if (other == null) return;
                    var rel = lw.GetOrCreate(r.Id, other.Id, w.Day);
                    var otherLeader = other.Leader ?? new RivalLeader();
                    float accept = 0.35f + 0.25f * otherLeader.Caution + 0.2f * leader.Charisma - rel.Hostility / 150f
                                   + (other.Temperament == RivalTemperament.Opportunist ? 0.1f : 0);
                    if (ctx.Rng.Chance(MathEx.Clamp(accept, 0.05f, 0.9f)))
                    {
                        rel.Hostility = Math.Min(rel.Hostility, 15);
                        SetRelation(ctx, rel, RelationStatus.Alliance, Fmt("{0} and {1} formed an alliance.", r.Name, other.Name));
                    }
                    else
                    {
                        rel.Hostility = MathEx.Pct(rel.Hostility + 3);
                        News(ctx, WorldNewsKind.Alliance, Fmt("{0} turned down an alliance with {1}.", other.Name, r.Name), false, null, r.Id, other.Id);
                    }
                    break;
                }
                case RivalGoal.MakePeace:
                {
                    var enemy = w.GetRival(target);
                    if (enemy == null) return;
                    var rel = lw.Get(r.Id, enemy.Id);
                    if (rel == null || rel.Status != RelationStatus.War) return;
                    var el = enemy.Leader ?? new RivalLeader();
                    float accept = 0.25f + 0.4f * el.Caution + (enemy.Morale < 40 ? 0.2f : 0) + (enemy.Strength < r.Strength * 1.2f ? 0.15f : -0.15f)
                                   - (enemy.Temperament == RivalTemperament.Fanatic ? 0.3f : 0);
                    if (ctx.Rng.Chance(MathEx.Clamp(accept, 0.05f, 0.9f)))
                    {
                        rel.Hostility = Math.Min(rel.Hostility, 55);
                        foreach (var b in lw.ActiveBattles.Where(b => Pair(b, r.Id, enemy.Id)).ToList()) EndBattle(ctx, b, BattleStatus.Cancelled);
                        SetRelation(ctx, rel, RelationStatus.Ceasefire, Fmt("{0} and {1} agreed to a ceasefire.", r.Name, enemy.Name));
                    }
                    break;
                }
                case RivalGoal.LayLow:
                    r.Morale = MathEx.Clamp(r.Morale + 5, 0, 100);
                    r.Hostility = MathEx.Pct(r.Hostility - 2);
                    break;
                default:
                {
                    r.Morale = MathEx.Clamp(r.Morale + 2, 0, 100);
                    foreach (var t in w.Territories.Where(t => t.OwnerId == r.Id)) TerritorySystem.AddInfluence(t, r.Id, 2f);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ battles

        public static WorldBattle StartBattle(SimContext ctx, RivalFaction attacker, Territory t)
        {
            var w = ctx.World;
            var lw = w.Living;
            var defender = w.GetRival(t.OwnerId);
            if (defender != null)
            {
                var rel = lw.GetOrCreate(attacker.Id, defender.Id, w.Day);
                rel.Hostility = MathEx.Pct(Math.Max(rel.Hostility, 70) + 5);
                if (rel.Status != RelationStatus.War)
                    SetRelation(ctx, rel, RelationStatus.War, Fmt("{0} declared war on {1}.", attacker.Name, defender.Name));
            }
            float armsA = 0.6f + 0.4f * attacker.Arms / 100f;
            var b = new WorldBattle
            {
                Id = Ids.New("bt"),
                TerritoryId = t.Id,
                AttackerId = attacker.Id,
                DefenderId = defender?.Id,
                AttackerForce = Math.Max(4, Math.Min(attacker.Manpower * 0.4f, 32)) * armsA,
                DefenderForce = defender != null
                    ? Math.Max(3, Math.Min(defender.Manpower * 0.35f, 30)) * (0.6f + 0.4f * defender.Arms / 100f) + t.Fortification * 0.15f
                    : ctx.Rng.Range(3, 7),
                StartTick = w.Tick,
                MaxHours = 4 + ctx.Rng.Next(0, 5),
            };
            b.AttackerStart = b.AttackerForce;
            b.DefenderStart = b.DefenderForce;
            lw.Battles.Add(b);
            lw.BattlesFought++;
            Api.ApiHub.PublishBattleStarted(b);
            t.Contested = true;
            News(ctx, WorldNewsKind.Battle, defender != null
                ? Fmt("{0} attacked {1} in {2}.", attacker.Name, defender.Name, t.Name)
                : Fmt("{0} is moving into {1}.", attacker.Name, t.Name), defender != null, t.Id, attacker.Id, defender?.Id);
            return b;
        }

        private static void ProgressBattles(SimContext ctx)
        {
            var w = ctx.World;
            foreach (var b in w.Living.Battles.Where(x => x.IsActive).ToList())
            {
                var att = w.GetRival(b.AttackerId);
                var def = w.GetRival(b.DefenderId);
                var t = w.GetTerritory(b.TerritoryId);
                if (att == null || !att.Active || t == null) { EndBattle(ctx, b, BattleStatus.Cancelled); continue; }
                if (b.DefenderId != null && (def == null || !def.Active)) { EndBattle(ctx, b, BattleStatus.AttackerWon); continue; }

                float aPow = b.AttackerForce * (0.6f + 0.4f * att.Arms / 100f) * (0.7f + 0.6f * att.Morale / 100f) * (0.85f + 0.3f * (att.Leader?.Brutality ?? 0.5f));
                float dPow = b.DefenderForce * (def != null ? (0.6f + 0.4f * def.Arms / 100f) * (0.7f + 0.6f * def.Morale / 100f) : 0.8f) * (1f + t.Fortification / 100f);
                float aLoss = dPow * ctx.Rng.Range(0.07f, 0.16f);
                float dLoss = aPow * ctx.Rng.Range(0.07f, 0.16f);
                b.AttackerForce = Math.Max(0, b.AttackerForce - aLoss);
                b.DefenderForce = Math.Max(0, b.DefenderForce - dLoss);
                // About half of the losses are deaths, the rest wounded.
                att.Strength = Math.Max(0, att.Strength - aLoss * 0.5f / 1.2f);
                att.Arms = Math.Max(0, att.Arms - 1.2f);
                if (def != null)
                {
                    def.Strength = Math.Max(0, def.Strength - dLoss * 0.5f / 1.2f);
                    def.Arms = Math.Max(0, def.Arms - 1f);
                }
                t.Unrest = MathEx.Pct(t.Unrest + 2);
                b.Hours++;

                if (b.AttackerForce <= b.AttackerStart * 0.25f) EndBattle(ctx, b, BattleStatus.DefenderWon);
                else if (b.DefenderForce <= b.DefenderStart * 0.25f) EndBattle(ctx, b, BattleStatus.AttackerWon);
                else if (b.Hours >= b.MaxHours)
                {
                    float ra = b.AttackerForce / Math.Max(1f, b.AttackerStart), rd = b.DefenderForce / Math.Max(1f, b.DefenderStart);
                    EndBattle(ctx, b, ra > rd + 0.15f ? BattleStatus.AttackerWon : rd > ra + 0.15f ? BattleStatus.DefenderWon : BattleStatus.Stalemate);
                }
            }
        }

        /// <summary>
        /// Live casualties from the game: fighters of one side of a battle died near the player.
        /// <paramref name="byPlayer"/> means the player or the player's members did it, which counts as taking sides.
        /// </summary>
        public static void ReportLiveCasualties(SimContext ctx, string battleId, bool attackerSide, int count, bool byPlayer)
        {
            var b = ctx.World.Living?.Battles.FirstOrDefault(x => x.Id == battleId && x.IsActive);
            if (b == null || count <= 0) return;
            if (attackerSide) { b.LiveAttackerLosses += count; b.AttackerForce = Math.Max(0, b.AttackerForce - count); }
            else { b.LiveDefenderLosses += count; b.DefenderForce = Math.Max(0, b.DefenderForce - count); }
            var side = ctx.World.GetRival(attackerSide ? b.AttackerId : b.DefenderId);
            if (side != null) side.Strength = Math.Max(0, side.Strength - count * 0.5f / 1.2f);
            if (byPlayer && b.PlayerSide == null) b.PlayerSide = attackerSide ? "defender" : "attacker";
            if (b.AttackerForce <= b.AttackerStart * 0.25f) EndBattle(ctx, b, BattleStatus.DefenderWon);
            else if (b.DefenderForce <= b.DefenderStart * 0.25f) EndBattle(ctx, b, BattleStatus.AttackerWon);
        }

        public static void EndBattle(SimContext ctx, WorldBattle b, BattleStatus status)
        {
            var w = ctx.World;
            b.Status = status;
            b.EndTick = w.Tick;
            var t = w.GetTerritory(b.TerritoryId);
            var att = w.GetRival(b.AttackerId);
            var def = w.GetRival(b.DefenderId);
            if (t != null) t.Contested = w.Living.ActiveBattles.Any(x => x.TerritoryId == t.Id) || w.PendingAttacks.Any(a => a.TerritoryId == t.Id);
            Api.ApiHub.PublishBattleEnded(b);
            if (status == BattleStatus.Cancelled || t == null || att == null) return;

            string defName = def?.Name ?? w.FactionName(b.DefenderId, ctx.Faction);
            if (status == BattleStatus.AttackerWon)
            {
                // A rout hands the territory over; a narrow win only dents the defender's hold.
                float decisive = MathEx.Clamp01(1f - b.DefenderForce / Math.Max(1f, b.DefenderStart));
                TerritorySystem.AddInfluence(t, att.Id, 20 + 45 * decisive);
                if (b.DefenderId != null) TerritorySystem.AddInfluence(t, b.DefenderId, -(15 + 35 * decisive));
                att.Morale = MathEx.Clamp(att.Morale + 8, 0, 100);
                if (def != null) def.Morale = MathEx.Clamp(def.Morale - 10, 0, 100);
            }
            else if (status == BattleStatus.DefenderWon)
            {
                TerritorySystem.AddInfluence(t, att.Id, -10);
                if (b.DefenderId != null) TerritorySystem.AddInfluence(t, b.DefenderId, 8);
                att.Morale = MathEx.Clamp(att.Morale - 10, 0, 100);
                if (def != null) def.Morale = MathEx.Clamp(def.Morale + 6, 0, 100);
            }
            bool changed = TerritorySystem.UpdateOwnership(t, out string prev);
            if (changed) TerritorySystem.OnOwnershipChanged(ctx, t, prev);

            string text;
            if (status == BattleStatus.AttackerWon)
                text = changed && t.OwnerId == att.Id
                    ? (def != null ? Fmt("{0} took {1} from {2}.", att.Name, t.Name, defName) : Fmt("{0} took control of {1}.", att.Name, t.Name))
                    : Fmt("{0} won the fight in {1}.", att.Name, t.Name);
            else if (status == BattleStatus.DefenderWon)
                text = def != null ? Fmt("{0} held {1} against {2}.", def.Name, t.Name, att.Name) : Fmt("{0} was driven out of {1}.", att.Name, t.Name);
            else
                text = Fmt("The fight for {0} between {1} and {2} ended in a stalemate.", t.Name, att.Name, defName);
            News(ctx, changed ? WorldNewsKind.Territory : WorldNewsKind.Battle, text, changed || b.PlayerSide != null, t.Id, att.Id, def?.Id);

            // Leaders and lieutenants can fall in a lost battle.
            var loser = status == BattleStatus.AttackerWon ? def : status == BattleStatus.DefenderWon ? att : null;
            if (loser != null)
            {
                if (ctx.Rng.Chance(0.05f)) KillLeader(ctx, loser, null, false);
                else if (loser.Lieutenants.Count > 0 && ctx.Rng.Chance(0.12f))
                {
                    var lt = ctx.Rng.Pick(loser.Lieutenants);
                    loser.Lieutenants.Remove(lt);
                    News(ctx, WorldNewsKind.Leader, Fmt("{0} {1} of {2} was killed in {3}.", lt.Title, lt.Name, loser.Name, t.Name), false, t.Id, loser.Id);
                }
            }

            // The player took a side: the helped faction warms up, the other one remembers.
            if (b.PlayerSide != null && ctx.Faction != null)
            {
                var helped = b.PlayerSide == "attacker" ? att : def;
                var hurt = b.PlayerSide == "attacker" ? def : att;
                if (helped != null) helped.Hostility = MathEx.Pct(helped.Hostility - 15);
                if (hurt != null) hurt.Hostility = MathEx.Pct(hurt.Hostility + 18 * ctx.Config.World.RivalHostility);
                ctx.Faction.Reputation = MathEx.Pct(ctx.Faction.Reputation + 2);
                if (helped != null)
                    ctx.Report("World", Fmt("{0} noticed you fought on their side in {1}.", helped.Name, t.Name), EventSeverity.Good);
            }
        }

        // ------------------------------------------------------------------ leadership

        private static void LeadershipTick(SimContext ctx, List<RivalFaction> active)
        {
            var w = ctx.World;
            foreach (var r in active)
            {
                if (r.Leader == null || !r.Leader.Alive) Succession(ctx, r, false);
                if (r.Lieutenants.Count < 2 && r.Strength > 20 && ctx.Rng.Chance(0.04f))
                {
                    var lt = NewLeader(ctx, r, true);
                    r.Lieutenants.Add(lt);
                    News(ctx, WorldNewsKind.Leader, Fmt("{0} promoted {1} to {2}.", r.Name, lt.Name, lt.Title), false, null, r.Id);
                }
                foreach (var lt in r.Lieutenants)
                {
                    float drift = (r.Morale - 50) * 0.002f - (r.Wealth < 10 ? 0.05f : 0);
                    lt.Loyalty = MathEx.Clamp(lt.Loyalty + drift, 0, 100);
                }
                // Coup: an ambitious, disloyal lieutenant in a demoralized faction.
                var plotter = r.Lieutenants.Where(l => l.Ambition > 0.7f && l.Loyalty < 40).OrderByDescending(l => l.Ambition).FirstOrDefault();
                if (plotter != null && r.Morale < 30 && ctx.Rng.Chance(0.02f))
                {
                    var old = r.Leader;
                    r.Lieutenants.Remove(plotter);
                    plotter.Title = LeaderTitle(r, false);
                    plotter.SinceDay = w.Day;
                    plotter.Loyalty = 100;
                    r.Leader = plotter;
                    r.Strength = Math.Max(0, r.Strength * 0.9f);
                    r.Morale = MathEx.Clamp(r.Morale + 10, 0, 100);
                    w.Living.LeadersFallen++;
                    News(ctx, WorldNewsKind.Leader, Fmt("{0} overthrew {1} and now leads {2}.", plotter.Name, old?.Name ?? "?", r.Name), true, null, r.Id);
                }
            }
        }

        /// <summary>Kills a faction's leader (in battle or by the player) and runs the succession.</summary>
        public static void KillLeader(SimContext ctx, RivalFaction r, string where, bool byPlayer)
        {
            if (r?.Leader == null || !r.Leader.Alive) return;
            var dead = r.Leader;
            dead.Alive = false;
            ctx.World.Living.LeadersFallen++;
            r.Morale = MathEx.Clamp(r.Morale - 15, 0, 100);
            if (byPlayer)
            {
                r.Hostility = MathEx.Pct(r.Hostility + 30 * ctx.Config.World.RivalHostility);
                if (ctx.Faction != null) ctx.Faction.Reputation = MathEx.Pct(ctx.Faction.Reputation + 5);
            }
            News(ctx, WorldNewsKind.Leader, byPlayer
                ? Fmt("You killed {0}, {1} of {2}.", dead.Name, dead.Title, r.Name)
                : Fmt("{0}, {1} of {2}, was killed.", dead.Name, dead.Title, r.Name), true, null, r.Id);
            Succession(ctx, r, true);
        }

        private static void Succession(SimContext ctx, RivalFaction r, bool announced)
        {
            var w = ctx.World;
            var heir = r.Lieutenants.OrderByDescending(l => l.Ambition * 0.6f + l.Charisma * 0.4f).FirstOrDefault();
            if (heir != null)
            {
                r.Lieutenants.Remove(heir);
                // A contested succession splits the faction.
                if (r.Lieutenants.Any(l => l.Ambition > 0.75f) && ctx.Rng.Chance(0.35f))
                {
                    r.Strength = Math.Max(0, r.Strength * 0.85f);
                    r.Morale = MathEx.Clamp(r.Morale - 10, 0, 100);
                    News(ctx, WorldNewsKind.Leader, Fmt("A power struggle split {0} after its leader fell.", r.Name), false, null, r.Id);
                }
            }
            else heir = NewLeader(ctx, r, false);
            heir.Title = LeaderTitle(r, false);
            heir.SinceDay = w.Day;
            heir.Loyalty = 100;
            r.Leader = heir;
            // The new leader reshapes the faction's attitude.
            if (heir.Brutality > 0.7f && r.Temperament != RivalTemperament.Aggressive && r.Kind == RivalKind.StreetGang) r.Temperament = RivalTemperament.Aggressive;
            else if (heir.Caution > 0.75f && r.Temperament == RivalTemperament.Aggressive) r.Temperament = RivalTemperament.Opportunist;
            News(ctx, WorldNewsKind.Leader, Fmt("{0} is the new {1} of {2}.", heir.Name, heir.Title, r.Name), announced, null, r.Id);
        }

        /// <summary>A faction with no turf and no fighters left collapses (without player credit unless the player caused it).</summary>
        private static void CheckCollapse(SimContext ctx, RivalFaction r)
        {
            var w = ctx.World;
            if (r.Eliminated || r.Strength > 1f || w.Territories.Any(t => t.OwnerId == r.Id)) return;
            if (ctx.Faction != null && r.Relation == RelationStatus.War && r.WarScore > 0)
            {
                WarSystem.CheckElimination(ctx, r);   // the player's war did it: normal credit
                if (r.Eliminated) News(ctx, WorldNewsKind.Elimination, Fmt("{0} collapsed and its crews scattered.", r.Name), false, null, r.Id);
                return;
            }
            r.Eliminated = true;
            r.Relation = RelationStatus.Neutral;
            w.PendingAttacks.RemoveAll(a => a.AttackerId == r.Id);
            foreach (var t in w.Territories) if (t.Influence.Remove(r.Id)) TerritorySystem.UpdateOwnership(t);
            foreach (var b in w.Living.ActiveBattles.Where(b => b.AttackerId == r.Id || b.DefenderId == r.Id).ToList())
                EndBattle(ctx, b, b.AttackerId == r.Id ? BattleStatus.Cancelled : BattleStatus.AttackerWon);
            News(ctx, WorldNewsKind.Elimination, Fmt("{0} collapsed and its crews scattered.", r.Name), true, null, r.Id);
        }

        // ------------------------------------------------------------------ player reactions

        /// <summary>The player (or members) killed fighters of <paramref name="victim"/>: its enemies appreciate it.</summary>
        public static void OnPlayerHitFaction(SimContext ctx, RivalFaction victim, int kills)
        {
            var lw = ctx.World.Living;
            if (victim == null || lw == null || kills <= 0) return;
            foreach (var war in WarsOf(ctx.World, victim.Id))
            {
                var enemy = ctx.World.GetRival(war.Other(victim.Id));
                if (enemy != null) enemy.Hostility = MathEx.Pct(enemy.Hostility - kills * 0.6f);
            }
        }

        // ------------------------------------------------------------------ helpers

        public static void SetRelation(SimContext ctx, FactionRelation rel, RelationStatus status, string news)
        {
            if (rel.Status == status) return;
            rel.Status = status;
            rel.SinceDay = ctx.World.Day;
            if (news != null)
            {
                var kind = status == RelationStatus.War ? WorldNewsKind.War : status == RelationStatus.Alliance ? WorldNewsKind.Alliance : WorldNewsKind.Peace;
                News(ctx, kind, news, status == RelationStatus.War || status == RelationStatus.Alliance, null, rel.A, rel.B);
            }
        }

        private static string Name(WorldState w, string id) => w.GetRival(id)?.Name ?? id;

        public static string Fmt(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);

        /// <summary>Adds a world news item; major items also become a toast depending on settings.</summary>
        public static void News(SimContext ctx, WorldNewsKind kind, string text, bool major, string territoryId, params string[] factions)
        {
            var w = ctx.World;
            var item = new WorldNews { Day = w.Day, Hour = w.Hour, Kind = kind, Text = text, TerritoryId = territoryId, FactionIds = factions.Where(f => f != null).ToList() };
            w.Living.News.Add(item);
            Api.ApiHub.PublishNews(item);
            var level = ctx.Config.World.LivingWorldNotifications;
            if (level == Config.WorldNotifyLevel.None) return;
            if (level == Config.WorldNotifyLevel.Major && !major) return;
            ctx.Notifications.Add(new SimNotification { Text = text, Severity = EventSeverity.Info, Category = "World" });
        }

        /// <summary>Short status line for menus.</summary>
        public static string GoalText(RivalGoal g)
        {
            switch (g)
            {
                case RivalGoal.Recruit: return "Recruiting";
                case RivalGoal.Arm: return "Arming up";
                case RivalGoal.Expand: return "Expanding";
                case RivalGoal.Retaliate: return "Planning retaliation";
                case RivalGoal.SeekAllies: return "Seeking allies";
                case RivalGoal.MakePeace: return "Seeking peace";
                case RivalGoal.LayLow: return "Laying low";
                default: return "Consolidating";
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    /// <summary>Random faction events with distinct pools per faction type (family events, internal conflicts, defections...).</summary>
    public static class EventSystem
    {
        private sealed class EventDef
        {
            public string Id;
            public float Weight;
            public FactionType? Type;
            public bool GovernmentOnly;
            public Func<SimContext, bool> Condition;
            public Action<SimContext> Apply;
        }

        private static readonly List<EventDef> Defs = new List<EventDef>
        {
            // ---------------- Common
            new EventDef { Id = "windfall", Weight = 1f, Apply = c =>
            {
                long amt = 1500 + c.Rng.Next(0, 4000) * Math.Max(1, c.Faction.Level / 3);
                c.Faction.Resources.Cash += amt;
                c.Report("Event", "A lucky score: one of your crews brought in an unexpected " + TextUtil.Money(amt) + ".", EventSeverity.Good);
            }},
            new EventDef { Id = "dispute", Weight = 1.2f, Condition = c => c.Faction.ActiveMembers.Count() >= 2, Apply = c =>
            {
                var two = c.Faction.ActiveMembers.ToList(); c.Rng.Shuffle(two);
                var a = two[0]; var b = two[1];
                MemberSystem.AdjustOpinion(a, b, -25); MemberSystem.AdjustOpinion(b, a, -25);
                a.Morale = MathEx.Pct(a.Morale - 5); b.Morale = MathEx.Pct(b.Morale - 5);
                c.Faction.Discipline = MathEx.Pct(c.Faction.Discipline - 2);
                c.Report("Internal", a.ShortName + " and " + b.ShortName + " got into a heated dispute. Tension in the ranks.", EventSeverity.Warning);
            }},
            new EventDef { Id = "heroics", Weight = 0.8f, Condition = c => c.Faction.ActiveMembers.Any(), Apply = c =>
            {
                var m = c.Rng.Pick(c.Faction.ActiveMembers.ToList());
                m.Loyalty = MathEx.Pct(m.Loyalty + 8); m.Skill = MathEx.Pct(m.Skill + 3);
                foreach (var o in c.Faction.ActiveMembers) MemberSystem.AdjustOpinion(o, m, 10);
                c.Report("Members", m.DisplayName + " earned respect handling a dangerous situation alone.", EventSeverity.Good);
            }},
            new EventDef { Id = "theft", Weight = 0.6f, Condition = c => c.Faction.Resources.Arms > 10, Apply = c =>
            {
                int lost = 2 + c.Rng.Next(0, 5);
                c.Faction.Resources.Arms = Math.Max(0, c.Faction.Resources.Arms - lost);
                c.Report("Internal", "Someone has been skimming from the stockpile: " + lost + " Arms missing.", EventSeverity.Warning);
            }},
            new EventDef { Id = "friendship", Weight = 0.8f, Condition = c => c.Faction.ActiveMembers.Count() >= 2, Apply = c =>
            {
                var two = c.Faction.ActiveMembers.ToList(); c.Rng.Shuffle(two);
                MemberSystem.AdjustOpinion(two[0], two[1], 20); MemberSystem.AdjustOpinion(two[1], two[0], 20);
                c.Report("Members", two[0].ShortName + " and " + two[1].ShortName + " have become close friends.", EventSeverity.Info, false);
            }},

            // ---------------- Street gang
            new EventDef { Id = "block_party", Type = FactionType.StreetGang, Weight = 1f, Apply = c =>
            {
                c.Faction.Resources.Cash = Math.Max(0, c.Faction.Resources.Cash - 1500);
                c.Faction.Reputation = MathEx.Pct(c.Faction.Reputation + 3);
                c.Faction.PublicSupport = MathEx.Pct(c.Faction.PublicSupport + 3);
                foreach (var m in c.Faction.ActiveMembers) m.Morale = MathEx.Pct(m.Morale + 4);
                c.Report("Event", "You threw a block party for the neighborhood. Reputation and support up.", EventSeverity.Good);
            }},
            new EventDef { Id = "online_beef", Type = FactionType.StreetGang, Weight = 1f, Condition = c => c.World.Rivals.Any(r => r.Active && r.Kind == RivalKind.StreetGang), Apply = c =>
            {
                var r = c.Rng.Pick(c.World.Rivals.Where(x => x.Active && x.Kind == RivalKind.StreetGang).ToList());
                r.Hostility = MathEx.Pct(r.Hostility + 12);
                c.Report("Relations", "A diss track about " + r.Name + " went viral. They are not happy.", EventSeverity.Warning);
            }},
            new EventDef { Id = "young_hopefuls", Type = FactionType.StreetGang, Weight = 0.8f, Apply = c =>
            {
                c.Faction.Reputation = MathEx.Pct(c.Faction.Reputation + 2);
                c.Faction.Recruitment.HireCost = Math.Max(1000, c.Faction.Recruitment.HireCost - 250);
                c.Report("Recruitment", "Kids on the block want in. Hiring through your network got cheaper.", EventSeverity.Good);
            }},
            new EventDef { Id = "corner_shootout", Type = FactionType.StreetGang, Weight = 0.6f, Condition = c => c.Faction.ActiveMembers.Any(), Apply = c =>
            {
                var m = c.Rng.Pick(c.Faction.ActiveMembers.ToList());
                m.Status = MemberStatus.Wounded; m.RecoveryHours = 18;
                c.Faction.Suspicion = MathEx.Pct(c.Faction.Suspicion + 4);
                c.Report("Members", m.DisplayName + " got caught in a drive-by and is recovering.", EventSeverity.Bad);
            }},

            // ---------------- Mafia
            new EventDef { Id = "wedding", Type = FactionType.Mafia, Weight = 0.9f, Apply = c =>
            {
                c.Faction.Resources.Cash = Math.Max(0, c.Faction.Resources.Cash - 5000);
                c.Faction.Resources.Influence += 4;
                foreach (var m in c.Faction.ActiveMembers) { m.Loyalty = MathEx.Pct(m.Loyalty + 4); m.Morale = MathEx.Pct(m.Morale + 5); }
                c.Report("Family", "A family wedding: envelopes, toasts and new friendships. Loyalty rises across the family.", EventSeverity.Good);
            }},
            new EventDef { Id = "funeral", Type = FactionType.Mafia, Weight = 0.5f, Condition = c => c.Faction.Members.Any(m => m.Status == MemberStatus.Dead), Apply = c =>
            {
                foreach (var m in c.Faction.ActiveMembers) { m.Loyalty = MathEx.Pct(m.Loyalty + 3); m.Morale = MathEx.Pct(m.Morale - 3); }
                c.Report("Family", "The family gathered to bury one of their own. Grief, and a renewed sense of loyalty.", EventSeverity.Info);
            }},
            new EventDef { Id = "made_man", Type = FactionType.Mafia, Weight = 0.7f, Condition = c => c.Faction.ActiveMembers.Any(m => m.RankId == c.Faction.LowestRank?.Id), Apply = c =>
            {
                var cands = c.Faction.ActiveMembers.Where(m => m.RankId == c.Faction.LowestRank?.Id).OrderByDescending(m => m.Loyalty).ToList();
                var m = cands.First();
                var res = RankSystem.Promote(c.Faction, m, c.World.Day);
                if (res.Ok) c.Report("Family", m.DisplayName + " made his bones and was formally inducted. " + res.Message, EventSeverity.Good);
            }},
            new EventDef { Id = "betrayal_rumor", Type = FactionType.Mafia, Weight = 0.6f, Condition = c => c.Faction.ActiveMembers.Any(m => m.Loyalty < 45), Apply = c =>
            {
                var m = c.Faction.ActiveMembers.OrderBy(x => x.Loyalty).First();
                if (c.Rng.Chance(0.4f))
                {
                    m.Status = MemberStatus.Deserted;
                    c.Faction.Suspicion = MathEx.Pct(c.Faction.Suspicion + 10);
                    c.Report("Family", m.DisplayName + " broke omertà and vanished. Heat rises.", EventSeverity.Bad);
                }
                else
                {
                    m.Loyalty = MathEx.Pct(m.Loyalty + 10);
                    c.Report("Family", "Rumors of betrayal around " + m.ShortName + " turned out false. He's grateful you trusted him.", EventSeverity.Info);
                }
            }},
            new EventDef { Id = "favor", Type = FactionType.Mafia, Weight = 0.8f, Apply = c =>
            {
                c.Faction.Resources.Influence += 6;
                c.World.State.Stability = MathEx.Pct(c.World.State.Stability - 1);
                c.Report("Family", "A city official asked the family for a quiet favor. Influence gained.", EventSeverity.Good);
            }},
            new EventDef { Id = "envoy", Type = FactionType.Mafia, Weight = 0.6f, Condition = c => c.World.Rivals.Any(r => r.Active && (r.Kind == RivalKind.Mafia || r.Kind == RivalKind.Cartel)), Apply = c =>
            {
                var r = c.Rng.Pick(c.World.Rivals.Where(x => x.Active && (x.Kind == RivalKind.Mafia || x.Kind == RivalKind.Cartel)).ToList());
                r.Hostility = MathEx.Pct(r.Hostility - 10);
                c.Report("Relations", r.Name + " sent an envoy with gifts. Relations improve.", EventSeverity.Good);
            }},

            // ---------------- Militia
            new EventDef { Id = "training", Type = FactionType.Militia, Weight = 1f, Apply = c =>
            {
                c.Faction.Resources.Supplies = Math.Max(0, c.Faction.Resources.Supplies - 3);
                foreach (var m in c.Faction.ActiveMembers) { m.Skill = MathEx.Pct(m.Skill + 1.5f); m.Discipline = MathEx.Pct(m.Discipline + 2); }
                c.Report("Training", "Field exercise in the hills. Skill and discipline improved.", EventSeverity.Good);
            }},
            new EventDef { Id = "defector", Type = FactionType.Militia, Weight = 0.6f, Apply = c =>
            {
                c.Faction.Resources.Intel += 6;
                c.World.State.Stability = MathEx.Pct(c.World.State.Stability - 1);
                c.Report("Intel", "A disillusioned state employee leaked documents to your network. +6 Intel.", EventSeverity.Good);
            }},
            new EventDef { Id = "propaganda", Type = FactionType.Militia, Weight = 0.8f, Apply = c =>
            {
                c.Faction.PublicSupport = MathEx.Pct(c.Faction.PublicSupport + 4);
                c.Faction.Suspicion = MathEx.Pct(c.Faction.Suspicion + 2);
                c.Report("Support", "Your message is spreading. Public support rises (and so does attention).", EventSeverity.Good);
            }},
            new EventDef { Id = "supply_drop", Type = FactionType.Militia, Weight = 0.7f, Apply = c =>
            {
                int amt = 5 + c.Rng.Next(0, 8);
                c.Faction.Resources.Supplies += amt;
                c.Report("Logistics", "Sympathizers delivered " + amt + " supplies.", EventSeverity.Good);
            }},
            new EventDef { Id = "split", Type = FactionType.Militia, Weight = 0.5f, Condition = c => c.Faction.ActiveMembers.Any(m => m.Personality == Personality.Rebellious), Apply = c =>
            {
                c.Faction.Discipline = MathEx.Pct(c.Faction.Discipline - 5);
                foreach (var m in c.Faction.ActiveMembers.Where(m => m.Personality == Personality.Rebellious)) m.Loyalty = MathEx.Pct(m.Loyalty - 8);
                c.Report("Internal", "An ideological split is brewing among the hardliners. Discipline suffers.", EventSeverity.Warning);
            }},
            new EventDef { Id = "surveillance", Type = FactionType.Militia, Weight = 0.6f, Apply = c =>
            {
                c.Faction.Suspicion = MathEx.Pct(c.Faction.Suspicion + 6);
                c.Report("Suspicion", "An unmarked van has been parked near your safehouse for days.", EventSeverity.Warning);
            }},

            // ---------------- Government
            new EventDef { Id = "protests", GovernmentOnly = true, Weight = 1f, Apply = c =>
            {
                var g = c.Faction.Government;
                g.Stability = MathEx.Pct(g.Stability - 4); g.PublicSupport = MathEx.Pct(g.PublicSupport - 2);
                c.Report("State", "Protesters filled Legion Square demanding reforms.", EventSeverity.Warning);
            }},
            new EventDef { Id = "investment", GovernmentOnly = true, Weight = 0.8f, Apply = c =>
            {
                long amt = 20000 + c.Rng.Next(0, 30000);
                c.Faction.Government.Treasury += amt;
                c.Report("State", "Outside investors signed a deal with the new government (+" + TextUtil.Money(amt) + " treasury).", EventSeverity.Good);
            }},
            new EventDef { Id = "scandal", GovernmentOnly = true, Weight = 0.6f, Condition = c => c.Faction.Government.Ministries.Any(m => m.OfficialMemberId != null), Apply = c =>
            {
                var g = c.Faction.Government;
                var min = c.Rng.Pick(g.Ministries.Where(m => m.OfficialMemberId != null).ToList());
                var official = c.Faction.GetMember(min.OfficialMemberId);
                g.Legitimacy = MathEx.Pct(g.Legitimacy - 5);
                c.Report("State", "Corruption scandal at the " + min.Name + (official != null ? " involving " + official.ShortName : "") + ".", EventSeverity.Bad);
            }},
            new EventDef { Id = "coup_plot", GovernmentOnly = true, Weight = 0.4f, Condition = c => c.Faction.Government.Stability < 35, Apply = c =>
            {
                var g = c.Faction.Government;
                var plotter = c.Faction.ActiveMembers.Where(m => m.GovernmentRole != null).OrderBy(m => m.Loyalty).FirstOrDefault();
                if (g.ForcesReadiness > 50 || plotter == null)
                {
                    g.Stability = MathEx.Pct(g.Stability + 5);
                    c.Report("State", "Loyal forces uncovered and crushed a coup plot.", EventSeverity.Good);
                }
                else
                {
                    plotter.Status = MemberStatus.Deserted;
                    g.Stability = MathEx.Pct(g.Stability - 10);
                    c.Report("State", plotter.DisplayName + " attempted a coup and fled when it failed. Stability shaken.", EventSeverity.Critical);
                }
            }},
        };

        public static void Tick(SimContext ctx)
        {
            if (!ctx.Config.Gameplay.RandomEventsEnabled) return;
            if (!ctx.Rng.Chance(0.035f)) return;
            var f = ctx.Faction;
            var pool = Defs.Where(d =>
                (d.Type == null || d.Type == f.Type) &&
                (!d.GovernmentOnly || f.IsGovernment) &&
                (d.Condition == null || d.Condition(ctx))).ToList();
            if (pool.Count == 0) return;
            float total = pool.Sum(d => d.Weight);
            float roll = ctx.Rng.Range(0, total);
            foreach (var d in pool)
            {
                roll -= d.Weight;
                if (roll <= 0)
                {
                    d.Apply(ctx);
                    return;
                }
            }
        }

        /// <summary>Forces a specific event (used by tests/debug).</summary>
        public static bool Trigger(SimContext ctx, string id)
        {
            var d = Defs.FirstOrDefault(x => x.Id == id);
            if (d == null || ctx.Faction == null) return false;
            if (d.Type != null && d.Type != ctx.Faction.Type) return false;
            if (d.GovernmentOnly && !ctx.Faction.IsGovernment) return false;
            if (d.Condition != null && !d.Condition(ctx)) return false;
            d.Apply(ctx);
            return true;
        }

        public static IEnumerable<string> EventIds => Defs.Select(d => d.Id);
    }
}

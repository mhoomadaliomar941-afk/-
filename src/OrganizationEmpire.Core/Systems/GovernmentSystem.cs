using System;
using System.Linq;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    /// <summary>In-game simulation of running the fictional state after a successful takeover.</summary>
    public static class GovernmentSystem
    {
        public static OpResult Appoint(SimContext ctx, Ministry ministry, Member m)
        {
            var f = ctx.Faction;
            if (!f.IsGovernment) return OpResult.Fail("No government established.");
            if (ministry == null) return OpResult.Fail("No ministry selected.");
            if (m == null)
            {
                var prev = f.GetMember(ministry.OfficialMemberId);
                if (prev != null) prev.GovernmentRole = null;
                ministry.OfficialMemberId = null;
                return OpResult.Success(ministry.Name + " is now vacant.");
            }
            if (!m.IsAvailable) return OpResult.Fail(m.ShortName + " is not available.");
            if (m.Loyalty < 40) return OpResult.Fail(m.ShortName + " is not trusted enough (loyalty 40+ needed).");
            foreach (var other in f.Government.Ministries.Where(x => x.OfficialMemberId == m.Id)) other.OfficialMemberId = null;
            var old = f.GetMember(ministry.OfficialMemberId);
            if (old != null) old.GovernmentRole = null;
            ministry.OfficialMemberId = m.Id;
            m.GovernmentRole = ministry.Name;
            m.Loyalty = MathEx.Pct(m.Loyalty + 8);
            m.Morale = MathEx.Pct(m.Morale + 10);
            ctx.Report("State", m.DisplayName + " appointed to head the " + ministry.Name + ".", EventSeverity.Good);
            return OpResult.Success(m.ShortName + " appointed.");
        }

        public static float Effectiveness(FactionState f, Ministry ministry)
        {
            var m = f.GetMember(ministry.OfficialMemberId);
            if (m == null || !m.IsAvailable) return 0f;
            float e = (m.Skill * 0.4f + m.Discipline * 0.3f + m.Loyalty * 0.3f) / 100f;
            switch (ministry.Id)
            {
                case "treasury":
                case "intelligence":
                    if (m.Personality == Personality.Intelligent) e += 0.2f; break;
                case "defense":
                case "interior":
                    if (m.Personality == Personality.Disciplined) e += 0.2f; break;
                case "information":
                case "foreign":
                    if (m.Personality == Personality.Calm || m.Personality == Personality.Sarcastic) e += 0.1f; break;
            }
            if (m.Personality == Personality.Ambitious && m.Loyalty < 50) e -= 0.15f;
            return MathEx.Clamp(e, 0f, 1.2f);
        }

        public static OpResult TogglePolicy(SimContext ctx, string policyId)
        {
            var g = ctx.Faction.Government;
            if (g == null || !g.Established) return OpResult.Fail("No government established.");
            if (g.Policies.Remove(policyId)) return OpResult.Success("Policy repealed.");
            if (g.Policies.Count >= 4) return OpResult.Fail("At most 4 active policies.");
            g.Policies.Add(policyId);
            return OpResult.Success("Policy enacted.");
        }

        public static void SetTaxRate(GovernmentState g, float rate) => g.TaxRate = MathEx.Clamp(rate, 0f, 0.5f);

        public static void Tick(SimContext ctx)
        {
            var f = ctx.Faction;
            if (!f.IsGovernment) return;
            var g = f.Government;
            var w = ctx.World;

            float E(string id) => Effectiveness(f, g.Ministries.FirstOrDefault(m => m.Id == id) ?? new Ministry());
            bool P(string id) => g.Policies.Contains(id);

            // Taxes
            double baseTax = w.Territories.Where(t => t.OwnerId == FactionIds.Player).Sum(t => t.BaseValue) / 24.0;
            double taxes = baseTax * g.TaxRate * 2.0 * (g.Stability / 100.0) * (1 + E("treasury") * 0.3) * ctx.Config.Gameplay.IncomeMultiplier;
            if (P("free_trade")) taxes *= 1.2;
            long budget = g.Ministries.Sum(m => (long)m.Budget) / 24;
            if (P("public_works")) budget += 600 / 24;
            g.Treasury += (long)taxes - budget;
            if (g.Treasury < 0)
            {
                long deficit = -g.Treasury;
                long fromFaction = Math.Min(deficit, f.Resources.Cash);
                f.Resources.Cash -= fromFaction;
                g.Treasury += fromFaction;
                if (g.Treasury < 0) { g.Treasury = 0; g.Stability = MathEx.Pct(g.Stability - 0.5f); }
            }

            // Stats drift
            float tax = g.TaxRate;
            float stabilityDelta = 0.02f + E("interior") * 0.08f - Math.Max(0, tax - 0.2f) * 0.3f - g.Insurgency * 0.002f;
            float supportDelta = E("information") * 0.06f + E("publicworks") * 0.05f - Math.Max(0, tax - 0.15f) * 0.4f;
            float legitDelta = E("justice") * 0.05f - 0.005f;
            float readinessDelta = E("defense") * 0.08f - 0.02f;
            float insurgDelta = 0.02f + (50 - g.PublicSupport) * 0.002f - E("intelligence") * 0.07f
                                + w.Rivals.Count(r => r.Active && r.Relation == RelationStatus.War) * 0.01f;

            if (P("martial_law")) { stabilityDelta += 0.08f; readinessDelta += 0.05f; supportDelta -= 0.06f; legitDelta -= 0.04f; }
            if (P("curfew")) { insurgDelta -= 0.05f; supportDelta -= 0.03f; }
            if (P("amnesty")) { supportDelta += 0.04f; legitDelta += 0.03f; insurgDelta += 0.02f; }
            if (P("public_works")) supportDelta += 0.05f;
            if (P("free_trade")) insurgDelta += 0.02f;
            if (P("state_media")) { supportDelta += 0.04f; legitDelta -= 0.03f; }
            if (P("conscription")) { readinessDelta += 0.06f; supportDelta -= 0.04f; }

            g.Stability = MathEx.Pct(g.Stability + stabilityDelta);
            g.PublicSupport = MathEx.Pct(g.PublicSupport + supportDelta);
            g.Legitimacy = MathEx.Pct(g.Legitimacy + legitDelta);
            g.ForcesReadiness = MathEx.Pct(g.ForcesReadiness + readinessDelta);
            g.Insurgency = MathEx.Pct(g.Insurgency + insurgDelta);
            f.PublicSupport = g.PublicSupport;

            float foreign = E("foreign");
            if (foreign > 0) foreach (var r in w.Rivals.Where(r => r.Active)) r.Hostility = MathEx.Pct(r.Hostility - foreign * 0.05f);
            if (E("intelligence") > 0 && w.Tick % 6 == 0) f.Resources.Intel += 1;

            // Insurgent attacks on state territory
            if (g.Insurgency > 40 && ctx.Rng.Chance(0.01f * (g.Insurgency / 50f)) && ctx.Config.Gameplay.RivalAttacksEnabled)
            {
                var attacker = w.Rivals.Where(r => r.Active).OrderByDescending(r => r.Hostility).FirstOrDefault();
                var target = ctx.Rng.Pick(TerritorySystem.OwnedBy(w, FactionIds.Player).ToList());
                if (attacker != null && target != null && !w.PendingAttacks.Any(a => a.AttackerId == attacker.Id))
                    WarSystem.CreateAttack(ctx, target, attacker.Id, attacker.Strength * 0.6f + g.Insurgency * 0.3f, false);
            }
        }

        public static OpResult Rename(SimContext ctx, string stateName, string governmentName, string rulerTitle)
        {
            var g = ctx.Faction.Government;
            if (g == null || !g.Established) return OpResult.Fail("No government established.");
            if (!string.IsNullOrWhiteSpace(stateName)) g.StateName = TextUtil.Truncate(stateName.Trim(), 48);
            if (!string.IsNullOrWhiteSpace(governmentName)) { g.GovernmentName = TextUtil.Truncate(governmentName.Trim(), 48); ctx.World.State.Name = g.GovernmentName; }
            if (!string.IsNullOrWhiteSpace(rulerTitle)) g.RulerTitle = TextUtil.Truncate(rulerTitle.Trim(), 32);
            return OpResult.Success("Updated.");
        }
    }
}

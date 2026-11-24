using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    public sealed class LedgerLine
    {
        public string Label;
        public double Amount;
    }

    /// <summary>One hour of income and expenses, used by the Resources page for a breakdown.</summary>
    public sealed class Ledger
    {
        public readonly List<LedgerLine> Income = new List<LedgerLine>();
        public readonly List<LedgerLine> Expenses = new List<LedgerLine>();
        public double TotalIncome => Income.Sum(l => l.Amount);
        public double TotalExpenses => Expenses.Sum(l => l.Amount);
        public double Net => TotalIncome - TotalExpenses;
        public bool PaidInFull = true;

        public void AddIncome(string label, double amount) { if (amount > 0.0001) Income.Add(new LedgerLine { Label = label, Amount = amount }); }
        public void AddExpense(string label, double amount) { if (amount > 0.0001) Expenses.Add(new LedgerLine { Label = label, Amount = amount }); }
    }

    public static class EconomySystem
    {
        public const int ProductPrice = 140;
        public const int ArmsPrice = 260;
        public const int SupplyPrice = 90;

        public static float TerritoryTypeMultiplier(FactionType f, TerritoryType t)
        {
            switch (f)
            {
                case FactionType.Mafia:
                    switch (t) { case TerritoryType.Commercial: return 1.4f; case TerritoryType.Industrial: return 1.2f; case TerritoryType.Residential: return 0.8f; case TerritoryType.Rural: return 0.6f; default: return 1.0f; }
                case FactionType.Militia:
                    switch (t) { case TerritoryType.Strategic: return 1.5f; case TerritoryType.Rural: return 1.2f; case TerritoryType.Industrial: return 1.0f; default: return 0.7f; }
                default:
                    switch (t) { case TerritoryType.Residential: return 1.2f; case TerritoryType.Industrial: return 1.0f; case TerritoryType.Commercial: return 0.9f; case TerritoryType.Strategic: return 0.8f; default: return 0.7f; }
            }
        }

        /// <summary>Computes (and optionally applies) one hour of economy.</summary>
        public static Ledger Compute(SimContext ctx, bool apply)
        {
            var f = ctx.Faction;
            var w = ctx.World;
            var dp = ctx.Difficulty;
            float incomeMult = ctx.Config.Gameplay.IncomeMultiplier * dp.Income;
            var ledger = new Ledger();
            var owned = TerritorySystem.OwnedBy(w, FactionIds.Player).ToList();

            double territoryIncome = 0;
            foreach (var t in owned)
            {
                territoryIncome += t.BaseValue / 24.0 * TerritoryTypeMultiplier(f.Type, t.Type) * (1 - t.Unrest / 200.0);
            }
            if (f.IsGovernment) territoryIncome *= 0.5; // replaced largely by taxes (handled by GovernmentSystem)
            ledger.AddIncome("Territory (" + owned.Count + ")", territoryIncome * incomeMult);

            double bizIncome = f.Businesses.Sum(b => BusinessCatalog.Get(b.Kind).DailyIncome * (1 + (b.Level - 1) * 0.5) / 24.0);
            if (f.Type == FactionType.Mafia) bizIncome *= 1.25;
            ledger.AddIncome("Businesses (" + f.Businesses.Count + ")", bizIncome * incomeMult);

            int earners = f.ActiveMembers.Count(m => m.Role == MemberRole.Earner);
            ledger.AddIncome("Earners (" + earners + ")", earners * 35 * incomeMult);

            int productSold = 0, armsSold = 0, productMade = 0;
            if (f.Type == FactionType.StreetGang)
            {
                if ((f.Activities & GangActivities.Narcotics) != 0)
                {
                    productMade = Math.Min(f.Resources.Supplies, 1 + owned.Count(t => t.Type == TerritoryType.Residential || t.Type == TerritoryType.Industrial) / 2);
                    int outlets = Math.Max(1, owned.Count(t => t.Type == TerritoryType.Residential || t.Type == TerritoryType.Commercial));
                    productSold = Math.Min(f.Resources.Product + productMade, outlets);
                    ledger.AddIncome("Product sales (" + productSold + ")", productSold * ProductPrice * incomeMult);
                }
                if ((f.Activities & GangActivities.Arms) != 0)
                {
                    int reserve = 25 + (f.Headquarters?.Level(HqUpgrade.Armory) ?? 0) * 15;
                    armsSold = f.Resources.Arms > reserve ? 1 : 0;
                    ledger.AddIncome("Arms trade (" + armsSold + ")", armsSold * ArmsPrice * incomeMult);
                }
            }

            // Expenses
            double salaries = 0;
            foreach (var m in f.Members.Where(m => m.Status == MemberStatus.Active || m.Status == MemberStatus.Wounded))
            {
                var r = f.GetRank(m.RankId);
                salaries += (r?.Salary ?? 100) / 24.0;
            }
            ledger.AddExpense("Salaries", salaries * dp.Upkeep);
            ledger.AddExpense("Vehicles (" + f.Vehicles.Count + ")", f.Vehicles.Count * 60 / 24.0 * dp.Upkeep);
            if (f.Headquarters != null)
            {
                int upgrades = f.Headquarters.Upgrades.Values.Sum();
                ledger.AddExpense("Headquarters", 250 * (1 + upgrades * 0.2) / 24.0 * dp.Upkeep);
            }

            if (apply)
            {
                long net = (long)Math.Round(ledger.Net);
                long before = f.Resources.Cash;
                f.Resources.Cash += net;
                if (ledger.TotalIncome > 0) f.Stats.TotalEarned += (long)ledger.TotalIncome;
                f.Stats.TotalSpent += (long)ledger.TotalExpenses;
                if (f.Resources.Cash < 0)
                {
                    ledger.PaidInFull = false;
                    f.Resources.Cash = 0;
                    if (before > 0 || ctx.Rng.Chance(0.1f))
                        ctx.Report("Economy", "The treasury is empty - members are not getting paid. Morale and loyalty are falling.", EventSeverity.Bad);
                }

                // Production / sales side effects
                if (productMade > 0) { f.Resources.Supplies -= productMade; f.Resources.Product += productMade; }
                if (productSold > 0)
                {
                    f.Resources.Product = Math.Max(0, f.Resources.Product - productSold);
                    f.Suspicion = MathEx.Pct(f.Suspicion + productSold * 0.04f * dp.Suspicion);
                }
                if (armsSold > 0)
                {
                    f.Resources.Arms -= armsSold;
                    f.Suspicion = MathEx.Pct(f.Suspicion + 0.05f * dp.Suspicion);
                }

                // Slow resource trickles (every few hours)
                int tick = w.Tick;
                if (tick % 6 == 0)
                {
                    int rural = owned.Count(t => t.Type == TerritoryType.Rural || t.Type == TerritoryType.Industrial);
                    f.Resources.Supplies += rural * (f.Type == FactionType.Militia ? 2 : 1);
                    if (f.Type == FactionType.Mafia) f.Resources.Influence += Math.Max(1, f.Businesses.Count);
                    if (f.Type == FactionType.Militia && f.PublicSupport > 30) f.Resources.Influence += 1;
                    int comms = f.Headquarters?.Level(HqUpgrade.Comms) ?? 0;
                    f.Resources.Intel += comms + f.ActiveMembers.Count(m => m.Role == MemberRole.Scout) / 2;
                    if ((f.Activities & GangActivities.Arms) != 0) f.Resources.Arms += 1 + owned.Count(t => t.Type == TerritoryType.Industrial) / 2;
                    foreach (var b in f.Businesses) f.Suspicion = MathEx.Pct(f.Suspicion + BusinessCatalog.Get(b.Kind).HeatPerDay / 4f * dp.Suspicion);
                }
                ClampResources(f.Resources);
            }
            return ledger;
        }

        public static void ClampResources(Resources r)
        {
            r.Supplies = MathEx.Clamp(r.Supplies, 0, 999);
            r.Product = MathEx.Clamp(r.Product, 0, 999);
            r.Arms = MathEx.Clamp(r.Arms, 0, 999);
            r.Influence = MathEx.Clamp(r.Influence, 0, 999);
            r.Intel = MathEx.Clamp(r.Intel, 0, 999);
            r.Cash = MathEx.Clamp(r.Cash, 0L, 999_999_999L);
        }

        public static OpResult Spend(FactionState f, long amount, string what)
        {
            if (amount < 0) return OpResult.Fail("Invalid amount.");
            if (f.Resources.Cash < amount) return OpResult.Fail("Not enough cash for " + what + " (" + TextUtil.Money(amount) + " needed).");
            f.Resources.Cash -= amount;
            f.Stats.TotalSpent += amount;
            return OpResult.Success();
        }

        public static OpResult BuySupplies(FactionState f, int units)
        {
            var r = Spend(f, (long)units * SupplyPrice, "supplies");
            if (!r.Ok) return r;
            f.Resources.Supplies += units;
            ClampResources(f.Resources);
            return OpResult.Success("Bought " + units + " supplies.");
        }

        public static OpResult BuyArms(FactionState f, int units)
        {
            long price = (long)(ArmsPrice * 1.3) * units;
            var r = Spend(f, price, "arms");
            if (!r.Ok) return r;
            f.Resources.Arms += units;
            f.Suspicion = MathEx.Pct(f.Suspicion + units * 0.1f);
            ClampResources(f.Resources);
            return OpResult.Success("Bought " + units + " arms.");
        }

        public static OpResult SellProduct(FactionState f, int units)
        {
            units = Math.Min(units, f.Resources.Product);
            if (units <= 0) return OpResult.Fail("No product to move.");
            f.Resources.Product -= units;
            long earned = (long)(units * ProductPrice * 0.8);
            f.Resources.Cash += earned;
            f.Stats.TotalEarned += earned;
            f.Suspicion = MathEx.Pct(f.Suspicion + units * 0.15f);
            return OpResult.Success("Moved " + units + " product for " + TextUtil.Money(earned) + ".");
        }

        public static OpResult BuyBusiness(SimContext ctx, BusinessKind kind, string territoryId)
        {
            var f = ctx.Faction;
            var def = BusinessCatalog.Get(kind);
            int max = f.Type == FactionType.Mafia ? 10 : 4;
            if (f.Businesses.Count >= max) return OpResult.Fail("Your faction can run at most " + max + " businesses.");
            if (f.Type != FactionType.Mafia && f.Level < 3) return OpResult.Fail("Front businesses unlock at level 3.");
            var r = Spend(f, def.Price, def.Name);
            if (!r.Ok) return r;
            var b = new Business { Id = Ids.New("biz"), Kind = kind, Name = def.Name, TerritoryId = territoryId, AcquiredDay = ctx.World.Day };
            f.Businesses.Add(b);
            ctx.Report("Business", "Acquired " + def.Name + ".", EventSeverity.Good);
            ProgressionSystem.AddXp(ctx, 150);
            return OpResult.Success("Acquired " + def.Name + ".");
        }

        public static OpResult UpgradeBusiness(SimContext ctx, Business b)
        {
            if (b.Level >= 3) return OpResult.Fail("Already fully upgraded.");
            long cost = BusinessCatalog.Get(b.Kind).Price / 2 * b.Level;
            var r = Spend(ctx.Faction, cost, "upgrade");
            if (!r.Ok) return r;
            b.Level++;
            return OpResult.Success(b.Name + " upgraded to level " + b.Level + ".");
        }

        public static OpResult UpgradeHq(SimContext ctx, HqUpgrade u)
        {
            var f = ctx.Faction;
            if (f.Headquarters == null) return OpResult.Fail("Establish a headquarters first.");
            int lvl = f.Headquarters.Level(u);
            var def = HqUpgradeCatalog.Get(u);
            if (lvl >= def.MaxLevel) return OpResult.Fail(def.Name + " is fully upgraded.");
            long cost = HqUpgradeCatalog.Cost(u, lvl);
            var r = Spend(f, cost, def.Name);
            if (!r.Ok) return r;
            f.Headquarters.Upgrades[u] = lvl + 1;
            ctx.Report("Headquarters", def.Name + " upgraded to level " + (lvl + 1) + ".", EventSeverity.Good);
            ProgressionSystem.AddXp(ctx, 100);
            return OpResult.Success(def.Name + " upgraded.");
        }
    }

    public static class ProgressionSystem
    {
        public const int MaxLevel = 20;

        public static void AddXp(SimContext ctx, long amount)
        {
            var f = ctx.Faction;
            if (f == null || amount <= 0) return;
            f.Experience += (long)(amount * ctx.Config.Gameplay.ProgressionMultiplier);
            while (f.Level < MaxLevel && f.Experience >= FactionTypeInfo.XpForLevel(f.Level + 1))
            {
                f.Experience -= FactionTypeInfo.XpForLevel(f.Level + 1);
                string oldTier = FactionTypeInfo.TierName(f);
                f.Level++;
                string newTier = FactionTypeInfo.TierName(f);
                ctx.Report("Progression", f.Name + " reached level " + f.Level + "!" + (oldTier != newTier ? " You are now a " + newTier + "." : ""), EventSeverity.Good);
                foreach (var m in f.ActiveMembers) m.Morale = MathEx.Pct(m.Morale + 3);
            }
            if (f.Level >= MaxLevel) f.Experience = Math.Min(f.Experience, FactionTypeInfo.XpForLevel(MaxLevel));
        }

        public static float LevelProgress(FactionState f)
        {
            if (f.Level >= MaxLevel) return 1f;
            long need = FactionTypeInfo.XpForLevel(f.Level + 1);
            return need <= 0 ? 1f : MathEx.Clamp01(f.Experience / (float)need);
        }

        /// <summary>Overall faction power, used for diplomacy and the takeover campaign.</summary>
        public static float Power(SimContext ctx)
        {
            var f = ctx.Faction;
            float members = f.ActiveMembers.Sum(MemberSystem.CombatValue);
            float territory = TerritorySystem.PlayerTerritoryCount(ctx.World) * 4;
            return members + territory + f.Level * 2 + f.Reputation * 0.3f;
        }
    }
}

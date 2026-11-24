using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    public enum RecruitApproach { Persuade, Pay, Intimidate, Ideology }

    public sealed class RecruitChance
    {
        public float Chance;
        public bool Blocked;
        public string BlockReason;
        public long Cost;
        public readonly List<KeyValuePair<string, float>> Factors = new List<KeyValuePair<string, float>>();

        public void Add(string label, float delta)
        {
            if (Math.Abs(delta) >= 0.5f) Factors.Add(new KeyValuePair<string, float>(label, delta));
        }
    }

    public sealed class RecruitResult
    {
        public bool Success;
        public float Roll;
        public float Chance;
        public string Message;
        public Member Member;
    }

    public static class RecruitmentSystem
    {
        public static IEnumerable<RecruitApproach> ApproachesFor(FactionType type)
        {
            yield return RecruitApproach.Persuade;
            yield return RecruitApproach.Pay;
            if (type != FactionType.Militia) yield return RecruitApproach.Intimidate;
            if (type == FactionType.Militia) yield return RecruitApproach.Ideology;
        }

        public static long PayCost(FactionState f) => 1000 + f.Level * 250;

        /// <summary>
        /// Builds a stable profile for a world ped. <paramref name="key"/> should be stable for the ped
        /// (handle + model) so the same NPC keeps the same name/personality while it exists.
        /// </summary>
        public static NpcProfile CreateProfile(string key, int seed, bool female, string affiliatedRivalId, bool lawEnforcement, string defaultPersonality)
        {
            var rng = new Rng(seed);
            var p = new NpcProfile
            {
                Key = key,
                Female = female,
                FirstName = NameGenerator.FirstName(rng, female),
                LastName = NameGenerator.LastName(rng, (FactionType)rng.Next(0, 3)),
                Age = rng.Next(18, 60),
                Hometown = NameGenerator.Hometown(rng),
                Background = NameGenerator.CivilianBackground(rng),
                Willingness = rng.Range(15, 85),
                Skill = rng.Range(15, 50),
                AffiliatedRivalId = affiliatedRivalId,
                IsLawEnforcement = lawEnforcement,
            };
            var all = (Personality[])Enum.GetValues(typeof(Personality));
            if (!string.IsNullOrEmpty(defaultPersonality) && !defaultPersonality.Equals("Random", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse(defaultPersonality, true, out Personality fixedP))
                p.Personality = fixedP;
            else
                p.Personality = rng.Pick(all);
            p.SecondaryTrait = rng.Pick(all);
            if (affiliatedRivalId != null) { p.Skill += 15; p.Background = "Runs with a local crew and knows how to handle trouble."; }
            if (lawEnforcement) { p.Skill += 20; p.Background = "Sworn officer with a steady paycheck and a pension to protect."; p.Willingness = Math.Min(p.Willingness, 25); }
            return p;
        }

        public static RecruitChance ComputeChance(SimContext ctx, NpcProfile p, RecruitApproach approach)
        {
            var f = ctx.Faction;
            var rc = new RecruitChance();
            if (!ctx.Config.Gameplay.RecruitmentEnabled) { rc.Blocked = true; rc.BlockReason = "Recruitment is disabled in settings."; return rc; }
            if (!f.Recruitment.Open) { rc.Blocked = true; rc.BlockReason = "Recruitment is closed (Recruitment page)."; return rc; }
            int capacity = MemberSystem.RosterCapacity(f, ctx.Config.Gameplay.MaxRosterSize);
            if (MemberSystem.LivingCount(f) >= capacity) { rc.Blocked = true; rc.BlockReason = "Roster full (" + capacity + "). Level up or upgrade the Barracks."; return rc; }
            if (p.IsLawEnforcement && !ctx.Config.Gameplay.AllowRecruitingLawEnforcement) { rc.Blocked = true; rc.BlockReason = "Law enforcement cannot be recruited (Settings)."; return rc; }
            if (p.Female && !f.Recruitment.AllowFemaleRecruits) { rc.Blocked = true; rc.BlockReason = "Your recruitment policy excludes this candidate."; return rc; }
            if (p.Willingness < f.Recruitment.MinimumWillingness) { rc.Blocked = true; rc.BlockReason = "Below your minimum willingness standard."; return rc; }

            float chance = FactionTypeInfo.RecruitBase(f.Type);
            rc.Add("Base (" + FactionTypeInfo.TypeName(f.Type) + ")", chance);

            float rep = f.Reputation * 0.3f; chance += rep; rc.Add("Reputation", rep);
            float lvl = Math.Min(15f, f.Level * 1.5f); chance += lvl; rc.Add("Faction level", lvl);
            float will = (p.Willingness - 50) * 0.5f; chance += will; rc.Add("Willingness", will);

            float pers = PersonalityModifier(f, p.Personality);
            chance += pers; rc.Add("Personality: " + p.Personality, pers);

            float diff = ctx.Difficulty.RecruitBonus; chance += diff; rc.Add("Difficulty", diff);

            float susp = SuspicionSystem.RecruitmentModifier(f); chance += susp; rc.Add(FactionTypeInfo.SuspicionLabel(f.Type), susp);

            if (f.IsGovernment) { chance += 15; rc.Add("Joining the state", 15); }

            if (p.AffiliatedRivalId != null)
            {
                var rival = ctx.World.GetRival(p.AffiliatedRivalId);
                float mod;
                if (rival == null || !rival.Active) mod = 5; // their gang is gone
                else if (rival.Relation == RelationStatus.War) mod = -40;
                else if (rival.Relation == RelationStatus.Alliance) mod = -10;
                else mod = -28;
                chance += mod; rc.Add("Member of " + (rival?.Name ?? "another crew"), mod);
            }
            if (p.IsLawEnforcement) { chance -= 35; rc.Add("Law enforcement", -35); }

            switch (approach)
            {
                case RecruitApproach.Pay:
                    rc.Cost = PayCost(f);
                    chance += 20; rc.Add("Cash offer (" + TextUtil.Money(rc.Cost) + ")", 20);
                    if (f.Resources.Cash < rc.Cost) { rc.Blocked = true; rc.BlockReason = "Not enough cash for the offer."; }
                    break;
                case RecruitApproach.Intimidate:
                    float intim = f.Type == FactionType.StreetGang ? 15 : 12;
                    if (p.Personality == Personality.Cowardly || p.Personality == Personality.Nervous) intim += 10;
                    if (p.Personality == Personality.Aggressive || p.Personality == Personality.Rebellious) intim -= 15;
                    chance += intim; rc.Add("Intimidation", intim);
                    break;
                case RecruitApproach.Ideology:
                    float ideo = 8 + f.PublicSupport * 0.15f;
                    if (f.Personality == FactionPersonality.Zealous) ideo += 6;
                    chance += ideo; rc.Add("Ideology / public support", ideo);
                    break;
            }

            if (p.RapportBonus > 0) { chance += p.RapportBonus; rc.Add("Rapport from conversation", p.RapportBonus); }

            rc.Chance = MathEx.Clamp(chance, 3f, 95f);
            return rc;
        }

        private static float PersonalityModifier(FactionState f, Personality p)
        {
            switch (p)
            {
                case Personality.Loyal: return 5;
                case Personality.Ambitious: return f.Level >= 5 ? 8 : 2;
                case Personality.Cowardly: return f.Type == FactionType.Militia ? -15 : -10;
                case Personality.Rebellious: return f.Type == FactionType.Militia ? -12 : -6;
                case Personality.Aggressive: return f.Type == FactionType.StreetGang ? 6 : f.Type == FactionType.Militia ? -3 : 0;
                case Personality.Disciplined: return f.Type == FactionType.Militia ? 8 : f.Type == FactionType.StreetGang ? -3 : 3;
                case Personality.Intelligent: return f.Type == FactionType.Mafia ? 6 : 1;
                case Personality.Nervous: return -6;
                case Personality.Calm: return 2;
                case Personality.Sarcastic: return -2;
                default: return 0;
            }
        }

        /// <summary>Rolls a recruitment attempt. On success a new member is created (the runtime attaches the ped).</summary>
        public static RecruitResult Attempt(SimContext ctx, NpcProfile p, RecruitApproach approach, string model, PedAppearance appearance)
        {
            var f = ctx.Faction;
            var rc = ComputeChance(ctx, p, approach);
            var res = new RecruitResult { Chance = rc.Chance };
            if (rc.Blocked) { res.Message = rc.BlockReason; return res; }
            if (rc.Cost > 0)
            {
                var spend = EconomySystem.Spend(f, rc.Cost, "recruitment offer");
                if (!spend.Ok) { res.Message = spend.Message; return res; }
            }
            res.Roll = ctx.Rng.Range(0, 100);
            res.Success = res.Roll < rc.Chance;
            p.RapportBonus = 0;
            if (!res.Success)
            {
                if (approach == RecruitApproach.Intimidate) SuspicionSystem.OnPublicIncident(ctx, 1.5f);
                res.Message = p.FirstName + " turned you down.";
                p.Willingness = Math.Max(0, p.Willingness - 10);
                return res;
            }

            var m = new Member
            {
                Id = Ids.New("mem"),
                FirstName = p.FirstName,
                LastName = p.LastName,
                Female = p.Female,
                Age = p.Age,
                Hometown = p.Hometown,
                Background = p.Background,
                Model = model,
                Appearance = appearance ?? new PedAppearance(),
                Personality = p.Personality,
                SecondaryTrait = p.SecondaryTrait,
                Role = f.Recruitment.DefaultRole,
                RankId = f.LowestRank?.Id,
                Skill = MathEx.Pct(p.Skill + f.Level),
                Loyalty = MathEx.Pct(45 + p.Willingness * 0.3f),
                Morale = 65,
                Discipline = 45,
                JoinedDay = ctx.World.Day,
                Order = OrderType.Follow,
                Loadout = LoadoutType.Sidearm,
            };
            if (approach == RecruitApproach.Intimidate) { m.Loyalty = MathEx.Pct(m.Loyalty - 20); m.Morale = MathEx.Pct(m.Morale - 10); }
            if (approach == RecruitApproach.Ideology) { m.Loyalty = MathEx.Pct(m.Loyalty + 10); m.Discipline = MathEx.Pct(m.Discipline + 10); }
            if (approach == RecruitApproach.Pay) m.Loyalty = MathEx.Pct(m.Loyalty - 5);
            MemberSystem.ApplyPersonalityBaseline(m);
            if (p.AffiliatedRivalId != null)
            {
                var rival = ctx.World.GetRival(p.AffiliatedRivalId);
                if (rival != null && rival.Active)
                {
                    rival.Hostility = MathEx.Pct(rival.Hostility + 6);
                    rival.Strength = Math.Max(0, rival.Strength - 0.5f);
                }
            }
            f.Members.Add(m);
            f.Stats.MembersRecruited++;
            ctx.State.KnownNpcs.Remove(p.Key);
            ProgressionSystem.AddXp(ctx, 60);
            ctx.Report("Recruitment", m.DisplayName + " joined " + f.Name + ".", EventSeverity.Good);
            res.Member = m;
            res.Message = m.FirstName + " is in.";
            return res;
        }

        /// <summary>Hires a brand-new recruit through the faction's network (spawned by the runtime in uniform).</summary>
        public static OpResult Hire(SimContext ctx, Rng rng, out Member member)
        {
            member = null;
            var f = ctx.Faction;
            int capacity = MemberSystem.RosterCapacity(f, ctx.Config.Gameplay.MaxRosterSize);
            if (MemberSystem.LivingCount(f) >= capacity) return OpResult.Fail("Roster full (" + capacity + ").");
            long cost = f.Recruitment.HireCost + f.Level * 150;
            var spend = EconomySystem.Spend(f, cost, "hiring");
            if (!spend.Ok) return spend;
            var uniform = UniformCatalog.Get(f.Identity.UniformPreset) ?? UniformCatalog.DefaultFor(f.Type);
            member = FactionFactory.GenerateMember(f, rng, rng.Pick(uniform.Models), false, ctx.World.Day);
            member.Loyalty = MathEx.Pct(member.Loyalty - 5);
            member.Order = OrderType.Follow;
            f.Members.Add(member);
            f.Stats.MembersRecruited++;
            ProgressionSystem.AddXp(ctx, 40);
            ctx.Report("Recruitment", member.DisplayName + " was hired through your network.", EventSeverity.Good);
            return OpResult.Success("Hired " + member.DisplayName + " for " + TextUtil.Money(cost) + ".");
        }
    }
}

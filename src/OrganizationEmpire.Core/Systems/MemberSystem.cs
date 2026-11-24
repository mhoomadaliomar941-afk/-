using System;
using System.Linq;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    public static class MemberSystem
    {
        public static void ApplyPersonalityBaseline(Member m)
        {
            switch (m.Personality)
            {
                case Personality.Loyal: m.Loyalty = MathEx.Pct(m.Loyalty + 15); break;
                case Personality.Disciplined: m.Discipline = MathEx.Pct(m.Discipline + 20); break;
                case Personality.Rebellious: m.Discipline = MathEx.Pct(m.Discipline - 20); m.Loyalty = MathEx.Pct(m.Loyalty - 8); break;
                case Personality.Aggressive: m.Skill = MathEx.Pct(m.Skill + 8); m.Discipline = MathEx.Pct(m.Discipline - 8); break;
                case Personality.Cowardly: m.Skill = MathEx.Pct(m.Skill - 10); m.Morale = MathEx.Pct(m.Morale - 8); break;
                case Personality.Intelligent: m.Skill = MathEx.Pct(m.Skill + 5); break;
                case Personality.Nervous: m.Morale = MathEx.Pct(m.Morale - 5); break;
                case Personality.Calm: m.Morale = MathEx.Pct(m.Morale + 5); break;
            }
        }

        public static float CombatValue(Member m)
        {
            float loadout;
            switch (m.Loadout)
            {
                case LoadoutType.Unarmed: loadout = 0.3f; break;
                case LoadoutType.Melee: loadout = 0.5f; break;
                case LoadoutType.Sidearm: loadout = 1.0f; break;
                case LoadoutType.SMG:
                case LoadoutType.Shotgun: loadout = 1.4f; break;
                case LoadoutType.Rifle: loadout = 1.8f; break;
                case LoadoutType.Marksman: loadout = 1.7f; break;
                case LoadoutType.Heavy: loadout = 2.2f; break;
                default: loadout = 1f; break;
            }
            float morale = 0.6f + m.Morale / 250f;
            return (2f + m.Skill / 20f) * loadout * morale;
        }

        /// <summary>Hourly update of every member: recovery, morale/loyalty drift, desertion.</summary>
        public static void Tick(SimContext ctx, bool paidInFull)
        {
            var f = ctx.Faction;
            var dp = ctx.Difficulty;
            bool atWar = ctx.World.Rivals.Any(r => r.Active && r.Relation == RelationStatus.War);
            int barracks = f.Headquarters?.Level(HqUpgrade.Barracks) ?? 0;
            int infirmary = f.Headquarters?.Level(HqUpgrade.Infirmary) ?? 0;
            var lowestRank = f.LowestRank;

            foreach (var m in f.Members)
            {
                if (m.Status == MemberStatus.Dead || m.Status == MemberStatus.Deserted) continue;

                if (m.Status == MemberStatus.Wounded || m.Status == MemberStatus.Jailed)
                {
                    m.RecoveryHours -= m.Status == MemberStatus.Wounded ? 1f + infirmary * 0.5f : 1f;
                    if (m.RecoveryHours <= 0)
                    {
                        string what = m.Status == MemberStatus.Wounded ? " has recovered and is back on duty." : " was released and is back on duty.";
                        m.Status = MemberStatus.Active;
                        m.RecoveryHours = 0;
                        ctx.Report("Members", m.DisplayName + what, EventSeverity.Good);
                    }
                    continue;
                }

                // Morale target
                float moraleTarget = 55 + barracks * 4 + f.Reputation * 0.1f;
                if (!paidInFull) moraleTarget -= 25;
                if (atWar)
                {
                    if (m.Personality == Personality.Aggressive) moraleTarget += 8;
                    if (m.Personality == Personality.Cowardly || m.Personality == Personality.Nervous) moraleTarget -= 10;
                }
                if (m.Personality == Personality.Calm) moraleTarget += 5;
                float moraleRate = m.Personality == Personality.Calm ? 0.25f : 0.5f;
                m.Morale = MathEx.Pct(MathEx.MoveTowards(m.Morale, moraleTarget, moraleRate * dp.LoyaltyDrift));

                // Loyalty target
                var rank = f.GetRank(m.RankId);
                int rankCount = Math.Max(1, f.Ranks.Count - 1);
                float rankBonus = rank == null ? 0 : (rankCount - (rank.Order - 1)) * 3f;
                float loyaltyTarget = 35 + m.Morale * 0.35f + rankBonus + m.Discipline * 0.1f;
                switch (m.Personality)
                {
                    case Personality.Loyal: loyaltyTarget += 15; break;
                    case Personality.Rebellious: loyaltyTarget -= 12; break;
                    case Personality.Ambitious:
                        int daysIn = ctx.World.Day - m.JoinedDay;
                        if (rank == lowestRank && daysIn > 7) loyaltyTarget -= 15;
                        else loyaltyTarget += 4;
                        break;
                    case Personality.Disciplined: loyaltyTarget += 6; break;
                }
                if (m.IsDeputy) loyaltyTarget += 8;
                if (!paidInFull) loyaltyTarget -= 15;
                m.Loyalty = MathEx.Pct(MathEx.MoveTowards(m.Loyalty, loyaltyTarget, 0.25f * dp.LoyaltyDrift));

                // Discipline slowly tends toward faction discipline modified by personality
                float discTarget = f.Discipline + (m.Personality == Personality.Disciplined ? 20 : m.Personality == Personality.Rebellious ? -20 : 0);
                m.Discipline = MathEx.Pct(MathEx.MoveTowards(m.Discipline, discTarget, 0.1f));

                // Rapport bonuses fade
                if (m.RapportBonus > 0) m.RapportBonus = Math.Max(0, m.RapportBonus - 0.2f);

                // Desertion
                if (m.Loyalty < 15 && ctx.Rng.Chance(0.02f))
                {
                    m.Status = MemberStatus.Deserted;
                    ctx.Report("Members", m.DisplayName + " has deserted the " + FactionTypeInfo.TierName(f).ToLowerInvariant() + ".", EventSeverity.Bad);
                    if (f.Type == FactionType.Mafia || f.Type == FactionType.Militia)
                    {
                        f.Suspicion = MathEx.Pct(f.Suspicion + 6);
                        ctx.Report("Members", "Word is " + m.ShortName + " might be talking to the wrong people. " + FactionTypeInfo.SuspicionLabel(f.Type) + " increased.", EventSeverity.Warning, false);
                    }
                }
            }

            // Faction-level aggregates
            var active = f.Members.Where(m => m.Status == MemberStatus.Active).ToList();
            if (active.Count > 0)
            {
                f.Morale = active.Average(m => m.Morale);
                float rankStructure = Math.Min(10f, f.Squads.Count(s => s.LeaderId != null) * 2.5f);
                f.Discipline = MathEx.Pct(MathEx.MoveTowards(f.Discipline, active.Average(m => m.Discipline) + rankStructure, 0.2f));
            }
            SquadSystem.Cleanup(f);
        }

        public static int RosterCapacity(FactionState f, int configMax)
        {
            int barracks = f.Headquarters?.Level(HqUpgrade.Barracks) ?? 0;
            int cap = 8 + f.Level * 3 + barracks * 6 + (f.IsGovernment ? 20 : 0);
            return Math.Min(cap, configMax);
        }

        public static int LivingCount(FactionState f) => f.Members.Count(m => m.Status != MemberStatus.Dead && m.Status != MemberStatus.Deserted);

        public static OpResult SetLoadout(FactionState f, Member m, LoadoutType type)
        {
            if (m == null) return OpResult.Fail("No member selected.");
            if (f.Level < LoadoutCatalog.MinLevel(type)) return OpResult.Fail(TextUtil.Humanize(type.ToString()) + " loadouts unlock at level " + LoadoutCatalog.MinLevel(type) + ".");
            var def = LoadoutCatalog.Get(type, f.Type);
            var oldDef = LoadoutCatalog.Get(m.Loadout, f.Type);
            int armory = f.Headquarters?.Level(HqUpgrade.Armory) ?? 0;
            int cost = Math.Max(0, def.ArmsCost - oldDef.ArmsCost - armory);
            if (cost > f.Resources.Arms) return OpResult.Fail("Not enough Arms (" + cost + " needed, " + f.Resources.Arms + " in stock).");
            f.Resources.Arms -= cost;
            m.Loadout = type;
            return OpResult.Success(m.ShortName + " equipped with " + TextUtil.Humanize(type.ToString()) + (cost > 0 ? " (-" + cost + " Arms)." : "."));
        }

        public static void OnKilled(SimContext ctx, Member m, bool permanentDeath)
        {
            var f = ctx.Faction;
            if (m == null || m.Status == MemberStatus.Dead) return;
            int infirmary = f.Headquarters?.Level(HqUpgrade.Infirmary) ?? 0;
            float surviveChance = permanentDeath ? 0.15f + infirmary * 0.12f : 1f;
            if (ctx.Rng.Chance(surviveChance))
            {
                m.Status = MemberStatus.Wounded;
                m.RecoveryHours = 24 + ctx.Rng.Next(0, 24);
                m.Morale = MathEx.Pct(m.Morale - 10);
                ctx.Report("Members", m.DisplayName + " was badly wounded and will be out for a while.", EventSeverity.Warning);
            }
            else
            {
                m.Status = MemberStatus.Dead;
                f.Stats.MembersLost++;
                ctx.Report("Members", m.DisplayName + " was killed in action.", EventSeverity.Bad);
                foreach (var other in f.Members.Where(o => o.Status == MemberStatus.Active))
                {
                    other.Morale = MathEx.Pct(other.Morale - 3);
                    if (other.Opinions.TryGetValue(m.Id, out float op) && op > 30) other.Morale = MathEx.Pct(other.Morale - 6);
                }
                var squad = f.GetSquad(m.SquadId);
                if (squad != null) SquadSystem.RemoveMember(f, squad, m);
                foreach (var v in f.Vehicles.Where(v => v.DriverMemberId == m.Id)) v.DriverMemberId = null;
                if (f.Headquarters != null) f.Headquarters.GuardMemberIds.Remove(m.Id);
                if (f.Government != null)
                {
                    foreach (var min in f.Government.Ministries.Where(x => x.OfficialMemberId == m.Id)) min.OfficialMemberId = null;
                }
            }
        }

        public static OpResult Expel(SimContext ctx, Member m)
        {
            var f = ctx.Faction;
            if (m == null) return OpResult.Fail("No member selected.");
            var squad = f.GetSquad(m.SquadId);
            if (squad != null) SquadSystem.RemoveMember(f, squad, m);
            f.Members.Remove(m);
            foreach (var v in f.Vehicles.Where(v => v.DriverMemberId == m.Id)) v.DriverMemberId = null;
            if (f.Headquarters != null) f.Headquarters.GuardMemberIds.Remove(m.Id);
            if (f.Government != null)
            {
                foreach (var min in f.Government.Ministries.Where(x => x.OfficialMemberId == m.Id)) min.OfficialMemberId = null;
            }
            foreach (var other in f.Members) other.Opinions.Remove(m.Id);
            ctx.State.Memories.Remove(m.Id);
            ctx.Report("Members", m.DisplayName + " was expelled.", EventSeverity.Warning);
            if (m.Loyalty < 30 && (f.Type != FactionType.StreetGang)) f.Suspicion = MathEx.Pct(f.Suspicion + 4);
            return OpResult.Success(m.DisplayName + " expelled.");
        }

        public static OpResult SetDeputy(FactionState f, Member m, bool deputy)
        {
            if (m == null) return OpResult.Fail("No member selected.");
            if (deputy && f.Members.Count(x => x.IsDeputy) >= 3) return OpResult.Fail("You can appoint at most 3 deputies.");
            m.IsDeputy = deputy;
            if (deputy) { m.Loyalty = MathEx.Pct(m.Loyalty + 10); m.Morale = MathEx.Pct(m.Morale + 8); }
            return OpResult.Success(m.ShortName + (deputy ? " appointed as a deputy." : " is no longer a deputy."));
        }

        public static void AdjustOpinion(Member a, Member b, float delta)
        {
            if (a == null || b == null || a == b) return;
            a.Opinions.TryGetValue(b.Id, out float v);
            a.Opinions[b.Id] = MathEx.Clamp(v + delta, -100, 100);
        }

        public static void GrantExperience(Member m, float skillGain)
        {
            m.Skill = MathEx.Pct(m.Skill + skillGain);
        }
    }
}

using System;
using System.Linq;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    /// <summary>
    /// Abstract secrecy/detection game mechanic. For the militia this is "Suspicion" (central to its gameplay),
    /// for other factions it is "Heat". High values trigger in-game consequences (investigations, raids);
    /// low values unlock opportunities. It is a pure game-balance mechanic.
    /// </summary>
    public static class SuspicionSystem
    {
        public const float WatchThreshold = 40;
        public const float InvestigationThreshold = 60;
        public const float RaidThreshold = 80;

        public static string Level(FactionState f)
        {
            float s = f.Suspicion;
            if (s >= RaidThreshold) return "Imminent raid";
            if (s >= InvestigationThreshold) return "Under investigation";
            if (s >= WatchThreshold) return "Being watched";
            if (s >= 20) return "Noticed";
            return "Low profile";
        }

        public static float RecruitmentModifier(FactionState f)
        {
            if (f.Type == FactionType.Militia && f.Suspicion < 20) return 5;
            if (f.Suspicion > WatchThreshold) return -(f.Suspicion - WatchThreshold) * (f.Type == FactionType.Militia ? 0.4f : 0.2f);
            return 0;
        }

        public static bool LowProfile(FactionState f) => f.Suspicion < 25;

        public static void Tick(SimContext ctx, int playerWantedLevel)
        {
            var f = ctx.Faction;
            var w = ctx.World;
            if (!ctx.Config.Gameplay.SuspicionEnabled)
            {
                f.Suspicion = Math.Max(0, f.Suspicion - 1);
                return;
            }
            var dp = ctx.Difficulty;
            float decay = 0.18f;
            if (f.Behavior == FactionBehavior.Covert) decay += 0.12f;
            if (f.Personality == FactionPersonality.Secretive) decay += 0.06f;
            if (f.Personality == FactionPersonality.Flamboyant) decay -= 0.06f;
            float gain = 0;
            if (playerWantedLevel > 0) gain += 0.35f * playerWantedLevel;
            if (f.Type == FactionType.Militia)
            {
                int size = f.LivingMemberCount;
                if (size > 15) gain += (size - 15) * 0.01f;
                if (f.Discipline < 40) gain += 0.05f;
            }
            if (f.IsGovernment) { f.Suspicion = 0; return; }
            f.Suspicion = MathEx.Pct(f.Suspicion + gain * dp.Suspicion - decay);

            // Consequences
            float raidThreshold = f.Type == FactionType.Militia ? RaidThreshold : RaidThreshold + 5;
            bool raidPending = w.PendingAttacks.Any(a => a.IsRaid);
            if (f.Suspicion >= raidThreshold && !raidPending && f.Headquarters != null && ctx.Rng.Chance(0.05f))
            {
                var hqTerritory = w.FindByZone(f.Headquarters.ZoneCode) ?? TerritorySystem.OwnedBy(w, FactionIds.Player).FirstOrDefault();
                if (hqTerritory != null)
                {
                    WarSystem.CreateAttack(ctx, hqTerritory, FactionIds.State, 25 + w.State.ForceStrength * 0.5f, true);
                }
            }
            else if (f.Suspicion >= InvestigationThreshold && ctx.Rng.Chance(0.02f))
            {
                long fine = Math.Min(f.Resources.Cash, 2000 + (long)(f.Suspicion * 60));
                f.Resources.Cash -= fine;
                f.Resources.Intel = Math.Max(0, f.Resources.Intel - 3);
                ctx.Report("Suspicion", "Investigators are digging into your operation. Legal costs and lost contacts: " + TextUtil.Money(fine) + ".", EventSeverity.Warning);
            }
            else if (f.Suspicion >= WatchThreshold && ctx.Rng.Chance(0.01f))
            {
                var informant = f.ActiveMembers.Where(m => m.Loyalty < 35).OrderBy(m => m.Loyalty).FirstOrDefault();
                if (informant != null)
                {
                    ctx.Report("Suspicion", "Rumors say " + informant.ShortName + " has been seen talking to detectives.", EventSeverity.Warning);
                    informant.Opinions.Clear();
                }
            }
        }

        /// <summary>Reported by the runtime: violent incidents in public raise heat.</summary>
        public static void OnPublicIncident(SimContext ctx, float amount)
        {
            if (!ctx.Config.Gameplay.SuspicionEnabled || ctx.Faction.IsGovernment) return;
            ctx.Faction.Suspicion = MathEx.Pct(ctx.Faction.Suspicion + amount * ctx.Difficulty.Suspicion);
        }
    }
}

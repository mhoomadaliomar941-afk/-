using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    public sealed class TakeoverRequirement
    {
        public string Text;
        public Func<SimContext, float> Progress; // 0..1
    }

    public sealed class TakeoverStage
    {
        public string Name;
        public string Description;
        public List<TakeoverRequirement> Requirements = new List<TakeoverRequirement>();
    }

    public sealed class GovernmentSetup
    {
        public string StateName;
        public string GovernmentName;
        public string RulerTitle;
        public GovernmentType Type;
        public bool KeepFactionFlag = true;
        public FlagDesign NewFlag;
        public string PrimaryColor;
        public string SecondaryColor;
    }

    /// <summary>
    /// Abstract, fictional campaign progression toward controlling the game's state of San Andreas.
    /// The militia path is fastest and deepest, mafia works through influence, the street gang path is slow and hard.
    /// </summary>
    public static class TakeoverSystem
    {
        private static TakeoverRequirement Req(string text, Func<SimContext, float> progress) => new TakeoverRequirement { Text = text, Progress = progress };

        private static float Ratio(float value, float target) => target <= 0 ? 1f : MathEx.Clamp01(value / target);
        private static float AtMost(float value, float target) => value <= target ? 1f : MathEx.Clamp01(1f - (value - target) / Math.Max(1f, 100f - target));

        private static int Owned(SimContext c) => TerritorySystem.PlayerTerritoryCount(c.World);
        private static int OwnedStrategic(SimContext c) => c.World.Territories.Count(t => t.OwnerId == FactionIds.Player && t.Type == TerritoryType.Strategic);
        private static int Living(SimContext c) => MemberSystem.LivingCount(c.Faction);
        private static bool Milestone(SimContext c, string id) => c.Faction.Takeover.CompletedMilestones.Contains(id);

        public static List<TakeoverStage> StagesFor(FactionType type)
        {
            switch (type)
            {
                case FactionType.Militia:
                    return new List<TakeoverStage>
                    {
                        new TakeoverStage { Name = "Establish the Movement", Description = "Build a disciplined core with a base of operations and a foothold in the county.",
                            Requirements = { Req("8 members", c => Ratio(Living(c), 8)), Req("Faction level 3", c => Ratio(c.Faction.Level, 3)),
                                             Req("Control 2 territories", c => Ratio(Owned(c), 2)), Req("Establish a headquarters", c => c.Faction.Headquarters != null ? 1 : 0) } },
                        new TakeoverStage { Name = "Win Hearts and Minds", Description = "Turn the population in your favor while staying under the radar.",
                            Requirements = { Req("Public support 45%", c => Ratio(c.Faction.PublicSupport, 45)), Req("Suspicion at most 60", c => AtMost(c.Faction.Suspicion, 60)),
                                             Req("Complete 'Hearts and Minds'", c => Milestone(c, "mil_hearts") ? 1 : 0), Req("30 Influence", c => Ratio(c.Faction.Resources.Influence, 30)) } },
                        new TakeoverStage { Name = "Seize Strategic Assets", Description = "Take key infrastructure from the state and weaken its grip.",
                            Requirements = { Req("Control 2 strategic sites", c => Ratio(OwnedStrategic(c), 2)), Req("State stability at most 60", c => AtMost(c.World.State.Stability, 60)),
                                             Req("18 members", c => Ratio(Living(c), 18)), Req("60 Arms stockpiled", c => Ratio(c.Faction.Resources.Arms, 60)) } },
                        new TakeoverStage { Name = "The Final Campaign", Description = "The state is on its knees. March on the capitol.",
                            Requirements = { Req("State stability at most 40", c => AtMost(c.World.State.Stability, 40)), Req("Morale 50+", c => Ratio(c.Faction.Morale, 50)) } },
                    };
                case FactionType.Mafia:
                    return new List<TakeoverStage>
                    {
                        new TakeoverStage { Name = "Made in Los Santos", Description = "Establish legitimate fronts and a name that opens doors.",
                            Requirements = { Req("Own 2 businesses", c => Ratio(c.Faction.Businesses.Count, 2)), Req("Faction level 4", c => Ratio(c.Faction.Level, 4)),
                                             Req("Reputation 35", c => Ratio(c.Faction.Reputation, 35)) } },
                        new TakeoverStage { Name = "Friends in High Places", Description = "Cultivate officials and accumulate leverage.",
                            Requirements = { Req("80 Influence", c => Ratio(c.Faction.Resources.Influence, 80)), Req("Complete 'The Councilman's Favor'", c => Milestone(c, "maf_councilman") ? 1 : 0),
                                             Req("Heat at most 50", c => AtMost(c.Faction.Suspicion, 50)) } },
                        new TakeoverStage { Name = "The Commission", Description = "Bring the other crime families to heel: eliminate them or bind them by treaty.",
                            Requirements = { Req("2 crime families eliminated or at peace", c => Ratio(c.World.Rivals.Count(r => (r.Kind == RivalKind.Mafia || r.Kind == RivalKind.Cartel) && (r.Eliminated || !r.Enabled || r.Relation == RelationStatus.Peace || r.Relation == RelationStatus.Alliance)), 2)),
                                             Req("Own 4 businesses", c => Ratio(c.Faction.Businesses.Count, 4)), Req("Control 6 territories", c => Ratio(Owned(c), 6)),
                                             Req("State stability at most 65", c => AtMost(c.World.State.Stability, 65)) } },
                        new TakeoverStage { Name = "Hostile Takeover", Description = "Only the state's loyal enforcers remain. Remove them.",
                            Requirements = { Req("State stability at most 45", c => AtMost(c.World.State.Stability, 45)), Req("150 Influence", c => Ratio(c.Faction.Resources.Influence, 150)) } },
                    };
                default:
                    return new List<TakeoverStage>
                    {
                        new TakeoverStage { Name = "Run the Streets", Description = "Become the set everybody knows.",
                            Requirements = { Req("Control 4 territories", c => Ratio(Owned(c), 4)), Req("12 members", c => Ratio(Living(c), 12)), Req("Reputation 40", c => Ratio(c.Faction.Reputation, 40)) } },
                        new TakeoverStage { Name = "Kings of the City", Description = "Dominate Los Santos and break rival sets.",
                            Requirements = { Req("Control 40% of city neighborhoods", c => Ratio(CityShare(c), 0.4f)), Req("Eliminate 2 rival factions", c => Ratio(c.Faction.Stats.RivalsEliminated, 2)),
                                             Req("Faction level 8", c => Ratio(c.Faction.Level, 8)) } },
                        new TakeoverStage { Name = "The People's Champions", Description = "Turn the city's frustration into loyalty.",
                            Requirements = { Req("Public support 50%", c => Ratio(c.Faction.PublicSupport, 50)), Req("Reputation 75", c => Ratio(c.Faction.Reputation, 75)) } },
                        new TakeoverStage { Name = "Break the System", Description = "Show the state it can't hold the city.",
                            Requirements = { Req("Control a strategic site", c => Ratio(OwnedStrategic(c), 1)), Req("State stability at most 50", c => AtMost(c.World.State.Stability, 50)),
                                             Req("25 members", c => Ratio(Living(c), 25)) } },
                        new TakeoverStage { Name = "Uprising", Description = "The final confrontation.",
                            Requirements = { Req("State stability at most 35", c => AtMost(c.World.State.Stability, 35)), Req("Morale 55+", c => Ratio(c.Faction.Morale, 55)) } },
                    };
            }
        }

        private static float CityShare(SimContext c)
        {
            var city = c.World.Territories.Where(t => t.Type == TerritoryType.Residential || t.Type == TerritoryType.Commercial || t.Type == TerritoryType.Industrial).ToList();
            if (city.Count == 0) return 0;
            return city.Count(t => t.OwnerId == FactionIds.Player) / (float)city.Count;
        }

        public static string FinalMissionId(FactionType type)
        {
            switch (type)
            {
                case FactionType.Militia: return "final_militia";
                case FactionType.Mafia: return "final_mafia";
                default: return "final_gang";
            }
        }

        public static float StageProgress(SimContext ctx, TakeoverStage stage)
        {
            if (stage.Requirements.Count == 0) return 1f;
            return stage.Requirements.Average(r => r.Progress(ctx));
        }

        public static float OverallProgress(SimContext ctx)
        {
            var f = ctx.Faction;
            if (f.Takeover.Completed) return 1f;
            var stages = StagesFor(f.Type);
            float done = f.Takeover.Stage;
            if (f.Takeover.Stage < stages.Count) done += StageProgress(ctx, stages[f.Takeover.Stage]) * 0.95f;
            return MathEx.Clamp01(done / stages.Count);
        }

        public static bool CampaignMissionAllowed(SimContext ctx, MissionDefinition d)
        {
            // Strategic strike missions appear once the faction is on the campaign path.
            return ctx.Faction.Takeover.Stage >= 1;
        }

        /// <summary>Hourly: state stability dynamics and stage advancement.</summary>
        public static void Tick(SimContext ctx)
        {
            var f = ctx.Faction;
            var w = ctx.World;
            var st = w.State;
            if (f.Takeover.Completed || st.Overthrown) return;

            float speed = FactionTypeInfo.TakeoverSpeed(f.Type);
            int strategic = OwnedStrategic(ctx);
            float pressure = strategic * 0.05f
                             + Math.Max(0, f.PublicSupport - 50) * 0.002f
                             + w.Territories.Where(t => t.OwnerId == FactionIds.Player).Sum(t => t.Unrest) * 0.0005f;
            if (f.Type == FactionType.Mafia) pressure += f.Resources.Influence / 50f * 0.02f;
            st.Stability = MathEx.Pct(st.Stability - pressure * speed + 0.03f);
            st.Authority = MathEx.Pct(MathEx.MoveTowards(st.Authority, st.Stability, 0.05f));
            st.ForceStrength = MathEx.Clamp(st.ForceStrength + 0.02f, 10, 100);

            var stages = StagesFor(f.Type);
            int guard = 0;
            while (f.Takeover.Stage < stages.Count && guard++ < 10)
            {
                var stage = stages[f.Takeover.Stage];
                bool complete = stage.Requirements.All(r => r.Progress(ctx) >= 0.999f);
                if (!complete) break;
                if (f.Takeover.Stage == stages.Count - 1)
                {
                    if (!f.Takeover.FinalMissionUnlocked)
                    {
                        f.Takeover.FinalMissionUnlocked = true;
                        ctx.Report("Campaign", "The decisive moment has come. The final campaign mission is available in Missions.", EventSeverity.Critical);
                        MissionBoard.Refresh(ctx, true);
                    }
                    break;
                }
                f.Takeover.Stage++;
                f.Takeover.Momentum += 10;
                ProgressionSystem.AddXp(ctx, 1000);
                ctx.Report("Campaign", "Campaign stage complete: " + stage.Name + ". Next: " + stages[f.Takeover.Stage].Name + ".", EventSeverity.Good);
            }
        }

        public static void OnFinalMissionSuccess(SimContext ctx)
        {
            var f = ctx.Faction;
            f.Takeover.Stage = StagesFor(f.Type).Count;
            f.Takeover.Completed = true;
            f.Takeover.CompletedDay = ctx.World.Day;
            ctx.World.State.Stability = 0;
            ctx.World.State.Overthrown = true;
            ctx.Report("Campaign", "The old government has fallen. " + f.Name + " controls San Andreas. Open the Government page to form your government.", EventSeverity.Critical);
        }

        public static bool AwaitingGovernmentFormation(FactionState f) => f.Takeover.Completed && !f.IsGovernment;

        /// <summary>Transforms the faction into the government of the fictional state.</summary>
        public static OpResult EstablishGovernment(SimContext ctx, GovernmentSetup setup)
        {
            var f = ctx.Faction;
            var w = ctx.World;
            if (!f.Takeover.Completed) return OpResult.Fail("The takeover campaign is not complete.");
            if (f.IsGovernment) return OpResult.Fail("A government is already established.");

            var g = new GovernmentState
            {
                Established = true,
                EstablishedDay = w.Day,
                StateName = string.IsNullOrWhiteSpace(setup.StateName) ? "Free State of San Andreas" : setup.StateName.Trim(),
                GovernmentName = string.IsNullOrWhiteSpace(setup.GovernmentName) ? "Provisional Council" : setup.GovernmentName.Trim(),
                RulerTitle = string.IsNullOrWhiteSpace(setup.RulerTitle) ? GovernmentCatalog.RulerTitleFor(f.Type) : setup.RulerTitle.Trim(),
                Type = setup.Type,
                KeptFactionFlag = setup.KeepFactionFlag,
                FormerFactionName = f.Name,
                FormerRanks = JsonMapperClone(f.Ranks),
                Ministries = GovernmentCatalog.DefaultMinistries(),
                Stability = 45 + (f.Type == FactionType.Militia ? 10 : f.Type == FactionType.Mafia ? 5 : 0),
                Legitimacy = f.Type == FactionType.Mafia ? 25 : f.Type == FactionType.Militia ? 30 : 35,
                PublicSupport = MathEx.Pct(f.PublicSupport),
                ForcesReadiness = 50,
                Treasury = 50000 + f.Resources.Cash / 4,
            };
            g.StateIdentity = f.Identity.Copy();
            if (!setup.KeepFactionFlag && setup.NewFlag != null) g.StateIdentity.Flag = setup.NewFlag.Copy();
            if (!string.IsNullOrEmpty(setup.PrimaryColor)) g.StateIdentity.PrimaryColor = setup.PrimaryColor;
            if (!string.IsNullOrEmpty(setup.SecondaryColor)) g.StateIdentity.SecondaryColor = setup.SecondaryColor;
            f.Government = g;

            // Members become state forces with government ranks.
            RankSystem.ApplyTemplate(f, RankTemplates.Government());
            foreach (var m in f.ActiveMembers)
            {
                m.Loyalty = MathEx.Pct(m.Loyalty + 10);
                m.Morale = MathEx.Pct(m.Morale + 15);
            }
            f.Identity.UniformPreset = f.Identity.UniformPreset; // kept; players may switch to "State Forces" in Identity.

            // The state's strategic holdings pass to the new government.
            foreach (var t in w.Territories.Where(t => t.OwnerId == FactionIds.State || t.InfluenceOf(FactionIds.State) > 0).ToList())
            {
                float stateInf = t.InfluenceOf(FactionIds.State);
                t.Influence.Remove(FactionIds.State);
                TerritorySystem.AddInfluence(t, FactionIds.Player, Math.Max(stateInf, 60));
                if (TerritorySystem.UpdateOwnership(t, out string prev)) TerritorySystem.OnOwnershipChanged(ctx, t, prev);
            }
            w.State.Name = g.GovernmentName;
            w.PendingAttacks.RemoveAll(a => a.IsRaid);
            f.Suspicion = 0;
            ctx.Report("State", "The " + g.GovernmentName + " of the " + g.StateName + " is established. " + g.RulerTitle + " - the state is yours.", EventSeverity.Critical);
            ProgressionSystem.AddXp(ctx, 3000);
            return OpResult.Success("Government established.");
        }

        private static List<Rank> JsonMapperClone(List<Rank> ranks) => Json.JsonMapper.Clone(ranks);
    }
}

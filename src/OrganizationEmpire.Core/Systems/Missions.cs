using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.Core.Systems
{
    public enum TargetRule { None, RivalTerritory, OwnTerritory, NeutralOrRival, Headquarters, StateTerritory, AnyRival }

    public sealed class MissionDefinition
    {
        public string Id;
        public string Title;
        public string Summary;
        public string Briefing;
        public MissionTemplate Template;
        public MissionCategory Category;
        public FactionType[] Types;           // null = all
        public int MinLevel = 1;
        public Func<SimContext, bool> Requirement;
        public string RequirementText;
        public TargetRule Target;
        public bool Repeatable = true;
        public bool Milestone;
        public bool GovernmentAllowed = true;
        public bool PreTakeoverOnly;

        // Rewards (scaled by difficulty 1..5)
        public long Cash;
        public int Xp = 200;
        public float Reputation;
        public int Influence, Supplies, Product, Arms, Intel;
        public float PublicSupport;
        public float SuspicionDelta;
        public float TerritoryInfluence;
        public float RivalDamage;
        public float StateStabilityDelta;

        // Runtime parameters
        public int Hostiles = 6;
        public int Waves = 1;
        public int TimeLimitSeconds;
        public int Stops = 3;

        public bool AvailableFor(FactionType t) => Types == null || Types.Contains(t);
    }

    public sealed class MissionOutcome
    {
        public bool Success;
        public int Kills;
        public int MembersLost;
        public List<string> ParticipantIds = new List<string>();
        public string FailureReason;
    }

    public static class MissionCatalog
    {
        private static readonly FactionType[] Gang = { FactionType.StreetGang };
        private static readonly FactionType[] Mafia = { FactionType.Mafia };
        private static readonly FactionType[] Militia = { FactionType.Militia };

        public static readonly List<MissionDefinition> All = new List<MissionDefinition>
        {
            // ======================= Common
            new MissionDefinition { Id = "recruit_drive", Title = "Recruitment Drive", Template = MissionTemplate.Recruit, Category = MissionCategory.Recruitment,
                Summary = "Bring new blood into the faction.", Briefing = "Recruit 2 people from the streets (aim at someone and press Interact) before time runs out.",
                Stops = 2, TimeLimitSeconds = 600, Cash = 2000, Xp = 250, Reputation = 2, Target = TargetRule.None },
            new MissionDefinition { Id = "defend_hq", Title = "Defend the Headquarters", Template = MissionTemplate.Defend, Category = MissionCategory.Defense,
                Summary = "Hostiles are coming for your base.", Briefing = "Get to the headquarters and hold it against incoming attackers.",
                Target = TargetRule.Headquarters, Waves = 3, Hostiles = 5, Cash = 6000, Xp = 500, Reputation = 4, Requirement = c => c.Faction.Headquarters != null, RequirementText = "Requires a headquarters" },

            // ======================= Street gang
            new MissionDefinition { Id = "gang_corner", Title = "Corner Takeover", Types = Gang, Template = MissionTemplate.Assault, Category = MissionCategory.Territory,
                Summary = "Push a rival set off their corner.", Briefing = "Roll up on the rival corner, take out their crew and hold the block until it's yours.",
                Target = TargetRule.RivalTerritory, Hostiles = 6, Cash = 4000, Xp = 400, Reputation = 4, TerritoryInfluence = 30, RivalDamage = 5, SuspicionDelta = 4 },
            new MissionDefinition { Id = "gang_hold_block", Title = "Hold the Block", Types = Gang, Template = MissionTemplate.Defend, Category = MissionCategory.Defense,
                Summary = "Rivals are rolling up on your turf.", Briefing = "Get to the block and hold it through the attack waves.",
                Target = TargetRule.OwnTerritory, Waves = 3, Hostiles = 4, Cash = 3500, Xp = 350, Reputation = 3, TerritoryInfluence = 15, RivalDamage = 4 },
            new MissionDefinition { Id = "gang_stash", Title = "Stash Run", Types = Gang, Template = MissionTemplate.Delivery, Category = MissionCategory.Resource,
                Summary = "Move a package from the stash house.", Briefing = "Pick up the van at the stash and drive it back to your HQ (or a safe drop). Don't lose the vehicle.",
                Requirement = c => (c.Faction.Activities & GangActivities.Narcotics) != 0, RequirementText = "Requires the Narcotics activity",
                Cash = 2500, Xp = 300, Product = 12, SuspicionDelta = 3, Target = TargetRule.None },
            new MissionDefinition { Id = "gang_arms", Title = "Arms Pickup", Types = Gang, Template = MissionTemplate.Delivery, Category = MissionCategory.Resource,
                Summary = "A connect has hardware waiting.", Briefing = "Collect the arms shipment vehicle and bring it home. Expect company.",
                Requirement = c => (c.Faction.Activities & GangActivities.Arms) != 0, RequirementText = "Requires the Arms activity",
                Cash = 2000, Xp = 300, Arms = 15, SuspicionDelta = 3, Target = TargetRule.None, Hostiles = 3 },
            new MissionDefinition { Id = "gang_supply", Title = "Supply Run", Types = Gang, Template = MissionTemplate.Delivery, Category = MissionCategory.Resource,
                Summary = "Restock the crew.", Briefing = "Grab the supply van and drive it back.", Cash = 1500, Xp = 200, Supplies = 15, Target = TargetRule.None },
            new MissionDefinition { Id = "gang_payback", Title = "Payback", Types = Gang, Template = MissionTemplate.Assassination, Category = MissionCategory.Conflict, MinLevel = 2,
                Summary = "A rival shot-caller crossed the line.", Briefing = "Find the rival shot-caller and take him out. His crew will protect him.",
                Target = TargetRule.AnyRival, Hostiles = 4, Cash = 5000, Xp = 450, Reputation = 5, RivalDamage = 8, SuspicionDelta = 5 },
            new MissionDefinition { Id = "gang_rounds", Title = "Protection Rounds", Types = Gang, Template = MissionTemplate.Collect, Category = MissionCategory.Resource,
                Summary = "Collect what local shops owe the crew.", Briefing = "Visit each marked shop and collect. Some owners may get brave.",
                Stops = 3, Cash = 4500, Xp = 250, Reputation = 1, SuspicionDelta = 2, Target = TargetRule.None },
            new MissionDefinition { Id = "gang_block_party", Title = "Give Back to the Block", Types = Gang, Template = MissionTemplate.Collect, Category = MissionCategory.Interaction, MinLevel = 3,
                Summary = "Win the neighborhood over.", Briefing = "Drop off food and supplies at each marked spot. The people remember who looked out for them.",
                Stops = 3, Cash = 0, Xp = 300, Reputation = 3, PublicSupport = 6, SuspicionDelta = -4, Supplies = -5, Target = TargetRule.None,
                Requirement = c => c.Faction.Resources.Supplies >= 5, RequirementText = "Requires 5 supplies" },

            // ======================= Mafia
            new MissionDefinition { Id = "maf_racket", Title = "Protection Racket", Types = Mafia, Template = MissionTemplate.Collect, Category = MissionCategory.Resource,
                Summary = "The family's businesses pay for protection.", Briefing = "Visit each business and collect the family's envelope.",
                Stops = 3, Cash = 7000, Xp = 300, Influence = 3, SuspicionDelta = 2, Target = TargetRule.None },
            new MissionDefinition { Id = "maf_sitdown", Title = "The Sit-Down", Types = Mafia, Template = MissionTemplate.Meeting, Category = MissionCategory.Interaction, MinLevel = 2,
                Summary = "A rival boss wants to talk.", Briefing = "Meet the rival boss at the marked location. Choose how the conversation goes.",
                Target = TargetRule.AnyRival, Cash = 0, Xp = 400, Influence = 8, Reputation = 3, Hostiles = 3 },
            new MissionDefinition { Id = "maf_example", Title = "Make an Example", Types = Mafia, Template = MissionTemplate.Assassination, Category = MissionCategory.Conflict, MinLevel = 2,
                Summary = "Someone disrespected the family.", Briefing = "Find the target and deal with him. Leave no doubt about who did it.",
                Target = TargetRule.AnyRival, Hostiles = 4, Cash = 6000, Xp = 500, Reputation = 5, RivalDamage = 8, Influence = 4, SuspicionDelta = 6 },
            new MissionDefinition { Id = "maf_escort", Title = "Escort the Shipment", Types = Mafia, Template = MissionTemplate.Escort, Category = MissionCategory.Escort,
                Summary = "Valuable cargo must reach the family.", Briefing = "Escort the family's car to its destination. Protect it from ambushes.",
                Cash = 8000, Xp = 450, Influence = 3, Hostiles = 4, Target = TargetRule.None },
            new MissionDefinition { Id = "maf_acquisition", Title = "Hostile Acquisition", Types = Mafia, Template = MissionTemplate.Assault, Category = MissionCategory.Territory, MinLevel = 3,
                Summary = "A rival's business district is ripe for the taking.", Briefing = "Clear out the rival's people and claim their streets for the family.",
                Target = TargetRule.RivalTerritory, Hostiles = 6, Cash = 7000, Xp = 500, Reputation = 4, TerritoryInfluence = 30, RivalDamage = 6, SuspicionDelta = 5 },
            new MissionDefinition { Id = "maf_councilman", Title = "The Councilman's Favor", Types = Mafia, Template = MissionTemplate.Meeting, Category = MissionCategory.Campaign, MinLevel = 4,
                Summary = "A councilman needs a problem to go away.", Briefing = "Meet the councilman's people and make the arrangement. Friends in high places matter.",
                Repeatable = false, Milestone = true, PreTakeoverOnly = true, Influence = 25, Xp = 800, StateStabilityDelta = -8, Hostiles = 2 },

            // ======================= Militia
            new MissionDefinition { Id = "mil_recon", Title = "Recon Patrol", Types = Militia, Template = MissionTemplate.Recon, Category = MissionCategory.Resource,
                Summary = "Map enemy positions.", Briefing = "Visit each observation point. Stay off the police radar: a wanted level fails the mission.",
                Stops = 3, Cash = 1500, Xp = 300, Intel = 8, SuspicionDelta = -3, Target = TargetRule.None, TimeLimitSeconds = 900 },
            new MissionDefinition { Id = "mil_cache", Title = "Supply Cache", Types = Militia, Template = MissionTemplate.Delivery, Category = MissionCategory.Resource,
                Summary = "Sympathizers left supplies for the unit.", Briefing = "Retrieve the supply truck and bring it to HQ.",
                Cash = 1000, Xp = 300, Supplies = 20, Arms = 6, Target = TargetRule.None, Hostiles = 2 },
            new MissionDefinition { Id = "mil_strike", Title = "Strike on Outpost", Types = Militia, Template = MissionTemplate.Assault, Category = MissionCategory.Conflict, MinLevel = 2,
                Summary = "An enemy outpost threatens the unit.", Briefing = "Assault the enemy position, neutralize all hostiles and secure the area.",
                Target = TargetRule.RivalTerritory, Hostiles = 7, Cash = 3500, Xp = 500, Reputation = 4, TerritoryInfluence = 30, RivalDamage = 6, SuspicionDelta = 6 },
            new MissionDefinition { Id = "mil_hold", Title = "Hold the Line", Types = Militia, Template = MissionTemplate.Defend, Category = MissionCategory.Defense,
                Summary = "Enemy forces are probing your area.", Briefing = "Dig in and hold the position through every wave.",
                Target = TargetRule.OwnTerritory, Waves = 3, Hostiles = 5, Cash = 3000, Xp = 450, Reputation = 3, TerritoryInfluence = 15, RivalDamage = 4 },
            new MissionDefinition { Id = "mil_convoy", Title = "Convoy Escort", Types = Militia, Template = MissionTemplate.Escort, Category = MissionCategory.Escort,
                Summary = "A supply convoy needs protection.", Briefing = "Escort the convoy vehicle to its destination.",
                Cash = 2500, Xp = 400, Supplies = 15, Hostiles = 4, Target = TargetRule.None },
            new MissionDefinition { Id = "mil_hearts", Title = "Hearts and Minds", Types = Militia, Template = MissionTemplate.Collect, Category = MissionCategory.Campaign,
                Summary = "Deliver aid and win the people over.", Briefing = "Deliver relief supplies to each marked community point. Public support is the movement's lifeblood.",
                Stops = 3, Xp = 500, PublicSupport = 10, SuspicionDelta = -2, Supplies = -10, Target = TargetRule.None, Milestone = true, PreTakeoverOnly = true,
                Requirement = c => c.Faction.Resources.Supplies >= 10, RequirementText = "Requires 10 supplies" },
            new MissionDefinition { Id = "mil_sabotage", Title = "Seize the Relay", Types = Militia, Template = MissionTemplate.Assault, Category = MissionCategory.Campaign, MinLevel = 5,
                Summary = "Take a state-held strategic site.", Briefing = "Assault the state security detail at the strategic site and hold it.",
                Target = TargetRule.StateTerritory, Hostiles = 8, Xp = 900, TerritoryInfluence = 35, StateStabilityDelta = -6, SuspicionDelta = 10, Reputation = 6, PreTakeoverOnly = true },

            // ======================= Gang campaign
            new MissionDefinition { Id = "gang_uprising_prep", Title = "Take It to the System", Types = Gang, Template = MissionTemplate.Assault, Category = MissionCategory.Campaign, MinLevel = 8,
                Summary = "Hit a state-held site to show the city who runs it.", Briefing = "Push the state security out of the strategic site and hold it.",
                Target = TargetRule.StateTerritory, Hostiles = 8, Xp = 900, TerritoryInfluence = 30, StateStabilityDelta = -5, SuspicionDelta = 10, Reputation = 6, PreTakeoverOnly = true },

            // ======================= Final campaign (unlocked by the takeover system)
            new MissionDefinition { Id = "final_militia", Title = "March on the Capitol", Types = Militia, Template = MissionTemplate.FinalCampaign, Category = MissionCategory.Campaign,
                Summary = "The decisive battle for San Andreas.", Briefing = "Lead your forces to the seat of government. Break the state's last defenders, then hold the plaza until they surrender.",
                Repeatable = true, Milestone = true, PreTakeoverOnly = true, Waves = 4, Hostiles = 7, Xp = 5000, Requirement = c => c.Faction.Takeover.FinalMissionUnlocked, RequirementText = "Complete the campaign stages" },
            new MissionDefinition { Id = "final_mafia", Title = "The Last Sit-Down", Types = Mafia, Template = MissionTemplate.FinalCampaign, Category = MissionCategory.Campaign,
                Summary = "Remove the last loyalists standing between the family and the state.", Briefing = "Go to the financial district. The state's loyal enforcers are protecting the old order. When they fall, the city is yours.",
                Repeatable = true, Milestone = true, PreTakeoverOnly = true, Waves = 3, Hostiles = 7, Xp = 5000, Requirement = c => c.Faction.Takeover.FinalMissionUnlocked, RequirementText = "Complete the campaign stages" },
            new MissionDefinition { Id = "final_gang", Title = "Uprising", Types = Gang, Template = MissionTemplate.FinalCampaign, Category = MissionCategory.Campaign,
                Summary = "The streets rise up.", Briefing = "Rally at Legion Square and hold it against the full weight of state forces. If you survive, the city belongs to the streets.",
                Repeatable = true, Milestone = true, PreTakeoverOnly = true, Waves = 5, Hostiles = 7, Xp = 6000, Requirement = c => c.Faction.Takeover.FinalMissionUnlocked, RequirementText = "Complete the campaign stages" },

            // ======================= Government era
            new MissionDefinition { Id = "gov_insurgents", Title = "Crush the Insurgents", Template = MissionTemplate.Assault, Category = MissionCategory.Conflict,
                Summary = "Remaining factions are resisting the new order.", Briefing = "Lead state forces against the insurgent cell and secure the area.",
                Target = TargetRule.RivalTerritory, Hostiles = 7, Cash = 8000, Xp = 600, TerritoryInfluence = 35, RivalDamage = 8, Requirement = c => c.Faction.IsGovernment, RequirementText = "Government only" },
        };

        public static MissionDefinition Get(string id) => All.FirstOrDefault(d => d.Id == id);

        public static bool IsEligible(SimContext ctx, MissionDefinition d, out string reason)
        {
            var f = ctx.Faction;
            reason = null;
            if (!d.AvailableFor(f.Type)) { reason = "Not available for your faction type."; return false; }
            if (f.Level < d.MinLevel) { reason = "Requires faction level " + d.MinLevel + "."; return false; }
            if (d.PreTakeoverOnly && f.IsGovernment) { reason = "Already in power."; return false; }
            if (!d.GovernmentAllowed && f.IsGovernment) { reason = "Not available to the government."; return false; }
            if (!d.Repeatable && f.Takeover.CompletedMilestones.Contains(d.Id)) { reason = "Already completed."; return false; }
            if (d.Requirement != null && !d.Requirement(ctx)) { reason = d.RequirementText ?? "Requirements not met."; return false; }
            if (d.Category == MissionCategory.Campaign && d.Template != MissionTemplate.FinalCampaign && d.Milestone == false && !TakeoverSystem.CampaignMissionAllowed(ctx, d)) { reason = "Not part of the current campaign stage."; return false; }
            return true;
        }
    }

    public static class MissionBoard
    {
        public const int MaxOffers = 6;
        public const int RefreshEveryTicks = 6;

        public static void Refresh(SimContext ctx, bool force = false)
        {
            var s = ctx.State;
            var w = ctx.World;
            s.MissionBoard.RemoveAll(o => o.ExpiresTick <= w.Tick && !o.Urgent);
            s.MissionBoard.RemoveAll(o => MissionCatalog.Get(o.DefinitionId) == null);
            if (!force && w.Tick - s.LastBoardRefreshTick < RefreshEveryTicks && s.MissionBoard.Count > 0) return;
            s.LastBoardRefreshTick = w.Tick;

            // Always surface campaign/milestone missions when eligible.
            foreach (var d in MissionCatalog.All.Where(d => d.Category == MissionCategory.Campaign))
            {
                if (s.MissionBoard.Any(o => o.DefinitionId == d.Id)) continue;
                if (MissionCatalog.IsEligible(ctx, d, out _)) TryAddOffer(ctx, d, false);
            }

            var pool = MissionCatalog.All.Where(d => d.Category != MissionCategory.Campaign && MissionCatalog.IsEligible(ctx, d, out _)).ToList();
            ctx.Rng.Shuffle(pool);
            foreach (var d in pool)
            {
                if (s.MissionBoard.Count >= MaxOffers) break;
                if (s.MissionBoard.Any(o => o.DefinitionId == d.Id)) continue;
                TryAddOffer(ctx, d, false);
            }
        }

        public static MissionOffer TryAddOffer(SimContext ctx, MissionDefinition d, bool urgent)
        {
            var w = ctx.World;
            var f = ctx.Faction;
            var offer = new MissionOffer
            {
                Id = Ids.New("mo"),
                DefinitionId = d.Id,
                ExpiresTick = w.Tick + 24 + ctx.Rng.Next(0, 24),
                Urgent = urgent,
                Seed = ctx.Rng.Next(1, int.MaxValue),
            };
            if (!SelectTarget(ctx, d, offer)) return null;
            if (d.Template == MissionTemplate.FinalCampaign)
            {
                // The decisive battle is always against the state's own forces.
                offer.RivalId = FactionIds.State;
                offer.TerritoryId = null;
            }
            int diff = 1 + f.Level / 4;
            var rival = w.GetRival(offer.RivalId);
            if (rival != null) diff += (int)(rival.Strength / 35);
            offer.Difficulty = MathEx.Clamp(diff, 1, 5);
            float scale = 1f + (offer.Difficulty - 1) * 0.35f;
            offer.RewardCash = (long)(d.Cash * scale);
            offer.RewardXp = (int)(d.Xp * scale);
            ctx.State.MissionBoard.Add(offer);
            return offer;
        }

        private static bool SelectTarget(SimContext ctx, MissionDefinition d, MissionOffer o)
        {
            var w = ctx.World;
            switch (d.Target)
            {
                case TargetRule.RivalTerritory:
                {
                    var candidates = w.Territories.Where(t => t.Type != TerritoryType.Strategic && w.GetRival(t.OwnerId)?.Active == true).ToList();
                    if (ctx.Faction.IsGovernment) candidates = w.Territories.Where(t => w.GetRival(t.OwnerId)?.Active == true).ToList();
                    // Prefer enemies we're at war with.
                    var war = candidates.Where(t => w.GetRival(t.OwnerId).Relation == RelationStatus.War).ToList();
                    var t2 = war.Count > 0 && ctx.Rng.Chance(0.7f) ? ctx.Rng.Pick(war) : ctx.Rng.Pick(candidates);
                    if (t2 == null) return false;
                    o.TerritoryId = t2.Id;
                    o.RivalId = t2.OwnerId;
                    return true;
                }
                case TargetRule.OwnTerritory:
                {
                    var own = TerritorySystem.OwnedBy(w, FactionIds.Player).ToList();
                    var t2 = ctx.Rng.Pick(own);
                    if (t2 == null) return false;
                    o.TerritoryId = t2.Id;
                    var attacker = w.Rivals.Where(r => r.Active).OrderByDescending(r => r.Hostility).FirstOrDefault();
                    o.RivalId = attacker?.Id;
                    return attacker != null;
                }
                case TargetRule.StateTerritory:
                {
                    var st = w.Territories.Where(t => t.OwnerId == FactionIds.State).ToList();
                    var t2 = ctx.Rng.Pick(st);
                    if (t2 == null) return false;
                    o.TerritoryId = t2.Id;
                    o.RivalId = FactionIds.State;
                    return true;
                }
                case TargetRule.AnyRival:
                {
                    var rivals = w.Rivals.Where(r => r.Active).ToList();
                    var war = rivals.Where(r => r.Relation == RelationStatus.War || r.Relation == RelationStatus.Rivalry).ToList();
                    var r2 = war.Count > 0 && ctx.Rng.Chance(0.7f) ? ctx.Rng.Pick(war) : ctx.Rng.Pick(rivals);
                    if (r2 == null) return false;
                    o.RivalId = r2.Id;
                    o.TerritoryId = w.Territories.FirstOrDefault(t => t.OwnerId == r2.Id)?.Id;
                    return true;
                }
                case TargetRule.Headquarters:
                {
                    if (ctx.Faction.Headquarters == null) return false;
                    var attacker = w.Rivals.Where(r => r.Active).OrderByDescending(r => r.Hostility).FirstOrDefault();
                    o.RivalId = attacker?.Id;
                    o.TerritoryId = w.FindByZone(ctx.Faction.Headquarters.ZoneCode)?.Id;
                    return attacker != null;
                }
                default:
                {
                    var hostile = w.Rivals.Where(r => r.Active).OrderByDescending(r => r.Hostility).FirstOrDefault();
                    o.RivalId = hostile?.Id;
                    return true;
                }
            }
        }

        /// <summary>Creates an urgent defense mission for a pending attack/raid.</summary>
        public static void AddDefenseOffer(SimContext ctx, AttackEvent a)
        {
            var d = MissionCatalog.Get(a.IsRaid ? "defend_hq" : ctx.Faction.Type == FactionType.Militia ? "mil_hold" : ctx.Faction.Type == FactionType.StreetGang ? "gang_hold_block" : "defend_hq");
            if (d == null) return;
            var offer = new MissionOffer
            {
                Id = Ids.New("mo"),
                DefinitionId = d.Id,
                TerritoryId = a.TerritoryId,
                RivalId = a.AttackerId,
                ExpiresTick = a.DeadlineTick,
                Urgent = true,
                AttackId = a.Id,
                Difficulty = MathEx.Clamp(1 + (int)(a.Strength / 20), 1, 5),
                Seed = ctx.Rng.Next(1, int.MaxValue),
            };
            float scale = 1f + (offer.Difficulty - 1) * 0.35f;
            offer.RewardCash = (long)(d.Cash * scale);
            offer.RewardXp = (int)(d.Xp * scale);
            ctx.State.MissionBoard.Insert(0, offer);
        }
    }

    public static class MissionRewards
    {
        /// <summary>Applies the result of a mission to the simulation and returns lines for the results screen.</summary>
        public static List<string> Apply(SimContext ctx, MissionOffer offer, MissionOutcome outcome)
        {
            var f = ctx.Faction;
            var w = ctx.World;
            var d = MissionCatalog.Get(offer.DefinitionId);
            var lines = new List<string>();
            if (d == null) return lines;
            ctx.State.MissionBoard.RemoveAll(o => o.Id == offer.Id);
            var rival = w.GetRival(offer.RivalId);
            var territory = w.GetTerritory(offer.TerritoryId);
            var attack = offer.AttackId != null ? w.PendingAttacks.FirstOrDefault(a => a.Id == offer.AttackId) : null;

            if (outcome.Kills > 0)
            {
                WarSystem.OnEnemiesKilled(ctx, rival, outcome.Kills);
                if (offer.RivalId == FactionIds.State) w.State.ForceStrength = Math.Max(10, w.State.ForceStrength - outcome.Kills * 0.3f);
            }

            if (outcome.Success)
            {
                f.Stats.MissionsCompleted++;
                float scale = 1f + (offer.Difficulty - 1) * 0.35f;
                f.Resources.Cash += offer.RewardCash;
                f.Stats.TotalEarned += offer.RewardCash;
                if (offer.RewardCash > 0) lines.Add("+" + TextUtil.Money(offer.RewardCash));
                ProgressionSystem.AddXp(ctx, offer.RewardXp);
                lines.Add("+" + offer.RewardXp + " XP");
                Add(lines, "Reputation", d.Reputation * scale, v => f.Reputation = MathEx.Pct(f.Reputation + v));
                Add(lines, "Influence", d.Influence, v => f.Resources.Influence += (int)v);
                Add(lines, "Supplies", d.Supplies, v => f.Resources.Supplies += (int)v);
                Add(lines, "Product", d.Product, v => f.Resources.Product += (int)v);
                Add(lines, "Arms", d.Arms, v => f.Resources.Arms += (int)v);
                Add(lines, "Intel", d.Intel, v => f.Resources.Intel += (int)v);
                Add(lines, "Public support", d.PublicSupport, v => f.PublicSupport = MathEx.Pct(f.PublicSupport + v));
                if (d.SuspicionDelta != 0)
                {
                    SuspicionSystem.OnPublicIncident(ctx, d.SuspicionDelta);
                    lines.Add(FactionTypeInfo.SuspicionLabel(f.Type) + (d.SuspicionDelta > 0 ? " +" : " ") + (int)d.SuspicionDelta);
                }
                if (territory != null && d.TerritoryInfluence > 0)
                {
                    TerritorySystem.AddInfluence(territory, FactionIds.Player, d.TerritoryInfluence);
                    if (offer.RivalId != null) TerritorySystem.AddInfluence(territory, offer.RivalId, -d.TerritoryInfluence * 0.8f);
                    if (TerritorySystem.UpdateOwnership(territory, out string prev)) TerritorySystem.OnOwnershipChanged(ctx, territory, prev);
                    lines.Add(territory.Name + " control: " + (int)territory.InfluenceOf(FactionIds.Player) + "%");
                }
                if (rival != null && d.RivalDamage > 0)
                {
                    WarSystem.OnPlayerVictory(ctx, rival, d.RivalDamage * scale);
                    lines.Add(rival.Name + " weakened");
                }
                if (d.StateStabilityDelta != 0)
                {
                    w.State.Stability = MathEx.Pct(w.State.Stability + d.StateStabilityDelta * FactionTypeInfo.TakeoverSpeed(f.Type));
                    lines.Add("State stability " + (int)w.State.Stability + "%");
                }
                if (attack != null)
                {
                    w.PendingAttacks.Remove(attack);
                    if (attack.IsRaid) { f.Stats.RaidsSurvived++; f.Suspicion = MathEx.Pct(f.Suspicion - 25); lines.Add("Raid repelled"); }
                    else lines.Add("Attack repelled");
                    if (territory != null) territory.Contested = false;
                }
                if (d.Milestone && !f.Takeover.CompletedMilestones.Contains(d.Id)) f.Takeover.CompletedMilestones.Add(d.Id);
                foreach (var id in outcome.ParticipantIds)
                {
                    var m = f.GetMember(id);
                    if (m == null || m.Status != MemberStatus.Active) continue;
                    m.MissionsCompleted++;
                    MemberSystem.GrantExperience(m, 1.5f);
                    m.Morale = MathEx.Pct(m.Morale + 4);
                    m.Loyalty = MathEx.Pct(m.Loyalty + 1);
                }
                foreach (var m in f.ActiveMembers) m.Morale = MathEx.Pct(m.Morale + 1);
                ctx.Report("Missions", "Mission complete: " + d.Title + ".", EventSeverity.Good);
                if (d.Template == MissionTemplate.FinalCampaign) TakeoverSystem.OnFinalMissionSuccess(ctx);
            }
            else
            {
                f.Stats.MissionsFailed++;
                f.Reputation = MathEx.Pct(f.Reputation - 2);
                foreach (var m in f.ActiveMembers) m.Morale = MathEx.Pct(m.Morale - 3);
                lines.Add("Reputation -2");
                if (attack != null) { WarSystem.ResolveAttackAbstract(ctx, attack); lines.Add("Your garrison had to fight alone"); }
                if (d.Template == MissionTemplate.FinalCampaign)
                {
                    f.Takeover.FinalAttempts++;
                    w.State.Stability = MathEx.Pct(w.State.Stability + 6);
                    lines.Add("The state regains some footing (stability +6)");
                }
                ctx.Report("Missions", "Mission failed: " + d.Title + (string.IsNullOrEmpty(outcome.FailureReason) ? "." : " (" + outcome.FailureReason + ")."), EventSeverity.Bad);
            }

            ctx.State.MissionHistory.Add(new MissionRecord { DefinitionId = d.Id, Title = d.Title, Day = w.Day, Success = outcome.Success, Notes = outcome.FailureReason });
            if (ctx.State.MissionHistory.Count > 50) ctx.State.MissionHistory.RemoveAt(0);
            return lines;
        }

        private static void Add(List<string> lines, string label, float value, Action<float> apply)
        {
            if (Math.Abs(value) < 0.01f) return;
            apply(value);
            lines.Add(label + (value > 0 ? " +" : " ") + (int)Math.Round(value));
        }
    }
}

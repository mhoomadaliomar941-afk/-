using System;
using System.Linq;
using OrganizationEmpire.Native;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.UI.Pages
{
    internal sealed class DashboardPage : Page
    {
        public override string Title => "Dashboard";
        public override string Subtitle => Mod.Faction.Description;
        public override float ListFraction => 0.4f;

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            var w = Mod.State.World;
            l.Header("Quick actions");
            var urgent = Mod.State.MissionBoard.FirstOrDefault(o => o.Urgent);
            if (urgent != null)
            {
                var def = MissionCatalog.Get(urgent.DefinitionId);
                l.Action("RESPOND: " + def?.Title + " (" + w.GetTerritory(urgent.TerritoryId)?.Name + ")", () => Report(Mod.Missions.Start(urgent)),
                    "An attack is underway. " + Math.Max(0, urgent.ExpiresTick - w.Tick) + " hours left before your garrison has to fight alone.", !Mod.Missions.IsActive, disabledReason: "Finish the current mission first.").Tint = Ui.Renderer.Theme.Bad;
            }
            l.Action("Call everyone to me", () => Report(Mod.Commands.OrderMany(f.ActiveMembers.Take(Mod.Config.Spawning.MaxDeployedMembers), OrderType.Follow)),
                "Summons up to " + Mod.Config.Spawning.MaxDeployedMembers + " available members (deployment limit, Settings > Spawning).");
            l.Action("Everyone stand down", () => Report(Mod.Commands.OrderMany(f.ActiveMembers.Where(m => Mod.Members.IsDeployed(m)), OrderType.Standby)),
                "Deployed members go home and despawn when out of sight.");
            l.Link("Mission board", () => new MissionsPage(), value: Mod.State.MissionBoard.Count + " available");
            l.Action("Quick save", () => Report(Mod.Save(string.IsNullOrEmpty(Mod.State.SlotName) ? "autosave" : Mod.State.SlotName)));

            l.Header("Status");
            l.Progress("Level " + f.Level + " - " + FactionTypeInfo.TierName(f), ProgressionSystem.LevelProgress(f), f.Experience + " / " + FactionTypeInfo.XpForLevel(f.Level + 1) + " XP");
            l.Progress("Reputation", f.Reputation / 100f, ((int)f.Reputation).ToString(), PageUtil.LevelColor(Ui.Renderer.Theme, f.Reputation));
            l.Progress("Morale", f.Morale / 100f, ((int)f.Morale).ToString(), PageUtil.LevelColor(Ui.Renderer.Theme, f.Morale));
            l.Progress("Discipline", f.Discipline / 100f, ((int)f.Discipline).ToString(), PageUtil.LevelColor(Ui.Renderer.Theme, f.Discipline));
            l.Progress(FactionTypeInfo.SuspicionLabel(f.Type) + " - " + SuspicionSystem.Level(f), f.Suspicion / 100f, ((int)f.Suspicion).ToString(), PageUtil.LevelColor(Ui.Renderer.Theme, f.Suspicion, true),
                f.Type == FactionType.Militia ? "Suspicion is central to the organization: keep it low to unlock covert opportunities; high values bring investigations and raids." : "Heat from police and public attention. High heat brings investigations and raids.");
            l.Progress("Public support", f.PublicSupport / 100f, ((int)f.PublicSupport).ToString(), Ui.Renderer.Theme.Info);
            if (!f.IsGovernment) l.Progress("State takeover campaign", TakeoverSystem.OverallProgress(Mod.Sim), TextUtil.Pct(TakeoverSystem.OverallProgress(Mod.Sim) * 100), null, "See the Government tab for stages and requirements.");
            else l.Progress("State stability", f.Government.Stability / 100f, ((int)f.Government.Stability).ToString());

            l.Header("Recent events");
            foreach (var e in f.Events.Skip(Math.Max(0, f.Events.Count - 8)).Reverse())
            {
                var it = l.Info("D" + e.Day + " " + e.Hour.ToString("00") + "h  " + e.Text, "", e.Text);
                it.Tint = Ui.Renderer.Theme.Severity(e.Severity);
            }
        }

        public override void DrawDetail(Renderer r, float x, float y, float w, float h, Item focused)
        {
            var t = r.Theme;
            var f = Mod.Faction;
            var world = Mod.State.World;
            r.Text("OVERVIEW", x, y, Theme.TinySize + 0.02f, t.Accent, Theme.Condensed);
            float cy = y + 34;
            float col = (w - 20) / 2;
            void Row(string a, string b, float cx)
            {
                r.Text(a, cx, cy, Theme.SmallSize, t.TextDim);
                r.Text(b, cx + col - 10, cy, Theme.SmallSize, t.Text, Theme.Body, Align.Right);
            }
            var lastLedger = Simulation.LastLedger ?? EconomySystem.Compute(Mod.Sim, false);
            Row("Cash", TextUtil.Money(f.Resources.Cash), x); Row("Net / hour", TextUtil.Money((long)lastLedger.Net), x + col + 20); cy += 30;
            Row("Members", f.ActiveMembers.Count() + " active / " + f.LivingMemberCount, x); Row("Deployed", Mod.Members.DeployedCount.ToString(), x + col + 20); cy += 30;
            Row("Territories", TerritorySystem.PlayerTerritoryCount(world).ToString(), x); Row("Squads", f.Squads.Count.ToString(), x + col + 20); cy += 30;
            Row("Supplies", f.Resources.Supplies.ToString(), x); Row("Arms", f.Resources.Arms.ToString(), x + col + 20); cy += 30;
            Row("Influence", f.Resources.Influence.ToString(), x); Row("Intel", f.Resources.Intel.ToString(), x + col + 20); cy += 30;
            if (f.Type == FactionType.StreetGang) { Row("Product", f.Resources.Product.ToString(), x); cy += 30; }
            Row("Missions done", f.Stats.MissionsCompleted.ToString(), x); Row("Enemies down", f.Stats.EnemiesKilled.ToString(), x + col + 20); cy += 30;
            Row("Members lost", f.Stats.MembersLost.ToString(), x); Row("Rivals eliminated", f.Stats.RivalsEliminated.ToString(), x + col + 20); cy += 40;

            r.Text("CONFLICTS", x, cy, Theme.TinySize + 0.02f, t.Accent, Theme.Condensed);
            cy += 30;
            var hostile = world.Rivals.Where(rv => rv.Active && rv.Relation != RelationStatus.Neutral).OrderByDescending(rv => rv.Hostility).Take(5).ToList();
            if (hostile.Count == 0) { r.Text("No active rivalries.", x, cy, Theme.SmallSize, t.TextDim); cy += 28; }
            foreach (var rv in hostile)
            {
                r.Rect(x, cy + 6, 10, 10, Rgb.Parse(rv.Color, new Rgb(150, 150, 150)).ToColor());
                r.Text(rv.Name, x + 20, cy, Theme.SmallSize, t.Text);
                r.Text(rv.Relation.ToString(), x + w, cy, Theme.SmallSize, rv.Relation == RelationStatus.War ? t.Bad : rv.Relation == RelationStatus.Alliance ? t.Good : t.Warn, Theme.Body, Align.Right);
                cy += 28;
            }
            if (focused != null && focused.Hint != null)
            {
                cy += 16;
                r.Rect(x, cy, w, 1, t.Line);
                r.Paragraph(focused.Hint, x, cy + 10, Theme.SmallSize, t.TextDim, w);
            }
        }
    }

    internal sealed class FactionPage : Page
    {
        public override string Title => Mod.Faction.IsGovernment ? "State Overview" : "Faction Management";

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            l.Header("Profile");
            l.Action("Name", () => Ui.AskText("Faction name", f.Name, 40, s => { f.Name = s; Mod.Hq.Refresh(true); Mod.Territories.ForceRefresh(); }, null, s => s.Length < 2 ? "Too short." : null), value: f.Name);
            l.Action("Description", () => Ui.AskText("Description", f.Description, 160, s => f.Description = s), "Also used as lore for AI dialogue.", value: TextUtil.Truncate(f.Description ?? "-", 28));
            l.Info("Type", FactionTypeInfo.TypeName(f.Type), FactionTypeInfo.TypeBlurb(f.Type));
            l.Info("Tier", FactionTypeInfo.TierName(f) + " (level " + f.Level + ")", "Tiers: " + string.Join(" > ", FactionTypeInfo.Tiers(f.Type)) + ". Every 4 levels unlocks the next tier.");
            l.Info("Difficulty", f.Difficulty.ToString(), "Difficulty is fixed per faction.");
            l.Choice("Faction personality", PageUtil.Name(f.Personality), d => f.Personality = PageUtil.Next(f.Personality, d), "Changes diplomacy odds, heat decay and dialogue tone.");
            l.Choice("Behavior", PageUtil.Name(f.Behavior), d => f.Behavior = PageUtil.Next(f.Behavior, d), "Aggressive / Defensive / Expansionist / Covert / Balanced.");
            if (f.Type == FactionType.StreetGang)
            {
                var acts = new[] { GangActivities.None, GangActivities.Narcotics, GangActivities.Arms, GangActivities.Both };
                l.Choice("Side activities", acts, f.Activities, a => f.Activities = a,
                    a => a == GangActivities.Narcotics ? "Product" : a == GangActivities.Arms ? "Arms trade" : a == GangActivities.Both ? "Product + Arms" : "None",
                    "Abstract resource systems that generate income and unlock related missions (and raise heat).");
            }

            l.Header("Leadership");
            var deputies = f.Members.Where(m => m.IsDeputy).ToList();
            l.Info("Leader", f.PlayerTitle + " (you)");
            foreach (var d in deputies) l.Link("Deputy: " + d.DisplayName, () => new MemberDetailPage(d), value: Mod.Faction.GetRank(d.RankId)?.Name);
            l.Action("Appoint a deputy", () => PageUtil.PickMember(Ui, "Appoint deputy", f.ActiveMembers.Where(m => !m.IsDeputy), m => Report(MemberSystem.SetDeputy(f, m, true))),
                "Deputies get a loyalty boost and lead when you're away (max 3).", deputies.Count < 3, disabledReason: "You already have 3 deputies.");

            l.Header("Statistics");
            l.Info("Founded", "Day " + f.CreatedDay);
            l.Info("Members recruited", f.Stats.MembersRecruited.ToString());
            l.Info("Territories captured / lost", f.Stats.TerritoriesCaptured + " / " + f.Stats.TerritoriesLost);
            l.Info("Missions completed / failed", f.Stats.MissionsCompleted + " / " + f.Stats.MissionsFailed);
            l.Info("Raids survived", f.Stats.RaidsSurvived.ToString());
            l.Info("Total earned / spent", TextUtil.Money(f.Stats.TotalEarned) + " / " + TextUtil.Money(f.Stats.TotalSpent));

            l.Header("Danger zone");
            l.Action("Disband faction", () => Ui.Confirm("Disband " + f.Name + "?",
                "All members are released and the faction is removed from this session. Your save files are NOT deleted - you can reload them from Save / Load.", () =>
                {
                    Mod.NewState();
                    Ui.RebuildTabs();
                    Ui.Notify.Push("The faction was disbanded.", EventSeverity.Warning);
                }, true, "Disband"), "Ends this faction (save files remain).", danger: true);
        }
    }
}

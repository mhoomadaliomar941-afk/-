using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using OrganizationEmpire.Native;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Persistence;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.UI.Pages
{
    /// <summary>Before the takeover: the campaign. After: running the state.</summary>
    internal sealed class GovernmentPage : Page
    {
        public override string Title => Mod.Faction.IsGovernment ? "Government" : TakeoverSystem.AwaitingGovernmentFormation(Mod.Faction) ? "Form Your Government" : "State Campaign";
        public override string Subtitle => Mod.Faction.IsGovernment ? Mod.Faction.Government.GovernmentName + " of the " + Mod.Faction.Government.StateName : "Fictional, abstract campaign progression toward controlling San Andreas.";

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            if (TakeoverSystem.AwaitingGovernmentFormation(f)) { BuildFormation(l); return; }
            if (!f.IsGovernment) { BuildCampaign(l); return; }
            BuildGovernment(l);
        }

        // ------------------------------------------------------------------ campaign

        private void BuildCampaign(ItemList l)
        {
            var f = Mod.Faction;
            var stages = TakeoverSystem.StagesFor(f.Type);
            var st = Mod.State.World.State;
            l.Header("Overview");
            l.Progress("Campaign progress", TakeoverSystem.OverallProgress(Mod.Sim), TextUtil.Pct(TakeoverSystem.OverallProgress(Mod.Sim) * 100), null,
                f.Type == FactionType.Militia ? "The organization has the most direct path to power." : f.Type == FactionType.Mafia ? "The family rises through influence, fronts and the subjugation of rival families." : "The streets can rise - but the path is long and hard.");
            l.Progress("State stability", st.Stability / 100f, ((int)st.Stability).ToString(), Ui.Renderer.Theme.Info,
                "Drops as you hold strategic sites, win campaign operations, grow public support and (mafia) influence. Recovers slowly on its own and after failed final assaults.");
            l.Progress("State force strength", st.ForceStrength / 100f, ((int)st.ForceStrength).ToString(), Ui.Renderer.Theme.Warn);
            for (int i = 0; i < stages.Count; i++)
            {
                var s = stages[i];
                bool done = i < f.Takeover.Stage;
                bool current = i == f.Takeover.Stage;
                l.Header("Stage " + (i + 1) + ": " + s.Name + (done ? " - COMPLETE" : current ? " - CURRENT" : ""));
                if (!done && !current) { l.Text(s.Description, Ui.Renderer.Theme.TextFaint); continue; }
                l.Text(s.Description);
                foreach (var req in s.Requirements)
                {
                    float p = done ? 1f : req.Progress(Mod.Sim);
                    l.Progress(req.Text, p, p >= 0.999f ? "done" : TextUtil.Pct(p * 100), p >= 0.999f ? Ui.Renderer.Theme.Good : (Color?)null);
                }
            }
            if (f.Takeover.FinalMissionUnlocked)
            {
                l.Header("Final campaign");
                var offer = Mod.State.MissionBoard.FirstOrDefault(o => o.DefinitionId == TakeoverSystem.FinalMissionId(f.Type));
                var def = MissionCatalog.Get(TakeoverSystem.FinalMissionId(f.Type));
                l.Action("LAUNCH: " + def.Title, () =>
                {
                    if (offer == null) { MissionBoard.Refresh(Mod.Sim, true); offer = Mod.State.MissionBoard.FirstOrDefault(o => o.DefinitionId == def.Id); }
                    if (offer == null) { Ui.Notify.Push("The operation isn't ready yet.", EventSeverity.Warning); return; }
                    Ui.Confirm(def.Title, def.Briefing + "\n\nA backup save ('" + SaveManager.PreTakeoverSlot + "') is created before the assault.", () => { Report(Mod.Missions.Start(offer)); if (Mod.Missions.IsActive) Ui.Close(); });
                }, def.Summary + " Location: " + Missions.MissionLocations.FinalLocationName(f.Type) + ".", !Mod.Missions.IsActive, disabledReason: "Another mission is active.");
                if (f.Takeover.FinalAttempts > 0) l.Info("Failed attempts", f.Takeover.FinalAttempts.ToString());
            }
        }

        // ------------------------------------------------------------------ formation

        private string _stateName = "Free State of San Andreas";
        private string _govName;
        private string _title;
        private GovernmentType _type;
        private bool _keepFlag = true;
        private bool _initialized;

        private void BuildFormation(ItemList l)
        {
            var f = Mod.Faction;
            if (!_initialized)
            {
                _initialized = true;
                _type = GovernmentCatalog.DefaultTypeFor(f.Type);
                _title = GovernmentCatalog.RulerTitleFor(f.Type);
                _govName = f.Type == FactionType.Mafia ? "The Directorate" : f.Type == FactionType.Militia ? "National Command Council" : "People's Council";
            }
            l.Text("The old government has fallen. Decide what comes next. Members become the state's forces and ranks become government ranks (your old ranks are archived).");
            l.Header("The new state");
            l.Action("State name", () => Ui.AskText("State name", _stateName, 48, s => _stateName = s), value: _stateName);
            l.Action("Government name", () => Ui.AskText("Government name", _govName, 48, s => _govName = s), value: _govName);
            l.Action("Your title", () => Ui.AskText("Ruler title", _title, 32, s => _title = s), value: _title);
            l.Choice("Form of government", PageUtil.Name(_type), d => _type = PageUtil.Next(_type, d));
            l.Toggle("Keep the faction flag as the state flag", _keepFlag, v => _keepFlag = v, "Turn off to start from a fresh state flag you can design in the Flag Designer.");
            l.Header("Proclaim");
            l.Action("PROCLAIM THE " + _stateName.ToUpperInvariant(), () => Ui.Confirm("Proclaim the new state?", "Establish the " + _govName + " of the " + _stateName + " with you as " + _title + "?", () =>
            {
                var setup = new GovernmentSetup
                {
                    StateName = _stateName,
                    GovernmentName = _govName,
                    RulerTitle = _title,
                    Type = _type,
                    KeepFactionFlag = _keepFlag,
                    NewFlag = _keepFlag ? null : new FlagDesign { Pattern = FlagPattern.HorizontalTricolor, Color1 = "#1F3A93", Color2 = "#F2F2F2", Color3 = f.Identity.PrimaryColor, Emblem = EmblemType.Star, EmblemColor = "#C8A165", MonogramText = "SA", WorldFlagModel = "prop_flag_sa" },
                };
                var res = TakeoverSystem.EstablishGovernment(Mod.Sim, setup);
                Mod.FlushNotifications();
                f.IdentityPresets.Add(new IdentityPreset { Name = "Former faction: " + f.Name, Identity = f.Identity.Copy() });
                Report(res);
                if (res.Ok)
                {
                    Mod.Save(string.IsNullOrEmpty(Mod.State.SlotName) ? "autosave" : Mod.State.SlotName);
                    Ui.RebuildTabs();
                    Mod.Hq.Refresh(true);
                    Mod.Territories.ForceRefresh();
                    Native.Nat.Sound("RANK_UP", "HUD_AWARDS");
                }
            }, false, "Proclaim"));
        }

        // ------------------------------------------------------------------ government

        private void BuildGovernment(ItemList l)
        {
            var f = Mod.Faction;
            var g = f.Government;
            var th = Ui.Renderer.Theme;
            l.Header("State");
            l.Progress("Stability", g.Stability / 100f, ((int)g.Stability).ToString(), PageUtil.LevelColor(th, g.Stability), "Below 35 the risk of coup plots rises.");
            l.Progress("Legitimacy", g.Legitimacy / 100f, ((int)g.Legitimacy).ToString(), PageUtil.LevelColor(th, g.Legitimacy));
            l.Progress("Public support", g.PublicSupport / 100f, ((int)g.PublicSupport).ToString(), PageUtil.LevelColor(th, g.PublicSupport));
            l.Progress("Forces readiness", g.ForcesReadiness / 100f, ((int)g.ForcesReadiness).ToString(), th.Info);
            l.Progress("Insurgency", g.Insurgency / 100f, ((int)g.Insurgency).ToString(), PageUtil.LevelColor(th, g.Insurgency, true), "Remaining factions resist the new order; high insurgency brings attacks on state territory.");
            l.Info("Treasury", TextUtil.Money(g.Treasury), "Funded by taxes on controlled territory; pays ministry budgets. Shortfalls are covered from faction cash.");
            l.Slider("Tax rate", g.TaxRate * 100, 0, 50, 1, v => GovernmentSystem.SetTaxRate(g, v / 100f), "0", "Higher taxes fill the treasury but erode stability and support above ~15-20%.");

            l.Header("Ruler");
            l.Action("Rename state", () => Ui.AskText("State name", g.StateName, 48, s => Report(GovernmentSystem.Rename(Mod.Sim, s, null, null))), value: g.StateName);
            l.Action("Rename government", () => Ui.AskText("Government name", g.GovernmentName, 48, s => Report(GovernmentSystem.Rename(Mod.Sim, null, s, null))), value: g.GovernmentName);
            l.Action("Your title", () => Ui.AskText("Ruler title", g.RulerTitle, 32, s => Report(GovernmentSystem.Rename(Mod.Sim, null, null, s))), value: g.RulerTitle);
            l.Choice("Form of government", PageUtil.Name(g.Type), d => g.Type = PageUtil.Next(g.Type, d));

            l.Header("Ministries & officials");
            foreach (var min in g.Ministries)
            {
                var ministry = min;
                var official = f.GetMember(min.OfficialMemberId);
                float eff = GovernmentSystem.Effectiveness(f, min);
                l.Link(min.Name, () => new MinistryPage(ministry), min.Description + "\nEffectiveness: " + TextUtil.Pct(eff * 100),
                    official != null ? official.DisplayName : "vacant");
            }

            l.Header("Policies (" + g.Policies.Count + "/4)");
            foreach (var p in GovernmentCatalog.Policies)
            {
                var pol = p;
                l.Toggle(p.name, g.Policies.Contains(p.id), v => Report(GovernmentSystem.TogglePolicy(Mod.Sim, pol.id)), p.effect);
            }

            l.Header("Forces");
            l.Info("State forces (members)", f.ActiveMembers.Count() + " active");
            l.Link("Manage forces (Members)", () => new MembersPage());
            l.Link("State territory", () => new TerritoryPage(), value: TerritorySystem.PlayerTerritoryCount(Mod.State.World).ToString());

            l.Header("Archive");
            bool hasBackup = Mod.Saves.Exists(SaveManager.PreTakeoverSlot);
            l.Action("Restore pre-takeover backup", () => Ui.Confirm("Restore backup?", "Load '" + SaveManager.PreTakeoverSlot + "' - the save made right before the final assault. Unsaved progress is lost.", () =>
            {
                Report(Mod.Load(SaveManager.PreTakeoverSlot));
                Ui.RebuildTabs();
            }, true, "Restore"), "Reverts to your faction as it was before the takeover.", hasBackup, disabledReason: "No pre-takeover backup exists.", danger: true);
        }
    }

    internal sealed class MinistryPage : Page
    {
        private readonly Ministry _m;
        public MinistryPage(Ministry m) { _m = m; }
        public override string Title => _m.Name;

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            var min = _m;
            var official = f.GetMember(min.OfficialMemberId);
            l.Text(min.Description);
            l.Info("Official", official?.DisplayName ?? "vacant");
            l.Info("Effectiveness", TextUtil.Pct(GovernmentSystem.Effectiveness(f, min) * 100), "Skill, discipline and loyalty of the official, plus personality fit (intelligent for treasury/intel, disciplined for defense/interior).");
            l.Action("Appoint official", () => PageUtil.PickMember(Ui, "Appoint to " + min.Name, f.ActiveMembers.Where(m => m.Loyalty >= 40), m => Report(GovernmentSystem.Appoint(Mod.Sim, min, m)),
                m => m.Personality + " | skill " + (int)m.Skill + " | discipline " + (int)m.Discipline + " | loyalty " + (int)m.Loyalty + (m.GovernmentRole != null ? " | currently " + m.GovernmentRole : "")),
                "Only members with loyalty 40+ can hold office.");
            l.Action("Dismiss official", () => Report(GovernmentSystem.Appoint(Mod.Sim, min, null)), enabled: official != null, disabledReason: "Nobody to dismiss.");
            l.Slider("Daily budget", min.Budget, 0, 30000, 500, v => min.Budget = (int)v, "0", "Paid from the treasury.");
        }
    }
}

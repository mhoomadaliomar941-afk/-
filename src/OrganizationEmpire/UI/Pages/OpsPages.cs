using System;
using System.Collections.Generic;
using System.Drawing;
using Font = GTA.UI.Font;
using System.Linq;
using OrganizationEmpire.Native;
using OrganizationEmpire.Core.AI;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.World;

namespace OrganizationEmpire.UI.Pages
{
    internal sealed class DialoguePage : Page
    {
        public override string Title => "AI Dialogue";

        public override void DrawDetail(Renderer r, float x, float y, float w, float h, Item focused)
        {
            // The chat window occupies the right side while a conversation is open.
            if (Ui.Chat.IsOpen) return;
            base.DrawDetail(r, x, y, w, h, focused);
        }
        public override string Subtitle => "Provider: " + (Mod.Config.AI.Enabled ? Mod.Config.AI.Provider.ToString() : "Offline (AI disabled)") + (Mod.Config.AI.Provider != Core.Config.AiProviderKind.Offline ? " | model " + Mod.Config.AI.Model : "");

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            if (Ui.Chat.IsOpen)
            {
                l.Header("Conversation open");
                l.Text("Type in the chat panel on the right. Enter sends, Esc closes, Ctrl+Enter confirms a detected order.");
                l.Action("Close conversation", () => Ui.Chat.Close());
                return;
            }
            l.Header("Talk to a member");
            l.Text("Members nearby talk face to face; others answer over the radio" + (Mod.Config.AI.AllowRadioChat ? "." : " (radio chat is disabled in Settings > AI)."));
            foreach (var m in f.ActiveMembers.OrderByDescending(x => Mod.Members.IsDeployed(x)).ThenBy(x => f.GetRank(x.RankId)?.Order ?? 99))
            {
                var mm = m;
                int lines = Mod.State.Memories.TryGetValue(m.Id, out var mem) ? mem.Dialogue.Count : 0;
                var it = l.Action(m.DisplayName, () => Ui.Chat.OpenForMember(mm, Mod.Members.Get(mm)?.Ped),
                    value: (Mod.Members.IsDeployed(m) ? "nearby" : "radio") + (lines > 0 ? " | " + lines / 2 + " exchanges" : ""));
                it.DrawDetail = (r, x, y, w, h) => MemberDetailPage.DrawCard(Ui, r, mm, x, y, w);
            }
            l.Header("Memory");
            l.Action("Clear all conversation history", () => Ui.Confirm("Clear history?", "Remove all dialogue history (long-term memories are kept)?", () => { MemoryStore.ClearDialogueOnly(Mod.State); Ui.Notify.Push("Dialogue history cleared.", EventSeverity.Info); }));
            l.Action("Clear ALL memories", () => Ui.Confirm("Clear all memories?", "Every NPC forgets everything (conversations and memories).", () => { MemoryStore.ClearAll(Mod.State); Ui.Notify.Push("All memories cleared.", EventSeverity.Info); }, true), danger: true);
            l.Link("AI settings", () => new AiSettingsPage());
        }
    }

    internal sealed class MissionsPage : Page
    {
        public override string Title => "Missions";
        public override string Subtitle => Mod.Missions.IsActive ? "Active: " + Mod.Missions.Active.Def.Title : Mod.State.MissionBoard.Count + " operation(s) available";

        public override void Build(ItemList l)
        {
            var w = Mod.State.World;
            if (Mod.Missions.IsActive)
            {
                var a = Mod.Missions.Active;
                l.Header("Active mission");
                l.Info(a.Def.Title, a.Objective, a.Def.Briefing);
                if (!string.IsNullOrEmpty(a.Detail)) l.Info("Status", a.Detail);
                l.Action("Abort mission", () => Ui.Confirm("Abort?", "Abort the mission? It counts as a failure.", () => Mod.Missions.Abort("Aborted by the player", true), true, "Abort"), danger: true);
            }
            l.Header("Mission board");
            if (Mod.State.MissionBoard.Count == 0) l.Text("No operations available right now. The board refreshes every few hours.");
            foreach (var o in Mod.State.MissionBoard.OrderByDescending(x => x.Urgent).ToList())
            {
                var offer = o;
                var def = MissionCatalog.Get(o.DefinitionId);
                if (def == null) continue;
                string where = w.GetTerritory(o.TerritoryId)?.Name ?? (def.Template == MissionTemplate.FinalCampaign ? Missions.MissionLocations.FinalLocationName(Mod.Faction.Type) : "");
                bool eligible = o.Urgent || MissionCatalog.IsEligible(Mod.Sim, def, out _);
                MissionCatalog.IsEligible(Mod.Sim, def, out string reason);
                var it = l.Action((o.Urgent ? "[URGENT] " : "") + def.Title, () => Ui.Confirm("Start " + def.Title + "?", def.Briefing, () => { Report(Mod.Missions.Start(offer)); if (Mod.Missions.IsActive) Ui.Close(); }),
                    value: new string('*', o.Difficulty) + (where.Length > 0 ? " " + where : ""), enabled: eligible && !Mod.Missions.IsActive,
                    disabledReason: Mod.Missions.IsActive ? "Another mission is active." : reason);
                it.DrawDetail = (r, x, y, ww, h) => DrawOffer(r, offer, def, x, y, ww);
                if (o.Urgent) it.Tint = Ui.Renderer.Theme.Bad;
            }
            l.Action("Refresh the board", () => { MissionBoard.Refresh(Mod.Sim, true); Mod.FlushNotifications(); }, "Find new opportunities.");
            l.Header("History");
            foreach (var h in Mod.State.MissionHistory.AsEnumerable().Reverse().Take(10))
                l.Info("Day " + h.Day + " - " + h.Title, h.Success ? "Success" : "Failed", h.Notes, h.Success ? Ui.Renderer.Theme.Good : Ui.Renderer.Theme.Bad);
        }

        private void DrawOffer(Renderer r, MissionOffer o, MissionDefinition d, float x, float y, float w)
        {
            var t = r.Theme;
            var world = Mod.State.World;
            r.Text(d.Title.ToUpperInvariant(), x, y, Theme.HeadSize, t.Text, Theme.Condensed);
            r.Text(PageUtil.Name(d.Category) + " | " + PageUtil.Name(d.Template) + " | difficulty " + o.Difficulty + "/5", x, y + 38, Theme.SmallSize, t.Accent);
            float cy = y + 72;
            cy += r.Paragraph(d.Summary, x, cy, Theme.SmallSize, t.Text, w) + 8;
            cy += r.Paragraph(d.Briefing, x, cy, Theme.SmallSize, t.TextDim, w) + 14;
            var rival = world.GetRival(o.RivalId);
            if (rival != null) { r.Text("Opposition: " + rival.Name + " (strength " + (int)rival.Strength + ", " + rival.Relation + ")", x, cy, Theme.SmallSize, t.Text); cy += 28; }
            if (o.RivalId == FactionIds.State || d.Template == MissionTemplate.FinalCampaign) { r.Text("Opposition: state security forces", x, cy, Theme.SmallSize, t.Bad); cy += 28; }
            var terr = world.GetTerritory(o.TerritoryId);
            if (terr != null) { r.Text("Location: " + terr.Name + " (" + world.FactionName(terr.OwnerId, Mod.Faction) + ")", x, cy, Theme.SmallSize, t.Text); cy += 28; }
            if (!o.Urgent) { r.Text("Expires in " + Math.Max(0, o.ExpiresTick - world.Tick) + " hours", x, cy, Theme.SmallSize, t.TextDim); cy += 28; }
            else { r.Text("Respond within " + Math.Max(0, o.ExpiresTick - world.Tick) + " hours or your garrison fights alone", x, cy, Theme.SmallSize, t.Bad); cy += 28; }
            cy += 8;
            r.Text("REWARDS", x, cy, Theme.TinySize + 0.02f, t.Accent, Theme.Condensed); cy += 28;
            var rewards = new List<string>();
            if (o.RewardCash > 0) rewards.Add(TextUtil.Money(o.RewardCash));
            rewards.Add(o.RewardXp + " XP");
            if (d.Reputation > 0) rewards.Add("+Reputation");
            if (d.Influence > 0) rewards.Add("+" + d.Influence + " Influence");
            if (d.Supplies > 0) rewards.Add("+" + d.Supplies + " Supplies");
            if (d.Product > 0) rewards.Add("+" + d.Product + " Product");
            if (d.Arms > 0) rewards.Add("+" + d.Arms + " Arms");
            if (d.Intel > 0) rewards.Add("+" + d.Intel + " Intel");
            if (d.PublicSupport > 0) rewards.Add("+Public support");
            if (d.TerritoryInfluence > 0) rewards.Add("+Territory control");
            if (d.StateStabilityDelta < 0) rewards.Add("Weakens the state");
            if (d.SuspicionDelta > 0) rewards.Add("+" + (int)d.SuspicionDelta + " " + FactionTypeInfo.SuspicionLabel(Mod.Faction.Type));
            if (d.SuspicionDelta < 0) rewards.Add(FactionTypeInfo.SuspicionLabel(Mod.Faction.Type) + " " + (int)d.SuspicionDelta);
            r.Paragraph(string.Join("  |  ", rewards), x, cy, Theme.SmallSize, t.Text, w);
        }
    }

    internal sealed class TerritoryPage : Page
    {
        private bool _onlyMine;
        public override string Title => "Territory";
        public override string Subtitle => TerritorySystem.PlayerTerritoryCount(Mod.State.World) + " of " + Mod.State.World.Territories.Count + " territories controlled";

        public override void Build(ItemList l)
        {
            var w = Mod.State.World;
            l.Toggle("Show only my territories", _onlyMine, v => _onlyMine = v);
            l.Toggle("Map overlay", Mod.Config.World.ShowTerritoryOverlay, v => { Mod.Config.World.ShowTerritoryOverlay = v; Mod.SaveConfig(); Mod.Territories.ForceRefresh(); });
            var current = Mod.Territories.Current;
            if (current != null) l.Link("You are in: " + current.Name, () => new TerritoryDetailPage(current), value: w.FactionName(current.OwnerId, Mod.Faction));
            foreach (var group in w.Territories.Where(t => !_onlyMine || t.OwnerId == FactionIds.Player).GroupBy(t => t.Type).OrderBy(g => g.Key))
            {
                l.Header(PageUtil.Name(group.Key));
                foreach (var t in group.OrderBy(x => x.Name))
                {
                    var terr = t;
                    var it = l.Link(t.Name + (t.Contested ? "  (contested)" : ""), () => new TerritoryDetailPage(terr),
                        value: w.FactionName(t.OwnerId, Mod.Faction) + " | you " + (int)t.InfluenceOf(FactionIds.Player) + "%");
                    if (t.OwnerId == FactionIds.Player) it.Tint = Ui.Renderer.Theme.Good;
                    it.DrawDetail = (r, x, y, ww, h) => TerritoryDetailPage.DrawInfo(Ui, r, terr, x, y, ww);
                }
            }
        }
    }

    internal sealed class TerritoryDetailPage : Page
    {
        private readonly Territory _t;
        public TerritoryDetailPage(Territory t) { _t = t; }
        public override string Title => _t.Name;

        public override void Build(ItemList l)
        {
            var w = Mod.State.World;
            var f = Mod.Faction;
            var t = _t;
            l.Header("Actions");
            l.Action("Set waypoint", () => { Mod.Territories.SetRoute(t); Ui.Notify.Push("Waypoint set to " + t.Name + ".", EventSeverity.Info); });
            var offers = Mod.State.MissionBoard.Where(o => o.TerritoryId == t.Id).ToList();
            foreach (var o in offers)
            {
                var offer = o;
                var def = MissionCatalog.Get(o.DefinitionId);
                l.Action("Mission: " + def?.Title, () => { Report(Mod.Missions.Start(offer)); if (Mod.Missions.IsActive) Ui.Close(); }, def?.Summary, !Mod.Missions.IsActive, disabledReason: "A mission is already active.");
            }
            if (offers.Count == 0 && t.OwnerId != FactionIds.Player)
            {
                l.Action("Plan an operation here", () =>
                {
                    var def = MissionCatalog.All.FirstOrDefault(d => d.Template == MissionTemplate.Assault && d.AvailableFor(f.Type) && (t.Type == TerritoryType.Strategic) == (d.Target == TargetRule.StateTerritory) && MissionCatalog.IsEligible(Mod.Sim, d, out _));
                    if (def == null) { Ui.Notify.Push(t.Type == TerritoryType.Strategic ? "Strategic sites can only be attacked once your campaign allows it (Government tab)." : "No suitable operation is available.", EventSeverity.Warning); return; }
                    var offer = MissionBoard.TryAddOffer(Mod.Sim, def, false);
                    if (offer != null) { offer.TerritoryId = t.Id; offer.RivalId = t.OwnerId ?? w.Rivals.Where(r => r.Active).OrderByDescending(r => r.Hostility).FirstOrDefault()?.Id; Ui.Notify.Push(def.Title + " added to the mission board.", EventSeverity.Info); }
                }, "Adds an assault mission targeting this territory to the board.");
            }
            var squads = f.Squads.Where(s => s.AssignedTerritoryId == t.Id).ToList();
            if (t.OwnerId == FactionIds.Player)
            {
                l.Action("Garrison a squad here", () =>
                {
                    var opts = f.Squads.Select(s => new PickOption(s.Name + " (" + s.MemberIds.Count + ")", s.AssignedTerritoryId != null ? "Currently garrisoning " + w.GetTerritory(s.AssignedTerritoryId)?.Name : null)).ToList();
                    Ui.ShowPicker("Garrison " + t.Name, opts, i => { f.Squads[i].AssignedTerritoryId = t.Id; Ui.Notify.Push(f.Squads[i].Name + " now garrisons " + t.Name + ".", EventSeverity.Good); });
                }, "Garrisons raise control over time and defend against attacks.");
                l.Action("Fortify (" + TextUtil.Money(5000 + (long)t.Fortification * 400) + ")", () =>
                {
                    var r = EconomySystem.Spend(f, 5000 + (long)t.Fortification * 400, "fortifications");
                    if (r.Ok) { t.Fortification = Math.Min(40, t.Fortification + 5); Ui.Notify.Push(t.Name + " fortified.", EventSeverity.Good); } else Report(r);
                }, "Permanent defense bonus against attacks.", t.Fortification < 40, disabledReason: "Fully fortified.");
            }
            foreach (var s in squads) l.Info("Garrison", s.Name + " (" + s.MemberIds.Count + ")");
            l.Header("Influence");
            foreach (var kv in t.Influence.OrderByDescending(kv => kv.Value))
                l.Progress(w.FactionName(kv.Key, f), kv.Value / 100f, (int)kv.Value + "%", Mod.Territories.OwnerColor(kv.Key).ToColor());
        }

        public override void DrawDetail(Renderer r, float x, float y, float w, float h, Item focused)
        {
            DrawInfo(Ui, r, _t, x, y, w);
        }

        public static void DrawInfo(UiManager ui, Renderer r, Territory t, float x, float y, float w)
        {
            var th = r.Theme;
            var world = ui.Mod.State.World;
            r.Text(t.Name.ToUpperInvariant(), x, y, Theme.HeadSize, th.Text, Theme.Condensed);
            var oc = ui.Mod.Territories.OwnerColor(t.OwnerId).ToColor();
            r.Rect(x, y + 44, 12, 12, oc);
            r.Text(world.FactionName(t.OwnerId, ui.Mod.Faction) + (t.Contested ? " - CONTESTED" : ""), x + 20, y + 38, Theme.SmallSize, th.Text);
            float cy = y + 72;
            r.Text("Type: " + PageUtil.Name(t.Type) + "   Value: " + TextUtil.Money(t.BaseValue) + "/day", x, cy, Theme.SmallSize, th.TextDim); cy += 28;
            r.Text("GTA zones: " + string.Join(", ", t.Zones) + (t.CenterVerified ? "" : "  (map position approximate until visited)"), x, cy, Theme.SmallSize, th.TextDim); cy += 28;
            r.Text("Unrest: " + (int)t.Unrest + "   Fortification: " + (int)t.Fortification, x, cy, Theme.SmallSize, th.TextDim); cy += 28;
            float mine = t.InfluenceOf(FactionIds.Player);
            r.Text("Your control", x, cy, Theme.SmallSize, th.Text);
            r.Text((int)mine + "%", x + w, cy, Theme.SmallSize, th.Text, Theme.Body, Align.Right); cy += 26;
            r.Bar(x, cy, w, 6, mine / 100f, th.Accent); cy += 18;
            var attack = world.PendingAttacks.FirstOrDefault(a => a.TerritoryId == t.Id);
            if (attack != null) { r.Text("UNDER ATTACK by " + world.FactionName(attack.AttackerId, ui.Mod.Faction) + " - " + Math.Max(0, attack.DeadlineTick - world.Tick) + "h left", x, cy, Theme.SmallSize, th.Bad); cy += 28; }
            cy += 8;
            r.Paragraph("Control comes from missions, fighting rivals here, your presence with members, garrisons and the HQ. Above 50% (and the highest share) you own it and collect its income.", x, cy, Theme.SmallSize, th.TextFaint, w);
        }
    }

    internal sealed class VehiclesPage : Page
    {
        public override string Title => "Vehicles";
        public override string Subtitle => Mod.Faction.Vehicles.Count + " / " + Mod.Vehicles.MaxGarage() + " in the garage, " + Mod.Vehicles.Spawned.Count() + " out";

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            l.Header("Garage");
            l.Action("Register my current vehicle", () => Report(Mod.Vehicles.RegisterCurrent()), "Adds the vehicle you're in to the faction garage with your colors and plate.");
            l.Action("Paint my current vehicle in faction colors", () => Report(Mod.Vehicles.ColorCurrentVehicle()));
            foreach (var v in f.Vehicles)
            {
                var fv = v;
                string state = v.Destroyed ? "repairing " + (int)Math.Ceiling(v.RepairHoursLeft) + "h" : Mod.Vehicles.IsSpawned(v) ? "out" : "garage";
                l.Link(v.DisplayName, () => new VehicleDetailPage(fv), value: state + (v.AssignedSquadId != null ? " | " + f.GetSquad(v.AssignedSquadId)?.Name : ""));
            }
            if (f.Vehicles.Count == 0) l.Text("No faction vehicles yet. Get in a car and register it.");
        }
    }

    internal sealed class VehicleDetailPage : Page
    {
        private readonly FactionVehicle _v;
        public VehicleDetailPage(FactionVehicle v) { _v = v; }
        public override string Title => _v.DisplayName;

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            var v = _v;
            if (!f.Vehicles.Contains(v)) { l.Text("Removed."); return; }
            l.Header("Deploy");
            l.Action("Spawn here", () => Report(Mod.Vehicles.SpawnNearPlayer(v)), "Appears on the nearest road.", v.Available, disabledReason: "Being repaired.");
            l.Action("Call with driver", () => Report(Mod.Vehicles.CallWithDriver(v)), "The assigned driver (or a faction driver) brings it to you.", v.Available, disabledReason: "Being repaired.");
            l.Action("Return to garage", () => Report(Mod.Vehicles.Store(v)), enabled: Mod.Vehicles.IsSpawned(v), disabledReason: "Already in the garage.");
            l.Header("Configuration");
            l.Action("Rename", () => Ui.AskText("Vehicle name", v.DisplayName, 30, s => v.DisplayName = s), value: v.DisplayName);
            l.Action("License plate", () => Ui.AskText("Plate (max 8)", v.Plate, 8, s => v.Plate = s.ToUpperInvariant()), value: v.Plate);
            l.Toggle("Use faction colors", v.UseFactionColors, b => v.UseFactionColors = b);
            if (!v.UseFactionColors)
            {
                PageUtil.ColorRow(l, Ui, "Primary color", v.PrimaryColor ?? "#1E1E1E", c => v.PrimaryColor = c);
                PageUtil.ColorRow(l, Ui, "Secondary color", v.SecondaryColor ?? "#1E1E1E", c => v.SecondaryColor = c);
            }
            var squads = new List<Squad> { null };
            squads.AddRange(f.Squads);
            l.Choice("Assigned squad", squads, f.GetSquad(v.AssignedSquadId), s => { v.AssignedSquadId = s?.Id; if (s != null) s.VehicleId = v.Id; }, s => s?.Name ?? "None");
            l.Action("Driver", () => PageUtil.PickMember(Ui, "Assign driver", f.ActiveMembers, m => { v.DriverMemberId = m.Id; Ui.Notify.Push(m.ShortName + " drives the " + v.DisplayName + ".", EventSeverity.Info); }),
                "The driver brings the vehicle when you call it.", value: f.GetMember(v.DriverMemberId)?.DisplayName ?? "any");
            l.Action("Apply identity now", () =>
            {
                var sv = Mod.Vehicles.Spawned.FirstOrDefault(s => s.Def == v);
                if (sv != null) { Mod.Vehicles.ApplyIdentity(sv); Ui.Notify.Push("Updated.", EventSeverity.Info); }
            }, enabled: Mod.Vehicles.IsSpawned(v), disabledReason: "The vehicle isn't out.");
            l.Info("Model", v.Model);
            l.Spacer();
            l.Action("Remove from garage", () => Ui.Confirm("Remove " + v.DisplayName + "?", "The vehicle is removed from the faction.", () => { Report(Mod.Vehicles.Remove(v)); Ui.Back(); }, true), danger: true);
        }
    }

    internal sealed class HeadquartersPage : Page
    {
        public override string Title => "Headquarters";
        public override string Subtitle => Mod.Faction.Headquarters == null ? "No headquarters yet." : Mod.Faction.Headquarters.Name + " - " + GtaUtil.ZoneName(Mod.Faction.Headquarters.Position.ToVector3OE());

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            var hq = f.Headquarters;
            l.Header("Location");
            l.Action(hq == null ? "Establish headquarters here" : "Move headquarters here", () => Ui.Confirm(hq == null ? "Establish HQ here?" : "Move HQ here?",
                "Uses your exact current position. " + (hq != null ? "Moving costs a little morale; upgrades are kept." : ""), () => Report(Mod.Hq.EstablishHere(hq?.Name))));
            if (Mod.Integrations != null && Mod.Integrations.Maps.Count > 0)
                l.Link("Locations from installed maps", () => new MapLocationsPage(), value: Mod.Integrations.Maps.Count.ToString(), hint: "Map Editor / Menyoo maps found in your game folder.");
            if (hq == null) return;
            l.Action("Rename", () => Ui.AskText("HQ name", hq.Name, 32, s => { hq.Name = s; Mod.Hq.Refresh(true); }), value: hq.Name);
            l.Action("Set waypoint to HQ", () => { GTA.World.WaypointPosition = hq.Position.ToVector3OE(); Ui.Notify.Push("Waypoint set.", EventSeverity.Info); });
            l.Header("Personnel");
            l.Action("Call everyone to HQ", () => Report(Mod.Commands.CallToHq(f.ActiveMembers.Where(m => Mod.Members.IsDeployed(m)))), "Deployed members head back to the HQ.");
            l.Action("Assign guards", () => PageUtil.PickMember(Ui, "Add a guard (" + hq.GuardMemberIds.Count + "/" + Mod.Config.Spawning.MaxHqGuards + ")", f.ActiveMembers.Where(m => !hq.GuardMemberIds.Contains(m.Id)), m =>
            {
                var guards = hq.GuardMemberIds.Select(f.GetMember).Where(x => x != null).ToList();
                guards.Add(m);
                Report(Mod.Commands.AssignHqGuards(guards));
            }), "Guards appear when you're near the HQ.", value: hq.GuardMemberIds.Count + "/" + Mod.Config.Spawning.MaxHqGuards);
            foreach (var id in hq.GuardMemberIds.ToList())
            {
                var m = f.GetMember(id);
                if (m == null) continue;
                l.Action("Guard: " + m.DisplayName, () => { hq.GuardMemberIds.Remove(m.Id); m.Order = OrderType.Standby; Ui.Notify.Push(m.ShortName + " relieved.", EventSeverity.Info); }, "Enter: relieve this guard.");
            }
            l.Choice("Guard behavior", PageUtil.Name(hq.GuardBehavior), d => hq.GuardBehavior = PageUtil.Next(hq.GuardBehavior, d), "Static posts, patrols around the HQ, or alert (engages threats in a wider radius).");
            l.Header("Identity");
            l.Toggle("Flag pole at HQ", hq.ShowFlag, v => { hq.ShowFlag = v; Mod.Hq.Refresh(true); }, "Places the in-game flag model chosen in the Flag Designer. Custom images are shown in menus only (see docs).");
            l.Header("Upgrades");
            foreach (var u in HqUpgradeCatalog.All)
            {
                var def = u;
                int lvl = hq.Level(u.Upgrade);
                bool max = lvl >= u.MaxLevel;
                l.Action(u.Name + " (" + lvl + "/" + u.MaxLevel + ")", () => Report(EconomySystem.UpgradeHq(Mod.Sim, def.Upgrade)), u.Effect, !max,
                    max ? "maxed" : TextUtil.Money(HqUpgradeCatalog.Cost(u.Upgrade, lvl)), disabledReason: "Fully upgraded.");
            }
        }
    }

    internal sealed class RelationsPage : Page
    {
        public override string Title => "Faction Relations";
        public override string Subtitle => "Diplomacy, wars and alliances with the other factions of San Andreas.";

        public override void Build(ItemList l)
        {
            var w = Mod.State.World;
            foreach (var group in w.Rivals.Where(r => r.Enabled).GroupBy(r => r.Eliminated ? "Eliminated" : r.Relation.ToString()).OrderBy(g => g.Key == "War" ? 0 : g.Key == "Rivalry" ? 1 : g.Key == "Eliminated" ? 9 : 5))
            {
                l.Header(group.Key);
                foreach (var r in group.OrderByDescending(x => x.Hostility))
                {
                    var rv = r;
                    var it = l.Link(r.Name, () => new RivalDetailPage(rv), value: r.Eliminated ? "gone" : "hostility " + (int)r.Hostility + " | strength " + (int)r.Strength);
                    it.Swatch = Rgb.Parse(r.Color, new Rgb(150, 150, 150));
                    it.DrawDetail = (rr, x, y, ww, h) => RivalDetailPage.DrawInfo(Ui, rr, rv, x, y, ww);
                }
            }
            var disabled = w.Rivals.Where(r => !r.Enabled).ToList();
            if (disabled.Count > 0) l.Info("Removed from simulation", disabled.Count + " faction(s)", "Re-enable them in Settings > World & Factions.");
        }
    }

    internal sealed class RivalDetailPage : Page
    {
        private readonly RivalFaction _r;
        public RivalDetailPage(RivalFaction r) { _r = r; }
        public override string Title => _r.Name;

        public override void Build(ItemList l)
        {
            var r = _r;
            if (!r.Active) { l.Text(r.Eliminated ? "This faction has been eliminated." : "This faction is disabled."); return; }
            l.Header("Diplomacy");
            l.Action("Declare rivalry", () => Report(WarSystem.DeclareRivalry(Mod.Sim, r)), "Open hostility without full war.", r.Relation == RelationStatus.Neutral || r.Relation == RelationStatus.Peace, disabledReason: "Not possible in the current state.");
            l.Action("Declare WAR", () => Ui.Confirm("Declare war on " + r.Name + "?", "Their crews will attack your territory and their members become hostile on sight.", () => { Report(WarSystem.DeclareWar(Mod.Sim, r)); Mod.FlushNotifications(); }, true, "Declare war"),
                enabled: r.Relation != RelationStatus.War, disabledReason: "Already at war.", danger: true);
            l.Action("Propose ceasefire (" + TextUtil.Pct(WarSystem.AcceptanceChance(Mod.Sim, r, RelationStatus.Ceasefire) * 100) + ")", () => { Report(WarSystem.Propose(Mod.Sim, r, RelationStatus.Ceasefire)); Mod.FlushNotifications(); },
                "Better odds when you're winning (war score).", r.Relation == RelationStatus.War, disabledReason: "Only during a war.");
            l.Action("Propose peace (" + TextUtil.Pct(WarSystem.AcceptanceChance(Mod.Sim, r, RelationStatus.Peace) * 100) + ")", () => { Report(WarSystem.Propose(Mod.Sim, r, RelationStatus.Peace)); Mod.FlushNotifications(); },
                enabled: r.Relation == RelationStatus.Neutral || r.Relation == RelationStatus.Rivalry || r.Relation == RelationStatus.Ceasefire, disabledReason: "Not possible now.");
            l.Action("Propose alliance (" + TextUtil.Pct(WarSystem.AcceptanceChance(Mod.Sim, r, RelationStatus.Alliance) * 100) + ")", () => { Report(WarSystem.Propose(Mod.Sim, r, RelationStatus.Alliance)); Mod.FlushNotifications(); },
                "Requires peace, 35 reputation and 15 Influence. Allies help defend and send gifts.", r.Relation == RelationStatus.Peace, disabledReason: "Requires a peace treaty.");
            l.Action("Pay tribute (" + TextUtil.Money(WarSystem.TributeCost(r)) + ")", () => { Report(WarSystem.PayTribute(Mod.Sim, r)); Mod.FlushNotifications(); }, "Reduces hostility; may end a war.");
            l.Action("Covert operation (" + (Mod.Faction.Type == FactionType.Militia ? 8 : 12) + " Intel)", () => { Report(WarSystem.Sabotage(Mod.Sim, r)); Mod.FlushNotifications(); },
                "Weakens their strength. Raises suspicion and hostility.");
            l.Header("Intel");
            l.Info("Status", r.Relation.ToString());
            l.Progress("Hostility", r.Hostility / 100f, ((int)r.Hostility).ToString(), Ui.Renderer.Theme.Bad);
            l.Progress("Strength", r.Strength / 100f, ((int)r.Strength).ToString(), Ui.Renderer.Theme.Warn);
            l.Progress("Wealth", r.Wealth / 100f, ((int)r.Wealth).ToString(), Ui.Renderer.Theme.Info);
            if (r.Relation == RelationStatus.War) l.Info("War score", ((int)r.WarScore).ToString(), "Positive means you're winning.");
            foreach (var t in Mod.State.World.Territories.Where(t => t.OwnerId == r.Id))
            {
                var terr = t;
                l.Link("Holds: " + t.Name, () => new TerritoryDetailPage(terr));
            }
        }

        public override void DrawDetail(Renderer rr, float x, float y, float w, float h, Item focused)
        {
            DrawInfo(Ui, rr, _r, x, y, w);
            if (focused?.Hint != null) rr.Paragraph(focused.Hint, x, y + 260, Theme.SmallSize, rr.Theme.TextDim, w);
        }

        public static void DrawInfo(UiManager ui, Renderer r, RivalFaction rv, float x, float y, float w)
        {
            var t = r.Theme;
            var c = Rgb.Parse(rv.Color, new Rgb(150, 150, 150)).ToColor();
            r.Rect(x, y, 6, 36, c);
            r.Text(rv.Name.ToUpperInvariant(), x + 16, y, Theme.HeadSize, t.Text, Theme.Condensed);
            r.Text(PageUtil.Name(rv.Kind) + " | " + rv.Relation, x + 16, y + 38, Theme.SmallSize, c);
            float cy = y + 72;
            cy += r.Paragraph(rv.Description ?? "", x, cy, Theme.SmallSize, t.TextDim, w) + 12;
            float half = (w - 20) / 2;
            PageUtil.Stat(r, x, cy, half, "HOSTILITY", rv.Hostility, t.Bad);
            PageUtil.Stat(r, x + half + 20, cy, half, "STRENGTH", rv.Strength, t.Warn);
            cy += 44;
            int held = ui.Mod.State.World.Territories.Count(tt => tt.OwnerId == rv.Id);
            r.Text("Territories held: " + held + "   Aggression: " + (int)(rv.Aggression * 100) + "%", x, cy, Theme.SmallSize, t.Text);
        }
    }

    internal sealed class ResourcesPage : Page
    {
        public override string Title => "Resources";

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            var res = f.Resources;
            l.Header("Stockpiles");
            l.Info("Cash", TextUtil.Money(res.Cash));
            l.Info("Supplies", res.Supplies.ToString(), "Generic materials. Consumed by product making and aid missions. Rural/industrial territories produce them.");
            l.Info("Arms", res.Arms.ToString(), "Weapons stock for loadouts and the arms trade.");
            l.Info("Influence", res.Influence.ToString(), "Political/social leverage. Central to the mafia's path to power; used for alliances.");
            l.Info("Intel", res.Intel.ToString(), "From scouts, comms upgrades and recon. Spent on covert operations.");
            if (f.Type == FactionType.StreetGang) l.Info("Product", res.Product.ToString(), "Fictional product made from supplies by the 'Product' activity and sold through your corners.");

            l.Header("Trade");
            l.Action("Buy 10 supplies", () => Report(EconomySystem.BuySupplies(f, 10)), value: TextUtil.Money(EconomySystem.SupplyPrice * 10));
            l.Action("Buy 5 arms", () => Report(EconomySystem.BuyArms(f, 5)), "Raises heat a little.", value: TextUtil.Money((long)(EconomySystem.ArmsPrice * 1.3) * 5));
            if (f.Type == FactionType.StreetGang)
                l.Action("Move 10 product in bulk", () => Report(EconomySystem.SellProduct(f, 10)), "Quick cash at a discount; raises heat.", res.Product > 0, disabledReason: "No product.");

            l.Header("Businesses (" + f.Businesses.Count + ")");
            foreach (var b in f.Businesses.ToList())
            {
                var biz = b;
                var def = BusinessCatalog.Get(b.Kind);
                l.Action(b.Name + " (L" + b.Level + ")", () => Report(EconomySystem.UpgradeBusiness(Mod.Sim, biz)),
                    "Income " + TextUtil.Money((long)(def.DailyIncome * (1 + (b.Level - 1) * 0.5))) + "/day. Enter: upgrade for " + TextUtil.Money(def.Price / 2 * b.Level) + ".", b.Level < 3, "upgrade", disabledReason: "Max level.");
            }
            l.Link("Acquire a business", () => new BusinessShopPage(), f.Type == FactionType.Mafia ? "Fronts are the family's backbone: steady income and Influence." : "Front businesses unlock at level 3.");

            var ledger = Simulation.LastLedger ?? EconomySystem.Compute(Mod.Sim, false);
            l.Header("Hourly ledger (net " + TextUtil.Money((long)ledger.Net) + ")");
            foreach (var i in ledger.Income) l.Info("+ " + i.Label, TextUtil.Money((long)Math.Round(i.Amount)), null, Ui.Renderer.Theme.Good);
            foreach (var e in ledger.Expenses) l.Info("- " + e.Label, TextUtil.Money((long)Math.Round(e.Amount)), null, Ui.Renderer.Theme.Bad);
            l.Info("Projected per day", TextUtil.Money((long)(ledger.Net * 24)));
        }
    }

    internal sealed class BusinessShopPage : Page
    {
        public override string Title => "Acquire a Business";

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            var owned = TerritorySystem.OwnedBy(Mod.State.World, FactionIds.Player).ToList();
            foreach (var b in BusinessCatalog.All)
            {
                var def = b;
                l.Action(b.Name, () => Ui.Confirm("Buy " + def.Name + "?", "Price " + TextUtil.Money(def.Price) + ", income " + TextUtil.Money(def.DailyIncome) + "/day.",
                    () => { Report(EconomySystem.BuyBusiness(Mod.Sim, def.Kind, owned.FirstOrDefault()?.Id)); Mod.FlushNotifications(); }),
                    "Income " + TextUtil.Money(b.DailyIncome) + "/day (mafia +25%). Heat +" + b.HeatPerDay.ToString("0.0") + "/day.", f.Resources.Cash >= b.Price, TextUtil.Money(b.Price), disabledReason: "Not enough cash.");
            }
        }
    }

    internal static class Vec3Ext
    {
        public static GTA.Math.Vector3 ToVector3OE(this Vec3 v) => new GTA.Math.Vector3(v.X, v.Y, v.Z);
    }
}

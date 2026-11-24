using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Native;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.World;

namespace OrganizationEmpire.UI.Pages
{
    internal sealed class RanksPage : Page
    {
        public override string Title => "Ranks & Hierarchy";
        public override string Subtitle => "Fully customizable. The top rank is always yours.";

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            l.Header("Hierarchy (highest first)");
            foreach (var r in f.Ranks.OrderBy(x => x.Order))
            {
                var rank = r;
                string v = r.ReservedForPlayer ? "you" : RankSystem.MemberCount(f, r) + " member(s), " + TextUtil.Money(r.Salary) + "/day";
                l.Link((r.Order + 1) + ". " + r.Name, () => new RankDetailPage(rank), value: v);
            }
            l.Header("Manage");
            l.Action("Create rank", () => Ui.AskText("New rank name", "", RankSystem.MaxNameLength, s => Report(RankSystem.Create(f, s, out _))),
                "New ranks are inserted just above the lowest rank; reorder them afterwards.", f.Ranks.Count < RankSystem.MaxRanks, disabledReason: "Maximum of " + RankSystem.MaxRanks + " ranks.");
            l.Action("Reset to " + FactionTypeInfo.TypeName(f.Type) + " template", () => Ui.Confirm("Replace ranks?", "Replace the current ranks with the default template? Members are mapped to similar positions.",
                () => Report(RankSystem.ApplyTemplate(f, f.IsGovernment ? RankTemplates.Government() : RankTemplates.For(f.Type)))), danger: true);
        }
    }

    internal sealed class RankDetailPage : Page
    {
        private readonly Rank _r;
        public RankDetailPage(Rank r) { _r = r; }
        public override string Title => "Rank: " + _r.Name;

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            var r = _r;
            if (!f.Ranks.Contains(r)) { l.Text("This rank no longer exists."); return; }
            l.Header("General");
            l.Action("Rename", () => Ui.AskText("Rank name", r.Name, RankSystem.MaxNameLength, s => Report(RankSystem.Rename(f, r, s))), value: r.Name);
            if (r.ReservedForPlayer) { l.Text("This is your rank. You can rename it; it cannot be moved, deleted or assigned."); return; }
            l.Action("Move up", () => Report(RankSystem.Move(f, r, -1)), enabled: r.Order > 1, disabledReason: "Already directly below your rank.");
            l.Action("Move down", () => Report(RankSystem.Move(f, r, 1)), enabled: r.Order < f.Ranks.Count - 1, disabledReason: "Already the lowest rank.");
            l.Slider("Daily salary", r.Salary, 0, 5000, 50, v => r.Salary = (int)v, "0", "Paid per member per day. Unpaid members lose morale and loyalty.");

            l.Header("Permissions");
            foreach (RankPermission p in new[] { RankPermission.Recruit, RankPermission.CommandSquads, RankPermission.LeadSquad, RankPermission.Promote, RankPermission.Negotiate, RankPermission.ManageResources, RankPermission.Armory, RankPermission.Leadership })
            {
                var perm = p;
                l.Toggle(PageUtil.Name(p), r.Has(p), v => RankSystem.TogglePermission(r, perm), PermissionHint(p));
            }

            l.Header("Members (" + RankSystem.MemberCount(f, r) + ")");
            foreach (var m in f.Members.Where(m => m.RankId == r.Id && m.Status != MemberStatus.Dead && m.Status != MemberStatus.Deserted))
            {
                var mm = m;
                l.Link(m.DisplayName, () => new MemberDetailPage(mm));
            }
            l.Action("Assign a member to this rank", () => PageUtil.PickMember(Ui, "Assign to " + r.Name, f.ActiveMembers.Where(m => m.RankId != r.Id), m => Report(RankSystem.SetRank(f, m, r, Mod.State.World.Day))));
            l.Spacer();
            l.Action("Delete rank", () => Ui.Confirm("Delete " + r.Name + "?", "Members holding it move to the next rank.", () => { Report(RankSystem.Delete(f, r)); Ui.Back(); }, true, "Delete"), danger: true);
        }

        private static string PermissionHint(RankPermission p)
        {
            switch (p)
            {
                case RankPermission.LeadSquad: return "Required to be appointed squad leader.";
                case RankPermission.Leadership: return "Marks leadership ranks (used in AI dialogue context and government appointments).";
                case RankPermission.Armory: return "Allowed to draw heavier loadouts.";
                case RankPermission.Negotiate: return "Can speak for the faction in sit-downs (flavor for dialogue).";
                default: return "Rank permission flag.";
            }
        }
    }

    internal sealed class SquadsPage : Page
    {
        public override string Title => Mod.Faction.Type == FactionType.Militia ? "Units" : "Squads";
        public override string Subtitle => "Independent groups with leaders, roles, vehicles, formations and orders.";

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            l.Header(f.Squads.Count + " / " + SquadSystem.MaxSquads);
            foreach (var s in f.Squads)
            {
                var sq = s;
                var leader = f.GetMember(s.LeaderId);
                string v = s.MemberIds.Count + " | " + PageUtil.Name(s.Role) + (s.AssignedTerritoryId != null ? " | " + Mod.State.World.GetTerritory(s.AssignedTerritoryId)?.Name : "");
                var it = l.Link(s.Name, () => new SquadDetailPage(sq), value: v);
                it.Hint = "Leader: " + (leader?.DisplayName ?? "none") + "\nMembers: " + string.Join(", ", SquadSystem.Members(f, s).Select(m => m.ShortName));
            }
            l.Header("Manage");
            l.Action("Create squad", () => Ui.AskText("Squad name", "", 32, s => Report(SquadSystem.Create(f, s, out _))), enabled: f.Squads.Count < SquadSystem.MaxSquads, disabledReason: "Maximum reached.");
            l.Action("Auto-assign squadless members", () =>
            {
                int n = 0;
                foreach (var m in f.ActiveMembers.Where(m => m.SquadId == null).ToList())
                {
                    var target = f.Squads.Where(s => s.MemberIds.Count < SquadSystem.MaxSquadSize).OrderBy(s => s.MemberIds.Count).FirstOrDefault();
                    if (target == null) break;
                    if (SquadSystem.AddMember(f, target, m).Ok) n++;
                }
                Ui.Notify.Push(n + " member(s) assigned.", EventSeverity.Info);
            });
        }
    }

    internal sealed class SquadDetailPage : Page
    {
        private readonly Squad _s;
        private OrderType _order = OrderType.Follow;
        public SquadDetailPage(Squad s) { _s = s; }
        public override string Title => _s.Name;

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            var s = _s;
            if (!f.Squads.Contains(s)) { l.Text("This squad no longer exists."); return; }
            l.Header("Orders");
            var orders = CommandSystem.PlayerIssuableOrders.Where(o => o != OrderType.Attack && o != OrderType.ExitVehicle).ToList();
            l.Choice("Order", orders, _order, o => _order = o, CommandSystem.Describe, "Choose an order, then execute it for the whole squad.");
            l.Action("Execute order", () => Report(Mod.Commands.OrderSquad(s, _order, _order == OrderType.MoveTo ? GtaUtil.GroundedWaypoint() : default(GTA.Math.Vector3))));
            l.Action("Attack my current target", () => Report(Mod.Commands.OrderSquad(s, OrderType.Attack, default(GTA.Math.Vector3), Mod.Commands.CurrentTarget())));
            l.Action("Summon squad to me", () => Report(Mod.Commands.SummonSquad(s)));
            l.Choice("Formation", PageUtil.Name(s.Formation), d => { s.Formation = PageUtil.Next(s.Formation, d); Mod.Members.ApplyFormation(s.Formation); },
                "Applied to everyone following you in the field.");

            l.Header("Configuration");
            l.Action("Rename", () => Ui.AskText("Squad name", s.Name, 32, n => Report(SquadSystem.Rename(f, s, n))), value: s.Name);
            l.Choice("Role", PageUtil.Name(s.Role), d => s.Role = PageUtil.Next(s.Role, d));
            var leader = f.GetMember(s.LeaderId);
            l.Action("Leader", () => PageUtil.PickMember(Ui, "Choose leader", SquadSystem.Members(f, s).Where(m => m.IsAvailable).Concat(f.ActiveMembers.Where(m => m.SquadId == null)), m => Report(SquadSystem.SetLeader(f, s, m))),
                "Leaders need the 'Lead squad' rank permission.", value: leader?.DisplayName ?? "none");
            var vehicles = new List<FactionVehicle> { null };
            vehicles.AddRange(f.Vehicles);
            l.Choice("Assigned vehicle", vehicles, f.GetVehicle(s.VehicleId), v => { s.VehicleId = v?.Id; if (v != null) v.AssignedSquadId = s.Id; }, v => v?.DisplayName ?? "None",
                "'Get in the vehicle' orders use this vehicle when it's spawned.");
            var territories = new List<Territory> { null };
            territories.AddRange(TerritorySystem.OwnedBy(Mod.State.World, FactionIds.Player));
            l.Choice("Garrison territory", territories, Mod.State.World.GetTerritory(s.AssignedTerritoryId), t => s.AssignedTerritoryId = t?.Id, t => t?.Name ?? "None",
                "A garrison raises influence over time and defends the territory against attacks while you're elsewhere.");
            l.Toggle("Guards headquarters", s.GuardsHeadquarters, v =>
            {
                s.GuardsHeadquarters = v;
                if (v) Report(Mod.Commands.AssignHqGuards(SquadSystem.Members(f, s)));
            }, "Posts this squad's members as HQ guards (up to the HQ guard limit).", f.Headquarters != null);

            l.Header("Members (" + s.MemberIds.Count + "/" + SquadSystem.MaxSquadSize + ")");
            foreach (var m in SquadSystem.Members(f, s).ToList())
            {
                var mm = m;
                var it = l.Action((s.LeaderId == m.Id ? "[Leader] " : "") + m.DisplayName, () => Ui.Confirm("Remove " + mm.ShortName + "?", "Remove from " + s.Name + "?", () => Report(SquadSystem.RemoveMember(f, s, mm))),
                    "Enter: remove from squad", value: (f.GetRank(m.RankId)?.Name ?? "") + " | " + m.Status);
                it.DrawDetail = (r, x, y, w, h) => MemberDetailPage.DrawCard(Ui, r, mm, x, y, w);
            }
            l.Action("Add member", () => PageUtil.PickMember(Ui, "Add to " + s.Name, f.Members.Where(m => m.Status != MemberStatus.Dead && m.Status != MemberStatus.Deserted && m.SquadId != s.Id), m => Report(SquadSystem.AddMember(f, s, m))),
                enabled: s.MemberIds.Count < SquadSystem.MaxSquadSize, disabledReason: "Squad is full.");
            l.Spacer();
            l.Action("Disband squad", () => Ui.Confirm("Disband " + s.Name + "?", "Members stay in the faction without a squad.", () => { Report(SquadSystem.Delete(f, s)); Ui.Back(); }, true, "Disband"), danger: true);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using GTA;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;
using OrganizationEmpire.World;

namespace OrganizationEmpire.UI.Pages
{
    /// <summary>Quick field orders (default F8): everyone near you, formations, squads, vehicles.</summary>
    internal sealed class QuickOrdersPage : Page
    {
        private int _squadIdx;
        private OrderType _squadOrder = OrderType.Follow;
        private int _vehicleIdx;

        public override string Title => "Field Orders";
        public override string Subtitle => Mod.Members.DeployedCount + " member(s) deployed";

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            var urgent = Mod.State.MissionBoard.FirstOrDefault(o => o.Urgent);
            if (urgent != null && !Mod.Missions.IsActive)
            {
                var def = MissionCatalog.Get(urgent.DefinitionId);
                l.Action("RESPOND: " + def?.Title, () => { Report(Mod.Missions.Start(urgent)); Ui.ClosePopup(); }, "An attack on your territory needs you.").Tint = Ui.Renderer.Theme.Bad;
            }
            l.Header("Everyone near me");
            foreach (var o in new[] { OrderType.Follow, OrderType.Stay, OrderType.Guard, OrderType.DefendPlayer, OrderType.Attack, OrderType.EnterVehicle, OrderType.ExitVehicle, OrderType.MoveTo, OrderType.ReturnToHQ, OrderType.Standby })
            {
                var order = o;
                string hint = order == OrderType.Attack ? (Mod.Commands.CurrentTarget() != null ? "Target locked." : "Aim at an enemy first (the last target is remembered for a few seconds).") :
                              order == OrderType.MoveTo ? (Game.IsWaypointActive ? "Moves to your waypoint." : "Set a map waypoint first.") : null;
                l.Action(CommandSystem.Describe(order), () => { Report(Mod.Commands.OrderField(order)); if (order != OrderType.Attack) Ui.ClosePopup(); }, hint);
            }
            l.Action("Call everyone to me", () => { Report(Mod.Commands.OrderMany(f.ActiveMembers.Take(Mod.Config.Spawning.MaxDeployedMembers), OrderType.Follow)); Ui.ClosePopup(); });
            var formations = ((FormationType[])System.Enum.GetValues(typeof(FormationType))).ToList();
            l.Choice("Formation", formations, Mod.Members.FieldFormation, fm => Mod.Members.ApplyFormation(fm), PageUtil.Name);

            if (f.Squads.Count > 0)
            {
                l.Header("Squads");
                _squadIdx = MathEx.Clamp(_squadIdx, 0, f.Squads.Count - 1);
                var sq = f.Squads[_squadIdx];
                l.Choice("Squad", sq.Name + " (" + sq.MemberIds.Count + ")", d => _squadIdx = (_squadIdx + d + f.Squads.Count) % f.Squads.Count);
                var orders = CommandSystem.PlayerIssuableOrders.Where(o => o != OrderType.ExitVehicle).ToList();
                l.Choice("Order", orders, _squadOrder, o => _squadOrder = o, CommandSystem.Describe);
                l.Action("Execute squad order", () =>
                {
                    var pos = _squadOrder == OrderType.MoveTo ? GtaUtil.GroundedWaypoint() : (_squadOrder == OrderType.Guard || _squadOrder == OrderType.DefendLocation || _squadOrder == OrderType.Stay ? GtaUtil.Player.Position : default(GTA.Math.Vector3));
                    Report(Mod.Commands.OrderSquad(sq, _squadOrder, pos, _squadOrder == OrderType.Attack ? Mod.Commands.CurrentTarget() : null));
                });
            }

            if (f.Vehicles.Count > 0)
            {
                l.Header("Vehicles");
                _vehicleIdx = MathEx.Clamp(_vehicleIdx, 0, f.Vehicles.Count - 1);
                var v = f.Vehicles[_vehicleIdx];
                l.Choice("Vehicle", v.DisplayName + (v.Destroyed ? " (repairing)" : Mod.Vehicles.IsSpawned(v) ? " (out)" : ""), d => _vehicleIdx = (_vehicleIdx + d + f.Vehicles.Count) % f.Vehicles.Count);
                l.Action("Call with driver", () => Report(Mod.Vehicles.CallWithDriver(v)), enabled: v.Available, disabledReason: "Being repaired.");
                l.Action("Spawn here", () => Report(Mod.Vehicles.SpawnNearPlayer(v)), enabled: v.Available && !Mod.Vehicles.IsSpawned(v), disabledReason: "Unavailable.");
            }

            if (Mod.Missions.IsActive)
            {
                l.Header("Mission");
                l.Action("Abort current mission", () => Ui.Confirm("Abort?", "Abort the mission? It counts as a failure.", () => { Mod.Missions.Abort("Aborted by the player", true); Ui.ClosePopup(); }, true, "Abort"), danger: true);
            }
            l.Header("More");
            l.Action("Open faction menu", () => { Ui.ClosePopup(); Ui.Open(); });
        }
    }

    /// <summary>Interaction with the NPC you're looking at: recruit, talk, profile, orders.</summary>
    internal sealed class InteractionPage : Page
    {
        private readonly Ped _ped;
        public InteractionPage(Ped ped) { _ped = ped; }

        public override string Title
        {
            get
            {
                var rt = Mod.Members.FindByPed(_ped);
                if (rt != null) return rt.Member.DisplayName;
                return GtaUtil.Valid(_ped) ? Mod.Interaction.ProfileFor(_ped).FullName : "Gone";
            }
        }

        public override string Subtitle
        {
            get
            {
                var rt = Mod.Members.FindByPed(_ped);
                if (rt != null) return (Mod.Faction.GetRank(rt.Member.RankId)?.Name ?? "") + " | " + rt.Member.Personality + " | loyalty " + (int)rt.Member.Loyalty;
                if (!GtaUtil.Valid(_ped)) return "";
                var p = Mod.Interaction.ProfileFor(_ped);
                return p.Age + " | " + p.Personality + (p.AffiliatedRivalId != null ? " | " + Mod.State.World.GetRival(p.AffiliatedRivalId)?.Name : "");
            }
        }

        public override void Build(ItemList l)
        {
            if (!GtaUtil.Alive(_ped)) { l.Text("They're gone."); l.Action("Close", () => Ui.ClosePopup()); return; }
            if (_ped.Position.DistanceTo(GtaUtil.Player.Position) > 25f) { l.Text("They're too far away now."); l.Action("Close", () => Ui.ClosePopup()); return; }
            var rt = Mod.Members.FindByPed(_ped);
            if (rt != null) BuildMember(l, rt);
            else BuildCivilian(l);
        }

        private void BuildCivilian(ItemList l)
        {
            var f = Mod.Faction;
            var p = Mod.Interaction.ProfileFor(_ped);
            string block = Mod.Interaction.RecruitBlockReason(_ped);
            l.Action("Talk", () => { Ui.ClosePopup(); Ui.Chat.OpenForCivilian(_ped); }, "Chat with them (AI dialogue). A good conversation can make them more willing to join.", Mod.Config.AI.AllowCivilianChat, disabledReason: "Disabled in settings.");
            var profileItem = l.Info("Profile", p.Personality + (p.SecondaryTrait != p.Personality ? " / " + p.SecondaryTrait : ""),
                p.Background + "\nFrom " + p.Hometown + ". Willingness: " + ContextBuilderWord(p.Willingness) + ". Fighting skill: " + ContextBuilderWord(p.Skill) + (p.RapportBonus > 0 ? ". Rapport bonus +" + (int)p.RapportBonus + "%." : "."));
            l.Header("Recruit");
            if (block != null) l.Text(block, Ui.Renderer.Theme.Warn);
            foreach (var ap in RecruitmentSystem.ApproachesFor(f.Type))
            {
                var approach = ap;
                var rc = RecruitmentSystem.ComputeChance(Mod.Sim, p, ap);
                string label = ApproachLabel(ap, f) + (ap == RecruitApproach.Pay ? " (" + TextUtil.Money(rc.Cost) + ")" : "");
                l.Action(label, () =>
                {
                    var res = Mod.Interaction.TryRecruit(_ped, approach);
                    Ui.Notify.Push(res.Message + (res.Roll > 0 ? " (" + TextUtil.Pct(res.Chance) + " chance)" : ""), res.Success ? EventSeverity.Good : EventSeverity.Warning);
                    if (res.Success) { Mod.Missions.OnRecruited(); Ui.ClosePopup(); }
                }, string.Join("\n", rc.Factors.Select(kv => kv.Key + ": " + (kv.Value >= 0 ? "+" : "") + (int)kv.Value)),
                   block == null && !rc.Blocked, rc.Blocked ? "-" : TextUtil.Pct(rc.Chance), disabledReason: block ?? rc.BlockReason);
            }
            l.Spacer();
            l.Action("Close", () => Ui.ClosePopup());
        }

        private static string ContextBuilderWord(float v) => Core.AI.ContextBuilder.LevelWord(v);

        private static string ApproachLabel(RecruitApproach ap, FactionState f)
        {
            switch (ap)
            {
                case RecruitApproach.Pay: return f.Type == FactionType.Mafia ? "Make an offer" : "Pay them";
                case RecruitApproach.Intimidate: return f.Type == FactionType.Mafia ? "An offer they can't refuse" : "Intimidate";
                case RecruitApproach.Ideology: return "Appeal to the cause";
                default: return f.Type == FactionType.Militia ? "Recruit for the movement" : f.Type == FactionType.Mafia ? "Invite them in" : "Put them on";
            }
        }

        private void BuildMember(ItemList l, MemberRuntime rt)
        {
            var m = rt.Member;
            var f = Mod.Faction;
            l.Action("Talk", () => { Ui.ClosePopup(); Ui.Chat.OpenForMember(m, rt.Ped); });
            l.Action("Open member file", () => { Ui.ClosePopup(); Ui.OpenAt("Members", new MemberDetailPage(m)); });
            l.Header("Orders");
            foreach (var o in new[] { OrderType.Follow, OrderType.DefendPlayer, OrderType.Stay, OrderType.Guard, OrderType.EnterVehicle, OrderType.ExitVehicle, OrderType.ReturnToHQ, OrderType.Standby })
            {
                var order = o;
                l.Action(CommandSystem.Describe(order), () => { Report(Mod.Commands.Order(m, order)); Ui.ClosePopup(); }, value: m.Order == order ? "current" : null);
            }
            l.Action("Attack my target", () => { Report(Mod.Commands.Order(m, OrderType.Attack, default(GTA.Math.Vector3), Mod.Commands.CurrentTarget())); Ui.ClosePopup(); });
            l.Header("Assign");
            var ranks = f.Ranks.Where(r => !r.ReservedForPlayer).OrderBy(r => r.Order).ToList();
            l.Choice("Rank", ranks, f.GetRank(m.RankId) ?? ranks.Last(), r => Report(RankSystem.SetRank(f, m, r, Mod.State.World.Day)), r => r.Name);
            l.Choice("Role", PageUtil.Name(m.Role), d => m.Role = PageUtil.Next(m.Role, d));
            var squads = new List<Squad> { null };
            squads.AddRange(f.Squads);
            l.Choice("Squad", squads, f.GetSquad(m.SquadId), s =>
            {
                if (s == null) { var old = f.GetSquad(m.SquadId); if (old != null) Report(SquadSystem.RemoveMember(f, old, m)); }
                else Report(SquadSystem.AddMember(f, s, m));
            }, s => s?.Name ?? "None");
            var loadouts = ((LoadoutType[])System.Enum.GetValues(typeof(LoadoutType))).ToList();
            l.Choice("Loadout", loadouts, m.Loadout, lt => Report(Mod.Commands.ChangeLoadout(m, lt)), lt => PageUtil.Name(lt) + " (" + LoadoutCatalog.Get(lt, f.Type).ArmsCost + " Arms)");
            l.Spacer();
            l.Action("Expel", () => Ui.Confirm("Expel " + m.DisplayName + "?", "They leave the faction for good.", () =>
            {
                rt.Ped.Task.WanderAround();
                Mod.Members.Despawn(rt, false);
                Report(MemberSystem.Expel(Mod.Sim, m));
                Ui.ClosePopup();
            }, true, "Expel"), danger: true);
        }
    }
}

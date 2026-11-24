using System;
using System.Collections.Generic;
using System.Drawing;
using Font = GTA.UI.Font;
using System.Linq;
using GTA;
using OrganizationEmpire.Native;
using OrganizationEmpire.Core.AI;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.World;

namespace OrganizationEmpire.UI.Pages
{
    internal sealed class MembersPage : Page
    {
        private enum Filter { All, Deployed, Available, Wounded, Squadless, Fallen }
        private Filter _filter;
        private enum Sort { Rank, Name, Loyalty, Skill }
        private Sort _sort;

        public override string Title => "Members";
        public override string Subtitle => Mod.Faction.LivingMemberCount + " members (capacity " + MemberSystem.RosterCapacity(Mod.Faction, Mod.Config.Gameplay.MaxRosterSize) + "), " + Mod.Members.DeployedCount + " deployed";

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            l.Choice("Filter", PageUtil.Name(_filter), d => { _filter = PageUtil.Next(_filter, d); Focus = 0; Scroll = 0; });
            l.Choice("Sort by", PageUtil.Name(_sort), d => _sort = PageUtil.Next(_sort, d));
            IEnumerable<Member> list = f.Members;
            switch (_filter)
            {
                case Filter.Deployed: list = list.Where(m => Mod.Members.IsDeployed(m)); break;
                case Filter.Available: list = list.Where(m => m.Status == MemberStatus.Active); break;
                case Filter.Wounded: list = list.Where(m => m.Status == MemberStatus.Wounded || m.Status == MemberStatus.Jailed); break;
                case Filter.Squadless: list = list.Where(m => m.SquadId == null && m.Status == MemberStatus.Active); break;
                case Filter.Fallen: list = list.Where(m => m.Status == MemberStatus.Dead || m.Status == MemberStatus.Deserted); break;
                default: list = list.Where(m => m.Status != MemberStatus.Dead && m.Status != MemberStatus.Deserted); break;
            }
            switch (_sort)
            {
                case Sort.Name: list = list.OrderBy(m => m.FirstName); break;
                case Sort.Loyalty: list = list.OrderByDescending(m => m.Loyalty); break;
                case Sort.Skill: list = list.OrderByDescending(m => m.Skill); break;
                default: list = list.OrderBy(m => f.GetRank(m.RankId)?.Order ?? 99).ThenBy(m => m.FirstName); break;
            }
            var members = list.ToList();
            l.Header(members.Count + " shown");
            if (members.Count == 0) l.Text("Nobody here. Recruit people from the street (look at someone and press " + Mod.Config.Hotkeys.Interact + ") or hire through your network on the Recruitment page.");
            foreach (var m in members)
            {
                var member = m;
                string status = Mod.Members.IsDeployed(m) ? "In the field" : m.Status == MemberStatus.Active ? CommandSystem.Describe(m.Order) : m.Status.ToString();
                var it = l.Link((m.IsDeputy ? "* " : "") + m.DisplayName, () => new MemberDetailPage(member), value: (f.GetRank(m.RankId)?.Name ?? "") + " | " + status);
                it.DrawDetail = (r, x, y, w, h) => MemberDetailPage.DrawCard(Ui, r, member, x, y, w);
                if (m.Status != MemberStatus.Active) it.Tint = Ui.Renderer.Theme.TextFaint;
            }
        }
    }

    internal sealed class MemberDetailPage : Page
    {
        private readonly Member _m;
        public MemberDetailPage(Member m) { _m = m; }

        public override string Title => _m.DisplayName;
        public override string Subtitle => (Mod.Faction.GetRank(_m.RankId)?.Name ?? "") + " | " + PageUtil.Name(_m.Role) + " | " + _m.Status;

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            var m = _m;
            if (!f.Members.Contains(m)) { l.Text("This member is no longer part of the faction."); return; }
            bool available = m.IsAvailable;
            bool deployed = Mod.Members.IsDeployed(m);

            l.Header("Orders");
            foreach (var o in new[] { OrderType.Follow, OrderType.DefendPlayer, OrderType.Stay, OrderType.Guard, OrderType.DefendLocation, OrderType.MoveTo, OrderType.EnterVehicle, OrderType.ReturnToHQ, OrderType.Standby })
            {
                var order = o;
                l.Action(CommandSystem.Describe(order), () => Report(Mod.Commands.Order(m, order)), OrderHint(order), available, m.Order == order ? "current" : null, disabledReason: m.ShortName + " is " + m.Status.ToString().ToLowerInvariant() + ".");
            }
            l.Action("Attack my current target", () => Report(Mod.Commands.Order(m, OrderType.Attack, default(GTA.Math.Vector3), Mod.Commands.CurrentTarget())),
                "Aim at an enemy within the last few seconds, then use this.", available && deployed, disabledReason: "Member must be with you.");
            l.Action("Talk (AI dialogue)", () => { Ui.Close(); Ui.Chat.OpenForMember(m, Mod.Members.Get(m)?.Ped); }, "Open a conversation. Works over the radio if they're far away.", available);

            l.Header("Assignment");
            var ranks = f.Ranks.Where(r => !r.ReservedForPlayer).OrderBy(r => r.Order).ToList();
            l.Choice("Rank", ranks, f.GetRank(m.RankId) ?? ranks.Last(), r =>
            {
                int oldOrder = f.GetRank(m.RankId)?.Order ?? 99;
                var res = RankSystem.SetRank(f, m, r, Mod.State.World.Day);
                if (res.Ok) MemoryStore.AddEntry(Mod.State, m.Id, m.FullName, r.Order < oldOrder ? MemoryKind.Promotion : MemoryKind.Demotion,
                    "Rank changed to " + r.Name + ".", 0.8f, Mod.State.World.Day, Mod.Config.AI.MaxMemoryEntries);
                Report(res);
            }, r => r.Name, "Promotion raises loyalty and morale; demotion hurts them (especially ambitious and rebellious members).");
            l.Action("Promote", () => { var res = RankSystem.Promote(f, m, Mod.State.World.Day); if (res.Ok) MemoryStore.AddEntry(Mod.State, m.Id, m.FullName, MemoryKind.Promotion, res.Message, 0.85f, Mod.State.World.Day, Mod.Config.AI.MaxMemoryEntries); Report(res); });
            l.Action("Demote", () => { var res = RankSystem.Demote(f, m, Mod.State.World.Day); if (res.Ok) MemoryStore.AddEntry(Mod.State, m.Id, m.FullName, MemoryKind.Demotion, res.Message, 0.85f, Mod.State.World.Day, Mod.Config.AI.MaxMemoryEntries); Report(res); });
            l.Choice("Role", PageUtil.Name(m.Role), d => m.Role = PageUtil.Next(m.Role, d),
                "Earners add income, Scouts gather intel, Medics help recovery, Guards suit the HQ, Drivers can be assigned to vehicles.");
            var squads = new List<Squad> { null };
            squads.AddRange(f.Squads);
            l.Choice("Squad", squads, f.GetSquad(m.SquadId), s =>
            {
                if (s == null) { var old = f.GetSquad(m.SquadId); if (old != null) Report(SquadSystem.RemoveMember(f, old, m)); }
                else Report(SquadSystem.AddMember(f, s, m));
            }, s => s?.Name ?? "None");
            var loadouts = ((LoadoutType[])Enum.GetValues(typeof(LoadoutType))).ToList();
            l.Choice("Loadout", loadouts, m.Loadout, lt => Report(Mod.Commands.ChangeLoadout(m, lt)), lt => PageUtil.Name(lt) + " (" + LoadoutCatalog.Get(lt, f.Type).ArmsCost + " Arms)",
                "Better weapons cost Arms and unlock with faction level. Armory upgrades reduce the cost.");
            l.Toggle("Deputy", m.IsDeputy, v => Report(MemberSystem.SetDeputy(f, m, v)));
            l.Toggle("Headquarters guard", f.Headquarters?.GuardMemberIds.Contains(m.Id) ?? false, v =>
            {
                if (f.Headquarters == null) { Ui.Notify.Push("No headquarters.", EventSeverity.Warning); return; }
                var guards = f.Headquarters.GuardMemberIds.Select(f.GetMember).Where(x => x != null).ToList();
                if (v) guards.Add(m); else { guards.Remove(m); m.Order = OrderType.Standby; }
                Report(Mod.Commands.AssignHqGuards(guards));
            }, "Guards stand watch at the HQ and appear whenever you're nearby.", f.Headquarters != null);

            l.Header("Personal");
            l.Action("Rename", () => Ui.AskText("First name", m.FirstName, 20, s => m.FirstName = s));
            l.Action("Set nickname", () => Ui.AskText("Nickname", m.Nickname, 24, s => m.Nickname = s));
            l.Action("Clear AI memory", () => Ui.Confirm("Clear memory?", "Forget all conversations and memories of " + m.ShortName + "?", () => { MemoryStore.Clear(Mod.State, m.Id); Ui.Notify.Push("Memory cleared.", EventSeverity.Info); }),
                "Memories: " + (Mod.State.Memories.TryGetValue(m.Id, out var mem) ? mem.Entries.Count + " entries, " + mem.Dialogue.Count + " dialogue lines" : "none"));
            l.Action("Expel from the faction", () => Ui.Confirm("Expel " + m.DisplayName + "?", "They leave the faction for good. Disloyal ex-members may talk.", () =>
            {
                var rt = Mod.Members.Get(m);
                if (rt != null) { rt.Ped.Task.WanderAround(); Mod.Members.Despawn(rt, false); }
                Report(MemberSystem.Expel(Mod.Sim, m));
                Mod.FlushNotifications();
                Ui.Back();
            }, true, "Expel"), danger: true);
        }

        private static string OrderHint(OrderType o)
        {
            switch (o)
            {
                case OrderType.Follow: return "Join you in the field. Up to 7 use the game's group AI (they follow, fight and ride with you); extras follow in formation.";
                case OrderType.DefendPlayer: return "Follow and actively engage anyone fighting you.";
                case OrderType.Stay: return "Hold position where they stand.";
                case OrderType.Guard: return "Guard your current position and react to threats.";
                case OrderType.DefendLocation: return "Defend the area around your current position aggressively.";
                case OrderType.MoveTo: return "Move to your map waypoint and hold there.";
                case OrderType.EnterVehicle: return "Get into your current (or last) vehicle.";
                case OrderType.ReturnToHQ: return "Head back to the headquarters.";
                case OrderType.Standby: return "Go home / off duty (despawns when out of sight).";
                default: return null;
            }
        }

        public override void DrawDetail(Renderer r, float x, float y, float w, float h, Item focused)
        {
            float used = DrawCard(Ui, r, _m, x, y, w);
            if (focused?.Hint != null)
            {
                r.Rect(x, y + used + 8, w, 1, r.Theme.Line);
                r.Paragraph(focused.Hint, x, y + used + 18, Theme.SmallSize, r.Theme.TextDim, w);
            }
        }

        /// <summary>Profile card used on several pages. Returns height used.</summary>
        public static float DrawCard(UiManager ui, Renderer r, Member m, float x, float y, float w)
        {
            var t = r.Theme;
            var f = ui.Mod.Faction;
            r.Text(m.DisplayName.ToUpperInvariant(), x, y, Theme.HeadSize, t.Text, Theme.Condensed);
            r.Text((f.GetRank(m.RankId)?.Name ?? "") + (m.IsDeputy ? " (Deputy)" : "") + "  |  " + PageUtil.Name(m.Role) + (m.GovernmentRole != null ? "  |  " + m.GovernmentRole : ""), x, y + 38, Theme.SmallSize, t.Accent);
            r.Text((m.Female ? "Female" : "Male") + ", " + m.Age + ", from " + m.Hometown, x, y + 64, Theme.SmallSize, t.TextDim);
            r.Text("Personality: " + m.Personality + (m.SecondaryTrait != m.Personality ? " / " + m.SecondaryTrait : ""), x, y + 90, Theme.SmallSize, t.TextDim);
            float cy = y + 122;
            float half = (w - 20) / 2;
            PageUtil.Stat(r, x, cy, half, "LOYALTY", m.Loyalty, PageUtil.LevelColor(t, m.Loyalty));
            PageUtil.Stat(r, x + half + 20, cy, half, "MORALE", m.Morale, PageUtil.LevelColor(t, m.Morale));
            cy += 40;
            PageUtil.Stat(r, x, cy, half, "SKILL", m.Skill, t.Info);
            PageUtil.Stat(r, x + half + 20, cy, half, "DISCIPLINE", m.Discipline, t.Info);
            cy += 46;
            var squad = f.GetSquad(m.SquadId);
            string where = ui.Mod.Members.IsDeployed(m) ? "In the field" : m.Status == MemberStatus.Active ? "Off-map" : m.Status + (m.RecoveryHours > 0 ? " (" + (int)Math.Ceiling(m.RecoveryHours) + "h)" : "");
            r.Text("Status: " + where + "   Orders: " + CommandSystem.Describe(m.Order), x, cy, Theme.SmallSize, t.Text); cy += 26;
            r.Text("Squad: " + (squad?.Name ?? "none") + "   Loadout: " + PageUtil.Name(m.Loadout), x, cy, Theme.SmallSize, t.Text); cy += 26;
            r.Text("Kills: " + m.Kills + "   Missions: " + m.MissionsCompleted + "   Joined: day " + m.JoinedDay, x, cy, Theme.SmallSize, t.Text); cy += 32;
            cy += r.Paragraph(m.Background ?? "", x, cy, Theme.SmallSize, t.TextDim, w, Theme.Body, 3) + 8;
            var rels = m.Opinions.OrderByDescending(kv => Math.Abs(kv.Value)).Take(3).Select(kv => (f.GetMember(kv.Key)?.ShortName ?? "?") + ": " + ContextBuilder.OpinionWord(kv.Value)).ToList();
            if (rels.Count > 0) { r.Text("Relationships: " + string.Join(", ", rels), x, cy, Theme.SmallSize, t.TextDim); cy += 28; }
            return cy - y;
        }
    }

    internal sealed class RecruitmentPage : Page
    {
        public override string Title => "Recruitment";
        public override string Subtitle => "Look at an NPC and press " + Mod.Config.Hotkeys.Interact + " to approach them. Hire through your network below.";

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            int cap = MemberSystem.RosterCapacity(f, Mod.Config.Gameplay.MaxRosterSize);
            l.Header("Roster");
            l.Progress("Roster", f.LivingMemberCount / (float)Math.Max(1, cap), f.LivingMemberCount + " / " + cap, null,
                "Capacity grows with faction level and HQ Barracks, up to the MaxRosterSize setting.");
            l.Info("Recruited so far", f.Stats.MembersRecruited.ToString());

            l.Header("Hire through your network");
            long hireCost = f.Recruitment.HireCost + f.Level * 150;
            l.Action("Hire a new recruit", () =>
            {
                var r = RecruitmentSystem.Hire(Mod.Sim, Mod.Rng, out var m);
                Mod.FlushNotifications();
                if (r.Ok && m != null)
                {
                    Mod.Members.Summon(m);
                    Mod.Missions.OnRecruited();
                }
                Report(r);
            }, "A fresh recruit in faction uniform arrives at your position. Cost scales with level.", value: TextUtil.Money(hireCost));

            l.Header("Policy");
            l.Toggle("Recruitment open", f.Recruitment.Open, v => f.Recruitment.Open = v, "Close recruitment to stop accepting new members.");
            l.Toggle("Accept female recruits", f.Recruitment.AllowFemaleRecruits, v => f.Recruitment.AllowFemaleRecruits = v);
            l.Slider("Minimum willingness", f.Recruitment.MinimumWillingness, 0, 80, 5, v => f.Recruitment.MinimumWillingness = v, "0", "Skip candidates who are less willing than this.");
            l.Choice("Default role for recruits", PageUtil.Name(f.Recruitment.DefaultRole), d => f.Recruitment.DefaultRole = PageUtil.Next(f.Recruitment.DefaultRole, d));
            l.Toggle("Recruits wear the uniform", f.Identity.ApplyUniformToRecruits, v => f.Identity.ApplyUniformToRecruits = v,
                "Swap recruited NPCs to your uniform model (keeps their identity).");
            l.Toggle("World recruitment enabled (global)", Mod.Config.Gameplay.RecruitmentEnabled, v => { Mod.Config.Gameplay.RecruitmentEnabled = v; Mod.SaveConfig(); });
            l.Toggle("Allow recruiting law enforcement", Mod.Config.Gameplay.AllowRecruitingLawEnforcement, v => { Mod.Config.Gameplay.AllowRecruitingLawEnforcement = v; Mod.SaveConfig(); },
                "Not recommended: may interfere with police behavior.");

            l.Header("Current target");
            var target = Mod.Interaction.Target;
            if (target == null || Mod.Interaction.TargetMember != null) l.Text("Look at a nearby person (outside the menu) to see their recruitment odds here.");
            else
            {
                var p = Mod.Interaction.ProfileFor(target);
                string block = Mod.Interaction.IneligibleReason;
                l.Info("Candidate", p.FullName + ", " + p.Age, p.Background);
                l.Info("Personality", p.Personality.ToString());
                if (block != null) l.Info("Status", block, null, Ui.Renderer.Theme.Warn);
                foreach (var ap in RecruitmentSystem.ApproachesFor(f.Type))
                {
                    var rc = RecruitmentSystem.ComputeChance(Mod.Sim, p, ap);
                    l.Info("Approach: " + ap, rc.Blocked ? "blocked" : TextUtil.Pct(rc.Chance), string.Join("\n", rc.Factors.Select(kv => kv.Key + ": " + (kv.Value >= 0 ? "+" : "") + (int)kv.Value)));
                }
            }

            l.Header("Recent recruits");
            foreach (var m in f.Members.OrderByDescending(x => x.JoinedDay).Take(6))
            {
                var mm = m;
                l.Link(m.DisplayName, () => new MemberDetailPage(mm), value: "day " + m.JoinedDay);
            }
        }
    }
}

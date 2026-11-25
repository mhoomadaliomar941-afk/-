using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Font = GTA.UI.Font;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Integration;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Systems;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.World;
using OrganizationEmpire.Native;

namespace OrganizationEmpire.UI.Pages
{
    /// <summary>The living world at a glance: battles under way, every faction with its leader and plans, and the news.</summary>
    internal sealed class WorldPage : Page
    {
        public override string Title => "World";
        public override string Subtitle
        {
            get
            {
                var w = Mod.State.World;
                return LivingWorldSystem.Fmt("{0} factions active, {1} battles under way", w.Rivals.Count(r => r.Active), w.Living?.ActiveBattles.Count() ?? 0);
            }
        }

        public override void Build(ItemList l)
        {
            var w = Mod.State.World;
            var lw = w.Living;
            if (!Mod.Config.World.LivingWorld)
                l.Text("The living world is switched off (Settings > World & factions). Rival factions only react to you.", Ui.Renderer.Theme.Warn);

            var battles = lw?.ActiveBattles.ToList() ?? new List<WorldBattle>();
            l.Header("Battles under way");
            if (battles.Count == 0) l.Text("No fighting right now.");
            foreach (var b in battles)
            {
                var bb = b;
                var t = w.GetTerritory(b.TerritoryId);
                var it = l.Action(LivingWorldSystem.Fmt("{0} vs {1}", w.FactionName(b.AttackerId, Mod.Faction), b.DefenderId == null ? "Locals" : w.FactionName(b.DefenderId, Mod.Faction)),
                    () => SetWaypoint(t),
                    "Set a waypoint. Near you the fight happens for real: kill one side to help the other.",
                    value: t?.Name);
                it.DrawDetail = (r, x, y, ww, h) => DrawBattle(r, bb, x, y, ww);
            }

            l.Header("Factions");
            foreach (var r in w.Rivals.Where(x => x.Active).OrderByDescending(x => x.Strength))
            {
                var rv = r;
                string leader = r.Leader != null ? r.Leader.Title + " " + r.Leader.Name : "?";
                var it = l.Link(r.Name, () => new WorldFactionPage(rv), value: LivingWorldSystem.GoalText(r.Goal));
                it.Swatch = Rgb.Parse(r.Color, new Rgb(150, 150, 150));
                it.Hint = leader;
                it.DrawDetail = (rr, x, y, ww, h) => WorldFactionPage.DrawCard(Ui, rr, rv, x, y, ww);
            }
            var gone = w.Rivals.Where(x => x.Enabled && x.Eliminated).ToList();
            if (gone.Count > 0) l.Info("Collapsed", string.Join(", ", gone.Select(x => x.Name)));

            l.Header("World news");
            var news = lw?.News ?? new List<WorldNews>();
            if (news.Count == 0) l.Text("Nothing has happened yet. The world moves every strategic hour.");
            foreach (var n in Enumerable.Reverse(news).Take(25))
                l.Text("D" + n.Day + " " + n.Hour.ToString("00") + "h  " + n.Text, n.Kind == WorldNewsKind.War || n.Kind == WorldNewsKind.Leader ? Ui.Renderer.Theme.Warn : (Color?)null);

            if (lw != null)
            {
                l.Header("Statistics");
                l.Info("Battles fought", lw.BattlesFought.ToString());
                l.Info("Leaders fallen", lw.LeadersFallen.ToString());
            }
        }

        private void SetWaypoint(Territory t)
        {
            if (t == null) return;
            GTA.World.WaypointPosition = t.Center.ToVector3OE();
            Ui.Notify.Push(LivingWorldSystem.Fmt("Waypoint set to {0}.", t.Name), EventSeverity.Info);
        }

        private void DrawBattle(Renderer r, WorldBattle b, float x, float y, float w)
        {
            var t = r.Theme;
            var world = Mod.State.World;
            var terr = world.GetTerritory(b.TerritoryId);
            r.Text((terr?.Name ?? "?").ToUpperInvariant(), x, y, Theme.HeadSize, t.Text, Theme.Condensed);
            float cy = y + 44;
            var att = world.GetRival(b.AttackerId);
            var def = world.GetRival(b.DefenderId);
            Color ca = att != null ? Rgb.Parse(att.Color, new Rgb(200, 80, 80)).ToColor() : t.Bad;
            Color cd = def != null ? Rgb.Parse(def.Color, new Rgb(80, 160, 220)).ToColor() : t.Info;
            PageUtil.Stat(r, x, cy, w, (att?.Name ?? "?").ToUpperInvariant(), b.AttackerStart <= 0 ? 0 : 100f * b.AttackerForce / b.AttackerStart, ca, ((int)Math.Ceiling(b.AttackerForce)).ToString());
            cy += 44;
            PageUtil.Stat(r, x, cy, w, (def?.Name ?? "Locals").ToUpperInvariant(), b.DefenderStart <= 0 ? 0 : 100f * b.DefenderForce / b.DefenderStart, cd, ((int)Math.Ceiling(b.DefenderForce)).ToString());
            cy += 50;
            r.Text(LivingWorldSystem.Fmt("Hour {0} of up to {1}", b.Hours, b.MaxHours), x, cy, Theme.SmallSize, t.TextDim);
            cy += 28;
            if (b.LiveAttackerLosses + b.LiveDefenderLosses > 0)
                r.Text(LivingWorldSystem.Fmt("Fallen in the street: {0} / {1}", b.LiveAttackerLosses, b.LiveDefenderLosses), x, cy, Theme.SmallSize, t.Text);
            if (b.PlayerSide != null)
                r.Text(b.PlayerSide == "attacker" ? "You are fighting for the attackers." : "You are fighting for the defenders.", x, cy + 28, Theme.SmallSize, t.Accent);
        }
    }

    /// <summary>One faction of the living world: leader, lieutenants, resources, plans, relations and news.</summary>
    internal sealed class WorldFactionPage : Page
    {
        private readonly RivalFaction _r;
        public WorldFactionPage(RivalFaction r) { _r = r; }

        public override string Title => _r.Name;
        public override string Subtitle => PageUtil.Name(_r.Kind) + " | " + PageUtil.Name(_r.Temperament) + (_r.External ? " | " + LivingWorldSystem.Fmt("added by {0}", _r.ExternalSource ?? "?") : "");

        public override void Build(ItemList l)
        {
            var w = Mod.State.World;
            var r = _r;
            var th = Ui.Renderer.Theme;
            l.Header("Leadership");
            if (r.Leader != null)
            {
                var lead = l.Info(r.Leader.Title, r.Leader.Name, LivingWorldSystem.Fmt("Age {0}, in charge since day {1}.", r.Leader.Age, r.Leader.SinceDay));
                lead.DrawDetail = (rr, x, y, ww, h) => DrawLeader(rr, r.Leader, x, y, ww);
            }
            foreach (var lt in r.Lieutenants)
            {
                var ltc = lt;
                var it = l.Info(lt.Title, lt.Name, LivingWorldSystem.Fmt("Loyalty to the leader: {0}", (int)lt.Loyalty));
                it.DrawDetail = (rr, x, y, ww, h) => DrawLeader(rr, ltc, x, y, ww);
            }
            if (Mod.Config.World.RivalLeadersInWorld && r.HomeTerritories.Count > 0)
                l.Text("The leader is sometimes seen with bodyguards at the faction's home turf.");

            l.Header("Resources");
            l.Progress("Members", MathEx.Clamp01(r.Manpower / 120f), r.Manpower.ToString(), th.Warn);
            l.Progress("Wealth", r.Wealth / 100f, ((int)r.Wealth).ToString(), th.Good);
            l.Progress("Arms", r.Arms / 100f, ((int)r.Arms).ToString(), th.Bad);
            l.Progress("Morale", r.Morale / 100f, ((int)r.Morale).ToString(), th.Info);
            l.Info("Current plan", LivingWorldSystem.GoalText(r.Goal), TargetName(r));
            var turf = w.Territories.Where(t => t.OwnerId == r.Id).ToList();
            l.Info("Territories held", turf.Count.ToString(), string.Join(", ", turf.Select(t => t.Name)));
            if (turf.Count > 0)
                l.Action("Set waypoint to their turf", () =>
                {
                    var home = turf.FirstOrDefault(t => r.HomeTerritories.Contains(t.Id)) ?? turf[0];
                    GTA.World.WaypointPosition = home.Center.ToVector3OE();
                    Ui.Notify.Push(LivingWorldSystem.Fmt("Waypoint set to {0}.", home.Name), EventSeverity.Info);
                });

            l.Header("Relations with other factions");
            var rels = w.Living?.Relations.Where(x => x.Involves(r.Id) && (w.GetRival(x.Other(r.Id))?.Active ?? false))
                .OrderByDescending(x => x.Status == RelationStatus.War ? 3 : x.Status == RelationStatus.Alliance ? 2 : x.Status == RelationStatus.Rivalry ? 1 : 0)
                .ThenByDescending(x => x.Hostility).ToList() ?? new List<FactionRelation>();
            foreach (var rel in rels.Take(12))
            {
                var other = w.GetRival(rel.Other(r.Id));
                var it = l.Info(other.Name, PageUtil.Name(rel.Status), LivingWorldSystem.Fmt("Hostility {0}, since day {1}.", (int)rel.Hostility, rel.SinceDay),
                    rel.Status == RelationStatus.War ? th.Bad : rel.Status == RelationStatus.Alliance ? th.Good : (Color?)null);
                it.Swatch = Rgb.Parse(other.Color, new Rgb(150, 150, 150));
            }
            if (Mod.HasFaction)
            {
                l.Info("Toward you", PageUtil.Name(r.Relation), LivingWorldSystem.Fmt("Hostility {0}", (int)r.Hostility));
                l.Link("Diplomacy with this faction", () => new RivalDetailPage(r));
            }

            var news = w.Living?.News.Where(n => n.FactionIds.Contains(r.Id)).ToList() ?? new List<WorldNews>();
            if (news.Count > 0)
            {
                l.Header("Recent news");
                foreach (var n in Enumerable.Reverse(news).Take(8)) l.Text("D" + n.Day + " " + n.Hour.ToString("00") + "h  " + n.Text);
            }
        }

        private string TargetName(RivalFaction r)
        {
            if (r.GoalTargetId == null) return null;
            var w = Mod.State.World;
            return w.GetTerritory(r.GoalTargetId)?.Name ?? w.GetRival(r.GoalTargetId)?.Name;
        }

        private static void DrawLeader(Renderer r, RivalLeader lead, float x, float y, float w)
        {
            var t = r.Theme;
            r.Text(lead.Name.ToUpperInvariant(), x, y, Theme.HeadSize, t.Text, Theme.Condensed);
            r.Text(lead.Title + " | " + LivingWorldSystem.Fmt("Age {0}", lead.Age), x, y + 38, Theme.SmallSize, t.Accent);
            float cy = y + 76;
            float half = (w - 20) / 2;
            PageUtil.Stat(r, x, cy, half, "AMBITION", lead.Ambition * 100, t.Warn);
            PageUtil.Stat(r, x + half + 20, cy, half, "CAUTION", lead.Caution * 100, t.Info);
            cy += 44;
            PageUtil.Stat(r, x, cy, half, "BRUTALITY", lead.Brutality * 100, t.Bad);
            PageUtil.Stat(r, x + half + 20, cy, half, "CHARISMA", lead.Charisma * 100, t.Good);
            cy += 54;
            r.Paragraph("Ambitious leaders expand and start wars, cautious ones defend and make peace, brutal ones retaliate harder, charismatic ones recruit more and find allies.", x, cy, Theme.SmallSize, t.TextDim, w);
        }

        public static void DrawCard(UiManager ui, Renderer r, RivalFaction rv, float x, float y, float w)
        {
            var t = r.Theme;
            var c = Rgb.Parse(rv.Color, new Rgb(150, 150, 150)).ToColor();
            r.Rect(x, y, 6, 36, c);
            r.Text(rv.Name.ToUpperInvariant(), x + 16, y, Theme.HeadSize, t.Text, Theme.Condensed);
            r.Text(PageUtil.Name(rv.Kind) + " | " + PageUtil.Name(rv.Temperament), x + 16, y + 38, Theme.SmallSize, c);
            float cy = y + 76;
            if (rv.Leader != null) { r.Text(rv.Leader.Title + ": " + rv.Leader.Name, x, cy, Theme.BodySize, t.Text); cy += 32; }
            float half = (w - 20) / 2;
            PageUtil.Stat(r, x, cy, half, "MEMBERS", MathEx.Clamp(rv.Manpower / 1.2f, 0, 100), t.Warn, rv.Manpower.ToString());
            PageUtil.Stat(r, x + half + 20, cy, half, "WEALTH", rv.Wealth, t.Good);
            cy += 44;
            PageUtil.Stat(r, x, cy, half, "ARMS", rv.Arms, t.Bad);
            PageUtil.Stat(r, x + half + 20, cy, half, "MORALE", rv.Morale, t.Info);
            cy += 50;
            var world = ui.Mod.State.World;
            int held = world.Territories.Count(tt => tt.OwnerId == rv.Id);
            r.Text(LivingWorldSystem.Fmt("Territories held: {0}", held), x, cy, Theme.SmallSize, t.Text);
            cy += 28;
            var wars = LivingWorldSystem.WarsOf(world, rv.Id).Select(x2 => world.GetRival(x2.Other(rv.Id))?.Name).Where(n => n != null).ToList();
            if (wars.Count > 0) { r.Paragraph(LivingWorldSystem.Fmt("At war with {0}", string.Join(", ", wars)), x, cy, Theme.SmallSize, t.Bad, w); cy += 28; }
            r.Text(LivingWorldSystem.GoalText(rv.Goal), x, cy, Theme.SmallSize, t.Accent);
        }
    }

    /// <summary>Settings &gt; Mods &amp; integrations: detected mods, compatibility issues and the optional integrations.</summary>
    internal sealed class ModsPage : ConfigPage
    {
        public override string Title => "Mods & Integrations";
        public override string Subtitle
        {
            get
            {
                var im = Mod.Integrations;
                if (im.Scanning) return "Scanning installed mods...";
                var scan = im.Scan;
                if (scan == null) return "Not scanned yet.";
                return LivingWorldSystem.Fmt("{0} mods detected | {1} issue(s)", scan.Mods.Count, scan.Issues.Count(i => i.Severity != IssueSeverity.Info));
            }
        }

        public override void Build(ItemList l)
        {
            var im = Mod.Integrations;
            var ic = C.Integrations;
            var th = Ui.Renderer.Theme;
            l.Toggle("Mod integration", ic.Enabled, v => { ic.Enabled = v; Save(); im.Reanalyze(); }, "Detect other mods and run the optional integrations. Other mods' files are only read, never changed.");
            l.Action("Rescan installed mods", () => { im.Rescan(); Ui.Notify.Push("Scanning installed mods...", EventSeverity.Info); }, enabled: !im.Scanning && ic.Enabled);
            var scan = im.Scan;

            var issues = scan?.Issues ?? new List<CompatIssue>();
            l.Header(LivingWorldSystem.Fmt("Compatibility ({0})", issues.Count));
            if (scan != null && issues.Count == 0) l.Text("No conflicts found.", th.Good);
            foreach (var issue in issues.OrderByDescending(i => i.Severity))
            {
                var iss = issue;
                Color tint = issue.Severity == IssueSeverity.Error ? th.Bad : issue.Severity == IssueSeverity.Warning ? th.Warn : th.TextDim;
                if (issue.Fix != null)
                    l.Action(issue.FixLabel, () => Ui.Confirm(issue.FixLabel + "?", issue.Text + "\n\nOnly Organization Empire's own settings are changed.",
                        () => Report(im.ApplyFix(iss) ? Core.Systems.OpResult.Success("Done.") : Core.Systems.OpResult.Fail("Could not apply."))), issue.Text, value: PageUtil.Name(issue.Severity));
                else l.Text(issue.Text, tint);
            }

            l.Header("Integrations");
            foreach (var st in im.Host.States)
            {
                var s = st;
                bool userOn = !im.Host.IsDisabledByUser(s.Module.Id);
                string status = PageUtil.Name(s.Status) + (s.Module.Detail != null && s.Status == Core.Integration.IntegrationStatus.Active ? " | " + s.Module.Detail : "");
                string hint = PageUtil.Name(s.Module.Level) + ". " + s.Module.Description + (s.Reason != null ? "\n" + s.Reason : "");
                var it = l.Choice(s.Module.Name, status, d =>
                {
                    if (!userOn && s.Module.Warning != null)
                        Ui.Confirm("Enable " + s.Module.Name + "?", s.Module.Warning, () => SetEnabled(s.Module.Id, true));
                    else SetEnabled(s.Module.Id, !userOn);
                }, hint);
                if (s.Status == Core.Integration.IntegrationStatus.Failed) it.Tint = th.Bad;
                else if (s.Status != Core.Integration.IntegrationStatus.Active) it.Tint = th.TextFaint;
            }
            l.Choice("Coexistence mode", PageUtil.Name(ic.Coexistence), d => { ic.Coexistence = PageUtil.Next(ic.Coexistence, d); Save(); im.Reanalyze(); },
                "Auto: on when a gang/war mod is detected. Fewer battle fighters, no ambient crews.");
            l.Toggle("Allow other scripts to change the game (API)", ic.AllowExternalCommands, v =>
            {
                if (v) Ui.Confirm("Allow external commands?", "Other installed scripts will be able to add factions, show notifications and report kills through Organization Empire's API. Only enable this for mods you trust.",
                    () => { ic.AllowExternalCommands = true; Save(); });
                else { ic.AllowExternalCommands = false; Save(); }
            }, "Reading data and events is always allowed while the API is on.");

            var mods = scan?.Mods ?? new List<DetectedMod>();
            l.Header(LivingWorldSystem.Fmt("Detected mods ({0})", mods.Count));
            foreach (var m in mods.OrderBy(x => x.Known ? 0 : 1).ThenBy(x => x.Category).ThenBy(x => x.Name))
            {
                string hint = PageUtil.Name(m.Kind) + " | " + PageUtil.Name(m.Category) + " | " + PageUtil.Name(m.Level) + "\n"
                    + (m.Catalog != null ? m.Catalog.Integration + "\n" + LivingWorldSystem.Fmt("License: {0}", m.Catalog.License) + "\n" : "Not in the catalog: shown so you know it is running.\n")
                    + LivingWorldSystem.Fmt("Found: {0}", m.Evidence);
                var it = l.Info(m.Name, m.Version ?? "", hint);
                if (!m.Known) it.Tint = th.TextDim;
            }

            if (im.Host.RecentLog.Count > 0)
            {
                l.Header("Integration log");
                foreach (var line in im.Host.RecentLog.Reverse().Take(10)) l.Text(line, th.TextFaint);
            }
        }

        private void SetEnabled(string id, bool enabled)
        {
            Mod.Integrations.Host.SetEnabledByUser(id, enabled);
            Save();
            Mod.Integrations.Reanalyze();
        }
    }

    /// <summary>Headquarters sites taken from installed map mods (Map Editor / Menyoo).</summary>
    internal sealed class MapLocationsPage : Page
    {
        public override string Title => "Map Locations";
        public override string Subtitle => "From installed Map Editor / Menyoo maps (files are only read).";

        public override void Build(ItemList l)
        {
            var maps = Mod.Integrations.Maps;
            if (maps.Count == 0) { l.Text("No map files found."); return; }
            l.Text("Pick a map to set a waypoint. Drive there, then use 'Establish / Move headquarters here'.");
            foreach (var m in maps.OrderBy(x => x.Name))
            {
                var mm = m;
                l.Action(m.Name, () =>
                {
                    GTA.World.WaypointPosition = mm.Center.ToVector3OE();
                    Ui.Notify.Push(LivingWorldSystem.Fmt("Waypoint set to {0}.", mm.Name), EventSeverity.Info);
                }, LivingWorldSystem.Fmt("{0} map, {1} objects. {2}", mm.Format, mm.ObjectCount, mm.File), value: GtaUtil.ZoneName(mm.Center.ToVector3OE()));
            }
        }
    }
}

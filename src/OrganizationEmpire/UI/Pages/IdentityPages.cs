using System.Collections.Generic;
using System.Linq;
using OrganizationEmpire.Native;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.UI.Pages
{
    internal sealed class IdentityPage : Page
    {
        public override string Title => "Faction Identity";
        public override string Subtitle => "Colors, tag, motto, uniforms and saved identity presets.";

        private Identity Target => Mod.Faction.IsGovernment ? Mod.Faction.Government.StateIdentity : Mod.Faction.Identity;

        public override void Build(ItemList l)
        {
            var f = Mod.Faction;
            var id = Target;
            if (f.IsGovernment) l.Text("Editing the identity of the " + f.Government.StateName + ". The former faction identity is kept in presets.");
            l.Header("Colors");
            PageUtil.ColorRow(l, Ui, "Primary color", id.PrimaryColor, c => { id.PrimaryColor = c; Refresh(); }, "Map blips, interface accent and vehicle paint.");
            PageUtil.ColorRow(l, Ui, "Secondary color", id.SecondaryColor, c => { id.SecondaryColor = c; Refresh(); });
            l.Header("Names");
            l.Action("Tag", () => Ui.AskText("Tag (max 5)", id.Tag, 5, s => { id.Tag = s; if (string.IsNullOrEmpty(id.Flag.MonogramText) || id.Flag.MonogramText.Length <= 5) id.Flag.MonogramText = s; }), value: id.Tag);
            l.Action("Motto", () => Ui.AskText("Motto", id.Motto, 60, s => id.Motto = s), value: string.IsNullOrEmpty(id.Motto) ? "-" : TextUtil.Truncate(id.Motto, 30));
            l.Header("Uniform");
            var uniforms = UniformCatalog.ForType(f.Type).ToList();
            l.Choice("Uniform preset", uniforms, UniformCatalog.Get(id.UniformPreset) ?? uniforms[0], u => { id.UniformPreset = u.Id; f.Identity.UniformPreset = u.Id; }, u => u.Name,
                "Ped models used for hired members, drivers and reinforcements. GTA can't recolor random NPC clothing, so 'uniform' means a consistent model set.");
            l.Toggle("Recruits wear the uniform", f.Identity.ApplyUniformToRecruits, v => f.Identity.ApplyUniformToRecruits = v);
            l.Action("Put every deployed member in uniform", () => Ui.Confirm("Change appearance?", "Deployed members will be re-spawned with the uniform model (names and stats are kept).", () =>
            {
                var uni = UniformCatalog.Get(f.Identity.UniformPreset) ?? UniformCatalog.DefaultFor(f.Type);
                int n = 0;
                foreach (var rt in Mod.Members.Deployed.ToList())
                {
                    var m = rt.Member;
                    var pos = rt.Ped.Position;
                    float heading = rt.Ped.Heading;
                    m.Model = Mod.Rng.Pick(uni.Models);
                    m.Appearance = new PedAppearance();
                    Mod.Members.Despawn(rt, true);
                    if (Mod.Members.Spawn(m, pos, heading) != null) n++;
                }
                Ui.Notify.Push(n + " member(s) changed into uniform.", EventSeverity.Good);
            }));

            l.Header("Presets");
            l.Action("Save current identity as preset", () => Ui.AskText("Preset name", "Preset " + (f.IdentityPresets.Count + 1), 24, s =>
            {
                f.IdentityPresets.RemoveAll(p => p.Name == s);
                f.IdentityPresets.Add(new IdentityPreset { Name = s, Identity = id.Copy() });
                Ui.Notify.Push("Preset saved.", EventSeverity.Good);
            }));
            foreach (var p in f.IdentityPresets.ToList())
            {
                var preset = p;
                var it = l.Action("Load preset: " + p.Name, () => Ui.Confirm("Apply '" + preset.Name + "'?", "Replace the current colors, flag and tag?", () =>
                {
                    var copy = preset.Identity.Copy();
                    if (f.IsGovernment) f.Government.StateIdentity = copy; else f.Identity = copy;
                    Refresh();
                }), "Hold Delete... use the entry below to remove.");
                it.Swatch = preset.Identity.Primary;
                it.DrawDetail = (r, x, y, w, h) => { Ui.Flags.Draw(r, preset.Identity.Flag, x, y, 240, 150); r.Text(preset.Name, x, y + 170, Theme.BodySize, r.Theme.Text); };
            }
            if (f.IdentityPresets.Count > 0)
            {
                l.Action("Delete a preset", () => Ui.ShowPicker("Delete preset", f.IdentityPresets.Select(p => new PickOption(p.Name)).ToList(), i => { f.IdentityPresets.RemoveAt(i); Ui.Notify.Push("Preset deleted.", EventSeverity.Info); }), danger: true);
            }
        }

        private void Refresh()
        {
            Mod.Hq.Refresh(true);
            Mod.Territories.ForceRefresh();
            Mod.Members.RefreshBlips();
            foreach (var sv in Mod.Vehicles.Spawned.ToList()) Mod.Vehicles.ApplyIdentity(sv);
        }

        public override void DrawDetail(Renderer r, float x, float y, float w, float h, Item focused)
        {
            var t = r.Theme;
            var id = Target;
            r.Text("PREVIEW", x, y, Theme.TinySize + 0.02f, t.Accent, Theme.Condensed);
            Ui.Flags.Draw(r, id.Flag, x, y + 30, 260, 162);
            float cy = y + 214;
            r.Rect(x, cy, 120, 40, id.Primary.ToColor());
            r.Rect(x + 130, cy, 120, 40, id.Secondary.ToColor());
            r.Text("Primary", x, cy + 46, Theme.TinySize, t.TextDim);
            r.Text("Secondary", x + 130, cy + 46, Theme.TinySize, t.TextDim);
            cy += 84;
            r.Text("[" + id.Tag + "] " + (Mod.Faction.IsGovernment ? Mod.Faction.Government.StateName : Mod.Faction.Name), x, cy, Theme.BodySize, id.Primary.ToColor());
            if (!string.IsNullOrEmpty(id.Motto)) r.Text("\"" + id.Motto + "\"", x, cy + 32, Theme.SmallSize, t.TextDim);
            if (focused?.Hint != null) r.Paragraph(focused.Hint, x, cy + 76, Theme.SmallSize, t.TextDim, w);
        }
    }

    internal sealed class FlagDesignerPage : Page
    {
        public override string Title => "Flag Designer";
        public override string Subtitle => "Pattern, colors, emblem, custom image and the in-world flag model.";
        public override float ListFraction => 0.5f;

        private FlagDesign Flag => Mod.Faction.IsGovernment ? Mod.Faction.Government.StateIdentity.Flag : Mod.Faction.Identity.Flag;

        public override void Build(ItemList l)
        {
            var flag = Flag;
            l.Header("Pattern");
            l.Choice("Pattern", PageUtil.Name(flag.Pattern), d => flag.Pattern = PageUtil.Next(flag.Pattern, d));
            PageUtil.ColorRow(l, Ui, "Color 1", flag.Color1, c => flag.Color1 = c);
            PageUtil.ColorRow(l, Ui, "Color 2", flag.Color2, c => flag.Color2 = c);
            PageUtil.ColorRow(l, Ui, "Color 3", flag.Color3, c => flag.Color3 = c);
            l.Header("Emblem");
            l.Choice("Emblem", PageUtil.Name(flag.Emblem), d => flag.Emblem = PageUtil.Next(flag.Emblem, d),
                "Star, crown and card suits use game textures where available; anything missing falls back to the monogram.");
            PageUtil.ColorRow(l, Ui, "Emblem color", flag.EmblemColor, c => flag.EmblemColor = c);
            l.Action("Monogram text", () => Ui.AskText("Monogram (max 3)", flag.MonogramText, 3, s => flag.MonogramText = s), value: flag.MonogramText);

            l.Header("Custom image");
            var images = new List<string> { "" };
            images.AddRange(Ui.Flags.AvailableImages());
            l.Choice("Custom flag image", images, flag.CustomImage ?? "", s => flag.CustomImage = s, s => string.IsNullOrEmpty(s) ? "None (use pattern)" : s,
                "Put PNG/JPG/DDS files in scripts\\OrganizationEmpire\\flags (about 512x320). Shown in all menus. In the 3D world GTA can only display its own flag models - see FLAG_ASSET_PIPELINE.md to replace a flag prop texture.");
            l.Action("Rescan flags folder", () => Ui.Notify.Push(Ui.Flags.AvailableImages().Count + " image(s) found in " + Mod.FlagsDir, EventSeverity.Info));

            l.Header("In-world flag");
            var models = new List<string> { "none" };
            models.AddRange(FlagCatalog.WorldFlagModels);
            l.Choice("Flag pole model", models, string.IsNullOrEmpty(flag.WorldFlagModel) ? "none" : flag.WorldFlagModel, s => { flag.WorldFlagModel = s; Mod.Hq.Refresh(true); },
                s => s == "none" ? "None" : FlagCatalog.WorldFlagLabel(s),
                "The physical flag pole placed at your HQ. Models missing from your game version are skipped automatically.");

            l.Header("Presets");
            l.Action("Reset to type default", () => Ui.Confirm("Reset flag?", "Restore the default flag for your faction type?", () =>
            {
                var id = Mod.Faction.Identity;
                var def = FlagCatalog.DefaultFor(Mod.Faction.Type, id.PrimaryColor, id.SecondaryColor);
                def.MonogramText = id.Tag;
                if (Mod.Faction.IsGovernment) Mod.Faction.Government.StateIdentity.Flag = def; else id.Flag = def;
                Mod.Hq.Refresh(true);
            }));
            l.Action("Use faction colors", () => { var id = Mod.Faction.Identity; flag.Color1 = id.SecondaryColor; flag.Color2 = id.PrimaryColor; });
        }

        public override void DrawDetail(Renderer r, float x, float y, float w, float h, Item focused)
        {
            var t = r.Theme;
            r.Text("LIVE PREVIEW", x, y, Theme.TinySize + 0.02f, t.Accent, Theme.Condensed);
            float fw = System.Math.Min(w, 420f);
            Ui.Flags.Draw(r, Flag, x, y + 32, fw, fw * 0.625f);
            float cy = y + 52 + fw * 0.625f;
            r.Text("Small:", x, cy, Theme.TinySize, t.TextDim);
            Ui.Flags.Draw(r, Flag, x + 60, cy, 64, 40);
            cy += 60;
            r.Text("World flag model: " + FlagCatalog.WorldFlagLabel(Flag.WorldFlagModel), x, cy, Theme.SmallSize, t.Text);
            cy += 34;
            if (focused?.Hint != null) r.Paragraph(focused.Hint, x, cy, Theme.SmallSize, t.TextDim, w);
        }
    }
}

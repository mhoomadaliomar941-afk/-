using System;
using System.Collections.Generic;
using System.Drawing;
using Font = GTA.UI.Font;
using System.Linq;
using OrganizationEmpire.Native;
using OrganizationEmpire.Core.Data;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.UI.Pages
{
    internal static class PageUtil
    {
        public static T Next<T>(T current, int dir) where T : struct, Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            int idx = Array.IndexOf(values, current);
            idx = ((idx + dir) % values.Length + values.Length) % values.Length;
            return values[idx];
        }

        public static string Name<T>(T v) => TextUtil.Humanize(v.ToString());

        public static void PickMember(UiManager ui, string title, IEnumerable<Member> candidates, Action<Member> onPick, Func<Member, string> detail = null)
        {
            var list = candidates.ToList();
            if (list.Count == 0) { ui.Notify.Push("No eligible members.", EventSeverity.Warning, 2500); return; }
            var f = ui.Mod.Faction;
            var opts = list.Select(m => new PickOption(m.DisplayName + "  -  " + (f.GetRank(m.RankId)?.Name ?? "?") + "  (" + m.Status + ")",
                detail != null ? detail(m) : m.Personality + ", skill " + (int)m.Skill + ", loyalty " + (int)m.Loyalty + ", morale " + (int)m.Morale)).ToList();
            ui.ShowPicker(title, opts, i => onPick(list[i]));
        }

        public static void PickColor(UiManager ui, string title, string current, Action<string> onPick)
        {
            var opts = FlagCatalog.Palette.Select(h => new PickOption(h + (h == current ? "  (current)" : ""), null)).ToList();
            opts.Add(new PickOption("Custom hex code...", "Type any #RRGGBB color."));
            ui.ShowPicker(title, opts, i =>
            {
                if (i < FlagCatalog.Palette.Length) onPick(FlagCatalog.Palette[i]);
                else ui.AskText(title + " (hex)", current, 7, s => onPick(s.StartsWith("#") ? s.ToUpperInvariant() : "#" + s.ToUpperInvariant()),
                    "Format: #RRGGBB, e.g. #B01E23", s => Rgb.TryParse(s.StartsWith("#") ? s : "#" + s, out _) ? null : "Not a valid #RRGGBB color.");
            });
        }

        /// <summary>Cycles through the shared palette (Left/Right on a color row).</summary>
        public static string CycleColor(string current, int dir)
        {
            int idx = Array.IndexOf(FlagCatalog.Palette, current);
            if (idx < 0) idx = 0;
            idx = ((idx + dir) % FlagCatalog.Palette.Length + FlagCatalog.Palette.Length) % FlagCatalog.Palette.Length;
            return FlagCatalog.Palette[idx];
        }

        public static Item ColorRow(ItemList list, UiManager ui, string label, string hex, Action<string> set, string hint = null)
        {
            var it = list.Choice(label, hex, d => set(CycleColor(hex, d)), (hint ?? "") + " Left/Right cycles the palette, Enter opens the full picker.");
            it.Activate = () => PickColor(ui, label, hex, set);
            it.Swatch = Rgb.Parse(hex, new Rgb(255, 255, 255));
            return it;
        }

        public static Color LevelColor(Theme t, float v, bool highIsBad = false)
        {
            if (highIsBad) v = 100 - v;
            return v >= 60 ? t.Good : v >= 35 ? t.Warn : t.Bad;
        }

        public static void Stat(Renderer r, float x, float y, float w, string label, float value, Color color, string text = null)
        {
            r.Text(label, x, y, Theme.TinySize, r.Theme.TextFaint);
            r.Text(text ?? ((int)value).ToString(), x + w, y - 2, Theme.SmallSize, r.Theme.Text, Theme.Numbers, Align.Right);
            r.Bar(x, y + 22, w, 4, value / 100f, color);
        }
    }
}

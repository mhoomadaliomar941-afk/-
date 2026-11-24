using System;
using System.Collections.Generic;
using System.Drawing;
using Font = GTA.UI.Font;
using System.Windows.Forms;
using OrganizationEmpire.Core.Util;

namespace OrganizationEmpire.UI
{
    internal enum ItemKind { Header, Action, Link, Toggle, Choice, Slider, Info, Progress, Text, Spacer }

    internal sealed class Item
    {
        public ItemKind Kind;
        public string Label;
        public string Value;
        public string Hint;
        public bool Enabled = true;
        public string DisabledReason;
        public bool Danger;
        public Color? Tint;
        public float Fraction;
        public Rgb? Swatch;
        public Action Activate;
        public Action<int> Adjust;
        public Func<Page> LinkTo;
        public Action<Renderer, float, float, float, float> DrawDetail;

        public bool Selectable
        {
            get
            {
                switch (Kind)
                {
                    case ItemKind.Action:
                    case ItemKind.Link:
                    case ItemKind.Toggle:
                    case ItemKind.Choice:
                    case ItemKind.Slider:
                        return true;
                    case ItemKind.Info:
                    case ItemKind.Progress:
                        return Hint != null || DrawDetail != null || Activate != null;
                    default:
                        return false;
                }
            }
        }

        public float RowHeight
        {
            get
            {
                switch (Kind)
                {
                    case ItemKind.Header: return 38f;
                    case ItemKind.Spacer: return 12f;
                    case ItemKind.Progress: return 44f;
                    default: return 36f;
                }
            }
        }
    }

    /// <summary>Fluent builder used by pages to declare their rows each frame.</summary>
    internal sealed class ItemList
    {
        public readonly List<Item> Items = new List<Item>();

        private Item Add(Item i) { Items.Add(i); return i; }

        public Item Header(string text) => Add(new Item { Kind = ItemKind.Header, Label = text });

        public Item Spacer() => Add(new Item { Kind = ItemKind.Spacer });

        public Item Text(string text, Color? tint = null) => Add(new Item { Kind = ItemKind.Text, Label = text, Tint = tint });

        public Item Action(string label, Action activate, string hint = null, bool enabled = true, string value = null, bool danger = false, string disabledReason = null)
            => Add(new Item { Kind = ItemKind.Action, Label = label, Activate = activate, Hint = hint, Enabled = enabled, Value = value, Danger = danger, DisabledReason = disabledReason });

        public Item Link(string label, Func<Page> page, string hint = null, string value = null, bool enabled = true, string disabledReason = null)
            => Add(new Item { Kind = ItemKind.Link, Label = label, LinkTo = page, Hint = hint, Value = value, Enabled = enabled, DisabledReason = disabledReason });

        public Item Toggle(string label, bool value, Action<bool> set, string hint = null, bool enabled = true)
            => Add(new Item { Kind = ItemKind.Toggle, Label = label, Value = value ? "ON" : "OFF", Activate = () => set(!value), Adjust = d => set(!value), Hint = hint, Enabled = enabled });

        public Item Choice(string label, string valueText, Action<int> adjust, string hint = null, bool enabled = true)
            => Add(new Item { Kind = ItemKind.Choice, Label = label, Value = valueText, Adjust = adjust, Activate = () => adjust(1), Hint = hint, Enabled = enabled });

        /// <summary>Cycles through a list of options.</summary>
        public Item Choice<T>(string label, IList<T> options, T current, Action<T> set, Func<T, string> format = null, string hint = null, bool enabled = true)
        {
            int idx = Math.Max(0, options.IndexOf(current));
            string text = options.Count == 0 ? "-" : (format != null ? format(options[idx]) : options[idx].ToString());
            return Choice(label, text, d =>
            {
                if (options.Count == 0) return;
                int n = (idx + d) % options.Count;
                if (n < 0) n += options.Count;
                set(options[n]);
            }, hint, enabled);
        }

        public Item Slider(string label, float value, float min, float max, float step, Action<float> set, string format = "0.##", string hint = null)
        {
            return Add(new Item
            {
                Kind = ItemKind.Slider,
                Label = label,
                Value = value.ToString(format, System.Globalization.CultureInfo.InvariantCulture),
                Fraction = max > min ? (value - min) / (max - min) : 0,
                Adjust = d => set(MathEx.Clamp(value + d * step, min, max)),
                Hint = hint,
            });
        }

        public Item Info(string label, string value, string hint = null, Color? tint = null)
            => Add(new Item { Kind = ItemKind.Info, Label = label, Value = value, Hint = hint, Tint = tint });

        public Item Progress(string label, float fraction, string value, Color? tint = null, string hint = null)
            => Add(new Item { Kind = ItemKind.Progress, Label = label, Fraction = fraction, Value = value, Tint = tint, Hint = hint });
    }

    /// <summary>A screen in the menu. Pages rebuild their rows every frame, so they always show live data.</summary>
    internal abstract class Page
    {
        public UiManager Ui;
        public int Focus = -1;
        public float Scroll;

        public ModContext Mod => Ui.Mod;
        public abstract string Title { get; }
        public virtual string Subtitle => null;
        /// <summary>Width fraction of the list column (rest is the detail panel).</summary>
        public virtual float ListFraction => 0.46f;

        public abstract void Build(ItemList list);

        public virtual void DrawDetail(Renderer r, float x, float y, float w, float h, Item focused)
        {
            if (focused == null) return;
            if (focused.DrawDetail != null) { focused.DrawDetail(r, x, y, w, h); return; }
            var t = r.Theme;
            r.Text(focused.Label?.ToUpperInvariant(), x, y, Theme.HeadSize, t.Text, Theme.Condensed);
            float cy = y + 40;
            if (!string.IsNullOrEmpty(focused.Value))
            {
                r.Text(focused.Value, x, cy, Theme.BodySize, t.Accent);
                cy += 34;
            }
            if (!focused.Enabled && !string.IsNullOrEmpty(focused.DisabledReason))
            {
                cy += r.Paragraph("Unavailable: " + focused.DisabledReason, x, cy, Theme.SmallSize, t.Warn, w) + 8;
            }
            if (!string.IsNullOrEmpty(focused.Hint)) r.Paragraph(focused.Hint, x, cy, Theme.SmallSize, t.TextDim, w);
        }

        public virtual bool HandleKey(Keys key, bool shift, bool ctrl) => false;
        public virtual void OnShow() { }

        protected void Report(Core.Systems.OpResult r)
        {
            Ui.Notify.Push(r.Message, r.Ok ? Core.Model.EventSeverity.Good : Core.Model.EventSeverity.Warning, 4000);
            Native.Nat.Sound(r.Ok ? "SELECT" : "ERROR");
        }

        protected void Open(Page p) => Ui.Push(p);
    }
}

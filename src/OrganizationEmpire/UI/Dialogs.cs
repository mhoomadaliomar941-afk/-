using System;
using System.Collections.Generic;
using System.Drawing;
using Font = GTA.UI.Font;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GTA;
using OrganizationEmpire.Core.Config;
using OrganizationEmpire.Core.Model;
using OrganizationEmpire.Core.Util;
using OrganizationEmpire.Native;

namespace OrganizationEmpire.UI
{
    internal sealed class Toast
    {
        public string Text;
        public EventSeverity Severity;
        public int Until;
        public int Born;
    }

    /// <summary>Stacked notifications at the right edge of the screen.</summary>
    internal sealed class Notifier
    {
        private readonly List<Toast> _toasts = new List<Toast>();

        public void Push(string text, EventSeverity severity, int durationMs = 6000)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = TextUtil.SafeForGame(text, 300);
            int now = Environment.TickCount;
            var dup = _toasts.FirstOrDefault(t => t.Text == text);
            if (dup != null) { dup.Until = now + durationMs; return; }
            _toasts.Add(new Toast { Text = text, Severity = severity, Born = now, Until = now + durationMs });
            if (_toasts.Count > 6) _toasts.RemoveAt(0);
            Log.Debug("Notify: " + text);
        }

        public void Draw(Renderer r, float topY, float rightEdge = -1f)
        {
            int now = Environment.TickCount;
            _toasts.RemoveAll(t => now > t.Until);
            float w = 430f;
            float x = (rightEdge > 0 ? rightEdge : r.W - 30f) - w;
            float y = topY;
            foreach (var t in _toasts)
            {
                float alpha = Math.Min(1f, Math.Min((now - t.Born) / 200f, (t.Until - now) / 400f));
                var lines = r.Wrap(t.Text, Theme.SmallSize, Theme.Body, w - 34f);
                float lh = r.LineHeight(Theme.SmallSize);
                float h = Math.Max(44f, lines.Count * lh + 18f);
                var c = r.Theme.Severity(t.Severity);
                r.Rect(x, y, w, h, Color.FromArgb((int)(225 * alpha), 12, 16, 22));
                r.Rect(x, y, 4f, h, Color.FromArgb((int)(255 * alpha), c.R, c.G, c.B));
                for (int i = 0; i < lines.Count; i++)
                    r.Text(lines[i], x + 18f, y + 8f + i * lh, Theme.SmallSize, Color.FromArgb((int)(255 * alpha), 232, 235, 240));
                y += h + 8f;
            }
        }
    }

    internal sealed class PickOption
    {
        public string Label;
        public string Detail;
        public bool Enabled = true;
        public PickOption(string label, string detail = null, bool enabled = true) { Label = label; Detail = detail; Enabled = enabled; }
    }

    /// <summary>Modal dialogs drawn above the menu.</summary>
    internal abstract class Dialog
    {
        public bool Done;
        public abstract void Draw(Renderer r, UiInput input);
        public abstract void HandleKey(Keys key, bool shift, bool ctrl, char? ch);
        public virtual bool CapturesText => false;
        public virtual void Update() { }

        protected static void DrawFrame(Renderer r, float x, float y, float w, float h, string title)
        {
            var t = r.Theme;
            r.Rect(0, 0, r.W, r.H, Color.FromArgb(140, 0, 0, 0));
            r.Rect(x, y, w, h, t.PanelRaised);
            r.Rect(x, y, w, 4f, t.Accent);
            r.Corners(x - 4, y - 4, w + 8, h + 8, 18f, 2f, t.Accent);
            r.Text(title?.ToUpperInvariant(), x + 24, y + 18, Theme.HeadSize, t.Text, Theme.Condensed);
        }

        protected static bool Button(Renderer r, UiInput input, float x, float y, float w, float h, string label, bool focused, bool danger = false)
        {
            var t = r.Theme;
            bool hover = r.Hover(x, y, w, h);
            Color bg = focused || hover ? (danger ? t.Bad : t.Accent) : Color.FromArgb(50, 255, 255, 255);
            r.Rect(x, y, w, h, bg);
            r.Text(label, x + w / 2, r.TextTop(y, h, Theme.BodySize), Theme.BodySize, focused || hover ? (danger ? Color.White : t.AccentText) : t.Text, Theme.Body, Align.Center);
            return hover && input.Click;
        }
    }

    internal sealed class ConfirmDialog : Dialog
    {
        private readonly string _title, _message, _yes, _no;
        private readonly Action _onYes, _onNo;
        private readonly bool _danger;
        private int _focus;

        public ConfirmDialog(string title, string message, Action onYes, Action onNo = null, string yes = "Confirm", string no = "Cancel", bool danger = false)
        {
            _title = title; _message = message; _onYes = onYes; _onNo = onNo; _yes = yes; _no = no; _danger = danger;
            _focus = danger ? 1 : 0;
        }

        public override void Draw(Renderer r, UiInput input)
        {
            float w = 640f;
            float textH = r.ParagraphHeight(_message, Theme.BodySize, Theme.Body, w - 48);
            float h = 140f + textH;
            float x = (r.W - w) / 2, y = (r.H - h) / 2;
            DrawFrame(r, x, y, w, h, _title);
            r.Paragraph(_message, x + 24, y + 62, Theme.BodySize, r.Theme.TextDim, w - 48);
            float by = y + h - 58;
            if (Button(r, input, x + 24, by, 280, 40, _yes, _focus == 0, _danger)) Choose(0);
            if (Button(r, input, x + w - 304, by, 280, 40, _no, _focus == 1)) Choose(1);
        }

        private void Choose(int i)
        {
            Done = true;
            Nat.Sound(i == 0 ? "SELECT" : "BACK");
            if (i == 0) _onYes?.Invoke(); else _onNo?.Invoke();
        }

        public override void HandleKey(Keys key, bool shift, bool ctrl, char? ch)
        {
            switch (key)
            {
                case Keys.Left: case Keys.Right: case Keys.Tab: _focus = 1 - _focus; Nat.Sound("NAV_LEFT_RIGHT"); break;
                case Keys.Enter: case Keys.Space: Choose(_focus); break;
                case Keys.Escape: case Keys.Back: Choose(1); break;
                case Keys.Y: Choose(0); break;
                case Keys.N: Choose(1); break;
            }
        }
    }

    internal sealed class MessageDialog : Dialog
    {
        private readonly string _title, _message;
        private readonly List<string> _lines;
        private readonly Color? _accent;
        private readonly Action _onClose;

        public MessageDialog(string title, string message, List<string> lines = null, Color? accent = null, Action onClose = null)
        {
            _title = title; _message = message; _lines = lines ?? new List<string>(); _accent = accent; _onClose = onClose;
        }

        public override void Draw(Renderer r, UiInput input)
        {
            float w = 660f;
            float textH = r.ParagraphHeight(_message ?? "", Theme.BodySize, Theme.Body, w - 48);
            float h = 150f + textH + _lines.Count * 30f;
            float x = (r.W - w) / 2, y = (r.H - h) / 2;
            DrawFrame(r, x, y, w, h, _title);
            if (_accent.HasValue) r.Rect(x, y, w, 4f, _accent.Value);
            float cy = y + 62 + r.Paragraph(_message ?? "", x + 24, y + 62, Theme.BodySize, r.Theme.TextDim, w - 48) + 8;
            foreach (var l in _lines)
            {
                r.Rect(x + 24, cy + 11, 6, 6, r.Theme.Accent);
                r.Text(l, x + 40, cy, Theme.SmallSize + 0.02f, r.Theme.Text);
                cy += 30f;
            }
            if (Button(r, input, x + w / 2 - 140, y + h - 56, 280, 40, "Continue", true)) Close();
        }

        private void Close()
        {
            Done = true;
            _onClose?.Invoke();
        }

        public override void HandleKey(Keys key, bool shift, bool ctrl, char? ch)
        {
            if (key == Keys.Enter || key == Keys.Escape || key == Keys.Back || key == Keys.Space) Close();
        }
    }

    internal sealed class PickerDialog : Dialog
    {
        private readonly string _title;
        private readonly List<PickOption> _options;
        private readonly Action<int> _onPick;
        private readonly Action _onCancel;
        private int _focus;
        private int _scroll;
        private const int Visible = 9;

        public PickerDialog(string title, List<PickOption> options, Action<int> onPick, Action onCancel)
        {
            _title = title; _options = options; _onPick = onPick; _onCancel = onCancel;
            _focus = Math.Max(0, options.FindIndex(o => o.Enabled));
        }

        public override void Draw(Renderer r, UiInput input)
        {
            var t = r.Theme;
            float w = 720f;
            int visible = Math.Min(Visible, _options.Count);
            float h = 110f + visible * 44f + 70f;
            float x = (r.W - w) / 2, y = (r.H - h) / 2;
            DrawFrame(r, x, y, w, h, _title);
            if (_focus < _scroll) _scroll = _focus;
            if (_focus >= _scroll + Visible) _scroll = _focus - Visible + 1;
            if (input.Wheel != 0) _scroll = MathEx.Clamp(_scroll - input.Wheel, 0, Math.Max(0, _options.Count - Visible));
            float cy = y + 62;
            for (int i = _scroll; i < Math.Min(_options.Count, _scroll + Visible); i++)
            {
                var o = _options[i];
                bool hover = r.Hover(x + 16, cy, w - 32, 40);
                if (hover && input.MouseMoved && o.Enabled) _focus = i;
                bool f = i == _focus;
                r.Rect(x + 16, cy, w - 32, 40, f ? t.AccentDim : Color.FromArgb(25, 255, 255, 255));
                if (f) r.Rect(x + 16, cy, 4, 40, t.Accent);
                r.Text(r.Ellipsize(o.Label, Theme.BodySize, Theme.Body, w - 80), x + 32, r.TextTop(cy, 40, Theme.BodySize), Theme.BodySize, o.Enabled ? t.Text : t.TextFaint);
                if (hover && input.Click && o.Enabled) { Pick(i); return; }
                cy += 44f;
            }
            var focused = _focus >= 0 && _focus < _options.Count ? _options[_focus] : null;
            if (focused?.Detail != null) r.Paragraph(focused.Detail, x + 24, cy + 6, Theme.SmallSize, t.TextDim, w - 48, Theme.Body, 2);
            if (input.RightClick) Cancel();
        }

        private void Pick(int i)
        {
            Done = true;
            Nat.Sound("SELECT");
            _onPick?.Invoke(i);
        }

        private void Cancel()
        {
            Done = true;
            Nat.Sound("BACK");
            _onCancel?.Invoke();
        }

        public override void HandleKey(Keys key, bool shift, bool ctrl, char? ch)
        {
            switch (key)
            {
                case Keys.Up: Move(-1); break;
                case Keys.Down: Move(1); break;
                case Keys.Enter: if (_focus >= 0 && _options[_focus].Enabled) Pick(_focus); break;
                case Keys.Escape: case Keys.Back: Cancel(); break;
            }
        }

        private void Move(int d)
        {
            for (int n = 0; n < _options.Count; n++)
            {
                _focus = (_focus + d + _options.Count) % _options.Count;
                if (_options[_focus].Enabled) break;
            }
            Nat.Sound("NAV_UP_DOWN");
        }
    }

    /// <summary>Text entry: inline typing (keyboard layout aware) or GTA's on-screen keyboard.</summary>
    internal sealed class TextInputDialog : Dialog
    {
        private readonly string _title, _hint;
        private readonly StringBuilder _text;
        private readonly int _max;
        private readonly Action<string> _onDone;
        private readonly Action _onCancel;
        private readonly Func<string, string> _validate;
        private readonly bool _mask;
        private string _error;
        private bool _native;
        private bool _nativeOpened;

        public TextInputDialog(string title, string initial, int maxLength, Action<string> onDone, Action onCancel = null, string hint = null, Func<string, string> validate = null, bool mask = false, TextInputMode mode = TextInputMode.Inline)
        {
            _title = title; _hint = hint; _text = new StringBuilder(initial ?? ""); _max = maxLength; _onDone = onDone; _onCancel = onCancel; _validate = validate; _mask = mask;
            _native = mode == TextInputMode.NativeKeyboard;
        }

        public override bool CapturesText => true;

        public override void Update()
        {
            if (!_native) return;
            if (!_nativeOpened) { Nat.OpenOnscreenKeyboard(_mask ? "" : _text.ToString(), _max); _nativeOpened = true; return; }
            int status = Nat.OnscreenKeyboardStatus();
            if (status == 1) { _text.Clear(); _text.Append(Nat.OnscreenKeyboardResult() ?? ""); Submit(); }
            else if (status == 2 || status == 3) { Done = true; _onCancel?.Invoke(); }
        }

        public override void Draw(Renderer r, UiInput input)
        {
            if (_native) return; // the game draws its own keyboard
            var t = r.Theme;
            float w = 760f, h = 230f;
            float x = (r.W - w) / 2, y = (r.H - h) / 2;
            DrawFrame(r, x, y, w, h, _title);
            float fy = y + 66;
            r.Rect(x + 24, fy, w - 48, 48, Color.FromArgb(255, 6, 8, 12));
            r.Border(x + 24, fy, w - 48, 48, 1.5f, t.Accent);
            string shown = _mask ? new string('*', _text.Length) : _text.ToString();
            bool caret = (Environment.TickCount / 500) % 2 == 0;
            string display = shown;
            while (display.Length > 0 && r.TextWidth(display + "_", Theme.BodySize) > w - 72) display = display.Substring(1);
            r.Text(display + (caret ? "_" : ""), x + 38, r.TextTop(fy, 48, Theme.BodySize), Theme.BodySize, t.Text);
            r.Text(_text.Length + "/" + _max, x + w - 30, fy + 54, Theme.TinySize, t.TextFaint, Theme.Body, Align.Right);
            string info = _error ?? _hint ?? "Enter: confirm   Esc: cancel   Ctrl+V: paste";
            r.Text(r.Ellipsize(info, Theme.SmallSize, Theme.Body, w - 200), x + 24, fy + 56, Theme.SmallSize, _error != null ? t.Bad : t.TextDim);
            if (Button(r, input, x + 24, y + h - 54, 220, 38, "Confirm", true)) Submit();
            if (Button(r, input, x + w - 244, y + h - 54, 220, 38, "Cancel", false)) { Done = true; _onCancel?.Invoke(); }
        }

        private void Submit()
        {
            string value = _text.ToString().Trim();
            string err = _validate?.Invoke(value);
            if (err != null) { _error = err; Nat.Sound("ERROR"); _native = false; return; }
            Done = true;
            Nat.Sound("SELECT");
            _onDone?.Invoke(value);
        }

        public override void HandleKey(Keys key, bool shift, bool ctrl, char? ch)
        {
            if (_native) return;
            switch (key)
            {
                case Keys.Enter: Submit(); return;
                case Keys.Escape: Done = true; _onCancel?.Invoke(); return;
                case Keys.Back: if (_text.Length > 0) _text.Length--; _error = null; return;
                case Keys.Delete: _text.Clear(); return;
            }
            if (ctrl && key == Keys.V)
            {
                try
                {
                    string clip = Clipboard.GetText();
                    foreach (char c in clip) if (!char.IsControl(c) && _text.Length < _max) _text.Append(c);
                }
                catch { /* clipboard requires STA; ignore if unavailable */ }
                return;
            }
            if (ch.HasValue && _text.Length < _max && ch.Value != '~')
            {
                _text.Append(ch.Value);
                _error = null;
            }
        }
    }

    internal sealed class KeyCaptureDialog : Dialog
    {
        private readonly string _what;
        private readonly Action<Keys> _onKey;

        public KeyCaptureDialog(string what, Action<Keys> onKey) { _what = what; _onKey = onKey; }

        public override bool CapturesText => true;

        public override void Draw(Renderer r, UiInput input)
        {
            float w = 600f, h = 170f;
            float x = (r.W - w) / 2, y = (r.H - h) / 2;
            DrawFrame(r, x, y, w, h, "Rebind: " + _what);
            r.Text("Press the new key now. Esc cancels.", x + 24, y + 70, Theme.BodySize, r.Theme.TextDim);
            r.Text("Avoid keys GTA already uses (W A S D, F, Space, Tab...).", x + 24, y + 106, Theme.SmallSize, r.Theme.TextFaint);
        }

        public override void HandleKey(Keys key, bool shift, bool ctrl, char? ch)
        {
            Done = true;
            if (key == Keys.Escape) return;
            _onKey?.Invoke(key);
        }
    }
}
